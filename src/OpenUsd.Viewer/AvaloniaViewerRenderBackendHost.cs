// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed class AvaloniaViewerRenderBackendHost(
    RendererSwitchingViewport viewportHost,
    UsdStageScheduler scheduler,
    UsdStageRenderSource source,
    string pluginPath,
    Action<string> reportStatus) : IViewerRenderBackendHost
{
    private readonly int[] _attachCounts =
        new int[Enum.GetValues<RenderBackendKind>().Max(kind => (int)kind) + 1];
    private readonly ViewerBackendRuntimeIdentity?[] _runtimeIdentities =
        new ViewerBackendRuntimeIdentity[
            Enum.GetValues<RenderBackendKind>().Max(kind => (int)kind) + 1];

    private const string OpenGlFramework =
        "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    private const int CglSuccess = 0;
    private const int CglBadPixelFormat = 10002;
    private const int CglPfaColorSize = 8;
    private const int CglPfaAlphaSize = 11;
    private const int CglPfaDepthSize = 12;
    private const int CglPfaStencilSize = 13;
    private const int CglPfaDoubleBuffer = 5;
    private const int CglPfaNoRecovery = 72;
    private const int CglPfaAccelerated = 73;
    private const int CglPfaOpenGlProfile = 99;
    private const int CglOglPVersion41Core = 0x4100;

    internal int GetAttachCount(RenderBackendKind kind) =>
        Volatile.Read(ref _attachCounts[(int)kind]);

    internal ViewerBackendRuntimeIdentity GetRuntimeIdentity(RenderBackendKind kind) =>
        Volatile.Read(ref _runtimeIdentities[(int)kind]) ??
        ViewerBackendRuntimeIdentity.Unknown;

    private bool AttachForSurfaceCreation(Control control)
    {
        bool keepActive = viewportHost.VisibleControlCount == 0;

        // Run 31210681699 proved that Linux X11 NativeControlHost can stay
        // uncreated forever when the first backend candidate is initialized
        // hidden, and macOS Metal composition can crash after initializing
        // against a zero-sized hidden surface.
        viewportHost.Attach(control, isActive: true);
        return keepActive;
    }

    private static async ValueTask HideInitializedCandidateUnlessFirstAsync(
        RendererSwitchingViewport viewportHost,
        Control control,
        bool keepActive,
        CancellationToken cancellationToken)
    {
        if (keepActive)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: false),
            DispatcherPriority.Send,
            cancellationToken);
    }

    public ValueTask<RenderBackendProbeResult> ProbeAsync(
        RenderBackendKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ViewerStartupOptions.IsBackendForcedUnavailable(kind))
        {
            return ValueTask.FromResult(Unavailable(
                kind,
                RenderBackendProbeFailureKind.RuntimeUnavailable,
                "VIEWER_BACKEND_FORCED_UNAVAILABLE",
                $"{kind} was disabled by the viewer diagnostic configuration."));
        }
        if (kind == RenderBackendKind.Metal)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return ValueTask.FromResult(Unavailable(
                    kind,
                    RenderBackendProbeFailureKind.UnsupportedPlatform,
                    "VIEWER_METAL_PLATFORM_UNSUPPORTED",
                    "Metal is available only on macOS."));
            }
            if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
            {
                return ValueTask.FromResult(Unavailable(
                    kind,
                    RenderBackendProbeFailureKind.RuntimeUnavailable,
                    "VIEWER_METAL_SHADER_PAIR_UNAVAILABLE",
                    "Metal composition requires the validated ten-entry " +
                    "mesh.metallib and sidecar."));
            }
            if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath))
            {
                return ValueTask.FromResult(Unavailable(
                    kind,
                    RenderBackendProbeFailureKind.RuntimeUnavailable,
                    "VIEWER_PLUGIN_PATH_UNAVAILABLE",
                    "The OpenUSD plugin directory is unavailable."));
            }
            return ValueTask.FromResult(RenderBackendProbeResult.Available());
        }
        if (kind == RenderBackendKind.Storm &&
            OperatingSystem.IsMacOS() &&
            TryGetMacOSStormCglUnavailable(out string? cglReason))
        {
            ViewerStartupOptions.WriteStatus(
                "Renderer probe diagnostic: Storm; " +
                $"VIEWER_STORM_MACOS_CGL_UNAVAILABLE; {cglReason}");
            return ValueTask.FromResult(Unavailable(
                kind,
                RenderBackendProbeFailureKind.RuntimeUnavailable,
                "VIEWER_STORM_MACOS_CGL_UNAVAILABLE",
                cglReason));
        }
        if (kind == RenderBackendKind.Storm && OperatingSystem.IsMacOS())
        {
            ViewerStartupOptions.WriteStatus(
                "Renderer probe diagnostic: Storm; VIEWER_STORM_MACOS_CGL_AVAILABLE; " +
                "macOS CGL OpenGL 4.1 core pixel format is available.");
        }
        if (!IsSupportedPlatform(kind))
        {
            return ValueTask.FromResult(Unavailable(
                kind,
                RenderBackendProbeFailureKind.UnsupportedPlatform,
                "VIEWER_BACKEND_PLATFORM_UNSUPPORTED",
                $"{kind} is unsupported on this operating system."));
        }
        if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath))
        {
            return ValueTask.FromResult(Unavailable(
                kind,
                RenderBackendProbeFailureKind.RuntimeUnavailable,
                "VIEWER_PLUGIN_PATH_UNAVAILABLE",
                "The OpenUSD plugin directory is unavailable."));
        }
        return ValueTask.FromResult(RenderBackendProbeResult.Available());
    }

    [SupportedOSPlatform("macos")]
    private static bool TryGetMacOSStormCglUnavailable(
        [NotNullWhen(true)] out string? reason) =>
        TryGetMacOSStormCglUnavailable(ProbeMacOSStormCgl, out reason);

    internal static bool TryGetMacOSStormCglUnavailable(
        Func<MacOSStormCglProbeResult> probe,
        [NotNullWhen(true)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(probe);
        MacOSStormCglProbeResult result;
        try
        {
            result = probe();
        }
        catch (DllNotFoundException exception)
        {
            reason =
                "macOS OpenGL.framework could not be loaded for Storm CGL preflight: " +
                exception.Message;
            return true;
        }
        catch (EntryPointNotFoundException exception)
        {
            reason =
                "macOS OpenGL.framework does not expose CGLChoosePixelFormat for " +
                "Storm CGL preflight: " + exception.Message;
            return true;
        }

        if (result.Error == CglSuccess &&
            result.PixelFormat != 0 &&
            result.PixelFormatCount > 0)
        {
            reason = null;
            return false;
        }

        reason = result.Error == CglBadPixelFormat || result.PixelFormatCount <= 0
            ? "macOS could not create the OpenGL 4.1 core pixel format."
            : $"macOS CGLChoosePixelFormat failed with CGL error {result.Error}.";
        return true;
    }

    [SupportedOSPlatform("macos")]
    private static MacOSStormCglProbeResult ProbeMacOSStormCgl()
    {
        nint pixelFormat = 0;
        int count = 0;
        int error = CGLChoosePixelFormat(
            [
                CglPfaOpenGlProfile,
                CglOglPVersion41Core,
                CglPfaColorSize,
                24,
                CglPfaAlphaSize,
                8,
                CglPfaDepthSize,
                24,
                CglPfaStencilSize,
                8,
                CglPfaDoubleBuffer,
                CglPfaAccelerated,
                CglPfaNoRecovery,
                0
            ],
            ref pixelFormat,
            out count);
        try
        {
            return new MacOSStormCglProbeResult(error, pixelFormat, count);
        }
        finally
        {
            if (pixelFormat != 0)
            {
                _ = CGLDestroyPixelFormat(pixelFormat);
            }
        }
    }

    internal readonly record struct MacOSStormCglProbeResult(
        int Error,
        nint PixelFormat,
        int PixelFormatCount);

    public async ValueTask<IViewerRenderBackendSession> AttachAsync(
        RenderBackendKind kind,
        StageRenderState initialState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        if (ViewerStartupOptions.IsBackendForcedInitializationFailure(kind))
        {
            throw Failure(
                kind,
                RenderBackendInitializationFailureKind.ResourceCreationFailed,
                "VIEWER_BACKEND_FORCED_INITIALIZATION_FAILURE",
                $"{kind} initialization was disabled by the viewer diagnostic configuration.");
        }

        Interlocked.Increment(ref _attachCounts[(int)kind]);
        return kind switch
        {
            RenderBackendKind.Storm =>
                await AttachStormAsync(initialState, cancellationToken).ConfigureAwait(false),
            RenderBackendKind.D3D12 =>
                OperatingSystem.IsWindows()
                    ? await AttachCompositionAsync(
                        kind,
                        initialState,
                        CreateD3D12Resources,
                        cancellationToken).ConfigureAwait(false)
                    : throw Failure(
                        kind,
                        RenderBackendInitializationFailureKind.UnsupportedConfiguration,
                        "VIEWER_D3D12_PLATFORM_UNSUPPORTED",
                        "Direct3D 12 is available only on Windows."),
            RenderBackendKind.Vulkan =>
                await AttachCompositionAsync(
                    kind,
                    initialState,
                    CreateVulkanResources,
                    cancellationToken).ConfigureAwait(false),
            RenderBackendKind.Metal =>
                OperatingSystem.IsMacOS()
                    ? await AttachCompositionAsync(
                        kind,
                        initialState,
                        CreateMetalResources,
                        cancellationToken).ConfigureAwait(false)
                    : throw Failure(
                        kind,
                        RenderBackendInitializationFailureKind.UnsupportedConfiguration,
                        "VIEWER_METAL_PLATFORM_UNSUPPORTED",
                        "Metal is available only on macOS."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async ValueTask<IViewerRenderBackendSession> AttachStormAsync(
        StageRenderState initialState,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() ||
            OperatingSystem.IsLinux() ||
            OperatingSystem.IsMacOS())
        {
            return await AttachNativeStormAsync(initialState, cancellationToken)
                .ConfigureAwait(false);
        }

        return await AttachLegacyStormAsync(initialState, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<IViewerRenderBackendSession> AttachNativeStormAsync(
        StageRenderState initialState,
        CancellationToken cancellationToken)
    {
        StormNativeControlHost? createdControl = null;
        StormNativeHostedBackendSession? cleanupOwner = null;
        bool keepActive = false;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var control = new StormNativeControlHost(pluginPath, source);
                createdControl = control;
                cleanupOwner = new StormNativeHostedBackendSession(
                    viewportHost,
                    control,
                    initialState,
                    RenderBackendDiagnostics.Empty);
                keepActive = AttachForSurfaceCreation(control);
            });
        }
        catch (OperationCanceledException exception) when (cleanupOwner is not null)
        {
            throw new ViewerBackendAttachmentCanceledException(cleanupOwner, exception);
        }
        catch (Exception exception) when (cleanupOwner is not null)
        {
            throw Failure(
                RenderBackendKind.Storm,
                RenderBackendInitializationFailureKind.ResourceCreationFailed,
                "VIEWER_STORM_NATIVE_CHILD_ATTACHMENT_FAILED",
                $"Storm native child attachment failed: {exception.Message}",
                exception,
                cleanupOwner);
        }
        StormNativeControlHost control = createdControl ??
            throw new InvalidOperationException("The native Storm child could not be created.");
        StormNativeHostedBackendSession owner = cleanupOwner ??
            throw new InvalidOperationException(
                "The native Storm child cleanup owner could not be created.");
        try
        {
            string rendererName = await control
                .WaitForInitializationAsync(cancellationToken)
                .ConfigureAwait(false);
            await HideInitializedCandidateUnlessFirstAsync(
                viewportHost,
                control,
                keepActive,
                cancellationToken).ConfigureAwait(false);
            owner.SetDiagnostics(Diagnostics(
                RenderBackendKind.Storm,
                "VIEWER_STORM_NATIVE_CHILD_READY",
                $"Storm native child initialized on {rendererName}."));
            SetRuntimeIdentity(
                RenderBackendKind.Storm,
                "Hydra/Storm OpenGL",
                rendererName);
            return owner;
        }
        catch (OperationCanceledException exception)
        {
            throw new ViewerBackendAttachmentCanceledException(
                owner,
                exception);
        }
        catch (Exception exception)
        {
            RenderBackendDiagnostics failureDiagnostics = Diagnostics(
                RenderBackendKind.Storm,
                "VIEWER_STORM_NATIVE_CHILD_INITIALIZATION_FAILED",
                $"Storm native child initialization failed: {exception.Message}",
                RenderBackendDiagnosticCategory.Initialization,
                RenderDiagnosticSeverity.Error);
            throw new ViewerBackendInitializationException(
                RenderBackendInitializationFailureKind.ResourceCreationFailed,
                failureDiagnostics,
                exception,
                owner);
        }
    }

    private async ValueTask<IViewerRenderBackendSession> AttachLegacyStormAsync(
        StageRenderState initialState,
        CancellationToken cancellationToken)
    {
        StormViewportControl? createdControl = null;
        bool keepActive = false;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = new StormViewportControl
            {
                PluginPath = pluginPath
            };
            control.SetRenderSource(scheduler, source);
            control.UpdateRenderState(initialState);
            control.StatusChanged += OnControlStatusChanged;
            keepActive = AttachForSurfaceCreation(control);
            createdControl = control;
        });
        StormViewportControl control = createdControl ??
            throw new InvalidOperationException("The Storm viewport could not be created.");
        try
        {
            string rendererName = await control
                .WaitForHostedInitializationAsync(cancellationToken)
                .ConfigureAwait(false);
            await HideInitializedCandidateUnlessFirstAsync(
                viewportHost,
                control,
                keepActive,
                cancellationToken).ConfigureAwait(false);
            SetRuntimeIdentity(
                RenderBackendKind.Storm,
                "Hydra/Storm OpenGL",
                rendererName);
            return new StormHostedBackendSession(
                viewportHost,
                control,
                initialState,
                Diagnostics(
                    RenderBackendKind.Storm,
                    "VIEWER_STORM_READY",
                    $"Storm initialized on {rendererName}."));
        }
        catch (OperationCanceledException)
        {
            await RemoveStormAfterFailureAsync(control).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await RemoveStormAfterFailureAsync(control).ConfigureAwait(false);
            throw Failure(
                RenderBackendKind.Storm,
                RenderBackendInitializationFailureKind.ResourceCreationFailed,
                "VIEWER_STORM_INITIALIZATION_FAILED",
                $"Storm initialization failed: {exception.Message}",
                exception);
        }
    }

    private async ValueTask<IViewerRenderBackendSession> AttachCompositionAsync(
        RenderBackendKind kind,
        StageRenderState initialState,
        Func<StageRenderState, SilkCompositionResources> resourceFactory,
        CancellationToken cancellationToken)
    {
        SilkCompositionResources? resources = null;
        CompositionViewportControl? createdControl = null;
        bool keepActive = false;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = new CompositionViewportControl
            {
                BackendKind = kind,
                ManagerControlsDeviceLoss = true,
                PresenterFactory = () =>
                {
                    resources = resourceFactory(initialState);
                    return resources.Presenter;
                }
            };
            control.StatusChanged += OnControlStatusChanged;
            keepActive = AttachForSurfaceCreation(control);
            createdControl = control;
        });
        CompositionViewportControl control = createdControl ??
            throw new InvalidOperationException("The composition viewport could not be created.");
        try
        {
            bool initialized = await control
                .WaitForInitializationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!initialized || resources is null)
            {
                throw new InvalidOperationException(
                    $"{kind} is incompatible with the active Avalonia compositor.");
            }
            await HideInitializedCandidateUnlessFirstAsync(
                viewportHost,
                control,
                keepActive,
                cancellationToken).ConfigureAwait(false);
            string diagnosticCode = kind == RenderBackendKind.Metal
                ? "VIEWER_METAL_HDSILK_READY"
                : "VIEWER_SILK_READY";
            string diagnosticMessage = kind == RenderBackendKind.Metal
                ? $"Metal hdSilk composition initialized on {resources.Capabilities.DeviceName}."
                : $"{kind} initialized on {resources.Capabilities.DeviceName}.";
            SetRuntimeIdentity(
                kind,
                kind switch
                {
                    RenderBackendKind.D3D12 => "hdSilk Direct3D 12",
                    RenderBackendKind.Vulkan => "hdSilk Vulkan",
                    RenderBackendKind.Metal => "hdSilk Metal",
                    _ => kind.ToString()
                },
                resources.Capabilities);
            return new CompositionHostedBackendSession(
                viewportHost,
                control,
                resources,
                initialState,
                Diagnostics(
                    kind,
                    diagnosticCode,
                    diagnosticMessage,
                    capabilities: resources.Capabilities));
        }
        catch (OperationCanceledException)
        {
            await RemoveCompositionAfterFailureAsync(control, resources).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await RemoveCompositionAfterFailureAsync(control, resources).ConfigureAwait(false);
            throw Failure(
                kind,
                RenderBackendInitializationFailureKind.SurfaceCreationFailed,
                "VIEWER_SILK_INITIALIZATION_FAILED",
                $"{kind} initialization failed: {exception.Message}",
                exception);
        }
    }

    [SupportedOSPlatform("windows")]
    private SilkCompositionResources CreateD3D12Resources(StageRenderState state)
    {
        OpenUsdSilkSession? session = null;
        D3D12SilkGraphicsDevice? device = null;
        SilkMeshRenderer? meshRenderer = null;
        try
        {
            session = OpenUsdSilkRuntime.Create(pluginPath, source);
            device = D3D12SilkGraphicsDevice.Create(useWarp: false);
            meshRenderer = new SilkMeshRenderer(device);
            var renderer = new D3D12StagePresentationRenderer(
                new OpenUsdSilkSessionAdapter(session),
                meshRenderer,
                state);
            var presenter = new D3D12CompositionViewportPresenter(device, renderer);
            return new SilkCompositionResources(
                presenter,
                session,
                meshRenderer,
                device,
                renderer,
                device.Capabilities);
        }
        catch
        {
            meshRenderer?.Dispose();
            device?.Dispose();
            session?.Dispose();
            throw;
        }
    }

    private SilkCompositionResources CreateVulkanResources(StageRenderState state)
    {
        OpenUsdSilkSession? session = null;
        try
        {
            session = OpenUsdSilkRuntime.Create(pluginPath, source);
            var renderer = new VulkanStagePresentationRenderer(
                new OpenUsdSilkSessionAdapter(session),
                state);
            VulkanCompositionViewportPresenter presenter =
                VulkanCompositionViewportPresenter.Create(renderer.Render);
            return new SilkCompositionResources(
                presenter,
                session,
                meshRenderer: null,
                device: null,
                renderer,
                new SilkGraphicsCapabilities(
                    "Vulkan composition device",
                    "Vulkan",
                    SupportsCompute: true,
                    IsSoftware: false));
        }
        catch
        {
            session?.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("macos")]
    private SilkCompositionResources CreateMetalResources(StageRenderState state)
    {
        OpenUsdSilkSession? session = null;
        MetalCompositionViewportPresenter? presenter = null;
        try
        {
            session = OpenUsdSilkRuntime.Create(pluginPath, source);
            var renderer = new MetalStagePresentationRenderer(
                new OpenUsdSilkSessionAdapter(session),
                state);
            presenter = new MetalCompositionViewportPresenter(
                renderer.Render,
                required: true);
            return new SilkCompositionResources(
                presenter,
                session,
                meshRenderer: null,
                device: null,
                renderer,
                new SilkGraphicsCapabilities(
                    "Metal IOSurface hdSilk composition",
                    "Metal",
                    SupportsCompute: true,
                    IsSoftware: false)
                {
                    SupportsDescriptorIndexedTextureTables = true
                });
        }
        catch
        {
            if (presenter is not null)
            {
                presenter.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            session?.Dispose();
            throw;
        }
    }

    private async Task RemoveStormAfterFailureAsync(StormViewportControl control)
    {
        control.StatusChanged -= OnControlStatusChanged;
        await Dispatcher.UIThread.InvokeAsync(() => viewportHost.Detach(control));
    }

    private async Task RemoveCompositionAfterFailureAsync(
        CompositionViewportControl control,
        SilkCompositionResources? resources)
    {
        control.StatusChanged -= OnControlStatusChanged;
        await control.DisposeAsync().ConfigureAwait(false);
        resources?.Dispose();
        await Dispatcher.UIThread.InvokeAsync(() => viewportHost.Detach(control));
    }

    private void OnControlStatusChanged(object? sender, string status) => reportStatus(status);

    private void SetRuntimeIdentity(
        RenderBackendKind kind,
        string api,
        string deviceName) =>
        Volatile.Write(
            ref _runtimeIdentities[(int)kind],
            new ViewerBackendRuntimeIdentity(
                Program.GetConfiguredShellMode(),
                api,
                deviceName));

    private void SetRuntimeIdentity(
        RenderBackendKind kind,
        string api,
        SilkGraphicsCapabilities? capabilities = null) =>
        Volatile.Write(
            ref _runtimeIdentities[(int)kind],
            new ViewerBackendRuntimeIdentity(
                Program.GetConfiguredShellMode(),
                api,
                capabilities?.DeviceName ?? "Unknown",
                capabilities?.SupportsCompute,
                capabilities?.SupportsDescriptorIndexedTextureTables,
                capabilities?.IsSoftware));

    private static bool IsSupportedPlatform(RenderBackendKind kind) =>
        kind switch
        {
            RenderBackendKind.Storm => true,
            RenderBackendKind.D3D12 => OperatingSystem.IsWindows(),
            RenderBackendKind.Vulkan => OperatingSystem.IsWindows() || OperatingSystem.IsLinux(),
            RenderBackendKind.Metal => OperatingSystem.IsMacOS(),
            _ => false
        };

    private static RenderBackendProbeResult Unavailable(
        RenderBackendKind kind,
        RenderBackendProbeFailureKind failure,
        string code,
        string message) =>
        RenderBackendProbeResult.Unavailable(
            failure,
            Diagnostics(
                kind,
                code,
                message,
                RenderBackendDiagnosticCategory.Probe,
                RenderDiagnosticSeverity.Warning,
                failure));

    private static ViewerBackendInitializationException Failure(
        RenderBackendKind kind,
        RenderBackendInitializationFailureKind failure,
        string code,
        string message,
        Exception? exception = null,
        IViewerRenderBackendSession? cleanupOwner = null) =>
        new(
            failure,
            new RenderBackendDiagnostics(
            [
                new RenderBackendDiagnostic(
                    kind,
                    RenderDiagnosticSeverity.Error,
                    RenderBackendDiagnosticCategory.Initialization,
                    code,
                    ViewerPackageErrorFormatter.Format(message, exception),
                    probeFailure: null,
                    initializationFailure: failure,
                    exception?.GetType().FullName,
                    exception?.Message)
            ]),
            exception,
            cleanupOwner);

    private static RenderBackendDiagnostics Diagnostics(
        RenderBackendKind kind,
        string code,
        string message,
        RenderBackendDiagnosticCategory category =
            RenderBackendDiagnosticCategory.Initialization,
        RenderDiagnosticSeverity severity = RenderDiagnosticSeverity.Information,
        RenderBackendProbeFailureKind? probeFailure = null,
        SilkGraphicsCapabilities? capabilities = null) =>
        new(
        [
            new RenderBackendDiagnostic(
                kind,
                severity,
                category,
                code,
                capabilities is null
                    ? message
                    : FormatCapabilityMessage(message, capabilities.Value),
                probeFailure,
                initializationFailure: null,
                exceptionType: null,
                exceptionMessage: null)
        ]);

    private static string FormatCapabilityMessage(
        string message,
        SilkGraphicsCapabilities capabilities) =>
        $"{message} capabilities: compute={capabilities.SupportsCompute}; " +
        $"descriptorIndexedTextures={capabilities.SupportsDescriptorIndexedTextureTables}; " +
        $"software={capabilities.IsSoftware}; api={capabilities.ApiVersion}.";

    [DllImport(OpenGlFramework, EntryPoint = "CGLChoosePixelFormat")]
    private static extern int CGLChoosePixelFormat(
        int[] attributes,
        ref nint pixelFormat,
        out int pixelFormatCount);

    [DllImport(OpenGlFramework, EntryPoint = "CGLDestroyPixelFormat")]
    private static extern int CGLDestroyPixelFormat(nint pixelFormat);
}

internal sealed class StormHostedBackendSession(
    RendererSwitchingViewport viewportHost,
    StormViewportControl control,
    StageRenderState initialState,
    RenderBackendDiagnostics diagnostics) :
    IViewerRenderBackendSession,
    IRenderPickingBackend,
    IViewerRenderedPickStateSource
{
    private StageRenderState _state = initialState;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private int _disposed;

    public RenderBackendDiagnostics Diagnostics { get; } = diagnostics;

    public StageRenderState CurrentState => Volatile.Read(ref _state);

    public ViewerRenderedPickState? LastRenderedPickState =>
        Volatile.Read(ref _lastRenderedPickState);

    public async ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: true),
            DispatcherPriority.Send,
            cancellationToken);
    }

    public async ValueTask DeactivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: false),
            DispatcherPriority.Send,
            cancellationToken);
    }

    public ValueTask UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _state, state);
        control.UpdateRenderState(state);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken)
    {
        StageRenderState state = CurrentState;
        if (ViewerStartupOptions.TryConsumeForcedDeviceLoss(RenderBackendKind.Storm))
        {
            return RenderFrameResult.LostDevice(
                state.Revision,
                RenderDeviceLossKind.Removed,
                new RenderBackendDiagnostics(
                [
                    new RenderBackendDiagnostic(
                        RenderBackendKind.Storm,
                        RenderDiagnosticSeverity.Error,
                        RenderBackendDiagnosticCategory.DeviceLoss,
                        "VIEWER_FORCED_DEVICE_LOSS",
                        "The viewer diagnostic configuration forced device loss.")
                ]));
        }
        Stopwatch timer = Stopwatch.StartNew();
        StormHostedFrameOutcome outcome = await control
            .RenderHostedFrameAsync(cancellationToken)
            .ConfigureAwait(false);
        ulong revision = outcome.StateRevision;
        if (outcome.DeviceLost)
        {
            return RenderFrameResult.LostDevice(
                revision,
                RenderDeviceLossKind.Unknown,
                FrameDiagnostic(
                    RenderBackendKind.Storm,
                    "VIEWER_STORM_CONTEXT_LOST",
                    outcome.Message ?? "Storm lost its OpenGL context."));
        }
        ViewerRenderedPickStateStore.PublishNewest(
            ref _lastRenderedPickState,
            new ViewerRenderedPickState(
                state,
                SceneRevision: null,
                RenderBackendKind.Storm));
        return RenderFrameResult.Rendered(
            revision,
            new RenderFrameStatistics(timer.Elapsed, null, 0, 0));
    }

    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken) =>
        control.PickHostedAsync(request, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }
        try
        {
            await control.ShutdownSoakRendererAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => viewportHost.Detach(control));
        }
        catch
        {
            Volatile.Write(ref _disposed, 0);
            throw;
        }
    }

    private static RenderBackendDiagnostics FrameDiagnostic(
        RenderBackendKind kind,
        string code,
        string message) =>
        new(
        [
            new RenderBackendDiagnostic(
                kind,
                RenderDiagnosticSeverity.Error,
                RenderBackendDiagnosticCategory.DeviceLoss,
                code,
                message)
        ]);
}

