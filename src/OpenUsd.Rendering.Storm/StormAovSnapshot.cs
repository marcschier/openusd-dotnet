// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Storm;

/// <summary>One exact applied double-precision clipping equation.</summary>
/// <param name="X">First component.</param>
/// <param name="Y">Second component.</param>
/// <param name="Z">Third component.</param>
/// <param name="W">Fourth component.</param>
public readonly record struct StormAovClipPlane(double X, double Y, double Z, double W);

/// <summary>Owns the exact double matrices and clip equations applied by native Storm.</summary>
public sealed class StormAovCamera
{
    internal StormAovCamera(in NativeRenderCamera camera)
    {
        View = new OwnedReadOnlyList<double>(CopyMatrix(in camera.View));
        Projection = new OwnedReadOnlyList<double>(CopyMatrix(in camera.Projection));
        var planes = new StormAovClipPlane[(int)camera.ClipPlaneCount];
        for (int index = 0; index < planes.Length; index++)
        {
            NativeRenderClipPlane plane = GetPlane(in camera, index);
            planes[index] = new StormAovClipPlane(plane.X, plane.Y, plane.Z, plane.W);
        }
        ClipPlanes = new OwnedReadOnlyList<StormAovClipPlane>(planes);
    }

    /// <summary>Gets the actual row-major world-to-view matrix, without float narrowing.</summary>
    public IReadOnlyList<double> View { get; }
    /// <summary>Gets the actual row-major projection matrix, without float narrowing.</summary>
    public IReadOnlyList<double> Projection { get; }
    /// <summary>Gets immutable applied clip equations in native order.</summary>
    public IReadOnlyList<StormAovClipPlane> ClipPlanes { get; }

    internal static NativeRenderClipPlane GetPlane(in NativeRenderCamera camera, int index) => index switch
    {
        0 => camera.ClipPlane0,
        1 => camera.ClipPlane1,
        2 => camera.ClipPlane2,
        3 => camera.ClipPlane3,
        4 => camera.ClipPlane4,
        5 => camera.ClipPlane5,
        6 => camera.ClipPlane6,
        7 => camera.ClipPlane7,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private static double[] CopyMatrix(in NativeRenderMatrix matrix) =>
    [
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44,
    ];
}

/// <summary>Detached immutable outputs from one completed native Storm capture.</summary>
/// <remarks>
/// Native capture sequence and applied matrices describe the actual capture.
/// The explicitly named caller revisions are opaque claims, not observed USD change serials.
/// </remarks>
public sealed class StormAovSnapshot
{
    internal unsafe StormAovSnapshot(
        in StormAovNative.View view,
        StormAovOutput[] outputs,
        StormAovIdentity[] identities,
        StormAovInstanceContext[] contexts,
        uint[] identityIndices,
        ulong managedStorageUpperBound)
    {
        Width = (int)view.Width;
        Height = (int)view.Height;
        CaptureSequence = view.CaptureId;
        TimeCode = view.TimeCode;
        CallerStateRevision = view.StateRevision;
        CallerSceneRevision = (view.RevisionFlags & StormAovNative.HasSceneRevision) != 0
            ? view.SceneRevision : null;
        UseSceneLights = (view.RevisionFlags & StormAovNative.UseSceneLights) != 0;
        NativeOwnedBytes = view.OwnedBytes;
        NativeWorkingBytesUpperBound = view.AdmittedWorkingBytes;
        NativeRetainedScratchUpperBound = view.RetainedScratchUpperBoundBytes;
        ManagedStorageUpperBound = managedStorageUpperBound;
        Outputs = new OwnedReadOnlyList<StormAovOutput>(outputs);
        AppliedCamera = new StormAovCamera(in view.AppliedCamera);
        IdentityStatus = view.IdentityStatus;
        Identities = new OwnedReadOnlyList<StormAovIdentity>(identities);
        InstanceContexts = new OwnedReadOnlyList<StormAovInstanceContext>(contexts);
        IdentityIndices = new OwnedReadOnlyList<uint>(identityIndices);
    }

