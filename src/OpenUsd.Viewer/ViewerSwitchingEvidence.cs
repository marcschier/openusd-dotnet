// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed record ViewerStateEvidence(
    string Backend,
    string Phase,
    ulong Revision,
    string StageIdentifier,
    double TimeCode,
    string CameraMode,
    string CameraPayload,
    string CameraSignature,
    string NativeCameraSignature,
    string[] Selection,
    string Purposes,
    string Visibility,
    string DrawMode,
    int ViewportWidth,
    int ViewportHeight,
    bool ExactReferencePreserved,
    int SchedulerIdentity,
    int RenderSourceIdentity);

internal sealed record ViewerPixelEvidence(
    string Backend,
    string Phase,
    string CaptureApi,
    string Sha256,
    int Width,
    int Height,
    string BackgroundBgra,
    long NonBackgroundPixels,
    double MeanRed,
    double MeanGreen,
    double MeanBlue,
    string[] Samples,
    string Artifact);

internal sealed record ViewerInputCounterEvidence(
    int ResizeEvents,
    int ScalingEvents,
    int FocusEvents,
    int PointerMoves,
    int PointerButtons,
    int WheelEvents,
    int KeyEvents,
    ulong NativeFocusEvents,
    ulong NativePointerEvents,
    ulong NativeWheelEvents,
    ulong NativeKeyEvents,
    double RenderScaling,
    uint Dpi,
    int PhysicalWidth,
    int PhysicalHeight);

internal sealed record ViewerWin32MessageEvidence(
    string Target,
    string Api,
    string Hwnd,
    string Message,
    uint MessageId,
    string WParam,
    string LParam,
    bool ApiSucceeded,
    string ApiReturn,
    int LastError,
    bool WndProcObserved,
    bool HandlerObserved,
    bool Synthesized,
    ViewerInputCounterEvidence Before,
    ViewerInputCounterEvidence After);

internal sealed record ViewerXTestCallEvidence(
    string Api,
    string Arguments,
    int Result);

internal sealed record ViewerXTestInjectionEvidence(
    string Target,
    string InjectionApi,
    bool ExtensionAvailable,
    int ExtensionMajor,
    int ExtensionMinor,
    int EventBase,
    int ErrorBase,
    string Display,
    string Xid,
    bool ServerGenerated,
    bool NativeSendEventFalseObserved,
    ViewerXTestCallEvidence[] Calls);

internal sealed record ViewerInputEvidence(
    string Backend,
    string DeliveryApi,
    bool Synthesized,
    int ResizeEvents,
    int ScalingEvents,
    int FocusEvents,
    int PointerMoves,
    int PointerButtons,
    int WheelEvents,
    int KeyEvents,
    long NativeFocusEvents,
    long NativePointerEvents,
    long NativeWheelEvents,
    long NativeKeyEvents,
    double RenderScalingBefore,
    double RenderScalingObserved,
    double RenderScalingAfter,
    uint NativeDpiBefore,
    uint NativeDpiObserved,
    uint NativeDpiAfter,
    int PhysicalWidthBefore,
    int PhysicalHeightBefore,
    int PhysicalWidthObserved,
    int PhysicalHeightObserved,
    int PhysicalWidthAfter,
    int PhysicalHeightAfter,
    ViewerWin32MessageEvidence[] Win32Messages,
    ViewerXTestInjectionEvidence[]? XTestInjections = null);

internal sealed record ViewerResourceEvidence(
    long ChildLive,
    long ChildPeak,
    long ManagedStorm,
    long NativeStorm,
    long ManagedSilk,
    long NativeSilk,
    long ManagedPages,
    long NativePages,
    long GpuScenes,
    long GpuMeshes,
    long AbandonedStorm,
    bool ContextLossSimulated);

internal sealed record ViewerCleanupQuarantineEvidence(
    string Backend,
    int RetiredBefore,
    int RetiredWhileBlocked,
    int RetiredAfterRecovery,
    int CandidateBefore,
    int CandidateAfterManual,
    int CandidateAfterAutomatic,
    int CandidateAfterRecovery,
    int FactoryBefore,
    int FactoryAfterManual,
    int FactoryAfterAutomatic,
    int FactoryAfterRecovery,
    int AttachBefore,
    int AttachAfterManual,
    int AttachAfterAutomatic,
    int AttachAfterRecovery,
    long ChildLiveBefore,
    long ChildPeakBefore,
    long ChildLiveWhileBlocked,
    long ChildPeakWhileBlocked,
    long ChildLiveAfterRecovery,
    long ChildPeakAfterRecovery,
    string ManualFailure,
    string ManualDiagnosticCode,
    bool AutomaticSucceeded,
    bool AutomaticSkippedQuarantinedKind,
    bool CleanupRecovered,
    bool ReactivatedAfterRecovery,
    ViewerHwndEvidence BlockedWindowOwnership);

internal sealed record ViewerCameraTransitionEvidence(
    string Backend,
    string AutomaticBeforePhase,
    string ExplicitPhase,
    string AutomaticRestoredPhase,
    string AutomaticCameraSignature,
    string ExplicitCameraSignature,
    string RestoredCameraSignature,
    string AutomaticPixelSha256,
    string ExplicitPixelSha256,
    string RestoredPixelSha256,
    string AutomaticPixelArtifact,
    string ExplicitPixelArtifact,
    string RestoredPixelArtifact,
    bool ExactReferencesPreserved,
    ulong LatestRequestedRevision,
    string LatestRequestedCameraSignature,
    string LatestRenderedCameraSignature,
    bool AsyncCoalescingValidated);

internal sealed record ViewerStageCameraBackendFrameEvidence(
    string Backend,
    string Phase,
    double TimeCode,
    ulong StateRevision,
    string CameraSignature,
    string NativeCameraSignature,
    string PixelSha256,
    string PixelArtifact,
    bool ExactReferencePreserved,
    ulong LatestRequestedRevision,
    string LatestRequestedCameraSignature,
    string LatestRenderedCameraSignature);

internal sealed record ViewerStageCameraAutomaticEvidence(
    string Backend,
    string Phase,
    double TimeCode,
    ulong StateRevision,
    string CameraSignature,
    string NativeCameraSignature,
    string PixelSha256,
    string PixelArtifact);

internal sealed record ViewerStageCameraEvidence(
    string Source,
    string StageIdentifier,
    string StageSha256,
    string CameraPath,
    double InitialTimeCode,
    double SampledTimeCode,
    string InitialSnapshotSha256,
    string SampledSnapshotSha256,
    ViewerStageCameraAutomaticEvidence AutomaticBefore,
    ViewerStageCameraBackendFrameEvidence[] InitialFrames,
    ViewerStageCameraBackendFrameEvidence[] SampledFrames,
    ViewerStageCameraAutomaticEvidence AutomaticRestored,
    bool ExactStatePreservedAcrossBackends);

internal sealed record ViewerNativeNavigationEvidence(
    string Backend,
    string Phase,
    string DeliveryApi,
    string SnapshotApi,
    int StormChildAbiVersion,
    string Gesture,
    ulong SequenceBefore,
    ulong SequencePressed,
    ulong SequenceMoved,
    ulong SequenceAfter,
    string PressedButtons,
    string PressedModifiers,
    string PressedState,
    int PointerDeltaX,
    int PointerDeltaY,
    int AvaloniaRoutedEvents,
    string CameraBeforeSignature,
    string CameraAfterSignature,
    string PixelBeforeSha256,
    string PixelAfterSha256,
    string PixelBeforeArtifact,
    string PixelAfterArtifact,
    bool CameraChanged,
    bool PixelChanged,
    ViewerWin32MessageEvidence[] Win32Messages);