internal sealed class StormNativeHostedBackendSession(
    RendererSwitchingViewport viewportHost,
    StormNativeControlHost control,
    StageRenderState initialState,
    RenderBackendDiagnostics diagnostics) :
    IViewerRenderBackendSession,
    IRenderPickingBackend,
    IViewerRenderedPickStateSource
{
    private StageRenderState _state = initialState;
    private SelectionState? _appliedSelection;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private int _disposed;
    private long _lastReportedContextGeneration;

    public RenderBackendDiagnostics Diagnostics { get; private set; } = diagnostics;

    public StageRenderState CurrentState => Volatile.Read(ref _state);

    public ViewerRenderedPickState? LastRenderedPickState =>
        Volatile.Read(ref _lastRenderedPickState);

    internal void SetDiagnostics(RenderBackendDiagnostics value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Diagnostics = value;
    }

    public async ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: true),
            DispatcherPriority.Send,
            cancellationToken);
        ApplySelection(CurrentState.Selection);
    }

    public async ValueTask DeactivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: false),
            DispatcherPriority.Send,
            cancellationToken);
    }

    public ValueTask UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _state, state);
        ApplySelection(state.Selection);
        control.RequestFrame(state);
        return ValueTask.CompletedTask;
    }

    public async ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await control.ResizeAsync(viewport, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RenderFrameResult> RenderAsync(
        CancellationToken cancellationToken)
    {
        StageRenderState state = CurrentState;
        ApplySelection(state.Selection);
        if (ViewerStartupOptions.TryConsumeForcedDeviceLoss(RenderBackendKind.Storm))
        {
            return RenderFrameResult.LostDevice(
                state.Revision,
                RenderDeviceLossKind.Removed,
                new RenderBackendDiagnostics(
                [
                    new RenderBackendDiagnostic(
                        RenderBackendKind.Storm,
                        RenderDiagnosticSeverity.Error,
                        RenderBackendDiagnosticCategory.DeviceLoss,
                        "VIEWER_FORCED_DEVICE_LOSS",
                        "The viewer diagnostic configuration forced device loss.")
                ]));
        }

        Stopwatch timer = Stopwatch.StartNew();
        try
        {
            if (ViewerStartupOptions.TryConsumeNativeStormContextLoss())
            {
                control.SimulateContextLoss();
            }
            OpenUsdStormChildDiagnostics native = await control
                .RenderFrameAsync(state, cancellationToken)
                .ConfigureAwait(false);
            ViewerRenderedPickStateStore.PublishNewest(
                ref _lastRenderedPickState,
                new ViewerRenderedPickState(
                    state,
                    SceneRevision: null,
                    RenderBackendKind.Storm));
            ViewerStartupOptions.RecordNativeStormPixelEvidence(
                native.PixelSampleCount,
                native.PixelSignature);
            if (Interlocked.Exchange(
                    ref _lastReportedContextGeneration,
                    checked((long)native.ContextGeneration)) !=
                checked((long)native.ContextGeneration))
            {
                ViewerStartupOptions.WriteStatus(
                    $"Storm native child frame: frame={native.FrameCount}; " +
                    $"pixelSamples={native.PixelSampleCount}; " +
                    $"pixel=0x{native.PixelSignature:X8}; " +
                    $"context={native.ContextGeneration}; " +
                    $"thread={native.RenderThreadId}; " +
                    $"gl={native.GlMajor}.{native.GlMinor}-" +
                    $"{(native.CompatibilityProfile ? "compat" : "core")}; " +
                    $"queue={native.PendingCommandCount}/{native.PeakPendingCommandCount}; " +
                    $"coalesced={native.CoalescedRequestCount}; " +
                    $"requestedRevision={native.LatestRequestedRevision}; " +
                    $"requestedCamera=0x{native.LatestRequestedCameraSignature:X16}; " +
                    $"renderedCamera=0x{native.LatestRenderedCameraSignature:X16}");
            }
            return RenderFrameResult.Rendered(
                state.Revision,
                new RenderFrameStatistics(
                    timer.Elapsed,
                    gpuTime: null,
                    drawCalls: 0,
                    triangles: 0),
                new RenderBackendDiagnostics(
                [
                    new RenderBackendDiagnostic(
                        RenderBackendKind.Storm,
                        RenderDiagnosticSeverity.Information,
                        RenderBackendDiagnosticCategory.Rendering,
                        "VIEWER_STORM_NATIVE_CHILD_FRAME",
                        $"frame={native.FrameCount}; pixel=0x{native.PixelSignature:X8}; " +
                        $"context={native.ContextGeneration}; " +
                        $"queue={native.PendingCommandCount}/{native.PeakPendingCommandCount}; " +
                        $"coalesced={native.CoalescedRequestCount}; " +
                        $"requestedCamera=0x{native.LatestRequestedCameraSignature:X16}; " +
                        $"renderedCamera=0x{native.LatestRenderedCameraSignature:X16}")
                ]));
        }
        catch (OpenUsdStormException exception)
        {
            return RenderFrameResult.LostDevice(
                state.Revision,
                RenderDeviceLossKind.Unknown,
                new RenderBackendDiagnostics(
                [
                    new RenderBackendDiagnostic(
                        RenderBackendKind.Storm,
                        RenderDiagnosticSeverity.Error,
                        RenderBackendDiagnosticCategory.DeviceLoss,
                        "VIEWER_STORM_NATIVE_CHILD_FAILED",
                        exception.Message,
                        probeFailure: null,
                        initializationFailure: null,
                        exceptionType: exception.GetType().FullName,
                        exceptionMessage: exception.Message)
                ]));
        }
    }

    public async ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken) =>
        await control.PickAsync(request, cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                control.DisposeSession();
                viewportHost.Detach(control);
            });
        }
        catch
        {
            Volatile.Write(ref _disposed, 0);
            throw;
        }
    }

    private void ApplySelection(SelectionState selection)
    {
        if (_appliedSelection == selection)
        {
            return;
        }
        control.SetSelection(selection);
        _appliedSelection = selection;
        ViewerStartupOptions.WriteStatus(
            $"Storm selection applied: items={selection.Items.Count}");
    }
}

