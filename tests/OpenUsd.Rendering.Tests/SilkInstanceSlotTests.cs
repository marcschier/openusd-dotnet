// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
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
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ActivatedInstanceCapacityCanRollBackAfterFrameWrites(bool existing, bool commit)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, 2, x: 2)], 2, 1));
        SilkMeshGpuResource[] meshes = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = meshes[0].Geometry;
        ISilkGraphicsBuffer? oldColor = null;
        ISilkGraphicsBuffer? oldShadow = null;
        if (existing)
        {
            oldColor = UpdateInstances(device, scene, geometry, meshes[..1], false, 0);
            oldShadow = UpdateInstances(device, scene, geometry, meshes[..1], true, 0);
        }
        ulong reserved = budget.Usage.ReservedBytes;
        using (var capacity = new SilkMeshGpuGeometryResource.PreparedInstanceCapacity(geometry))
        {
            capacity.Prepare(device, [2, 2], 2);
            capacity.Activate();
            await Assert.That(geometry.InstanceSlotCount).IsEqualTo(2);
            await Assert.That(geometry.RequireInstanceBuffer(0).Size)
                .IsEqualTo((nuint)(2 * SilkSceneUniformWriter.ByteSize));
            using (SilkSceneGpuResources.PreparedFrameUniforms writes = resources.PrepareFrameUniforms()!)
            {
                writes.TrackInstances(geometry);
                geometry.UpdateInstanceBuffer(device, scene.Frame, meshes, false, 0);
                geometry.UpdateInstanceBuffer(device, scene.Frame, meshes, false, 1);
                _ = capacity.WriteShadow([new(meshes[0], Matrix4x4.Identity), new(meshes[1], Matrix4x4.Identity)]);
                if (commit)
                {
                    writes.Commit();
                }
            }
            if (commit)
            {
                capacity.Commit();
            }
        }
        if (commit)
        {
            await Assert.That(geometry.InstanceSlotCount).IsEqualTo(2);
            await Assert.That(geometry.ShadowInstanceBuffer!.Size)
                .IsEqualTo((nuint)(2 * SilkSceneUniformWriter.ByteSize));
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved +
                ((existing ? 10ul : 12ul) * SilkSceneUniformWriter.ByteSize));
        }
        else
        {
            await Assert.That(geometry.InstanceSlotCount).IsEqualTo(existing ? 1 : 0);
            await Assert.That(geometry.ShadowInstanceBuffer).IsSameReferenceAs(oldShadow);
            if (existing)
            {
                await Assert.That(geometry.RequireInstanceBuffer(0)).IsSameReferenceAs(oldColor);
            }
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        }
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActivatedFirstPageReleasesOrPublishesItsFrameAndMeshCapacity(bool commit)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        using var page = new OpenUsdSilkPage(27, 1, SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1);
        using (SilkSceneState.PreparedPage cpu = scene.PreparePage(page))
        using (SilkSceneGpuResources.PreparedMeshUpdate gpu = resources.Prepare(
            scene, cpu.Delta, prepareUniformCapacity: true)!)
        {
            gpu.Activate();
            using (SilkSceneGpuResources.PreparedFrameUniforms uniforms = resources.PrepareFrameUniforms()!)
            {
                _ = resources.UpdateUniforms(scene.Frame);
                _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1);
                if (commit)
                {
                    uniforms.Commit();
                }
            }
            if (commit)
            {
                gpu.Commit();
                cpu.Commit();
            }
        }
        await Assert.That(resources.Revision).IsEqualTo(commit ? 1ul : 0ul);
        await Assert.That(scene.Revision).IsEqualTo(commit ? 1ul : 0ul);
        await Assert.That(resources.MeshValues.Count).IsEqualTo(commit ? 1 : 0);
        if (commit)
        {
            await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(0);
            await Assert.That(resources.MeshValues.Single().HasUniformSpare).IsTrue();
        }
        else
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
            await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
        }
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActivatedGpuPageRestoresMembershipMetadataAndSurfaceCapacityOnRollback(bool replaceGeometry)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, 2)], 2, 1));
        _ = resources.UpdateUniforms(scene.Frame);
        ISilkGraphicsBuffer frame = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 0);
        SilkMeshGpuResource[] original = [.. resources.MeshValues];
        SilkMeshData[] metadata = [.. original.Select(mesh => mesh.Mesh)];
        ulong revision = resources.Revision;
        ulong reserved = budget.Usage.ReservedBytes;
        byte[] changed = SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 4);
        if (replaceGeometry)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                changed.AsSpan(268 + System.Text.Encoding.UTF8.GetByteCount(FirstPath)), 0.25f);
        }
        using var page = new OpenUsdSilkPage(27, 2,
            [.. changed, .. SilkTransactionalApplyTests.Removal(SecondPath),
                .. SilkTransactionalApplyTests.Mesh("/World/Geom/Third", 3)], 3);
        bool removedWasAbsent;
        double activeTransform;
        using (SilkSceneState.PreparedPage cpu = scene.PreparePage(page))
        using (SilkSceneGpuResources.PreparedMeshUpdate gpu = resources.Prepare(
            scene, cpu.Delta, prepareUniformCapacity: true)!)
        {
            gpu.Activate();
            removedWasAbsent = !resources.MeshValues.Any(mesh => mesh.Mesh.Path == SecondPath);
            activeTransform = resources.MeshValues.Single(mesh => mesh.Mesh.Path == FirstPath).Mesh.Transform.Span[12];
            using (resources.PrepareFrameUniforms())
            {
                _ = resources.UpdateUniforms(scene.Frame);
                _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1);
            }
        }
        await Assert.That(removedWasAbsent).IsTrue();
        await Assert.That(activeTransform).IsEqualTo(4.0);
        await Assert.That(resources.Revision).IsEqualTo(revision);
        await Assert.That(scene.Revision).IsEqualTo(1ul);
        await Assert.That(resources.MeshValues.Count).IsEqualTo(2);
        for (int index = 0; index < original.Length; index++)
        {
            await Assert.That(resources.MeshValues.Single(mesh => mesh.Mesh.Path == metadata[index].Path))
                .IsSameReferenceAs(original[index]);
            await Assert.That(original[index].Mesh).IsSameReferenceAs(metadata[index]);
            await Assert.That(original[index].HasUniformSpare).IsFalse();
        }
        await Assert.That(resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 0))
            .IsSameReferenceAs(frame);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, "rollback")]
    [Arguments(true, "rollback")]
    [Arguments(false, "commit")]
    [Arguments(true, "commit")]
    [Arguments(false, "loss")]
    [Arguments(true, "loss")]
    public async Task RepeatedUniformWritesKeepTheCheckpointBuffersUntouched(bool warmSpare, string outcome)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        _ = resources.UpdateUniforms(scene.Frame);
        _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 0);
        if (warmSpare)
        {
            resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 1), 1, 2));
            _ = resources.UpdateUniforms(scene.Frame);
            _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1);
        }
        SilkMeshGpuResource mesh = resources.MeshValues.Single();
        var originalMesh = (FailingBuffer)mesh.UniformBuffer;
        var originalFrame = (FailingBuffer)resources.RequireFrameBuffer(
            scene, RenderOutputTransform.Identity, warmSpare ? 1 : 0);
        byte[] meshBytes = originalMesh.Snapshot();
        byte[] frameBytes = originalFrame.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        using (SilkSceneGpuResources.PreparedFrameUniforms preparation = resources.PrepareFrameUniforms()!)
        {
            for (int write = 0; write < 3; write++)
            {
                resources.Apply(scene, scene.Apply(
                    SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: write + 10), 1, (ulong)(write + 3)));
                await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
                _ = resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, write + 3);
                await Assert.That(originalMesh.Snapshot().SequenceEqual(meshBytes)).IsTrue();
                await Assert.That(originalFrame.Snapshot().SequenceEqual(frameBytes)).IsTrue();
            }
            if (outcome == "commit")
            {
                preparation.Commit();
            }
            else if (outcome == "loss")
            {
                device.DeviceLossGeneration++;
            }
        }
        if (outcome == "rollback")
        {
            await Assert.That(mesh.UniformBuffer).IsSameReferenceAs(originalMesh);
            await Assert.That(resources.RequireFrameBuffer(
                scene, RenderOutputTransform.Identity, warmSpare ? 1 : 0)).IsSameReferenceAs(originalFrame);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        }
        else
        {
            await Assert.That(originalMesh.IsReleased).IsTrue();
            await Assert.That(originalFrame.IsReleased).IsTrue();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(warmSpare ? reserved :
                reserved + SilkSceneUniformWriter.ByteSize + SilkFrameUniformWriter.ByteSize);
        }
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, "rollback")]
    [Arguments(true, "rollback")]
    [Arguments(false, "commit")]
    [Arguments(true, "commit")]
    [Arguments(false, "loss")]
    [Arguments(true, "loss")]
    public async Task FrameInstanceWritesPreservePublishedStorageAcrossRepeatedUpdates(bool grow, string outcome)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, 2, x: 2),
                .. SilkTransactionalApplyTests.Mesh("/World/Geom/Third", 3, x: 4)], 3, 1));
        SilkMeshGpuResource[] meshes = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = meshes[0].Geometry;
        var priorSpare = UpdateInstances(device, scene, geometry, meshes[..2], false, 0);
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 1), 1, 2));
        var original = UpdateInstances(device, scene, geometry, meshes[..2], false, 0);
        byte[] publishedBytes = original.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        var candidates = new List<FailingBuffer>();
        SilkMeshData intermediate = meshes[0].Mesh;
        using (SilkSceneGpuResources.PreparedFrameUniforms preparation = resources.PrepareFrameUniforms()!)
        {
            preparation.TrackInstances(geometry);
            for (int write = 0; write < 3; write++)
            {
                resources.Apply(scene, scene.Apply(
                    SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: write + 2), 1, (ulong)(write + 3)));
                if (write == 1)
                {
                    intermediate = meshes[0].Mesh;
                }
                candidates.Add(UpdateInstances(device, scene, geometry, grow ? meshes : meshes[..2], false, 0));
                await Assert.That(original.IsReleased).IsFalse();
                await Assert.That(priorSpare.IsReleased).IsFalse();
                await Assert.That(original.Snapshot().SequenceEqual(publishedBytes)).IsTrue();
            }
            if (outcome == "commit")
            {
                preparation.Commit();
            }
            else if (outcome == "loss")
            {
                device.DeviceLossGeneration++;
            }
        }
        if (outcome == "rollback")
        {
            await Assert.That(geometry.RequireInstanceBuffer(0)).IsSameReferenceAs(original);
            await Assert.That(original.Snapshot().SequenceEqual(publishedBytes)).IsTrue();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
            foreach (FailingBuffer candidate in candidates)
            {
                await Assert.That(candidate.IsReleased).IsEqualTo(!ReferenceEquals(candidate, priorSpare));
            }
            meshes[0].UpdateMesh(intermediate);
            var retried = UpdateInstances(device, scene, geometry, grow ? meshes : meshes[..2], false, 0);
            await Assert.That(retried.Snapshot().SequenceEqual(publishedBytes)).IsFalse();
        }
        else
        {
            await Assert.That(geometry.RequireInstanceBuffer(0)).IsSameReferenceAs(candidates[^1]);
            await Assert.That(original.IsReleased).IsTrue();
            await Assert.That(priorSpare.IsReleased).IsEqualTo(grow);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(
                grow ? reserved + (4ul * SilkSceneUniformWriter.ByteSize) : reserved);
        }
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FrameInstanceWritesPublishOrRemoveNewBatchSlotsTogether(bool commit)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        SilkMeshGpuResource[] meshes = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = meshes[0].Geometry;
        ulong reserved = budget.Usage.ReservedBytes;
        using (SilkSceneGpuResources.PreparedFrameUniforms preparation = resources.PrepareFrameUniforms()!)
        {
            preparation.TrackInstances(geometry);
            geometry.UpdateInstanceBuffer(device, scene.Frame, meshes, false, 0);
            geometry.UpdateInstanceBuffer(device, scene.Frame, meshes, false, 1);
            await Assert.That(geometry.InstanceSlotCount).IsEqualTo(2);
            if (commit)
            {
                preparation.Commit();
            }
        }
        await Assert.That(geometry.InstanceSlotCount).IsEqualTo(commit ? 2 : 0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(
            commit ? reserved + (2ul * SilkSceneUniformWriter.ByteSize) : reserved);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FrameUniformRollbackReleasesCandidatesButNeverRestoresALostGeneration(bool deviceLoss)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        _ = resources.UpdateUniforms(scene.Frame);
        SilkMeshGpuResource mesh = resources.MeshValues.Single();
        var originalMesh = (FailingBuffer)mesh.UniformBuffer;
        var originalFrame = (FailingBuffer)resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 0);
        byte[] meshBytes = originalMesh.Snapshot();
        byte[] frameBytes = originalFrame.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 4), 1, 2));
        FailingBuffer candidateMesh;
        FailingBuffer candidateFrame;
        using (resources.PrepareFrameUniforms())
        {
            await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
            candidateMesh = (FailingBuffer)mesh.UniformBuffer;
            candidateFrame = (FailingBuffer)resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1);
            if (deviceLoss)
            {
                device.DeviceLossGeneration++;
            }
        }
        await Assert.That(candidateMesh.IsReleased).IsEqualTo(!deviceLoss);
        await Assert.That(candidateFrame.IsReleased).IsEqualTo(!deviceLoss);
        await Assert.That(mesh.UniformBuffer).IsSameReferenceAs(deviceLoss ? candidateMesh : originalMesh);
        await Assert.That(resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, deviceLoss ? 1 : 0))
            .IsSameReferenceAs(deviceLoss ? candidateFrame : originalFrame);
        await Assert.That(originalMesh.Snapshot().SequenceEqual(meshBytes)).IsTrue();
        await Assert.That(originalFrame.Snapshot().SequenceEqual(frameBytes)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(deviceLoss
            ? reserved + SilkSceneUniformWriter.ByteSize + SilkFrameUniformWriter.ByteSize : reserved);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task AdmittedPageReservesUniformSparesBeforeLaterFrameUpdates()
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var first = new OpenUsdSilkPage(27, 1, SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1);
        renderer.ApplyPage(first);
        _ = renderer.GpuResources.UpdateUniforms(renderer.Scene.Frame);
        _ = renderer.GpuResources.RequireFrameBuffer(renderer.Scene, RenderOutputTransform.Identity, 0);
        using var moved = new OpenUsdSilkPage(27, 2, SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 4), 1);
        renderer.ApplyPage(moved);
        ulong reserved = budget.Usage.ReservedBytes;
        using (budget.Reserve(budget.MaximumBytes - reserved))
        {
            await Assert.That(renderer.GpuResources.UpdateUniforms(renderer.Scene.Frame)).IsEqualTo(1);
            _ = renderer.GpuResources.RequireFrameBuffer(renderer.Scene, RenderOutputTransform.Identity, 1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(budget.MaximumBytes);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task ZeroValuedFirstUniformStillInitializesNativeStorage()
    {
        using var device = new FailingBufferDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        byte[] command = SilkTransactionalApplyTests.Mesh(FirstPath, 1);
        command.AsSpan(64, 16 + (16 * sizeof(double))).Clear();
        resources.Apply(scene, scene.Apply(command, 1, 1));
        var buffer = (FailingBuffer)resources.MeshValues.Single().UniformBuffer;
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
        await Assert.That(buffer.WrittenRows).IsEqualTo(1);
        await Assert.That(buffer.Snapshot().All(value => value == 0)).IsTrue();
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmittedFrameUniformRefusalPreservesPublishedBytesAndRevision(bool quota)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(65_536);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        var original = (FailingBuffer)resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 0);
        byte[] before = original.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        IDisposable? pressure = quota
            ? budget.Reserve(budget.MaximumBytes - reserved - SilkFrameUniformWriter.ByteSize + 1) : null;
        device.FailWritesAfterCopy = !quota;
        try
        {
            if (quota)
            {
                await Assert.That(() => resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1))
                    .Throws<SilkGpuBufferBudgetExceededException>();
            }
            else
            {
                await Assert.That(() => resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1))
                    .Throws<InvalidOperationException>();
            }
            await Assert.That(original.Snapshot().SequenceEqual(before)).IsTrue();
            await Assert.That(resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 0))
                .IsSameReferenceAs(original);
        }
        finally
        {
            pressure?.Dispose();
            device.FailWritesAfterCopy = false;
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        var replacement = (FailingBuffer)resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 1);
        await Assert.That(replacement).IsNotSameReferenceAs(original);
        await Assert.That(replacement.Snapshot().SequenceEqual(before)).IsFalse();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(2 * reserved);
        await Assert.That(resources.RequireFrameBuffer(scene, RenderOutputTransform.Identity, 2))
            .IsSameReferenceAs(original);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(2 * reserved);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task MeshUniformRefusalDoesNotAdvanceTheLastUploadedIdentity()
    {
        using var device = new FailingBufferDevice();
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
        var buffer = (FailingBuffer)resources.MeshValues.Single().UniformBuffer;
        byte[] before = buffer.Snapshot();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 4), 1, 2));
        buffer.FailNextWrite = true;
        await Assert.That(() => resources.UpdateUniforms(scene.Frame)).Throws<InvalidOperationException>();
        await Assert.That(buffer.Snapshot().SequenceEqual(before)).IsTrue();
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
        await Assert.That(buffer.Snapshot().SequenceEqual(before)).IsFalse();
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmittedMeshUniformRefusalPreservesPublishedBytesUntilSuccessfulRetry(bool quota)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        _ = resources.UpdateUniforms(scene.Frame);
        SilkMeshGpuResource mesh = resources.MeshValues.Single();
        var original = (FailingBuffer)mesh.UniformBuffer;
        byte[] before = original.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 4), 1, 2));
        IDisposable? pressure = quota
            ? budget.Reserve(budget.MaximumBytes - reserved - SilkSceneUniformWriter.ByteSize + 1) : null;
        device.FailWritesAfterCopy = !quota;
        try
        {
            if (quota)
            {
                await Assert.That(() => resources.UpdateUniforms(scene.Frame))
                    .Throws<SilkGpuBufferBudgetExceededException>();
            }
            else
            {
                await Assert.That(() => resources.UpdateUniforms(scene.Frame)).Throws<InvalidOperationException>();
            }
            await Assert.That(mesh.UniformBuffer).IsSameReferenceAs(original);
            await Assert.That(original.Snapshot().SequenceEqual(before)).IsTrue();
        }
        finally
        {
            pressure?.Dispose();
            device.FailWritesAfterCopy = false;
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
        await Assert.That(mesh.UniformBuffer).IsNotSameReferenceAs(original);
        await Assert.That(original.Snapshot().SequenceEqual(before)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + SilkSceneUniformWriter.ByteSize);
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(0);
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 8), 1, 3));
        await Assert.That(resources.UpdateUniforms(scene.Frame)).IsEqualTo(1);
        await Assert.That(mesh.UniformBuffer).IsSameReferenceAs(original);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + SilkSceneUniformWriter.ByteSize);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task InstanceCapacityRefusalRollsBackThePageBeforePublishingAnotherInstance(int prototypes)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        byte[] initial = prototypes == 1 ? SilkTransactionalApplyTests.Mesh(FirstPath, 1) :
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1), .. SilkTransactionalApplyTests.Mesh(SecondPath, 2)];
        using var first = new OpenUsdSilkPage(27, 1, initial, (uint)prototypes);
        renderer.ApplyPage(first);
        SilkMeshGpuResource[] originals = [.. renderer.GpuResources.MeshValues];
        ulong reserved = budget.Usage.ReservedBytes;
        byte[] instances = Instance(FirstPath, 1);
        if (prototypes == 2)
        {
            instances = [.. instances, .. Instance(SecondPath, 2)];
        }
        using var second = new OpenUsdSilkPage(27, 2, instances, (uint)prototypes);
        ulong required = (ulong)prototypes * 6ul * SilkSceneUniformWriter.ByteSize;
        using (budget.Reserve(budget.MaximumBytes - reserved - required + 1))
        {
            await Assert.That(() => renderer.ApplyPage(second)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
            await Assert.That(renderer.GpuResources.MeshValues.SequenceEqual(originals)).IsTrue();
            foreach (SilkMeshGpuResource original in originals)
            {
                await Assert.That(original.Geometry.InstanceSlotCount).IsEqualTo(0);
            }
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.ApplyPage(second);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(2 * prototypes);
        foreach (SilkMeshGpuResource original in originals)
        {
            await Assert.That(original.Geometry.InstanceSlotCount).IsEqualTo(1);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + required);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task ReplayedCapacityRefusalKeepsTheOriginalBatchingOptions()
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var first = new OpenUsdSilkPage(27, 1, SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1);
        renderer.ApplyPage(first);
        ulong reserved = budget.Usage.ReservedBytes;
        byte[] instance = Instance(FirstPath, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(instance.AsSpan(44), (uint)SilkMeshCullStyle.Front);
        using var second = new OpenUsdSilkPage(27, 2, instance, 1);
        using var third = new OpenUsdSilkPage(27, 3, [], 0);
        SilkMeshRenderOptions merged = SilkMeshRenderOptions.Default with { BackfaceCulling = false };
        using (budget.Reserve(budget.MaximumBytes - reserved - (6ul * SilkSceneUniformWriter.ByteSize) + 1))
        {
            await Assert.That(() => renderer.ApplyPage(second, merged)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(() => renderer.ApplyPage(third)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        }
        renderer.ApplyPage(third);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        await Assert.That(renderer.GpuResources.MeshValues.First().Geometry.InstanceSlotCount).IsEqualTo(1);
    }

    [Test]
    public async Task CulledSingleDrawsDoNotReserveUnusedInstanceSlots()
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var first = new OpenUsdSilkPage(27, 1, SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1);
        renderer.ApplyPage(first);
        ulong reserved = budget.Usage.ReservedBytes;
        byte[] instance = Instance(FirstPath, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(instance.AsSpan(44), (uint)SilkMeshCullStyle.Front);
        using var second = new OpenUsdSilkPage(27, 2, instance, 1);
        renderer.ApplyPage(second);
        await Assert.That(renderer.GpuResources.MeshValues.First().Geometry.InstanceSlotCount).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + (2ul * SilkSceneUniformWriter.ByteSize));
    }

    private static byte[] Instance(string path, int primId)
    {
        byte[] instance = SilkTransactionalApplyTests.Mesh(path, primId, x: 2);
        BinaryPrimitives.WriteInt32LittleEndian(instance.AsSpan(24), 1);
        return instance;
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AdmittedInstanceRefusalPreservesPublishedBytesAndCharge(bool shadow, bool quota)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, 2, x: 2)], 2, 1));
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;
        FailingBuffer original = UpdateInstances(device, scene, geometry, instances, shadow, 0);
        byte[] before = original.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        int live = device.LiveBufferCount;
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 9), 1, 2));
        instances = [.. resources.MeshValues];
        IDisposable? pressure = quota
            ? budget.Reserve(budget.MaximumBytes - reserved - (ulong)original.Size + 1) : null;
        device.FailWritesAfterCopy = !quota;
        try
        {
            if (quota)
            {
                await Assert.That(() => UpdateInstances(device, scene, geometry, instances, shadow, 4))
                    .Throws<SilkGpuBufferBudgetExceededException>();
            }
            else
            {
                await Assert.That(() => UpdateInstances(device, scene, geometry, instances, shadow, 4))
                    .Throws<InvalidOperationException>();
            }
            await Assert.That(original.IsReleased).IsFalse();
            await Assert.That(original.Snapshot().SequenceEqual(before)).IsTrue();
            await Assert.That(shadow ? geometry.ShadowInstanceBuffer : geometry.RequireInstanceBuffer(0))
                .IsSameReferenceAs(original);
            await Assert.That(device.LiveBufferCount).IsEqualTo(live);
        }
        finally
        {
            pressure?.Dispose();
            device.FailWritesAfterCopy = false;
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        FailingBuffer replacement = UpdateInstances(device, scene, geometry, instances, shadow, 4);
        await Assert.That(replacement).IsNotSameReferenceAs(original);
        await Assert.That(replacement.Snapshot().SequenceEqual(before)).IsFalse();
        await Assert.That(original.Snapshot().SequenceEqual(before)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + (ulong)original.Size);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmittedInstanceTablesReuseTwoChargedSlotsAndSkipUnchangedWrites(bool shadow)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, 2, x: 2)], 2, 1));
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;
        FailingBuffer first = UpdateInstances(device, scene, geometry, instances, shadow, 0);
        ulong firstReserved = budget.Usage.ReservedBytes;
        FailingBuffer? second = null;
        for (int index = 1; index <= 4; index++)
        {
            resources.Apply(scene, scene.Apply(
                SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: index), 1, (ulong)index + 1));
            instances = [.. resources.MeshValues];
            FailingBuffer buffer = UpdateInstances(device, scene, geometry, instances, shadow, index);
            second ??= buffer;
            await Assert.That(buffer).IsSameReferenceAs(index % 2 == 0 ? first : second);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(firstReserved + (ulong)first.Size);
            int writes = buffer.WrittenRows;
            await Assert.That(UpdateInstances(device, scene, geometry, instances, shadow, index))
                .IsSameReferenceAs(buffer);
            await Assert.That(buffer.WrittenRows).IsEqualTo(writes);
        }
        await Assert.That(second).IsNotSameReferenceAs(first);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmittedInstanceRetryRewritesEveryRowOfAPartiallyWrittenSpare(bool shadow)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(
            [.. SilkTransactionalApplyTests.Mesh(FirstPath, 1),
                .. SilkTransactionalApplyTests.Mesh(SecondPath, 2, x: 2)], 2, 1));
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;
        FailingBuffer first = UpdateInstances(device, scene, geometry, instances, shadow, 0);
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 3), 1, 2));
        instances = [.. resources.MeshValues];
        FailingBuffer second = UpdateInstances(device, scene, geometry, instances, shadow, 3);
        byte[] published = second.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 5), 1, 3));
        instances = [.. resources.MeshValues];
        device.FailWritesAfterCopy = true;
        await Assert.That(() => UpdateInstances(device, scene, geometry, instances, shadow, 5))
            .Throws<InvalidOperationException>();
        device.FailWritesAfterCopy = false;
        await Assert.That(second.Snapshot().SequenceEqual(published)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        int rows = first.WrittenRows;
        FailingBuffer recovered = UpdateInstances(device, scene, geometry, instances, shadow, 5);
        await Assert.That(recovered).IsSameReferenceAs(first);
        await Assert.That(recovered.WrittenRows - rows).IsEqualTo(instances.Length);
        await Assert.That(recovered.Snapshot().SequenceEqual(published)).IsFalse();
    }

    [Test]
    public async Task AdmittedShadowCountChangesNeverTrustUnwrittenCandidateRows()
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        SilkMeshGpuResource mesh = resources.MeshValues.Single();
        SilkMeshGpuGeometryResource geometry = mesh.Geometry;
        SilkShadowCaster[] first = [new(mesh, Matrix4x4.Identity), new(mesh, Matrix4x4.Identity)];
        _ = geometry.RequireShadowInstanceBuffer(device, first);
        SilkShadowCaster moved = new(mesh, Matrix4x4.CreateTranslation(3, 0, 0));
        var smaller = (FailingBuffer)geometry.RequireShadowInstanceBuffer(device, [moved]);
        var grown = (FailingBuffer)geometry.RequireShadowInstanceBuffer(device, [moved, new(mesh, default)]);
        await Assert.That(grown).IsNotSameReferenceAs(smaller);
        byte[] expected = new byte[2 * SilkSceneUniformWriter.ByteSize];
        SilkShadowInstanceWriter.Write(moved.ObjectToLightClip, expected.AsSpan(0, SilkSceneUniformWriter.ByteSize));
        SilkShadowInstanceWriter.Write(default, expected.AsSpan(SilkSceneUniformWriter.ByteSize));
        await Assert.That(grown.Snapshot().SequenceEqual(expected)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmittedInstanceGrowthKeepsBothOldSlotsUntilTheLargerWriteSucceeds(bool shadow)
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;
        FailingBuffer first = UpdateInstances(device, scene, geometry, instances, shadow, 0);
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 1), 1, 2));
        instances = [.. resources.MeshValues];
        FailingBuffer second = UpdateInstances(device, scene, geometry, instances, shadow, 1);
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(SecondPath, 2, x: 2), 1, 3));
        instances = [.. resources.MeshValues];
        ulong reserved = budget.Usage.ReservedBytes;
        byte[] before = second.Snapshot();
        device.FailWritesAfterCopy = true;
        await Assert.That(() => UpdateInstances(device, scene, geometry, instances, shadow, 2))
            .Throws<InvalidOperationException>();
        device.FailWritesAfterCopy = false;
        await Assert.That(first.IsReleased).IsFalse();
        await Assert.That(second.IsReleased).IsFalse();
        await Assert.That(second.Snapshot().SequenceEqual(before)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        FailingBuffer grown = UpdateInstances(device, scene, geometry, instances, shadow, 2);
        await Assert.That(grown.Size).IsEqualTo(2 * second.Size);
        await Assert.That(first.IsReleased).IsTrue();
        await Assert.That(second.IsReleased).IsFalse();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + (ulong)second.Size);
        resources.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task AdmittedInstancePublicationFailureDoesNotReplaceThePublishedTable()
    {
        using var device = new FailingBufferDevice();
        var budget = new SilkGpuBufferBudget(1_048_576);
        budget.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device);
        var scene = new SilkSceneState();
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1), 1, 1));
        SilkMeshGpuResource[] instances = [.. resources.MeshValues];
        SilkMeshGpuGeometryResource geometry = instances[0].Geometry;
        FailingBuffer first = UpdateInstances(device, scene, geometry, instances, false, 0);
        byte[] before = first.Snapshot();
        ulong reserved = budget.Usage.ReservedBytes;
        resources.Apply(scene, scene.Apply(SilkTransactionalApplyTests.Mesh(FirstPath, 1, x: 1), 1, 2));
        instances = [.. resources.MeshValues];
        geometry.FailNextInstanceSlotPublicationForTesting(
            () => new InvalidOperationException("Injected candidate publication failure."));
        await Assert.That(() => UpdateInstances(device, scene, geometry, instances, false, 1))
            .Throws<InvalidOperationException>();
        await Assert.That(geometry.RequireInstanceBuffer(0)).IsSameReferenceAs(first);
        await Assert.That(first.Snapshot().SequenceEqual(before)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(UpdateInstances(device, scene, geometry, instances, false, 1))
            .IsNotSameReferenceAs(first);
    }

    private static FailingBuffer UpdateInstances(
        FailingBufferDevice device, SilkSceneState scene, SilkMeshGpuGeometryResource geometry,
        SilkMeshGpuResource[] instances, bool shadow, float offset)
    {
        if (shadow)
        {
            SilkShadowCaster[] casters = [.. instances.Select((mesh, index) =>
                new SilkShadowCaster(mesh, Matrix4x4.CreateTranslation(offset + index, 0, 0)))];
            return (FailingBuffer)geometry.RequireShadowInstanceBuffer(device, casters);
        }
        geometry.UpdateInstanceBuffer(device, scene.Frame, instances, flipClipSpaceY: false);
        return (FailingBuffer)geometry.RequireInstanceBuffer(0);
    }

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

    private sealed class FailingBufferDevice
        : SilkGraphicsDeviceLifetimeBase, ISilkGraphicsDevice, ISilkDeviceLossGraphicsDevice
    {
        private readonly List<FailingBuffer> _buffers = [];
        private readonly SilkShadowAtlasRetentionTests.ShadowDevice _auxiliary = new(SilkGraphicsBackend.Vulkan);

        internal bool FailNextCreate { get; set; }
        internal bool FailWritesAfterCopy { get; set; }

        public ulong DeviceLossGeneration { get; set; }

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
            IDisposable? reservation = ReserveBufferAllocation(size);
            try
            {
                var buffer = new FailingBuffer(size, usage, this);
                buffer.OwnBufferReservation(reservation);
                _buffers.Add(buffer);
                return buffer;
            }
            catch
            {
                reservation?.Dispose();
                throw;
            }
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            _auxiliary.CreateTexture2D(width, height, format);

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            _auxiliary.CreateTexture2D(descriptor);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            _auxiliary.CreateSampler(descriptor);

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
            _auxiliary.CreateCommandList();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            _auxiliary.Submit(commandList);

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
            _auxiliary.Dispose();
        }
    }

    private sealed class FailingBuffer(nuint size, SilkBufferUsage usage, FailingBufferDevice device)
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
            if (device.FailWritesAfterCopy)
            {
                data[..(data.Length / 2)].CopyTo(_data.AsSpan(checked((int)offset)));
                throw new InvalidOperationException("The injected write failed after changing native bytes.");
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