internal sealed record ViewerSwitchingEvidenceArtifact(
    int SchemaVersion,
    string Scenario,
    string RuntimeCompositor,
    string PlatformHandle,
    int StormChildAbiVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string[] LoadedArtifacts,
    ViewerStateEvidence[] States,
    ViewerPixelEvidence[] Pixels,
    ViewerInputEvidence[] Inputs,
    ViewerCompositionEvidence[] Compositions,
    ViewerHwndEvidence[] WindowOwnership,
    ViewerCameraTransitionEvidence[] CameraTransitions,
    ViewerResourceEvidence Resources,
    ViewerCleanupQuarantineEvidence? CleanupQuarantine = null,
    ViewerNativeNavigationEvidence[]? NativeNavigation = null,
    ViewerStageCameraEvidence? StageCamera = null)
{
    internal const int CurrentSchemaVersion = 8;
    internal const int RequiredStormChildAbiVersion = 8;
    internal const string StormCaptureApi =
        "openusd_storm_child_capture_framebuffer(ABI8,preserved-texture)";
    internal const string StormNavigationDeliveryApi =
        "SendMessageTimeoutW+StormChildWndProc+ABI8Poll+" +
        "ViewerCameraNavigationUiAdapter";
    internal const string StormNavigationSnapshotApi =
        "openusd_storm_child_get_navigation_input(ABI8,v2)";

    internal static ViewerSwitchingEvidenceArtifact ReadAndValidate(string path)
    {
        using FileStream stream = File.OpenRead(path);
        ViewerSwitchingEvidenceArtifact artifact =
            JsonSerializer.Deserialize(
                stream,
                ViewerSwitchingEvidenceJsonContext.Default.ViewerSwitchingEvidenceArtifact)
            ?? throw new InvalidDataException("Viewer switching evidence was empty.");
        artifact.Validate();
        return artifact;
    }

    internal void Validate()
    {
        bool linux = string.Equals(PlatformHandle, "XID", StringComparison.OrdinalIgnoreCase);
        bool windows = string.Equals(PlatformHandle, "HWND", StringComparison.OrdinalIgnoreCase);
        bool stageCameraScenario = string.Equals(
            Scenario,
            ViewerStageCameraSmokeContract.ScenarioName,
            StringComparison.Ordinal);
        if (SchemaVersion != CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(Scenario) ||
            (!linux && !windows) ||
            (windows && !string.Equals(
                RuntimeCompositor,
                "ANGLE/D3D11 (runtime-observed)",
                StringComparison.Ordinal)) ||
            (linux && !RuntimeCompositor.StartsWith("X11", StringComparison.Ordinal)) ||
            StormChildAbiVersion != RequiredStormChildAbiVersion ||
            CompletedAt <= StartedAt ||
            LoadedArtifacts.Length == 0 ||
            States.Length == 0 ||
            Pixels.Length == 0 ||
            Inputs.Length == 0 ||
            CameraTransitions.Length == 0 ||
            (windows && (Compositions.Length == 0 || WindowOwnership.Length == 0)))
        {
            throw new InvalidDataException("Viewer switching evidence is incomplete.");
        }

        ViewerStateEvidence baselineState = States[0];
        var stateKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ViewerStateEvidence state in States)
        {
            if (string.IsNullOrWhiteSpace(state.Backend) ||
                string.IsNullOrWhiteSpace(state.Phase) ||
                state.Revision == 0 ||
                string.IsNullOrWhiteSpace(state.StageIdentifier) ||
                !ViewerCameraEvidence.IsValid(
                    state.CameraMode,
                    state.CameraPayload,
                    state.CameraSignature,
                    state.NativeCameraSignature) ||
                state.ViewportWidth <= 0 ||
                state.ViewportHeight <= 0 ||
                !state.ExactReferencePreserved ||
                state.SchedulerIdentity == 0 ||
                state.RenderSourceIdentity == 0 ||
                !stateKeys.Add($"{state.Backend}\0{state.Phase}"))
            {
                throw new InvalidDataException("Viewer state-preservation evidence is invalid.");
            }
            if (!string.Equals(
                    state.StageIdentifier,
                    baselineState.StageIdentifier,
                    StringComparison.Ordinal) ||
                (!stageCameraScenario &&
                 state.TimeCode != baselineState.TimeCode) ||
                !state.Selection.SequenceEqual(
                    baselineState.Selection,
                    StringComparer.Ordinal) ||
                !string.Equals(state.Purposes, baselineState.Purposes, StringComparison.Ordinal) ||
                !string.Equals(
                    state.Visibility,
                    baselineState.Visibility,
                    StringComparison.Ordinal) ||
                !string.Equals(state.DrawMode, baselineState.DrawMode, StringComparison.Ordinal) ||
                state.SchedulerIdentity != baselineState.SchedulerIdentity ||
                state.RenderSourceIdentity != baselineState.RenderSourceIdentity)
            {
                throw new InvalidDataException(
                    "Viewer switching changed shared stage, time, selection, or display state.");
            }
        }

        string? previousHash = null;
        foreach (ViewerPixelEvidence pixel in Pixels)
        {
            long minimumScenePixels = Math.Max(100, pixel.Width * (long)pixel.Height / 1000);
            if (string.IsNullOrWhiteSpace(pixel.Backend) ||
                string.IsNullOrWhiteSpace(pixel.Phase) ||
                !IsSupportedCaptureApi(pixel.CaptureApi, windows, linux) ||
                pixel.Width <= 0 ||
                pixel.Height <= 0 ||
                pixel.Sha256.Length != 64 ||
                !pixel.Sha256.All(Uri.IsHexDigit) ||
                pixel.BackgroundBgra.Length != 8 ||
                pixel.Samples.Length < 4 ||
                pixel.NonBackgroundPixels < minimumScenePixels ||
                string.IsNullOrWhiteSpace(pixel.Artifact) ||
                string.Equals(previousHash, pixel.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Viewer pixel evidence is invalid or stale.");
            }
            previousHash = pixel.Sha256;
        }
        ValidateCameraTransitions();
        ValidateStageCameraEvidence(stageCameraScenario);
        if (States.Any(state => string.Equals(
                state.Backend,
                "Storm",
                StringComparison.Ordinal)) &&
            !Pixels.Any(pixel =>
                string.Equals(pixel.Backend, "Storm", StringComparison.Ordinal) &&
                string.Equals(
                    pixel.CaptureApi,
                    StormCaptureApi,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Storm evidence did not include the exact preserved native frame.");
        }
        if (linux)
        {
            foreach (string backend in States.Select(state => state.Backend).Distinct())
            {
                if (!Pixels.Any(pixel =>
                    string.Equals(pixel.Backend, backend, StringComparison.Ordinal) &&
                    string.Equals(
                        pixel.CaptureApi,
                        "XGetImage(ZPixmap,viewer-shell)",
                        StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        $"Linux shell pixels were not captured for {backend}.");
                }
            }
        }

        foreach (ViewerInputEvidence input in Inputs)
        {
            bool storm = string.Equals(input.Backend, "Storm", StringComparison.Ordinal);
            bool physicalSizeChanged =
                input.PhysicalWidthObserved != input.PhysicalWidthBefore ||
                input.PhysicalHeightObserved != input.PhysicalHeightBefore;
            if (input.ResizeEvents < 1 ||
                input.Synthesized ||
                string.IsNullOrWhiteSpace(input.DeliveryApi) ||
                input.DeliveryApi.Contains("RaiseEvent", StringComparison.OrdinalIgnoreCase) ||
                input.DeliveryApi.Contains("RawInput", StringComparison.OrdinalIgnoreCase) ||
                input.DeliveryApi.Contains("synthetic", StringComparison.OrdinalIgnoreCase) ||
                input.DeliveryApi.Contains("XSendEvent", StringComparison.OrdinalIgnoreCase) ||
                input.RenderScalingBefore <= 0 ||
                input.RenderScalingObserved <= 0 ||
                input.RenderScalingAfter <= 0 ||
                input.NativeDpiBefore == 0 ||
                input.NativeDpiObserved == 0 ||
                input.NativeDpiAfter == 0 ||
                !physicalSizeChanged ||
                input.PhysicalWidthAfter != input.PhysicalWidthBefore ||
                input.PhysicalHeightAfter != input.PhysicalHeightBefore ||
                input.FocusEvents < 1 ||
                input.PointerMoves < 1 ||
                input.PointerButtons < 2 ||
                input.WheelEvents < 1 ||
                input.KeyEvents < 2)
            {
                throw new InvalidDataException("Viewer input-routing evidence is incomplete.");
            }
            if (windows)
            {
                if (input.ScalingEvents < 2 ||
                    input.NativeDpiObserved == input.NativeDpiBefore ||
                    input.NativeDpiAfter != input.NativeDpiBefore ||
                    Math.Abs(input.RenderScalingObserved - input.RenderScalingBefore) < 0.001 ||
                    Math.Abs(input.RenderScalingAfter - input.RenderScalingBefore) >= 0.001 ||
                    !input.DeliveryApi.Contains(
                        "SendMessageTimeoutW",
                        StringComparison.Ordinal) ||
                    !input.DeliveryApi.Contains(
                        "EnableMouseInPointer(false,success=True,error=0)",
                        StringComparison.Ordinal) ||
                    !input.DeliveryApi.Contains(
                        "DiagnosticWM_DPICHANGED",
                        StringComparison.Ordinal) ||
                    input.Win32Messages.Length == 0)
                {
                    throw new InvalidDataException(
                        "Viewer DPI evidence did not change and restore through Win32.");
                }
                ValidateWin32Messages(input, storm);
            }
            else if (input.Win32Messages.Length != 0)
            {
                throw new InvalidDataException(
                    "Non-Windows Viewer input evidence contained Win32 messages.");
            }
            if (linux)
            {
                ValidateXTestInjections(input, storm);
            }
            if (storm &&
                (input.NativeFocusEvents < 1 ||
                 input.NativePointerEvents < 3 ||
                 input.NativeWheelEvents < 1 ||
                 input.NativeKeyEvents < 2 ||
                 input.PhysicalWidthObserved == input.PhysicalWidthBefore &&
                 input.PhysicalHeightObserved == input.PhysicalHeightBefore))
            {
                throw new InvalidDataException("Storm native input counters did not advance.");
            }
        }
        ValidateNativeNavigation(windows);

        if (windows)
        {
            foreach (string backend in States
                .Select(state => state.Backend)
                .Distinct(StringComparer.Ordinal))
            {
                if (!WindowOwnership.Any(ownership =>
                    string.Equals(ownership.Backend, backend, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        $"Viewer HWND ownership was not observed for {backend}.");
                }
                if (backend is "D3D12" or "Vulkan" &&
                    !Compositions.Any(composition =>
                        string.Equals(
                            composition.Backend,
                            backend,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        $"Runtime compositor imports were not observed for {backend}.");
                }
            }

            foreach (ViewerCompositionEvidence composition in Compositions)
            {
                if (composition.Backend is not ("D3D12" or "Vulkan") ||
                    !composition.CompositionHostVisible ||
                    composition.SuccessfulImports < 1 ||
                    composition.SuccessfulPresents < 1 ||
                    composition.DeviceLuid.Length != 16 ||
                    !composition.DeviceLuid.All(Uri.IsHexDigit) ||
                    !composition.SupportedImageHandleTypes.Contains(
                        "D3D11TextureNtHandle",
                        StringComparer.Ordinal) ||
                    !string.Equals(
                        composition.UsedImageHandleType,
                        "D3D11TextureNtHandle",
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        composition.SynchronizationKind,
                        nameof(CompositionFrameSynchronizationKind.KeyedMutex),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Viewer compositor evidence was configured rather than runtime-observed.");
                }
            }

            foreach (ViewerHwndEvidence ownership in WindowOwnership)
            {
                bool storm = string.Equals(ownership.Backend, "Storm", StringComparison.Ordinal);
                if (string.IsNullOrWhiteSpace(ownership.TopLevelHwnd) ||
                    ownership.TopLevelProcessId == 0 ||
                    ownership.TopLevelThreadId == 0 ||
                    ownership.StaleLiveStormCount != 0 ||
                    ownership.EnumeratedStormCount > 1 ||
                    ownership.VisibleStormCount > 1 ||
                    (storm &&
                     (ownership.EnumeratedStormCount != 1 ||
                      ownership.VisibleStormCount != 1 ||
                      ownership.LiveKnownStormCount != 1 ||
                      !ownership.StormIsWindow ||
                      !ownership.StormIsVisible ||
                      !ownership.StormParentWithinViewer ||
                      ownership.StormClassName != "OpenUsdStormNativeChild" ||
                      ownership.StormProcessId != ownership.TopLevelProcessId ||
                      ownership.StormThreadId != ownership.TopLevelThreadId ||
                      ownership.ExpectedStormHwnd != ownership.ObservedStormHwnd ||
                      ownership.CompositionHostVisible)) ||
                    (!storm &&
                     (ownership.EnumeratedStormCount != 0 ||
                      ownership.VisibleStormCount != 0 ||
                      ownership.LiveKnownStormCount != 0 ||
                      !ownership.CompositionHostVisible)))
                {
                    throw new InvalidDataException(
                        "Viewer HWND evidence contains duplicate, stale, or control-count-only ownership.");
                }
            }
        }

        if (Resources.ChildLive != 0 ||
            Resources.ManagedStorm != 0 ||
            Resources.NativeStorm != 0 ||
            Resources.ManagedSilk != 0 ||
            Resources.NativeSilk != 0 ||
            Resources.ManagedPages != 0 ||
            Resources.NativePages != 0 ||
            Resources.GpuScenes != 0 ||
            Resources.GpuMeshes != 0 ||
            (!Resources.ContextLossSimulated && Resources.AbandonedStorm != 0))
        {
            throw new InvalidDataException("Viewer resource evidence did not return to zero.");
        }

        bool quarantineScenario = string.Equals(
            Scenario,
            "renderer-retired-kind-quarantine",
            StringComparison.Ordinal);
        if (quarantineScenario != (CleanupQuarantine is not null))
        {
            throw new InvalidDataException(
                "Viewer cleanup-quarantine evidence does not match its scenario.");
        }
        if (CleanupQuarantine is { } quarantine)
        {
            ViewerHwndEvidence blocked = quarantine.BlockedWindowOwnership;
            if (!string.Equals(quarantine.Backend, "Storm", StringComparison.Ordinal) ||
                quarantine.RetiredBefore != 0 ||
                quarantine.RetiredWhileBlocked != 1 ||
                quarantine.RetiredAfterRecovery != 0 ||
                quarantine.CandidateAfterManual != quarantine.CandidateBefore ||
                quarantine.CandidateAfterAutomatic != quarantine.CandidateBefore ||
                quarantine.CandidateAfterRecovery != quarantine.CandidateBefore + 1 ||
                quarantine.FactoryAfterManual != quarantine.FactoryBefore ||
                quarantine.FactoryAfterAutomatic != quarantine.FactoryBefore ||
                quarantine.FactoryAfterRecovery != quarantine.FactoryBefore + 1 ||
                quarantine.AttachAfterManual != quarantine.AttachBefore ||
                quarantine.AttachAfterAutomatic != quarantine.AttachBefore ||
                quarantine.AttachAfterRecovery != quarantine.AttachBefore + 1 ||
                quarantine.ChildLiveBefore != 1 ||
                quarantine.ChildLiveWhileBlocked != 1 ||
                quarantine.ChildLiveAfterRecovery != 1 ||
                quarantine.ChildPeakBefore != 1 ||
                quarantine.ChildPeakWhileBlocked != 1 ||
                quarantine.ChildPeakAfterRecovery != 1 ||
                !string.Equals(
                    quarantine.ManualFailure,
                    nameof(RenderBackendManagerFailureKind.CleanupPending),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    quarantine.ManualDiagnosticCode,
                    "manager.backend_cleanup_pending",
                    StringComparison.Ordinal) ||
                !quarantine.AutomaticSucceeded ||
                !quarantine.AutomaticSkippedQuarantinedKind ||
                !quarantine.CleanupRecovered ||
                !quarantine.ReactivatedAfterRecovery ||
                blocked.EnumeratedStormCount != 1 ||
                blocked.VisibleStormCount != 0 ||
                blocked.LiveKnownStormCount != 1 ||
                blocked.StaleLiveStormCount != 0 ||
                !blocked.StormIsWindow ||
                blocked.StormIsVisible ||
                !blocked.StormParentWithinViewer ||
                blocked.StormClassName != "OpenUsdStormNativeChild" ||
                !blocked.CompositionHostVisible)
            {
                throw new InvalidDataException(
                    "Viewer retained-kind quarantine evidence is incomplete.");
            }
        }
    }

    private void ValidateNativeNavigation(bool windows)
    {
        ViewerNativeNavigationEvidence[] navigation = NativeNavigation ?? [];
        bool hasStormInput = Inputs.Any(input => string.Equals(
            input.Backend,
            "Storm",
            StringComparison.Ordinal));
        if (!windows)
        {
            if (navigation.Length != 0)
            {
                throw new InvalidDataException(
                    "Non-Windows evidence contained Win32 native navigation proof.");
            }
            return;
        }
        if (hasStormInput && navigation.Length == 0)
        {
            throw new InvalidDataException(
                "Windows Storm evidence omitted native navigation proof.");
        }
        foreach (ViewerNativeNavigationEvidence evidence in navigation)
        {
            if (!string.Equals(evidence.Backend, "Storm", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(evidence.Phase) ||
                !string.Equals(
                    evidence.DeliveryApi,
                    StormNavigationDeliveryApi,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    evidence.SnapshotApi,
                    StormNavigationSnapshotApi,
                    StringComparison.Ordinal) ||
                evidence.StormChildAbiVersion != RequiredStormChildAbiVersion ||
                !string.Equals(
                    evidence.Gesture,
                    "Alt+Left Orbit",
                    StringComparison.Ordinal) ||
                ViewerStormNavigationInputTracker.CounterDelta(
                    evidence.SequencePressed,
                    evidence.SequenceBefore) == 0 ||
                ViewerStormNavigationInputTracker.CounterDelta(
                    evidence.SequenceMoved,
                    evidence.SequencePressed) == 0 ||
                ViewerStormNavigationInputTracker.CounterDelta(
                    evidence.SequenceAfter,
                    evidence.SequenceMoved) == 0 ||
                !string.Equals(
                    evidence.PressedButtons,
                    nameof(OpenUsdStormPointerButtons.Left),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    evidence.PressedModifiers,
                    nameof(OpenUsdStormInputModifiers.Alt),
                    StringComparison.Ordinal) ||
                !evidence.PressedState.Contains(
                    nameof(OpenUsdStormNavigationState.Focused),
                    StringComparison.Ordinal) ||
                evidence.PointerDeltaX == 0 ||
                evidence.PointerDeltaY == 0 ||
                evidence.AvaloniaRoutedEvents != 0 ||
                string.IsNullOrWhiteSpace(evidence.CameraBeforeSignature) ||
                string.IsNullOrWhiteSpace(evidence.CameraAfterSignature) ||
                string.Equals(
                    evidence.CameraBeforeSignature,
                    evidence.CameraAfterSignature,
                    StringComparison.Ordinal) ||
                evidence.PixelBeforeSha256.Length != 64 ||
                evidence.PixelAfterSha256.Length != 64 ||
                string.Equals(
                    evidence.PixelBeforeSha256,
                    evidence.PixelAfterSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(evidence.PixelBeforeArtifact) ||
                string.IsNullOrWhiteSpace(evidence.PixelAfterArtifact) ||
                !evidence.CameraChanged ||
                !evidence.PixelChanged ||
                evidence.Win32Messages.Length < 7 ||
                evidence.Win32Messages.Any(message =>
                    !string.Equals(message.Target, "StormChild", StringComparison.Ordinal) ||
                    !string.Equals(
                        message.Api,
                        "SendMessageTimeoutW",
                        StringComparison.Ordinal) ||
                    !message.ApiSucceeded ||
                    !message.WndProcObserved ||
                    !message.HandlerObserved ||
                    message.Synthesized))
            {
                throw new InvalidDataException(
                    "Viewer native Storm navigation evidence is incomplete.");
            }
            _ = FindPixel(
                evidence.PixelBeforeSha256,
                evidence.PixelBeforeArtifact);
            _ = FindPixel(
                evidence.PixelAfterSha256,
                evidence.PixelAfterArtifact);
            if (!States.Any(state =>
                    string.Equals(state.Backend, evidence.Backend, StringComparison.Ordinal) &&
                    string.Equals(state.Phase, evidence.Phase, StringComparison.Ordinal) &&
                    string.Equals(
                        state.CameraSignature,
                        evidence.CameraAfterSignature,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Native Storm navigation camera evidence is not state-bound.");
            }
        }
    }

    private void ValidateStageCameraEvidence(bool required)
    {
        if (!required)
        {
            if (StageCamera is not null)
            {
                throw new InvalidDataException(
                    "Stage-camera evidence is present outside its dedicated scenario.");
            }
            return;
        }

        ViewerStageCameraEvidence evidence = StageCamera ??
            throw new InvalidDataException(
                "The stage-camera backend scenario omitted authored-camera evidence.");
        var requiredBackends = new HashSet<string>(
            ["Storm", "D3D12", "Vulkan"],
            StringComparer.Ordinal);
        if (!string.Equals(
                evidence.Source,
                ViewerStageCameraSmokeContract.SourceName,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(evidence.StageIdentifier) ||
            !IsHex(evidence.StageSha256, 64) ||
            string.IsNullOrWhiteSpace(evidence.CameraPath) ||
            !evidence.CameraPath.StartsWith('/') ||
            !double.IsFinite(evidence.InitialTimeCode) ||
            !double.IsFinite(evidence.SampledTimeCode) ||
            evidence.InitialTimeCode == evidence.SampledTimeCode ||
            !IsHex(evidence.InitialSnapshotSha256, 64) ||
            !IsHex(evidence.SampledSnapshotSha256, 64) ||
            string.Equals(
                evidence.InitialSnapshotSha256,
                evidence.SampledSnapshotSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !evidence.ExactStatePreservedAcrossBackends ||
            evidence.AutomaticBefore is null ||
            evidence.AutomaticRestored is null ||
            evidence.InitialFrames is null ||
            evidence.SampledFrames is null ||
            evidence.InitialFrames.Length != requiredBackends.Count ||
            evidence.SampledFrames.Length != requiredBackends.Count ||
            !requiredBackends.SetEquals(
                evidence.InitialFrames.Select(frame => frame.Backend)) ||
            !requiredBackends.SetEquals(
                evidence.SampledFrames.Select(frame => frame.Backend)) ||
            States.Any(state =>
                !string.Equals(
                    state.StageIdentifier,
                    evidence.StageIdentifier,
                    StringComparison.Ordinal) ||
                state.Selection.Length != 1 ||
                !string.Equals(
                    state.Selection[0],
                    evidence.CameraPath,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Stage-camera source, stage, path, time, or snapshot provenance is invalid.");
        }

        (
            ViewerStateEvidence beforeState,
            ViewerPixelEvidence beforePixel) = validateAutomatic(
                evidence.AutomaticBefore);
        (
            ViewerStateEvidence restoredState,
            ViewerPixelEvidence restoredPixel) = validateAutomatic(
                evidence.AutomaticRestored);
        ViewerStateEvidence initialState = validateFrames(
            evidence.InitialFrames,
            evidence.InitialTimeCode);
        ViewerStateEvidence sampledState = validateFrames(
            evidence.SampledFrames,
            evidence.SampledTimeCode);

        if (beforeState.Revision >= initialState.Revision ||
            initialState.Revision >= sampledState.Revision ||
            sampledState.Revision >= restoredState.Revision ||
            !string.Equals(
                beforeState.CameraSignature,
                restoredState.CameraSignature,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                beforeState.NativeCameraSignature,
                restoredState.NativeCameraSignature,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                initialState.CameraSignature,
                sampledState.CameraSignature,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                initialState.NativeCameraSignature,
                sampledState.NativeCameraSignature,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stage-camera revisions, signatures, or automatic restoration are stale.");
        }

        ViewerStageCameraBackendFrameEvidence initialStorm =
            evidence.InitialFrames.Single(frame =>
                string.Equals(frame.Backend, "Storm", StringComparison.Ordinal));
        ViewerStageCameraBackendFrameEvidence sampledStorm =
            evidence.SampledFrames.Single(frame =>
                string.Equals(frame.Backend, "Storm", StringComparison.Ordinal));
        if (string.Equals(
                beforePixel.Sha256,
                initialStorm.PixelSha256,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                beforePixel.Sha256,
                sampledStorm.PixelSha256,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                restoredPixel.Sha256,
                initialStorm.PixelSha256,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                restoredPixel.Sha256,
                sampledStorm.PixelSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Automatic camera pixels did not differ from the authored stage camera.");
        }

        foreach (string backend in requiredBackends)
        {
            ViewerStageCameraBackendFrameEvidence initial =
                evidence.InitialFrames.Single(frame =>
                    string.Equals(frame.Backend, backend, StringComparison.Ordinal));
            ViewerStageCameraBackendFrameEvidence sampled =
                evidence.SampledFrames.Single(frame =>
                    string.Equals(frame.Backend, backend, StringComparison.Ordinal));
            if (string.Equals(
                    initial.PixelSha256,
                    sampled.PixelSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The {backend} initial and sampled stage-camera pixels are stale.");
            }
        }

        (ViewerStateEvidence State, ViewerPixelEvidence Pixel) validateAutomatic(
            ViewerStageCameraAutomaticEvidence automatic)
        {
            ViewerStateEvidence state = FindState(automatic.Backend, automatic.Phase);
            ViewerPixelEvidence pixel = FindPixel(
                automatic.PixelSha256,
                automatic.PixelArtifact);
            if (!string.Equals(
                    automatic.Backend,
                    "Storm",
                    StringComparison.Ordinal) ||
                automatic.TimeCode != evidence.InitialTimeCode ||
                automatic.StateRevision != state.Revision ||
                !string.Equals(
                    state.CameraMode,
                    nameof(CameraMode.Automatic),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    automatic.CameraSignature,
                    state.CameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    automatic.NativeCameraSignature,
                    state.NativeCameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    pixel.Backend,
                    automatic.Backend,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Stage-camera automatic reset evidence is not state/pixel bound.");
            }
            return (state, pixel);
        }

        ViewerStateEvidence validateFrames(
            ViewerStageCameraBackendFrameEvidence[] frames,
            double timeCode)
        {
            ViewerStateEvidence? common = null;
            foreach (ViewerStageCameraBackendFrameEvidence frame in frames)
            {
                ViewerStateEvidence state = FindState(frame.Backend, frame.Phase);
                ViewerPixelEvidence pixel = FindPixel(
                    frame.PixelSha256,
                    frame.PixelArtifact);
                bool storm = string.Equals(
                    frame.Backend,
                    "Storm",
                    StringComparison.Ordinal);
                if (frame.TimeCode != timeCode ||
                    frame.StateRevision != state.Revision ||
                    state.TimeCode != timeCode ||
                    !string.Equals(
                        state.CameraMode,
                        nameof(CameraMode.Matrices),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        frame.CameraSignature,
                        state.CameraSignature,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        frame.NativeCameraSignature,
                        state.NativeCameraSignature,
                        StringComparison.OrdinalIgnoreCase) ||
                    !frame.ExactReferencePreserved ||
                    !state.ExactReferencePreserved ||
                    !string.Equals(
                        pixel.Backend,
                        frame.Backend,
                        StringComparison.Ordinal) ||
                    (storm &&
                     (frame.LatestRequestedRevision != state.Revision ||
                      !string.Equals(
                          frame.LatestRequestedCameraSignature,
                          state.NativeCameraSignature,
                          StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(
                          frame.LatestRenderedCameraSignature,
                          state.NativeCameraSignature,
                          StringComparison.OrdinalIgnoreCase))) ||
                    (!storm &&
                     (frame.LatestRequestedRevision != 0 ||
                      !string.IsNullOrEmpty(frame.LatestRequestedCameraSignature) ||
                      !string.IsNullOrEmpty(frame.LatestRenderedCameraSignature))))
                {
                    throw new InvalidDataException(
                        $"Stage-camera evidence is invalid for {frame.Backend}/{frame.Phase}.");
                }
                if (common is null)
                {
                    common = state;
                }
                else if (common.Revision != state.Revision ||
                    !string.Equals(
                        common.CameraSignature,
                        state.CameraSignature,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        common.NativeCameraSignature,
                        state.NativeCameraSignature,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "A backend switch did not preserve the exact authored-camera state.");
                }
            }
            return common ??
                throw new InvalidDataException("Stage-camera backend frames are missing.");
        }
    }

    private static bool IsHex(string? value, int length) =>
        value is not null &&
        value.Length == length &&
        value.All(Uri.IsHexDigit);

    private void ValidateCameraTransitions()
    {
        if (CameraTransitions
            .GroupBy(value => value.Backend, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException(
                "Viewer camera evidence contains duplicate backend transitions.");
        }

        foreach (string backend in States
            .Select(state => state.Backend)
            .Where(backend => backend is "Storm" or "D3D12" or "Vulkan")
            .Distinct(StringComparer.Ordinal))
        {
            if (!CameraTransitions.Any(transition =>
                string.Equals(transition.Backend, backend, StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Viewer camera evidence is missing the {backend} transition.");
            }
        }

        foreach (ViewerCameraTransitionEvidence transition in CameraTransitions)
        {
            ViewerStateEvidence before = FindState(
                transition.Backend,
                transition.AutomaticBeforePhase);
            ViewerStateEvidence explicitState = FindState(
                transition.Backend,
                transition.ExplicitPhase);
            ViewerStateEvidence restored = FindState(
                transition.Backend,
                transition.AutomaticRestoredPhase);
            ViewerPixelEvidence beforePixel = FindPixel(
                transition.AutomaticPixelSha256,
                transition.AutomaticPixelArtifact);
            ViewerPixelEvidence explicitPixel = FindPixel(
                transition.ExplicitPixelSha256,
                transition.ExplicitPixelArtifact);
            ViewerPixelEvidence restoredPixel = FindPixel(
                transition.RestoredPixelSha256,
                transition.RestoredPixelArtifact);

            bool storm = string.Equals(
                transition.Backend,
                "Storm",
                StringComparison.Ordinal);
            if (!string.Equals(
                    before.CameraMode,
                    nameof(CameraMode.Automatic),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    explicitState.CameraMode,
                    nameof(CameraMode.Matrices),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    restored.CameraMode,
                    nameof(CameraMode.Automatic),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    before.CameraSignature,
                    transition.AutomaticCameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    explicitState.CameraSignature,
                    transition.ExplicitCameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    restored.CameraSignature,
                    transition.RestoredCameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    before.CameraPayload,
                    restored.CameraPayload,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    before.CameraSignature,
                    restored.CameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    before.CameraPayload,
                    explicitState.CameraPayload,
                    StringComparison.Ordinal) ||
                string.Equals(
                    before.CameraSignature,
                    explicitState.CameraSignature,
                    StringComparison.OrdinalIgnoreCase) ||
                !transition.ExactReferencesPreserved ||
                !before.ExactReferencePreserved ||
                !explicitState.ExactReferencePreserved ||
                !restored.ExactReferencePreserved ||
                !string.Equals(
                    beforePixel.Backend,
                    transition.Backend,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    explicitPixel.Backend,
                    transition.Backend,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    restoredPixel.Backend,
                    transition.Backend,
                    StringComparison.Ordinal) ||
                string.Equals(
                    beforePixel.Sha256,
                    explicitPixel.Sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    restoredPixel.Sha256,
                    explicitPixel.Sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                (storm &&
                 (!transition.AsyncCoalescingValidated ||
                  transition.LatestRequestedRevision != explicitState.Revision ||
                  !string.Equals(
                      transition.LatestRequestedCameraSignature,
                      explicitState.NativeCameraSignature,
                      StringComparison.OrdinalIgnoreCase) ||
                  !string.Equals(
                      transition.LatestRenderedCameraSignature,
                      explicitState.NativeCameraSignature,
                      StringComparison.OrdinalIgnoreCase))) ||
                (!storm &&
                 (transition.AsyncCoalescingValidated ||
                  transition.LatestRequestedRevision != 0 ||
                  !string.IsNullOrEmpty(transition.LatestRequestedCameraSignature) ||
                  !string.IsNullOrEmpty(transition.LatestRenderedCameraSignature))))
            {
                throw new InvalidDataException(
                    $"Viewer explicit-camera evidence is invalid for {transition.Backend}.");
            }
        }
    }

    private ViewerStateEvidence FindState(string backend, string phase)
    {
        ViewerStateEvidence[] matches = States.Where(state =>
            string.Equals(state.Backend, backend, StringComparison.Ordinal) &&
            string.Equals(state.Phase, phase, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Viewer camera state evidence is missing or duplicated for {backend}/{phase}.");
        }
        return matches[0];
    }

    private ViewerPixelEvidence FindPixel(string sha256, string artifact)
    {
        ViewerPixelEvidence[] matches = Pixels.Where(pixel =>
            string.Equals(pixel.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pixel.Artifact, artifact, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Viewer camera pixel evidence is missing or duplicated for {artifact}.");
        }
        return matches[0];
    }

    private static void ValidateWin32Messages(ViewerInputEvidence input, bool storm)
    {
        string[] topLevelMessages =
        [
            "WM_DPICHANGED(change)",
            "WM_DPICHANGED(restore)",
            "WM_KILLFOCUS",
            "WM_MOUSEMOVE",
            "WM_LBUTTONDOWN",
            "WM_LBUTTONUP",
            "WM_MOUSEWHEEL",
            "WM_KEYDOWN",
            "WM_KEYUP"
        ];
        string[] childMessages =
        [
            "WM_SETFOCUS",
            "WM_MOUSEMOVE",
            "WM_LBUTTONDOWN",
            "WM_LBUTTONUP",
            "WM_MOUSEWHEEL",
            "WM_KEYDOWN",
            "WM_KEYUP"
        ];
        foreach (string message in topLevelMessages)
        {
            if (input.Win32Messages.Count(item =>
                    string.Equals(item.Target, "ViewerTopLevel", StringComparison.Ordinal) &&
                    string.Equals(item.Message, message, StringComparison.Ordinal)) != 1)
            {
                throw new InvalidDataException(
                    $"Viewer top-level Win32 message evidence is missing {message}.");
            }

        }
        if (storm)
        {
            foreach (string message in childMessages)
            {
                if (input.Win32Messages.Count(item =>
                        string.Equals(item.Target, "StormChild", StringComparison.Ordinal) &&
                        string.Equals(item.Message, message, StringComparison.Ordinal)) != 1)
                {
                    throw new InvalidDataException(
                        $"Storm child Win32 message evidence is missing {message}.");
                }
            }
        }
        else if (input.Win32Messages.Any(item =>
                     string.Equals(item.Target, "StormChild", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "A non-Storm input record contained Storm child messages.");
        }

        foreach (ViewerWin32MessageEvidence message in input.Win32Messages)
        {
            if (!string.Equals(message.Api, "SendMessageTimeoutW", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(message.Hwnd) ||
                string.IsNullOrWhiteSpace(message.ApiReturn) ||
                !message.ApiSucceeded ||
                message.LastError != 0 ||
                !message.WndProcObserved ||
                !message.HandlerObserved ||
                message.Synthesized ||
                !MessageCounterAdvanced(message))
            {
                throw new InvalidDataException(
                    $"Win32 message {message.Target}/{message.Message} was not OS-routed.");
            }
        }

        ViewerWin32MessageEvidence changed = input.Win32Messages.Single(item =>
            string.Equals(item.Target, "ViewerTopLevel", StringComparison.Ordinal) &&
            string.Equals(item.Message, "WM_DPICHANGED(change)", StringComparison.Ordinal));
        ViewerWin32MessageEvidence restored = input.Win32Messages.Single(item =>
            string.Equals(item.Target, "ViewerTopLevel", StringComparison.Ordinal) &&
            string.Equals(item.Message, "WM_DPICHANGED(restore)", StringComparison.Ordinal));
        if (changed.Before.Dpi != input.NativeDpiBefore ||
            changed.After.Dpi != input.NativeDpiObserved ||
            restored.Before.Dpi != input.NativeDpiObserved ||
            restored.After.Dpi != input.NativeDpiAfter)
        {
            throw new InvalidDataException(
                "Win32 DPI message counters do not match the aggregate DPI transition.");
        }
    }

    private static void ValidateXTestInjections(ViewerInputEvidence input, bool storm)
    {
        ViewerXTestInjectionEvidence[] injections = input.XTestInjections ?? [];
        if (!input.DeliveryApi.Contains("XTest", StringComparison.Ordinal) ||
            injections.Length == 0 ||
            !injections.Any(value =>
                string.Equals(value.Target, "ViewerTopLevel", StringComparison.Ordinal)) ||
            storm && !injections.Any(value =>
                string.Equals(value.Target, "StormChild", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Linux XTest input provenance is incomplete.");
        }

        string[] requiredCalls =
        [
            "XTestQueryExtension",
            "XSetInputFocus",
            "XTestFakeMotionEvent",
            "XTestFakeButtonEvent(button=1,press)",
            "XTestFakeButtonEvent(button=1,release)",
            "XTestFakeButtonEvent(button=4,press)",
            "XTestFakeButtonEvent(button=4,release)",
            "XTestFakeButtonEvent(button=5,press)",
            "XTestFakeButtonEvent(button=5,release)",
            "XTestFakeKeyEvent(press)",
            "XTestFakeKeyEvent(release)",
            "XFlush",
            "XSync"
        ];
        foreach (ViewerXTestInjectionEvidence injection in injections)
        {
            if (!string.Equals(injection.InjectionApi, "XTest", StringComparison.Ordinal) ||
                !injection.ExtensionAvailable ||
                injection.ExtensionMajor < 1 ||
                injection.ExtensionMinor < 0 ||
                string.IsNullOrWhiteSpace(injection.Display) ||
                !injection.Xid.StartsWith("0x", StringComparison.Ordinal) ||
                !injection.ServerGenerated ||
                injection.Calls.Length < requiredCalls.Length ||
                injection.Calls.Any(call =>
                    call.Result == 0 ||
                    call.Api.Contains("XSendEvent", StringComparison.OrdinalIgnoreCase)) ||
                requiredCalls.Any(required =>
                    !injection.Calls.Any(call =>
                        string.Equals(call.Api, required, StringComparison.Ordinal))))
            {
                throw new InvalidDataException(
                    $"Linux XTest provenance is invalid for {injection.Target}.");
            }
            if (storm &&
                string.Equals(injection.Target, "StormChild", StringComparison.Ordinal) &&
                !injection.NativeSendEventFalseObserved)
            {
                throw new InvalidDataException(
                    "Storm native diagnostics did not observe server-routed XTest events.");
            }
        }
    }

    private static bool MessageCounterAdvanced(ViewerWin32MessageEvidence message)
    {
        ViewerInputCounterEvidence before = message.Before;
        ViewerInputCounterEvidence after = message.After;
        bool child = string.Equals(message.Target, "StormChild", StringComparison.Ordinal);
        return message.Message switch
        {
            "WM_DPICHANGED(change)" =>
                after.ScalingEvents > before.ScalingEvents &&
                after.ResizeEvents > before.ResizeEvents &&
                after.Dpi != before.Dpi &&
                (after.PhysicalWidth != before.PhysicalWidth ||
                 after.PhysicalHeight != before.PhysicalHeight),
            "WM_DPICHANGED(restore)" =>
                after.ScalingEvents > before.ScalingEvents &&
                after.ResizeEvents > before.ResizeEvents &&
                after.Dpi != before.Dpi,
            "WM_KILLFOCUS" or "WM_SETFOCUS" => child
                ? after.NativeFocusEvents > before.NativeFocusEvents
                : after.FocusEvents > before.FocusEvents,
            "WM_MOUSEMOVE" => child
                ? after.NativePointerEvents > before.NativePointerEvents
                : after.PointerMoves > before.PointerMoves,
            "WM_LBUTTONDOWN" or "WM_LBUTTONUP" => child
                ? after.NativePointerEvents > before.NativePointerEvents
                : after.PointerButtons > before.PointerButtons,
            "WM_MOUSEWHEEL" => child
                ? after.NativeWheelEvents > before.NativeWheelEvents
                : after.WheelEvents > before.WheelEvents,
            "WM_KEYDOWN" or "WM_KEYUP" => child
                ? after.NativeKeyEvents > before.NativeKeyEvents
                : after.KeyEvents > before.KeyEvents,
            _ => false
        };
    }

    private static bool IsSupportedCaptureApi(
        string captureApi,
        bool windows,
        bool linux) =>
        (windows && string.Equals(
            captureApi,
            "PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush",
            StringComparison.Ordinal)) ||
        (linux && string.Equals(
            captureApi,
            "XGetImage(ZPixmap,viewer-shell)",
            StringComparison.Ordinal)) ||
        string.Equals(
            captureApi,
            StormCaptureApi,
            StringComparison.Ordinal);
}

internal sealed class ViewerSwitchingEvidenceSession
{
    private readonly string _path;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly List<ViewerStateEvidence> _states = [];
    private readonly List<ViewerPixelEvidence> _pixels = [];
    private readonly List<ViewerInputEvidence> _inputs = [];
    private readonly Dictionary<string, ViewerCompositionEvidence> _compositions =
        new(StringComparer.Ordinal);
    private readonly List<ViewerHwndEvidence> _windowOwnership = [];
    private readonly List<ViewerCameraTransitionEvidence> _cameraTransitions = [];
    private readonly List<ViewerNativeNavigationEvidence> _nativeNavigation = [];
    private ViewerCleanupQuarantineEvidence? _cleanupQuarantine;
    private ViewerStageCameraEvidence? _stageCamera;
    private readonly ViewerWindowsHwndObserver _hwndObserver = new();

    internal ViewerSwitchingEvidenceSession(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    internal string ArtifactDirectory =>
        Path.GetDirectoryName(_path)
        ?? throw new InvalidOperationException("Evidence path has no parent directory.");

    internal ViewerStateEvidence RecordState(
        RenderBackendKind backend,
        StageRenderState state,
        string phase,
        bool exactReferencePreserved,
        int schedulerIdentity,
        int renderSourceIdentity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ViewerCameraDescriptor camera = ViewerCameraEvidence.Describe(state.Camera);
        var evidence = new ViewerStateEvidence(
            backend.ToString(),
            phase,
            state.Revision,
            state.Stage.Identifier,
            state.Time.TimeCode,
            camera.Mode,
            camera.Payload,
            camera.Signature,
            camera.NativeSignature,
            [.. state.Selection.PrimPaths],
            state.Display.Purposes.ToString(),
            state.Display.Visibility.ToString(),
            state.Display.DrawMode.ToString(),
            state.Viewport.Width,
            state.Viewport.Height,
            exactReferencePreserved,
            schedulerIdentity,
            renderSourceIdentity);
        _states.Add(evidence);
        return evidence;
    }

    internal void RecordPixel(ViewerPixelEvidence pixel)
    {
        ArgumentNullException.ThrowIfNull(pixel);
        _pixels.Add(pixel);
    }

    internal void RecordInput(ViewerInputEvidence input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _inputs.Add(input);
    }

    internal void RecordComposition(ViewerCompositionEvidence composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        _compositions[composition.Backend] = composition;
    }

    internal ViewerHwndEvidence RecordWindowOwnership(
        Window window,
        RendererSwitchingViewport viewport,
        RenderBackendKind backend,
        string phase)
    {
        ViewerHwndEvidence observation =
            _hwndObserver.Observe(window, viewport, backend, phase);
        _windowOwnership.Add(observation);
        return observation;
    }

    internal ViewerHwndEvidence ObserveWindowOwnership(
        Window window,
        RendererSwitchingViewport viewport,
        RenderBackendKind backend,
        string phase) =>
        _hwndObserver.Observe(window, viewport, backend, phase);

    internal void RecordCleanupQuarantine(ViewerCleanupQuarantineEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        _cleanupQuarantine = evidence;
    }

    internal void RecordCameraTransition(ViewerCameraTransitionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        _cameraTransitions.Add(evidence);
    }

    internal void RecordNativeNavigation(ViewerNativeNavigationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        _nativeNavigation.Add(evidence);
    }

    internal void RecordStageCamera(ViewerStageCameraEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (_stageCamera is not null)
        {
            throw new InvalidOperationException(
                "Stage-camera evidence was already recorded for this process.");
        }
        _stageCamera = evidence;
    }

    internal void Complete(
        Window window,
        ViewerResourceEvidence resources)
    {
        ArgumentNullException.ThrowIfNull(window);
        IPlatformHandle handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Avalonia did not expose a window handle.");
        string[] loadedArtifacts = GetLoadedArtifactPaths(AppContext.BaseDirectory);
        string runtimeCompositor = string.Equals(
            handle.HandleDescriptor,
            "HWND",
            StringComparison.OrdinalIgnoreCase)
            ? ClassifyWindowsCompositor()
            : Program.GetConfiguredShellMode();
        var artifact = new ViewerSwitchingEvidenceArtifact(
            ViewerSwitchingEvidenceArtifact.CurrentSchemaVersion,
            ViewerStartupOptions.SwitchingEvidenceScenario,
            runtimeCompositor,
            handle.HandleDescriptor ??
                throw new InvalidOperationException(
                    "Avalonia returned a platform handle without a descriptor."),
            checked((int)OpenUsdStormChildRuntime.AbiVersion),
            _startedAt,
            DateTimeOffset.UtcNow,
            loadedArtifacts,
            [.. _states],
            [.. _pixels],
            [.. _inputs],
            [.. _compositions.Values.OrderBy(value => value.Backend, StringComparer.Ordinal)],
            [.. _windowOwnership],
            [.. _cameraTransitions],
            resources,
            _cleanupQuarantine,
            [.. _nativeNavigation],
            _stageCamera);
        Directory.CreateDirectory(ArtifactDirectory);
        using FileStream stream = File.Create(_path);
        JsonSerializer.Serialize(
            stream,
            artifact,
            ViewerSwitchingEvidenceJsonContext.Default.ViewerSwitchingEvidenceArtifact);
        stream.Flush();
        artifact.Validate();
    }

    private string ClassifyWindowsCompositor()
    {
        ViewerCompositionEvidence[] observations = [.. _compositions.Values];
        if (observations.Length == 0 ||
            observations.Any(observation =>
                observation.SuccessfulImports < 1 ||
                observation.SuccessfulPresents < 1 ||
                observation.DeviceLuid.Length != 16 ||
                !observation.SupportedImageHandleTypes.Contains(
                    "D3D11TextureNtHandle",
                    StringComparer.Ordinal) ||
                !string.Equals(
                    observation.UsedImageHandleType,
                    "D3D11TextureNtHandle",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    observation.SynchronizationKind,
                    nameof(CompositionFrameSynchronizationKind.KeyedMutex),
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The Windows compositor could not be classified from successful runtime imports.");
        }
        return "ANGLE/D3D11 (runtime-observed)";
    }

    private static string[] GetLoadedArtifactPaths(string root)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string location = assembly.Location;
            if (!string.IsNullOrWhiteSpace(location) &&
                Path.GetFullPath(location).StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(Path.GetFullPath(location));
            }
        }
        using Process process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            string? location = module.FileName;
            if (!string.IsNullOrWhiteSpace(location) &&
                Path.GetFullPath(location).StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(Path.GetFullPath(location));
            }
        }
        return [.. paths.Order(StringComparer.OrdinalIgnoreCase)];
    }
}

internal readonly record struct ViewerCaptureRectangle(int X, int Y, int Width, int Height);

internal static class ViewerWindowsCapture
{
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint PwClientOnly = 0x1;
    private const uint PwRenderFullContent = 0x2;
    private const uint SrcCopy = 0x00CC0020;

    internal static async Task<ViewerPixelEvidence> CaptureAsync(
        Window window,
        Control viewport,
        RenderBackendKind backend,
        string phase,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewport);
        cancellationToken.ThrowIfCancellationRequested();
        if (backend == RenderBackendKind.Storm)
        {
            RendererSwitchingViewport switching =
                viewport as RendererSwitchingViewport
                ?? throw new InvalidOperationException(
                    "Storm framebuffer evidence requires the switching viewport.");
            string stormPath = Path.Combine(
                artifactDirectory,
                $"{phase}-storm.bmp");
            return await CaptureStormAsync(
                switching,
                phase,
                stormPath,
                cancellationToken);
        }
        nint nativeWindow =
            (viewport as RendererSwitchingViewport)?.GetEvidenceNativeWindow() ?? 0;
        if (nativeWindow != 0)
        {
            throw new InvalidOperationException(
                "A native child framebuffer must use the Storm ABI v4 capture API.");
        }
        IPlatformHandle handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Avalonia did not expose a window handle.");
        Point origin = viewport.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("Could not locate the viewport.");
        double scaling = window.RenderScaling;
        var crop = new ViewerCaptureRectangle(
            checked((int)Math.Round(origin.X * scaling)),
            checked((int)Math.Round(origin.Y * scaling)),
            checked((int)Math.Round(viewport.Bounds.Width * scaling)),
            checked((int)Math.Round(viewport.Bounds.Height * scaling)));
        string path = Path.Combine(
            artifactDirectory,
            $"{phase}-{backend.ToString().ToLowerInvariant()}.bmp");
        return await Task.Run(
            () => OperatingSystem.IsLinux()
                ? CaptureX11(handle.Handle, crop, backend, phase, path)
                : Capture(handle.Handle, crop, backend, phase, path));
    }

    internal static Task<ViewerPixelEvidence> CaptureLinuxShellAsync(
        Window window,
        Control viewport,
        RenderBackendKind backend,
        string phase,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Linux shell capture requires X11 or compositor-managed XWayland.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        IPlatformHandle handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Avalonia did not expose a window handle.");
        Point origin = viewport.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("Could not locate the viewport.");
        double scaling = window.RenderScaling;
        var crop = new ViewerCaptureRectangle(
            checked((int)Math.Round(origin.X * scaling)),
            checked((int)Math.Round(origin.Y * scaling)),
            checked((int)Math.Round(viewport.Bounds.Width * scaling)),
            checked((int)Math.Round(viewport.Bounds.Height * scaling)));
        string path = Path.Combine(
            artifactDirectory,
            $"{phase}-shell-{backend.ToString().ToLowerInvariant()}.bmp");
        return Task.Run(
            () => CaptureX11(handle.Handle, crop, backend, phase, path),
            cancellationToken);
    }

    private static async Task<ViewerPixelEvidence> CaptureStormAsync(
        RendererSwitchingViewport viewport,
        string phase,
        string path,
        CancellationToken cancellationToken)
    {
        OpenUsdStormFramebufferCapture capture =
            await viewport.CaptureStormFramebufferAsync(cancellationToken);
        ReadOnlyMemory<byte> rgba = capture.RgbaPixels;
        int expectedBytes = OpenUsdStormChildRuntime.GetCaptureByteCount(
            capture.Width,
            capture.Height);
        if (rgba.Length != expectedBytes ||
            capture.PixelCount != (ulong)(capture.Width * (long)capture.Height))
        {
            throw new InvalidDataException(
                "Storm returned incomplete framebuffer pixels.");
        }

        byte[] bgra = ConvertBottomUpRgbaToTopDownBgra(
            rgba.Span,
            capture.Width,
            capture.Height);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteBitmap(path, capture.Width, capture.Height, bgra);
        return new ViewerPixelEvidence(
            RenderBackendKind.Storm.ToString(),
            phase,
            ViewerSwitchingEvidenceArtifact.StormCaptureApi,
            Convert.ToHexString(SHA256.HashData(rgba.Span)),
            capture.Width,
            capture.Height,
            RgbaToBgraHex(capture.BackgroundRgba),
            checked((long)capture.NonBackgroundPixelCount),
            (byte)capture.AverageRgba,
            (byte)(capture.AverageRgba >> 8),
            (byte)(capture.AverageRgba >> 16),
            [
                SampleRgba(rgba.Span, capture.Width, capture.Width / 4, capture.Height / 4),
                SampleRgba(rgba.Span, capture.Width, capture.Width / 2, capture.Height / 4),
                SampleRgba(
                    rgba.Span,
                    capture.Width,
                    capture.Width * 3 / 4,
                    capture.Height / 2),
                SampleRgba(rgba.Span, capture.Width, capture.Width / 2, capture.Height / 2),
                SampleRgba(
                    rgba.Span,
                    capture.Width,
                    capture.Width / 4,
                    capture.Height * 3 / 4),
                SampleRgba(
                    rgba.Span,
                    capture.Width,
                    capture.Width * 3 / 4,
                    capture.Height * 3 / 4)
            ],
            Path.GetFullPath(path));
    }

    private static byte[] ConvertBottomUpRgbaToTopDownBgra(
        ReadOnlySpan<byte> rgba,
        int width,
        int height)
    {
        byte[] bgra = GC.AllocateUninitializedArray<byte>(rgba.Length);
        int rowBytes = checked(width * 4);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = rgba.Slice(
                checked((height - 1 - y) * rowBytes),
                rowBytes);
            Span<byte> destination = bgra.AsSpan(y * rowBytes, rowBytes);
            for (int offset = 0; offset < rowBytes; offset += 4)
            {
                destination[offset] = source[offset + 2];
                destination[offset + 1] = source[offset + 1];
                destination[offset + 2] = source[offset];
                destination[offset + 3] = source[offset + 3];
            }
        }
        return bgra;
    }

    private static string RgbaToBgraHex(uint rgba) =>
        Convert.ToHexString(
        [
            (byte)(rgba >> 16),
            (byte)(rgba >> 8),
            (byte)rgba,
            (byte)(rgba >> 24)
        ]);

    private static string SampleRgba(
        ReadOnlySpan<byte> rgba,
        int width,
        int x,
        int y)
    {
        int offset = checked((y * width + x) * 4);
        return Convert.ToHexString(
        [
            rgba[offset + 2],
            rgba[offset + 1],
            rgba[offset],
            rgba[offset + 3]
        ]);
    }

    private static ViewerPixelEvidence CaptureX11(
        nint window,
        ViewerCaptureRectangle crop,
        RenderBackendKind backend,
        string phase,
        string path)
    {
        nint display = XOpenDisplay(0);
        if (display == 0)
        {
            throw new InvalidOperationException("Could not open DISPLAY for Linux shell capture.");
        }
        nint image = 0;
        nint imageData = 0;
        try
        {
            _ = XSync(display, false);
            if (XGetWindowAttributes(display, window, out XWindowAttributes attributes) == 0 ||
                attributes.MapState != IsViewable)
            {
                throw new InvalidOperationException(
                    "The Viewer X11 shell is not mapped and viewable.");
            }
            if (crop.X < 0 ||
                crop.Y < 0 ||
                crop.Width <= 0 ||
                crop.Height <= 0 ||
                crop.X + crop.Width > attributes.Width ||
                crop.Y + crop.Height > attributes.Height)
            {
                throw new InvalidOperationException(
                    "The Linux viewport capture rectangle was invalid.");
            }
            image = XGetImage(
                display,
                window,
                crop.X,
                crop.Y,
                checked((uint)crop.Width),
                checked((uint)crop.Height),
                nuint.MaxValue,
                ZPixmap);
            if (image == 0)
            {
                throw new InvalidOperationException("XGetImage failed for the Viewer shell.");
            }
            XImage native = Marshal.PtrToStructure<XImage>(image);
            imageData = native.Data;
            if (native.BitsPerPixel != 32 || native.Data == 0)
            {
                throw new InvalidOperationException(
                    $"Unsupported XImage format: {native.BitsPerPixel} bits per pixel.");
            }
            byte[] source = new byte[checked(native.BytesPerLine * native.Height)];
            Marshal.Copy(native.Data, source, 0, source.Length);
            byte[] bgra = new byte[checked(crop.Width * crop.Height * 4)];
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    int sourceOffset = checked(y * native.BytesPerLine + x * 4);
                    uint pixel = native.ByteOrder == LsbFirst
                        ? BinaryPrimitives.ReadUInt32LittleEndian(
                            source.AsSpan(sourceOffset, 4))
                        : BinaryPrimitives.ReadUInt32BigEndian(
                            source.AsSpan(sourceOffset, 4));
                    int destination = checked((y * crop.Width + x) * 4);
                    bgra[destination] = ExtractChannel(pixel, native.BlueMask);
                    bgra[destination + 1] = ExtractChannel(pixel, native.GreenMask);
                    bgra[destination + 2] = ExtractChannel(pixel, native.RedMask);
                    bgra[destination + 3] = 255;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            WriteBitmap(path, crop.Width, crop.Height, bgra);
            return Analyze(
                backend,
                phase,
                "XGetImage(ZPixmap,viewer-shell)",
                path,
                crop.Width,
                crop.Height,
                bgra);
        }
        finally
        {
            if (image != 0)
            {
                if (imageData != 0)
                {
                    _ = XFree(imageData);
                }
                _ = XFree(image);
            }
            _ = XCloseDisplay(display);
        }
    }

    private static byte ExtractChannel(uint pixel, nuint nativeMask)
    {
        uint mask = checked((uint)nativeMask);
        if (mask == 0)
        {
            return 0;
        }
        int shift = BitOperations.TrailingZeroCount(mask);
        uint maximum = mask >> shift;
        uint value = (pixel & mask) >> shift;
        return checked((byte)((value * 255u + maximum / 2u) / maximum));
    }

    private static ViewerPixelEvidence CaptureFull(
        nint window,
        RenderBackendKind backend,
        string phase,
        string path)
    {
        ThrowIfFalse(GetClientRect(window, out NativeRect client), "GetClientRect");
        return Capture(
            window,
            new ViewerCaptureRectangle(
                0,
                0,
                checked(client.Right - client.Left),
                checked(client.Bottom - client.Top)),
            backend,
            phase,
            path,
            screenCapture: true);
    }

    private static ViewerPixelEvidence Capture(
        nint window,
        ViewerCaptureRectangle crop,
        RenderBackendKind backend,
        string phase,
        string path,
        bool screenCapture = false)
    {
        string captureApi = screenCapture
            ? "BitBltDesktop"
            : "PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)+DwmFlush";
        ThrowIfFalse(GetClientRect(window, out NativeRect client), "GetClientRect");
        int clientWidth = checked(client.Right - client.Left);
        int clientHeight = checked(client.Bottom - client.Top);
        if (crop.X < 0 ||
            crop.Y < 0 ||
            crop.Width <= 0 ||
            crop.Height <= 0 ||
            crop.X + crop.Width > clientWidth ||
            crop.Y + crop.Height > clientHeight)
        {
            throw new InvalidOperationException("The viewport capture rectangle was invalid.");
        }

        ThrowIfFailed(DwmFlush(), "DwmFlush");
        nint memoryDc = CreateCompatibleDC(0);
        if (memoryDc == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC failed.");
        }
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                    Width = clientWidth,
                    Height = -clientHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb
                }
            };
            bitmap = CreateDIBSection(
                memoryDc,
                in bitmapInfo,
                DibRgbColors,
                out nint pixels,
                0,
                0);
            if (bitmap == 0 || pixels == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "CreateDIBSection failed.");
            }
            previous = SelectObject(memoryDc, bitmap);
            if (previous == 0 || previous == -1)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "SelectObject failed.");
            }
            if (screenCapture)
            {
                var origin = new NativePoint();
                ThrowIfFalse(ClientToScreen(window, ref origin), "ClientToScreen");
                nint screenDc = GetDC(0);
                if (screenDc == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "GetDC failed.");
                }
                try
                {
                    int virtualLeft = GetSystemMetrics(SmXVirtualScreen);
                    int virtualTop = GetSystemMetrics(SmYVirtualScreen);
                    int virtualRight =
                        virtualLeft + GetSystemMetrics(SmCxVirtualScreen);
                    int virtualBottom =
                        virtualTop + GetSystemMetrics(SmCyVirtualScreen);
                    int sourceLeft = Math.Max(origin.X, virtualLeft);
                    int sourceTop = Math.Max(origin.Y, virtualTop);
                    int sourceRight = Math.Min(origin.X + clientWidth, virtualRight);
                    int sourceBottom = Math.Min(origin.Y + clientHeight, virtualBottom);
                    int copyWidth = sourceRight - sourceLeft;
                    int copyHeight = sourceBottom - sourceTop;
                    if (copyWidth <= 0 || copyHeight <= 0)
                    {
                        throw new InvalidOperationException(
                            "The Storm child viewport is outside the virtual screen.");
                    }
                    bool copied = BitBlt(
                        memoryDc,
                        sourceLeft - origin.X,
                        sourceTop - origin.Y,
                        copyWidth,
                        copyHeight,
                        screenDc,
                        sourceLeft,
                        sourceTop,
                        SrcCopy);
                    if (!copied)
                    {
                        nint windowDc = GetDC(window);
                        if (windowDc != 0)
                        {
                            try
                            {
                                copied = BitBlt(
                                    memoryDc,
                                    0,
                                    0,
                                    clientWidth,
                                    clientHeight,
                                    windowDc,
                                    0,
                                    0,
                                    SrcCopy);
                                if (copied)
                                {
                                    captureApi = "BitBltWindow";
                                }
                            }
                            finally
                            {
                                _ = ReleaseDC(window, windowDc);
                            }
                        }
                    }
                    if (!copied)
                    {
                        captureApi = "PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)";
                        ThrowIfFalse(
                            PrintWindow(
                                window,
                                memoryDc,
                                PwClientOnly | PwRenderFullContent),
                            "BitBlt and PrintWindow");
                    }
                }
                finally
                {
                    _ = ReleaseDC(0, screenDc);
                }
            }
            else
            {
                ThrowIfFalse(
                    PrintWindow(window, memoryDc, PwClientOnly | PwRenderFullContent),
                    "PrintWindow");
            }
            ThrowIfFailed(DwmFlush(), "DwmFlush");
            byte[] clientPixels = new byte[checked(clientWidth * clientHeight * 4)];
            Marshal.Copy(pixels, clientPixels, 0, clientPixels.Length);
            byte[] viewportPixels = Crop(clientPixels, clientWidth, crop);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            WriteBitmap(path, crop.Width, crop.Height, viewportPixels);
            return Analyze(
                backend,
                phase,
                captureApi,
                path,
                crop.Width,
                crop.Height,
                viewportPixels);
        }
        finally
        {
            if (previous != 0 && previous != -1)
            {
                _ = SelectObject(memoryDc, previous);
            }
            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }
            _ = DeleteDC(memoryDc);
        }
    }

    private static ViewerPixelEvidence Analyze(
        RenderBackendKind backend,
        string phase,
        string captureApi,
        string path,
        int width,
        int height,
        byte[] pixels)
    {
        var frequencies = new Dictionary<uint, int>();
        long redTotal = 0;
        long greenTotal = 0;
        long blueTotal = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte blue = pixels[offset];
            byte green = pixels[offset + 1];
            byte red = pixels[offset + 2];
            uint color = (uint)(blue | green << 8 | red << 16 | pixels[offset + 3] << 24);
            frequencies[color] = frequencies.GetValueOrDefault(color) + 1;
            redTotal += red;
            greenTotal += green;
            blueTotal += blue;
        }
        uint background = frequencies.MaxBy(pair => pair.Value).Key;
        byte backgroundBlue = (byte)background;
        byte backgroundGreen = (byte)(background >> 8);
        byte backgroundRed = (byte)(background >> 16);
        long nonBackground = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            int delta = Math.Max(
                Math.Abs(pixels[offset] - backgroundBlue),
                Math.Max(
                    Math.Abs(pixels[offset + 1] - backgroundGreen),
                    Math.Abs(pixels[offset + 2] - backgroundRed)));
            if (delta >= 12)
            {
                nonBackground++;
            }
        }
        long count = width * (long)height;
        return new ViewerPixelEvidence(
            backend.ToString(),
            phase,
            captureApi,
            Convert.ToHexString(SHA256.HashData(pixels)),
            width,
            height,
            background.ToString("X8", CultureInfo.InvariantCulture),
            nonBackground,
            redTotal / (double)count,
            greenTotal / (double)count,
            blueTotal / (double)count,
            [
                Sample(pixels, width, width / 4, height / 4),
                Sample(pixels, width, width / 2, height / 4),
                Sample(pixels, width, width * 3 / 4, height / 2),
                Sample(pixels, width, width / 2, height / 2),
                Sample(pixels, width, width / 4, height * 3 / 4),
                Sample(pixels, width, width * 3 / 4, height * 3 / 4)
            ],
            Path.GetFullPath(path));
    }

    private static byte[] Crop(
        byte[] pixels,
        int clientWidth,
        ViewerCaptureRectangle crop)
    {
        int rowBytes = checked(crop.Width * 4);
        byte[] result = new byte[checked(rowBytes * crop.Height)];
        for (int row = 0; row < crop.Height; row++)
        {
            Buffer.BlockCopy(
                pixels,
                checked(((crop.Y + row) * clientWidth + crop.X) * 4),
                result,
                row * rowBytes,
                rowBytes);
        }
        return result;
    }

    private static string Sample(byte[] pixels, int width, int x, int y)
    {
        int offset = checked((y * width + x) * 4);
        return Convert.ToHexString(pixels.AsSpan(offset, 4));
    }

    private static void WriteBitmap(string path, int width, int height, byte[] pixels)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        int dataOffset = fileHeaderSize + infoHeaderSize;
        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(checked(dataOffset + pixels.Length));
        writer.Write(0);
        writer.Write(dataOffset);
        writer.Write(infoHeaderSize);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(BiRgb);
        writer.Write(pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
    }

    private static void ThrowIfFalse(bool result, string operation)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{operation} failed.");
        }
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        internal BitmapInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        internal uint[]? Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int ZPixmap = 2;
    private const int LsbFirst = 0;
    private const int IsViewable = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowAttributes
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
        internal int BorderWidth;
        internal int Depth;
        internal nint Visual;
        internal nint Root;
        internal int Class;
        internal int BitGravity;
        internal int WinGravity;
        internal int BackingStore;
        internal nuint BackingPlanes;
        internal nuint BackingPixel;
        internal int SaveUnder;
        internal nint Colormap;
        internal int MapInstalled;
        internal int MapState;
        internal nint AllEventMasks;
        internal nint YourEventMask;
        internal nint DoNotPropagateMask;
        internal int OverrideRedirect;
        internal nint Screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        internal int Width;
        internal int Height;
        internal int XOffset;
        internal int Format;
        internal nint Data;
        internal int ByteOrder;
        internal int BitmapUnit;
        internal int BitmapBitOrder;
        internal int BitmapPad;
        internal int Depth;
        internal int BytesPerLine;
        internal int BitsPerPixel;
        internal nuint RedMask;
        internal nuint GreenMask;
        internal nuint BlueMask;
        internal nint ObData;
        internal nint Functions;
    }

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XSync(
        nint display,
        [MarshalAs(UnmanagedType.Bool)] bool discard);

    [DllImport("libX11.so.6")]
    private static extern int XGetWindowAttributes(
        nint display,
        nint window,
        out XWindowAttributes attributes);

    [DllImport("libX11.so.6")]
    private static extern nint XGetImage(
        nint display,
        nint drawable,
        int x,
        int y,
        uint width,
        uint height,
        nuint planeMask,
        int format);

    [DllImport("libX11.so.6")]
    private static extern int XFree(nint data);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint target, uint flags);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        in BitmapInfo bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destination,
        int x,
        int y,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmFlush();
}

[JsonSerializable(typeof(ViewerSwitchingEvidenceArtifact))]
internal sealed partial class ViewerSwitchingEvidenceJsonContext : JsonSerializerContext;
