// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private CommandPaletteWindow? _commandPalette;
    private CaptureComparisonWindow? _captureComparison;
    private readonly Dictionary<MenuItem, ViewerWorkspacePreset> _workspaceItems = [];

    private bool WorkspaceOwnsKeyboard =>
        _commandPalette is { IsActive: true } || _captureComparison is { IsActive: true } ||
        _propertyEditor is { IsActive: true } || _documentChanges is { IsActive: true } ||
        _documentAction is { IsActive: true } || _renderSequenceWindow is { IsActive: true };

    private void InitializeWorkspaceCommands()
    {
        _workspaceItems.Add(WorkspaceReviewMenuItem, ViewerWorkspacePreset.Review);
        _workspaceItems.Add(WorkspaceInspectMenuItem, ViewerWorkspacePreset.Inspect);
        _workspaceItems.Add(WorkspaceMaterialsMenuItem, ViewerWorkspacePreset.MaterialsLighting);
        _workspaceItems.Add(WorkspacePresentationMenuItem, ViewerWorkspacePreset.Presentation);
        foreach (MenuItem item in _workspaceItems.Keys)
        {
            item.Click += OnWorkspacePresetClick;
        }
        WorkspaceMenu.SubmenuOpened += (_, _) => SyncWorkspaceMenu();
        CommandPaletteMenuItem.Click += (_, _) => ShowCommandPalette();
        CompareCapturesMenuItem.Click += (_, _) => ShowCaptureComparison();
        RenderImageSequenceMenuItem.Click += (_, _) => ShowRenderImageSequence();
        FindPrimMenuItem.Click += (_, _) =>
        {
            RevealWorkspacePanel(stage: true, selectedTabId: CurrentSelectedTabId());
            HierarchyFilter.Focus();
            HierarchyFilter.SelectAll();
        };
        InspectSelectionMenuItem.Click += (_, _) =>
            RevealWorkspacePanel(stage: false, ViewerInspectorLayoutPolicy.PropertiesTabId);
        _commands.Connect(OpenStageButton, ViewerCommandIds.FileOpenStage);
        _commands.Connect(ReloadStageButton, ViewerCommandIds.FileReloadStage);
        _commands.Connect(FrameSelectedButton, ViewerCommandIds.CameraFrameSelected);
        _commands.Connect(CommandPaletteButton, ViewerCommandIds.ViewCommandPalette);
        _commands.Connect(WorkspaceInspectButton, ViewerCommandIds.ViewInspectSelection);
        _commands.Connect(WorkspaceCaptureButton, ViewerCommandIds.FileCaptureFrame);
        _commands.Connect(AppearanceMaterialsButton, ViewerCommandIds.RenderSceneMaterials);
        _commands.Connect(AppearanceLightingButton, ViewerCommandIds.RenderSceneLighting);
        _commands.Connect(AppearanceShadowsButton, ViewerCommandIds.RenderSceneShadows);
        _commands.Connect(AppearanceCullingButton, ViewerCommandIds.RenderBackfaceCulling);
        _commands.Connect(AppearanceSmoothButton, ViewerCommandIds.RenderDrawModeSmoothShaded);
        _commands.Connect(AppearanceColorButton, ViewerCommandIds.RenderColorManagementEnabled);
        _commands.Connect(AppearanceConfigButton, ViewerCommandIds.RenderColorManagementChooseConfig);
        _commands.Connect(AppearanceClearConfigButton, ViewerCommandIds.RenderColorManagementClearConfig);
        _commands.Connect(AppearanceInspectButton, ViewerCommandIds.ViewInspectSelection);
        StagePanelSplitter.AddHandler(PointerPressedEvent, PrepareWorkspaceSplitter, RoutingStrategies.Tunnel);
        StagePanelSplitter.AddHandler(KeyDownEvent, PrepareWorkspaceSplitter, RoutingStrategies.Tunnel);
        InspectorPanelSplitter.AddHandler(PointerPressedEvent, PrepareWorkspaceSplitter, RoutingStrategies.Tunnel);
        InspectorPanelSplitter.AddHandler(KeyDownEvent, PrepareWorkspaceSplitter, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => UpdateWorkspacePanelBudget();
        SyncWorkspaceMenu();
    }

    private async void OnWorkspacePresetClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not MenuItem item || !_workspaceItems.TryGetValue(item, out ViewerWorkspacePreset preset))
        {
            throw new ArgumentException("The workspace menu item has no preset.", nameof(sender));
        }
        _settings = ViewerInspectorLayoutPolicy.ApplyPreset(CaptureSettings(), preset);
        ApplyLayoutSettings(_settings);
        ViewerStatus.Text = "Workspace applied. Scene edits, renderer, pick, theme and colour choices are unchanged.";
        if (!IsAutomatedViewerRun())
        {
            await SaveSettingsAsync();
        }
    }

    private void ApplyLayoutSettings(ViewerSettings settings)
    {
        _applyingLayout = true;
        try
        {
            // Set the columns before visibility captures their old widths.
            _stagePanelWidth = settings.StagePanelWidth;
            _inspectorPanelWidth = settings.InspectorPanelWidth;
            StagePanelGridColumn.Width = new GridLength(_stagePanelWidth);
            InspectorPanelGridColumn.Width = new GridLength(_inspectorPanelWidth);
            StagePanelMenuItem.IsChecked = settings.StagePanelVisible;
            InspectorPanelMenuItem.IsChecked = settings.InspectorPanelVisible;
            TimelineMenuItem.IsChecked = settings.TimelineVisible;
            DiagnosticsMenuItem.IsChecked = settings.DiagnosticsVisible;
            ToolsDiagnosticsTabVisibleMenuItem.IsChecked = settings.DiagnosticsVisible;
            HydraTabVisibleMenuItem.IsChecked = settings.HydraVisible;
            ToolsHydraTabVisibleMenuItem.IsChecked = settings.HydraVisible;
            TfDebugTabVisibleMenuItem.IsChecked = settings.TfDebugVisible;
            ToolsTfDebugTabVisibleMenuItem.IsChecked = settings.TfDebugVisible;
            ApplyPanelVisibility(
                settings.StagePanelVisible, settings.InspectorPanelVisible, settings.TimelineVisible,
                settings.DiagnosticsVisible, settings.HydraVisible, settings.TfDebugVisible, settings.SelectedTabId);
        }
        finally
        {
            _applyingLayout = false;
        }
        SyncWorkspaceMenu();
    }

    private void RevealWorkspacePanel(bool stage, string selectedTabId)
    {
        ViewerSettings layout = CaptureSettings() with { SelectedTabId = selectedTabId };
        ApplyLayoutSettings(stage
            ? layout with { StagePanelVisible = true }
            : layout with { InspectorPanelVisible = true });
    }

    private void SyncWorkspaceMenu()
    {
        ViewerSettings current = CaptureSettings();
        foreach ((MenuItem item, ViewerWorkspacePreset preset) in _workspaceItems)
        {
            item.IsChecked = ViewerInspectorLayoutPolicy.ApplyPreset(current, preset) == current;
        }
    }

    private void UpdateWorkspacePanelBudget()
    {
        // MaxWidth constrains presentation without replacing the persisted splitter widths.
        double stage = StagePanel.IsVisible ? StagePanelGridColumn.Width.Value : 0;
        double inspector = InspectorPanel.IsVisible ? InspectorPanelGridColumn.Width.Value : 0;
        double available = Math.Max(360, Bounds.Width - 430);
        double minimum = ViewerSettings.MinimumPanelWidth;
        double stageExtra = Math.Max(0, stage - minimum);
        double inspectorExtra = Math.Max(0, inspector - minimum);
        double minimumTotal = (stage > 0 ? minimum : 0) + (inspector > 0 ? minimum : 0);
        double extras = stageExtra + inspectorExtra;
        double scale = extras > 0 ? Math.Min(1, Math.Max(0, available - minimumTotal) / extras) : 1;
        StagePanelGridColumn.MinWidth = stage > 0 ? ViewerSettings.MinimumPanelWidth : 0;
        InspectorPanelGridColumn.MinWidth = inspector > 0 ? ViewerSettings.MinimumPanelWidth : 0;
        StagePanelGridColumn.MaxWidth = stage > 0
            ? minimum + (stageExtra * scale) : 0;
        InspectorPanelGridColumn.MaxWidth = inspector > 0
            ? minimum + (inspectorExtra * scale) : 0;
    }

    private void PrepareWorkspaceSplitter(object? sender, RoutedEventArgs e)
    {
        bool stage = ReferenceEquals(sender, StagePanelSplitter);
        ColumnDefinition target = stage ? StagePanelGridColumn : InspectorPanelGridColumn;
        ColumnDefinition peer = stage ? InspectorPanelGridColumn : StagePanelGridColumn;
        // The passive fit cap must not become a permanent limit on an operator's resize.
        target.MaxWidth = Math.Clamp(
            Math.Max(360, Bounds.Width - 430) - peer.ActualWidth,
            ViewerSettings.MinimumPanelWidth,
            ViewerSettings.MaximumPanelWidth);
    }

    private void ShowCommandPalette()
    {
        if (_commandPalette is { } existing)
        {
            existing.Activate();
            return;
        }
        SyncWorkspaceMenu();
        WorkspaceFocus previousFocus = CaptureWorkspaceFocus();
        var palette = new CommandPaletteWindow(_commands, message => ViewerStatus.Text = message);
        _commandPalette = palette;
        palette.Closed += (_, _) =>
        {
            _commandPalette = null;
            RestoreWorkspaceFocus(previousFocus);
        };
        palette.Show(this);
    }

    private void ShowCaptureComparison()
    {
        if (_captureComparison is { } existing)
        {
            existing.Activate();
            return;
        }
        WorkspaceFocus previousFocus = CaptureWorkspaceFocus();
        var comparison = new CaptureComparisonWindow();
        _captureComparison = comparison;
        comparison.Closed += (_, _) =>
        {
            _captureComparison = null;
            RestoreWorkspaceFocus(previousFocus);
        };
        comparison.Show(this);
    }

    private WorkspaceFocus CaptureWorkspaceFocus()
    {
        StormNativeControlHost? source = ViewportHost.GetActiveStormNavigationSource();
        bool nativeFocused = source is not null && source.TryGetNavigationInput(out var input) && input.Focused;
        return new WorkspaceFocus(FocusManager?.GetFocusedElement(), nativeFocused ? source : null);
    }

    private void RestoreWorkspaceFocus(WorkspaceFocus previousFocus)
    {
        _cameraShortcutRepeat.Reset();
        _physicsShortcutRepeat.Reset();
        _stormNavigationInput.Reset();
        _stormPickInput.Reset();
        if (!_shutdownStarted)
        {
            Activate();
            if (previousFocus.NativeViewport is { } native &&
                ReferenceEquals(native, ViewportHost.GetActiveStormNavigationSource()) &&
                ViewportHost.IsEffectivelyVisible)
            {
                native.FocusNativeViewport();
            }
            else if (previousFocus.Element?.Focus() != true)
            {
                CommandPaletteButton.Focus();
            }
        }
    }

    private readonly record struct WorkspaceFocus(IInputElement? Element, StormNativeControlHost? NativeViewport);

    private bool TryHandleWorkspaceShortcut(KeyEventArgs e)
    {
        bool control = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (control && e.Key is Key.Z or Key.Y)
        {
            if (IsCameraShortcutEditing())
            {
                return false;
            }
            if (!_physicsShortcutRepeat.TryPress(e.Key))
            {
                return true;
            }
        }
        return (control || e.Key == Key.F1) && _commands.TryExecuteGesture(e);
    }
}
