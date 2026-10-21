// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

internal static unsafe partial class PhysxNativeMethods
{
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_copy_shape_identities")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial PhysxStatus WorldCopyShapeIdentities(
        PhysxWorldHandle world, PhysxShapeIdentity* identities, uint capacity,
        ref PhysxShapeSnapshotInfo info, ref PhysxErrorBuffer error);
}
