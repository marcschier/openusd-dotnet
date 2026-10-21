// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class D3D12UniformPoolTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task TinyUniformsShareCommittedPagesWithoutSharingTheirBytes(bool gpuUpload, bool storage)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 is Windows-only.");
            return;
        }
        if (gpuUpload && Environment.GetEnvironmentVariable("OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_TEST_D3D12_GPU_UPLOADS_REQUIRED=1 on supported hardware.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: !gpuUpload, useGpuUploadHeaps: gpuUpload);
        var budget = new SilkGpuBufferBudget(160_000);
        budget.ConfigureDevice(device);
        var buffers = new List<ISilkGraphicsBuffer>();
        var resources = new HashSet<nint>();
        var addresses = new HashSet<ulong>();
        try
        {
            for (int index = 0; index < 1000; index++)
            {
                SilkBufferUsage usage = SilkBufferUsage.Uniform | SilkBufferUsage.Upload |
                    (storage ? SilkBufferUsage.Storage : 0);
                ISilkGraphicsBuffer buffer = device.CreateBuffer(160, usage);
                buffers.Add(buffer);
                resources.Add(Resource(buffer));
                addresses.Add(Address(buffer));
                byte[] bytes = new byte[160];
                Array.Fill(bytes, (byte)(index % 251));
                buffer.Write(bytes);
            }
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(160_000ul);
            await Assert.That(resources.Count).IsEqualTo(4);
            await Assert.That(addresses.Count).IsEqualTo(1000);
            await Assert.That(addresses.All(static value => value % 256 == 0)).IsTrue();
            for (int index = 0; index < buffers.Count; index++)
            {
                byte[] actual = ReadUniform(buffers[index]);
                await Assert.That(actual.All(value => value == (byte)(index % 251))).IsTrue();
            }
            if (storage)
            {
                byte[] readback = new byte[160];
                buffers[1].ReadbackForTesting(readback);
                await Assert.That(readback.All(static value => value == 1)).IsTrue();
            }
        }
        finally
        {
            foreach (ISilkGraphicsBuffer buffer in buffers)
            {
                buffer.Dispose();
            }
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(device.UniformPageCountForTesting).IsEqualTo(0);
    }

    [Test]
    public async Task LeasedUniformRangesCannotBeReusedUntilTheirFinalOwnerReleases()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 is Windows-only.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create();
        var budget = new SilkGpuBufferBudget(4 * 256);
        budget.ConfigureDevice(device);
        using ISilkGraphicsBuffer anchor = device.CreateBuffer(256, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer first = device.CreateBuffer(256, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        ulong address = Address(first);
        byte[] pattern = new byte[256];
        Array.Fill(pattern, (byte)0x5a);
        first.Write(pattern);
        using IDisposable lease = Lease(first);
        first.Dispose();
        using ISilkGraphicsBuffer second = device.CreateBuffer(256, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        await Assert.That(Address(second)).IsNotEqualTo(address);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(768ul);
        lease.Dispose();
        using ISilkGraphicsBuffer reused = device.CreateBuffer(256, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        await Assert.That(Address(reused)).IsEqualTo(address);
        await Assert.That(ReadUniform(reused).All(static value => value == 0)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(768ul);
    }

    [Test]
    public async Task UniformPageRolloverPreservesOffsetsAndAdmissionFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 is Windows-only.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create();
        var budget = new SilkGpuBufferBudget(17 * 4096);
        budget.ConfigureDevice(device);
        var buffers = new List<ISilkGraphicsBuffer>();
        try
        {
            for (int index = 0; index < 17; index++)
            {
                ISilkGraphicsBuffer buffer = device.CreateBuffer(4096, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
                buffers.Add(buffer);
                buffer.Write([(byte)(index + 1)], 4095);
            }
            await Assert.That(device.UniformPageCountForTesting).IsEqualTo(2);
            await Assert.That(() => CreateUniform(device, 1)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(device.UniformPageCountForTesting).IsEqualTo(2);
            for (int index = 0; index < buffers.Count; index++)
            {
                byte[] bytes = ReadUniform(buffers[index]);
                await Assert.That(bytes[^1]).IsEqualTo((byte)(index + 1));
                await Assert.That(bytes.Take(bytes.Length - 1).All(static value => value == 0)).IsTrue();
            }
        }
        finally
        {
            foreach (ISilkGraphicsBuffer buffer in buffers)
            { buffer.Dispose(); }
        }
        await Assert.That(device.UniformPageCountForTesting).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task ConcurrentUniformWritersOwnDisjointRanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 is Windows-only.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create();
        var buffers = new ISilkGraphicsBuffer?[128];
        try
        {
            Parallel.For(0, buffers.Length, index =>
            {
                ISilkGraphicsBuffer buffer = CreateUniform(device, 160);
                buffers[index] = buffer;
                byte[] bytes = new byte[160];
                Array.Fill(bytes, (byte)index);
                buffer.Write(bytes);
            });
            var addresses = new HashSet<ulong>();
            for (int index = 0; index < buffers.Length; index++)
            {
                ISilkGraphicsBuffer buffer = buffers[index]!;
                addresses.Add(Address(buffer));
                await Assert.That(ReadUniform(buffer).All(value => value == (byte)index)).IsTrue();
            }
            await Assert.That(addresses.Count).IsEqualTo(buffers.Length);
            await Assert.That(device.UniformPageCountForTesting).IsEqualTo(1);
        }
        finally
        {
            Parallel.ForEach(buffers, static buffer => buffer?.Dispose());
        }
        await Assert.That(device.UniformPageCountForTesting).IsEqualTo(0);
    }

    [Test]
    public async Task VariableUniformRangesAreAlignedAndLargeBuffersStayDedicated()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 is Windows-only.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create();
        using ISilkGraphicsBuffer small = CreateUniform(device, 1);
        using ISilkGraphicsBuffer medium = CreateUniform(device, 257);
        using ISilkGraphicsBuffer large = CreateUniform(device, 4096);
        using ISilkGraphicsBuffer dedicated = CreateUniform(device, 4097);
        await Assert.That(Resource(medium)).IsEqualTo(Resource(small));
        await Assert.That(Resource(large)).IsEqualTo(Resource(small));
        await Assert.That(Address(medium) - Address(small)).IsEqualTo(256ul);
        await Assert.That(Address(large) - Address(medium)).IsEqualTo(512ul);
        await Assert.That(Resource(dedicated)).IsNotEqualTo(Resource(small));
        medium.Write([91], 256);
        await Assert.That(ReadUniform(medium)[256]).IsEqualTo((byte)91);
        await Assert.That(ReadUniform(large).All(static value => value == 0)).IsTrue();
        await Assert.That(() => medium.Write([1, 2], 256)).Throws<ArgumentException>();
    }

    private static ISilkGraphicsBuffer CreateUniform(D3D12SilkGraphicsDevice device, nuint size) =>
        OperatingSystem.IsWindows()
            ? device.CreateBuffer(size, SilkBufferUsage.Uniform | SilkBufferUsage.Upload)
            : throw new PlatformNotSupportedException();

    [SupportedOSPlatform("windows")]
    private static ulong Address(ISilkGraphicsBuffer buffer) => ((D3D12SilkGraphicsBuffer)buffer).GpuVirtualAddress;

    [SupportedOSPlatform("windows")]
    private static IDisposable Lease(ISilkGraphicsBuffer buffer) => ((D3D12SilkGraphicsBuffer)buffer).AcquireLease();

    [SupportedOSPlatform("windows")]
    private static unsafe byte[] ReadUniform(ISilkGraphicsBuffer buffer)
    {
        var native = (D3D12SilkGraphicsBuffer)buffer;
        var range = new global::Silk.NET.Direct3D12.Range(native.ResourceOffset, native.ResourceOffset + buffer.Size);
        void* mapped;
        Marshal.ThrowExceptionForHR(native.Resource->Map(0, &range, &mapped));
        try
        {
            return new ReadOnlySpan<byte>((byte*)mapped + native.ResourceOffset, checked((int)buffer.Size)).ToArray();
        }
        finally { native.Resource->Unmap(0, null); }
    }

    [SupportedOSPlatform("windows")]
    private static unsafe nint Resource(ISilkGraphicsBuffer buffer) =>
        (nint)((D3D12SilkGraphicsBuffer)buffer).Resource;
}
