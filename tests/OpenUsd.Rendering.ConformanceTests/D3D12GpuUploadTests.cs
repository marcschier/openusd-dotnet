// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using Silk.NET.Direct3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class D3D12GpuUploadTests
{
    [Test]
    public async Task DefaultUploadsKeepTheirExistingHeapAndUnsupportedOptInIsExplicit()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 is available only on Windows.");
            return;
        }
        await Assert.That(() => OperatingSystem.IsWindows()
                ? D3D12SilkGraphicsDevice.Create(useWarp: true, useGpuUploadHeaps: true)
                : throw new PlatformNotSupportedException())
            .Throws<NotSupportedException>();
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        using ISilkGraphicsBuffer buffer = device.CreateBuffer(
            64, SilkBufferUsage.Vertex | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        await Assert.That(device.UsesGpuUploadHeaps).IsFalse();
        await Assert.That(Heap(buffer)).IsEqualTo(HeapType.Upload);
        buffer.Write([1, 7, 3, 9]);
        byte[] copy = new byte[64];
        buffer.ReadbackForTesting(copy);
        await Assert.That(copy.AsSpan(0, 4).SequenceEqual(new byte[] { 1, 7, 3, 9 })).IsTrue();
    }

    [Test]
    public async Task RequiredHardwareUsesGpuUploadHeapsAndPreservesAdmissionAndBytes()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED=1 on supported D3D12 hardware.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: false, useGpuUploadHeaps: true);
        var budget = new SilkGpuBufferBudget(4096);
        budget.ConfigureDevice(device);
        await Assert.That(device.UsesGpuUploadHeaps).IsTrue();
        using (ISilkGraphicsBuffer buffer = device.CreateBuffer(
            4096, SilkBufferUsage.Vertex | SilkBufferUsage.Storage | SilkBufferUsage.Upload))
        {
            await Assert.That(Heap(buffer)).IsEqualTo(HeapType.GpuUpload);
            byte[] expected = new byte[4096];
            for (int index = 0; index < expected.Length; index++)
            {
                expected[index] = (byte)(index % 251);
            }
            buffer.Write(expected);
            byte[] actual = new byte[4096];
            buffer.ReadbackForTesting(actual);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            await Assert.That(() => OperatingSystem.IsWindows()
                    ? device.CreateBuffer(1, SilkBufferUsage.Upload)
                    : throw new PlatformNotSupportedException())
                .Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(4096ul);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        using (ISilkGraphicsBuffer storage = device.CreateBuffer(64, SilkBufferUsage.Vertex | SilkBufferUsage.Storage))
        {
            await Assert.That(Heap(storage)).IsEqualTo(HeapType.Default);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task RequiredHardwareGpuUploadGeometryMatchesDefaultColorAndDepth()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED=1 on supported D3D12 hardware.");
            return;
        }
        (byte[] color, float[] depth) = Render(useGpuUploadHeaps: false);
        (byte[] localColor, float[] localDepth) = Render(useGpuUploadHeaps: true);
        await Assert.That(localColor.SequenceEqual(color)).IsTrue();
        await Assert.That(localDepth.SequenceEqual(depth)).IsTrue();
        await Assert.That(depth.Count(static value => value < 1)).IsGreaterThan(100);
    }

    [SupportedOSPlatform("windows")]
    private static (byte[] Color, float[] Depth) Render(bool useGpuUploadHeaps)
    {
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: false, useGpuUploadHeaps: useGpuUploadHeaps);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 32));
        SilkMeshRendererConformance.Apply(renderer, 1,
            SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(1, "/Triangle",
                [-0.8f, -0.8f, 0.5f, 0, 0.8f, 0.5f, 0.8f, -0.8f, 0.5f], [0, 1, 2]));
        _ = renderer.Render(color, depth);
        byte[] rgba = new byte[32 * 32 * 4];
        float[] values = new float[32 * 32];
        color.ReadbackForTesting(rgba);
        depth.ReadbackForTesting(values);
        return (rgba, values);
    }

    [SupportedOSPlatform("windows")]
    private static unsafe HeapType Heap(ISilkGraphicsBuffer buffer)
    {
        HeapProperties properties;
        HeapFlags flags;
        Marshal.ThrowExceptionForHR(((D3D12SilkGraphicsBuffer)buffer).Resource->GetHeapProperties(&properties, &flags));
        return properties.Type;
    }
}
