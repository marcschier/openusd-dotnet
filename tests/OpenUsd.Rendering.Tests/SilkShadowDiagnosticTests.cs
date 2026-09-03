// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins that an authored <c>inputs:shadow:enable</c> that did not produce a shadow
/// map is reported, and that one that did is silent.
/// </summary>
/// <remarks>
/// <para>
/// hdSilk publishes shadow-enable per direct light, a resolved per-prim shadow-link
/// mask since page ABI 18, and a bounded shadow descriptor table since ABI 19. A
/// light that got a map casts a measurable shadow and needs no diagnostic; a light
/// that did not renders exactly as one that never asked, which is
/// indistinguishable from a renderer that tried and produced nothing.
/// </para>
/// <para>
/// That is the failure mode this profile exists to avoid. The gap is named against
/// the light that asked for it, with the reason it went unfilled, and these tests
/// keep the naming honest in both directions: a light that authors shadows and
/// gets none must be reported, and a light that does not ask, or that got its map,
/// must not be, so the diagnostic cannot degrade into noise that gets ignored.
/// </para>
/// </remarks>
public sealed class SilkShadowDiagnosticTests
{
    private const int LightingSize = 1976;
    private const int LightCountOffset = 536;
    private const int LightTableOffset = 552;
    private const int LightEntrySize = 176;

    [Test]
    public async Task ADirectLightThatAuthorsShadowEnableIsNamedAsUnsupported()
    {
        IReadOnlyList<RenderDiagnostic> diagnostics = ResolveDiagnostics(
            CreateLightingFrame([(Type: 1u, ShadowEnabled: 1u)]));

        RenderDiagnostic diagnostic = diagnostics.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported);

