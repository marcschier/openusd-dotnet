// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal sealed class ViewerSourceChangeQueue
{
    private readonly object _gate = new();
    private UsdStageChange? _pending;
    private bool _paused;
    private bool _scheduled;
    private bool _retired;
    private bool _refreshDocument;

    internal bool Post(UsdStageChange change)
    {
        lock (_gate)
        {
            if (_retired)
            {
                return false;
            }
            _pending = _pending is { } pending ? pending.Coalesce(change) : change;
            _refreshDocument |= _paused;
            return Schedule();
        }
    }

    internal void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            _refreshDocument |= _pending is not null;
        }
    }

    internal bool Resume()
    {
        lock (_gate)
        {
            _paused = false;
            return Schedule();
        }
    }

    internal bool TryTake(out UsdStageChange change, out bool refreshDocument)
    {
        lock (_gate)
        {
            if (_retired || _paused || _pending is not { } pending)
            {
                change = default;
                refreshDocument = false;
                return false;
            }
            change = pending;
            refreshDocument = _refreshDocument;
            _pending = null;
            _refreshDocument = false;
            return true;
        }
    }

    internal bool Complete()
    {
        lock (_gate)
        {
            _scheduled = false;
            return Schedule();
        }
    }

    internal void Retire()
    {
        lock (_gate)
        {
            _retired = true;
            _pending = null;
            _refreshDocument = false;
        }
    }

    private bool Schedule()
    {
        if (_retired || _paused || _scheduled || _pending is null)
        {
            return false;
        }
        _scheduled = true;
        return true;
    }
}
