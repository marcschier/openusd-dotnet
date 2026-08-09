// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerSourceContractTests
{
    [Test]
    public async Task BackendCandidatesStayVisibleWhileCreatingPlatformSurfaces()
    {
        string root = FindRepositoryRoot();
        string host = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "AvaloniaViewerRenderBackendHost.cs"));

        await Assert.That(host).Contains("private bool AttachForSurfaceCreation(Control control)");
        await Assert.That(host).Contains("viewportHost.Attach(control, isActive: true);");
        await Assert.That(host)
            .Contains("Linux X11 NativeControlHost can stay");
        await Assert.That(host)
            .Contains("macOS Metal composition can crash");
        await Assert.That(host).Contains("HideInitializedCandidateUnlessFirstAsync(");
        await Assert.That(host).DoesNotContain("viewportHost.Attach(control, isActive: false);");
    }

    [Test]
    public async Task MacOSStormProbeRejectsMissingCglBeforeCreatingNativeChild()
    {
        string root = FindRepositoryRoot();
        string host = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "AvaloniaViewerRenderBackendHost.cs"));

        await Assert.That(host).Contains("TryGetMacOSStormCglUnavailable");
        await Assert.That(host).Contains("VIEWER_STORM_MACOS_CGL_UNAVAILABLE");
        await Assert.That(host).Contains("VIEWER_STORM_MACOS_CGL_AVAILABLE");
        await Assert.That(host).Contains("catch (DllNotFoundException exception)");
        await Assert.That(host).Contains("catch (EntryPointNotFoundException exception)");
        await Assert.That(host).DoesNotContain("CglPfaAllowOfflineRenderers");
        await Assert.That(host).Contains("CglOglPVersion41Core");
        await Assert.That(host).Contains("CGLChoosePixelFormat");
        await Assert.That(host.IndexOf(
            "TryGetMacOSStormCglUnavailable",
            StringComparison.Ordinal)).IsLessThan(host.IndexOf(
                "private async ValueTask<IViewerRenderBackendSession> AttachNativeStormAsync",
                StringComparison.Ordinal));
    }

    [Test]
    public async Task MacOSStormCglPreflightMirrorsNativePixelFormat()
    {
        string root = FindRepositoryRoot();
        string host = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "AvaloniaViewerRenderBackendHost.cs"));
        string native = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_macos.mm"));

        string nativeAttributes = ExtractNativeStormPixelFormatAttributes(native);
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFAOpenGLProfile");
        await Assert.That(nativeAttributes).Contains("NSOpenGLProfileVersion4_1Core");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFAColorSize, 24");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFAAlphaSize, 8");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFADepthSize, 24");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFAStencilSize, 8");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFADoubleBuffer");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFAAccelerated");
        await Assert.That(nativeAttributes).Contains("NSOpenGLPFANoRecovery");
        await Assert.That(nativeAttributes).DoesNotContain("NSOpenGLPFAAllowOfflineRenderers");

        string managedAttributes = ExtractManagedStormCglAttributes(host);
        await Assert.That(managedAttributes).DoesNotContain("CglPfaAllowOfflineRenderers");
        AssertContainsInOrder(
            managedAttributes,
            "CglPfaOpenGlProfile",
            "CglOglPVersion41Core",
            "CglPfaColorSize",
            "24",
            "CglPfaAlphaSize",
            "8",
            "CglPfaDepthSize",
            "24",
            "CglPfaStencilSize",
            "8",
            "CglPfaDoubleBuffer",
            "CglPfaAccelerated",
            "CglPfaNoRecovery",
            "0");
    }

    [Test]
    public async Task AutomatedStageOpenReportsEachBlockingBoundary()
    {
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

        foreach (string status in new[]
        {
            "Viewer stage open: resolved",
            "Viewer stage open: validation scheduler starting",
            "Viewer stage open: validation root layer query starting",
            "Viewer stage open: validation root layer query completed",
            "Viewer stage open: render coordinator starting",
            "Viewer stage open: render coordinator acquired",
            "Viewer stage open: document snapshot starting",
            "Viewer stage open: document snapshot completed",
            "Viewer stage open: UI binding completed",
            "Viewer stage open: timeline initialization starting",
            "Viewer stage open: timeline initialization completed",
            "Viewer stage open: validation refresh starting",
            "Viewer stage open: validation refresh completed",
            "Viewer stage open: viewport state update starting",
            "Viewer stage open: viewport state update completed",
            "Viewer stage open: render loop starting",
            "Viewer stage open: render loop task created",
            "Renderer render loop: started",
            "Renderer render loop: first tick",
            "Renderer render loop: first render request starting",
            "Renderer render loop: first render request completed"
        })
        {
            await Assert.That(window).Contains(status);
        }

        foreach (string status in new[]
        {
            "Renderer coordinator: stage scheduler starting",
            "Renderer coordinator: render source acquiring",
            "Renderer coordinator: render source acquired",
            "Renderer coordinator: root layer query starting",
            "Renderer coordinator: backend initialization starting",
            "Renderer coordinator: initialization result publish starting",
            "Renderer coordinator: initialization diagnostics published",
            "Renderer coordinator: initialization summary publishing",
            "Renderer coordinator: initialization summary published",
            "Renderer coordinator: initialized backend returning"
        })
        {
            await Assert.That(coordinator).Contains(status);
        }
    }

    [Test]
    public async Task MacOSViewerBundleCompositionSkipIsExplicitAndDocumented()
    {
        string root = FindRepositoryRoot();
        string smokeRunner = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "test-viewer-bundle-smoke.ps1"));
        string compositionSession = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "CompositionViewportSession.cs"));
        string testing = await File.ReadAllTextAsync(Path.Combine(
            root,
            "docs",
            "testing.md"));

        await Assert.That(compositionSession).Contains(
            "submitted to Avalonia compositor");
        await Assert.That(smokeRunner).Contains("viewer-composition-capability.json");
        await Assert.That(smokeRunner).Contains("status = 'skipped'");
        await Assert.That(smokeRunner).Contains("macos-avalonia-metal-composition");
        await Assert.That(smokeRunner).Contains("submitted to Avalonia compositor$'");
        await Assert.That(smokeRunner).Contains("VIEWER_BUNDLE_SMOKE_SKIPPED");
        await Assert.That(smokeRunner).Contains("Get-Command lldb");
        await Assert.That(testing).Contains(
            "artifacts/viewer-distribution-smoke/osx-arm64/viewer-composition-capability.json");
        await Assert.That(testing).Contains("submitted a frame to the Avalonia compositor");
    }

    [Test]
    public async Task ViewerSourceKeepsDetachedTraversalAndSerializedDocumentLifecycle()
    {
        string root = FindRepositoryRoot();
        string models = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerDocumentModels.cs"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        await Assert.That(models).Contains("ViewerDocumentSnapshot BuildDocument(UsdStage stage)");
        await Assert.That(models).Contains(
            "foreach (UsdPrim root in stage.Traverse().Where(static prim => GetPrimDepth(prim.Path) == 0))");
        await Assert.That(models).Contains("foreach (UsdPrim child in prim.GetChildren())");
        await Assert.That(models).Contains("ViewerVariantSetSnapshot[] variantSets = BuildVariantSets(prim);");
        await Assert.That(models).Contains("stage.StartTimeCode");
        await Assert.That(models).Contains("stage.EndTimeCode");
        await Assert.That(models).Contains("stage.FramesPerSecond");
        await Assert.That(models).Contains("stage.TimeCodesPerSecond");
        await Assert.That(models).Contains("stage.GetLayerStackIdentifiers()");
        await Assert.That(models).Contains("layer.IsMuted");
        await Assert.That(models).DoesNotContain("stage.IsLayerMuted(");
        await Assert.That(models).Contains("stage.EditTargetLayerIdentifier");
        await Assert.That(window).Contains("private readonly SemaphoreSlim _documentGate");
        await Assert.That(window).Contains("await StopCurrentDocumentAsync();");
        await Assert.That(window).Contains("await StopTimelineAsync();");
        await Assert.That(window).Contains("_documentLifetime?.Cancel();");
        await Assert.That(window).Contains("await _coordinator.DisposeAsync();");
        await Assert.That(window.IndexOf("await StopTimelineAsync();", StringComparison.Ordinal))
            .IsLessThan(window.IndexOf("await _coordinator.DisposeAsync();", StringComparison.Ordinal));
    }

    [Test]
    public async Task ViewerMarkupAndSourceExposeOpenReloadDropHierarchyAndInspector()
    {
        string root = FindRepositoryRoot();
        string markup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        await Assert.That(markup).Contains("x:Name=\"OpenStageButton\"");
        await Assert.That(markup).Contains("x:Name=\"ReloadStageButton\"");
        await Assert.That(markup).Contains("x:Name=\"RecentStagesMenu\"");
        await Assert.That(markup).Contains("x:Name=\"HierarchyFilter\"");
        await Assert.That(markup).Contains("x:Name=\"ShowInactivePrimsCheckBox\"");
        await Assert.That(markup).Contains("x:Name=\"ShowUndefinedPrimsCheckBox\"");
        await Assert.That(markup).Contains("x:Name=\"ShowAbstractPrimsCheckBox\"");
        await Assert.That(markup).Contains("x:Name=\"ShowPrototypePrimsCheckBox\"");
        await Assert.That(markup).Contains("x:Name=\"StageHierarchy\"");
        await Assert.That(markup).Contains("x:Name=\"InspectorRows\"");
        await Assert.That(markup).Contains("x:Name=\"ValueRows\"");
        await Assert.That(markup).Contains("x:Name=\"MetadataRows\"");
        await Assert.That(markup).Contains("x:Name=\"LayersTab\" Header=\"_LayerStack\"");
        await Assert.That(markup).Contains("x:Name=\"LayersRows\"");
        await Assert.That(markup).Contains("x:Name=\"SetSessionEditTargetButton\"");
        await Assert.That(markup).Contains("x:Name=\"SetRootEditTargetButton\"");
        await Assert.That(markup).Contains("x:Name=\"PlayPauseButton\"");
        await Assert.That(markup).Contains("x:Name=\"CurrentTimeInput\"");
        await Assert.That(markup).Contains("x:Name=\"TimelineSlider\"");
        await Assert.That(window).Contains("StorageProvider.OpenFilePickerAsync");
        await Assert.That(window).Contains("DragDrop.AddDropHandler");
        await Assert.That(window).Contains("ViewerStageSnapshotBuilder.BuildInspector");
        await Assert.That(window).Contains("CreateHierarchyVariantSelector");
        await Assert.That(window).Contains("CreateHierarchyContextMenu");
        await Assert.That(window).Contains("RunHierarchyPrimCommandAsync");
        await Assert.That(window).Contains("await StartInspectorLoadAsync(selectedPrimPath, cancellationToken);");
        await Assert.That(window).Contains("state => state.WithTime(new StageTime(timeCode))");
        await Assert.That(window).Contains("state => state.WithSelection(selection)");
        await Assert.That(window).Contains("ResetInspector();");
    }

    [Test]
    public async Task LayerCommandsRemainSchedulerOwnedSerializedAndInteractiveOnly()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        await Assert.That(window).Contains("await _documentGate.WaitAsync(cancellationToken);");
        await Assert.That(window).Contains("coordinator.Scheduler.EditAsync(");
        await Assert.That(window).Contains("UsdStageInvalidationKind.Composition");
        await Assert.That(window).Contains("UsdStageInvalidationKind.Property");
        await Assert.That(window).Contains("stage.SetEditTargetToSessionLayer();");
        await Assert.That(window).Contains("stage.SetEditTargetToRootLayer();");
        await Assert.That(window).Contains("stage.MuteLayer(layerIdentifier!);");
        await Assert.That(window).Contains("stage.UnmuteLayer(layerIdentifier!);");
        await Assert.That(window).Contains("if (coordinator is null || IsAutomatedViewerRun())");
        await Assert.That(window).Contains("ViewerStageSnapshotBuilder.BuildDocument(");
        await Assert.That(window).Contains("previousLayers,");
        await Assert.That(window).Contains("_selectionState.PrimPath");
        await Assert.That(window).DoesNotContain("stage.SetEditTarget(");
    }

    [Test]
    public async Task SessionDiagnosticsAndSettingsRemainBoundedExplicitAndEvidenceSafe()
    {
        string root = FindRepositoryRoot();
        string markup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string session = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerSessionModels.cs"));
        string diagnostics = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerDiagnosticsModels.cs"));
        string settings = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerSettings.cs"));
        string models = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerDocumentModels.cs"));

        await Assert.That(markup).Contains("x:Name=\"DiagnosticsTab\"");
        await Assert.That(markup).Contains("x:Name=\"SettingsTab\"");
        await Assert.That(markup).Contains("x:Name=\"CopyDiagnosticsButton\"");
        await Assert.That(markup).Contains("x:Name=\"ExportDiagnosticsButton\"");
        await Assert.That(markup).Contains("x:Name=\"SnapTimelineCheckBox\"");
        await Assert.That(markup).Contains("AutomationProperties.Name=");
        await Assert.That(window).Contains("RunPrimCommandAsync(");
        await Assert.That(window).Contains("await _documentGate.WaitAsync(cancellationToken);");
        await Assert.That(window).Contains("coordinator.Scheduler.EditAsync(");
        await Assert.That(window).Contains("stage.SetEditTargetToSessionLayer();");
        await Assert.That(window).Contains("stage.SetEditTargetToRootLayer();");
        await Assert.That(window).Contains("prim.SetActive(");
        await Assert.That(window).Contains("prim.Load();");
        await Assert.That(window).Contains("prim.Unload();");
        await Assert.That(window).Contains("prim.SetInstanceable(");
        await Assert.That(window).Contains("UsdGeomImageable.Wrap(prim).SetVisibility(");
        await Assert.That(window).Contains("UsdGeomImageable.Wrap(prim).SetPurpose(");
        await Assert.That(window).Contains("if (IsAutomatedViewerRun()");
        await Assert.That(window).Contains("await SaveSettingsAsync();");
        await Assert.That(session).Contains("ViewerSessionEditTarget.Session");
        await Assert.That(session).DoesNotContain("CORE_VARIANT_SET_ENUMERATION_UNAVAILABLE");
        await Assert.That(session).DoesNotContain("CORE_PAYLOAD_ARC_ENUMERATION_UNAVAILABLE");
        await Assert.That(diagnostics).Contains("DefaultEntryCapacity = 32");
        await Assert.That(diagnostics).Contains("MaximumTextLength = 32 * 1024");
        await Assert.That(diagnostics).Contains("\"<source-tree>\"");
        await Assert.That(diagnostics).Contains("\"<user-profile>\"");
        await Assert.That(settings).Contains("MaximumFileBytes = 16 * 1024");
        await Assert.That(settings).Contains("File.Move(temporaryPath, StorePath, overwrite: true)");
        await Assert.That(settings).DoesNotContain("JsonSerializer");
        await Assert.That(models).Contains("UsdGeomImageable.TryWrap(");
        await Assert.That(models).Contains("prim.IsPrototype()");
        await Assert.That(models).Contains("prim.GetPrototypePath()");
        await Assert.That(models).Contains("ViewerStageStatisticsSnapshot");
        await Assert.That(window).DoesNotContain(".Save();");
    }

    [Test]
    public async Task LoadCommandStatusDescribesStageRulesInsteadOfLayerAuthoring()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string command = SliceMethod(
            window,
            "private async Task RunPrimCommandAsync",
            "private static void SetSessionCommandEditTarget");

        await Assert.That(command).Contains("ViewerPrimCommand.SetLoaded =>");
        await Assert.That(command)
            .Contains("Stage load rules changed; load/unload is not layer-authored.");
        await Assert.That(command.IndexOf(
            "ViewerPrimCommand.SetLoaded =>",
            StringComparison.Ordinal))
            .IsLessThan(command.IndexOf(
                "_ when target == ViewerSessionEditTarget.ExplicitRoot =>",
                StringComparison.Ordinal));
    }

    [Test]
    public async Task VariantAndPayloadInspectorRemainsDetachedInteractiveAndEvidenceSafe()
    {
        string root = FindRepositoryRoot();
        string markup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string models = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerDocumentModels.cs"));
        string session = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerSessionModels.cs"));

        await Assert.That(models).Contains("prim.GetVariantSetNames()");
        await Assert.That(models).Contains("prim.GetVariantNames(variantSetName)");
        await Assert.That(models).Contains("prim.GetVariantSelection(variantSetName)");
        await Assert.That(models).Contains("prim.GetPayloadArcs()");
        await Assert.That(models).Contains("ViewerVariantSetSnapshot[] VariantSets");
        await Assert.That(models).Contains("ViewerPayloadArcSnapshot[] PayloadArcs");
        await Assert.That(models).Contains("[relative authored asset path]");
        await Assert.That(models).Contains("[anonymous source layer; process-local]");
        await Assert.That(session).Contains("ViewerPrimCommand.SetVariantSelection");
        await Assert.That(session).Contains("UsdStageInvalidationKind.Composition");
        await Assert.That(window).Contains("OnVariantSelectionChanged");
        await Assert.That(window).Contains("await RunPrimCommandAsync(");
        await Assert.That(window).Contains("await _documentGate.WaitAsync(cancellationToken);");
        await Assert.That(window).Contains("SetSessionCommandEditTarget(stage, target);");
        await Assert.That(window).Contains("ViewerStageSnapshotBuilder.BuildDocument(");
        await Assert.That(window).Contains("FocusVariantSelector(request.VariantSetName);");
        await Assert.That(window).Contains("IsAutomated: IsAutomatedViewerRun()");
        await Assert.That(window).DoesNotContain("prim.AddPayload(");
        await Assert.That(window).DoesNotContain("prim.ClearPayloads(");
        await Assert.That(markup)
            .Contains("Selected prim properties, variants, payloads, and session controls");
        await Assert.That(markup).Contains("AutomationProperties.Name=");
        await Assert.That(session).DoesNotContain("CORE_VARIANT_SET_ENUMERATION_UNAVAILABLE");
        await Assert.That(session).DoesNotContain("CORE_PAYLOAD_ARC_ENUMERATION_UNAVAILABLE");
    }

    [Test]
    public async Task ClosingSettingsAndOpenStageAssignmentsRemainLifecycleSafe()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        string closing = SliceMethod(
            window,
            "private async void OnClosing",
            "private void OnClosed");
        int cancel = closing.IndexOf("e.Cancel = true;", StringComparison.Ordinal);
        int guardedTry = closing.IndexOf("try", cancel, StringComparison.Ordinal);
        int save = closing.IndexOf("await SaveSettingsAsync();", StringComparison.Ordinal);
        int closingFinally = closing.LastIndexOf("finally", StringComparison.Ordinal);
        int close = closing.IndexOf("Close();", closingFinally, StringComparison.Ordinal);

        await Assert.That(cancel).IsGreaterThanOrEqualTo(0);
        await Assert.That(guardedTry).IsGreaterThan(cancel);
        await Assert.That(save).IsGreaterThan(guardedTry);
        await Assert.That(closingFinally).IsGreaterThan(save);
        await Assert.That(close).IsGreaterThan(closingFinally);

        string openStage = SliceMethod(
            window,
            "private async Task OpenStageCoreAsync",
            "private async Task ReloadStageAsync");
        await Assert.That(CountOccurrences(
            openStage,
            "_statistics = document.Statistics;")).IsEqualTo(1);
        await Assert.That(CountOccurrences(
            openStage,
            "_currentInspector = document.SelectedPrim;")).IsEqualTo(1);
    }

    [Test]
    public async Task CameraPropagationAndSchemaEightEvidenceRemainSourceBound()
    {
        string root = FindRepositoryRoot();
        string host = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "StormNativeControlHost.cs"));
        string backends = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "AvaloniaViewerRenderBackendHost.cs"));
        string compatibility = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "StormViewportControl.cs"));
        string evidence = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerSwitchingEvidence.cs"));
        string cameraEvidence = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerCameraEvidence.cs"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string contract = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "viewer-evidence-contract.ps1"));
        string runner = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "run-storm-native-child.ps1"));
        string identity = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "viewer-source-identity.ps1"));
        string runViewer = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "run-viewer.ps1"));

        await Assert.That(host).Contains("ViewerFrameRequest.Capture(state)");
        await Assert.That(host).Contains("ViewerFrameAdapter.RequestStorm(adapter, request)");
        await Assert.That(backends).Contains("ViewerFrameAdapter.SyncSilk(");
        await Assert.That(backends).Contains("resources.Renderer.LastStateRevision");
        await Assert.That(compatibility).Contains("state.Camera");
        await Assert.That(evidence).Contains("CurrentSchemaVersion = 8");
        await Assert.That(evidence).Contains("CameraPayload");
        await Assert.That(evidence).Contains("ViewerCameraTransitionEvidence");
        await Assert.That(evidence).Contains("ViewerNativeNavigationEvidence");
        await Assert.That(evidence).Contains("ViewerStageCameraEvidence");
        await Assert.That(evidence)
            .Contains("openusd_storm_child_capture_framebuffer(ABI7,preserved-texture)");
        await Assert.That(evidence)
            .Contains("openusd_storm_child_get_navigation_input(ABI7,v1)");
        await Assert.That(evidence).DoesNotContain("ABI4");
        await Assert.That(cameraEvidence).Contains("SHA256.HashData(payload)");
        await Assert.That(cameraEvidence).Contains("BinaryPrimitives.WriteUInt64LittleEndian");
        await Assert.That(window).Contains("RunExplicitCameraEvidenceAsync(");
        await Assert.That(window).Contains("RunStageCameraBackendSmokeAsync(");
        await Assert.That(window).Contains("RecordNativeStormNavigationEvidenceAsync(");
        await Assert.That(window).Contains("LatestRequestedCameraSignature");
        await Assert.That(contract).Contains("$script:ViewerEvidenceSchemaVersion = 8");
        await Assert.That(contract).Contains("Assert-ViewerCameraEvidence");
        await Assert.That(contract).Contains("Assert-ViewerNativeNavigationEvidence");
        await Assert.That(contract).Contains("Assert-ViewerStageCameraEvidence");
        await Assert.That(contract).Contains(
            "[string]$before[0].cameraSignature -ceq");
        await Assert.That(runner).Contains("[int]$artifact.stormChildAbiVersion -ne 7");
        await Assert.That(runner).Contains("cameraTransitionCount");
        await Assert.That(runner).Contains("nativeNavigationCount");
        await Assert.That(runner).Contains(
            "-Name 'stage-camera-backend-smoke'");
        await Assert.That(runner).Contains(
            "$expectedScenarioCount = $FreshProcessCount + 7");
        await Assert.That(runner).Contains(
            "[ValidateRange(15, 15)]");
        await Assert.That(runner).Contains(
            "Viewer HWND ownership: phase=initial-camera-automatic-before-after; " +
            "backend=D3D12;.*live=0; visible=0; stale=0;.*retiredCleanup=0");
        await Assert.That(runner).DoesNotContain(
            "Viewer HWND ownership: phase=initial-after; backend=D3D12;");
        await Assert.That(runner).DoesNotContain("ABI4");
        await Assert.That(contract).DoesNotContain("ABI4");
        await Assert.That(identity).Contains("ViewerCameraPropagationTests.cs");
        await Assert.That(identity).Contains(
            "test-assets/viewer-stage-camera-smoke.usda");
        await Assert.That(identity).Contains(
            "eng/run-viewer-stage-camera-smoke.ps1");
        await Assert.That(runViewer).Contains("-EvidenceCameraPath");
        await Assert.That(runViewer).Contains(
            "OPENUSD_VIEWER_STAGE_CAMERA_PATH");
        await Assert.That(backends).DoesNotContain("CameraState.Default");
    }

    [Test]
    public async Task MandatoryStormChildRunnersRequireAbiSevenProvenance()
    {
        string root = FindRepositoryRoot();
        string windowsRunner = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "run-storm-native-child.ps1"));
        string linuxRunner = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "run-storm-native-child-linux.sh"));

        const string captureApi =
            "openusd_storm_child_capture_framebuffer(ABI7,preserved-texture)";
        const string navigationDeliveryApi =
            "SendMessageTimeoutW+StormChildWndProc+ABI7Poll+" +
            "ViewerCameraNavigationUiAdapter";
        const string navigationSnapshotApi =
            "openusd_storm_child_get_navigation_input(ABI7,v1)";

        await Assert.That(windowsRunner).Contains(
            "[int]$artifact.stormChildAbiVersion -ne 7");
        await Assert.That(windowsRunner).Contains(captureApi);
        await Assert.That(windowsRunner).Contains(navigationDeliveryApi);
        await Assert.That(windowsRunner).Contains(navigationSnapshotApi);
        await Assert.That(windowsRunner).Contains(
            "stormChildAbiVersion = [int]$navigation.stormChildAbiVersion");
        await Assert.That(windowsRunner).DoesNotContain("ABI6");
        await Assert.That(windowsRunner).DoesNotContain("ABI 6");
        await Assert.That(windowsRunner).DoesNotContain(
            "stormChildAbiVersion -ne 6");

        await Assert.That(linuxRunner).Contains(
            "if get(value, \"stormChildAbiVersion\") != 7:");
        await Assert.That(linuxRunner).Contains(
            "Viewer evidence Storm child ABI must be 7.");
        await Assert.That(linuxRunner).Contains(captureApi);
        await Assert.That(linuxRunner).Contains(
            "Storm Viewer pixels did not use the ABI 7 capture label.");
        await Assert.That(linuxRunner).DoesNotContain("ABI6");
        await Assert.That(linuxRunner).DoesNotContain("ABI 6");
        await Assert.That(linuxRunner).DoesNotContain(
            "\"stormChildAbiVersion\") != 6");
    }

    [Test]
    public async Task PickingIntegrationRemainsBoundToFinalBackendContracts()
    {
        string root = FindRepositoryRoot();
        string adapters = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerRenderBackendAdapters.cs"));
        string coordinator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerRenderCoordinator.cs"));
        string host = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "AvaloniaViewerRenderBackendHost.cs"));
        string storm = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "StormViewportControl.cs"));
        string nativeStorm = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "StormNativeControlHost.cs"));
        string picking = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerPicking.cs"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string startup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerStartupOptions.cs"));
        string smoke = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerPickingSmoke.cs"));
        string smokeRunner = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "run-viewer-picking-smoke.ps1"));
        string viewportHost = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "RendererSwitchingViewport.cs"));

        await Assert.That(adapters).Contains("IRenderPickingBackend");
        await Assert.That(adapters).Contains("RenderBackendCapability.Picking");
        await Assert.That(coordinator).Contains("_pickQueue.PickAsync(");
        await Assert.That(coordinator).Contains("ReapplyCurrentStateAfterSwitchAsync(");
        await Assert.That(coordinator).Contains(".UpdateStateAsync(state,");
        await Assert.That(host).Contains("control.PickHostedAsync(request, cancellationToken)");
        await Assert.That(host).Contains("control.PickAsync(request, cancellationToken)");
        await Assert.That(host).Contains(
            "SilkPickFrameBinding.FromState(state, sceneRevision: null)");
        await Assert.That(host).Contains(
            "renderer.UpdateSelection(");
        await Assert.That(host).Contains(
            "context.Renderer.UpdateSelection(");
        await Assert.That(host).Contains(
            "SilkSelectionOutlineSettings.Default");
        await Assert.That(storm).Contains("renderer.Pick(pending.Request)");
        await Assert.That(storm).Contains("ViewerPickingPolicy.StormSelectionColor");
        await Assert.That(nativeStorm).Contains(
            "GetSession().SetSelection(selection, ViewerPickingPolicy.StormSelectionColor)");
        await Assert.That(picking).Contains("Superseding a request cancels its token");
        await Assert.That(picking).Contains("for (int attempt = 0; attempt < 2; attempt++)");
        await Assert.That(window).Contains("OnPickPointerPressed");
        await Assert.That(window).Contains("_stormPickInput.TryUpdate(");
        await Assert.That(window).Contains("ViewerPickPixelMapper.TryMap(");
        await Assert.That(window).Contains("case RenderPickStatus.Stale:");
        await Assert.That(window).Contains("case RenderPickStatus.Unsupported:");
        await Assert.That(window).Contains("ApplyPickedHitAsync(");
        await Assert.That(window).Contains("Subprim element");
        await Assert.That(startup).Contains("OPENUSD_VIEWER_PICK_SMOKE_PATH");
        await Assert.That(smoke).Contains("CurrentSchemaVersion = 3");
        await Assert.That(smoke).Contains("viewer-picking-short-smoke");
        await Assert.That(smoke).Contains("ViewerSilkOutlineEvidence");
        await Assert.That(smoke).Contains("ViewerPickingSmokeHostObserver");
        await Assert.That(smokeRunner).Contains("-NativeRuntimeOverridePath");
        await Assert.That(smokeRunner).Contains("selectionPreservedAcrossSwitches");
        await Assert.That(smokeRunner).Contains("hostPickHitObserved");
        await Assert.That(smokeRunner).Contains("silkOutlines");
        await Assert.That(viewportHost).Contains("ExerciseStormClickAsync(");
        await Assert.That(viewportHost).Contains("ExerciseCompositionClickAsync(");
        await Assert.That(viewportHost).Contains("WM_LBUTTONDOWN(click)");
        await Assert.That(smokeRunner).DoesNotContain("viewer-evidence-contract.ps1");
        await Assert.That(host).DoesNotContain("visible outline is rendered");
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                $"Could not locate source range '{startMarker}' to '{endMarker}'.");
        }
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static void AssertContainsInOrder(string source, params string[] values)
    {
        int offset = 0;
        foreach (string value in values)
        {
            int index = source.IndexOf(value, offset, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"Expected to find '{value}' after offset {offset}.");
            }
            offset = index + value.Length;
        }
    }

    private static string ExtractNativeStormPixelFormatAttributes(string nativeSource)
    {
        const string startMarker = "const NSOpenGLPixelFormatAttribute attributes[]";
        int start = nativeSource.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Could not find native Storm pixel format attributes.");
        }

        int end = nativeSource.IndexOf("NSOpenGLPixelFormat* format", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Could not find end of native Storm pixel format attributes.");
        }

        return nativeSource[start..end];
    }

    private static string ExtractManagedStormCglAttributes(string viewerHostSource)
    {
        const string startMarker = "int error = CGLChoosePixelFormat(";
        int start = viewerHostSource.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Could not find managed Storm CGL pixel format probe.");
        }

        int end = viewerHostSource.IndexOf("out count);", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Could not find end of managed Storm CGL pixel format probe.");
        }

        return viewerHostSource[start..end];
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
        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
