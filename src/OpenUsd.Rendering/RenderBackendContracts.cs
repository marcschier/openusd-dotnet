// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies a renderer backend without exposing implementation handles.
/// </summary>
public sealed record RenderBackendIdentity
{
    /// <summary>Initializes a backend identity.</summary>
    public RenderBackendIdentity(RenderBackendKind kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Kind = kind;
        Name = name;
    }

    /// <summary>Gets the backend kind.</summary>
    public RenderBackendKind Kind { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }
}

/// <summary>
/// Identifies renderer-neutral backend features.
/// </summary>
[Flags]
public enum RenderBackendCapability
{
    /// <summary>No optional features.</summary>
    None = 0,

    /// <summary>Presentation to a viewport surface.</summary>
    Presentation = 1,

    /// <summary>Rendering without a presentation surface.</summary>
    Offscreen = 2,

    /// <summary>General compute operations.</summary>
    Compute = 4,

    /// <summary>Multisample rendering.</summary>
    Multisampling = 8,

    /// <summary>Shadow rendering.</summary>
    Shadows = 16,

    /// <summary>Explicit device-loss detection.</summary>
    DeviceLossDetection = 32,

    /// <summary>Renderer-neutral one-pixel scene picking.</summary>
    Picking = 64
}

/// <summary>
/// Describes renderer-neutral backend capabilities.
/// </summary>
public readonly record struct RenderBackendCapabilities
{
    /// <summary>Gets a minimal capability set.</summary>
    public static RenderBackendCapabilities None { get; } = new(
        RenderBackendCapability.None,
        maxSamplesPerPixel: 1,
        isSoftware: false);

    /// <summary>Initializes backend capabilities.</summary>
    public RenderBackendCapabilities(
        RenderBackendCapability features,
        int maxSamplesPerPixel,
        bool isSoftware)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSamplesPerPixel, 1);
        Features = features;
        MaxSamplesPerPixel = maxSamplesPerPixel;
        IsSoftware = isSoftware;
    }

    /// <summary>Gets supported features.</summary>
    public RenderBackendCapability Features { get; }

    /// <summary>Gets the maximum supported samples per pixel.</summary>
    public int MaxSamplesPerPixel { get; }

    /// <summary>Gets a value indicating whether the backend is software rendered.</summary>
    public bool IsSoftware { get; }

    /// <summary>Determines whether every requested feature is supported.</summary>
    public bool Supports(RenderBackendCapability capabilities) =>
        (Features & capabilities) == capabilities;
}

/// <summary>
/// Identifies the operation that produced a backend diagnostic.
/// </summary>
public enum RenderBackendDiagnosticCategory
{
    /// <summary>General backend operation.</summary>
    General,

    /// <summary>Availability probing.</summary>
    Probe,

    /// <summary>Backend initialization.</summary>
    Initialization,

    /// <summary>Frame rendering.</summary>
    Rendering,

    /// <summary>Device-loss detection or handling.</summary>
    DeviceLoss,

    /// <summary>Backend selection.</summary>
    Selection,

    /// <summary>Fallback from one backend candidate to another.</summary>
    Fallback,

    /// <summary>Backend cleanup or disposal.</summary>
    Cleanup,

    /// <summary>Renderer-neutral scene picking.</summary>
    Picking
}

/// <summary>
/// Describes one immutable backend diagnostic.
/// </summary>
public sealed record RenderBackendDiagnostic
{
    /// <summary>Initializes a backend diagnostic.</summary>
    public RenderBackendDiagnostic(
        RenderBackendKind? backend,
        RenderDiagnosticSeverity severity,
        RenderBackendDiagnosticCategory category,
        string code,
        string message)
        : this(
            backend,
            severity,
            category,
            code,
            message,
            probeFailure: null,
            initializationFailure: null,
            exceptionType: null,
            exceptionMessage: null)
    {
    }

    /// <summary>Initializes a typed backend failure or cleanup diagnostic.</summary>
    public RenderBackendDiagnostic(
        RenderBackendKind? backend,
        RenderDiagnosticSeverity severity,
        RenderBackendDiagnosticCategory category,
        string code,
        string message,
        RenderBackendProbeFailureKind? probeFailure,
        RenderBackendInitializationFailureKind? initializationFailure,
        string? exceptionType,
        string? exceptionMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        Backend = backend;
        Severity = severity;
        Category = category;
        Code = code;
        Message = message;
        ProbeFailure = probeFailure;
        InitializationFailure = initializationFailure;
        ExceptionType = exceptionType;
        ExceptionMessage = exceptionMessage;
    }

