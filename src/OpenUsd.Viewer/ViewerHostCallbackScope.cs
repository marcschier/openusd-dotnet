// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Viewer;

internal sealed class ViewerHostCallbackScope : IAsyncDisposable, IDisposable
{
    private const int MaximumCallbacks = 256;
    private readonly object _gate = new();
    private readonly HashSet<Task> _tasks = [];
    private readonly CancellationTokenSource _lifetime;
    private bool _stopping;
    private bool _disposed;

    internal ViewerHostCallbackScope(CancellationToken documentLifetime) =>
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime);

    internal CancellationToken Token => _lifetime.Token;

    internal bool IsQuiesced
    {
        get
        {
            lock (_gate)
            {
                return _stopping && _tasks.Count == 0;
            }
        }
    }

    internal bool TryRun(
        Func<CancellationToken, Task> callback, [NotNullWhen(true)] out Task? task)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            if (_stopping || _tasks.Count >= MaximumCallbacks)
            {
                task = null;
                return false;
            }
            task = Task.Run(() => InvokeAsync(callback, _lifetime.Token), CancellationToken.None);
            _tasks.Add(task);
            _ = task.ContinueWith(static (finished, state) =>
            {
                var scope = (ViewerHostCallbackScope)state!;
                lock (scope._gate)
                {
                    scope._tasks.Remove(finished);
                }
            }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return true;
        }
    }

    private static async Task InvokeAsync(Func<CancellationToken, Task> callback, CancellationToken cancellationToken)
    {
        try
        {
            await callback(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Foreign callback failures remain observable at the narrow retirement boundary.
            throw new InvalidOperationException("A host callback failed while owning document access.", exception);
        }
    }

    internal async Task QuiesceAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _stopping = true;
            pending = [.. _tasks];
        }
        AggregateException? cancellationFailure = null;
        try
        {
            _lifetime.Cancel();
        }
        catch (AggregateException exception)
        {
            cancellationFailure = exception;
        }
        Task completion = Task.WhenAll(pending);
        try
        {
            await completion.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && completion.IsCompleted && _lifetime.IsCancellationRequested)
        {
        }
        if (cancellationFailure is not null)
        {
            throw new InvalidOperationException("Host callback cancellation failed.", cancellationFailure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await QuiesceAsync(CancellationToken.None).ConfigureAwait(false);
        Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (!_stopping || _tasks.Count != 0)
            {
                throw new InvalidOperationException("Host callbacks must be quiesced before disposing their scope.");
            }
            _disposed = true;
        }
        _lifetime.Dispose();
    }
}
