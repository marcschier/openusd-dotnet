// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;

namespace OpenUsd.Viewer;

/// <summary>
/// Tracks which character-controller movement keys are currently held.
/// </summary>
/// <remarks>
/// <para>
/// <b>A press is a policy decision; a release is not.</b> Whether a key press starts walking depends
/// on whether a text field has focus, whether a modifier is held, and whether the operator asked for
/// controller driving at all. None of that is true of the release: the key is physically up, so the
/// direction must stop regardless of what changed while it was down. Applying the press policy to
/// the release is exactly how a controller latches forever - hold W, press Shift, release W, and the
/// release is refused because it now carries a modifier, so the viewer keeps walking with no key
/// down at all. Focus moving into a text box between press and release does the same thing.
/// </para>
/// <para>
/// The same reasoning covers the drive toggle: turning driving off, or the built world refusing the
/// input, has to clear the held set. Leaving it populated would resume walking the moment driving
/// was switched back on, from keys the operator released long ago.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsControllerKeyState
{
    /// <summary>Gets the directions currently held.</summary>
    internal ViewerPhysicsControllerDirection Held { get; private set; }

    /// <summary>Gets a value indicating whether any direction is held.</summary>
    internal bool HasHeldKeys => Held != ViewerPhysicsControllerDirection.None;

    /// <summary>Records one key press, subject to the press policy.</summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="modifiers">The modifiers held while the key was pressed.</param>
    /// <param name="isEditing">Whether a text field currently has keyboard focus.</param>
    /// <param name="driveEnabled">Whether the operator asked for controller driving.</param>
    /// <returns><see langword="true"/> when the press was consumed as movement.</returns>
    internal bool TryPress(
        Key key,
        KeyModifiers modifiers,
        bool isEditing,
        bool driveEnabled)
    {
        if (!driveEnabled)
        {
            return false;
        }

        ViewerPhysicsControllerDirection direction =
            ViewerPhysicsControllerKeyPolicy.Classify(key, modifiers, isEditing);
        if (direction == ViewerPhysicsControllerDirection.None)
        {
            return false;
        }

        Held |= direction;
        return true;
    }

    /// <summary>Records one key release, unconditionally.</summary>
    /// <param name="key">The released key.</param>
    /// <returns><see langword="true"/> when the key was one this state was holding.</returns>
    /// <remarks>
    /// The release ignores modifiers, text focus, and the drive toggle on purpose: the key is up,
    /// so whatever it was contributing must stop. It reports whether it actually cleared something
    /// so the caller only marks the event handled when the release belonged to a live gesture.
    /// </remarks>
    internal bool TryRelease(Key key)
    {
        ViewerPhysicsControllerDirection direction = ViewerPhysicsControllerKeyPolicy.Map(key);
        if (direction == ViewerPhysicsControllerDirection.None)
        {
            return false;
        }

        bool wasHeld = (Held & direction) != 0;
        Held &= ~direction;
        return wasHeld;
    }

    /// <summary>Releases every held direction.</summary>
    internal void Clear() => Held = ViewerPhysicsControllerDirection.None;
}

/// <summary>Identifies why an interactive body drag ended.</summary>
internal enum ViewerPhysicsDragEnd
{
    /// <summary>No drag was active.</summary>
    None,

    /// <summary>The pointer button was released.</summary>
    Released,

    /// <summary>The pointer capture was taken away.</summary>
    CaptureLost,

    /// <summary>The window lost focus, so no further pointer event will arrive.</summary>
    Deactivated,

    /// <summary>The drag failed, or the document it belonged to went away.</summary>
    Abandoned,
}

