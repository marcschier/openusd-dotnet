// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerSilkDeformationUploadTests
{
    [Test]
    public async Task TheFirstPresentedSceneResolvesAndUploadsItsExactPhysicsPose()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(64, 64));
        var presentation = new D3D12StagePresentationRenderer(
            new PhysicsPageSession(() => CreateMeshPage(1)), renderer, StageRenderState.Default);
        ViewerPhysicsOverrideStage stage = presentation.PhysicsStage!;
        StageRigidPose(stage, revision: 17);

        _ = presentation.Render(color, depth, CancellationToken.None);

        await Assert.That(stage.TryTakeReport(out ViewerPhysicsOverrideReport report)).IsTrue();
        await Assert.That(report.Revision).IsEqualTo(17ul);
        await Assert.That(report.Applied).IsEqualTo(1);
        await Assert.That(report.Unresolved).IsEqualTo(0);
        SilkMeshGpuResource mesh = renderer.GpuResources.Meshes[MeshId(renderer)];
        float[] uniforms = MemoryMarshal.Cast<byte, float>(device.Track(mesh.UniformBuffer).Data).ToArray();
        // Column-major shader constants include [-1,1] to [0,1] depth; this device does not flip Y.
        await Assert.That(uniforms[3]).IsEqualTo(2f);
        await Assert.That(uniforms[7]).IsEqualTo(3f);
        await Assert.That(uniforms[11]).IsEqualTo(2.5f);
    }

    [Test]
    public async Task AnAuthoredScaleInTheCurrentPageReachesThePhysicsDraw()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(64, 64));
        int frames = 0;
        var presentation = new D3D12StagePresentationRenderer(
            new PhysicsPageSession(() => ++frames == 1 ? CreateMeshPage(1) : CreateScaledMeshPage()),
            renderer, StageRenderState.Default);
        _ = presentation.Render(color, depth, CancellationToken.None);
        StageRigidPose(presentation.PhysicsStage!, revision: 19);

        _ = presentation.Render(color, depth, CancellationToken.None);

        SilkMeshGpuResource mesh = renderer.GpuResources.Meshes[MeshId(renderer)];
        float[] uniforms = MemoryMarshal.Cast<byte, float>(device.Track(mesh.UniformBuffer).Data).ToArray();
        await Assert.That(uniforms[0]).IsEqualTo(2f);
        await Assert.That(uniforms[5]).IsEqualTo(3f);
        await Assert.That(uniforms[10]).IsEqualTo(2f);
        await Assert.That(uniforms[3]).IsEqualTo(2f);
        await Assert.That(uniforms[7]).IsEqualTo(3f);
        await Assert.That(uniforms[11]).IsEqualTo(2.5f);
    }

    [Test]
    public async Task AFailedPresentationDoesNotAcknowledgeItsPhysicsBatchAndCanRetry()
    {
        using var device = new RecordingSilkDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(64, 64));
        var presentation = new D3D12StagePresentationRenderer(
            new PhysicsPageSession(() => CreateMeshPage(1)), renderer, StageRenderState.Default);
        ViewerPhysicsOverrideStage stage = presentation.PhysicsStage!;
        StageRigidPose(stage, revision: 23);
        bool reportedBeforeSubmission = false;
        device.BeforeSubmit = () => reportedBeforeSubmission |= stage.TryTakeReport(out _);
        device.FailSubmission = true;

        await Assert.That(() => presentation.Render(color, depth, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(reportedBeforeSubmission).IsFalse();
        await Assert.That(stage.TryTakeReport(out _)).IsFalse();

        device.FailSubmission = false;
        StageRigidPose(stage, revision: 23);
        _ = presentation.Render(color, depth, CancellationToken.None);
        await Assert.That(reportedBeforeSubmission).IsFalse();
        await Assert.That(stage.TryTakeReport(out ViewerPhysicsOverrideReport report)).IsTrue();
        await Assert.That(report.Revision).IsEqualTo(23ul);
        await Assert.That(report.Applied).IsEqualTo(1);
        await Assert.That(report.Unresolved).IsEqualTo(0);
    }

    private static OpenUsdSilkPage CreateScaledMeshPage()
    {
        byte[] mesh = CreateMeshCommand(MeshPath, 7, AuthoredPoints, normals: null);
        BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(80), 2);
        BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(120), 3);
        BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(160), 4);
        return CreatePage(2, Concat(CreateFrameCommand(), mesh), commandCount: 2);
    }

    private static void StageRigidPose(ViewerPhysicsOverrideStage stage, ulong revision)
    {
        var identity = new PhysicsRenderObjectId(0xB0D1, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        if (!bindings.TryBind(identity, MeshPath))
        {
            throw new InvalidOperationException("The test's sole rigid binding was refused.");
        }
        var view = new PhysicsRenderOverrideView(
            new PhysicsRenderTransformOverride[]
            {
                new(identity, new UsdVec3d(2, 3, 4), PhysicsRenderOrientation.Identity, Snapped: true)
            }, revision);
        if (stage.Stage(view, bindings) != 1)
        {
            throw new InvalidOperationException("The test's sole rigid pose was refused.");
        }
    }

    private sealed class PhysicsPageSession(Func<OpenUsdSilkPage> create) : IViewerSilkSessionAdapter
    {
        public OpenUsdSilkPage Sync(int width, int height, ViewerFrameRequest request) => create();
    }
}
