// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform;
using OpenUsd.Geom;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;
using OpenUsd.Viewer;

namespace OpenUsd.AvaloniaVulkanSmoke;

internal sealed class SmokeApplication : Application
{
    internal static int ExitCode { get; private set; } = 1;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new PlatformNotSupportedException("A desktop Avalonia lifetime is required.");
        }

        var runner = new SmokeRunner(desktop);
        desktop.MainWindow = runner.CreateWindow();
        base.OnFrameworkInitializationCompleted();
    }

    private sealed class SmokeRunner(IClassicDesktopStyleApplicationLifetime desktop)
        : IAsyncDisposable
    {
        private readonly ConcurrentQueue<FrameEvidence> _frames = new();
        private readonly List<string> _statuses = [];
        private readonly Lock _statusGate = new();
        private CompositionViewportControl? _viewport;
        private VulkanCompositionViewportPresenter? _presenter;
        private UsdStageScheduler? _scheduler;
        private UsdStageRenderSource? _source;
        private OpenUsdSilkSession? _session;
        private Window? _window;
        private VulkanSmokePixelEvidence? _pixelEvidence;
        private int _presentedStatusCount;
        private ulong _initialRevision;
        private bool _editIssued;
        private bool _capabilityResizeObserved;

        internal Window CreateWindow()
        {
            _viewport = new CompositionViewportControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _viewport.StatusChanged += OnStatusChanged;
            if (SmokeOptions.CapabilityOnly)
            {
                _presenter = VulkanCompositionViewportPresenter.Create(SmokeOptions.Required);
                _viewport.PresenterFactory = () => _presenter;
            }
            _window = new Window
            {
                Width = 640,
                Height = 480,
                Title = "OpenUSD Avalonia Vulkan smoke",
                Content = _viewport
            };
            _window.Opened += OnOpened;
            return _window;
        }

        private async void OnOpened(object? sender, EventArgs args)
        {
            try
            {
                await RunAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                await FinishAsync("failed", exception.ToString()).ConfigureAwait(true);
            }
        }

        private async Task RunAsync()
        {
            if (SmokeOptions.CapabilityOnly)
            {
                await RunCapabilityOnlyAsync().ConfigureAwait(true);
                return;
            }

            _scheduler = UsdStageScheduler.Open(SmokeOptions.StagePath);
            await _scheduler.EditAsync(
                stage =>
                {
                    UsdGeomMesh mesh = stage.DefineMesh("/World/InitialMesh");
                    mesh.SetPoints(
                    [
                        new UsdVec3f(-0.6f, -0.5f, 0),
                        new UsdVec3f(0.6f, -0.5f, 0),
                        new UsdVec3f(0, 0.6f, 0)
                    ]);
                    mesh.SetTopology([3], [0, 1, 2]);
                    mesh.SetNormals(
                    [
                        new UsdVec3f(0, 0, 1),
                        new UsdVec3f(0, 0, 1),
                        new UsdVec3f(0, 0, 1)
                    ],
                    UsdGeomInterpolation.Vertex);
                    mesh.SubdivisionScheme = UsdGeomSubdivisionScheme.None;
                },
                UsdStageInvalidationKind.Topology).ConfigureAwait(true);
            _source = await _scheduler.AcquireRenderSourceAsync().ConfigureAwait(true);
            _session = OpenUsdSilkRuntime.Create(SmokeOptions.PluginPath, _source);
            _presenter = VulkanCompositionViewportPresenter.Create(
                RenderFrame,
                SmokeOptions.Required);
            _viewport!.PresenterFactory = () => _presenter;
            await _viewport.ReinitializePresentationAsync().ConfigureAwait(true);

            await WaitUntilAsync(
                () => SnapshotFrames().Length >= 9 &&
                    Volatile.Read(ref _presentedStatusCount) >= 9 &&
                    SnapshotFrames()
                        .GroupBy(frame => frame.AllocationId)
                        .Count(group => group.Max(frame => frame.UseCount) >= 3) >= 3,
                "three imported ring images did not complete two reuse cycles")
                .ConfigureAwait(true);

            FrameEvidence[] initialFrames = SnapshotFrames();
            if (initialFrames.All(frame => frame.DrawCount == 0))
            {
                throw new InvalidOperationException("hdSilk produced no retained mesh draw.");
            }
            _initialRevision = initialFrames.Max(frame => frame.Revision);
            await Task.Delay(250).ConfigureAwait(true);
            WindowsClientCaptureResult? initialCapture = null;
            if (OperatingSystem.IsWindows() && SmokeOptions.Required)
            {
                initialCapture = await CaptureViewportAsync("initial").ConfigureAwait(true);
            }
            int editStartFrame = initialFrames.Length;
            int editStartPresented = Volatile.Read(ref _presentedStatusCount);
            await _scheduler.EditAsync(
                stage =>
                {
                    UsdGeomMesh mesh = stage.DefineMesh("/World/LiveMesh");
                    mesh.SetPoints(
                    [
                        new UsdVec3f(-0.75f, -0.5f, 0),
                        new UsdVec3f(0.75f, -0.5f, 0),
                        new UsdVec3f(0, 0.75f, 0)
                    ]);
                    mesh.SetTopology([3], [0, 1, 2]);
                    mesh.SetNormals(
                    [
                        new UsdVec3f(0, 0, 1),
                        new UsdVec3f(0, 0, 1),
                        new UsdVec3f(0, 0, 1)
                    ],
                    UsdGeomInterpolation.Vertex);
                    mesh.SubdivisionScheme = UsdGeomSubdivisionScheme.None;
                },
                UsdStageInvalidationKind.Topology).ConfigureAwait(true);
            _editIssued = true;

            await WaitUntilAsync(
                () => SnapshotFrames().Any(frame =>
                        frame.Revision > _initialRevision && frame.DrawCount > 0) &&
                    SnapshotFrames().Length >= editStartFrame + 2 &&
                    Volatile.Read(ref _presentedStatusCount) >= editStartPresented + 2,
                "the live stage edit did not produce a newer rendered revision")
                .ConfigureAwait(true);
            if (initialCapture is not null)
            {
                await Task.Delay(250).ConfigureAwait(true);
                WindowsClientCaptureResult editedCapture =
                    await CaptureViewportAsync("edited").ConfigureAwait(true);
                (long changedPixels, double meanDelta) =
                    WindowsClientCaptureResult.Compare(initialCapture, editedCapture);
                _pixelEvidence = new VulkanSmokePixelEvidence(
                    VulkanSmokePixelEvidence.RequiredCaptureApi,
                    initialCapture.Evidence,
                    editedCapture.Evidence,
                    changedPixels,
                    meanDelta);
                _pixelEvidence.Validate();
            }

            int resizeStart = SnapshotFrames().Length;
            _window!.Width = 820;
            _window.Height = 540;
            await WaitUntilAsync(
                () => SnapshotFrames()
                    .Skip(resizeStart)
                    .Any(frame => frame.Width >= 800 && frame.Height >= 500),
                "the resized Avalonia viewport did not render a new generation")
                .ConfigureAwait(true);

            await FinishAsync("passed", null).ConfigureAwait(true);
        }

        private async Task RunCapabilityOnlyAsync()
        {
            if (_presenter is null)
            {
                _presenter = VulkanCompositionViewportPresenter.Create(SmokeOptions.Required);
                _viewport!.PresenterFactory = () => _presenter;
                await _viewport.ReinitializePresentationAsync().ConfigureAwait(true);
            }
            await WaitUntilAsync(
                () =>
                {
                    VulkanCompositionPresenterDiagnostics value =
                        _presenter.GetDiagnostics();
                    return value.PresentedFrames >= 9 && value.RingReuseFrames >= 6;
                },
                "the compositor import ring did not complete two reuse cycles")
                .ConfigureAwait(true);

            long beforeResize = _presenter.GetDiagnostics().PresentedFrames;
            _window!.Width = 820;
            _window.Height = 540;
            await WaitUntilAsync(
                () => _presenter.GetDiagnostics().PresentedFrames >= beforeResize + 3,
                "the resized capability probe did not present a new generation")
                .ConfigureAwait(true);
            _capabilityResizeObserved = true;
            await FinishAsync("capability-passed", null).ConfigureAwait(true);
        }

        private SilkMeshRenderResult RenderFrame(VulkanCompositionRenderContext context)
        {
            OpenUsdSilkSession session = _session ??
                throw new InvalidOperationException("The hdSilk session is unavailable.");
            using OpenUsdSilkPage page = session.Sync(
                checked((int)context.ColorTarget.Width),
                checked((int)context.ColorTarget.Height),
                camera: CameraState.Default);
            SilkMeshRenderResult result = context.Renderer.ApplyAndRender(
                page,
                context.ColorTarget,
                context.DepthTarget,
                new SilkMeshRenderOptions(new SilkColor(0.02f, 0.03f, 0.05f, 1), 1));
            _frames.Enqueue(new FrameEvidence(
                context.AllocationId,
                context.FrameIndex,
                context.UseCount,
                context.ColorTarget.Width,
                context.ColorTarget.Height,
                page.Revision,
                result.DrawCount,
                result.UniformUploads));
            return result;
        }

        private async Task WaitUntilAsync(Func<bool> condition, string failure)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(SmokeOptions.TimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                string status = _viewport?.Status ?? string.Empty;
                if (status.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                    status.Contains("failed", StringComparison.OrdinalIgnoreCase))
                {
                    if (SmokeOptions.Required)
                    {
                        throw new InvalidOperationException(
                            $"Required Vulkan presentation failed: {status}");
                    }
                    await FinishAsync("unavailable", status).ConfigureAwait(true);
                    throw new SmokeCompletedException();
                }
                await Task.Delay(50).ConfigureAwait(true);
            }
            throw new TimeoutException(failure);
        }

        private async Task FinishAsync(string outcome, string? blocker)
        {
            if (ExitCode == 0 || desktop.MainWindow is null)
            {
                return;
            }

            var cleanupFailures = new List<string>();
            if (_viewport is not null)
            {
                try
                {
                    await _viewport.DisposeAsync().ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception.ToString());
                }
            }
            VulkanCompositionPresenterDiagnostics diagnostics =
                _presenter?.GetDiagnostics() ?? default;
            try
            {
                _session?.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception.ToString());
            }
            _session = null;
            _source?.Dispose();
            _source = null;
            if (_scheduler is not null)
            {
                try
                {
                    await _scheduler.DisposeAsync().ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception.ToString());
                }
            }
            _scheduler = null;

            if (diagnostics.ActiveGenerations != 0 || diagnostics.ActiveFrames != 0)
            {
                cleanupFailures.Add(
                    $"Vulkan teardown retained {diagnostics.ActiveGenerations} generations " +
                    $"and {diagnostics.ActiveFrames} frames.");
            }
            if (cleanupFailures.Count != 0)
            {
                outcome = "failed";
                blocker = string.Join(Environment.NewLine, cleanupFailures);
            }
            if (SmokeOptions.Required &&
                !SmokeOptions.CapabilityOnly &&
                OperatingSystem.IsWindows() &&
                _pixelEvidence is null)
            {
                outcome = "failed";
                blocker = "Required Windows smoke did not produce composed pixel evidence.";
            }

            FrameEvidence[] frames = SnapshotFrames();
            DateTimeOffset artifactWrittenUtc = DateTimeOffset.UtcNow;
            var artifact = new SmokeArtifact(
                1,
                outcome,
                SmokeOptions.Platform,
                SmokeOptions.Required,
                SmokeOptions.CapabilityOnly,
                blocker,
                LastStatus(),
                frames.Length,
                frames.Select(frame => frame.AllocationId).Distinct().Count(),
                SmokeOptions.CapabilityOnly
                    ? diagnostics.RingReuseFrames / 3
                    : frames
                        .GroupBy(frame => frame.AllocationId)
                        .Select(group => Math.Max(0, group.Max(frame => frame.UseCount) - 1))
                        .Where(cycles => cycles > 0)
                        .DefaultIfEmpty()
                        .Min(),
                diagnostics.ImageHandleType,
                diagnostics.PresentationPath,
                _initialRevision,
                frames.Select(frame => frame.Revision).DefaultIfEmpty().Max(),
                _editIssued &&
                    frames.Any(frame => frame.Revision > _initialRevision),
                _capabilityResizeObserved ||
                    frames.Select(frame => (frame.Width, frame.Height)).Distinct().Count() > 1,
                frames.Select(frame => frame.DrawCount).DefaultIfEmpty().Max(),
                diagnostics,
                SmokeOptions.CreateIdentityEvidence(artifactWrittenUtc),
                _pixelEvidence,
                Statuses());
            Directory.CreateDirectory(Path.GetDirectoryName(SmokeOptions.ArtifactPath)!);
            await File.WriteAllTextAsync(
                SmokeOptions.ArtifactPath,
                JsonSerializer.Serialize(
                    artifact,
                    SmokeJsonContext.Default.SmokeArtifact)).ConfigureAwait(true);
            Console.WriteLine(
                $"OPENUSD_AVALONIA_VULKAN_SMOKE outcome={outcome} " +
                $"platform={SmokeOptions.Platform} capability=\"{LastStatus()}\" " +
                $"frames={frames.Length} allocations={artifact.DistinctAllocations} " +
                $"reuse={artifact.RingReuseCycles} revisions=" +
                $"{artifact.InitialRevision}->{artifact.FinalRevision} " +
                $"resize={artifact.ResizeObserved} draws={artifact.MaxDrawCount} " +
                $"pixels={artifact.PixelEvidence?.ChangedPixels ?? 0} " +
                $"resources={diagnostics.ActiveGenerations}/{diagnostics.ActiveFrames}");
            bool accepted = outcome == "passed" ||
                outcome == "capability-passed" ||
                (outcome == "unavailable" && !SmokeOptions.Required);
            ExitCode = accepted ? 0 : 1;
            desktop.Shutdown(ExitCode);
        }

        public ValueTask DisposeAsync() =>
            _viewport?.DisposeAsync() ?? ValueTask.CompletedTask;

        private void OnStatusChanged(object? sender, string status)
        {
            lock (_statusGate)
            {
                _statuses.Add(status);
            }
            if (status.Contains("presented", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _presentedStatusCount);
            }
            Console.WriteLine($"OPENUSD_AVALONIA_VULKAN_STATUS {status}");
        }

        private async Task<WindowsClientCaptureResult> CaptureViewportAsync(string phase)
        {
            Window window = _window ??
                throw new InvalidOperationException("The smoke window is unavailable.");
            CompositionViewportControl viewport = _viewport ??
                throw new InvalidOperationException("The composition viewport is unavailable.");
            IPlatformHandle handle = window.TryGetPlatformHandle() ??
                throw new InvalidOperationException(
                    "Avalonia did not expose a platform window handle.");
            if (!string.Equals(
                handle.HandleDescriptor,
                "HWND",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected an HWND capture target, got '{handle.HandleDescriptor}'.");
            }
            Point origin = viewport.TranslatePoint(default, window) ??
                throw new InvalidOperationException(
                    "Could not locate the viewport in the client area.");
            double scale = window.RenderScaling;
            var crop = new PixelCaptureRectangle(
                checked((int)Math.Round(origin.X * scale)),
                checked((int)Math.Round(origin.Y * scale)),
                checked((int)Math.Round(viewport.Bounds.Width * scale)),
                checked((int)Math.Round(viewport.Bounds.Height * scale)));
            string artifactPath = SmokeOptions.ArtifactPath;
            string bitmapPath = Path.Combine(
                Path.GetDirectoryName(artifactPath)!,
                $"{Path.GetFileNameWithoutExtension(artifactPath)}.{phase}.bmp");
            return await Task.Run(
                () => WindowsClientCapture.Capture(
                    handle.Handle,
                    crop,
                    phase,
                    bitmapPath)).ConfigureAwait(true);
        }

        private FrameEvidence[] SnapshotFrames() => [.. _frames];

        private string[] Statuses()
        {
            lock (_statusGate)
            {
                return [.. _statuses];
            }
        }

        private string LastStatus()
        {
            lock (_statusGate)
            {
                return _statuses.Count == 0 ? _viewport?.Status ?? "not initialized" : _statuses[^1];
            }
        }
    }

    private sealed class SmokeCompletedException : Exception;

    private readonly record struct FrameEvidence(
        long AllocationId,
        int FrameIndex,
        int UseCount,
        uint Width,
        uint Height,
        ulong Revision,
        int DrawCount,
        int UniformUploads);
}