/// <summary>
/// Owns one interactive body drag: the pointer that started it and the spring that drives it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ending is idempotent and always produces exactly one clear.</b> A drag can end four ways - the
/// button comes up, the capture is taken away, the window is deactivated, or the step fails - and
/// two of those can arrive for the same gesture. The runtime applies whatever force is staged before
/// the next sub-steps, so a drag that simply stopped submitting would push the body once more; a
/// drag that cleared twice would cost a second refused command. The session therefore hands out the
/// clear exactly once per begun drag and refuses every later attempt.
/// </para>
/// <para>
/// <b>The session also refuses to keep dragging without a button.</b> A move that arrives with the
/// left button up means the release was delivered somewhere the viewer never saw - a capture stolen
/// by another control, a release outside the window - so the session ends rather than continuing to
/// push the body and continuing to mark pointer moves handled, which would suppress hover everywhere
/// the pointer went afterwards.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsDragSession
{
    /// <summary>The pointer identifier used when no drag is active.</summary>
    internal const int NoPointer = -1;

    private readonly ViewerPhysicsDragModel _model;

    /// <summary>Initializes a drag session.</summary>
    /// <param name="gains">The spring gains, or the defaults.</param>
    internal ViewerPhysicsDragSession(ViewerPhysicsDragGains gains = default) =>
        _model = new ViewerPhysicsDragModel(gains);

    /// <summary>Gets the pointer that owns the drag, or <see cref="NoPointer"/>.</summary>
    internal int PointerId { get; private set; } = NoPointer;

    /// <summary>Gets a value indicating whether a drag is active.</summary>
    internal bool IsActive => _model.IsActive;

    /// <summary>Gets the identity being dragged, or zero.</summary>
    internal ulong TargetId => _model.TargetId;

    /// <summary>Gets the number of clears this session has produced.</summary>
    internal int Clears { get; private set; }

    /// <summary>Begins a drag owned by one pointer.</summary>
    /// <param name="pointerId">The pointer that started the drag.</param>
    /// <param name="targetId">The stable simulation identity being grabbed.</param>
    /// <param name="localPoint">The grabbed point in the body's local space.</param>
    /// <param name="grabDistance">The parametric depth along the pointer ray.</param>
    /// <returns><see langword="true"/> when the drag started.</returns>
    internal bool TryBegin(
        int pointerId,
        ulong targetId,
        ViewerPhysicsVector3 localPoint,
        double grabDistance)
    {
        if (IsActive || !_model.Begin(targetId, localPoint, grabDistance))
        {
            return false;
        }

        PointerId = pointerId;
        return true;
    }

    /// <summary>Reports whether one pointer owns the active drag.</summary>
    /// <param name="pointerId">The pointer to test.</param>
    /// <returns><see langword="true"/> when the pointer owns an active drag.</returns>
    internal bool Owns(int pointerId) => IsActive && PointerId == pointerId;

    /// <summary>Advances the drag for one pointer move.</summary>
    /// <param name="pointerId">The pointer that moved.</param>
    /// <param name="isLeftButtonDown">Whether the left button is still down.</param>
    /// <param name="grabWorldPoint">Where the grabbed point is now, in stage space.</param>
    /// <param name="pointerRay">The ray under the pointer now.</param>
    /// <param name="deltaSeconds">The simulated time the step covers.</param>
    /// <param name="command">Receives the force command, when one was produced.</param>
    /// <returns>What the move produced.</returns>
    internal ViewerPhysicsDragStep Step(
        int pointerId,
        bool isLeftButtonDown,
        ViewerPhysicsVector3 grabWorldPoint,
        ViewerGizmoRay pointerRay,
        double deltaSeconds,
        out ViewerPhysicsRuntimeCommand command)
    {
        command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.Force, TargetId, ViewerPhysicsVector3.Zero);
        if (!Owns(pointerId))
        {
            return ViewerPhysicsDragStep.Ignored;
        }

        if (!isLeftButtonDown)
        {
            // The release happened somewhere the viewer never saw it, so the drag ends here rather
            // than pushing the body for as long as the pointer keeps moving.
            return ViewerPhysicsDragStep.MustEnd;
        }

        return _model.TryUpdate(grabWorldPoint, pointerRay, deltaSeconds, out command)
            ? ViewerPhysicsDragStep.Applied
            : ViewerPhysicsDragStep.Consumed;
    }

    /// <summary>Ends the drag, producing the clear exactly once.</summary>
    /// <param name="reason">Why the drag ended.</param>
    /// <param name="command">Receives the command that clears the accumulated force.</param>
    /// <returns><see langword="true"/> when this call ended a live drag.</returns>
    internal bool TryEnd(ViewerPhysicsDragEnd reason, out ViewerPhysicsRuntimeCommand command)
    {
        _ = reason;
        PointerId = NoPointer;
        if (!_model.TryEnd(out command))
        {
            return false;
        }

        Clears++;
        return true;
    }
}

/// <summary>What one pointer move produced for an interactive body drag.</summary>
internal enum ViewerPhysicsDragStep
{
    /// <summary>The move belongs to another pointer or to no drag at all.</summary>
    Ignored,

    /// <summary>The move belongs to the drag but produced no force.</summary>
    Consumed,

    /// <summary>The move produced a force command.</summary>
    Applied,

    /// <summary>The drag must end because the button is no longer down.</summary>
    MustEnd,
}

