// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>The menu a command belongs to in the target menu-first shell.</summary>
internal enum ViewerCommandGroup
{
    File,
    View,
    Render,
    Camera,
    Physics,
    Tools,
    Help,
}

/// <summary>How a command's current state is presented, if at all.</summary>
internal enum ViewerCommandCheckKind
{
    /// <summary>A plain action with no persistent checked state.</summary>
    None,

    /// <summary>An independent boolean toggle, shown with a check mark.</summary>
    Check,

    /// <summary>One of a mutually exclusive set, shown with a radio mark.</summary>
    Radio,
}

/// <summary>
/// Describes one stable Viewer command: the menu it belongs to, the text a
/// menu item or button would show, the input gesture that already triggers
/// it (if any), whether it carries checked/radio state, and the accessible
/// name automation already exposes for it.
/// </summary>
/// <param name="Id">
/// A stable, dotted identity (for example <c>"camera.frameSelected"</c>) that
/// never changes once assigned, even if <see cref="Label"/> or
/// <see cref="Group"/> changes. Automation, settings, and future menu/toolbar
/// code should key off this rather than display text or position.
/// </param>
/// <param name="Group">The menu this command belongs to.</param>
/// <param name="Label">
/// The text a menu item or button shows, including the existing mnemonic
/// marker where one is already assigned.
/// </param>
/// <param name="AccessibleName">
/// The screen-reader name this action already exposes (or should expose),
/// matching the <c>AutomationProperties.Name</c> the Viewer sets today.
/// </param>
/// <param name="CheckKind">Whether this command has checked or radio state.</param>
/// <param name="RadioGroup">
/// When <see cref="CheckKind"/> is <see cref="ViewerCommandCheckKind.Radio"/>,
/// the identity shared by every other option in the same mutually exclusive
/// set; otherwise <see langword="null"/>.
/// </param>
/// <param name="Gesture">
/// The input gesture already bound to this command (for example
/// <c>"Ctrl+O"</c> or <c>"F"</c>), or <see langword="null"/> when none is
/// bound today.
/// </param>
internal sealed record ViewerCommandDescriptor(
    string Id,
    ViewerCommandGroup Group,
    string Label,
    string AccessibleName,
    ViewerCommandCheckKind CheckKind = ViewerCommandCheckKind.None,
    string? RadioGroup = null,
    string? Gesture = null);

/// <summary>
/// Stable identities for every Viewer command the catalog describes. Consumers
/// key lookups and settings off these constants instead of restating the
/// dotted string, so a typo becomes a compile error rather than a silent
/// lookup miss.
/// </summary>
internal static class ViewerCommandIds
{
    internal const string FileOpenStage = "file.openStage";
    internal const string FileRecentStages = "file.recentStages";
    internal const string FileReloadStage = "file.reloadStage";
    internal const string FileCaptureFrame = "file.captureFrame";
    internal const string FileExit = "file.exit";

    internal const string ViewStagePanel = "view.stagePanel";
    internal const string ViewInspectorPanel = "view.inspectorPanel";
    internal const string ViewTimeline = "view.timeline";
    internal const string ViewDiagnosticsTab = "view.diagnosticsTab";
    internal const string ViewHydraTabVisible = "view.hydraTabVisible";
    internal const string ViewTfDebugTabVisible = "view.tfDebugTabVisible";
    internal const string ViewSnapTimelineToFrames = "view.snapTimelineToFrames";
    internal const string ViewResetLayout = "view.resetLayout";

