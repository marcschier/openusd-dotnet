// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Requires the command catalog to stay a trustworthy single source: every
/// identity unique, every command filed under exactly one menu, every radio
/// group internally consistent, every command accessible, and every current
/// key action still represented.
/// </summary>
public sealed class ViewerCommandCatalogTests
{
    [Test]
    public async Task AllIsNotEmpty()
    {
        await Assert.That(ViewerCommandCatalog.All).IsNotEmpty();
    }

    [Test]
    public async Task EveryCommandIdIsUnique()
    {
        List<string> duplicates = [.. ViewerCommandCatalog.All
            .GroupBy(command => command.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        await Assert.That(duplicates)
            .IsEmpty()
            .Because("command identities must be unique: " + string.Join(", ", duplicates));
    }

    [Test]
    public async Task EveryCommandIdIsNonEmptyAndDotted()
    {
        List<string> malformed = [.. ViewerCommandCatalog.All
            .Where(command =>
                string.IsNullOrWhiteSpace(command.Id) ||
                command.Id != command.Id.Trim() ||
                !command.Id.Contains('.', StringComparison.Ordinal))
            .Select(command => command.Id)];

        await Assert.That(malformed).IsEmpty();
    }

    [Test]
    public async Task EveryCommandBelongsToExactlyOneDefinedMenuGroup()
    {
        ViewerCommandGroup[] definedGroups =
        [
            ViewerCommandGroup.File,
            ViewerCommandGroup.View,
            ViewerCommandGroup.Render,
            ViewerCommandGroup.Camera,
            ViewerCommandGroup.Physics,
            ViewerCommandGroup.Tools,
            ViewerCommandGroup.Help,
        ];

        List<string> misfiled = [.. ViewerCommandCatalog.All
            .Where(command => !definedGroups.Contains(command.Group))
            .Select(command => command.Id)];

        await Assert.That(misfiled).IsEmpty();
    }

    [Test]
    public async Task EveryMenuGroupHasAtLeastOneCommand()
    {
        foreach (ViewerCommandGroup group in Enum.GetValues<ViewerCommandGroup>())
        {
            int count = ViewerCommandCatalog.ForGroup(group).Count();
            await Assert.That(count)
                .IsGreaterThan(0)
                .Because($"the {group} menu has no catalog entries");
        }
    }

    [Test]
    public async Task EveryRadioGroupSharesExactlyOneMenu()
    {
        List<string> mixed = [.. ViewerCommandCatalog.All
            .Where(command => command.RadioGroup is not null)
            .GroupBy(command => command.RadioGroup, StringComparer.Ordinal)
            .Where(group => group.Select(command => command.Group).Distinct().Count() > 1)
            .Select(group => group.Key!)];

        await Assert.That(mixed)
            .IsEmpty()
            .Because(
                "a radio group must not span multiple menus: " + string.Join(", ", mixed));
    }

    [Test]
    public async Task RadioGroupIsOnlySetWhenCheckKindIsRadio()
    {
        List<string> inconsistent = [.. ViewerCommandCatalog.All
            .Where(command =>
                (command.CheckKind == ViewerCommandCheckKind.Radio) !=
                (command.RadioGroup is not null))
            .Select(command => command.Id)];

        await Assert.That(inconsistent).IsEmpty();
    }

    [Test]
    public async Task EveryCommandHasNonEmptyAccessibleName()
    {
        List<string> missing = [.. ViewerCommandCatalog.All
            .Where(command => string.IsNullOrWhiteSpace(command.AccessibleName))
            .Select(command => command.Id)];

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task EveryCommandHasNonEmptyLabel()
    {
        List<string> missing = [.. ViewerCommandCatalog.All
            .Where(command => string.IsNullOrWhiteSpace(command.Label))
            .Select(command => command.Id)];

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [Arguments(ViewerCommandIds.FileOpenStage)]
    [Arguments(ViewerCommandIds.FileReloadStage)]
    [Arguments(ViewerCommandIds.FileCaptureFrame)]
    [Arguments(ViewerCommandIds.ViewStagePanel)]
    [Arguments(ViewerCommandIds.ViewInspectorPanel)]
    [Arguments(ViewerCommandIds.ViewTimeline)]
    [Arguments(ViewerCommandIds.ViewDiagnosticsTab)]
    [Arguments(ViewerCommandIds.RenderRendererAuto)]
    [Arguments(ViewerCommandIds.RenderDrawModeWireframe)]
    [Arguments(ViewerCommandIds.RenderSceneLighting)]
    [Arguments(ViewerCommandIds.RenderBackgroundColorBlack)]
    [Arguments(ViewerCommandIds.CameraFrameSelected)]
    [Arguments(ViewerCommandIds.CameraResetAutomatic)]
    [Arguments(ViewerCommandIds.CameraToggleProjection)]
    [Arguments(ViewerCommandIds.PhysicsEnable)]
    [Arguments(ViewerCommandIds.PhysicsPlayPause)]
    [Arguments(ViewerCommandIds.PhysicsGizmoMove)]
    [Arguments(ViewerCommandIds.PhysicsSpeedNormal)]
    [Arguments(ViewerCommandIds.PhysicsRefreshProperties)]
    [Arguments(ViewerCommandIds.PhysicsApplyProperty)]
    [Arguments(ViewerCommandIds.PhysicsClearProperty)]
    [Arguments(ViewerCommandIds.PhysicsApplyForce)]
    [Arguments(ViewerCommandIds.PhysicsApplyImpulse)]
    [Arguments(ViewerCommandIds.PhysicsApplyTorque)]
    [Arguments(ViewerCommandIds.PhysicsWake)]
    [Arguments(ViewerCommandIds.PhysicsSleep)]
    [Arguments(ViewerCommandIds.PhysicsControllerDrive)]
    [Arguments(ViewerCommandIds.PhysicsVehicleDrive)]
    [Arguments(ViewerCommandIds.ToolsValidationRun)]
    [Arguments(ViewerCommandIds.ToolsPickModePrims)]
    [Arguments(ViewerCommandIds.ToolsDeveloperCopyDiagnostics)]
    [Arguments(ViewerCommandIds.HelpShortcuts)]
    public async Task EveryCurrentKeyActionIsRepresented(string id)
    {
        await Assert.That(ViewerCommandCatalog.TryGet(id, out ViewerCommandDescriptor? descriptor))
            .IsTrue();
        await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task EveryPhysicsAuthoringActionWiredInMainWindowIsRepresented()
    {
        // MainWindow.PhysicsAuthoring.cs wires these controls today (property
        // inspector, force/impulse/torque, wake/sleep, controller/vehicle drive).
        // The catalog must not silently drop the authoring surface just because
        // it lives in a different partial file than the toolbar.
        string[] ids =
        [
            ViewerCommandIds.PhysicsRefreshProperties,
            ViewerCommandIds.PhysicsApplyProperty,
            ViewerCommandIds.PhysicsClearProperty,
            ViewerCommandIds.PhysicsApplyForce,
            ViewerCommandIds.PhysicsApplyImpulse,
            ViewerCommandIds.PhysicsApplyTorque,
            ViewerCommandIds.PhysicsWake,
            ViewerCommandIds.PhysicsSleep,
            ViewerCommandIds.PhysicsControllerDrive,
            ViewerCommandIds.PhysicsVehicleDrive,
        ];

        foreach (string id in ids)
        {
            ViewerCommandDescriptor descriptor = ViewerCommandCatalog.Get(id);
            await Assert.That(descriptor.Group).IsEqualTo(ViewerCommandGroup.Physics);
        }

        // The two drive toggles carry persistent checked state; the rest are actions.
        await Assert.That(ViewerCommandCatalog.Get(ViewerCommandIds.PhysicsControllerDrive).CheckKind)
            .IsEqualTo(ViewerCommandCheckKind.Check);
        await Assert.That(ViewerCommandCatalog.Get(ViewerCommandIds.PhysicsVehicleDrive).CheckKind)
            .IsEqualTo(ViewerCommandCheckKind.Check);
        await Assert.That(ViewerCommandCatalog.Get(ViewerCommandIds.PhysicsApplyForce).CheckKind)
            .IsEqualTo(ViewerCommandCheckKind.None);
    }

    [Test]
    public async Task PlannedDeveloperTabVisibilityCommandsExistForTheNextWorkstream()
    {
        // No control wires these yet (Hydra and TfDebug tabs are always visible
        // today), but the next workstream needs a single source of truth for
        // their label, accessible name, and check semantics rather than
        // inventing its own when it adds the toggle.
        ViewerCommandDescriptor hydra = ViewerCommandCatalog.Get(ViewerCommandIds.ViewHydraTabVisible);
        ViewerCommandDescriptor tfDebug = ViewerCommandCatalog.Get(ViewerCommandIds.ViewTfDebugTabVisible);

        await Assert.That(hydra.Group).IsEqualTo(ViewerCommandGroup.View);
        await Assert.That(hydra.CheckKind).IsEqualTo(ViewerCommandCheckKind.Check);
        await Assert.That(tfDebug.Group).IsEqualTo(ViewerCommandGroup.View);
        await Assert.That(tfDebug.CheckKind).IsEqualTo(ViewerCommandCheckKind.Check);
    }

    [Test]
    public async Task EveryRequiredMenuHasItsKeyActionsRepresented()
    {
        // Non-vacuity plus coverage: every menu the plan names for the eventual
        // menu-first shell must already have at least this many current actions
        // catalogued, so a menu cannot quietly regress to empty.
        Dictionary<ViewerCommandGroup, int> minimumCounts = new()
        {
            [ViewerCommandGroup.File] = 3,
            [ViewerCommandGroup.View] = 6,
            [ViewerCommandGroup.Render] = 8,
            [ViewerCommandGroup.Camera] = 8,
            [ViewerCommandGroup.Physics] = 18,
            [ViewerCommandGroup.Tools] = 5,
            [ViewerCommandGroup.Help] = 1,
        };

        foreach ((ViewerCommandGroup group, int minimum) in minimumCounts)
        {
            int count = ViewerCommandCatalog.ForGroup(group).Count();
            await Assert.That(count)
                .IsGreaterThanOrEqualTo(minimum)
                .Because($"{group} has only {count} catalogued commands");
        }
    }

    [Test]
    public async Task GestureBoundCommandsAgreeWithTheShortcutsCatalog()
    {
        // The command catalog and the shortcuts dialog catalog are independent,
        // deliberately: one describes menu semantics, the other describes input.
        // Where both cover the same action, they must not silently disagree.
        Dictionary<string, string> expectedGestureByShortcutAction = ViewerShortcutCatalog.All
            .Where(shortcut => shortcut.Kind == ViewerShortcutKind.Keyboard)
            .ToDictionary(shortcut => shortcut.Action, shortcut => shortcut.Gesture);

        (string CommandId, string ShortcutAction)[] crossChecked =
        [
            (ViewerCommandIds.CameraFrameSelected, "Frame selected"),
            (ViewerCommandIds.CameraResetAutomatic, "Reset camera"),
            (ViewerCommandIds.CameraToggleProjection, "Toggle projection"),
            (ViewerCommandIds.CameraOrbitLeft, "Orbit left"),
            (ViewerCommandIds.CameraOrbitRight, "Orbit right"),
            (ViewerCommandIds.CameraOrbitUp, "Orbit up"),
            (ViewerCommandIds.CameraOrbitDown, "Orbit down"),
            (ViewerCommandIds.PhysicsPlayPause, "Play or pause physics"),
            (ViewerCommandIds.PhysicsStop, "Stop physics"),
            (ViewerCommandIds.PhysicsStep, "Step one physics frame"),
            (ViewerCommandIds.PhysicsBake, "Bake physics"),
            (ViewerCommandIds.PhysicsGizmoNone, "No gizmo"),
            (ViewerCommandIds.PhysicsGizmoMove, "Move gizmo"),
            (ViewerCommandIds.PhysicsGizmoRotate, "Rotate gizmo"),
            (ViewerCommandIds.PhysicsGizmoScale, "Scale gizmo"),
            (ViewerCommandIds.PhysicsGizmoDrag, "Drag body"),
            (ViewerCommandIds.PhysicsSnap, "Toggle snapping"),
            (ViewerCommandIds.PhysicsUndo, "Undo physics edit"),
            (ViewerCommandIds.PhysicsRedo, "Redo physics edit"),
        ];

        List<string> mismatched = [];
        foreach ((string commandId, string shortcutAction) in crossChecked)
        {
            ViewerCommandDescriptor command = ViewerCommandCatalog.Get(commandId);
            string expected = expectedGestureByShortcutAction[shortcutAction];
            if (!string.Equals(command.Gesture, expected, StringComparison.Ordinal))
            {
                mismatched.Add($"{commandId}: catalog says '{command.Gesture}', shortcuts say '{expected}'");
            }
        }

        await Assert.That(mismatched).IsEmpty();
    }

    [Test]
    public async Task GetThrowsForAnUnknownId()
    {
        await Assert.That(() => ViewerCommandCatalog.Get("nonexistent.command"))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task TryGetReturnsFalseForAnUnknownId()
    {
        bool found = ViewerCommandCatalog.TryGet("nonexistent.command", out ViewerCommandDescriptor? descriptor);
        await Assert.That(found).IsFalse();
        await Assert.That(descriptor).IsNull();
    }
}
