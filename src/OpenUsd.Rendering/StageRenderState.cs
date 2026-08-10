// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Geom;

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies a stage without exposing an OpenUSD or renderer-specific handle.
/// </summary>
public sealed record StageIdentity
{
    /// <summary>Gets the identity used when no stage is loaded.</summary>
    public static StageIdentity Empty { get; } = new(string.Empty);

    /// <summary>Initializes a stage identity.</summary>
    /// <param name="identifier">The stable application-visible stage identifier.</param>
    public StageIdentity(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        Identifier = identifier;
    }

    /// <summary>Gets the stable application-visible stage identifier.</summary>
    public string Identifier { get; }
}

/// <summary>
/// Selects how a renderer obtains the camera for a frame.
/// </summary>
public enum CameraMode : uint
{
    /// <summary>Uses the renderer's fixed automatic camera.</summary>
    Automatic = 0,

    /// <summary>Uses the caller-supplied view and projection matrices.</summary>
    Matrices = 1,
}

/// <summary>
/// Describes the renderer-neutral camera for a frame.
/// </summary>
public readonly record struct CameraState
{
    private readonly ImmutableArray<Vector4> _clipPlanes;

    /// <summary>Gets the maximum number of camera clip planes accepted by the native ABI.</summary>
    public const int MaxClipPlanes = 8;

    /// <summary>Initializes an explicit matrix camera.</summary>
    /// <param name="view">The world-to-view matrix.</param>
    /// <param name="projection">The view-to-clip projection matrix.</param>
    /// <exception cref="ArgumentException">
    /// Either matrix contains a NaN or infinity value.
    /// </exception>
    public CameraState(Matrix4x4 view, Matrix4x4 projection)
        : this(view, projection, Array.Empty<Vector4>())
    {
    }

    /// <summary>Initializes an explicit matrix camera with renderer clip planes.</summary>
    /// <param name="view">The world-to-view matrix.</param>
    /// <param name="projection">The view-to-clip projection matrix.</param>
    /// <param name="clipPlanes">The view-space clipping plane equations.</param>
    /// <exception cref="ArgumentException">
    /// Either matrix or any clip plane contains a NaN or infinity value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// More than <see cref="MaxClipPlanes"/> clip planes were supplied.
    /// </exception>
    public CameraState(
        Matrix4x4 view,
        Matrix4x4 projection,
        IEnumerable<Vector4> clipPlanes)
    {
        ArgumentNullException.ThrowIfNull(clipPlanes);
        if (!IsFinite(view))
        {
            throw new ArgumentException(
                "The view matrix must contain only finite values.",
                nameof(view));
        }
        if (!IsFinite(projection))
        {
            throw new ArgumentException(
                "The projection matrix must contain only finite values.",
                nameof(projection));
        }

        ImmutableArray<Vector4> planes = clipPlanes.ToImmutableArray();
        if (planes.Length > MaxClipPlanes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipPlanes),
                $"A camera can contain at most {MaxClipPlanes} clip planes.");
        }
        foreach (Vector4 plane in planes)
        {
            if (!IsFinite(plane))
            {
                throw new ArgumentException(
                    "Clip planes must contain only finite values.",
                    nameof(clipPlanes));
            }
        }

        Mode = CameraMode.Matrices;
        View = view;
        Projection = projection;
        _clipPlanes = planes;
    }

    /// <summary>Gets the automatic camera used for initial state.</summary>
    public static CameraState Default => default;

    /// <summary>Creates a matrix camera from a stage camera prim at default time.</summary>
    /// <param name="stage">The stage containing the camera prim.</param>
    /// <param name="primPath">The absolute path to a UsdGeomCamera prim.</param>
    /// <returns>The renderer-neutral matrix camera.</returns>
    public static CameraState FromStageCamera(UsdStage stage, string primPath) =>
        FromStageCameraCore(stage, primPath, timeCode: null, ViewportDimensions.Empty);

    /// <summary>Creates a matrix camera from a stage camera prim at default time.</summary>
    /// <param name="stage">The stage containing the camera prim.</param>
    /// <param name="primPath">The absolute path to a UsdGeomCamera prim.</param>
    /// <param name="viewport">The output viewport used to conform the authored aperture.</param>
    /// <returns>The renderer-neutral matrix camera.</returns>
    public static CameraState FromStageCamera(
        UsdStage stage,
        string primPath,
        ViewportDimensions viewport) =>
        FromStageCameraCore(stage, primPath, timeCode: null, viewport);

    /// <summary>Creates a matrix camera from a stage camera prim at default time.</summary>
    /// <param name="stage">The stage containing the camera prim.</param>
    /// <param name="primPath">The absolute path to a UsdGeomCamera prim.</param>
    /// <param name="width">The output width in pixels.</param>
    /// <param name="height">The output height in pixels.</param>
    /// <returns>The renderer-neutral matrix camera.</returns>
    public static CameraState FromStageCamera(
        UsdStage stage,
        string primPath,
        int width,
        int height) =>
        FromStageCamera(stage, primPath, new ViewportDimensions(width, height));

    /// <summary>Creates a matrix camera from a time-sampled stage camera prim.</summary>
    /// <param name="stage">The stage containing the camera prim.</param>
    /// <param name="primPath">The absolute path to a UsdGeomCamera prim.</param>
    /// <param name="timeCode">The numeric time code used for optics and transform samples.</param>
    /// <returns>The renderer-neutral matrix camera.</returns>
    public static CameraState FromStageCamera(
        UsdStage stage,
        string primPath,
        double timeCode) =>
        FromStageCameraCore(stage, primPath, timeCode, ViewportDimensions.Empty);

    /// <summary>Creates a matrix camera from a time-sampled stage camera prim.</summary>
    /// <param name="stage">The stage containing the camera prim.</param>
    /// <param name="primPath">The absolute path to a UsdGeomCamera prim.</param>
    /// <param name="timeCode">The numeric time code used for optics and transform samples.</param>
    /// <param name="viewport">The output viewport used to conform the authored aperture.</param>
    /// <returns>The renderer-neutral matrix camera.</returns>
    public static CameraState FromStageCamera(
        UsdStage stage,
        string primPath,
        double timeCode,
        ViewportDimensions viewport) =>
        FromStageCameraCore(stage, primPath, timeCode, viewport);

    /// <summary>Creates a matrix camera from a time-sampled stage camera prim.</summary>
    /// <param name="stage">The stage containing the camera prim.</param>
    /// <param name="primPath">The absolute path to a UsdGeomCamera prim.</param>
    /// <param name="timeCode">The numeric time code used for optics and transform samples.</param>
    /// <param name="width">The output width in pixels.</param>
    /// <param name="height">The output height in pixels.</param>
    /// <returns>The renderer-neutral matrix camera.</returns>
    public static CameraState FromStageCamera(
        UsdStage stage,
        string primPath,
        double timeCode,
        int width,
        int height) =>
        FromStageCamera(stage, primPath, timeCode, new ViewportDimensions(width, height));

    /// <summary>Gets the camera selection mode.</summary>
    public CameraMode Mode { get; }

    /// <summary>Gets the world-to-view matrix when <see cref="Mode"/> is matrix-based.</summary>
    public Matrix4x4 View { get; }

    /// <summary>Gets the view-to-clip matrix when <see cref="Mode"/> is matrix-based.</summary>
    public Matrix4x4 Projection { get; }

    /// <summary>Gets the view-space clipping plane equations.</summary>
    public IReadOnlyList<Vector4> ClipPlanes =>
        _clipPlanes.IsDefault ? Array.Empty<Vector4>() : _clipPlanes;

    internal int ClipPlaneCount => _clipPlanes.IsDefault ? 0 : _clipPlanes.Length;

    /// <inheritdoc/>
    public bool Equals(CameraState other) =>
        Mode == other.Mode &&
        View.Equals(other.View) &&
        Projection.Equals(other.Projection) &&
        ClipPlaneCount == other.ClipPlaneCount &&
        ClipPlanesEqual(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        hash.Add(View);
        hash.Add(Projection);
        for (int index = 0; index < ClipPlaneCount; index++)
        {
            hash.Add(GetClipPlane(index));
        }
        return hash.ToHashCode();
    }

    private bool ClipPlanesEqual(CameraState other)
    {
        for (int index = 0; index < ClipPlaneCount; index++)
        {
            if (GetClipPlane(index) != other.GetClipPlane(index))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Deconstructs the explicit matrix values.</summary>
    public void Deconstruct(out Matrix4x4 view, out Matrix4x4 projection)
    {
        view = View;
        projection = Projection;
    }

    internal Vector4 GetClipPlane(int index) => _clipPlanes[index];

    private static CameraState FromStageCameraCore(
        UsdStage stage,
        string primPath,
        double? timeCode,
        ViewportDimensions viewport)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        if (timeCode is double value && !double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The stage-camera time code must be finite.");
        }
        if (!stage.HasPrim(primPath))
        {
            throw new ArgumentException(
                $"Stage prim '{primPath}' does not exist.",
                nameof(primPath));
        }

        UsdPrim prim = stage.GetPrim(primPath);
        if (!UsdGeomCamera.TryWrap(prim, out UsdGeomCamera camera))
        {
            throw new ArgumentException(
                $"Stage prim '{primPath}' is not a UsdGeomCamera.",
                nameof(primPath));
        }

        UsdMatrix4d localToWorld = timeCode is double sampleTime
            ? camera.Xformable.GetWorldTransform(sampleTime)
            : camera.Xformable.GetWorldTransform();
        UsdGeomCameraState optics = timeCode is double opticsTime
            ? camera.GetState(opticsTime)
            : camera.GetState();
        if (!localToWorld.TryInvert(out UsdMatrix4d worldToView))
        {
            string sample = timeCode is double invalidTime
                ? $" at time {invalidTime}"
                : string.Empty;
            throw new InvalidOperationException(
                $"Camera '{primPath}' has a non-finite or non-invertible " +
                $"world transform{sample}.");
        }

        return StageCameraProjectionMath.CreateCameraState(worldToView, optics, viewport);
    }

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) &&
        float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) &&
        float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) &&
        float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) &&
        float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) &&
        float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) &&
        float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) &&
        float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) &&
        float.IsFinite(value.M44);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRenderMatrix
{
    internal NativeRenderMatrix(Matrix4x4 value)
    {
        M11 = value.M11;
        M12 = value.M12;
        M13 = value.M13;
        M14 = value.M14;
        M21 = value.M21;
        M22 = value.M22;
        M23 = value.M23;
        M24 = value.M24;
        M31 = value.M31;
        M32 = value.M32;
        M33 = value.M33;
        M34 = value.M34;
        M41 = value.M41;
        M42 = value.M42;
        M43 = value.M43;
        M44 = value.M44;
    }

    internal readonly double M11;
    internal readonly double M12;
    internal readonly double M13;
    internal readonly double M14;
    internal readonly double M21;
    internal readonly double M22;
    internal readonly double M23;
    internal readonly double M24;
    internal readonly double M31;
    internal readonly double M32;
    internal readonly double M33;
    internal readonly double M34;
    internal readonly double M41;
    internal readonly double M42;
    internal readonly double M43;
    internal readonly double M44;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRenderCamera
{
    internal NativeRenderCamera(CameraState camera)
    {
        StructSize = (uint)Unsafe.SizeOf<NativeRenderCamera>();
        Mode = camera.Mode;
        View = new NativeRenderMatrix(camera.View);
        Projection = new NativeRenderMatrix(camera.Projection);
        ClipPlaneCount = checked((uint)camera.ClipPlaneCount);
        Reserved0 = 0;
        ClipPlane0 = GetClipPlane(camera, 0);
        ClipPlane1 = GetClipPlane(camera, 1);
        ClipPlane2 = GetClipPlane(camera, 2);
        ClipPlane3 = GetClipPlane(camera, 3);
        ClipPlane4 = GetClipPlane(camera, 4);
        ClipPlane5 = GetClipPlane(camera, 5);
        ClipPlane6 = GetClipPlane(camera, 6);
        ClipPlane7 = GetClipPlane(camera, 7);
    }

    internal readonly uint StructSize;
    internal readonly CameraMode Mode;
    internal readonly NativeRenderMatrix View;
    internal readonly NativeRenderMatrix Projection;
    internal readonly uint ClipPlaneCount;
    internal readonly uint Reserved0;
    internal readonly NativeRenderClipPlane ClipPlane0;
    internal readonly NativeRenderClipPlane ClipPlane1;
    internal readonly NativeRenderClipPlane ClipPlane2;
    internal readonly NativeRenderClipPlane ClipPlane3;
    internal readonly NativeRenderClipPlane ClipPlane4;
    internal readonly NativeRenderClipPlane ClipPlane5;
    internal readonly NativeRenderClipPlane ClipPlane6;
    internal readonly NativeRenderClipPlane ClipPlane7;

    private static NativeRenderClipPlane GetClipPlane(CameraState camera, int index) =>
        index < camera.ClipPlaneCount ? new NativeRenderClipPlane(camera.GetClipPlane(index)) : default;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRenderClipPlane
{
    internal NativeRenderClipPlane(Vector4 value)
    {
        X = value.X;
        Y = value.Y;
        Z = value.Z;
        W = value.W;
    }

    internal readonly double X;
    internal readonly double Y;
    internal readonly double Z;
    internal readonly double W;
}

/// <summary>
/// Describes the stage time sampled by a frame.
/// </summary>
/// <param name="TimeCode">The numeric USD time code.</param>
public readonly record struct StageTime(double TimeCode)
{
    /// <summary>Gets the initial time state.</summary>
    public static StageTime Default { get; } = new(0);
}

/// <summary>
/// Identifies one selected prim, instance, or subprim without retaining a stage object.
/// </summary>
public readonly record struct SelectionItem
{
    /// <summary>Initializes one renderer-neutral selection item.</summary>
    /// <param name="primPath">The selected absolute prim path.</param>
    /// <param name="instancerPath">The absolute instancer path, when the item is an instance.</param>
    /// <param name="instanceIndex">The zero-based instance index, when the item is an instance.</param>
    /// <param name="elementIndex">The zero-based face, edge, point, or other subprim index.</param>
    public SelectionItem(
        string primPath,
        string? instancerPath = null,
        int? instanceIndex = null,
        int? elementIndex = null)
    {
        SelectionPathValidation.ValidateAbsolutePrimPath(primPath, nameof(primPath));
        if (instancerPath is not null)
        {
            SelectionPathValidation.ValidateAbsolutePrimPath(instancerPath, nameof(instancerPath));
        }
        if (instancerPath is null != instanceIndex is null)
        {
            throw new ArgumentException(
                "An instancer path and instance index must either both be supplied or both be omitted.",
                nameof(instanceIndex));
        }
        if (instanceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        }
        if (elementIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        }

        PrimPath = primPath;
        InstancerPath = instancerPath;
        InstanceIndex = instanceIndex;
        ElementIndex = elementIndex;
    }

    /// <summary>Gets the selected absolute prim path.</summary>
    public string PrimPath { get; }

    /// <summary>Gets the absolute instancer path, when the item is an instance.</summary>
    public string? InstancerPath { get; }

    /// <summary>Gets the zero-based instance index, when the item is an instance.</summary>
    public int? InstanceIndex { get; }

    /// <summary>Gets the zero-based face, edge, point, or other subprim index.</summary>
    public int? ElementIndex { get; }

    internal void Validate(string parameterName)
    {
        SelectionPathValidation.ValidateAbsolutePrimPath(PrimPath, parameterName);
        if (InstancerPath is not null)
        {
            SelectionPathValidation.ValidateAbsolutePrimPath(InstancerPath, parameterName);
        }
        if (InstancerPath is null != InstanceIndex is null)
        {
            throw new ArgumentException(
                "The selection item contains invalid instance or element identity.",
                parameterName);
        }
        if (InstanceIndex < 0 || ElementIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Selection instance and element indices must be non-negative.");
        }
    }
}

/// <summary>
/// Contains an immutable, ordered selection of prim, instance, or subprim identities.
/// </summary>
public sealed class SelectionState : IEquatable<SelectionState>
{
    private readonly ImmutableArray<SelectionItem> _items;
    private readonly ImmutableArray<string> _primPaths;

    /// <summary>Gets an empty selection.</summary>
    public static SelectionState Empty { get; } = new(Array.Empty<string>());

    /// <summary>Initializes a selection by defensively copying prim paths.</summary>
    /// <param name="primPaths">The selected absolute prim paths.</param>
    /// <remarks>
    /// Input order is preserved. Exact duplicate strings are rejected rather than deduplicated.
    /// </remarks>
    public SelectionState(IEnumerable<string> primPaths)
    {
        ArgumentNullException.ThrowIfNull(primPaths);

        var itemBuilder = ImmutableArray.CreateBuilder<SelectionItem>();
        var pathBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (string primPath in primPaths)
        {
            var item = new SelectionItem(primPath);
            AddItem(itemBuilder, pathBuilder, item, nameof(primPaths));
        }
        _items = itemBuilder.ToImmutable();
        _primPaths = pathBuilder.ToImmutable();
    }

    /// <summary>Initializes a selection by defensively copying renderer-neutral identities.</summary>
    /// <param name="items">The selected prim, instance, or subprim identities.</param>
    /// <remarks>
    /// Input order is preserved. Exact duplicate items are rejected rather than deduplicated;
    /// selections of different instances or elements of the same prim remain distinct.
    /// </remarks>
    public SelectionState(IEnumerable<SelectionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemBuilder = ImmutableArray.CreateBuilder<SelectionItem>();
        var pathBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (SelectionItem item in items)
        {
            item.Validate(nameof(items));
            AddItem(itemBuilder, pathBuilder, item, nameof(items));
        }
        _items = itemBuilder.ToImmutable();
        _primPaths = pathBuilder.ToImmutable();
    }

    /// <summary>Gets selected prim, instance, or subprim identities in stable input order.</summary>
    public IReadOnlyList<SelectionItem> Items => _items;

    /// <summary>Gets the selected prim paths in stable order.</summary>
    /// <remarks>
    /// One path is returned for each item. Different selected instances or elements of the same
    /// prim therefore produce repeated paths while retaining distinct entries in <see cref="Items"/>.
    /// </remarks>
    public IReadOnlyList<string> PrimPaths => _primPaths;

    /// <inheritdoc />
    public bool Equals(SelectionState? other)
    {
        return other is not null
            && _items.AsSpan().SequenceEqual(other._items.AsSpan());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SelectionState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (SelectionItem item in _items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two selections have the same ordered identities.</summary>
    public static bool operator ==(SelectionState? left, SelectionState? right) =>
        EqualityComparer<SelectionState>.Default.Equals(left, right);

    /// <summary>Determines whether two selections have different ordered identities.</summary>
    public static bool operator !=(SelectionState? left, SelectionState? right) => !(left == right);

    private static void AddItem(
        ImmutableArray<SelectionItem>.Builder itemBuilder,
        ImmutableArray<string>.Builder pathBuilder,
        SelectionItem item,
        string parameterName)
    {
        foreach (SelectionItem existing in itemBuilder)
        {
            if (existing == item)
            {
                throw new ArgumentException("Exact duplicate selection items are not allowed.", parameterName);
            }
        }

        itemBuilder.Add(item);
        pathBuilder.Add(item.PrimPath);
    }
}

internal static class SelectionPathValidation
{
    internal static void ValidateAbsolutePrimPath(string? path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path.Length < 2 || path[0] != '/' || path[^1] == '/')
        {
            throw new ArgumentException(
                "The path must identify an absolute prim below the pseudo-root.",
                parameterName);
        }

        bool previousWasSlash = true;
        for (int index = 1; index < path.Length; index++)
        {
            char value = path[index];
            if (char.IsWhiteSpace(value) ||
                value is '\0' or '.' or '[' or ']' ||
                (value == '/' && previousWasSlash))
            {
                throw new ArgumentException("The path must be an absolute prim path.", parameterName);
            }
            previousWasSlash = value == '/';
        }
    }
}

/// <summary>
/// Identifies the USD purposes included in rendering.
/// </summary>
[Flags]
public enum RenderPurpose
{
    /// <summary>No purposes are included.</summary>
    None = 0,

    /// <summary>Default-purpose prims are included.</summary>
    Default = 1,

    /// <summary>Proxy-purpose prims are included.</summary>
    Proxy = 2,

    /// <summary>Render-purpose prims are included.</summary>
    Render = 4,

    /// <summary>Guide-purpose prims are included.</summary>
    Guide = 8
}

/// <summary>
/// Controls how authored visibility participates in rendering.
/// </summary>
public enum RenderVisibility
{
    /// <summary>Respect authored and inherited visibility.</summary>
    RespectAuthored,

    /// <summary>Include prims regardless of authored visibility.</summary>
    IncludeInvisible
}

/// <summary>
/// Identifies a renderer-neutral scene draw mode.
/// </summary>
public enum RenderDrawMode
{
    /// <summary>Draw smooth shaded surfaces.</summary>
    SmoothShaded,

    /// <summary>Draw flat shaded surfaces.</summary>
    FlatShaded,

    /// <summary>Draw surface wireframes.</summary>
    Wireframe,

    /// <summary>Draw points.</summary>
    Points,

    /// <summary>Draw world-space bounds.</summary>
    Bounds,

    /// <summary>Draw shaded surfaces with a wireframe overlay.</summary>
    WireframeOnSurface,

    /// <summary>Draw geometry without authored surface shading.</summary>
    GeomOnly,

    /// <summary>Draw flat-shaded geometry without authored surface shading.</summary>
    GeomFlat,

    /// <summary>Draw smooth-shaded geometry without authored surface shading.</summary>
    GeomSmooth,

    /// <summary>Draw wireframes with hidden-surface depth rejection.</summary>
    HiddenSurfaceWireframe
}

/// <summary>
/// Selects renderer-neutral geometric complexity.
/// </summary>
/// <remarks>
/// hdSilk applies complexity to emitted curve and point tessellation density only.
/// Subdivision refinement is intentionally out of scope so the default preserves the
/// existing Storm parity baseline for subdivision scenes.
/// </remarks>
public enum RenderComplexity
{
    /// <summary>Use the current parity baseline density.</summary>
    Low,

    /// <summary>Use a medium curve and point density.</summary>
    Medium,

    /// <summary>Use a high curve and point density.</summary>
    High,

    /// <summary>Use the highest curve and point density.</summary>
    VeryHigh
}

/// <summary>
/// Describes purpose, visibility, and draw-mode filtering.
/// </summary>
/// <param name="Purposes">The USD purposes included in rendering.</param>
/// <param name="Visibility">The authored visibility policy.</param>
/// <param name="DrawMode">The scene draw mode.</param>
public readonly record struct SceneDisplayState(
    RenderPurpose Purposes,
    RenderVisibility Visibility,
    RenderDrawMode DrawMode)
{
    /// <summary>Gets the initial scene display state.</summary>
    public static SceneDisplayState Default { get; } = new(
        RenderPurpose.Default | RenderPurpose.Proxy | RenderPurpose.Render,
        RenderVisibility.RespectAuthored,
        RenderDrawMode.SmoothShaded);
}

/// <summary>
/// Describes viewport pixel dimensions.
/// </summary>
public readonly record struct ViewportDimensions
{
    /// <summary>Gets dimensions for an uninitialized viewport.</summary>
    public static ViewportDimensions Empty { get; } = new(0, 0);

    /// <summary>Initializes viewport dimensions.</summary>
    /// <param name="width">The viewport width in pixels.</param>
    /// <param name="height">The viewport height in pixels.</param>
    public ViewportDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
    }

    /// <summary>Gets the viewport width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the viewport height in pixels.</summary>
    public int Height { get; }
}

/// <summary>
/// Describes renderer-neutral quality and presentation settings.
/// </summary>
public readonly record struct RenderSettings
{
    /// <summary>Gets the initial render settings.</summary>
    public static RenderSettings Default { get; } = new(
        samplesPerPixel: 1,
        enableLighting: true,
        enableShadows: true,
        new Vector4(0, 0, 0, 1),
        backfaceCulling: true,
        useSceneMaterials: true,
        RenderComplexity.Low);

    /// <summary>Initializes render settings.</summary>
    /// <param name="samplesPerPixel">The requested samples per pixel.</param>
    /// <param name="enableLighting">Whether scene lighting is enabled.</param>
    /// <param name="enableShadows">Whether shadows are enabled.</param>
    /// <param name="clearColor">The linear RGBA viewport clear color.</param>
    /// <param name="backfaceCulling">Whether back-facing single-sided surfaces are culled.</param>
    /// <param name="useSceneMaterials">Whether authored scene materials are used.</param>
    /// <param name="complexity">The requested curve and point tessellation density.</param>
    public RenderSettings(
        int samplesPerPixel,
        bool enableLighting,
        bool enableShadows,
        Vector4 clearColor,
        bool backfaceCulling = true,
        bool useSceneMaterials = true,
        RenderComplexity complexity = RenderComplexity.Low)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerPixel, 1);
        if (complexity is < RenderComplexity.Low or > RenderComplexity.VeryHigh)
        {
            throw new ArgumentOutOfRangeException(nameof(complexity));
        }
        SamplesPerPixel = samplesPerPixel;
        EnableLighting = enableLighting;
        EnableShadows = enableShadows;
        ClearColor = clearColor;
        BackfaceCulling = backfaceCulling;
        UseSceneMaterials = useSceneMaterials;
        Complexity = complexity;
    }

    /// <summary>Gets the requested samples per pixel.</summary>
    public int SamplesPerPixel { get; }

    /// <summary>Gets a value indicating whether scene lighting is enabled.</summary>
    public bool EnableLighting { get; }

    /// <summary>Gets a value indicating whether shadows are enabled.</summary>
    public bool EnableShadows { get; }

    /// <summary>Gets the linear RGBA viewport clear color.</summary>
    public Vector4 ClearColor { get; }

    /// <summary>Gets a value indicating whether back-facing single-sided surfaces are culled.</summary>
    public bool BackfaceCulling { get; }

    /// <summary>Gets a value indicating whether authored scene materials are used.</summary>
    public bool UseSceneMaterials { get; }

    /// <summary>
    /// Gets the requested curve and point tessellation density. Subdivision refinement is
    /// intentionally not controlled by this setting.
    /// </summary>
    public RenderComplexity Complexity { get; }
}

