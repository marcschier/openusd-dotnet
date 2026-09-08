// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal static class ViewerHostInteraction
{
    internal static Task RunStageReadyCallbackAsync(
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken,
        ViewerHostCallbackScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }
        if (scope is not null)
        {
            return scope.TryRun(callback, out Task? task) ? task :
                Task.FromException(new InvalidOperationException("The host callback scope is quiescing or full."));
        }
        return Task.Run(
            () => callback(cancellationToken),
            CancellationToken.None);
    }

    internal static bool TryMapViewportClick(
        double logicalX,
        double logicalY,
        in ViewerLogicalContentBounds contentBounds,
        double renderScaling,
        ViewportDimensions viewport,
        out ViewerPhysicalPixel pixel) =>
        ViewerPickPixelMapper.TryMap(
            logicalX,
            logicalY,
            contentBounds,
            renderScaling,
            viewport,
            out pixel);

    internal static async ValueTask<RenderPickResult> PickAndDispatchAsync(
        ViewerPhysicalPixel pixel,
        RenderPickTarget target,
        Func<ViewerPhysicalPixel, RenderPickTarget, RenderPickOptions, CancellationToken,
            ValueTask<RenderPickResult>> pickAsync,
        Func<ViewerPickEventArgs, CancellationToken, Task>? callback,
        CancellationToken cancellationToken,
        ViewerHostCallbackScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(pickAsync);
        RenderPickResult result = await pickAsync(
            pixel,
            target,
            RenderPickOptions.None,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        DispatchPickCallback(result, callback, cancellationToken, scope);
        return result;
    }

    internal static bool DispatchPickCallback(
        RenderPickResult result,
        Func<ViewerPickEventArgs, CancellationToken, Task>? callback,
        CancellationToken cancellationToken,
        ViewerHostCallbackScope? scope = null)
    {
        if (callback is null)
        {
            return false;
        }

        var args = new ViewerPickEventArgs(result);
        async Task invoke(CancellationToken token)
        {
            try
            {
                await callback(args, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ViewerStartupOptions.WriteStatus($"Host pick callback failed: {exception.Message}");
            }
        }
        if (scope is not null)
        {
            bool accepted = scope.TryRun(invoke, out _);
            if (!accepted)
            {
                ViewerStartupOptions.WriteStatus("Host pick callback refused while callbacks are quiescing or full.");
            }
            return accepted;
        }
        _ = Task.Run(() => invoke(cancellationToken), CancellationToken.None);
        return true;
    }

    internal static bool DispatchSelectionCallback(
        SelectionState selection,
        string? subtree,
        Func<ViewerSelectionChangedEventArgs, CancellationToken, Task>? callback,
        CancellationToken cancellationToken,
        ViewerHostCallbackScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (callback is null || !ShouldNotifySelection(selection, subtree))
        {
            return false;
        }

        var args = new ViewerSelectionChangedEventArgs(selection.PrimPaths.ToArray());
        async Task invoke(CancellationToken token)
        {
            try
            {
                await callback(args, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ViewerStartupOptions.WriteStatus($"Host selection callback failed: {exception.Message}");
            }
        }
        if (scope is not null)
        {
            bool accepted = scope.TryRun(invoke, out _);
            if (!accepted)
            {
                ViewerStartupOptions.WriteStatus(
                    "Host selection callback refused while callbacks are quiescing or full.");
            }
            return accepted;
        }
        _ = Task.Run(() => invoke(cancellationToken), CancellationToken.None);
        return true;
    }

    internal static bool ShouldNotifySelection(SelectionState selection, string? subtree)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!IsValidSubtree(subtree))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(subtree))
        {
            return true;
        }
        if (selection.PrimPaths.Count == 0)
        {
            return true;
        }
        return selection.PrimPaths.Any(path => IsInSubtree(path, subtree));
    }

    private static bool IsInSubtree(string primPath, string root)
    {
        if (string.Equals(primPath, root, StringComparison.Ordinal))
        {
            return true;
        }
        return root == "/" ||
            (primPath.Length > root.Length &&
             primPath[root.Length] == '/' &&
             primPath.StartsWith(root, StringComparison.Ordinal));
    }

    private static bool IsValidSubtree(string? root) =>
        string.IsNullOrWhiteSpace(root) ||
        (root[0] == '/' && root.IndexOf('\\') < 0);
}
