// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenUsd.Geom;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal enum ViewerLayerCommand
{
    SetSessionEditTarget,
    SetRootEditTarget,
    Mute,
    Unmute
}

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly CancellationTokenSource _viewerLifetime = new();
    private readonly CancellationTokenRegistration _hostShutdown;
    private readonly SemaphoreSlim _documentGate = new(1, 1);
    private readonly RecentStageStore _recentStageStore;
    private readonly ViewerSettingsStore _settingsStore;
    private readonly ViewerDiagnosticsBuffer _diagnostics = new();
    private readonly ViewerDiagnosticsCadence _diagnosticsCadence =
        new(TimeSpan.FromMilliseconds(500));
    private readonly ViewerDiagnosticsFormatter _diagnosticsFormatter =
        new(ViewerPathRedactor.CreateDefault());
    private readonly ViewerStageCameraModeState _stageCameraMode =
        new(ViewportDimensions.Empty);
    private readonly ViewerCameraNavigationController _cameraNavigationController =
        new(ViewportDimensions.Empty);
    private readonly ViewerCameraNavigationUiAdapter _cameraNavigation;
    private readonly ViewerCameraShortcutRepeatGuard _cameraShortcutRepeat = new();
    private readonly ViewerStormNavigationInputTracker _stormNavigationInput = new();
    private readonly ViewerStormPickInputTracker _stormPickInput = new();
    private readonly DispatcherTimer _stormNavigationTimer;
    private ViewerRenderCoordinator? _coordinator;
    private AvaloniaViewerRenderBackendHost? _backendHost;
    private CancellationTokenSource? _documentLifetime;
    private CancellationTokenSource? _pickLifetime;
    private CancellationTokenSource? _selectionLifetime;
    private CancellationTokenSource? _playbackLifetime;
    private readonly StormViewportControl? _soakStormViewport;
    private Task? _renderLoop;
    private Task? _diagnosticSequence;
    private Task? _pickTask;
    private Task? _selectionTask;
    private Task? _playbackTask;
    private ViewerTimeUpdatePump? _timeUpdates;
    private ViewerCameraUpdatePump? _cameraUpdates;
    private ViewerStageCameraRefreshPump? _stageCameraRefreshes;
    private ViewerHierarchySnapshot _hierarchy = ViewerHierarchySnapshot.Empty;
    private ViewerStageTimingSnapshot _timing = ViewerStageTimingSnapshot.Empty;
    private ViewerLayerStackSnapshot _layers = ViewerLayerStackSnapshot.Empty;
    private ViewerStageStatisticsSnapshot _statistics = ViewerStageStatisticsSnapshot.Empty;
    private ViewerValidationSnapshot _validation = ViewerValidationSnapshot.Empty;
    private ViewerPrimInspectorSnapshot? _currentInspector;
    private ViewerDiagnosticsSnapshot _latestDiagnostics = ViewerDiagnosticsSnapshot.Empty;
    private ViewerSettings _settings = ViewerSettings.Default;
    private readonly ViewerSelectionState _selectionState = new();
    private readonly object _timelineGate = new();
    private string? _stagePath;
    private double _currentTimeCode;
    private bool _updatingTimelineUi;
    private bool _documentBusy;
    private bool _rebuildingHierarchy;
    private bool _rebuildingLayers;
    private bool _rebuildingInspector;
    private bool _validationBusy;
    private bool _layerCommandBusy;
    private bool _primCommandBusy;
    private bool _rootLayerEditsExplicitlyEnabled;
    private bool _applyingLayout;
    private int _hierarchyExpandDepth;
    private bool _applyingViewportDisplay;
    private double _stagePanelWidth = ViewerSettings.Default.StagePanelWidth;
    private double _inspectorPanelWidth = ViewerSettings.Default.InspectorPanelWidth;
    private int _timelineUiUpdatePosted;
    private readonly bool _selectorReady;
    private readonly ViewerSwitchingEvidenceSession? _switchingEvidence;
    private int _disposed;
    private bool _shutdownComplete;
    private bool _shutdownStarted;
    private int _diagnosticOwnsRendering;
    private ViewerCameraPointerGesture _cameraPointerGesture;
    private IPointer? _cameraPointer;
    private Point _lastCameraPointerPosition;
    private IPointer? _pickPointer;
    private Point _pickPointerOrigin;
    private bool _pickPointerDragged;
    private long _pickGeneration;
    private StormNativeControlHost? _stormNavigationHost;
    private ulong _cameraRoutedInputGeneration;
    private ViewerCameraDisplayMode? _displayedCameraMode;
    private string? _displayedCameraPath;
    private bool _stageCameraSelectionBusy;
    private int _cameraStatusUpdatePosted;

    private ColumnDefinition StagePanelGridColumn => MainContentGrid.ColumnDefinitions[0];

    private ColumnDefinition StageSplitterGridColumn => MainContentGrid.ColumnDefinitions[1];

    private ColumnDefinition InspectorSplitterGridColumn => MainContentGrid.ColumnDefinitions[3];

    private ColumnDefinition InspectorPanelGridColumn => MainContentGrid.ColumnDefinitions[4];

    public MainWindow()
        : this(new RecentStageStore(), new ViewerSettingsStore())
    {
    }

    internal MainWindow(RecentStageStore recentStageStore)
        : this(recentStageStore, new ViewerSettingsStore())
    {
    }

    internal MainWindow(
        RecentStageStore recentStageStore,
        ViewerSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(recentStageStore);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _recentStageStore = recentStageStore;
        _settingsStore = settingsStore;
        _cameraNavigation = new ViewerCameraNavigationUiAdapter(
            _cameraNavigationController,
            _stageCameraMode,
            AvaloniaViewerUiThreadVerifier.Instance);
        _stormNavigationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _stormNavigationTimer.Tick += OnStormNavigationTick;
        InitializeComponent();
        if (ViewerStartupOptions.HostTitle is { Length: > 0 } hostTitle)
        {
            Title = hostTitle;
        }
        if (ViewerStartupOptions.HostShutdownToken.CanBeCanceled)
        {
            _hostShutdown = ViewerStartupOptions.HostShutdownToken.Register(
                static state => Dispatcher.UIThread.Post(
                    () => ((MainWindow)state!).Close()),
                this);
        }
        UpdateCameraStatus();
        if (ViewerStartupOptions.SwitchingEvidencePath is { } evidencePath)
        {
            _switchingEvidence = new ViewerSwitchingEvidenceSession(evidencePath);
        }
        if (ViewerStartupOptions.SharedStageSoak)
        {
            _soakStormViewport = new StormViewportControl
            {
                PluginPath = ViewerStartupOptions.PluginPath
            };
            _soakStormViewport.StatusChanged += OnRendererStatusChanged;
            ViewportHost.Children.Add(_soakStormViewport);
            RendererSelector.SelectedIndex = 1;
            RendererSelector.IsEnabled = false;
            Opened += OnSharedStageSoakOpened;
        }
        else
        {
            RendererSelector.SelectedIndex = GetRendererSelectionIndex();
            RendererSelector.SelectionChanged += OnRendererSelectionChanged;
            ViewportHost.SizeChanged += OnViewportSizeChanged;
            OpenStageButton.Click += OnOpenStageClick;
            OpenStageMenuItem.Click += OnOpenStageClick;
            ReloadStageButton.Click += OnReloadStageClick;
            ReloadStageMenuItem.Click += OnReloadStageClick;
            HierarchyFilter.TextChanged += OnHierarchyFilterChanged;
            HierarchyTypeFilter.TextChanged += OnHierarchyFilterChanged;
            HierarchyExpandDepthInput.TextChanged += OnHierarchyExpandDepthChanged;
            StageHierarchy.SelectionChanged += OnHierarchySelectionChanged;
            PlayPauseButton.Click += OnPlayPauseClick;
            CurrentTimeInput.KeyDown += OnCurrentTimeInputKeyDown;
            CurrentTimeInput.LostFocus += OnCurrentTimeInputLostFocus;
            TimelineSlider.ValueChanged += OnTimelineSliderValueChanged;
            SetSessionEditTargetButton.Click += OnSetSessionEditTargetClick;
            SetRootEditTargetButton.Click += OnSetRootEditTargetClick;
            StagePanelMenuItem.Click += OnPanelVisibilityChanged;
            InspectorPanelMenuItem.Click += OnPanelVisibilityChanged;
            TimelineMenuItem.Click += OnPanelVisibilityChanged;
            DiagnosticsMenuItem.Click += OnPanelVisibilityChanged;
            StagePanelCheckBox.Click += OnPanelVisibilityChanged;
            InspectorPanelCheckBox.Click += OnPanelVisibilityChanged;
            TimelineCheckBox.Click += OnPanelVisibilityChanged;
            DiagnosticsCheckBox.Click += OnPanelVisibilityChanged;
            CopyDiagnosticsButton.Click += OnCopyDiagnosticsClick;
            ExportDiagnosticsButton.Click += OnExportDiagnosticsClick;
            IncludeDiagnosticPathsCheckBox.Click += OnDiagnosticPathSettingChanged;
            RefreshValidationButton.Click += OnRefreshValidationClick;
            ViewportDrawModeSelector.SelectionChanged += OnViewportDrawModeChanged;
            PurposeDefaultCheckBox.Click += OnViewportPurposeChanged;
            PurposeProxyCheckBox.Click += OnViewportPurposeChanged;
            PurposeRenderCheckBox.Click += OnViewportPurposeChanged;
            PurposeGuideCheckBox.Click += OnViewportPurposeChanged;
            SceneLightingCheckBox.Click += OnViewportLightingChanged;
            SceneShadowsCheckBox.Click += OnViewportShadowsChanged;
            BackfaceCullingCheckBox.Click += OnViewportBackfaceCullingChanged;
            SceneMaterialsCheckBox.Click += OnViewportSceneMaterialsChanged;
            BackgroundColorSelector.SelectionChanged += OnViewportBackgroundChanged;
            ResetCameraAutomaticButton.Click += OnResetCameraAutomaticClick;
            ResetCameraAutomaticMenuItem.Click += OnResetCameraAutomaticClick;
            ResetCameraLegacyButton.Click += OnResetCameraLegacyClick;
            ResetCameraLegacyMenuItem.Click += OnResetCameraLegacyClick;
            ToggleCameraProjectionButton.Click += OnToggleCameraProjectionClick;
            ToggleCameraProjectionMenuItem.Click += OnToggleCameraProjectionClick;
            UseSelectedCameraButton.Click += OnUseSelectedCameraClick;
            UseSelectedCameraMenuItem.Click += OnUseSelectedCameraClick;
            FrameSelectedButton.Click += OnFrameSelectedClick;
            FrameSelectedMenuItem.Click += OnFrameSelectedClick;
            ShortcutsMenuItem.Click += OnShortcutsClick;
            KeyDown += OnWindowKeyDown;
            KeyUp += OnWindowKeyUp;
            Deactivated += OnWindowDeactivated;
            RegisterCameraInputHandlers(this);
            RegisterCameraInputHandlers(ViewportHost);
            RegisterPickingInputHandlers(this);
            RegisterPickingInputHandlers(ViewportHost);
            ViewportHost.PointerCaptureLost += OnCameraPointerCaptureLost;
            DragDrop.SetAllowDrop(this, true);
            DragDrop.AddDragOverHandler(this, OnDragOver);
            DragDrop.AddDropHandler(this, OnDrop);
            Opened += OnViewerOpened;
        }
        Closing += OnClosing;
        Closed += OnClosed;
        _selectorReady = true;
    }

    private void OnRendererStatusChanged(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() => RendererStatus.Text = status);
    }

    private void OnCompositionStatusChanged(string status) =>
        Dispatcher.UIThread.Post(() => CompositionStatus.Text = status);

    private async void OnViewerOpened(object? sender, EventArgs e)
    {
        Opened -= OnViewerOpened;
        try
        {
            if (!IsAutomatedViewerRun())
            {
                await LoadSettingsAsync(_viewerLifetime.Token);
                await LoadRecentStagesAsync(_viewerLifetime.Token);
            }
            if (string.IsNullOrWhiteSpace(ViewerStartupOptions.StagePath))
            {
                RendererStatus.Text = string.IsNullOrWhiteSpace(ViewerStartupOptions.PluginPath)
                    ? "Renderer: unavailable until a plugin path is configured"
                    : "Renderer: no stage loaded";
                return;
            }

            await OpenStageCoreAsync(
                ViewerStartupOptions.StagePath,
                addToRecent: !IsAutomatedViewerRun(),
                _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            string status =
                $"Renderer initialization failed: {ViewerPackageErrorFormatter.Format(exception)}";
            RendererStatus.Text = status;
            ShowError(status);
        }
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            ViewerSettingsLoadResult result =
                await _settingsStore.LoadAsync(cancellationToken);
            ApplySettings(result.Settings);
            SettingsState.Text = result.Status switch
            {
                ViewerSettingsLoadStatus.Missing =>
                    "Using default settings. Settings are saved atomically when the Viewer closes.",
                ViewerSettingsLoadStatus.Loaded =>
                    "Settings loaded. Changes are saved atomically when the Viewer closes.",
                ViewerSettingsLoadStatus.Migrated =>
                    result.Diagnostic ?? "Legacy settings were migrated.",
                ViewerSettingsLoadStatus.Malformed =>
                    $"Malformed settings were ignored: {result.Diagnostic}",
                _ => throw new InvalidOperationException("Unknown settings load status.")
            };
        }
        catch (IOException exception)
        {
            SettingsState.Text = $"Settings could not be read: {exception.Message}";
            ShowError(SettingsState.Text);
        }
        catch (UnauthorizedAccessException exception)
        {
            SettingsState.Text = $"Settings could not be read: {exception.Message}";
            ShowError(SettingsState.Text);
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            _settings = CaptureSettings();
            await _settingsStore.SaveAsync(_settings, CancellationToken.None);
            SettingsState.Text = "Settings saved.";
        }
        catch (IOException exception)
        {
            string status = $"Settings could not be saved: {exception.Message}";
            SettingsState.Text = status;
            ViewerStatus.Text = status;
            ViewerStartupOptions.WriteStatus(status);
        }
        catch (UnauthorizedAccessException exception)
        {
            string status = $"Settings could not be saved: {exception.Message}";
            SettingsState.Text = status;
            ViewerStatus.Text = status;
            ViewerStartupOptions.WriteStatus(status);
        }
    }

    private void ApplySettings(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        _stagePanelWidth = settings.StagePanelWidth;
        _inspectorPanelWidth = settings.InspectorPanelWidth;
        if (string.Equals(ViewerStartupOptions.Renderer, "Auto", StringComparison.Ordinal))
        {
            RendererSelector.SelectedIndex =
                GetRendererSelectionIndex(settings.RendererPreference);
        }

        _applyingLayout = true;
        try
        {
            StagePanelMenuItem.IsChecked = settings.StagePanelVisible;
            StagePanelCheckBox.IsChecked = settings.StagePanelVisible;
            InspectorPanelMenuItem.IsChecked = settings.InspectorPanelVisible;
            InspectorPanelCheckBox.IsChecked = settings.InspectorPanelVisible;
            TimelineMenuItem.IsChecked = settings.TimelineVisible;
            TimelineCheckBox.IsChecked = settings.TimelineVisible;
            DiagnosticsMenuItem.IsChecked = settings.DiagnosticsVisible;
            DiagnosticsCheckBox.IsChecked = settings.DiagnosticsVisible;
            SnapTimelineCheckBox.IsChecked = settings.SnapTimelineToFrames;
            ApplyPanelVisibility(
                settings.StagePanelVisible,
                settings.InspectorPanelVisible,
                settings.TimelineVisible,
                settings.DiagnosticsVisible);
            InspectorTabs.SelectedIndex =
                !settings.DiagnosticsVisible && settings.SelectedInspectorTab == 2
                    ? 3
                    : settings.SelectedInspectorTab;
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private ViewerSettings CaptureSettings()
    {
        if (StagePanel.IsVisible && StagePanelGridColumn.Width.Value > 0)
        {
            _stagePanelWidth = StagePanelGridColumn.Width.Value;
        }
        if (InspectorPanel.IsVisible && InspectorPanelGridColumn.Width.Value > 0)
        {
            _inspectorPanelWidth = InspectorPanelGridColumn.Width.Value;
        }
        return new ViewerSettings
        {
            WindowWidth = ClampDimension(
                Bounds.Width,
                ViewerSettings.MinimumWindowWidth,
                ViewerSettings.MaximumWindowWidth,
                _settings.WindowWidth),
            WindowHeight = ClampDimension(
                Bounds.Height,
                ViewerSettings.MinimumWindowHeight,
                ViewerSettings.MaximumWindowHeight,
                _settings.WindowHeight),
            StagePanelWidth = ClampDimension(
                _stagePanelWidth,
                ViewerSettings.MinimumPanelWidth,
                ViewerSettings.MaximumPanelWidth,
                ViewerSettings.Default.StagePanelWidth),
            InspectorPanelWidth = ClampDimension(
                _inspectorPanelWidth,
                ViewerSettings.MinimumPanelWidth,
                ViewerSettings.MaximumPanelWidth,
                ViewerSettings.Default.InspectorPanelWidth),
            RendererPreference = GetSelectedRendererPreference(),
            SelectedInspectorTab = Math.Clamp(InspectorTabs.SelectedIndex, 0, 3),
            StagePanelVisible = StagePanel.IsVisible,
            InspectorPanelVisible = InspectorPanel.IsVisible,
            TimelineVisible = TimelinePanel.IsVisible,
            DiagnosticsVisible = DiagnosticsTab.IsVisible,
            SnapTimelineToFrames = SnapTimelineCheckBox.IsChecked == true
        };
    }

    private static double ClampDimension(
        double value,
        double minimum,
        double maximum,
        double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private void OnPanelVisibilityChanged(object? sender, RoutedEventArgs e)
    {
        if (_applyingLayout)
        {
            return;
        }

        bool stageVisible = sender switch
        {
            MenuItem when ReferenceEquals(sender, StagePanelMenuItem) =>
                StagePanelMenuItem.IsChecked,
            CheckBox when ReferenceEquals(sender, StagePanelCheckBox) =>
                StagePanelCheckBox.IsChecked == true,
            _ => StagePanel.IsVisible
        };
        bool inspectorVisible = sender switch
        {
            MenuItem when ReferenceEquals(sender, InspectorPanelMenuItem) =>
                InspectorPanelMenuItem.IsChecked,
            CheckBox when ReferenceEquals(sender, InspectorPanelCheckBox) =>
                InspectorPanelCheckBox.IsChecked == true,
            _ => InspectorPanel.IsVisible
        };
        bool timelineVisible = sender switch
        {
            MenuItem when ReferenceEquals(sender, TimelineMenuItem) =>
                TimelineMenuItem.IsChecked,
            CheckBox when ReferenceEquals(sender, TimelineCheckBox) =>
                TimelineCheckBox.IsChecked == true,
            _ => TimelinePanel.IsVisible
        };
        bool diagnosticsVisible = sender switch
        {
            MenuItem when ReferenceEquals(sender, DiagnosticsMenuItem) =>
                DiagnosticsMenuItem.IsChecked,
            CheckBox when ReferenceEquals(sender, DiagnosticsCheckBox) =>
                DiagnosticsCheckBox.IsChecked == true,
            _ => DiagnosticsTab.IsVisible
        };

        _applyingLayout = true;
        try
        {
            StagePanelMenuItem.IsChecked = stageVisible;
            StagePanelCheckBox.IsChecked = stageVisible;
            InspectorPanelMenuItem.IsChecked = inspectorVisible;
            InspectorPanelCheckBox.IsChecked = inspectorVisible;
            TimelineMenuItem.IsChecked = timelineVisible;
            TimelineCheckBox.IsChecked = timelineVisible;
            DiagnosticsMenuItem.IsChecked = diagnosticsVisible;
            DiagnosticsCheckBox.IsChecked = diagnosticsVisible;
            ApplyPanelVisibility(
                stageVisible,
                inspectorVisible,
                timelineVisible,
                diagnosticsVisible);
        }
        finally
        {
            _applyingLayout = false;
        }
        if (diagnosticsVisible && _coordinator is { } coordinator)
        {
            CaptureDiagnostics(coordinator, frameResult: null, force: true);
        }
    }

    private void ApplyPanelVisibility(
        bool stageVisible,
        bool inspectorVisible,
        bool timelineVisible,
        bool diagnosticsVisible)
    {
        if (StagePanel.IsVisible && StagePanelGridColumn.Width.Value > 0)
        {
            _stagePanelWidth = StagePanelGridColumn.Width.Value;
        }
        if (InspectorPanel.IsVisible && InspectorPanelGridColumn.Width.Value > 0)
        {
            _inspectorPanelWidth = InspectorPanelGridColumn.Width.Value;
        }

        StagePanel.IsVisible = stageVisible;
        StagePanelSplitter.IsVisible = stageVisible;
        StagePanelGridColumn.Width = new GridLength(stageVisible ? _stagePanelWidth : 0);
        StageSplitterGridColumn.Width = new GridLength(stageVisible ? 5 : 0);
        InspectorPanel.IsVisible = inspectorVisible;
        InspectorPanelSplitter.IsVisible = inspectorVisible;
        InspectorPanelGridColumn.Width =
            new GridLength(inspectorVisible ? _inspectorPanelWidth : 0);
        InspectorSplitterGridColumn.Width = new GridLength(inspectorVisible ? 5 : 0);
        TimelinePanel.IsVisible = timelineVisible;
        DiagnosticsTab.IsVisible = diagnosticsVisible;
        if (!diagnosticsVisible && ReferenceEquals(InspectorTabs.SelectedItem, DiagnosticsTab))
        {
            InspectorTabs.SelectedItem = SettingsTab;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool firstCameraShortcutPress = _cameraShortcutRepeat.TryPress(e.Key);
        bool control = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool editing = IsCameraShortcutEditing();
        if (control && e.Key == Key.O)
        {
            e.Handled = true;
            OnOpenStageClick(OpenStageButton, new RoutedEventArgs());
            return;
        }
        if (control && e.Key == Key.R && ReloadStageButton.IsEnabled)
        {
            e.Handled = true;
            OnReloadStageClick(ReloadStageButton, new RoutedEventArgs());
            return;
        }
        if (control && e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4)
        {
            int index = e.Key switch
            {
                Key.D1 => 0,
                Key.D2 => 1,
                Key.D3 => 2,
                Key.D4 => 3,
                _ => 0
            };
            bool diagnosticsVisible = index == 2 || DiagnosticsTab.IsVisible;
            ApplyPanelVisibility(
                stageVisible: StagePanel.IsVisible,
                inspectorVisible: true,
                timelineVisible: TimelinePanel.IsVisible,
                diagnosticsVisible: diagnosticsVisible);
            InspectorPanelMenuItem.IsChecked = true;
            InspectorPanelCheckBox.IsChecked = true;
            if (index == 2)
            {
                DiagnosticsMenuItem.IsChecked = true;
                DiagnosticsCheckBox.IsChecked = true;
                if (_coordinator is { } coordinator)
                {
                    CaptureDiagnostics(coordinator, frameResult: null, force: true);
                }
            }
            InspectorTabs.SelectedIndex = index;
            e.Handled = true;
            return;
        }
        ViewerCameraShortcut cameraShortcut = ViewerCameraShortcutPolicy.Classify(
            e.Key,
            e.KeyModifiers,
            editing);
        if (cameraShortcut != ViewerCameraShortcut.None &&
            !firstCameraShortcutPress)
        {
            e.Handled = true;
            return;
        }
        if (cameraShortcut == ViewerCameraShortcut.ResetAutomatic &&
            ResetCameraAutomaticButton.IsEnabled)
        {
            MarkAvaloniaCameraInput();
            ResetCameraToAutomatic();
            e.Handled = true;
            return;
        }
        if (cameraShortcut == ViewerCameraShortcut.ToggleProjection &&
            ToggleCameraProjectionButton.IsEnabled)
        {
            MarkAvaloniaCameraInput();
            ToggleCameraProjection();
            e.Handled = true;
            return;
        }
        if (cameraShortcut == ViewerCameraShortcut.FrameSelected &&
            FrameSelectedButton.IsEnabled)
        {
            MarkAvaloniaCameraInput();
            _ = FrameSelectedAsync();
            e.Handled = true;
            return;
        }
        if (!control &&
            e.Key == Key.Space &&
            !editing &&
            PlayPauseButton.IsEnabled)
        {
            e.Handled = true;
            OnPlayPauseClick(PlayPauseButton, new RoutedEventArgs());
        }
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        _ = sender;
        _cameraShortcutRepeat.Release(e.Key);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _cameraShortcutRepeat.Reset();
    }

    private void RegisterCameraInputHandlers(Interactive target)
    {
        target.AddHandler(
            PointerPressedEvent,
            OnCameraPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        target.AddHandler(
            PointerMovedEvent,
            OnCameraPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        target.AddHandler(
            PointerReleasedEvent,
            OnCameraPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        target.AddHandler(
            PointerWheelChangedEvent,
            OnCameraPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void RegisterPickingInputHandlers(Interactive target)
    {
        target.AddHandler(
            PointerPressedEvent,
            OnPickPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        target.AddHandler(
            PointerMovedEvent,
            OnPickPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        target.AddHandler(
            PointerReleasedEvent,
            OnPickPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void OnResetCameraAutomaticClick(object? sender, RoutedEventArgs e) =>
        ResetCameraToAutomatic();

    private void OnResetCameraLegacyClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateCamera())
        {
            return;
        }
        PublishCameraMutation(
            _cameraNavigation.ResetToExplicitPose(),
            "Camera set to the explicit legacy pose.");
    }

    private void OnToggleCameraProjectionClick(object? sender, RoutedEventArgs e) =>
        ToggleCameraProjection();

    private async void OnUseSelectedCameraClick(object? sender, RoutedEventArgs e)
    {
        if (!CanUseSelectedCamera())
        {
            ShowCameraMessage(GetUseSelectedCameraUnavailableMessage());
            return;
        }

        CancellationToken cancellationToken =
            _documentLifetime?.Token ?? _viewerLifetime.Token;
        try
        {
            await _documentGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            ViewerPrimInspectorSnapshot? inspector = _currentInspector;
            string? selectedPrimPath = _selectionState.PrimPath;
            if (coordinator is null ||
                inspector is not { IsCamera: true } ||
                selectedPrimPath is null ||
                !string.Equals(
                    inspector.Path,
                    selectedPrimPath,
                    StringComparison.Ordinal) ||
                IsAutomatedViewerRun())
            {
                return;
            }

            _stageCameraSelectionBusy = true;
            UpdateCameraAvailability();
            double selectedCameraTimeCode;
            lock (_timelineGate)
            {
                selectedCameraTimeCode = _currentTimeCode;
            }
            var selectedCameraTime = new StageTime(selectedCameraTimeCode);
            ViewerStageCameraActivation activation =
                _cameraNavigation.CaptureStageCameraActivation(
                    selectedPrimPath,
                    selectedCameraTimeCode);
            var source = new ViewerSchedulerStageCameraSource(coordinator.Scheduler);
            ViewerStageCameraQueryResult result =
                await ViewerStageCameraQuery.QueryAsync(
                    source,
                    selectedPrimPath,
                    selectedCameraTime,
                    cancellationToken);
            if (!string.Equals(
                    _selectionState.PrimPath,
                    selectedPrimPath,
                    StringComparison.Ordinal) ||
                _currentInspector is not { IsCamera: true } currentInspector ||
                !string.Equals(
                    currentInspector.Path,
                    selectedPrimPath,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (result.Outcome == ViewerStageCameraQueryOutcome.Ready)
            {
                try
                {
                    if (!_cameraNavigation.TryActivateStageCamera(
                            activation,
                            result.Snapshot,
                            out CameraState camera))
                    {
                        ShowCameraMessage(
                            "The selected camera was not activated because the camera " +
                            "mode changed while it was loading.");
                        return;
                    }

                    await coordinator.MutateStateAsync(
                        state => state
                            .WithTime(selectedCameraTime)
                            .WithCamera(camera),
                        cancellationToken);
                    UpdateCameraStatus();
                    double latestTimeCode;
                    lock (_timelineGate)
                    {
                        latestTimeCode = _currentTimeCode;
                    }
                    if (latestTimeCode != selectedCameraTimeCode)
                    {
                        _ = TryQueueStageCameraRefresh(
                            latestTimeCode,
                            applyTime: true);
                    }
                    ShowCameraMessage(
                        $"Using selected stage camera '{result.Snapshot.PrimPath}'.");
                    return;
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _cameraNavigation.ResetToAutomatic();
                    await coordinator.MutateStateAsync(
                        state => state.WithCamera(CameraState.Default),
                        cancellationToken);
                    UpdateCameraStatus();
                    ShowCameraMessage(
                        $"Camera '{selectedPrimPath}' cannot be used for rendering: " +
                        $"{exception.Message} Camera reset to Automatic.");
                    return;
                }
            }

            if (_cameraNavigation.TryFallbackStageCameraActivation(
                    activation,
                    out long fallbackGeneration))
            {
                await coordinator.MutateStateAsync(
                    state => _stageCameraMode.IsAutomaticFallback(fallbackGeneration)
                        ? state.WithCamera(CameraState.Default)
                        : state,
                    cancellationToken);
                UpdateCameraStatus();
            }
            ShowCameraMessage(
                $"{result.Error ?? "The selected stage camera is unavailable."} " +
                "Camera reset to Automatic.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _cameraNavigation.ResetToAutomatic();
            UpdateCameraStatus();
            _ = TryPostCameraSnapshot();
            ShowError($"Could not use the selected stage camera: {exception.Message}");
        }
        finally
        {
            _stageCameraSelectionBusy = false;
            UpdateCameraAvailability();
            _documentGate.Release();
        }
    }

    private void ResetCameraToAutomatic()
    {
        if (!CanNavigateCamera())
        {
            return;
        }
        PublishCameraMutation(
            _cameraNavigation.ResetToAutomatic(),
            "Camera reset to Automatic.");
    }

    private void ToggleCameraProjection()
    {
        if (!CanNavigateCamera())
        {
            return;
        }
        PublishCameraMutation(
            _cameraNavigation.ToggleProjection(),
            "Camera projection toggled.");
    }

    private bool CanUseSelectedCamera() =>
        CanNavigateCamera() &&
        _stageCameraRefreshes is { IsAccepting: true } &&
        !_stageCameraSelectionBusy &&
        _currentInspector is { IsCamera: true } inspector &&
        string.Equals(
            inspector.Path,
            _selectionState.PrimPath,
            StringComparison.Ordinal);

    private string GetUseSelectedCameraUnavailableMessage()
    {
        if (IsAutomatedViewerRun())
        {
            return "Selected stage cameras are disabled during automated Viewer runs.";
        }
        if (_coordinator is null)
        {
            return "Open a stage before using a selected camera.";
        }
        if (_selectionState.PrimPath is null || _currentInspector is null)
        {
            return "Select a UsdGeomCamera prim before using the selected camera.";
        }
        if (!_currentInspector.IsCamera)
        {
            return $"The selected prim '{_currentInspector.Path}' is not a UsdGeomCamera.";
        }
        return "The selected stage camera is temporarily unavailable.";
    }

    private void PublishCameraMutation(bool changed, string status)
    {
        UpdateCameraStatus();
        if (changed && !TryPostCameraSnapshot())
        {
            ShowError("The camera update worker is unavailable.");
            return;
        }
        ViewerStatus.Text = status;
        ViewerStatus.Foreground = null;
        ViewerStartupOptions.WriteStatus(status);
    }

    private void OnCameraPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled ||
            !CanNavigateCamera() ||
            !IsPointerInsideViewport(e))
        {
            return;
        }

        _cameraShortcutRepeat.ResetForFocusTransfer();
        _ = ViewportHost.Focus();
        PointerPointProperties properties = e.GetCurrentPoint(ViewportHost).Properties;
        ViewerCameraPointerGesture gesture = ViewerCameraGestureClassifier.Classify(
            e.KeyModifiers,
            GetPressedButtons(properties));
        if (gesture == ViewerCameraPointerGesture.None)
        {
            return;
        }

        _cameraPointerGesture = gesture;
        _cameraPointer = e.Pointer;
        _lastCameraPointerPosition = e.GetPosition(ViewportHost);
        MarkAvaloniaCameraInput();
        e.Pointer.Capture(ViewportHost);
        e.Handled = true;
    }

    private void OnCameraPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled ||
            _cameraPointerGesture == ViewerCameraPointerGesture.None ||
            !ReferenceEquals(e.Pointer, _cameraPointer))
        {
            return;
        }

        Point current = e.GetPosition(ViewportHost);
        var logicalDelta = new Vector2(
            (float)(current.X - _lastCameraPointerPosition.X),
            (float)(current.Y - _lastCameraPointerPosition.Y));
        _lastCameraPointerPosition = current;
        Vector2 physicalDelta = ViewerCameraPointerDeltas.ToPhysicalPixels(
            logicalDelta,
            GetViewportRenderScaling());
        MarkAvaloniaCameraInput();
        if (_cameraNavigation.ApplyGesture(_cameraPointerGesture, physicalDelta))
        {
            UpdateCameraStatus();
            if (!TryPostCameraSnapshot())
            {
                EndCameraPointerGesture();
                ShowError("The camera update worker is unavailable.");
                return;
            }
        }
        e.Handled = true;
    }

    private void OnCameraPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled ||
            _cameraPointerGesture == ViewerCameraPointerGesture.None ||
            !ReferenceEquals(e.Pointer, _cameraPointer))
        {
            return;
        }

        MarkAvaloniaCameraInput();
        EndCameraPointerGesture();
        e.Handled = true;
    }

    private void OnCameraPointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _cameraPointer))
        {
            _cameraPointerGesture = ViewerCameraPointerGesture.None;
            _cameraPointer = null;
        }
    }

    private void OnPickPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_documentBusy ||
            _coordinator is null ||
            (IsAutomatedViewerRun() && !ViewerStartupOptions.PickSmokeEnabled) ||
            !IsPointerInsideViewport(e))
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(ViewportHost).Properties;
        if (!ViewerPickGestureClassifier.CanStart(
                e.KeyModifiers,
                GetPressedButtons(properties)))
        {
            return;
        }

        _pickPointer = e.Pointer;
        _pickPointerOrigin = e.GetPosition(ViewportHost);
        _pickPointerDragged = false;
    }

    private void OnPickPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pickPointer) || _pickPointerDragged)
        {
            return;
        }

        Point current = e.GetPosition(ViewportHost);
        PointerPointProperties properties = e.GetCurrentPoint(ViewportHost).Properties;
        _pickPointerDragged =
            !ViewerPickGestureClassifier.CanStart(
                e.KeyModifiers,
                GetPressedButtons(properties)) ||
            ViewerPickGestureClassifier.IsDrag(
                current.X - _pickPointerOrigin.X,
                current.Y - _pickPointerOrigin.Y,
                GetViewportRenderScaling());
    }

    private void OnPickPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pickPointer))
        {
            return;
        }

        bool dragged = _pickPointerDragged;
        _pickPointer = null;
        _pickPointerDragged = false;
        ViewerRenderCoordinator? coordinator = _coordinator;
        if (dragged ||
            e.KeyModifiers != KeyModifiers.None ||
            coordinator is null)
        {
            return;
        }

        Point logical = e.GetPosition(ViewportHost);
        var contentBounds = new ViewerLogicalContentBounds(
            0,
            0,
            ViewportHost.Bounds.Width,
            ViewportHost.Bounds.Height);
        if (!ViewerPickPixelMapper.TryMap(
                logical.X,
                logical.Y,
                contentBounds,
                GetViewportRenderScaling(),
                coordinator.CurrentState.Viewport,
                out ViewerPhysicalPixel pixel))
        {
            return;
        }

        StartViewportPick(coordinator, pixel);
    }

    private void StartViewportPick(
        ViewerRenderCoordinator coordinator,
        ViewerPhysicalPixel pixel)
    {
        CancellationToken documentToken =
            _documentLifetime?.Token ?? _viewerLifetime.Token;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(documentToken);
        CancellationTokenSource? superseded =
            Interlocked.Exchange(ref _pickLifetime, lifetime);
        superseded?.Cancel();
        long generation = Interlocked.Increment(ref _pickGeneration);
        ShowPickStatus($"Picking pixel {pixel.X}, {pixel.Y}...");
        _pickTask = RunViewportPickAsync(
            coordinator,
            pixel,
            generation,
            lifetime);
    }

    private async Task RunViewportPickAsync(
        ViewerRenderCoordinator coordinator,
        ViewerPhysicalPixel pixel,
        long generation,
        CancellationTokenSource lifetime)
    {
        CancellationToken cancellationToken = lifetime.Token;
        bool entered = false;
        try
        {
            RenderPickResult result = await coordinator
                .PickAsync(pixel, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await _documentGate.WaitAsync(cancellationToken);
            entered = true;
            if (!ReferenceEquals(_pickLifetime, lifetime) ||
                !ReferenceEquals(_coordinator, coordinator) ||
                generation != Interlocked.Read(ref _pickGeneration))
            {
                return;
            }

            switch (result.Status)
            {
                case RenderPickStatus.Hit when result.Item is { } item:
                    await ApplyPickedHitAsync(
                        coordinator,
                        item,
                        generation,
                        cancellationToken);
                    break;
                case RenderPickStatus.Miss:
                    await ClearSelectionAsync();
                    if (generation == Interlocked.Read(ref _pickGeneration))
                    {
                        RenderHierarchy();
                        ShowPickStatus("Pick missed; selection cleared.");
                    }
                    break;
                case RenderPickStatus.Stale:
                    ShowPickStatus(
                        $"Pick stayed stale after one retry: {result.StaleReasons}. " +
                        "The current selection was kept.");
                    break;
                case RenderPickStatus.Unsupported:
                    _diagnostics.AddUnsupported(
                    [
                        ViewerUnsupportedFeatureCatalog.RendererPickingUnavailable
                    ]);
                    RenderDiagnostics();
                    ShowPickStatus(
                        "Picking is unsupported for this request; the current selection was kept.");
                    break;
                default:
                    throw new InvalidDataException(
                        "The renderer returned a hit without selection identity.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_pickLifetime, lifetime))
            {
                ShowPickStatus(
                    $"Pick failed; the current selection was kept: {exception.Message}");
            }
        }
        finally
        {
            if (entered)
            {
                _documentGate.Release();
            }
            Interlocked.CompareExchange(ref _pickLifetime, null, lifetime);
            lifetime.Dispose();
        }
    }

    private async Task ApplyPickedHitAsync(
        ViewerRenderCoordinator coordinator,
        SelectionItem item,
        long generation,
        CancellationToken cancellationToken)
    {
        if (!_hierarchy.Contains(item.PrimPath))
        {
            ShowPickStatus(
                $"Picked path '{item.PrimPath}' is no longer in the hierarchy; " +
                "the current selection was kept.");
            return;
        }

        SelectionItem? previousSelection = _selectionState.Item;
        try
        {
            if (_selectionState.TrySetItem(item, out SelectionState selection))
            {
                await coordinator.MutateStateAsync(
                    state => state.WithSelection(selection),
                    cancellationToken);
            }
            if (generation != Interlocked.Read(ref _pickGeneration))
            {
                return;
            }

            RenderHierarchy();
            _currentInspector = null;
            UpdateCameraAvailability();
            await StartInspectorLoadAsync(item.PrimPath, cancellationToken);
            ShowPickStatus($"Selected {FormatSelectionItem(item)}.");
        }
        catch
        {
            _selectionState.Restore(previousSelection);
            throw;
        }
    }

    private void ShowPickStatus(string status)
    {
        string bounded = ViewerScalarFormatter.Bound(status, 512);
        ViewerStatus.Text = bounded;
        ViewerStatus.Foreground = null;
        ViewerStartupOptions.WriteStatus(bounded);
    }

    private static string FormatSelectionItem(in SelectionItem item)
    {
        string instance = item.InstanceIndex is { } instanceIndex
            ? $"; instance={instanceIndex}; instancer={item.InstancerPath}"
            : string.Empty;
        string element = item.ElementIndex is { } elementIndex
            ? $"; subprim={elementIndex}"
            : string.Empty;
        return $"{item.PrimPath}{instance}{element}";
    }

    private void OnCameraPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled ||
            !CanNavigateCamera() ||
            !ViewportHost.IsKeyboardFocusWithin ||
            !IsPointerInsideViewport(e) ||
            e.Delta.Y == 0d)
        {
            return;
        }

        MarkAvaloniaCameraInput();
        if (_cameraNavigation.ZoomWheel((float)e.Delta.Y))
        {
            UpdateCameraStatus();
            if (!TryPostCameraSnapshot())
            {
                ShowError("The camera update worker is unavailable.");
                return;
            }
        }
        e.Handled = true;
    }

    private void EndCameraPointerGesture()
    {
        IPointer? pointer = _cameraPointer;
        _cameraPointerGesture = ViewerCameraPointerGesture.None;
        _cameraPointer = null;
        pointer?.Capture(null);
    }

    private bool IsPointerInsideViewport(PointerEventArgs e)
    {
        Point point = e.GetPosition(ViewportHost);
        return point.X >= 0d &&
            point.Y >= 0d &&
            point.X <= ViewportHost.Bounds.Width &&
            point.Y <= ViewportHost.Bounds.Height;
    }

    private static ViewerPointerButtons GetPressedButtons(
        PointerPointProperties properties)
    {
        ViewerPointerButtons buttons = ViewerPointerButtons.None;
        if (properties.IsLeftButtonPressed)
        {
            buttons |= ViewerPointerButtons.Left;
        }
        if (properties.IsMiddleButtonPressed)
        {
            buttons |= ViewerPointerButtons.Middle;
        }
        if (properties.IsRightButtonPressed)
        {
            buttons |= ViewerPointerButtons.Right;
        }
        return buttons;
    }

    private bool IsCameraShortcutEditing()
    {
        IInputElement? focused = FocusManager?.GetFocusedElement();
        return focused is TextBox or ComboBox;
    }

    private double GetViewportRenderScaling() =>
        TopLevel.GetTopLevel(ViewportHost)?.RenderScaling ?? 1d;

    private void MarkAvaloniaCameraInput() =>
        _cameraRoutedInputGeneration = unchecked(_cameraRoutedInputGeneration + 1);

    private void RefreshStormNavigationPolling()
    {
        _cameraShortcutRepeat.ResetForFocusTransfer();
        StopStormNavigationPolling();
        if (_coordinator?.ActiveBackend?.Kind == RenderBackendKind.Storm &&
            ViewportHost.GetActiveStormNavigationSource() is { } source)
        {
            _stormNavigationHost = source;
            _stormNavigationTimer.Start();
        }
    }

    private void StopStormNavigationPolling()
    {
        _stormNavigationTimer.Stop();
        _stormNavigationInput.Reset();
        _stormPickInput.Reset();
        _stormNavigationHost = null;
    }

    private void OnStormNavigationTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_coordinator?.ActiveBackend?.Kind != RenderBackendKind.Storm)
        {
            StopStormNavigationPolling();
            return;
        }

        StormNativeControlHost? source =
            ViewportHost.GetActiveStormNavigationSource();
        if (source is null)
        {
            StopStormNavigationPolling();
            return;
        }
        if (!ReferenceEquals(source, _stormNavigationHost))
        {
            _stormNavigationHost = source;
            _stormNavigationInput.Reset();
            _stormPickInput.Reset();
        }

        try
        {
            if (!source.TryGetNavigationInput(
                    out OpenUsdStormNavigationInput input))
            {
                _stormNavigationInput.Reset();
                _stormPickInput.Reset();
                return;
            }
            if ((!IsAutomatedViewerRun() || ViewerStartupOptions.PickSmokeEnabled) &&
                _stormPickInput.TryUpdate(input, out ViewerPhysicalPixel pickPixel) &&
                _coordinator is { } pickCoordinator &&
                pickPixel.X < pickCoordinator.CurrentState.Viewport.Width &&
                pickPixel.Y < pickCoordinator.CurrentState.Viewport.Height)
            {
                StartViewportPick(pickCoordinator, pickPixel);
            }
            if (!CanNavigateCamera())
            {
                _stormNavigationInput.Reset();
                return;
            }
            ViewerStormNavigationDelta delta = _stormNavigationInput.Update(
                input,
                _cameraRoutedInputGeneration);
            if (delta.ResetPointerGesture)
            {
                if (input.Focused)
                {
                    _cameraShortcutRepeat.ResetForFocusTransfer();
                }
                EndCameraPointerGesture();
            }

            bool changed = false;
            if (delta.Gesture != ViewerCameraPointerGesture.None)
            {
                changed |= _cameraNavigation.ApplyGesture(
                    delta.Gesture,
                    delta.PointerDelta);
            }
            if (delta.WheelDelta != 0f)
            {
                changed |= _cameraNavigation.ZoomWheel(delta.WheelDelta);
            }
            if (delta.ResetAutomaticPresses != 0)
            {
                changed |= _cameraNavigation.ResetToAutomatic();
            }
            if ((delta.ToggleProjectionPresses & 1) != 0)
            {
                changed |= _cameraNavigation.ToggleProjection();
            }
            if (changed)
            {
                UpdateCameraStatus();
                if (!TryPostCameraSnapshot())
                {
                    StopStormNavigationPolling();
                    ShowError("The camera update worker is unavailable.");
                    return;
                }
            }
            for (ulong count = delta.FrameSelectedPresses; count != 0; count--)
            {
                _ = FrameSelectedAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            StopStormNavigationPolling();
        }
        catch (OpenUsdStormException exception)
        {
            StopStormNavigationPolling();
            ShowError($"Storm camera input failed: {exception.Message}");
        }
    }

    private void InitializeCameraUpdates(
        ViewerRenderCoordinator coordinator,
        CancellationToken documentToken)
    {
        if (IsAutomatedViewerRun())
        {
            UpdateCameraAvailability();
            return;
        }
        if (_cameraUpdates is not null)
        {
            throw new InvalidOperationException(
                "The camera update worker is already initialized.");
        }

        _cameraUpdates = new ViewerCameraUpdatePump(
            (camera, cancellationToken) => PublishCameraAsync(
                coordinator,
                camera,
                cancellationToken),
            OnCameraUpdateFailed,
            documentToken);
        _stageCameraRefreshes = new ViewerStageCameraRefreshPump(
            new ViewerSchedulerStageCameraSource(coordinator.Scheduler),
            _stageCameraMode,
            (application, cancellationToken) => ApplyStageCameraRefreshAsync(
                coordinator,
                application,
                cancellationToken),
            OnStageCameraRefreshFailed,
            documentToken);
        coordinator.StageChanged += OnStageChanged;
        UpdateCameraAvailability();
        RefreshStormNavigationPolling();
    }

    private static async ValueTask PublishCameraAsync(
        ViewerRenderCoordinator coordinator,
        CameraState camera,
        CancellationToken cancellationToken)
    {
        await coordinator.MutateStateAsync(
            state => state.WithCamera(camera),
            cancellationToken).ConfigureAwait(false);
    }

    private void OnCameraUpdateFailed(Exception exception) =>
        Dispatcher.UIThread.Post(() =>
        {
            StopStormNavigationPolling();
            UpdateCameraAvailability();
            ShowError($"Camera update failed: {exception.Message}");
        });

    private void OnStageChanged(UsdStageChange change)
    {
        _ = change;
        if (IsAutomatedViewerRun())
        {
            return;
        }

        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            if (coordinator is not null)
            {
                _ = TryQueueStageCameraRefresh(
                    coordinator.CurrentState.Time.TimeCode,
                    applyTime: false);
            }
        }
        catch (Exception exception)
        {
            OnStageCameraRefreshFailed(exception);
        }
    }

    private async ValueTask ApplyStageCameraRefreshAsync(
        ViewerRenderCoordinator coordinator,
        ViewerStageCameraRefreshApplication application,
        CancellationToken cancellationToken)
    {
        bool cameraApplied = false;
        await coordinator.MutateStateAsync(
            state =>
            {
                StageRenderState revised = application.Request.ApplyTime
                    ? state.WithTime(new StageTime(application.Request.TimeCode))
                    : state;
                if (application.Outcome == ViewerStageCameraRefreshOutcome.Ready &&
                    _stageCameraMode.TryGetActiveCamera(
                        application.Generation,
                        application.Request.PrimPath,
                        out CameraState activeCamera))
                {
                    cameraApplied = true;
                    return revised.WithCamera(activeCamera);
                }
                if (application.Outcome ==
                        ViewerStageCameraRefreshOutcome.FallbackAutomatic &&
                    _stageCameraMode.IsAutomaticFallback(application.Generation))
                {
                    cameraApplied = true;
                    return revised.WithCamera(CameraState.Default);
                }
                return revised;
            },
            cancellationToken).ConfigureAwait(false);

        if (cameraApplied)
        {
            QueueCameraStatusUpdate();
            if (application.Outcome ==
                ViewerStageCameraRefreshOutcome.FallbackAutomatic)
            {
                string error = application.Error ??
                    $"Camera '{application.Request.PrimPath}' is unavailable.";
                Dispatcher.UIThread.Post(() =>
                    ShowCameraMessage($"{error} Falling back to Automatic."));
            }
        }
    }

    private bool TryQueueStageCameraRefresh(double timeCode, bool applyTime)
    {
        if (!_stageCameraMode.TryCreateRefreshRequest(
                timeCode,
                applyTime,
                out ViewerStageCameraRefreshRequest request))
        {
            return false;
        }
        if (_stageCameraRefreshes?.TryPost(request) != true)
        {
            throw new InvalidOperationException(
                "The stage-camera refresh worker is unavailable.");
        }
        return true;
    }

    private void QueueCameraStatusUpdate()
    {
        if (Interlocked.Exchange(ref _cameraStatusUpdatePosted, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _cameraStatusUpdatePosted, 0);
            UpdateCameraStatus();
        });
    }

    private void OnStageCameraRefreshFailed(Exception exception) =>
        Dispatcher.UIThread.Post(() =>
        {
            _cameraNavigation.ResetToAutomatic();
            UpdateCameraStatus();
            _ = TryPostCameraSnapshot();
            ShowError($"Stage-camera refresh failed: {exception.Message}");
        });

    private bool CanNavigateCamera() =>
        !_documentBusy &&
        !IsAutomatedViewerRun() &&
        _coordinator is not null &&
        _cameraUpdates is { IsAccepting: true };

    private bool TryPostCameraSnapshot()
    {
        ViewerCameraUpdatePump? cameraUpdates = _cameraUpdates;
        return cameraUpdates is not null &&
            cameraUpdates.TryPost(_cameraNavigation.Camera);
    }

    private void UpdateCameraAvailability()
    {
        bool enabled = CanNavigateCamera();
        ResetCameraAutomaticButton.IsEnabled = enabled;
        ResetCameraAutomaticMenuItem.IsEnabled = enabled;
        ResetCameraLegacyButton.IsEnabled = enabled;
        ResetCameraLegacyMenuItem.IsEnabled = enabled;
        ToggleCameraProjectionButton.IsEnabled = enabled;
        ToggleCameraProjectionMenuItem.IsEnabled = enabled;
        bool selectedCamera = enabled &&
            _stageCameraRefreshes is { IsAccepting: true } &&
            !_stageCameraSelectionBusy &&
            _currentInspector is { IsCamera: true } inspector &&
            string.Equals(
                inspector.Path,
                _selectionState.PrimPath,
                StringComparison.Ordinal);
        UseSelectedCameraButton.IsEnabled = selectedCamera;
        UseSelectedCameraMenuItem.IsEnabled = selectedCamera;
        string selectedCameraTip = selectedCamera
            ? $"Use authored camera '{_selectionState.PrimPath}'."
            : GetUseSelectedCameraUnavailableMessage();
        ToolTip.SetTip(UseSelectedCameraButton, selectedCameraTip);
        ToolTip.SetTip(UseSelectedCameraMenuItem, selectedCameraTip);
        string selectedCameraAutomationName = selectedCamera
            ? "Use selected UsdGeomCamera"
            : $"Use selected UsdGeomCamera unavailable: {selectedCameraTip}";
        AutomationProperties.SetName(
            UseSelectedCameraButton,
            selectedCameraAutomationName);
        AutomationProperties.SetName(
            UseSelectedCameraMenuItem,
            selectedCameraAutomationName);
        FrameSelectedButton.IsEnabled = enabled;
        FrameSelectedMenuItem.IsEnabled = enabled;
    }

    private void UpdateCameraStatus()
    {
        ViewerCameraDisplayMode mode = _cameraNavigation.DisplayMode;
        string? stageCameraPath = _cameraNavigation.StageCameraPath;
        RenderBackendKind? backendKind = _coordinator?.ActiveBackend?.Kind;
        if (_displayedCameraMode == mode &&
            string.Equals(
                _displayedCameraPath,
                stageCameraPath,
                StringComparison.Ordinal))
        {
            return;
        }

        _displayedCameraMode = mode;
        _displayedCameraPath = stageCameraPath;
        string cameraStatus = mode switch
        {
            ViewerCameraDisplayMode.Automatic => "Camera: Automatic",
            ViewerCameraDisplayMode.Perspective => "Camera: Perspective",
            ViewerCameraDisplayMode.Orthographic => "Camera: Orthographic",
            ViewerCameraDisplayMode.StagePerspective =>
                $"Camera: Stage Perspective · {stageCameraPath}",
            ViewerCameraDisplayMode.StageOrthographic =>
                $"Camera: Stage Orthographic · {stageCameraPath}",
            _ => throw new InvalidOperationException("Unknown camera display mode."),
        };
        CameraStatus.Text = ViewerCameraInputAvailability.FormatStatus(
            cameraStatus,
            backendKind);
    }

    private void OnFrameSelectedClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = FrameSelectedAsync();
    }

    private void OnShortcutsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var shortcuts = new ShortcutsWindow();
        _ = shortcuts.ShowDialog(this);
    }

    private async Task FrameSelectedAsync()
    {
        if (!CanNavigateCamera())
        {
            return;
        }
        if (_cameraNavigation.ExitStageCameraForNavigation())
        {
            UpdateCameraStatus();
            if (!TryPostCameraSnapshot())
            {
                ShowError("The camera update worker is unavailable.");
                return;
            }
        }

        CancellationToken cancellationToken =
            _documentLifetime?.Token ?? _viewerLifetime.Token;
        try
        {
            await _documentGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            if (coordinator is null || IsAutomatedViewerRun())
            {
                return;
            }

            StageRenderState current = coordinator.CurrentState;
            var source = new ViewerSchedulerSelectedBoundsSource(
                coordinator.Scheduler);
            ViewerFrameSelectedResult result = await ViewerFrameSelectedQuery.QueryAsync(
                source,
                _selectionState.PrimPath,
                current.Time,
                current.Display.Purposes,
                cancellationToken);
            switch (result.Outcome)
            {
                case ViewerFrameSelectedOutcome.NoSelection:
                    ShowCameraMessage("Select a prim before using Frame Selected.");
                    return;
                case ViewerFrameSelectedOutcome.MissingPrim:
                    ShowCameraMessage(
                        $"The selected prim '{result.PrimPath}' no longer exists.");
                    return;
                case ViewerFrameSelectedOutcome.EmptyBounds:
                    ShowCameraMessage(
                        $"The selected prim '{result.PrimPath}' has no world bounds " +
                        "for the current time and render purposes; the camera was unchanged.");
                    return;
                case ViewerFrameSelectedOutcome.Ready:
                    if (!_cameraNavigation.FrameBounds(result.Bounds))
                    {
                        ShowCameraMessage(
                            $"The selected prim '{result.PrimPath}' has no frameable bounds; " +
                            "the camera was unchanged.");
                        return;
                    }
                    UpdateCameraStatus();
                    if (!TryPostCameraSnapshot())
                    {
                        ShowError("The camera update worker is unavailable.");
                        return;
                    }
                    ShowCameraMessage($"Framed selected prim '{result.PrimPath}'.");
                    return;
                default:
                    throw new InvalidOperationException(
                        "Unknown frame-selected result.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not frame the selected prim: {exception.Message}");
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private void ShowCameraMessage(string status)
    {
        ViewerStatus.Text = status;
        ViewerStatus.Foreground = null;
        ViewerStartupOptions.WriteStatus(status);
    }

    private async void OnRendererSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_selectorReady)
        {
            return;
        }

        try
        {
            await _documentGate.WaitAsync(_viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
            return;
        }
        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            CancellationToken cancellationToken =
                _documentLifetime?.Token ?? _viewerLifetime.Token;
            if (coordinator is null)
            {
                return;
            }
            RenderBackendManagerResult result = await coordinator.SwitchAsync(
                GetSelectedBackend(),
                cancellationToken);
            SetActiveBackendStatus();
            CaptureDiagnostics(coordinator, frameResult: null, force: true);
            if (!result.IsSuccess)
            {
                RendererStatus.Text = $"Renderer switch failed: {result.Failure}";
            }
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RendererStatus.Text = $"Renderer switch failed: {exception.Message}";
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        try
        {
            await _documentGate.WaitAsync(_viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
            return;
        }
        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            if (coordinator is not null)
            {
                await UpdateViewportStateAsync(
                    coordinator,
                    _documentLifetime?.Token ?? _viewerLifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _cameraNavigation.ResetToAutomatic();
            UpdateCameraStatus();
            ShowError($"Viewport camera update failed: {exception.Message}");
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async Task UpdateViewportStateAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        double scaling = TopLevel.GetTopLevel(ViewportHost)?.RenderScaling ?? 1;
        ViewportDimensions viewport = ViewportPixelMath.ToPixels(
            ViewportHost.Bounds.Width,
            ViewportHost.Bounds.Height,
            scaling);
        ViewerCameraResizeUpdate cameraResize = _cameraNavigation.Resize(viewport);
        if (IsAutomatedViewerRun())
        {
            await coordinator.MutateStateAsync(
                state => state.WithViewport(viewport),
                cancellationToken);
            return;
        }

        await coordinator.MutateStateAsync(
            state => ViewerCameraStateMutation.ApplyResize(state, cameraResize),
            cancellationToken);
        UpdateCameraStatus();
        bool stageCameraActive = _stageCameraMode.GetView().IsActive;
        if (stageCameraActive)
        {
            _ = TryQueueStageCameraRefresh(
                coordinator.CurrentState.Time.TimeCode,
                applyTime: false);
        }
    }

    private async Task RunRenderLoopAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Volatile.Read(ref _diagnosticOwnsRendering) != 0)
                {
                    continue;
                }
                ManagedRenderFrameResult result =
                    await coordinator.RenderAsync(cancellationToken);
                CaptureDiagnostics(
                    coordinator,
                    result,
                    force: result.DidFailOver || !result.IsSuccess);
                if (result.DidFailOver)
                {
                    await Dispatcher.UIThread.InvokeAsync(SetActiveBackendStatus);
                }
                if (!result.IsSuccess)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        RendererStatus.Text = $"Renderer frame failed: {result.Failure}");
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ViewerStartupOptions.WriteStatus($"Renderer frame failed: {exception.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                RendererStatus.Text =
                    $"Renderer frame failed: {ViewerPackageErrorFormatter.Format(exception)}");
        }
    }

    private void CaptureDiagnostics(
        ViewerRenderCoordinator coordinator,
        ManagedRenderFrameResult? frameResult,
        bool force)
    {
        if (IsAutomatedViewerRun() ||
            !InspectorPanel.IsVisible ||
            !DiagnosticsTab.IsVisible)
        {
            return;
        }

        RenderBackendIdentity? active = coordinator.ActiveBackend;
        ulong stateKey =
            ((ulong)((active is null ? 0 : (int)active.Kind + 1) & 0xFF) << 56) |
            ((ulong)(uint)coordinator.RetiredCleanupCount << 24) |
            (uint)(frameResult?.Failure ?? RenderBackendManagerFailureKind.None);
        if (!_diagnosticsCadence.ShouldSample(
            Stopwatch.GetTimestamp(),
            stateKey,
            force))
        {
            return;
        }

        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        RenderFrameStatistics statistics =
            frameResult?.Frame?.Statistics ?? RenderFrameStatistics.Empty;
        RenderBackendDiagnostics diagnostics =
            frameResult?.Diagnostics.Entries.Count > 0
                ? frameResult.Diagnostics
                : coordinator.LatestDiagnostics;
        ViewerDiagnosticEntry[] diagnosticEntries =
        [
            .. ViewerDiagnosticEntryFactory.From(diagnostics, timestamp),
            .. ViewerSilkFrameDiagnosticEntryFactory.From(coordinator.FrameDiagnostics, timestamp)
        ];
        ViewerBackendRuntimeIdentity runtimeIdentity =
            active is null || _backendHost is null
                ? ViewerBackendRuntimeIdentity.Unknown
                : _backendHost.GetRuntimeIdentity(active.Kind);
        _diagnostics.Observe(new ViewerDiagnosticsSample(
            timestamp,
            active?.Name ?? "Unavailable",
            runtimeIdentity,
            coordinator.LatestRecoveryReason,
            statistics.CpuTime,
            statistics.GpuTime,
            statistics.DrawCalls,
            statistics.Triangles,
            coordinator.RetiredCleanupCount,
            frameResult?.Frame?.StateRevision ?? coordinator.CurrentState.Revision,
            ViewerResourceCounters.Capture(),
            diagnosticEntries));
        _latestDiagnostics = _diagnostics.Snapshot();
        if (Dispatcher.UIThread.CheckAccess())
        {
            RenderDiagnostics();
        }
        else
        {
            Dispatcher.UIThread.Post(RenderDiagnostics);
        }
    }

    private void RenderDiagnostics()
    {
        _latestDiagnostics = _diagnostics.Snapshot();
        bool hasSample = _latestDiagnostics.Timestamp != DateTimeOffset.MinValue;
        DiagnosticsText.Text = hasSample
            ? _diagnosticsFormatter.Format(
                _latestDiagnostics,
                IncludeDiagnosticPathsCheckBox.IsChecked == true)
            : "No diagnostics have been sampled.";
        DiagnosticsState.Text = hasSample
            ? $"Latest bounded sample; {_latestDiagnostics.Entries.Length}/" +
                $"{ViewerDiagnosticsBuffer.DefaultEntryCapacity} diagnostic entries retained."
            : "Diagnostics are sampled from normal rendering at a bounded cadence.";
        CopyDiagnosticsButton.IsEnabled = hasSample;
        ExportDiagnosticsButton.IsEnabled = hasSample;
        ShowStageSummary();
    }

    private void OnDiagnosticPathSettingChanged(object? sender, RoutedEventArgs e) =>
        RenderDiagnostics();

    private async void OnRefreshValidationClick(object? sender, RoutedEventArgs e)
    {
        if (_coordinator is null || _documentLifetime is null)
        {
            return;
        }

        await RefreshValidationAsync(_coordinator, _documentLifetime.Token);
    }

    private async Task RefreshValidationAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (_validationBusy)
        {
            return;
        }

        _validationBusy = true;
        RenderValidation();
        try
        {
            ValidationState.Text = "Running UsdValidation...";
            _validation = await coordinator.Scheduler.InvokeAsync(
                static stage =>
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    IReadOnlyList<UsdValidationValidatorInfo> validators =
                        UsdValidation.GetRegisteredValidators();
                    IReadOnlyList<UsdValidationError> errors = UsdValidation.Validate(stage);
                    timer.Stop();
                    return ViewerValidationSnapshot.Create(validators, errors, timer.Elapsed);
                },
                cancellationToken);
            RenderValidation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ValidationState.Text =
                $"UsdValidation failed: {ViewerPackageErrorFormatter.Format(exception)}";
            ValidationText.Text = exception.ToString();
        }
        finally
        {
            _validationBusy = false;
            RefreshValidationButton.IsEnabled = _coordinator is not null && !_documentBusy;
        }
    }

    private void RenderValidation()
    {
        ValidationState.Text = _validationBusy
            ? "Running UsdValidation..."
            : ViewerValidationFormatter.FormatState(_validation);
        ValidationText.Text = ViewerValidationFormatter.FormatDetails(_validation);
        RefreshValidationButton.IsEnabled =
            _coordinator is not null &&
            !_documentBusy &&
            !_validationBusy;
    }

    private async void OnViewportDrawModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingViewportDisplay ||
            ViewportDrawModeSelector.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag ||
            !Enum.TryParse(tag, out RenderDrawMode drawMode))
        {
            return;
        }

        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithDrawMode(state, drawMode),
            $"Viewport draw mode: {drawMode}.");
    }

    private async void OnViewportPurposeChanged(object? sender, RoutedEventArgs e)
    {
        if (_applyingViewportDisplay ||
            sender is not CheckBox checkBox ||
            GetPurposeForCheckBox(checkBox) is not { } purpose)
        {
            return;
        }

        bool enabled = checkBox.IsChecked == true;
        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithPurpose(state, purpose, enabled),
            $"Viewport purposes: {purpose} {(enabled ? "enabled" : "disabled")}.");
    }

    private async void OnViewportLightingChanged(object? sender, RoutedEventArgs e)
    {
        if (_applyingViewportDisplay)
        {
            return;
        }

        bool enabled = SceneLightingCheckBox.IsChecked == true;
        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithLighting(state, enabled),
            enabled ? "Viewport scene lighting enabled." : "Viewport scene lighting disabled.");
    }

    private async void OnViewportShadowsChanged(object? sender, RoutedEventArgs e)
    {
        if (_applyingViewportDisplay)
        {
            return;
        }

        bool enabled = SceneShadowsCheckBox.IsChecked == true;
        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithShadows(state, enabled),
            enabled ? "Viewport shadows enabled." : "Viewport shadows disabled.");
    }

    private async void OnViewportBackfaceCullingChanged(object? sender, RoutedEventArgs e)
    {
        if (_applyingViewportDisplay)
        {
            return;
        }

        bool enabled = BackfaceCullingCheckBox.IsChecked == true;
        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithBackfaceCulling(state, enabled),
            enabled ? "Viewport backface culling enabled." : "Viewport backface culling disabled.");
    }

    private async void OnViewportSceneMaterialsChanged(object? sender, RoutedEventArgs e)
    {
        if (_applyingViewportDisplay)
        {
            return;
        }

        bool enabled = SceneMaterialsCheckBox.IsChecked == true;
        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithSceneMaterials(state, enabled),
            enabled ? "Viewport scene materials enabled." : "Viewport scene materials disabled.");
    }

    private async void OnViewportBackgroundChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingViewportDisplay ||
            BackgroundColorSelector.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag ||
            !Enum.TryParse(tag, out ViewerBackgroundPreset preset))
        {
            return;
        }

        await ApplyViewportStateAsync(
            state => ViewerViewportStateMutation.WithBackground(state, preset),
            $"Viewport background: {item.Content}.");
    }

    private async Task ApplyViewportStateAsync(
        Func<StageRenderState, StageRenderState> mutate,
        string status)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ViewerRenderCoordinator? coordinator = _coordinator;
        if (coordinator is null || _documentBusy)
        {
            ApplyViewportDisplayState(coordinator?.CurrentState ?? StageRenderState.Default);
            return;
        }

        CancellationToken cancellationToken = _documentLifetime?.Token ?? _viewerLifetime.Token;
        try
        {
            bool changed = await coordinator.MutateStateAsync(
                state => mutate(state),
                cancellationToken);
            ApplyViewportDisplayState(coordinator.CurrentState);
            if (changed)
            {
                ViewerStatus.Text = status;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not update viewport display: {exception.Message}");
            ApplyViewportDisplayState(coordinator.CurrentState);
        }
    }

    private void ApplyViewportDisplayState(StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _applyingViewportDisplay = true;
        try
        {
            SelectComboBoxTag(ViewportDrawModeSelector, state.Display.DrawMode.ToString());
            PurposeDefaultCheckBox.IsChecked = (state.Display.Purposes & RenderPurpose.Default) != 0;
            PurposeProxyCheckBox.IsChecked = (state.Display.Purposes & RenderPurpose.Proxy) != 0;
            PurposeRenderCheckBox.IsChecked = (state.Display.Purposes & RenderPurpose.Render) != 0;
            PurposeGuideCheckBox.IsChecked = (state.Display.Purposes & RenderPurpose.Guide) != 0;
            SceneLightingCheckBox.IsChecked = state.RenderSettings.EnableLighting;
            SceneShadowsCheckBox.IsChecked = state.RenderSettings.EnableShadows;
            BackfaceCullingCheckBox.IsChecked = state.RenderSettings.BackfaceCulling;
            SceneMaterialsCheckBox.IsChecked = state.RenderSettings.UseSceneMaterials;
            SelectComboBoxTag(BackgroundColorSelector, GetBackgroundPreset(state).ToString());
        }
        finally
        {
            _applyingViewportDisplay = false;
        }
        UpdateViewportDisplayAvailability();
    }

    private void UpdateViewportDisplayAvailability()
    {
        bool enabled = _coordinator is not null && !_documentBusy && !IsAutomatedViewerRun();
        ViewportDrawModeSelector.IsEnabled = enabled;
        PurposeDefaultCheckBox.IsEnabled = enabled;
        PurposeProxyCheckBox.IsEnabled = enabled;
        PurposeRenderCheckBox.IsEnabled = enabled;
        PurposeGuideCheckBox.IsEnabled = enabled;
        SceneLightingCheckBox.IsEnabled = enabled;
        SceneShadowsCheckBox.IsEnabled = enabled;
        BackfaceCullingCheckBox.IsEnabled = enabled;
        SceneMaterialsCheckBox.IsEnabled = enabled;
        BackgroundColorSelector.IsEnabled = enabled;
    }

    private static RenderPurpose? GetPurposeForCheckBox(CheckBox checkBox) =>
        checkBox.Name switch
        {
            nameof(PurposeDefaultCheckBox) => RenderPurpose.Default,
            nameof(PurposeProxyCheckBox) => RenderPurpose.Proxy,
            nameof(PurposeRenderCheckBox) => RenderPurpose.Render,
            nameof(PurposeGuideCheckBox) => RenderPurpose.Guide,
            _ => null
        };

    private static ViewerBackgroundPreset GetBackgroundPreset(StageRenderState state)
    {
        Vector4 color = state.RenderSettings.ClearColor;
        foreach (ViewerBackgroundPreset preset in Enum.GetValues<ViewerBackgroundPreset>())
        {
            if (color == ViewerViewportStateMutation.ToColor(preset))
            {
                return preset;
            }
        }
        return ViewerBackgroundPreset.Black;
    }

    private static void SelectComboBoxTag(ComboBox selector, string tag)
    {
        foreach (object? item in selector.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                comboBoxItem.Tag is string candidate &&
                string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                selector.SelectedItem = comboBoxItem;
                return;
            }
        }
    }

    private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Avalonia.Input.Platform.IClipboard? clipboard =
                TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                throw new InvalidOperationException("The platform clipboard is unavailable.");
            }
            string text = _diagnosticsFormatter.Format(
                _latestDiagnostics,
                IncludeDiagnosticPathsCheckBox.IsChecked == true);
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
            DiagnosticsState.Text = "Diagnostics copied.";
        }
        catch (Exception exception)
        {
            ShowError($"Diagnostics could not be copied: {exception.Message}");
        }
    }

    private async void OnExportDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export Viewer Diagnostics",
                    SuggestedFileName = "openusd-viewer-diagnostics.txt",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Text")
                        {
                            Patterns = ["*.txt"]
                        }
                    ]
                });
            if (file is null)
            {
                return;
            }

            string text = _diagnosticsFormatter.Format(
                _latestDiagnostics,
                IncludeDiagnosticPathsCheckBox.IsChecked == true);
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(
                stream,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: false);
            await writer.WriteAsync(text.AsMemory(), _viewerLifetime.Token);
            DiagnosticsState.Text = "Diagnostics exported.";
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            ShowError($"Diagnostics could not be exported: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowError($"Diagnostics could not be exported: {exception.Message}");
        }
        catch (Exception exception)
        {
            ShowError($"Diagnostics could not be exported: {exception.Message}");
        }
    }

    private async Task InitializeTimelineAsync(
        ViewerRenderCoordinator coordinator,
        ViewerStageTimingSnapshot timing,
        CancellationToken documentToken)
    {
        double current = coordinator.CurrentState.Time.TimeCode;
        bool automated = IsAutomatedViewerRun();
        if (!automated && timing.HasFiniteRange)
        {
            current = ViewerTimelineMath.Clamp(current, timing);
            await coordinator.MutateStateAsync(
                state => state.WithTime(new StageTime(current)),
                documentToken);
        }

        lock (_timelineGate)
        {
            _currentTimeCode = current;
        }
        UpdateTimelineUi(current);
        if (!automated && timing.HasFiniteRange)
        {
            _timeUpdates = new ViewerTimeUpdatePump(
                async (timeCode, cancellationToken) =>
                {
                    if (TryQueueStageCameraRefresh(
                            timeCode,
                            applyTime: true))
                    {
                        return;
                    }
                    await coordinator.MutateStateAsync(
                        state => state.WithTime(new StageTime(timeCode)),
                        cancellationToken);
                },
                OnTimelineUpdateFailed,
                documentToken);
        }
        UpdateTimelineAvailability();
    }

    private async void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_playbackLifetime is not null)
            {
                await StopPlaybackAsync();
                return;
            }
            ViewerPlaybackPlan? plan = _timing.PlaybackPlan;
            CancellationTokenSource? documentLifetime = _documentLifetime;
            if (plan is null || documentLifetime is null || IsAutomatedViewerRun())
            {
                return;
            }

            var playbackLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
            _playbackLifetime = playbackLifetime;
            PlayPauseButton.Content = "_Pause";
            AutomationProperties.SetName(PlayPauseButton, "Pause timeline");
            UpdateTimelineAvailability();
            _playbackTask = RunPlaybackAsync(
                playbackLifetime,
                _timing,
                plan,
                playbackLifetime.Token);
        }
        catch (Exception exception)
        {
            ShowTimelineError($"Playback could not start: {exception.Message}");
        }
    }

    private async Task RunPlaybackAsync(
        CancellationTokenSource playbackLifetime,
        ViewerStageTimingSnapshot timing,
        ViewerPlaybackPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            var clock = new ViewerPlaybackClock(plan.FrameInterval);
            while (true)
            {
                await clock.WaitForNextTickAsync(cancellationToken);
                double next;
                lock (_timelineGate)
                {
                    next = ViewerTimelineMath.Advance(_currentTimeCode, timing, plan);
                    _currentTimeCode = next;
                }
                if (_timeUpdates?.TryPost(next) != true)
                {
                    throw new InvalidOperationException(
                        "The timeline update worker is unavailable.");
                }
                QueueTimelineUiUpdate(playbackLifetime);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(playbackLifetime, _playbackLifetime))
                {
                    _playbackLifetime = null;
                    _playbackTask = null;
                    playbackLifetime.Dispose();
                    PlayPauseButton.Content = "_Play";
                    AutomationProperties.SetName(PlayPauseButton, "Play timeline");
                    double current;
                    lock (_timelineGate)
                    {
                        current = _currentTimeCode;
                    }
                    UpdateTimelineUi(current);
                    UpdateTimelineAvailability();
                }
                ShowTimelineError($"Playback stopped: {exception.Message}");
            });
        }
    }

    private async Task StopPlaybackAsync()
    {
        CancellationTokenSource? playbackLifetime = _playbackLifetime;
        Task? playbackTask = _playbackTask;
        _playbackLifetime = null;
        _playbackTask = null;
        playbackLifetime?.Cancel();
        if (playbackTask is not null)
        {
            await playbackTask;
        }
        playbackLifetime?.Dispose();
        PlayPauseButton.Content = "_Play";
        AutomationProperties.SetName(PlayPauseButton, "Play timeline");
        double current;
        lock (_timelineGate)
        {
            current = _currentTimeCode;
        }
        UpdateTimelineUi(current);
        UpdateTimelineAvailability();
    }

    private async Task StopTimelineAsync()
    {
        await StopPlaybackAsync();
        ViewerTimeUpdatePump? timeUpdates = _timeUpdates;
        _timeUpdates = null;
        if (timeUpdates is not null)
        {
            await timeUpdates.DisposeAsync();
        }
    }

    private void OnCurrentTimeInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        ApplyCurrentTimeInput();
        e.Handled = true;
    }

    private void OnCurrentTimeInputLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!_updatingTimelineUi)
        {
            ApplyCurrentTimeInput();
        }
    }

    private void ApplyCurrentTimeInput()
    {
        if (!_timing.HasFiniteRange)
        {
            return;
        }
        if (!ViewerTimelineMath.TryParse(CurrentTimeInput.Text, out double parsed))
        {
            CurrentTimeInput.Foreground = null;
            ShowTimelineError("Enter a finite time code using invariant numeric syntax.");
            return;
        }
        SetCurrentTime(NormalizeManualTime(parsed));
    }

    private void OnTimelineSliderValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingTimelineUi && _timing.HasFiniteRange)
        {
            SetCurrentTime(NormalizeManualTime(e.NewValue));
        }
    }

    private double NormalizeManualTime(double timeCode)
    {
        double clamped = ViewerTimelineMath.Clamp(timeCode, _timing);
        return SnapTimelineCheckBox.IsChecked == true
            ? ViewerTimelineMath.SnapToFrame(clamped, _timing)
            : clamped;
    }

    private void SetCurrentTime(double timeCode)
    {
        lock (_timelineGate)
        {
            _currentTimeCode = timeCode;
        }
        UpdateTimelineUi(timeCode);
        if (_timeUpdates?.TryPost(timeCode) != true)
        {
            ShowTimelineError("The timeline update worker is unavailable.");
        }
    }

    private void UpdateTimelineUi(double timeCode)
    {
        _updatingTimelineUi = true;
        try
        {
            CurrentTimeInput.Text = ViewerTimelineMath.Format(timeCode);
            CurrentTimeInput.Foreground = null;
            TimelineSlider.Minimum = _timing.PresentationStart;
            TimelineSlider.Maximum = _timing.PresentationEnd;
            TimelineSlider.Value = _timing.HasFiniteRange
                ? Math.Clamp(timeCode, _timing.PresentationStart, _timing.PresentationEnd)
                : 0;
            TimelineStart.Text =
                $"Start: {ViewerTimelineMath.FormatAuthored(_timing.StartTimeCode)}";
            TimelineEndAndRate.Text =
                $"End: {ViewerTimelineMath.FormatAuthored(_timing.EndTimeCode)} · " +
                $"FPS: {ViewerTimelineMath.FormatAuthored(_timing.FramesPerSecond)} · " +
                $"TCPS: {ViewerTimelineMath.FormatAuthored(_timing.TimeCodesPerSecond)}";
            TimelineDiagnostic.Text = _timing.Diagnostic ?? string.Empty;
        }
        finally
        {
            _updatingTimelineUi = false;
        }
    }

    private void UpdateTimelineAvailability()
    {
        bool interactiveDocument =
            !_documentBusy && _coordinator is not null && !IsAutomatedViewerRun();
        bool canEditTime =
            interactiveDocument && _playbackLifetime is null && _timing.HasFiniteRange;
        CurrentTimeInput.IsEnabled = canEditTime;
        TimelineSlider.IsEnabled = canEditTime;
        PlayPauseButton.IsEnabled = interactiveDocument && _timing.CanPlay;
    }

    private void QueueTimelineUiUpdate(CancellationTokenSource playbackLifetime)
    {
        if (Interlocked.Exchange(ref _timelineUiUpdatePosted, 1) != 0)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _timelineUiUpdatePosted, 0);
            if (!ReferenceEquals(playbackLifetime, _playbackLifetime) ||
                playbackLifetime.IsCancellationRequested)
            {
                return;
            }
            double current;
            lock (_timelineGate)
            {
                current = _currentTimeCode;
            }
            UpdateTimelineUi(current);
        });
    }

    private void OnTimelineUpdateFailed(Exception exception) =>
        Dispatcher.UIThread.Post(() =>
            ShowTimelineError($"Timeline update failed: {exception.Message}"));

    private void ShowTimelineError(string message)
    {
        TimelineDiagnostic.Text = message;
        ViewerStartupOptions.WriteStatus(message);
    }

    private async void OnOpenStageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open USD Stage",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Universal Scene Description")
                        {
                            Patterns = ["*.usd", "*.usda", "*.usdc", "*.usdz"]
                        }
                    ]
                });
            string? path = files.Count == 0 ? null : files[0].TryGetLocalPath();
            if (path is not null)
            {
                await OpenStageAndReportAsync(path);
            }
        }
        catch (Exception exception)
        {
            ShowError($"The stage picker failed: {exception.Message}");
        }
    }

    private async void OnReloadStageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ReloadStageAsync(_viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not reload the stage: {exception.Message}");
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles()?
            .Any(item => IsUsdStagePath(item.TryGetLocalPath())) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        string? path = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .FirstOrDefault(IsUsdStagePath);
        if (path is not null)
        {
            await OpenStageAndReportAsync(path);
        }
    }

    private async Task OpenStageAndReportAsync(string stagePath)
    {
        try
        {
            await OpenStageCoreAsync(stagePath, addToRecent: true, _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError(
                $"Could not open '{stagePath}': {ViewerPackageErrorFormatter.Format(exception)}");
        }
    }

    private async Task OpenStageCoreAsync(
        string stagePath,
        bool addToRecent,
        CancellationToken cancellationToken)
    {
        string normalizedPath = Path.GetFullPath(stagePath);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("The selected USD stage does not exist.", normalizedPath);
        }
        if (!IsUsdStagePath(normalizedPath))
        {
            throw new InvalidOperationException(
                "Select a .usd, .usda, .usdc, or .usdz stage.");
        }

        await _documentGate.WaitAsync(cancellationToken);
        try
        {
            SetBusy($"Opening {Path.GetFileName(normalizedPath)}...");
            if (!IsAutomatedViewerRun())
            {
                await using UsdStageScheduler validationScheduler =
                    UsdStageScheduler.Open(normalizedPath);
                _ = await validationScheduler.InvokeAsync(
                    static stage => stage.RootLayerIdentifier,
                    cancellationToken);
            }

            await StopCurrentDocumentAsync();
            _cameraNavigation.ResetToAutomatic();
            UpdateCameraStatus();
            var documentLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(_viewerLifetime.Token);
            ViewerRenderCoordinator? coordinator = null;
            AvaloniaViewerRenderBackendHost? backendHost = null;
            try
            {
                coordinator = await ViewerRenderCoordinator.OpenAsync(
                    normalizedPath,
                    (scheduler, source) =>
                    {
                        backendHost = new AvaloniaViewerRenderBackendHost(
                            ViewportHost,
                            scheduler,
                            source,
                            ViewerStartupOptions.PluginPath ?? string.Empty,
                            OnCompositionStatusChanged);
                        return backendHost;
                    },
                    GetSelectedBackend(),
                    documentLifetime.Token);
                ViewerDocumentSnapshot document = await coordinator.Scheduler.InvokeAsync(
                    ViewerStageSnapshotBuilder.BuildDocument,
                    documentLifetime.Token);

                _documentLifetime = documentLifetime;
                _coordinator = coordinator;
                _backendHost = backendHost;
                _hierarchy = document.Hierarchy;
                _timing = document.Timing;
                _layers = document.Layers;
                _statistics = document.Statistics;
                _currentInspector = document.SelectedPrim;
                _rootLayerEditsExplicitlyEnabled = false;
                _stagePath = normalizedPath;
                coordinator.StatusChanged += OnRendererStatusChanged;
                InitializeCameraUpdates(coordinator, documentLifetime.Token);
                coordinator = null;
                documentLifetime = null!;

                StageStatus.Text = Path.GetFileName(normalizedPath);
                ShowStageSummary();
                ReloadStageButton.IsEnabled = true;
                ReloadStageMenuItem.IsEnabled = true;
                RenderHierarchy();
                RenderLayers();
                RenderValidation();
                SetActiveBackendStatus();
                CaptureDiagnostics(
                    _coordinator,
                    frameResult: null,
                    force: true);
                await InitializeTimelineAsync(
                    _coordinator,
                    _timing,
                    _documentLifetime.Token);
                await RefreshValidationAsync(_coordinator, _documentLifetime.Token);
                await UpdateViewportStateAsync(_coordinator, _documentLifetime.Token);
                if (ViewerStartupOptions.IsCleanupRetryEvidenceScenario ||
                    ViewerStartupOptions.IsRetiredKindQuarantineEvidenceScenario ||
                    ViewerStartupOptions.IsStageCameraEvidenceScenario ||
                    ViewerStartupOptions.PickSmokeEnabled)
                {
                    Volatile.Write(ref _diagnosticOwnsRendering, 1);
                }
                _renderLoop = RunRenderLoopAsync(_coordinator, _documentLifetime.Token);
                if (IsAutomatedViewerRun())
                {
                    _diagnosticSequence = RunObservedDiagnosticSequenceAsync(
                        _coordinator,
                        _documentLifetime.Token);
                }
                SetReady($"Opened {normalizedPath}");
                await NotifyStageReadyAsync(_coordinator, normalizedPath, _documentLifetime.Token);
            }
            finally
            {
                if (coordinator is not null)
                {
                    await coordinator.DisposeAsync();
                }
                documentLifetime?.Dispose();
            }

            if (addToRecent)
            {
                try
                {
                    IReadOnlyList<string> recent = await _recentStageStore.AddAsync(
                        normalizedPath,
                        cancellationToken);
                    RefreshRecentMenu(recent);
                }
                catch (IOException exception)
                {
                    ShowError($"The recent-stage list could not be saved: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    ShowError($"The recent-stage list could not be saved: {exception.Message}");
                }
            }
        }
        finally
        {
            _documentGate.Release();
        }
    }

    /// <summary>
    /// Applies the embedding host's startup stage camera and invokes its stage-ready
    /// callback, if either was supplied. A failing callback is reported as a viewer error
    /// and never tears the shell down.
    /// </summary>
    private async Task NotifyStageReadyAsync(
        ViewerRenderCoordinator coordinator,
        string stagePath,
        CancellationToken cancellationToken)
    {
        Func<ViewerStageSession, CancellationToken, Task>? callback =
            ViewerStartupOptions.StageReadyAsync;
        string? cameraPath = ViewerStartupOptions.HostStageCameraPath;
        if (callback is null && string.IsNullOrEmpty(cameraPath))
        {
            return;
        }
        try
        {
            if (!string.IsNullOrEmpty(cameraPath))
            {
                await ApplyHostStageCameraAsync(coordinator, cameraPath!, cancellationToken);
            }
            if (callback is not null)
            {
                await callback(
                    new ViewerStageSession(coordinator.Scheduler, stagePath),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The document was closed while the host was starting; nothing to report.
        }
#pragma warning disable CA1031 // A host callback must not be able to tear down the shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            ShowError($"The viewer host failed to start: {exception.Message}");
        }
    }

    /// <summary>
    /// Applies a stage camera the embedding host asked to start on, so a host can open
    /// the viewport on, say, an overhead view instead of the automatic framing. A camera
    /// that cannot be used is reported and leaves the automatic camera in place.
    /// </summary>
    private async Task ApplyHostStageCameraAsync(
        ViewerRenderCoordinator coordinator,
        string primPath,
        CancellationToken cancellationToken)
    {
        double timeCode;
        lock (_timelineGate)
        {
            timeCode = _currentTimeCode;
        }
        var time = new StageTime(timeCode);
        ViewerStageCameraActivation activation =
            _cameraNavigation.CaptureStageCameraActivation(primPath, timeCode);
        var source = new ViewerSchedulerStageCameraSource(coordinator.Scheduler);
        ViewerStageCameraQueryResult result = await ViewerStageCameraQuery.QueryAsync(
            source,
            primPath,
            time,
            cancellationToken);
        if (result.Outcome != ViewerStageCameraQueryOutcome.Ready)
        {
            ShowCameraMessage(
                $"{result.Error ?? $"Stage camera '{primPath}' is unavailable."} " +
                "The camera stays Automatic.");
            return;
        }
        if (!_cameraNavigation.TryActivateStageCamera(
                activation,
                result.Snapshot,
                out CameraState camera))
        {
            return;
        }
        await coordinator.MutateStateAsync(
            state => state.WithTime(time).WithCamera(camera),
            cancellationToken);
        UpdateCameraStatus();
        ShowCameraMessage($"Using stage camera '{result.Snapshot.PrimPath}'.");
    }

    private async Task ReloadStageAsync(CancellationToken cancellationToken)
    {
        await _documentGate.WaitAsync(cancellationToken);
        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            CancellationToken documentToken =
                _documentLifetime?.Token ?? cancellationToken;
            if (coordinator is null)
            {
                return;
            }

            SetBusy($"Reloading {Path.GetFileName(_stagePath)}...");
            await coordinator.Scheduler.EditAsync(
                static stage =>
                {
                    stage.Reload();
                    return true;
                },
                UsdStageInvalidationKind.Full,
                documentToken);
            ViewerDocumentSnapshot document = await coordinator.Scheduler.InvokeAsync(
                stage => ViewerStageSnapshotBuilder.BuildDocument(
                    stage,
                    _layers,
                    _selectionState.PrimPath),
                documentToken);
            await ApplyDocumentRefreshAsync(coordinator, document, documentToken);
            SetReady($"Reloaded {_stagePath}");
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async Task StopCurrentDocumentAsync()
    {
        StopStormNavigationPolling();
        _cameraShortcutRepeat.Reset();
        EndCameraPointerGesture();
        if (_coordinator is { } stageChangeCoordinator)
        {
            stageChangeCoordinator.StageChanged -= OnStageChanged;
        }
        ViewerStageCameraRefreshPump? stageCameraRefreshes =
            _stageCameraRefreshes;
        _stageCameraRefreshes = null;
        if (stageCameraRefreshes is not null)
        {
            await stageCameraRefreshes.DisposeAsync();
        }
        ViewerCameraUpdatePump? cameraUpdates = _cameraUpdates;
        _cameraUpdates = null;
        UpdateCameraAvailability();
        if (cameraUpdates is not null)
        {
            await cameraUpdates.DisposeAsync();
        }
        await StopTimelineAsync();
        _pickLifetime?.Cancel();
        _selectionLifetime?.Cancel();
        _documentLifetime?.Cancel();
        if (_pickTask is not null)
        {
            await _pickTask;
        }
        if (_selectionTask is not null)
        {
            await _selectionTask;
        }
        if (_renderLoop is not null)
        {
            await _renderLoop;
        }
        if (_diagnosticSequence is not null)
        {
            await _diagnosticSequence;
        }
        if (_coordinator is not null)
        {
            _coordinator.StatusChanged -= OnRendererStatusChanged;
            await _coordinator.DisposeAsync();
        }

        _selectionLifetime?.Dispose();
        _documentLifetime?.Dispose();
        _selectionLifetime = null;
        _documentLifetime = null;
        _pickLifetime = null;
        _pickTask = null;
        _selectionTask = null;
        _renderLoop = null;
        _diagnosticSequence = null;
        _coordinator = null;
        _backendHost = null;
        _hierarchy = ViewerHierarchySnapshot.Empty;
        _timing = ViewerStageTimingSnapshot.Empty;
        _layers = ViewerLayerStackSnapshot.Empty;
        _stagePath = null;
        _pickPointer = null;
        _pickPointerDragged = false;
        _selectionState.TrySet(null, out _);
        _cameraNavigation.ResetToAutomatic();
        UpdateCameraStatus();
        lock (_timelineGate)
        {
            _currentTimeCode = 0;
        }
        Volatile.Write(ref _diagnosticOwnsRendering, 0);
        ClearDocumentUi();
    }

    private async Task LoadRecentStagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            RefreshRecentMenu(await _recentStageStore.LoadAsync(cancellationToken));
        }
        catch (IOException exception)
        {
            ShowError($"The recent-stage list could not be loaded: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowError($"The recent-stage list could not be loaded: {exception.Message}");
        }
    }

    private void RefreshRecentMenu(IReadOnlyList<string> paths)
    {
        RecentStagesMenu.ItemsSource = paths.Select(path =>
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(path),
                Tag = path
            };
            ToolTip.SetTip(item, path);
            item.Click += OnRecentStageClick;
            return item;
        }).ToArray();
        RecentStagesMenu.IsEnabled = paths.Count != 0;
    }

    private async void OnRecentStageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path })
        {
            await OpenStageAndReportAsync(path);
        }
    }

    private void OnHierarchyFilterChanged(object? sender, TextChangedEventArgs e) =>
        RenderHierarchy();

    private void OnHierarchyExpandDepthChanged(object? sender, TextChangedEventArgs e)
    {
        string? text = HierarchyExpandDepthInput.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _hierarchyExpandDepth = 0;
            RenderHierarchy();
            return;
        }
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int depth) &&
            depth >= 0)
        {
            _hierarchyExpandDepth = depth;
            HierarchyExpandDepthInput.Foreground = null;
            RenderHierarchy();
            return;
        }
        HierarchyExpandDepthInput.Foreground = Brushes.OrangeRed;
    }

    private void ShowStageSummary()
    {
        StageIdentity.Text = ViewerStageHudFormatter.FormatIdentity(_statistics);
        StageStatistics.Text = ViewerStageHudFormatter.FormatStatistics(
            _statistics,
            _timing,
            _latestDiagnostics);
    }

    private void RenderHierarchy()
    {
        ViewerHierarchySnapshot filtered = _hierarchy.Filter(new ViewerHierarchyFilter(
            HierarchyFilter.Text,
            HierarchyTypeFilter.Text));
        var source = new ViewerHierarchyTreeSource(filtered);
        TreeViewItem? selectedItem = null;
        _rebuildingHierarchy = true;
        try
        {
            StageHierarchy.ItemsSource = CreateTreeItems(source.Roots, ref selectedItem);
            StageHierarchy.IsVisible = source.Roots.Count != 0;
            HierarchyState.IsVisible = source.Roots.Count == 0;
            HierarchyState.Text = _hierarchy.Entries.Length == 0
                ? "The stage contains no traversable prims."
                : "No prims match the current filter.";
            if (selectedItem is not null)
            {
                StageHierarchy.SelectedItem = selectedItem;
            }
        }
        finally
        {
            _rebuildingHierarchy = false;
        }
    }

    private void RenderLayers()
    {
        _rebuildingLayers = true;
        try
        {
            LayersRows.Children.Clear();
            LayerEditTarget.Text = string.IsNullOrEmpty(_layers.EditTargetIdentifier)
                ? "Edit target: —"
                : $"Edit target: {_layers.EditTargetIdentifier}";
            RootEditWarning.Text = _rootLayerEditsExplicitlyEnabled
                ? "Root-layer prim edits are explicitly enabled for this document. " +
                    "They remain in memory and are never saved automatically."
                : "Prim controls author to the session layer by default. Root edits require " +
                    "the explicit button above and are never saved automatically.";
            LayersState.Text = _layers.Layers.Count == 0
                ? "The stage has no visible local layers."
                : "Strongest to weakest local layer order.";
            foreach (ViewerLayerSnapshot layer in _layers.Layers)
            {
                string role = layer.IsSession && layer.IsRoot
                    ? "Session, Root"
                    : layer.IsSession
                        ? "Session"
                        : layer.IsRoot
                            ? "Root"
                            : "Local";
                var row = new StackPanel
                {
                    Spacing = 3
                };
                row.Children.Add(new TextBlock
                {
                    FontWeight = layer.IsEditTarget
                        ? FontWeight.SemiBold
                        : FontWeight.Normal,
                    Text = $"{layer.StrengthIndex + 1}. {role}" +
                        (layer.IsEditTarget ? " · Edit target" : string.Empty)
                });
                row.Children.Add(new TextBlock
                {
                    Text = layer.Identifier,
                    TextWrapping = TextWrapping.Wrap
                });
                var muted = new CheckBox
                {
                    Content = "Muted",
                    IsChecked = layer.IsMuted,
                    IsEnabled = layer.CanChangeMuted,
                    Tag = layer
                };
                AutomationProperties.SetName(
                    muted,
                    $"Mute local layer {layer.Identifier}");
                muted.PropertyChanged += OnLayerMutedPropertyChanged;
                row.Children.Add(muted);
                LayersRows.Children.Add(row);
            }
        }
        finally
        {
            _rebuildingLayers = false;
        }
        UpdateLayerAvailability();
    }

    private void UpdateLayerAvailability()
    {
        bool interactiveDocument =
            !_documentBusy &&
            !_layerCommandBusy &&
            _coordinator is not null &&
            !IsAutomatedViewerRun();
        SetSessionEditTargetButton.IsEnabled =
            interactiveDocument &&
            (_rootLayerEditsExplicitlyEnabled ||
             !string.Equals(
                 _layers.EditTargetIdentifier,
                 _layers.SessionLayerIdentifier,
                 StringComparison.Ordinal));
        SetRootEditTargetButton.IsEnabled =
            interactiveDocument &&
            (!_rootLayerEditsExplicitlyEnabled ||
             !string.Equals(
                 _layers.EditTargetIdentifier,
                 _layers.RootLayerIdentifier,
                 StringComparison.Ordinal));
        foreach (Control control in LayersRows.Children)
        {
            if (control is StackPanel row)
            {
                foreach (Control child in row.Children)
                {
                    if (child is CheckBox { Tag: ViewerLayerSnapshot layer } muted)
                    {
                        muted.IsEnabled = interactiveDocument && layer.CanChangeMuted;
                    }
                }
            }
        }
    }

    private async void OnSetSessionEditTargetClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RunLayerCommandAsync(
                ViewerLayerCommand.SetSessionEditTarget,
                layerIdentifier: null,
                _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not set the session edit target: {exception.Message}");
        }
    }

    private async void OnSetRootEditTargetClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RunLayerCommandAsync(
                ViewerLayerCommand.SetRootEditTarget,
                layerIdentifier: null,
                _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not set the root edit target: {exception.Message}");
        }
    }

    private async void OnLayerMutedPropertyChanged(
        object? sender,
        Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ToggleButton.IsCheckedProperty ||
            _rebuildingLayers ||
            sender is not CheckBox
            {
                Tag: ViewerLayerSnapshot layer
            } muted)
        {
            return;
        }

        bool requestedMuted = muted.IsChecked == true;
        _rebuildingLayers = true;
        try
        {
            muted.IsChecked = layer.IsMuted;
        }
        finally
        {
            _rebuildingLayers = false;
        }

        try
        {
            await RunLayerCommandAsync(
                requestedMuted ? ViewerLayerCommand.Mute : ViewerLayerCommand.Unmute,
                layer.Identifier,
                _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError(
                $"Could not {(requestedMuted ? "mute" : "unmute")} " +
                $"'{layer.Identifier}': {exception.Message}");
            RenderLayers();
        }
    }

    private async Task RunLayerCommandAsync(
        ViewerLayerCommand command,
        string? layerIdentifier,
        CancellationToken cancellationToken)
    {
        await _documentGate.WaitAsync(cancellationToken);
        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            CancellationToken documentToken =
                _documentLifetime?.Token ?? cancellationToken;
            if (coordinator is null || IsAutomatedViewerRun())
            {
                return;
            }
            if (command is ViewerLayerCommand.Mute or ViewerLayerCommand.Unmute)
            {
                ViewerLayerSnapshot layer = _layers.Layers.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Identifier,
                        layerIdentifier,
                        StringComparison.Ordinal)) ??
                    throw new InvalidOperationException("The selected layer is no longer available.");
                if (!layer.CanChangeMuted)
                {
                    throw new InvalidOperationException(
                        "Root and session layers cannot be muted from the Viewer.");
                }
            }
            if (command == ViewerLayerCommand.SetRootEditTarget &&
                string.Equals(
                    _layers.EditTargetIdentifier,
                    _layers.RootLayerIdentifier,
                    StringComparison.Ordinal))
            {
                _rootLayerEditsExplicitlyEnabled = true;
                RenderLayers();
                ViewerStatus.Text =
                    "Root-layer prim edits are explicitly enabled in memory; they will not be saved.";
                return;
            }
            if (command == ViewerLayerCommand.SetSessionEditTarget &&
                string.Equals(
                    _layers.EditTargetIdentifier,
                    _layers.SessionLayerIdentifier,
                    StringComparison.Ordinal))
            {
                _rootLayerEditsExplicitlyEnabled = false;
                RenderLayers();
                ViewerStatus.Text = "Session-layer prim edits are enabled.";
                return;
            }

            _layerCommandBusy = true;
            UpdateLayerAvailability();
            ViewerLayerStackSnapshot previousLayers = _layers;
            bool previousRootEditPolicy = _rootLayerEditsExplicitlyEnabled;
            try
            {
                UsdStageInvalidationKind invalidation =
                    command is ViewerLayerCommand.Mute or ViewerLayerCommand.Unmute
                        ? UsdStageInvalidationKind.Composition
                        : UsdStageInvalidationKind.Property;
                ViewerDocumentSnapshot document = await coordinator.Scheduler.EditAsync(
                    stage =>
                    {
                        switch (command)
                        {
                            case ViewerLayerCommand.SetSessionEditTarget:
                                stage.SetEditTargetToSessionLayer();
                                break;
                            case ViewerLayerCommand.SetRootEditTarget:
                                stage.SetEditTargetToRootLayer();
                                break;
                            case ViewerLayerCommand.Mute:
                                stage.MuteLayer(layerIdentifier!);
                                break;
                            case ViewerLayerCommand.Unmute:
                                stage.UnmuteLayer(layerIdentifier!);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(command));
                        }
                        return ViewerStageSnapshotBuilder.BuildDocument(
                            stage,
                            previousLayers,
                            _selectionState.PrimPath);
                    },
                    invalidation,
                    documentToken);
                await ApplyDocumentRefreshAsync(coordinator, document, documentToken);
                if (command == ViewerLayerCommand.SetSessionEditTarget)
                {
                    _rootLayerEditsExplicitlyEnabled = false;
                }
                else if (command == ViewerLayerCommand.SetRootEditTarget)
                {
                    _rootLayerEditsExplicitlyEnabled = true;
                }
                RenderLayers();
                ViewerStatus.Text = command switch
                {
                    ViewerLayerCommand.SetSessionEditTarget => "Session layer is the edit target.",
                    ViewerLayerCommand.SetRootEditTarget => "Root layer is the edit target.",
                    ViewerLayerCommand.Mute => $"Muted {layerIdentifier}.",
                    ViewerLayerCommand.Unmute => $"Unmuted {layerIdentifier}.",
                    _ => ViewerStatus.Text
                };
            }
            catch (OperationCanceledException) when (documentToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception operationFailure)
            {
                _rootLayerEditsExplicitlyEnabled = previousRootEditPolicy;
                try
                {
                    ViewerDocumentSnapshot document = await coordinator.Scheduler.InvokeAsync(
                        stage => ViewerStageSnapshotBuilder.BuildDocument(
                            stage,
                            previousLayers,
                            _selectionState.PrimPath),
                        documentToken);
                    await ApplyDocumentRefreshAsync(coordinator, document, documentToken);
                }
                catch (Exception refreshFailure)
                {
                    throw new AggregateException(
                        "The layer command failed and the Viewer could not refresh the stage.",
                        operationFailure,
                        refreshFailure);
                }
                throw;
            }
            finally
            {
                _layerCommandBusy = false;
                UpdateLayerAvailability();
            }
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async Task ApplyDocumentRefreshAsync(
        ViewerRenderCoordinator coordinator,
        ViewerDocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        bool timingChanged = _timing != document.Timing;
        if (timingChanged)
        {
            await StopTimelineAsync();
        }
        ViewerDocumentRefreshPlan plan = ViewerDocumentRefreshPlan.Create(
            _timing,
            document,
            _selectionState.PrimPath,
            coordinator.CurrentState.Time.TimeCode);
        _hierarchy = document.Hierarchy;
        _timing = document.Timing;
        _layers = document.Layers;

        if (!plan.SelectionSurvives &&
            _selectionState.TrySet(null, out SelectionState selection))
        {
            _selectionLifetime?.Cancel();
            _selectionLifetime?.Dispose();
            _selectionLifetime = null;
            try
            {
                await coordinator.MutateStateAsync(
                    state => state.WithSelection(selection),
                    cancellationToken);
            }
            catch
            {
                _selectionState.Synchronize(coordinator.CurrentState.Selection);
                throw;
            }
            ResetInspector();
        }
        if (timingChanged)
        {
            if (!IsAutomatedViewerRun() && plan.RequiresTimeUpdate)
            {
                await coordinator.MutateStateAsync(
                    state => state.WithTime(new StageTime(plan.PreservedTimeCode)),
                    cancellationToken);
            }
            await InitializeTimelineAsync(coordinator, _timing, cancellationToken);
        }
        RenderHierarchy();
        RenderLayers();
        ShowStageSummary();
        await RefreshValidationAsync(coordinator, cancellationToken);
        if (_selectionState.PrimPath is { } selectedPrimPath)
        {
            if (document.SelectedPrim is { } selectedPrim &&
                string.Equals(selectedPrim.Path, selectedPrimPath, StringComparison.Ordinal))
            {
                ShowInspector(selectedPrim);
            }
            else
            {
                await StartInspectorLoadAsync(selectedPrimPath, cancellationToken);
            }
        }
        _ = TryQueueStageCameraRefresh(
            coordinator.CurrentState.Time.TimeCode,
            applyTime: false);
    }

    private TreeViewItem CreateTreeItem(
        ViewerHierarchyTreeNode node,
        ref TreeViewItem? selectedItem)
    {
        var item = new TreeViewItem
        {
            Header = node.Entry.Name,
            Tag = node
        };
        AutomationProperties.SetName(item, $"Prim {node.Entry.Path}");
        ToolTip.SetTip(item, node.Entry.Path);
        string? selectedPrimPath = _selectionState.PrimPath;
        bool containsSelection = selectedPrimPath is not null &&
            (selectedPrimPath == node.Entry.Path ||
             selectedPrimPath.StartsWith(
                 string.Concat(node.Entry.Path, "/"),
                 StringComparison.Ordinal));
        if (node.Entry.ChildCount != 0)
        {
            if (ViewerHierarchyExpansionPolicy.ShouldMaterializeChildren(
                node.Entry,
                _hierarchyExpandDepth,
                containsSelection))
            {
                item.ItemsSource = CreateTreeItems(node.Children, ref selectedItem);
                item.IsExpanded = true;
            }
            else
            {
                item.ItemsSource = new[] { new TreeViewItem { Header = "…" } };
                item.Expanded += OnTreeItemExpanded;
            }
        }
        if (selectedPrimPath == node.Entry.Path)
        {
            selectedItem = item;
        }
        return item;
    }

    private TreeViewItem[] CreateTreeItems(
        IReadOnlyList<ViewerHierarchyTreeNode> nodes,
        ref TreeViewItem? selectedItem)
    {
        var items = new TreeViewItem[nodes.Count];
        for (int index = 0; index < nodes.Count; index++)
        {
            items[index] = CreateTreeItem(nodes[index], ref selectedItem);
        }
        return items;
    }

    private void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem
            {
                Tag: ViewerHierarchyTreeNode node
            } item ||
            node.IsChildrenMaterialized)
        {
            return;
        }
        TreeViewItem? selectedItem = null;
        item.ItemsSource = CreateTreeItems(node.Children, ref selectedItem);
        item.Expanded -= OnTreeItemExpanded;
    }

    private async void OnHierarchySelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (StageHierarchy.SelectedItem is not TreeViewItem
            {
                Tag: ViewerHierarchyTreeNode node
            })
        {
            if (!_rebuildingHierarchy)
            {
                await ClearSelectionAsync();
            }
            return;
        }
        SelectionItem? previousSelection = _selectionState.Item;
        if (!_selectionState.TrySet(node.Entry.Path, out SelectionState selection))
        {
            return;
        }

        CancellationToken documentToken =
            _documentLifetime?.Token ?? _viewerLifetime.Token;
        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            if (coordinator is null)
            {
                _selectionState.Restore(previousSelection);
                return;
            }
            await coordinator.MutateStateAsync(
                state => state.WithSelection(selection),
                documentToken);
            if (string.Equals(
                _selectionState.PrimPath,
                node.Entry.Path,
                StringComparison.Ordinal))
            {
                _currentInspector = null;
                UpdateCameraAvailability();
                await StartInspectorLoadAsync(node.Entry.Path, documentToken);
            }
        }
        catch (OperationCanceledException) when (documentToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_coordinator is { } coordinator)
            {
                _selectionState.Synchronize(coordinator.CurrentState.Selection);
            }
            else
            {
                _selectionState.Restore(previousSelection);
            }
            ShowError($"Could not select '{node.Entry.Path}': {exception.Message}");
        }
    }

    private async Task ClearSelectionAsync()
    {
        SelectionItem? previousSelection = _selectionState.Item;
        if (!_selectionState.TrySet(null, out SelectionState selection))
        {
            return;
        }
        _selectionLifetime?.Cancel();
        ResetInspector();
        ViewerRenderCoordinator? coordinator = _coordinator;
        CancellationToken cancellationToken =
            _documentLifetime?.Token ?? _viewerLifetime.Token;
        try
        {
            if (coordinator is not null)
            {
                await coordinator.MutateStateAsync(
                    state => state.WithSelection(selection),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (coordinator is not null)
            {
                _selectionState.Synchronize(coordinator.CurrentState.Selection);
            }
            else
            {
                _selectionState.Restore(previousSelection);
            }
            ShowError($"Could not clear the selection: {exception.Message}");
        }
    }

    private async Task StartInspectorLoadAsync(
        string primPath,
        CancellationToken documentToken)
    {
        _selectionLifetime?.Cancel();
        _selectionLifetime?.Dispose();
        _selectionLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(documentToken);
        CancellationTokenSource selectionLifetime = _selectionLifetime;
        _selectionTask = LoadInspectorAsync(
            primPath,
            selectionLifetime,
            selectionLifetime.Token);
        await _selectionTask;
    }

    private async Task LoadInspectorAsync(
        string primPath,
        CancellationTokenSource selectionLifetime,
        CancellationToken cancellationToken)
    {
        try
        {
            InspectorRows.Children.Clear();
            CompositionRows.Children.Clear();
            InspectorRows.Children.Add(new TextBlock
            {
                Text = $"Loading {primPath}..."
            });
            CompositionRows.Children.Add(new TextBlock
            {
                Text = $"Loading composition for {primPath}...",
                TextWrapping = TextWrapping.Wrap
            });
            ViewerRenderCoordinator? coordinator = _coordinator;
            if (coordinator is null)
            {
                return;
            }
            ViewerPrimInspectorSnapshot inspector = await coordinator.Scheduler.InvokeAsync(
                stage => ViewerStageSnapshotBuilder.BuildInspector(stage, primPath),
                cancellationToken);
            if (!ReferenceEquals(selectionLifetime, _selectionLifetime) ||
                cancellationToken.IsCancellationRequested)
            {
                return;
            }
            ShowInspector(inspector);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(selectionLifetime, _selectionLifetime))
            {
                ShowError($"Could not inspect '{primPath}': {exception.Message}");
                InspectorRows.Children.Clear();
                CompositionRows.Children.Clear();
                InspectorRows.Children.Add(new TextBlock
                {
                    FontWeight = FontWeight.SemiBold,
                    Text = $"Inspection failed: {exception.Message}",
                    TextWrapping = TextWrapping.Wrap
                });
                CompositionRows.Children.Add(new TextBlock
                {
                    Text = $"Composition inspection failed: {exception.Message}",
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
    }

    private void ShowInspector(ViewerPrimInspectorSnapshot inspector)
    {
        _currentInspector = inspector;
        _diagnostics.AddUnsupported(inspector.UnsupportedFeatures);
        _rebuildingInspector = true;
        try
        {
            InspectorRows.Children.Clear();
            CompositionRows.Children.Clear();
            AddInspectorHeading("Prim identity");
            AddInspectorRow("Path", inspector.Path);
            AddInspectorRow(
                "Type",
                string.IsNullOrEmpty(inspector.TypeName) ? "<untyped>" : inspector.TypeName);
            AddInspectorRow("Instance", inspector.IsInstance.ToString());
            AddInspectorRow("Prototype", inspector.IsPrototype.ToString());
            AddInspectorRow(
                "Prototype path",
                string.IsNullOrEmpty(inspector.PrototypePath)
                    ? "<none>"
                    : inspector.PrototypePath);
            if (_selectionState.Item is { } selectedItem &&
                string.Equals(
                    selectedItem.PrimPath,
                    inspector.Path,
                    StringComparison.Ordinal))
            {
                AddInspectorHeading("Selected item");
                AddInspectorRow("Selected prim", selectedItem.PrimPath);
                if (selectedItem.InstancerPath is { } instancerPath)
                {
                    AddInspectorRow("Instancer", instancerPath);
                    AddInspectorRow(
                        "Instance index",
                        selectedItem.InstanceIndex!.Value.ToString(
                            CultureInfo.InvariantCulture));
                }
                if (selectedItem.ElementIndex is { } elementIndex)
                {
                    AddInspectorRow(
                        "Subprim element",
                        elementIndex.ToString(CultureInfo.InvariantCulture));
                }
            }
            AddInspectorRow(
                "Applied schemas",
                inspector.AppliedSchemas.Length == 0
                    ? "<none>"
                    : string.Join(", ", inspector.AppliedSchemas));

            AddInspectorHeading("Session controls");
            AddInspectorButtonRow(
                "Active",
                inspector.IsActive.ToString(),
                inspector.IsActive ? "Deactivate" : "Activate",
                new ViewerPrimCommandRequest(
                    ViewerPrimCommand.SetActive,
                    BooleanValue: !inspector.IsActive),
                inspector);
            AddInspectorButtonRow(
                "Load state",
                inspector.IsLoaded ? "Loaded" : "Unloaded",
                inspector.IsLoaded ? "Unload" : "Load",
                new ViewerPrimCommandRequest(
                    ViewerPrimCommand.SetLoaded,
                    BooleanValue: !inspector.IsLoaded),
                inspector);
            AddInspectorButtonRow(
                "Instanceable",
                inspector.IsInstanceable.ToString(),
                inspector.IsInstanceable ? "Clear instanceable" : "Make instanceable",
                new ViewerPrimCommandRequest(
                    ViewerPrimCommand.SetInstanceable,
                    BooleanValue: !inspector.IsInstanceable),
                inspector);
            if (inspector.IsImageable)
            {
                AddInspectorTokenRow(
                    "Visibility",
                    inspector.Visibility?.ToString() ?? "<unknown>",
                    Enum.GetValues<UsdGeomVisibility>()
                        .Select(value => value.ToString())
                        .ToArray(),
                    new ViewerPrimCommandRequest(
                        ViewerPrimCommand.SetVisibility,
                        TokenValue: inspector.Visibility?.ToString()),
                    inspector);
                AddInspectorTokenRow(
                    "Purpose",
                    inspector.Purpose?.ToString() ?? "<unknown>",
                    Enum.GetValues<UsdGeomPurpose>()
                        .Select(value => value.ToString())
                        .ToArray(),
                    new ViewerPrimCommandRequest(
                        ViewerPrimCommand.SetPurpose,
                        TokenValue: inspector.Purpose?.ToString()),
                    inspector);
            }

            AddInspectorHeading($"Variant sets ({inspector.VariantSets.Length})");
            if (inspector.VariantSets.Length == 0)
            {
                AddInspectorRow("Variant sets", "<none>");
            }
            foreach (ViewerVariantSetSnapshot variantSet in inspector.VariantSets)
            {
                AddInspectorVariantRow(variantSet, inspector);
            }

            AddInspectorHeading($"Payload arcs ({inspector.PayloadArcs.Length})");
            if (inspector.PayloadArcs.Length == 0)
            {
                AddInspectorRow("Payload arcs", "<none>");
            }
            for (int index = 0; index < inspector.PayloadArcs.Length; index++)
            {
                ViewerPayloadArcSnapshot payloadArc = inspector.PayloadArcs[index];
                string label = $"Payload {index + 1}";
                AddInspectorRow(
                    $"{label} asset",
                    ViewerPayloadArcFormatter.FormatAssetPath(payloadArc.AssetPath));
                AddInspectorRow(
                    $"{label} target",
                    ViewerPayloadArcFormatter.FormatTargetPrimPath(payloadArc.TargetPrimPath));
                AddInspectorRow(
                    $"{label} source",
                    ViewerPayloadArcFormatter.FormatSourceLayerIdentifier(
                        payloadArc.SourceLayerIdentifier));
            }

            AddInspectorHeading("Composition");
            AddInspectorRow("Prim index", ViewerCompositionFormatter.FormatSummary(inspector.Composition));
            AddCompositionHeading("Pcp prim index");
            AddCompositionRow("Summary", ViewerCompositionFormatter.FormatSummary(inspector.Composition));
            for (int index = 0; index < inspector.Composition.Nodes.Count; index++)
            {
                string nodeText = ViewerCompositionFormatter.FormatNode(
                    inspector.Composition.Nodes[index],
                    index);
                AddInspectorRow("Pcp node", nodeText);
                AddCompositionRow("Node", nodeText);
            }
            foreach (string error in inspector.Composition.Errors)
            {
                AddInspectorRow("Pcp error", error);
                AddCompositionRow("Error", error);
            }

            AddInspectorHeading($"Attributes ({inspector.Attributes.Length})");
            foreach (ViewerAttributeSnapshot attribute in inspector.Attributes)
            {
                AddInspectorRow(
                    attribute.Name,
                    $"{attribute.TypeName}; authored={attribute.HasAuthoredValue}; " +
                    $"blocked={attribute.IsBlocked}; samples={attribute.TimeSampleCount}; " +
                    $"value={attribute.Value}");
            }
            AddInspectorHeading($"Relationships ({inspector.Relationships.Length})");
            foreach (ViewerRelationshipSnapshot relationship in inspector.Relationships)
            {
                AddInspectorRow(
                    relationship.Name,
                    string.IsNullOrEmpty(relationship.Targets)
                        ? "<no targets>"
                        : relationship.Targets);
            }
            AddInspectorHeading($"Unsupported ({inspector.UnsupportedFeatures.Length})");
            foreach (ViewerUnsupportedFeature unsupported in inspector.UnsupportedFeatures)
            {
                AddInspectorRow(unsupported.Code, unsupported.Message);
            }
        }
        finally
        {
            _rebuildingInspector = false;
        }
        RenderDiagnostics();
        UpdateCameraAvailability();
    }

    private void AddInspectorHeading(string text) =>
        InspectorRows.Children.Add(new TextBlock
        {
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            FontWeight = FontWeight.SemiBold,
            Text = text
        });

    private void AddCompositionHeading(string text) =>
        CompositionRows.Children.Add(new TextBlock
        {
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            FontWeight = FontWeight.SemiBold,
            Text = text
        });

    private void AddInspectorRow(string name, string value) =>
        InspectorRows.Children.Add(new TextBlock
        {
            Text = $"{name}: {ViewerScalarFormatter.Bound(value, 512)}",
            TextWrapping = TextWrapping.Wrap
        });

    private void AddCompositionRow(string name, string value) =>
        CompositionRows.Children.Add(new TextBlock
        {
            Text = $"{name}: {ViewerScalarFormatter.Bound(value, 512)}",
            TextWrapping = TextWrapping.Wrap
        });

    private void AddInspectorButtonRow(
        string name,
        string value,
        string action,
        ViewerPrimCommandRequest request,
        ViewerPrimInspectorSnapshot inspector)
    {
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };
        row.Children.Add(new TextBlock
        {
            Text = $"{name}: {value}",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        var button = new Button
        {
            Content = action,
            Tag = request,
            IsEnabled = CanRunPrimCommand(request.Command, inspector)
        };
        AutomationProperties.SetName(button, $"{action} prim {inspector.Path}");
        button.Click += OnPrimCommandClick;
        row.Children.Add(button);
        InspectorRows.Children.Add(row);
    }

    private void AddInspectorTokenRow(
        string name,
        string currentValue,
        string[] values,
        ViewerPrimCommandRequest request,
        ViewerPrimInspectorSnapshot inspector)
    {
        var row = new StackPanel
        {
            Spacing = 3
        };
        row.Children.Add(new TextBlock
        {
            Text = name
        });
        var selector = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = currentValue,
            Tag = request,
            IsEnabled = CanRunPrimCommand(request.Command, inspector)
        };
        AutomationProperties.SetName(selector, $"{name} for prim {inspector.Path}");
        selector.SelectionChanged += OnPrimTokenSelectionChanged;
        row.Children.Add(selector);
        InspectorRows.Children.Add(row);
    }

    private void AddInspectorVariantRow(
        ViewerVariantSetSnapshot variantSet,
        ViewerPrimInspectorSnapshot inspector)
    {
        string displayName = string.IsNullOrEmpty(variantSet.Name)
            ? "<empty variant-set name>"
            : variantSet.Name;
        string currentSelection = variantSet.Selection is null
            ? "<no selection>"
            : string.IsNullOrEmpty(variantSet.Selection)
                ? "<empty variant name>"
                : variantSet.Selection;
        var request = new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetVariantSelection,
            TokenValue: variantSet.Selection,
            VariantSetName: variantSet.Name,
            AvailableVariantNames: variantSet.VariantNames.ToArray());
        ViewerVariantSelectionOption[] options =
            ViewerVariantSelectionOption.Create(variantSet);
        var row = new StackPanel
        {
            Spacing = 3
        };
        row.Children.Add(new TextBlock
        {
            Text = $"{displayName} · current: {currentSelection} · " +
                $"available: {variantSet.VariantNames.Count}",
            TextWrapping = TextWrapping.Wrap
        });
        var selector = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(option =>
                string.Equals(
                    option.Selection,
                    variantSet.Selection,
                    StringComparison.Ordinal)),
            Tag = request,
            IsEnabled = !string.IsNullOrWhiteSpace(variantSet.Name) &&
                (variantSet.VariantNames.Count != 0 || variantSet.Selection is not null) &&
                CanRunPrimCommand(request.Command, inspector)
        };
        AutomationProperties.SetName(
            selector,
            $"Variant set {displayName} selection for prim {inspector.Path}");
        ToolTip.SetTip(
            selector,
            "Choose an available variant or the explicit no-selection option.");
        selector.SelectionChanged += OnVariantSelectionChanged;
        row.Children.Add(selector);
        InspectorRows.Children.Add(row);
    }

    private bool CanRunPrimCommand(
        ViewerPrimCommand command,
        ViewerPrimInspectorSnapshot inspector) =>
        ViewerSessionCommandPolicy.CanExecute(
            command,
            new ViewerPrimCommandContext(
                HasDocument: _coordinator is not null,
                IsBusy: _documentBusy || _primCommandBusy,
                IsAutomated: IsAutomatedViewerRun(),
                HasSelection: string.Equals(
                    _selectionState.PrimPath,
                    inspector.Path,
                    StringComparison.Ordinal),
                inspector.IsImageable,
                inspector.IsInstance,
                inspector.IsPrototype));

    private async void OnPrimCommandClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ViewerPrimCommandRequest request })
        {
            return;
        }
        try
        {
            await RunPrimCommandAsync(request, _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not edit the selected prim: {exception.Message}");
            if (_currentInspector is { } inspector)
            {
                ShowInspector(inspector);
            }
        }
    }

    private async void OnPrimTokenSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_rebuildingInspector ||
            sender is not ComboBox
            {
                Tag: ViewerPrimCommandRequest request,
                SelectedItem: string selected
            })
        {
            return;
        }

        string? current = request.TokenValue;
        _rebuildingInspector = true;
        try
        {
            ((ComboBox)sender).SelectedItem = current;
        }
        finally
        {
            _rebuildingInspector = false;
        }
        if (string.Equals(current, selected, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await RunPrimCommandAsync(
                request with { TokenValue = selected },
                _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not edit the selected prim: {exception.Message}");
            if (_currentInspector is { } inspector)
            {
                ShowInspector(inspector);
            }
        }
    }

    private async void OnVariantSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_rebuildingInspector ||
            sender is not ComboBox
            {
                Tag: ViewerPrimCommandRequest request,
                SelectedItem: ViewerVariantSelectionOption selected,
                ItemsSource: IEnumerable<ViewerVariantSelectionOption> options
            } selector ||
            request.Command != ViewerPrimCommand.SetVariantSelection)
        {
            return;
        }

        string? current = request.TokenValue;
        _rebuildingInspector = true;
        try
        {
            selector.SelectedItem = options.FirstOrDefault(option =>
                string.Equals(option.Selection, current, StringComparison.Ordinal));
        }
        finally
        {
            _rebuildingInspector = false;
        }
        if (string.Equals(current, selected.Selection, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await RunPrimCommandAsync(
                request with { TokenValue = selected.Selection },
                _viewerLifetime.Token);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError($"Could not change the variant selection: {exception.Message}");
            if (_currentInspector is { } inspector)
            {
                ShowInspector(inspector);
            }
            FocusVariantSelector(request.VariantSetName);
        }
    }

    private void FocusVariantSelector(string? variantSetName)
    {
        foreach (Control control in InspectorRows.Children)
        {
            if (control is not StackPanel row)
            {
                continue;
            }
            foreach (Control child in row.Children)
            {
                if (child is ComboBox
                    {
                        Tag: ViewerPrimCommandRequest
                        {
                            Command: ViewerPrimCommand.SetVariantSelection
                        } request
                    } selector &&
                    string.Equals(
                        request.VariantSetName,
                        variantSetName,
                        StringComparison.Ordinal))
                {
                    if (selector.Focus())
                    {
                        return;
                    }
                }
            }
        }
        _ = StageHierarchy.Focus();
    }

    private async Task RunPrimCommandAsync(
        ViewerPrimCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        await _documentGate.WaitAsync(cancellationToken);
        try
        {
            ViewerRenderCoordinator? coordinator = _coordinator;
            ViewerPrimInspectorSnapshot? inspector = _currentInspector;
            string? primPath = _selectionState.PrimPath;
            CancellationToken documentToken =
                _documentLifetime?.Token ?? cancellationToken;
            if (coordinator is null ||
                inspector is null ||
                primPath is null ||
                !string.Equals(inspector.Path, primPath, StringComparison.Ordinal) ||
                !CanRunPrimCommand(request.Command, inspector))
            {
                return;
            }

            _primCommandBusy = true;
            ShowInspector(inspector);
            ViewerLayerStackSnapshot previousLayers = _layers;
            ViewerSessionEditTarget target = ViewerSessionCommandPolicy.ResolveEditTarget(
                _rootLayerEditsExplicitlyEnabled);
            try
            {
                ViewerDocumentSnapshot document = await coordinator.Scheduler.EditAsync(
                    stage =>
                    {
                        string previousTarget = stage.EditTargetLayerIdentifier;
                        try
                        {
                            SetSessionCommandEditTarget(stage, target);
                            ApplyPrimCommand(stage.GetPrim(primPath), request);
                            return ViewerStageSnapshotBuilder.BuildDocument(
                                stage,
                                previousLayers,
                                primPath);
                        }
                        catch
                        {
                            RestoreEditTarget(stage, previousTarget);
                            throw;
                        }
                    },
                    ViewerSessionCommandPolicy.GetInvalidation(request.Command),
                    documentToken);
                await ApplyDocumentRefreshAsync(coordinator, document, documentToken);
                ViewerStatus.Text = request.Command switch
                {
                    ViewerPrimCommand.SetLoaded =>
                        "Stage load rules changed; load/unload is not layer-authored.",
                    _ when target == ViewerSessionEditTarget.ExplicitRoot =>
                        "Prim edit authored to the root layer in memory; it was not saved.",
                    _ => "Prim edit authored to the session layer."
                };
            }
            catch (OperationCanceledException) when (documentToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception operationFailure)
            {
                try
                {
                    ViewerDocumentSnapshot document = await coordinator.Scheduler.InvokeAsync(
                        stage => ViewerStageSnapshotBuilder.BuildDocument(
                            stage,
                            previousLayers,
                            primPath),
                        documentToken);
                    await ApplyDocumentRefreshAsync(coordinator, document, documentToken);
                }
                catch (Exception refreshFailure)
                {
                    throw new AggregateException(
                        "The prim edit failed and the Viewer could not refresh the stage.",
                        operationFailure,
                        refreshFailure);
                }
                throw;
            }
            finally
            {
                _primCommandBusy = false;
                if (_currentInspector is { } current)
                {
                    ShowInspector(current);
                }
                if (request.Command == ViewerPrimCommand.SetVariantSelection)
                {
                    FocusVariantSelector(request.VariantSetName);
                }
            }
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private static void SetSessionCommandEditTarget(
        UsdStage stage,
        ViewerSessionEditTarget target)
    {
        if (target == ViewerSessionEditTarget.ExplicitRoot)
        {
            stage.SetEditTargetToRootLayer();
        }
        else
        {
            stage.SetEditTargetToSessionLayer();
        }
    }

    private static void RestoreEditTarget(UsdStage stage, string previousTarget)
    {
        if (string.Equals(
            previousTarget,
            stage.RootLayerIdentifier,
            StringComparison.Ordinal))
        {
            stage.SetEditTargetToRootLayer();
        }
        else
        {
            stage.SetEditTargetToSessionLayer();
        }
    }

    private static void ApplyPrimCommand(
        UsdPrim prim,
        ViewerPrimCommandRequest request)
    {
        switch (request.Command)
        {
            case ViewerPrimCommand.SetActive:
                prim.SetActive(request.BooleanValue!.Value);
                break;
            case ViewerPrimCommand.SetLoaded:
                if (request.BooleanValue!.Value)
                {
                    prim.Load();
                }
                else
                {
                    prim.Unload();
                }
                break;
            case ViewerPrimCommand.SetInstanceable:
                prim.SetInstanceable(request.BooleanValue!.Value);
                break;
            case ViewerPrimCommand.SetVisibility:
                UsdGeomImageable.Wrap(prim).SetVisibility(request.TokenValue switch
                {
                    "Inherited" => UsdGeomVisibility.Inherited,
                    "Invisible" => UsdGeomVisibility.Invisible,
                    _ => throw new ArgumentOutOfRangeException(nameof(request))
                });
                break;
            case ViewerPrimCommand.SetPurpose:
                UsdGeomImageable.Wrap(prim).SetPurpose(request.TokenValue switch
                {
                    "Default" => UsdGeomPurpose.Default,
                    "Render" => UsdGeomPurpose.Render,
                    "Proxy" => UsdGeomPurpose.Proxy,
                    "Guide" => UsdGeomPurpose.Guide,
                    _ => throw new ArgumentOutOfRangeException(nameof(request))
                });
                break;
            case ViewerPrimCommand.SetVariantSelection:
                ApplyVariantSelection(prim, request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static void ApplyVariantSelection(
        UsdPrim prim,
        ViewerPrimCommandRequest request)
    {
        string variantSetName = request.VariantSetName!;
        if (!prim.GetVariantSetNames().Contains(variantSetName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Variant set '{variantSetName}' is no longer available.");
        }
        string[] availableVariantNames = prim.GetVariantNames(variantSetName);
        if (request.TokenValue is { } selection &&
            !availableVariantNames.Contains(selection, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Variant '{selection}' is no longer available in set '{variantSetName}'.");
        }
        prim.SetVariantSelection(variantSetName, request.TokenValue);
    }

    private void SetBusy(string status)
    {
        _documentBusy = true;
        ViewerStatus.Text = status;
        ViewerStatus.Foreground = null;
        HierarchyState.Text = status;
        HierarchyState.IsVisible = true;
        OpenStageButton.IsEnabled = false;
        OpenStageMenuItem.IsEnabled = false;
        ReloadStageButton.IsEnabled = false;
        ReloadStageMenuItem.IsEnabled = false;
        UpdateCameraAvailability();
        UpdateTimelineAvailability();
        UpdateLayerAvailability();
        UpdateViewportDisplayAvailability();
        RenderValidation();
        if (_currentInspector is { } inspector)
        {
            ShowInspector(inspector);
        }
    }

    private void SetReady(string status)
    {
        _documentBusy = false;
        ViewerStatus.Text = status;
        ViewerStatus.Foreground = null;
        OpenStageButton.IsEnabled = true;
        OpenStageMenuItem.IsEnabled = true;
        ReloadStageButton.IsEnabled = _coordinator is not null;
        ReloadStageMenuItem.IsEnabled = _coordinator is not null;
        UpdateCameraAvailability();
        UpdateTimelineAvailability();
        UpdateLayerAvailability();
        ApplyViewportDisplayState(_coordinator?.CurrentState ?? StageRenderState.Default);
        RenderValidation();
        if (_currentInspector is { } inspector)
        {
            ShowInspector(inspector);
        }
        ViewerStartupOptions.WriteStatus(status);
    }

    private void ShowError(string status)
    {
        _documentBusy = false;
        ViewerStatus.Text = $"Error: {status}";
        ViewerStatus.Foreground = null;
        OpenStageButton.IsEnabled = true;
        OpenStageMenuItem.IsEnabled = true;
        ReloadStageButton.IsEnabled = _coordinator is not null;
        ReloadStageMenuItem.IsEnabled = _coordinator is not null;
        UpdateCameraAvailability();
        UpdateTimelineAvailability();
        UpdateLayerAvailability();
        UpdateViewportDisplayAvailability();
        RenderValidation();
        if (_currentInspector is { } inspector)
        {
            ShowInspector(inspector);
        }
        ViewerStartupOptions.WriteStatus(status);
    }

    private void ClearDocumentUi()
    {
        StageStatus.Text = "No stage loaded";
        _statistics = ViewerStageStatisticsSnapshot.Empty;
        _validation = ViewerValidationSnapshot.Empty;
        _currentInspector = null;
        _rootLayerEditsExplicitlyEnabled = false;
        ShowStageSummary();
        OpenStageButton.IsEnabled = !_documentBusy;
        OpenStageMenuItem.IsEnabled = !_documentBusy;
        ReloadStageButton.IsEnabled = false;
        ReloadStageMenuItem.IsEnabled = false;
        UpdateCameraAvailability();
        StageHierarchy.ItemsSource = null;
        StageHierarchy.IsVisible = false;
        HierarchyState.Text = "Open or drop a USD stage to inspect its prims.";
        HierarchyState.IsVisible = true;
        ResetTimelineUi();
        UpdateTimelineAvailability();
        ResetInspector();
        RenderLayers();
        RenderDiagnostics();
        RenderValidation();
    }

    private void ResetTimelineUi()
    {
        _updatingTimelineUi = true;
        try
        {
            CurrentTimeInput.Text = string.Empty;
            CurrentTimeInput.Foreground = null;
            TimelineSlider.Minimum = 0;
            TimelineSlider.Maximum = 1;
            TimelineSlider.Value = 0;
            TimelineStart.Text = "Start: —";
            TimelineEndAndRate.Text = "End: — · FPS: — · TCPS: —";
            TimelineDiagnostic.Text = string.Empty;
            PlayPauseButton.Content = "_Play";
            AutomationProperties.SetName(PlayPauseButton, "Play timeline");
        }
        finally
        {
            _updatingTimelineUi = false;
        }
    }

    private void ResetInspector()
    {
        _currentInspector = null;
        UpdateCameraAvailability();
        InspectorRows.Children.Clear();
        CompositionRows.Children.Clear();
        InspectorRows.Children.Add(new TextBlock
        {
            Text = _coordinator is null
                ? "Open a USD stage, then select a prim to inspect it."
                : "Select a prim to inspect metadata, variants, payloads, and session controls.",
            TextWrapping = TextWrapping.Wrap
        });
        CompositionRows.Children.Add(new TextBlock
        {
            Text = _coordinator is null
                ? "Open a USD stage, then select a prim to inspect composition."
                : "Select a prim to inspect its Pcp prim index.",
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static bool IsUsdStagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        string extension = Path.GetExtension(path);
        return extension.Equals(".usd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".usda", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".usdc", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".usdz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAutomatedViewerRun() =>
        ViewerStartupOptions.LiveEditSmoke ||
        ViewerStartupOptions.PickSmokeEnabled ||
        ViewerStartupOptions.SmokeSwitchBackend is not null ||
        ViewerStartupOptions.SwitchSoakCount > 0 ||
        ViewerStartupOptions.SwitchingEvidenceEnabled;

    private async Task RunDiagnosticSequenceAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        StageRenderState stateBeforeEdit = coordinator.CurrentState;
        if (ViewerStartupOptions.LiveEditSmoke)
        {
            await coordinator.Scheduler.EditAsync(
                static stage => stage.GetPrim("/World/Cube")
                    .SetDouble("viewer:liveEdit", 1),
                UsdStageInvalidationKind.Property,
                cancellationToken);
            while (ReferenceEquals(coordinator.CurrentState, stateBeforeEdit))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
            ViewerStartupOptions.WriteStatus(
                $"Viewer live edit observed: revision={stateBeforeEdit.Revision}->" +
                $"{coordinator.CurrentState.Revision}");
        }

        if (ViewerStartupOptions.PickSmokeEnabled)
        {
            await RunPickingSmokeAsync(coordinator, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(Close);
            return;
        }

        if (ViewerStartupOptions.IsRetiredKindQuarantineEvidenceScenario)
        {
            await RunRetiredKindQuarantineEvidenceAsync(coordinator, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(Close);
            return;
        }
        if (ViewerStartupOptions.IsStageCameraEvidenceScenario)
        {
            await RunStageCameraBackendSmokeAsync(coordinator, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(Close);
            return;
        }

        if (ViewerStartupOptions.SmokeSwitchBackend is { } target)
        {
            StageRenderState stateBeforeSwitch = coordinator.CurrentState;
            RenderBackendIdentity? previous = coordinator.ActiveBackend;
            if (_switchingEvidence is not null && previous is not null)
            {
                RecordWindowOwnership(
                    _switchingEvidence,
                    previous.Kind,
                    "requested-switch-before");
            }
            RenderBackendManagerResult result = await coordinator.SwitchAsync(
                target,
                cancellationToken);
            bool sameReference = ReferenceEquals(
                stateBeforeSwitch,
                coordinator.CurrentState);
            ViewerStartupOptions.WriteStatus(
                $"Viewer switch state preserved: {previous?.Kind.ToString() ?? "none"}->" +
                $"{result.ActiveBackend?.Kind.ToString() ?? "none"}; " +
                $"revision={stateBeforeSwitch.Revision}; sameReference={sameReference}");
            await Dispatcher.UIThread.InvokeAsync(SetActiveBackendStatus);
        }

        if (ViewerStartupOptions.SwitchSoakCount > 0)
        {
            await RunRendererSwitchSoakAsync(coordinator, cancellationToken);
            return;
        }

        if (_switchingEvidence is not null)
        {
            await RunSingleProcessEvidenceAsync(coordinator, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(Close);
        }
    }

    private async Task RunPickingSmokeAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The short Viewer picking smoke currently requires Windows.");
        }

        _cameraNavigation.ResetToExplicitPose();
        await coordinator.MutateStateAsync(
            state => state.WithCamera(_cameraNavigation.Camera),
            cancellationToken);
        ViewerPhysicalPixel[] samples = CreatePickingSmokeSamples(
            coordinator.CurrentState.Viewport);
        RenderBackendKind[] backends =
        [
            RenderBackendKind.Storm,
            RenderBackendKind.D3D12,
            RenderBackendKind.Vulkan
        ];
        var observations =
            new Dictionary<RenderBackendKind, RenderPickResult[]>(backends.Length);
        foreach (RenderBackendKind backend in backends)
        {
            await EnsurePickingSmokeBackendAsync(
                coordinator,
                backend,
                cancellationToken);
            var results = new RenderPickResult[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                results[index] = await PickWithRenderingAsync(
                    coordinator,
                    samples[index],
                    cancellationToken);
            }
            observations.Add(backend, results);
            ViewerStartupOptions.WriteStatus(
                $"Viewer picking sample summary: backend={backend}; " +
                $"hits={results.Count(result => result.Status == RenderPickStatus.Hit)}; " +
                $"misses={results.Count(result => result.Status == RenderPickStatus.Miss)}; " +
                $"stale={results.Count(result => result.Status == RenderPickStatus.Stale)}; " +
                $"unsupported={results.Count(result => result.Status == RenderPickStatus.Unsupported)}; " +
                $"paths={string.Join(",", results
                    .Where(result => result.Status == RenderPickStatus.Hit)
                    .Select(result => result.PrimPath)
                    .Distinct(StringComparer.Ordinal))}");
        }

        var hitIndices = new Dictionary<RenderBackendKind, int>(backends.Length);
        var missIndices = new Dictionary<RenderBackendKind, int>(backends.Length);
        foreach (RenderBackendKind backend in backends)
        {
            RenderPickResult[] results = observations[backend];
            int hitIndex = Array.FindIndex(
                results,
                result => result.Status == RenderPickStatus.Hit);
            int missIndex = Array.FindIndex(
                results,
                result => result.Status == RenderPickStatus.Miss);
            if (hitIndex < 0 || missIndex < 0)
            {
                throw new InvalidOperationException(
                    $"The short picking smoke did not find both a hit and miss on {backend}.");
            }
            hitIndices.Add(backend, hitIndex);
            missIndices.Add(backend, missIndex);
        }

        string commonPath = observations[RenderBackendKind.Storm][
            hitIndices[RenderBackendKind.Storm]].PrimPath;
        if (backends.Any(backend => !string.Equals(
                observations[backend][hitIndices[backend]].PrimPath,
                commonPath,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The short picking smoke did not resolve the same prim path across " +
                "Storm, Direct3D 12, and Vulkan.");
        }

        var clickResults =
            new Dictionary<RenderBackendKind, (bool Hit, bool Miss)>(backends.Length);
        var silkOutlines = new List<ViewerSilkOutlineEvidence>(capacity: 2);
        foreach (RenderBackendKind backend in backends)
        {
            await EnsurePickingSmokeBackendAsync(
                coordinator,
                backend,
                cancellationToken);
            await coordinator.MutateStateAsync(
                state => state.WithSelection(SelectionState.Empty),
                cancellationToken);
            _selectionState.Synchronize(SelectionState.Empty);
            ViewerPhysicalPixel hitPixel = samples[hitIndices[backend]];
            ViewerPhysicalPixel missPixel = samples[missIndices[backend]];
            if (backend == RenderBackendKind.Storm)
            {
                await ViewportHost.ExerciseStormClickAsync(
                    this,
                    hitPixel,
                    PollStormPickingSmokeInput,
                    cancellationToken);
            }
            else
            {
                await ViewportHost.ExerciseCompositionClickAsync(
                    this,
                    hitPixel,
                    cancellationToken);
            }
            await CompletePickingSmokeUiPickAsync(
                coordinator,
                cancellationToken);
            await WaitForPickingSmokeSelectionAsync(commonPath, cancellationToken);
            bool clickHit = string.Equals(
                _selectionState.PrimPath,
                commonPath,
                StringComparison.Ordinal);
            _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
            SilkSelectionOutlineDiagnostics? selectedOutline = null;
            if (backend != RenderBackendKind.Storm)
            {
                selectedOutline = await WaitForSilkOutlineStatusAsync(
                    coordinator,
                    SilkSelectionOutlineStatus.Rendered,
                    cancellationToken);
                if (selectedOutline.Value.ResolvedMeshCount == 0 ||
                    selectedOutline.Value.MaskPasses == 0 ||
                    selectedOutline.Value.OutlinePasses == 0 ||
                    selectedOutline.Value.SelectedDraws == 0)
                {
                    throw new InvalidOperationException(
                        $"{backend} did not record a visible selection mask and outline.");
                }
            }
            if (backend == RenderBackendKind.Storm)
            {
                await ViewportHost.ExerciseStormClickAsync(
                    this,
                    missPixel,
                    PollStormPickingSmokeInput,
                    cancellationToken);
            }
            else
            {
                await ViewportHost.ExerciseCompositionClickAsync(
                    this,
                    missPixel,
                    cancellationToken);
            }
            await CompletePickingSmokeUiPickAsync(
                coordinator,
                cancellationToken);
            await WaitForPickingSmokeSelectionAsync(
                expectedPath: null,
                cancellationToken);
            bool clickMiss = _selectionState.PrimPath is null;
            if (selectedOutline is { } selectedOutlineDiagnostics)
            {
                SilkSelectionOutlineDiagnostics cleared =
                    await WaitForSilkOutlineStatusAsync(
                        coordinator,
                        SilkSelectionOutlineStatus.EmptySelection,
                        cancellationToken);
                SilkSelectionOutlineDiagnostics postClear =
                    await WaitForSilkOutlineStatusAsync(
                        coordinator,
                        SilkSelectionOutlineStatus.EmptySelection,
                        cancellationToken);
                bool clearedWithoutAdditionalPass =
                    postClear.MaskPasses == cleared.MaskPasses &&
                    postClear.OutlinePasses == cleared.OutlinePasses &&
                    postClear.SelectedDraws == cleared.SelectedDraws;
                if (!clearedWithoutAdditionalPass)
                {
                    throw new InvalidOperationException(
                        $"{backend} recorded an outline pass after selection was cleared.");
                }
                silkOutlines.Add(new ViewerSilkOutlineEvidence(
                    backend.ToString(),
                    selectedOutlineDiagnostics.Status.ToString(),
                    selectedOutlineDiagnostics.MaskPasses,
                    selectedOutlineDiagnostics.OutlinePasses,
                    selectedOutlineDiagnostics.SelectedDraws,
                    postClear.Status.ToString(),
                    clearedWithoutAdditionalPass));
            }
            if (!clickHit || !clickMiss)
            {
                throw new InvalidOperationException(
                    $"{backend} click routing did not apply both hit and miss selection policy.");
            }
            clickResults.Add(backend, (clickHit, clickMiss));
        }

        await EnsurePickingSmokeBackendAsync(
            coordinator,
            RenderBackendKind.D3D12,
            cancellationToken);
        long retriesBefore = coordinator.PickingStatistics.StaleRetries;
        ViewerPhysicalPixel stalePixel =
            samples[hitIndices[RenderBackendKind.D3D12]];
        Task<RenderPickResult> staleRetry = coordinator
            .PickAsync(stalePixel, cancellationToken: cancellationToken)
            .AsTask();
        await coordinator.MutateStateAsync(
            static state => state.AdvanceRevision(),
            cancellationToken);
        RenderPickResult retried = await CompletePickWithRenderingAsync(
            coordinator,
            staleRetry,
            cancellationToken);
        long staleRetries =
            coordinator.PickingStatistics.StaleRetries - retriesBefore;
        ViewerStartupOptions.WriteStatus(
            $"Viewer picking stale retry observation: status={retried.Status}; " +
            $"reasons={retried.StaleReasons}; path={retried.PrimPath}; " +
            $"retries={staleRetries}; requested={retried.RequestedStateRevision}; " +
            $"actual={retried.StateRevision}");
        if (retried.Status != RenderPickStatus.Hit ||
            !string.Equals(retried.PrimPath, commonPath, StringComparison.Ordinal) ||
            staleRetries != 1)
        {
            throw new InvalidOperationException(
                "The short picking smoke did not observe exactly one successful stale retry.");
        }

        SelectionItem selectedItem = retried.Item ??
            throw new InvalidOperationException(
                "The stale-retry hit did not carry detached selection identity.");
        var selected = new SelectionState([selectedItem]);
        await coordinator.MutateStateAsync(
            state => state.WithSelection(selected),
            cancellationToken);
        foreach (RenderBackendKind backend in backends)
        {
            await EnsurePickingSmokeBackendAsync(
                coordinator,
                backend,
                cancellationToken);
            if (coordinator.CurrentState.Selection != selected)
            {
                throw new InvalidOperationException(
                    $"Selection identity was not preserved while switching to {backend}.");
            }
        }

        await EnsurePickingSmokeBackendAsync(
            coordinator,
            RenderBackendKind.Storm,
            cancellationToken);
        await coordinator.MutateStateAsync(
            state => state
                .WithCamera(CameraState.Default)
                .WithSelection(SelectionState.Empty),
            cancellationToken);
        ulong unselectedHash = await CaptureStormPickingHashAsync(
            coordinator,
            cancellationToken);
        await coordinator.MutateStateAsync(
            state => state.WithSelection(selected),
            cancellationToken);
        ulong selectedHash = await CaptureStormPickingHashAsync(
            coordinator,
            cancellationToken);
        await coordinator.MutateStateAsync(
            state => state.WithSelection(SelectionState.Empty),
            cancellationToken);
        ulong clearedHash = await CaptureStormPickingHashAsync(
            coordinator,
            cancellationToken);
        bool highlightChanged = selectedHash != unselectedHash;
        bool highlightCleared = clearedHash == unselectedHash;
        ViewerStartupOptions.WriteStatus(
            $"Viewer Storm selection hash observation: unselected=0x{unselectedHash:X16}; " +
            $"selected=0x{selectedHash:X16}; cleared=0x{clearedHash:X16}; " +
            $"selectionCount={coordinator.CurrentState.Selection.Items.Count}");
        if (!highlightChanged || !highlightCleared)
        {
            throw new InvalidOperationException(
                "Storm framebuffer hashes did not prove selection highlight change and clear.");
        }

        ViewerPickingSmokeBackendEvidence[] backendEvidence = backends
            .Select(backend => new ViewerPickingSmokeBackendEvidence(
                backend.ToString(),
                samples[hitIndices[backend]].X,
                samples[hitIndices[backend]].Y,
                observations[backend][hitIndices[backend]].PrimPath,
                samples[missIndices[backend]].X,
                samples[missIndices[backend]].Y,
                clickResults[backend].Hit,
                clickResults[backend].Miss))
            .ToArray();
        var evidence = new ViewerPickingSmokeEvidence(
            ViewerPickingSmokeContract.CurrentSchemaVersion,
            ViewerPickingSmokeContract.ScenarioName,
            _stagePath ?? string.Empty,
            commonPath,
            backendEvidence,
            clickResults[RenderBackendKind.Storm].Hit,
            clickResults[RenderBackendKind.Storm].Miss,
            staleRetries,
            SelectionPreservedAcrossSwitches: true,
            $"0x{unselectedHash:X16}",
            $"0x{selectedHash:X16}",
            $"0x{clearedHash:X16}",
            highlightChanged,
            highlightCleared,
            [.. silkOutlines],
            DateTimeOffset.UtcNow);
        await ViewerPickingSmokeWriter.WriteAsync(
            ViewerStartupOptions.PickSmokeEvidencePath!,
            evidence,
            cancellationToken);
        ViewerStartupOptions.WriteStatus(
            $"Viewer picking short smoke passed: path={commonPath}; " +
            "clicks=Storm,D3D12,Vulkan; " +
            $"d3d12Hit={stalePixel.X},{stalePixel.Y}; " +
            $"staleRetries={staleRetries}; silkOutlines={silkOutlines.Count}; " +
            $"storm=0x{unselectedHash:X16}->" +
            $"0x{selectedHash:X16}->0x{clearedHash:X16}");
    }

    private async Task EnsurePickingSmokeBackendAsync(
        ViewerRenderCoordinator coordinator,
        RenderBackendKind backend,
        CancellationToken cancellationToken)
    {
        RenderBackendManagerResult switched = await coordinator.SwitchAsync(
            backend,
            cancellationToken);
        if (!switched.IsSuccess || switched.ActiveBackend?.Kind != backend)
        {
            throw new InvalidOperationException(
                $"The short picking smoke could not activate {backend}: {switched.Failure}.");
        }
        await Dispatcher.UIThread.InvokeAsync(SetActiveBackendStatus);
        _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
        _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
    }

    private async Task WaitForPickingSmokeSelectionAsync(
        string? expectedPath,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (string.Equals(
                    _selectionState.PrimPath,
                    expectedPath,
                    StringComparison.Ordinal) &&
                (_pickTask is null || _pickTask.IsCompleted))
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
        throw new TimeoutException(
            $"The short picking smoke did not observe selection '{expectedPath ?? "<none>"}'.");
    }

    private static async Task<SilkSelectionOutlineDiagnostics>
        WaitForSilkOutlineStatusAsync(
            ViewerRenderCoordinator coordinator,
            SilkSelectionOutlineStatus expectedStatus,
            CancellationToken cancellationToken)
    {
        SilkSelectionOutlineDiagnostics? latest = null;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
            latest = coordinator.SelectionOutlineDiagnostics;
            if (latest is { Status: var status } && status == expectedStatus)
            {
                return latest.Value;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
        }
        throw new TimeoutException(
            $"The active Silk backend did not report selection outline status " +
            $"'{expectedStatus}'. Last status: {latest?.Status.ToString() ?? "<none>"}.");
    }

    private async Task CompletePickingSmokeUiPickAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0;
             attempt < 8 && _pickTask is { IsCompleted: false };
             attempt++)
        {
            _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
        if (_pickTask is { } pending)
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private void PollStormPickingSmokeInput()
    {
        if (ViewportHost.GetActiveStormNavigationSource() is { } source &&
            source.TryGetNavigationInput(out OpenUsdStormNavigationInput input))
        {
            ViewerStartupOptions.WriteStatus(
                $"Viewer Storm click poll: sequence={input.Sequence}; " +
                $"point={input.PointerX},{input.PointerY}; buttons={input.Buttons}; " +
                $"modifiers={input.Modifiers}; focused={input.Focused}; inside={input.Inside}");
        }
        OnStormNavigationTick(null, EventArgs.Empty);
    }

    private static ViewerPhysicalPixel[] CreatePickingSmokeSamples(
        ViewportDimensions viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new InvalidOperationException(
                "The short picking smoke requires a positive physical viewport.");
        }

        double[] fractions = ViewerPickingSmokeContract.SampleFractions;
        var samples = new ViewerPhysicalPixel[fractions.Length * fractions.Length];
        int index = 0;
        foreach (double y in fractions)
        {
            foreach (double x in fractions)
            {
                samples[index++] = new ViewerPhysicalPixel(
                    Math.Clamp(
                        (int)Math.Floor((viewport.Width - 1) * x),
                        0,
                        viewport.Width - 1),
                    Math.Clamp(
                        (int)Math.Floor((viewport.Height - 1) * y),
                        0,
                        viewport.Height - 1));
            }
        }
        return samples;
    }

    private static async Task<RenderPickResult> PickWithRenderingAsync(
        ViewerRenderCoordinator coordinator,
        ViewerPhysicalPixel pixel,
        CancellationToken cancellationToken)
    {
        Task<RenderPickResult> pending = coordinator
            .PickAsync(pixel, cancellationToken: cancellationToken)
            .AsTask();
        return await CompletePickWithRenderingAsync(
            coordinator,
            pending,
            cancellationToken);
    }

    private static async Task<RenderPickResult> CompletePickWithRenderingAsync(
        ViewerRenderCoordinator coordinator,
        Task<RenderPickResult> pending,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8 && !pending.IsCompleted; attempt++)
        {
            _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
        return await pending.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);
    }

    private async Task<ulong> CaptureStormPickingHashAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        OpenUsdStormChildDiagnostics diagnostics = default;
        for (int frame = 0; frame < 120; frame++)
        {
            _ = await RenderUntilPresentedAsync(coordinator, cancellationToken);
            diagnostics = ViewportHost.GetActiveStormDiagnostics();
            if (diagnostics.Converged &&
                diagnostics.LatestRequestedRevision ==
                    coordinator.CurrentState.Revision)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
        }
        if (!diagnostics.Converged ||
            diagnostics.LatestRequestedRevision != coordinator.CurrentState.Revision)
        {
            throw new TimeoutException(
                "Storm did not converge the current selection state before capture.");
        }
        OpenUsdStormFramebufferCapture capture = await ViewportHost
            .CaptureStormFramebufferAsync(cancellationToken);
        return capture.PixelHash;
    }

    private async Task RunRendererSwitchSoakAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        RenderBackendKind[] sequence = GetSwitchSoakSequence();
        var rendered = new HashSet<RenderBackendKind>();
        var exercisedInputs = new HashSet<RenderBackendKind>();
        var exercisedCameras = new HashSet<RenderBackendKind>();
        int compositionDraws = 0;
        int compositionFrames = 0;
        for (int index = 0; index < ViewerStartupOptions.SwitchSoakCount; index++)
        {
            await ApplyVisualEditAsync(coordinator, index, cancellationToken);
            StageRenderState expectedState = coordinator.CurrentState;
            RenderBackendKind target = sequence[index % sequence.Length];
            if (_switchingEvidence is not null && coordinator.ActiveBackend is { } active)
            {
                RecordWindowOwnership(
                    _switchingEvidence,
                    active.Kind,
                    $"switch-{index + 1:D3}-before");
            }
            RenderBackendManagerResult switched = await coordinator.SwitchAsync(
                target,
                cancellationToken);
            if (!switched.IsSuccess ||
                switched.ActiveBackend?.Kind != target ||
                !ReferenceEquals(expectedState, coordinator.CurrentState))
            {
                throw new InvalidOperationException(
                    $"Renderer switch {index + 1} did not preserve the exact stage state.");
            }

            ManagedRenderFrameResult frame =
                await RenderUntilPresentedAsync(coordinator, cancellationToken);
            if (frame.ActiveBackend?.Kind != target ||
                frame.Frame?.Status != RenderFrameStatus.Rendered)
            {
                throw new InvalidOperationException(
                    $"Renderer switch {index + 1} did not present on {target}.");
            }
            rendered.Add(target);
            if (target is RenderBackendKind.D3D12 or
                RenderBackendKind.Vulkan or
                RenderBackendKind.Metal)
            {
                compositionDraws += frame.Frame.Statistics.DrawCalls;
                compositionFrames++;
            }
            bool exerciseInput = exercisedInputs.Add(target);
            if (_switchingEvidence is not null)
            {
                if (exercisedCameras.Add(target))
                {
                    await RunExplicitCameraEvidenceAsync(
                        coordinator,
                        target,
                        expectedState,
                        $"switch-{index + 1:D3}-camera",
                        exerciseInput,
                        cancellationToken);
                }
                else
                {
                    _ = await RecordSwitchingEvidenceAsync(
                        coordinator,
                        target,
                        expectedState,
                        $"switch-{index + 1:D3}",
                        exerciseInput,
                        cancellationToken);
                }
            }
            else if (exerciseInput)
            {
                _ = await ViewportHost.ExerciseEvidenceInputAsync(
                    this,
                    target,
                    cancellationToken);
            }
        }

        TimeSpan remaining =
            TimeSpan.FromSeconds(ViewerStartupOptions.SwitchSoakSeconds) -
            (DateTimeOffset.UtcNow - startedAt);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
        if (rendered.Count != sequence.Length ||
            compositionFrames <= 0 ||
            compositionDraws <= 0)
        {
            throw new InvalidOperationException(
                "The switch soak did not produce native and composition renderer evidence.");
        }
        (long samples, ulong signature) =
            ViewerStartupOptions.GetNativeStormPixelEvidence();
        if (samples <= 0)
        {
            throw new InvalidOperationException(
                "The switch soak did not capture a native Storm diagnostic pixel.");
        }
        ViewerStartupOptions.WriteStatus(
            $"Viewer switch soak passed: switches={ViewerStartupOptions.SwitchSoakCount}; " +
            $"revision={coordinator.CurrentState.Revision}; sameState=True; " +
            $"compositionFrames={compositionFrames}; compositionDraws={compositionDraws}; " +
            $"stormPixelSamples={samples}; " +
            $"stormPixel=0x{signature:X8}; seconds=" +
            $"{(DateTimeOffset.UtcNow - startedAt).TotalSeconds:F1}");
        await Dispatcher.UIThread.InvokeAsync(Close);
    }

    internal static RenderBackendKind[] GetSwitchSoakSequence() =>
        OperatingSystem.IsMacOS()
            ? [RenderBackendKind.Metal, RenderBackendKind.Storm]
            : OperatingSystem.IsLinux()
                ? [RenderBackendKind.Vulkan, RenderBackendKind.Storm]
                :
                [
                    RenderBackendKind.D3D12,
                    RenderBackendKind.Vulkan,
                    RenderBackendKind.Storm
                ];

    private async Task RunStageCameraBackendSmokeAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The authored stage-camera backend smoke is a Windows evidence scenario.");
        }

        ViewerSwitchingEvidenceSession evidence = _switchingEvidence ??
            throw new InvalidOperationException("Switching evidence is unavailable.");
        string cameraPath = ViewerStartupOptions.StageCameraPrimPath ??
            throw new InvalidOperationException(
                "The stage-camera evidence prim path is unavailable.");
        string stagePath = ViewerStartupOptions.StagePath ??
            throw new InvalidOperationException(
                "The stage-camera evidence stage path is unavailable.");
        StageRenderState seed = coordinator.CurrentState;
        await EnsureStageCameraBackendAsync(
            coordinator,
            RenderBackendKind.Storm,
            seed,
            cancellationToken);

        bool automaticPrepared = await coordinator.MutateStateAsync(
            state => ViewerStageCameraSmokeContract.ApplyAutomatic(
                state,
                seed,
                cameraPath,
                ViewerStageCameraSmokeContract.InitialTimeCode),
            cancellationToken);
        if (!automaticPrepared)
        {
            throw new InvalidOperationException(
                "The stage-camera scenario did not create its selected automatic state.");
        }
        StageRenderState automaticBeforeState = coordinator.CurrentState;
        var automaticBeforeFrame = await RecordSwitchingEvidenceAsync(
            coordinator,
            RenderBackendKind.Storm,
            automaticBeforeState,
            "stage-camera-automatic-before",
            exerciseInput: false,
            cancellationToken);
        RequireStageCameraPixels(automaticBeforeFrame.Pixel);

        var source = new ViewerSchedulerStageCameraSource(coordinator.Scheduler);
        ViewerStageCameraActivation activation = _stageCameraMode.CaptureActivation(
            cameraPath,
            ViewerStageCameraSmokeContract.InitialTimeCode);
        ViewerStageCameraQueryResult initialResult =
            await ViewerStageCameraQuery.QueryAsync(
                source,
                cameraPath,
                new StageTime(ViewerStageCameraSmokeContract.InitialTimeCode),
                cancellationToken);
        ViewerStageCameraSnapshot initialSnapshot =
            ViewerStageCameraSmokeContract.RequireReady(
                initialResult,
                cameraPath,
                ViewerStageCameraSmokeContract.InitialTimeCode);
        if (!_stageCameraMode.TryActivate(
                activation,
                initialSnapshot,
                out CameraState initialCamera))
        {
            throw new InvalidOperationException(
                "The initial authored camera activation became stale.");
        }
        bool initialApplied = await coordinator.MutateStateAsync(
            state => ViewerStageCameraSmokeContract.ApplyCamera(
                state,
                automaticBeforeState,
                cameraPath,
                ViewerStageCameraSmokeContract.InitialTimeCode,
                initialCamera),
            cancellationToken);
        if (!initialApplied)
        {
            throw new InvalidOperationException(
                "The initial authored camera did not advance Viewer state.");
        }
        StageRenderState initialState = coordinator.CurrentState;
        ViewerStageCameraBackendFrameEvidence[] initialFrames =
            await RecordStageCameraFramesAsync(
                coordinator,
                initialState,
                ViewerStageCameraSmokeContract.InitialTimeCode,
                "stage-camera-initial",
                [
                    RenderBackendKind.Storm,
                    RenderBackendKind.D3D12,
                    RenderBackendKind.Vulkan
                ],
                cancellationToken);

        if (!_stageCameraMode.TryCreateRefreshRequest(
                ViewerStageCameraSmokeContract.SampledTimeCode,
                applyTime: true,
                out ViewerStageCameraRefreshRequest sampledRequest))
        {
            throw new InvalidOperationException(
                "The active authored camera did not create a sampled refresh request.");
        }
        ViewerStageCameraQueryResult sampledResult =
            await ViewerStageCameraQuery.QueryAsync(
                source,
                cameraPath,
                new StageTime(ViewerStageCameraSmokeContract.SampledTimeCode),
                cancellationToken);
        ViewerStageCameraSnapshot sampledSnapshot =
            ViewerStageCameraSmokeContract.RequireReady(
                sampledResult,
                cameraPath,
                ViewerStageCameraSmokeContract.SampledTimeCode);
        if (!_stageCameraMode.TryRefresh(
                sampledRequest,
                sampledSnapshot,
                out CameraState sampledCamera))
        {
            throw new InvalidOperationException(
                "The sampled authored camera refresh became stale.");
        }
        ValidateStageCameraSmokeSnapshots(initialSnapshot, sampledSnapshot);
        bool sampledApplied = await coordinator.MutateStateAsync(
            state => ViewerStageCameraSmokeContract.ApplyCamera(
                state,
                initialState,
                cameraPath,
                ViewerStageCameraSmokeContract.SampledTimeCode,
                sampledCamera),
            cancellationToken);
        if (!sampledApplied)
        {
            throw new InvalidOperationException(
                "The sampled authored camera did not advance Viewer state.");
        }
        StageRenderState sampledState = coordinator.CurrentState;
        ViewerStageCameraBackendFrameEvidence[] sampledFrames =
            await RecordStageCameraFramesAsync(
                coordinator,
                sampledState,
                ViewerStageCameraSmokeContract.SampledTimeCode,
                "stage-camera-sampled",
                [
                    RenderBackendKind.Vulkan,
                    RenderBackendKind.D3D12,
                    RenderBackendKind.Storm
                ],
                cancellationToken);

        if (!_stageCameraMode.ResetToAutomatic())
        {
            throw new InvalidOperationException(
                "The authored camera did not reset to Automatic mode.");
        }
        bool automaticRestored = await coordinator.MutateStateAsync(
            state => ViewerStageCameraSmokeContract.ApplyAutomatic(
                state,
                sampledState,
                cameraPath,
                ViewerStageCameraSmokeContract.InitialTimeCode),
            cancellationToken);
        if (!automaticRestored)
        {
            throw new InvalidOperationException(
                "The automatic reset did not advance Viewer state.");
        }
        StageRenderState automaticRestoredState = coordinator.CurrentState;
        var automaticRestoredFrame = await RecordSwitchingEvidenceAsync(
            coordinator,
            RenderBackendKind.Storm,
            automaticRestoredState,
            "stage-camera-automatic-restored",
            exerciseInput: false,
            cancellationToken);
        RequireStageCameraPixels(automaticRestoredFrame.Pixel);

        string initialSnapshotSha256 =
            ViewerStageCameraSmokeContract.ComputeSnapshotSha256(initialSnapshot);
        string sampledSnapshotSha256 =
            ViewerStageCameraSmokeContract.ComputeSnapshotSha256(sampledSnapshot);
        string stageSha256 = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(stagePath)));
        evidence.RecordStageCamera(new ViewerStageCameraEvidence(
            ViewerStageCameraSmokeContract.SourceName,
            automaticBeforeState.Stage.Identifier,
            stageSha256,
            cameraPath,
            ViewerStageCameraSmokeContract.InitialTimeCode,
            ViewerStageCameraSmokeContract.SampledTimeCode,
            initialSnapshotSha256,
            sampledSnapshotSha256,
            CreateAutomaticStageCameraEvidence(automaticBeforeFrame),
            initialFrames,
            sampledFrames,
            CreateAutomaticStageCameraEvidence(automaticRestoredFrame),
            ExactStatePreservedAcrossBackends: true));

        StageRenderState genericState = coordinator.CurrentState;
        foreach (RenderBackendKind backend in
            new[]
            {
                RenderBackendKind.D3D12,
                RenderBackendKind.Vulkan,
                RenderBackendKind.Storm
            })
        {
            await EnsureStageCameraBackendAsync(
                coordinator,
                backend,
                genericState,
                cancellationToken);
            await RunExplicitCameraEvidenceAsync(
                coordinator,
                backend,
                genericState,
                $"stage-camera-contract-{backend}",
                exerciseInput: true,
                cancellationToken);
            genericState = coordinator.CurrentState;
        }

        ViewerStartupOptions.WriteStatus(
            $"Viewer stage camera smoke passed: path={cameraPath}; " +
            $"stageSha256={stageSha256}; initialSnapshot={initialSnapshotSha256}; " +
            $"sampledSnapshot={sampledSnapshotSha256}; " +
            $"initialRevision={initialState.Revision}; " +
            $"sampledRevision={sampledState.Revision}; " +
            $"initialPixels={FormatStageCameraPixels(initialFrames)}; " +
            $"sampledPixels={FormatStageCameraPixels(sampledFrames)}; " +
            $"automaticPixel={automaticRestoredFrame.Pixel.Sha256}; exactState=True");
    }

    private async Task<ViewerStageCameraBackendFrameEvidence[]>
        RecordStageCameraFramesAsync(
            ViewerRenderCoordinator coordinator,
            StageRenderState state,
            double timeCode,
            string phasePrefix,
            RenderBackendKind[] backends,
            CancellationToken cancellationToken)
    {
        var frames = new List<ViewerStageCameraBackendFrameEvidence>(backends.Length);
        foreach (RenderBackendKind backend in backends)
        {
            await EnsureStageCameraBackendAsync(
                coordinator,
                backend,
                state,
                cancellationToken);
            var captured = await RecordSwitchingEvidenceAsync(
                coordinator,
                backend,
                state,
                $"{phasePrefix}-{backend}",
                exerciseInput: false,
                cancellationToken);
            RequireStageCameraPixels(captured.Pixel);
            if (captured.State.TimeCode != timeCode ||
                captured.State.Revision != state.Revision ||
                !string.Equals(
                    captured.State.CameraMode,
                    nameof(CameraMode.Matrices),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The {backend} stage-camera frame did not bind its sampled state.");
            }

            ulong requestedRevision = 0;
            string requestedSignature = string.Empty;
            string renderedSignature = string.Empty;
            if (backend == RenderBackendKind.Storm)
            {
                OpenUsdStormChildDiagnostics diagnostics =
                    captured.StormDiagnostics ??
                    throw new InvalidOperationException(
                        "Storm stage-camera diagnostics were unavailable.");
                requestedRevision = diagnostics.LatestRequestedRevision;
                requestedSignature = diagnostics.LatestRequestedCameraSignature.ToString(
                    "X16",
                    CultureInfo.InvariantCulture);
                renderedSignature = diagnostics.LatestRenderedCameraSignature.ToString(
                    "X16",
                    CultureInfo.InvariantCulture);
                if (requestedRevision != state.Revision ||
                    !string.Equals(
                        requestedSignature,
                        captured.State.NativeCameraSignature,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        renderedSignature,
                        captured.State.NativeCameraSignature,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Storm did not render the requested authored-camera revision/signature.");
                }
            }

            frames.Add(new ViewerStageCameraBackendFrameEvidence(
                backend.ToString(),
                captured.State.Phase,
                timeCode,
                captured.State.Revision,
                captured.State.CameraSignature,
                captured.State.NativeCameraSignature,
                captured.Pixel.Sha256,
                captured.Pixel.Artifact,
                ExactReferencePreserved: true,
                requestedRevision,
                requestedSignature,
                renderedSignature));
        }
        return [.. frames];
    }

    private async Task EnsureStageCameraBackendAsync(
        ViewerRenderCoordinator coordinator,
        RenderBackendKind backend,
        StageRenderState expectedState,
        CancellationToken cancellationToken)
    {
        if (coordinator.ActiveBackend?.Kind == backend)
        {
            if (!ReferenceEquals(expectedState, coordinator.CurrentState))
            {
                throw new InvalidOperationException(
                    $"The {backend} stage-camera state became stale before rendering.");
            }
            return;
        }

        RenderBackendManagerResult switched = await coordinator.SwitchAsync(
            backend,
            cancellationToken);
        if (!switched.IsSuccess ||
            switched.ActiveBackend?.Kind != backend ||
            !ReferenceEquals(expectedState, coordinator.CurrentState))
        {
            throw new InvalidOperationException(
                $"Switching to {backend} did not preserve the exact stage-camera state.");
        }
        if (backend == RenderBackendKind.Storm)
        {
            RenderBackendManagerResult synchronized =
                await coordinator.UpdateStateAsync(
                    expectedState,
                    cancellationToken);
            if (!synchronized.IsSuccess ||
                synchronized.ActiveBackend?.Kind != backend ||
                !ReferenceEquals(expectedState, coordinator.CurrentState))
            {
                throw new InvalidOperationException(
                    "Storm did not accept the exact preserved stage-camera state after switching.");
            }
        }
        await Dispatcher.UIThread.InvokeAsync(SetActiveBackendStatus);
    }

    private static ViewerStageCameraAutomaticEvidence
        CreateAutomaticStageCameraEvidence((
            ViewerStateEvidence State,
            ViewerPixelEvidence Pixel,
            OpenUsdStormChildDiagnostics? StormDiagnostics) frame) =>
        new(
            frame.State.Backend,
            frame.State.Phase,
            frame.State.TimeCode,
            frame.State.Revision,
            frame.State.CameraSignature,
            frame.State.NativeCameraSignature,
            frame.Pixel.Sha256,
            frame.Pixel.Artifact);

    private static void RequireStageCameraPixels(ViewerPixelEvidence pixel)
    {
        long minimum = Math.Max(100, pixel.Width * (long)pixel.Height / 1000);
        if (pixel.NonBackgroundPixels < minimum)
        {
            throw new InvalidOperationException(
                $"The {pixel.Backend} stage-camera capture was background-only.");
        }
    }

    private static void ValidateStageCameraSmokeSnapshots(
        in ViewerStageCameraSnapshot initial,
        in ViewerStageCameraSnapshot sampled)
    {
        UsdVec3d initialTranslation = initial.LocalToWorld.ExtractTranslation();
        UsdVec3d sampledTranslation = sampled.LocalToWorld.ExtractTranslation();
        bool initialWorldTransform =
            Math.Abs(initialTranslation.X) < 1e-9d &&
            Math.Abs(initialTranslation.Y) < 1e-9d &&
            Math.Abs(initialTranslation.Z - 12d) < 1e-9d;
        bool sampledWorldTransform =
            Math.Abs(sampledTranslation.X - 1.25d) < 1e-9d &&
            Math.Abs(sampledTranslation.Y - 0.5d) < 1e-9d &&
            Math.Abs(sampledTranslation.Z - 8d) < 1e-9d;
        if (!initialWorldTransform ||
            !sampledWorldTransform ||
            initial.Projection != UsdGeomCameraProjection.Perspective ||
            sampled.Projection != UsdGeomCameraProjection.Perspective ||
            initial.Optics.HorizontalApertureOffset == 0d ||
            initial.Optics.VerticalApertureOffset == 0d ||
            sampled.Optics.HorizontalApertureOffset == 0d ||
            sampled.Optics.VerticalApertureOffset == 0d ||
            initial.Optics.HorizontalApertureOffset ==
                sampled.Optics.HorizontalApertureOffset ||
            initial.Optics.VerticalApertureOffset ==
                sampled.Optics.VerticalApertureOffset ||
            initial.Optics.FocalLength == sampled.Optics.FocalLength ||
            initial.Optics.ClippingNear <= 0d ||
            sampled.Optics.ClippingNear <= 0d ||
            initial.Optics.ClippingFar <= initial.Optics.ClippingNear ||
            sampled.Optics.ClippingFar <= sampled.Optics.ClippingNear)
        {
            throw new InvalidDataException(
                "The authored smoke camera did not expose its composed sampled transform/optics.");
        }
    }

    private static string FormatStageCameraPixels(
        ViewerStageCameraBackendFrameEvidence[] frames) =>
        string.Join(
            ",",
            frames
                .OrderBy(frame => frame.Backend, StringComparer.Ordinal)
                .Select(frame => $"{frame.Backend}:{frame.PixelSha256}"));

    private async Task RunSingleProcessEvidenceAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        RenderBackendKind initial = coordinator.ActiveBackend?.Kind
            ?? throw new InvalidOperationException("No renderer is active for evidence.");
        StageRenderState state = coordinator.CurrentState;
        await RunExplicitCameraEvidenceAsync(
            coordinator,
            initial,
            state,
            "initial-camera",
            exerciseInput: true,
            cancellationToken);

        if (string.Equals(
            ViewerStartupOptions.SwitchingEvidenceScenario,
            "device-loss",
            StringComparison.Ordinal))
        {
            ViewerStartupOptions.ArmEvidenceDeviceLoss(initial);
            ManagedRenderFrameResult? failedOver = null;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                failedOver = await coordinator.RenderAsync(cancellationToken);
                if (coordinator.ActiveBackend?.Kind != initial)
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
            }
            if (failedOver is null ||
                coordinator.ActiveBackend?.Kind == initial)
            {
                throw new InvalidOperationException(
                    "The device-loss scenario did not activate a fallback renderer.");
            }
            RenderBackendIdentity fallbackIdentity = coordinator.ActiveBackend ??
                throw new InvalidOperationException(
                    "The device-loss fallback renderer is unavailable.");
            RenderBackendKind fallback = fallbackIdentity.Kind;
            await ApplyVisualEditAsync(coordinator, 1, cancellationToken);
            state = coordinator.CurrentState;
            await RunExplicitCameraEvidenceAsync(
                coordinator,
                fallback,
                state,
                "device-loss-fallback-camera",
                exerciseInput: true,
                cancellationToken);
            return;
        }

        await ApplyVisualEditAsync(coordinator, 1, cancellationToken);
        state = coordinator.CurrentState;
        _ = await RecordSwitchingEvidenceAsync(
            coordinator,
            initial,
            state,
            "edited",
            exerciseInput: false,
            cancellationToken);
    }

    private async Task RunRetiredKindQuarantineEvidenceAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        ViewerSwitchingEvidenceSession evidence = _switchingEvidence ??
            throw new InvalidOperationException("Switching evidence is unavailable.");
        AvaloniaViewerRenderBackendHost host = _backendHost ??
            throw new InvalidOperationException("The Viewer backend host is unavailable.");
        if (coordinator.ActiveBackend?.Kind != RenderBackendKind.Storm ||
            !ViewerStartupOptions.NativeStormPersistentDestroyFailure)
        {
            throw new InvalidOperationException(
                "The retained-kind quarantine scenario requires Storm and its persistent failpoint.");
        }

        StageRenderState state = coordinator.CurrentState;
        await RunExplicitCameraEvidenceAsync(
            coordinator,
            RenderBackendKind.Storm,
            state,
            "quarantine-initial-camera",
            exerciseInput: true,
            cancellationToken);
        state = coordinator.CurrentState;
        int retiredBefore = coordinator.RetiredCleanupCount;
        int candidateBefore =
            coordinator.GetCandidateSelectionCount(RenderBackendKind.Storm);
        int factoryBefore = coordinator.GetFactoryCreationCount(RenderBackendKind.Storm);
        int attachBefore = host.GetAttachCount(RenderBackendKind.Storm);
        (long childLiveBefore, long childPeakBefore) =
            OpenUsdStormChildRuntime.GetChildCounts();

        RenderBackendManagerResult switched = await coordinator.SwitchAsync(
            RenderBackendKind.D3D12,
            cancellationToken);
        if (!switched.IsSuccess ||
            switched.ActiveBackend?.Kind != RenderBackendKind.D3D12 ||
            coordinator.RetiredCleanupCount != 1)
        {
            throw new InvalidOperationException(
                "Storm cleanup did not remain retained after the D3D12 switch.");
        }
        state = coordinator.CurrentState;

        RenderBackendManagerResult manual = await coordinator.SwitchAsync(
            RenderBackendKind.Storm,
            cancellationToken);
        int candidateAfterManual =
            coordinator.GetCandidateSelectionCount(RenderBackendKind.Storm);
        int factoryAfterManual =
            coordinator.GetFactoryCreationCount(RenderBackendKind.Storm);
        int attachAfterManual = host.GetAttachCount(RenderBackendKind.Storm);
        RenderBackendManagerResult automatic = await coordinator.SwitchAsync(
            requestedBackend: null,
            cancellationToken);
        int candidateAfterAutomatic =
            coordinator.GetCandidateSelectionCount(RenderBackendKind.Storm);
        int factoryAfterAutomatic =
            coordinator.GetFactoryCreationCount(RenderBackendKind.Storm);
        int attachAfterAutomatic = host.GetAttachCount(RenderBackendKind.Storm);
        bool automaticSkipped = automatic.Diagnostics.Entries.Any(diagnostic =>
            diagnostic.Backend == RenderBackendKind.Storm &&
            diagnostic.Code == "manager.backend_cleanup_pending");
        ViewerHwndEvidence blockedOwnership = evidence.ObserveWindowOwnership(
            this,
            ViewportHost,
            RenderBackendKind.D3D12,
            "quarantine-blocked");
        int retiredWhileBlocked = coordinator.RetiredCleanupCount;
        (long childLiveWhileBlocked, long childPeakWhileBlocked) =
            OpenUsdStormChildRuntime.GetChildCounts();

        ViewerStartupOptions.ReleasePersistentNativeStormDestroyFailure();
        RenderBackendManagerResult recovered = await coordinator.SwitchAsync(
            RenderBackendKind.D3D12,
            cancellationToken);
        int retiredAfterRecovery = coordinator.RetiredCleanupCount;
        await RunExplicitCameraEvidenceAsync(
            coordinator,
            RenderBackendKind.D3D12,
            state,
            "quarantine-cleanup-recovered-camera",
            exerciseInput: false,
            cancellationToken);
        state = coordinator.CurrentState;
        RenderBackendManagerResult reactivated = await coordinator.SwitchAsync(
            RenderBackendKind.Storm,
            cancellationToken);
        _ = await RecordSwitchingEvidenceAsync(
            coordinator,
            RenderBackendKind.Storm,
            state,
            "quarantine-reactivated",
            exerciseInput: false,
            cancellationToken);
        int candidateAfterRecovery =
            coordinator.GetCandidateSelectionCount(RenderBackendKind.Storm);
        int factoryAfterRecovery =
            coordinator.GetFactoryCreationCount(RenderBackendKind.Storm);
        int attachAfterRecovery = host.GetAttachCount(RenderBackendKind.Storm);
        (long childLiveAfterRecovery, long childPeakAfterRecovery) =
            OpenUsdStormChildRuntime.GetChildCounts();
        string manualDiagnostic = manual.Diagnostics.Entries
            .FirstOrDefault(diagnostic =>
                diagnostic.Code == "manager.backend_cleanup_pending")?.Code
            ?? string.Empty;

        evidence.RecordCleanupQuarantine(new ViewerCleanupQuarantineEvidence(
            RenderBackendKind.Storm.ToString(),
            retiredBefore,
            retiredWhileBlocked,
            retiredAfterRecovery,
            candidateBefore,
            candidateAfterManual,
            candidateAfterAutomatic,
            candidateAfterRecovery,
            factoryBefore,
            factoryAfterManual,
            factoryAfterAutomatic,
            factoryAfterRecovery,
            attachBefore,
            attachAfterManual,
            attachAfterAutomatic,
            attachAfterRecovery,
            childLiveBefore,
            childPeakBefore,
            childLiveWhileBlocked,
            childPeakWhileBlocked,
            childLiveAfterRecovery,
            childPeakAfterRecovery,
            manual.Failure.ToString(),
            manualDiagnostic,
            automatic.IsSuccess &&
                automatic.ActiveBackend?.Kind == RenderBackendKind.D3D12,
            automaticSkipped,
            recovered.IsSuccess && retiredAfterRecovery == 0,
            reactivated.IsSuccess &&
                reactivated.ActiveBackend?.Kind == RenderBackendKind.Storm,
            blockedOwnership));
        ViewerStartupOptions.WriteStatus(
            "Viewer retired-kind quarantine passed: kind=Storm; retired=0->1->0; " +
            $"candidate={candidateBefore}->{candidateAfterManual}->" +
            $"{candidateAfterAutomatic}->{candidateAfterRecovery}; " +
            $"factory={factoryBefore}->{factoryAfterManual}->" +
            $"{factoryAfterAutomatic}->{factoryAfterRecovery}; " +
            $"attach={attachBefore}->{attachAfterManual}->" +
            $"{attachAfterAutomatic}->{attachAfterRecovery}; " +
            $"child={childLiveBefore}/{childPeakBefore}->" +
            $"{childLiveWhileBlocked}/{childPeakWhileBlocked}->" +
            $"{childLiveAfterRecovery}/{childPeakAfterRecovery}; " +
            $"manual={manual.Failure}; automaticSkipped={automaticSkipped}");
    }

    private async Task RunExplicitCameraEvidenceAsync(
        ViewerRenderCoordinator coordinator,
        RenderBackendKind backend,
        StageRenderState automaticState,
        string phasePrefix,
        bool exerciseInput,
        CancellationToken cancellationToken)
    {
        ViewerSwitchingEvidenceSession evidence = _switchingEvidence ??
            throw new InvalidOperationException("Switching evidence is unavailable.");
        if (automaticState.Camera.Mode != CameraMode.Automatic ||
            !ReferenceEquals(automaticState, coordinator.CurrentState))
        {
            throw new InvalidOperationException(
                $"The {backend} camera evidence did not start from the exact automatic state.");
        }

        var before = await RecordSwitchingEvidenceAsync(
            coordinator,
            backend,
            automaticState,
            $"{phasePrefix}-automatic-before",
            exerciseInput: false,
            cancellationToken);

        CameraState explicitCamera = ViewerCameraEvidence.CreateDeterministicExplicitCamera();
        bool changed = await coordinator.MutateStateAsync(
            state => state.WithCamera(explicitCamera),
            cancellationToken);
        if (!changed)
        {
            throw new InvalidOperationException(
                $"The {backend} explicit camera did not create a new render state.");
        }
        StageRenderState explicitState = coordinator.CurrentState;
        (
            ViewerStateEvidence State,
            ViewerPixelEvidence Pixel,
            OpenUsdStormChildDiagnostics? StormDiagnostics) explicitFrame;
        try
        {
            explicitFrame = await RecordSwitchingEvidenceAsync(
                coordinator,
                backend,
                explicitState,
                $"{phasePrefix}-explicit",
                exerciseInput: false,
                cancellationToken);
        }
        finally
        {
            _ = await coordinator.MutateStateAsync(
                state => state.WithCamera(automaticState.Camera),
                CancellationToken.None);
        }

        StageRenderState restoredState = coordinator.CurrentState;
        var restoredFrame = await RecordSwitchingEvidenceAsync(
            coordinator,
            backend,
            restoredState,
            $"{phasePrefix}-automatic-restored",
            exerciseInput,
            cancellationToken);
        if (restoredState.Camera != automaticState.Camera ||
            string.Equals(
                before.Pixel.Sha256,
                explicitFrame.Pixel.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                restoredFrame.Pixel.Sha256,
                explicitFrame.Pixel.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {backend} explicit camera did not produce and restore distinct evidence.");
        }

        ulong requestedRevision = 0;
        string requestedSignature = string.Empty;
        string renderedSignature = string.Empty;
        bool asyncCoalescingValidated = false;
        if (backend == RenderBackendKind.Storm)
        {
            OpenUsdStormChildDiagnostics native = explicitFrame.StormDiagnostics ??
                throw new InvalidOperationException(
                    "Storm explicit-camera diagnostics were unavailable.");
            requestedRevision = native.LatestRequestedRevision;
            requestedSignature = native.LatestRequestedCameraSignature.ToString(
                "X16",
                CultureInfo.InvariantCulture);
            renderedSignature = native.LatestRenderedCameraSignature.ToString(
                "X16",
                CultureInfo.InvariantCulture);
            asyncCoalescingValidated =
                requestedRevision == explicitState.Revision &&
                string.Equals(
                    requestedSignature,
                    explicitFrame.State.NativeCameraSignature,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    renderedSignature,
                    explicitFrame.State.NativeCameraSignature,
                    StringComparison.OrdinalIgnoreCase);
            if (!asyncCoalescingValidated)
            {
                throw new InvalidOperationException(
                    "Storm did not preserve the latest requested and rendered explicit camera.");
            }
        }

        evidence.RecordCameraTransition(new ViewerCameraTransitionEvidence(
            backend.ToString(),
            before.State.Phase,
            explicitFrame.State.Phase,
            restoredFrame.State.Phase,
            before.State.CameraSignature,
            explicitFrame.State.CameraSignature,
            restoredFrame.State.CameraSignature,
            before.Pixel.Sha256,
            explicitFrame.Pixel.Sha256,
            restoredFrame.Pixel.Sha256,
            before.Pixel.Artifact,
            explicitFrame.Pixel.Artifact,
            restoredFrame.Pixel.Artifact,
            before.State.ExactReferencePreserved &&
                explicitFrame.State.ExactReferencePreserved &&
                restoredFrame.State.ExactReferencePreserved,
            requestedRevision,
            requestedSignature,
            renderedSignature,
            asyncCoalescingValidated));
        ViewerStartupOptions.WriteStatus(
            $"Viewer explicit camera evidence: backend={backend}; " +
            $"automatic={before.State.CameraSignature}; " +
            $"explicit={explicitFrame.State.CameraSignature}; " +
            $"restored={restoredFrame.State.CameraSignature}; " +
            $"automaticPixel={before.Pixel.Sha256}; " +
            $"explicitPixel={explicitFrame.Pixel.Sha256}; " +
            $"restoredPixel={restoredFrame.Pixel.Sha256}; " +
            $"exactReferences=True; requestedRevision={requestedRevision}; " +
            $"requestedCamera={requestedSignature}; renderedCamera={renderedSignature}");
    }

    private async Task<(
        ViewerStateEvidence State,
        ViewerPixelEvidence Pixel,
        OpenUsdStormChildDiagnostics? StormDiagnostics)> RecordSwitchingEvidenceAsync(
        ViewerRenderCoordinator coordinator,
        RenderBackendKind backend,
        StageRenderState expectedState,
        string phase,
        bool exerciseInput,
        CancellationToken cancellationToken)
    {
        ViewerSwitchingEvidenceSession evidence = _switchingEvidence ??
            throw new InvalidOperationException("Switching evidence is unavailable.");
        ManagedRenderFrameResult frame =
            await RenderUntilPresentedAsync(coordinator, cancellationToken);
        if (frame.ActiveBackend?.Kind != backend ||
            !ReferenceEquals(expectedState, coordinator.CurrentState))
        {
            throw new InvalidOperationException(
                $"The {backend} evidence frame did not preserve the exact state.");
        }
        ViewerStateEvidence stateEvidence = evidence.RecordState(
            backend,
            expectedState,
            phase,
            exactReferencePreserved: true,
            coordinator.SchedulerEvidenceIdentity,
            coordinator.RenderSourceEvidenceIdentity);
        if (backend is RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or
            RenderBackendKind.Metal)
        {
            ViewerCompositionEvidence composition =
                ViewportHost.GetCompositionRuntimeEvidence(backend);
            evidence.RecordComposition(composition);
            ViewerStartupOptions.WriteStatus(
                $"Viewer runtime compositor observed: backend={backend}; " +
                $"image={composition.UsedImageHandleType}; " +
                $"sync={composition.SynchronizationKind}; " +
                $"luid={composition.DeviceLuid}; imports={composition.SuccessfulImports}; " +
                $"presents={composition.SuccessfulPresents}");
        }
        RecordWindowOwnership(evidence, backend, $"{phase}-after");
        ViewerPixelEvidence pixel = await ViewerWindowsCapture.CaptureAsync(
            this,
            ViewportHost,
            backend,
            phase,
            evidence.ArtifactDirectory,
            cancellationToken);
        for (int attempt = 0;
             pixel.NonBackgroundPixels <
                 Math.Max(100, pixel.Width * (long)pixel.Height / 1000) &&
             attempt < 5;
             attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            _ = await coordinator.RenderAsync(cancellationToken);
            pixel = await ViewerWindowsCapture.CaptureAsync(
                this,
                ViewportHost,
                backend,
                $"{phase}-retry-{attempt + 1:D2}",
                evidence.ArtifactDirectory,
                cancellationToken);
        }
        evidence.RecordPixel(pixel);
        OpenUsdStormChildDiagnostics? stormDiagnostics =
            backend == RenderBackendKind.Storm
                ? ViewportHost.GetActiveStormDiagnostics()
                : null;
        if (OperatingSystem.IsLinux() && backend == RenderBackendKind.Storm)
        {
            ViewerPixelEvidence shellPixel =
                await ViewerWindowsCapture.CaptureLinuxShellAsync(
                    this,
                    ViewportHost,
                    backend,
                    $"{phase}-shell",
                    evidence.ArtifactDirectory,
                    cancellationToken);
            evidence.RecordPixel(shellPixel);
            ViewerStartupOptions.WriteStatus(
                $"Viewer shell pixels: backend={backend}; phase={phase}; " +
                $"sha256={shellPixel.Sha256}; nonBackground={shellPixel.NonBackgroundPixels}");
        }
        ViewerStartupOptions.WriteStatus(
            $"Viewer pixels: backend={backend}; phase={phase}; sha256={pixel.Sha256}; " +
            $"nonBackground={pixel.NonBackgroundPixels}; revision={expectedState.Revision}");
        if (exerciseInput)
        {
            ViewerInputEvidence input = await ViewportHost.ExerciseEvidenceInputAsync(
                this,
                backend,
                cancellationToken);
            evidence.RecordInput(input);
            ViewerStartupOptions.WriteStatus(
                $"Viewer input: backend={backend}; resize={input.ResizeEvents}; " +
                $"scaling={input.ScalingEvents}; focus={input.FocusEvents}; " +
                $"pointer={input.PointerMoves + input.PointerButtons}; " +
                $"wheel={input.WheelEvents}; key={input.KeyEvents}; " +
                $"nativeFocus={input.NativeFocusEvents}; " +
                $"nativePointer={input.NativePointerEvents}; " +
                $"nativeWheel={input.NativeWheelEvents}; nativeKey={input.NativeKeyEvents}; " +
                $"dpi={input.NativeDpiBefore}->{input.NativeDpiObserved}->" +
                $"{input.NativeDpiAfter}; physical={input.PhysicalWidthBefore}x" +
                $"{input.PhysicalHeightBefore}->{input.PhysicalWidthObserved}x" +
                $"{input.PhysicalHeightObserved}->{input.PhysicalWidthAfter}x" +
                $"{input.PhysicalHeightAfter}; api={input.DeliveryApi}");
            await UpdateViewportStateAsync(coordinator, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            if (backend == RenderBackendKind.Storm && OperatingSystem.IsWindows())
            {
                await RecordNativeStormNavigationEvidenceAsync(
                    coordinator,
                    stateEvidence,
                    pixel,
                    phase,
                    cancellationToken);
            }
        }
        return (stateEvidence, pixel, stormDiagnostics);
    }

    private async Task RecordNativeStormNavigationEvidenceAsync(
        ViewerRenderCoordinator coordinator,
        ViewerStateEvidence beforeState,
        ViewerPixelEvidence beforePixel,
        string phase,
        CancellationToken cancellationToken)
    {
        ViewerSwitchingEvidenceSession evidence = _switchingEvidence ??
            throw new InvalidOperationException("Switching evidence is unavailable.");
        StageRenderState automaticState = coordinator.CurrentState;
        if (automaticState.Camera.Mode != CameraMode.Automatic)
        {
            throw new InvalidOperationException(
                "Native Storm navigation evidence must start from Automatic camera mode.");
        }

        ViewerStormNavigationDelivery delivery =
            await ViewportHost.ExerciseStormNavigationGestureAsync(
                this,
                cancellationToken);
        var tracker = new ViewerStormNavigationInputTracker();
        _ = tracker.Update(delivery.Before, routedInputGeneration: 0);
        _ = tracker.Update(delivery.Pressed, routedInputGeneration: 0);
        ViewerStormNavigationDelta moved = tracker.Update(
            delivery.Moved,
            routedInputGeneration: 0);
        _ = tracker.Update(delivery.After, routedInputGeneration: 0);
        if (moved.Gesture != ViewerCameraPointerGesture.Orbit ||
            moved.PointerDelta == Vector2.Zero)
        {
            throw new InvalidOperationException(
                "The OS-routed Storm gesture did not produce an Alt-left orbit delta: " +
                $"before={delivery.Before.Sequence}/" +
                $"{delivery.Before.Buttons}/{delivery.Before.Modifiers}/" +
                $"{delivery.Before.State}@{delivery.Before.PointerX}," +
                $"{delivery.Before.PointerY}; pressed={delivery.Pressed.Sequence}/" +
                $"{delivery.Pressed.Buttons}/{delivery.Pressed.Modifiers}/" +
                $"{delivery.Pressed.State}@{delivery.Pressed.PointerX}," +
                $"{delivery.Pressed.PointerY}; moved={delivery.Moved.Sequence}/" +
                $"{delivery.Moved.Buttons}/{delivery.Moved.Modifiers}/" +
                $"{delivery.Moved.State}@{delivery.Moved.PointerX}," +
                $"{delivery.Moved.PointerY}; delta={moved.Gesture}/" +
                $"{moved.PointerDelta}.");
        }

        CameraState navigatedCamera = default;
        bool changed = await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                bool applied = _cameraNavigation.ApplyGesture(
                    moved.Gesture,
                    moved.PointerDelta);
                navigatedCamera = _cameraNavigation.Camera;
                return applied;
            },
            DispatcherPriority.Send,
            cancellationToken);
        if (!changed || navigatedCamera.Mode != CameraMode.Matrices)
        {
            throw new InvalidOperationException(
                "The native Storm gesture did not change the Viewer camera.");
        }

        try
        {
            await coordinator.MutateStateAsync(
                state => state.WithCamera(navigatedCamera),
                cancellationToken);
            StageRenderState navigatedState = coordinator.CurrentState;
            ManagedRenderFrameResult frame =
                await RenderUntilPresentedAsync(coordinator, cancellationToken);
            if (frame.ActiveBackend?.Kind != RenderBackendKind.Storm ||
                !ReferenceEquals(navigatedState, coordinator.CurrentState))
            {
                throw new InvalidOperationException(
                    "The native Storm navigation frame did not preserve its camera state.");
            }

            string navigationPhase = $"{phase}-native-navigation-after";
            ViewerStateEvidence afterState = evidence.RecordState(
                RenderBackendKind.Storm,
                navigatedState,
                navigationPhase,
                exactReferencePreserved: true,
                coordinator.SchedulerEvidenceIdentity,
                coordinator.RenderSourceEvidenceIdentity);
            RecordWindowOwnership(
                evidence,
                RenderBackendKind.Storm,
                $"{navigationPhase}-after");
            ViewerPixelEvidence afterPixel = await ViewerWindowsCapture.CaptureAsync(
                this,
                ViewportHost,
                RenderBackendKind.Storm,
                navigationPhase,
                evidence.ArtifactDirectory,
                cancellationToken);
            evidence.RecordPixel(afterPixel);
            bool cameraChanged = !string.Equals(
                beforeState.CameraSignature,
                afterState.CameraSignature,
                StringComparison.Ordinal);
            bool pixelChanged = !string.Equals(
                beforePixel.Sha256,
                afterPixel.Sha256,
                StringComparison.OrdinalIgnoreCase);
            if (!cameraChanged || !pixelChanged)
            {
                throw new InvalidOperationException(
                    "The native Storm gesture did not change both camera state and pixels.");
            }

            evidence.RecordNativeNavigation(new ViewerNativeNavigationEvidence(
                "Storm",
                navigationPhase,
                ViewerSwitchingEvidenceArtifact.StormNavigationDeliveryApi,
                ViewerSwitchingEvidenceArtifact.StormNavigationSnapshotApi,
                checked((int)OpenUsdStormChildRuntime.AbiVersion),
                "Alt+Left Orbit",
                delivery.Before.Sequence,
                delivery.Pressed.Sequence,
                delivery.Moved.Sequence,
                delivery.After.Sequence,
                delivery.Pressed.Buttons.ToString(),
                delivery.Pressed.Modifiers.ToString(),
                delivery.Pressed.State.ToString(),
                checked((int)moved.PointerDelta.X),
                checked((int)moved.PointerDelta.Y),
                delivery.AvaloniaRoutedEvents,
                beforeState.CameraSignature,
                afterState.CameraSignature,
                beforePixel.Sha256,
                afterPixel.Sha256,
                beforePixel.Artifact,
                afterPixel.Artifact,
                cameraChanged,
                pixelChanged,
                delivery.Messages));
            ViewerStartupOptions.WriteStatus(
                $"Viewer native navigation: sequence={delivery.Before.Sequence}->" +
                $"{delivery.After.Sequence}; camera={beforeState.CameraSignature}->" +
                $"{afterState.CameraSignature}; pixel={beforePixel.Sha256}->" +
                $"{afterPixel.Sha256}; routedDuplicates={delivery.AvaloniaRoutedEvents}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => _cameraNavigation.ResetToAutomatic(),
                DispatcherPriority.Send,
                CancellationToken.None);
            await coordinator.MutateStateAsync(
                state => state.WithCamera(automaticState.Camera),
                CancellationToken.None);
            _ = await RenderUntilPresentedAsync(
                coordinator,
                CancellationToken.None);
        }
    }

    private void RecordWindowOwnership(
        ViewerSwitchingEvidenceSession evidence,
        RenderBackendKind backend,
        string phase)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        ViewerHwndEvidence ownership =
            evidence.RecordWindowOwnership(this, ViewportHost, backend, phase);
        ViewerStartupOptions.WriteStatus(
            $"Viewer HWND ownership: phase={phase}; backend={backend}; " +
            $"top={ownership.TopLevelHwnd}; expectedStorm={ownership.ExpectedStormHwnd}; " +
            $"observedStorm={ownership.ObservedStormHwnd}; live={ownership.LiveKnownStormCount}; " +
            $"visible={ownership.VisibleStormCount}; stale={ownership.StaleLiveStormCount}; " +
            $"parentOk={ownership.StormParentWithinViewer}; " +
            $"compositionVisible={ownership.CompositionHostVisible}; " +
            $"retiredCleanup={_coordinator?.RetiredCleanupCount ?? 0}");
    }

    private static async Task ApplyVisualEditAsync(
        ViewerRenderCoordinator coordinator,
        int index,
        CancellationToken cancellationToken)
    {
        StageRenderState before = coordinator.CurrentState;
        double translation = (index % 6) switch
        {
            0 => -0.40,
            1 => -0.20,
            2 => 0.00,
            3 => 0.20,
            4 => 0.40,
            _ => 0.60
        };
        await coordinator.Scheduler.EditAsync(
            stage => UsdGeomXformable.Wrap(stage.GetPrim("/World/Cube"))
                .SetLocalTransform(UsdMatrix4d.CreateTranslation(translation, 0, 0)),
            UsdStageInvalidationKind.Property,
            cancellationToken);
        while (ReferenceEquals(before, coordinator.CurrentState))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
    }

    private static async Task<ManagedRenderFrameResult> RenderUntilPresentedAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            ManagedRenderFrameResult result =
                await coordinator.RenderAsync(cancellationToken);
            if (result.Frame?.Status == RenderFrameStatus.Rendered)
            {
                return result;
            }
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Renderer failed during switch evidence: {result.Failure}.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
        }
        throw new TimeoutException("The switched renderer did not present a frame.");
    }

    private async Task RunObservedDiagnosticSequenceAsync(
        ViewerRenderCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunDiagnosticSequenceAsync(coordinator, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            string status = $"Viewer diagnostic sequence failed: {exception.Message}";
            ViewerStartupOptions.WriteStatus(status);
            await Dispatcher.UIThread.InvokeAsync(() => RendererStatus.Text = status);
            if (ViewerStartupOptions.SwitchingEvidenceEnabled ||
                ViewerStartupOptions.PickSmokeEnabled)
            {
                await Dispatcher.UIThread.InvokeAsync(Close);
            }
        }
        finally
        {
            Volatile.Write(ref _diagnosticOwnsRendering, 0);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewerStartupOptions.SharedStageSoak)
        {
            _shutdownComplete = true;
            return;
        }
        if (_shutdownComplete || _shutdownStarted)
        {
            return;
        }
        _shutdownStarted = true;
        e.Cancel = true;
        try
        {
            if (!IsAutomatedViewerRun())
            {
                await SaveSettingsAsync();
            }
            _viewerLifetime.Cancel();
            await _documentGate.WaitAsync();
            try
            {
                await StopCurrentDocumentAsync();
            }
            finally
            {
                _documentGate.Release();
            }
            (
                long childLive,
                long childPeak) = OpenUsdStormChildRuntime.GetChildCounts();
            (
                long managedStorm,
                long nativeStorm,
                _,
                long abandonedStorm) = OpenUsdStormRuntime.GetDiagnostics();
            (
                long managedSilk,
                long nativeSilk,
                _,
                long managedPages,
                long nativePages,
                _,
                long gpuScenes,
                long gpuMeshes) = OpenUsdSilkRuntime.GetDiagnostics();
            ViewerStartupOptions.WriteStatus(
                $"Viewer final resources: child={childLive}; childPeak={childPeak}; " +
                $"managedStorm={managedStorm}; nativeStorm={nativeStorm}; " +
                $"managedSilk={managedSilk}; nativeSilk={nativeSilk}; " +
                $"managedPages={managedPages}; nativePages={nativePages}; " +
                $"gpuScenes={gpuScenes}; gpuMeshes={gpuMeshes}; " +
                $"abandonedStorm={abandonedStorm}");
            _switchingEvidence?.Complete(
                this,
                new ViewerResourceEvidence(
                    childLive,
                    childPeak,
                    managedStorm,
                    nativeStorm,
                    managedSilk,
                    nativeSilk,
                    managedPages,
                    nativePages,
                    gpuScenes,
                    gpuMeshes,
                    abandonedStorm,
                    ViewerStartupOptions.NativeStormContextLoss));
            if (childLive != 0 ||
                managedStorm != 0 ||
                nativeStorm != 0 ||
                managedSilk != 0 ||
                nativeSilk != 0 ||
                managedPages != 0 ||
                nativePages != 0 ||
                gpuScenes != 0 ||
                gpuMeshes != 0)
            {
                throw new InvalidOperationException(
                    "Viewer renderer resources did not return to zero.");
            }
        }
        catch (Exception exception)
        {
            ViewerStartupOptions.WriteStatus(
                $"Viewer renderer shutdown failed: {exception.Message}");
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void SetActiveBackendStatus()
    {
        string backend = _coordinator?.ActiveBackend?.Name ?? "unavailable";
        RendererStatus.Text = $"Renderer: {backend}";
        UpdateCameraStatus();
        RefreshStormNavigationPolling();
        ViewerStartupOptions.WriteStatus($"Active renderer: {backend}");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            StopStormNavigationPolling();
            _stormNavigationTimer.Tick -= OnStormNavigationTick;
            _hostShutdown.Dispose();
            _pickLifetime?.Cancel();
            _viewerLifetime.Cancel();
            _viewerLifetime.Dispose();
            _documentGate.Dispose();
            _settingsStore.Dispose();
        }
    }

    private static int GetRendererSelectionIndex() =>
        GetRendererSelectionIndex(ViewerStartupOptions.Renderer);

    private static int GetRendererSelectionIndex(string renderer) =>
        renderer switch
        {
            "Storm" => 1,
            "D3D12" => 2,
            "Vulkan" => 3,
            "Metal" => 4,
            _ => 0
        };

    private string GetSelectedRendererPreference() =>
        RendererSelector.SelectedIndex switch
        {
            1 => "Storm",
            2 => "D3D12",
            3 => "Vulkan",
            4 => "Metal",
            _ => "Auto"
        };

    private RenderBackendKind? GetSelectedBackend() =>
        RendererSelector.SelectedIndex switch
        {
            1 => RenderBackendKind.Storm,
            2 => RenderBackendKind.D3D12,
            3 => RenderBackendKind.Vulkan,
            4 => RenderBackendKind.Metal,
            _ => null
        };

    private async void OnSharedStageSoakOpened(object? sender, EventArgs e)
    {
        Opened -= OnSharedStageSoakOpened;
        await RunSharedStageSoakAsync();
    }

    private async Task RunSharedStageSoakAsync()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        UsdStageScheduler? scheduler = null;
        UsdStageRenderSource? source = null;
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMinutes(Math.Max(10, ViewerStartupOptions.SoakSeconds / 60 + 5)));
        try
        {
            string stagePath = ViewerStartupOptions.StagePath!;
            string pluginPath = ViewerStartupOptions.PluginPath!;
            OpenUsdStormRuntime.ResetDiagnosticPeak();
            SharedStageSoak.ResetDiagnosticPeaks();
            SharedStageResourceSnapshot baseline =
                SharedStageSoak.CaptureResources(
                    null,
                    _soakStormViewport!.GetSoakDiagnostics);
            scheduler = UsdStageScheduler.Open(
                stagePath,
                capacity: 1024,
                notificationCapacity: 8);
            source = await scheduler.AcquireRenderSourceAsync(timeout.Token);
            _soakStormViewport!.SetRenderSource(
                scheduler,
                source,
                StormStageOwnership.FullyBorrowed);
            _soakStormViewport.RequestSoakFrame();
            await WaitForFirstStormFrameAsync(timeout.Token);

            SharedStageSoakResult result = await SharedStageSoak.RunAsync(
                pluginPath,
                scheduler,
                source,
                new SharedStageSoakOptions
                {
                    AssetPath = Path.Combine(
                        Path.GetDirectoryName(stagePath)!,
                        "shared-stage-soak-asset.usda"),
                    MinimumDuration =
                        TimeSpan.FromSeconds(ViewerStartupOptions.SoakSeconds),
                    Timeout = TimeSpan.FromMinutes(
                        Math.Max(8, ViewerStartupOptions.SoakSeconds / 60 + 4)),
                    RequestStormFrame = _soakStormViewport.RequestSoakFrame,
                    GetStormFrameCount = () => _soakStormViewport.SoakFrameCount,
                    GetRendererDiagnostics = _soakStormViewport.GetSoakDiagnostics,
                    SimulateContextLossAsync =
                        _soakStormViewport.SimulateContextLossForSoakAsync,
                    BuildIdentity = SharedStageBuildIdentity.FromEnvironment(),
                    BaselineResources = baseline,
                    CreateGraphicsDevice = CreateGraphicsDevice,
                    ReportStatus = ViewerStartupOptions.WriteStatus
                },
                timeout.Token);

            await _soakStormViewport.ShutdownSoakRendererAsync(timeout.Token);
            source.Dispose();
            source = null;
            await scheduler.DisposeAsync();
            scheduler = null;
            SharedStageRendererDiagnostics finalRendererDiagnostics =
                _soakStormViewport.GetSoakDiagnostics();
            result = result.WithResourcesReleased(
                SharedStageSoak.CaptureResources(
                    null,
                    () => finalRendererDiagnostics),
                finalRendererDiagnostics);
            SharedStageSoak.WriteArtifact(
                ViewerStartupOptions.SoakArtifactPath!,
                result);
            ViewerStartupOptions.WriteStatus(
                $"Shared-stage soak passed: edits={result.MutatingOperations}; " +
                $"reads={result.ReadOperations}; " +
                $"frames={result.StormFrames}; syncs={result.SilkSyncPages}; " +
                $"upserts={result.SilkMeshUpserts}; removals={result.SilkMeshRemovals}; " +
                $"resourcesReleased={result.ResourcesReleased}");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            SharedStageSoak.WriteFailureArtifact(
                ViewerStartupOptions.SoakArtifactPath!,
                startedAt,
                exception);
            ViewerStartupOptions.WriteStatus(
                $"Shared-stage soak failed: {exception.Message}");
            Shutdown(1);
        }
        finally
        {
            source?.Dispose();
            if (scheduler is not null)
            {
                try
                {
                    await scheduler.DisposeAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private async Task WaitForFirstStormFrameAsync(CancellationToken cancellationToken)
    {
        while (_soakStormViewport!.SoakFrameCount == 0)
        {
            _soakStormViewport.RequestSoakFrame();
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private static void Shutdown(int exitCode)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(exitCode);
        }
    }

    private static ISilkGraphicsDevice CreateGraphicsDevice()
    {
        if (OperatingSystem.IsWindows())
        {
            return D3D12SilkGraphicsDevice.Create(useWarp: true);
        }
        if (OperatingSystem.IsMacOS())
        {
            return MetalSilkGraphicsDevice.Create();
        }
        return VulkanSilkGraphicsDevice.Create();
    }
}