internal sealed class CompositionHostedBackendSession(
    RendererSwitchingViewport viewportHost,
    CompositionViewportControl control,
    SilkCompositionResources resources,
    StageRenderState initialState,
    RenderBackendDiagnostics diagnostics) :
    IViewerRenderBackendSession,
    IRenderPickingBackend,
    IViewerRenderedPickStateSource,
    IViewerSelectionOutlineDiagnosticsSource,
    IViewerFrameDiagnosticsSource,
    IViewerHydraSceneSnapshotSource,
    IViewerFrameCaptureBackend
{
    private StageRenderState _state = initialState;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private int _disposed;

    public RenderBackendDiagnostics Diagnostics { get; } = diagnostics;

    public StageRenderState CurrentState => Volatile.Read(ref _state);

    public ViewerRenderedPickState? LastRenderedPickState =>
        Volatile.Read(ref _lastRenderedPickState);

    public SilkSelectionOutlineDiagnostics? SelectionOutlineDiagnostics =>
        resources.Renderer.SelectionOutlineDiagnostics;

    public ViewerSilkFrameDiagnosticsSnapshot? FrameDiagnostics =>
        resources.Renderer.LastCommandCount == 0 &&
        resources.Renderer.LastDrawCount == 0 &&
        resources.Renderer.LastUniformUploads == 0 &&
        resources.Renderer.LastGpuStatistics == default
            ? null
            : new ViewerSilkFrameDiagnosticsSnapshot(
                resources.Renderer.Kind,
                resources.Renderer.LastCommandCount,
                resources.Renderer.LastDrawCount,
                resources.Renderer.LastUniformUploads,
                resources.Renderer.LastGpuStatistics);

    public ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        resources.Renderer.HydraSceneSnapshot;

    public async ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: true),
            DispatcherPriority.Send,
            cancellationToken);
    }

    public async ValueTask DeactivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => viewportHost.SetActive(control, isActive: false),
            DispatcherPriority.Send,
            cancellationToken);
    }

    public ValueTask UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _state, state);
        resources.Renderer.UpdateState(state);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken)
    {
        if (ViewerStartupOptions.TryConsumeForcedDeviceLoss(resources.Renderer.Kind))
        {
            return RenderFrameResult.LostDevice(
                CurrentState.Revision,
                RenderDeviceLossKind.Removed,
                new RenderBackendDiagnostics(
                [
                    new RenderBackendDiagnostic(
                        resources.Renderer.Kind,
                        RenderDiagnosticSeverity.Error,
                        RenderBackendDiagnosticCategory.DeviceLoss,
                        "VIEWER_FORCED_DEVICE_LOSS",
                        "The viewer diagnostic configuration forced device loss.")
                ]));
        }
        Stopwatch timer = Stopwatch.StartNew();
        CompositionPresentOutcome outcome = await control
            .PresentNextFrameAsync(cancellationToken)
            .ConfigureAwait(false);
        ulong revision = outcome.Result == CompositionPresentResult.Presented
            ? resources.Renderer.LastStateRevision
            : CurrentState.Revision;
        if (outcome.Result == CompositionPresentResult.Presented &&
            resources.Renderer.LastRenderedPickState is { } rendered)
        {
            ViewerRenderedPickStateStore.PublishNewest(
                ref _lastRenderedPickState,
                rendered);
        }
        StageRenderState selectionState =
            resources.Renderer.LastRenderedPickState?.State ?? CurrentState;
        RenderBackendDiagnostics selectionDiagnostics =
            ViewerSilkSelectionDiagnostics.Create(
                selectionState,
                resources.Renderer.Kind,
                resources.Renderer.SelectionOutlineDiagnostics);
        return outcome.Result switch
        {
            CompositionPresentResult.Presented => RenderFrameResult.Rendered(
                revision,
                new RenderFrameStatistics(
                    timer.Elapsed,
                    null,
                    resources.Renderer.LastDrawCount,
                    resources.Renderer.LastTriangleCount),
                selectionDiagnostics),
            CompositionPresentResult.Idle or CompositionPresentResult.Backpressured =>
                RenderFrameResult.Skipped(revision, selectionDiagnostics),
            CompositionPresentResult.Lost => RenderFrameResult.LostDevice(
                revision,
                RenderDeviceLossKind.Unknown,
                new RenderBackendDiagnostics(
                [
                    new RenderBackendDiagnostic(
                        resources.Renderer.Kind,
                        RenderDiagnosticSeverity.Error,
                        RenderBackendDiagnosticCategory.DeviceLoss,
                        "VIEWER_COMPOSITION_DEVICE_LOST",
                        "The composition presenter reported device loss.")
                ])),
            _ => RenderFrameResult.Failed(revision)
        };
    }

    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken) =>
        resources.Renderer.PickAsync(request, cancellationToken);

    public ValueTask<SilkFrameCaptureResult> CaptureFrameAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (resources.Device is not ISilkGraphicsDevice device)
        {
            throw new NotSupportedException(
                "Frame capture is available only for hdSilk backends with a managed graphics device.");
        }
        SilkFrameCaptureResult capture = SilkFrameCapture.Capture(
            resources.Session,
            device,
            width,
            height,
            CurrentState.RenderSettings,
            CurrentState.Time.TimeCode,
            CurrentState.Camera);
        return ValueTask.FromResult(capture);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }
        try
        {
            await control.DisposeAsync().ConfigureAwait(false);
            resources.Dispose();
            await Dispatcher.UIThread.InvokeAsync(() => viewportHost.Detach(control));
        }
        catch
        {
            Volatile.Write(ref _disposed, 0);
            throw;
        }
    }

}

