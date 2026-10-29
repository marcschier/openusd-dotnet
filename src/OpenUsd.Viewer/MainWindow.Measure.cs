// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Interop;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private readonly ViewerMeasureSession _measure = new();

    internal ViewerMeasureSession MeasureSession => _measure;

    private void OnToolsMeasureDistanceMenuClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ToolsMeasureDistanceMenuItem.IsChecked && _coordinator is not null)
        {
            ShowPickStatus(_measure.Activate());
        }
        else
        {
            CancelMeasureMode("Measure mode ended.");
        }
    }

    /// <summary>Leaves measure mode, unchecks the menu entry and optionally reports why.</summary>
    /// <returns><see langword="true"/> when measure mode was active.</returns>
    private bool CancelMeasureMode(string? status)
    {
        bool wasActive = _measure.Cancel();
        ToolsMeasureDistanceMenuItem.IsChecked = false;
        if (wasActive && status is not null)
        {
            ShowPickStatus(status);
        }
        return wasActive;
    }

    private void UpdateMeasureAvailability(bool available)
    {
        ToolsMeasureDistanceMenuItem.IsEnabled = available;
        if (!available)
        {
            CancelMeasureMode(status: null);
        }
    }

    private void InvalidateMeasurement(string reason)
    {
        if (_measure.Invalidate(reason) is { } status)
        {
            ShowPickStatus(status);
        }
    }

    private void InvalidateMeasurementForState(StageRenderState state)
    {
        if (_measure.InvalidateForStateRevision(state.Revision) is { } status)
        {
            ShowPickStatus(status);
        }
    }

    /// <summary>
    /// Feeds one pick to the measure session instead of the selection. The stage units are read
    /// on the stage scheduler only when the pick completes a pair.
    /// </summary>
    private async Task ApplyMeasurePickAsync(
        ViewerRenderCoordinator coordinator,
        RenderPickResult result,
        CancellationToken cancellationToken)
    {
        if (!_measure.NeedsUnits(result))
        {
            ShowPickStatus(_measure.Accept(result, units: null));
            return;
        }
        UsdStageLinearUnits units;
        try
        {
            units = await coordinator.Scheduler.InvokeAsync(
                static stage => stage.GetLinearUnits(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is NotSupportedException or OpenUsdNativeException)
        {
            ShowPickStatus(_measure.RefuseUnits(exception.Message));
            return;
        }
        if (_measure.IsActive)
        {
            ShowPickStatus(_measure.Accept(result, units));
        }
    }

    private void PostMeasurementInvalidation(string reason) =>
        Dispatcher.UIThread.Post(() => InvalidateMeasurement(reason));
}
