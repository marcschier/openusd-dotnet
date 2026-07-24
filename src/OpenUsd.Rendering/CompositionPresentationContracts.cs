// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// Describes the compositor device and external-object capabilities visible to a presenter.
/// </summary>
public sealed class CompositionPresentationTarget
{
    /// <summary>Initializes a composition presentation target.</summary>
    public CompositionPresentationTarget(
        IEnumerable<string> imageHandleTypes,
        IEnumerable<string> semaphoreHandleTypes,
        byte[]? deviceLuid,
        byte[]? deviceUuid)
    {
        ArgumentNullException.ThrowIfNull(imageHandleTypes);
        ArgumentNullException.ThrowIfNull(semaphoreHandleTypes);
        ImageHandleTypes = Array.AsReadOnly(imageHandleTypes.ToArray());
        SemaphoreHandleTypes = Array.AsReadOnly(semaphoreHandleTypes.ToArray());
        DeviceLuid = Array.AsReadOnly(deviceLuid?.ToArray() ?? []);
        DeviceUuid = Array.AsReadOnly(deviceUuid?.ToArray() ?? []);
    }

    /// <summary>Gets supported external image handle descriptors.</summary>
    public IReadOnlyList<string> ImageHandleTypes { get; }

    /// <summary>Gets supported external semaphore handle descriptors.</summary>
    public IReadOnlyList<string> SemaphoreHandleTypes { get; }

    /// <summary>Gets the compositor adapter LUID, when available.</summary>
    public IReadOnlyList<byte> DeviceLuid { get; }

    /// <summary>Gets the compositor adapter UUID, when available.</summary>
    public IReadOnlyList<byte> DeviceUuid { get; }
}

/// <summary>
/// Reports whether a presenter can provide compatible external GPU frames.
/// </summary>
public readonly record struct CompositionPresenterProbeResult
{
    private CompositionPresenterProbeResult(bool isAvailable, string status)
    {
        IsAvailable = isAvailable;
        Status = status;
    }

    /// <summary>Gets a value indicating whether presentation is available.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets a user-facing availability description.</summary>
    public string Status { get; }

    /// <summary>Creates an available result.</summary>
    public static CompositionPresenterProbeResult Available(string status = "GPU composition available")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return new CompositionPresenterProbeResult(isAvailable: true, status);
    }

    /// <summary>Creates an unavailable result.</summary>
    public static CompositionPresenterProbeResult Unavailable(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return new CompositionPresenterProbeResult(isAvailable: false, status);
    }
}

/// <summary>
/// Identifies the pixel format of an externally shared presentation image.
/// </summary>
public enum CompositionExternalImageFormat
{
    /// <summary>Eight-bit normalized red, green, blue, and alpha channels.</summary>
    R8G8B8A8UNorm,

    /// <summary>Eight-bit normalized blue, green, red, and alpha channels.</summary>
    B8G8R8A8UNorm
}

/// <summary>
/// Determines what happens to a dedicated external handle after an import attempt.
/// </summary>
public enum CompositionExternalHandleOwnership
{
    /// <summary>
    /// The importer borrows the dedicated handle until import completion; the lease then releases it.
    /// </summary>
    BorrowedUntilImportCompleted,

    /// <summary>
    /// A successful import consumes ownership; a failed import leaves the lease responsible for release.
    /// </summary>
    TransferOnSuccessfulImport
}

/// <summary>
/// Defines how a native external handle value is recognized as invalid.
/// </summary>
public enum CompositionExternalHandleValidityPolicy
{
    /// <summary>Zero is invalid; nonzero pointer or global handle values are valid.</summary>
    NonZero,

    /// <summary>Negative file descriptors are invalid; descriptor zero is valid.</summary>
    NonNegativeFileDescriptor
}

/// <summary>
/// Owns one dedicated or duplicated native handle for exactly one compositor import attempt.
/// </summary>
/// <remarks>
/// The handle must not be the presenter's canonical resource handle. For transfer ownership,
/// <see cref="CommitTransfer"/> is called only after Avalonia reports successful import completion.
/// Each lease is committed independently; failure or cancellation of another frame resource does not
/// roll back a handle that its corresponding import already consumed.
/// Once Avalonia returns an imported object, caller cancellation does not shorten the lease lifetime:
/// the lease remains owned until that object's non-cancelable import completion succeeds or fails.
/// Disposal before that point must close or return the handle; disposal after commit must relinquish it.
/// Implementations must make disposal idempotent and <see cref="CommitTransfer"/> non-throwing.
/// </remarks>
public interface ICompositionExternalHandleLease : IAsyncDisposable
{
    /// <summary>Gets the process-local native handle value.</summary>
    public nint Handle { get; }

