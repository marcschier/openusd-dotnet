// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

[NotInParallel]
public sealed class UsdStageSchedulerRetirementTests
{
    [Test]
    public async Task RetirementFencesForeignProducersAndCanResumeOrCommitAfterOwnerCleanup()
    {
        await using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStageScheduler scheduler = document.Scheduler;
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using UsdStageRenderLease retained = source.AcquireLease();
        UsdStageRetirementLease retirement = await scheduler.TryPrepareRetirementAsync(
            static stage => stage.GetPrim("/World").GetDouble("weight") == 7)
            ?? throw new InvalidOperationException("The unchanged document was not admitted.");
        await using (retirement)
        {
            await Assert.That(() => scheduler.EditAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 31),
                UsdStageInvalidationKind.Property).AsTask()).Throws<InvalidOperationException>();
            await Assert.That(() => scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 32)).AsTask())
                .Throws<InvalidOperationException>();
            await Assert.That(async () =>
            {
                using UsdStageRenderSource unexpected = await scheduler.AcquireRenderSourceAsync();
            })
                .Throws<InvalidOperationException>();
            await Assert.That(() => source.AcquireLease()).Throws<InvalidOperationException>();
            await Assert.That(() => scheduler.DisposeAsync().AsTask()).Throws<InvalidOperationException>();
            await Assert.That(await retirement.InvokeCleanupAsync(
                static stage => stage.GetPrim("/World").GetDouble("weight"))).IsEqualTo(7d);
        }

        await scheduler.EditAsync(
            static stage => stage.GetPrim("/World").SetDouble("weight", 9),
            UsdStageInvalidationKind.Property);
        await using UsdStageRetirementLease committed = await scheduler.TryPrepareRetirementAsync(
            static stage => stage.GetPrim("/World").GetDouble("weight") == 9)
            ?? throw new InvalidOperationException("The resumed document was not admitted.");
        await Assert.That(() => committed.CommitAsync().AsTask()).Throws<InvalidOperationException>();
        await committed.InvokeCleanupAsync(static stage =>
        {
            if (stage.GetPrim("/World").GetDouble("weight") != 9)
            {
                throw new InvalidOperationException("Foreign work crossed the retirement fence.");
            }
        });
        retained.Dispose();
        source.Dispose();
        await committed.CommitAsync();
        await Assert.That(() => scheduler.InvokeAsync(static stage => stage.ChangeSerial).AsTask())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task RetirementDrainsAlreadyAdmittedBackpressuredWritersBeforeCheckingTheDocument()
    {
        await using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStageScheduler scheduler = document.Scheduler;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = scheduler.EditAsync(stage =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The admitted writer was not released.");
            }
            stage.GetPrim("/World").SetDouble("weight", 1);
        }, UsdStageInvalidationKind.Property).AsTask();
        Task<UsdStageRetirementLease?>? pending = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task queued = scheduler.EditAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 2),
                UsdStageInvalidationKind.Property).AsTask();
            Task backpressured = scheduler.EditAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 3),
                UsdStageInvalidationKind.Property).AsTask();
            pending = scheduler.TryPrepareRetirementAsync(
                static stage => stage.GetPrim("/World").GetDouble("weight") == 3).AsTask();
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(() => scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 99)).AsTask())
                .Throws<InvalidOperationException>();

            release.Set();
            await Task.WhenAll(first, queued, backpressured).WaitAsync(TimeSpan.FromSeconds(10));
            await using UsdStageRetirementLease retirement = await pending.WaitAsync(TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("A pre-fence writer was omitted from the predicate.");
            await Assert.That(await retirement.InvokeCleanupAsync(
                static stage => stage.GetPrim("/World").GetDouble("weight"))).IsEqualTo(3d);
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            if (pending is not null &&
                await pending.WaitAsync(TimeSpan.FromSeconds(10)) is { } retirement)
            {
                await retirement.DisposeAsync();
            }
        }
    }

    [Test]
    [Arguments("false")]
    [Arguments("throw")]
    [Arguments("cancel")]
    public async Task UnacceptedRetirementLeavesTheDocumentUsable(string outcome)
    {
        await using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStageScheduler scheduler = document.Scheduler;
        using var cancellation = new CancellationTokenSource();
        Task<UsdStageRetirementLease?> pending = scheduler.TryPrepareRetirementAsync(stage =>
        {
            if (stage.GetPrim("/World").GetDouble("weight") != 7)
            {
                throw new InvalidOperationException("The source changed before admission.");
            }
            if (outcome == "throw")
            {
                throw new InvalidDataException("The document observation could not be read.");
            }
            if (outcome == "cancel")
            {
                cancellation.Cancel();
                return true;
            }
            return false;
        }, cancellation.Token).AsTask();

        async Task ConsumeUnexpectedLeaseAsync()
        {
            await using UsdStageRetirementLease? unexpected = await pending;
            await Assert.That(unexpected).IsNull();
        }

        if (outcome == "throw")
        {
            await Assert.That(ConsumeUnexpectedLeaseAsync).Throws<InvalidDataException>();
        }
        else if (outcome == "cancel")
        {
            await Assert.That(ConsumeUnexpectedLeaseAsync).Throws<OperationCanceledException>();
        }
        else
        {
            await ConsumeUnexpectedLeaseAsync();
        }
        await scheduler.EditAsync(
            static stage => stage.GetPrim("/World").SetDouble("weight", 11),
            UsdStageInvalidationKind.Property);
        await Assert.That(await scheduler.InvokeAsync(
            static stage => stage.GetPrim("/World").GetDouble("weight"))).IsEqualTo(11d);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using UsdStageRenderLease lease = source.AcquireLease();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanceledPreFenceAcquisitionIsFullyReleasedBeforeRetirement(bool backpressured)
    {
        await using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStageScheduler scheduler = document.Scheduler;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = scheduler.InvokeAsync(_ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The owner thread was not released.");
            }
        }).AsTask();
        Task<UsdStageRetirementLease?>? pending = null;
        Task<UsdStageRenderSource>? acquisition = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task queued = backpressured
                ? scheduler.InvokeAsync(static _ => { }).AsTask()
                : Task.CompletedTask;
            acquisition = scheduler.AcquireRenderSourceAsync(cancellation.Token).AsTask();
            pending = scheduler.TryPrepareRetirementAsync(
                static stage => stage.GetPrim("/World").GetDouble("weight") == 7).AsTask();
            cancellation.Cancel();
            release.Set();
            await using UsdStageRetirementLease retirement = await pending.WaitAsync(TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("The unchanged document was not admitted.");
            await retirement.CommitAsync();
            await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            if (acquisition is not null)
            {
                await Assert.That(async () =>
                {
                    using UsdStageRenderSource unexpected = await acquisition;
                }).Throws<OperationCanceledException>();
            }
            if (pending is not null &&
                await pending.WaitAsync(TimeSpan.FromSeconds(10)) is { } retirement)
            {
                await retirement.DisposeAsync();
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClosingRetirementDrainsAuthorizedCleanupBeforeResumingOrDisposing(bool commit)
    {
        await using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStageScheduler scheduler = document.Scheduler;
        await using UsdStageRetirementLease retirement = await scheduler.TryPrepareRetirementAsync(
            static stage => stage.HasPrim("/World"))
            ?? throw new InvalidOperationException("The document was not admitted.");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completed = 0;
        Task first = retirement.InvokeCleanupAsync(_ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Owner cleanup was not released.");
            }
            Interlocked.Increment(ref completed);
        }).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task second = retirement.InvokeCleanupAsync(_ => Interlocked.Increment(ref completed)).AsTask();
            Task third = retirement.InvokeCleanupAsync(_ => Interlocked.Increment(ref completed)).AsTask();
            Task closing = commit ? retirement.CommitAsync().AsTask() : retirement.DisposeAsync().AsTask();
            await Assert.That(closing.IsCompleted).IsFalse();
            await Assert.That(() => retirement.InvokeCleanupAsync(static _ => 1).AsTask())
                .Throws<InvalidOperationException>();
            await Assert.That(() => scheduler.InvokeAsync(static _ => 1).AsTask())
                .Throws<InvalidOperationException>();
            release.Set();
            await Task.WhenAll(first, second, third, closing).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(completed).IsEqualTo(3);
            if (commit)
            {
                await Assert.That(() => scheduler.InvokeAsync(static _ => 1).AsTask())
                    .Throws<ObjectDisposedException>();
            }
            else
            {
                await Assert.That(await scheduler.InvokeAsync(
                    static stage => stage.GetPrim("/World").GetDouble("weight"))).IsEqualTo(7d);
            }
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelingRetirementBeforeValidationResumesAdmissionWithoutDroppingWriters(bool pendingWriter)
    {
        await using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStageScheduler scheduler = document.Scheduler;
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool predicateCalled = false;
        Task first = scheduler.InvokeAsync(_ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The first operation was not released.");
            }
        }).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task second = scheduler.EditAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 2),
                UsdStageInvalidationKind.Property).AsTask();
            Task third = pendingWriter
                ? scheduler.EditAsync(static stage => stage.GetPrim("/World").SetDouble("weight", 3),
                    UsdStageInvalidationKind.Property).AsTask()
                : Task.CompletedTask;
            Task<UsdStageRetirementLease?> preparation = scheduler.TryPrepareRetirementAsync(_ =>
            {
                predicateCalled = true;
                return true;
            }, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.That(async () =>
            {
                await using UsdStageRetirementLease? unexpected =
                    await preparation.WaitAsync(TimeSpan.FromSeconds(10));
            }).Throws<OperationCanceledException>();
            Task resumed = scheduler.EditAsync(
                static stage => stage.GetPrim("/World").SetDouble("weight", 4),
                UsdStageInvalidationKind.Property).AsTask();
            release.Set();
            await Task.WhenAll(first, second, third, resumed).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(predicateCalled).IsFalse();
            await Assert.That(await scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").GetDouble("weight"))).IsEqualTo(4d);
        }
        finally
        {
            cancellation.Cancel();
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class NativeDocument : IAsyncDisposable
    {
        private readonly string _path;

        private NativeDocument(string path, UsdStageScheduler scheduler)
        {
            _path = path;
            Scheduler = scheduler;
        }

        internal UsdStageScheduler Scheduler { get; }

        internal static async Task<NativeDocument> OpenAsync(int capacity = 1)
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH and provide a matching native runtime.");
            }
            _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
            string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
                ?? Path.Combine(AppContext.BaseDirectory, "native-work");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, $"retirement-{Guid.NewGuid():N}.usda");
            await File.WriteAllTextAsync(path,
                "#usda 1.0\ndef Xform \"World\"\n{\n    double weight = 7\n}\n");
            return new NativeDocument(path, UsdStageScheduler.Open(path, capacity));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Scheduler.DisposeAsync();
            }
            finally
            {
                File.Delete(_path);
            }
        }
    }
}
