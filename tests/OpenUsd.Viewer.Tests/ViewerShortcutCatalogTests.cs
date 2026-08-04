// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Requires every binding the shortcuts dialog advertises to be one the
/// Viewer actually honours.
/// </summary>
/// <remarks>
/// The catalog is documentation shown to a user, and documentation that
/// drifts is worse than none: a help dialog listing a key that does nothing
/// costs more trust than an absent dialog. So rather than restating the
/// bindings, these tests drive every entry through the classifiers that
/// interpret input at runtime and require them to agree.
///
/// That direction matters. Asserting the catalog contains "F" would pass even
/// if `F` were unbound tomorrow. Asserting that
/// <see cref="ViewerCameraShortcutPolicy"/> maps the catalog's own key to a
/// real action cannot.
/// </remarks>
public sealed class ViewerShortcutCatalogTests
{
    [Test]
    public async Task EveryKeyboardShortcutResolvesToARealCameraAction()
    {
        List<string> unbound = [];
        int checkedCount = 0;

        foreach (ViewerShortcut shortcut in ViewerShortcutCatalog.All)
        {
            if (shortcut.Kind != ViewerShortcutKind.Keyboard)
            {
                continue;
            }

            Key? key = ViewerShortcutCatalog.TryResolveKey(shortcut);
            if (key is null)
            {
                unbound.Add($"{shortcut.Gesture} (unrecognised key)");
                continue;
            }

            checkedCount++;
            ViewerCameraShortcut action = ViewerCameraShortcutPolicy.Classify(
                key.Value,
                KeyModifiers.None,
                isEditing: false);

            if (action == ViewerCameraShortcut.None)
            {
                unbound.Add($"{shortcut.Gesture} -> {shortcut.Action}");
            }
        }

        // Non-vacuity: an empty catalog would make the offender list trivially
        // empty and this test would pass while checking nothing.
        await Assert.That(checkedCount).IsGreaterThanOrEqualTo(3);
        await Assert.That(unbound)
            .IsEmpty()
            .Because(
                "the shortcuts dialog advertises these keys, but the camera " +
                "shortcut policy does not act on them: " +
                string.Join(", ", unbound));
    }

    [Test]
    public async Task EveryPointerDragResolvesToTheGestureItAdvertises()
    {
        Dictionary<string, ViewerCameraPointerGesture> expected = new(StringComparer.Ordinal)
        {
            ["Orbit"] = ViewerCameraPointerGesture.Orbit,
            ["Pan"] = ViewerCameraPointerGesture.Pan,
            ["Dolly (zoom in and out)"] = ViewerCameraPointerGesture.Dolly,
        };

        List<string> mismatched = [];
        int checkedCount = 0;

        foreach (ViewerShortcut shortcut in ViewerShortcutCatalog.All)
        {
            if (shortcut.Kind != ViewerShortcutKind.PointerDrag)
            {
                continue;
            }

            ViewerPointerButtons? button = ViewerShortcutCatalog.TryResolveButton(shortcut);
            if (button is null || !expected.TryGetValue(shortcut.Action, out ViewerCameraPointerGesture want))
            {
                mismatched.Add($"{shortcut.Gesture} (unrecognised)");
                continue;
            }

            checkedCount++;
            ViewerCameraPointerGesture actual = ViewerCameraGestureClassifier.Classify(
                KeyModifiers.Alt,
                button.Value);

            if (actual != want)
            {
                mismatched.Add($"{shortcut.Gesture} advertises {want} but classifies as {actual}");
            }
        }

        await Assert.That(checkedCount).IsGreaterThanOrEqualTo(3);
        await Assert.That(mismatched)
            .IsEmpty()
            .Because(
                "the shortcuts dialog advertises these drags, but the gesture " +
                "classifier disagrees: " + string.Join("; ", mismatched));
    }

    [Test]
    public async Task PointerDragsAdvertiseAltBecauseTheClassifierRequiresIt()
    {
        // The classifier returns None for any modifier other than Alt, so a
        // dialog omitting "Alt" would send a reader to drag with no modifier
        // and conclude the viewport is broken.
        foreach (ViewerShortcut shortcut in ViewerShortcutCatalog.All)
        {
            if (shortcut.Kind != ViewerShortcutKind.PointerDrag)
            {
                continue;
            }

            ViewerPointerButtons? button = ViewerShortcutCatalog.TryResolveButton(shortcut);
            await Assert.That(button).IsNotNull();

            await Assert.That(shortcut.Gesture)
                .Contains("Alt")
                .Because("the gesture classifier only acts when Alt is held");

            await Assert.That(
                    ViewerCameraGestureClassifier.Classify(KeyModifiers.None, button!.Value))
                .IsEqualTo(ViewerCameraPointerGesture.None)
                .Because("dragging without Alt must not move the camera");
        }
    }

    [Test]
    public async Task EveryEntryIsPresentableToAUser()
    {
        foreach (ViewerShortcut shortcut in ViewerShortcutCatalog.All)
        {
            await Assert.That(shortcut.Gesture.Trim()).IsNotEmpty();
            await Assert.That(shortcut.Action.Trim()).IsNotEmpty();

            // The detail line is what makes the dialog useful rather than a
            // restatement of the key name, so it is required, not optional.
            await Assert.That(shortcut.Detail.Trim())
                .IsNotEmpty()
                .Because($"{shortcut.Gesture} needs an explanation of its effect");
        }

        // Gestures must be unique, or the dialog lists the same row twice.
        string[] gestures = [.. ViewerShortcutCatalog.All.Select(entry => entry.Gesture)];
        await Assert.That(gestures.Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(gestures.Length);
    }
}
