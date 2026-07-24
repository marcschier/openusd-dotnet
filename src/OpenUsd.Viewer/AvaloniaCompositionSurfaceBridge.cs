// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class AvaloniaCompositionDispatcher : ICompositionUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public async ValueTask InvokeAsync(Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            await action().ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        await completion.Task.ConfigureAwait(false);
    }
}

internal interface ICompositionImportApi
{
    IImportedGpuImageResource ImportImage(
        IPlatformHandle handle,
        PlatformGraphicsExternalImageProperties properties);

    IImportedGpuSemaphoreResource ImportSemaphore(IPlatformHandle handle);
}

internal interface IImportedGpuResource : IAsyncDisposable
{
    Task ImportCompleted { get; }

    bool IsLost { get; }
}

internal interface IImportedGpuImageResource : IImportedGpuResource
{
    ICompositionImportedGpuImage Native { get; }
}

internal interface IImportedGpuSemaphoreResource : IImportedGpuResource
{
    ICompositionImportedGpuSemaphore Native { get; }
}

internal sealed class AvaloniaCompositionImportApi(
    ICompositionGpuInterop interop,
    ICompositionUiDispatcher dispatcher)
    : ICompositionImportApi
{
    public IImportedGpuImageResource ImportImage(
        IPlatformHandle handle,
        PlatformGraphicsExternalImageProperties properties)
    {
        VerifyAccess();
        return new ImportedGpuImageResource(
            interop.ImportImage(handle, properties),
            dispatcher);
    }

    public IImportedGpuSemaphoreResource ImportSemaphore(IPlatformHandle handle)
    {
        VerifyAccess();
        return new ImportedGpuSemaphoreResource(
            interop.ImportSemaphore(handle),
            dispatcher);
    }

    private void VerifyAccess()
    {
        if (!dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "Avalonia composition imports must run on the compositor dispatcher.");
        }
    }

    private sealed class ImportedGpuImageResource(
        ICompositionImportedGpuImage native,
        ICompositionUiDispatcher dispatcher)
        : IImportedGpuImageResource
    {
        public ICompositionImportedGpuImage Native { get; } = native;

        public Task ImportCompleted => Native.ImportCompleted;

        public bool IsLost => Native.IsLost;

        public ValueTask DisposeAsync() =>
            dispatcher.InvokeAsync(async () =>
                await Native.DisposeAsync().ConfigureAwait(true));
    }

    private sealed class ImportedGpuSemaphoreResource(
        ICompositionImportedGpuSemaphore native,
        ICompositionUiDispatcher dispatcher)
        : IImportedGpuSemaphoreResource
    {
        public ICompositionImportedGpuSemaphore Native { get; } = native;

        public Task ImportCompleted => Native.ImportCompleted;

        public bool IsLost => Native.IsLost;

        public ValueTask DisposeAsync() =>
            dispatcher.InvokeAsync(async () =>
                await Native.DisposeAsync().ConfigureAwait(true));
    }
}

internal sealed class AvaloniaCompositionFrameImporter
{
    private readonly ICompositionImportApi _importApi;
    private readonly Action? _reportLoss;

    internal AvaloniaCompositionFrameImporter(
        ICompositionImportApi importApi,
        Action? reportLoss = null)
    {
        _importApi = importApi;
        _reportLoss = reportLoss;
    }

    internal async ValueTask<AvaloniaImportedCompositionFrame> ImportAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var imports = new List<ImportLeaseState>();
        IImportedGpuImageResource? image = null;
        var semaphores = new Dictionary<long, IImportedGpuSemaphoreResource>();
        var failures = new List<Exception>();
        try
        {
            Dictionary<long, CompositionExternalSemaphore> descriptors =
                ValidateSemaphoreDescriptors(frame.Semaphores);
            ICompositionExternalHandleLease imageLease =
                await frame.LeaseImageHandleAsync(cancellationToken).ConfigureAwait(true);
            var imageImport = new ImportLeaseState(imageLease);
            imports.Add(imageImport);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateLease(imageLease, frame.Image.HandleType);
            image = _importApi.ImportImage(
                new PlatformHandle(imageLease.Handle, imageLease.HandleType),
                CreateImageProperties(frame.Image));
            imageImport.Bind(image);

            foreach ((long resourceId, CompositionExternalSemaphore descriptor) in descriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ICompositionExternalHandleLease lease =
                    await frame.LeaseSemaphoreHandleAsync(resourceId, cancellationToken)
                        .ConfigureAwait(true);
                var semaphoreImport = new ImportLeaseState(lease);
                imports.Add(semaphoreImport);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateLease(lease, descriptor.HandleType);
                IImportedGpuSemaphoreResource semaphore = _importApi.ImportSemaphore(
                    new PlatformHandle(lease.Handle, lease.HandleType));
                semaphores.Add(resourceId, semaphore);
                semaphoreImport.Bind(semaphore);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        await FinalizeImportsAsync(imports, failures).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested &&
            !failures.Any(failure => failure is OperationCanceledException))
        {
            failures.Add(new OperationCanceledException(cancellationToken));
        }
        await ReleaseLeasesAsync(imports, failures).ConfigureAwait(true);
        if (failures.Count == 0)
        {
            return new AvaloniaImportedCompositionFrame(image!, semaphores);
        }

        if (image?.IsLost == true || semaphores.Values.Any(resource => resource.IsLost))
        {
            _reportLoss?.Invoke();
        }
        await DisposeImportedAsync(image, semaphores.Values, failures).ConfigureAwait(true);
        ThrowImportFailures(failures);
        throw new UnreachableException();
    }

