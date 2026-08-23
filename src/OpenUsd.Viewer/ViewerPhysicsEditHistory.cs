// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer;

/// <summary>One authored physics edit, expressed so it can be applied and reverted.</summary>
/// <param name="PrimPath">The prim the property is authored on.</param>
/// <param name="Name">The authored property name.</param>
/// <param name="Label">The label the history shows for the property.</param>
/// <param name="Before">The value the property held before the edit.</param>
/// <param name="After">The value the edit authored.</param>
/// <remarks>
/// Both sides of the edit are carried, including the unauthored state, because undo has to be able
/// to remove an opinion the edit created. An edit that only knew its new value could restore the
/// schema fallback as an authored opinion at best, which changes what the file says even though the
/// user asked for the change to be undone.
/// </remarks>
internal sealed record ViewerPhysicsEdit(
    string PrimPath,
    string Name,
    string Label,
    ViewerPhysicsValue Before,
    ViewerPhysicsValue After)
{
    /// <summary>Gets the sentence the undo and redo menus show.</summary>
    internal string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Label} on {PrimPath}");

    /// <summary>Returns the edit that reverses this one.</summary>
    /// <returns>The reversed edit.</returns>
    internal ViewerPhysicsEdit Reversed() => new(PrimPath, Name, Label, After, Before);
}

/// <summary>One undoable authoring step, which may carry several property edits.</summary>
/// <param name="Description">The sentence the undo and redo menus show.</param>
/// <param name="Edits">The property edits the step authored, in submission order.</param>
internal sealed record ViewerPhysicsEditStep(
    string Description,
    IReadOnlyList<ViewerPhysicsEdit> Edits)
{
    /// <summary>Returns the step that reverses this one.</summary>
    /// <remarks>
    /// The edits are reversed in the opposite order they were applied, so a step that authored two
    /// opinions on the same property - which a coalesced drag can produce - unwinds to exactly the
    /// value the step started from rather than to whichever of the two happened to be applied last.
    /// </remarks>
    /// <returns>The reversed step.</returns>
    internal ViewerPhysicsEditStep Reversed()
    {
        var reversed = new ViewerPhysicsEdit[Edits.Count];
        for (int index = 0; index < Edits.Count; index++)
        {
            reversed[index] = Edits[Edits.Count - 1 - index].Reversed();
        }

        return new ViewerPhysicsEditStep(Description, reversed);
    }
}