    /// <summary>Gets the platform handle descriptor understood by the compositor.</summary>
    public string HandleType { get; }

    /// <summary>Gets the value-validity rule for this handle type.</summary>
    CompositionExternalHandleValidityPolicy ValidityPolicy =>
        string.Equals(
            HandleType,
            "VulkanOpaquePosixFileDescriptor",
            StringComparison.Ordinal)
            ? CompositionExternalHandleValidityPolicy.NonNegativeFileDescriptor
            : CompositionExternalHandleValidityPolicy.NonZero;

    /// <summary>Gets whether the leased native handle value is invalid for its typed policy.</summary>
    bool IsInvalid => ValidityPolicy switch
    {
        CompositionExternalHandleValidityPolicy.NonNegativeFileDescriptor => Handle < 0,
        CompositionExternalHandleValidityPolicy.NonZero => Handle == 0,
        _ => true
    };

    /// <summary>Gets the import ownership rule for this dedicated handle.</summary>
    CompositionExternalHandleOwnership Ownership { get; }

    /// <summary>Commits transfer after the compositor successfully imports the handle.</summary>
    void CommitTransfer();
}

/// <summary>
/// Describes one externally shared GPU image without retaining a raw native handle.
/// </summary>
public readonly record struct CompositionExternalImage
{
    /// <summary>Initializes external image metadata.</summary>
    public CompositionExternalImage(
        string handleType,
        ViewportDimensions size,
        CompositionExternalImageFormat format,
        ulong memoryOffset = 0,
        ulong memorySize = 0,
        bool topLeftOrigin = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handleType);
        if (size.Width == 0 || size.Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        HandleType = handleType;
        Size = size;
        Format = format;
        MemoryOffset = memoryOffset;
        MemorySize = memorySize;
        TopLeftOrigin = topLeftOrigin;
    }

    /// <summary>Gets the platform handle descriptor requested from an import lease.</summary>
    public string HandleType { get; }

    /// <summary>Gets the image size in physical pixels.</summary>
    public ViewportDimensions Size { get; }

    /// <summary>Gets the image pixel format.</summary>
    public CompositionExternalImageFormat Format { get; }

    /// <summary>Gets the byte offset into externally shared memory.</summary>
    public ulong MemoryOffset { get; }

    /// <summary>Gets the externally shared memory size, or zero when not required.</summary>
    public ulong MemorySize { get; }

    /// <summary>Gets a value indicating whether the first row is the top row.</summary>
    public bool TopLeftOrigin { get; }
}

/// <summary>
/// Describes one external synchronization resource without retaining a raw native handle.
/// </summary>
public readonly record struct CompositionExternalSemaphore
{
    /// <summary>Initializes an external semaphore descriptor.</summary>
    public CompositionExternalSemaphore(long resourceId, string handleType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(handleType);
        ResourceId = resourceId;
        HandleType = handleType;
    }

    /// <summary>
    /// Gets the stable frame-local synchronization resource identifier.
    /// Wait and signal references to the same native semaphore must use the same identifier.
    /// </summary>
    public long ResourceId { get; }

    /// <summary>Gets the platform handle descriptor requested from an import lease.</summary>
    public string HandleType { get; }
}

/// <summary>
/// Identifies the synchronization mechanism used by an external frame.
/// </summary>
public enum CompositionFrameSynchronizationKind
{
    /// <summary>The platform provides implicit synchronization.</summary>
    Automatic,

    /// <summary>The image uses a keyed mutex.</summary>
    KeyedMutex,

    /// <summary>The image uses a pair of binary semaphores.</summary>
    Semaphores,

    /// <summary>The image uses a pair of timeline semaphores.</summary>
    TimelineSemaphores
}

/// <summary>
/// Describes how the compositor synchronizes access to an external frame.
/// </summary>
public sealed class CompositionFrameSynchronization
{
    private CompositionFrameSynchronization(
        CompositionFrameSynchronizationKind kind,
        long? waitSemaphoreId,
        long? signalSemaphoreId,
        ulong waitValue,
        ulong signalValue)
    {
        Kind = kind;
        WaitSemaphoreId = waitSemaphoreId;
        SignalSemaphoreId = signalSemaphoreId;
        WaitValue = waitValue;
        SignalValue = signalValue;
    }

    /// <summary>Gets automatic platform synchronization.</summary>
    public static CompositionFrameSynchronization Automatic { get; } = new(
        CompositionFrameSynchronizationKind.Automatic,
        waitSemaphoreId: null,
        signalSemaphoreId: null,
        waitValue: 0,
        signalValue: 0);

