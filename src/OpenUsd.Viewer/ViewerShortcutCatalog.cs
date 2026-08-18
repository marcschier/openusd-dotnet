// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;

namespace OpenUsd.Viewer;

/// <summary>Identifies how an input binding is triggered.</summary>
internal enum ViewerShortcutKind
{
    /// <summary>A key press with no pointer involvement.</summary>
    Keyboard,

    /// <summary>A pointer drag, optionally with modifier keys held.</summary>
    PointerDrag,

    /// <summary>A mouse wheel rotation.</summary>
    Wheel,
}

/// <summary>Describes one input binding for the shortcuts dialog.</summary>
/// <param name="Kind">How the binding is triggered.</param>
/// <param name="Gesture">The gesture as the user performs it, for example "Alt + Left drag".</param>
/// <param name="Action">The short name of what it does.</param>
/// <param name="Detail">A sentence explaining the effect, or an empty string.</param>
internal sealed record ViewerShortcut(
    ViewerShortcutKind Kind,
    string Gesture,
    string Action,
    string Detail);

/// <summary>
/// The single list of input bindings the Viewer offers, used by the
/// keyboard shortcuts dialog.
/// </summary>
/// <remarks>
/// This is deliberately data rather than markup, so that
/// <c>ViewerShortcutCatalogTests</c> can drive each entry through the input
/// classifiers that actually interpret input at runtime --
/// <see cref="ViewerCameraShortcutPolicy"/> and
/// <see cref="ViewerCameraGestureClassifier"/> -- and fail when the two
/// disagree.
///
/// A help dialog is worse than no help dialog once it is wrong, and a
/// hand-maintained list in XAML would drift silently the first time a
/// binding changed. Deriving the expectations from the classifiers means the
/// dialog cannot quietly start lying.
/// </remarks>
internal static class ViewerShortcutCatalog
{
    /// <summary>Camera bindings, in the order the dialog presents them.</summary>
    internal static IReadOnlyList<ViewerShortcut> Camera { get; } =
    [
        new(
            ViewerShortcutKind.PointerDrag,
            "Alt + Left drag",
            "Orbit",
            "Rotates the camera around the focus point. Drag left or right to " +
            "swing around it, up or down to raise or lower the eye."),
        new(
            ViewerShortcutKind.PointerDrag,
            "Alt + Middle drag",
            "Pan",
            "Slides the camera and its focus point together, parallel to the " +
            "screen. Drag in the direction you want the scene to move."),
        new(
            ViewerShortcutKind.PointerDrag,
            "Alt + Right drag",
            "Dolly (zoom in and out)",
            "Moves the camera toward or away from the focus point. Drag up to " +
            "move closer, down to pull back."),
        new(
            ViewerShortcutKind.Wheel,
            "Mouse wheel",
            "Dolly (zoom in and out)",
            "Same as an Alt + Right drag, without holding Alt. The pointer " +
            "must be over the viewport."),
        new(
            ViewerShortcutKind.Keyboard,
            "F",
            "Frame selected",
            "Moves the camera so the current selection fills the viewport."),
        new(
            ViewerShortcutKind.Keyboard,
            "Home",
            "Reset camera",
            "Returns to the automatic framing chosen when the stage opened."),
        new(
            ViewerShortcutKind.Keyboard,
            "P",
            "Toggle projection",
            "Switches between perspective and orthographic projection."),
        new(
            ViewerShortcutKind.Keyboard,
            "Left Arrow",
            "Orbit left",
            "Rotates 5 degrees around the focus point while the viewport has focus."),
        new(
            ViewerShortcutKind.Keyboard,
            "Right Arrow",
            "Orbit right",
            "Rotates 5 degrees around the focus point while the viewport has focus."),
        new(
            ViewerShortcutKind.Keyboard,
            "Up Arrow",
            "Orbit up",
            "Raises the camera 5 degrees while the viewport has focus."),
        new(
            ViewerShortcutKind.Keyboard,
            "Down Arrow",
            "Orbit down",
            "Lowers the camera 5 degrees while the viewport has focus."),
    ];

    /// <summary>Every binding the dialog lists.</summary>
    internal static IReadOnlyList<ViewerShortcut> All => Camera;

    /// <summary>
    /// Returns the key a keyboard entry is bound to, so tests can drive it
    /// through the policy that interprets key presses at runtime.
    /// </summary>
    internal static Key? TryResolveKey(ViewerShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (shortcut.Kind != ViewerShortcutKind.Keyboard)
        {
            return null;
        }

        return shortcut.Gesture switch
        {
            "F" => Key.F,
            "Home" => Key.Home,
            "P" => Key.P,
            "Left Arrow" => Key.Left,
            "Right Arrow" => Key.Right,
            "Up Arrow" => Key.Up,
            "Down Arrow" => Key.Down,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the pointer button a drag entry is bound to, so tests can drive
    /// it through the classifier that interprets pointer input at runtime.
    /// </summary>
    internal static ViewerPointerButtons? TryResolveButton(ViewerShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (shortcut.Kind != ViewerShortcutKind.PointerDrag)
        {
            return null;
        }

        return shortcut.Gesture switch
        {
            "Alt + Left drag" => ViewerPointerButtons.Left,
            "Alt + Middle drag" => ViewerPointerButtons.Middle,
            "Alt + Right drag" => ViewerPointerButtons.Right,
            _ => null,
        };
    }
}