    /// <summary>Gets physical capture width.</summary>
    public int Width { get; }
    /// <summary>Gets physical capture height.</summary>
    public int Height { get; }
    /// <summary>Gets the renderer-local successful capture sequence, not a global frame ID.</summary>
    public ulong CaptureSequence { get; }
    /// <summary>Gets the numeric time actually passed to the completed render.</summary>
    public double TimeCode { get; }
    /// <summary>Gets the caller's opaque state revision claim.</summary>
    public ulong CallerStateRevision { get; }
    /// <summary>Gets the caller's optional scene revision claim, not a measured USD serial.</summary>
    public ulong? CallerSceneRevision { get; }
    /// <summary>Gets the explicit scene-lighting mode used by the render.</summary>
    public bool UseSceneLights { get; }
    /// <summary>Gets immutable exact applied double matrices and clip planes.</summary>
    public StormAovCamera AppliedCamera { get; }
    /// <summary>Gets outputs in the original request order, including explicit unavailable entries.</summary>
    public IReadOnlyList<StormAovOutput> Outputs { get; }
    /// <summary>Gets native owned payload and record capacities at capture time.</summary>
    public ulong NativeOwnedBytes { get; }
    /// <summary>Gets the admitted known native copy/readback working-storage upper bound.</summary>
    public ulong NativeWorkingBytesUpperBound { get; }
    /// <summary>Gets the conservative renderer-lifetime retained readback high-water.</summary>
    public ulong NativeRetainedScratchUpperBound { get; }
    /// <summary>Gets conservative admitted managed snapshot storage, not process RSS.</summary>
    public ulong ManagedStorageUpperBound { get; }
    /// <summary>Gets explicit optional identity-table availability.</summary>
    public StormAovStatus IdentityStatus { get; }
    /// <summary>Gets immutable unique native-pair identities, not a stable-ID registry.</summary>
    public IReadOnlyList<StormAovIdentity> Identities { get; }
    /// <summary>Gets the immutable native context table in native order.</summary>
    public IReadOnlyList<StormAovInstanceContext> InstanceContexts { get; }
    /// <summary>Gets top-down identity-table indices; uint.MaxValue denotes background only.</summary>
    public IReadOnlyList<uint> IdentityIndices { get; }

    /// <summary>Gets one explicitly requested output, including its availability status.</summary>
    /// <param name="kind">Requested output kind.</param>
    /// <returns>Immutable output metadata and any typed payload.</returns>
    public StormAovOutput GetOutput(StormAovKind kind)
    {
        foreach (StormAovOutput output in Outputs)
        {
            if (output.Kind == kind)
            {
                return output;
            }
        }
        throw new KeyNotFoundException($"The capture did not request '{kind}'.");
    }

    /// <summary>Gets an available output with its exact managed pixel representation.</summary>
    /// <typeparam name="TPixel">The required pixel type.</typeparam>
    /// <param name="kind">Requested output kind.</param>
    /// <returns>The typed immutable output.</returns>
    /// <exception cref="InvalidOperationException">The output is unavailable or has another pixel type.</exception>
    public StormAovOutput<TPixel> GetOutput<TPixel>(StormAovKind kind)
        where TPixel : unmanaged =>
        GetOutput(kind) is StormAovOutput<TPixel> typed
            ? typed
            : throw new InvalidOperationException($"Output '{kind}' is unavailable or has a different pixel type.");

    /// <summary>Reads detached identity for one pixel without native interop.</summary>
    /// <param name="x">Zero-based physical column.</param>
    /// <param name="y">Zero-based physical row from the top.</param>
    /// <returns>A resolved or unresolved record, or null only for background.</returns>
    /// <exception cref="InvalidOperationException">No identity-index image is available.</exception>
    public StormAovIdentity? GetIdentity(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        if (IdentityStatus != StormAovStatus.Ready)
        {
            throw new InvalidOperationException("The capture has no available identity-index image.");
        }
        uint index = IdentityIndices[(y * Width) + x];
        return index == uint.MaxValue ? null : Identities[(int)index];
    }
}
