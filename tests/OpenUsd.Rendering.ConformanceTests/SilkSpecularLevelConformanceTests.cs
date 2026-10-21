// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkSpecularLevelConformanceTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, -0.5f)]
    [Arguments(SilkGraphicsBackend.Vulkan, -0.5f)]
    [Arguments(SilkGraphicsBackend.D3D12, 0.5f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.5f)]
    public async Task DielectricCurveAndMetallicMixtureMatchIndependentLinearControls(
        SilkGraphicsBackend backend, float z)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        new SilkGpuBufferBudget(1_048_576).ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 32));
        byte[] mesh = VolumeCommandAuthoring.WithMaterialBinding(SilkMeshRendererConformance.CreateMeshCommand(
            1, "/Mesh", [-0.9f, -0.9f, z, 0.9f, -0.9f, z, 0.9f, 0.9f, z, -0.9f, 0.9f, z],
            [0, 1, 2, 0, 2, 3]), "/Material");
        using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity(
                )), .. mesh], 2);
        renderer.ApplyPage(initial);
        ulong revision = 1;
        float[] render(float? level, float metallic, float ior = 1.5f)
        {
            using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, ++revision,
                Material(level, metallic, ior), 1);
            _ = renderer.ApplyAndRender(page, color, depth);
            byte[] bytes = new byte[32 * 32 * 8];
            color.ReadbackForTesting(bytes);
            float[] result = new float[bytes.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (float)BinaryPrimitives.ReadHalfLittleEndian(bytes.AsSpan(i * 2));
            }
            return result;
        }
        float[] zero = render(0, 0);
        float[] one = render(1, 0);
        float[] half = render(0.5f, 0);
        float[] metal = render(0, 1);
        float[] metalFullLevel = render(1, 1);
        float[] mixed = render(0.5f, 0.4f);
        float ratio = MathF.Sqrt(0.08f);
        float[] unitCurveReference = render(null, 0, (1 + ratio) / (1 - ratio));
        float[] depths = new float[32 * 32];
        depth.ReadbackForTesting(depths);
        int tested = 0;
        for (int pixel = 0; pixel < depths.Length; pixel++)
        {
            if (depths[pixel] >= 1)
            {
                continue;
            }
            tested++;
            for (int channel = 0; channel < 3; channel++)
            {
                int index = pixel * 4 + channel;
                float diffuse = channel switch { 0 => 0.2f, 1 => 0.4f, _ => 0.6f };
                await Assert.That(zero[index]).IsEqualTo(diffuse).Within(0.0005f);
                await Assert.That(one[index]).IsEqualTo(unitCurveReference[index]).Within(0.001f);
                await Assert.That(half[index]).IsEqualTo((zero[index] + one[index]) * 0.5f).Within(0.001f);
                await Assert.That(mixed[index]).IsEqualTo(half[index] * 0.6f + metal[index] * 0.4f).Within(0.001f);
                await Assert.That(metalFullLevel[index]).IsEqualTo(metal[index]);
            }
        }
        await Assert.That(tested).IsGreaterThan(500);
        await Assert.That(one.SequenceEqual(zero)).IsFalse();
        await Assert.That(mixed.SequenceEqual(half)).IsFalse();
    }

    private static byte[] Material(float? level, float metallic, float ior)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        const string path = "/Material";
        writer.Write((uint)SilkCommandType.MaterialUpsert);
        writer.Write(0u);
        writer.Write(VolumeCommandAuthoring.ComputeStableHash(path));
        writer.Write((uint)path.Length);
        writer.Write((uint)SilkSurfaceKind.MdlDistilled);
        writer.Write(level.HasValue ? 5u : 4u);
        writer.Write(0u);
        writer.Write(Encoding.UTF8.GetBytes(path));
        scalar(SilkMaterialParameter.DiffuseColor, [0.2f, 0.4f, 0.6f]);
        scalar(SilkMaterialParameter.Metallic, [metallic]);
        scalar(SilkMaterialParameter.Roughness, [0.6f]);
        scalar(SilkMaterialParameter.Ior, [ior]);
        if (level.HasValue)
        {
            scalar(SilkMaterialParameter.OmniPbrSpecularLevel, [level.Value]);
        }
        writer.Write(0u);
        writer.Write(0u);
        foreach (float value in new float[] { 1, 0, 0, 1, 0, 0 })
        {
            writer.Write(value);
        }
        byte[] bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        return bytes;

        void scalar(SilkMaterialParameter parameter, float[] values)
        {
            writer.Write((uint)parameter);
            writer.Write((uint)values.Length);
            foreach (float value in values)
            {
                writer.Write(value);
            }
        }
    }
}
