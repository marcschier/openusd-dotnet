// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Storm;

/// <summary>Identifies a native Storm render output without implying availability.</summary>
public enum StormAovKind : uint
{
    /// <summary>No output; requests reject this default value.</summary>
    None = 0,
    /// <summary>Native Float16 RGBA render color.</summary>
    Color = 1,
    /// <summary>Float32 normalized OpenGL window depth, not linear view distance.</summary>
    Depth = 2,
    /// <summary>Snapshot-local signed Hydra primitive IDs; background is minus one.</summary>
    PrimId = 3,
    /// <summary>Snapshot-local signed Hydra instance IDs; not authored instance IDs.</summary>
    InstanceId = 4,
    /// <summary>Native element IDs, absent from the pinned Storm output route.</summary>
    ElementId = 5,
    /// <summary>Raw quantized UNorm8 eye-normal output, not general signed float normals.</summary>
    Neye = 6,
    /// <summary>The unsupported plain normal output; never substituted with beauty color.</summary>
    Normal = 7,
}

/// <summary>Explicit native output-copy and managed snapshot storage admission limits.</summary>
/// <remarks>These are not whole-render, process RSS, GPU-memory, or driver-wait guarantees.</remarks>
public sealed class StormAovLimits
{
    /// <summary>Gets the maximum number of output slots.</summary>
    public const int MaximumOutputs = 8;
    /// <summary>Gets the largest admitted width or height.</summary>
    public const int MaximumDimension = 4096;
    /// <summary>Gets the largest admitted pixel count.</summary>
    public const int MaximumPixels = 1_048_576;
    /// <summary>Gets the largest number of unique native ID pairs to decode.</summary>
    public const int MaximumIdentities = 4096;
    /// <summary>Gets the largest decoded instance-context table.</summary>
    public const int MaximumInstanceContexts = 16_384;
    /// <summary>Gets the largest native UTF-8 path payload.</summary>
    public const int MaximumPathBytes = 1_048_576;
    /// <summary>Gets the native known output-copy/readback working-storage ceiling.</summary>
    public const ulong MaximumNativeWorkingBytes = 134_217_728;
    /// <summary>Gets the managed snapshot storage admission ceiling.</summary>
    public const ulong MaximumManagedBytes = 134_217_728;

    /// <summary>Gets immutable limits using the documented hard ceilings.</summary>
    public static StormAovLimits Default { get; } = new();

    /// <summary>Initializes explicit bounded snapshot limits.</summary>
    /// <param name="pixelLimit">Maximum pixels in one output.</param>
    /// <param name="identityLimit">Maximum distinct native ID pairs; zero permits only background.</param>
    /// <param name="instanceContextLimit">Maximum decoded context entries.</param>
    /// <param name="pathByteLimit">Maximum native UTF-8 path bytes.</param>
    /// <param name="nativeWorkingByteLimit">Known native output-copy and retained/replacement scratch budget.</param>
    /// <param name="managedByteLimit">Conservative managed snapshot storage budget, excluding allocator bookkeeping.</param>
    public StormAovLimits(
        int pixelLimit = MaximumPixels,
        int identityLimit = MaximumIdentities,
        int instanceContextLimit = MaximumInstanceContexts,
        int pathByteLimit = MaximumPathBytes,
        ulong nativeWorkingByteLimit = MaximumNativeWorkingBytes,
        ulong managedByteLimit = MaximumManagedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelLimit, MaximumPixels);
        ArgumentOutOfRangeException.ThrowIfNegative(identityLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(identityLimit, MaximumIdentities);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceContextLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(instanceContextLimit, MaximumInstanceContexts);
        ArgumentOutOfRangeException.ThrowIfNegative(pathByteLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pathByteLimit, MaximumPathBytes);
        ArgumentOutOfRangeException.ThrowIfZero(nativeWorkingByteLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nativeWorkingByteLimit, MaximumNativeWorkingBytes);
        ArgumentOutOfRangeException.ThrowIfZero(managedByteLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(managedByteLimit, MaximumManagedBytes);
        PixelLimit = pixelLimit;
        IdentityLimit = identityLimit;
        InstanceContextLimit = instanceContextLimit;
        PathByteLimit = pathByteLimit;
        NativeWorkingByteLimit = nativeWorkingByteLimit;
        ManagedByteLimit = managedByteLimit;
    }

    /// <summary>Gets the admitted pixels per output.</summary>
    public int PixelLimit { get; }
    /// <summary>Gets the admitted unique native ID pairs.</summary>
    public int IdentityLimit { get; }
    /// <summary>Gets the admitted decoded instance-context entries.</summary>
    public int InstanceContextLimit { get; }
    /// <summary>Gets the admitted native UTF-8 path bytes.</summary>
    public int PathByteLimit { get; }
    /// <summary>Gets the native copy/readback working-storage budget.</summary>
    public ulong NativeWorkingByteLimit { get; }
    /// <summary>Gets the conservative managed snapshot storage budget.</summary>
    public ulong ManagedByteLimit { get; }
}

/// <summary>Owns one ordered, bounded request for outputs from the same completed Storm render.</summary>
/// <remarks>
/// State and scene revisions are explicitly caller claims, not measured USD stage serials.
/// The renderer uses the requested camera/time and returns the actual applied double matrices.
/// </remarks>
public sealed class StormAovRequest
{
    private readonly CameraState _camera;