internal interface ISilkStagePresentationRenderer :
    IRenderPickingBackend,
    IViewerRenderedPickStateSource
{
    RenderBackendKind Kind { get; }

    uint LastCommandCount => 0;

    int LastDrawCount { get; }

    int LastUniformUploads => 0;

    int LastTriangleCount => 0;

    SilkSceneGpuStatistics LastGpuStatistics => default;

    ulong LastSceneRevision => 0;

    ulong LastStateRevision { get; }

    SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics { get; }

    ViewerHydraSceneSnapshot? HydraSceneSnapshot { get; }

    void UpdateState(StageRenderState state);
}

internal static class ViewerSilkSelectionDiagnostics
{
    internal static RenderBackendDiagnostics Create(
        StageRenderState state,
        RenderBackendKind kind,
        SilkSelectionOutlineDiagnostics outline)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Selection.Items.Count == 0 ||
            outline.Status == SilkSelectionOutlineStatus.Rendered)
        {
            return RenderBackendDiagnostics.Empty;
        }

        (string code, string message) = outline.Status switch
        {
            SilkSelectionOutlineStatus.Disabled => (
                "VIEWER_SILK_SELECTION_OUTLINE_DISABLED",
                "Visible hdSilk selection outlining is disabled."),
            SilkSelectionOutlineStatus.UnsupportedDevice => (
                "VIEWER_SILK_SELECTION_OUTLINE_UNSUPPORTED",
                "The active hdSilk backend does not support visible selection outlines."),
            SilkSelectionOutlineStatus.XRayUnsupported => (
                "VIEWER_SILK_SELECTION_XRAY_UNSUPPORTED",
                "The active hdSilk backend supports visible-only selection outlines, not x-ray."),
            SilkSelectionOutlineStatus.DepthSamplingUnsupported => (
                "VIEWER_SILK_SELECTION_DEPTH_UNAVAILABLE",
                "The active hdSilk depth target is not shader-readable, so the selection " +
                "outline was not rendered."),
            SilkSelectionOutlineStatus.NoMatchingMeshes => (
                "VIEWER_SILK_SELECTION_MESH_UNAVAILABLE",
                "No retained hdSilk mesh matched the selected prim path."),
            _ => (
                "VIEWER_SILK_SELECTION_OUTLINE_PENDING",
                "The visible hdSilk selection outline is waiting for the next rendered frame.")
        };
        return new RenderBackendDiagnostics(
        [
            new RenderBackendDiagnostic(
                kind,
                RenderDiagnosticSeverity.Warning,
                RenderBackendDiagnosticCategory.Selection,
                code,
                message)
        ]);
    }
}

