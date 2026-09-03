// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Draws two identical quads under two dome lights and requires the published
/// UsdLux dome mask to change which sky reaches each of them.
/// </summary>
/// <remarks>
/// <para>
/// The case is analytic rather than a reference image. Two textured domes are
/// authored over two white quads that differ only in their X translation: one
/// dome's image is pure red, the other's is pure blue. With no dome linking both
/// quads receive both skies and come back magenta. The link table then excludes
/// the red dome from the left quad and the blue dome from the right quad, and the
/// two quads must come back pure blue and pure red respectively.
/// </para>
/// <para>
/// The quads are drawn twice: once with the default rough surface, which measures
/// the <em>diffuse</em> half of the response through the irradiance atlas, and
/// once with a smooth metal, which has no diffuse lobe at all and therefore
/// measures the <em>specular</em> half through the prefiltered radiance atlas.
/// A per-dome bake that split only one of the two would pass one and fail the
/// other.
/// </para>
/// <para>
/// The unlinked image is then required to be byte-identical to the image the same
/// scene produced before any dome collection existed, and again after the table is
/// retired. That is the property the grouped atlas is arranged to protect: a scene
/// that links no dome bakes one composed group and addresses it through exactly
/// the texture coordinates it always did, so the pixels are the pixels rather than
/// values that merely round to the same place.
/// </para>
/// <para>
/// It runs on the D3D12 WARP and Vulkan SwiftShader devices, so the evidence is
/// cross-backend and needs no GPU.
/// </para>
/// </remarks>
internal static class SilkDomeLinkConformance
{
    private const string BlueLitQuad = "/World/UnderBlue";
    private const string RedLitQuad = "/World/UnderRed";
    private const string InstancedQuad = "/World/Row/Quad";
    private const string RedDome = "/World/Lights/DomeRed";
    private const string BlueDome = "/World/Lights/DomeBlue";
    private const string UntexturedDome = "/World/Lights/DomeAmbient";
    private const string RedTexture = "/assets/red.hdr";
    private const string BlueTexture = "/assets/blue.hdr";
    private const string MirrorMaterial = "/World/Materials/Mirror";
    private const uint Size = 64;
    private const int LeftX = 19;
    private const int RightX = 44;
    private const int SampleY = 32;
    private const uint SkyWidth = 32;
    private const uint SkyHeight = 16;

    /// <summary>
    /// Two prims under complementary dome collections receive different diffuse
    /// and specular skies, and an unlinked scene renders byte-identically.
    /// </summary>
    internal static async Task LinkedDomesReachOnlyTheirPrims(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device, GetShaderFormat(device), Sky);