    /// <summary>Gets the backend that produced the diagnostic.</summary>
    public RenderBackendKind? Backend { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public RenderDiagnosticSeverity Severity { get; }

    /// <summary>Gets the diagnostic category.</summary>
    public RenderBackendDiagnosticCategory Category { get; }

    /// <summary>Gets the stable machine-readable code.</summary>
    public string Code { get; }

    /// <summary>Gets the human-readable message.</summary>
    public string Message { get; }

    /// <summary>Gets the typed probe failure when applicable.</summary>
    public RenderBackendProbeFailureKind? ProbeFailure { get; }

    /// <summary>Gets the typed initialization failure when applicable.</summary>
    public RenderBackendInitializationFailureKind? InitializationFailure { get; }

    /// <summary>Gets the exception type without retaining the exception object.</summary>
    public string? ExceptionType { get; }

    /// <summary>Gets the exception message without retaining the exception object.</summary>
    public string? ExceptionMessage { get; }
}

/// <summary>
/// Contains an immutable ordered set of backend diagnostics.
/// </summary>
public sealed class RenderBackendDiagnostics : IEquatable<RenderBackendDiagnostics>
{
    private readonly ImmutableArray<RenderBackendDiagnostic> _entries;

    /// <summary>Gets empty backend diagnostics.</summary>
    public static RenderBackendDiagnostics Empty { get; } = new([]);

    /// <summary>Initializes backend diagnostics by defensively copying entries.</summary>
    public RenderBackendDiagnostics(IEnumerable<RenderBackendDiagnostic> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = ImmutableArray.CreateBuilder<RenderBackendDiagnostic>();
        foreach (RenderBackendDiagnostic entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            builder.Add(entry);
        }
        _entries = builder.ToImmutable();
    }

    /// <summary>Gets diagnostic entries in stable order.</summary>
    public IReadOnlyList<RenderBackendDiagnostic> Entries => _entries;

