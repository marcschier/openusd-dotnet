// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPickingTests
{
    [Test]
    public async Task PickingSmokeUsesBoundedCenterAnchoredSampleGrid()
    {
        double[] fractions = ViewerPickingSmokeContract.SampleFractions;

        await Assert.That(fractions.SequenceEqual([0.05, 0.50, 0.95])).IsTrue();
        await Assert.That(fractions.Length * fractions.Length).IsEqualTo(9);
    }

    [Test]
    public async Task LogicalCoordinatesMapToExactTopLeftPhysicalPixels()
    {
        var bounds = new ViewerLogicalContentBounds(5, 7, 100, 80);

        await Assert.That(ViewerPickPixelMapper.TryMap(
            15.9,
            17.9,
            bounds,
            1,
            new ViewportDimensions(100, 80),
            out ViewerPhysicalPixel one)).IsTrue();
        await Assert.That(one).IsEqualTo(new ViewerPhysicalPixel(10, 10));

        await Assert.That(ViewerPickPixelMapper.TryMap(
            15.9,
            17.9,
            bounds,
            1.5,
            new ViewportDimensions(150, 120),
            out ViewerPhysicalPixel oneAndHalf)).IsTrue();
        await Assert.That(oneAndHalf).IsEqualTo(new ViewerPhysicalPixel(16, 16));

        await Assert.That(ViewerPickPixelMapper.TryMap(
            15.9,
            17.9,
            bounds,
            2,
            new ViewportDimensions(200, 160),
            out ViewerPhysicalPixel two)).IsTrue();
        await Assert.That(two).IsEqualTo(new ViewerPhysicalPixel(21, 21));

        await Assert.That(ViewerPickPixelMapper.TryMap(
            105,
            20,
            bounds,
            1,
            new ViewportDimensions(100, 80),
            out _)).IsFalse();
    }

    [Test]
    public async Task PlainLeftClickIsReservedWhileAltAndDragsAreRejected()
    {
        await Assert.That(ViewerPickGestureClassifier.CanStart(
            KeyModifiers.None,
            ViewerPointerButtons.Left)).IsTrue();
        await Assert.That(ViewerPickGestureClassifier.CanStart(
            KeyModifiers.Alt,
            ViewerPointerButtons.Left)).IsFalse();
        await Assert.That(ViewerPickGestureClassifier.CanStart(
            KeyModifiers.None,
            ViewerPointerButtons.Middle)).IsFalse();
        await Assert.That(ViewerPickGestureClassifier.CanStart(
            KeyModifiers.None,
            ViewerPointerButtons.Left | ViewerPointerButtons.Right)).IsFalse();
        await Assert.That(ViewerPickGestureClassifier.IsDrag(3.9, 0, 1)).IsFalse();
        await Assert.That(ViewerPickGestureClassifier.IsDrag(4, 0, 1)).IsTrue();
        await Assert.That(ViewerPickGestureClassifier.IsDrag(2, 0, 2)).IsTrue();
    }

    [Test]
    public async Task NativeStormPollingRecognizesPlainClickButNotAltOrDrag()
    {
        var tracker = new ViewerStormPickInputTracker();
        _ = tracker.TryUpdate(Navigation(1, 10, 20), out _);
        _ = tracker.TryUpdate(
            Navigation(2, 10, 20, OpenUsdStormPointerButtons.Left),
            out _);
        bool clicked = tracker.TryUpdate(
            Navigation(3, 11, 21),
            out ViewerPhysicalPixel pixel);

        tracker.Reset();
        _ = tracker.TryUpdate(Navigation(4, 10, 20), out _);
        _ = tracker.TryUpdate(
            Navigation(
                5,
                10,
                20,
                OpenUsdStormPointerButtons.Left,
                OpenUsdStormInputModifiers.Alt),
            out _);
        bool altClicked = tracker.TryUpdate(Navigation(6, 10, 20), out _);

        tracker.Reset();
        _ = tracker.TryUpdate(Navigation(7, 10, 20), out _);
        _ = tracker.TryUpdate(
            Navigation(8, 10, 20, OpenUsdStormPointerButtons.Left),
            out _);
        _ = tracker.TryUpdate(
            Navigation(9, 20, 20, OpenUsdStormPointerButtons.Left),
            out _);
        bool dragClicked = tracker.TryUpdate(Navigation(10, 20, 20), out _);

        tracker.Reset();
        _ = tracker.TryUpdate(
            Navigation(
                11,
                30,
                40,
                OpenUsdStormPointerButtons.Left,
                inside: false),
            out _);
        bool midPressBaselineClick = tracker.TryUpdate(
            Navigation(12, 30, 40),
            out ViewerPhysicalPixel midPressPixel);

        await Assert.That(clicked).IsTrue();
        await Assert.That(pixel).IsEqualTo(new ViewerPhysicalPixel(11, 21));
        await Assert.That(altClicked).IsFalse();
        await Assert.That(dragClicked).IsFalse();
        await Assert.That(midPressBaselineClick).IsTrue();
        await Assert.That(midPressPixel).IsEqualTo(new ViewerPhysicalPixel(30, 40));
    }

    [Test]
    public async Task MappingAndGestureClassificationAllocateNothingAfterWarmup()
    {
        var bounds = new ViewerLogicalContentBounds(0, 0, 100, 80);
        var viewport = new ViewportDimensions(150, 120);
        _ = ViewerPickPixelMapper.TryMap(10, 10, bounds, 1.5, viewport, out _);
        _ = ViewerPickGestureClassifier.IsDrag(1, 1, 1.5);

        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            if (ViewerPickPixelMapper.TryMap(
                    index % 99,
                    index % 79,
                    bounds,
                    1.5,
                    viewport,
                    out ViewerPhysicalPixel pixel))
            {
                checksum += pixel.X + pixel.Y;
            }
            checksum += ViewerPickGestureClassifier.IsDrag(1, 1, 1.5) ? 1 : 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(checksum).IsGreaterThan(0);
    }

    [Test]
    public async Task QueueReturnsFullHitIdentityAndHandlesMissAndUnsupported()
    {
        StageRenderState state = CreateState(revisionAdvances: 1);
        var fullItem = new SelectionItem(
            "/World/Prototype",
            "/World/Instances",
            instanceIndex: 3,
            elementIndex: 7);
        var backend = new DelegatePickingBackend(
            (request, _) => ValueTask.FromResult(RenderPickResult.Hit(
                request,
                state.Revision,
                sceneRevision: 9,
                fullItem,
                backendKind: RenderBackendKind.Storm)));
        ViewerPickBackendSnapshot? snapshot = new(
            backend,
            new ViewerRenderedPickState(state, 9, RenderBackendKind.Storm),
            Generation: 1);
        using var queue = new ViewerPickOperationQueue(
            () => snapshot,
            () => state,
            CancellationToken.None);

        RenderPickResult hit = await queue.PickAsync(new ViewerPhysicalPixel(4, 5));
        backend.Handler = (request, _) => ValueTask.FromResult(RenderPickResult.Miss(
            request,
            state.Revision,
            sceneRevision: 9));
        RenderPickResult miss = await queue.PickAsync(new ViewerPhysicalPixel(4, 5));
        snapshot = null;
        RenderPickResult unsupported =
            await queue.PickAsync(new ViewerPhysicalPixel(4, 5));

        await Assert.That(hit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(hit.Item).IsEqualTo(fullItem);
        await Assert.That(hit.InstancerPath).IsEqualTo("/World/Instances");
        await Assert.That(hit.InstanceIndex).IsEqualTo(3);
        await Assert.That(hit.ElementIndex).IsEqualTo(7);
        await Assert.That(miss.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(miss.Item).IsNull();
        await Assert.That(unsupported.Status).IsEqualTo(RenderPickStatus.Unsupported);
        await Assert.That(unsupported.Item).IsNull();
    }

    [Test]
    public async Task StaleResultRetriesOnceWithNewestRenderedState()
    {
        StageRenderState first = CreateState(revisionAdvances: 1);
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
                    new ViewerRenderedPickState(second, 12, RenderBackendKind.Vulkan),
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
                new SelectionItem("/World/Cube"),
                backendKind: RenderBackendKind.Vulkan));
        });
        snapshot = new ViewerPickBackendSnapshot(
            backend!,
            new ViewerRenderedPickState(first, 11, RenderBackendKind.Vulkan),
            Generation: 1);
        using var queue = new ViewerPickOperationQueue(
            () => snapshot,
            () => second,
            CancellationToken.None);

        RenderPickResult result =
            await queue.PickAsync(new ViewerPhysicalPixel(10, 12));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.RequestedStateRevision).IsEqualTo(second.Revision);
        await Assert.That(result.RequestedSceneRevision).IsEqualTo(12UL);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(queue.Statistics.StaleRetries).IsEqualTo(1);
    }

    [Test]
    public async Task StaleAfterRetrySurfacesWithoutAThirdBackendCall()
    {
        StageRenderState state = CreateState(revisionAdvances: 1);
        int calls = 0;
        var backend = new DelegatePickingBackend((request, _) =>
        {
            calls++;
            return ValueTask.FromResult(RenderPickResult.Stale(
                request,
                state.Revision,
                sceneRevision: 4,
                RenderPickStaleReason.ContextGeneration));
        });
        using var queue = new ViewerPickOperationQueue(
            () => new ViewerPickBackendSnapshot(
                backend,
                new ViewerRenderedPickState(state, 4, RenderBackendKind.D3D12),
                Generation: 1),
            () => state,
            CancellationToken.None);

        RenderPickResult result =
            await queue.PickAsync(new ViewerPhysicalPixel(1, 1));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(queue.Statistics.StaleRetries).IsEqualTo(1);
    }

    [Test]
    public async Task LatestRequestCancelsAnAlreadyAdmittedPick()
    {
        StageRenderState state = CreateState(revisionAdvances: 1);
        var firstAdmitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var backend = new DelegatePickingBackend(async (request, cancellationToken) =>
        {
            calls++;
            if (calls == 1)
            {
                firstAdmitted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return RenderPickResult.Miss(request, state.Revision, sceneRevision: 2);
        });
        using var queue = new ViewerPickOperationQueue(
            () => new ViewerPickBackendSnapshot(
                backend,
                new ViewerRenderedPickState(state, 2, RenderBackendKind.Storm),
                Generation: 1),
            () => state,
            CancellationToken.None);

        Task<RenderPickResult> first =
            queue.PickAsync(new ViewerPhysicalPixel(1, 1)).AsTask();
        await firstAdmitted.Task;
        Task<RenderPickResult> second =
            queue.PickAsync(new ViewerPhysicalPixel(2, 2)).AsTask();

        await Assert.That(first).Throws<OperationCanceledException>();
        RenderPickResult latest = await second;

        await Assert.That(latest.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(latest.Request.X).IsEqualTo(2);
        await Assert.That(queue.Statistics.SupersededRequests).IsEqualTo(1);
    }

    [Test]
    public async Task DisposingQueueCancelsAnAlreadyAdmittedPick()
    {
        StageRenderState state = CreateState(revisionAdvances: 1);
        var admitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new DelegatePickingBackend(async (request, cancellationToken) =>
        {
            admitted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return RenderPickResult.Miss(request, state.Revision, sceneRevision: 2);
        });
        var queue = new ViewerPickOperationQueue(
            () => new ViewerPickBackendSnapshot(
                backend,
                new ViewerRenderedPickState(state, 2, RenderBackendKind.Storm),
                Generation: 1),
            () => state,
            CancellationToken.None);
        Task<RenderPickResult> pick =
            queue.PickAsync(new ViewerPhysicalPixel(1, 1)).AsTask();
        await admitted.Task;

        queue.Dispose();

        await Assert.That(pick).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ViewerSelectionRetainsInstanceAndSubprimIdentity()
    {
        var selection = new ViewerSelectionState();
        var item = new SelectionItem(
            "/World/Prototype",
            "/World/Instances",
            instanceIndex: 5,
            elementIndex: 9);

        bool changed = selection.TrySetItem(item, out SelectionState rendered);
        selection.Synchronize(rendered);

        await Assert.That(changed).IsTrue();
        await Assert.That(selection.Item).IsEqualTo(item);
        await Assert.That(selection.PrimPath).IsEqualTo(item.PrimPath);
        await Assert.That(rendered.Items[0]).IsEqualTo(item);
    }

    [Test]
    public async Task ViewerBackendsAdvertisePickingAndUnavailableSessionReturnsUnsupported()
    {
        var host = new NonPickingHost();
        foreach (RenderBackendKind kind in Enum.GetValues<RenderBackendKind>())
        {
            await using var backend = new ViewerRenderBackend(kind, host);
            StageRenderState state = CreateState(revisionAdvances: 1);
            _ = await backend.InitializeAsync(state);
            _ = await backend.RenderAsync();
            RenderPickResult result = await backend.PickAsync(new RenderPickRequest(
                1,
                1,
                state.Viewport,
                state.Revision));

            await Assert.That(backend.Capabilities.Supports(
                RenderBackendCapability.Picking)).IsTrue();
            await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Unsupported);
        }
    }

    [Test]
    public async Task SilkOutlineDiagnosticsAreEmptyOnlyAfterVisibleRendering()
    {
        StageRenderState selected = CreateState(revisionAdvances: 1).WithSelection(
            new SelectionState(["/World/Cube"]));
        RenderBackendDiagnostics rendered = ViewerSilkSelectionDiagnostics.Create(
            selected,
            RenderBackendKind.D3D12,
            OutlineDiagnostics(SilkSelectionOutlineStatus.Rendered));
        RenderBackendDiagnostics unavailable = ViewerSilkSelectionDiagnostics.Create(
            selected,
            RenderBackendKind.Vulkan,
            OutlineDiagnostics(SilkSelectionOutlineStatus.DepthSamplingUnsupported));

        await Assert.That(rendered.Entries).IsEmpty();
        await Assert.That(unavailable.Entries).HasSingleItem();
        await Assert.That(unavailable.Entries[0].Code)
            .IsEqualTo("VIEWER_SILK_SELECTION_DEPTH_UNAVAILABLE");
    }

    private static StageRenderState CreateState(int revisionAdvances)
    {
        StageRenderState state = StageRenderState
            .Create(new StageIdentity("stage.usda"))
            .WithViewport(new ViewportDimensions(64, 48));
        for (int index = 0; index < revisionAdvances; index++)
        {
            state = state.AdvanceRevision();
        }
        return state;
    }

    private static SilkSelectionOutlineDiagnostics OutlineDiagnostics(
        SilkSelectionOutlineStatus status) =>
        new(
            status,
            SelectionRevision: 1,
            SelectionItemCount: 1,
            ResolvedMeshCount: status == SilkSelectionOutlineStatus.Rendered ? 1 : 0,
            MissingPathCount: 0,
            MaskPasses: status == SilkSelectionOutlineStatus.Rendered ? 1UL : 0,
            OutlinePasses: status == SilkSelectionOutlineStatus.Rendered ? 1UL : 0,
            SelectedDraws: status == SilkSelectionOutlineStatus.Rendered ? 1UL : 0,
            PipelineCreations: 2,
            TargetCreations: 1,
            BindingCreations: 1,
            ParameterUploads: 1,
            DeviceInvalidations: 0,
            UnsupportedXRayRequests: 0);

    private static OpenUsdStormNavigationInput Navigation(
        ulong sequence,
        int x,
        int y,
        OpenUsdStormPointerButtons buttons = OpenUsdStormPointerButtons.None,
        OpenUsdStormInputModifiers modifiers = OpenUsdStormInputModifiers.None,
        bool inside = true) =>
        new(
            sequence,
            x,
            y,
            buttons,
            modifiers,
            CumulativeWheelDelta: 0,
            FrameSelectedPressCount: 0,
            ResetAutomaticPressCount: 0,
            ToggleProjectionPressCount: 0,
            OpenUsdStormNavigationState.Focused |
                (inside ? OpenUsdStormNavigationState.Inside : 0));

    private sealed class DelegatePickingBackend(
        Func<RenderPickRequest, CancellationToken, ValueTask<RenderPickResult>> handler)
        : IRenderPickingBackend
    {
        internal Func<
            RenderPickRequest,
            CancellationToken,
            ValueTask<RenderPickResult>> Handler
        { get; set; } = handler;

        public ValueTask<RenderPickResult> PickAsync(
            RenderPickRequest request,
            CancellationToken cancellationToken = default) =>
            Handler(request, cancellationToken);
    }

    private sealed class NonPickingHost : IViewerRenderBackendHost
    {
        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            RenderBackendKind kind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RenderBackendProbeResult.Available());

        public ValueTask<IViewerRenderBackendSession> AttachAsync(
            RenderBackendKind kind,
            StageRenderState initialState,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IViewerRenderBackendSession>(
                new NonPickingSession(initialState));
    }

    private sealed class NonPickingSession(StageRenderState state)
        : IViewerRenderBackendSession
    {
        public RenderBackendDiagnostics Diagnostics => RenderBackendDiagnostics.Empty;

        public StageRenderState CurrentState { get; private set; } = state;

        public ValueTask ActivateAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DeactivateAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask UpdateStateAsync(
            StageRenderState state,
            CancellationToken cancellationToken)
        {
            CurrentState = state;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(
            ViewportDimensions viewport,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<RenderFrameResult> RenderAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RenderFrameResult.Rendered(
                CurrentState.Revision,
                RenderFrameStatistics.Empty));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