    private static PlatformGraphicsExternalImageProperties CreateImageProperties(
        CompositionExternalImage image) =>
        new()
        {
            Width = image.Size.Width,
            Height = image.Size.Height,
            Format = image.Format switch
            {
                CompositionExternalImageFormat.R8G8B8A8UNorm =>
                    PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
                CompositionExternalImageFormat.B8G8R8A8UNorm =>
                    PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
                _ => throw new ArgumentOutOfRangeException(nameof(image))
            },
            MemoryOffset = image.MemoryOffset,
            MemorySize = image.MemorySize,
            TopLeftOrigin = image.TopLeftOrigin
        };

    private static Dictionary<long, CompositionExternalSemaphore>
        ValidateSemaphoreDescriptors(IReadOnlyList<CompositionExternalSemaphore> semaphores)
    {
        var result = new Dictionary<long, CompositionExternalSemaphore>();
        foreach (CompositionExternalSemaphore semaphore in semaphores)
        {
            if (result.TryGetValue(semaphore.ResourceId, out CompositionExternalSemaphore existing))
            {
                if (!string.Equals(
                    existing.HandleType,
                    semaphore.HandleType,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Semaphore resource {semaphore.ResourceId} has conflicting handle types.");
                }
                continue;
            }
            result.Add(semaphore.ResourceId, semaphore);
        }
        return result;
    }

    private static void ValidateLease(
        ICompositionExternalHandleLease lease,
        string expectedHandleType)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.IsInvalid)
        {
            throw new InvalidOperationException(
                $"Import lease type '{lease.HandleType}' supplied an invalid native handle.");
        }
        if (!string.Equals(lease.HandleType, expectedHandleType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Import lease type '{lease.HandleType}' does not match '{expectedHandleType}'.");
        }
    }

    private static async ValueTask FinalizeImportsAsync(
        IEnumerable<ImportLeaseState> imports,
        List<Exception> failures)
    {
        Task<Exception?>[] finalizations =
        [
            .. imports
                .Where(import => import.IsBound)
                .Select(import => CaptureFailureAsync(
                    import.FinalizeAsync))
        ];
        foreach (Exception? failure in await Task.WhenAll(finalizations).ConfigureAwait(true))
        {
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }
    }