internal sealed record SmokeArtifact(
    int SchemaVersion,
    string Outcome,
    string Platform,
    bool Required,
    bool CapabilityOnly,
    string? Blocker,
    string Capability,
    int FrameCount,
    int DistinctAllocations,
    long RingReuseCycles,
    string? HandleType,
    string? PresentationPath,
    ulong InitialRevision,
    ulong FinalRevision,
    bool LiveEditObserved,
    bool ResizeObserved,
    int MaxDrawCount,
    VulkanCompositionPresenterDiagnostics Diagnostics,
    VulkanSmokeIdentityEvidence? Identity,
    VulkanSmokePixelEvidence? PixelEvidence,
    string[] Statuses);

internal sealed record VulkanSmokeIdentityEvidence(
    string SourceSha256,
    int SourceFileCount,
    string LatestSourceWriteUtc,
    string ExecutableSha256,
    long ExecutableLength,
    string ExecutableLastWriteUtc,
    string BuildCompletedUtc,
    string RunStartedUtc,
    string ArtifactWrittenUtc);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(SmokeArtifact))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;

internal static class SmokeOptions
{
    internal static string Platform { get; private set; } = "windows";

    internal static bool Required { get; private set; }

    internal static bool CapabilityOnly { get; private set; }

    internal static string StagePath { get; private set; } = string.Empty;