    /// <inheritdoc />
    public bool Equals(RenderBackendDiagnostics? other) =>
        other is not null && _entries.AsSpan().SequenceEqual(other._entries.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is RenderBackendDiagnostics other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (RenderBackendDiagnostic entry in _entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two diagnostic collections have equal entries.</summary>
    public static bool operator ==(RenderBackendDiagnostics? left, RenderBackendDiagnostics? right) =>
        EqualityComparer<RenderBackendDiagnostics>.Default.Equals(left, right);

    /// <summary>Determines whether two diagnostic collections have different entries.</summary>
    public static bool operator !=(RenderBackendDiagnostics? left, RenderBackendDiagnostics? right) =>
        !(left == right);
}

/// <summary>
/// Identifies why a backend availability probe failed.
/// </summary>
public enum RenderBackendProbeFailureKind
{
    /// <summary>The probe succeeded.</summary>
    None,

    /// <summary>The current operating system is unsupported.</summary>
    UnsupportedPlatform,

    /// <summary>A required runtime component is unavailable.</summary>
    RuntimeUnavailable,

    /// <summary>No compatible device is available.</summary>
    DeviceUnavailable,

    /// <summary>The installed driver is incompatible.</summary>
    IncompatibleDriver,

    /// <summary>The process lacks required permissions.</summary>
    PermissionDenied,

    /// <summary>The probe failed for an unclassified reason.</summary>
    Unknown
}

/// <summary>
/// Reports backend availability without creating renderer resources.
/// </summary>
public sealed record RenderBackendProbeResult
{
    private RenderBackendProbeResult(
        bool isAvailable,
        RenderBackendProbeFailureKind failure,
        RenderBackendDiagnostics diagnostics)
    {
        IsAvailable = isAvailable;
        Failure = failure;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether the backend is available.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets the failure category, or <see cref="RenderBackendProbeFailureKind.None"/>.</summary>
    public RenderBackendProbeFailureKind Failure { get; }

    /// <summary>Gets probe diagnostics.</summary>
    public RenderBackendDiagnostics Diagnostics { get; }

    /// <summary>Creates a successful probe result.</summary>
    public static RenderBackendProbeResult Available(
        RenderBackendDiagnostics? diagnostics = null) =>
        new(
            isAvailable: true,
            RenderBackendProbeFailureKind.None,
            diagnostics ?? RenderBackendDiagnostics.Empty);

    /// <summary>Creates an unavailable probe result.</summary>
    public static RenderBackendProbeResult Unavailable(
        RenderBackendProbeFailureKind failure,
        RenderBackendDiagnostics? diagnostics = null)
    {
        if (failure == RenderBackendProbeFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }
        return new RenderBackendProbeResult(
            isAvailable: false,
            failure,
            diagnostics ?? RenderBackendDiagnostics.Empty);
    }
}

/// <summary>
/// Identifies why backend initialization failed.
/// </summary>
public enum RenderBackendInitializationFailureKind
{
    /// <summary>Initialization succeeded.</summary>
    None,

    /// <summary>The requested state or settings are unsupported.</summary>
    UnsupportedConfiguration,

    /// <summary>Device creation failed.</summary>
    DeviceCreationFailed,

    /// <summary>Presentation-surface creation failed.</summary>
    SurfaceCreationFailed,

    /// <summary>Required renderer resources could not be created.</summary>
    ResourceCreationFailed,

    /// <summary>The device was lost during initialization.</summary>
    DeviceLost,

    /// <summary>Initialization failed for an unclassified reason.</summary>
    Unknown
}

/// <summary>
/// Reports the result of backend initialization.
/// </summary>
public sealed record RenderBackendInitializationResult
{
    private RenderBackendInitializationResult(
        bool isSuccess,
        RenderBackendInitializationFailureKind failure,
        RenderBackendDiagnostics diagnostics)
    {
        IsSuccess = isSuccess;
        Failure = failure;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether initialization succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the initialization failure category.</summary>
    public RenderBackendInitializationFailureKind Failure { get; }

    /// <summary>Gets initialization diagnostics.</summary>
    public RenderBackendDiagnostics Diagnostics { get; }

    /// <summary>Creates a successful initialization result.</summary>
    public static RenderBackendInitializationResult Success(
        RenderBackendDiagnostics? diagnostics = null) =>
        new(
            isSuccess: true,
            RenderBackendInitializationFailureKind.None,
            diagnostics ?? RenderBackendDiagnostics.Empty);

    /// <summary>Creates a failed initialization result.</summary>
    public static RenderBackendInitializationResult Failed(
        RenderBackendInitializationFailureKind failure,
        RenderBackendDiagnostics? diagnostics = null)
    {
        if (failure == RenderBackendInitializationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }
        return new RenderBackendInitializationResult(
            isSuccess: false,
            failure,
            diagnostics ?? RenderBackendDiagnostics.Empty);
    }
}

/// <summary>
/// Identifies the outcome of one render request.
/// </summary>
public enum RenderFrameStatus
{
    /// <summary>A frame was rendered.</summary>
    Rendered,

    /// <summary>No frame was required.</summary>
    Skipped,

    /// <summary>The render device was lost.</summary>
    DeviceLost,

    /// <summary>Rendering failed without a device-loss report.</summary>
    Failed
}

/// <summary>
/// Identifies a renderer-neutral device-loss category.
/// </summary>
public enum RenderDeviceLossKind
{
    /// <summary>No device loss.</summary>
    None,

    /// <summary>The device was removed.</summary>
    Removed,

    /// <summary>The device was reset.</summary>
    Reset,

    /// <summary>The device stopped responding.</summary>
    Hung,

    /// <summary>The graphics driver failed.</summary>
    DriverFailure,

    /// <summary>An unclassified device loss occurred.</summary>
    Unknown
}

/// <summary>
/// Contains renderer-neutral frame timing and workload statistics.
/// </summary>
public readonly record struct RenderFrameStatistics
{
    /// <summary>Gets empty frame statistics.</summary>
    public static RenderFrameStatistics Empty { get; } = new(
        TimeSpan.Zero,
        gpuTime: null,
        drawCalls: 0,
        triangles: 0);

    /// <summary>Initializes frame statistics.</summary>
    public RenderFrameStatistics(
        TimeSpan cpuTime,
        TimeSpan? gpuTime,
        int drawCalls,
        long triangles)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cpuTime, TimeSpan.Zero);
        if (gpuTime is { } reportedGpuTime)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                reportedGpuTime,
                TimeSpan.Zero,
                nameof(gpuTime));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(drawCalls);
        ArgumentOutOfRangeException.ThrowIfNegative(triangles);
        CpuTime = cpuTime;
        GpuTime = gpuTime;
        DrawCalls = drawCalls;
        Triangles = triangles;
    }

    /// <summary>Gets CPU frame time.</summary>
    public TimeSpan CpuTime { get; }

    /// <summary>Gets GPU frame time when reported.</summary>
    public TimeSpan? GpuTime { get; }

    /// <summary>Gets the submitted draw-call count.</summary>
    public int DrawCalls { get; }

    /// <summary>Gets the rendered triangle count.</summary>
    public long Triangles { get; }
}

/// <summary>
/// Reports one render request and the state revision it consumed.
/// </summary>
public sealed record RenderFrameResult
{
    private RenderFrameResult(
        RenderFrameStatus status,
        ulong stateRevision,
        RenderFrameStatistics statistics,
        RenderDeviceLossKind deviceLoss,
        RenderBackendDiagnostics diagnostics)
    {
        Status = status;
        StateRevision = stateRevision;
        Statistics = statistics;
        DeviceLoss = deviceLoss;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the render outcome.</summary>
    public RenderFrameStatus Status { get; }

    /// <summary>Gets the exact state revision consumed by the backend.</summary>
    public ulong StateRevision { get; }

    /// <summary>Gets frame statistics.</summary>
    public RenderFrameStatistics Statistics { get; }

    /// <summary>Gets the device-loss category.</summary>
    public RenderDeviceLossKind DeviceLoss { get; }

    /// <summary>Gets frame diagnostics.</summary>
    public RenderBackendDiagnostics Diagnostics { get; }

    /// <summary>Creates a successfully rendered frame result.</summary>
    public static RenderFrameResult Rendered(
        ulong stateRevision,
        RenderFrameStatistics statistics,
        RenderBackendDiagnostics? diagnostics = null) =>
        new(
            RenderFrameStatus.Rendered,
            stateRevision,
            statistics,
            RenderDeviceLossKind.None,
            diagnostics ?? RenderBackendDiagnostics.Empty);

    /// <summary>Creates a skipped frame result.</summary>
    public static RenderFrameResult Skipped(
        ulong stateRevision,
        RenderBackendDiagnostics? diagnostics = null) =>
        new(
            RenderFrameStatus.Skipped,
            stateRevision,
            RenderFrameStatistics.Empty,
            RenderDeviceLossKind.None,
            diagnostics ?? RenderBackendDiagnostics.Empty);

    /// <summary>Creates a device-loss frame result.</summary>
    public static RenderFrameResult LostDevice(
        ulong stateRevision,
        RenderDeviceLossKind deviceLoss,
        RenderBackendDiagnostics? diagnostics = null)
    {
        if (deviceLoss == RenderDeviceLossKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceLoss));
        }
        return new RenderFrameResult(
            RenderFrameStatus.DeviceLost,
            stateRevision,
            RenderFrameStatistics.Empty,
            deviceLoss,
            diagnostics ?? RenderBackendDiagnostics.Empty);
    }

