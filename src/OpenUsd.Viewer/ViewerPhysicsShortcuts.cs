// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;

namespace OpenUsd.Viewer;

/// <summary>Identifies one interactive physics keyboard shortcut.</summary>
internal enum ViewerPhysicsShortcut
{
    /// <summary>No physics shortcut.</summary>
    None,

    /// <summary>Start or stop pacing the simulation.</summary>
    PlayPause,

    /// <summary>Return the simulation to the authored start.</summary>
    Stop,

    /// <summary>Advance exactly one fixed simulation step.</summary>
    StepOneFrame,

    /// <summary>Open the bake dialog.</summary>
    Bake,

    /// <summary>Turn every viewport gizmo off.</summary>
    GizmoNone,

    /// <summary>Manipulate the selection with the move gizmo.</summary>
    GizmoTranslate,

    /// <summary>Manipulate the selection with the rotate gizmo.</summary>
    GizmoRotate,

    /// <summary>Manipulate the selection with the scale gizmo.</summary>
    GizmoScale,

    /// <summary>Drag a simulated body through the solver.</summary>
    GizmoDrag,

    /// <summary>Turn gizmo snapping on or off.</summary>
    ToggleSnap,

    /// <summary>Undo the newest physics property edit.</summary>
    Undo,

    /// <summary>Redo the newest undone physics property edit.</summary>
    Redo,
}

/// <summary>
/// Decides whether a key press is a physics command or ordinary typing.
/// </summary>
/// <remarks>
/// <para>
/// The rule that matters is the text-input guard. A user renaming a prim or typing a time code
/// expects letters to reach the field they are typing into; a viewer that started or stopped a
/// simulation because a name contains a "k" would be unusable. So the policy refuses every binding
/// while a text field is being edited, exactly as the camera policy does.
/// </para>
/// <para>
/// Modified key presses are refused too. Every accelerator that uses a modifier belongs to a menu,
/// and silently stealing one would make that menu item stop working with no explanation.
/// </para>
/// </remarks>
internal static class ViewerPhysicsShortcutPolicy
{
    /// <summary>Classifies one key press.</summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="modifiers">The modifiers held while the key was pressed.</param>
    /// <param name="isEditing">Whether a text field currently has keyboard focus.</param>
    /// <returns>The physics command, or <see cref="ViewerPhysicsShortcut.None"/>.</returns>
    internal static ViewerPhysicsShortcut Classify(
        Key key,
        KeyModifiers modifiers,
        bool isEditing)
    {
        if (isEditing || modifiers != KeyModifiers.None)
        {
            return ViewerPhysicsShortcut.None;
        }

        return key switch
        {
            Key.K => ViewerPhysicsShortcut.PlayPause,
            Key.J => ViewerPhysicsShortcut.Stop,
            Key.N => ViewerPhysicsShortcut.StepOneFrame,
            Key.B => ViewerPhysicsShortcut.Bake,
            Key.Q => ViewerPhysicsShortcut.GizmoNone,
            Key.G => ViewerPhysicsShortcut.GizmoTranslate,
            Key.E => ViewerPhysicsShortcut.GizmoRotate,
            Key.R => ViewerPhysicsShortcut.GizmoScale,
            Key.H => ViewerPhysicsShortcut.GizmoDrag,
            Key.X => ViewerPhysicsShortcut.ToggleSnap,
            Key.Z => ViewerPhysicsShortcut.Undo,
            Key.Y => ViewerPhysicsShortcut.Redo,
            _ => ViewerPhysicsShortcut.None,
        };
    }
}