    internal static string PluginPath { get; private set; } = string.Empty;

    internal static string ArtifactPath { get; private set; } = string.Empty;

    internal static int TimeoutSeconds { get; private set; } = 60;

    internal static VulkanSmokeIdentityEvidence? CreateIdentityEvidence(
        DateTimeOffset artifactWrittenUtc)
    {
        string? sourceHash = Get("OPENUSD_AVALONIA_VULKAN_SOURCE_SHA256");
        if (string.IsNullOrWhiteSpace(sourceHash))
        {
            if (Required)
            {
                throw new InvalidOperationException(
                    "Required smoke source identity was not provided.");
            }
            return null;
        }
        return new VulkanSmokeIdentityEvidence(
            sourceHash,
            GetInt("OPENUSD_AVALONIA_VULKAN_SOURCE_FILE_COUNT"),
            GetRequired("OPENUSD_AVALONIA_VULKAN_SOURCE_LATEST_WRITE_UTC"),
            GetRequired("OPENUSD_AVALONIA_VULKAN_EXECUTABLE_SHA256"),
            GetLong("OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LENGTH"),
            GetRequired("OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LAST_WRITE_UTC"),
            GetRequired("OPENUSD_AVALONIA_VULKAN_BUILD_COMPLETED_UTC"),
            GetRequired("OPENUSD_AVALONIA_VULKAN_RUN_STARTED_UTC"),
            artifactWrittenUtc.ToString("O"));
    }

