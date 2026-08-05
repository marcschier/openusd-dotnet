// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerHostContractTests
{
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
