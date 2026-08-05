// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal static class ViewerHostInteraction
{
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pickAsync);
        RenderPickResult result = await pickAsync(
            pixel,
            target,
            RenderPickOptions.None,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        DispatchPickCallback(result, callback, cancellationToken);
        return result;
    }

    internal static bool DispatchPickCallback(
        RenderPickResult result,
        Func<ViewerPickEventArgs, CancellationToken, Task>? callback,
        CancellationToken cancellationToken)
    {
        if (callback is null)
        {
            return false;
        }

        var args = new ViewerPickEventArgs(result);
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await callback(args, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    ViewerStartupOptions.WriteStatus(
                        $"Host pick callback failed: {exception.Message}");
                }
            },
            CancellationToken.None);
        return true;
    }

    internal static bool DispatchSelectionCallback(
        SelectionState selection,
        string? subtree,
        Func<ViewerSelectionChangedEventArgs, CancellationToken, Task>? callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (callback is null || !ShouldNotifySelection(selection, subtree))
        {
            return false;
        }

        var args = new ViewerSelectionChangedEventArgs(selection.PrimPaths.ToArray());
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await callback(args, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    ViewerStartupOptions.WriteStatus(
                        $"Host selection callback failed: {exception.Message}");
                }
            },
            CancellationToken.None);
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
