// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace OpenUsd.Mcp;

internal static class McpRenderProgress
{
    internal static async ValueTask<T> RunAsync<T>(
        Func<Action<int, int>, CancellationToken, ValueTask<T>> operation,
        Func<int, int, CancellationToken, ValueTask> notify, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var channel = Channel.CreateBounded<(int Completed, int Total)>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task pump = PumpAsync(channel.Reader, notify, lifetime);
        T? result = default;
        Exception? failure = null;
        try
        {
            result = await operation((completed, total) =>
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (!channel.Writer.TryWrite((completed, total)))
                {
                    throw new InvalidOperationException("The render progress observer has already stopped.");
                }
            }, lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            channel.Writer.TryComplete();
        }
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (Exception notificationFailure)
        {
            if (failure is not null && failure is not OperationCanceledException)
            {
                throw new AggregateException(
                    "Rendering and progress delivery both failed.", failure, notificationFailure);
            }
            throw;
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result!;
    }

    private static async Task PumpAsync(
        ChannelReader<(int Completed, int Total)> reader,
        Func<int, int, CancellationToken, ValueTask> notify, CancellationTokenSource lifetime)
    {
        try
        {
            int previous = 0;
            int expectedTotal = 0;
            await foreach ((int completed, int total) in reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (completed <= previous || completed > total || total is < 1 or > 4096 ||
                    (expectedTotal != 0 && total != expectedTotal))
                {
                    throw new InvalidDataException("Render progress must have increasing bounded frame counts.");
                }
                previous = completed;
                expectedTotal = total;
                await notify(completed, total, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }
}
