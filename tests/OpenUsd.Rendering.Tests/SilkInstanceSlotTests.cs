// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins that a per-batch instance transform table is replaced atomically: a
/// refused allocation or a failed write leaves the slot exactly as it was, and
/// the retry uploads everything the device does not hold.
/// </summary>
/// <remarks>
/// The slot caches the byte image the device is known to hold and uploads only
/// the rows that differ from it, so a partially applied update is not a lost
/// frame -- it is a permanently wrong one. If the retained image is advanced for
/// a row whose write failed, every later frame compares equal against it and
/// skips the upload forever, and the instance is drawn from whatever the buffer
/// held before. The same is true of a refused allocation: the slot used to keep
/// the new capacity with no buffer behind it, and never allocated again.
/// </remarks>
public sealed class SilkInstanceSlotTests
{
    private const string FirstPath = "/World/Geom/First";
    private const string SecondPath = "/World/Geom/Second";

    [Test]
    public async Task ARefusedInstanceBufferLeavesNoSlotAndTheRetryAllocates()
    {
        using var device = new FailingBufferDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        SilkSceneDelta delta = scene.Apply(
            [
                .. SilkTransactionalApplyTests.Mesh(FirstPath, primId: 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, primId: 2, x: 2),
            ],
            2,
            1);
        resources.Apply(scene, delta);
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;

        device.FailNextCreate = true;
        await Assert.That(() => geometry.UpdateInstanceBuffer(
                device,
                scene.Frame,
                instances,
                flipClipSpaceY: false))
            .Throws<InvalidOperationException>();

        await Assert.That(geometry.InstanceSlotCount)
            .IsEqualTo(0)
            .Because(
                "A slot may not exist until its buffer does: one that claims a " +
                "capacity with no buffer behind it never allocates again.");
        await Assert.That(() => geometry.RequireInstanceBuffer(0))
            .Throws<InvalidOperationException>();

        // The retry, with nothing about the scene changed.
        geometry.UpdateInstanceBuffer(device, scene.Frame, instances, flipClipSpaceY: false);
        await Assert.That(geometry.InstanceSlotCount).IsEqualTo(1);

        var buffer = (FailingBuffer)geometry.RequireInstanceBuffer(0);
        await Assert.That(buffer.WrittenRows)
            .IsEqualTo(instances.Length)
            .Because("Every row must reach a buffer the device has never held.");
    }

    [Test]
    public async Task AFailedInstanceWriteIsRecordedAgainByTheNextUpdate()
    {
        using var device = new FailingBufferDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        SilkSceneDelta delta = scene.Apply(
            [
                .. SilkTransactionalApplyTests.Mesh(FirstPath, primId: 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, primId: 2, x: 2),
            ],
            2,
            1);
        resources.Apply(scene, delta);
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;

        geometry.UpdateInstanceBuffer(device, scene.Frame, instances, flipClipSpaceY: false);
        var buffer = (FailingBuffer)geometry.RequireInstanceBuffer(0);
        byte[] settled = buffer.Snapshot();

        // Move one instance, so exactly one row of the table has to change.
        SilkSceneDelta moved = scene.Apply(
            SilkTransactionalApplyTests.Mesh(FirstPath, primId: 1, topologyRevision: 1, x: 9),
            1,
            2);
        resources.Apply(scene, moved);
        SilkMeshGpuResource[] updated = [.. resources.MeshValues];

        buffer.FailNextWrite = true;
        await Assert.That(() => geometry.UpdateInstanceBuffer(
                device,
                scene.Frame,
                updated,
                flipClipSpaceY: false))
            .Throws<InvalidOperationException>();

        await Assert.That(buffer.Snapshot().AsSpan().SequenceEqual(settled))
            .IsTrue()
            .Because("A write that failed cannot have changed the device image.");

        // The retry has to re-record the row the failed write never delivered.
        geometry.UpdateInstanceBuffer(device, scene.Frame, updated, flipClipSpaceY: false);

        await Assert.That(buffer.Snapshot().AsSpan().SequenceEqual(settled))
            .IsFalse()
            .Because(
                "The retry must upload the row the failed write never delivered; " +
                "a retained image advanced past a failed write would compare " +
                "equal forever and the instance would keep its old transform.");
    }

