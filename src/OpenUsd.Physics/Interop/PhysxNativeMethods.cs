// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Owns the lifetime of one native <c>openusd_physx_world</c>.
/// </summary>
/// <remarks>
/// The raw pointer never leaves this handle: every entry point takes the handle itself, so no caller
/// can observe, store, or accidentally outlive the native pointer. Releasing the last world also
/// releases the reference-counted process runtime that owns the single PhysX foundation instance.
/// </remarks>
internal sealed class PhysxWorldHandle : SafeHandle
{
    /// <summary>Initializes an invalid handle; the marshaller fills it on a successful create call.</summary>
    public PhysxWorldHandle()
        : base(nint.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == nint.Zero;

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        PhysxNativeMethods.WorldRelease(handle);
        return true;
    }
}

/// <summary>
/// Declares every entry point of the retained physics world C ABI, version 1.
/// </summary>
/// <remarks>
/// Every declaration uses an explicit entry point name, the C calling convention, and
/// <see cref="nuint"/> for <c>size_t</c>. The ABI contains no boolean, so no marshalling ambiguity
/// exists; every buffer is caller owned and only borrowed for the duration of a call.
/// </remarks>
internal static unsafe partial class PhysxNativeMethods
{
    /// <summary>Reports the exact ABI version, page magic, and native record sizes.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_get_abi")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldGetAbi(
        ref PhysxAbiInfo info,
        ref PhysxErrorBuffer error);

    /// <summary>Reports runtime capabilities; requires an exact ABI match.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_get_capabilities")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldGetCapabilities(
        uint abiVersion,
        ref PhysxCapabilitiesInfo capabilities,
        ref PhysxErrorBuffer error);

    /// <summary>Computes the stable identity of a prim path plus instance domain and index.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_identity_compute")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus IdentityCompute(
        byte* path,
        nuint pathLength,
        uint instanceDomain,
        uint instanceIndex,
        out ulong id,
        ref PhysxErrorBuffer error);

    /// <summary>Validates a pointer-free build page without creating any simulation object.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_page_validate")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus PageValidate(
        void* page,
        nuint pageSize,
        ref PhysxPageValidation validation,
        ref PhysxErrorBuffer error);

    /// <summary>Creates an empty retained world.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_create")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldCreate(
        ref PhysxWorldDesc desc,
        out PhysxWorldHandle world,
        ref PhysxErrorBuffer error);

    /// <summary>Releases a world. Passing a null pointer is a no-op.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_release")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void WorldRelease(nint world);

    /// <summary>Validates and applies a build page; the page is never retained after the call.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_build")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldBuild(
        PhysxWorldHandle world,
        void* page,
        nuint pageSize,
        ref PhysxPageValidation validation,
        ref PhysxErrorBuffer error);

    /// <summary>Restores the built state, optionally overridden by a batch of body states.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldReset(
        PhysxWorldHandle world,
        ref PhysxResetDesc desc,
        ref PhysxErrorBuffer error);

    /// <summary>Applies one command batch, advances the simulation, and fills one result page.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_step")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldStep(
        PhysxWorldHandle world,
        ref PhysxStepDesc desc,
        ref PhysxResultPage results,
        ref PhysxErrorBuffer error);

    /// <summary>Fills a caller-owned result page from the current state without stepping.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_fetch_results")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldFetchResults(
        PhysxWorldHandle world,
        ref PhysxResultPage results,
        ref PhysxErrorBuffer error);

    /// <summary>Runs one batch of raycast, sweep, and overlap requests.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_query")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldQuery(
        PhysxWorldHandle world,
        ref PhysxQueryDesc desc,
        ref PhysxQueryResultInfo result,
        ref PhysxErrorBuffer error);

    /// <summary>Reports world state, revision, counts, and the declared result capacities.</summary>
    [LibraryImport(PhysxAbi.LibraryName, EntryPoint = "openusd_physx_world_get_status")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial PhysxStatus WorldGetStatus(
        PhysxWorldHandle world,
        ref PhysxWorldStatusInfo info,
        ref PhysxErrorBuffer error);
}