    internal const string RenderRendererAuto = "render.renderer.auto";
    internal const string RenderRendererStorm = "render.renderer.storm";
    internal const string RenderRendererD3D12 = "render.renderer.d3d12";
    internal const string RenderRendererVulkan = "render.renderer.vulkan";
    internal const string RenderRendererMetal = "render.renderer.metal";
    internal const string RenderDrawModeWireframe = "render.drawMode.wireframe";
    internal const string RenderDrawModeWireframeOnSurface = "render.drawMode.wireframeOnSurface";
    internal const string RenderDrawModeSmoothShaded = "render.drawMode.smoothShaded";
    internal const string RenderDrawModeFlatShaded = "render.drawMode.flatShaded";
    internal const string RenderDrawModePoints = "render.drawMode.points";
    internal const string RenderDrawModeGeomOnly = "render.drawMode.geomOnly";
    internal const string RenderDrawModeGeomFlat = "render.drawMode.geomFlat";
    internal const string RenderDrawModeGeomSmooth = "render.drawMode.geomSmooth";
    internal const string RenderDrawModeHiddenSurfaceWireframe =
        "render.drawMode.hiddenSurfaceWireframe";
    internal const string RenderPurposeDefault = "render.purpose.default";
    internal const string RenderPurposeProxy = "render.purpose.proxy";
    internal const string RenderPurposeRender = "render.purpose.render";
    internal const string RenderPurposeGuide = "render.purpose.guide";
    internal const string RenderSceneLighting = "render.sceneLighting";
    internal const string RenderSceneShadows = "render.sceneShadows";
    internal const string RenderBackfaceCulling = "render.backfaceCulling";
    internal const string RenderSceneMaterials = "render.sceneMaterials";
    internal const string RenderColorManagementEnabled = "render.colorManagement.enabled";
    internal const string RenderColorManagementChooseConfig =
        "render.colorManagement.chooseConfig";
    internal const string RenderColorManagementClearConfig =
        "render.colorManagement.clearConfig";
    internal const string RenderBackgroundColorBlack = "render.backgroundColor.black";
    internal const string RenderBackgroundColorDarkGray = "render.backgroundColor.darkGray";
    internal const string RenderBackgroundColorLightGray = "render.backgroundColor.lightGray";
    internal const string RenderBackgroundColorWhite = "render.backgroundColor.white";

    internal const string CameraResetAutomatic = "camera.resetAutomatic";
    internal const string CameraResetLegacyPose = "camera.resetLegacyPose";
    internal const string CameraToggleProjection = "camera.toggleProjection";
    internal const string CameraUseSelectedCamera = "camera.useSelectedCamera";
    internal const string CameraStageCameras = "camera.stageCameras";
    internal const string CameraFrameSelected = "camera.frameSelected";
    internal const string CameraOrbitLeft = "camera.orbitLeft";
    internal const string CameraOrbitRight = "camera.orbitRight";
    internal const string CameraOrbitUp = "camera.orbitUp";
    internal const string CameraOrbitDown = "camera.orbitDown";

    internal const string PhysicsEnable = "physics.enable";
    internal const string PhysicsPlayPause = "physics.playPause";
    internal const string PhysicsStop = "physics.stop";
    internal const string PhysicsStep = "physics.step";
    internal const string PhysicsLoop = "physics.loop";
    internal const string PhysicsSpeedQuarter = "physics.speed.quarter";
    internal const string PhysicsSpeedHalf = "physics.speed.half";
    internal const string PhysicsSpeedNormal = "physics.speed.normal";
    internal const string PhysicsSpeedDouble = "physics.speed.double";
    internal const string PhysicsSpeedQuadruple = "physics.speed.quadruple";
    internal const string PhysicsPreviewApply = "physics.previewApply";
    internal const string PhysicsBake = "physics.bake";
    internal const string PhysicsGizmoNone = "physics.gizmo.none";
    internal const string PhysicsGizmoMove = "physics.gizmo.move";
    internal const string PhysicsGizmoRotate = "physics.gizmo.rotate";
    internal const string PhysicsGizmoScale = "physics.gizmo.scale";
    internal const string PhysicsGizmoDrag = "physics.gizmo.drag";
    internal const string PhysicsSnap = "physics.snap";
    internal const string PhysicsUndo = "physics.undo";
    internal const string PhysicsRedo = "physics.redo";
    internal const string PhysicsRefreshProperties = "physics.refreshProperties";
    internal const string PhysicsApplyProperty = "physics.applyProperty";
    internal const string PhysicsClearProperty = "physics.clearProperty";
    internal const string PhysicsApplyForce = "physics.applyForce";
    internal const string PhysicsApplyImpulse = "physics.applyImpulse";
    internal const string PhysicsApplyTorque = "physics.applyTorque";
    internal const string PhysicsWake = "physics.wake";
    internal const string PhysicsSleep = "physics.sleep";
    internal const string PhysicsControllerDrive = "physics.controllerDrive";
    internal const string PhysicsVehicleDrive = "physics.vehicleDrive";
    internal const string PhysicsShowInspector = "physics.showInspector";

