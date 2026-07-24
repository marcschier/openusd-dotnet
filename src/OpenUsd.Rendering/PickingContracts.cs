// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies the renderer-neutral scene element requested by a one-pixel pick.
/// </summary>
public enum RenderPickTarget
{
    /// <summary>Resolve the nearest rendered prim or instance.</summary>
    Primitive,

    /// <summary>Resolve the nearest rendered face and its element index.</summary>
    Face,

    /// <summary>Resolve the nearest rendered edge and its element index.</summary>
    Edge,

    /// <summary>Resolve the nearest rendered point and its element index.</summary>
    Point
}

/// <summary>
/// Controls optional renderer-neutral picking behavior.
/// </summary>
[Flags]
public enum RenderPickOptions
{
    /// <summary>Use the backend's normal visible-surface picking behavior.</summary>
    None = 0,

    /// <summary>Exclude back-facing surfaces from the pick.</summary>
    CullBackFaces = 1
}

/// <summary>
/// Describes an immutable one-pixel request in physical, top-left-origin viewport coordinates.
/// </summary>
public readonly record struct RenderPickRequest
{
    /// <summary>Initializes a one-pixel request.</summary>
    /// <param name="x">The zero-based physical pixel column from the viewport's left edge.</param>
    /// <param name="y">The zero-based physical pixel row from the viewport's top edge.</param>
    /// <param name="viewport">The exact physical viewport dimensions used by the request.</param>
    /// <param name="requestedStateRevision">The exact requested <see cref="StageRenderState.Revision"/>.</param>
    /// <param name="requestedSceneRevision">
    /// An optional application scene-content revision that the result must echo when bound.
    /// </param>
    /// <param name="target">The requested scene element.</param>
    /// <param name="flags">Optional picking behavior.</param>
    public RenderPickRequest(
        int x,
        int y,
        ViewportDimensions viewport,
        ulong requestedStateRevision,
        ulong? requestedSceneRevision = null,
        RenderPickTarget target = RenderPickTarget.Primitive,
        RenderPickOptions flags = RenderPickOptions.None)
        : this(
            x,
            y,
            width: 1,
            height: 1,
            viewport,
            requestedStateRevision,
            requestedSceneRevision,
            target,
            flags)
    {
    }

    /// <summary>Initializes a request region, currently restricted to exactly one pixel.</summary>
    /// <param name="x">The zero-based physical pixel column from the viewport's left edge.</param>
    /// <param name="y">The zero-based physical pixel row from the viewport's top edge.</param>
    /// <param name="width">The request width, which must be one.</param>
    /// <param name="height">The request height, which must be one.</param>
    /// <param name="viewport">The exact physical viewport dimensions used by the request.</param>
    /// <param name="requestedStateRevision">The exact requested <see cref="StageRenderState.Revision"/>.</param>
    /// <param name="requestedSceneRevision">
    /// An optional application scene-content revision that the result must echo when bound.
    /// </param>
    /// <param name="target">The requested scene element.</param>
    /// <param name="flags">Optional picking behavior.</param>
    public RenderPickRequest(
        int x,
        int y,
        int width,
        int height,
        ViewportDimensions viewport,
        ulong requestedStateRevision,
        ulong? requestedSceneRevision = null,
        RenderPickTarget target = RenderPickTarget.Primitive,
        RenderPickOptions flags = RenderPickOptions.None)
    {
        Validate(x, y, width, height, viewport, target, flags);
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Viewport = viewport;
        RequestedStateRevision = requestedStateRevision;
        RequestedSceneRevision = requestedSceneRevision;
        Target = target;
        Flags = flags;
    }

    /// <summary>Gets the zero-based physical pixel column from the viewport's left edge.</summary>
    public int X { get; }

    /// <summary>Gets the zero-based physical pixel row from the viewport's top edge.</summary>
    public int Y { get; }

    /// <summary>Gets the request width, currently always one pixel.</summary>
    public int Width { get; }

    /// <summary>Gets the request height, currently always one pixel.</summary>
    public int Height { get; }

    /// <summary>Gets the exact physical viewport dimensions used by the request.</summary>
    public ViewportDimensions Viewport { get; }

    /// <summary>Gets the exact requested <see cref="StageRenderState.Revision"/>.</summary>
    public ulong RequestedStateRevision { get; }

    /// <summary>
    /// Gets the optional requested scene-content revision that the result must echo when bound.
    /// </summary>
    public ulong? RequestedSceneRevision { get; }

    /// <summary>Gets the requested scene element.</summary>
    public RenderPickTarget Target { get; }

    /// <summary>Gets optional picking behavior.</summary>
    public RenderPickOptions Flags { get; }

    /// <summary>Determines whether actual backend revisions are stale for this request.</summary>
    /// <param name="stateRevision">The exact state revision used by the backend.</param>
    /// <param name="sceneRevision">
    /// The actual scene revision bound by the coordinator/backend, or null when no scene revision
    /// was bound. A request with a scene revision is stale when this value is null or different.
    /// </param>
    public bool IsStale(ulong stateRevision, ulong? sceneRevision) =>
        InferStaleReasons(stateRevision, sceneRevision) != RenderPickStaleReason.None;

    /// <summary>Infers stale reasons from the requested and actual revision bindings.</summary>
    /// <param name="stateRevision">The exact state revision used by the backend.</param>
    /// <param name="sceneRevision">
    /// The actual scene revision bound by the coordinator/backend, or null when no scene revision
    /// was bound. A request with a scene revision is stale when this value is null or different.
    /// </param>
    public RenderPickStaleReason InferStaleReasons(
        ulong stateRevision,
        ulong? sceneRevision)
    {
        RenderPickStaleReason reasons = RenderPickStaleReason.None;
        if (stateRevision != RequestedStateRevision)
        {
            reasons |= RenderPickStaleReason.StateRevision;
        }
        if (RequestedSceneRevision.HasValue && sceneRevision != RequestedSceneRevision)
        {
            reasons |= RenderPickStaleReason.SceneRevision;
        }
        return reasons;
    }

    internal void Validate() =>
        Validate(X, Y, Width, Height, Viewport, Target, Flags);

    private static void Validate(
        int x,
        int y,
        int width,
        int height,
        ViewportDimensions viewport,
        RenderPickTarget target,
        RenderPickOptions flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (width != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Picking currently supports one-pixel requests only.");
        }
        if (height != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Picking currently supports one-pixel requests only.");
        }
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "The viewport must have positive dimensions.");
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, viewport.Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, viewport.Height);
        if (target is not (
            RenderPickTarget.Primitive or
            RenderPickTarget.Face or
            RenderPickTarget.Edge or
            RenderPickTarget.Point))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        if ((flags & ~RenderPickOptions.CullBackFaces) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }
    }
}