    /// <summary>Initializes a render and its ordered native output selection.</summary>
    /// <param name="width">Physical output width.</param>
    /// <param name="height">Physical output height.</param>
    /// <param name="framebuffer">Existing OpenGL presentation framebuffer.</param>
    /// <param name="outputs">Known unique output kinds; availability is reported, not presumed.</param>
    /// <param name="camera">Automatic or explicit camera, defensively copied.</param>
    /// <param name="timeCode">Finite numeric USD render time.</param>
    /// <param name="callerStateRevision">Opaque caller state revision claim.</param>
    /// <param name="callerSceneRevision">Optional opaque caller scene revision claim.</param>
    /// <param name="includeIdentities">Decode a bounded unique-pair table; requires both ID outputs.</param>
    /// <param name="useSceneLights">Use the existing explicit scene-lighting mode.</param>
    /// <param name="limits">Explicit limits, or the documented immutable defaults.</param>
    public StormAovRequest(
        int width,
        int height,
        uint framebuffer,
        IReadOnlyList<StormAovKind> outputs,
        CameraState camera = default,
        double timeCode = 0,
        ulong callerStateRevision = 0,
        ulong? callerSceneRevision = null,
        bool includeIdentities = false,
        bool useSceneLights = false,
        StormAovLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, StormAovLimits.MaximumDimension);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, StormAovLimits.MaximumDimension);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode), "Render time must be finite.");
        }
        if (camera.Mode is not (CameraMode.Automatic or CameraMode.Matrices))
        {
            throw new ArgumentException("The requested camera mode is invalid.", nameof(camera));
        }
        StormAovLimits admitted = limits ?? StormAovLimits.Default;
        if ((long)width * height > admitted.PixelLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The output exceeds its pixel limit.");
        }
        int count = outputs.Count;
        if (count is < 1 or > StormAovLimits.MaximumOutputs)
        {
            throw new ArgumentException("An AOV request requires one to eight output slots.", nameof(outputs));
        }
        Span<StormAovKind> kinds = stackalloc StormAovKind[StormAovLimits.MaximumOutputs];
        uint seen = 0;
        for (int index = 0; index < count; index++)
        {
            StormAovKind kind = outputs[index];
            if (kind is < StormAovKind.Color or > StormAovKind.Normal ||
                (seen & (1u << (int)kind)) != 0)
            {
                throw new ArgumentException("AOV kinds must be known and unique.", nameof(outputs));
            }
            seen |= 1u << (int)kind;
            kinds[index] = kind;
        }
        const uint identityKinds = (1u << (int)StormAovKind.PrimId) | (1u << (int)StormAovKind.InstanceId);
        if (includeIdentities && (seen & identityKinds) != identityKinds)
        {
            throw new ArgumentException("Identity output requires primId and instanceId.", nameof(outputs));
        }
        Width = width;
        Height = height;
        Framebuffer = framebuffer;
        TimeCode = timeCode;
        CallerStateRevision = callerStateRevision;
        CallerSceneRevision = callerSceneRevision;
        IncludeIdentities = includeIdentities;
        UseSceneLights = useSceneLights;
        Limits = admitted;
        _camera = CopyCamera(camera);
        Outputs = new OwnedReadOnlyList<StormAovKind>(kinds[..count].ToArray());
    }

    /// <summary>Gets the physical output width.</summary>
    public int Width { get; }
    /// <summary>Gets the physical output height.</summary>
    public int Height { get; }
    /// <summary>Gets the existing OpenGL presentation framebuffer.</summary>
    public uint Framebuffer { get; }
    /// <summary>Gets a defensive copy of the requested camera.</summary>
    public CameraState Camera => CopyCamera(_camera);
    /// <summary>Gets the exact requested numeric render time.</summary>
    public double TimeCode { get; }
    /// <summary>Gets the caller's state revision claim, not an observed renderer serial.</summary>
    public ulong CallerStateRevision { get; }
    /// <summary>Gets the optional caller scene revision claim, not a measured USD serial.</summary>
    public ulong? CallerSceneRevision { get; }
    /// <summary>Gets the immutable requested output order.</summary>
    public IReadOnlyList<StormAovKind> Outputs { get; }
    /// <summary>Gets whether a bounded canonical identity table is requested.</summary>
    public bool IncludeIdentities { get; }
    /// <summary>Gets whether the existing scene-lighting mode is selected.</summary>
    public bool UseSceneLights { get; }
    /// <summary>Gets immutable native/managed admission limits.</summary>
    public StormAovLimits Limits { get; }

    internal NativeRenderCamera NativeCamera => new(_camera);
    internal CameraState FrameCamera => _camera;

    private static CameraState CopyCamera(CameraState camera)
    {
        if (camera.Mode == CameraMode.Automatic)
        {
            return default;
        }
        var planes = new Vector4[camera.ClipPlaneCount];
        for (int index = 0; index < planes.Length; index++)
        {
            planes[index] = camera.GetClipPlane(index);
        }
        return new CameraState(camera.View, camera.Projection, planes);
    }
}
