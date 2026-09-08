// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private SourceChangeRegistration? _sourceChanges;

    private void AttachSourceChanges(ViewerRenderCoordinator coordinator, CancellationToken documentToken)
    {
        if (IsAutomatedViewerRun())
        {
            return;
        }
        if (_sourceChanges is not null)
        {
            throw new InvalidOperationException("A document source-change subscription is already attached.");
        }
        _sourceChanges = new SourceChangeRegistration(coordinator, OnStageChanged, documentToken);
    }

    private void OnStageChanged(SourceChangeRegistration source, UsdStageChange change)
    {
        if (source.Changes.Post(change))
        {
            ScheduleSourceRefresh(source);
        }
    }

    private void ScheduleSourceRefresh(SourceChangeRegistration source) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrentSource(source))
            {
                source.Changes.Retire();
                return;
            }
            source.Work = RefreshSourceChangesAsync(source);
        }, DispatcherPriority.Background);

    private bool IsCurrentSource(SourceChangeRegistration source) =>
        _coordinator is { } coordinator &&
        ReferenceEquals(source, _sourceChanges) &&
        ReferenceEquals(source.Coordinator, coordinator) &&
        ReferenceEquals(source.Scheduler, coordinator.Scheduler) &&
        !source.Cancellation.IsCancellationRequested && !_shutdownStarted;

    private async Task RefreshSourceChangesAsync(SourceChangeRegistration source)
    {
        CancellationToken cancellationToken = source.Cancellation.Token;
        bool entered = false;
        try
        {
            await _documentGate.WaitAsync(cancellationToken);
            entered = true;
            if (!IsCurrentSource(source) ||
                !source.Changes.TryTake(out UsdStageChange change, out bool refreshDocument))
            {
                return;
            }
            NotifyPhysicsStageChanged(change);
            if (refreshDocument || change.Invalidation >= UsdStageInvalidationKind.Topology)
            {
                ViewerLayerStackSnapshot layers = _layers;
                string? selectedPath = _selectionState.PrimPath;
                double? inspectionTime = _inspectorTimeCode;
                ViewerDocumentSnapshot document = await source.Scheduler.InvokeAsync(
                    stage => ViewerStageSnapshotBuilder.BuildDocument(stage, layers, selectedPath, inspectionTime),
                    cancellationToken);
                if (!IsCurrentSource(source))
                {
                    return;
                }
                _statistics = document.Statistics;
                _stageCameras = document.StageCameras ?? [];
                _primaryCameraPath = document.PrimaryCameraPath;
                await ApplyDocumentRefreshAsync(
                    source.Coordinator, document, cancellationToken, preserveTime: true);
                RenderStageCameraMenu();
            }
            else
            {
                _ = TryQueueStageCameraRefresh(source.Coordinator.CurrentState.Time.TimeCode, applyTime: false);
            }
            if (IsCurrentSource(source))
            {
                QueueDocumentEditingRefresh();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or InvalidDataException or
            InvalidOperationException or NotSupportedException or ArgumentException)
        {
            if (IsCurrentSource(source))
            {
                ShowError($"Could not refresh changed source data: {exception.Message}");
            }
        }
        finally
        {
            if (entered)
            {
                _documentGate.Release();
            }
            if (source.Changes.Complete())
            {
                ScheduleSourceRefresh(source);
            }
        }
    }

    private async Task DetachSourceChangesAsync()
    {
        SourceChangeRegistration? source = _sourceChanges;
        _sourceChanges = null;
        if (source is not null)
        {
            await source.DisposeAsync();
        }
    }

    private sealed class SourceChangeRegistration : IAsyncDisposable
    {
        private readonly Action<UsdStageChange> _handler;

        internal SourceChangeRegistration(
            ViewerRenderCoordinator coordinator,
            Action<SourceChangeRegistration, UsdStageChange> receive,
            CancellationToken documentToken)
        {
            Coordinator = coordinator;
            Scheduler = coordinator.Scheduler;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(documentToken);
            _handler = change => receive(this, change);
            coordinator.StageChanged += _handler;
        }

        internal ViewerRenderCoordinator Coordinator { get; }
        internal UsdStageScheduler Scheduler { get; }
        internal CancellationTokenSource Cancellation { get; }
        internal ViewerSourceChangeQueue Changes { get; } = new();
        internal Task Work { get; set; } = Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            Coordinator.StageChanged -= _handler;
            Changes.Retire();
            Cancellation.Cancel();
            await Work.ConfigureAwait(false);
            Cancellation.Dispose();
        }
    }
}
