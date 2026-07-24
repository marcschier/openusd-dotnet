// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.NativeProbe;

internal static class SchedulerAsyncResultProbe
{
    internal static async Task RunAsync(string directory)
    {
        string path = Path.Combine(directory, "scheduler-async-result.usda");
        File.Delete(path);
        await using var scheduler = UsdStageScheduler.Create(path);
        var incomplete = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool staticCallbackRan = false;
        await ExpectRejectedAsync(() => scheduler.InvokeAsync<Task<object>>(stage =>
        {
            staticCallbackRan = true;
            return Task.FromResult<object>(stage);
        }).AsTask()).ConfigureAwait(false);
        await ExpectRejectedAsync(() => scheduler.InvokeAsync<ValueTask<object>>(stage =>
        {
            staticCallbackRan = true;
            return ValueTask.FromResult<object>(stage.GetRootLayer());
        }).AsTask()).ConfigureAwait(false);
        await ExpectRejectedAsync(() => scheduler.InvokeAsync(stage =>
        {
            staticCallbackRan = true;
            return Task.CompletedTask;
        }).AsTask()).ConfigureAwait(false);
        await ExpectRejectedAsync(() => scheduler.InvokeAsync(stage =>
        {
            staticCallbackRan = true;
            return incomplete.Task;
        }).AsTask()).ConfigureAwait(false);
        await ExpectRejectedAsync(() => scheduler.InvokeAsync(stage =>
        {
            staticCallbackRan = true;
            return ValueTask.CompletedTask;
        }).AsTask()).ConfigureAwait(false);
        await ExpectRejectedAsync(() => scheduler.InvokeAsync(stage =>
        {
            staticCallbackRan = true;
            return new ValueTask(incomplete.Task);
        }).AsTask()).ConfigureAwait(false);
        await ExpectRejectedAsync(() => scheduler.InvokeAsync<DerivedTask>(_ =>
        {
            staticCallbackRan = true;
            return new DerivedTask();
        }).AsTask()).ConfigureAwait(false);

        if (staticCallbackRan)
        {
            throw new InvalidOperationException(
                "A statically forbidden asynchronous callback was enqueued.");
        }

        await scheduler.InvokeAsync(stage =>
        {
            stage.DefinePrim("/World", "Xform");
        }).ConfigureAwait(false);

        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                static stage => Task.FromResult<object>(stage)).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                static stage => Task.FromResult<object>(stage.GetRootLayer())).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                static stage => Task.FromResult<object>(stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                static stage => new ValueTask<object>(stage)).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                static stage => new ValueTask<object>(stage.GetRootLayer())).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(
                static stage => new ValueTask<object>(stage.GetPrim("/World"))).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(static _ => Task.CompletedTask).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(_ => incomplete.Task).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(static _ => ValueTask.CompletedTask).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(_ => new ValueTask(incomplete.Task)).AsTask())
            .ConfigureAwait(false);
        await ExpectRejectedAsync(
            () => scheduler.InvokeAsync<object>(static _ => new DerivedTask()).AsTask())
            .ConfigureAwait(false);

        string identifier = await scheduler.InvokeAsync(
            static stage => stage.RootLayerIdentifier).ConfigureAwait(false);
        int detached = await scheduler.InvokeAsync(static _ => 42).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(identifier) ||
            detached != 42 ||
            incomplete.Task.IsCompleted)
        {
            throw new InvalidOperationException(
                "The scheduler did not remain usable after asynchronous result rejection.");
        }
    }

    private static async Task ExpectRejectedAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (UsdStageBoundResultException exception)
            when (exception.Code == UsdStageBoundResultException.ErrorCode &&
                  exception.Message == UsdStageBoundResultException.ErrorMessage)
        {
            return;
        }

        throw new InvalidOperationException(
            "An asynchronous scheduler result was not rejected with the stable contract error.");
    }

    private sealed class DerivedTask : Task
    {
        internal DerivedTask()
            : base(static () => { })
        {
        }
    }
}
