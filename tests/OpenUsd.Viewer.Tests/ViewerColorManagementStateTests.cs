// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Gates the three Viewer behaviours a colour-managed display transform depends on:
/// surviving every other viewport toggle, never being claimed when the renderer refused
/// it, and being restored into a newly opened stage rather than only into the menu.
/// </summary>
public sealed class ViewerColorManagementStateTests
{
    private static string ConfigPath { get; } = Path.Combine(
        Path.GetFullPath(AppContext.BaseDirectory),
        "viewer-color-management-tests.ocio");

    private static RenderDisplayTransform Transform { get; } =
        new(ConfigPath, "linear", "sRGB", "Film");

    private static StageRenderState WithTransform() =>
        ViewerViewportStateMutation.WithDisplayTransform(
            StageRenderState.Default.WithRenderSettings(RenderSettings.PresentationDefault),
            Transform);

    [Test]
    public async Task EveryViewportToggleKeepsTheDisplayTransform()
    {
        // Each of these used to rebuild RenderSettings positionally, which silently
        // dropped the display transform: turning shadows off unset colour management.
        (string Name, Func<StageRenderState, StageRenderState> Mutate)[] mutations =
        [
            ("lighting off", state =>
                ViewerViewportStateMutation.WithLighting(state, enabled: false)),
            ("lighting on", state =>
                ViewerViewportStateMutation.WithLighting(state, enabled: true)),
            ("shadows off", state =>
                ViewerViewportStateMutation.WithShadows(state, enabled: false)),
            ("shadows on", state =>
                ViewerViewportStateMutation.WithShadows(state, enabled: true)),
            ("backface culling off", state =>
                ViewerViewportStateMutation.WithBackfaceCulling(state, enabled: false)),
            ("backface culling on", state =>
                ViewerViewportStateMutation.WithBackfaceCulling(state, enabled: true)),
            ("scene materials off", state =>
                ViewerViewportStateMutation.WithSceneMaterials(state, enabled: false)),
            ("scene materials on", state =>
                ViewerViewportStateMutation.WithSceneMaterials(state, enabled: true)),
            ("background white", state =>
                ViewerViewportStateMutation.WithBackground(
                    state,
                    ViewerBackgroundPreset.White)),
            ("background black", state =>
                ViewerViewportStateMutation.WithBackground(
                    state,
                    ViewerBackgroundPreset.Black)),
            ("draw mode", state =>
                ViewerViewportStateMutation.WithDrawMode(
                    state,
                    RenderDrawMode.WireframeOnSurface)),
            ("purpose", state =>
                ViewerViewportStateMutation.WithPurpose(
                    state,
                    RenderPurpose.Guide,
                    enabled: true)),
        ];

        foreach ((string name, Func<StageRenderState, StageRenderState> mutate) in mutations)
        {
            StageRenderState mutated = mutate(WithTransform());
            await Assert.That(mutated.RenderSettings.DisplayTransform)
                .IsEqualTo(Transform)
                .Because($"the '{name}' toggle must preserve the display transform");
            await Assert.That(mutated.RenderSettings.OutputTransform)
                .IsEqualTo(RenderOutputTransform.Identity)
                .Because($"the '{name}' toggle must not reintroduce a built-in transform");
            mutated.RenderSettings.ValidateDisplayTransform();
        }
    }

    [Test]
    public async Task ChainedViewportTogglesKeepTheDisplayTransform()
    {
        StageRenderState state = WithTransform();
        state = ViewerViewportStateMutation.WithLighting(state, enabled: false);
        state = ViewerViewportStateMutation.WithShadows(state, enabled: false);
        state = ViewerViewportStateMutation.WithBackground(
            state,
            ViewerBackgroundPreset.LightGray);
        state = ViewerViewportStateMutation.WithBackfaceCulling(state, enabled: false);
        state = ViewerViewportStateMutation.WithSceneMaterials(state, enabled: false);

        await Assert.That(state.RenderSettings.DisplayTransform).IsEqualTo(Transform);
        await Assert.That(state.RenderSettings.EnableLighting).IsFalse();
        await Assert.That(state.RenderSettings.EnableShadows).IsFalse();
        await Assert.That(state.RenderSettings.BackfaceCulling).IsFalse();
        await Assert.That(state.RenderSettings.UseSceneMaterials).IsFalse();
    }

