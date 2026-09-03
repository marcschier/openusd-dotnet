// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Builds the menu-first shell: maps stable inspector-tab identities to their <see
/// cref="TabItem"/>, applies the accessible names <see cref="ViewerCommandCatalog"/> already
/// declares, and wires every menu item this workstream adds. Existing toolbar/tab controls and
/// their handlers are untouched; menu items that replace a moved control either call the same
/// handler directly (stateless actions) or set the state of a control that remains the source
/// of truth for existing logic and is no longer shown directly, which raises the same
/// SelectionChanged/Click/IsCheckedChanged event that logic already subscribes to.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Maps every stable <see cref="ViewerInspectorLayoutPolicy"/> tab identity to the
    /// concrete <see cref="TabItem"/> it names today, so tab-selection logic can be driven
    /// by the pure policy's string identities instead of a visual index or a hard-coded
    /// control reference.
    /// </summary>
    private Dictionary<string, TabItem> BuildInspectorTabsById() => new(StringComparer.Ordinal)
    {
        [ViewerInspectorLayoutPolicy.PropertiesTabId] = PropertiesTab,
        [ViewerInspectorLayoutPolicy.ValueTabId] = ValueTab,
        [ViewerInspectorLayoutPolicy.MetadataTabId] = MetadataTab,
        [ViewerInspectorLayoutPolicy.CompositionTabId] = CompositionTab,
        [ViewerInspectorLayoutPolicy.LayersTabId] = LayersTab,
        [ViewerInspectorLayoutPolicy.DiagnosticsTabId] = DiagnosticsTab,
        [ViewerInspectorLayoutPolicy.ValidationTabId] = ValidationTab,
        [ViewerInspectorLayoutPolicy.HydraTabId] = HydraSceneTab,
        [ViewerInspectorLayoutPolicy.PhysicsTabId] = PhysicsTab,
        [ViewerInspectorLayoutPolicy.TfDebugTabId] = TfDebugTab,
    };

    /// <summary>
    /// Applies the accessible name <see cref="ViewerCommandCatalog"/> already declares for
    /// today's menu items, buttons, and checkable controls, so the catalog is the live
    /// source automation reads rather than a second copy of what XAML already sets.
    /// </summary>
    private void ApplyCommandAccessibleNames()
    {
        static void setAccessibleName(StyledElement element, string commandId) =>
            AutomationProperties.SetName(element, ViewerCommandCatalog.Get(commandId).AccessibleName);

        setAccessibleName(OpenStageButton, ViewerCommandIds.FileOpenStage);
        setAccessibleName(OpenStageMenuItem, ViewerCommandIds.FileOpenStage);
        setAccessibleName(ReloadStageButton, ViewerCommandIds.FileReloadStage);
        setAccessibleName(ReloadStageMenuItem, ViewerCommandIds.FileReloadStage);
        setAccessibleName(CaptureFrameMenuItem, ViewerCommandIds.FileCaptureFrame);
        setAccessibleName(RecentStagesMenu, ViewerCommandIds.FileRecentStages);
        setAccessibleName(FileExitMenuItem, ViewerCommandIds.FileExit);

        setAccessibleName(StagePanelMenuItem, ViewerCommandIds.ViewStagePanel);
        setAccessibleName(InspectorPanelMenuItem, ViewerCommandIds.ViewInspectorPanel);
        setAccessibleName(TimelineMenuItem, ViewerCommandIds.ViewTimeline);
        setAccessibleName(DiagnosticsMenuItem, ViewerCommandIds.ViewDiagnosticsTab);
        setAccessibleName(HydraTabVisibleMenuItem, ViewerCommandIds.ViewHydraTabVisible);
        setAccessibleName(TfDebugTabVisibleMenuItem, ViewerCommandIds.ViewTfDebugTabVisible);
        setAccessibleName(SnapTimelineCheckBox, ViewerCommandIds.ViewSnapTimelineToFrames);
        setAccessibleName(ResetLayoutMenuItem, ViewerCommandIds.ViewResetLayout);

        setAccessibleName(RenderRendererAutoMenuItem, ViewerCommandIds.RenderRendererAuto);
        setAccessibleName(RenderRendererStormMenuItem, ViewerCommandIds.RenderRendererStorm);
        setAccessibleName(RenderRendererD3D12MenuItem, ViewerCommandIds.RenderRendererD3D12);
        setAccessibleName(RenderRendererVulkanMenuItem, ViewerCommandIds.RenderRendererVulkan);
        setAccessibleName(RenderRendererMetalMenuItem, ViewerCommandIds.RenderRendererMetal);
        setAccessibleName(
            RenderDrawModeWireframeMenuItem, ViewerCommandIds.RenderDrawModeWireframe);
        setAccessibleName(
            RenderDrawModeWireframeOnSurfaceMenuItem,
            ViewerCommandIds.RenderDrawModeWireframeOnSurface);
        setAccessibleName(
            RenderDrawModeSmoothShadedMenuItem, ViewerCommandIds.RenderDrawModeSmoothShaded);
        setAccessibleName(
            RenderDrawModeFlatShadedMenuItem, ViewerCommandIds.RenderDrawModeFlatShaded);
        setAccessibleName(RenderDrawModePointsMenuItem, ViewerCommandIds.RenderDrawModePoints);
        setAccessibleName(
            RenderDrawModeGeomOnlyMenuItem, ViewerCommandIds.RenderDrawModeGeomOnly);
        setAccessibleName(
            RenderDrawModeGeomFlatMenuItem, ViewerCommandIds.RenderDrawModeGeomFlat);
        setAccessibleName(
            RenderDrawModeGeomSmoothMenuItem, ViewerCommandIds.RenderDrawModeGeomSmooth);
        setAccessibleName(
            RenderDrawModeHiddenSurfaceWireframeMenuItem,
            ViewerCommandIds.RenderDrawModeHiddenSurfaceWireframe);
        setAccessibleName(RenderPurposeDefaultMenuItem, ViewerCommandIds.RenderPurposeDefault);
        setAccessibleName(RenderPurposeProxyMenuItem, ViewerCommandIds.RenderPurposeProxy);
        setAccessibleName(RenderPurposeRenderMenuItem, ViewerCommandIds.RenderPurposeRender);
        setAccessibleName(RenderPurposeGuideMenuItem, ViewerCommandIds.RenderPurposeGuide);
        setAccessibleName(RenderSceneLightingMenuItem, ViewerCommandIds.RenderSceneLighting);
        setAccessibleName(RenderSceneShadowsMenuItem, ViewerCommandIds.RenderSceneShadows);
        setAccessibleName(RenderBackfaceCullingMenuItem, ViewerCommandIds.RenderBackfaceCulling);
        setAccessibleName(RenderSceneMaterialsMenuItem, ViewerCommandIds.RenderSceneMaterials);
        setAccessibleName(
            RenderColorManagementEnabledMenuItem,
            ViewerCommandIds.RenderColorManagementEnabled);
        setAccessibleName(
            RenderColorManagementChooseConfigMenuItem,
            ViewerCommandIds.RenderColorManagementChooseConfig);
        setAccessibleName(
            RenderColorManagementClearConfigMenuItem,
            ViewerCommandIds.RenderColorManagementClearConfig);
        setAccessibleName(
            RenderBackgroundColorBlackMenuItem, ViewerCommandIds.RenderBackgroundColorBlack);
        setAccessibleName(
            RenderBackgroundColorDarkGrayMenuItem,
            ViewerCommandIds.RenderBackgroundColorDarkGray);
        setAccessibleName(
            RenderBackgroundColorLightGrayMenuItem,
            ViewerCommandIds.RenderBackgroundColorLightGray);
        setAccessibleName(
            RenderBackgroundColorWhiteMenuItem, ViewerCommandIds.RenderBackgroundColorWhite);

        setAccessibleName(ResetCameraAutomaticMenuItem, ViewerCommandIds.CameraResetAutomatic);
        setAccessibleName(ResetCameraLegacyMenuItem, ViewerCommandIds.CameraResetLegacyPose);
        setAccessibleName(
            ToggleCameraProjectionMenuItem, ViewerCommandIds.CameraToggleProjection);
        setAccessibleName(UseSelectedCameraMenuItem, ViewerCommandIds.CameraUseSelectedCamera);
        setAccessibleName(StageCamerasMenu, ViewerCommandIds.CameraStageCameras);
        setAccessibleName(FrameSelectedButton, ViewerCommandIds.CameraFrameSelected);
        setAccessibleName(FrameSelectedMenuItem, ViewerCommandIds.CameraFrameSelected);
        setAccessibleName(CameraOrbitLeftMenuItem, ViewerCommandIds.CameraOrbitLeft);
        setAccessibleName(CameraOrbitRightMenuItem, ViewerCommandIds.CameraOrbitRight);
        setAccessibleName(CameraOrbitUpMenuItem, ViewerCommandIds.CameraOrbitUp);
        setAccessibleName(CameraOrbitDownMenuItem, ViewerCommandIds.CameraOrbitDown);

        setAccessibleName(PhysicsEnableButton, ViewerCommandIds.PhysicsEnable);
        setAccessibleName(PhysicsPlayPauseMenuItem, ViewerCommandIds.PhysicsPlayPause);
        setAccessibleName(PhysicsStopMenuItem, ViewerCommandIds.PhysicsStop);
        setAccessibleName(PhysicsStepMenuItem, ViewerCommandIds.PhysicsStep);
        setAccessibleName(PhysicsLoopMenuItem, ViewerCommandIds.PhysicsLoop);
        setAccessibleName(PhysicsSpeedQuarterMenuItem, ViewerCommandIds.PhysicsSpeedQuarter);
        setAccessibleName(PhysicsSpeedHalfMenuItem, ViewerCommandIds.PhysicsSpeedHalf);
        setAccessibleName(PhysicsSpeedNormalMenuItem, ViewerCommandIds.PhysicsSpeedNormal);
        setAccessibleName(PhysicsSpeedDoubleMenuItem, ViewerCommandIds.PhysicsSpeedDouble);
        setAccessibleName(
            PhysicsSpeedQuadrupleMenuItem, ViewerCommandIds.PhysicsSpeedQuadruple);
        setAccessibleName(PhysicsPreviewMenuItem, ViewerCommandIds.PhysicsPreviewApply);
        setAccessibleName(PhysicsBakeButton, ViewerCommandIds.PhysicsBake);
        setAccessibleName(PhysicsGizmoNoneMenuItem, ViewerCommandIds.PhysicsGizmoNone);
        setAccessibleName(PhysicsGizmoMoveMenuItem, ViewerCommandIds.PhysicsGizmoMove);
        setAccessibleName(PhysicsGizmoRotateMenuItem, ViewerCommandIds.PhysicsGizmoRotate);
        setAccessibleName(PhysicsGizmoScaleMenuItem, ViewerCommandIds.PhysicsGizmoScale);
        setAccessibleName(PhysicsGizmoDragMenuItem, ViewerCommandIds.PhysicsGizmoDrag);
        setAccessibleName(PhysicsSnapMenuItem, ViewerCommandIds.PhysicsSnap);
        setAccessibleName(PhysicsUndoButton, ViewerCommandIds.PhysicsUndo);
        setAccessibleName(PhysicsRedoButton, ViewerCommandIds.PhysicsRedo);
        setAccessibleName(PhysicsShowInspectorMenuItem, ViewerCommandIds.PhysicsShowInspector);
        setAccessibleName(
            PhysicsRefreshPropertiesButton, ViewerCommandIds.PhysicsRefreshProperties);
        setAccessibleName(PhysicsApplyPropertyButton, ViewerCommandIds.PhysicsApplyProperty);
        setAccessibleName(PhysicsClearPropertyButton, ViewerCommandIds.PhysicsClearProperty);
        setAccessibleName(PhysicsApplyForceButton, ViewerCommandIds.PhysicsApplyForce);
        setAccessibleName(PhysicsApplyImpulseButton, ViewerCommandIds.PhysicsApplyImpulse);
        setAccessibleName(PhysicsApplyTorqueButton, ViewerCommandIds.PhysicsApplyTorque);
        setAccessibleName(PhysicsWakeButton, ViewerCommandIds.PhysicsWake);
        setAccessibleName(PhysicsSleepButton, ViewerCommandIds.PhysicsSleep);
        setAccessibleName(
            PhysicsControllerDriveCheckBox, ViewerCommandIds.PhysicsControllerDrive);
        setAccessibleName(PhysicsVehicleDriveCheckBox, ViewerCommandIds.PhysicsVehicleDrive);

        setAccessibleName(ToolsValidationRunMenuItem, ViewerCommandIds.ToolsValidationRun);
        setAccessibleName(RefreshValidationButton, ViewerCommandIds.ToolsValidationRun);
        setAccessibleName(
            ToolsValidationScopeStageMenuItem, ViewerCommandIds.ToolsValidationScopeStage);
        setAccessibleName(
            ToolsValidationScopePrimMenuItem, ViewerCommandIds.ToolsValidationScopePrim);
        setAccessibleName(ToolsPickModePrimsMenuItem, ViewerCommandIds.ToolsPickModePrims);
        setAccessibleName(ToolsPickModeModelsMenuItem, ViewerCommandIds.ToolsPickModeModels);
        setAccessibleName(
            ToolsPickModeInstancesMenuItem, ViewerCommandIds.ToolsPickModeInstances);
        setAccessibleName(
            ToolsPickModePrototypesMenuItem, ViewerCommandIds.ToolsPickModePrototypes);
        setAccessibleName(
            ToolsPickTargetPrimitiveMenuItem, ViewerCommandIds.ToolsPickTargetPrimitive);
        setAccessibleName(ToolsPickTargetFaceMenuItem, ViewerCommandIds.ToolsPickTargetFace);
        setAccessibleName(ToolsPickTargetEdgeMenuItem, ViewerCommandIds.ToolsPickTargetEdge);
        setAccessibleName(ToolsPickTargetPointMenuItem, ViewerCommandIds.ToolsPickTargetPoint);
        setAccessibleName(
            ToolsSelectionVisibleOnlyMenuItem, ViewerCommandIds.ToolsSelectionVisibleOnly);
        setAccessibleName(ToolsSelectionXRayMenuItem, ViewerCommandIds.ToolsSelectionXRay);
        setAccessibleName(
            ToolsCopyDiagnosticsMenuItem, ViewerCommandIds.ToolsDeveloperCopyDiagnostics);
        setAccessibleName(CopyDiagnosticsButton, ViewerCommandIds.ToolsDeveloperCopyDiagnostics);
        setAccessibleName(
            ToolsExportDiagnosticsMenuItem, ViewerCommandIds.ToolsDeveloperExportDiagnostics);
        setAccessibleName(
            ExportDiagnosticsButton, ViewerCommandIds.ToolsDeveloperExportDiagnostics);
        setAccessibleName(
            ToolsIncludeDiagnosticPathsMenuItem,
            ViewerCommandIds.ToolsDeveloperIncludeDiagnosticPaths);
        setAccessibleName(
            IncludeDiagnosticPathsCheckBox,
            ViewerCommandIds.ToolsDeveloperIncludeDiagnosticPaths);
        setAccessibleName(
            ToolsDiagnosticsTabVisibleMenuItem, ViewerCommandIds.ViewDiagnosticsTab);
        setAccessibleName(
            ToolsRefreshHydraSceneMenuItem, ViewerCommandIds.ToolsDeveloperRefreshHydraScene);
        setAccessibleName(
            RefreshHydraSceneButton, ViewerCommandIds.ToolsDeveloperRefreshHydraScene);
        setAccessibleName(ToolsHydraTabVisibleMenuItem, ViewerCommandIds.ViewHydraTabVisible);
        setAccessibleName(
            ToolsRefreshTfDebugMenuItem, ViewerCommandIds.ToolsDeveloperRefreshTfDebug);
        setAccessibleName(RefreshTfDebugButton, ViewerCommandIds.ToolsDeveloperRefreshTfDebug);
        setAccessibleName(ToolsTfDebugTabVisibleMenuItem, ViewerCommandIds.ViewTfDebugTabVisible);
        setAccessibleName(
            ToolsOmniverseBridgeMenuItem, ViewerCommandIds.ToolsConnectionsOmniverseBridge);

        setAccessibleName(ShortcutsMenuItem, ViewerCommandIds.HelpShortcuts);
        setAccessibleName(AboutMenuItem, ViewerCommandIds.HelpAbout);
    }

    /// <summary>
    /// Reflects the hidden viewport-display controls and the visible renderer selector into the
    /// Render menu's radio and check items. This is the single place Render menu state is
    /// derived from: call it whenever authoritative renderer/display state or its availability
    /// changes (stage open/close, backend switch or failover, a rejected mutation, or the
    /// controls' own enablement), so the menu never shows a blank or stale radio/check state.
    /// The actual index/flag-to-menu-state mapping lives in the pure, independently tested
    /// <see cref="ViewerRenderMenuSync"/>; this method is only the live-control adapter.
    /// </summary>
    private void SyncRenderMenuFromState()
    {
        ViewerRenderMenuState state = ViewerRenderMenuSync.Compute(new ViewerRenderMenuInput(
            RendererIndex: RendererSelector.SelectedIndex,
            RendererEnabled: RendererSelector.IsEnabled,
            DrawModeIndex: ViewportDrawModeSelector.SelectedIndex,
            DrawModeEnabled: ViewportDrawModeSelector.IsEnabled,
            PurposeDefault: PurposeDefaultCheckBox.IsChecked == true,
            PurposeProxy: PurposeProxyCheckBox.IsChecked == true,
            PurposeRender: PurposeRenderCheckBox.IsChecked == true,
            PurposeGuide: PurposeGuideCheckBox.IsChecked == true,
            PurposesEnabled: PurposeDefaultCheckBox.IsEnabled,
            SceneLighting: SceneLightingCheckBox.IsChecked == true,
            SceneShadows: SceneShadowsCheckBox.IsChecked == true,
            BackfaceCulling: BackfaceCullingCheckBox.IsChecked == true,
            SceneMaterials: SceneMaterialsCheckBox.IsChecked == true,
            SceneTogglesEnabled: SceneLightingCheckBox.IsEnabled,
            BackgroundColorIndex: BackgroundColorSelector.SelectedIndex,
            BackgroundColorEnabled: BackgroundColorSelector.IsEnabled));

        ApplyRadioStates(
            state.Renderer,
            RenderRendererAutoMenuItem,
            RenderRendererStormMenuItem,
            RenderRendererD3D12MenuItem,
            RenderRendererVulkanMenuItem,
            RenderRendererMetalMenuItem);
        ApplyRadioStates(
            state.DrawMode,
            RenderDrawModeWireframeMenuItem,
            RenderDrawModeWireframeOnSurfaceMenuItem,
            RenderDrawModeSmoothShadedMenuItem,
            RenderDrawModeFlatShadedMenuItem,
            RenderDrawModePointsMenuItem,
            RenderDrawModeGeomOnlyMenuItem,
            RenderDrawModeGeomFlatMenuItem,
            RenderDrawModeGeomSmoothMenuItem,
            RenderDrawModeHiddenSurfaceWireframeMenuItem);
        ApplyItemState(RenderPurposeDefaultMenuItem, state.PurposeDefault);
        ApplyItemState(RenderPurposeProxyMenuItem, state.PurposeProxy);
        ApplyItemState(RenderPurposeRenderMenuItem, state.PurposeRender);
        ApplyItemState(RenderPurposeGuideMenuItem, state.PurposeGuide);
        ApplyItemState(RenderSceneLightingMenuItem, state.SceneLighting);
        ApplyItemState(RenderSceneShadowsMenuItem, state.SceneShadows);
        ApplyItemState(RenderBackfaceCullingMenuItem, state.BackfaceCulling);
        ApplyItemState(RenderSceneMaterialsMenuItem, state.SceneMaterials);
        ApplyRadioStates(
            state.BackgroundColor,
            RenderBackgroundColorBlackMenuItem,
            RenderBackgroundColorDarkGrayMenuItem,
            RenderBackgroundColorLightGrayMenuItem,
            RenderBackgroundColorWhiteMenuItem);
    }

    private static void ApplyItemState(MenuItem item, ViewerRenderMenuItemState state)
    {
        item.IsChecked = state.IsChecked;
        item.IsEnabled = state.IsEnabled;
    }

    private static void ApplyRadioStates(
        IReadOnlyList<ViewerRenderMenuItemState> states,
        params MenuItem[] items)
    {
        for (int index = 0; index < items.Length; index++)
        {
            ApplyItemState(items[index], states[index]);
        }
    }

    /// <summary>
    /// Wires every menu item this workstream adds. Stateless actions subscribe the same
    /// handler the existing control already uses; toggles and radios set the state of a
    /// control that remains hidden and authoritative, which raises the same event that
    /// control's existing subscription already handles.
    /// </summary>
    private void WireMenuCommands()
    {
        FileExitMenuItem.Click += (_, _) => Close();
        ResetLayoutMenuItem.Click += OnResetLayoutClick;

        // Diagnostics/Hydra/TfDebug tab visibility is independently toggleable from both View
        // (Inspector Tabs) and Tools > Developer, since a developer looking for the Hydra scene
        // refresh action naturally expects the tab's visibility switch right next to it.
        ToolsDiagnosticsTabVisibleMenuItem.Click += OnPanelVisibilityChanged;
        ToolsHydraTabVisibleMenuItem.Click += OnPanelVisibilityChanged;
        ToolsTfDebugTabVisibleMenuItem.Click += OnPanelVisibilityChanged;

        RenderRendererAutoMenuItem.Click += OnRenderRendererMenuClick;
        RenderRendererStormMenuItem.Click += OnRenderRendererMenuClick;
        RenderRendererD3D12MenuItem.Click += OnRenderRendererMenuClick;
        RenderRendererVulkanMenuItem.Click += OnRenderRendererMenuClick;
        RenderRendererMetalMenuItem.Click += OnRenderRendererMenuClick;

        RenderDrawModeWireframeMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeWireframeOnSurfaceMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeSmoothShadedMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeFlatShadedMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModePointsMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeGeomOnlyMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeGeomFlatMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeGeomSmoothMenuItem.Click += OnRenderDrawModeMenuClick;
        RenderDrawModeHiddenSurfaceWireframeMenuItem.Click += OnRenderDrawModeMenuClick;

        RenderPurposeDefaultMenuItem.Click += OnRenderPurposeMenuClick;
        RenderPurposeProxyMenuItem.Click += OnRenderPurposeMenuClick;
        RenderPurposeRenderMenuItem.Click += OnRenderPurposeMenuClick;
        RenderPurposeGuideMenuItem.Click += OnRenderPurposeMenuClick;

        RenderSceneLightingMenuItem.Click += (_, e) =>
        {
            SceneLightingCheckBox.IsChecked = RenderSceneLightingMenuItem.IsChecked;
            OnViewportLightingChanged(SceneLightingCheckBox, e);
        };
        RenderSceneShadowsMenuItem.Click += (_, e) =>
        {
            SceneShadowsCheckBox.IsChecked = RenderSceneShadowsMenuItem.IsChecked;
            OnViewportShadowsChanged(SceneShadowsCheckBox, e);
        };
        RenderBackfaceCullingMenuItem.Click += (_, e) =>
        {
            BackfaceCullingCheckBox.IsChecked = RenderBackfaceCullingMenuItem.IsChecked;
            OnViewportBackfaceCullingChanged(BackfaceCullingCheckBox, e);
        };
        RenderSceneMaterialsMenuItem.Click += (_, e) =>
        {
            SceneMaterialsCheckBox.IsChecked = RenderSceneMaterialsMenuItem.IsChecked;
            OnViewportSceneMaterialsChanged(SceneMaterialsCheckBox, e);
        };

        RenderBackgroundColorBlackMenuItem.Click += OnRenderBackgroundColorMenuClick;
        RenderBackgroundColorDarkGrayMenuItem.Click += OnRenderBackgroundColorMenuClick;
        RenderBackgroundColorLightGrayMenuItem.Click += OnRenderBackgroundColorMenuClick;
        RenderBackgroundColorWhiteMenuItem.Click += OnRenderBackgroundColorMenuClick;

        InitializeColorManagementMenu();

        PhysicsPlayPauseMenuItem.Click += OnPhysicsPlayPauseClick;
        PhysicsStopMenuItem.Click += OnPhysicsStopClick;
        PhysicsStepMenuItem.Click += OnPhysicsStepClick;
        PhysicsLoopMenuItem.Click += (_, _) =>
            PhysicsLoopCheckBox.IsChecked = PhysicsLoopMenuItem.IsChecked;
        PhysicsPreviewMenuItem.Click += (_, _) =>
            PhysicsPreviewCheckBox.IsChecked = PhysicsPreviewMenuItem.IsChecked;
        PhysicsSnapMenuItem.Click += (_, _) =>
            PhysicsSnapCheckBox.IsChecked = PhysicsSnapMenuItem.IsChecked;

        PhysicsSpeedQuarterMenuItem.Click += OnPhysicsSpeedMenuClick;
        PhysicsSpeedHalfMenuItem.Click += OnPhysicsSpeedMenuClick;
        PhysicsSpeedNormalMenuItem.Click += OnPhysicsSpeedMenuClick;
        PhysicsSpeedDoubleMenuItem.Click += OnPhysicsSpeedMenuClick;
        PhysicsSpeedQuadrupleMenuItem.Click += OnPhysicsSpeedMenuClick;

        PhysicsGizmoNoneMenuItem.Click += OnPhysicsGizmoMenuClick;
        PhysicsGizmoMoveMenuItem.Click += OnPhysicsGizmoMenuClick;
        PhysicsGizmoRotateMenuItem.Click += OnPhysicsGizmoMenuClick;
        PhysicsGizmoScaleMenuItem.Click += OnPhysicsGizmoMenuClick;
        PhysicsGizmoDragMenuItem.Click += OnPhysicsGizmoMenuClick;

        PhysicsShowInspectorMenuItem.Click += OnPhysicsShowInspectorClick;

        ToolsValidationRunMenuItem.Click += OnRefreshValidationClick;
        ToolsValidationScopeStageMenuItem.Click += OnToolsValidationScopeMenuClick;
        ToolsValidationScopePrimMenuItem.Click += OnToolsValidationScopeMenuClick;

        ToolsPickModePrimsMenuItem.Click += OnToolsPickModeMenuClick;
        ToolsPickModeModelsMenuItem.Click += OnToolsPickModeMenuClick;
        ToolsPickModeInstancesMenuItem.Click += OnToolsPickModeMenuClick;
        ToolsPickModePrototypesMenuItem.Click += OnToolsPickModeMenuClick;

        ToolsPickTargetPrimitiveMenuItem.Click += OnToolsPickTargetMenuClick;
        ToolsPickTargetFaceMenuItem.Click += OnToolsPickTargetMenuClick;
        ToolsPickTargetEdgeMenuItem.Click += OnToolsPickTargetMenuClick;
        ToolsPickTargetPointMenuItem.Click += OnToolsPickTargetMenuClick;
        ToolsSelectionVisibleOnlyMenuItem.Click += OnToolsSelectionModeMenuClick;
        ToolsSelectionXRayMenuItem.Click += OnToolsSelectionModeMenuClick;

        ToolsCopyDiagnosticsMenuItem.Click += OnCopyDiagnosticsClick;
        ToolsExportDiagnosticsMenuItem.Click += OnExportDiagnosticsClick;
        ToolsIncludeDiagnosticPathsMenuItem.Click += OnToolsIncludeDiagnosticPathsMenuClick;
        IncludeDiagnosticPathsCheckBox.Click += OnToolsIncludeDiagnosticPathsMenuClick;
        ToolsRefreshHydraSceneMenuItem.Click += OnRefreshHydraSceneClick;
        ToolsRefreshTfDebugMenuItem.Click += OnRefreshTfDebugClick;

        AboutMenuItem.Click += OnAboutClick;

        // The bridge surface is built last because it is the only menu entry whose very
        // existence depends on runtime state a host supplied rather than on markup.
        WireBridgeConnection();

        // Establishes the Render menu's initial radio/check state from whatever the hidden
        // display controls and renderer selector already hold, so the menu is never blank
        // before the first stage-related activity calls the same sync through the viewport
        // display pipeline.
        SyncRenderMenuFromState();
    }

    /// <summary>
    /// The backend a pick or selection composite would currently run on, which
    /// is what decides whether a target or mode is offered.
    /// </summary>
    /// <remarks>
    /// A Viewer with no attached backend reports Storm, the most restrictive
    /// kind, so the subprim targets and the x-ray mode stay disabled until a
    /// backend that answers them is actually attached rather than being offered
    /// and then refused.
    /// </remarks>
    internal RenderBackendKind CurrentPickBackendKind =>
        _coordinator?.ActiveBackend?.Kind ?? RenderBackendKind.Storm;

    /// <summary>
    /// Pushes one selection mode onto the shared selection-outline policy, so
    /// the next composite of every render path uses it.
    /// </summary>
    private static void ApplySelectionOutlineMode(ViewerSelectionMode mode) =>
        ViewerSelectionOutlinePolicy.SetMode(mode);

    /// <summary>
    /// Restores the clean default profile. The colour-management half is transactional:
    /// an active OpenColorIO display transform is cleared from the coordinator through the
    /// ordinary request pipeline before the default choice is committed, so the menu, the
    /// model, the cached key, and the persisted profile never claim an untransformed image
    /// the viewport is not showing.
    /// </summary>
    private async void OnResetLayoutClick(object? sender, RoutedEventArgs e)
    {
        await ResetLayoutAsync();
    }

    /// <summary>
    /// Shows the Physics inspector: reveals the Inspector panel if it is currently hidden and
    /// selects the Physics tab, so the command works from any starting layout rather than only
    /// selecting a tab inside a panel the operator cannot see.
    /// </summary>
    private void OnPhysicsShowInspectorClick(object? sender, RoutedEventArgs e)
    {
        _applyingLayout = true;
        try
        {
            InspectorPanelMenuItem.IsChecked = true;
            ApplyPanelVisibility(
                StagePanel.IsVisible,
                inspectorVisible: true,
                TimelinePanel.IsVisible,
                DiagnosticsTab.IsVisible,
                HydraSceneTab.IsVisible,
                TfDebugTab.IsVisible,
                selectedTabId: ViewerInspectorLayoutPolicy.PhysicsTabId);
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private void OnRenderRendererMenuClick(object? sender, RoutedEventArgs e)
    {
        RendererSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, RenderRendererAutoMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, RenderRendererStormMenuItem) => 1,
            MenuItem when ReferenceEquals(sender, RenderRendererD3D12MenuItem) => 2,
            MenuItem when ReferenceEquals(sender, RenderRendererVulkanMenuItem) => 3,
            MenuItem when ReferenceEquals(sender, RenderRendererMetalMenuItem) => 4,
            _ => RendererSelector.SelectedIndex,
        };
    }

    private void OnRenderDrawModeMenuClick(object? sender, RoutedEventArgs e)
    {
        ViewportDrawModeSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, RenderDrawModeWireframeMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, RenderDrawModeWireframeOnSurfaceMenuItem) => 1,
            MenuItem when ReferenceEquals(sender, RenderDrawModeSmoothShadedMenuItem) => 2,
            MenuItem when ReferenceEquals(sender, RenderDrawModeFlatShadedMenuItem) => 3,
            MenuItem when ReferenceEquals(sender, RenderDrawModePointsMenuItem) => 4,
            MenuItem when ReferenceEquals(sender, RenderDrawModeGeomOnlyMenuItem) => 5,
            MenuItem when ReferenceEquals(sender, RenderDrawModeGeomFlatMenuItem) => 6,
            MenuItem when ReferenceEquals(sender, RenderDrawModeGeomSmoothMenuItem) => 7,
            MenuItem when ReferenceEquals(sender, RenderDrawModeHiddenSurfaceWireframeMenuItem) => 8,
            _ => ViewportDrawModeSelector.SelectedIndex,
        };
    }

    private void OnRenderPurposeMenuClick(object? sender, RoutedEventArgs e)
    {
        (CheckBox target, bool isChecked)? mapped = sender switch
        {
            MenuItem { IsChecked: var isChecked } item when
                ReferenceEquals(item, RenderPurposeDefaultMenuItem) =>
                (PurposeDefaultCheckBox, isChecked),
            MenuItem { IsChecked: var isChecked } item when
                ReferenceEquals(item, RenderPurposeProxyMenuItem) =>
                (PurposeProxyCheckBox, isChecked),
            MenuItem { IsChecked: var isChecked } item when
                ReferenceEquals(item, RenderPurposeRenderMenuItem) =>
                (PurposeRenderCheckBox, isChecked),
            MenuItem { IsChecked: var isChecked } item when
                ReferenceEquals(item, RenderPurposeGuideMenuItem) =>
                (PurposeGuideCheckBox, isChecked),
            _ => null,
        };
        if (mapped is not { } resolved)
        {
            return;
        }
        resolved.target.IsChecked = resolved.isChecked;
        OnViewportPurposeChanged(resolved.target, e);
    }

    private void OnRenderBackgroundColorMenuClick(object? sender, RoutedEventArgs e)
    {
        BackgroundColorSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, RenderBackgroundColorBlackMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, RenderBackgroundColorDarkGrayMenuItem) => 1,
            MenuItem when ReferenceEquals(sender, RenderBackgroundColorLightGrayMenuItem) => 2,
            MenuItem when ReferenceEquals(sender, RenderBackgroundColorWhiteMenuItem) => 3,
            _ => BackgroundColorSelector.SelectedIndex,
        };
    }

    private void OnPhysicsSpeedMenuClick(object? sender, RoutedEventArgs e)
    {
        PhysicsSpeedSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, PhysicsSpeedQuarterMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, PhysicsSpeedHalfMenuItem) => 1,
            MenuItem when ReferenceEquals(sender, PhysicsSpeedNormalMenuItem) => 2,
            MenuItem when ReferenceEquals(sender, PhysicsSpeedDoubleMenuItem) => 3,
            MenuItem when ReferenceEquals(sender, PhysicsSpeedQuadrupleMenuItem) => 4,
            _ => PhysicsSpeedSelector.SelectedIndex,
        };
    }

    private void OnPhysicsGizmoMenuClick(object? sender, RoutedEventArgs e)
    {
        PhysicsGizmoSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, PhysicsGizmoNoneMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, PhysicsGizmoMoveMenuItem) => 1,
            MenuItem when ReferenceEquals(sender, PhysicsGizmoRotateMenuItem) => 2,
            MenuItem when ReferenceEquals(sender, PhysicsGizmoScaleMenuItem) => 3,
            MenuItem when ReferenceEquals(sender, PhysicsGizmoDragMenuItem) => 4,
            _ => PhysicsGizmoSelector.SelectedIndex,
        };
    }

    private void OnToolsValidationScopeMenuClick(object? sender, RoutedEventArgs e)
    {
        ValidationScopeSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, ToolsValidationScopeStageMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, ToolsValidationScopePrimMenuItem) => 1,
            _ => ValidationScopeSelector.SelectedIndex,
        };
    }

    private void OnToolsPickModeMenuClick(object? sender, RoutedEventArgs e)
    {
        PickModeSelector.SelectedIndex = sender switch
        {
            MenuItem when ReferenceEquals(sender, ToolsPickModePrimsMenuItem) => 0,
            MenuItem when ReferenceEquals(sender, ToolsPickModeModelsMenuItem) => 1,
            MenuItem when ReferenceEquals(sender, ToolsPickModeInstancesMenuItem) => 2,
            MenuItem when ReferenceEquals(sender, ToolsPickModePrototypesMenuItem) => 3,
            _ => PickModeSelector.SelectedIndex,
        };
    }

    /// <summary>
    /// Applies the Tools &gt; Pick Target radio group. The target changes what a
    /// later viewport click resolves -- a prim, an authored face, an authored
    /// edge, or an authored point -- and nothing else, so it is applied to the
    /// pick policy rather than performing an action of its own.
    /// </summary>
    private void OnToolsPickTargetMenuClick(object? sender, RoutedEventArgs e)
    {
        RenderPickTarget target = sender switch
        {
            MenuItem when ReferenceEquals(sender, ToolsPickTargetFaceMenuItem) =>
                RenderPickTarget.Face,
            MenuItem when ReferenceEquals(sender, ToolsPickTargetEdgeMenuItem) =>
                RenderPickTarget.Edge,
            MenuItem when ReferenceEquals(sender, ToolsPickTargetPointMenuItem) =>
                RenderPickTarget.Point,
            MenuItem when ReferenceEquals(sender, ToolsPickTargetPrimitiveMenuItem) =>
                RenderPickTarget.Primitive,
            _ => PickTarget,
        };
        SetPickTarget(target);
    }

    /// <summary>
    /// Applies the Tools &gt; Selection Outline radio group, which chooses
    /// between the depth-tested visible-only outline and the x-ray outline that
    /// also draws the occluded part of the selection in a distinct style.
    /// </summary>
    private void OnToolsSelectionModeMenuClick(object? sender, RoutedEventArgs e)
    {
        ViewerSelectionMode mode =
            sender is MenuItem && ReferenceEquals(sender, ToolsSelectionXRayMenuItem)
                ? ViewerSelectionMode.XRay
                : ViewerSelectionMode.VisibleOnly;
        SetSelectionMode(mode);
    }

    /// <summary>Gets the pick target every viewport click currently resolves.</summary>
    internal RenderPickTarget PickTarget { get; private set; } =
        ViewerPickTargetPolicy.DefaultTarget;

    /// <summary>Gets how a selected surface's outline treats occluders.</summary>
    internal ViewerSelectionMode SelectionMode { get; private set; } =
        ViewerPickTargetPolicy.DefaultSelectionMode;

    /// <summary>Gets the pick target the user asked for, whatever the backend can answer.</summary>
    /// <remarks>
    /// The desired value and the effective one are separate because a Viewer
    /// that starts on Storm, or that has no backend attached yet, cannot answer
    /// edge or point picks. Collapsing the two would make startup overwrite the
    /// user's saved choice with the restrictive fallback, so a profile that
    /// asked for edge picking would silently become a prim-picking profile the
    /// first time the Viewer opened before a capable backend attached.
    /// </remarks>
    internal RenderPickTarget DesiredPickTarget { get; private set; } =
        ViewerPickTargetPolicy.DefaultTarget;

    /// <summary>Gets the selection mode the user asked for, whatever the backend can composite.</summary>
    internal ViewerSelectionMode DesiredSelectionMode { get; private set; } =
        ViewerPickTargetPolicy.DefaultSelectionMode;

    /// <summary>
    /// Applies one pick target and leaves the menu showing exactly what is in
    /// effect, including when a backend refuses the requested target.
    /// </summary>
    /// <remarks>
    /// The request is remembered even when it is refused, so switching to a
    /// backend that can answer it restores it without the user asking twice,
    /// and saving the profile records what the user wanted rather than what the
    /// current backend happened to allow.
    /// </remarks>
    internal void SetPickTarget(RenderPickTarget target)
    {
        DesiredPickTarget = target;
        if (!ViewerPickTargetPolicy.SupportsTarget(CurrentPickBackendKind, target))
        {
            ViewerStartupOptions.WriteStatus(
                ViewerPickTargetPolicy.DescribeUnsupportedTarget(
                    CurrentPickBackendKind,
                    target));
            PickTarget = ViewerPickTargetPolicy.DefaultTarget;
            SyncPickTargetMenu();
            return;
        }
        PickTarget = target;
        SyncPickTargetMenu();
    }

    /// <summary>Applies one selection mode, refusing what the backend cannot composite.</summary>
    /// <remarks>
    /// As with the pick target, a refused mode is still remembered so it is
    /// restored on a backend that can composite it and is persisted as the
    /// user's choice.
    /// </remarks>
    internal void SetSelectionMode(ViewerSelectionMode mode)
    {
        DesiredSelectionMode = mode;
        if (!ViewerPickTargetPolicy.SupportsSelectionMode(CurrentPickBackendKind, mode))
        {
            ViewerStartupOptions.WriteStatus(
                ViewerPickTargetPolicy.DescribeUnsupportedSelectionMode(
                    CurrentPickBackendKind,
                    mode));
            SelectionMode = ViewerPickTargetPolicy.DefaultSelectionMode;
            ApplySelectionOutlineMode(SelectionMode);
            SyncPickTargetMenu();
            return;
        }
        SelectionMode = mode;
        ApplySelectionOutlineMode(mode);
        SyncPickTargetMenu();
    }

    /// <summary>
    /// Re-applies the desired pick target and selection mode after the active
    /// backend changed.
    /// </summary>
    /// <remarks>
    /// Backend activation is the only moment at which a previously refused
    /// request can become answerable, and the only moment at which an
    /// in-effect one can stop being answerable. Reconciling here -- rather than
    /// on every render -- keeps the menu, the renderer, and the profile in step
    /// across a restart, a backend switch, and a device loss, without the
    /// switch ever writing back over what the user asked for.
    /// </remarks>
    internal void ReconcilePickPolicyWithBackend()
    {
        RenderBackendKind backend = CurrentPickBackendKind;
        PickTarget = ViewerPickTargetPolicy.ResolveEffectiveTarget(
            backend,
            DesiredPickTarget);
        SelectionMode = ViewerPickTargetPolicy.ResolveEffectiveSelectionMode(
            backend,
            DesiredSelectionMode);
        ApplySelectionOutlineMode(SelectionMode);
        SyncPickTargetMenu();
    }

    /// <summary>
    /// Puts the two Tools radio groups back in step with the applied state, and
    /// disables what the current backend cannot answer.
    /// </summary>
    /// <remarks>
    /// The unsupported entries are disabled rather than hidden, so a capability
    /// difference between backends reads as a capability difference rather than
    /// as a missing feature, and a screen reader still finds and announces the
    /// control.
    /// </remarks>
    private void SyncPickTargetMenu()
    {
        RenderBackendKind backend = CurrentPickBackendKind;
        ToolsPickTargetPrimitiveMenuItem.IsChecked =
            PickTarget == RenderPickTarget.Primitive;
        ToolsPickTargetFaceMenuItem.IsChecked = PickTarget == RenderPickTarget.Face;
        ToolsPickTargetEdgeMenuItem.IsChecked = PickTarget == RenderPickTarget.Edge;
        ToolsPickTargetPointMenuItem.IsChecked = PickTarget == RenderPickTarget.Point;
        ToolsPickTargetFaceMenuItem.IsEnabled =
            ViewerPickTargetPolicy.SupportsTarget(backend, RenderPickTarget.Face);
        ToolsPickTargetEdgeMenuItem.IsEnabled =
            ViewerPickTargetPolicy.SupportsTarget(backend, RenderPickTarget.Edge);
        ToolsPickTargetPointMenuItem.IsEnabled =
            ViewerPickTargetPolicy.SupportsTarget(backend, RenderPickTarget.Point);

        ToolsSelectionVisibleOnlyMenuItem.IsChecked =
            SelectionMode == ViewerSelectionMode.VisibleOnly;
        ToolsSelectionXRayMenuItem.IsChecked = SelectionMode == ViewerSelectionMode.XRay;
        ToolsSelectionXRayMenuItem.IsEnabled =
            ViewerPickTargetPolicy.SupportsSelectionMode(
                backend,
                ViewerSelectionMode.XRay);
    }

    /// <summary>
    /// Keeps the Diagnostics tab's "Include paths" checkbox and the mirrored Tools &gt;
    /// Developer &gt; Diagnostics &gt; Include paths menu item showing the same value, whichever
    /// one the user actually clicked, exactly like the existing dual panel-visibility controls.
    /// </summary>
    private void OnToolsIncludeDiagnosticPathsMenuClick(object? sender, RoutedEventArgs e)
    {
        bool includePaths = sender switch
        {
            CheckBox when ReferenceEquals(sender, IncludeDiagnosticPathsCheckBox) =>
                IncludeDiagnosticPathsCheckBox.IsChecked == true,
            MenuItem when ReferenceEquals(sender, ToolsIncludeDiagnosticPathsMenuItem) =>
                ToolsIncludeDiagnosticPathsMenuItem.IsChecked,
            _ => IncludeDiagnosticPathsCheckBox.IsChecked == true,
        };
        IncludeDiagnosticPathsCheckBox.IsChecked = includePaths;
        ToolsIncludeDiagnosticPathsMenuItem.IsChecked = includePaths;
        RenderDiagnostics();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            var about = new AboutWindow();
            _ = about.ShowDialog(this);
        }
#pragma warning disable CA1031 // A dialog failure must not tear down an embedding host.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            ShowError($"Could not open the About dialog: {exception.Message}");
        }
    }
}
