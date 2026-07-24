// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal enum CompositionViewportState
{
    Detached,
    Probing,
    Ready,
    Backpressured,
    Unavailable,
    Lost,
    Disposed
}

internal enum CompositionPresentResult
{
    Presented,
    Idle,
    Backpressured,
    Lost,
    Detached
}

internal readonly record struct CompositionPresentOutcome(
    CompositionPresentResult Result,
    bool ContinueRendering,
    Task? RetryAvailable = null);

internal readonly record struct CompositionViewportSessionStatistics(
    long CurrentGenerationId,
    long SurfaceUpdateStartedCount,
    long SurfaceUpdateCompletedCount,
    long LastPresentedGenerationId,
    long LastPresentedAllocationId,
    long GenerationRetirementStartedCount,
    long GenerationRetirementCompletedCount,
    long LastRetiredGenerationId,
    long ImportedFrameDisposalCount,
    long StaleImportedFrameReuseCount);

internal interface ICompositionUiDispatcher
{
    bool CheckAccess();

    ValueTask InvokeAsync(Func<ValueTask> action);
}

internal interface IImportedCompositionFrame : IAsyncDisposable
{
    bool IsLost { get; }
}

internal interface ICompositionSurfaceBridge : IAsyncDisposable
{
    bool IsLost { get; }

    ValueTask<IImportedCompositionFrame> ImportAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken);

    Task PresentAsync(
        IImportedCompositionFrame importedFrame,
        CompositionFrameSynchronization synchronization);
}

internal sealed class ObservedTaskRunner(Action<string, Exception> reportFailure)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, Task> _tasks = [];
    private readonly List<Exception> _failures = [];
    private int _nextId;

    internal IReadOnlyList<Exception> Failures
    {
        get
        {
            lock (_lock)
            {
                return [.. _failures];
            }
        }
    }

    internal Task Run(string operation, Func<Task> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);
        int id;
        Task task;
        lock (_lock)
        {
            id = ++_nextId;
            task = ExecuteAsync(id, operation, action);
            _tasks.Add(id, task);
        }
        return task;
    }

    internal void Report(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_lock)
        {
            _failures.Add(exception);
        }
        reportFailure(operation, exception);
    }

    internal async Task DrainAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_lock)
            {
                tasks = [.. _tasks.Values];
            }
            if (tasks.Length == 0)
            {
                return;
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAsync(int id, string operation, Func<Task> action)
    {
        await Task.Yield();
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Report(operation, exception);
        }
        finally
        {
            lock (_lock)
            {
                _tasks.Remove(id);
            }
        }
    }
}

internal sealed class CompositionPresentationPump : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly Func<CancellationToken, ValueTask<CompositionPresentOutcome>> _present;
    private readonly ObservedTaskRunner _tasks;
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource _idle = CompletedSource();
    private bool _pending;
    private bool _running;
    private bool _disposed;

    internal CompositionPresentationPump(
        Func<CancellationToken, ValueTask<CompositionPresentOutcome>> present,
        ObservedTaskRunner tasks)
    {
        _present = present;
        _tasks = tasks;
    }

    internal void Request()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _pending = true;
            if (_running)
            {
                return;
            }
            _running = true;
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _tasks.Run("composition presentation pump", RunAsync);
        }
    }

    internal Task WaitForIdleAsync()
    {
        lock (_lock)
        {
            return _idle.Task;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task idle;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _pending = false;
            _lifetime.Cancel();
            idle = _idle.Task;
        }
        await idle.ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (TryTakePending())
            {
                CompositionPresentOutcome outcome =
                    await _present(_lifetime.Token).ConfigureAwait(false);
                if (outcome.Result == CompositionPresentResult.Backpressured &&
                    outcome.RetryAvailable is not null)
                {
                    await outcome.RetryAvailable.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                    MarkPending();
                }
                else if (outcome.ContinueRendering)
                {
                    MarkPending();
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
                _idle.TrySetResult();
                if (_pending && !_disposed)
                {
                    _running = true;
                    _idle = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _tasks.Run("composition presentation pump", RunAsync);
                }
            }
        }
    }

    private bool TryTakePending()
    {
        lock (_lock)
        {
            if (!_pending || _disposed)
            {
                return false;
            }
            _pending = false;
            return true;
        }
    }

    private void MarkPending()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _pending = true;
            }
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