    /// <summary>Creates a failed frame result.</summary>
    public static RenderFrameResult Failed(
        ulong stateRevision,
        RenderBackendDiagnostics? diagnostics = null) =>
        new(
            RenderFrameStatus.Failed,
            stateRevision,
            RenderFrameStatistics.Empty,
            RenderDeviceLossKind.None,
            diagnostics ?? RenderBackendDiagnostics.Empty);
}

/// <summary>
/// Defines the renderer-neutral lifecycle implemented by render backends.
/// </summary>
public interface IRenderBackend : IAsyncDisposable
{
    /// <summary>Gets backend identity.</summary>
    RenderBackendIdentity Identity { get; }

    /// <summary>Gets declared backend capabilities.</summary>
    RenderBackendCapabilities Capabilities { get; }

    /// <summary>Probes runtime and device availability without allocating render resources.</summary>
    ValueTask<RenderBackendProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Initializes the backend with the exact current state snapshot.</summary>
    ValueTask<RenderBackendInitializationResult> InitializeAsync(
        StageRenderState initialState,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the exact immutable state snapshot consumed by later frames.</summary>
    ValueTask UpdateStateAsync(
        StageRenderState state,
        CancellationToken cancellationToken = default);

    /// <summary>Resizes renderer presentation resources.</summary>
    ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken = default);

    /// <summary>Renders one frame from the latest supplied state snapshot.</summary>
    ValueTask<RenderFrameResult> RenderAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides transactional presentation activation for hosted render backends.
/// </summary>
/// <remarks>
/// Initialization must leave the backend inactive. Deactivation and activation are transactional:
/// when either operation throws, the backend remains in its prior activation state.
/// </remarks>
public interface IRenderBackendActivationControl
{
    /// <summary>Hides or deactivates presentation without releasing owned resources.</summary>
    ValueTask DeactivateAsync(CancellationToken cancellationToken = default);

    /// <summary>Makes an initialized backend the visible active presenter.</summary>
    ValueTask ActivateAsync(CancellationToken cancellationToken = default);
}
