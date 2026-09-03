// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerHostContractTests
{
    [Test]
    public async Task StageReadyCallbackRunsWithoutCapturedSynchronizationContext()
    {
        SynchronizationContext? entryContext = null;
        SynchronizationContext? continuationContext = null;
        var callbackEntered = CreateCompletion();
        var callbackContinued = CreateCompletion();
        using var callbackLifetime = new CancellationTokenSource();
        Task callbackTask;
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(
                new NonPumpingSynchronizationContext());
            callbackTask = ViewerHostInteraction.RunStageReadyCallbackAsync(
                async cancellationToken =>
                {
                    entryContext = SynchronizationContext.Current;
                    callbackEntered.SetResult();
                    await Task.Yield();
                    continuationContext = SynchronizationContext.Current;
                    callbackContinued.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                },
                callbackLifetime.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        await callbackEntered.Task.WaitAsync(TestTimeout);
        await callbackContinued.Task.WaitAsync(TestTimeout);

        await Assert.That(entryContext).IsNull();
        await Assert.That(continuationContext).IsNull();
        await Assert.That(callbackTask.IsCompleted).IsFalse();

        callbackLifetime.Cancel();
        await Assert.That(callbackTask).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task CancelledStageReadyCallbackIsNotStarted()
    {
        using var callbackLifetime = new CancellationTokenSource();
        callbackLifetime.Cancel();
        bool invoked = false;

        Task callbackTask = ViewerHostInteraction.RunStageReadyCallbackAsync(
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            callbackLifetime.Token);

        await Assert.That(callbackTask).Throws<OperationCanceledException>();
        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task StageReadyCallbackFaultIsObservableAtTheHostBoundary()
    {
        Task callbackTask = ViewerHostInteraction.RunStageReadyCallbackAsync(
            _ => Task.FromException(new InvalidOperationException("host failed")),
            CancellationToken.None);

        await Assert.That(callbackTask).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SyntheticViewportClickDispatchesHostPickForResolvedPrim()
    {
        StageRenderState state = CreateViewportState();
        RenderPickRequest? observedRequest = null;
        var backend = new DelegatePickingBackend((request, _) =>
        {
            observedRequest = request;
            return ValueTask.FromResult(RenderPickResult.Hit(
                request,
                state.Revision,
                sceneRevision: 7,
                new SelectionItem("/World/Robot/Target"),
                backendKind: RenderBackendKind.Storm));
        });
        using ViewerPickOperationQueue queue = CreateQueue(state, backend, sceneRevision: 7);
        TaskCompletionSource<ViewerPickEventArgs> picked = CreateCompletion<ViewerPickEventArgs>();

        bool mapped = ViewerHostInteraction.TryMapViewportClick(
            logicalX: 10.25,
            logicalY: 5.75,
            new ViewerLogicalContentBounds(0, 0, 100, 50),
            renderScaling: 2,
            state.Viewport,
            out ViewerPhysicalPixel pixel);
        RenderPickResult result = await ViewerHostInteraction.PickAndDispatchAsync(
            pixel,
            RenderPickTarget.Primitive,
            queue.PickAsync,
            (args, _) =>
            {
                picked.SetResult(args);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        ViewerPickEventArgs callback = await picked.Task.WaitAsync(TestTimeout);

        await Assert.That(mapped).IsTrue();
        await Assert.That(pixel).IsEqualTo(new ViewerPhysicalPixel(20, 11));
        await Assert.That(observedRequest?.X).IsEqualTo(20);
        await Assert.That(observedRequest?.Y).IsEqualTo(11);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(callback.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(callback.PrimPath).IsEqualTo("/World/Robot/Target");
    }

    [Test]
    public async Task HostPickCallbackReceivesRetriedResultAfterStaleBackendReply()
    {
        StageRenderState first = CreateViewportState();
        StageRenderState second = first.AdvanceRevision();
        int calls = 0;
        ViewerPickBackendSnapshot? snapshot = null;
        DelegatePickingBackend? backend = null;
        backend = new DelegatePickingBackend((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                snapshot = new ViewerPickBackendSnapshot(
                    backend!,
                    new ViewerRenderedPickState(second, 12, RenderBackendKind.D3D12),
                    Generation: 1);
                return ValueTask.FromResult(RenderPickResult.Stale(
                    request,
                    second.Revision,
                    sceneRevision: 12));
            }
            return ValueTask.FromResult(RenderPickResult.Hit(
                request,
                second.Revision,
                sceneRevision: 12,
                new SelectionItem("/World/Robot/Retried"),
                backendKind: RenderBackendKind.D3D12));
        });
        snapshot = new ViewerPickBackendSnapshot(
            backend,
            new ViewerRenderedPickState(first, 11, RenderBackendKind.D3D12),
            Generation: 1);
        using var queue = new ViewerPickOperationQueue(
            () => snapshot,
            () => second,
            CancellationToken.None);
        TaskCompletionSource<ViewerPickEventArgs> picked = CreateCompletion<ViewerPickEventArgs>();

        RenderPickResult result = await ViewerHostInteraction.PickAndDispatchAsync(
            new ViewerPhysicalPixel(3, 4),
            RenderPickTarget.Primitive,
            queue.PickAsync,
            (args, _) =>
            {
                picked.SetResult(args);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        ViewerPickEventArgs callback = await picked.Task.WaitAsync(TestTimeout);

        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(queue.Statistics.StaleRetries).IsEqualTo(1);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(callback.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(callback.PrimPath).IsEqualTo("/World/Robot/Retried");
        await Assert.That(callback.StateRevision).IsEqualTo(second.Revision);
        await Assert.That(callback.SceneRevision).IsEqualTo(12UL);
    }

    [Test]
    public async Task AFixedHostPrimitiveTargetSurvivesEveryToolsMenuChange()
    {
        // The regression this pins: a host that explicitly asked for primitive
        // picks used to be indistinguishable from a host that asked for nothing,
        // so the one host that stated the default was the only one whose choice
        // the Tools menu could override.
        await Assert.That(ViewerPickTargetPolicy.ResolveHostRequestedTarget(
                followViewer: false,
                RenderPickTarget.Primitive,
                RenderPickTarget.Face))
            .IsEqualTo(RenderPickTarget.Primitive);
        await Assert.That(ViewerPickTargetPolicy.ResolveHostRequestedTarget(
                followViewer: false,
                RenderPickTarget.Primitive,
                RenderPickTarget.Edge))
            .IsEqualTo(RenderPickTarget.Primitive);
        await Assert.That(ViewerPickTargetPolicy.ResolveHostRequestedTarget(
                followViewer: false,
                RenderPickTarget.Primitive,
                RenderPickTarget.Point))
            .IsEqualTo(RenderPickTarget.Primitive);
        await Assert.That(ViewerPickTargetPolicy.ResolveHostRequestedTarget(
                followViewer: false,
                RenderPickTarget.Primitive,
                RenderPickTarget.Primitive))
            .IsEqualTo(RenderPickTarget.Primitive);
    }

    [Test]
    public async Task AFixedHostSubprimTargetSurvivesEveryToolsMenuChange()
    {
        foreach (RenderPickTarget fixedTarget in new[]
        {
            RenderPickTarget.Face,
            RenderPickTarget.Edge,
            RenderPickTarget.Point
        })
        {
            foreach (RenderPickTarget menuTarget in new[]
            {
                RenderPickTarget.Primitive,
                RenderPickTarget.Face,
                RenderPickTarget.Edge,
                RenderPickTarget.Point
            })
            {
                await Assert.That(ViewerPickTargetPolicy.ResolveHostRequestedTarget(
                        followViewer: false,
                        fixedTarget,
                        menuTarget))
                    .IsEqualTo(fixedTarget);
            }
        }
    }

    [Test]
    public async Task AHostThatOptsIntoFollowViewerFollowsTheToolsMenu()
    {
        foreach (RenderPickTarget menuTarget in new[]
        {
            RenderPickTarget.Primitive,
            RenderPickTarget.Face,
            RenderPickTarget.Edge,
            RenderPickTarget.Point
        })
        {
            await Assert.That(ViewerPickTargetPolicy.ResolveHostRequestedTarget(
                    followViewer: true,
                    RenderPickTarget.Primitive,
                    menuTarget))
                .IsEqualTo(menuTarget);
        }

        // The fixed-target mode is the default and the target property keeps its
        // long-standing non-nullable shape, so a host that configures nothing at
        // all keeps the behaviour it always had.
        await Assert.That(new ViewerHostOptions().PickTarget)
            .IsEqualTo(RenderPickTarget.Primitive);
        await Assert.That(new ViewerHostOptions().FollowViewerPickTarget).IsFalse();
        await Assert.That(
                new ViewerHostOptions { PickTarget = RenderPickTarget.Primitive }
                    .PickTarget)
            .IsEqualTo(RenderPickTarget.Primitive);
        await Assert.That(
                new ViewerHostOptions { FollowViewerPickTarget = true }
                    .FollowViewerPickTarget)
            .IsTrue();
    }

    [Test]
    public async Task HostPickCallbackCarriesTheRequestedTargetAndCompleteIdentity()
    {
        StageRenderState state = CreateViewportState();
        SelectionItem nested = SelectionItem.FromInstancerContext(
            "/World/Prototypes/Leaf",
            [
                new SelectionInstancerEntry("/World/Outer", 2),
                new SelectionInstancerEntry("/World/Outer/Inner", 5)
            ],
            elementIndex: 11,
            SelectionElementKind.Edge);
        var backend = new DelegatePickingBackend((request, _) =>
            ValueTask.FromResult(RenderPickResult.Hit(
                request,
                state.Revision,
                sceneRevision: null,
                nested,
                backendKind: RenderBackendKind.Vulkan)));
        using ViewerPickOperationQueue queue = CreateQueue(state, backend, sceneRevision: null);
        TaskCompletionSource<ViewerPickEventArgs> picked =
            CreateCompletion<ViewerPickEventArgs>();

        _ = await ViewerHostInteraction.PickAndDispatchAsync(
            new ViewerPhysicalPixel(3, 4),
            RenderPickTarget.Edge,
            queue.PickAsync,
            (args, _) =>
            {
                picked.SetResult(args);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        ViewerPickEventArgs callback = await picked.Task.WaitAsync(TestTimeout);

        await Assert.That(callback.RequestedTarget).IsEqualTo(RenderPickTarget.Edge);
        await Assert.That(callback.ElementKind).IsEqualTo(SelectionElementKind.Edge);
        await Assert.That(callback.Item).IsNotNull();

        // The complete ordered chain survives, outermost first, and the
        // flattened convenience properties report the innermost level.
        SelectionItem item = callback.Item!.Value;
        await Assert.That(item.InstancerContext.Count).IsEqualTo(2);
        await Assert.That(item.InstancerContext[0].InstancerPath)
            .IsEqualTo("/World/Outer");
        await Assert.That(item.InstancerContext[0].InstanceIndex).IsEqualTo(2);
        await Assert.That(item.InstancerContext[1].InstancerPath)
            .IsEqualTo("/World/Outer/Inner");
        await Assert.That(item.InstancerContext[1].InstanceIndex).IsEqualTo(5);
        await Assert.That(callback.InstancerPath).IsEqualTo("/World/Outer/Inner");
        await Assert.That(callback.InstanceIndex).IsEqualTo(5);
        await Assert.That(callback.ElementIndex).IsEqualTo(11);
        await Assert.That(callback.PrimPath).IsEqualTo("/World/Prototypes/Leaf");
    }

    [Test]
    public async Task HostPickCallbackReceivesMissWithNullPrimPath()
    {
        StageRenderState state = CreateViewportState();
        var backend = new DelegatePickingBackend((request, _) =>
            ValueTask.FromResult(RenderPickResult.Miss(
                request,
                state.Revision,
                sceneRevision: null)));
        using ViewerPickOperationQueue queue = CreateQueue(state, backend, sceneRevision: null);
        TaskCompletionSource<ViewerPickEventArgs> picked = CreateCompletion<ViewerPickEventArgs>();

        _ = await ViewerHostInteraction.PickAndDispatchAsync(
            new ViewerPhysicalPixel(1, 2),
            RenderPickTarget.Primitive,
            queue.PickAsync,
            (args, _) =>
            {
                picked.SetResult(args);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        ViewerPickEventArgs callback = await picked.Task.WaitAsync(TestTimeout);

        await Assert.That(callback.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(callback.PrimPath).IsNull();

        // A miss carries no identity at all, so a host cannot mistake a stale
        // item for a fresh one, but it still reports the target it asked for.
        await Assert.That(callback.Item).IsNull();
        await Assert.That(callback.ElementKind).IsEqualTo(SelectionElementKind.None);
        await Assert.That(callback.RequestedTarget)
            .IsEqualTo(RenderPickTarget.Primitive);
    }

    [Test]
    public async Task SelectionChangedSubtreeScopesPositiveAndNegativeNotifications()
    {
        TaskCompletionSource<ViewerSelectionChangedEventArgs> inside =
            CreateCompletion<ViewerSelectionChangedEventArgs>();
        bool insideDispatched = ViewerHostInteraction.DispatchSelectionCallback(
            new SelectionState(["/World/Robot/Target"]),
            "/World/Robot",
            (args, _) =>
            {
                inside.SetResult(args);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        bool outsideDispatched = ViewerHostInteraction.DispatchSelectionCallback(
            new SelectionState(["/World/Other"]),
            "/World/Robot",
            (_, _) => throw new InvalidOperationException("Outside selection notified."),
            CancellationToken.None);
        bool clearDispatched = ViewerHostInteraction.ShouldNotifySelection(
            SelectionState.Empty,
            "/World/Robot");
        ViewerSelectionChangedEventArgs insideArgs =
            await inside.Task.WaitAsync(TestTimeout);

        await Assert.That(insideDispatched).IsTrue();
        await Assert.That(insideArgs.PrimPaths).IsEquivalentTo(["/World/Robot/Target"]);
        await Assert.That(outsideDispatched).IsFalse();
        await Assert.That(clearDispatched).IsTrue();
    }

    [Test]
    public async Task HostPickCallbackIsDispatchedWithoutAwaitingTheCallback()
    {
        StageRenderState state = CreateViewportState();
        var backend = new DelegatePickingBackend((request, _) =>
            ValueTask.FromResult(RenderPickResult.Hit(
                request,
                state.Revision,
                sceneRevision: null,
                new SelectionItem("/World/Robot/BlockingCallback"),
                backendKind: RenderBackendKind.Vulkan)));
        using ViewerPickOperationQueue queue = CreateQueue(state, backend, sceneRevision: null);
        TaskCompletionSource callbackEntered = CreateCompletion();
        TaskCompletionSource releaseCallback = CreateCompletion();

        Task<RenderPickResult> pickTask = ViewerHostInteraction.PickAndDispatchAsync(
            new ViewerPhysicalPixel(6, 7),
            RenderPickTarget.Primitive,
            queue.PickAsync,
            async (_, _) =>
            {
                callbackEntered.SetResult();
                await releaseCallback.Task;
            },
            CancellationToken.None).AsTask();
        Task completed = await Task.WhenAny(pickTask, Task.Delay(TestTimeout));

        await Assert.That(completed).IsSameReferenceAs(pickTask);
        await Assert.That(pickTask.Result.Status).IsEqualTo(RenderPickStatus.Hit);
        await callbackEntered.Task.WaitAsync(TestTimeout);
        releaseCallback.SetResult();
    }

    private static TimeSpan TestTimeout => TimeSpan.FromSeconds(5);

    private static StageRenderState CreateViewportState() =>
        StageRenderState
            .Create(new StageIdentity("stage.usda"))
            .WithViewport(new ViewportDimensions(200, 100));

    private static ViewerPickOperationQueue CreateQueue(
        StageRenderState state,
        IRenderPickingBackend backend,
        ulong? sceneRevision)
    {
        var snapshot = new ViewerPickBackendSnapshot(
            backend,
            new ViewerRenderedPickState(state, sceneRevision, RenderBackendKind.Storm),
            Generation: 1);
        return new ViewerPickOperationQueue(
            () => snapshot,
            () => state,
            CancellationToken.None);
    }

    private static TaskCompletionSource CreateCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> CreateCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            _ = callback;
            _ = state;
        }
    }

    private sealed class DelegatePickingBackend(
        Func<RenderPickRequest, CancellationToken, ValueTask<RenderPickResult>> handler)
        : IRenderPickingBackend
    {
        public ValueTask<RenderPickResult> PickAsync(
            RenderPickRequest request,
            CancellationToken cancellationToken = default) =>
            handler(request, cancellationToken);
    }
}
