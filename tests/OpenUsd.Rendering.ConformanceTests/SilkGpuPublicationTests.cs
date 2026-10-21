// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGpuPublicationTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task OutputResizeAdmitsExactOldAndNewTargetOverlap(SilkGraphicsBackend backend, bool oneByteShort) =>
        DisplayTransformConformance.OutputResizeUsesExactOverlap(backend, oneByteShort);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task CancelledHdrCaptureRollsBackAndDoesNotPoisonOrderedReplay(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.CancelledCapturePreservesFrameAndReplay(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task CompletedOutputPublicationCannotBeUndoneByCleanupFailure(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.CompletedPublicationSurvivesCleanupFailure(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task FinalPublicationRefusalRestoresShadowActivation(SilkGraphicsBackend backend, bool remove) =>
        SilkShadowConformance.FinalPublicationKeepsShadowActivation(backend, remove);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task FinalPublicationRefusalRestoresStagedPhysicsGeometry(SilkGraphicsBackend backend, bool retained) =>
        DisplayTransformConformance.FinalPublicationKeepsPhysicsGeometry(backend, retained);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public Task FinalSubmissionRefusalKeepsThePublicPageUnpublished(
        SilkGraphicsBackend backend, bool capture, bool publish) =>
        DisplayTransformConformance.FinalSubmissionKeepsPageUnpublished(backend, capture, publish);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task ActivatedGpuPoseRollsBackAfterRefusedColorSubmission(SilkGraphicsBackend backend) =>
        SilkDeformationRenderConformance.ActivatedPoseRollback(
            () => SilkDepthCaptureConformance.CreateDevice(backend),
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task RefusedColorSubmissionRestoresPublishedDisplayResources(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.RefusedColorSubmissionKeepsDisplay(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task RefusedColorSubmissionRestoresPublishedSelectionResources(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.RefusedColorSubmissionKeepsSelection(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task RefusedColorSubmissionRestoresInstanceSlotPublication(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.RefusedColorSubmissionKeepsInstances(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task RefusedColorSubmissionRestoresMeshAndFrameUniformPublication(
        SilkGraphicsBackend backend, bool lazyCapacity) =>
        DisplayTransformConformance.RefusedColorSubmissionKeepsUniforms(backend, lazyCapacity);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task SelectionPageRollbackDistinguishesRefusalFromDeviceLoss(SilkGraphicsBackend backend, bool deviceLoss) =>
        DisplayTransformConformance.SelectionPageRollbackRespectsGeneration(backend, deviceLoss);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, SelectionElementKind.None)]
    [Arguments(SilkGraphicsBackend.Vulkan, SelectionElementKind.None)]
    [Arguments(SilkGraphicsBackend.D3D12, SelectionElementKind.Face)]
    [Arguments(SilkGraphicsBackend.Vulkan, SelectionElementKind.Face)]
    [Arguments(SilkGraphicsBackend.D3D12, SelectionElementKind.Edge)]
    [Arguments(SilkGraphicsBackend.Vulkan, SelectionElementKind.Edge)]
    [Arguments(SilkGraphicsBackend.D3D12, SelectionElementKind.Point)]
    [Arguments(SilkGraphicsBackend.Vulkan, SelectionElementKind.Point)]
    public Task SelectionMaskVariantsArePreparedBeforePagePublication(
        SilkGraphicsBackend backend, SelectionElementKind kind) =>
        DisplayTransformConformance.SelectionMaskPageKeepsPriorScene(backend, kind);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task CaptureReplayDoesNotAdoptTheFollowingPagesOverlay(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.CaptureReplayKeepsSelectionOmitted(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task TargetlessFrameReplayAdmitsExactOutputOwnership(SilkGraphicsBackend backend, bool oneByteShort) =>
        DisplayTransformConformance.SelectionReplayAdmitsTemporaryDepth(backend, oneByteShort);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task SelectionOverlayRefusalPrecedesPagePublication(SilkGraphicsBackend backend, bool targetlessReplay) =>
        DisplayTransformConformance.SelectionOverlayPageKeepsPriorScene(backend, targetlessReplay);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task ComponentSelectionCapacityRefusalPrecedesPagePublication(
        SilkGraphicsBackend backend, bool latePipeline) =>
        DisplayTransformConformance.ComponentSelectionPageKeepsPriorScene(backend, latePipeline);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task ScopedSelectionRefusalPreservesThePreviousIndexOwnership(
        SilkGraphicsBackend backend, bool multipleFaces) =>
        DisplayTransformConformance.ScopedSelectionRefusalKeepsPublishedIndices(backend, multipleFaces);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "binding")]
    [Arguments(SilkGraphicsBackend.Vulkan, "binding")]
    [Arguments(SilkGraphicsBackend.D3D12, "format")]
    [Arguments(SilkGraphicsBackend.Vulkan, "format")]
    [Arguments(SilkGraphicsBackend.D3D12, "quota")]
    [Arguments(SilkGraphicsBackend.Vulkan, "quota")]
    public Task SelectionPreparationRefusalKeepsPublishedOverlay(SilkGraphicsBackend backend, string failure) =>
        DisplayTransformConformance.SelectionRefusalKeepsPublishedResources(backend, failure);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task ColorPipelineRefusalPrecedesScenePublication(SilkGraphicsBackend backend) =>
        DisplayTransformConformance.ColorPipelineRefusalKeepsPublishedScene(backend);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    public Task DisplayAndScenePublicationRefuseTogether(
        SilkGraphicsBackend backend, bool lateMesh, bool capture) =>
        DisplayTransformConformance.DisplayAndSceneRefusalIsAtomic(backend, lateMesh, capture);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task PageShadowUsesTheUnpublishedDeformationVertices(SilkGraphicsBackend backend) =>
        SilkDeformationRenderConformance.PageShadowUsesPendingVertices(
            () => SilkDepthCaptureConformance.CreateDevice(backend),
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "fresh")]
    [Arguments(SilkGraphicsBackend.Vulkan, "fresh")]
    [Arguments(SilkGraphicsBackend.D3D12, "reused")]
    [Arguments(SilkGraphicsBackend.Vulkan, "reused")]
    [Arguments(SilkGraphicsBackend.D3D12, "second-light")]
    [Arguments(SilkGraphicsBackend.Vulkan, "second-light")]
    [Arguments(SilkGraphicsBackend.D3D12, "pipeline")]
    [Arguments(SilkGraphicsBackend.Vulkan, "pipeline")]
    public Task ShadowSubmissionRefusalKeepsPublishedSceneAndPixels(SilkGraphicsBackend backend, string stage) =>
        SilkShadowConformance.PageSubmissionRefusalKeepsPublishedShadows(backend, stage);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "binding")]
    [Arguments(SilkGraphicsBackend.Vulkan, "binding")]
    [Arguments(SilkGraphicsBackend.D3D12, "upload")]
    [Arguments(SilkGraphicsBackend.Vulkan, "upload")]
    [Arguments(SilkGraphicsBackend.D3D12, "parameters")]
    [Arguments(SilkGraphicsBackend.Vulkan, "parameters")]
    [Arguments(SilkGraphicsBackend.D3D12, "format")]
    [Arguments(SilkGraphicsBackend.Vulkan, "format")]
    [Arguments(SilkGraphicsBackend.D3D12, "quota")]
    [Arguments(SilkGraphicsBackend.Vulkan, "quota")]
    public Task DisplayPreparationRefusalPreservesPublishedResourcesAndPixels(
        SilkGraphicsBackend backend, string failure) =>
        DisplayTransformConformance.AdmissionRefusalKeepsPublishedTransform(backend, failure);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public Task DeformationDispatchRefusalPrecedesPagePublicationAndPreservesPixels(
        SilkGraphicsBackend backend, bool lateCapacity) =>
        SilkDeformationRenderConformance.DispatchRefusalKeepsPublishedPose(
            () => SilkDepthCaptureConformance.CreateDevice(backend),
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            lateCapacity);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task LateGpuPosePreparationRefusalPreservesPixelsAndAmortizedBufferReuse(SilkGraphicsBackend backend) =>
        SilkDeformationRenderConformance.LatePageRefusalPreservesThePublishedPose(
            () => SilkDepthCaptureConformance.CreateDevice(backend),
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV);
}