/// <summary>
/// Identifies the renderer-neutral outcome of a picking request.
/// </summary>
public enum RenderPickStatus
{
    /// <summary>No rendered identity occupied the requested pixel.</summary>
    Miss,

    /// <summary>A rendered prim, instance, or subprim was resolved.</summary>
    Hit,

    /// <summary>One or more revision, request-binding, context, or backend-state inputs changed.</summary>
    Stale,

    /// <summary>The backend does not support the requested picking operation.</summary>
    Unsupported
}

/// <summary>
/// Identifies why a picking result could not be bound to the immutable request.
/// </summary>
[Flags]
public enum RenderPickStaleReason
{
    /// <summary>The result is not stale.</summary>
    None = 0,

    /// <summary>The backend consumed a different renderer-neutral state revision.</summary>
    StateRevision = 1 << 0,

    /// <summary>The backend consumed a missing or different scene-content revision.</summary>
    SceneRevision = 1 << 1,

    /// <summary>The camera no longer matched the camera bound to the request.</summary>
    Camera = 1 << 2,

    /// <summary>The physical viewport no longer matched the viewport bound to the request.</summary>
    Viewport = 1 << 3,

    /// <summary>The stage time no longer matched the time bound to the request.</summary>
    Time = 1 << 4,

    /// <summary>The backend context or device generation changed while the pick was in flight.</summary>
    ContextGeneration = 1 << 5,

    /// <summary>Other backend-owned state invalidated the pick.</summary>
    BackendState = 1 << 6
}