        // Baseline: both domes reach both quads, and no link table exists at all.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            Frame(domeCount: 2, textured: 2),
            Quad(1, BlueLitQuad, -0.4, string.Empty),
            Quad(2, RedLitQuad, 0.4, string.Empty),
            DomeUpsert(RedDome, RedTexture, domeIndex: 0),
            DomeUpsert(BlueDome, BlueTexture, domeIndex: 1));
        _ = renderer.Render(color, depth);
        byte[] unlinked = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasDomeLinks)
            .IsFalse()
            .Because("No dome collection was published, so nothing may be retained.");
        await Assert.That(renderer.GpuResources.EnvironmentBinding.Enabled)
            .IsTrue()
            .Because("Both domes must resolve into the prefiltered environment.");
        await Assert.That(renderer.GpuResources.EnvironmentBinding.GroupCount)
            .IsEqualTo(1u)
            .Because(
                "A scene with no dome collection must bake one composed group, " +
                "which is the layout that renders the pre-dome-linking bytes.");
        await AssertLit(unlinked, LeftX, red: true, blue: true, "unlinked left quad");
        await AssertLit(unlinked, RightX, red: true, blue: true, "unlinked right quad");

        // Dome bit 0 is the red dome and bit 1 the blue one, by the dome indices
        // the two environment records carry.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            LightLink(
                domeCount: 2,
                (BlueLitQuad, 0b10u),
                (RedLitQuad, 0b01u)));
        _ = renderer.Render(color, depth);
        byte[] linked = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasDomeLinks).IsTrue();
        await Assert.That(renderer.Scene.LightLinks.DomeCount).IsEqualTo(2u);
        await Assert.That(renderer.Scene.LightLinks.Resolve(BlueLitQuad, 0).DomeMask)
            .IsEqualTo(0b10u);
        await Assert.That(renderer.GpuResources.EnvironmentBinding.GroupCount)
            .IsEqualTo(3u)
            .Because(
                "A linked scene must bake one group per dome plus the composed " +
                "group a fully linked prim reads.");
        await AssertLit(linked, LeftX, red: false, blue: true, "blue-linked quad");
        await AssertLit(linked, RightX, red: true, blue: false, "red-linked quad");

        // Retiring the table restores the unlinked image exactly. This is the
        // byte-identity claim: the environment goes back to a single composed
        // group and is addressed through the ungrouped coordinates again.
        SilkMeshRendererConformance.Apply(renderer, revision: 3, LightLink(domeCount: 0));
        _ = renderer.Render(color, depth);
        byte[] retired = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasDomeLinks).IsFalse();
        await Assert.That(renderer.GpuResources.EnvironmentBinding.GroupCount)
            .IsEqualTo(1u);
        await Assert.That(retired.AsSpan().SequenceEqual(unlinked))
            .IsTrue()
            .Because("Retiring the dome link table must reproduce the unlinked image exactly.");

        // A prim that links every dome must also reproduce the unlinked image
        // exactly, even while the grouped atlas is resident: it reads the
        // composed bake rather than a sum of separately rounded groups.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 4,
            LightLink(
                domeCount: 2,
                (BlueLitQuad, 0b11u),
                (RedLitQuad, 0b10u)));
        _ = renderer.Render(color, depth);
        byte[] partiallyLinked = ReadPixels(color);

        await Assert.That(renderer.GpuResources.EnvironmentBinding.GroupCount)
            .IsEqualTo(3u);
        await AssertSameColumn(partiallyLinked, unlinked, LeftX)
            .ConfigureAwait(false);
        await AssertLit(partiallyLinked, RightX, red: false, blue: true, "blue-only quad");
    }

    /// <summary>
    /// The same two collections must split the specular response as well as the
    /// diffuse one.
    /// </summary>
    /// <remarks>
    /// A smooth metal has no diffuse lobe, so every measurable channel here comes
    /// from the prefiltered radiance atlas. A per-dome bake that grouped only the
    /// irradiance map would leave both quads reflecting the composed magenta sky.
    /// </remarks>
    internal static async Task LinkedDomesSplitTheSpecularSky(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device, GetShaderFormat(device), Sky);

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            Frame(domeCount: 2, textured: 2),
            MirrorMaterialPage(),
            Quad(1, BlueLitQuad, -0.4, MirrorMaterial),
            Quad(2, RedLitQuad, 0.4, MirrorMaterial),
            DomeUpsert(RedDome, RedTexture, domeIndex: 0, diffuse: 0f, specular: 1f),
            DomeUpsert(BlueDome, BlueTexture, domeIndex: 1, diffuse: 0f, specular: 1f));
        _ = renderer.Render(color, depth);
        byte[] unlinked = ReadPixels(color);

        await AssertLit(unlinked, LeftX, red: true, blue: true, "unlinked mirror");

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            LightLink(
                domeCount: 2,
                (BlueLitQuad, 0b10u),
                (RedLitQuad, 0b01u)));
        _ = renderer.Render(color, depth);
        byte[] linked = ReadPixels(color);

        await AssertLit(linked, LeftX, red: false, blue: true, "blue-linked mirror");
        await AssertLit(linked, RightX, red: true, blue: false, "red-linked mirror");
    }

    /// <summary>
    /// The untextured dome's ambient term is maskable per draw, and summing every
    /// published dome reproduces the scene-wide term the producer accumulated.
    /// </summary>
    /// <remarks>
    /// An untextured dome publishes no environment record at all: its whole
    /// contribution is one summand of the frame ambient colour. Masking it is
    /// therefore a different code path from masking a prefiltered dome, and it is
    /// the path a fallback dome also lands on.
    /// </remarks>
    internal static async Task AnUntexturedDomeIsMaskablePerDraw(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device, GetShaderFormat(device), Sky);

        // One untextured dome contributing a green ambient term, and nothing else.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            Frame(
                domeCount: 1,
                textured: 0,
                ambient: new Vector3(0f, 0.6f, 0f)),
            Quad(1, BlueLitQuad, -0.4, string.Empty),
            Quad(2, RedLitQuad, 0.4, string.Empty));
        _ = renderer.Render(color, depth);
        byte[] unlinked = ReadPixels(color);

        await Assert.That(Channel(unlinked, LeftX, 1))
            .IsGreaterThan((byte)40)
            .Because("The untextured dome must light both quads before it is linked.");
        await Assert.That(Channel(unlinked, RightX, 1)).IsGreaterThan((byte)40);

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            LightLink(domeCount: 1, (RedLitQuad, 0u)));
        _ = renderer.Render(color, depth);
        byte[] linked = ReadPixels(color);

        await Assert.That(Channel(linked, LeftX, 1))
            .IsEqualTo(Channel(unlinked, LeftX, 1))
            .Because("The unlinked quad keeps the aggregate ambient term unchanged.");
        await Assert.That(Channel(linked, RightX, 1))
            .IsLessThan((byte)8)
            .Because("A prim outside the dome's collection receives none of its ambient.");
    }

    /// <summary>
    /// One prototype drawn as several instances, split into two batches by
    /// complementary dome collections, keeps every instance's own transform.
    /// </summary>
    /// <remarks>
    /// Every batch of a frame is recorded before any of them is submitted, so a
    /// geometry split across two batches must not share one mutable instance
    /// transform table: the second batch would rewrite it while the first batch's
    /// draw still referenced it. The symptom is not a subtle shift -- some
    /// instances are drawn twice at another instance's position and others are
    /// not drawn at all -- so this measures every instance's own quad, both for
    /// coverage and for the sky it received.
    /// </remarks>
    internal static async Task SplitDomeMasksKeepEveryInstanceTransform(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device, GetShaderFormat(device), Sky);

        // Four instances of ONE prototype path: a payload record at instance 0
        // and three lightweight instance references that reuse its geometry. That
        // is the only shape that batches -- a retained geometry is keyed by prim
        // path -- so it is the only shape a split mask can tear.
        double[] positions = [-0.6, -0.2, 0.2, 0.6];
        var page = new List<byte[]>
        {
            Frame(domeCount: 2, textured: 2),
            Quad(1, InstancedQuad, positions[0], string.Empty),
        };
        for (int index = 1; index < positions.Length; index++)
        {
            page.Add(InstanceReference(InstancedQuad, index, positions[index]));
        }
        page.Add(DomeUpsert(RedDome, RedTexture, domeIndex: 0));
        page.Add(DomeUpsert(BlueDome, BlueTexture, domeIndex: 1));
        SilkMeshRendererConformance.Apply(renderer, revision: 1, [.. page]);
        SilkMeshRenderResult unlinkedResult = renderer.Render(color, depth);
        byte[] unlinked = ReadPixels(color);

        // One instanced draw: the four instances share the prototype's geometry
        // and every batch-key field, so they batch. Without that this case would
        // measure four separate draws and could not see the defect at all.
        await Assert.That(unlinkedResult.DrawCount)
            .IsEqualTo(1)
            .Because("Four instances of one prototype must batch into one draw.");

        // Every instance is drawn, at its own place, under both skies.
        int[] samples = SampleColumns(positions);
        for (int index = 0; index < samples.Length; index++)
        {
            await AssertLit(
                unlinked,
                samples[index],
                red: true,
                blue: true,
                $"unlinked instance {index}");
        }

        // Split the batch: the outer two keep the red dome, the inner two the
        // blue one. That is two instanced batches over one geometry.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            InstanceLightLink(
                domeCount: 2,
                (InstancedQuad, 0, 0b01u),
                (InstancedQuad, 1, 0b10u),
                (InstancedQuad, 2, 0b10u),
                (InstancedQuad, 3, 0b01u)));
        _ = renderer.Render(color, depth);
        byte[] split = ReadPixels(color);

        SilkMeshRenderResult splitResult = renderer.Render(color, depth);
        await Assert.That(splitResult.DrawCount)
            .IsEqualTo(2)
            .Because(
                "The dome mask must split one geometry into two instanced " +
                "batches, which is the state a shared transform table corrupts.");

        // Every instance still occupies its own column -- nothing was drawn twice
        // and nothing vanished -- and each carries exactly the sky its collection
        // admits. A shared instance table gives the whole first batch the second
        // batch's transforms, which collapses two columns onto two others.
        await AssertLit(split, samples[0], red: true, blue: false, "red instance 0");
        await AssertLit(split, samples[1], red: false, blue: true, "blue instance 1");
        await AssertLit(split, samples[2], red: false, blue: true, "blue instance 2");
        await AssertLit(split, samples[3], red: true, blue: false, "red instance 3");

        // Coverage is unchanged: the same texels are lit before and after the
        // split, which is what proves no instance moved onto another's quad.
        await Assert.That(LitTexelCount(split))
            .IsEqualTo(LitTexelCount(unlinked))
            .Because(
                "Splitting one geometry across two instanced batches must not " +
                "duplicate or drop an instance transform.");

        // And retiring the collection puts the frame back exactly.
        SilkMeshRendererConformance.Apply(renderer, revision: 3, LightLink(domeCount: 0));
        _ = renderer.Render(color, depth);
        byte[] retired = ReadPixels(color);
        await Assert.That(retired.AsSpan().SequenceEqual(unlinked))
            .IsTrue()
            .Because("Retiring the split must reproduce the unsplit image exactly.");
    }

    /// <summary>
    /// A frame whose submission or wait fails must upload the prefiltered
    /// environment again on the frame after it, and come back correct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recording a copy into a command list is not performing it. The renderer
    /// therefore marks the environment and BRDF uploads <em>pending</em> and
    /// commits them only once the submission carrying them has been waited on. If
    /// the marks were set at record time, a frame whose submission threw would
    /// leave the maps counted as uploaded while the device still held whatever it
    /// held before -- and every later frame would skip the copy and sample memory
    /// nothing ever wrote.
    /// </para>
    /// <para>
    /// This drives the whole renderer rather than the upload methods directly: a
    /// real device is wrapped so that one submission throws, then so that one
    /// wait throws, and in both cases the frame after it has to reach the same
    /// pixels a frame that never failed reaches.
    /// </para>
    /// <para>
    /// The failure is injected into the frame that carries a <em>rebuild</em>
    /// rather than into the very first frame, because that is the case with a
    /// wrong answer available: the device already holds the previous bake, so an
    /// upload wrongly counted as done leaves the old sky on screen instead of an
    /// obviously empty one. The rebuild re-binds the second dome to the first
    /// dome's image, which turns the composed magenta sky pure red -- a skipped
    /// re-upload is therefore visible as blue that should not be there.
    /// </para>
    /// </remarks>
    internal static async Task AFailedSubmissionUploadsTheEnvironmentAgain(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // What the rebuilt scene must look like, measured on the bare device.
        using ISilkGraphicsTexture cleanColor = CreateColorTarget(device);
        using ISilkGraphicsTexture cleanDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Size, Size));
        using var clean = new SilkMeshRenderer(device, GetShaderFormat(device), Sky);
        SilkMeshRendererConformance.Apply(clean, revision: 1, MagentaScene());
        _ = clean.Render(cleanColor, cleanDepth);
        SilkMeshRendererConformance.Apply(
            clean,
            revision: 2,
            DomeUpsert(BlueDome, RedTexture, domeIndex: 1));
        _ = clean.Render(cleanColor, cleanDepth);
        byte[] expected = ReadPixels(cleanColor);
        await AssertLit(expected, LeftX, red: true, blue: false, "rebuilt reference");

        foreach (FailurePoint failure in new[] { FailurePoint.Submit, FailurePoint.Wait })
        {
            using var failing = new FailingSubmissionDevice(device);
            using ISilkGraphicsTexture color = CreateColorTarget(failing);
            using ISilkGraphicsTexture depth = failing.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(Size, Size));
            using var renderer = new SilkMeshRenderer(
                failing,
                GetShaderFormat(device),
                Sky);

            SilkMeshRendererConformance.Apply(renderer, revision: 1, MagentaScene());
            _ = renderer.Render(color, depth);
            await AssertLit(ReadPixels(color), LeftX, red: true, blue: true, "magenta sky");
            ulong settled = renderer.GpuResources.EnvironmentUploadBytes;
            await Assert.That(settled).IsGreaterThan(0UL);

            // Re-bind the second dome to the first dome's image. The bake changes,
            // so the frame below records a fresh copy of both maps.
            SilkMeshRendererConformance.Apply(
                renderer,
                revision: 2,
                DomeUpsert(BlueDome, RedTexture, domeIndex: 1));

            failing.FailNext(failure);
            bool threw = false;
            try
            {
                _ = renderer.Render(color, depth);
            }
            catch (SilkSubmissionFailure)
            {
                threw = true;
            }

            await Assert.That(threw)
                .IsTrue()
                .Because($"The injected {failure} failure must reach the caller.");
            await Assert.That(renderer.GpuResources.EnvironmentUploadBytes)
                .IsEqualTo(settled)
                .Because(
                    "A frame that never completed may not count the copies it " +
                    "recorded as uploads.");

            // The retry: nothing about the scene changed, so the only way the
            // rebuilt maps reach the device is the abandoned marks being recorded
            // again.
            _ = renderer.Render(color, depth);
            byte[] retried = ReadPixels(color);

            await Assert.That(renderer.GpuResources.EnvironmentUploadBytes)
                .IsGreaterThan(settled)
                .Because("The retry must record and complete the abandoned uploads.");
            await Assert.That(renderer.GpuResources.EnvironmentBinding.Enabled).IsTrue();
            await AssertLit(retried, LeftX, red: true, blue: false, "retried left quad");
            await AssertLit(retried, RightX, red: true, blue: false, "retried right quad");
            await Assert.That(retried.AsSpan().SequenceEqual(expected))
                .IsTrue()
                .Because(
                    $"A frame retried after a {failure} failure must be " +
                    "byte-identical to one that never failed.");
        }
    }

    /// <summary>Two quads under a red dome and a blue dome, which compose to magenta.</summary>
    private static byte[][] MagentaScene() =>
    [
        Frame(domeCount: 2, textured: 2),
        Quad(1, BlueLitQuad, -0.4, string.Empty),
        Quad(2, RedLitQuad, 0.4, string.Empty),
        DomeUpsert(RedDome, RedTexture, domeIndex: 0),
        DomeUpsert(BlueDome, BlueTexture, domeIndex: 1),
    ];

    private enum FailurePoint
    {
        None,
        Submit,
        Wait,
    }

    /// <summary>The injected device failure, distinguishable from a real one.</summary>
    private sealed class SilkSubmissionFailure(string message)
        : InvalidOperationException(message);

    /// <summary>
    /// Forwards every call to a real device, failing one submission or one wait
    /// on demand.
    /// </summary>
    /// <remarks>
    /// The failure is injected after the frame recorded its uploads and before
    /// they could complete, which is exactly the window the pending marks exist
    /// for. Everything else is the real backend, so the retry's pixels are real
    /// pixels.
    /// </remarks>
    private sealed class FailingSubmissionDevice(ISilkGraphicsDevice inner)
        : ISilkGraphicsDevice
    {
        private FailurePoint _armed;

        public SilkGraphicsBackend Backend => inner.Backend;

        public SilkGraphicsCapabilities Capabilities => inner.Capabilities;

        internal void FailNext(FailurePoint failure) => _armed = failure;

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            inner.CreateBuffer(size, usage);

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            inner.CreateTexture2D(descriptor);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format) =>
            inner.CreateTexture2D(width, height, format);

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            inner.CreateComputeBindingLayout(descriptor);

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            inner.CreateComputeShaderProgram(descriptor);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            inner.CreateSampler(descriptor);

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) =>
            inner.CreateShaderModule(descriptor);

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            inner.CreateBindingLayout(descriptor);

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            inner.CreateShaderProgram(descriptor);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            inner.CreateGraphicsPipeline(descriptor);

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            inner.CreateComputePipeline(descriptor);

        public ISilkGraphicsCommandList CreateCommandList() => inner.CreateCommandList();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
        {
            if (_armed == FailurePoint.Submit)
            {
                _armed = FailurePoint.None;
                throw new SilkSubmissionFailure("The injected submission failed.");
            }
            ISilkGraphicsSubmission submission = inner.Submit(commandList);
            if (_armed != FailurePoint.Wait)
            {
                return submission;
            }

            _armed = FailurePoint.None;
            return new FailingWaitSubmission(submission);
        }

        public void WaitIdle() => inner.WaitIdle();

        public void Dispose()
        {
            // The inner device is owned by the caller and outlives this wrapper.
        }
    }

    /// <summary>A real submission whose wait is reported as failed exactly once.</summary>
    private sealed class FailingWaitSubmission(ISilkGraphicsSubmission inner)
        : ISilkGraphicsSubmission
    {
        public bool IsCompleted => inner.IsCompleted;

        public void Wait()
        {
            // The real wait still runs, so the device is quiesced and the wrapper
            // leaks no work; only the outcome the renderer sees is a failure.
            inner.Wait();
            throw new SilkSubmissionFailure("The injected wait failed.");
        }

        public void Dispose() => inner.Dispose();
    }

    /// <summary>Maps each instance's world X onto the texel column at its centre.</summary>
    private static int[] SampleColumns(double[] positions)
    {
        var columns = new int[positions.Length];
        for (int index = 0; index < positions.Length; index++)
        {
            columns[index] = (int)Math.Round(((positions[index] + 1.0) / 2.0) * Size);
        }
        return columns;
    }

    /// <summary>
    /// Builds a lightweight ABI v8 instance record that reuses the prototype's
    /// geometry and carries only its own transform.
    /// </summary>
    private static byte[] InstanceReference(string path, int instanceIndex, double x)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] instancerPathBytes = Encoding.UTF8.GetBytes("/Instancer");
        int size = 268 + pathBytes.Length + instancerPathBytes.Length +
            8 + instancerPathBytes.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);

        // A non-zero instancer id and a positive instance index with no geometry
        // is what makes this a reference to the payload record at index zero.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 7);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1f);
        }
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[12] = x;
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                transform[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(264), 1);
        pathBytes.CopyTo(bytes, 268);
        instancerPathBytes.CopyTo(bytes, 268 + pathBytes.Length);
        int contextOffset = 268 + pathBytes.Length + instancerPathBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(contextOffset),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(contextOffset + 4),
            instanceIndex);
        instancerPathBytes.CopyTo(bytes, contextOffset + 8);
        return bytes;
    }

    /// <summary>
    /// Builds a link table whose entries name individual instances of one path.
    /// </summary>
    private static byte[] InstanceLightLink(
        uint domeCount,
        params (string Path, int InstanceIndex, uint DomeMask)[] entries)
    {
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, int instanceIndex, uint domeMask) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(0u));
            payload.AddRange(BitConverter.GetBytes(0u));
            payload.AddRange(BitConverter.GetBytes(domeMask));
            payload.AddRange(BitConverter.GetBytes(instanceIndex));
            payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>Counts the texels any dome measurably lit.</summary>
    private static int LitTexelCount(byte[] pixels)
    {
        int count = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (Math.Max(pixels[index], pixels[index + 2]) > 24)
            {
                count++;
            }
        }
        return count;
    }

    private static async Task AssertSameColumn(byte[] actual, byte[] expected, int x)
    {
        int offset = ((SampleY * (int)Size) + x) * 4;
        for (int channel = 0; channel < 4; channel++)
        {
            await Assert.That(actual[offset + channel])
                .IsEqualTo(expected[offset + channel])
                .Because(
                    "A prim linked to every published dome must read the composed " +
                    "bake, which is the same value an unlinked prim reads.");
        }
    }

    private static async Task AssertLit(
        byte[] pixels,
        int x,
        bool red,
        bool blue,
        string what)
    {
        int offset = ((SampleY * (int)Size) + x) * 4;
        string evidence =
            $"The {what} at ({x},{SampleY}) was rgba({pixels[offset]}," +
            $"{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]}).";
        await Assert.That(pixels[offset] > 24).IsEqualTo(red).Because(evidence);
        await Assert.That(pixels[offset + 2] > 24).IsEqualTo(blue).Because(evidence);

        // Neither sky emits green, so a green channel would mean the sample
        // landed on something other than the lit quad.
        await Assert.That(pixels[offset + 1]).IsLessThan((byte)24).Because(evidence);
        await Assert.That(pixels[offset + 3]).IsGreaterThan((byte)100).Because(evidence);
    }

    private static byte Channel(byte[] pixels, int x, int channel) =>
        pixels[(((SampleY * (int)Size) + x) * 4) + channel];

    private static ISilkGraphicsTexture CreateColorTarget(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static SilkShaderBinaryFormat GetShaderFormat(ISilkGraphicsDevice device) =>
        device.Backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => SilkShaderBinaryFormat.SpirV
        };

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    /// <summary>A uniform red sky for one asset and a uniform blue one for the other.</summary>
    private static SilkDecodedImage Sky(string asset, bool srgb)
    {
        Vector3 radiance = string.Equals(asset, RedTexture, StringComparison.Ordinal)
            ? new Vector3(0.6f, 0f, 0f)
            : new Vector3(0f, 0f, 0.6f);
        int count = checked((int)(SkyWidth * SkyHeight));
        var pixels = new byte[count * 4 * sizeof(float)];
        Span<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
            pixels.AsSpan());
        for (int texel = 0; texel < count; texel++)
        {
            floats[(texel * 4) + 0] = radiance.X;
            floats[(texel * 4) + 1] = radiance.Y;
            floats[(texel * 4) + 2] = radiance.Z;
            floats[(texel * 4) + 3] = 1f;
        }
        return new SilkDecodedImage(
            SkyWidth,
            SkyHeight,
            pixels,
            SilkTextureFormat.Rgba32Float);
    }

    /// <summary>
    /// Builds the ABI v21 frame: no direct light, an optional ambient term, and
    /// the bounded dome table the per-prim dome mask indexes.
    /// </summary>
    private static byte[] Frame(uint domeCount, uint textured, Vector3 ambient = default)
    {
        const int frameSize = 2248;
        const int ambientOffset = 536 + 16 + (8 * 176);
        const int domeCountOffset = 1976;
        const int domeTableOffset = 1992;
        var bytes = new byte[frameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)Size));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)Size));
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (element * 8)),
                identity[element]);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (element * 8)),
                identity[element]);
        }

        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset), ambient.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 4), ambient.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ambientOffset + 8), ambient.Z);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(ambientOffset + 12),
            domeCount > textured ? 1f : 0f);

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(domeCountOffset), domeCount);
        for (uint dome = 0; dome < domeCount; dome++)
        {
            int entry = domeTableOffset + checked((int)(dome * 32));
            bool isTextured = dome < textured;

            // The untextured dome carries the whole ambient term, so summing the
            // published domes reproduces the scene-wide value exactly, which is
            // what the producer guarantees.
            if (!isTextured)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry), ambient.X);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 4), ambient.Y);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 8), ambient.Z);
            }

            // OPENUSD_SILK_DOME_FLAG_PRESENT, plus TEXTURED for a dome that
            // publishes an environment record. There is no managed enum for the
            // raw wire value.
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(entry + 16),
                isTextured ? 3u : 1u);
        }
        return bytes;
    }

    private static byte[] Quad(ulong id, string path, double x, string material)
    {
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            id,
            path,
            [
                -0.2f, -0.35f, 0.4f,
                 0.2f, -0.35f, 0.4f,
                 0.2f,  0.35f, 0.4f,
                -0.2f,  0.35f, 0.4f,
            ],
            [0, 2, 1, 0, 3, 2],
            x,
            0,
            [1, 1, 1, 1]);
        if (material.Length == 0)
        {
            return mesh;
        }

        // MESH_UPSERT carries the bound material's hash at offset 208 and its
        // path byte count at 216, with the path bytes appended last.
        byte[] materialBytes = Encoding.UTF8.GetBytes(material);
        Array.Resize(ref mesh, mesh.Length + materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(mesh.AsSpan(208), ComputeStableHash(material));
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(216), (uint)materialBytes.Length);
        materialBytes.CopyTo(mesh.AsSpan(mesh.Length - materialBytes.Length));
        return mesh;
    }

    /// <summary>
    /// Builds a fully metallic white UsdPreviewSurface, which has no diffuse lobe
    /// and therefore measures the specular half of the environment response on its
    /// own.
    /// </summary>
    private static byte[] MirrorMaterialPage()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MirrorMaterial);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(MirrorMaterial)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface),
            .. BitConverter.GetBytes(3u),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
            .. BitConverter.GetBytes((uint)SilkMaterialParameter.DiffuseColor),
            .. BitConverter.GetBytes(3u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes((uint)SilkMaterialParameter.Metallic),
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes((uint)SilkMaterialParameter.Roughness),
            .. BitConverter.GetBytes(1u),
            .. BitConverter.GetBytes(0.05f),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ];

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] DomeUpsert(
        string path,
        string texture,
        uint domeIndex,
        float diffuse = 1f,
        float specular = 0f)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] textureBytes = Encoding.UTF8.GetBytes(texture);
        double[] identity = SilkMeshRendererConformance.Identity();
        List<byte> payload =
        [
            .. BitConverter.GetBytes(ComputeStableHash(path)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)textureBytes.Length),
            .. BitConverter.GetBytes((uint)SilkDomeTextureFormat.Latlong),
            .. BitConverter.GetBytes((uint)SilkColorSpace.Raw),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(domeIndex),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(diffuse),
            .. BitConverter.GetBytes(specular),
            .. BitConverter.GetBytes(0u),
        ];
        for (int element = 0; element < 16; element++)
        {
            payload.AddRange(BitConverter.GetBytes(identity[element]));
        }
        payload.AddRange(pathBytes);
        payload.AddRange(textureBytes);

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.EnvironmentUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>
    /// Builds a link table whose direct-light masks are all default and whose dome
    /// masks are the property under test.
    /// </summary>
    private static byte[] LightLink(
        uint domeCount,
        params (string Path, uint DomeMask)[] entries)
    {
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, uint domeMask) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(0u));
            payload.AddRange(BitConverter.GetBytes(0u));
            payload.AddRange(BitConverter.GetBytes(domeMask));
            payload.AddRange(BitConverter.GetBytes(SilkLightLinkCommand.AllInstances));
            payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static ulong ComputeStableHash(string value)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
