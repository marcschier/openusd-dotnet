// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerSwitchingEvidenceTests
{
    [Test]
    public async Task ExplicitAngleShellSelectionOverridesInheritedPlatformChoice()
    {
        await Assert.That(Program.GetWindowsRenderingModes(false, "angle"))
            .IsEquivalentTo([Avalonia.Win32RenderingMode.AngleEgl]);
        await Assert.That(Program.GetWindowsRenderingModes(false, "wgl"))
            .IsEquivalentTo([Avalonia.Win32RenderingMode.Wgl]);
    }

    [Test]
    public async Task CompleteMeasuredEvidenceIsAccepted()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();

        artifact.Validate();

        await Assert.That(artifact.Pixels.Length).IsEqualTo(3);
        await Assert.That(artifact.States[0].ExactReferencePreserved).IsTrue();
        await Assert.That(artifact.CameraTransitions.Length).IsEqualTo(1);
    }

    [Test]
    public async Task SourceGeneratedJsonContainsCanonicalCameraSchema()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();

        string json = JsonSerializer.Serialize(
            artifact,
            ViewerSwitchingEvidenceJsonContext.Default.ViewerSwitchingEvidenceArtifact);
        ViewerSwitchingEvidenceArtifact roundTrip =
            JsonSerializer.Deserialize(
                json,
                ViewerSwitchingEvidenceJsonContext.Default.ViewerSwitchingEvidenceArtifact)
            ?? throw new InvalidDataException("Round-tripped evidence was empty.");

        roundTrip.Validate();
        await Assert.That(json).Contains("\"CameraMode\"");
        await Assert.That(json).Contains("\"CameraPayload\"");
        await Assert.That(json).Contains("\"CameraTransitions\"");
        await Assert.That(json).Contains("\"NativeNavigation\"");
        await Assert.That(json).DoesNotContain("\"Camera\":");
    }

    [Test]
    public async Task Schema7EvidenceIsRejected()
    {
        ViewerSwitchingEvidenceArtifact current = CreateValidArtifact();
        ViewerSwitchingEvidenceArtifact artifact = current with { SchemaVersion = 7 };

        await Assert.That(current.SchemaVersion).IsEqualTo(8);
        await Assert.That(() => artifact.Validate())
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task MissingMeasuredSectionsAreRejected()
    {
        ViewerSwitchingEvidenceArtifact artifact =
            CreateValidArtifact() with { Inputs = [] };

        await Assert.That(() => artifact.Validate())
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task RetiredKindQuarantineRequiresMeasuredFactoryAttachAndHwndEvidence()
    {
        ViewerSwitchingEvidenceArtifact baseline = CreateValidArtifact();
        var blockedOwnership = new ViewerHwndEvidence(
            "D3D12",
            "quarantine-blocked",
            "0x100",
            TopLevelProcessId: 10,
            TopLevelThreadId: 20,
            ExpectedStormHwnd: string.Empty,
            ObservedStormHwnd: "0x200",
            StormClassName: "OpenUsdStormNativeChild",
            StormIsWindow: true,
            StormIsVisible: false,
            StormParentHwnd: "0x101",
            StormParentWithinViewer: true,
            StormProcessId: 10,
            StormThreadId: 20,
            EnumeratedStormCount: 1,
            VisibleStormCount: 0,
            LiveKnownStormCount: 1,
            StaleLiveStormCount: 0,
            CompositionHostVisible: true);
        var quarantine = new ViewerCleanupQuarantineEvidence(
            "Storm",
            RetiredBefore: 0,
            RetiredWhileBlocked: 1,
            RetiredAfterRecovery: 0,
            CandidateBefore: 1,
            CandidateAfterManual: 1,
            CandidateAfterAutomatic: 1,
            CandidateAfterRecovery: 2,
            FactoryBefore: 1,
            FactoryAfterManual: 1,
            FactoryAfterAutomatic: 1,
            FactoryAfterRecovery: 2,
            AttachBefore: 1,
            AttachAfterManual: 1,
            AttachAfterAutomatic: 1,
            AttachAfterRecovery: 2,
            ChildLiveBefore: 1,
            ChildPeakBefore: 1,
            ChildLiveWhileBlocked: 1,
            ChildPeakWhileBlocked: 1,
            ChildLiveAfterRecovery: 1,
            ChildPeakAfterRecovery: 1,
            ManualFailure: "CleanupPending",
            ManualDiagnosticCode: "manager.backend_cleanup_pending",
            AutomaticSucceeded: true,
            AutomaticSkippedQuarantinedKind: true,
            CleanupRecovered: true,
            ReactivatedAfterRecovery: true,
            blockedOwnership);
        ViewerSwitchingEvidenceArtifact artifact = baseline with
        {
            Scenario = "renderer-retired-kind-quarantine",
            CleanupQuarantine = quarantine
        };

        artifact.Validate();
        await Assert.That(() => (artifact with
        {
            CleanupQuarantine = quarantine with { FactoryAfterManual = 2 }
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            CleanupQuarantine = quarantine with
            {
                BlockedWindowOwnership = blockedOwnership with
                {
                    VisibleStormCount = 1,
                    StormIsVisible = true
                }
            }
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ConfiguredAngleStringWithoutRuntimeImportIsRejected()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact() with
        {
            RuntimeCompositor = "AngleEgl",
            Compositions = []
        };

        await Assert.That(() => artifact.Validate())
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task SynthesizedInputAndControlCountOwnershipAreRejected()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();

        await Assert.That(() => (artifact with
        {
            Inputs = [artifact.Inputs[0] with { Synthesized = true }]
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            WindowOwnership = []
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task StalePixelChecksumIsRejected()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();
        artifact = artifact with
        {
            Pixels =
            [
                artifact.Pixels[0],
                artifact.Pixels[1] with { Sha256 = artifact.Pixels[0].Sha256 }
            ]
        };

        await Assert.That(() => artifact.Validate())
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AbandonedStormIsAllowedOnlyForRecordedContextLoss()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();
        ViewerResourceEvidence abandoned =
            artifact.Resources with { AbandonedStorm = 1 };

        await Assert.That(() => (artifact with { Resources = abandoned }).Validate())
            .Throws<InvalidDataException>();

        (artifact with
        {
            Resources = abandoned with { ContextLossSimulated = true }
        }).Validate();
    }

    [Test]
    public async Task StormEvidenceRequiresAbiV6NativeFramebufferCapture()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidStormArtifact();

        artifact.Validate();

        ViewerSwitchingEvidenceArtifact abi4NegativeFixture = artifact with
        {
            StormChildAbiVersion = 4
        };
        await Assert.That(() => abi4NegativeFixture.Validate())
            .Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            Pixels =
            [
                artifact.Pixels[0] with { CaptureApi = "BitBltDesktop" },
                .. artifact.Pixels[1..]
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task WindowsStormEvidenceRequiresBoundNativeNavigationProof()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidStormArtifact();

        artifact.Validate();

        await Assert.That(artifact.NativeNavigation).IsNotNull();
        await Assert.That(artifact.NativeNavigation!.Length).IsEqualTo(1);
        await Assert.That(() => (artifact with
        {
            NativeNavigation = []
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            NativeNavigation =
            [
                artifact.NativeNavigation[0] with { AvaloniaRoutedEvents = 1 }
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task LinuxStormEvidenceRequiresMappedShellPixelsAndNativeInputDeltas()
    {
        ViewerSwitchingEvidenceArtifact baseline = CreateValidStormArtifact();
        ViewerInputEvidence nativeInput = baseline.Inputs[0] with
        {
            Backend = "Storm",
            DeliveryApi = "XTest+AvaloniaRoutedHandlers+NativeDiagnostics",
            FocusEvents = 1,
            PointerMoves = 5,
            PointerButtons = 5,
            WheelEvents = 1,
            KeyEvents = 2,
            NativeFocusEvents = 1,
            NativePointerEvents = 5,
            NativeWheelEvents = 1,
            NativeKeyEvents = 2,
            PhysicalWidthObserved = 672,
            PhysicalHeightObserved = 504,
            Win32Messages = [],
            XTestInjections =
            [
                CreateXTestInjection("ViewerTopLevel", nativeObserved: false),
                CreateXTestInjection("StormChild", nativeObserved: true)
            ]
        };
        ViewerSwitchingEvidenceArtifact artifact = baseline with
        {
            RuntimeCompositor = "X11 / compositor-managed XWayland",
            PlatformHandle = "XID",
            Pixels =
            [
                .. baseline.Pixels,
                CreatePixel("Storm", "camera-Storm-automatic-before-shell", 'E') with
                {
                    CaptureApi = "XGetImage(ZPixmap,viewer-shell)"
                }
            ],
            Inputs = [nativeInput],
            Compositions = [],
            WindowOwnership = [],
            NativeNavigation = []
        };

        artifact.Validate();

        await Assert.That(artifact.SchemaVersion).IsEqualTo(8);
        await Assert.That(nativeInput.XTestInjections).IsNotNull();
        await Assert.That(nativeInput.XTestInjections!.Length).IsEqualTo(2);
        await Assert.That(() => (artifact with
        {
            Inputs =
            [
                nativeInput with
                {
                    FocusEvents = 0,
                    PointerMoves = 0,
                    PointerButtons = 0,
                    WheelEvents = 0,
                    KeyEvents = 0,
                    NativeFocusEvents = 0,
                    NativePointerEvents = 0,
                    NativeWheelEvents = 0,
                    NativeKeyEvents = 0,
                    Win32Messages = []
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            Inputs = [nativeInput with { XTestInjections = null }]
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            Inputs =
            [
                nativeInput with
                {
                    DeliveryApi = "XSendEvent+AvaloniaRoutedHandlers+NativeDiagnostics"
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task FakeXTestApiRecordsServerRoutedCallProvenance()
    {
        var api = new FakeXTestApi();

        ViewerXTestInjectionEvidence evidence =
            RendererSwitchingViewport.InjectXTestEvidence(
                api,
                (nint)0x1234,
                8,
                9,
                "ViewerTopLevel",
                ":fake");

        await Assert.That(evidence.InjectionApi).IsEqualTo("XTest");
        await Assert.That(evidence.ExtensionAvailable).IsTrue();
        await Assert.That(evidence.ExtensionMajor).IsEqualTo(2);
        await Assert.That(evidence.ExtensionMinor).IsEqualTo(2);
        await Assert.That(evidence.Display).IsEqualTo(":fake");
        await Assert.That(evidence.Xid).IsEqualTo("0x1234");
        await Assert.That(evidence.ServerGenerated).IsTrue();
        await Assert.That(evidence.NativeSendEventFalseObserved).IsFalse();
        await Assert.That(evidence.Calls.Any(call =>
            call.Api == "XTestFakeMotionEvent" && call.Result == 1)).IsTrue();
        await Assert.That(evidence.Calls.Count(call =>
            call.Api.StartsWith("XTestFakeButtonEvent", StringComparison.Ordinal)))
            .IsEqualTo(6);
        await Assert.That(api.Closed).IsTrue();
    }

    [Test]
    public async Task MissingXTestExtensionProducesTypedUnavailableFailure()
    {
        var api = new FakeXTestApi { QueryResult = 0 };

        await Assert.That(() =>
            RendererSwitchingViewport.InjectXTestEvidence(
                api,
                (nint)0x1234,
                8,
                9,
                "ViewerTopLevel",
                ":fake")).Throws<XTestUnavailableException>();
        await Assert.That(api.Closed).IsTrue();
    }

    [Test]
    public async Task ManualRawInputAndFailedWin32RoutingAreRejected()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();
        ViewerInputEvidence input = artifact.Inputs[0];

        await Assert.That(() => (artifact with
        {
            Inputs =
            [
                input with
                {
                    DeliveryApi = "AvaloniaRawInputPipeline+SendMessageTimeoutW"
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            Inputs =
            [
                input with
                {
                    Win32Messages =
                    [
                        input.Win32Messages[0] with
                        {
                            ApiSucceeded = false,
                            Synthesized = true
                        },
                        .. input.Win32Messages[1..]
                    ]
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task DpiMustChangeAndRestoreToOriginal()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();
        ViewerInputEvidence input = artifact.Inputs[0];

        await Assert.That(() => (artifact with
        {
            Inputs = [input with { NativeDpiObserved = input.NativeDpiBefore }]
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            Inputs = [input with { NativeDpiAfter = input.NativeDpiObserved }]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task CameraStateUsesCanonicalPayloadAndRejectsTampering()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();
        ViewerStateEvidence explicitState = artifact.States[1];
        byte[] payload = Convert.FromBase64String(explicitState.CameraPayload);

        await Assert.That(typeof(ViewerStateEvidence).GetProperty("Camera")).IsNull();
        await Assert.That(explicitState.CameraMode).IsEqualTo(nameof(CameraMode.Matrices));
        await Assert.That(payload.Length).IsEqualTo(ViewerCameraEvidence.PayloadSize);
        await Assert.That(explicitState.CameraSignature.Length).IsEqualTo(64);
        await Assert.That(explicitState.NativeCameraSignature.Length).IsEqualTo(16);
        await Assert.That(() => (artifact with
        {
            States =
            [
                artifact.States[0],
                explicitState with { CameraSignature = new string('0', 64) },
                artifact.States[2]
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ExplicitCameraTransitionRequiresDistinctBoundPixelsAndRestoration()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidArtifact();
        ViewerCameraTransitionEvidence transition = artifact.CameraTransitions[0];

        await Assert.That(() => (artifact with
        {
            CameraTransitions =
            [
                transition with
                {
                    ExplicitPixelSha256 = transition.AutomaticPixelSha256,
                    ExplicitPixelArtifact = transition.AutomaticPixelArtifact
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            States =
            [
                artifact.States[0],
                artifact.States[1],
                artifact.States[2] with
                {
                    CameraMode = artifact.States[1].CameraMode,
                    CameraPayload = artifact.States[1].CameraPayload,
                    CameraSignature = artifact.States[1].CameraSignature,
                    NativeCameraSignature = artifact.States[1].NativeCameraSignature
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task StormCameraTransitionRequiresLatestRequestedAndRenderedSignatures()
    {
        ViewerSwitchingEvidenceArtifact artifact = CreateValidStormArtifact();

        artifact.Validate();

        ViewerCameraTransitionEvidence transition = artifact.CameraTransitions[0];
        await Assert.That(transition.AsyncCoalescingValidated).IsTrue();
        await Assert.That(() => (artifact with
        {
            CameraTransitions =
            [
                transition with
                {
                    LatestRequestedCameraSignature = new string('0', 16)
                }
            ]
        }).Validate()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task StageCameraEvidenceRequiresPathHashesPixelsAndFreshState()
    {
        ViewerSwitchingEvidenceArtifact artifact =
            CreateValidStageCameraArtifact();

        artifact.Validate();

        ViewerStageCameraEvidence stageCamera = artifact.StageCamera ??
            throw new InvalidOperationException("Stage-camera fixture was missing.");
        await Assert.That(stageCamera.InitialFrames.Length).IsEqualTo(3);
        await Assert.That(stageCamera.SampledFrames.Length).IsEqualTo(3);
        await Assert.That(() => (artifact with
        {
            StageCamera = stageCamera with { CameraPath = string.Empty }
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            StageCamera = stageCamera with { StageSha256 = string.Empty }
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            StageCamera = stageCamera with
            {
                SampledFrames =
                [
                    stageCamera.SampledFrames[0] with
                    {
                        PixelSha256 = stageCamera.InitialFrames[2].PixelSha256,
                        PixelArtifact = stageCamera.InitialFrames[2].PixelArtifact
                    },
                    .. stageCamera.SampledFrames[1..]
                ]
            }
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with
        {
            StageCamera = stageCamera with
            {
                SampledFrames =
                [
                    stageCamera.SampledFrames[0] with
                    {
                        StateRevision = stageCamera.InitialFrames[0].StateRevision
                    },
                    .. stageCamera.SampledFrames[1..]
                ]
            }
        }).Validate()).Throws<InvalidDataException>();
        await Assert.That(() => (artifact with { StageCamera = null }).Validate())
            .Throws<InvalidDataException>();
    }

    private static ViewerSwitchingEvidenceArtifact CreateValidStageCameraArtifact()
    {
        const string stageIdentifier = "viewer-stage-camera-smoke.usda";
        const string cameraPath = "/World/CameraRig/Offset/ShotCamera";
        ViewerCameraDescriptor automatic =
            ViewerCameraEvidence.Describe(CameraState.Default);
        ViewerCameraDescriptor initialCamera = ViewerCameraEvidence.Describe(
            new CameraState(
                Matrix4x4.CreateTranslation(0f, 0f, -12f),
                Matrix4x4.CreatePerspectiveFieldOfView(
                    0.8f,
                    4f / 3f,
                    0.1f,
                    100f)));
        ViewerCameraDescriptor sampledCamera = ViewerCameraEvidence.Describe(
            new CameraState(
                Matrix4x4.CreateTranslation(-1.25f, -0.5f, -8f),
                Matrix4x4.CreatePerspectiveFieldOfView(
                    1.1f,
                    4f / 3f,
                    0.1f,
                    100f)));

        static ViewerStateEvidence state(
            string backend,
            string phase,
            ulong revision,
            double timeCode,
            ViewerCameraDescriptor camera) =>
            new(
                backend,
                phase,
                revision,
                stageIdentifier,
                timeCode,
                camera.Mode,
                camera.Payload,
                camera.Signature,
                camera.NativeSignature,
                [cameraPath],
                "Default",
                "RespectAuthored",
                "SmoothShaded",
                640,
                480,
                ExactReferencePreserved: true,
                SchedulerIdentity: 101,
                RenderSourceIdentity: 202);

        ViewerStateEvidence automaticBefore = state(
            "Storm",
            "stage-camera-automatic-before",
            1,
            0,
            automatic);
        ViewerStateEvidence initialStorm = state(
            "Storm",
            "stage-camera-initial-Storm",
            2,
            0,
            initialCamera);
        ViewerStateEvidence initialD3D = state(
            "D3D12",
            "stage-camera-initial-D3D12",
            2,
            0,
            initialCamera);
        ViewerStateEvidence initialVulkan = state(
            "Vulkan",
            "stage-camera-initial-Vulkan",
            2,
            0,
            initialCamera);
        ViewerStateEvidence sampledVulkan = state(
            "Vulkan",
            "stage-camera-sampled-Vulkan",
            3,
            24,
            sampledCamera);
        ViewerStateEvidence sampledD3D = state(
            "D3D12",
            "stage-camera-sampled-D3D12",
            3,
            24,
            sampledCamera);
        ViewerStateEvidence sampledStorm = state(
            "Storm",
            "stage-camera-sampled-Storm",
            3,
            24,
            sampledCamera);
        ViewerStateEvidence automaticRestored = state(
            "Storm",
            "stage-camera-automatic-restored",
            4,
            0,
            automatic);

        ViewerPixelEvidence automaticBeforePixel =
            CreatePixel("Storm", automaticBefore.Phase, 'A');
        ViewerPixelEvidence initialStormPixel =
            CreatePixel("Storm", initialStorm.Phase, 'B');
        ViewerPixelEvidence initialD3DPixel =
            CreatePixel("D3D12", initialD3D.Phase, 'C');
        ViewerPixelEvidence initialVulkanPixel =
            CreatePixel("Vulkan", initialVulkan.Phase, 'D');
        ViewerPixelEvidence sampledVulkanPixel =
            CreatePixel("Vulkan", sampledVulkan.Phase, 'E');
        ViewerPixelEvidence sampledD3DPixel =
            CreatePixel("D3D12", sampledD3D.Phase, 'F');
        ViewerPixelEvidence sampledStormPixel =
            CreatePixel("Storm", sampledStorm.Phase, 'C');
        ViewerPixelEvidence automaticRestoredPixel =
            CreatePixel("Storm", automaticRestored.Phase, 'A');

        ViewerSwitchingEvidenceArtifact d3d = RebindBackendArtifact(
            CreateValidArtifact(),
            "D3D12",
            revisionStart: 10,
            ['B', 'C', 'D']);
        ViewerSwitchingEvidenceArtifact vulkan = RebindBackendArtifact(
            CreateValidArtifact(),
            "Vulkan",
            revisionStart: 20,
            ['E', 'F', 'A']);
        ViewerSwitchingEvidenceArtifact storm = RebindBackendArtifact(
            CreateValidStormArtifact(),
            "Storm",
            revisionStart: 30,
            ['B', 'C', 'D', 'E']);

        var stageCamera = new ViewerStageCameraEvidence(
            ViewerStageCameraSmokeContract.SourceName,
            stageIdentifier,
            new string('1', 64),
            cameraPath,
            InitialTimeCode: 0,
            SampledTimeCode: 24,
            InitialSnapshotSha256: new string('2', 64),
            SampledSnapshotSha256: new string('3', 64),
            new ViewerStageCameraAutomaticEvidence(
                "Storm",
                automaticBefore.Phase,
                0,
                automaticBefore.Revision,
                automaticBefore.CameraSignature,
                automaticBefore.NativeCameraSignature,
                automaticBeforePixel.Sha256,
                automaticBeforePixel.Artifact),
            [
                CreateStageFrame(initialStorm, initialStormPixel, storm: true),
                CreateStageFrame(initialD3D, initialD3DPixel, storm: false),
                CreateStageFrame(initialVulkan, initialVulkanPixel, storm: false)
            ],
            [
                CreateStageFrame(sampledVulkan, sampledVulkanPixel, storm: false),
                CreateStageFrame(sampledD3D, sampledD3DPixel, storm: false),
                CreateStageFrame(sampledStorm, sampledStormPixel, storm: true)
            ],
            new ViewerStageCameraAutomaticEvidence(
                "Storm",
                automaticRestored.Phase,
                0,
                automaticRestored.Revision,
                automaticRestored.CameraSignature,
                automaticRestored.NativeCameraSignature,
                automaticRestoredPixel.Sha256,
                automaticRestoredPixel.Artifact),
            ExactStatePreservedAcrossBackends: true);

        return d3d with
        {
            Scenario = ViewerStageCameraSmokeContract.ScenarioName,
            States =
            [
                automaticBefore,
                initialStorm,
                initialD3D,
                initialVulkan,
                sampledVulkan,
                sampledD3D,
                sampledStorm,
                automaticRestored,
                .. d3d.States,
                .. vulkan.States,
                .. storm.States
            ],
            Pixels =
            [
                automaticBeforePixel,
                initialStormPixel,
                initialD3DPixel,
                initialVulkanPixel,
                sampledVulkanPixel,
                sampledD3DPixel,
                sampledStormPixel,
                automaticRestoredPixel,
                .. d3d.Pixels,
                .. vulkan.Pixels,
                .. storm.Pixels
            ],
            Inputs = [.. d3d.Inputs, .. vulkan.Inputs, .. storm.Inputs],
            Compositions = [.. d3d.Compositions, .. vulkan.Compositions],
            WindowOwnership =
            [
                .. d3d.WindowOwnership,
                .. vulkan.WindowOwnership,
                .. storm.WindowOwnership
            ],
            CameraTransitions =
            [
                .. d3d.CameraTransitions,
                .. vulkan.CameraTransitions,
                .. storm.CameraTransitions
            ],
            NativeNavigation = storm.NativeNavigation,
            StageCamera = stageCamera
        };
    }

    private static ViewerStageCameraBackendFrameEvidence CreateStageFrame(
        ViewerStateEvidence state,
        ViewerPixelEvidence pixel,
        bool storm) =>
        new(
            state.Backend,
            state.Phase,
            state.TimeCode,
            state.Revision,
            state.CameraSignature,
            state.NativeCameraSignature,
            pixel.Sha256,
            pixel.Artifact,
            ExactReferencePreserved: true,
            LatestRequestedRevision: storm ? state.Revision : 0,
            LatestRequestedCameraSignature:
                storm ? state.NativeCameraSignature : string.Empty,
            LatestRenderedCameraSignature:
                storm ? state.NativeCameraSignature : string.Empty);

    private static ViewerSwitchingEvidenceArtifact RebindBackendArtifact(
        ViewerSwitchingEvidenceArtifact source,
        string backend,
        ulong revisionStart,
        char[] pixelHashes)
    {
        const string stageIdentifier = "viewer-stage-camera-smoke.usda";
        const string cameraPath = "/World/CameraRig/Offset/ShotCamera";
        ViewerStateEvidence[] states = source.States
            .Select((state, index) => state with
            {
                Backend = backend,
                Phase = $"stage-camera-contract-{backend}-{index:D2}",
                Revision = revisionStart + (ulong)index,
                StageIdentifier = stageIdentifier,
                TimeCode = 0,
                Selection = [cameraPath]
            })
            .ToArray();
        ViewerPixelEvidence[] pixels = source.Pixels
            .Select((pixel, index) => CreatePixel(
                backend,
                states[index].Phase,
                pixelHashes[index]))
            .ToArray();
        bool storm = string.Equals(backend, "Storm", StringComparison.Ordinal);
        ViewerCameraTransitionEvidence transition =
            source.CameraTransitions[0] with
            {
                Backend = backend,
                AutomaticBeforePhase = states[0].Phase,
                ExplicitPhase = states[1].Phase,
                AutomaticRestoredPhase = states[2].Phase,
                AutomaticCameraSignature = states[0].CameraSignature,
                ExplicitCameraSignature = states[1].CameraSignature,
                RestoredCameraSignature = states[2].CameraSignature,
                AutomaticPixelSha256 = pixels[0].Sha256,
                ExplicitPixelSha256 = pixels[1].Sha256,
                RestoredPixelSha256 = pixels[2].Sha256,
                AutomaticPixelArtifact = pixels[0].Artifact,
                ExplicitPixelArtifact = pixels[1].Artifact,
                RestoredPixelArtifact = pixels[2].Artifact,
                LatestRequestedRevision = storm ? states[1].Revision : 0,
                LatestRequestedCameraSignature =
                    storm ? states[1].NativeCameraSignature : string.Empty,
                LatestRenderedCameraSignature =
                    storm ? states[1].NativeCameraSignature : string.Empty,
                AsyncCoalescingValidated = storm
            };
        ViewerNativeNavigationEvidence[] navigation = storm
            ? source.NativeNavigation!
                .Select(value => value with
                {
                    Phase = states[3].Phase,
                    CameraBeforeSignature = states[2].CameraSignature,
                    CameraAfterSignature = states[3].CameraSignature,
                    PixelBeforeSha256 = pixels[2].Sha256,
                    PixelAfterSha256 = pixels[3].Sha256,
                    PixelBeforeArtifact = pixels[2].Artifact,
                    PixelAfterArtifact = pixels[3].Artifact
                })
                .ToArray()
            : [];
        ViewerCompositionEvidence[] compositions = storm
            ? []
            :
            [
                source.Compositions[0] with { Backend = backend }
            ];
        return source with
        {
            States = states,
            Pixels = pixels,
            Inputs = [source.Inputs[0] with { Backend = backend }],
            Compositions = compositions,
            WindowOwnership =
            [
                source.WindowOwnership[0] with
                {
                    Backend = backend,
                    Phase = $"stage-camera-contract-{backend}-ownership"
                }
            ],
            CameraTransitions = [transition],
            NativeNavigation = navigation
        };
    }

    private static ViewerSwitchingEvidenceArtifact CreateValidArtifact()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddSeconds(-1);
        ViewerCameraDescriptor automatic =
            ViewerCameraEvidence.Describe(CameraState.Default);
        ViewerCameraDescriptor explicitCamera =
            ViewerCameraEvidence.Describe(
                ViewerCameraEvidence.CreateDeterministicExplicitCamera());
        var automaticBefore = new ViewerStateEvidence(
            "D3D12",
            "camera-D3D12-automatic-before",
            7,
            "stage.usda",
            1,
            automatic.Mode,
            automatic.Payload,
            automatic.Signature,
            automatic.NativeSignature,
            ["/World/Cube"],
            "Default",
            "RespectAuthored",
            "SmoothShaded",
            640,
            480,
            ExactReferencePreserved: true,
            SchedulerIdentity: 101,
            RenderSourceIdentity: 202);
        ViewerStateEvidence explicitState = automaticBefore with
        {
            Phase = "camera-D3D12-explicit",
            Revision = 8,
            CameraMode = explicitCamera.Mode,
            CameraPayload = explicitCamera.Payload,
            CameraSignature = explicitCamera.Signature,
            NativeCameraSignature = explicitCamera.NativeSignature
        };
        ViewerStateEvidence restored = automaticBefore with
        {
            Phase = "camera-D3D12-automatic-restored",
            Revision = 9
        };
        ViewerPixelEvidence automaticPixel =
            CreatePixel("D3D12", automaticBefore.Phase, 'A');
        ViewerPixelEvidence explicitPixel =
            CreatePixel("D3D12", explicitState.Phase, 'B');
        ViewerPixelEvidence restoredPixel =
            CreatePixel("D3D12", restored.Phase, 'C');
        var input = new ViewerInputEvidence(
            "D3D12",
            "EnableMouseInPointer(false,success=True,error=0)+" +
            "SendMessageTimeoutW+Win32WndProc+DiagnosticWM_DPICHANGED+" +
            "AvaloniaRoutedHandlers+NativeDiagnostics",
            Synthesized: false,
            ResizeEvents: 2,
            ScalingEvents: 2,
            FocusEvents: 1,
            PointerMoves: 1,
            PointerButtons: 2,
            WheelEvents: 1,
            KeyEvents: 2,
            NativeFocusEvents: 0,
            NativePointerEvents: 0,
            NativeWheelEvents: 0,
            NativeKeyEvents: 0,
            RenderScalingBefore: 1,
            RenderScalingObserved: 1.25,
            RenderScalingAfter: 1,
            NativeDpiBefore: 96,
            NativeDpiObserved: 120,
            NativeDpiAfter: 96,
            PhysicalWidthBefore: 640,
            PhysicalHeightBefore: 480,
            PhysicalWidthObserved: 800,
            PhysicalHeightObserved: 600,
            PhysicalWidthAfter: 640,
            PhysicalHeightAfter: 480,
            Win32Messages: CreateWin32Messages());
        return new ViewerSwitchingEvidenceArtifact(
            ViewerSwitchingEvidenceArtifact.CurrentSchemaVersion,
            "test",
            "ANGLE/D3D11 (runtime-observed)",
            "HWND",
            ViewerSwitchingEvidenceArtifact.RequiredStormChildAbiVersion,
            started,
            DateTimeOffset.UtcNow,
            ["OpenUsd.Viewer.dll"],
            [automaticBefore, explicitState, restored],
            [automaticPixel, explicitPixel, restoredPixel],
            [input],
            [
                new ViewerCompositionEvidence(
                    "D3D12",
                    ["D3D11TextureNtHandle"],
                    [],
                    "0102030405060708",
                    string.Empty,
                    "D3D11TextureNtHandle",
                    [],
                    "KeyedMutex",
                    SuccessfulImports: 1,
                    SuccessfulPresents: 1,
                    CompositionHostVisible: true)
            ],
            [
                new ViewerHwndEvidence(
                    "D3D12",
                    "initial-after",
                    "0x100",
                    TopLevelProcessId: 10,
                    TopLevelThreadId: 20,
                    ExpectedStormHwnd: string.Empty,
                    ObservedStormHwnd: string.Empty,
                    StormClassName: string.Empty,
                    StormIsWindow: false,
                    StormIsVisible: false,
                    StormParentHwnd: string.Empty,
                    StormParentWithinViewer: false,
                    StormProcessId: 0,
                    StormThreadId: 0,
                    EnumeratedStormCount: 0,
                    VisibleStormCount: 0,
                    LiveKnownStormCount: 0,
                    StaleLiveStormCount: 0,
                    CompositionHostVisible: true)
            ],
            [
                new ViewerCameraTransitionEvidence(
                    "D3D12",
                    automaticBefore.Phase,
                    explicitState.Phase,
                    restored.Phase,
                    automaticBefore.CameraSignature,
                    explicitState.CameraSignature,
                    restored.CameraSignature,
                    automaticPixel.Sha256,
                    explicitPixel.Sha256,
                    restoredPixel.Sha256,
                    automaticPixel.Artifact,
                    explicitPixel.Artifact,
                    restoredPixel.Artifact,
                    ExactReferencesPreserved: true,
                    LatestRequestedRevision: 0,
                    LatestRequestedCameraSignature: string.Empty,
                    LatestRenderedCameraSignature: string.Empty,
                    AsyncCoalescingValidated: false)
            ],
            new ViewerResourceEvidence(
                ChildLive: 0,
                ChildPeak: 1,
                ManagedStorm: 0,
                NativeStorm: 0,
                ManagedSilk: 0,
                NativeSilk: 0,
                ManagedPages: 0,
                NativePages: 0,
                GpuScenes: 0,
                GpuMeshes: 0,
                AbandonedStorm: 0,
                ContextLossSimulated: false));
    }

    private static ViewerSwitchingEvidenceArtifact CreateValidStormArtifact()
    {
        ViewerSwitchingEvidenceArtifact baseline = CreateValidArtifact();
        ViewerStateEvidence[] states =
        [
            .. baseline.States.Select(state => state with { Backend = "Storm" })
        ];
        ViewerPixelEvidence[] pixels =
        [
            .. baseline.Pixels.Select(pixel => pixel with
            {
                Backend = "Storm",
                CaptureApi =
                    ViewerSwitchingEvidenceArtifact.StormCaptureApi
            })
        ];
        ViewerStateEvidence navigationState = states[1] with
        {
            Phase = "camera-Storm-native-navigation-after",
            Revision = 10
        };
        ViewerPixelEvidence navigationPixel = CreatePixel(
            "Storm",
            navigationState.Phase,
            'D');
        states = [.. states, navigationState];
        pixels = [.. pixels, navigationPixel];
        ViewerCameraTransitionEvidence transition = baseline.CameraTransitions[0] with
        {
            Backend = "Storm",
            LatestRequestedRevision = states[1].Revision,
            LatestRequestedCameraSignature = states[1].NativeCameraSignature,
            LatestRenderedCameraSignature = states[1].NativeCameraSignature,
            AsyncCoalescingValidated = true
        };
        var ownership = new ViewerHwndEvidence(
            "Storm",
            "camera-Storm-automatic-restored-after",
            "0x100",
            TopLevelProcessId: 10,
            TopLevelThreadId: 20,
            ExpectedStormHwnd: "0x200",
            ObservedStormHwnd: "0x200",
            StormClassName: "OpenUsdStormNativeChild",
            StormIsWindow: true,
            StormIsVisible: true,
            StormParentHwnd: "0x100",
            StormParentWithinViewer: true,
            StormProcessId: 10,
            StormThreadId: 20,
            EnumeratedStormCount: 1,
            VisibleStormCount: 1,
            LiveKnownStormCount: 1,
            StaleLiveStormCount: 0,
            CompositionHostVisible: false);
        ViewerInputEvidence stormInput = baseline.Inputs[0] with
        {
            Backend = "Storm",
            NativeFocusEvents = 1,
            NativePointerEvents = 3,
            NativeWheelEvents = 1,
            NativeKeyEvents = 2,
            Win32Messages =
            [
                .. baseline.Inputs[0].Win32Messages,
                .. CreateStormChildInputMessages()
            ]
        };
        return baseline with
        {
            States = states,
            Pixels = pixels,
            Inputs = [stormInput],
            WindowOwnership = [ownership],
            CameraTransitions = [transition],
            NativeNavigation =
            [
                new ViewerNativeNavigationEvidence(
                    "Storm",
                    navigationState.Phase,
                    ViewerSwitchingEvidenceArtifact.StormNavigationDeliveryApi,
                    ViewerSwitchingEvidenceArtifact.StormNavigationSnapshotApi,
                    ViewerSwitchingEvidenceArtifact.RequiredStormChildAbiVersion,
                    "Alt+Left Orbit",
                    SequenceBefore: 1,
                    SequencePressed: 4,
                    SequenceMoved: 5,
                    SequenceAfter: 8,
                    PressedButtons: "Left",
                    PressedModifiers: "Alt",
                    PressedState: "Focused, Inside",
                    PointerDeltaX: 80,
                    PointerDeltaY: 48,
                    AvaloniaRoutedEvents: 0,
                    CameraBeforeSignature: states[2].CameraSignature,
                    CameraAfterSignature: navigationState.CameraSignature,
                    PixelBeforeSha256: pixels[2].Sha256,
                    PixelAfterSha256: navigationPixel.Sha256,
                    PixelBeforeArtifact: pixels[2].Artifact,
                    PixelAfterArtifact: navigationPixel.Artifact,
                    CameraChanged: true,
                    PixelChanged: true,
                    Win32Messages: CreateNativeNavigationMessages())
            ]
        };
    }

    private static ViewerWin32MessageEvidence[] CreateStormChildInputMessages()
    {
        ViewerInputCounterEvidence current = new(
            ResizeEvents: 2,
            ScalingEvents: 2,
            FocusEvents: 1,
            PointerMoves: 1,
            PointerButtons: 2,
            WheelEvents: 1,
            KeyEvents: 2,
            NativeFocusEvents: 0,
            NativePointerEvents: 0,
            NativeWheelEvents: 0,
            NativeKeyEvents: 0,
            RenderScaling: 1,
            Dpi: 96,
            PhysicalWidth: 640,
            PhysicalHeight: 480);
        var messages = new List<ViewerWin32MessageEvidence>();

        void add(string name, uint id, ViewerInputCounterEvidence after)
        {
            messages.Add(CreateWin32Message(
                name,
                id,
                current,
                after,
                "StormChild"));
            current = after;
        }

        add("WM_SETFOCUS", 0x0007, current with { NativeFocusEvents = 1 });
        add("WM_MOUSEMOVE", 0x0200, current with { NativePointerEvents = 1 });
        add("WM_LBUTTONDOWN", 0x0201, current with { NativePointerEvents = 2 });
        add("WM_LBUTTONUP", 0x0202, current with { NativePointerEvents = 3 });
        add("WM_MOUSEWHEEL", 0x020A, current with { NativeWheelEvents = 1 });
        add("WM_KEYDOWN", 0x0100, current with { NativeKeyEvents = 1 });
        add("WM_KEYUP", 0x0101, current with { NativeKeyEvents = 2 });
        return messages.ToArray();
    }

    private static ViewerWin32MessageEvidence[] CreateNativeNavigationMessages()
    {
        ViewerInputCounterEvidence counters = new(
            ResizeEvents: 0,
            ScalingEvents: 0,
            FocusEvents: 0,
            PointerMoves: 0,
            PointerButtons: 0,
            WheelEvents: 0,
            KeyEvents: 0,
            NativeFocusEvents: 1,
            NativePointerEvents: 4,
            NativeWheelEvents: 0,
            NativeKeyEvents: 2,
            RenderScaling: 1,
            Dpi: 96,
            PhysicalWidth: 640,
            PhysicalHeight: 480);
        return
        [
            CreateWin32Message(
                "WM_SETFOCUS",
                0x0007,
                counters,
                counters,
                "StormChild"),
            CreateWin32Message(
                "WM_SYSKEYDOWN(VK_MENU)",
                0x0104,
                counters,
                counters,
                "StormChild"),
            CreateWin32Message(
                "WM_MOUSEMOVE(start)",
                0x0200,
                counters,
                counters,
                "StormChild"),
            CreateWin32Message(
                "WM_LBUTTONDOWN",
                0x0201,
                counters,
                counters,
                "StormChild"),
            CreateWin32Message(
                "WM_MOUSEMOVE(drag)",
                0x0200,
                counters,
                counters,
                "StormChild"),
            CreateWin32Message(
                "WM_LBUTTONUP",
                0x0202,
                counters,
                counters,
                "StormChild"),
            CreateWin32Message(
                "WM_SYSKEYUP(VK_MENU)",
                0x0105,
                counters,
                counters,
                "StormChild")
        ];
    }

    private static ViewerWin32MessageEvidence[] CreateWin32Messages()
    {
        var initial = new ViewerInputCounterEvidence(
            ResizeEvents: 0,
            ScalingEvents: 0,
            FocusEvents: 0,
            PointerMoves: 0,
            PointerButtons: 0,
            WheelEvents: 0,
            KeyEvents: 0,
            NativeFocusEvents: 0,
            NativePointerEvents: 0,
            NativeWheelEvents: 0,
            NativeKeyEvents: 0,
            RenderScaling: 1,
            Dpi: 96,
            PhysicalWidth: 640,
            PhysicalHeight: 480);
        ViewerInputCounterEvidence changed = initial with
        {
            ResizeEvents = 1,
            ScalingEvents = 1,
            RenderScaling = 1.25,
            Dpi = 120,
            PhysicalWidth = 800,
            PhysicalHeight = 600
        };
        ViewerInputCounterEvidence restored = initial with
        {
            ResizeEvents = 2,
            ScalingEvents = 2
        };
        ViewerInputCounterEvidence lostFocus = restored with { FocusEvents = 1 };
        ViewerInputCounterEvidence moved = lostFocus with { PointerMoves = 1 };
        ViewerInputCounterEvidence pressed = moved with { PointerButtons = 1 };
        ViewerInputCounterEvidence released = pressed with { PointerButtons = 2 };
        ViewerInputCounterEvidence wheeled = released with { WheelEvents = 1 };
        ViewerInputCounterEvidence keyDown = wheeled with { KeyEvents = 1 };
        ViewerInputCounterEvidence keyUp = keyDown with { KeyEvents = 2 };
        return
        [
            CreateWin32Message("WM_DPICHANGED(change)", 0x02E0, initial, changed),
            CreateWin32Message("WM_DPICHANGED(restore)", 0x02E0, changed, restored),
            CreateWin32Message("WM_KILLFOCUS", 0x0008, restored, lostFocus),
            CreateWin32Message("WM_MOUSEMOVE", 0x0200, lostFocus, moved),
            CreateWin32Message("WM_LBUTTONDOWN", 0x0201, moved, pressed),
            CreateWin32Message("WM_LBUTTONUP", 0x0202, pressed, released),
            CreateWin32Message("WM_MOUSEWHEEL", 0x020A, released, wheeled),
            CreateWin32Message("WM_KEYDOWN", 0x0100, wheeled, keyDown),
            CreateWin32Message("WM_KEYUP", 0x0101, keyDown, keyUp)
        ];
    }

    private static ViewerWin32MessageEvidence CreateWin32Message(
        string message,
        uint messageId,
        ViewerInputCounterEvidence before,
        ViewerInputCounterEvidence after,
        string target = "ViewerTopLevel") =>
        new(
            target,
            "SendMessageTimeoutW",
            "0x100",
            message,
            messageId,
            "0x0",
            "0x0",
            ApiSucceeded: true,
            ApiReturn: "0x0",
            LastError: 0,
            WndProcObserved: true,
            HandlerObserved: true,
            Synthesized: false,
            before,
            after);

    private static ViewerPixelEvidence CreatePixel(
        string backend,
        string phase,
        char hashDigit) =>
        new(
            backend,
            phase,
            backend == "Storm"
                ? ViewerSwitchingEvidenceArtifact.StormCaptureApi
                : "PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush",
            new string(hashDigit, 64),
            640,
            480,
            "FF000000",
            10_000,
            30,
            40,
            50,
            ["A", "B", "C", "D"],
            $"{phase}.bmp");

    private static ViewerXTestInjectionEvidence CreateXTestInjection(
        string target,
        bool nativeObserved) =>
        new(
            target,
            "XTest",
            ExtensionAvailable: true,
            ExtensionMajor: 2,
            ExtensionMinor: 2,
            EventBase: 100,
            ErrorBase: 150,
            Display: ":99",
            Xid: target == "StormChild" ? "0x200" : "0x100",
            ServerGenerated: true,
            NativeSendEventFalseObserved: nativeObserved,
            Calls:
            [
                new("XTestQueryExtension", string.Empty, 1),
                new("XTranslateCoordinates", "x=8,y=8", 1),
                new("XSetInputFocus", "xid=0x100", 1),
                new("XTestFakeMotionEvent", "screen=0,x=8,y=8", 1),
                new("XTestFakeButtonEvent(button=1,press)", string.Empty, 1),
                new("XTestFakeButtonEvent(button=1,release)", string.Empty, 1),
                new("XTestFakeButtonEvent(button=4,press)", string.Empty, 1),
                new("XTestFakeButtonEvent(button=4,release)", string.Empty, 1),
                new("XTestFakeButtonEvent(button=5,press)", string.Empty, 1),
                new("XTestFakeButtonEvent(button=5,release)", string.Empty, 1),
                new("XTestFakeKeyEvent(press)", "keycode=65", 1),
                new("XTestFakeKeyEvent(release)", "keycode=65", 1),
                new("XFlush", string.Empty, 1),
                new("XSync", "discard=false", 1)
            ]);

    private sealed class FakeXTestApi : IXTestApi
    {
        internal bool Closed { get; private set; }

        internal int QueryResult { get; init; } = 1;

        public nint OpenDisplay() => (nint)0x42;

        public int CloseDisplay(nint display)
        {
            Closed = display == (nint)0x42;
            return 1;
        }

        public int QueryExtension(
            nint display,
            out int eventBase,
            out int errorBase,
            out int majorVersion,
            out int minorVersion)
        {
            eventBase = 100;
            errorBase = 150;
            majorVersion = 2;
            minorVersion = 2;
            return QueryResult;
        }

        public int SetInputFocus(nint display, nint window, int revertTo, nuint time) => 1;

        public int DefaultScreen(nint display) => 0;

        public nint DefaultRootWindow(nint display) => (nint)1;

        public int TranslateCoordinates(
            nint display,
            nint window,
            nint root,
            int x,
            int y,
            out int rootX,
            out int rootY)
        {
            rootX = x + 10;
            rootY = y + 20;
            return 1;
        }

        public uint KeySymToKeyCode(nint display, nuint keySym) => 65;

        public int FakeMotionEvent(
            nint display,
            int screen,
            int x,
            int y,
            nuint delay) => 1;

        public int FakeButtonEvent(
            nint display,
            uint button,
            bool pressed,
            nuint delay) => 1;

        public int FakeKeyEvent(
            nint display,
            uint keyCode,
            bool pressed,
            nuint delay) => 1;

        public int Flush(nint display) => 1;

        public int Sync(nint display, bool discard) => 1;
    }
}