    [Test]
    public async Task AGrownTableUploadsEveryRowRatherThanOnlyTheChangedOnes()
    {
        // A reallocated buffer holds nothing, so no row may be skipped as
        // already resident whatever the retained image says.
        using var device = new FailingBufferDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        SilkSceneDelta delta = scene.Apply(
            SilkTransactionalApplyTests.Mesh(FirstPath, primId: 1),
            1,
            1);
        resources.Apply(scene, delta);
        SilkMeshGpuResource[] one = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = one[0].Geometry;
        geometry.UpdateInstanceBuffer(device, scene.Frame, one, flipClipSpaceY: false);

        SilkSceneDelta grown = scene.Apply(
            SilkTransactionalApplyTests.Mesh(SecondPath, primId: 2, x: 2),
            1,
            2);
        resources.Apply(scene, grown);
        SilkMeshGpuResource[] two = [.. resources.MeshValues];
        geometry.UpdateInstanceBuffer(device, scene.Frame, two, flipClipSpaceY: false);

        var buffer = (FailingBuffer)geometry.RequireInstanceBuffer(0);
        await Assert.That(buffer.WrittenRows)
            .IsGreaterThanOrEqualTo(2)
            .Because("A fresh allocation must receive every row it is drawn from.");
    }

    [Test]
    public async Task AFailedSlotPublicationDisposesTheBufferItHadAlreadyCreated()
    {
        // The window between the buffer existing and anything referencing it is
        // where the slot object and the room for it in the list are obtained, and
        // both of those allocate. A failure there used to leave a device
        // allocation nothing would ever dispose, and no slot to find it through.
        using var device = new FailingBufferDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        SilkSceneDelta delta = scene.Apply(
            SilkTransactionalApplyTests.Mesh(FirstPath, primId: 1),
            1,
            1);
        resources.Apply(scene, delta);
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;

        int before = device.LiveBufferCount;
        geometry.FailNextInstanceSlotPublicationForTesting(
            static () => new InvalidOperationException("The injected publication failed."));
        await Assert.That(() => geometry.UpdateInstanceBuffer(
                device,
                scene.Frame,
                instances,
                flipClipSpaceY: false))
            .Throws<InvalidOperationException>();

        await Assert.That(geometry.InstanceSlotCount)
            .IsEqualTo(0)
            .Because("A publication that failed may not leave a slot behind.");
        await Assert.That(device.LiveBufferCount)
            .IsEqualTo(before)
            .Because(
                "The buffer created before the failed publication must be " +
                "disposed: nothing else can ever reach it.");

        // And the retry allocates again and publishes.
        geometry.UpdateInstanceBuffer(device, scene.Frame, instances, flipClipSpaceY: false);
        await Assert.That(geometry.InstanceSlotCount).IsEqualTo(1);
        await Assert.That(device.LiveBufferCount).IsEqualTo(before + 1);
    }

    private sealed class FailingBufferDevice : ISilkGraphicsDevice
    {
        private readonly List<FailingBuffer> _buffers = [];

        internal bool FailNextCreate { get; set; }

        internal int LiveBufferCount => _buffers.Count(buffer => !buffer.IsReleased);

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Test", "1", SupportsCompute: false, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new InvalidOperationException("The injected allocation failed.");
            }
            var buffer = new FailingBuffer(size, usage);
            _buffers.Add(buffer);
            return buffer;
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

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

    private sealed class FailingBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        private readonly byte[] _data = new byte[checked((int)size)];

        internal bool FailNextWrite { get; set; }

        internal int WrittenRows { get; private set; }

        internal bool IsReleased { get; private set; }

        internal byte[] Snapshot() => [.. _data];

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("The injected write failed.");
            }
            data.CopyTo(_data.AsSpan(checked((int)offset)));
            WrittenRows += data.Length / SilkSceneUniformWriter.ByteSize;
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            _data.CopyTo(destination);
        }

        protected override void ReleaseNative() => IsReleased = true;
    }
}
