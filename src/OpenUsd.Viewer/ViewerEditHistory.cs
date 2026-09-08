// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Viewer;

internal interface IViewerHistoryStep<TStep> where TStep : class
{
    string Description { get; }

    int ChangeCount { get; }

    long RetainedBytes { get; }

    TStep Reversed();

    bool TryCoalesce(TStep next, [NotNullWhen(true)] out TStep? merged);
}

/// <summary>
/// One bounded history engine for immutable editor entries, independent of their native representation.
/// </summary>
internal sealed class ViewerEditHistory<TStep> where TStep : class, IViewerHistoryStep<TStep>
{
    internal const int DefaultCapacity = 128;
    internal const long DefaultMaximumRetainedBytes = 32 * 1024 * 1024;
    private readonly List<TStep> _undo = [];
    private readonly List<TStep> _redo = [];
    private readonly int _capacity;
    private readonly long _maximumRetainedBytes;
    private readonly double _mergeSeconds;
    private double _lastSeconds = double.NegativeInfinity;
    private Guid? _lastGestureId;
    private PendingChange? _pending;

    internal ViewerEditHistory(
        int capacity = DefaultCapacity,
        double mergeSeconds = 0.5d,
        long maximumRetainedBytes = DefaultMaximumRetainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRetainedBytes);
        if (!double.IsFinite(mergeSeconds) || mergeSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(mergeSeconds));
        }
        _capacity = capacity;
        _mergeSeconds = mergeSeconds;
        _maximumRetainedBytes = maximumRetainedBytes;
    }

    internal bool CanUndo => _pending is null && _undo.Count != 0;

    internal bool CanRedo => _pending is null && _redo.Count != 0;

    internal int UndoDepth => _undo.Count;

    internal int RedoDepth => _redo.Count;

    internal long RetainedBytes { get; private set; }

    internal string UndoDescription => CanUndo ? _undo[^1].Description : string.Empty;

    internal string RedoDescription => CanRedo ? _redo[^1].Description : string.Empty;

    internal sealed record PendingChange(TStep Step, bool IsUndo);

    internal bool TryBeginUndo([NotNullWhen(true)] out PendingChange? change)
    {
        return TryBegin(undo: true, out change);
    }

    internal bool TryBeginRedo([NotNullWhen(true)] out PendingChange? change)
    {
        return TryBegin(undo: false, out change);
    }

    private bool TryBegin(bool undo, [NotNullWhen(true)] out PendingChange? change)
    {
        if (_pending is not null)
        {
            throw new InvalidOperationException("A history application is already pending.");
        }
        List<TStep> source = undo ? _undo : _redo;
        if (source.Count == 0)
        {
            change = null;
            return false;
        }
        change = new PendingChange(source[^1], undo);
        _pending = change;
        return true;
    }

    internal void Complete(PendingChange change, bool applied, TStep? replacement = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!ReferenceEquals(_pending, change))
        {
            throw new InvalidOperationException("The history application no longer owns the pending entry.");
        }
        List<TStep> source = change.IsUndo ? _undo : _redo;
        if (source.Count == 0 || !ReferenceEquals(source[^1], change.Step))
        {
            throw new InvalidOperationException("The pending history entry changed before completion.");
        }
        if (applied)
        {
            if (replacement is not null)
            {
                if (replacement.ChangeCount <= 0 || replacement.RetainedBytes <= 0 ||
                    replacement.RetainedBytes > _maximumRetainedBytes)
                {
                    throw new ArgumentException("Replacement history state exceeds its bounds.", nameof(replacement));
                }
                RetainedBytes -= source[^1].RetainedBytes;
                source[^1] = replacement;
                RetainedBytes = checked(RetainedBytes + replacement.RetainedBytes);
            }
            MoveLast(source, change.IsUndo ? _redo : _undo);
            while (RetainedBytes > _maximumRetainedBytes)
            {
                List<TStep> oldest = _undo.Count != 0 ? _undo : _redo;
                RetainedBytes -= oldest[0].RetainedBytes;
                oldest.RemoveAt(0);
            }
        }
        else if (replacement is not null)
        {
            throw new ArgumentException(
                "An unapplied operation cannot replace its history state.", nameof(replacement));
        }
        _pending = null;
        BreakGesture();
    }

    internal bool CanRecord(TStep step, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_pending is not null)
        {
            diagnostic = "Wait for the pending undo or redo application before recording another edit.";
            return false;
        }
        if (step.ChangeCount <= 0)
        {
            diagnostic = "The edit carries no change to record.";
            return false;
        }
        if (step.RetainedBytes <= 0 || step.RetainedBytes > _maximumRetainedBytes)
        {
            diagnostic = "The edit exceeds the history payload safety bound.";
            return false;
        }
        diagnostic = string.Empty;
        return true;
    }

    internal bool Record(TStep step, double nowSeconds, Guid? gestureId = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_pending is not null)
        {
            throw new InvalidOperationException("A history application is pending; recording is not available.");
        }
        if (!double.IsFinite(nowSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
        if (gestureId == Guid.Empty)
        {
            throw new ArgumentException("An explicit gesture must have a non-empty identity.", nameof(gestureId));
        }
        if (step.ChangeCount == 0)
        {
            return false;
        }
        if (!CanRecord(step, out string diagnostic))
        {
            throw new ArgumentException(diagnostic, nameof(step));
        }

        bool coalesced = false;
        bool continuingGesture = gestureId is { } identity
            ? identity == _lastGestureId
            : _lastGestureId is null && nowSeconds - _lastSeconds <= _mergeSeconds;
        if (CanUndo && nowSeconds >= _lastSeconds && continuingGesture &&
            _undo[^1].TryCoalesce(step, out TStep? merged) && CanRecord(merged, out _))
        {
            RetainedBytes -= _undo[^1].RetainedBytes;
            _undo.RemoveAt(_undo.Count - 1);
            step = merged;
            coalesced = true;
        }
        foreach (TStep undone in _redo)
        {
            RetainedBytes -= undone.RetainedBytes;
        }
        _redo.Clear();
        _undo.Add(step);
        RetainedBytes = checked(RetainedBytes + step.RetainedBytes);
        _lastSeconds = nowSeconds;
        _lastGestureId = gestureId;
        while (_undo.Count > _capacity || RetainedBytes > _maximumRetainedBytes)
        {
            RetainedBytes -= _undo[0].RetainedBytes;
            _undo.RemoveAt(0);
        }
        return !coalesced;
    }

    internal bool TryPeekUndo([NotNullWhen(true)] out TStep? step)
    {
        step = CanUndo ? _undo[^1] : null;
        return step is not null;
    }

    internal bool TryPeekRedo([NotNullWhen(true)] out TStep? step)
    {
        step = CanRedo ? _redo[^1] : null;
        return step is not null;
    }

    internal bool TryTakeUndo([NotNullWhen(true)] out TStep? step)
    {
        if (!TryPeekUndo(out TStep? original))
        {
            step = null;
            return false;
        }
        step = original.Reversed();
        MoveLast(_undo, _redo);
        BreakGesture();
        return true;
    }

    internal bool TryTakeRedo([NotNullWhen(true)] out TStep? step)
    {
        if (!TryPeekRedo(out step))
        {
            return false;
        }
        MoveLast(_redo, _undo);
        BreakGesture();
        return true;
    }

    internal void RestoreTravel(bool wasUndo)
    {
        if (_pending is not null)
        {
            throw new InvalidOperationException("A reserved history application must be completed explicitly.");
        }
        List<TStep> source = wasUndo ? _redo : _undo;
        List<TStep> destination = wasUndo ? _undo : _redo;
        if (source.Count == 0)
        {
            throw new InvalidOperationException("The failed history operation no longer owns a pending entry.");
        }
        MoveLast(source, destination);
        BreakGesture();
    }

    internal void BreakGesture()
    {
        _lastSeconds = double.NegativeInfinity;
        _lastGestureId = null;
    }

    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _pending = null;
        RetainedBytes = 0;
        BreakGesture();
    }

    private static void MoveLast(List<TStep> source, List<TStep> destination)
    {
        TStep step = source[^1];
        source.RemoveAt(source.Count - 1);
        destination.Add(step);
    }
}