internal sealed class BoundedCompositionRecovery
{
    private readonly int _maxAttempts;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal BoundedCompositionRecovery(
        int maxAttempts = 3,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        _maxAttempts = maxAttempts;
        _delay = delay ?? Task.Delay;
    }

    internal int Attempts { get; private set; }

    internal bool IsExhausted => Attempts >= _maxAttempts;

    internal async ValueTask<bool> TryRecoverAsync(
        Func<CancellationToken, Task<bool>> recover,
        CancellationToken cancellationToken)
    {
        if (IsExhausted)
        {
            return false;
        }

        int attempt = ++Attempts;
        var backoff = TimeSpan.FromMilliseconds(50 * (1 << (attempt - 1)));
        await _delay(backoff, cancellationToken).ConfigureAwait(false);
        return await recover(cancellationToken).ConfigureAwait(false);
    }

    internal void Reset() => Attempts = 0;
}

internal static class CompositionControlDisposal
{
    internal static async ValueTask DisposeAttachedAsync(
        ICompositionUiDispatcher dispatcher,
        Action clearChildVisual,
        Func<ValueTask> teardown)
    {
        var failures = new List<Exception>();
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                clearChildVisual();
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await teardown().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more attached composition resources failed to dispose.",
                failures);
        }
    }
}

internal readonly record struct ViewerPlatformDecision(
    bool UseWayland,
    bool UsesXWaylandFallback,
    string BackendName,
    string? FailureReason,
    string? Display,
    string? WaylandDisplay);

internal static class ViewerPlatformSelection
{
    internal static ViewerPlatformDecision Decide(
        string? platformOverride,
        bool isLinux,
        bool stormRequested,
        bool nativeWaylandStormSupported,
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        string? display = getEnvironmentVariable("DISPLAY");
        string? waylandDisplay = getEnvironmentVariable("WAYLAND_DISPLAY");
        bool waylandRequested = platformOverride == "linux-wayland" ||
            platformOverride is null && isLinux &&
            (string.Equals(
                getEnvironmentVariable("XDG_SESSION_TYPE"),
                "wayland",
                StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(waylandDisplay));
        if (!waylandRequested)
        {
            return new ViewerPlatformDecision(
                UseWayland: false,
                UsesXWaylandFallback: false,
                BackendName: "X11",
                FailureReason: null,
                Display: display,
                WaylandDisplay: waylandDisplay);
        }
        if (!string.IsNullOrWhiteSpace(display))
        {
            return new ViewerPlatformDecision(
                UseWayland: false,
                UsesXWaylandFallback: true,
                BackendName: "X11 / XWayland",
                FailureReason: null,
                Display: display,
                WaylandDisplay: waylandDisplay);
        }
        return new ViewerPlatformDecision(
            UseWayland: false,
            UsesXWaylandFallback: false,
            BackendName: "X11 / XWayland",
            FailureReason:
                "The Linux Viewer uses one fixed X11 shell for Storm and Vulkan switching; " +
                "a Wayland session therefore requires an XWayland DISPLAY.",
            Display: display,
            WaylandDisplay: waylandDisplay);
    }

    internal static bool HasNativeWaylandStormSupport(
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        string? value = getEnvironmentVariable("OPENUSD_STORM_NATIVE_WAYLAND");
        return string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ViewportPixelMath
{
    internal static ViewportDimensions ToPixels(
        double logicalWidth,
        double logicalHeight,
        double renderScaling)
    {
        if (logicalWidth <= 0 || logicalHeight <= 0 || renderScaling <= 0)
        {
            return ViewportDimensions.Empty;
        }
        if (!double.IsFinite(logicalWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }
        if (!double.IsFinite(logicalHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        }
        if (!double.IsFinite(renderScaling))
        {
            throw new ArgumentOutOfRangeException(nameof(renderScaling));
        }

        double scaledWidth = logicalWidth * renderScaling;
        double scaledHeight = logicalHeight * renderScaling;
        if (!double.IsFinite(scaledWidth) || scaledWidth > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }
        if (!double.IsFinite(scaledHeight) || scaledHeight > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        }

        return new ViewportDimensions(
            Math.Max(1, (int)Math.Ceiling(scaledWidth)),
            Math.Max(1, (int)Math.Ceiling(scaledHeight)));
    }
}
