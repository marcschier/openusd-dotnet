// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PhysxMeshInput
{
    public uint Approximation;
    public uint PointOffset;
    public uint PointCount;
    public uint IndexOffset;
    public uint IndexCount;
    public uint MaxHulls;
    public uint MaxVertices;
    public uint Resolution;
    public double VolumeErrorPercent;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PhysxMeshHull
{
    public uint InputIndex;
    public uint PointOffset;
    public uint PointCount;
    public uint Reserved;
}

internal sealed class PhysxMeshPreparationHandle : SafeHandle
{
    public PhysxMeshPreparationHandle() : base(nint.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        PhysxNativeMethods.MeshPreparationRelease(handle);
        return true;
    }
}

internal sealed class PhysxMeshPreparationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static unsafe class PhysxMeshPreparation
{
    internal const int MaxBatch = 64;
    internal const int MaxPoints = 4 * 1024 * 1024;
    internal const int MaxIndices = 12 * 1024 * 1024;

    internal static (PhysxMeshHull[] Hulls, PhysxVec3f[] Points) Compute(
        ReadOnlySpan<PhysxMeshInput> inputs, ReadOnlySpan<PhysxVec3f> points,
        ReadOnlySpan<uint> indices, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (inputs.IsEmpty || inputs.Length > MaxBatch || points.Length > MaxPoints ||
            indices.Length > MaxIndices || Unsafe.SizeOf<PhysxMeshInput>() != 40 ||
            Unsafe.SizeOf<PhysxMeshHull>() != 16)
        {
            throw new ArgumentException("The collider preparation batch exceeds the native contract.");
        }
        byte* storage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(storage, PhysxErrorScope.DefaultCapacity);
        PhysxStatus status = PhysxNativeMethods.MeshPreparationCreate(1,
            out PhysxMeshPreparationHandle handle, ref error);
        using (handle)
        {
            Check(status, in error, cancellationToken);
            using CancellationTokenRegistration cancellation = cancellationToken.Register(
                static state => PhysxNativeMethods.MeshPreparationCancel((PhysxMeshPreparationHandle)state!), handle);
            fixed (PhysxMeshInput* inputBuffer = inputs)
            fixed (PhysxVec3f* pointBuffer = points)
            fixed (uint* indexBuffer = indices)
            {
                status = PhysxNativeMethods.MeshPreparationCompute(handle,
                    inputBuffer, (uint)inputs.Length, pointBuffer, (uint)points.Length,
                    indexBuffer, (uint)indices.Length, ref error);
            }
            Check(status, in error, cancellationToken);
            status = PhysxNativeMethods.MeshPreparationCopy(handle, null, 0, null, 0,
                out uint hullCount, out uint pointCount, ref error);
            if (status != PhysxStatus.BufferTooSmall)
            {
                Check(status, in error, cancellationToken);
                throw new PhysxMeshPreparationException("Collider preparation returned no complete hull set.");
            }
            if (hullCount == 0 || hullCount > inputs.Length * 256 || pointCount == 0 || pointCount > MaxPoints)
            {
                throw new PhysxMeshPreparationException("Collider preparation returned counts outside its bounds.");
            }
            var hulls = new PhysxMeshHull[hullCount];
            var resultPoints = new PhysxVec3f[pointCount];
            fixed (PhysxMeshHull* hullBuffer = hulls)
            fixed (PhysxVec3f* pointBuffer = resultPoints)
            {
                status = PhysxNativeMethods.MeshPreparationCopy(handle,
                    hullBuffer, hullCount, pointBuffer, pointCount,
                    out uint copiedHulls, out uint copiedPoints, ref error);
                Check(status, in error, cancellationToken);
                if (copiedHulls != hullCount || copiedPoints != pointCount)
                {
                    throw new PhysxMeshPreparationException("Prepared geometry changed during its complete copy.");
                }
            }
            foreach (PhysxMeshHull hull in hulls)
            {
                if (hull.Reserved != 0 || hull.InputIndex >= inputs.Length ||
                    hull.PointCount < 4 || hull.PointCount > 255 ||
                    hull.PointOffset > pointCount || hull.PointCount > pointCount - hull.PointOffset)
                {
                    throw new PhysxMeshPreparationException("Collider preparation returned an invalid hull range.");
                }
            }
            foreach (PhysxVec3f point in resultPoints)
            {
                if (!point.IsFinite)
                {
                    throw new PhysxMeshPreparationException("Collider preparation returned a nonfinite hull point.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return (hulls, resultPoints);
        }
    }

    private static void Check(PhysxStatus status, in PhysxErrorBuffer error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (status != PhysxStatus.Ok)
        {
            throw new PhysxMeshPreparationException(PhysxErrorScope.Describe(status, in error));
        }
    }
}

internal static unsafe partial class PhysxNativeMethods
{
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_mesh_preparation_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial PhysxStatus MeshPreparationCreate(
        uint version, out PhysxMeshPreparationHandle handle, ref PhysxErrorBuffer error);

    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_mesh_preparation_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void MeshPreparationRelease(nint handle);

    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_mesh_preparation_cancel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void MeshPreparationCancel(PhysxMeshPreparationHandle handle);

    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_mesh_preparation_compute")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial PhysxStatus MeshPreparationCompute(
        PhysxMeshPreparationHandle handle, PhysxMeshInput* inputs, uint inputCount,
        PhysxVec3f* points, uint pointCount, uint* indices, uint indexCount, ref PhysxErrorBuffer error);

    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_mesh_preparation_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial PhysxStatus MeshPreparationCopy(
        PhysxMeshPreparationHandle handle, PhysxMeshHull* hulls, uint hullCapacity,
        PhysxVec3f* points, uint pointCapacity, out uint hullCount, out uint pointCount, ref PhysxErrorBuffer error);
}
