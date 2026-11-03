// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static partial class SilkShadowConformance
{
    internal static async Task EveryPointCubeFaceUpdatesAndRestoresPixels(ISilkGraphicsDevice device)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        Matrix4x4[] rotations =
        [
            Matrix4x4.CreateRotationY(-MathF.PI / 2),
            Matrix4x4.CreateRotationY(MathF.PI / 2),
            Matrix4x4.CreateRotationX(MathF.PI / 2),
            Matrix4x4.CreateRotationX(-MathF.PI / 2),
            Matrix4x4.CreateRotationY(MathF.PI),
            Matrix4x4.Identity,
        ];
        ulong revision = 0;
        int? referenceBrightness = null;
        for (int face = 0; face < rotations.Length; face++)
        {
            Matrix4x4 rotation = rotations[face];
            byte[] frame = CreateShadowLightFrame(0, 0);
            WriteCubeMatrix(frame.AsSpan(16), Matrix4x4.Transpose(rotation));
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(552), 2);
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(724), 0);
            Vector3 position = Vector3.Transform(new Vector3(0, 0, 1.4f), rotation);
            WriteCubeMatrix(frame.AsSpan(584), Matrix4x4.CreateTranslation(position));
            byte[] receiver = CreateReceiver();
            byte[] caster = CreateCaster(0);
            WriteCubeMatrix(receiver.AsSpan(80), rotation);
            WriteCubeMatrix(caster.AsSpan(80), rotation);
            byte[][] meshes = face % 2 == 0 ? [receiver, caster] : [caster, receiver];
            SilkMeshRendererConformance.Apply(renderer, ++revision, frame, meshes[0], meshes[1], CreateShadow(0));
            _ = renderer.Render(color, depth);
            byte[] unshadowed = ReadPixels(color);
            int center = (int)(((Size / 2 * Size) + (Size / 2)) * 4);
            int clear = (int)(((Size / 2 * Size) + LitX) * 4);
            int brightness = unshadowed[center];
            await Assert.That(brightness).IsGreaterThan(30);
            if (referenceBrightness is int expected)
            {
                await Assert.That(Math.Abs(brightness - expected)).IsLessThanOrEqualTo(3);
            }
            referenceBrightness = brightness;
            SilkMeshRendererConformance.Apply(renderer, ++revision, CreatePointCubeTable(position));
            _ = renderer.Render(color, depth);
            byte[] shadowed = ReadPixels(color);
            await Assert.That((int)shadowed[center]).IsLessThan(brightness - 8);
            await Assert.That(Math.Abs(shadowed[clear] - unshadowed[clear])).IsLessThanOrEqualTo(3);
            ulong retainedCount = renderer.ShadowMapRenderCount;
            _ = renderer.Render(color, depth);
            await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(retainedCount);
            await Assert.That(ReadPixels(color).SequenceEqual(shadowed)).IsTrue();
            byte[] moved = CreateCaster(0.6);
            WriteCubeMatrix(moved.AsSpan(80), Matrix4x4.CreateTranslation(0.6f, 0, 0) * rotation);
            SilkMeshRendererConformance.Apply(renderer, ++revision, moved);
            _ = renderer.Render(color, depth);
            await Assert.That((int)ReadPixels(color)[center]).IsGreaterThan(shadowed[center] + 8);
            SilkMeshRendererConformance.Apply(renderer, ++revision, caster);
            _ = renderer.Render(color, depth);
            await Assert.That(ReadPixels(color).SequenceEqual(shadowed)).IsTrue();
            SilkMeshRendererConformance.Apply(renderer, ++revision, CreateShadow(0));
            _ = renderer.Render(color, depth);
            await Assert.That(ReadPixels(color).SequenceEqual(unshadowed)).IsTrue();
        }
    }

    private static byte[] CreatePointCubeTable(Vector3 position)
    {
        var result = new byte[24 + (6 * 288)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), 1);
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        const double near = 0.001;
        const double far = 4;
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, -(far + near) / (far - near), -1,
            0, 0, -(2 * far * near) / (far - near), 0,
        ];
        for (int face = 0; face < axes.Length; face++)
        {
            Span<byte> entry = result.AsSpan(24 + (face * 288), 288);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)face);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], 1024);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)SilkShadowDescriptorOptions.CubeFace);
            Vector3 up = Math.Abs(axes[face].Y) > 0.5f ? Vector3.UnitZ : Vector3.UnitY;
            WriteCubeMatrix(entry[16..], Matrix4x4.CreateLookAt(position, position + axes[face], up));
            for (int element = 0; element < projection.Length; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(entry[(144 + (element * 8))..], projection[element]);
            }
            BinaryPrimitives.WriteSingleLittleEndian(entry[272..], 0.000001f);
            BinaryPrimitives.WriteSingleLittleEndian(entry[276..], 0.005f);
            BinaryPrimitives.WriteSingleLittleEndian(entry[280..], 1);
        }
        return result;
    }

    private static void WriteCubeMatrix(Span<byte> destination, Matrix4x4 matrix)
    {
        float[] elements =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        ];
        for (int element = 0; element < elements.Length; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(destination[(element * 8)..], elements[element]);
        }
    }
}
