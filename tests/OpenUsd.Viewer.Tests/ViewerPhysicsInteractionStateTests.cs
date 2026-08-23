// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the interaction state the viewer keeps between input events: the held movement keys, the
/// pointer that owns a body drag, and the inspector selection that must survive a reload.
/// </summary>
public sealed class ViewerPhysicsInteractionStateTests
{
    private const string SleepThreshold = "openUsdPhysics:body:sleepThreshold";
    private const string ContactOffset = "openUsdPhysics:collision:contactOffset";
    private const string VehicleAxis = "openUsdPhysics:vehicle:lateralAxis";

    private static readonly ViewerPhysicsVector3 Forward = new(0d, 0d, -1d);
    private static readonly ViewerPhysicsVector3 Right = new(1d, 0d, 0d);
    private static readonly ViewerPhysicsVector3 Up = new(0d, 1d, 0d);

    [Test]
    public async Task AKeyReleasedWithAModifierStillStopsTheController()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        await Assert.That(keys.TryPress(Key.W, KeyModifiers.None, false, true)).IsTrue();
        await Assert.That(keys.Held).IsEqualTo(ViewerPhysicsControllerDirection.Forward);

        // The operator pressed Shift while walking, then let go of W. The key is physically up, so
        // the direction has to stop even though the release now carries a modifier.
        await Assert.That(keys.TryRelease(Key.W)).IsTrue();