    /// <summary>Gets the synchronization mechanism.</summary>
    public CompositionFrameSynchronizationKind Kind { get; }

    /// <summary>Gets the semaphore resource waited on before compositor access.</summary>
    public long? WaitSemaphoreId { get; }

    /// <summary>Gets the semaphore resource signaled after compositor access.</summary>
    public long? SignalSemaphoreId { get; }

    /// <summary>Gets the keyed-mutex acquire key or timeline wait value.</summary>
    public ulong WaitValue { get; }

    /// <summary>Gets the keyed-mutex release key or timeline signal value.</summary>
    public ulong SignalValue { get; }

    /// <summary>Creates keyed-mutex synchronization.</summary>
    public static CompositionFrameSynchronization KeyedMutex(uint acquireKey, uint releaseKey) =>
        new(
            CompositionFrameSynchronizationKind.KeyedMutex,
            waitSemaphoreId: null,
            signalSemaphoreId: null,
            acquireKey,
            releaseKey);

    /// <summary>Creates binary semaphore synchronization.</summary>
    public static CompositionFrameSynchronization Semaphores(
        long waitSemaphoreId,
        long signalSemaphoreId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(waitSemaphoreId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(signalSemaphoreId);
        return new(
            CompositionFrameSynchronizationKind.Semaphores,
            waitSemaphoreId,
            signalSemaphoreId,
            waitValue: 0,
            signalValue: 0);
    }

    /// <summary>Creates timeline semaphore synchronization.</summary>
    public static CompositionFrameSynchronization TimelineSemaphores(
        long waitSemaphoreId,
        ulong waitValue,
        long signalSemaphoreId,
        ulong signalValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(waitSemaphoreId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(signalSemaphoreId);
        return new CompositionFrameSynchronization(
            CompositionFrameSynchronizationKind.TimelineSemaphores,
            waitSemaphoreId,
            signalSemaphoreId,
            waitValue,
            signalValue);
    }
}

/// <summary>
/// Identifies the result of rendering into one external presentation allocation.
/// </summary>
public enum CompositionFrameRenderStatus
{
    /// <summary>A frame was produced and is ready for compositor consumption.</summary>
    Presented,

    /// <summary>No new frame was required.</summary>
    NoFrame,

    /// <summary>The presentation device or allocation was lost.</summary>
    DeviceLost
}

/// <summary>
/// Reports the result of one external frame render.
/// </summary>
public readonly record struct CompositionFrameRenderResult(
    CompositionFrameRenderStatus Status,
    bool ContinueRendering,
    CompositionFrameSynchronization Synchronization);

/// <summary>
/// Represents one GPU allocation in a composition presentation ring.
/// </summary>
public interface ICompositionPresentationFrame
{
    /// <summary>Gets an identifier that remains stable for the allocation lifetime.</summary>
    long AllocationId { get; }

    /// <summary>Gets the externally shared image descriptor.</summary>
    CompositionExternalImage Image { get; }

    /// <summary>Gets synchronization resources that may be referenced by rendered frames.</summary>
    IReadOnlyList<CompositionExternalSemaphore> Semaphores { get; }

    /// <summary>Leases a dedicated image handle for one compositor import attempt.</summary>
    ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Leases a dedicated semaphore handle for one compositor import attempt.</summary>
    ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
        long resourceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns one same-sized generation of external presentation allocations.
/// </summary>
public interface ICompositionPresentationGeneration : IAsyncDisposable
{
    /// <summary>Gets the physical pixel size of every frame allocation.</summary>
    ViewportDimensions Size { get; }

    /// <summary>Gets the two or three frame allocations in presentation order.</summary>
    IReadOnlyList<ICompositionPresentationFrame> Frames { get; }
}

/// <summary>
/// Produces backend frames for import by a UI composition system.
/// </summary>
public interface ICompositionViewportPresenter : IAsyncDisposable
{
    /// <summary>Probes compatibility with the active compositor device and handle support.</summary>
    ValueTask<CompositionPresenterProbeResult> ProbeAsync(
        CompositionPresentationTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a same-sized generation containing two or three reusable allocations.</summary>
    ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
        ViewportDimensions size,
        int frameCount,
        CancellationToken cancellationToken = default);

    /// <summary>Renders into a frame that is not currently being consumed by the compositor.</summary>
    ValueTask<CompositionFrameRenderResult> RenderAsync(
        ICompositionPresentationFrame frame,
        CancellationToken cancellationToken = default);
}
