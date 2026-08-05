// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal static class ViewerStartupOptions
{
    private static readonly object StatusGate = new();

    internal static string? PluginPath { get; private set; }

    internal static string? StagePath { get; private set; }

    internal static string? StatusFile { get; private set; }

    internal static string? LogFile { get; private set; }

    internal static string? SoakArtifactPath { get; private set; }

    internal static bool SharedStageSoak { get; private set; }

    internal static int SoakSeconds { get; private set; } = 90;

    internal static string Renderer { get; private set; } = "Auto";

    internal static bool LiveEditSmoke { get; private set; }

    internal static int SwitchSoakCount { get; private set; }

    internal static int SwitchSoakSeconds { get; private set; } = 90;

    internal static string? SwitchingEvidencePath { get; private set; }

    internal static string SwitchingEvidenceScenario { get; private set; } = "interactive";

    internal static string? StageCameraPrimPath { get; private set; }

    internal static string? CommandLineStageCameraPath { get; private set; }

    internal static string? PickSmokeEvidencePath { get; private set; }

    internal static bool PickSmokeEnabled =>
        !string.IsNullOrWhiteSpace(PickSmokeEvidencePath);

    internal static string? WindowsRenderingOverride { get; private set; }

    /// <summary>
    /// Callback supplied by an embedding host, invoked once the startup stage is open.
    /// </summary>
    internal static Func<ViewerStageSession, CancellationToken, Task>? StageReadyAsync
    {
        get;
        private set;
    }

    internal static Func<ViewerPickEventArgs, CancellationToken, Task>? PrimPickedAsync
    {
        get;
        private set;
    }

    internal static RenderPickTarget HostPickTarget { get; private set; } =
        RenderPickTarget.Primitive;

    internal static Func<ViewerViewportPointerEventArgs, CancellationToken, Task>?
        ViewportPointerPressedAsync
    { get; private set; }

    internal static Func<ViewerViewportPointerEventArgs, CancellationToken, Task>?
        ViewportPointerMovedAsync
    { get; private set; }

    internal static Func<ViewerViewportPointerEventArgs, CancellationToken, Task>?
        ViewportPointerReleasedAsync
    { get; private set; }

    internal static Func<ViewerSelectionChangedEventArgs, CancellationToken, Task>?
        SelectionChangedAsync
    { get; private set; }

    internal static string? SelectionChangedPrimSubtree { get; private set; }

    /// <summary>
    /// Window title override supplied by an embedding host.
    /// </summary>
    internal static string? HostTitle { get; private set; }

    /// <summary>
    /// Closes the shell when cancelled by an embedding host.
    /// </summary>
    internal static CancellationToken HostShutdownToken { get; private set; }

    /// <summary>
    /// Stage camera an embedding host asked the shell to start on.
    /// </summary>
    internal static string? HostStageCameraPath { get; private set; }

    internal static bool SwitchingEvidenceEnabled =>
        !string.IsNullOrWhiteSpace(SwitchingEvidencePath);

    internal static bool IsCleanupRetryEvidenceScenario =>
        string.Equals(
            SwitchingEvidenceScenario,
            "storm-destroy-cleanup-retry",
            StringComparison.Ordinal);

    internal static bool IsRetiredKindQuarantineEvidenceScenario =>
        string.Equals(
            SwitchingEvidenceScenario,
            "renderer-retired-kind-quarantine",
            StringComparison.Ordinal);

    internal static bool IsStageCameraEvidenceScenario =>
        string.Equals(
            SwitchingEvidenceScenario,
            ViewerStageCameraSmokeContract.ScenarioName,
            StringComparison.Ordinal);

    internal static bool NativeStormContextLoss { get; private set; }

    internal static bool NativeStormDestroyFailure { get; private set; }

    internal static bool NativeStormPersistentDestroyFailure { get; private set; }

    internal static RenderBackendKind? SmokeSwitchBackend { get; private set; }

    internal static RenderBackendKind? ForcedDeviceLossBackend { get; private set; }

    private static int _forcedDeviceLossConsumed;
    private static int _nativeStormContextLossConsumed;
    private static int _nativeStormDestroyFailureConsumed;
    private static long _nativeStormPixelSampleCount;
    private static long _nativeStormPixelSignature;

    internal static ViewerPlatformDecision PlatformDecision { get; private set; }

    internal static bool IsStormRenderer =>
        string.Equals(Renderer, "Storm", StringComparison.Ordinal);

    internal static RenderBackendKind? RequestedBackend =>
        Renderer switch
        {
            "Auto" => null,
            "Storm" => RenderBackendKind.Storm,
            "D3D12" => RenderBackendKind.D3D12,
            "Vulkan" => RenderBackendKind.Vulkan,
            "Metal" => RenderBackendKind.Metal,
            _ => null
        };

    internal static bool IsBackendForcedUnavailable(RenderBackendKind kind) =>
        IsEnabled(Environment.GetEnvironmentVariable(
            $"OPENUSD_FORCE_{kind.ToString().ToUpperInvariant()}_UNAVAILABLE"));

    internal static bool IsBackendForcedInitializationFailure(RenderBackendKind kind) =>
        IsEnabled(Environment.GetEnvironmentVariable(
            $"OPENUSD_FORCE_{kind.ToString().ToUpperInvariant()}_FAILURE"));

    internal static bool RequiresCompositionPlatform =>
        RequestedBackend is RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or RenderBackendKind.Metal ||
        SmokeSwitchBackend is RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or RenderBackendKind.Metal ||
        ForcedDeviceLossBackend is RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or RenderBackendKind.Metal ||
        PickSmokeEnabled ||
        IsStageCameraEvidenceScenario ||
        IsBackendForcedUnavailable(RenderBackendKind.Storm) ||
        IsBackendForcedInitializationFailure(RenderBackendKind.Storm);

    internal static void Initialize(string[] args)
    {
        PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH");
        StagePath = Environment.GetEnvironmentVariable("OPENUSD_STAGE_PATH");
        StatusFile = Environment.GetEnvironmentVariable("OPENUSD_STATUS_FILE");
        LogFile = Environment.GetEnvironmentVariable("OPENUSD_LOG_FILE");
        SoakArtifactPath = Environment.GetEnvironmentVariable("OPENUSD_SOAK_ARTIFACT");
        SharedStageSoak = IsEnabled(
            Environment.GetEnvironmentVariable("OPENUSD_SHARED_STAGE_SOAK"));
        SoakSeconds = ParseSoakSeconds(
            Environment.GetEnvironmentVariable("OPENUSD_SOAK_SECONDS"));
        Renderer = NormalizeRenderer(Environment.GetEnvironmentVariable("OPENUSD_RENDERER"));
        LiveEditSmoke = IsEnabled(
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_LIVE_EDIT"));
        SwitchSoakCount = ParseSwitchCount(
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_SWITCH_SOAK"));
        SwitchSoakSeconds = ParseSwitchSeconds(
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_SWITCH_SOAK_SECONDS"));
        SwitchingEvidencePath =
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_EVIDENCE_PATH");
        SwitchingEvidenceScenario =
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_EVIDENCE_SCENARIO")
            ?? "interactive";
        StageCameraPrimPath = IsStageCameraEvidenceScenario
            ? Environment.GetEnvironmentVariable("OPENUSD_VIEWER_STAGE_CAMERA_PATH")
            : null;
        CommandLineStageCameraPath = null;
        PickSmokeEvidencePath =
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PICK_SMOKE_PATH");
        PrimPickedAsync = PickSmokeEnabled
            ? ViewerPickingSmokeHostObserver.ObservePickAsync
            : null;
        HostPickTarget = RenderPickTarget.Primitive;
        ViewportPointerPressedAsync = null;
        ViewportPointerMovedAsync = null;
        ViewportPointerReleasedAsync = null;
        SelectionChangedAsync = PickSmokeEnabled
            ? ViewerPickingSmokeHostObserver.ObserveSelectionAsync
            : null;
        SelectionChangedPrimSubtree = PickSmokeEnabled ? "/World" : null;
        WindowsRenderingOverride = null;
        NativeStormContextLoss = IsEnabled(
            Environment.GetEnvironmentVariable("OPENUSD_NATIVE_STORM_CONTEXT_LOSS"));
        NativeStormDestroyFailure = IsEnabled(
            Environment.GetEnvironmentVariable("OPENUSD_FORCE_STORM_DESTROY_FAILURE"));
        NativeStormPersistentDestroyFailure = IsEnabled(
            Environment.GetEnvironmentVariable(
                "OPENUSD_FORCE_STORM_DESTROY_FAILURE_PERSISTENT"));
        SmokeSwitchBackend = ParseOptionalBackend(
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_SWITCH_TO"));
        ForcedDeviceLossBackend = ParseOptionalBackend(
            Environment.GetEnvironmentVariable("OPENUSD_FORCE_DEVICE_LOSS"));
        Volatile.Write(ref _forcedDeviceLossConsumed, 0);
        Volatile.Write(ref _nativeStormContextLossConsumed, 0);
        Volatile.Write(ref _nativeStormDestroyFailureConsumed, 0);
        Interlocked.Exchange(ref _nativeStormPixelSampleCount, 0);
        Interlocked.Exchange(ref _nativeStormPixelSignature, 0);
        WriteStatus("Viewer starting");

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--plugins" && i + 1 < args.Length)
            {
                PluginPath = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--stage" && i + 1 < args.Length)
            {
                StagePath = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--renderer" && i + 1 < args.Length)
            {
                Renderer = NormalizeRenderer(args[++i]);
            }
            else if (args[i] == "--camera" && i + 1 < args.Length)
            {
                CommandLineStageCameraPath = NormalizeStageCameraPath(args[++i]);
            }
            else if (args[i].StartsWith(
                "--camera=",
                StringComparison.OrdinalIgnoreCase))
            {
                CommandLineStageCameraPath = NormalizeStageCameraPath(
                    args[i]["--camera=".Length..]);
            }
            else if (args[i] == "--shared-stage-soak")
            {
                SharedStageSoak = true;
            }
            else if (args[i] == "--renderer-switch-soak" && i + 1 < args.Length)
            {
                SwitchSoakCount = ParseSwitchCount(args[++i]);
            }
            else if (args[i] == "--switch-soak-seconds" && i + 1 < args.Length)
            {
                SwitchSoakSeconds = ParseSwitchSeconds(args[++i]);
            }
            else if (args[i] == "--soak-artifact" && i + 1 < args.Length)
            {
                SoakArtifactPath = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--soak-seconds" && i + 1 < args.Length)
            {
                SoakSeconds = ParseSoakSeconds(args[++i]);
            }
            else if (args[i].StartsWith(
                "--windows-rendering=",
                StringComparison.OrdinalIgnoreCase))
            {
                WindowsRenderingOverride = NormalizeWindowsRendering(
                    args[i]["--windows-rendering=".Length..]);
            }
            else if (args[i] == "--windows-rendering" && i + 1 < args.Length)
            {
                WindowsRenderingOverride = NormalizeWindowsRendering(args[++i]);
            }
            else if ((args[i].Length == 0 || args[i][0] != '-') &&
                string.IsNullOrWhiteSpace(StagePath) &&
                IsUsdStagePath(args[i]))
            {
                StagePath = Path.GetFullPath(args[i]);
            }
        }

        if (SharedStageSoak)
        {
            if (string.Equals(Renderer, "Auto", StringComparison.Ordinal))
            {
                Renderer = "Storm";
            }
            if (!IsStormRenderer)
            {
                throw new ArgumentException(
                    "The shared-stage viewer soak requires the Storm renderer.");
            }
            if (string.IsNullOrWhiteSpace(StagePath) ||
                string.IsNullOrWhiteSpace(PluginPath))
            {
                throw new ArgumentException(
                    "The shared-stage viewer soak requires --stage and --plugins.");
            }
            SoakArtifactPath ??= Path.Combine(
                Path.GetDirectoryName(StatusFile ?? StagePath)!,
                "shared-stage-soak.json");
        }
        if (IsStageCameraEvidenceScenario &&
            (!SwitchingEvidenceEnabled ||
             string.IsNullOrWhiteSpace(StageCameraPrimPath) ||
            !StageCameraPrimPath.StartsWith('/') ||
             StageCameraPrimPath.Contains('\\')))
        {
            throw new ArgumentException(
                "The stage-camera evidence scenario requires an absolute USD camera prim path.");
        }
    }

    /// <summary>
    /// Initializes startup state for an embedding host. Environment defaults are applied
    /// first, then the supplied options override them.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    internal static void Initialize(ViewerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Initialize([]);
        if (!string.IsNullOrWhiteSpace(options.StagePath))
        {
            StagePath = Path.GetFullPath(options.StagePath);
        }
        if (!string.IsNullOrWhiteSpace(options.PluginPath))
        {
            PluginPath = Path.GetFullPath(options.PluginPath);
        }
        if (!string.IsNullOrWhiteSpace(options.Renderer))
        {
            Renderer = NormalizeRenderer(options.Renderer);
        }
        HostTitle = options.Title;
        HostShutdownToken = options.ShutdownToken;
        HostStageCameraPath = NormalizeStageCameraPath(options.StageCameraPath);
        StageReadyAsync = options.StageReadyAsync;
        PrimPickedAsync = options.PrimPicked;
        HostPickTarget = options.PickTarget;
        ViewportPointerPressedAsync = options.ViewportPointerPressed;
        ViewportPointerMovedAsync = options.ViewportPointerMoved;
        ViewportPointerReleasedAsync = options.ViewportPointerReleased;
        SelectionChangedAsync = options.SelectionChanged;
        SelectionChangedPrimSubtree = options.SelectionChangedPrimSubtree;
    }

    private static string? NormalizeStageCameraPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed[0] != '/' || trimmed.Contains('\\'))
        {
            throw new ArgumentException(
                "The stage camera path must be an absolute USD prim path.",
                nameof(value));
        }
        return trimmed;
    }

    private static bool IsUsdStagePath(string value) =>
        value.EndsWith(".usd", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".usda", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".usdc", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".usdz", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWindowsRendering(string value)
    {
        if (string.Equals(value, "angle", StringComparison.OrdinalIgnoreCase))
        {
            return "angle";
        }
        if (string.Equals(value, "wgl", StringComparison.OrdinalIgnoreCase))
        {
            return "wgl";
        }
        throw new ArgumentException(
            $"Unknown Windows rendering mode '{value}'. Expected angle or wgl.");
    }

    internal static void ConfigurePlatform(ViewerPlatformDecision decision)
    {
        PlatformDecision = decision;
    }

    internal static bool TryConsumeForcedDeviceLoss(RenderBackendKind kind) =>
        ForcedDeviceLossBackend == kind &&
        Interlocked.Exchange(ref _forcedDeviceLossConsumed, 1) == 0;

    internal static void ArmEvidenceDeviceLoss(RenderBackendKind kind)
    {
        if (!SwitchingEvidenceEnabled)
        {
            throw new InvalidOperationException(
                "Viewer device-loss arming is available only during evidence runs.");
        }
        ForcedDeviceLossBackend = kind;
        Volatile.Write(ref _forcedDeviceLossConsumed, 0);
    }

    internal static bool TryConsumeNativeStormContextLoss() =>
        NativeStormContextLoss &&
        Interlocked.Exchange(ref _nativeStormContextLossConsumed, 1) == 0;

    internal static bool TryConsumeNativeStormDestroyFailure() =>
        NativeStormPersistentDestroyFailure ||
        (NativeStormDestroyFailure &&
         Interlocked.Exchange(ref _nativeStormDestroyFailureConsumed, 1) == 0);

    internal static void ReleasePersistentNativeStormDestroyFailure() =>
        NativeStormPersistentDestroyFailure = false;

    internal static void RecordNativeStormPixelEvidence(
        ulong sampleCount,
        ulong signature)
    {
        Interlocked.Exchange(
            ref _nativeStormPixelSampleCount,
            checked((long)sampleCount));
        Interlocked.Exchange(
            ref _nativeStormPixelSignature,
            unchecked((long)signature));
    }

    internal static (long Samples, ulong Signature) GetNativeStormPixelEvidence() =>
        (
            Interlocked.Read(ref _nativeStormPixelSampleCount),
            unchecked((ulong)Interlocked.Read(ref _nativeStormPixelSignature))
        );

    internal static string FormatStormRendererName(string nativeName)
    {
        return PlatformDecision.UsesXWaylandFallback
            ? $"{nativeName} / XWayland"
            : nativeName;
    }

    internal static void WriteStatus(string status)
    {
        if (!string.IsNullOrWhiteSpace(StatusFile))
        {
            try
            {
                lock (StatusGate)
                {
                    File.AppendAllText(StatusFile, status + Environment.NewLine);
                }
            }
            catch (IOException exception)
            {
                Trace.WriteLine($"Could not write viewer status: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Trace.WriteLine($"Could not write viewer status: {exception.Message}");
            }
        }
    }

    private static string NormalizeRenderer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Automatic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Silk", StringComparison.OrdinalIgnoreCase))
        {
            return "Auto";
        }
        if (string.Equals(value, "Storm", StringComparison.OrdinalIgnoreCase))
        {
            return "Storm";
        }
        if (string.Equals(value, "D3D12", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Direct3D12", StringComparison.OrdinalIgnoreCase))
        {
            return "D3D12";
        }
        if (string.Equals(value, "Vulkan", StringComparison.OrdinalIgnoreCase))
        {
            return "Vulkan";
        }
        if (string.Equals(value, "Metal", StringComparison.OrdinalIgnoreCase))
        {
            return "Metal";
        }
        throw new ArgumentException(
            $"Unsupported renderer '{value}'. Expected Auto, Storm, D3D12, Vulkan, or Metal.",
            nameof(value));
    }

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static RenderBackendKind? ParseOptionalBackend(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return NormalizeRenderer(value) switch
        {
            "Storm" => RenderBackendKind.Storm,
            "D3D12" => RenderBackendKind.D3D12,
            "Vulkan" => RenderBackendKind.Vulkan,
            "Metal" => RenderBackendKind.Metal,
            _ => throw new ArgumentException(
                "A viewer smoke switch requires an explicit backend.",
                nameof(value))
        };
    }

    private static int ParseSoakSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 90;
        }
        if (!int.TryParse(value, out int seconds) || seconds < 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Shared-stage soak survival must be at least 90 seconds.");
        }
        return seconds;
    }

    private static int ParseSwitchCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }
        if (!int.TryParse(value, out int count) || count < 1 || count > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Renderer switch soak count must be between 1 and 10,000.");
        }
        return count;
    }

    private static int ParseSwitchSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 90;
        }
        if (!int.TryParse(value, out int seconds) || seconds < 1 || seconds > 86_400)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Renderer switch soak duration must be between 1 and 86,400 seconds.");
        }
        return seconds;
    }
}