/// <summary>
/// Suppresses the operating system's key auto-repeat for the discrete physics shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// Every physics shortcut is a discrete command: it toggles playback, steps one frame, opens the
/// bake dialog, switches a gizmo, or undoes an edit. Holding the key down produces a stream of
/// repeated key-down events, and running the command once per repeat means a held <c>Z</c> unwinds
/// the whole undo history, a held <c>N</c> steps dozens of frames, and a held <c>B</c> opens a
/// dialog again and again. The guard therefore accepts the first press and refuses every repeat
/// until the key is physically released.
/// </para>
/// <para>
/// The character-controller movement keys are deliberately not guarded here. Movement is a held
/// state rather than a command, and its own key state already collapses repeats into one held
/// direction.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsShortcutRepeatGuard
{
    private ushort _pressed;

    /// <summary>Records a key press and reports whether it is the first one.</summary>
    /// <param name="key">The pressed key.</param>
    /// <returns>
    /// <see langword="true"/> for a key that is not guarded or was not already held.
    /// </returns>
    internal bool TryPress(Key key)
    {
        ushort flag = GetFlag(key);
        if (flag == 0)
        {
            return true;
        }

        if ((_pressed & flag) != 0)
        {
            return false;
        }

        _pressed |= flag;
        return true;
    }

    /// <summary>Records a key release.</summary>
    /// <param name="key">The released key.</param>
    internal void Release(Key key) => _pressed = (ushort)(_pressed & ~GetFlag(key));

    /// <summary>Drops every held key, for a focus transfer that swallows the releases.</summary>
    internal void ResetForFocusTransfer() => _pressed = 0;

    /// <summary>Drops every held key.</summary>
    internal void Reset() => ResetForFocusTransfer();

    /// <summary>Reports whether a key is one the guard suppresses repeats for.</summary>
    /// <param name="key">The key to classify.</param>
    internal static bool IsGuarded(Key key) => GetFlag(key) != 0;

    private static ushort GetFlag(Key key) =>
        key switch
        {
            Key.K => 1 << 0,
            Key.J => 1 << 1,
            Key.N => 1 << 2,
            Key.B => 1 << 3,
            Key.Q => 1 << 4,
            Key.G => 1 << 5,
            Key.E => 1 << 6,
            Key.R => 1 << 7,
            Key.H => 1 << 8,
            Key.X => 1 << 9,
            Key.Z => 1 << 10,
            Key.Y => 1 << 11,
            _ => 0,
        };
}

/// <summary>
/// Maps the held movement keys onto the directions a character controller is driven in.
/// </summary>
/// <remarks>
/// <para>
/// Movement is a held state, not a command: the controller is asked to move once per simulated step
/// for as long as the key is down, which is what makes walking feel continuous instead of stepping
/// once per key repeat. The mapping is therefore separate from the single-press shortcut policy.
/// </para>
/// <para>
/// The same text-input guard applies. A user typing a prim name must not walk a character across
/// the scene, so every key is refused while a text field has focus.
/// </para>
/// </remarks>
internal static class ViewerPhysicsControllerKeyPolicy
{
    /// <summary>Maps one key onto a movement direction.</summary>
    /// <param name="key">The key that changed state.</param>
    /// <param name="modifiers">The modifiers held while the key changed state.</param>
    /// <param name="isEditing">Whether a text field currently has keyboard focus.</param>
    /// <returns>The direction, or <see cref="ViewerPhysicsControllerDirection.None"/>.</returns>
    internal static ViewerPhysicsControllerDirection Classify(
        Key key,
        KeyModifiers modifiers,
        bool isEditing)
    {
        if (isEditing || modifiers != KeyModifiers.None)
        {
            return ViewerPhysicsControllerDirection.None;
        }

        return Map(key);
    }

    /// <summary>Maps one key onto a movement direction, ignoring every policy.</summary>
    /// <param name="key">The key that changed state.</param>
    /// <returns>The direction, or <see cref="ViewerPhysicsControllerDirection.None"/>.</returns>
    /// <remarks>
    /// A key release must clear the direction it started, whatever changed while the key was down.
    /// A release that went through <see cref="Classify"/> would be refused the moment a modifier was
    /// added or focus moved into a text field between the press and the release, and the controller
    /// would keep walking with nothing held. Releases therefore use this raw map.
    /// </remarks>
    internal static ViewerPhysicsControllerDirection Map(Key key) => key switch
    {
        Key.W => ViewerPhysicsControllerDirection.Forward,
        Key.S => ViewerPhysicsControllerDirection.Back,
        Key.A => ViewerPhysicsControllerDirection.Left,
        Key.D => ViewerPhysicsControllerDirection.Right,
        Key.Space => ViewerPhysicsControllerDirection.Up,
        Key.C => ViewerPhysicsControllerDirection.Down,
        _ => ViewerPhysicsControllerDirection.None,
    };
}