/// <summary>
/// Identifies diagnostic severity independently of a render backend.
/// </summary>
public enum RenderDiagnosticSeverity
{
    /// <summary>Informational diagnostic.</summary>
    Information,

    /// <summary>Recoverable warning.</summary>
    Warning,

    /// <summary>Rendering error.</summary>
    Error
}

/// <summary>
/// Describes one renderer-neutral diagnostic.
/// </summary>
public sealed record RenderDiagnostic
{
    /// <summary>Initializes a diagnostic.</summary>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="code">A stable machine-readable code.</param>
    /// <param name="message">The human-readable message.</param>
    public RenderDiagnostic(RenderDiagnosticSeverity severity, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        Severity = severity;
        Code = code;
        Message = message;
    }

    /// <summary>Gets the diagnostic severity.</summary>
    public RenderDiagnosticSeverity Severity { get; }

    /// <summary>Gets the stable machine-readable code.</summary>
    public string Code { get; }

    /// <summary>Gets the human-readable message.</summary>
    public string Message { get; }
}

/// <summary>
/// Contains immutable diagnostics associated with a state revision.
/// </summary>
public sealed class RenderDiagnosticsState : IEquatable<RenderDiagnosticsState>
{
    private readonly ImmutableArray<RenderDiagnostic> _entries;

