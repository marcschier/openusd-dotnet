// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class SilkFiniteAreaShadowConformance
{
    private const uint Size = 64;
    private const int FrameSize = 23096;
    private const int LightTableOffset = 552;
    private const int LightCountOffset = 536;
    private const int DescriptorSize = 288;
    private const float ReceiverDepth = 0.2f;
    private const float CasterDepth = 0.8f;
    private const float CasterHalfExtent = 0.25f;
    private const int CenterX = 32;
    private const int PartialShadowX = 17;
    private const int LitControlX = 58;
    private const int SampleY = 32;

    internal static async Task RectEmitterShadowsMatchFiveRayReferenceAcrossUpdateAndRetirement(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        await Assert.That(device.Capabilities.SupportsRasterShadows)
            .IsTrue()
            .Because("This gate measures five perspective maps for one finite-area emitter.");

        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth =
            device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);

        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            CreateFrame(6.4f, shadowEnabled: false),
            CreateReceiver(),
            CreateCaster());
        _ = renderer.Render(color, depth);
        byte[] unshadowed = ReadPixels(color);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(0);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(0ul);
        await Assert.That(CountOccludingSamples(CenterX, 6.4f)).IsEqualTo(1);
        await Assert.That(CountOccludingSamples(PartialShadowX, 6.4f)).IsEqualTo(2);
        await Assert.That(CountOccludingSamples(LitControlX, 6.4f)).IsEqualTo(0);

        SilkMeshRendererConformance.Apply(
            renderer,
            2,
            CreateFrame(6.4f, shadowEnabled: true),
            CreateShadowTable(6.4f));
        _ = renderer.Render(color, depth);
        byte[] withShadows = ReadPixels(color);
        await Assert.That(renderer.Scene.Shadows.Descriptors.Count).IsEqualTo(5);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(5);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(1ul);
        int centerAttenuation = Red(unshadowed, CenterX) - Red(withShadows, CenterX);
        int partialAttenuation = Red(unshadowed, PartialShadowX) - Red(withShadows, PartialShadowX);
        await Assert.That(centerAttenuation)
            .IsGreaterThan(0)
            .Because("The center emitter sample's ray crosses the caster.");
        await Assert.That(partialAttenuation)
            .IsGreaterThan(centerAttenuation)
            .Because("The two blocked samples remove more light at the partial-shadow pixel.");
        await Assert.That(Red(withShadows, LitControlX))
            .IsEqualTo(Red(unshadowed, LitControlX))
            .Because("No emitter sample's ray to the control pixel crosses the caster.");

        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).SequenceEqual(withShadows)).IsTrue();
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(1ul);

        SilkMeshRendererConformance.Apply(
            renderer,
            3,
            CreateFrame(2f, shadowEnabled: true),
            CreateShadowTable(2f));
        _ = renderer.Render(color, depth);
        byte[] changed = ReadPixels(color);
        await Assert.That(CountOccludingSamples(PartialShadowX, 2f)).IsEqualTo(0);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(5);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(2ul);
        await Assert.That(changed.SequenceEqual(withShadows)).IsFalse();
        await Assert.That(Red(changed, PartialShadowX))
            .IsGreaterThan(Red(withShadows, PartialShadowX))
            .Because("The narrower emitter no longer blocks either reference ray at this pixel.");

        SilkMeshRendererConformance.Apply(
            renderer,
            4,
            CreateFrame(6.4f, shadowEnabled: true),
            CreateShadowTable(6.4f));
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).SequenceEqual(withShadows)).IsTrue();
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(3ul);

        SilkMeshRendererConformance.Apply(
            renderer,
            5,
            CreateShadowTable(0.0f));
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.Scene.Shadows.HasShadows).IsFalse();
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(0);
        await Assert.That(ReadPixels(color).SequenceEqual(unshadowed)).IsTrue();
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(3ul);
    }

    internal static async Task TransformedRectEmitterShadowsMatchFiveRayReference(
        ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        await Assert.That(device.Capabilities.SupportsRasterShadows)
            .IsTrue()
            .Because("Each transform control compares five independent sample rays with rendered pixels.");

        var cases = new[]
        {
            new TransformedEmitterCase(
                "rotated",
                [0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
                [Vector3.Zero, new(-0.2f, 3.2f, 0), new(0.2f, 3.2f, 0),
                    new(-0.2f, -3.2f, 0), new(0.2f, -3.2f, 0)],
                23, 37, 2),
            new TransformedEmitterCase(
                "nonuniformly scaled",
                [2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
                [Vector3.Zero, new(6.4f, 0.6f, 0), new(6.4f, -0.6f, 0),
                    new(-6.4f, 0.6f, 0), new(-6.4f, -0.6f, 0)],
                23, 32, 1),
            new TransformedEmitterCase(
                "sheared",
                [1, 0, 0, 0, 0.6, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
                [Vector3.Zero, new(3.32f, 0.2f, 0), new(3.08f, -0.2f, 0),
                    new(-3.08f, 0.2f, 0), new(-3.32f, -0.2f, 0)],
                9, 32, 2),
            new TransformedEmitterCase(
                "reflected",
                [-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
                [Vector3.Zero, new(-3.2f, 0.2f, 0), new(-3.2f, -0.2f, 0),
                    new(3.2f, 0.2f, 0), new(3.2f, -0.2f, 0)],
                8, 32, 2),
        };

        foreach (TransformedEmitterCase testCase in cases)
        {
            await AssertTransformedEmitterPixelsMatchReference(device, testCase);
        }
    }

    private static async Task AssertTransformedEmitterPixelsMatchReference(
        ISilkGraphicsDevice device,
        TransformedEmitterCase testCase)
    {
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Size,
            Size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth =
            device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var renderer = new SilkMeshRenderer(device);
        const float width = 6.4f;
        const int litControlX = 58;
        const int litControlY = 58;

        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            CreateFrame(width, shadowEnabled: false, emitterTransform: testCase.Transform),
            CreateReceiver(),
            CreateCaster());
        _ = renderer.Render(color, depth);
        byte[] unshadowed = ReadPixels(color);

        int referenceOccluders = CountOccludingSamples(
            testCase.PartialX,
            testCase.PartialY,
            width,
            testCase.ExpectedOffsets);
        await Assert.That(referenceOccluders).IsEqualTo(testCase.ExpectedOccludingSamples)
            .Because($"{testCase.Name} pixel's independently transformed emitter rays cross the caster.");
        await Assert.That(CountOccludingSamples(
                litControlX, litControlY, width, testCase.ExpectedOffsets))
            .IsEqualTo(0)
            .Because($"{testCase.Name} lit control has no ray crossing the caster.");
        await Assert.That(Red(unshadowed, litControlX, litControlY))
            .IsGreaterThan((byte)0)
            .Because($"{testCase.Name} control pixel must receive visible light.");

        SilkMeshRendererConformance.Apply(
            renderer,
            2,
            CreateFrame(width, shadowEnabled: true, emitterTransform: testCase.Transform),
            CreateShadowTable(width, testCase.ExpectedOffsets));
        _ = renderer.Render(color, depth);
        byte[] shadowed = ReadPixels(color);

        await Assert.That(renderer.Scene.Shadows.Descriptors.Count).IsEqualTo(5);
        await Assert.That(Red(shadowed, testCase.PartialX, testCase.PartialY))
            .IsLessThan(Red(unshadowed, testCase.PartialX, testCase.PartialY))
            .Because($"{testCase.Name} pixel's reference rays are blocked by the caster.");
        await Assert.That(Red(shadowed, litControlX, litControlY))
            .IsEqualTo(Red(unshadowed, litControlX, litControlY))
            .Because($"{testCase.Name} control pixel remains lit by all transformed samples.");
    }

    private readonly record struct TransformedEmitterCase(
        string Name,
        double[] Transform,
        Vector3[] ExpectedOffsets,
        int PartialX,
        int PartialY,
        int ExpectedOccludingSamples);

    private static byte[] CreateFrame(
        float width,
        bool shadowEnabled,
        double[]? emitterTransform = null)
    {
        var bytes = new byte[FrameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)Size));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)Size));
        double[] identity = SilkMeshRendererConformance.Identity();
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (element * sizeof(double))),
                identity[element]);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (element * sizeof(double))),
                identity[element]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(LightCountOffset), 1);

        Span<byte> light = bytes.AsSpan(LightTableOffset, 176);
        BinaryPrimitives.WriteUInt32LittleEndian(light, 3u);
        BinaryPrimitives.WriteUInt32LittleEndian(light[4..], shadowEnabled ? 1u : 0u);
        BinaryPrimitives.WriteSingleLittleEndian(light[8..], width);
        BinaryPrimitives.WriteSingleLittleEndian(light[12..], 0.4f);
        BinaryPrimitives.WriteSingleLittleEndian(light[16..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(light[20..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(light[24..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(light[28..], 1f);
        double[] transform = emitterTransform is null
            ? SilkMeshRendererConformance.Identity()
            : (double[])emitterTransform.Clone();
        transform[12] = 0.0;
        transform[13] = 0.0;
        transform[14] = 5.0;
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                light.Slice(32 + (element * sizeof(double)), sizeof(double)),
                transform[element]);
        }
        BinaryPrimitives.WriteSingleLittleEndian(light[160..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(light[164..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(light[168..], 1f);
        return bytes;
    }

    private static byte[] CreateReceiver() =>
        SilkMeshRendererConformance.CreateMeshCommand(
            1,
            "/World/FiniteArea/Receiver",
            [
                -1f, -1f, ReceiverDepth,
                 1f, -1f, ReceiverDepth,
                 1f,  1f, ReceiverDepth,
                -1f,  1f, ReceiverDepth,
            ],
            [0, 2, 1, 0, 3, 2],
            0,
            0,
            [1, 1, 1, 1]);

    private static byte[] CreateCaster() =>
        SilkMeshRendererConformance.CreateMeshCommand(
            2,
            "/World/FiniteArea/Caster",
            [
                -CasterHalfExtent, -CasterHalfExtent, CasterDepth,
                 CasterHalfExtent, -CasterHalfExtent, CasterDepth,
                 CasterHalfExtent,  CasterHalfExtent, CasterDepth,
                -CasterHalfExtent,  CasterHalfExtent, CasterDepth,
            ],
            [0, 2, 1, 0, 3, 2],
            0,
            0,
            [1, 1, 1, 1]);

    private static byte[] CreateShadowTable(float width)
        => CreateShadowTable(width, RectSampleOffsets(width, 0.4f));

    private static byte[] CreateShadowTable(float width, Vector3[] offsets)
    {
        uint descriptorCount = width > 0 ? 5u : 0u;
        var bytes = new byte[checked(24 + (int)(descriptorCount * DescriptorSize))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), descriptorCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), descriptorCount > 0 ? 1u : 0u);

        if (descriptorCount == 0)
        {
            return bytes;
        }

        var center = new Vector3(0, 0, (ReceiverDepth + CasterDepth) * 0.5f);
        double radius = Math.Sqrt(1.0 + 1.0 + (0.3 * 0.3));
        for (uint index = 0; index < descriptorCount; index++)
        {
            Vector3 offset = offsets[checked((int)index)];
            Vector3 eye = new(offset.X, offset.Y, 5f + offset.Z);
            BuildPerspectiveProjection(eye, center, radius,
                out double[] view, out double[] projection,
                out double farPlane, out double tangent);
            int entry = 24 + (checked((int)index) * DescriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), index);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), 1024u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 12), 8u);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 16 + (element * sizeof(double))),
                    view[element]);
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 144 + (element * sizeof(double))),
                    projection[element]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 272), 1.5f / 1024f);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 276),
                (float)(1.5 * (2.0 * farPlane * tangent / 1024.0)));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 280), 1f);
        }
        return bytes;
    }

    private static Vector3[] RectSampleOffsets(float width, float height)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        return
        [
            Vector3.Zero,
            new Vector3(halfWidth, halfHeight, 0),
            new Vector3(halfWidth, -halfHeight, 0),
            new Vector3(-halfWidth, halfHeight, 0),
            new Vector3(-halfWidth, -halfHeight, 0),
        ];
    }

    private static void BuildPerspectiveProjection(
        Vector3 eye,
        Vector3 center,
        double radius,
        out double[] view,
        out double[] projection,
        out double farPlane,
        out double tangent)
    {
        Vector3 eyeAxis = Vector3.Normalize(eye - center);
        Vector3 up = Math.Abs(eyeAxis.Y) > 0.99f
            ? Vector3.UnitX
            : Vector3.UnitY;
        Vector3 xAxis = Vector3.Normalize(Vector3.Cross(up, eyeAxis));
        Vector3 yAxis = Vector3.Normalize(Vector3.Cross(eyeAxis, xAxis));
        double distance = Vector3.Distance(eye, center);
        double padding = Math.Max(radius / 512.0, 1e-6);
        double nearPlane = distance - radius - padding;
        farPlane = distance + radius + padding;
        tangent = (radius / Math.Sqrt((distance - radius) * (distance + radius))) *
            (1.0 + (2.0 / 1024.0));

        view =
        [
            xAxis.X, yAxis.X, eyeAxis.X, 0,
            xAxis.Y, yAxis.Y, eyeAxis.Y, 0,
            xAxis.Z, yAxis.Z, eyeAxis.Z, 0,
            -Vector3.Dot(eye, xAxis), -Vector3.Dot(eye, yAxis), -Vector3.Dot(eye, eyeAxis), 1,
        ];
        projection = new double[16];
        projection[0] = 1.0 / tangent;
        projection[5] = 1.0 / tangent;
        projection[10] = -(farPlane + nearPlane) / (farPlane - nearPlane);
        projection[11] = -1.0;
        projection[14] = -(2.0 * farPlane * nearPlane) / (farPlane - nearPlane);
    }

    private static int CountOccludingSamples(int pixelX, float width)
        => CountOccludingSamples(pixelX, SampleY, width, expectedOffsets: null);

    private static int CountOccludingSamples(
        int pixelX,
        int pixelY,
        float width,
        IReadOnlyList<Vector3>? expectedOffsets)
    {
        return CountOccludingSamples(
            PixelWorldX(pixelX),
            PixelWorldY(pixelY),
            width,
            expectedOffsets);
    }

    private static int CountOccludingSamples(
        float receiverX,
        float receiverY,
        float width,
        IReadOnlyList<Vector3>? expectedOffsets = null)
    {
        const float fraction = (CasterDepth - ReceiverDepth) / (5f - ReceiverDepth);
        int occludingSamples = 0;
        foreach (Vector3 offset in expectedOffsets ?? RectSampleOffsets(width, 0.4f))
        {
            float casterX = receiverX + (fraction * (offset.X - receiverX));
            float casterY = receiverY + (fraction * (offset.Y - receiverY));
            if (Math.Abs(casterX) < CasterHalfExtent &&
                Math.Abs(casterY) < CasterHalfExtent)
            {
                occludingSamples++;
            }
        }
        return occludingSamples;
    }

    private static float PixelWorldX(int x) =>
        (2f * ((x + 0.5f) / Size)) - 1f;

    private static float PixelWorldY(int y) =>
        1f - (2f * ((y + 0.5f) / Size));

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    private static byte Red(byte[] pixels, int x) => Red(pixels, x, SampleY);

    private static byte Red(byte[] pixels, int x, int y) =>
        pixels[checked(((y * (int)Size) + x) * 4)];
}