        await Assert.That(keys.Held).IsEqualTo(ViewerPhysicsControllerDirection.None);
        await Assert.That(keys.HasHeldKeys).IsFalse();
        await AssertNoControllerMoveAsync(keys);
    }

    [Test]
    public async Task AKeyReleasedWhileFocusMovedIntoATextFieldStillStopsTheController()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        keys.TryPress(Key.A, KeyModifiers.None, false, true);
        keys.TryPress(Key.Space, KeyModifiers.None, false, true);

        // Focus moved into a text box between the press and the release; the release must not be
        // filtered by the press policy or both directions latch forever.
        await Assert.That(keys.TryRelease(Key.A)).IsTrue();
        await Assert.That(keys.TryRelease(Key.Space)).IsTrue();

        await Assert.That(keys.Held).IsEqualTo(ViewerPhysicsControllerDirection.None);
        await AssertNoControllerMoveAsync(keys);
    }

    [Test]
    public async Task ASpaceReleaseConsumedByAFocusedButtonStillStopsTheController()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        keys.TryPress(Key.Space, KeyModifiers.None, false, true);
        await Assert.That(keys.Held).IsEqualTo(ViewerPhysicsControllerDirection.Up);

        // A focused Button or CheckBox marks the Space release handled. The window handler is
        // registered to see handled events precisely so this release still arrives, and once it
        // does it must clear regardless of who consumed it first.
        await Assert.That(keys.TryRelease(Key.Space)).IsTrue();

        await Assert.That(keys.HasHeldKeys).IsFalse();
        await AssertNoControllerMoveAsync(keys);
    }

    [Test]
    public async Task ARoutedReleaseSeenTwiceClearsOnceAndIsIdempotent()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        keys.TryPress(Key.D, KeyModifiers.None, false, true);

        // The handler is registered for both the tunnel and the bubble, so the same release is
        // delivered twice; the second delivery must be a harmless no-op.
        await Assert.That(keys.TryRelease(Key.D)).IsTrue();
        await Assert.That(keys.TryRelease(Key.D)).IsFalse();
        await Assert.That(keys.HasHeldKeys).IsFalse();
    }

    [Test]
    public async Task FocusTransferToTheNativeChildDropsEveryHeldKey()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        keys.TryPress(Key.W, KeyModifiers.None, false, true);
        keys.TryPress(Key.Space, KeyModifiers.None, false, true);

        // The native child owns the keyboard now, so no release for either key will ever reach the
        // window; the recovery hook drops them exactly as the deactivation path does.
        keys.Clear();

        await Assert.That(keys.HasHeldKeys).IsFalse();
        await AssertNoControllerMoveAsync(keys);
    }

    [Test]
    public async Task EveryMovementKeyReleasesRegardlessOfHowItWasHeld()
    {
        Key[] movement = [Key.W, Key.A, Key.S, Key.D, Key.Space, Key.C];

        var latched = new List<string>();
        foreach (Key key in movement)
        {
            var keys = new ViewerPhysicsControllerKeyState();
            keys.TryPress(key, KeyModifiers.None, false, true);
            keys.TryRelease(key);
            if (keys.HasHeldKeys)
            {
                latched.Add(key.ToString());
            }
        }

        await Assert.That(latched).IsEmpty();
    }

    [Test]
    public async Task ReleasingAKeyThatWasNeverHeldIsNotConsumed()
    {
        var keys = new ViewerPhysicsControllerKeyState();

        await Assert.That(keys.TryRelease(Key.W)).IsFalse();
        await Assert.That(keys.TryRelease(Key.F1)).IsFalse();
        await Assert.That(keys.HasHeldKeys).IsFalse();
    }

    [Test]
    public async Task APressIsStillRefusedWhileTypingWithAModifierOrWithDrivingOff()
    {
        var keys = new ViewerPhysicsControllerKeyState();

        await Assert.That(keys.TryPress(Key.W, KeyModifiers.None, true, true)).IsFalse();
        await Assert.That(keys.TryPress(Key.W, KeyModifiers.Control, false, true)).IsFalse();
        await Assert.That(keys.TryPress(Key.W, KeyModifiers.None, false, false)).IsFalse();
        await Assert.That(keys.HasHeldKeys).IsFalse();
        await AssertNoControllerMoveAsync(keys);
    }

    [Test]
    public async Task TurningDrivingOffDropsEveryHeldKey()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        keys.TryPress(Key.W, KeyModifiers.None, false, true);
        keys.TryPress(Key.D, KeyModifiers.None, false, true);
        await Assert.That(keys.HasHeldKeys).IsTrue();

        // The operator unchecked "Drive with WASD", or the control was disabled by a capability
        // change. Either way, re-enabling it must not resume a gesture that already ended.
        keys.Clear();

        await Assert.That(keys.Held).IsEqualTo(ViewerPhysicsControllerDirection.None);
        await AssertNoControllerMoveAsync(keys);
        await Assert.That(keys.TryPress(Key.W, KeyModifiers.None, false, false)).IsFalse();
        await Assert.That(keys.HasHeldKeys).IsFalse();
    }

    [Test]
    public async Task ARefusedDriveBatchDropsEveryHeldKeySoNoFurtherMoveIsBuilt()
    {
        var keys = new ViewerPhysicsControllerKeyState();
        keys.TryPress(Key.S, KeyModifiers.None, false, true);

        // This is what the pump does when the world refuses the batch: the controls are switched
        // off and whatever they were holding is dropped, so the next pump submits nothing.
        keys.Clear();

        await Assert.That(keys.HasHeldKeys).IsFalse();
        await AssertNoControllerMoveAsync(keys);
    }

    [Test]
    public async Task AReleasedDragProducesExactlyOneClearAndThenIgnoresEverything()
    {
        var session = new ViewerPhysicsDragSession();
        await Assert.That(session.TryBegin(3, 9UL, ViewerPhysicsVector3.Zero, 5d)).IsTrue();
        await Assert.That(session.Owns(3)).IsTrue();

        // The pointer was released outside the viewport, which only reaches the viewer because the
        // drag captured it.
        await Assert.That(session.TryEnd(
            ViewerPhysicsDragEnd.Released, out ViewerPhysicsRuntimeCommand clear)).IsTrue();
        await Assert.That(clear.Kind).IsEqualTo(ViewerPhysicsRuntimeCommandKind.ClearForce);
        await Assert.That(clear.TargetId).IsEqualTo(9UL);
        await Assert.That(session.Clears).IsEqualTo(1);

        // A capture-lost that arrives for the same gesture must not clear a second time.
        await Assert.That(session.TryEnd(ViewerPhysicsDragEnd.CaptureLost, out _)).IsFalse();
        await Assert.That(session.Clears).IsEqualTo(1);
        await Assert.That(session.IsActive).IsFalse();
        await Assert.That(session.Owns(3)).IsFalse();
        await Assert.That(session.PointerId).IsEqualTo(ViewerPhysicsDragSession.NoPointer);
    }

    [Test]
    public async Task ALostCaptureEndsTheDragExactlyOnceAndALaterReleaseDoesNothing()
    {
        var session = new ViewerPhysicsDragSession();
        session.TryBegin(4, 11UL, ViewerPhysicsVector3.Zero, 5d);

        await Assert.That(session.TryEnd(
            ViewerPhysicsDragEnd.CaptureLost, out ViewerPhysicsRuntimeCommand clear)).IsTrue();
        await Assert.That(clear.TargetId).IsEqualTo(11UL);
        await Assert.That(session.TryEnd(ViewerPhysicsDragEnd.Released, out _)).IsFalse();
        await Assert.That(session.Clears).IsEqualTo(1);
    }

    [Test]
    public async Task DeactivationEndsTheDragOnceAndNoFurtherForceIsProduced()
    {
        var session = new ViewerPhysicsDragSession();
        session.TryBegin(5, 13UL, ViewerPhysicsVector3.Zero, 5d);

        await Assert.That(session.TryEnd(ViewerPhysicsDragEnd.Deactivated, out _)).IsTrue();
        await Assert.That(session.Clears).IsEqualTo(1);

        // Whatever pointer traffic arrives after the window came back must not push the body.
        ViewerPhysicsDragStep step = session.Step(
            5,
            isLeftButtonDown: true,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerGizmoRay(ViewerPhysicsVector3.Zero, Forward),
            1d / 60d,
            out _);
        await Assert.That(step).IsEqualTo(ViewerPhysicsDragStep.Ignored);
        await Assert.That(session.TryEnd(ViewerPhysicsDragEnd.Released, out _)).IsFalse();
    }

    [Test]
    public async Task AMoveWithTheButtonUpEndsTheDragRatherThanPushingTheBody()
    {
        var session = new ViewerPhysicsDragSession();
        session.TryBegin(6, 17UL, ViewerPhysicsVector3.Zero, 5d);

        ViewerPhysicsDragStep step = session.Step(
            6,
            isLeftButtonDown: false,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerGizmoRay(ViewerPhysicsVector3.Zero, Forward),
            1d / 60d,
            out ViewerPhysicsRuntimeCommand command);

        await Assert.That(step).IsEqualTo(ViewerPhysicsDragStep.MustEnd);
        await Assert.That(command.Vector.Length).IsEqualTo(0d);

        await Assert.That(session.TryEnd(ViewerPhysicsDragEnd.Released, out _)).IsTrue();
        await Assert.That(session.Clears).IsEqualTo(1);

        // The drag is over, so nothing further is consumed and no move is marked handled - which
        // is what stops hover being suppressed across the whole viewport.
        await Assert.That(session.Step(
            6,
            isLeftButtonDown: false,
            ViewerPhysicsVector3.Zero,
            new ViewerGizmoRay(ViewerPhysicsVector3.Zero, Forward),
            1d / 60d,
            out _)).IsEqualTo(ViewerPhysicsDragStep.Ignored);
    }

    [Test]
    public async Task AMoveFromAnotherPointerIsNeverConsumed()
    {
        var session = new ViewerPhysicsDragSession();
        session.TryBegin(7, 19UL, ViewerPhysicsVector3.Zero, 5d);

        await Assert.That(session.Step(
            8,
            isLeftButtonDown: true,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerGizmoRay(ViewerPhysicsVector3.Zero, Forward),
            1d / 60d,
            out _)).IsEqualTo(ViewerPhysicsDragStep.Ignored);
        await Assert.That(session.Owns(7)).IsTrue();
    }

    [Test]
    public async Task ASecondBeginIsRefusedWhileADragIsAlreadyActive()
    {
        var session = new ViewerPhysicsDragSession();
        session.TryBegin(1, 21UL, ViewerPhysicsVector3.Zero, 5d);

        await Assert.That(session.TryBegin(2, 23UL, ViewerPhysicsVector3.Zero, 5d)).IsFalse();
        await Assert.That(session.TargetId).IsEqualTo(21UL);
        await Assert.That(session.Owns(1)).IsTrue();
    }

    [Test]
    public async Task EndingADragThatNeverStartedProducesNoClear()
    {
        var session = new ViewerPhysicsDragSession();

        await Assert.That(session.TryEnd(ViewerPhysicsDragEnd.Released, out _)).IsFalse();
        await Assert.That(session.Clears).IsEqualTo(0);
    }

    [Test]
    public async Task AHeldDragStillProducesAForceWhileTheButtonIsDown()
    {
        var session = new ViewerPhysicsDragSession();
        session.TryBegin(2, 29UL, ViewerPhysicsVector3.Zero, 10d);

        ViewerPhysicsDragStep step = session.Step(
            2,
            isLeftButtonDown: true,
            new ViewerPhysicsVector3(0d, 0d, -9d),
            new ViewerGizmoRay(ViewerPhysicsVector3.Zero, Forward),
            1d / 60d,
            out ViewerPhysicsRuntimeCommand command);

        await Assert.That(step).IsEqualTo(ViewerPhysicsDragStep.Applied);
        await Assert.That(command.Kind).IsEqualTo(ViewerPhysicsRuntimeCommandKind.Force);
        await Assert.That(command.TargetId).IsEqualTo(29UL);
    }

    [Test]
    public async Task TheSelectionAnchorFindsTheSameObjectAfterTheListIsRebuilt()
    {
        List<ViewerPhysicsObjectSection> before = Bodies("/A", "/B", "/C");
        ViewerPhysicsSelectionAnchor anchor =
            ViewerPhysicsSelectionAnchor.For(before[2], SleepThreshold);

        // The edit changed the extraction fingerprint, so the list was rebuilt - here with an extra
        // object in front, which is exactly what would push a positional restore off.
        List<ViewerPhysicsObjectSection> after = Bodies("/New", "/A", "/B", "/C");
        int section = ViewerPhysicsSelectionResolver.ResolveSection(after, anchor);

        await Assert.That(after[section].PrimPath).IsEqualTo("/C");
        await Assert.That(ViewerPhysicsSelectionResolver.ResolveRow(after, anchor))
            .IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ThePropertyOfOneOfSeveralSectionsOnAPathIsRestoredToThatSection()
    {
        // One prim, three extracted objects - which is the ordinary shape of a driveable vehicle.
        List<ViewerPhysicsObjectSection> sections = MultiKind("/Car");
        ViewerPhysicsSelectionAnchor collider =
            ViewerPhysicsSelectionAnchor.For(sections[1], ContactOffset);
        ViewerPhysicsSelectionAnchor vehicle =
            ViewerPhysicsSelectionAnchor.For(sections[2], VehicleAxis);

        List<ViewerPhysicsObjectSection> reloaded = MultiKind("/Car");

        int colliderSection = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, collider);
        int colliderRow = ViewerPhysicsSelectionResolver.ResolveRow(reloaded, collider);
        await Assert.That(reloaded[colliderSection].Kind).IsEqualTo("Collider");
        await Assert.That(reloaded[colliderSection].ObjectId).IsEqualTo(collider.ObjectId);
        await Assert.That(reloaded[colliderSection].Rows[colliderRow].Name)
            .IsEqualTo(ContactOffset);

        int vehicleSection = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, vehicle);
        int vehicleRow = ViewerPhysicsSelectionResolver.ResolveRow(reloaded, vehicle);
        await Assert.That(reloaded[vehicleSection].Kind).IsEqualTo("Vehicle");
        await Assert.That(reloaded[vehicleSection].Rows[vehicleRow].Name).IsEqualTo(VehicleAxis);
    }

    [Test]
    public async Task ADuplicatePathSelectionSurvivesApplyClearUndoAndRedo()
    {
        List<ViewerPhysicsObjectSection> initial = MultiKind("/Car");
        ViewerPhysicsSelectionAnchor anchor =
            ViewerPhysicsSelectionAnchor.For(initial[1], ContactOffset);

        // Apply, clear, undo, and redo each re-extract with different content; none of them may
        // move the selection off the collider section onto the rigid body that shares its path.
        var drifted = new List<string>();
        foreach (string value in new[] { "0.02", "(no value)", "0.01", "0.03" })
        {
            List<ViewerPhysicsObjectSection> reloaded = MultiKind("/Car", value);
            int section = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, anchor);
            int row = ViewerPhysicsSelectionResolver.ResolveRow(reloaded, anchor);
            if (section < 0 ||
                row < 0 ||
                reloaded[section].Kind != "Collider" ||
                reloaded[section].ObjectId != anchor.ObjectId ||
                reloaded[section].Rows[row].Name != ContactOffset ||
                reloaded[section].Rows[row].ValueText != value)
            {
                drifted.Add(value);
            }
        }

        await Assert.That(drifted).IsEmpty();
    }

    [Test]
    public async Task ADuplicatePathVehicleSelectionSurvivesApplyClearUndoAndRedo()
    {
        List<ViewerPhysicsObjectSection> initial = MultiKind("/Car");
        ViewerPhysicsSelectionAnchor anchor =
            ViewerPhysicsSelectionAnchor.For(initial[2], VehicleAxis);

        var drifted = new List<string>();
        foreach (string value in new[] { "X", "(no value)", "Y", "Z" })
        {
            List<ViewerPhysicsObjectSection> reloaded = MultiKind("/Car", "0.02", value);
            int section = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, anchor);
            int row = ViewerPhysicsSelectionResolver.ResolveRow(reloaded, anchor);
            if (section < 0 ||
                row < 0 ||
                reloaded[section].Kind != "Vehicle" ||
                reloaded[section].Rows[row].ValueText != value)
            {
                drifted.Add(value);
            }
        }

        await Assert.That(drifted).IsEmpty();
    }

    [Test]
    public async Task EveryInteractionTargetsTheIdentityOfTheAnchoredSectionNotThePathsFirst()
    {
        List<ViewerPhysicsObjectSection> reloaded = MultiKind("/Car");
        ViewerPhysicsSelectionAnchor anchor =
            ViewerPhysicsSelectionAnchor.For(reloaded[2], VehicleAxis);

        int section = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, anchor);
        ulong target = reloaded[section].ObjectId;

        // The rigid body section for the same path comes first; targeting it would be the bug.
        await Assert.That(reloaded[0].ObjectId).IsNotEqualTo(target);
        await Assert.That(target).IsEqualTo(VehicleId);

        await Assert.That(ViewerPhysicsImpulseBuilder.TryBuild(
            ViewerPhysicsRuntimeCommandKind.Impulse,
            target,
            Up,
            5d,
            ViewerPhysicsForceMode.Default,
            out ViewerPhysicsRuntimeCommand impulse,
            out _)).IsTrue();
        await Assert.That(impulse.TargetId).IsEqualTo(VehicleId);

        await Assert.That(ViewerPhysicsControllerInput.TryBuild(
            target,
            ViewerPhysicsControllerDirection.Forward,
            Forward,
            Right,
            Up,
            2d,
            1d / 60d,
            out ViewerPhysicsRuntimeCommand move)).IsTrue();
        await Assert.That(move.TargetId).IsEqualTo(VehicleId);

        await Assert.That(new ViewerPhysicsVehicleInput(1d, 0d, 0d, 0d, 0d, 3)
            .ToCommand(target).TargetId).IsEqualTo(VehicleId);

        var session = new ViewerPhysicsDragSession();
        session.TryBegin(1, target, ViewerPhysicsVector3.Zero, 5d);
        await Assert.That(session.TargetId).IsEqualTo(VehicleId);
    }

    [Test]
    public async Task APropertyIsNeverBorrowedFromAnotherSectionOfTheSamePath()
    {
        List<ViewerPhysicsObjectSection> sections = MultiKind("/Car");
        ViewerPhysicsSelectionAnchor collider =
            ViewerPhysicsSelectionAnchor.For(sections[1], ContactOffset);

        // The collider was removed but the rigid body on the same path survived. Falling back to
        // that body is right; selecting its unrelated rows as if they were the anchor's is not.
        List<ViewerPhysicsObjectSection> reloaded = [sections[0], sections[2]];
        int section = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, collider);

        await Assert.That(reloaded[section].Kind).IsNotEqualTo("Collider");
        await Assert.That(ViewerPhysicsSelectionResolver.ResolveRow(reloaded, collider))
            .IsEqualTo(-1);
    }

    [Test]
    public async Task AKindOnlyAnchorStillPicksTheRightSectionWhenNoIdentityIsCarried()
    {
        List<ViewerPhysicsObjectSection> reloaded = MultiKind("/Car");
        var anchor = new ViewerPhysicsSelectionAnchor(0UL, "/Car", "Vehicle", VehicleAxis);

        int section = ViewerPhysicsSelectionResolver.ResolveSection(reloaded, anchor);

        await Assert.That(reloaded[section].Kind).IsEqualTo("Vehicle");
        await Assert.That(ViewerPhysicsSelectionResolver.ResolveRow(reloaded, anchor))
            .IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ARemovedObjectIsTheOnlyCaseThatFallsBackToTheFirstObject()
    {
        List<ViewerPhysicsObjectSection> reloaded = Bodies("/A", "/B");
        var anchor = new ViewerPhysicsSelectionAnchor(
            999UL, "/Gone", "RigidBody", SleepThreshold);

        await Assert.That(ViewerPhysicsSelectionResolver.ResolveSection(reloaded, anchor))
            .IsEqualTo(0);
        await Assert.That(ViewerPhysicsSelectionResolver.ResolveRow(reloaded, anchor))
            .IsEqualTo(-1);
    }

    [Test]
    public async Task AnEmptyExtractionSelectsNothingRatherThanTheFirstObject()
    {
        var anchor = new ViewerPhysicsSelectionAnchor(1UL, "/A", "RigidBody", SleepThreshold);

        await Assert.That(ViewerPhysicsSelectionResolver.ResolveSection([], anchor)).IsEqualTo(-1);
        await Assert.That(ViewerPhysicsSelectionResolver.ResolveRow([], anchor)).IsEqualTo(-1);
        await Assert.That(ViewerPhysicsSelectionAnchor.None.HasPrim).IsFalse();
        await Assert.That(ViewerPhysicsSelectionAnchor.None.HasProperty).IsFalse();
    }

    [Test]
    public async Task AnAnchorWithoutAPropertyResolvesTheObjectButNoRow()
    {
        List<ViewerPhysicsObjectSection> reloaded = Bodies("/A", "/B");
        ViewerPhysicsSelectionAnchor anchor =
            ViewerPhysicsSelectionAnchor.For(reloaded[1], string.Empty);

        await Assert.That(ViewerPhysicsSelectionResolver.ResolveSection(reloaded, anchor))
            .IsEqualTo(1);
        await Assert.That(ViewerPhysicsSelectionResolver.ResolveRow(reloaded, anchor))
            .IsEqualTo(-1);
    }

    private const ulong BodyId = 501UL;
    private const ulong ColliderId = 502UL;
    private const ulong VehicleId = 503UL;

    private static async Task AssertNoControllerMoveAsync(ViewerPhysicsControllerKeyState keys) =>
        await Assert.That(ViewerPhysicsControllerInput.TryBuild(
            5UL,
            keys.Held,
            Forward,
            Right,
            Up,
            2d,
            1d / 60d,
            out _)).IsFalse();

    /// <summary>Builds one rigid-body section per path, each with a distinct identity.</summary>
    private static List<ViewerPhysicsObjectSection> Bodies(params string[] paths)
    {
        var sections = new List<ViewerPhysicsObjectSection>(paths.Length);
        for (int index = 0; index < paths.Length; index++)
        {
            sections.Add(Section(
                (ulong)(100 + Math.Abs(string.GetHashCode(paths[index], StringComparison.Ordinal) % 1000)),
                paths[index],
                "RigidBody",
                Row(paths[index], SleepThreshold, "Sleep Threshold", "0.5")));
        }

        return sections;
    }

    /// <summary>
    /// Builds the three sections one prim commonly produces: a body, its collider, and a vehicle.
    /// </summary>
    private static List<ViewerPhysicsObjectSection> MultiKind(
        string path,
        string colliderValue = "0.02",
        string vehicleValue = "X") =>
    [
        Section(BodyId, path, "RigidBody", Row(path, SleepThreshold, "Sleep Threshold", "0.5")),
        Section(ColliderId, path, "Collider", Row(path, ContactOffset, "Contact Offset", colliderValue)),
        Section(VehicleId, path, "Vehicle", Row(path, VehicleAxis, "Lateral Axis", vehicleValue)),
    ];

    private static ViewerPhysicsObjectSection Section(
        ulong objectId,
        string path,
        string kind,
        ViewerPhysicsPropertyRow row) =>
        new(
            objectId,
            path,
            kind,
            $"{kind} at {path} is simulated.",
            [],
            [
                row,
                Row(path, "physics:mass", "Mass", "4", ViewerPhysicsAuthorability.UnsupportedType),
            ]);

    private static ViewerPhysicsPropertyRow Row(
        string path,
        string name,
        string label,
        string value,
        ViewerPhysicsAuthorability authorability = ViewerPhysicsAuthorability.Editable) =>
        new(
            path,
            name,
            label,
            "Doc.",
            ViewerPhysicsValueKind.Number,
            [],
            value,
            "Project",
            authorability,
            "Editable.");
}