internal sealed class D3D12StagePresentationRenderer(
    IViewerSilkSessionAdapter session,
    SilkMeshRenderer renderer,
    StageRenderState initialState)
    : ISilkPresentationRenderer, ISilkStagePresentationRenderer
{
    private StageRenderState _state = initialState;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private SilkSceneGpuStatistics _lastGpuStatistics;
    private uint _lastCommandCount;
    private int _lastDrawCount;
    private int _lastUniformUploads;
    private ulong _lastSceneRevision;
    private ulong _lastStateRevision;
    private ViewerHydraSceneSnapshot? _hydraSceneSnapshot;

    public RenderBackendKind Kind => RenderBackendKind.D3D12;

    public uint LastCommandCount => Volatile.Read(ref _lastCommandCount);

    public int LastDrawCount => Volatile.Read(ref _lastDrawCount);

    public int LastUniformUploads => Volatile.Read(ref _lastUniformUploads);

    public SilkSceneGpuStatistics LastGpuStatistics => _lastGpuStatistics;

    public ulong LastSceneRevision => Volatile.Read(ref _lastSceneRevision);

    public ulong LastStateRevision => Volatile.Read(ref _lastStateRevision);

    public SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics =>
        renderer.SelectionOutlineDiagnostics;

    public ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        Volatile.Read(ref _hydraSceneSnapshot);

    public ViewerRenderedPickState? LastRenderedPickState =>
        Volatile.Read(ref _lastRenderedPickState);

    public void UpdateState(StageRenderState state) => Volatile.Write(ref _state, state);

    public SilkPresentationRenderResult Render(
        ISilkGraphicsTexture colorTarget,
        ISilkGraphicsTexture depthTarget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StageRenderState state = Volatile.Read(ref _state);
        using OpenUsdSilkPage page = ViewerFrameAdapter.SyncSilk(
            session,
            checked((int)colorTarget.Width),
            checked((int)colorTarget.Height),
            state);
        Volatile.Write(ref _hydraSceneSnapshot, ViewerHydraSceneSnapshot.FromPage(page));
        renderer.UpdateSelection(
            state.Selection,
            SilkSelectionOutlineSettings.Default);
        SilkMeshRenderResult result = renderer.ApplyAndRender(
            page,
            colorTarget,
            depthTarget,
            SilkPickFrameBinding.FromState(state, sceneRevision: null),
            ViewerViewportStateMutation.ToSilkOptions(state.RenderSettings));
        Volatile.Write(ref _lastCommandCount, page.CommandCount);
        Volatile.Write(ref _lastDrawCount, result.DrawCount);
        Volatile.Write(ref _lastUniformUploads, result.UniformUploads);
        _lastGpuStatistics = result.Statistics;
        Volatile.Write(ref _lastSceneRevision, page.Revision);
        Volatile.Write(ref _lastStateRevision, state.Revision);
        ViewerRenderedPickStateStore.PublishNewest(
            ref _lastRenderedPickState,
            new ViewerRenderedPickState(
                state,
                SceneRevision: null,
                RenderBackendKind.D3D12));
        return new SilkPresentationRenderResult(
            page.Revision,
            result.DrawCount,
            ContinueRendering: false);
    }

    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken) =>
        ViewerSilkPickDispatch.PickAsync(
            renderer,
            LastRenderedPickState,
            Volatile.Read(ref _state),
            request,
            cancellationToken);
}