    [Test]
    public async Task TogglesDoNotIntroduceADisplayTransformWhenThereIsNone()
    {
        StageRenderState state = StageRenderState.Default
            .WithRenderSettings(RenderSettings.PresentationDefault);
        state = ViewerViewportStateMutation.WithLighting(state, enabled: false);

        await Assert.That(state.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(state.RenderSettings.OutputTransform)
            .IsEqualTo(RenderSettings.PresentationDefault.OutputTransform);
    }

    [Test]
    [Arguments(SilkDisplayTransformStatus.ConfigUnavailable)]
    [Arguments(SilkDisplayTransformStatus.TransformUnsupported)]
    [Arguments(SilkDisplayTransformStatus.UnsupportedDevice)]
    public async Task ARefusedTransformIsNeverClaimedAsActive(
        SilkDisplayTransformStatus status)
    {
        var diagnostic = new RenderDiagnostic(
            RenderDiagnosticSeverity.Warning,
            "OPENUSD_SILK_DISPLAY_TRANSFORM_CONFIG_UNAVAILABLE",
            "The config went away.");
        ViewerColorManagementSyncResult result = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: "key",
            hasPendingRequest: false,
            status,
            backendRequestKey: "key",
            diagnostic);

        await Assert.That(result.State).IsEqualTo(ViewerColorManagementState.Failed);
        await Assert.That(result.Enabled).IsFalse();
        await Assert.That(result.ClearTransform).IsTrue();
        await Assert.That(result.Status).IsEqualTo("The config went away.");
    }

    [Test]
    public async Task ARefusedTransformWithoutADiagnosticStillReportsAReason()
    {
        ViewerColorManagementSyncResult result = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: "key",
            hasPendingRequest: false,
            SilkDisplayTransformStatus.TransformUnsupported,
            backendRequestKey: "key",
            diagnostic: null);

