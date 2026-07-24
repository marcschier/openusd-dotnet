// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class StageChangeFeedProbe
{
    internal static async Task RunAsync(string directory)
    {
        string stagePath = Path.Combine(directory, "scheduler-change-feed.usda");
        File.Delete(stagePath);

        var scheduler = UsdStageScheduler.Create(
            stagePath,
            capacity: 2048,
            notificationCapacity: 1);
        await VerifySingleReaderAsync(scheduler).ConfigureAwait(false);

        await scheduler.EditAsync(
            stage =>
            {
                _ = stage.DefinePrim("/World/Signal", "Xform");
            },
            UsdStageInvalidationKind.Topology).ConfigureAwait(false);

        await scheduler.InvokeAsync(
            stage => stage.GetPrim("/World/Signal").SetDouble("custom:invoke", 1))
            .ConfigureAwait(false);

        bool invokeFailurePropagated = false;
        try
        {
            await scheduler.InvokeAsync(
                stage =>
                {
                    stage.GetPrim("/World/Signal").SetDouble("custom:invokeFailed", 2);
                    throw new InvalidOperationException("Expected invoke failure.");
                }).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == "Expected invoke failure.")
        {
            invokeFailurePropagated = true;
        }

        bool editFailurePropagated = false;
        try
        {
            await scheduler.EditAsync(
                stage =>
                {
                    stage.GetPrim("/World/Signal").SetDouble("custom:value", -1);
                    throw new InvalidOperationException("Expected edit failure.");
                },
                UsdStageInvalidationKind.Full).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == "Expected edit failure.")
        {
            editFailurePropagated = true;
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool cancellationPropagated = false;
        try
        {
            await scheduler.EditAsync(
                stage => stage.GetPrim("/World/Signal").SetDouble("custom:value", -2),
                UsdStageInvalidationKind.Property,
                canceled.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationPropagated = true;
        }

        Task[] edits = new Task[1000];
        for (int i = 0; i < edits.Length; i++)
        {
            int value = i;
            edits[i] = scheduler.EditAsync(
                stage =>
                {
                    stage.GetPrim("/World/Signal").SetDouble("custom:value", value);
                    if (value == edits.Length - 1)
                    {
                        stage.Save();
                    }
                },
                UsdStageInvalidationKind.Property).AsTask();
        }
        await Task.WhenAll(edits).ConfigureAwait(false);
        await scheduler.DisposeAsync().ConfigureAwait(false);

        var changes = new List<UsdStageChange>();
        await foreach (UsdStageChange change in scheduler.ReadChangesAsync().ConfigureAwait(false))
        {
            changes.Add(change);
        }

        int sequentialChangeCount = 0;
        await foreach (UsdStageChange _ in scheduler.ReadChangesAsync().ConfigureAwait(false))
        {
            sequentialChangeCount++;
        }

        if (!invokeFailurePropagated ||
            !editFailurePropagated ||
            !cancellationPropagated ||
            changes.Count != 1 ||
            changes[0].EditCount != edits.Length + 4 ||
            changes[0].Invalidation != UsdStageInvalidationKind.Full ||
            sequentialChangeCount != 0)
        {
            throw new InvalidOperationException(
                "Stage invocation, cancellation, coalescing, or durable completion did not behave as expected.");
        }

        using (UsdStage reopened = UsdStage.Open(stagePath))
        {
            if (reopened.GetPrim("/World/Signal").GetDouble("custom:value") != edits.Length - 1)
            {
                throw new InvalidOperationException("Scheduled edits were not applied in order.");
            }
        }

        await VerifyOwnerFailureAsync(directory).ConfigureAwait(false);
        Console.WriteLine("Stage change feed passed.");
    }

    private static async Task VerifySingleReaderAsync(UsdStageScheduler scheduler)
    {
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<UsdStageChange> first =
            scheduler.ReadChangesAsync(cancellation.Token).GetAsyncEnumerator();
        Task<bool> pendingRead = first.MoveNextAsync().AsTask();
        await using IAsyncEnumerator<UsdStageChange> second =
            scheduler.ReadChangesAsync().GetAsyncEnumerator();

        bool secondReaderRejected = false;
        try
        {
            _ = await second.MoveNextAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            secondReaderRejected = true;
        }

        cancellation.Cancel();
        try
        {
            _ = await pendingRead.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (!secondReaderRejected)
        {
            throw new InvalidOperationException("A second active stage change reader was accepted.");
        }
    }

    private static async Task VerifyOwnerFailureAsync(string directory)
    {
        string missingPath = Path.Combine(directory, "missing-stage-change-feed.usda");
        File.Delete(missingPath);
        var scheduler = UsdStageScheduler.Open(missingPath);

        bool invocationFailed = false;
        try
        {
            _ = await scheduler.InvokeAsync(static _ => true).ConfigureAwait(false);
        }
        catch (OpenUsdNativeException)
        {
            invocationFailed = true;
        }

        bool disposalFailed = false;
        try
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
        catch (OpenUsdNativeException)
        {
            disposalFailed = true;
        }

        int failedEnumerations = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await foreach (UsdStageChange _ in scheduler.ReadChangesAsync().ConfigureAwait(false))
                {
                }
            }
            catch (OpenUsdNativeException)
            {
                failedEnumerations++;
            }
        }

        if (!invocationFailed || !disposalFailed || failedEnumerations != 2)
        {
            throw new InvalidOperationException("Stage owner failure was not propagated deterministically.");
        }
    }
}