/// <summary>
/// The bounded undo and redo history of the physics inspector's authoring.
/// </summary>
/// <remarks>
/// <para>
/// <b>The history stores intent, not stage state.</b> Each step carries the exact before and after
/// value of every property it authored, so undo re-authors the previous opinion through the same
/// transactional path a forward edit uses. Snapshotting layers instead would either restore edits
/// the user made in between through some other surface, or fail the moment the stage was reloaded.
/// </para>
/// <para>
/// <b>A drag is one step.</b> A slider produces a value per pointer move; recording each as its own
/// step would make undo take a hundred presses to reverse one gesture. Consecutive edits to the
/// same property inside the merge window are therefore coalesced into a single step whose before
/// value is the first one observed, which is what a user means by "undo that change".
/// </para>
/// <para>
/// <b>Redo is dropped on a new edit.</b> A history that kept a redo branch after a divergent edit
/// would let a later redo re-author a value the user has already replaced.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsEditHistory
{
    /// <summary>The default number of steps the history keeps.</summary>
    internal const int DefaultCapacity = 128;

    private readonly List<ViewerPhysicsEditStep> _undo = [];
    private readonly List<ViewerPhysicsEditStep> _redo = [];
    private readonly int _capacity;
    private readonly double _mergeSeconds;
    private double _lastSeconds = double.NegativeInfinity;
    private string _lastKey = string.Empty;

    /// <summary>Initializes a bounded history.</summary>
    /// <param name="capacity">The number of steps the history keeps.</param>
    /// <param name="mergeSeconds">How long consecutive edits to one property coalesce.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not positive and finite.</exception>
    internal ViewerPhysicsEditHistory(int capacity = DefaultCapacity, double mergeSeconds = 0.5d)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!double.IsFinite(mergeSeconds) || mergeSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mergeSeconds),
                mergeSeconds,
                "The merge window must be finite and non-negative.");
        }

        _capacity = capacity;
        _mergeSeconds = mergeSeconds;
    }

    /// <summary>Gets a value indicating whether a step can be undone.</summary>
    internal bool CanUndo => _undo.Count != 0;

    /// <summary>Gets a value indicating whether a step can be redone.</summary>
    internal bool CanRedo => _redo.Count != 0;

    /// <summary>Gets the number of steps that can be undone.</summary>
    internal int UndoDepth => _undo.Count;

    /// <summary>Gets the number of steps that can be redone.</summary>
    internal int RedoDepth => _redo.Count;

    /// <summary>Gets the sentence describing the step undo would reverse.</summary>
    internal string UndoDescription =>
        _undo.Count == 0 ? string.Empty : _undo[^1].Description;

    /// <summary>Gets the sentence describing the step redo would replay.</summary>
    internal string RedoDescription =>
        _redo.Count == 0 ? string.Empty : _redo[^1].Description;

    /// <summary>Records one applied step, coalescing a continuing gesture into the previous one.</summary>
    /// <param name="step">The step that was applied.</param>
    /// <param name="nowSeconds">The monotonic time the step was applied at.</param>
    /// <returns><see langword="true"/> when the step became a new entry rather than merging.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nowSeconds"/> is not finite.</exception>
    internal bool Record(ViewerPhysicsEditStep step, double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!double.IsFinite(nowSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "The record time must be finite.");
        }

        if (step.Edits.Count == 0)
        {
            return false;
        }

        _redo.Clear();
        string key = DescribeKey(step);
        bool continues = key.Length != 0 &&
            string.Equals(key, _lastKey, StringComparison.Ordinal) &&
            _undo.Count != 0 &&
            nowSeconds - _lastSeconds <= _mergeSeconds;
        _lastKey = key;
        _lastSeconds = nowSeconds;

        if (continues)
        {
            ViewerPhysicsEditStep previous = _undo[^1];
            var merged = new List<ViewerPhysicsEdit>(previous.Edits.Count);
            merged.AddRange(previous.Edits);
            for (int index = 0; index < step.Edits.Count; index++)
            {
                merged.Add(step.Edits[index]);
            }

            _undo[^1] = new ViewerPhysicsEditStep(previous.Description, merged);
            return false;
        }

        _undo.Add(step);
        if (_undo.Count > _capacity)
        {
            // Dropping the oldest step only costs reach; keeping every step would let one long
            // authoring session grow the history without bound.
            _undo.RemoveAt(0);
        }

        return true;
    }

    /// <summary>Takes the step undo must apply, moving it onto the redo stack.</summary>
    /// <param name="step">Receives the reversing step.</param>
    /// <returns><see langword="true"/> when a step was taken.</returns>
    internal bool TryTakeUndo(out ViewerPhysicsEditStep step)
    {
        if (_undo.Count == 0)
        {
            step = new ViewerPhysicsEditStep(string.Empty, []);
            return false;
        }

        ViewerPhysicsEditStep applied = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(applied);
        BreakGesture();
        step = applied.Reversed();
        return true;
    }

    /// <summary>Takes the step redo must apply, moving it back onto the undo stack.</summary>
    /// <param name="step">Receives the replaying step.</param>
    /// <returns><see langword="true"/> when a step was taken.</returns>
    internal bool TryTakeRedo(out ViewerPhysicsEditStep step)
    {
        if (_redo.Count == 0)
        {
            step = new ViewerPhysicsEditStep(string.Empty, []);
            return false;
        }

        ViewerPhysicsEditStep applied = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(applied);
        BreakGesture();
        step = applied;
        return true;
    }

    /// <summary>Puts back a step whose application failed, so the history still matches the stage.</summary>
    /// <param name="step">The step that was taken but not applied.</param>
    /// <param name="wasUndo">Whether the step came from the undo stack.</param>
    /// <remarks>
    /// A failed undo that stayed popped would leave the history claiming the stage holds a value it
    /// does not, and the next undo would then re-author an older value over the newer one.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    internal void Restore(ViewerPhysicsEditStep step, bool wasUndo)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (wasUndo)
        {
            if (_redo.Count != 0)
            {
                _redo.RemoveAt(_redo.Count - 1);
            }

            _undo.Add(step);
            return;
        }

        if (_undo.Count != 0)
        {
            _undo.RemoveAt(_undo.Count - 1);
        }

        _redo.Add(step);
    }

    /// <summary>Ends the current gesture so the next edit starts a new step.</summary>
    internal void BreakGesture()
    {
        _lastKey = string.Empty;
        _lastSeconds = double.NegativeInfinity;
    }

    /// <summary>Discards the whole history.</summary>
    /// <remarks>
    /// A document change invalidates every remembered value: the prims the steps name may not exist
    /// on the new stage, and re-authoring an old value onto a matching path would corrupt it.
    /// </remarks>
    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        BreakGesture();
    }

    private static string DescribeKey(ViewerPhysicsEditStep step)
    {
        if (step.Edits.Count != 1)
        {
            // Only a single-property gesture coalesces. A multi-property step is a deliberate
            // action - applying a preset, say - and merging two of them would make one undo
            // reverse changes the user made in two separate actions.
            return string.Empty;
        }

        ViewerPhysicsEdit edit = step.Edits[0];
        return edit.PrimPath + "\u0000" + edit.Name;
    }
}
