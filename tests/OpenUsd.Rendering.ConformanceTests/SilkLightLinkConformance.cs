// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Draws two identical quads under two coloured scene lights and requires the
/// published UsdLux link table to change what reaches each of them.
/// </summary>
/// <remarks>
/// <para>
/// The case is analytic rather than a reference image. Two distant lights are
/// authored, one pure red and one pure blue, aimed straight down the view axis
/// at two white quads that differ only in their X translation. With no linking
/// every quad is magenta. The link table then excludes the red light from the
/// left quad and the blue light from the right quad, and the two quads must come
/// back pure blue and pure red respectively: the excluded channel has to fall to
/// the clear value, and the retained channel must not move.
/// </para>
/// <para>
/// That is exactly the property a per-draw mask can get wrong in ways a coverage
/// count cannot see. Binding the wrong surface block would leave both quads
/// magenta; batching the two masks together would give both quads whichever mask
/// was bound last; reading the mask from the wrong float would mask every light
/// or none. Each of those changes the measured channels here.
/// </para>
/// <para>
/// It runs on the D3D12 WARP and Vulkan SwiftShader devices, so the evidence is
/// cross-backend and needs no GPU.
/// </para>
/// </remarks>
internal static class SilkLightLinkConformance
{
    private const string RedLightOnly = "/World/RedOnly";
    private const string BlueLightOnly = "/World/BlueOnly";