internal sealed class VulkanStagePresentationRenderer(
    IViewerSilkSessionAdapter session,
    StageRenderState initialState) : ISilkStagePresentationRenderer
{
    private SilkMeshRenderer? _currentRenderer;
    private StageRenderState _state = initialState;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private SilkSceneGpuStatistics _lastGpuStatistics;
    private uint _lastCommandCount;
    private int _lastDrawCount;
    private int _lastUniformUploads;
    private ulong _lastSceneRevision;
    private ulong _lastStateRevision;
    private ViewerHydraSceneSnapshot? _hydraSceneSnapshot;

    public RenderBackendKind Kind => RenderBackendKind.Vulkan;

    public uint LastCommandCount => Volatile.Read(ref _lastCommandCount);

    public int LastDrawCount => Volatile.Read(ref _lastDrawCount);

    public int LastUniformUploads => Volatile.Read(ref _lastUniformUploads);

    public SilkSceneGpuStatistics LastGpuStatistics => _lastGpuStatistics;

    public ulong LastSceneRevision => Volatile.Read(ref _lastSceneRevision);

    public ulong LastStateRevision => Volatile.Read(ref _lastStateRevision);

    public SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics =>
        Volatile.Read(ref _currentRenderer)?.SelectionOutlineDiagnostics ?? default;

    public ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        Volatile.Read(ref _hydraSceneSnapshot);

    public ViewerRenderedPickState? LastRenderedPickState =>
        Volatile.Read(ref _lastRenderedPickState);

    public void UpdateState(StageRenderState state) => Volatile.Write(ref _state, state);

    public SilkMeshRenderResult Render(VulkanCompositionRenderContext context)
    {
        StageRenderState state = Volatile.Read(ref _state);
        Volatile.Write(ref _currentRenderer, context.Renderer);
        using OpenUsdSilkPage page = ViewerFrameAdapter.SyncSilk(
            session,
            checked((int)context.ColorTarget.Width),
            checked((int)context.ColorTarget.Height),
            state);
        Volatile.Write(ref _hydraSceneSnapshot, ViewerHydraSceneSnapshot.FromPage(page));
        context.Renderer.UpdateSelection(
            state.Selection,
            SilkSelectionOutlineSettings.Default);
        SilkMeshRenderResult result = context.Renderer.ApplyAndRender(
            page,
            context.ColorTarget,
            context.DepthTarget,
            SilkPickFrameBinding.FromState(state, sceneRevision: null),
            ViewerViewportStateMutation.ToSilkOptions(state.RenderSettings));
        Volatile.Write(ref _lastCommandCount, page.CommandCount);
        Volatile.Write(ref _lastDrawCount, result.DrawCount);
        Volatile.Write(ref _lastUniformUploads, result.UniformUploads);
        _lastGpuStatistics = result.Statistics;
        Volatile.Write(ref _lastSceneRevision, page.Revision);
        Volatile.Write(ref _lastStateRevision, state.Revision);
        ViewerRenderedPickStateStore.PublishNewest(
            ref _lastRenderedPickState,
            new ViewerRenderedPickState(
                state,
                SceneRevision: null,
                RenderBackendKind.Vulkan));
        return result;
    }

    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken) =>
        ViewerSilkPickDispatch.PickAsync(
            Volatile.Read(ref _currentRenderer),
            LastRenderedPickState,
            Volatile.Read(ref _state),
            request,
            cancellationToken);
}