    internal static void Initialize(string[] args)
    {
        Platform = Get("OPENUSD_AVALONIA_VULKAN_PLATFORM") ??
            (OperatingSystem.IsWindows() ? "windows" : "x11");
        Required = string.Equals(
            Get("OPENUSD_REQUIRE_VULKAN_PRESENTATION"),
            "1",
            StringComparison.Ordinal);
        CapabilityOnly = string.Equals(
            Get("OPENUSD_AVALONIA_VULKAN_CAPABILITY_ONLY"),
            "1",
            StringComparison.Ordinal);
        if (!CapabilityOnly)
        {
            StagePath = Path.GetFullPath(
                Get("OPENUSD_STAGE_PATH") ??
                throw new InvalidOperationException("OPENUSD_STAGE_PATH is required."));
            PluginPath = Path.GetFullPath(
                Get("OPENUSD_PLUGIN_PATH") ??
                throw new InvalidOperationException("OPENUSD_PLUGIN_PATH is required."));
        }
        ArtifactPath = Path.GetFullPath(
            Get("OPENUSD_AVALONIA_VULKAN_ARTIFACT") ??
            Path.Combine(AppContext.BaseDirectory, "avalonia-vulkan-smoke.json"));
        if (int.TryParse(
            Get("OPENUSD_AVALONIA_VULKAN_TIMEOUT_SECONDS"),
            out int timeout))
        {
            TimeoutSeconds = Math.Clamp(timeout, 10, 600);
        }
    }

    private static string? Get(string name) =>
        Environment.GetEnvironmentVariable(name);

    private static string GetRequired(string name) =>
        Get(name) ?? throw new InvalidOperationException($"{name} is required.");

    private static int GetInt(string name) =>
        int.TryParse(GetRequired(name), out int value)
            ? value
            : throw new InvalidOperationException($"{name} must be an integer.");

    private static long GetLong(string name) =>
        long.TryParse(GetRequired(name), out long value)
            ? value
            : throw new InvalidOperationException($"{name} must be an integer.");
}