    internal static async Task LinkedLightsReachOnlyTheirPrims(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        const uint size = 64;
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            size,
            size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        using var renderer = new SilkMeshRenderer(device);

        // Baseline: both lights reach both quads, so both are magenta.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateTwoLightFrame(size, size),
            CreateQuad(1, BlueLightOnly, -0.4),
            CreateQuad(2, RedLightOnly, 0.4));
        _ = renderer.Render(color, depth);
        byte[] unlinked = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasLinks)
            .IsFalse()
            .Because("No link table was published, so nothing may be retained.");
        await AssertLit(unlinked, size, LeftX, red: true, blue: true, "unlinked left quad");
        await AssertLit(unlinked, size, RightX, red: true, blue: true, "unlinked right quad");

        // Light 0 is red and light 1 is blue, matching the frame table below.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            CreateLightLink(
                lightCount: 2,
                (BlueLightOnly, SilkLightLinkCommand.AllInstances, 0b10u, 0b10u),
                (RedLightOnly, SilkLightLinkCommand.AllInstances, 0b01u, 0b01u)));
        _ = renderer.Render(color, depth);
        byte[] linked = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasLinks).IsTrue();
        await Assert.That(renderer.Scene.LightLinks.LightCount).IsEqualTo(2u);
        await Assert.That(renderer.Scene.LightLinks.Resolve(BlueLightOnly, 0).LightMask)
            .IsEqualTo(0b10u);
        await AssertLit(linked, size, LeftX, red: false, blue: true, "blue-linked quad");
        await AssertLit(linked, size, RightX, red: true, blue: false, "red-linked quad");

        // Retiring the table restores the unlinked result exactly, which is what
        // proves the mask is applied per draw rather than baked into a resource
        // that survives the collection being removed.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 3,
            CreateLightLink(lightCount: 0));
        _ = renderer.Render(color, depth);
        byte[] retired = ReadPixels(color);

        await Assert.That(renderer.Scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(retired.AsSpan().SequenceEqual(unlinked))
            .IsTrue()
            .Because("Retiring the link table must reproduce the unlinked image exactly.");
    }

    private const int LeftX = 19;
    private const int RightX = 44;

    private static async Task AssertLit(
        byte[] pixels,
        uint width,
        int x,
        bool red,
        bool blue,
        string what)
    {
        const int y = 32;
        int offset = checked(((y * (int)width) + x) * 4);
        string evidence =
            $"The {what} at ({x},{y}) was rgba({pixels[offset]},{pixels[offset + 1]}," +
            $"{pixels[offset + 2]},{pixels[offset + 3]}).";
        await Assert.That(pixels[offset] > 60).IsEqualTo(red).Because(evidence);
        await Assert.That(pixels[offset + 2] > 60).IsEqualTo(blue).Because(evidence);

        // Neither light emits green, so a green channel would mean the sample
        // landed on something other than the lit quad.
        await Assert.That(pixels[offset + 1]).IsLessThan((byte)60).Because(evidence);
        await Assert.That(pixels[offset + 3]).IsGreaterThan((byte)100).Because(evidence);
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    /// <summary>
    /// Builds the 23096-byte lighting frame with a red distant light at index 0
    /// and a blue one at index 1, both aimed along +Z so they light the quads
    /// head on.
    /// </summary>
    private static byte[] CreateTwoLightFrame(uint width, uint height)
    {
        const int lightingSize = 23096;
        const int lightCountOffset = 536;
        const int lightTableOffset = 552;
        const int lightEntrySize = 176;
        var bytes = new byte[lightingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)width));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)height));
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8)), identity[i]);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8)), identity[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), 2);

        float[][] colors = [[1, 0, 0], [0, 0, 1]];
        for (int light = 0; light < 2; light++)
        {
            int entry = lightTableOffset + (light * lightEntrySize);
            // OPENUSD_SILK_LIGHT_DISTANT. The frame light table carries the raw
            // ABI value; there is no managed enum for it.
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 1u);
            for (int component = 0; component < 3; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(entry + 16 + (component * 4)),
                    colors[light][component]);
            }

            // Intensity, then an identity light-to-world so the light points
            // along +Z, straight at the quads' front faces.
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28), 1f);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)),
                    identity[element]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 164), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 168), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 172), 0.5f);
        }
        return bytes;
    }

    private static byte[] CreateQuad(ulong id, string path, double x) =>
        SilkMeshRendererConformance.CreateMeshCommand(
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

    private static byte[] CreateLightLink(
        uint lightCount,
        params (string Path, int InstanceIndex, UInt128 LightMask, UInt128 ShadowMask)[] entries) =>
        CreateLightLink(lightCount, domeCount: 0, entries);

    private static byte[] CreateLightLink(
        uint lightCount,
        uint domeCount,
        params (string Path, int InstanceIndex, UInt128 LightMask, UInt128 ShadowMask)[] entries)
    {
        uint allDomes = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(lightCount),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, int instanceIndex, UInt128 lightMask, UInt128 shadowMask) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            byte[] prefix = new byte[44];
            BinaryPrimitives.WriteUInt128LittleEndian(prefix, lightMask);
            BinaryPrimitives.WriteUInt128LittleEndian(prefix.AsSpan(16), shadowMask);
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(32), allDomes);
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(36), instanceIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(40), (uint)pathBytes.Length);
            payload.AddRange(prefix);
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private const uint HighWordSize = 64;
    private static readonly string[] HighWordPaths =
    [
        "/World/HighWord/Receiver0",
        "/World/HighWord/Receiver1",
        "/World/HighWord/Receiver2",
        "/World/HighWord/Receiver3",
    ];
    private static readonly double[] HighWordPositions = [-0.6, -0.2, 0.2, 0.6];
    private static readonly int[] HighWordCentres = [13, 25, 38, 51];
    private static readonly int[] HighWordGaps = [3, 19, 32, 45, 60];

    // Synthetic ABI24 bytes on a real offscreen device, not native selection evidence.
    internal static async Task HighWordMasksUseDistinctSurfaceBindings(
        ISilkGraphicsDevice device,
        int index)
    {
        ArgumentNullException.ThrowIfNull(device);
        UInt128 blue = UInt128.One;
        UInt128 red = UInt128.One << index;
        var excluded = new SilkLightLinkMasks(blue, blue | red, 0u);
        var included = new SilkLightLinkMasks(blue | red, blue | red, 0u);
        SilkLightLinkMasks[] baselineMasks = [excluded, excluded, excluded, excluded];
        SilkLightLinkMasks[] alternatingMasks = [included, excluded, included, excluded];
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            HighWordSize,
            HighWordSize,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(HighWordSize, HighWordSize));
        using var renderer = new SilkMeshRenderer(device);

        var page = new List<byte[]>
        {
            CreateHighWordFrame(index),
            CreateHighWordLinks(checked((uint)index + 1), baselineMasks),
        };
        for (int receiver = 0; receiver < HighWordPaths.Length; receiver++)
        {
            page.Add(CreateHighWordQuad(receiver));
        }
        SilkMeshRendererConformance.Apply(renderer, revision: 1, [.. page]);
        SilkMeshRenderResult baselineResult = renderer.Render(color, depth);
        byte[] baseline = ReadPixels(color);

        // Four distinct geometry paths sort in Receiver0..3 order. Their material,
        // headlight and low mask words are identical. The baseline also exercises
        // consecutive same-mask draws rather than only resource-cache lookups.
        await Assert.That(baselineResult.DrawCount).IsEqualTo(4);
        await AssertHighWordRegions(baseline, $"bit {index} blue baseline");
        await AssertHighWordPixels(baseline, baselineMasks, index, "blue baseline");
        ISilkGraphicsBuffer[] baselineBuffers =
            await AssertHighWordSurfaces(renderer, baselineMasks, index, "blue baseline");

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            CreateHighWordLinks(checked((uint)index + 1), alternatingMasks));
        SilkMeshRenderResult alternatingResult = renderer.Render(color, depth);
        byte[] alternating = ReadPixels(color);

        // A -> B -> A -> B requires rebinding three times. Truncating the
        // last-bound key to the low word leaves the wrong red membership behind.
        await Assert.That(alternatingResult.DrawCount).IsEqualTo(4);
        await AssertHighWordRegions(alternating, $"bit {index} alternating masks");
        await AssertHighWordStableCoverage(baseline, alternating, "alternating masks");
        await AssertHighWordPixels(
            alternating, alternatingMasks, index, "alternating masks", baseline);
        ISilkGraphicsBuffer[] alternatingBuffers =
            await AssertHighWordSurfaces(renderer, alternatingMasks, index, "alternating masks");
        await Assert.That(alternatingBuffers[1]).IsSameReferenceAs(baselineBuffers[1]);
        await Assert.That(alternatingBuffers[3]).IsSameReferenceAs(baselineBuffers[3]);

        SilkMeshRenderResult repeatedResult = renderer.Render(color, depth);
        byte[] repeated = ReadPixels(color);
        await Assert.That(repeatedResult.DrawCount).IsEqualTo(4);
        await Assert.That(repeated.AsSpan().SequenceEqual(alternating)).IsTrue()
            .Because("Reusing the same full masks must preserve every rendered texel.");
        ISilkGraphicsBuffer[] repeatedBuffers =
            await AssertHighWordSurfaces(renderer, alternatingMasks, index, "unchanged masks");
        for (int receiver = 0; receiver < HighWordPaths.Length; receiver++)
        {
            await Assert.That(repeatedBuffers[receiver])
                .IsSameReferenceAs(alternatingBuffers[receiver]);
        }

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 3,
            CreateHighWordLinks(checked((uint)index + 1), baselineMasks));
        SilkMeshRenderResult restoredResult = renderer.Render(color, depth);
        byte[] restored = ReadPixels(color);
        await Assert.That(restoredResult.DrawCount).IsEqualTo(4);
        await Assert.That(restored.AsSpan().SequenceEqual(baseline)).IsTrue()
            .Because("Removing the high light membership must restore the blue-only image.");
        await AssertHighWordRegions(restored, "restored blue baseline");
        _ = await AssertHighWordSurfaces(renderer, baselineMasks, index, "restored masks");
    }

    internal static async Task HighShadowOnlyChangePreservesIllumination(
        ISilkGraphicsDevice device,
        int index)
    {
        ArgumentNullException.ThrowIfNull(device);
        UInt128 blue = UInt128.One;
        UInt128 high = UInt128.One << index;
        var lowShadow = new SilkLightLinkMasks(blue | high, blue, 0u);
        var highShadow = new SilkLightLinkMasks(blue | high, blue | high, 0u);
        SilkLightLinkMasks[] baselineMasks = [lowShadow, lowShadow, lowShadow, lowShadow];
        SilkLightLinkMasks[] changedMasks = [highShadow, lowShadow, highShadow, lowShadow];
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            HighWordSize,
            HighWordSize,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(HighWordSize, HighWordSize));
        using var renderer = new SilkMeshRenderer(device);
        var page = new List<byte[]>
        {
            CreateHighWordFrame(index),
            CreateHighWordLinks(checked((uint)index + 1), baselineMasks),
        };
        for (int receiver = 0; receiver < HighWordPaths.Length; receiver++)
        {
            page.Add(CreateHighWordQuad(receiver));
        }
        SilkMeshRendererConformance.Apply(renderer, revision: 1, [.. page]);
        SilkMeshRenderResult baselineResult = renderer.Render(color, depth);
        byte[] baseline = ReadPixels(color);
        await Assert.That(baselineResult.DrawCount).IsEqualTo(4);
        await AssertHighWordRegions(baseline, $"bit {index} low-shadow baseline");
        await AssertHighWordPixels(baseline, baselineMasks, index, "low-shadow baseline");
        _ = await AssertHighWordSurfaces(renderer, baselineMasks, index, "low-shadow baseline");

        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 2,
            CreateHighWordLinks(checked((uint)index + 1), changedMasks));
        SilkMeshRenderResult changedResult = renderer.Render(color, depth);
        byte[] changed = ReadPixels(color);
        await Assert.That(changedResult.DrawCount).IsEqualTo(4);
        await Assert.That(changed.AsSpan().SequenceEqual(baseline)).IsTrue()
            .Because(
                "High shadow membership restricts casting, not direct-light reception; " +
                "all four receivers must retain both the red light and the blue baseline.");
        await AssertHighWordRegions(changed, "independent high-shadow masks");
        await AssertHighWordPixels(changed, changedMasks, index, "independent high-shadow masks");
        _ = await AssertHighWordSurfaces(renderer, changedMasks, index, "high-shadow masks");

        // There is deliberately no SHADOW command here. Surface readback proves
        // distinct retained bytes, not which buffer SetStorageBuffer last bound.
        // Actual caster recording is a separate unit test. The unit case
        // ConsecutiveSurfaceBindingsObserveEveryMaskWord separately records the
        // renderer's shadow-only binding operation; unchanged RGB cannot prove it.
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 3,
            CreateHighWordLinks(checked((uint)index + 1), baselineMasks));
        SilkMeshRenderResult restoredResult = renderer.Render(color, depth);
        byte[] restored = ReadPixels(color);
        await Assert.That(restoredResult.DrawCount).IsEqualTo(4);
        await Assert.That(restored.AsSpan().SequenceEqual(baseline)).IsTrue();
        _ = await AssertHighWordSurfaces(renderer, baselineMasks, index, "restored shadow masks");
    }

    private static async Task<ISilkGraphicsBuffer[]> AssertHighWordSurfaces(
        SilkMeshRenderer renderer,
        SilkLightLinkMasks[] expected,
        int index,
        string what)
    {
        await Assert.That(renderer.Scene.MeshesByPath.Count).IsEqualTo(4);
        await Assert.That(renderer.Scene.LightLinks.HasLinks).IsTrue();
        await Assert.That(renderer.Scene.LightLinks.LightCount).IsEqualTo(checked((uint)index + 1));
        await Assert.That(renderer.Scene.LightLinks.DomeCount).IsEqualTo(0u);
        var buffers = new ISilkGraphicsBuffer[HighWordPaths.Length];
        for (int receiver = 0; receiver < HighWordPaths.Length; receiver++)
        {
            string path = HighWordPaths[receiver];
            string evidence = $"{what}, {path}, high bit {index}";
            SilkMeshData mesh = renderer.Scene.MeshesByPath[(path, 0)];
            if (receiver > 0)
            {
                await Assert.That(mesh.MaterialPath)
                    .IsEqualTo(renderer.Scene.MeshesByPath[(HighWordPaths[0], 0)].MaterialPath)
                    .Because("Only the masks may distinguish consecutive surface keys. " + evidence);
            }
            SilkLightLinkMasks resolved = renderer.Scene.LightLinks.Resolve(path, 0);
            await Assert.That(resolved.LightMask).IsEqualTo(expected[receiver].LightMask)
                .Because(evidence);
            await Assert.That(resolved.ShadowMask).IsEqualTo(expected[receiver].ShadowMask)
                .Because(evidence);
            await Assert.That(resolved.DomeMask).IsEqualTo(expected[receiver].DomeMask)
                .Because(evidence);

            buffers[receiver] = renderer.GpuResources.RequireSurfaceBuffer(
                renderer.Scene, mesh, RenderHeadlight.Deterministic);
            await Assert.That(buffers[receiver].Size).IsEqualTo((nuint)240).Because(evidence);
            var bytes = new byte[240];
            buffers[receiver].ReadbackForTesting(bytes);
            await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(bytes.AsSpan(208, 16)))
                .IsEqualTo(expected[receiver].LightMask).Because(evidence);
            await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(bytes.AsSpan(224, 16)))
                .IsEqualTo(expected[receiver].ShadowMask).Because(evidence);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(76, 4)))
                .IsEqualTo(0u).Because(evidence);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(140, 4)))
                .IsEqualTo(0u).Because(evidence);
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(192, 4)))
                .IsEqualTo((float)expected[receiver].DomeMask).Because(evidence);
            for (int previous = 0; previous < receiver; previous++)
            {
                await Assert.That(ReferenceEquals(buffers[receiver], buffers[previous]))
                    .IsEqualTo(expected[receiver] == expected[previous])
                    .Because($"{evidence}: surface reuse must compare the complete mask value.");
            }
        }
        return buffers;
    }

    private static async Task AssertHighWordPixels(
        byte[] pixels,
        SilkLightLinkMasks[] expected,
        int index,
        string what,
        byte[]? blueBaseline = null)
    {
        for (int receiver = 0; receiver < HighWordCentres.Length; receiver++)
        {
            bool red = (expected[receiver].LightMask & (UInt128.One << index)) != UInt128.Zero;
            foreach (int y in new[] { 28, 32, 36 })
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = HighWordCentres[receiver] + dx;
                    int offset = checked(((y * (int)HighWordSize) + x) * 4);
                    string evidence =
                        $"{what}, bit {index}, receiver {receiver} at ({x},{y}): " +
                        $"rgba({pixels[offset]},{pixels[offset + 1]}," +
                        $"{pixels[offset + 2]},{pixels[offset + 3]}).";
                    if (red)
                    {
                        await Assert.That(pixels[offset]).IsGreaterThan((byte)60).Because(evidence);
                    }
                    else
                    {
                        await Assert.That(pixels[offset]).IsLessThan((byte)3).Because(evidence);
                    }
                    await Assert.That(pixels[offset + 1]).IsLessThan((byte)3).Because(evidence);
                    await Assert.That(pixels[offset + 2]).IsGreaterThan((byte)12).Because(evidence);
                    if (blueBaseline is not null)
                    {
                        if (red)
                        {
                            await Assert.That((int)pixels[offset] - blueBaseline[offset])
                                .IsGreaterThan(48).Because(evidence);
                        }
                        else
                        {
                            await Assert.That(pixels[offset]).IsEqualTo(blueBaseline[offset])
                                .Because("Excluded reference receivers must not gain any red. " + evidence);
                        }
                        for (int channel = 1; channel < 4; channel++)
                        {
                            await Assert.That(pixels[offset + channel])
                                .IsEqualTo(blueBaseline[offset + channel])
                                .Because("A red-only link edit must preserve the other channels. " + evidence);
                        }
                    }
                }
            }
        }
    }

    private static async Task AssertHighWordRegions(byte[] pixels, string what)
    {
        bool[] covered = HighWordCoverage(pixels);
        foreach (int y in new[] { 28, 32, 36 })
        {
            foreach (int centre in HighWordCentres)
            {
                await Assert.That(covered[(y * (int)HighWordSize) + centre]).IsTrue()
                    .Because($"{what}: receiver centre ({centre},{y}) must be covered.");
            }
            foreach (int gap in HighWordGaps)
            {
                await Assert.That(covered[(y * (int)HighWordSize) + gap]).IsFalse()
                    .Because($"{what}: gap ({gap},{y}) must remain cleared.");
            }
        }
        int runs = 0;
        int columns = 0;
        bool previous = false;
        for (int x = 0; x < (int)HighWordSize; x++)
        {
            bool current = covered[(32 * (int)HighWordSize) + x];
            if (current)
            {
                columns++;
                if (!previous)
                {
                    runs++;
                }
            }
            previous = current;
        }
        await Assert.That(runs).IsEqualTo(4).Because(what);
        await Assert.That(columns).IsGreaterThan(24).Because(what);
    }

    private static async Task AssertHighWordStableCoverage(
        byte[] baseline,
        byte[] pixels,
        string what)
    {
        await Assert.That(HighWordCoverage(pixels).AsSpan().SequenceEqual(HighWordCoverage(baseline)))
            .IsTrue().Because($"{what}: linking must not move, duplicate or drop a receiver.");
        await Assert.That(pixels.AsSpan(0, 4).SequenceEqual(baseline.AsSpan(0, 4)))
            .IsTrue().Because($"{what}: the excluded background must not change.");
    }

    private static bool[] HighWordCoverage(byte[] pixels)
    {
        var covered = new bool[pixels.Length / 4];
        for (int texel = 0; texel < covered.Length; texel++)
        {
            int offset = texel * 4;
            covered[texel] =
                pixels[offset] != pixels[0] ||
                pixels[offset + 1] != pixels[1] ||
                pixels[offset + 2] != pixels[2] ||
                pixels[offset + 3] != pixels[3];
        }
        return covered;
    }

    private static byte[] CreateHighWordQuad(int receiver) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            checked((ulong)receiver + 1),
            HighWordPaths[receiver],
            [
                -0.15f, -0.35f, 0.4f,
                 0.15f, -0.35f, 0.4f,
                 0.15f,  0.35f, 0.4f,
                -0.15f,  0.35f, 0.4f,
            ],
            [0, 2, 1, 0, 3, 2],
            HighWordPositions[receiver],
            0,
            [1, 1, 1, 1]);

    private static byte[] CreateHighWordFrame(int index)
    {
        // A full ABI24 table, independent of the two-light fixture above.
        var bytes = new byte[23096];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), (int)HighWordSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), (int)HighWordSize);
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), identity[element]);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), checked((uint)index + 1));
        // Authored-direct flag candidate 0x1: the parent must confirm its encoding.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(540), 0x1u);
        for (int light = 0; light <= index; light++)
        {
            int entry = 552 + (light * 176);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 1u); // distant
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 16), light == index ? 1f : 0f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 24), light == 0 ? 1f : 0f);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 28), light == index ? 1f : light == 0 ? 0.3f : 0f);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)), identity[element]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 164), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 172), 0.5f);
        }
        // Reserved header words, inactive slots and ambient float4 at 23080 stay zero.
        return bytes;
    }

    private static byte[] CreateHighWordLinks(uint lightCount, SilkLightLinkMasks[] masks)
    {
        int size = 24;
        foreach (string path in HighWordPaths)
        {
            size += 44 + Encoding.UTF8.GetByteCount(path);
        }
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)masks.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(16), (uint)SilkLightLinkUnsupportedFeatures.None);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 0u);
        int entry = 24;
        for (int receiver = 0; receiver < masks.Length; receiver++)
        {
            byte[] path = Encoding.UTF8.GetBytes(HighWordPaths[receiver]);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(entry), masks[receiver].LightMask);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(entry + 16), masks[receiver].ShadowMask);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 32), masks[receiver].DomeMask);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(entry + 36), SilkLightLinkCommand.AllInstances);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 40), (uint)path.Length);
            path.CopyTo(bytes, entry + 44);
            entry += 44 + path.Length;
        }
        return bytes;
    }
}