    private static async ValueTask DisposeImportedAsync(
        IImportedGpuImageResource? image,
        IEnumerable<IImportedGpuSemaphoreResource> semaphores,
        List<Exception> failures)
    {
        foreach (IImportedGpuSemaphoreResource semaphore in semaphores)
        {
            try
            {
                await semaphore.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (image is not null)
        {
            try
            {
                await image.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static async ValueTask ReleaseLeasesAsync(
        IEnumerable<ImportLeaseState> imports,
        List<Exception> failures)
    {
        foreach (ImportLeaseState import in imports)
        {
            try
            {
                await import.ReleaseAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static async Task<Exception?> CaptureFailureAsync(Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(true);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void ThrowImportFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        throw new AggregateException("Composition import and rollback failed.", failures);
    }

    private sealed class ImportLeaseState(ICompositionExternalHandleLease lease)
    {
        private bool _released;
        private IImportedGpuResource? _resource;

        internal bool IsBound => _resource is not null;

        internal void Bind(IImportedGpuResource resource) =>
            _resource = resource ?? throw new ArgumentNullException(nameof(resource));

        internal async ValueTask FinalizeAsync()
        {
            IImportedGpuResource resource = _resource ??
                throw new InvalidOperationException("The import lease has no imported resource.");
            var failures = new List<Exception>();
            bool imported = false;
            try
            {
                await resource.ImportCompleted.ConfigureAwait(true);
                imported = true;
                if (lease.Ownership ==
                    CompositionExternalHandleOwnership.TransferOnSuccessfulImport)
                {
                    lease.CommitTransfer();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                await ReleaseAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count != 0)
            {
                throw new AggregateException(
                    imported
                        ? "A consumed composition import lease failed to finalize."
                        : "A failed composition import lease failed to roll back.",
                    failures);
            }
        }

        internal ValueTask ReleaseAsync()
        {
            if (_released)
            {
                return ValueTask.CompletedTask;
            }
            _released = true;
            return lease.DisposeAsync();
        }
    }
}

internal sealed class AvaloniaImportedCompositionFrame(
    IImportedGpuImageResource image,
    IReadOnlyDictionary<long, IImportedGpuSemaphoreResource> semaphores)
    : IImportedCompositionFrame
{
    private bool _disposed;

    internal IImportedGpuImageResource Image { get; } = image;

    internal IReadOnlyDictionary<long, IImportedGpuSemaphoreResource> Semaphores { get; } =
        semaphores;

    public bool IsLost => Image.IsLost || Semaphores.Values.Any(value => value.IsLost);

    internal IImportedGpuSemaphoreResource GetSemaphore(long? resourceId)
    {
        long id = resourceId ??
            throw new InvalidOperationException("Frame synchronization requires a semaphore.");
        return Semaphores.TryGetValue(id, out IImportedGpuSemaphoreResource? semaphore)
            ? semaphore
            : throw new InvalidOperationException(
                $"Frame synchronization references unknown semaphore resource {id}.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var failures = new List<Exception>();
        foreach (IImportedGpuSemaphoreResource semaphore in Semaphores.Values.Distinct())
        {
            try
            {
                await semaphore.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        try
        {
            await Image.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more imported composition resources failed to dispose.",
                failures);
        }
    }
}

internal sealed class AvaloniaCompositionSurfaceBridge : ICompositionSurfaceBridge
{
    private readonly CompositionDrawingSurface _surface;
    private readonly ICompositionGpuInterop _interop;
    private readonly AvaloniaCompositionFrameImporter _importer;
    private readonly Action<ICompositionPresentationFrame>? _reportImport;
    private readonly Action<CompositionFrameSynchronization>? _reportPresent;
    private bool _disposed;
    private volatile bool _resourceLost;

    internal AvaloniaCompositionSurfaceBridge(
        CompositionDrawingSurface surface,
        ICompositionGpuInterop interop,
        ICompositionUiDispatcher dispatcher,
        Action<ICompositionPresentationFrame>? reportImport = null,
        Action<CompositionFrameSynchronization>? reportPresent = null)
    {
        _surface = surface;
        _interop = interop;
        _reportImport = reportImport;
        _reportPresent = reportPresent;
        _importer = new AvaloniaCompositionFrameImporter(
            new AvaloniaCompositionImportApi(interop, dispatcher),
            () => _resourceLost = true);
    }

    public bool IsLost => _interop.IsLost || _resourceLost;

    public async ValueTask<IImportedCompositionFrame> ImportAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken)
    {
        IImportedCompositionFrame imported =
            await _importer.ImportAsync(frame, cancellationToken).ConfigureAwait(false);
        _reportImport?.Invoke(frame);
        return imported;
    }

    public async Task PresentAsync(
        IImportedCompositionFrame importedFrame,
        CompositionFrameSynchronization synchronization)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var imported = (AvaloniaImportedCompositionFrame)importedFrame;
        Task presentation = synchronization.Kind switch
        {
            CompositionFrameSynchronizationKind.Automatic =>
                _surface.UpdateAsync(imported.Image.Native),
            CompositionFrameSynchronizationKind.KeyedMutex =>
                _surface.UpdateWithKeyedMutexAsync(
                    imported.Image.Native,
                    checked((uint)synchronization.WaitValue),
                    checked((uint)synchronization.SignalValue)),
            CompositionFrameSynchronizationKind.Semaphores =>
                _surface.UpdateWithSemaphoresAsync(
                    imported.Image.Native,
                    imported.GetSemaphore(synchronization.WaitSemaphoreId).Native,
                    imported.GetSemaphore(synchronization.SignalSemaphoreId).Native),
            CompositionFrameSynchronizationKind.TimelineSemaphores =>
                _surface.UpdateWithTimelineSemaphoresAsync(
                    imported.Image.Native,
                    imported.GetSemaphore(synchronization.WaitSemaphoreId).Native,
                    synchronization.WaitValue,
                    imported.GetSemaphore(synchronization.SignalSemaphoreId).Native,
                    synchronization.SignalValue),
            _ => throw new ArgumentOutOfRangeException(nameof(synchronization))
        };
        await presentation.ConfigureAwait(false);
        _reportPresent?.Invoke(synchronization);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
