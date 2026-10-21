// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics;

internal sealed unsafe partial class UsdPhysicsNativeWorld
{
    internal PhysxWorldStatusInfo InspectStatus()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle is null || _handle.IsInvalid)
        {
            throw new InvalidOperationException("Build a native world before inspecting its status.");
        }
        byte* storage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(storage, PhysxErrorScope.DefaultCapacity);
        var info = new PhysxWorldStatusInfo { StructSize = (uint)Unsafe.SizeOf<PhysxWorldStatusInfo>() };
        PhysxStatus status = PhysxNativeMethods.WorldGetStatus(_handle, ref info, ref error);
        if (status != PhysxStatus.Ok)
        {
            throw new InvalidOperationException(PhysxErrorScope.Describe(status, in error));
        }
        return info;
    }

    internal (PhysxShapeSnapshotInfo Info, PhysxShapeIdentity[] Identities) InspectRigidShapes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle is null || _handle.IsInvalid)
        {
            throw new InvalidOperationException("Build a native world before inspecting its shapes.");
        }
        byte* storage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(storage, PhysxErrorScope.DefaultCapacity);
        var info = new PhysxShapeSnapshotInfo
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxShapeSnapshotInfo>(),
            Version = 1,
        };
        PhysxStatus status = PhysxNativeMethods.WorldCopyShapeIdentities(_handle, null, 0, ref info, ref error);
        if (status is not (PhysxStatus.Ok or PhysxStatus.BufferTooSmall))
        {
            throw new InvalidOperationException(PhysxErrorScope.Describe(status, in error));
        }
        if (info.ShapeCount > PhysxAbi.MaxResultCapacity)
        {
            throw new InvalidOperationException("The native shape snapshot exceeds its declared inspection capacity.");
        }
        var identities = new PhysxShapeIdentity[info.ShapeCount];
        fixed (PhysxShapeIdentity* buffer = identities)
        {
            status = PhysxNativeMethods.WorldCopyShapeIdentities(
                _handle, buffer, (uint)identities.Length, ref info, ref error);
        }
        if (status != PhysxStatus.Ok)
        {
            throw new InvalidOperationException(PhysxErrorScope.Describe(status, in error));
        }
        if (info.ShapeCount != identities.Length)
        {
            throw new InvalidOperationException("The native shape set changed during inspection.");
        }
        return (info, identities);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PhysxShapeIdentity
{
    public ulong ActorId;
    public ulong ShapeId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PhysxShapeSnapshotInfo
{
    public uint StructSize;
    public uint Version;
    public uint ActorCount;
    public uint ShapeCount;
    public uint ConvexMeshCount;
    public uint TriangleMeshCount;
}
