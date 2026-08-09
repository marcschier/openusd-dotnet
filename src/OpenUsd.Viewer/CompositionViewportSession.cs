// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class CompositionViewportSession : IAsyncDisposable
{
    private readonly Lock _stateLock = new();
    private readonly Lock _disposeLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _presenterGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ICompositionViewportPresenter _presenter;
    private readonly ICompositionSurfaceBridge _surface;
    private readonly ICompositionUiDispatcher _dispatcher;
    private readonly int _frameCount;
    private readonly List<Task> _retiredGenerations = [];
    private GenerationState? _generation;
    private Task? _disposeTask;
    private long _nextGenerationId;
    private long _currentGenerationId;
    private long _surfaceUpdateStartedCount;
    private long _surfaceUpdateCompletedCount;
    private long _lastPresentedGenerationId;
    private long _lastPresentedAllocationId;
    private long _generationRetirementStartedCount;
    private long _generationRetirementCompletedCount;
    private long _lastRetiredGenerationId;
    private long _importedFrameDisposalCount;
    private long _staleImportedFrameReuseCount;
    private bool _disposed;

    internal CompositionViewportSession(
        ICompositionViewportPresenter presenter,
        ICompositionSurfaceBridge surface,
        ICompositionUiDispatcher dispatcher,
        int frameCount = 3)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, 3);
        _presenter = presenter;
        _surface = surface;
        _dispatcher = dispatcher;
        _frameCount = frameCount;
    }

    internal event EventHandler? StateChanged;

    internal CompositionViewportState State { get; private set; } =
        CompositionViewportState.Detached;

    internal string Status { get; private set; } = "GPU composition: detached";

    internal CompositionViewportSessionStatistics GetStatistics() =>
        new(
            Interlocked.Read(ref _currentGenerationId),
            Interlocked.Read(ref _surfaceUpdateStartedCount),
            Interlocked.Read(ref _surfaceUpdateCompletedCount),
            Interlocked.Read(ref _lastPresentedGenerationId),
            Interlocked.Read(ref _lastPresentedAllocationId),
            Interlocked.Read(ref _generationRetirementStartedCount),
            Interlocked.Read(ref _generationRetirementCompletedCount),
            Interlocked.Read(ref _lastRetiredGenerationId),
            Interlocked.Read(ref _importedFrameDisposalCount),
            Interlocked.Read(ref _staleImportedFrameReuseCount));

    internal async ValueTask<bool> AttachAsync(
        CompositionPresentationTarget target,
        ViewportDimensions size,
        CancellationToken cancellationToken = default)
    {
        await SetStateAsync(
            CompositionViewportState.Probing,
            "GPU composition: probing compositor interop").ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        CompositionPresenterProbeResult probe = await InvokePresenterAsync(
            token => _presenter.ProbeAsync(target, token),
            linked.Token).ConfigureAwait(false);
        if (!probe.IsAvailable)
        {
            await SetStateAsync(
                CompositionViewportState.Unavailable,
                $"GPU composition unavailable: {probe.Status}").ConfigureAwait(false);
            return false;
        }

        await ResizeAsync(size, linked.Token).ConfigureAwait(false);
        if (size == ViewportDimensions.Empty)
        {
            await SetStateAsync(
                CompositionViewportState.Ready,
                "GPU composition: waiting for viewport size").ConfigureAwait(false);
        }
        return true;
    }

    internal async ValueTask ResizeAsync(
        ViewportDimensions size,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GenerationState? replacement = null;
            if (size != ViewportDimensions.Empty)
            {
                ICompositionPresentationGeneration generation =
                    await InvokePresenterAsync(
                        token => _presenter.CreateGenerationAsync(size, _frameCount, token),
                        cancellationToken).ConfigureAwait(false);
                try
                {
                    ValidateGeneration(generation, size);
                }
                catch
                {
                    await InvokePresenterAsync(
                        async _ => await generation.DisposeAsync().ConfigureAwait(false),
                        CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                replacement = new GenerationState(
                    generation,
                    Interlocked.Increment(ref _nextGenerationId),
                    DisposeGenerationAsync,
                    () => Interlocked.Increment(ref _importedFrameDisposalCount),
                    () => Interlocked.Increment(ref _staleImportedFrameReuseCount));
            }

            GenerationState? stale;
            lock (_stateLock)
            {
                stale = _generation;
                _generation = replacement;
                Interlocked.Exchange(ref _currentGenerationId, replacement?.Id ?? 0);
            }
            if (stale is not null)
            {
                Task retirement = RetireGeneration(stale);
                lock (_stateLock)
                {
                    _retiredGenerations.Add(retirement);
                }
            }

            await SetStateAsync(
                CompositionViewportState.Ready,
                size == ViewportDimensions.Empty
                    ? "GPU composition: waiting for viewport size"
                    : $"GPU composition: ready ({size.Width} × {size.Height})").ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async ValueTask<CompositionPresentOutcome> PresentNextFrameAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new CompositionPresentOutcome(CompositionPresentResult.Detached, false);
        }
        if (_surface.IsLost)
        {
            await SetStateAsync(
                CompositionViewportState.Lost,
                "GPU composition: compositor device lost").ConfigureAwait(false);
            return new CompositionPresentOutcome(CompositionPresentResult.Lost, false);
        }

        GenerationState? generation;
        GenerationLease? lease;
        Task? retryAvailable = null;
        lock (_stateLock)
        {
            generation = _generation;
            lease = generation?.TryAcquire(out retryAvailable);
        }
        if (generation is null)
        {
            return new CompositionPresentOutcome(CompositionPresentResult.Idle, false);
        }
        if (lease is null)
        {
            await SetStateAsync(
                CompositionViewportState.Backpressured,
                "GPU composition: waiting for Avalonia to consume a frame").ConfigureAwait(false);
            return new CompositionPresentOutcome(
                CompositionPresentResult.Backpressured,
                ContinueRendering: false,
                retryAvailable);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        try
        {
            IImportedCompositionFrame imported;
            try
            {
                imported = await lease.GetOrImportAsync(
                    _surface,
                    _dispatcher,
                    linked.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (_surface.IsLost)
                {
                    return await SetLostAsync(
                        "GPU composition: compositor import lost").ConfigureAwait(false);
                }
                throw;
            }
            if (imported.IsLost)
            {
                return await SetLostAsync(
                    "GPU composition: imported image or semaphore lost").ConfigureAwait(false);
            }

            CompositionFrameRenderResult rendered = await InvokePresenterAsync(
                token => _presenter.RenderAsync(lease.Frame, token),
                linked.Token).ConfigureAwait(false);
            if (rendered.Status == CompositionFrameRenderStatus.DeviceLost)
            {
                await SetStateAsync(
                    CompositionViewportState.Lost,
                    "GPU composition: presenter device lost").ConfigureAwait(false);
                return new CompositionPresentOutcome(CompositionPresentResult.Lost, false);
            }
            if (rendered.Status == CompositionFrameRenderStatus.NoFrame)
            {
                return new CompositionPresentOutcome(
                    CompositionPresentResult.Idle,
                    rendered.ContinueRendering);
            }

            try
            {
                Interlocked.Increment(ref _surfaceUpdateStartedCount);
                await SetStateAsync(
                    CompositionViewportState.Ready,
                    $"GPU composition: frame {lease.Frame.AllocationId} submitted to Avalonia compositor")
                    .ConfigureAwait(false);
                await _dispatcher.InvokeAsync(
                    () => new ValueTask(_surface.PresentAsync(imported, rendered.Synchronization)))
                    .ConfigureAwait(false);
                Interlocked.Increment(ref _surfaceUpdateCompletedCount);
                Interlocked.Exchange(ref _lastPresentedGenerationId, generation.Id);
                Interlocked.Exchange(ref _lastPresentedAllocationId, lease.Frame.AllocationId);
            }
            catch (Exception)
            {
                if (_surface.IsLost || imported.IsLost)
                {
                    return await SetLostAsync(
                        "GPU composition: compositor update lost").ConfigureAwait(false);
                }
                throw;
            }
            if (_surface.IsLost || imported.IsLost)
            {
                return await SetLostAsync(
                    "GPU composition: compositor update lost").ConfigureAwait(false);
            }
            await SetStateAsync(
                CompositionViewportState.Ready,
                $"GPU composition: frame {lease.Frame.AllocationId} presented")
                .ConfigureAwait(false);
            return new CompositionPresentOutcome(
                CompositionPresentResult.Presented,
                rendered.ContinueRendering);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return new CompositionPresentOutcome(CompositionPresentResult.Detached, false);
        }
        finally
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<CompositionPresentOutcome> SetLostAsync(string status)
    {
        await SetStateAsync(CompositionViewportState.Lost, status).ConfigureAwait(false);
        return new CompositionPresentOutcome(CompositionPresentResult.Lost, false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _lifetime.Cancel();
                _disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            GenerationState? generation;
            lock (_stateLock)
            {
                generation = _generation;
                _generation = null;
            }
            if (generation is not null)
            {
                lock (_stateLock)
                {
                    _retiredGenerations.Add(RetireGeneration(generation));
                }
            }
            Task[] retirements;
            lock (_stateLock)
            {
                retirements = [.. _retiredGenerations];
            }
            try
            {
                await Task.WhenAll(retirements).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        try
        {
            await _dispatcher.InvokeAsync(_surface.DisposeAsync).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await InvokePresenterAsync(
                async _ => await _presenter.DisposeAsync().ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await SetStateAsync(
                CompositionViewportState.Disposed,
                "GPU composition: disposed").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        _lifetime.Dispose();
        _lifecycleGate.Dispose();
        _presenterGate.Dispose();
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more composition session resources failed to dispose.",
                failures);
        }
    }

    private async ValueTask DisposeGenerationAsync(GenerationState generation)
    {
        var failures = new List<Exception>();
        try
        {
            await _dispatcher.InvokeAsync(generation.DisposeImportedAsync).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await InvokePresenterAsync(
                async _ => await generation.Generation.DisposeAsync().ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more presentation generation resources failed to dispose.",
                failures);
        }
        Interlocked.Increment(ref _generationRetirementCompletedCount);
        Interlocked.Exchange(ref _lastRetiredGenerationId, generation.Id);
    }

    private Task RetireGeneration(GenerationState generation)
    {
        Interlocked.Increment(ref _generationRetirementStartedCount);
        return generation.RetireAsync();
    }

    private async ValueTask<T> InvokePresenterAsync<T>(
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        await _presenterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _presenterGate.Release();
        }
    }

    private async ValueTask InvokePresenterAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        await _presenterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _presenterGate.Release();
        }
    }

    private async ValueTask SetStateAsync(CompositionViewportState state, string status)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = state;
            Status = status;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
    }

    private static void ValidateGeneration(
        ICompositionPresentationGeneration generation,
        ViewportDimensions requestedSize)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (generation.Size != requestedSize)
        {
            throw new InvalidOperationException("Presenter returned a generation with the wrong size.");
        }
        if (generation.Frames.Count is < 2 or > 3)
        {
            throw new InvalidOperationException(
                "Composition presentation generations must contain two or three frames.");
        }
        if (generation.Frames.Any(frame => frame.Image.Size != requestedSize))
        {
            throw new InvalidOperationException(
                "Presenter returned a frame with dimensions that do not match its generation.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class GenerationState
    {
        private readonly Lock _lock = new();
        private readonly Func<GenerationState, ValueTask> _dispose;
        private readonly Action _recordImportedFrameDisposal;
        private readonly Action _recordStaleImportedFrameReuse;
        private readonly Slot[] _slots;
        private readonly TaskCompletionSource _retired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _disposal;
        private int _nextIndex;
        private int _busyCount;
        private bool _isRetired;
        private bool _disposeStarted;
        private bool _importsDisposed;
        private TaskCompletionSource? _slotAvailable;

        internal GenerationState(
            ICompositionPresentationGeneration generation,
            long id,
            Func<GenerationState, ValueTask> dispose,
            Action recordImportedFrameDisposal,
            Action recordStaleImportedFrameReuse)
        {
            Generation = generation;
            Id = id;
            _dispose = dispose;
            _recordImportedFrameDisposal = recordImportedFrameDisposal;
            _recordStaleImportedFrameReuse = recordStaleImportedFrameReuse;
            _slots = [.. generation.Frames.Select(frame => new Slot(frame))];
        }

        internal long Id { get; }

        internal ICompositionPresentationGeneration Generation { get; }

        internal GenerationLease? TryAcquire(out Task? retryAvailable)
        {
            lock (_lock)
            {
                if (_isRetired)
                {
                    retryAvailable = null;
                    return null;
                }
                for (int offset = 0; offset < _slots.Length; offset++)
                {
                    int index = (_nextIndex + offset) % _slots.Length;
                    Slot slot = _slots[index];
                    if (slot.IsBusy)
                    {
                        continue;
                    }

                    slot.IsBusy = true;
                    _busyCount++;
                    _nextIndex = (index + 1) % _slots.Length;
                    retryAvailable = null;
                    return new GenerationLease(this, slot);
                }
                _slotAvailable ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                retryAvailable = _slotAvailable.Task;
                return null;
            }
        }

        internal Task RetireAsync()
        {
            bool dispose;
            lock (_lock)
            {
                _isRetired = true;
                dispose = _busyCount == 0 && !_disposeStarted;
                _disposeStarted |= dispose;
            }
            if (dispose)
            {
                StartDisposal();
            }
            return _retired.Task;
        }

        internal async ValueTask DisposeImportedAsync()
        {
            _importsDisposed = true;
            var failures = new List<Exception>();
            foreach (Slot slot in _slots)
            {
                if (slot.Imported is not null)
                {
                    try
                    {
                        await slot.Imported.DisposeAsync().ConfigureAwait(true);
                        _recordImportedFrameDisposal();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException(
                    "One or more imported frame slots failed to dispose.",
                    failures);
            }
        }

        internal async ValueTask<IImportedCompositionFrame> GetOrImportAsync(
            Slot slot,
            ICompositionSurfaceBridge surface,
            ICompositionUiDispatcher dispatcher,
            CancellationToken cancellationToken)
        {
            if (_importsDisposed)
            {
                _recordStaleImportedFrameReuse();
                throw new ObjectDisposedException(
                    nameof(GenerationState),
                    "A retired generation cannot reuse an imported composition frame.");
            }
            if (slot.Imported is not null)
            {
                return slot.Imported;
            }

            IImportedCompositionFrame? imported = null;
            await dispatcher.InvokeAsync(async () =>
            {
                imported = await surface.ImportAsync(slot.Frame, cancellationToken)
                    .ConfigureAwait(true);
            }).ConfigureAwait(false);
            slot.Imported = imported ??
                throw new InvalidOperationException("The composition importer returned no frame.");
            return slot.Imported;
        }

        internal void Release(Slot slot)
        {
            bool dispose;
            TaskCompletionSource? slotAvailable;
            lock (_lock)
            {
                slot.IsBusy = false;
                _busyCount--;
                dispose = _isRetired && _busyCount == 0 && !_disposeStarted;
                _disposeStarted |= dispose;
                slotAvailable = _slotAvailable;
                _slotAvailable = null;
            }
            slotAvailable?.TrySetResult();
            if (dispose)
            {
                StartDisposal();
            }
        }

        private void StartDisposal() => _disposal = DisposeCoreAsync();

        private async Task DisposeCoreAsync()
        {
            try
            {
                await _dispose(this).ConfigureAwait(false);
                _retired.TrySetResult();
            }
            catch (Exception exception)
            {
                _retired.TrySetException(exception);
            }
        }

        internal sealed class Slot(ICompositionPresentationFrame frame)
        {
            internal ICompositionPresentationFrame Frame { get; } = frame;

            internal IImportedCompositionFrame? Imported { get; set; }

            internal bool IsBusy { get; set; }
        }
    }

    private sealed class GenerationLease(
        GenerationState owner,
        GenerationState.Slot slot) : IAsyncDisposable
    {
        private bool _disposed;

        internal ICompositionPresentationFrame Frame => slot.Frame;

        internal ValueTask<IImportedCompositionFrame> GetOrImportAsync(
            ICompositionSurfaceBridge surface,
            ICompositionUiDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            owner.GetOrImportAsync(slot, surface, dispatcher, cancellationToken);

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Release(slot);
            }
            return ValueTask.CompletedTask;
        }
    }
}
