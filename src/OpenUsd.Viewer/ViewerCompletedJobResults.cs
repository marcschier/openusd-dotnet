// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerCompletedJobResults
{
    private readonly Window _owner;
    private readonly Button _action;
    private RenderDiskJobResult? _job;
    private string _description = string.Empty;
    private CompletedRenderJobWindow? _window;

    internal ViewerCompletedJobResults(Window owner, Button action)
    {
        _owner = owner;
        _action = action;
        _action.IsEnabled = false;
        _action.Click += (_, _) => Show();
    }

    internal void Publish(RenderDiskJobResult job, string description)
    {
        _job = job;
        _description = description;
        _action.IsEnabled = true;
    }

    internal Task ClearAsync()
    {
        _job = null;
        _action.IsEnabled = false;
        return _window?.CloseAsync() ?? Task.CompletedTask;
    }

    private void Show()
    {
        if (_job is not { } job || !_action.IsEnabled || !_owner.IsVisible)
        {
            return;
        }
        if (_window is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new CompletedRenderJobWindow(job, _description);
        _window = window;
        window.Closed += (_, _) =>
        {
            _window = null;
            if (_job is not null && _owner.IsVisible && _action.IsEnabled)
            {
                _owner.Activate();
                _action.Focus();
            }
        };
        window.Show(_owner);
    }
}