/// <summary>Identifies the inspector selection an operator is working in.</summary>
/// <param name="ObjectId">The extractor's stable identity for the selected object.</param>
/// <param name="PrimPath">The selected object's prim path, or an empty string.</param>
/// <param name="Kind">The selected object's extracted kind, or an empty string.</param>
/// <param name="PropertyName">The selected property name, or an empty string.</param>
/// <remarks>
/// The path alone is not an object. A single prim commonly produces a rigid body, a collider, and
/// a vehicle section, so restoring by path would put the operator back on whichever of them the
/// extractor happened to emit first - and every interaction that followed would target that one.
/// The extractor's identity is matched first, the kind second, and the bare path only as a last
/// resort for a document whose extraction does not carry identities.
/// </remarks>
internal readonly record struct ViewerPhysicsSelectionAnchor(
    ulong ObjectId,
    string PrimPath,
    string Kind,
    string PropertyName)
{
    /// <summary>Gets the anchor of an inspector with nothing selected.</summary>
    internal static ViewerPhysicsSelectionAnchor None { get; } =
        new(0UL, string.Empty, string.Empty, string.Empty);

    /// <summary>Gets a value indicating whether an object was selected.</summary>
    internal bool HasPrim => PrimPath.Length != 0 || ObjectId != 0UL;

    /// <summary>Gets a value indicating whether a property was selected.</summary>
    internal bool HasProperty => HasPrim && PropertyName.Length != 0;

    /// <summary>Creates the anchor that names one section and one of its properties.</summary>
    /// <param name="section">The selected section.</param>
    /// <param name="propertyName">The selected property name, or an empty string.</param>
    /// <returns>The anchor.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static ViewerPhysicsSelectionAnchor For(
        ViewerPhysicsObjectSection section,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(propertyName);
        return new ViewerPhysicsSelectionAnchor(
            section.ObjectId,
            section.PrimPath,
            section.Kind,
            propertyName);
    }
}

/// <summary>
/// Restores the inspector selection across a reload that changed the extraction.
/// </summary>
/// <remarks>
/// <para>
/// Authoring a property changes what the extractor produces, which changes the extraction
/// fingerprint, which rebuilds the object list. Rebuilding it by position would silently move the
/// selection back to the first object - so the very act of editing a property would retarget every
/// interaction that follows it. Applying a force after editing the third body would push the first
/// one instead, and the operator would have no way to tell that from a simulation bug.
/// </para>
/// <para>
/// The selection is therefore restored by identity, not by index, and the property is looked up
/// only inside the section that was resolved. Searching every section for the property name would
/// happily find the same name on a different object of the same prim - which is the very confusion
/// the identity exists to prevent.
/// </para>
/// </remarks>
internal static class ViewerPhysicsSelectionResolver
{
    /// <summary>Finds the section index one anchor names.</summary>
    /// <param name="sections">The rebuilt sections.</param>
    /// <param name="anchor">The selection captured before the rebuild.</param>
    /// <returns>The index, or <c>-1</c> when there is nothing to select.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sections"/> is null.</exception>
    internal static int ResolveSection(
        IReadOnlyList<ViewerPhysicsObjectSection> sections,
        ViewerPhysicsSelectionAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count == 0)
        {
            return -1;
        }

        if (anchor.ObjectId != 0UL)
        {
            for (int index = 0; index < sections.Count; index++)
            {
                if (sections[index].ObjectId == anchor.ObjectId)
                {
                    return index;
                }
            }
        }

        if (anchor.PrimPath.Length != 0 && anchor.Kind.Length != 0)
        {
            for (int index = 0; index < sections.Count; index++)
            {
                if (string.Equals(
                        sections[index].PrimPath,
                        anchor.PrimPath,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        sections[index].Kind,
                        anchor.Kind,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        if (anchor.PrimPath.Length != 0)
        {
            for (int index = 0; index < sections.Count; index++)
            {
                if (string.Equals(
                    sections[index].PrimPath,
                    anchor.PrimPath,
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        // The object the operator was working in is no longer extracted, so the first one is the
        // only honest choice left.
        return 0;
    }

    /// <summary>Finds the property row index one anchor names inside its own section.</summary>
    /// <param name="sections">The rebuilt sections.</param>
    /// <param name="anchor">The selection captured before the rebuild.</param>
    /// <returns>The row index, or <c>-1</c> when the property is gone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sections"/> is null.</exception>
    internal static int ResolveRow(
        IReadOnlyList<ViewerPhysicsObjectSection> sections,
        ViewerPhysicsSelectionAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (!anchor.HasProperty)
        {
            return -1;
        }

        int section = ResolveSection(sections, anchor);
        if (section < 0 || !Matches(sections[section], anchor))
        {
            // The resolver fell back to a different object, so the property the anchor names is
            // not this object's property and must not be selected on it.
            return -1;
        }

        IReadOnlyList<ViewerPhysicsPropertyRow> rows = sections[section].Rows;
        for (int index = 0; index < rows.Count; index++)
        {
            if (string.Equals(rows[index].Name, anchor.PropertyName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool Matches(
        ViewerPhysicsObjectSection section,
        ViewerPhysicsSelectionAnchor anchor)
    {
        if (anchor.ObjectId != 0UL)
        {
            return section.ObjectId == anchor.ObjectId;
        }

        if (!string.Equals(section.PrimPath, anchor.PrimPath, StringComparison.Ordinal))
        {
            return false;
        }

        return anchor.Kind.Length == 0 ||
            string.Equals(section.Kind, anchor.Kind, StringComparison.Ordinal);
    }
}