    internal const string ToolsValidationRun = "tools.validationRun";
    internal const string ToolsValidationScopeStage = "tools.validationScope.stage";
    internal const string ToolsValidationScopePrim = "tools.validationScope.prim";
    internal const string ToolsPickModePrims = "tools.pickMode.prims";
    internal const string ToolsPickModeModels = "tools.pickMode.models";
    internal const string ToolsPickModeInstances = "tools.pickMode.instances";
    internal const string ToolsPickModePrototypes = "tools.pickMode.prototypes";
    internal const string ToolsPickTargetPrimitive = "tools.pickTarget.primitive";
    internal const string ToolsPickTargetFace = "tools.pickTarget.face";
    internal const string ToolsPickTargetEdge = "tools.pickTarget.edge";
    internal const string ToolsPickTargetPoint = "tools.pickTarget.point";
    internal const string ToolsSelectionVisibleOnly = "tools.selection.visibleOnly";
    internal const string ToolsSelectionXRay = "tools.selection.xray";
    internal const string ToolsDeveloperCopyDiagnostics = "tools.developer.copyDiagnostics";
    internal const string ToolsDeveloperExportDiagnostics = "tools.developer.exportDiagnostics";
    internal const string ToolsDeveloperIncludeDiagnosticPaths =
        "tools.developer.includeDiagnosticPaths";
    internal const string ToolsDeveloperRefreshHydraScene = "tools.developer.refreshHydraScene";
    internal const string ToolsDeveloperRefreshTfDebug = "tools.developer.refreshTfDebug";
    internal const string ToolsConnectionsOmniverseBridge =
        "tools.connections.omniverseBridge";
    internal const string ToolsConnectionsBridgeConnect = "tools.connections.bridge.connect";
    internal const string ToolsConnectionsBridgeDisconnect =
        "tools.connections.bridge.disconnect";
    internal const string ToolsConnectionsBridgeResync = "tools.connections.bridge.resync";

    internal const string HelpShortcuts = "help.shortcuts";
    internal const string HelpAbout = "help.about";
}

/// <summary>
/// The single, shared source of label, menu group, gesture, check/radio
/// semantics, and accessible name for every Viewer command that exists
/// today, plus a small number of planned commands the next workstream needs
/// (currently the Hydra and TfDebug tab visibility toggles, which have no
/// control yet).
/// </summary>
/// <remarks>
/// This is deliberately data, not menu markup: a future menu or toolbar
/// reads it instead of restating each command's text and semantics a second
/// time. Every entry mirrors a control that already ships in
/// <c>MainWindow.axaml</c> today, except the handful explicitly marked
/// "Planned" in <see cref="BuildCatalog"/> (see
/// <c>ViewerCommandCatalogTests</c>), grouped under the menu it belongs to in
/// the eventual menu-first shell rather than the ad hoc toolbar row or tab it
/// currently lives in. Adding a command here does not move, add, or remove
/// any UI by itself.
/// </remarks>
internal static class ViewerCommandCatalog
{
    internal static IReadOnlyList<ViewerCommandDescriptor> All { get; } = BuildCatalog();

    private static readonly Dictionary<string, ViewerCommandDescriptor> ById =
        All.ToDictionary(static command => command.Id, StringComparer.Ordinal);