        await Assert.That(result.State).IsEqualTo(ViewerColorManagementState.Failed);
        await Assert.That(result.Status).IsNotNull();
        await Assert.That(result.Status!).Contains("untransformed");
    }

    [Test]
    public async Task AnAppliedTransformStaysClaimed()
    {
        ViewerColorManagementSyncResult result = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: "key",
            hasPendingRequest: false,
            SilkDisplayTransformStatus.Applied,
            backendRequestKey: "key",
            diagnostic: null);

        await Assert.That(result.State).IsEqualTo(ViewerColorManagementState.Active);
        await Assert.That(result.Enabled).IsTrue();
        await Assert.That(result.ClearTransform).IsFalse();
        await Assert.That(result.Status).IsNull();
    }

    [Test]
    public async Task AnUnrenderedOrBackendlessRequestIsNotTreatedAsAFailure()
    {
        // Nothing has been rendered yet, so there is nothing to contradict the request.
        // Turning the toggle off here would fight the user on every stage open.
        ViewerColorManagementSyncResult unknown = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: "key",
            hasPendingRequest: false,
            backendStatus: null,
            backendRequestKey: null,
            diagnostic: null);
        ViewerColorManagementSyncResult inactive = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: "key",
            hasPendingRequest: false,
            SilkDisplayTransformStatus.Inactive,
            backendRequestKey: null,
            diagnostic: null);

        await Assert.That(unknown.State).IsEqualTo(ViewerColorManagementState.Active);
        await Assert.That(unknown.ClearTransform).IsFalse();
        await Assert.That(inactive.State).IsEqualTo(ViewerColorManagementState.Active);
        await Assert.That(inactive.ClearTransform).IsFalse();
    }

    [Test]
    public async Task ADisabledRequestClearsTheTransformWithoutComplaining()
    {
        ViewerColorManagementSyncResult result = ViewerColorManagementSync.Compute(
            requestedEnabled: false,
            committedRequestKey: "key",
            hasPendingRequest: false,
            SilkDisplayTransformStatus.Applied,
            backendRequestKey: "key",
            diagnostic: null);

        await Assert.That(result.State).IsEqualTo(ViewerColorManagementState.Disabled);
        await Assert.That(result.Enabled).IsFalse();
        await Assert.That(result.ClearTransform).IsTrue();
        await Assert.That(result.Status).IsNull();
    }

    [Test]
    public async Task ARestoredEnabledChoiceProducesTheOpeningRenderSettings()
    {
        // This is the shape of what a newly opened coordinator is initialized with: the
        // persisted choice has to arrive as render settings, not merely as a checked menu
        // item, or the first presented frame of every stage is untransformed.
        var colorManagement = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = ConfigPath,
            SourceColorSpace = "linear",
            Display = "sRGB",
            View = "Film",
        };
        bool resolved = colorManagement.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);
        await Assert.That(resolved).IsTrue();
        await Assert.That(diagnostic).IsNull();

        RenderSettings opening = ViewerViewportStateMutation.CopyRenderSettings(
            RenderSettings.PresentationDefault,
            outputTransform: RenderOutputTransform.Identity,
            displayTransform: transform);
        opening.ValidateDisplayTransform();

        await Assert.That(opening.DisplayTransform).IsEqualTo(transform);
        await Assert.That(opening.OutputTransform)
            .IsEqualTo(RenderOutputTransform.Identity);
        await Assert.That(opening.Exposure)
            .IsEqualTo(RenderSettings.PresentationDefault.Exposure);
        await Assert.That(opening.ClearColor)
            .IsEqualTo(RenderSettings.PresentationDefault.ClearColor);
    }

    [Test]
    public async Task ARestoredDisabledChoiceOpensWithThePresentationDefault()
    {
        bool resolved = ViewerColorManagement.Default.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);

        await Assert.That(resolved).IsFalse();
        await Assert.That(transform).IsNull();
        await Assert.That(diagnostic).IsNull();
        await Assert.That(RenderSettings.PresentationDefault.DisplayTransform).IsNull();
    }

    [Test]
    public async Task ARestoredChoiceWithADeletedConfigIsReportedRatherThanApplied()
    {
        string missing = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"deleted-{Guid.NewGuid():N}.ocio");
        var colorManagement = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = missing,
            SourceColorSpace = "linear",
        };

        // Resolving succeeds because the names are well formed; validating is what proves
        // the config is actually usable, and that is what the Viewer does before it
        // claims a transform.
        bool resolved = colorManagement.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);
        await Assert.That(resolved).IsTrue();
        await Assert.That(diagnostic).IsNull();

        string? failure = ViewerColorManagementValidation.Validate(transform!);
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!).Contains("not found");
    }

    [Test]
    public async Task ARestoredChoiceWithARelativeConfigIsReportedRatherThanApplied()
    {
        var colorManagement = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = Path.Combine("relative", "config.ocio"),
            SourceColorSpace = "linear",
        };

        bool resolved = colorManagement.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);

        await Assert.That(resolved).IsFalse();
        await Assert.That(transform).IsNull();
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!).Contains("not usable");
    }

    [Test]
    public async Task TheOpeningRenderSettingsAreWiredThroughEveryStageOpen()
    {
        // The unit assertions above prove the settings are built correctly. This proves
        // they actually reach the coordinator, which is the difference between restoring
        // a menu item and restoring the image.
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string coordinator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerRenderCoordinator.cs"));

        await Assert.That(window).Contains("openingSettings,");
        await Assert.That(coordinator).Contains(
            "RenderSettings initialRenderSettings,");
        await Assert.That(coordinator).Contains(
            ".WithRenderSettings(initialRenderSettings);");
        await Assert.That(coordinator).DoesNotContain(
            ".WithRenderSettings(RenderSettings.PresentationDefault);");
        await Assert.That(coordinator).Contains(
            "initialRenderSettings.ValidateDisplayTransform();");
    }

    [Test]
    public async Task EveryViewportSettingMutationRoutesThroughTheSharedCopy()
    {
        // A future toggle that builds RenderSettings positionally would silently drop
        // the display transform again, so the single copy helper is pinned here.
        string root = FindRepositoryRoot();
        string models = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerViewportModels.cs"));

        int copyUses = CountOccurrences(models, "CopyRenderSettings(");
        await Assert.That(copyUses).IsGreaterThanOrEqualTo(7);

        // Exactly one positional construction remains: the copy helper itself.
        await Assert.That(CountOccurrences(models, "new RenderSettings(")).IsEqualTo(1);
        await Assert.That(models).Contains("DisplayTransform = clearDisplayTransform");
    }

    [Test]
    public async Task AnUnsupportedBackendReportsUnsupportedDeviceForARequestedTransform()
    {
        // This is the production reporting type the Storm sessions expose, not a stand-in
        // for it: a backend with no fullscreen display-transform pass has to say so, or
        // the Viewer keeps claiming a transform that never ran.
        StageRenderState requested = WithTransform();
        SilkDisplayTransformDiagnostics diagnostics =
            ViewerUnsupportedDisplayTransform.Describe(requested);
        RenderDiagnostic? diagnostic =
            ViewerUnsupportedDisplayTransform.Diagnose(requested);

        await Assert.That(diagnostics.Status)
            .IsEqualTo(SilkDisplayTransformStatus.UnsupportedDevice);
        await Assert.That(diagnostics.Failures).IsEqualTo(1UL);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Code).IsEqualTo(
            SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported);

        ViewerColorManagementSyncResult result = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: requested.RenderSettings.DisplayTransform?.CacheKey,
            hasPendingRequest: false,
            diagnostics.Status,
            diagnostics.RequestKey,
            diagnostic);
        await Assert.That(result.State).IsEqualTo(ViewerColorManagementState.Failed);
        await Assert.That(result.Enabled).IsFalse();
        await Assert.That(result.ClearTransform).IsTrue();
    }

    [Test]
    public async Task AnUnsupportedBackendStaysSilentWhenNoTransformIsRequested()
    {
        StageRenderState plain = StageRenderState.Default
            .WithRenderSettings(RenderSettings.PresentationDefault);

        await Assert.That(ViewerUnsupportedDisplayTransform.Describe(plain).Status)
            .IsEqualTo(SilkDisplayTransformStatus.Inactive);
        await Assert.That(ViewerUnsupportedDisplayTransform.Describe(plain).Failures)
            .IsEqualTo(0UL);
        await Assert.That(ViewerUnsupportedDisplayTransform.Diagnose(plain)).IsNull();
    }

    [Test]
    public async Task EveryBackendSessionForwardsDisplayTransformDiagnostics()
    {
        // The sync rule is only worth anything if the diagnostics actually reach it. Each
        // session type -- hosted Storm, native Storm, and the Silk composition session --
        // must implement the source, and the registry, adapter, and coordinator must all
        // forward it, or a Storm viewport silently keeps a checked menu item.
        string root = FindRepositoryRoot();
        string host = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "AvaloniaViewerRenderBackendHost.cs"));
        string adapters = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "ViewerRenderBackendAdapters.cs"));
        string coordinator = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "ViewerRenderCoordinator.cs"));

        int sessionCount = CountOccurrences(host, "IViewerRenderBackendSession,\n");
        int sourceCount = CountOccurrences(
            host,
            "IViewerDisplayTransformDiagnosticsSource,");
        await Assert.That(sessionCount).IsEqualTo(3);
        await Assert.That(sourceCount).IsEqualTo(sessionCount);

        await Assert.That(host).Contains(
            "ViewerUnsupportedDisplayTransform.Describe(CurrentState)");
        await Assert.That(host).Contains(
            "resources.Renderer.DisplayTransformDiagnostics");
        await Assert.That(host).Contains(
            "SilkDisplayTransformDiagnostics DisplayTransformDiagnostics => default;");

        await Assert.That(adapters).Contains(
            "internal SilkDisplayTransformDiagnostics? CaptureDisplayTransform()");
        await Assert.That(adapters).Contains(
            "internal RenderDiagnostic? CaptureDisplayTransformDiagnostic()");
        await Assert.That(adapters).Contains(
            "(_session as IViewerDisplayTransformDiagnosticsSource)?");
        await Assert.That(coordinator).Contains(
            "_backendRegistry.CaptureDisplayTransform();");
        await Assert.That(coordinator).Contains(
            "_backendRegistry.CaptureDisplayTransformDiagnostic();");
    }

    [Test]
    public async Task TheRestoredTransformIsSemanticallyValidatedBeforeTheCoordinatorOpens()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));
        string colorManagement = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        // Awaited, never waited on: a Wait() or .Result here would deadlock the very
        // dispatcher thread the bake's continuation needs.
        await Assert.That(window).Contains(
            "RenderSettings openingSettings = await BuildInitialRenderSettingsAsync();");
        await Assert.That(window).DoesNotContain("BuildInitialRenderSettings()");
        await Assert.That(colorManagement).Contains(
            "internal async Task<RenderSettings> BuildInitialRenderSettingsAsync()");
        await Assert.That(colorManagement).Contains(
            "ViewerColorManagementValidation.Validate(restored))");
        await Assert.That(colorManagement).DoesNotContain(".Result");
        await Assert.That(colorManagement).DoesNotContain(".Wait()");
        await Assert.That(colorManagement).DoesNotContain("GetAwaiter().GetResult()");
    }

    [Test]
    public async Task DisposalStopsUnsubscribesAndDropsTheColorManagementPoller()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));
        string colorManagement = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        int disposeIndex = window.IndexOf(
            "    public void Dispose()",
            StringComparison.Ordinal);
        await Assert.That(disposeIndex).IsGreaterThan(0);
        string dispose = window[disposeIndex..];
        int stopIndex = dispose.IndexOf(
            "StopColorManagementPolling();",
            StringComparison.Ordinal);
        int stormIndex = dispose.IndexOf(
            "StopStormNavigationPolling();",
            StringComparison.Ordinal);

        // First, before any other teardown: a live poll loop can tick against
        // half-disposed state.
        await Assert.That(stopIndex).IsGreaterThan(0);
        await Assert.That(stopIndex).IsLessThan(stormIndex);

        // Closing drains before it disposes anything a tick touches, and it does so
        // before the first awaited shutdown step rather than after it.
        int closingIndex = window.IndexOf(
            "private async void OnClosing(",
            StringComparison.Ordinal);
        await Assert.That(closingIndex).IsGreaterThan(0);
        string closing = window[closingIndex..disposeIndex];
        int drainIndex = closing.IndexOf(
            "await StopColorManagementPollingAsync();",
            StringComparison.Ordinal);
        await Assert.That(drainIndex).IsGreaterThan(0);
        await Assert.That(drainIndex).IsLessThan(
            closing.IndexOf("await SaveSettingsAsync();", StringComparison.Ordinal));
        await Assert.That(drainIndex).IsLessThan(
            closing.IndexOf("_viewerLifetime.Cancel();", StringComparison.Ordinal));

        // No async void tick is left anywhere in the colour-management surface: an
        // unawaitable tick cannot be drained and its faults reach the dispatcher
        // unobserved.
        await Assert.That(colorManagement).DoesNotContain("private async void OnColorManagement");
        await Assert.That(colorManagement).DoesNotContain("DispatcherTimer");
        await Assert.That(colorManagement).Contains(
            "Interlocked.Exchange(ref _colorManagementPoller, null)?.Dispose();");
        await Assert.That(colorManagement).Contains(
            "internal bool HasColorManagementTimer => _colorManagementPoller is not null;");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
