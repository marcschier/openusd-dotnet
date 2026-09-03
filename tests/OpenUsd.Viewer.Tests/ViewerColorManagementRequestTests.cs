// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the production colour-management request pipeline and poll loop concurrently,
/// and asserts what a user would see when they change their mind mid-bake or close the
/// window mid-tick.
/// </summary>
/// <remarks>
/// Both defects these pin are ordering defects, so both tests deliberately complete work
/// out of the order it was started. Asserting on the final value alone would pass against
/// the broken code whenever the scheduler happened to be kind.
/// </remarks>
public sealed class ViewerColorManagementRequestTests
{
    private static readonly string ConfigA = Path.Combine(
        AppContext.BaseDirectory, "request-pipeline-a.ocio");

    private static readonly string ConfigB = Path.Combine(
        AppContext.BaseDirectory, "request-pipeline-b.ocio");

    [Test]
    public async Task ASlowEnableCannotOverwriteALaterDisable()
    {
        var slow = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) =>
            {
                started.TrySetResult();
                return slow.Task;
            });

        var enable = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = ConfigA,
            Display = "sRGB",
            View = "view",
        };
        Task<ViewerColorManagementOutcome?> first = pipeline.RunAsync(enable);
        await started.Task;

        // The user changed their mind while the bake was still running.
        ViewerColorManagementOutcome? second =
            await pipeline.RunAsync(enable with { Enabled = false });
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Value.Transform).IsNull();
        await Assert.That(second.Value.Diagnostic).IsNull();

        // Now the superseded bake finally succeeds. Its result must be thrown away, or
        // the display transform the user just turned off would be applied after it.
        slow.SetResult(null);
        ViewerColorManagementOutcome? discarded = await first;
        await Assert.That(discarded).IsNull();
        await Assert.That(pipeline.SupersededResults).IsGreaterThanOrEqualTo(1L);
        await Assert.That(pipeline.Version).IsEqualTo(2L);
    }

    [Test]
    public async Task ASlowConfigCannotOverwriteALaterConfig()
    {
        var slow = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startedSlow = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledSlow = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) =>
            {
                if (string.Equals(transform.ConfigPath, ConfigA, StringComparison.Ordinal))
                {
                    _ = cancellationToken.Register(() => cancelledSlow.TrySetResult());
                    startedSlow.TrySetResult();
                    return slow.Task;
                }

                return fast.Task;
            });

        var request = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = ConfigA,
            Display = "sRGB",
            View = "view",
        };
        Task<ViewerColorManagementOutcome?> first = pipeline.RunAsync(request);
        await startedSlow.Task;

        Task<ViewerColorManagementOutcome?> secondTask =
            pipeline.RunAsync(request with { ConfigPath = ConfigB });

        // The newer request cancels the older one even though the older one is the only
        // thing that can still produce a value.
        await cancelledSlow.Task;

        fast.SetResult(null);
        ViewerColorManagementOutcome? second = await secondTask;
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Value.Transform).IsNotNull();
        await Assert.That(second.Value.Transform!.ConfigPath).IsEqualTo(ConfigB);

        slow.SetResult(null);
        await Assert.That(await first).IsNull();
    }

    [Test]
    public async Task ADiagnosticFromTheCurrentRequestIsStillReported()
    {
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) => Task.FromResult<string?>("no such view"));

        ViewerColorManagementOutcome? outcome = await pipeline.RunAsync(
            new ViewerColorManagement
            {
                Enabled = true,
                ConfigPath = ConfigA,
                Display = "sRGB",
                View = "view",
            });

        await Assert.That(outcome).IsNotNull();
        await Assert.That(outcome!.Value.Resolved).IsFalse();
        await Assert.That(outcome.Value.Diagnostic).IsEqualTo("no such view");
    }

    [Test]
    public async Task StoppingDrainsTheTickThatIsAlreadyRunning()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ViewerColorManagementPoller(
            TimeSpan.FromMilliseconds(1),
            async _ =>
            {
                entered.TrySetResult();
                await release.Task;
            })
        {
            IsEnabled = true,
        };
        poller.Start();
        await entered.Task;

        Task stop = poller.StopAsync();

        // The whole point: stopping does not return while a tick is still touching the
        // state the caller is about to dispose.
        await Task.Delay(50);
        await Assert.That(stop.IsCompleted).IsFalse();
        await Assert.That(poller.IsStopped).IsTrue();

        release.SetResult();
        await stop;
        await Assert.That(poller.IsRunning).IsFalse();

        long ticks = poller.Ticks;
        await Task.Delay(50);
        await Assert.That(poller.Ticks).IsEqualTo(ticks);
    }

    [Test]
    public async Task ADisabledPollerNeverTicks()
    {
        using var poller = new ViewerColorManagementPoller(
            TimeSpan.FromMilliseconds(1),
            _ => Task.CompletedTask);
        poller.Start();
        await Task.Delay(60);
        await Assert.That(poller.Ticks).IsEqualTo(0L);

        poller.IsEnabled = true;
        await WaitForTickAsync(poller);
        await Assert.That(poller.Ticks).IsGreaterThan(0L);
        await poller.StopAsync();
    }

    [Test]
    public async Task StoppingIsIdempotentAndSurvivesDisposalRaces()
    {
        using var poller = new ViewerColorManagementPoller(
            TimeSpan.FromMilliseconds(1),
            _ => Task.CompletedTask)
        {
            IsEnabled = true,
        };
        poller.Start();
        await WaitForTickAsync(poller);

        await poller.StopAsync();
        await poller.StopAsync();
        poller.Dispose();
        poller.Cancel();
        poller.Start();

        await Assert.That(poller.IsRunning).IsFalse();
    }

    [Test]
    public async Task ASupersededSlowRequestCannotKeepTheStatePendingOnceTheNewestCommits()
    {
        // A bake already inside OpenColorIO cannot be cancelled, so request A can still
        // be running long after request B replaced it. Counting in-flight operations
        // would keep reporting "pending" until A finished, which suppresses every
        // diagnostic about B -- including B's own failure.
        var slowA = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startedA = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) =>
            {
                if (string.Equals(transform.ConfigPath, ConfigA, StringComparison.Ordinal))
                {
                    startedA.TrySetResult();
                    return slowA.Task;
                }

                return Task.FromResult<string?>("config B has no such view");
            });

        var request = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = ConfigA,
            Display = "sRGB",
            View = "view",
        };
        Task<ViewerColorManagementOutcome?> first = pipeline.RunAsync(request);
        await startedA.Task;
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        ViewerColorManagementOutcome? second =
            await pipeline.RunAsync(request with { ConfigPath = ConfigB });
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Value.Diagnostic).IsEqualTo("config B has no such view");

        // B has decided and reached the state, so nothing is pending any more even
        // though A is still running.
        pipeline.MarkCommitted(second.Value.Version);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();

        // A finally finishes. Its result is discarded and it must not drag the
        // generation backwards or make the state pending again.
        slowA.SetResult(null);
        await Assert.That(await first).IsNull();
        pipeline.MarkCommitted(1);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
        await Assert.That(pipeline.CommittedVersion).IsEqualTo(second.Value.Version);

        // And B's failure is now actionable rather than suppressed.
        ViewerColorManagementSyncResult reconciled = ViewerColorManagementSync.Compute(
            requestedEnabled: true,
            committedRequestKey: "ocio:b",
            pipeline.HasPendingRequest,
            SilkDisplayTransformStatus.TransformUnsupported,
            backendRequestKey: "ocio:b",
            diagnostic: null);
        await Assert.That(reconciled.State).IsEqualTo(ViewerColorManagementState.Failed);
        await Assert.That(reconciled.ClearTransform).IsTrue();
    }

    [Test]
    public async Task ADeferredRequestStaysPendingUntilItIsReplayed()
    {
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) => Task.FromResult<string?>(null));

        ViewerColorManagementOutcome? outcome = await pipeline.RunAsync(
            new ViewerColorManagement
            {
                Enabled = true,
                ConfigPath = ConfigA,
                Display = "sRGB",
                View = "view",
            });
        await Assert.That(outcome).IsNotNull();

        // Deliberately not committed: the coordinator refused it, so it is deferred.
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        // The replay -- the next document open -- is what commits it.
        pipeline.MarkCommitted(pipeline.Version);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
    }

    [Test]
    public async Task ADisposedPipelineDoesNotLeaveTheStatePendingForever()
    {
        var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) => Task.FromResult<string?>(null));
        _ = await pipeline.RunAsync(new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = ConfigA,
            Display = "sRGB",
            View = "view",
        });
        pipeline.AbandonNewestRequest();
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
        pipeline.Dispose();
    }

    private static readonly RenderDisplayTransform TransformA = new(
        OperatingSystem.IsWindows() ? @"C:\configs\a.ocio" : "/configs/a.ocio",
        "linear",
        "sRGB",
        "view");

    [Test]
    public async Task ARequestMadeWhileTheDocumentIsBusyIsDeferredRatherThanCommitted()
    {
        var committed = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };
        var requested = committed with { Enabled = true, Display = "sRGB", View = "view" };

        // No coordinator, or a document change in flight: the mutation never reached the
        // state, so none of the four views may move.
        ViewerColorManagementCommit deferred = ViewerColorManagementCommit.Decide(
            committed,
            committedTransformKey: null,
            committedStateTransform: null,
            requested,
            TransformA,
            diagnostic: null,
            applied: false);

        await Assert.That(deferred.Committed.Enabled).IsFalse();
        await Assert.That(deferred.CommittedTransformKey).IsNull();
        await Assert.That(deferred.StateTransform).IsNull();
        await Assert.That(deferred.Deferred).IsNotNull();
        await Assert.That(deferred.Deferred!.Enabled).IsTrue();
        await Assert.That(deferred.IsConsistent).IsTrue();

        // The next open replays it, and now all four agree on the enabled choice.
        ViewerColorManagementCommit replayed = ViewerColorManagementCommit.Decide(
            deferred.Committed,
            deferred.CommittedTransformKey,
            deferred.StateTransform,
            deferred.Deferred!,
            TransformA,
            diagnostic: null,
            applied: true);

        await Assert.That(replayed.Committed.Enabled).IsTrue();
        await Assert.That(replayed.CommittedTransformKey).IsEqualTo(TransformA.CacheKey);
        await Assert.That(replayed.StateTransform).IsEqualTo(TransformA);
        await Assert.That(replayed.Deferred).IsNull();
        await Assert.That(replayed.IsConsistent).IsTrue();
    }

    [Test]
    public async Task ADisableMadeDuringAnOpenRaceNeverLeavesTheTransformCommitted()
    {
        var enabled = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = TransformA.ConfigPath,
            Display = "sRGB",
            View = "view",
        };

        // Enabled and committed.
        ViewerColorManagementCommit live = ViewerColorManagementCommit.Decide(
            enabled with { Enabled = false },
            committedTransformKey: null,
            committedStateTransform: null,
            enabled,
            TransformA,
            diagnostic: null,
            applied: true);
        await Assert.That(live.IsConsistent).IsTrue();

        // The user disables it while the document is being swapped. Nothing commits, so
        // the menu still shows enabled and the state still carries the transform -- which
        // is the truth, not a lie.
        ViewerColorManagementCommit raced = ViewerColorManagementCommit.Decide(
            live.Committed,
            live.CommittedTransformKey,
            live.StateTransform,
            enabled with { Enabled = false },
            validated: null,
            diagnostic: null,
            applied: false);
        await Assert.That(raced.Committed.Enabled).IsTrue();
        await Assert.That(raced.CommittedTransformKey).IsEqualTo(TransformA.CacheKey);
        await Assert.That(raced.StateTransform).IsEqualTo(TransformA);
        await Assert.That(raced.Deferred!.Enabled).IsFalse();
        await Assert.That(raced.IsConsistent).IsTrue();

        // The open replays the disable, and now every view agrees there is no transform.
        ViewerColorManagementCommit settled = ViewerColorManagementCommit.Decide(
            raced.Committed,
            raced.CommittedTransformKey,
            raced.StateTransform,
            raced.Deferred!,
            validated: null,
            diagnostic: null,
            applied: true);
        await Assert.That(settled.Committed.Enabled).IsFalse();
        await Assert.That(settled.CommittedTransformKey).IsNull();
        await Assert.That(settled.StateTransform).IsNull();
        await Assert.That(settled.Deferred).IsNull();
        await Assert.That(settled.IsConsistent).IsTrue();
    }

    [Test]
    public async Task ARefusedRequestCommitsAsDisabledWithNoTransformInTheState()
    {
        var requested = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = TransformA.ConfigPath,
            Display = "sRGB",
            View = "view",
        };

        ViewerColorManagementCommit refused = ViewerColorManagementCommit.Decide(
            requested with { Enabled = false },
            committedTransformKey: null,
            committedStateTransform: null,
            requested,
            validated: null,
            diagnostic: "no such view",
            applied: true);

        await Assert.That(refused.Committed.Enabled).IsFalse();
        await Assert.That(refused.CommittedTransformKey).IsNull();
        await Assert.That(refused.StateTransform).IsNull();
        await Assert.That(refused.Deferred).IsNull();
        await Assert.That(refused.IsConsistent).IsTrue();
    }

    [Test]
    public async Task ADeferredRequestIsReplayedOnlyWhileItIsStillTheNewest()
    {
        var committed = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };
        var deferred = new ViewerDeferredColorManagement(
            committed with { Enabled = true, Display = "sRGB", View = "view" },
            Generation: 3);

        // Still the newest: the open replays it and will commit exactly that generation.
        ViewerOpeningColorManagement replayed =
            ViewerDeferredColorManagement.SelectOpeningChoice(committed, deferred, 3, 2);
        await Assert.That(replayed.Choice.Enabled).IsTrue();
        await Assert.That(replayed.Generation).IsEqualTo(3L);
        await Assert.That(replayed.DiscardDeferred).IsFalse();

        // A newer request arrived while the open was being prepared. Replaying the old
        // deferral would re-apply a decision the user already changed, so it is dropped,
        // the open uses the committed choice, and it speaks only for the generation
        // already committed -- leaving the newer request pending.
        ViewerOpeningColorManagement superseded =
            ViewerDeferredColorManagement.SelectOpeningChoice(committed, deferred, 4, 2);
        await Assert.That(superseded.Choice.Enabled).IsFalse();
        await Assert.That(superseded.Generation).IsEqualTo(2L);
        await Assert.That(superseded.DiscardDeferred).IsTrue();

        // With nothing deferred the committed choice opens, and again the open cannot
        // claim a generation whose validation it never saw.
        ViewerOpeningColorManagement plain =
            ViewerDeferredColorManagement.SelectOpeningChoice(committed, deferred: null, 7, 5);
        await Assert.That(plain.Choice).IsEqualTo(committed);
        await Assert.That(plain.Generation).IsEqualTo(5L);
        await Assert.That(plain.DiscardDeferred).IsFalse();
    }

    [Test]
    public async Task AnOpenNeverCommitsAValidationThatIsStillInFlight()
    {
        var slow = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) =>
            {
                started.TrySetResult();
                return slow.Task;
            });

        var committed = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };

        // A request whose validation has not finished. Nothing is deferred yet, because
        // the request has not decided anything.
        Task<ViewerColorManagementOutcome?> inFlight = pipeline.RunAsync(
            committed with { Enabled = true, Display = "sRGB", View = "view" });
        await started.Task;
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        // The open happens now. It resolves the committed choice, and must claim only
        // the generation already committed -- claiming the newest would unpend a request
        // it never saw the result of and silence the reconciliation about it.
        ViewerOpeningColorManagement opening =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                committed,
                deferred: null,
                pipeline.Version,
                pipeline.CommittedVersion);
        await Assert.That(opening.Choice.Enabled).IsFalse();
        await Assert.That(opening.Generation).IsEqualTo(pipeline.CommittedVersion);

        pipeline.MarkCommitted(opening.Generation);
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        // The validation lands after the open. Its result is still current, so it is
        // deferred at its own generation and the drain applies it.
        slow.SetResult(null);
        ViewerColorManagementOutcome? landed = await inFlight;
        await Assert.That(landed).IsNotNull();
        var deferred = new ViewerDeferredColorManagement(
            landed!.Value.Requested,
            landed.Value.Version);
        await Assert.That(deferred.Generation).IsGreaterThan(opening.Generation);

        ViewerOpeningColorManagement replay =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                committed,
                deferred,
                pipeline.Version,
                pipeline.CommittedVersion);
        await Assert.That(replay.Choice.Enabled).IsTrue();
        await Assert.That(replay.Generation).IsEqualTo(deferred.Generation);
        pipeline.MarkCommitted(replay.Generation);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
    }

    [Test]
    public async Task AValidationThatCompletesBeforeTheOpenIsCommittedByIt()
    {
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) => Task.FromResult<string?>(null));

        var committed = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };

        // Validation completes first, and the coordinator was not there, so the decided
        // request is deferred at its own generation.
        ViewerColorManagementOutcome? decided = await pipeline.RunAsync(
            committed with { Enabled = true, Display = "sRGB", View = "view" });
        var deferred = new ViewerDeferredColorManagement(
            decided!.Value.Requested,
            decided.Value.Version);
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        // The open sees a deferred result that is still the newest generation, so it
        // replays it and commits exactly that generation.
        ViewerOpeningColorManagement opening =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                committed,
                deferred,
                pipeline.Version,
                pipeline.CommittedVersion);
        await Assert.That(opening.Choice.Enabled).IsTrue();
        await Assert.That(opening.Generation).IsEqualTo(decided.Value.Version);

        pipeline.MarkCommitted(opening.Generation);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
    }

    [Test]
    public async Task AFailedOpenLeavesTheDeferredRequestForTheNextAttempt()
    {
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) => Task.FromResult<string?>(null));

        var committed = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };
        ViewerColorManagementOutcome? outcome = await pipeline.RunAsync(
            committed with { Enabled = true, Display = "sRGB", View = "view" });
        await Assert.That(outcome).IsNotNull();

        // The coordinator was not there, so the request is deferred at its own
        // generation and nothing is committed.
        var deferred = new ViewerDeferredColorManagement(
            outcome!.Value.Requested,
            outcome.Value.Version);
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        // First open attempt selects it -- and then fails, so no generation is marked and
        // the deferral survives untouched for the next attempt.
        ViewerOpeningColorManagement firstAttempt =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                committed,
                deferred,
                pipeline.Version,
                pipeline.CommittedVersion);
        await Assert.That(firstAttempt.Choice.Enabled).IsTrue();
        await Assert.That(firstAttempt.DiscardDeferred).IsFalse();
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        // Second attempt succeeds: only now is the generation committed.
        ViewerOpeningColorManagement secondAttempt =
            ViewerDeferredColorManagement.SelectOpeningChoice(
                committed,
                deferred,
                pipeline.Version,
                pipeline.CommittedVersion);
        await Assert.That(secondAttempt.Choice.Enabled).IsTrue();
        pipeline.MarkCommitted(secondAttempt.Generation);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
    }

    [Test]
    public async Task ARequestThatArrivesDuringAnOpenIsNotSwallowedByTheCommit()
    {
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) => Task.FromResult<string?>(null));

        var committed = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };

        // The open resolved generation 1.
        ViewerColorManagementOutcome? opening = await pipeline.RunAsync(
            committed with { Enabled = true, Display = "sRGB", View = "view" });
        long openingGeneration = opening!.Value.Version;

        // While it was running, the user changed their mind: generation 2 is deferred.
        ViewerColorManagementOutcome? later = await pipeline.RunAsync(committed);
        var deferredNewer = new ViewerDeferredColorManagement(
            later!.Value.Requested,
            later.Value.Version);

        // The open commits only its own generation, so the newer deferral is not
        // swallowed by it and the state stays pending until it is replayed.
        pipeline.MarkCommitted(openingGeneration);
        await Assert.That(deferredNewer.Generation).IsGreaterThan(openingGeneration);
        await Assert.That(pipeline.HasPendingRequest).IsTrue();

        pipeline.MarkCommitted(deferredNewer.Generation);
        await Assert.That(pipeline.HasPendingRequest).IsFalse();
    }

    [Test]
    public async Task ASupersededDeferralIsDiscardedWithoutCancellingTheNewerRequest()
    {
        // The drain replays a deferred request. If it replayed a superseded one it would
        // start a fresh pipeline request, and starting one cancels whatever is running --
        // which here is the newer validation the deferral was superseded by.
        var slow = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pipeline = new ViewerColorManagementRequestPipeline(
            (transform, cancellationToken) =>
            {
                if (string.Equals(transform.ConfigPath, ConfigB, StringComparison.Ordinal))
                {
                    _ = cancellationToken.Register(() => cancelled.TrySetResult());
                    started.TrySetResult();
                    return slow.Task;
                }

                return Task.FromResult<string?>(null);
            });

        var request = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = ConfigA,
            Display = "sRGB",
            View = "view",
        };

        // Request N decides and is deferred at its own generation.
        ViewerColorManagementOutcome? decided = await pipeline.RunAsync(request);
        var deferred = new ViewerDeferredColorManagement(
            decided!.Value.Requested,
            decided.Value.Version);

        // Request N+1 is still validating.
        Task<ViewerColorManagementOutcome?> newer =
            pipeline.RunAsync(request with { ConfigPath = ConfigB });
        await started.Task;
        await Assert.That(pipeline.Version).IsGreaterThan(deferred.Generation);

        // The drain rule: replay only when the deferral is still the newest generation.
        bool replay = deferred.Generation == pipeline.Version;
        await Assert.That(replay).IsFalse();

        // Nothing entered the pipeline, so the newer request is untouched and still able
        // to complete.
        await Task.Delay(30);
        await Assert.That(cancelled.Task.IsCompleted).IsFalse();
        slow.SetResult(null);
        ViewerColorManagementOutcome? landed = await newer;
        await Assert.That(landed).IsNotNull();
        await Assert.That(landed!.Value.Transform!.ConfigPath).IsEqualTo(ConfigB);
    }

    [Test]
    public async Task TheDrainOnlyReplaysTheCurrentGeneration()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        int index = source.IndexOf(
            "internal async Task DrainColorManagementRequestsAsync()",
            StringComparison.Ordinal);
        await Assert.That(index).IsGreaterThan(0);
        string body = source[index..];
        int end = body.IndexOf("\n    /// <summary>", StringComparison.Ordinal);
        if (end > 0)
        {
            body = body[..end];
        }

        // The comparison happens immediately before the replay, against the pipeline's
        // current version, and a mismatch returns without calling ApplyColorManagementAsync.
        await Assert.That(body).Contains("long newest = _colorManagementRequests?.Version ?? 0;");
        await Assert.That(body).Contains("if (newer.Generation != newest)");
        int guardIndex = body.IndexOf("if (newer.Generation != newest)", StringComparison.Ordinal);
        int replayIndex = body.IndexOf(
            "await ApplyColorManagementAsync(newer.Request);",
            StringComparison.Ordinal);
        await Assert.That(replayIndex).IsGreaterThan(guardIndex);
    }

    [Test]
    public async Task ARollbackNeverRestoresACommitOlderThanTheNewestOne()
    {
        // A request that ends up deferred must not restore a snapshot taken before its
        // own validation started: an open may have committed a newer choice in between,
        // and rolling that back is a visible regression of a decision that already took
        // effect. The commit is therefore decided against the *current* committed view.
        var first = new ViewerColorManagement
        {
            Enabled = false,
            ConfigPath = TransformA.ConfigPath,
        };
        var openedByOpen = first with { Enabled = true, Display = "sRGB", View = "view" };

        // The state as it was when the failing request started.
        ViewerColorManagement staleSnapshot = first;

        // ... and as it is when the request finally decides, after an open committed.
        ViewerColorManagement current = openedByOpen;

        ViewerColorManagementCommit decision = ViewerColorManagementCommit.Decide(
            current,
            committedTransformKey: TransformA.CacheKey,
            committedStateTransform: TransformA,
            first with { Enabled = true, Display = "sRGB", View = "other" },
            validated: null,
            diagnostic: null,
            applied: false);

        await Assert.That(decision.Committed).IsEqualTo(current);
        await Assert.That(decision.Committed).IsNotEqualTo(staleSnapshot);
        await Assert.That(decision.CommittedTransformKey).IsEqualTo(TransformA.CacheKey);
        await Assert.That(decision.StateTransform).IsEqualTo(TransformA);
        await Assert.That(decision.Deferred).IsNotNull();
    }

    [Test]
    public async Task TheRequestCommitReadsTheCommittedViewAfterItsAwait()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        int index = source.IndexOf(
            "private async Task ApplyColorManagementAsync(",
            StringComparison.Ordinal);
        await Assert.That(index).IsGreaterThan(0);
        string body = source[index..];
        int end = body.IndexOf(
            "internal async Task SynchronizeColorManagementFromBackendAsync",
            StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(0);
        body = body[..end];

        // No pre-await snapshot of the committed choice exists to roll back to.
        await Assert.That(body).DoesNotContain("ViewerColorManagement committed = _colorManagement;");
        int decideIndex = body.IndexOf(
            "ViewerColorManagementCommit.Decide(",
            StringComparison.Ordinal);
        await Assert.That(decideIndex).IsGreaterThan(0);
        string arguments = body[decideIndex..];
        int firstArgument = arguments.IndexOf('\n') + 1;
        await Assert.That(arguments[firstArgument..].TrimStart()).StartsWith("_colorManagement,");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static async Task WaitForTickAsync(ViewerColorManagementPoller poller)
    {
        for (int attempt = 0; attempt < 200 && poller.Ticks == 0; attempt++)
        {
            await Task.Delay(10);
        }
    }
}
