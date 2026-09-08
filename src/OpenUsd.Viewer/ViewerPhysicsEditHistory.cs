// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Viewer;

/// <summary>One authored physics edit, expressed so it can be applied and reverted.</summary>
/// <param name="PrimPath">The prim the property is authored on.</param>
/// <param name="Name">The authored property name.</param>
/// <param name="Label">The label the history shows for the property.</param>
/// <param name="Before">The value the property held before the edit.</param>
/// <param name="After">The value the edit authored.</param>
/// <remarks>
/// The scalar before-value supports the legacy adapter and detached controller tests. Production
/// authoring captures exact native target-layer state through the shared document editor instead;
/// this supplied value is never accepted as proof of a review-layer opinion.
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
    IReadOnlyList<ViewerPhysicsEdit> Edits) : IViewerHistoryStep<ViewerPhysicsEditStep>
{
    public IReadOnlyList<ViewerPhysicsEdit> Edits { get; } = Array.AsReadOnly(Edits.ToArray());

    public int ChangeCount => Edits.Count;

    public long RetainedBytes
    {
        get
        {
            long bytes = 128L + (Description.Length * 2L);
            foreach (ViewerPhysicsEdit edit in Edits)
            {
                bytes = checked(bytes + 256L +
                    ((edit.PrimPath.Length + (long)edit.Name.Length + edit.Label.Length +
                    (edit.Before.TextValue?.Length ?? 0) + (edit.After.TextValue?.Length ?? 0)) * 2L));
            }
            return bytes;
        }
    }

    public bool TryCoalesce(
        ViewerPhysicsEditStep next, [NotNullWhen(true)] out ViewerPhysicsEditStep? merged)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (Edits.Count == 1 && next.Edits.Count == 1 &&
            Edits[0].PrimPath == next.Edits[0].PrimPath && Edits[0].Name == next.Edits[0].Name &&
            Edits[0].After == next.Edits[0].Before)
        {
            merged = new ViewerPhysicsEditStep(Description, [Edits[0] with { After = next.Edits[0].After }]);
            return true;
        }
        merged = null;
        return false;
    }

    /// <summary>Returns the step that reverses this one.</summary>
    /// <remarks>
    /// The edits are reversed in the opposite order they were applied, so a step that authored two
    /// opinions on the same property - which a coalesced drag can produce - unwinds to exactly the
    /// value the step started from rather than to whichever of the two happened to be applied last.
    /// </remarks>
    /// <returns>The reversed step.</returns>
    public ViewerPhysicsEditStep Reversed()
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
/// This scalar history is used only when no shared document editor is supplied. Production physics
/// and focused property edits use one bounded native authored-snapshot history with affected-field CAS.
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
internal sealed class ViewerPhysicsEditHistory : IViewerEditHistoryView
{
    /// <summary>The default number of steps the history keeps.</summary>
    internal const int DefaultCapacity = ViewerEditHistory<ViewerPhysicsEditStep>.DefaultCapacity;
    private readonly ViewerEditHistory<ViewerPhysicsEditStep> _history;

    /// <summary>Initializes a bounded history.</summary>
    /// <param name="capacity">The number of steps the history keeps.</param>
    /// <param name="mergeSeconds">How long consecutive edits to one property coalesce.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not positive and finite.</exception>
    internal ViewerPhysicsEditHistory(
        int capacity = DefaultCapacity,
        double mergeSeconds = 0.5d,
        long maximumRetainedBytes = ViewerEditHistory<ViewerPhysicsEditStep>.DefaultMaximumRetainedBytes)
    {
        _history = new ViewerEditHistory<ViewerPhysicsEditStep>(capacity, mergeSeconds, maximumRetainedBytes);
    }

    /// <summary>Gets a value indicating whether a step can be undone.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Gets a value indicating whether a step can be redone.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>Gets the number of steps that can be undone.</summary>
    public int UndoDepth => _history.UndoDepth;

    /// <summary>Gets the number of steps that can be redone.</summary>
    public int RedoDepth => _history.RedoDepth;

    internal long RetainedBytes => _history.RetainedBytes;

    internal bool CanRecord(ViewerPhysicsEditStep step, out string diagnostic) =>
        _history.CanRecord(step, out diagnostic);

    /// <summary>Gets the sentence describing the step undo would reverse.</summary>
    public string UndoDescription =>
        _history.UndoDescription;

    /// <summary>Gets the sentence describing the step redo would replay.</summary>
    public string RedoDescription =>
        _history.RedoDescription;

    /// <summary>Records one applied step, coalescing a continuing gesture into the previous one.</summary>
    /// <param name="step">The step that was applied.</param>
    /// <param name="nowSeconds">The monotonic time the step was applied at.</param>
    /// <returns><see langword="true"/> when the step became a new entry rather than merging.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nowSeconds"/> is not finite.</exception>
    internal bool Record(ViewerPhysicsEditStep step, double nowSeconds)
    {
        return _history.Record(step, nowSeconds);
    }

    /// <summary>Takes the step undo must apply, moving it onto the redo stack.</summary>
    /// <param name="step">Receives the reversing step.</param>
    /// <returns><see langword="true"/> when a step was taken.</returns>
    internal bool TryTakeUndo(out ViewerPhysicsEditStep step)
    {
        if (!_history.TryTakeUndo(out ViewerPhysicsEditStep? taken))
        {
            step = new ViewerPhysicsEditStep(string.Empty, []);
            return false;
        }

        step = taken;
        return true;
    }

    /// <summary>Takes the step redo must apply, moving it back onto the undo stack.</summary>
    /// <param name="step">Receives the replaying step.</param>
    /// <returns><see langword="true"/> when a step was taken.</returns>
    internal bool TryTakeRedo(out ViewerPhysicsEditStep step)
    {
        if (!_history.TryTakeRedo(out ViewerPhysicsEditStep? taken))
        {
            step = new ViewerPhysicsEditStep(string.Empty, []);
            return false;
        }

        step = taken;
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
        _history.RestoreTravel(wasUndo);
    }

    /// <summary>Ends the current gesture so the next edit starts a new step.</summary>
    internal void BreakGesture()
    {
        _history.BreakGesture();
    }

    /// <summary>Discards the whole history.</summary>
    /// <remarks>
    /// A document change invalidates every remembered value: the prims the steps name may not exist
    /// on the new stage, and re-authoring an old value onto a matching path would corrupt it.
    /// </remarks>
    internal void Clear()
    {
        _history.Clear();
    }
}
