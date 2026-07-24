// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class CompositionViewportTests
{
    [Test]
    public async Task PixelSizeUsesDpiAndRejectsOverflow()
    {
        ViewportDimensions scaled = ViewportPixelMath.ToPixels(100.25, 50.1, 1.5);
        ViewportDimensions empty = ViewportPixelMath.ToPixels(0, 50, 2);

        await Assert.That(scaled).IsEqualTo(new ViewportDimensions(151, 76));
        await Assert.That(empty).IsEqualTo(ViewportDimensions.Empty);
        await Assert.That(() => ViewportPixelMath.ToPixels(double.MaxValue, 1, 2))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ViewportPixelMath.ToPixels(int.MaxValue, 1, 2))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RingBackpressureReturnsOneReleaseRetryAndImportsOnce()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface { HoldPresentations = true };
        var presenter = new FakePresenter();
        await using var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));

        Task<CompositionPresentOutcome>[] active =
        [
            session.PresentNextFrameAsync().AsTask(),
            session.PresentNextFrameAsync().AsTask(),
            session.PresentNextFrameAsync().AsTask()
        ];
        await WaitUntilAsync(() => surface.PresentCount == 3);
        CompositionPresentOutcome backpressured = await session.PresentNextFrameAsync();

        await Assert.That(backpressured.Result)
            .IsEqualTo(CompositionPresentResult.Backpressured);
        await Assert.That(backpressured.RetryAvailable).IsNotNull();
        await Assert.That(backpressured.RetryAvailable!.IsCompleted).IsFalse();
        surface.ReleaseOnePresentation();
        await backpressured.RetryAvailable;
        surface.ReleasePresentations();
        await Task.WhenAll(active);
        await session.PresentNextFrameAsync();
        await session.PresentNextFrameAsync();
        await session.PresentNextFrameAsync();
        await Assert.That(surface.ImportCounts.Values.Sum()).IsEqualTo(3);
        await Assert.That(surface.ImportCounts.Values.All(count => count == 1)).IsTrue();
    }

    [Test]
    public async Task ImportExceptionBecomesLostWhenSurfaceReportsLoss()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface
        {
            ThrowOnImport = true,
            MarkSurfaceLostOnImportFailure = true
        };
        var presenter = new FakePresenter();
        await using var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));

        CompositionPresentOutcome outcome = await session.PresentNextFrameAsync();

        await Assert.That(outcome.Result).IsEqualTo(CompositionPresentResult.Lost);
        await Assert.That(session.State).IsEqualTo(CompositionViewportState.Lost);
        await Assert.That(presenter.RenderCount).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateExceptionBecomesLostWhenImportedResourceReportsLoss()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface
        {
            ThrowOnPresent = true,
            MarkImportedLostOnPresentFailure = true
        };
        var presenter = new FakePresenter();
        await using var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));

        CompositionPresentOutcome outcome = await session.PresentNextFrameAsync();

        await Assert.That(outcome.Result).IsEqualTo(CompositionPresentResult.Lost);
        await Assert.That(session.State).IsEqualTo(CompositionViewportState.Lost);
        await Assert.That(surface.IsLost).IsFalse();
        await Assert.That(surface.ImportedFrames.Single().IsLost).IsTrue();
    }

    [Test]
    public async Task UnrelatedImportAndUpdateExceptionsRemainVisible()
    {
        var dispatcher = new FakeDispatcher();
        var importSurface = new FakeSurface { ThrowOnImport = true };
        var importSession = new CompositionViewportSession(
            new FakePresenter(),
            importSurface,
            dispatcher);
        await importSession.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));

        Exception? importFailure = await CaptureFailureAsync(
            importSession.PresentNextFrameAsync().AsTask());
        await importSession.DisposeAsync();

        var updateSurface = new FakeSurface { ThrowOnPresent = true };
        var updateSession = new CompositionViewportSession(
            new FakePresenter(),
            updateSurface,
            dispatcher);
        await updateSession.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));
        Exception? updateFailure = await CaptureFailureAsync(
            updateSession.PresentNextFrameAsync().AsTask());
        await updateSession.DisposeAsync();

        await Assert.That(importFailure).IsTypeOf<InvalidOperationException>();
        await Assert.That(updateFailure).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task SlowRenderSerializesResizeGenerationAndDisposal()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface();
        var presenter = new FakePresenter { HoldRenders = true };
        var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));

        Task<CompositionPresentOutcome> presentation =
            session.PresentNextFrameAsync().AsTask();
        await WaitUntilAsync(() => presenter.RenderCount == 1);
        Task resize = session.ResizeAsync(new ViewportDimensions(800, 600)).AsTask();
        Task disposal = session.DisposeAsync().AsTask();
        await Task.Delay(25);

        await Assert.That(presenter.Generations).Count().IsEqualTo(1);
        await Assert.That(presenter.IsDisposed).IsFalse();
        presenter.ReleaseRenders();
        await presentation;
        await resize;
        await disposal;

        await Assert.That(presenter.Generations).Count().IsEqualTo(2);
        await Assert.That(presenter.IsDisposed).IsTrue();
        await Assert.That(presenter.MaximumConcurrentCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ResizeRetiresGenerationAfterConsumption()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface { HoldPresentations = true };
        var presenter = new FakePresenter();
        await using var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(320, 200));
        FakeGeneration stale = presenter.Generations.Single();
        Task<CompositionPresentOutcome> active = session.PresentNextFrameAsync().AsTask();
        await WaitUntilAsync(() => surface.PresentCount == 1);

        await session.ResizeAsync(new ViewportDimensions(800, 600));

        await Assert.That(stale.IsDisposed).IsFalse();
        surface.ReleasePresentations();
        await active;
        await WaitUntilAsync(() => stale.IsDisposed);
        CompositionViewportSessionStatistics statistics = session.GetStatistics();
        await Assert.That(presenter.Generations[1].Size)
            .IsEqualTo(new ViewportDimensions(800, 600));
        await Assert.That(statistics.CurrentGenerationId).IsEqualTo(2);
        await Assert.That(statistics.SurfaceUpdateStartedCount).IsEqualTo(1);
        await Assert.That(statistics.SurfaceUpdateCompletedCount).IsEqualTo(1);
        await Assert.That(statistics.GenerationRetirementStartedCount).IsEqualTo(1);
        await Assert.That(statistics.GenerationRetirementCompletedCount).IsEqualTo(1);
        await Assert.That(statistics.LastRetiredGenerationId).IsEqualTo(1);
        await Assert.That(statistics.ImportedFrameDisposalCount).IsEqualTo(1);
        await Assert.That(statistics.StaleImportedFrameReuseCount).IsEqualTo(0);
    }

    [Test]
    public async Task AttachedActiveDisposalWaitsAndSerializesPresenterDispose()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface { HoldPresentations = true };
        var presenter = new FakePresenter();
        var session = new CompositionViewportSession(presenter, surface, dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));
        Task<CompositionPresentOutcome> presentation =
            session.PresentNextFrameAsync().AsTask();
        await WaitUntilAsync(() => surface.PresentCount == 1);

        Task disposal = session.DisposeAsync().AsTask();
        await Task.Delay(50);

        await Assert.That(disposal.IsCompleted).IsFalse();
        surface.ReleasePresentations();
        await presentation;
        await disposal;
        await Assert.That(presenter.IsDisposed).IsTrue();
        await Assert.That(presenter.MaximumConcurrentCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentSessionDisposalReturnsOneIncompleteTask()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface { HoldDisposal = true };
        var presenter = new FakePresenter();
        var session = new CompositionViewportSession(presenter, surface, dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));

        Task first = session.DisposeAsync().AsTask();
        await surface.DisposalStarted.Task;
        Task second = session.DisposeAsync().AsTask();

        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(second.IsCompleted).IsFalse();
        surface.ReleaseDisposal();
        await Task.WhenAll(first, second);
        await Assert.That(presenter.IsDisposed).IsTrue();
    }

    [Test]
    public async Task GenerationDisposalAttemptsEveryImportedSlotBeforePresenterGeneration()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface { FailImportedFrameDisposal = true };
        var presenter = new FakePresenter();
        var session = new CompositionViewportSession(presenter, surface, dispatcher);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(640, 480));
        await session.PresentNextFrameAsync();
        await session.PresentNextFrameAsync();
        await session.PresentNextFrameAsync();

        Exception? failure = await CaptureFailureAsync(session.DisposeAsync().AsTask());

        await Assert.That(failure).IsTypeOf<AggregateException>();
        await Assert.That(surface.ImportedFrames).Count().IsEqualTo(3);
        await Assert.That(surface.ImportedFrames.All(frame => frame.DisposeCount == 1)).IsTrue();
        await Assert.That(presenter.Generations.Single().IsDisposed).IsTrue();
        await Assert.That(surface.IsDisposed).IsTrue();
        await Assert.That(presenter.IsDisposed).IsTrue();
    }

    [Test]
    public async Task PumpCoalescesRequestsAndRetriesBackpressureOnce()
    {
        var failures = new List<Exception>();
        var runner = new ObservedTaskRunner((_, exception) => failures.Add(exception));
        var retry = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var pump = new CompositionPresentationPump(
            _ =>
            {
                int call = Interlocked.Increment(ref calls);
                return ValueTask.FromResult(
                    call == 1
                        ? new CompositionPresentOutcome(
                            CompositionPresentResult.Backpressured,
                            false,
                            retry.Task)
                        : new CompositionPresentOutcome(
                            CompositionPresentResult.Idle,
                            false));
            },
            runner);

        pump.Request();
        pump.Request();
        pump.Request();
        await WaitUntilAsync(() => calls == 1);
        retry.SetResult();
        await pump.WaitForIdleAsync();

        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task TaskRunnerObservesAndReportsBackgroundFailure()
    {
        string? operation = null;
        Exception? reported = null;
        var runner = new ObservedTaskRunner((value, exception) =>
        {
            operation = value;
            reported = exception;
        });

        await runner.Run(
            "failing operation",
            () => Task.FromException(new InvalidOperationException("boom")));
        await runner.DrainAsync();

        await Assert.That(operation).IsEqualTo("failing operation");
        await Assert.That(reported).IsTypeOf<InvalidOperationException>();
        await Assert.That(runner.Failures).Count().IsEqualTo(1);
    }

    [Test]
    public async Task PersistentLossRecoveryIsBoundedAndBackedOff()
    {
        var delays = new List<TimeSpan>();
        var recovery = new BoundedCompositionRecovery(
            maxAttempts: 3,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });
        int recoveries = 0;

        for (int index = 0; index < 5; index++)
        {
            _ = await recovery.TryRecoverAsync(
                _ =>
                {
                    recoveries++;
                    return Task.FromResult(true);
                },
                CancellationToken.None);
        }

        await Assert.That(recoveries).IsEqualTo(3);
        await Assert.That(recovery.IsExhausted).IsTrue();
        await Assert.That(delays)
            .IsEquivalentTo(
                [
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(200)
                ]);
    }

    [Test]
    public async Task WaylandStormUsesXWaylandWhenNativeGlfSupportIsUnavailable()
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = "wayland",
            ["WAYLAND_DISPLAY"] = "wayland-0",
            ["DISPLAY"] = ":1"
        };

        ViewerPlatformDecision decision = ViewerPlatformSelection.Decide(
            platformOverride: null,
            isLinux: true,
            stormRequested: true,
            nativeWaylandStormSupported: false,
            name => environment.GetValueOrDefault(name));

        await Assert.That(decision.UseWayland).IsFalse();
        await Assert.That(decision.UsesXWaylandFallback).IsTrue();
        await Assert.That(decision.Display).IsEqualTo(":1");
        await Assert.That(decision.WaylandDisplay).IsEqualTo("wayland-0");
    }

    [Test]
    public async Task WaylandStormRequiresDisplayWhenNativeGlfSupportIsUnavailable()
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = "wayland",
            ["WAYLAND_DISPLAY"] = "wayland-0"
        };

        ViewerPlatformDecision decision = ViewerPlatformSelection.Decide(
            platformOverride: "linux-wayland",
            isLinux: true,
            stormRequested: true,
            nativeWaylandStormSupported: false,
            name => environment.GetValueOrDefault(name));

        await Assert.That(decision.FailureReason).IsNotNull();
        await Assert.That(decision.FailureReason).Contains("XWayland DISPLAY");
    }

    [Test]
    public async Task NativeWaylandStormSupportStillKeepsFixedXWaylandShell()
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = "wayland",
            ["WAYLAND_DISPLAY"] = "wayland-0",
            ["DISPLAY"] = ":1"
        };

        ViewerPlatformDecision decision = ViewerPlatformSelection.Decide(
            platformOverride: "linux-wayland",
            isLinux: true,
            stormRequested: true,
            nativeWaylandStormSupported: true,
            name => environment.GetValueOrDefault(name));

        await Assert.That(decision.UseWayland).IsFalse();
        await Assert.That(decision.UsesXWaylandFallback).IsTrue();
        await Assert.That(decision.FailureReason).IsNull();
    }

    [Test]
    public async Task SilkAlsoUsesWholeShellXWaylandForRuntimeSwitching()
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = "wayland",
            ["WAYLAND_DISPLAY"] = "wayland-0",
            ["DISPLAY"] = ":1"
        };

        ViewerPlatformDecision decision = ViewerPlatformSelection.Decide(
            platformOverride: "linux-wayland",
            isLinux: true,
            stormRequested: false,
            nativeWaylandStormSupported: false,
            name => environment.GetValueOrDefault(name));

        await Assert.That(decision.UseWayland).IsFalse();
        await Assert.That(decision.UsesXWaylandFallback).IsTrue();
    }

    [Test]
    public async Task AttachedDisposalClearsVisualOnDispatcherBeforeTeardown()
    {
        var dispatcher = new FakeDispatcher();
        var order = new List<string>();

        await CompositionControlDisposal.DisposeAttachedAsync(
            dispatcher,
            () =>
            {
                order.Add("clear");
                if (!dispatcher.IsInvoking)
                {
                    throw new InvalidOperationException("Not dispatched.");
                }
            },
            () =>
            {
                order.Add("teardown");
                return ValueTask.CompletedTask;
            });

        await Assert.That(order).IsEquivalentTo(["clear", "teardown"]);
    }

    [Test]
    public async Task AttachedDisposalAggregatesVisualClearAndTeardownFailures()
    {
        var dispatcher = new FakeDispatcher();
        bool teardownAttempted = false;

        Exception? failure = await CaptureFailureAsync(
            CompositionControlDisposal.DisposeAttachedAsync(
                dispatcher,
                () => throw new InvalidOperationException("visual clear failed"),
                () =>
                {
                    teardownAttempted = true;
                    return ValueTask.FromException(
                        new InvalidOperationException("teardown failed"));
                }).AsTask());

        await Assert.That(teardownAttempted).IsTrue();
        await Assert.That(failure).IsTypeOf<AggregateException>();
        await Assert.That(((AggregateException)failure!).InnerExceptions).Count().IsEqualTo(2);
    }

    [Test]
    public async Task TimelineValuesCanAdvanceWhileAllocationImportIsReused()
    {
        var dispatcher = new FakeDispatcher();
        var surface = new FakeSurface();
        var presenter = new FakePresenter
        {
            RenderResults = new Queue<CompositionFrameRenderResult>(
            [
                PresentedTimeline(1, 2),
                PresentedTimeline(3, 4),
                PresentedTimeline(5, 6)
            ])
        };
        await using var session = new CompositionViewportSession(
            presenter,
            surface,
            dispatcher,
            frameCount: 2);
        await session.AttachAsync(CreateTarget(), new ViewportDimensions(320, 200));

        await session.PresentNextFrameAsync();
        await session.PresentNextFrameAsync();
        await session.PresentNextFrameAsync();

        await Assert.That(surface.Synchronizations[0].WaitValue).IsEqualTo(1ul);
        await Assert.That(surface.Synchronizations[1].WaitValue).IsEqualTo(3ul);
        await Assert.That(surface.Synchronizations[2].WaitValue).IsEqualTo(5ul);
        await Assert.That(surface.ImportCounts.Values.Sum()).IsEqualTo(2);
    }

    private static CompositionFrameRenderResult PresentedTimeline(ulong wait, ulong signal) =>
        new(
            CompositionFrameRenderStatus.Presented,
            ContinueRendering: false,
            CompositionFrameSynchronization.TimelineSemaphores(1, wait, 1, signal));

    private static CompositionPresentationTarget CreateTarget() =>
        new(["test-image"], ["test-semaphore"], deviceLuid: null, deviceUuid: null);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<Exception?> CaptureFailureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class FakeDispatcher : ICompositionUiDispatcher
    {
        public bool IsInvoking { get; private set; }

        public bool CheckAccess() => IsInvoking;

        public async ValueTask InvokeAsync(Func<ValueTask> action)
        {
            bool previous = IsInvoking;
            IsInvoking = true;
            try
            {
                await action();
            }
            finally
            {
                IsInvoking = previous;
            }
        }
    }

    private sealed class FakeSurface : ICompositionSurfaceBridge
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _pending = new();
        private readonly TaskCompletionSource _disposalRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentDictionary<long, int> ImportCounts { get; } = new();

        public List<FakeImportedFrame> ImportedFrames { get; } = [];

        public List<CompositionFrameSynchronization> Synchronizations { get; } = [];

        public TaskCompletionSource DisposalStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailImportedFrameDisposal { get; set; }

        public bool HoldDisposal { get; set; }

        public bool HoldPresentations { get; set; }

        public bool MarkImportedLostOnPresentFailure { get; set; }

        public bool MarkSurfaceLostOnImportFailure { get; set; }

        public bool ThrowOnImport { get; set; }

        public bool ThrowOnPresent { get; set; }

        public bool IsLost { get; set; }

        public bool IsDisposed { get; private set; }

        public int PresentCount { get; private set; }

        public ValueTask<IImportedCompositionFrame> ImportAsync(
            ICompositionPresentationFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnImport)
            {
                IsLost = MarkSurfaceLostOnImportFailure;
                throw new InvalidOperationException("surface import failed");
            }
            ImportCounts.AddOrUpdate(frame.AllocationId, 1, (_, count) => count + 1);
            var imported = new FakeImportedFrame(FailImportedFrameDisposal);
            ImportedFrames.Add(imported);
            return ValueTask.FromResult<IImportedCompositionFrame>(imported);
        }

        public Task PresentAsync(
            IImportedCompositionFrame importedFrame,
            CompositionFrameSynchronization synchronization)
        {
            PresentCount++;
            Synchronizations.Add(synchronization);
            if (ThrowOnPresent)
            {
                if (MarkImportedLostOnPresentFailure)
                {
                    ((FakeImportedFrame)importedFrame).IsLost = true;
                }
                throw new InvalidOperationException("surface update failed");
            }
            if (!HoldPresentations)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(completion);
            return completion.Task;
        }

        public async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            ReleasePresentations();
            DisposalStarted.TrySetResult();
            if (HoldDisposal)
            {
                await _disposalRelease.Task;
            }
        }

        public void ReleaseDisposal() => _disposalRelease.TrySetResult();

        public void ReleaseOnePresentation()
        {
            if (_pending.TryDequeue(out TaskCompletionSource? completion))
            {
                completion.TrySetResult();
            }
        }

        public void ReleasePresentations()
        {
            HoldPresentations = false;
            while (_pending.TryDequeue(out TaskCompletionSource? completion))
            {
                completion.TrySetResult();
            }
        }
    }

    private sealed class FakeImportedFrame(bool failDisposal) : IImportedCompositionFrame
    {
        public bool IsLost { get; set; }

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return failDisposal
                ? ValueTask.FromException(
                    new InvalidOperationException("imported frame disposal failed"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class FakePresenter : ICompositionViewportPresenter
    {
        private readonly SemaphoreSlim _renderRelease = new(0);
        private long _nextAllocation;
        private int _concurrentCalls;

        public List<FakeGeneration> Generations { get; } = [];

        public bool HoldRenders { get; set; }

        public int RenderCount { get; private set; }

        public int MaximumConcurrentCalls { get; private set; }

        public bool IsDisposed { get; private set; }

        public Queue<CompositionFrameRenderResult> RenderResults { get; set; } = [];

        public async ValueTask<CompositionPresenterProbeResult> ProbeAsync(
            CompositionPresentationTarget target,
            CancellationToken cancellationToken = default)
        {
            EnterCall();
            try
            {
                await Task.Yield();
                return CompositionPresenterProbeResult.Available();
            }
            finally
            {
                ExitCall();
            }
        }

        public ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
            ViewportDimensions size,
            int frameCount,
            CancellationToken cancellationToken = default)
        {
            EnterCall();
            try
            {
                var generation = new FakeGeneration(
                    size,
                    [.. Enumerable.Range(0, frameCount).Select(_ => new FakeFrame(++_nextAllocation, size))]);
                Generations.Add(generation);
                return ValueTask.FromResult<ICompositionPresentationGeneration>(generation);
            }
            finally
            {
                ExitCall();
            }
        }

        public async ValueTask<CompositionFrameRenderResult> RenderAsync(
            ICompositionPresentationFrame frame,
            CancellationToken cancellationToken = default)
        {
            EnterCall();
            try
            {
                RenderCount++;
                if (HoldRenders)
                {
                    await _renderRelease.WaitAsync(CancellationToken.None);
                }
                return RenderResults.TryDequeue(out CompositionFrameRenderResult result)
                    ? result
                    : new CompositionFrameRenderResult(
                        CompositionFrameRenderStatus.Presented,
                        ContinueRendering: false,
                        CompositionFrameSynchronization.Automatic);
            }
            finally
            {
                ExitCall();
            }
        }

        public ValueTask DisposeAsync()
        {
            EnterCall();
            try
            {
                IsDisposed = true;
                return ValueTask.CompletedTask;
            }
            finally
            {
                ExitCall();
            }
        }

        public void ReleaseRenders()
        {
            HoldRenders = false;
            _renderRelease.Release(3);
        }

        private void EnterCall()
        {
            int concurrent = Interlocked.Increment(ref _concurrentCalls);
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, concurrent);
        }

        private void ExitCall() => Interlocked.Decrement(ref _concurrentCalls);
    }

    private sealed class FakeGeneration(
        ViewportDimensions size,
        IReadOnlyList<ICompositionPresentationFrame> frames)
        : ICompositionPresentationGeneration
    {
        public ViewportDimensions Size { get; } = size;

        public IReadOnlyList<ICompositionPresentationFrame> Frames { get; } = frames;

        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeFrame(long allocationId, ViewportDimensions size)
        : ICompositionPresentationFrame
    {
        public long AllocationId { get; } = allocationId;

        public CompositionExternalImage Image { get; } = new(
            "test-image",
            size,
            CompositionExternalImageFormat.B8G8R8A8UNorm);

        public IReadOnlyList<CompositionExternalSemaphore> Semaphores { get; } =
            [new CompositionExternalSemaphore(1, "test-semaphore")];

        public ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ICompositionExternalHandleLease>(
                new FakeHandleLease((nint)AllocationId, "test-image"));

        public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
            long resourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ICompositionExternalHandleLease>(
                new FakeHandleLease((nint)(1000 + AllocationId), "test-semaphore"));
    }

    private sealed class FakeHandleLease(nint handle, string handleType)
        : ICompositionExternalHandleLease
    {
        public nint Handle { get; } = handle;

        public string HandleType { get; } = handleType;

        public CompositionExternalHandleOwnership Ownership =>
            CompositionExternalHandleOwnership.BorrowedUntilImportCompleted;

        public void CommitTransfer()
        {
            throw new InvalidOperationException("Borrowed handles cannot transfer.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