/// <summary>
/// Reports one immutable renderer-neutral picking result and its exact request/revision binding.
/// </summary>
public readonly record struct RenderPickResult
{
    private RenderPickResult(
        RenderPickStatus status,
        RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision,
        RenderPickStaleReason staleReasons,
        SelectionItem? item,
        Vector3? worldPosition,
        Vector3? worldNormal,
        float? normalizedDepth,
        RenderBackendKind? backendKind,
        uint? backendToken)
    {
        Status = status;
        Request = request;
        StateRevision = stateRevision;
        SceneRevision = sceneRevision;
        StaleReasons = staleReasons;
        Item = item;
        WorldPosition = worldPosition;
        WorldNormal = worldNormal;
        NormalizedDepth = normalizedDepth;
        BackendKind = backendKind;
        BackendToken = backendToken;
    }

    /// <summary>Gets the result status.</summary>
    public RenderPickStatus Status { get; }

    /// <summary>Gets the exact immutable request answered by this result.</summary>
    public RenderPickRequest Request { get; }

    /// <summary>Gets the requested <see cref="StageRenderState.Revision"/>.</summary>
    public ulong RequestedStateRevision => Request.RequestedStateRevision;

    /// <summary>Gets the optional requested scene-content revision.</summary>
    public ulong? RequestedSceneRevision => Request.RequestedSceneRevision;

    /// <summary>Gets the exact state revision used by the backend.</summary>
    public ulong StateRevision { get; }

    /// <summary>
    /// Gets the actual scene revision bound by the coordinator/backend, or null when none was bound.
    /// </summary>
    public ulong? SceneRevision { get; }

    /// <summary>
    /// Gets the reasons for a <see cref="RenderPickStatus.Stale"/> result, or
    /// <see cref="RenderPickStaleReason.None"/> for every other status.
    /// </summary>
    public RenderPickStaleReason StaleReasons { get; }

    /// <summary>Gets the authoritative renderer-neutral selection identity for a hit.</summary>
    public SelectionItem? Item { get; }

    /// <summary>Gets the absolute hit prim path, or an empty string for a non-hit result.</summary>
    public string PrimPath => Item?.PrimPath ?? string.Empty;

    /// <summary>Gets the hit instancer path, when the hit resolves an instance.</summary>
    public string? InstancerPath => Item?.InstancerPath;

    /// <summary>Gets the zero-based hit instance index, when the hit resolves an instance.</summary>
    public int? InstanceIndex => Item?.InstanceIndex;

    /// <summary>Gets the zero-based hit face, edge, point, or other subprim index.</summary>
    public int? ElementIndex => Item?.ElementIndex;

    /// <summary>Gets the world-space hit point when the backend provides one.</summary>
    public Vector3? WorldPosition { get; }

    /// <summary>Gets the world-space hit normal when the backend provides one.</summary>
    public Vector3? WorldNormal { get; }

    /// <summary>Gets normalized near-to-far depth in [0, 1] when the backend provides it.</summary>
    public float? NormalizedDepth { get; }

    /// <summary>Gets the backend kind recorded for diagnostics on a hit.</summary>
    /// <remarks>This value is not authoritative selection identity.</remarks>
    public RenderBackendKind? BackendKind { get; }

    /// <summary>Gets an optional backend-local diagnostic token for a hit.</summary>
    /// <remarks>
    /// Tokens are nonzero, backend-local, and never authoritative across frames, revisions, or backends.
    /// </remarks>
    public uint? BackendToken { get; }

    /// <summary>
    /// Creates a bound hit result with required selection identity and independently optional geometry.
    /// </summary>
    public static RenderPickResult Hit(
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision,
        in SelectionItem item,
        Vector3? worldPosition = null,
        Vector3? worldNormal = null,
        float? normalizedDepth = null,
        RenderBackendKind? backendKind = null,
        uint? backendToken = null)
    {
        request.Validate();
        ValidateCurrentBinding(request, stateRevision, sceneRevision);
        item.Validate(nameof(item));
        if (request.Target != RenderPickTarget.Primitive && item.ElementIndex is null)
        {
            throw new ArgumentException("Subprim pick targets require an element index.", nameof(item));
        }
        if (worldPosition is { } position && !IsFinite(position))
        {
            throw new ArgumentException("The world hit point must contain only finite values.", nameof(worldPosition));
        }
        if (worldNormal is { } normal && !IsFinite(normal))
        {
            throw new ArgumentException("The world hit normal must contain only finite values.", nameof(worldNormal));
        }
        if (normalizedDepth is { } depth &&
            (!float.IsFinite(depth) || depth < 0 || depth > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedDepth));
        }
        if (backendKind is { } kind && !IsKnownBackend(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(backendKind));
        }
        if (backendToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(backendToken));
        }
        if (backendToken.HasValue && !backendKind.HasValue)
        {
            throw new ArgumentException("A backend-local token requires a backend kind.", nameof(backendToken));
        }

        return new RenderPickResult(
            RenderPickStatus.Hit,
            request,
            stateRevision,
            sceneRevision,
            RenderPickStaleReason.None,
            item,
            worldPosition,
            worldNormal,
            normalizedDepth,
            backendKind,
            backendToken);
    }

    /// <summary>Creates a bound miss result with deterministic empty identity.</summary>
    public static RenderPickResult Miss(
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision)
    {
        request.Validate();
        ValidateCurrentBinding(request, stateRevision, sceneRevision);
        return EmptyIdentity(RenderPickStatus.Miss, request, stateRevision, sceneRevision);
    }

    /// <summary>
    /// Creates a stale result by inferring state and scene revision reasons.
    /// </summary>
    /// <param name="request">The immutable request answered by this result.</param>
    /// <param name="stateRevision">The exact state revision used by the backend.</param>
    /// <param name="sceneRevision">The actual scene revision bound by the coordinator/backend.</param>
    public static RenderPickResult Stale(
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision) =>
        Stale(
            request,
            stateRevision,
            sceneRevision,
            RenderPickStaleReason.None);

    /// <summary>
    /// Creates a stale result with deterministic empty identity and geometry.
    /// </summary>
    /// <param name="request">The immutable request answered by this result.</param>
    /// <param name="stateRevision">The exact state revision used by the backend.</param>
    /// <param name="sceneRevision">The actual scene revision bound by the coordinator/backend.</param>
    /// <param name="staleReasons">
    /// Additional backend-supplied stale reasons. State and scene revision mismatches are inferred
    /// and combined with this value.
    /// </param>
    public static RenderPickResult Stale(
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision,
        RenderPickStaleReason staleReasons)
    {
        request.Validate();
        if ((staleReasons & ~AllStaleReasons) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleReasons));
        }

        staleReasons |= request.InferStaleReasons(stateRevision, sceneRevision);
        if (staleReasons == RenderPickStaleReason.None)
        {
            throw new ArgumentException(
                "A stale result requires at least one inferred or backend-supplied reason.",
                nameof(staleReasons));
        }
        return EmptyIdentity(
            RenderPickStatus.Stale,
            request,
            stateRevision,
            sceneRevision,
            staleReasons);
    }

    /// <summary>Creates a bound unsupported result with deterministic empty identity.</summary>
    public static RenderPickResult Unsupported(
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision)
    {
        request.Validate();
        ValidateCurrentBinding(request, stateRevision, sceneRevision);
        return EmptyIdentity(RenderPickStatus.Unsupported, request, stateRevision, sceneRevision);
    }

    private static RenderPickResult EmptyIdentity(
        RenderPickStatus status,
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision,
        RenderPickStaleReason staleReasons = RenderPickStaleReason.None) =>
        new(
            status,
            request,
            stateRevision,
            sceneRevision,
            staleReasons,
            item: null,
            worldPosition: null,
            worldNormal: null,
            normalizedDepth: null,
            backendKind: null,
            backendToken: null);

    private static void ValidateCurrentBinding(
        in RenderPickRequest request,
        ulong stateRevision,
        ulong? sceneRevision)
    {
        if (request.IsStale(stateRevision, sceneRevision))
        {
            throw new ArgumentException(
                "Hit, miss, and unsupported results must match the requested state revision " +
                "and echo the actual bound scene revision.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsKnownBackend(RenderBackendKind kind) =>
        kind is
            RenderBackendKind.Storm or
            RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or
            RenderBackendKind.Metal;

    private const RenderPickStaleReason AllStaleReasons =
        RenderPickStaleReason.StateRevision |
        RenderPickStaleReason.SceneRevision |
        RenderPickStaleReason.Camera |
        RenderPickStaleReason.Viewport |
        RenderPickStaleReason.Time |
        RenderPickStaleReason.ContextGeneration |
        RenderPickStaleReason.BackendState;
}

/// <summary>
/// Defines optional one-pixel picking support implemented alongside <see cref="IRenderBackend"/>.
/// </summary>
/// <remarks>
/// Implementations consume their retained renderer-neutral state snapshot and echo the actual scene
/// revision bound by the coordinator/backend. Stale results carry one or more
/// <see cref="RenderPickStaleReason"/> values and deterministic empty identity and geometry.
/// Unsupported operations return <see cref="RenderPickStatus.Unsupported"/>. Operational failures
/// remain typed backend exceptions and may carry diagnostics categorized as
/// <see cref="RenderBackendDiagnosticCategory.Picking"/>.
/// </remarks>
public interface IRenderPickingBackend
{
    /// <summary>Resolves one immutable physical-pixel request without exposing native handles.</summary>
    ValueTask<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken = default);
}