    /// <summary>Gets an empty diagnostic state.</summary>
    public static RenderDiagnosticsState Empty { get; } = new([]);

    /// <summary>Initializes diagnostic state by defensively copying entries.</summary>
    /// <param name="entries">The diagnostic entries.</param>
    public RenderDiagnosticsState(IEnumerable<RenderDiagnostic> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = ImmutableArray.CreateBuilder<RenderDiagnostic>();
        foreach (RenderDiagnostic entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            builder.Add(entry);
        }
        _entries = builder.ToImmutable();
    }

    /// <summary>Gets diagnostic entries in stable order.</summary>
    public IReadOnlyList<RenderDiagnostic> Entries => _entries;

    /// <inheritdoc />
    public bool Equals(RenderDiagnosticsState? other)
    {
        return other is not null
            && _entries.AsSpan().SequenceEqual(other._entries.AsSpan());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RenderDiagnosticsState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (RenderDiagnostic entry in _entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two diagnostic states contain the same ordered entries.</summary>
    public static bool operator ==(RenderDiagnosticsState? left, RenderDiagnosticsState? right) =>
        EqualityComparer<RenderDiagnosticsState>.Default.Equals(left, right);

    /// <summary>Determines whether two diagnostic states contain different ordered entries.</summary>
    public static bool operator !=(RenderDiagnosticsState? left, RenderDiagnosticsState? right) => !(left == right);
}

/// <summary>
/// Provides an immutable renderer-neutral stage snapshot for render backends.
/// </summary>
public sealed record StageRenderState
{
    /// <summary>Gets the initial state used before a stage is loaded.</summary>
    public static StageRenderState Default { get; } = new(
        StageIdentity.Empty,
        CameraState.Default,
        StageTime.Default,
        SelectionState.Empty,
        SceneDisplayState.Default,
        ViewportDimensions.Empty,
        RenderSettings.Default,
        RenderDiagnosticsState.Empty,
        revision: 0);

    private StageRenderState(
        StageIdentity stage,
        CameraState camera,
        StageTime time,
        SelectionState selection,
        SceneDisplayState display,
        ViewportDimensions viewport,
        RenderSettings renderSettings,
        RenderDiagnosticsState diagnostics,
        ulong revision)
    {
        Stage = stage;
        Camera = camera;
        Time = time;
        Selection = selection;
        Display = display;
        Viewport = viewport;
        RenderSettings = renderSettings;
        Diagnostics = diagnostics;
        Revision = revision;
    }

    /// <summary>Gets the stage identity.</summary>
    public StageIdentity Stage { get; }

    /// <summary>Gets the camera state.</summary>
    public CameraState Camera { get; }

    /// <summary>Gets the sampled stage time.</summary>
    public StageTime Time { get; }

    /// <summary>Gets the selected prim paths.</summary>
    public SelectionState Selection { get; }

    /// <summary>Gets scene filtering and draw-mode state.</summary>
    public SceneDisplayState Display { get; }

    /// <summary>Gets viewport pixel dimensions.</summary>
    public ViewportDimensions Viewport { get; }

    /// <summary>Gets renderer-neutral render settings.</summary>
    public RenderSettings RenderSettings { get; }

    /// <summary>Gets diagnostics associated with this state.</summary>
    public RenderDiagnosticsState Diagnostics { get; }

    /// <summary>Gets the monotonic revision for this state lineage.</summary>
    public ulong Revision { get; }

    /// <summary>Creates initial state for a loaded stage.</summary>
    /// <param name="stage">The stage identity.</param>
    /// <returns>A revision-zero immutable state.</returns>
    public static StageRenderState Create(StageIdentity stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return Default.With(stage: stage, advanceRevision: false);
    }

    /// <summary>Returns state with a different stage identity.</summary>
    public StageRenderState WithStage(StageIdentity stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return Stage == stage ? this : With(stage: stage);
    }

    /// <summary>Returns state with a different camera.</summary>
    public StageRenderState WithCamera(CameraState camera) =>
        Camera == camera ? this : With(camera: camera);

    /// <summary>Returns state with a different sampled time.</summary>
    public StageRenderState WithTime(StageTime time) =>
        Time == time ? this : With(time: time);

    /// <summary>Returns state with a different selection.</summary>
    public StageRenderState WithSelection(SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return Selection == selection ? this : With(selection: selection);
    }

    /// <summary>Returns state with different scene filtering or draw mode.</summary>
    public StageRenderState WithDisplay(SceneDisplayState display) =>
        Display == display ? this : With(display: display);

    /// <summary>Returns state with different viewport dimensions.</summary>
    public StageRenderState WithViewport(ViewportDimensions viewport) =>
        Viewport == viewport ? this : With(viewport: viewport);

    /// <summary>Returns state with different renderer-neutral settings.</summary>
    public StageRenderState WithRenderSettings(RenderSettings renderSettings) =>
        RenderSettings == renderSettings ? this : With(renderSettings: renderSettings);

    /// <summary>Returns state with different diagnostics.</summary>
    public StageRenderState WithDiagnostics(RenderDiagnosticsState diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return Diagnostics == diagnostics ? this : With(diagnostics: diagnostics);
    }

    /// <summary>
    /// Returns an otherwise identical snapshot with its monotonic revision advanced.
    /// </summary>
    /// <remarks>
    /// Use this when stage contents change without changing renderer-neutral camera,
    /// time, selection, display, viewport, settings, or diagnostics values.
    /// </remarks>
    public StageRenderState AdvanceRevision() => With();

    private StageRenderState With(
        StageIdentity? stage = null,
        CameraState? camera = null,
        StageTime? time = null,
        SelectionState? selection = null,
        SceneDisplayState? display = null,
        ViewportDimensions? viewport = null,
        RenderSettings? renderSettings = null,
        RenderDiagnosticsState? diagnostics = null,
        bool advanceRevision = true)
    {
        return new StageRenderState(
            stage ?? Stage,
            camera ?? Camera,
            time ?? Time,
            selection ?? Selection,
            display ?? Display,
            viewport ?? Viewport,
            renderSettings ?? RenderSettings,
            diagnostics ?? Diagnostics,
            advanceRevision ? checked(Revision + 1) : Revision);
    }
}