    /// <summary>Returns the descriptor for <paramref name="id"/>.</summary>
    /// <exception cref="KeyNotFoundException">No command has this identity.</exception>
    internal static ViewerCommandDescriptor Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ById[id];
    }

    internal static bool TryGet(string id, out ViewerCommandDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ById.TryGetValue(id, out descriptor);
    }

    internal static IEnumerable<ViewerCommandDescriptor> ForGroup(ViewerCommandGroup group) =>
        All.Where(command => command.Group == group);

    private static IReadOnlyList<ViewerCommandDescriptor> BuildCatalog() =>
    [
        // File
        new(ViewerCommandIds.FileOpenStage, ViewerCommandGroup.File,
            "_Open Stage...", "Open stage", Gesture: "Ctrl+O"),
        new(ViewerCommandIds.FileRecentStages, ViewerCommandGroup.File,
            "Recent Stages", "Recent stages"),
        new(ViewerCommandIds.FileReloadStage, ViewerCommandGroup.File,
            "_Reload Stage", "Reload current stage", Gesture: "Ctrl+R"),
        new(ViewerCommandIds.FileCaptureFrame, ViewerCommandGroup.File,
            "_Capture Frame...", "Capture current frame"),
        new(ViewerCommandIds.FileExit, ViewerCommandGroup.File,
            "E_xit", "Exit OpenUsd Viewer"),

        // View
        new(ViewerCommandIds.ViewStagePanel, ViewerCommandGroup.View,
            "_Stage panel", "Show stage panel", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewInspectorPanel, ViewerCommandGroup.View,
            "_Inspector panel", "Show inspector panel", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewTimeline, ViewerCommandGroup.View,
            "_Timeline", "Show timeline", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewDiagnosticsTab, ViewerCommandGroup.View,
            "_Diagnostics tab", "Show diagnostics tab", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewHydraTabVisible, ViewerCommandGroup.View,
            "_Hydra tab", "Show Hydra scene tab", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewTfDebugTabVisible, ViewerCommandGroup.View,
            "_TfDebug tab", "Show TfDebug tab", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewSnapTimelineToFrames, ViewerCommandGroup.View,
            "Snap manual time changes to authored frames",
            "Snap manual timeline changes to authored frames",
            ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ViewResetLayout, ViewerCommandGroup.View,
            "_Reset Layout", "Reset the Viewer layout to its clean defaults"),

        // Render
        new(ViewerCommandIds.RenderRendererAuto, ViewerCommandGroup.Render,
            "Automatic", "Rendering backend: Automatic",
            ViewerCommandCheckKind.Radio, "render.renderer"),
        new(ViewerCommandIds.RenderRendererStorm, ViewerCommandGroup.Render,
            "Storm", "Rendering backend: Storm",
            ViewerCommandCheckKind.Radio, "render.renderer"),
        new(ViewerCommandIds.RenderRendererD3D12, ViewerCommandGroup.Render,
            "Direct3D 12", "Rendering backend: Direct3D 12",
            ViewerCommandCheckKind.Radio, "render.renderer"),
        new(ViewerCommandIds.RenderRendererVulkan, ViewerCommandGroup.Render,
            "Vulkan", "Rendering backend: Vulkan",
            ViewerCommandCheckKind.Radio, "render.renderer"),
        new(ViewerCommandIds.RenderRendererMetal, ViewerCommandGroup.Render,
            "Metal", "Rendering backend: Metal",
            ViewerCommandCheckKind.Radio, "render.renderer"),
        new(ViewerCommandIds.RenderDrawModeWireframe, ViewerCommandGroup.Render,
            "Wireframe", "Viewport draw mode: Wireframe",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeWireframeOnSurface, ViewerCommandGroup.Render,
            "Wireframe on surface", "Viewport draw mode: Wireframe on surface",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeSmoothShaded, ViewerCommandGroup.Render,
            "Smooth shaded", "Viewport draw mode: Smooth shaded",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeFlatShaded, ViewerCommandGroup.Render,
            "Flat shaded", "Viewport draw mode: Flat shaded",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModePoints, ViewerCommandGroup.Render,
            "Points", "Viewport draw mode: Points",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeGeomOnly, ViewerCommandGroup.Render,
            "Geom only", "Viewport draw mode: Geom only",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeGeomFlat, ViewerCommandGroup.Render,
            "Geom flat", "Viewport draw mode: Geom flat",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeGeomSmooth, ViewerCommandGroup.Render,
            "Geom smooth", "Viewport draw mode: Geom smooth",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderDrawModeHiddenSurfaceWireframe, ViewerCommandGroup.Render,
            "Hidden surface wireframe", "Viewport draw mode: Hidden surface wireframe",
            ViewerCommandCheckKind.Radio, "render.drawMode"),
        new(ViewerCommandIds.RenderPurposeDefault, ViewerCommandGroup.Render,
            "Default", "Show default purpose", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderPurposeProxy, ViewerCommandGroup.Render,
            "Proxy", "Show proxy purpose", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderPurposeRender, ViewerCommandGroup.Render,
            "Render", "Show render purpose", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderPurposeGuide, ViewerCommandGroup.Render,
            "Guide", "Show guide purpose", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderSceneLighting, ViewerCommandGroup.Render,
            "Use scene lighting", "Use scene lighting", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderSceneShadows, ViewerCommandGroup.Render,
            "Use shadows", "Use scene shadows", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderBackfaceCulling, ViewerCommandGroup.Render,
            "Cull backfaces", "Cull backfaces", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderSceneMaterials, ViewerCommandGroup.Render,
            "Use scene materials", "Use authored scene materials", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderColorManagementEnabled, ViewerCommandGroup.Render,
            "Use OpenColorIO display transform", "Use OpenColorIO display transform",
            ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.RenderColorManagementChooseConfig, ViewerCommandGroup.Render,
            "Choose OpenColorIO _Config...", "Choose OpenColorIO config"),
        new(ViewerCommandIds.RenderColorManagementClearConfig, ViewerCommandGroup.Render,
            "Clear OpenColorIO Config", "Clear OpenColorIO config"),
        new(ViewerCommandIds.RenderBackgroundColorBlack, ViewerCommandGroup.Render,
            "Black", "Viewport background colour: Black",
            ViewerCommandCheckKind.Radio, "render.backgroundColor"),
        new(ViewerCommandIds.RenderBackgroundColorDarkGray, ViewerCommandGroup.Render,
            "Dark gray", "Viewport background colour: Dark gray",
            ViewerCommandCheckKind.Radio, "render.backgroundColor"),
        new(ViewerCommandIds.RenderBackgroundColorLightGray, ViewerCommandGroup.Render,
            "Light gray", "Viewport background colour: Light gray",
            ViewerCommandCheckKind.Radio, "render.backgroundColor"),
        new(ViewerCommandIds.RenderBackgroundColorWhite, ViewerCommandGroup.Render,
            "White", "Viewport background colour: White",
            ViewerCommandCheckKind.Radio, "render.backgroundColor"),

        // Camera
        new(ViewerCommandIds.CameraResetAutomatic, ViewerCommandGroup.Camera,
            "Reset _Automatic", "Reset camera to automatic", Gesture: "Home"),
        new(ViewerCommandIds.CameraResetLegacyPose, ViewerCommandGroup.Camera,
            "Explicit _Legacy Pose", "Use explicit legacy camera pose"),
        new(ViewerCommandIds.CameraToggleProjection, ViewerCommandGroup.Camera,
            "Toggle _Projection", "Toggle camera projection", Gesture: "P"),
        new(ViewerCommandIds.CameraUseSelectedCamera, ViewerCommandGroup.Camera,
            "_Use Selected Camera", "Use selected UsdGeomCamera"),
        new(ViewerCommandIds.CameraStageCameras, ViewerCommandGroup.Camera,
            "Stage _Cameras", "Stage-authored cameras"),
        new(ViewerCommandIds.CameraFrameSelected, ViewerCommandGroup.Camera,
            "_Frame Selected", "Frame selected prim", Gesture: "F"),
        new(ViewerCommandIds.CameraOrbitLeft, ViewerCommandGroup.Camera,
            "Orbit Left", "Orbit camera left", Gesture: "Left Arrow"),
        new(ViewerCommandIds.CameraOrbitRight, ViewerCommandGroup.Camera,
            "Orbit Right", "Orbit camera right", Gesture: "Right Arrow"),
        new(ViewerCommandIds.CameraOrbitUp, ViewerCommandGroup.Camera,
            "Orbit Up", "Orbit camera up", Gesture: "Up Arrow"),
        new(ViewerCommandIds.CameraOrbitDown, ViewerCommandGroup.Camera,
            "Orbit Down", "Orbit camera down", Gesture: "Down Arrow"),

        // Physics
        new(ViewerCommandIds.PhysicsEnable, ViewerCommandGroup.Physics,
            "_Physics", "Enable physics simulation"),
        new(ViewerCommandIds.PhysicsPlayPause, ViewerCommandGroup.Physics,
            "Play/Pause", "Play physics simulation", Gesture: "K"),
        new(ViewerCommandIds.PhysicsStop, ViewerCommandGroup.Physics,
            "Stop", "Stop physics simulation", Gesture: "J"),
        new(ViewerCommandIds.PhysicsStep, ViewerCommandGroup.Physics,
            "Step", "Step one physics frame", Gesture: "N"),
        new(ViewerCommandIds.PhysicsLoop, ViewerCommandGroup.Physics,
            "Loop", "Loop physics playback", ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.PhysicsSpeedQuarter, ViewerCommandGroup.Physics,
            "0.25x", "Physics playback speed: 0.25x",
            ViewerCommandCheckKind.Radio, "physics.speed"),
        new(ViewerCommandIds.PhysicsSpeedHalf, ViewerCommandGroup.Physics,
            "0.5x", "Physics playback speed: 0.5x",
            ViewerCommandCheckKind.Radio, "physics.speed"),
        new(ViewerCommandIds.PhysicsSpeedNormal, ViewerCommandGroup.Physics,
            "1x", "Physics playback speed: 1x",
            ViewerCommandCheckKind.Radio, "physics.speed"),
        new(ViewerCommandIds.PhysicsSpeedDouble, ViewerCommandGroup.Physics,
            "2x", "Physics playback speed: 2x",
            ViewerCommandCheckKind.Radio, "physics.speed"),
        new(ViewerCommandIds.PhysicsSpeedQuadruple, ViewerCommandGroup.Physics,
            "4x", "Physics playback speed: 4x",
            ViewerCommandCheckKind.Radio, "physics.speed"),
        new(ViewerCommandIds.PhysicsPreviewApply, ViewerCommandGroup.Physics,
            "Apply Preview", "Apply physics preview to the session overlay",
            ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.PhysicsBake, ViewerCommandGroup.Physics,
            "Bake...", "Bake physics simulation", Gesture: "B"),
        new(ViewerCommandIds.PhysicsGizmoNone, ViewerCommandGroup.Physics,
            "No Gizmo", "Physics manipulation gizmo: No Gizmo",
            ViewerCommandCheckKind.Radio, "physics.gizmo", "Q"),
        new(ViewerCommandIds.PhysicsGizmoMove, ViewerCommandGroup.Physics,
            "Move", "Physics manipulation gizmo: Move",
            ViewerCommandCheckKind.Radio, "physics.gizmo", "G"),
        new(ViewerCommandIds.PhysicsGizmoRotate, ViewerCommandGroup.Physics,
            "Rotate", "Physics manipulation gizmo: Rotate",
            ViewerCommandCheckKind.Radio, "physics.gizmo", "E"),
        new(ViewerCommandIds.PhysicsGizmoScale, ViewerCommandGroup.Physics,
            "Scale", "Physics manipulation gizmo: Scale",
            ViewerCommandCheckKind.Radio, "physics.gizmo", "R"),
        new(ViewerCommandIds.PhysicsGizmoDrag, ViewerCommandGroup.Physics,
            "Drag Body", "Physics manipulation gizmo: Drag Body",
            ViewerCommandCheckKind.Radio, "physics.gizmo", "H"),
        new(ViewerCommandIds.PhysicsSnap, ViewerCommandGroup.Physics,
            "Snap", "Snap gizmo drags to increments", ViewerCommandCheckKind.Check,
            Gesture: "X"),
        new(ViewerCommandIds.PhysicsUndo, ViewerCommandGroup.Physics,
            "Undo", "Undo the last physics property edit", Gesture: "Z"),
        new(ViewerCommandIds.PhysicsRedo, ViewerCommandGroup.Physics,
            "Redo", "Redo the last undone physics property edit", Gesture: "Y"),
        new(ViewerCommandIds.PhysicsRefreshProperties, ViewerCommandGroup.Physics,
            "Reload Properties", "Reload physics properties"),
        new(ViewerCommandIds.PhysicsApplyProperty, ViewerCommandGroup.Physics,
            "Apply", "Author the physics property"),
        new(ViewerCommandIds.PhysicsClearProperty, ViewerCommandGroup.Physics,
            "Clear", "Clear the authored physics property"),
        new(ViewerCommandIds.PhysicsApplyForce, ViewerCommandGroup.Physics,
            "Force", "Apply a force to the selected body"),
        new(ViewerCommandIds.PhysicsApplyImpulse, ViewerCommandGroup.Physics,
            "Impulse", "Apply an impulse to the selected body"),
        new(ViewerCommandIds.PhysicsApplyTorque, ViewerCommandGroup.Physics,
            "Torque", "Apply a torque to the selected body"),
        new(ViewerCommandIds.PhysicsWake, ViewerCommandGroup.Physics,
            "Wake", "Wake the selected body"),
        new(ViewerCommandIds.PhysicsSleep, ViewerCommandGroup.Physics,
            "Sleep", "Put the selected body to sleep"),
        new(ViewerCommandIds.PhysicsControllerDrive, ViewerCommandGroup.Physics,
            "Drive with WASD", "Drive the selected character controller",
            ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.PhysicsVehicleDrive, ViewerCommandGroup.Physics,
            "Send vehicle input every step", "Drive the selected vehicle",
            ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.PhysicsShowInspector, ViewerCommandGroup.Physics,
            "Show Physics _Inspector", "Show the Physics inspector tab"),

        // Tools
        new(ViewerCommandIds.ToolsValidationRun, ViewerCommandGroup.Tools,
            "_Refresh", "Refresh UsdValidation results"),
        new(ViewerCommandIds.ToolsValidationScopeStage, ViewerCommandGroup.Tools,
            "Whole stage", "UsdValidation scope: Whole stage",
            ViewerCommandCheckKind.Radio, "tools.validationScope"),
        new(ViewerCommandIds.ToolsValidationScopePrim, ViewerCommandGroup.Tools,
            "Selected prim", "UsdValidation scope: Selected prim",
            ViewerCommandCheckKind.Radio, "tools.validationScope"),
        new(ViewerCommandIds.ToolsPickModePrims, ViewerCommandGroup.Tools,
            "Prims", "usdview pick mode: Prims",
            ViewerCommandCheckKind.Radio, "tools.pickMode"),
        new(ViewerCommandIds.ToolsPickModeModels, ViewerCommandGroup.Tools,
            "Models", "usdview pick mode: Models",
            ViewerCommandCheckKind.Radio, "tools.pickMode"),
        new(ViewerCommandIds.ToolsPickModeInstances, ViewerCommandGroup.Tools,
            "Instances", "usdview pick mode: Instances",
            ViewerCommandCheckKind.Radio, "tools.pickMode"),
        new(ViewerCommandIds.ToolsPickModePrototypes, ViewerCommandGroup.Tools,
            "Prototypes", "usdview pick mode: Prototypes",
            ViewerCommandCheckKind.Radio, "tools.pickMode"),
        new(ViewerCommandIds.ToolsPickTargetPrimitive, ViewerCommandGroup.Tools,
            "Prim", "Pick target: Prim",
            ViewerCommandCheckKind.Radio, "tools.pickTarget"),
        new(ViewerCommandIds.ToolsPickTargetFace, ViewerCommandGroup.Tools,
            "Face", "Pick target: Authored face",
            ViewerCommandCheckKind.Radio, "tools.pickTarget"),
        new(ViewerCommandIds.ToolsPickTargetEdge, ViewerCommandGroup.Tools,
            "Edge", "Pick target: Authored edge",
            ViewerCommandCheckKind.Radio, "tools.pickTarget"),
        new(ViewerCommandIds.ToolsPickTargetPoint, ViewerCommandGroup.Tools,
            "Point", "Pick target: Authored point",
            ViewerCommandCheckKind.Radio, "tools.pickTarget"),
        new(ViewerCommandIds.ToolsSelectionVisibleOnly, ViewerCommandGroup.Tools,
            "Visible only", "Selection outline: Visible only",
            ViewerCommandCheckKind.Radio, "tools.selection"),
        new(ViewerCommandIds.ToolsSelectionXRay, ViewerCommandGroup.Tools,
            "X-ray", "Selection outline: X-ray through occluders",
            ViewerCommandCheckKind.Radio, "tools.selection"),
        new(ViewerCommandIds.ToolsDeveloperCopyDiagnostics, ViewerCommandGroup.Tools,
            "_Copy", "Copy redacted diagnostics"),
        new(ViewerCommandIds.ToolsDeveloperExportDiagnostics, ViewerCommandGroup.Tools,
            "_Export...", "Export redacted diagnostics"),
        new(ViewerCommandIds.ToolsDeveloperIncludeDiagnosticPaths, ViewerCommandGroup.Tools,
            "Include paths", "Include source and user paths in diagnostics",
            ViewerCommandCheckKind.Check),
        new(ViewerCommandIds.ToolsDeveloperRefreshHydraScene, ViewerCommandGroup.Tools,
            "_Refresh", "Refresh Hydra scene browser"),
        new(ViewerCommandIds.ToolsDeveloperRefreshTfDebug, ViewerCommandGroup.Tools,
            "_Refresh", "Refresh TfDebug flags"),
        // Reachable only when an embedding host injects an IViewerBridgeConnectionProvider
        // through ViewerHostOptions.BridgeConnection. With no provider the entry stays hidden
        // and disabled, and the base Viewer keeps no networking, gRPC, or NVIDIA dependency:
        // the transport lives in the optional OpenUsd.Viewer.Bridge.Grpc package instead.
        new(ViewerCommandIds.ToolsConnectionsOmniverseBridge, ViewerCommandGroup.Tools,
            "Omniverse _Bridge...", "Connect to an Omniverse Bridge session"),
        new(ViewerCommandIds.ToolsConnectionsBridgeConnect, ViewerCommandGroup.Tools,
            "_Connect", "Connect to the selected Omniverse Bridge session"),
        new(ViewerCommandIds.ToolsConnectionsBridgeDisconnect, ViewerCommandGroup.Tools,
            "_Disconnect", "Disconnect the current Omniverse Bridge session"),
        new(ViewerCommandIds.ToolsConnectionsBridgeResync, ViewerCommandGroup.Tools,
            "_Resync", "Resynchronize the Omniverse Bridge session"),

        // Help
        new(ViewerCommandIds.HelpShortcuts, ViewerCommandGroup.Help,
            "Keyboard _Shortcuts", "Show keyboard and mouse shortcuts", Gesture: "F1"),
        new(ViewerCommandIds.HelpAbout, ViewerCommandGroup.Help,
            "_About OpenUsd Viewer...", "Show OpenUSD and Omniverse compatibility information"),
    ];
}