internal sealed class MetalStagePresentationRenderer(
    IViewerSilkSessionAdapter session,
    StageRenderState initialState) : ISilkStagePresentationRenderer
{
    private SilkMeshRenderer? _currentRenderer;
    private StageRenderState _state = initialState;
    private ViewerRenderedPickState? _lastRenderedPickState;
    private SilkSceneGpuStatistics _lastGpuStatistics;
    private uint _lastCommandCount;
    private int _lastDrawCount;
    private int _lastUniformUploads;
    private int _lastTriangleCount;
    private ulong _lastSceneRevision;
    private ulong _lastStateRevision;
    private long _lastReportedRevision;
    private ViewerHydraSceneSnapshot? _hydraSceneSnapshot;

    public RenderBackendKind Kind => RenderBackendKind.Metal;

    public uint LastCommandCount => Volatile.Read(ref _lastCommandCount);

    public int LastDrawCount => Volatile.Read(ref _lastDrawCount);

    public int LastUniformUploads => Volatile.Read(ref _lastUniformUploads);

    public int LastTriangleCount => Volatile.Read(ref _lastTriangleCount);

    public SilkSceneGpuStatistics LastGpuStatistics => _lastGpuStatistics;

    public ulong LastSceneRevision => Volatile.Read(ref _lastSceneRevision);

    public ulong LastStateRevision => Volatile.Read(ref _lastStateRevision);

    public SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics =>
        Volatile.Read(ref _currentRenderer)?.SelectionOutlineDiagnostics ?? default;

    public ViewerHydraSceneSnapshot? HydraSceneSnapshot =>
        Volatile.Read(ref _hydraSceneSnapshot);

    public ViewerRenderedPickState? LastRenderedPickState =>
        Volatile.Read(ref _lastRenderedPickState);

    public void UpdateState(StageRenderState state) => Volatile.Write(ref _state, state);

    public MetalCompositionRenderResult Render(MetalCompositionRenderContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        StageRenderState state = Volatile.Read(ref _state);
        Volatile.Write(ref _currentRenderer, context.Renderer);
        using OpenUsdSilkPage page = ViewerFrameAdapter.SyncSilk(
            session,
            checked((int)context.ColorTarget.Width),
            checked((int)context.ColorTarget.Height),
            state);
        Volatile.Write(ref _hydraSceneSnapshot, ViewerHydraSceneSnapshot.FromPage(page));
        context.Renderer.UpdateSelection(
            state.Selection,
            SilkSelectionOutlineSettings.Default);
        SilkMeshRenderResult result = context.Renderer.ApplyAndRender(
            page,
            context.ColorTarget,
            context.DepthTarget,
            SilkPickFrameBinding.FromState(state, sceneRevision: null),
            ViewerViewportStateMutation.ToSilkOptions(state.RenderSettings));
        Volatile.Write(ref _lastCommandCount, page.CommandCount);
        int triangles = checked((int)context.Renderer.GpuResources.Meshes.Values.Sum(
            mesh => (long)mesh.IndexCount / 3));
        Volatile.Write(ref _lastDrawCount, result.DrawCount);
        Volatile.Write(ref _lastUniformUploads, result.UniformUploads);
        Volatile.Write(ref _lastTriangleCount, triangles);
        _lastGpuStatistics = result.Statistics;
        Volatile.Write(ref _lastSceneRevision, page.Revision);
        Volatile.Write(ref _lastStateRevision, state.Revision);
        ViewerRenderedPickStateStore.PublishNewest(
            ref _lastRenderedPickState,
            new ViewerRenderedPickState(
                state,
                SceneRevision: null,
                RenderBackendKind.Metal));
        if (Interlocked.Exchange(
                ref _lastReportedRevision,
                checked((long)page.Revision)) != checked((long)page.Revision))
        {
            ViewerStartupOptions.WriteStatus(
                $"Metal hdSilk frame: revision={page.Revision}; draws={result.DrawCount}; " +
                $"triangles={triangles}; allocation={context.AllocationId}; " +
                $"ring={context.FrameIndex}/{context.UseCount}");
        }
        return new MetalCompositionRenderResult(page.Revision, result);
    }

    public ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken) =>
        ViewerSilkPickDispatch.PickAsync(
            Volatile.Read(ref _currentRenderer),
            LastRenderedPickState,
            Volatile.Read(ref _state),
            request,
            cancellationToken);
}