        // The frame light table carries no prim path, so the light is named by its
        // table index and resolved type. Stating that plainly is better than
        // inventing a path the renderer was never given.
        await Assert.That(diagnostic.Message).Contains("Direct light 0");
        await Assert.That(diagnostic.Message).Contains("distant");
        await Assert.That(diagnostic.Message).Contains("shadow:enable");
        await Assert.That(diagnostic.Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task EveryShadowCastingLightTypeIsNamedByItsOwnType()
    {
        // A renderer that only ever reported "a light" would make an unsupported
        // sphere light indistinguishable from an unsupported distant one, which is
        // the first question an author asks.
        IReadOnlyList<RenderDiagnostic> diagnostics = ResolveDiagnostics(
            CreateLightingFrame(
            [
                (Type: 1u, ShadowEnabled: 1u),
                (Type: 2u, ShadowEnabled: 1u),
                (Type: 3u, ShadowEnabled: 1u),
                (Type: 4u, ShadowEnabled: 1u),
                (Type: 5u, ShadowEnabled: 1u)
            ]));

        string[] messages =
        [
            .. diagnostics
                .Where(entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported)
                .Select(entry => entry.Message)
        ];

        await Assert.That(messages.Length).IsEqualTo(5);
        foreach (string type in (string[])["distant", "sphere", "rect", "disk", "cylinder"])
        {
            await Assert.That(messages.Any(
                    message => message.Contains(type, StringComparison.Ordinal)))
                .IsTrue()
                .Because($"A {type} light that authors shadows must be named as one.");
        }
    }

    [Test]
    public async Task ALightThatDoesNotAuthorShadowsIsNotDiagnosed()
    {
        IReadOnlyList<RenderDiagnostic> diagnostics = ResolveDiagnostics(
            CreateLightingFrame([(Type: 1u, ShadowEnabled: 0u)]));

        await Assert.That(diagnostics.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.ShadowUnsupported);
    }

    [Test]
    public async Task AFrameWithNoLightsIsNotDiagnosed()
    {
        // The no-shadow fast path: a scene with no lights at all pays nothing and
        // says nothing.
        IReadOnlyList<RenderDiagnostic> diagnostics = ResolveDiagnostics(
            CreateLightingFrame([]));

        await Assert.That(diagnostics.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.ShadowUnsupported);
    }

    [Test]
    public async Task ShadowEnableBeyondThePublishedLightCountIsNotDiagnosed()
    {
        // The light table is a fixed eight entries and only the first light_count
        // of them are published. A stale entry past that count is not part of the
        // scene, so reporting it would name a light the author cannot find.
        byte[] page = CreateLightingFrame([(Type: 1u, ShadowEnabled: 0u)]);
        int unpublished = LightTableOffset + LightEntrySize;
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(unpublished, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(unpublished + 4, 4), 1u);

        IReadOnlyList<RenderDiagnostic> diagnostics = ResolveDiagnostics(page);

        await Assert.That(diagnostics.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.ShadowUnsupported);
    }

    [Test]
    public async Task TurningShadowsOffClearsTheDiagnosticItRaised()
    {
        // A diagnostic that outlived the condition it describes is worse than
        // none: an author who removes shadow:enable would keep being told about a
        // light that no longer asks for anything.
        using var device = new ShadowDiagnosticGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => throw new InvalidOperationException("No image is decoded."));
        var scene = new SilkSceneState();

        _ = scene.Apply(CreateLightingFrame([(Type: 1u, ShadowEnabled: 1u)]), 1, 1);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, exposure: 0f);
        bool reportedWhileEnabled = resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported);

        _ = scene.Apply(CreateLightingFrame([(Type: 1u, ShadowEnabled: 0u)]), 1, 2);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, exposure: 0f);
        bool reportedAfterDisable = resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported);

        await Assert.That(reportedWhileEnabled).IsTrue();
        await Assert.That(reportedAfterDisable).IsFalse();
    }

    private static IReadOnlyList<RenderDiagnostic> ResolveDiagnostics(byte[] page)
    {
        using var device = new ShadowDiagnosticGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => throw new InvalidOperationException("No image is decoded."));
        var scene = new SilkSceneState();
        _ = scene.Apply(page, 1, 1);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, exposure: 0f);
        return [.. resources.Diagnostics.Entries];
    }

    [Test]
    public async Task ADeviceThatCannotRecordADepthOnlyPassIsNamedAsTheReason()
    {
        // The default test device reports no raster-shadow capability, so the
        // diagnostic must name the device rather than the light's type. A backend
        // that cannot record the pass allocates no map, which is a different fact
        // from a light whose projection could not be derived.
        IReadOnlyList<RenderDiagnostic> diagnostics = ResolveDiagnostics(
            CreateLightingFrame([(Type: 1u, ShadowEnabled: 1u)]));

        RenderDiagnostic diagnostic = diagnostics.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported);
        await Assert.That(diagnostic.Message).Contains("depth-only pass");
        await Assert.That(diagnostic.Message).Contains("D3D12");
    }

    [Test]
    public async Task ALightThatGotItsShadowMapIsNotDiagnosed()
    {
        // The positive control. Without it every assertion here could pass because
        // the diagnostic fires unconditionally, which would make it noise the
        // moment shadows started working.
        using var device = new ShadowDiagnosticGraphicsDevice(supportsRasterShadows: true);
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => throw new InvalidOperationException("No image is decoded."));
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateLightingFrame([(Type: 1u, ShadowEnabled: 1u)]), 1, 1);
        _ = scene.Apply(CreateShadowTable(lightCount: 1, lightIndex: 0), 1, 2);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, exposure: 0f);

        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.ShadowUnsupported);
    }

    [Test]
    public async Task AnUnsupportedLightTypeIsNamedByTheProjectionItWouldNeed()
    {
        // A shadow-capable device with a sphere light that got no descriptor: only
        // a distant light has an exact light-space projection, and saying so is
        // what tells an author the difference between "not yet" and "not here".
        using var device = new ShadowDiagnosticGraphicsDevice(supportsRasterShadows: true);
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => throw new InvalidOperationException("No image is decoded."));
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateLightingFrame([(Type: 2u, ShadowEnabled: 1u)]), 1, 1);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, exposure: 0f);

        RenderDiagnostic diagnostic = resources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.ShadowUnsupported);
        await Assert.That(diagnostic.Message).Contains("sphere");
        await Assert.That(diagnostic.Message).Contains("only a distant light");
    }

    /// <summary>Builds an ABI v19 shadow table with one orthographic descriptor.</summary>
    private static byte[] CreateShadowTable(uint lightCount, uint lightIndex)
    {
        var bytes = new byte[24 + 288];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), lightIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 1024u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 1u);
        for (int element = 0; element < 16; element++)
        {
            double identity = element % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(40 + (element * 8)),
                identity);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(168 + (element * 8)),
                identity);
        }
        return bytes;
    }

    /// <summary>
    /// Builds the 1976-byte lighting frame with the given direct lights, each
    /// carrying an invertible identity transform so frame packing succeeds.
    /// </summary>
    private static byte[] CreateLightingFrame(
        IReadOnlyList<(uint Type, uint ShadowEnabled)> lights)
    {
        var bytes = new byte[LightingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 64);
        for (int index = 0; index < 16; index++)
        {
            double identity = index % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (index * 8)), identity);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (index * 8)), identity);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(LightCountOffset, 4),
            (uint)lights.Count);
        for (int light = 0; light < lights.Count; light++)
        {
            int entry = LightTableOffset + (light * LightEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry, 4), lights[light].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(entry + 4, 4),
                lights[light].ShadowEnabled);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28, 4), 1f);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8), 8),
                    element % 5 == 0 ? 1 : 0);
            }
        }
        return bytes;
    }

    private sealed class ShadowDiagnosticGraphicsDevice(bool supportsRasterShadows = false)
        : ISilkGraphicsDevice
    {
        public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

        public SilkGraphicsCapabilities Capabilities => new(
            "Shadow diagnostic test device",
            "test",
            SupportsCompute: false,
            IsSoftware: true)
        {
            SupportsRasterShadows = supportsRasterShadows,
        };

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new ShadowDiagnosticBuffer(size, usage);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ShadowDiagnosticBuffer(nuint size, SilkBufferUsage usage)
        : ISilkGraphicsBuffer
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];

        public nuint Size => size;

        public SilkBufferUsage Usage => usage;

        public void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));

        public void ReadbackForTesting(Span<byte> destination) =>
            _bytes.AsSpan(0, destination.Length).CopyTo(destination);

        public void Dispose()
        {
        }
    }
}
