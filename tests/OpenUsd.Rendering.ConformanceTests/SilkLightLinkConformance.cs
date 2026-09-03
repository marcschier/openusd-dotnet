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
    /// Builds the 1976-byte lighting frame with a red distant light at index 0
    /// and a blue one at index 1, both aimed along +Z so they light the quads
    /// head on.
    /// </summary>
    private static byte[] CreateTwoLightFrame(uint width, uint height)
    {
        const int lightingSize = 1976;
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
        params (string Path, int InstanceIndex, uint LightMask, uint ShadowMask)[] entries) =>
        CreateLightLink(lightCount, domeCount: 0, entries);

    private static byte[] CreateLightLink(
        uint lightCount,
        uint domeCount,
        params (string Path, int InstanceIndex, uint LightMask, uint ShadowMask)[] entries)
    {
        uint allDomes = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(lightCount),
            .. BitConverter.GetBytes((uint)SilkLightLinkUnsupportedFeatures.None),
            .. BitConverter.GetBytes(domeCount),
        ];
        foreach ((string path, int instanceIndex, uint lightMask, uint shadowMask) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(lightMask));
            payload.AddRange(BitConverter.GetBytes(shadowMask));
            payload.AddRange(BitConverter.GetBytes(allDomes));
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
}