internal static class ViewerSilkPickDispatch
{
    internal static ValueTask<RenderPickResult> PickAsync(
        SilkMeshRenderer? renderer,
        ViewerRenderedPickState? rendered,
        StageRenderState currentState,
        RenderPickRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        cancellationToken.ThrowIfCancellationRequested();
        if (rendered is null || renderer is null)
        {
            ulong? sceneRevision = rendered?.SceneRevision ??
                request.RequestedSceneRevision;
            return ValueTask.FromResult(
                request.IsStale(currentState.Revision, sceneRevision)
                    ? RenderPickResult.Stale(
                        request,
                        currentState.Revision,
                        sceneRevision)
                    : RenderPickResult.Unsupported(
                        request,
                        currentState.Revision,
                        sceneRevision));
        }
        if (request.IsStale(
                rendered.State.Revision,
                rendered.SceneRevision))
        {
            return ValueTask.FromResult(RenderPickResult.Stale(
                request,
                rendered.State.Revision,
                rendered.SceneRevision));
        }
        return renderer.PickAsync(request, cancellationToken);
    }
}

internal sealed class SilkCompositionResources(
    ICompositionViewportPresenter presenter,
    OpenUsdSilkSession session,
    SilkMeshRenderer? meshRenderer,
    IDisposable? device,
    ISilkStagePresentationRenderer renderer,
    SilkGraphicsCapabilities capabilities) : IDisposable
{
    private IDisposable? _device = device;
    private SilkMeshRenderer? _meshRenderer = meshRenderer;
    private OpenUsdSilkSession? _session = session;
    private int _disposed;

    internal ICompositionViewportPresenter Presenter { get; } = presenter;

    internal ISilkStagePresentationRenderer Renderer { get; } = renderer;

    internal SilkGraphicsCapabilities Capabilities { get; } = capabilities;

    internal OpenUsdSilkSession Session =>
        Volatile.Read(ref _session) ??
        throw new ObjectDisposedException(nameof(SilkCompositionResources));

    internal IDisposable? Device => Volatile.Read(ref _device);

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        List<Exception>? failures = null;
        TryDispose(ref _meshRenderer, ref failures);
        TryDispose(ref _session, ref failures);
        TryDispose(ref _device, ref failures);
        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more Silk composition resources remain owned for cleanup retry.",
                failures);
        }
        Volatile.Write(ref _disposed, 1);
    }

    private static void TryDispose<T>(ref T? owner, ref List<Exception>? failures)
        where T : class, IDisposable
    {
        T? value = owner;
        if (value is null)
        {
            return;
        }
        try
        {
            value.Dispose();
            owner = null;
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }
}
