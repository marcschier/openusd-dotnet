// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VolumeCameraConformanceTests
{
    private const uint Width = 96;
    private const uint Height = 96;
    private static readonly AsyncLocal<SilkGraphicsBackend?> GateBackend = new();

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task CameraIntegrationBackendGate(SilkGraphicsBackend backend)
    {
        bool intended = OperatingSystem.IsWindows()
            ? backend is SilkGraphicsBackend.D3D12 or SilkGraphicsBackend.Vulkan
            : OperatingSystem.IsLinux() ? backend == SilkGraphicsBackend.Vulkan
            : OperatingSystem.IsMacOS() && backend == SilkGraphicsBackend.Metal;
        if (!intended)
        {
            Skip.Test($"{backend} is not a camera integration target on this operating system.");
            return;
        }
        string platform = OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsLinux() ? "linux" : "macos";
        string root = Path.Combine(EvidenceDirectory, $"camera-{platform}-{backend}");
        Directory.CreateDirectory(root);
        string receiptPath = Path.Combine(root, "receipt.json");
        var cases = new List<string>();
        string deviceName = string.Empty;
        bool software = false;
        void WriteReceipt(string status) => File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            platform,
            backend = backend.ToString(),
            status,
            deviceName,
            software,
            cases,
        }));
        WriteReceipt("started");
        SilkGraphicsBackend? previousBackend = GateBackend.Value;
        GateBackend.Value = backend;
        try
        {
            using (ISilkGraphicsDevice device = CreateVulkanDevice())
            {
                deviceName = device.Capabilities.DeviceName;
                software = device.Capabilities.IsSoftware;
                if (OperatingSystem.IsWindows() && backend == SilkGraphicsBackend.Vulkan &&
                    (!software || !deviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Camera gate requires SwiftShader, selected '{deviceName}'.");
                }
            }
            async Task Run(string id, Func<Task> test)
            {
                await test();
                cases.Add(id);
                WriteReceipt("started");
            }
            await Run("analytic-slab-511-side", () => WideShallowNonuniformGridMatchesAnAnalyticSlabOnVulkan(511, 90));
            await Run("analytic-slab-512-side", () => WideShallowNonuniformGridMatchesAnAnalyticSlabOnVulkan(512, 90));
            await Run("analytic-slab-512-oblique", () => WideShallowNonuniformGridMatchesAnAnalyticSlabOnVulkan(512, 45));
            await Run("large-scale-inside", () => LargeScaleInsideRayMatchesFieldLocalAnalyticReference(1e9, false));
            await Run("large-scale-threshold", () => LargeScaleInsideRayMatchesFieldLocalAnalyticReference(1e6, false));
            await Run("parallel-outside", () => LargeScaleInsideRayMatchesFieldLocalAnalyticReference(1e9, true));
            await Run("camera-inside", CameraInsideVolumeIntegratesOnlyTheVisibleRaySegmentOnVulkan);
            await Run("camera-perspective", PerspectiveCameraUsesOffAxisRayForVolumeIntersectionOnVulkan);
            await Run("camera-near", NearPlaneClippedVolumeEntryStillRendersOnlyTheVisibleSegmentOnVulkan);
            await Run("camera-far", FarPlaneClipsTheVolumeIntegrationSegmentOnVulkan);
            await Run("camera-enclosing-clip", VolumeEnclosingNearAndFarPlanesMatchesFieldLocalReferenceOnVulkan);
            await Run("scaled-field-local", ScaledNonUniformVolumeMatchesTheFieldLocalDistanceReferenceOnVulkan);
            await Run("same-tint-order", OverlappingEqualTintVolumesCompositeIndependentlyOfDepthOrderOnVulkan);
            await Run("opaque-update-reuse", OpaqueDepthTerminatesVolumeAndSnapshotTracksUpdateAndRestoreOnVulkan);
            await Run("transform-refusal-retry", SingularVolumeTransformIsRefusedAndDisposedRendererRejectsUseOnVulkan);
            await Run("material-disabled-single", MaterialDisabledSingleVolumeMatchesOrdinaryProxyOnVulkan);
            await Run("camera-far-origin", () => EyeSpaceRayMatchesIndependentLocalChord(1e12, 1, 1, false, false));
            await Run("camera-nonrigid-view", () => EyeSpaceRayMatchesIndependentLocalChord(1e9, 2, 2, false, false));
            await Run("camera-micro-outside", () => EyeSpaceRayMatchesIndependentLocalChord(0, 1e-6, 1, false, false));
            await Run("camera-micro-inside", () => EyeSpaceRayMatchesIndependentLocalChord(0, 1e-6, 1, true, false));
            await Run("camera-micro-clipped", () => EyeSpaceRayMatchesIndependentLocalChord(0, 1e-6, 1, false, true));
            await Run("perspective-near-small", () => PerspectiveRangeAndAsymmetryMatchAnalyticOffAxisChord(0.01, 1000, 0.25));
            await Run("perspective-near-one", () => PerspectiveRangeAndAsymmetryMatchAnalyticOffAxisChord(1, 11, 0));
            await Run("perspective-near-two-asymmetric", () => PerspectiveRangeAndAsymmetryMatchAnalyticOffAxisChord(2, 100, 0.25));
            await Run("oblique-off-center-clip", ObliqueProjectionClipsAtThePixelLocalNearPlane);
            await Run("perspective-finite-large-far", () => HomogeneousFarPlaneMatchesIndependentOpticalLength(false));
            await Run("perspective-infinite-far", () => HomogeneousFarPlaneMatchesIndependentOpticalLength(true));
            await Run("opaque-one-ulp-before-clear", LargeFarOpaqueDepthOneUlpBeforeClearTerminatesTheVolume);
            foreach (string plane in new[] { "entry", "exit", "oblique", "excluded", "parallel" })
            {
                await Run($"scene-clip-{plane}", () => SceneClipPlanesBoundTheIntegratedChord(plane));
            }
            var jointPass = new VolumeCameraJointPassConformanceTests();
            await Run("joint-unequal-tint-reference", () =>
                jointPass.NestedUnequalTintVolumesMatchIndependentReferenceAcrossInsertionAndCameraOrder(backend));
            await Run("joint-uniform-mixed-clipping", () =>
                jointPass.UniformAndMixedTexturedFieldsRespectFarAndOpaqueClippingOnIntendedBackend(backend));
            await Run("joint-replacement-CpuAdmission", () =>
                jointPass.RendererAtlasReplacementRefusalPreservesPixelsAndRetriesAfterCorrection(
                    backend, VolumeCameraJointPassConformanceTests.VolumeCameraFailure.CpuAdmission));
            await Run("joint-replacement-GpuAdmission", () =>
                jointPass.RendererAtlasReplacementRefusalPreservesPixelsAndRetriesAfterCorrection(
                    backend, VolumeCameraJointPassConformanceTests.VolumeCameraFailure.GpuAdmission));
            await Run("joint-replacement-AtlasSubmission", () =>
                jointPass.RendererAtlasReplacementRefusalPreservesPixelsAndRetriesAfterCorrection(
                    backend, VolumeCameraJointPassConformanceTests.VolumeCameraFailure.AtlasSubmission));
            await Run("joint-ownership-uniform", () =>
                jointPass.StagedApplyAndRenderPublishesVolumeResourcesThatSurviveUnchangedRedraw(backend, false));
            await Run("joint-ownership-atlas", () =>
                jointPass.StagedApplyAndRenderPublishesVolumeResourcesThatSurviveUnchangedRedraw(backend, true));
            await Run("joint-zero-opacity-field-limit", () =>
                jointPass.ZeroOpacityFieldsDoNotConsumeActiveFieldSlotsAndRetryAfterRefusal(backend));
            await Run("joint-missing-asset-refusal", () =>
                jointPass.MissingSampledFieldRefusesUpdateAndPreservesPublishedFrame(backend));
            await Run("joint-retained-atlas-source-removal", () =>
                jointPass.PublishedAtlasRemainsAvailableWhenSourceFileIsMoved(backend));
            await Run("joint-unsupported-uniform-map", () =>
                jointPass.UnsupportedVolumeTexturesRefusePublicationAndAllowCorrectedRetry(
                    backend, VolumeCameraJointPassConformanceTests.VolumeTextureFailure.UniformSurfaceMap));
            await Run("joint-unsupported-zero-opacity-map", () =>
                jointPass.UnsupportedVolumeTexturesRefusePublicationAndAllowCorrectedRetry(
                    backend, VolumeCameraJointPassConformanceTests.VolumeTextureFailure.ZeroOpacitySurfaceMap));
            await Run("joint-unsupported-sampled-map", () =>
                jointPass.UnsupportedVolumeTexturesRefusePublicationAndAllowCorrectedRetry(
                    backend, VolumeCameraJointPassConformanceTests.VolumeTextureFailure.SampledSurfaceMap));
            await Run("joint-unsupported-density-duplicate", () =>
                jointPass.UnsupportedVolumeTexturesRefusePublicationAndAllowCorrectedRetry(
                    backend, VolumeCameraJointPassConformanceTests.VolumeTextureFailure.DuplicateDensity));
            await Run("joint-unsupported-density-composite", () =>
                jointPass.UnsupportedVolumeTexturesRefusePublicationAndAllowCorrectedRetry(
                    backend, VolumeCameraJointPassConformanceTests.VolumeTextureFailure.CompositeDensity));
            await Run("joint-dense-unequal-tint-uniform", () =>
                jointPass.OpaqueUnequalDensityFieldsPreserveExtinctionTintWeights(backend, false));
            await Run("joint-dense-unequal-tint-atlas", () =>
                jointPass.OpaqueUnequalDensityFieldsPreserveExtinctionTintWeights(backend, true));
            await Run("joint-workload-refusal-retry", () =>
                jointPass.ExpensiveJointWorkloadRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(backend));
            await Run("joint-nonfinite-density-scalar", () =>
                jointPass.NonfiniteDensityRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(backend, false));
            await Run("joint-nonfinite-density-grid", () =>
                jointPass.NonfiniteDensityRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(backend, true));
            await Run("joint-finite-density-scalar", () =>
                jointPass.FiniteDensityProductRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(backend, false));
            await Run("joint-finite-density-grid", () =>
                jointPass.FiniteDensityProductRefusalPreservesFrameAndSceneAndRetriesAfterCorrection(backend, true));
            await Run("joint-thin-density-scalar-translucent", () =>
                jointPass.LargeFiniteDensityInThinFieldRemainsVisible(backend, false, 1e30f, 0.5f));
            await Run("joint-thin-density-scalar-opaque", () =>
                jointPass.LargeFiniteDensityInThinFieldRemainsVisible(backend, false, 1e30f, 1f));
            await Run("joint-thin-density-grid-translucent", () =>
                jointPass.LargeFiniteDensityInThinFieldRemainsVisible(backend, true, 1e30f, 0.5f));
            await Run("joint-thin-density-grid-opaque", () =>
                jointPass.LargeFiniteDensityInThinFieldRemainsVisible(backend, true, 1e30f, 1f));
            await Run("joint-tint-exposure-scalar-retained", () =>
                jointPass.VolumeTintExposureOverflowRefusesBeforePublicationAndRetries(backend, false, false));
            await Run("joint-tint-exposure-scalar-staged", () =>
                jointPass.VolumeTintExposureOverflowRefusesBeforePublicationAndRetries(backend, false, true));
            await Run("joint-tint-exposure-grid-retained", () =>
                jointPass.VolumeTintExposureOverflowRefusesBeforePublicationAndRetries(backend, true, false));
            await Run("joint-tint-exposure-grid-staged", () =>
                jointPass.VolumeTintExposureOverflowRefusesBeforePublicationAndRetries(backend, true, true));
            await Run("joint-tint-exposure-zero-opacity", () =>
                jointPass.VolumeTintExposureAdmissionSkipsInactiveMaterials(backend, false));
            await Run("joint-tint-exposure-material-disabled", () =>
                jointPass.VolumeTintExposureAdmissionSkipsInactiveMaterials(backend, true));
            await Run("joint-high-tint-overlap-scalar", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, false, 1));
            await Run("joint-high-tint-overlap-dense-scalar", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, false, 1e30f));
            await Run("joint-high-tint-overlap-grid", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, true, 1));
            await Run("joint-high-tint-overlap-dense-grid", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, true, 1e30f));
            await Run("joint-high-tint-overlap-1e-37-scalar", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, false, 1e-37f));
            await Run("joint-high-tint-overlap-1e-20-scalar", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, false, 1e-20f));
            await Run("joint-high-tint-overlap-1e-8-scalar", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, false, 1e-8f));
            await Run("joint-high-tint-overlap-1e-37-grid", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, true, 1e-37f));
            await Run("joint-high-tint-overlap-1e-20-grid", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, true, 1e-20f));
            await Run("joint-high-tint-overlap-1e-8-grid", () =>
                jointPass.OverlappingHighFiniteTintsMatchTheIndependentComposite(backend, true, 1e-8f));
            await Run("joint-signal-subnormal-scale-scalar", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, false, VolumeCameraJointPassConformanceTests.LowDensitySignal.SubnormalScale));
            await Run("joint-signal-step-underflow-scalar", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, false, VolumeCameraJointPassConformanceTests.LowDensitySignal.StepUnderflow));
            await Run("joint-signal-opacity-product-scalar", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, false, VolumeCameraJointPassConformanceTests.LowDensitySignal.OpacityProduct));
            await Run("joint-signal-subnormal-scale-grid", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, true, VolumeCameraJointPassConformanceTests.LowDensitySignal.SubnormalScale));
            await Run("joint-signal-step-underflow-grid", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, true, VolumeCameraJointPassConformanceTests.LowDensitySignal.StepUnderflow));
            await Run("joint-signal-opacity-product-grid", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, true, VolumeCameraJointPassConformanceTests.LowDensitySignal.OpacityProduct));
            await Run("joint-signal-subnormal-sample-grid", () =>
                jointPass.UnderflowingVolumeSignalRefusesPublicationAndAllowsRetry(
                    backend, true, VolumeCameraJointPassConformanceTests.LowDensitySignal.SubnormalSample));
            await Run("joint-signal-zero-opacity", () =>
                jointPass.UnderflowAdmissionSkipsInactiveMaterials(backend, false));
            await Run("joint-signal-material-disabled", () =>
                jointPass.UnderflowAdmissionSkipsInactiveMaterials(backend, true));
            await Run("joint-signal-retained-resize-scalar", () =>
                jointPass.RetainedUnderflowSignalRefusesBeforeResizedOutputAndAllowsRetry(backend, false));
            await Run("joint-signal-retained-resize-grid", () =>
                jointPass.RetainedUnderflowSignalRefusesBeforeResizedOutputAndAllowsRetry(backend, true));
            await Run("joint-output-allocation-retained", () =>
                jointPass.VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
                    backend, VolumeCameraJointPassConformanceTests.VolumeFrameFailure.OutputAllocation, false));
            await Run("joint-output-frame-submission-retained", () =>
                jointPass.VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
                    backend, VolumeCameraJointPassConformanceTests.VolumeFrameFailure.FrameSubmission, false));
            await Run("joint-output-publication-retained", () =>
                jointPass.VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
                    backend, VolumeCameraJointPassConformanceTests.VolumeFrameFailure.OutputPublication, false));
            await Run("joint-output-allocation-staged", () =>
                jointPass.VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
                    backend, VolumeCameraJointPassConformanceTests.VolumeFrameFailure.OutputAllocation, true));
            await Run("joint-output-frame-submission-staged", () =>
                jointPass.VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
                    backend, VolumeCameraJointPassConformanceTests.VolumeFrameFailure.FrameSubmission, true));
            await Run("joint-output-publication-staged", () =>
                jointPass.VolumeCameraCandidateRollsBackOnOutputFailureAndRetries(
                    backend, VolumeCameraJointPassConformanceTests.VolumeFrameFailure.OutputPublication, true));
            foreach ((string id, VolumeCameraJointPassConformanceTests.VolumeCleanupFailure failure) in new[]
            {
                ("none", VolumeCameraJointPassConformanceTests.VolumeCleanupFailure.None),
                ("depth", VolumeCameraJointPassConformanceTests.VolumeCleanupFailure.DepthSnapshot),
                ("resources", VolumeCameraJointPassConformanceTests.VolumeCleanupFailure.VolumeResources),
                ("both", VolumeCameraJointPassConformanceTests.VolumeCleanupFailure.Both),
            })
            {
                await Run($"joint-direct-retirement-{id}", () =>
                    jointPass.UnadmittedRetainedVolumeFrameSurvivesRetirementFailure(backend, failure));
                foreach (bool staged in new[] { false, true })
                {
                    string frame = staged ? "staged" : "retained";
                    await Run($"joint-retirement-{id}-{frame}", () =>
                        jointPass.PublishedVolumeFrameSurvivesRetirementFailure(backend, failure, staged));
                }
            }
            foreach (bool staged in new[] { false, true })
            {
                string frame = staged ? "staged" : "retained";
                await Run($"joint-failed-frame-cleanup-{frame}", () =>
                    jointPass.FailedVolumeFrameReportsOperationAndCandidateCleanupFailuresAndRetries(backend, staged));
                await Run($"joint-snapshot-rollback-{frame}", () =>
                    jointPass.SameSizePublicationFailurePreservesPublishedVolumeDepthSnapshot(backend, staged));
                await Run($"joint-snapshot-budget-refusal-{frame}", () =>
                    jointPass.SnapshotBudgetEnforcesExactOverlapAndPreservesRefusedFrame(backend, staged, false));
                await Run($"joint-snapshot-budget-exact-{frame}", () =>
                    jointPass.SnapshotBudgetEnforcesExactOverlapAndPreservesRefusedFrame(backend, staged, true));
            }
            await Run("joint-snapshot-budget-reuse", () =>
                jointPass.AdmittedVolumeDepthSnapshotsReuseTwoBudgetedTextures(backend));
            await Run("joint-renderer-disposal-retry", () =>
                jointPass.RendererDisposalReportsVolumeCleanupFailuresAndAllowsReleaseRetry(backend));
            WriteReceipt("passed");
        }
        finally
        {
            GateBackend.Value = previousBackend;
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task CameraUniformBindingFollowsPipelineAcrossDrawTransitions(
        SilkGraphicsBackend backend,
        bool densityAtlas)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        SilkShaderBinaryFormat format = backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
        using ISilkGraphicsShaderModule volumeVertex = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadVolumeCameraVertex(format));
        using ISilkGraphicsShaderModule volumeFragment = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadVolumeCameraFragment(format, densityAtlas));
        using ISilkGraphicsBindingLayout volumeLayout = device.CreateBindingLayout(
            SilkBindingLayoutDescriptor.VolumeCameraParameters(densityAtlas));
        using ISilkGraphicsShaderProgram volumeProgram = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(volumeVertex, volumeFragment, volumeLayout));
        using ISilkGraphicsPipeline volumePipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                volumeProgram,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));

        using ISilkGraphicsShaderModule sceneVertex = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshVertex(format));
        using ISilkGraphicsShaderModule sceneFragment = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshFragment(format));
        using ISilkGraphicsBindingLayout sceneLayout = device.CreateBindingLayout(
            SilkBindingLayoutDescriptor.SceneParameters);
        using ISilkGraphicsShaderProgram sceneProgram = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(sceneVertex, sceneFragment, sceneLayout));
        using ISilkGraphicsPipeline scenePipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                sceneProgram,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
        using ISilkPickGraphicsPipeline pickPipeline =
            ((ISilkPickingGraphicsDevice)device).CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(format, SilkPickPrimitiveTopology.TriangleList));
        using ISilkSelectionMaskGraphicsPipeline maskPipeline =
            ((ISilkSelectionOutlineGraphicsDevice)device).CreateSelectionMaskGraphicsPipeline(
                SilkSelectionMaskPipelineDescriptor.CreateChecked(format, depthTested: true));

        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(4, 4));
        using ISilkGraphicsTexture mask = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.Sampled));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(4, 4));
        using ISilkGraphicsBuffer vertices = device.CreateBuffer(
            72, SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer indices = device.CreateBuffer(
            12, SilkBufferUsage.Index | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer shortVolume = device.CreateBuffer(
            31, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer volumeUniform = device.CreateBuffer(
            32, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer shortScene = device.CreateBuffer(
            79, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer sceneUniform = device.CreateBuffer(
            80, SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetViewport(new SilkViewport(0, 0, 4, 4));
        commands.SetScissor(new SilkScissor(0, 0, 4, 4));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);

        await Assert.That(() => commands.SetUniformBuffer(0, 0, volumeUniform))
            .Throws<ArgumentException>();
        await Assert.That(() => commands.SetUniformBuffer(0, 0, sceneUniform))
            .ThrowsNothing();
        commands.SetGraphicsPipeline(volumePipeline);
        await Assert.That(() => commands.SetUniformBuffer(0, 0, shortVolume))
            .Throws<ArgumentException>();
        await Assert.That(() => commands.SetUniformBuffer(0, 0, volumeUniform))
            .ThrowsNothing();
        await Assert.That(() => commands.DrawIndexed(3)).ThrowsNothing();

        commands.SetGraphicsPipeline(scenePipeline);
        await Assert.That(() => commands.DrawIndexed(3))
            .Throws<InvalidOperationException>().WithMessageContaining("80");
        await Assert.That(() => commands.SetUniformBuffer(0, 0, shortScene))
            .Throws<ArgumentException>();
        commands.SetUniformBuffer(0, 0, sceneUniform);
        await Assert.That(() => commands.DrawIndexed(3)).ThrowsNothing();

        commands.SetGraphicsPipeline(volumePipeline);
        commands.SetUniformBuffer(0, 0, volumeUniform);
        var picking = (ISilkPickGraphicsCommandList)commands;
        picking.SetPickGraphicsPipeline(pickPipeline);
        picking.SetPickBaseToken(1);
        await Assert.That(() => commands.DrawIndexed(3))
            .Throws<InvalidOperationException>().WithMessageContaining("80");
        commands.SetUniformBuffer(0, 0, sceneUniform);
        await Assert.That(() => commands.DrawIndexed(3)).ThrowsNothing();

        commands.SetGraphicsPipeline(volumePipeline);
        commands.SetUniformBuffer(0, 0, volumeUniform);
        commands.EndRendering();
        var selection = (ISilkSelectionOutlineGraphicsCommandList)commands;
        selection.BeginSelectionMaskRendering(new SilkSelectionMaskRenderingDescriptor(mask, depth));
        selection.SetSelectionMaskGraphicsPipeline(maskPipeline);
        await Assert.That(() => commands.DrawIndexed(3))
            .Throws<InvalidOperationException>().WithMessageContaining("80");
        commands.SetUniformBuffer(0, 0, sceneUniform);
        await Assert.That(() => commands.DrawIndexed(3)).ThrowsNothing();
        if (backend == SilkGraphicsBackend.Vulkan)
        {
            commands.SetGraphicsPipeline(volumePipeline);
            await Assert.That(() => commands.SetUniformBuffer(0, 0, volumeUniform))
                .Throws<ArgumentException>();
            await Assert.That(() => commands.DrawIndexed(3)).ThrowsNothing();
        }
        commands.EndRendering();
    }

    [Test]
    public async Task LargeFarOpaqueDepthOneUlpBeforeClearTerminatesTheVolume()
    {
        const float far = 16777216;
        const string occluderPath = "/World/OneUlpOccluder";
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        using ISilkGraphicsDevice device = CreateVulkanDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            Width, Height, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        byte[] frame = CreateOrthographicFrame(0, far);
        SilkMeshRendererConformance.Apply(renderer, 1, frame,
            VolumeCommandAuthoring.CreateDepthOccluderMeshCommand(occluderPath, -(far - 1)));
        _ = renderer.Render(color, depth, options);
        var depths = new float[checked((int)(Width * Height))];
        depth.ReadbackForTesting(depths);
        await Assert.That(depths[center / ParityImage.BytesPerPixel])
            .IsEqualTo(MathF.BitDecrement(1f));
        byte[] opaque = new byte[checked((int)(Width * Height * ParityImage.BytesPerPixel))];
        color.ReadbackForTesting(opaque);

        string grid = CreateUniformGrid("one-ulp-opaque", 8, 8, 8);
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[10] = 4;
        transform[14] = -(far - 2);
        SilkMeshRendererConformance.Apply(renderer, 2,
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "8,8,8"),
            VolumeCommandAuthoring.WithTransform(
                VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath), transform));
        _ = renderer.Render(color, depth, options);
        byte[] clipped = new byte[opaque.Length];
        color.ReadbackForTesting(clipped);

        SilkMeshRendererConformance.Apply(renderer, 3,
            SilkMeshRendererConformance.CreateRemoveCommand(100, occluderPath));
        _ = renderer.Render(color, depth, options);
        byte[] clear = new byte[opaque.Length];
        color.ReadbackForTesting(clear);
        double[] tint = [0.9, 0.36, 0.12];
        double clippedTransmission = Math.Exp(-0.75);
        double clearTransmission = Math.Exp(-1);
        for (int channel = 0; channel < tint.Length; channel++)
        {
            double expectedClipped = 255 * tint[channel] * (1 - clippedTransmission) +
                opaque[center + channel] * clippedTransmission;
            double expectedClear = 255 * tint[channel] * (1 - clearTransmission);
            await Assert.That(Math.Abs(clipped[center + channel] - expectedClipped))
                .IsLessThanOrEqualTo(2.0);
            await Assert.That(Math.Abs(clear[center + channel] - expectedClear))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    public async Task ObliqueProjectionClipsAtThePixelLocalNearPlane()
    {
        const int x = 60;
        const int y = 48;
        const double near = 0.75;
        const double far = 2;
        const double shear = 0.8;
        byte[] frame = CreateOrthographicFrame(near, far);
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + 2 * sizeof(double)), shear);
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[14] = -1;
        string grid = CreateUniformGrid("oblique-projection", 8, 8, 8);
        ParityImage image = CaptureSceneWithFrame(CreateVulkanDevice, frame,
            [VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "8,8,8")],
            VolumeCommandAuthoring.WithTransform(
                VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath), transform));

        double eyeX = 2 * (x + 0.5) / Width - 1;
        // clip.z = shear*x - 2*z/(far-near) - (far+near)/(far-near).
        // The GL near plane clip.z=-1 lies at z=-(near-shear*x*(far-near)/2).
        double pixelNear = near - shear * eyeX * (far - near) / 2;
        double opticalDistance = 1.5 - pixelNear;
        double alpha = 1 - Math.Exp(-opticalDistance);
        double[] tint = [0.9, 0.36, 0.12];
        int pixel = PixelOffset(x, y);
        for (int channel = 0; channel < tint.Length; channel++)
        {
            await Assert.That(Math.Abs(PixelChannel(image, pixel, channel) - 255 * tint[channel] * alpha))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HomogeneousFarPlaneMatchesIndependentOpticalLength(bool infiniteFar)
    {
        const int x = 60;
        const int y = 48;
        double near = infiniteFar ? 0.1 : 1e6;
        double far = 2e7;
        double scale = infiniteFar ? 1 : far;
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width, Height, SilkMeshRendererConformance.Identity());
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, infiniteFar ? -1 : -(far + near) / (far - near), -1,
            0, 0, infiniteFar ? -2 * near : -2 * far * near / (far - near), 0,
        ];
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + index * sizeof(double)), projection[index]);
        }
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[0] = transform[5] = transform[10] = scale;
        transform[14] = -scale;
        string grid = CreateUniformGrid("homogeneous-far", 8, 8, 8);
        ParityImage image = CaptureSceneWithFrame(CreateVulkanDevice, frame,
            [VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "8,8,8")],
            VolumeCommandAuthoring.WithTransform(
                VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath), transform));

        double xSlope = 2 * (x + 0.5) / Width - 1;
        double ySlope = 2 * (y + 0.5) / Height - 1;
        // The finite case clips [0.5*scale,1.5*scale] at far=scale.
        // The infinite case includes the full field chord.
        double opticalDistance = (infiniteFar ? 1 : 0.5) *
            Math.Sqrt(1 + xSlope * xSlope + ySlope * ySlope);
        double alpha = 1 - Math.Exp(-opticalDistance);
        double[] tint = [0.9, 0.36, 0.12];
        int pixel = PixelOffset(x, y);
        for (int channel = 0; channel < tint.Length; channel++)
        {
            await Assert.That(Math.Abs(PixelChannel(image, pixel, channel) - 255 * tint[channel] * alpha))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    [Arguments(0.01, 1000.0, 0.25)]
    [Arguments(1.0, 11.0, 0.0)]
    [Arguments(2.0, 100.0, 0.25)]
    public async Task PerspectiveRangeAndAsymmetryMatchAnalyticOffAxisChord(double near, double far, double horizontalOffset)
    {
        const int x = 60;
        const int y = 48;
        double xSlope = 2 * (x + 0.5) / Width - 1 + horizontalOffset;
        double ySlope = 2 * (y + 0.5) / Height - 1;
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width, Height, SilkMeshRendererConformance.Identity());
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            horizontalOffset, 0, -(far + near) / (far - near), -1,
            0, 0, -2 * far * near / (far - near), 0,
        ];
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + index * sizeof(double)), projection[index]);
        }
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[12] = 3 * xSlope;
        transform[14] = -3;
        string grid = CreateUniformGrid("perspective-range", 8, 8, 8);
        ParityImage image = CaptureSceneWithFrame(CreateVulkanDevice, frame,
            [VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "8,8,8")],
            VolumeCommandAuthoring.WithTransform(
                VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath), transform));
        double opticalDistance = Math.Sqrt(1 + xSlope * xSlope + ySlope * ySlope);
        double alpha = 1 - Math.Exp(-opticalDistance);
        double[] tint = [0.9, 0.36, 0.12];
        int pixel = PixelOffset(x, y);
        for (int channel = 0; channel < tint.Length; channel++)
        {
            await Assert.That(Math.Abs(PixelChannel(image, pixel, channel) - 255 * tint[channel] * alpha))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    [Arguments(1e12, 1, 1, false, false)]
    [Arguments(1e9, 2, 2, false, false)]
    [Arguments(0, 1e-6, 1, false, false)]
    [Arguments(0, 1e-6, 1, true, false)]
    [Arguments(0, 1e-6, 1, false, true)]
    public async Task EyeSpaceRayMatchesIndependentLocalChord(
        double origin, double scale, double viewScale, bool inside, bool clipped)
    {
        double near = clipped ? 0.75 : 0.1;
        byte[] frame = CreateOrthographicFrame(near * scale * viewScale, 2 * scale * viewScale);
        double[] view = SilkMeshRendererConformance.Identity();
        view[10] = viewScale;
        view[12] = view[13] = -origin;
        view[14] = -origin * viewScale;
        for (int index = 0; index < view.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(16 + index * sizeof(double)), view[index]);
        }
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144), 1 / scale);
        BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(144 + 5 * sizeof(double)), 1 / scale);
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[0] = transform[5] = transform[10] = scale;
        transform[12] = transform[13] = origin;
        transform[14] = origin - (inside ? 0 : scale);
        string grid = CreateUniformGrid("eye-space-reference", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "8,8,8");
        byte[] mesh = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath), transform);
        ParityImage image = CaptureSceneWithFrame(CreateVulkanDevice, frame, [material], mesh);
        double opticalDistance = inside ? 0.5 - near : clipped ? 1.5 - near : 1;
        double alpha = 1 - Math.Exp(-opticalDistance);
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        double[] tint = [0.9, 0.36, 0.12];
        for (int channel = 0; channel < tint.Length; channel++)
        {
            await Assert.That(Math.Abs(PixelChannel(image, center, channel) - 255 * tint[channel] * alpha))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    [Arguments("entry")]
    [Arguments("exit")]
    [Arguments("oblique")]
    [Arguments("excluded")]
    [Arguments("parallel")]
    public async Task SceneClipPlanesBoundTheIntegratedChord(string kind)
    {
        double[] plane = kind switch
        {
            "entry" => [0, 0, -1, -1],
            "exit" => [0, 0, 1, 1],
            "oblique" => [1, 0, 1, 1],
            "excluded" => [0, 0, 1, 0],
            "parallel" => [1, 0, 0, -1],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        byte[] frame = new byte[536];
        CreateOrthographicFrame(0.1, 2).CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(272), 1);
        for (int index = 0; index < plane.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(280 + index * sizeof(double)), plane[index]);
        }
        string grid = CreateUniformGrid($"scene-clip-{kind}", 8, 8, 8);
        ParityImage image = CaptureSceneWithFrame(CreateVulkanDevice, frame,
            [VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "8,8,8")],
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath, zOffset: -1));
        double opticalDistance = kind is "excluded" or "parallel" ? 0 :
            kind == "oblique" ? 0.5 + 1.0 / Width : 0.5;
        double alpha = 1 - Math.Exp(-opticalDistance);
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        double[] tint = [0.9, 0.36, 0.12];
        for (int channel = 0; channel < tint.Length; channel++)
        {
            await Assert.That(Math.Abs(PixelChannel(image, center, channel) - 255 * tint[channel] * alpha))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    public async Task MaterialDisabledSingleVolumeMatchesOrdinaryProxyOnVulkan()
    {
        using ISilkGraphicsDevice device = CreateVulkanDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        string grid = CreateUniformGrid("material-disabled", 2, 2, 2);
        byte[] mesh = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath, zOffset: -1);
        SilkMeshRenderOptions options = new(new SilkColor(0, 0, 0, 1), 1, UseSceneMaterials: false);
        SilkMeshRendererConformance.Apply(renderer, 1,
            CreateOrthographicFrame(0.1, 2),
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(MaterialPath, 1, grid, "2,2,2"),
            mesh);
        _ = renderer.Render(color, depth, options);
        byte[] disabled = new byte[checked((int)(Width * Height * ParityImage.BytesPerPixel))];
        color.ReadbackForTesting(disabled);

        SilkMeshRendererConformance.Apply(renderer, 2,
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, string.Empty, zOffset: -1));
        _ = renderer.Render(color, depth, options);
        byte[] ordinary = new byte[disabled.Length];
        color.ReadbackForTesting(ordinary);
        await Assert.That(HasNonZeroRgb(ordinary)).IsTrue();
        await Assert.That(disabled.SequenceEqual(ordinary)).IsTrue();
    }

    [Test]
    [Arguments(1e6, false)]
    [Arguments(1e9, false)]
    [Arguments(1e9, true)]
    public async Task LargeScaleInsideRayMatchesFieldLocalAnalyticReference(double scale, bool outside)
    {
        string grid = CreateUniformGrid($"large-scale-{scale:R}-{outside}", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath, authoredDensity: 1, grid, "8,8,8");
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[0] = transform[5] = transform[10] = scale;
        if (outside)
        {
            transform[12] = scale;
        }
        byte[] mesh = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath), transform);
        ParityImage image = CaptureSceneWithFrame(
            CreateVulkanDevice, CreateOrthographicFrame(0.1, scale), [material], mesh);
        // The camera is at local z=0. The visible chord to z=-0.5 has length
        // 0.5 - near/scale, regardless of the world-space exit distance.
        double opticalDistance = outside ? 0 : 0.5 - 0.1 / scale;
        double alpha = 1 - Math.Exp(-opticalDistance);
        double[] tint = [0.9, 0.36, 0.12];
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        for (int channel = 0; channel < tint.Length; channel++)
        {
            await Assert.That(Math.Abs(PixelChannel(image, center, channel) - 255 * tint[channel] * alpha))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    [Arguments(511, 90)]
    [Arguments(512, 90)]
    [Arguments(512, 45)]
    public async Task WideShallowNonuniformGridMatchesAnAnalyticSlabOnVulkan(int width, int degrees)
    {
        int first = width / 4;
        int last = width / 2;
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            EvidenceDirectory, $"analytic-slab-{degrees}", width, 1, 1,
            (x, _, _) => x >= first && x < last ? 1f : 0f);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath, authoredDensity: 1, grid, $"{width},1,1");
        double angle = degrees * Math.PI / 180;
        double sine = Math.Sin(angle);
        double cosine = Math.Cos(angle);
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[0] = cosine;
        transform[2] = -sine;
        transform[8] = sine;
        transform[10] = cosine;
        transform[14] = -1;
        byte[] mesh = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(MeshPath, MaterialPath),
            transform);
        ParityImage image = CaptureSceneWithFrame(
            CreateVulkanDevice, CreateOrthographicFrame(0.1, 2), [material], mesh);

        // Linear filtering preserves the area of this interior slab's two ramps.
        // ds = dx / sin(angle); neither end of the slab is clipped by the box.
        double opticalDistance = (double)(last - first) / width / Math.Abs(sine);
        double alpha = 1 - Math.Exp(-opticalDistance);
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        double[] tint = [0.9, 0.36, 0.12];
        for (int channel = 0; channel < tint.Length; channel++)
        {
            double expected = 255 * tint[channel] * alpha;
            await Assert.That(Math.Abs(PixelChannel(image, center, channel) - expected))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    public async Task CameraInsideVolumeIntegratesOnlyTheVisibleRaySegmentOnVulkan()
    {
        string grid = CreateUniformGrid("camera", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        byte[] mesh = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
            MeshPath, MaterialPath, zOffset: -0.7f);
        ParityImage outside = Capture(CreateVulkanDevice, material, meshCommands: [mesh]);

        double[] view = SilkMeshRendererConformance.Identity();
        view[14] = 0.7;
        ParityImage inside = Capture(CreateVulkanDevice, material, view, mesh);

        int center = checked((int)(((Height / 2) * Width + (Width / 2)) * ParityImage.BytesPerPixel));
        await Assert.That(PixelChannel(inside, center, 0))
            .IsGreaterThan((byte)0)
            .Because("the visible segment must still contribute when the camera is inside.");
        await Assert.That(PixelChannel(inside, center, 0))
            .IsLessThan(PixelChannel(outside, center, 0))
            .Because("a camera inside the volume integrates from its origin to the exit face.");
        double[] tint = [0.9, 0.36, 0.12];
        for (int channel = 0; channel < tint.Length; channel++)
        {
            double expectedOutside = 255 * tint[channel] * (1 - Math.Exp(-1));
            double expectedInside = 255 * tint[channel] * (1 - Math.Exp(-0.4));
            await Assert.That(Math.Abs(PixelChannel(outside, center, channel) - expectedOutside))
                .IsLessThanOrEqualTo(2.0);
            await Assert.That(Math.Abs(PixelChannel(inside, center, channel) - expectedInside))
                .IsLessThanOrEqualTo(2.0);
        }
    }

    [Test]
    public async Task OverlappingEqualTintVolumesCompositeIndependentlyOfDepthOrderOnVulkan()
    {
        string grid = CreateUniformGrid("overlap", 8, 8, 8);
        byte[] materialA = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPathA,
            authoredDensity: 0.5f,
            grid,
            "8,8,8");
        byte[] materialB = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPathB,
            authoredDensity: 1,
            grid,
            "8,8,8");
        byte[] near = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
            "/World/VolumeA",
            MaterialPathA,
            zOffset: -0.7f,
            id: 1);
        byte[] far = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
            "/World/VolumeB",
            MaterialPathB,
            zOffset: -1,
            id: 2);
        byte[] frame = CreateOrthographicFrame(0.1, 2);
        ParityImage firstOrder = CaptureSceneWithFrame(
            CreateVulkanDevice,
            frame,
            [materialA, materialB],
            near,
            far);
        ParityImage reversedOrder = CaptureSceneWithFrame(
            CreateVulkanDevice,
            CreateReverseOrthographicFrame(),
            [materialA, materialB],
            near,
            far);
        ParityImage singleA = CaptureScene(CreateVulkanDevice, null, [materialA], near);
        ParityImage singleB = CaptureScene(CreateVulkanDevice, null, [materialB], far);

        ImageDelta orderDelta = ImageDelta.Compare(firstOrder, reversedOrder);
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        await Assert.That(orderDelta.MaximumChannelDelta)
            .IsLessThanOrEqualTo(1)
            .Because("equal-tint volume alpha compositing must commute when the camera reverses their depth order.");
        await Assert.That(PixelChannel(firstOrder, center, 0))
            .IsGreaterThan(Math.Max(
                PixelChannel(singleA, center, 0),
                PixelChannel(singleB, center, 0)))
            .Because("the intersecting volumes must both contribute at the center pixel.");
    }

    [Test]
    public async Task PerspectiveCameraUsesOffAxisRayForVolumeIntersectionOnVulkan()
    {
        string grid = CreateUniformGrid("perspective", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        byte[] mesh = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
            MeshPath,
            MaterialPath,
            zOffset: -0.7f);
        ParityImage image = CaptureSceneWithFrame(
            CreateVulkanDevice,
            CreatePerspectiveFrame(),
            [material],
            mesh);

        int centerX = (int)(Width / 2);
        int offAxisX = (int)(Width * 0.8125);
        int pixelY = (int)(Height / 2);
        int center = PixelOffset(centerX, pixelY);
        int offAxis = PixelOffset(offAxisX, pixelY);
        await Assert.That(PixelChannel(image, center, 0))
            .IsGreaterThan((byte)0)
            .Because("the center perspective ray intersects the volume.");
        await Assert.That(PixelChannel(image, offAxis, 0))
            .IsGreaterThan((byte)0)
            .Because("the off-axis perspective ray still intersects the volume.");
        await Assert.That(PixelChannel(image, offAxis, 0))
            .IsLessThan(PixelChannel(image, center, 0))
            .Because("the off-axis perspective ray crosses a shorter segment of the cube.");

        double[] tint = [0.9, 0.36, 0.12];
        foreach (int pixelX in new[] { centerX, offAxisX })
        {
            double rayX = (2.0 * (pixelX + 0.5) / Width) - 1;
            double rayY = (2.0 * (pixelY + 0.5) / Height) - 1;
            double exitDepth = Math.Min(1.2, Math.Min(0.5 / Math.Abs(rayX), 0.5 / Math.Abs(rayY)));
            double opticalDistance = (exitDepth - 0.2) * Math.Sqrt(1 + (rayX * rayX) + (rayY * rayY));
            int pixelOffset = PixelOffset(pixelX, pixelY);
            for (int channel = 0; channel < tint.Length; channel++)
            {
                double expected = 255 * tint[channel] * (1 - Math.Exp(-opticalDistance));
                await Assert.That(Math.Abs(PixelChannel(image, pixelOffset, channel) - expected))
                    .IsLessThanOrEqualTo(2.0)
                    .Because("the pixel must match its independently calculated ray-box chord.");
            }
        }
    }

    [Test]
    public async Task ScaledNonUniformVolumeMatchesTheFieldLocalDistanceReferenceOnVulkan()
    {
        string grid = CreateUniformGrid("scaled-reference", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        byte[] referenceMesh = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
            MeshPath,
            MaterialPath,
            zOffset: -1.5f);
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[0] = 2;
        transform[5] = 0.5;
        transform[10] = 1.5;
        transform[14] = -1.5;
        byte[] scaledMesh = VolumeCommandAuthoring.WithTransform(referenceMesh, transform);
        byte[] frame = CreateOrthographicFrame(0.1, 3);

        ParityImage reference = CaptureSceneWithFrame(
            CreateVulkanDevice,
            frame,
            [material],
            referenceMesh);
        ParityImage scaled = CaptureSceneWithFrame(
            CreateVulkanDevice,
            frame,
            [material],
            scaledMesh);

        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        await Assert.That(PixelRgbDelta(reference, scaled, center))
            .IsLessThanOrEqualTo(1)
            .Because("scaling the same homogeneous field changes world length, not field-local optical distance.");
    }

    [Test]
    public async Task NearPlaneClippedVolumeEntryStillRendersOnlyTheVisibleSegmentOnVulkan()
    {
        string grid = CreateUniformGrid("near-clip", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        byte[] mesh = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
            MeshPath,
            MaterialPath,
            zOffset: -0.35f);
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[10] = 0.6;
        transform[14] = -0.35;
        mesh = VolumeCommandAuthoring.WithTransform(mesh, transform);

        ParityImage clipped = CaptureSceneWithFrame(
            CreateVulkanDevice,
            CreateOrthographicFrame(near: 0.1, far: 1),
            [material],
            mesh);
        ParityImage unclipped = CaptureSceneWithFrame(
            CreateVulkanDevice,
            CreateOrthographicFrame(near: 0.001, far: 1),
            [material],
            mesh);

        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        byte clippedRed = PixelChannel(clipped, center, 0);
        byte unclippedRed = PixelChannel(unclipped, center, 0);
        await Assert.That(clippedRed)
            .IsGreaterThan((byte)0)
            .Because("the visible exit face must emit a fragment when the entry face is before the near plane.");
        await Assert.That(clippedRed)
            .IsLessThan(unclippedRed)
            .Because("the clipped image integrates from the near plane rather than from the hidden entry.");
    }

    [Test]
    public async Task FarPlaneClipsTheVolumeIntegrationSegmentOnVulkan()
    {
        string grid = CreateUniformGrid("far-clip", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        double[] transform = SilkMeshRendererConformance.Identity();
        transform[10] = 0.6;
        transform[14] = -0.65;
        byte[] mesh = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                MeshPath,
                MaterialPath,
                zOffset: -0.65f),
            transform);

        ParityImage clipped = CaptureSceneWithFrame(
            CreateVulkanDevice,
            CreateOrthographicFrame(near: 0.1, far: 0.7),
            [material],
            mesh);
        ParityImage full = CaptureSceneWithFrame(
            CreateVulkanDevice,
            CreateOrthographicFrame(near: 0.1, far: 1.2),
            [material],
            mesh);

        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        await Assert.That(PixelChannel(clipped, center, 0))
            .IsGreaterThan((byte)0)
            .Because("the entry face remains visible before the far plane.");
        await Assert.That(PixelChannel(clipped, center, 0))
            .IsLessThan(PixelChannel(full, center, 0))
            .Because("integration stops at the far plane even though the cube exits later.");
    }

    [Test]
    public async Task VolumeEnclosingNearAndFarPlanesMatchesFieldLocalReferenceOnVulkan()
    {
        string grid = CreateUniformGrid("near-far-enclosed", 8, 8, 8);
        byte[] enclosingMaterial = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        byte[] referenceMaterial = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 0.25f,
            grid,
            "8,8,8");
        double[] enclosingTransform = SilkMeshRendererConformance.Identity();
        enclosingTransform[10] = 4;
        enclosingTransform[14] = -1.5;
        double[] referenceTransform = SilkMeshRendererConformance.Identity();
        referenceTransform[14] = -0.5;
        byte[] enclosingVolume = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                MeshPath,
                MaterialPath,
                zOffset: -1.5f),
            enclosingTransform);
        byte[] referenceVolume = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                MeshPath,
                MaterialPath,
                zOffset: -0.5f),
            referenceTransform);
        byte[] frame = CreateOrthographicFrame(near: 0.1, far: 1);

        ParityImage enclosing = CaptureSceneWithFrame(
            CreateVulkanDevice,
            frame,
            [enclosingMaterial],
            enclosingVolume);
        ParityImage reference = CaptureSceneWithFrame(
            CreateVulkanDevice,
            frame,
            [referenceMaterial],
            referenceVolume);

        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));
        await Assert.That(PixelChannel(enclosing, center, 0))
            .IsGreaterThan((byte)0)
            .Because("the raster proxy must still cover rays when both box faces lie outside the clip interval.");
        await Assert.That(PixelRgbDelta(enclosing, reference, center))
            .IsLessThanOrEqualTo(1)
            .Because("the clipped segment has the same field-local optical distance as the reference field.");
    }

    [Test]
    public async Task OpaqueDepthTerminatesVolumeAndSnapshotTracksUpdateAndRestoreOnVulkan()
    {
        string grid = CreateUniformGrid("opaque-depth", 8, 8, 8);
        byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            grid,
            "8,8,8");
        double[] volumeTransform = SilkMeshRendererConformance.Identity();
        volumeTransform[10] = 0.6;
        volumeTransform[14] = -0.55;
        byte[] volume = VolumeCommandAuthoring.WithTransform(
            VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                MeshPath,
                MaterialPath,
                zOffset: -0.55f),
            volumeTransform);
        byte[] frame = CreateOrthographicFrame(near: 0.1, far: 1.2);
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        int center = PixelOffset((int)(Width / 2), (int)(Height / 2));

        using ISilkGraphicsDevice device = CreateVulkanDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width,
                Height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        byte[] occluder = VolumeCommandAuthoring.CreateDepthOccluderMeshCommand(
            "/World/OpaqueOccluder",
            z: -0.55f);
        byte[] opaqueOnly = CaptureSceneWithFrame(
            CreateVulkanDevice,
            frame,
            [],
            occluder).Rgba.ToArray();

        byte[] Render(uint revision, byte[] depthOccluder)
        {
            SilkMeshRendererConformance.Apply(
                renderer,
                revision,
                frame,
                material,
                volume,
                depthOccluder);
            _ = renderer.Render(color, depth, options);
            byte[] pixels = new byte[checked((int)(Width * Height * ParityImage.BytesPerPixel))];
            color.ReadbackForTesting(pixels);
            return pixels;
        }

        byte[] movedMesh = VolumeCommandAuthoring.CreateDepthOccluderMeshCommand(
            "/World/OpaqueOccluder",
            z: -0.75f);
        byte[] movedOpaqueOnly = CaptureSceneWithFrame(
            CreateVulkanDevice, frame, [], movedMesh).Rgba.ToArray();
        byte[] first = Render(1, occluder);
        byte[] moved = Render(2, movedMesh);
        byte[] restored = Render(3, occluder);
        byte[] frontMesh = VolumeCommandAuthoring.CreateDepthOccluderMeshCommand(
            "/World/OpaqueOccluder",
            z: -0.1f);
        byte[] frontOpaqueOnly = CaptureSceneWithFrame(
            CreateVulkanDevice, frame, [], frontMesh).Rgba.ToArray();
        byte[] frontOccluder = Render(4, frontMesh);

        int firstDistance = PixelRgbDelta(first, opaqueOnly, center);
        int movedDistance = PixelRgbDelta(moved, movedOpaqueOnly, center);
        await Assert.That(firstDistance)
            .IsGreaterThan(0)
            .Because("the initial opaque surface leaves a visible volume segment.");
        await Assert.That(movedDistance)
            .IsGreaterThan(firstDistance)
            .Because("moving the opaque surface deeper increases the visible field-local segment.");
        await Assert.That(PixelRgbDelta(first, restored, center))
            .IsLessThanOrEqualTo(1)
            .Because("restoring the opaque geometry restores the earlier depth-limited pixel.");
        await Assert.That(frontOpaqueOnly[center + 1])
            .IsGreaterThan((byte)0)
            .Because("the front opaque surface covers the tested pixel.");
        await Assert.That(PixelRgbDelta(frontOccluder, frontOpaqueOnly, center))
            .IsLessThanOrEqualTo(1)
            .Because("opaque depth before the volume entry leaves no volume segment to composite.");

        for (uint revision = 5; revision < 9; revision++)
        {
            byte[] repeated = Render(
                revision,
                (revision & 1) == 1
                    ? occluder
                    : VolumeCommandAuthoring.CreateDepthOccluderMeshCommand(
                        "/World/OpaqueOccluder", z: -0.75f));
            await Assert.That(PixelRgbDelta(
                repeated, (revision & 1) == 1 ? first : moved, center))
                .IsLessThanOrEqualTo(1)
                .Because("reusing a sampled snapshot must publish the new depth copy on every frame.");
        }
    }

    [Test]
    public async Task SingularVolumeTransformIsRefusedAndDisposedRendererRejectsUseOnVulkan()
    {
        ISilkGraphicsDevice device = CreateVulkanDevice();

        using (device)
        using (ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width,
                Height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource)))
        using (ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height)))
        {
            string grid = CreateUniformGrid("singular", 8, 8, 8);
            byte[] material = VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                MaterialPath,
                authoredDensity: 1,
                grid,
                "8,8,8");
            byte[] mesh = VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                MeshPath,
                MaterialPath,
                zOffset: -0.7f);
            using var renderer = new SilkMeshRenderer(device);
            SilkMeshRendererConformance.Apply(
                renderer,
                revision: 1,
                CreateOrthographicFrame(0.1, 2),
                material,
                mesh);
            var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
            _ = renderer.Render(color, depth, options);
            byte[] visiblePixels = new byte[checked((int)(Width * Height * ParityImage.BytesPerPixel))];
            color.ReadbackForTesting(visiblePixels);

            var body = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
            var bindings = new PhysicsRenderBindingTable(1);
            _ = bindings.TryBind(body, MeshPath);
            var overrides = new SilkPhysicsTransformOverrides(1);
            renderer.PhysicsOverrides = overrides;
            PhysicsRenderOverrideView Pose(double x, ulong revision) => new(
                new[] { new PhysicsRenderTransformOverride(
                    body, new UsdVec3d(x, 0, -0.7), PhysicsRenderOrientation.Identity, true) }, revision);
            overrides.Stage(bindings, Pose(double.MaxValue, 1));
            await Assert.That(() => renderer.Render(color, depth, options))
                .Throws<InvalidDataException>();
            using (var invalidOverridePage = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, [], 0))
            {
                await Assert.That(() => renderer.ApplyPage(invalidOverridePage))
                    .Throws<InvalidDataException>();
            }
            byte[] refusedPixels = new byte[visiblePixels.Length];
            color.ReadbackForTesting(refusedPixels);
            await Assert.That(refusedPixels.SequenceEqual(visiblePixels)).IsTrue();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
            overrides.Stage(bindings, Pose(0, 2));
            using (var correctedOverridePage = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, [], 0))
            {
                renderer.ApplyPage(correctedOverridePage);
            }
            _ = renderer.Render(color, depth, options);
            color.ReadbackForTesting(refusedPixels);
            await Assert.That(refusedPixels.SequenceEqual(visiblePixels)).IsTrue();
            renderer.PhysicsOverrides = null;
            _ = renderer.Render(color, depth, options);
            color.ReadbackForTesting(refusedPixels);
            await Assert.That(refusedPixels.SequenceEqual(visiblePixels)).IsTrue();

            BinaryPrimitives.WriteDoubleLittleEndian(
                mesh.AsSpan(80 + (10 * sizeof(double))),
                0);
            await Assert.That(() => SilkMeshRendererConformance.Apply(
                    renderer,
                    revision: 3,
                    CreateOrthographicFrame(0.1, 2),
                    material,
                    mesh))
                .Throws<InvalidDataException>();
            _ = renderer.Render(color, depth, options);
            byte[] singularPixels = new byte[checked((int)(Width * Height * ParityImage.BytesPerPixel))];
            color.ReadbackForTesting(singularPixels);
            BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(80 + 10 * sizeof(double)), 1);
            SilkMeshRendererConformance.Apply(renderer, revision: 4, material, mesh);
            _ = renderer.Render(color, depth, options);
            color.ReadbackForTesting(refusedPixels);
            await Assert.That(refusedPixels.SequenceEqual(visiblePixels)).IsTrue();
            renderer.Dispose();
            await Assert.That(HasNonZeroRgb(visiblePixels)).IsTrue()
                .Because("the control volume must render before testing a singular transform.");
            await Assert.That(singularPixels.SequenceEqual(visiblePixels)).IsTrue()
                .Because("a refused transform must preserve the previously published volume.");
            await Assert.That(() => renderer.Render(color, depth, options))
                .Throws<ObjectDisposedException>();
        }
    }

    private static string CreateUniformGrid(string name, int width, int height, int depth) =>
        VolumeCommandAuthoring.WriteDensityGrid(
            EvidenceDirectory,
            name,
            width,
            height,
            depth,
            static (_, _, _) => 1);

    private static ParityImage Capture(
        Func<ISilkGraphicsDevice> createDevice,
        byte[] materialCommand,
        double[]? view = null,
        params byte[][] meshCommands)
        => CaptureScene(createDevice, view, [materialCommand], meshCommands);

    private static ParityImage CaptureScene(
        Func<ISilkGraphicsDevice> createDevice,
        double[]? view,
        byte[][] materialCommands,
        params byte[][] meshCommands)
        => CaptureSceneWithFrame(
            createDevice,
            CreateOrthographicFrame(0.1, 2, view),
            materialCommands,
            meshCommands);

    private static ParityImage CaptureSceneWithFrame(
        Func<ISilkGraphicsDevice> createDevice,
        byte[] frameCommand,
        byte[][] materialCommands,
        params byte[][] meshCommands)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width,
                Height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        if (meshCommands.Length == 0)
        {
            meshCommands = [VolumeCommandAuthoring.CreateVolumeCameraProxyMeshCommand(
                MeshPath,
                MaterialPath)];
        }

        byte[][] commands =
        [
            frameCommand,
            .. materialCommands,
            .. meshCommands,
        ];
        SilkMeshRendererConformance.Apply(renderer, revision: 1, commands);

        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(color, depth, options);

        byte[] pixels = new byte[checked((int)(Width * Height * ParityImage.BytesPerPixel))];
        color.ReadbackForTesting(pixels);
        return new ParityImage((int)Width, (int)Height, pixels);
    }

    private static byte[] CreatePerspectiveFrame()
    {
        const double near = 0.1;
        const double far = 11;
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width,
            Height,
            SilkMeshRendererConformance.Identity());
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, -(far + near) / (far - near), -1,
            0, 0, -(2 * far * near) / (far - near), 0,
        ];
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                frame.AsSpan(144 + (index * sizeof(double))),
                projection[index]);
        }
        return frame;
    }

    private static byte[] CreateReverseOrthographicFrame()
    {
        double[] view = SilkMeshRendererConformance.Identity();
        view[10] = -1;
        view[14] = -1.7;
        return CreateOrthographicFrame(0.1, 2, view);
    }

    private static byte[] CreateOrthographicFrame(
        double near, double far, double[]? view = null)
    {
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width,
            Height,
            view ?? SilkMeshRendererConformance.Identity());
        double[] projection =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, -2 / (far - near), 0,
            0, 0, -(far + near) / (far - near), 1,
        ];
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                frame.AsSpan(144 + (index * sizeof(double))),
                projection[index]);
        }
        return frame;
    }

    private static int PixelOffset(int x, int y) =>
        checked(((y * (int)Width) + x) * ParityImage.BytesPerPixel);

    private static byte PixelChannel(ParityImage image, int pixelOffset, int channel) =>
        image.Rgba.Span[pixelOffset + channel];

    private static int PixelRgbDelta(ParityImage left, ParityImage right, int pixelOffset) =>
        Math.Abs(PixelChannel(left, pixelOffset, 0) - PixelChannel(right, pixelOffset, 0)) +
        Math.Abs(PixelChannel(left, pixelOffset, 1) - PixelChannel(right, pixelOffset, 1)) +
        Math.Abs(PixelChannel(left, pixelOffset, 2) - PixelChannel(right, pixelOffset, 2));

    private static int PixelRgbDelta(byte[] left, byte[] right, int pixelOffset) =>
        Math.Abs(left[pixelOffset] - right[pixelOffset]) +
        Math.Abs(left[pixelOffset + 1] - right[pixelOffset + 1]) +
        Math.Abs(left[pixelOffset + 2] - right[pixelOffset + 2]);

    private static bool HasNonZeroRgb(ReadOnlySpan<byte> pixels)
    {
        for (int offset = 0; offset < pixels.Length; offset += ParityImage.BytesPerPixel)
        {
            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static ISilkGraphicsDevice CreateVulkanDevice()
    {
        if (GateBackend.Value is { } backend)
        {
            ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            if (OperatingSystem.IsWindows() &&
                ((backend == SilkGraphicsBackend.Vulkan &&
                  (!device.Capabilities.IsSoftware ||
                   !device.Capabilities.DeviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase))) ||
                 (backend == SilkGraphicsBackend.D3D12 && !device.Capabilities.IsSoftware)))
            {
                string deviceName = device.Capabilities.DeviceName;
                device.Dispose();
                throw new InvalidOperationException(
                    $"Camera gate requires Windows WARP/SwiftShader, selected '{deviceName}' for {backend}.");
            }
            return device;
        }
        try
        {
            return VulkanSilkGraphicsDevice.Create();
        }
        catch (Exception exception) when (IsVulkanUnavailable(exception))
        {
            Skip.Test($"No Vulkan device is available on this host: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
    }

    private static bool IsVulkanUnavailable(Exception exception) =>
        exception is DllNotFoundException or
            EntryPointNotFoundException or
            PlatformNotSupportedException or
            InvalidOperationException;

    private static string EvidenceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestResults", "volumes");

    private const string MaterialPath = "/World/VolumeCameraMaterial";
    private const string MaterialPathA = "/World/VolumeCameraMaterialA";
    private const string MaterialPathB = "/World/VolumeCameraMaterialB";
    private const string MeshPath = "/World/VolumeCameraProxy";
}
