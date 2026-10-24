// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

public static unsafe partial class OpenUsdSilkRuntime
{
    private static void ConfigureCommandStorage(nint session, SilkCommandStorageLimits limits)
    {
        var native = new NativeCommandStorageLimits
        {
            StructSize = (uint)sizeof(NativeCommandStorageLimits),
            Version = 1,
            MaximumLiveBytes = limits.MaximumLiveBytes,
            MaximumUpdateBytes = limits.MaximumUpdateBytes,
            MaximumCommandBytes = (uint)limits.MaximumCommandBytes,
            TransferBufferBytes = (uint)limits.TransferBufferBytes
        };
        Span<byte> message = stackalloc byte[ErrorBufferSize];
        fixed (byte* pointer = message)
        {
            var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
            try
            {
                ThrowIfFailed(NativeMethods.SetCommandStorageLimits(session, in native, ref error), message, error);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw new NotSupportedException(
                    "File-backed updates require the version-1 native command-storage exports.", exception);
            }
        }
    }

    internal static OpenUsdSilkPage SyncStored(
        nint session, int width, int height, double timeCode, CameraState camera,
        RenderComplexity complexity, RenderDrawMode drawMode, SilkPreparationLimits preparation,
        SilkSceneIngestionOptions? ingestion)
    {
        SilkPreparationLimits.ValidateProfile(complexity, drawMode);
        SilkCommandStorageLimits storageLimits = preparation.CommandStorage ??
            throw new InvalidOperationException("This session has no command-storage policy.");
        ulong meshLimit = ingestion?.MaximumMeshPreparationReservationBytes ??
            preparation.MaximumMeshPreparationReservationBytes;
        int pageLimit = ingestion?.MaximumCommandPageBytes ?? preparation.MaximumCommandPageBytes;
        int commandLimit = Math.Min(pageLimit, storageLimits.MaximumCommandBytes);
        string? purpose = ingestion?.MaterialBindingPurpose;
        if (ingestion is not null)
        {
            SilkSceneIngestionOptions.ValidateIncludedPurposes(ingestion.IncludedPurposes);
            purpose = SilkSceneIngestionOptions.NormalizeMaterialBindingPurpose(purpose!);
            if (purpose.Length == 0)
            {
                throw new NotSupportedException("Explicit scene ingestion requires full or preview material binding.");
            }
        }
        byte[] material = purpose is null ? [] : Encoding.UTF8.GetBytes(purpose + '\0');
        var nativeCamera = new NativeRenderCamera(camera);
        var limits = new NativeMeshPreparationPageLimits((uint)sizeof(NativeMeshPreparationPageLimits),
            meshLimit, (ulong)pageLimit);
        var usage = new NativeMeshPreparationUsage { StructSize = (uint)sizeof(NativeMeshPreparationUsage) };
        var view = new NativeCommandStorageView { StructSize = (uint)sizeof(NativeCommandStorageView), Version = 1 };
        Span<byte> message = stackalloc byte[ErrorBufferSize];
        nint handle = 0;
        SilkNativeCommandStorage? storage = null;
        try
        {
            fixed (byte* materialPointer = material)
            fixed (byte* messagePointer = message)
            {
                var request = new NativeSceneIngestionRequest((uint)sizeof(NativeSceneIngestionRequest), 1,
                    (uint)(ingestion?.IncludedPurposes ?? RenderPurpose.Default),
                    (uint)complexity, (uint)drawMode, materialPointer);
                var error = new NativeErrorBuffer(messagePointer, (nuint)message.Length);
                OpenUsdNativeStatus status = NativeMethods.SyncToStorage(
                    session, width, height, timeCode, in nativeCamera, ingestion is null ? null : &request,
                    in limits, ref usage, out handle, ref view, ref error);
                ThrowIfFailed(status, message, error);
                if (handle == 0 || view.Version != 1 || view.DataSize < 8 ||
                    view.DataSize > storageLimits.MaximumUpdateBytes ||
                    view.MaximumCommandBytes != commandLimit ||
                    view.MaximumTransferBytes != storageLimits.TransferBufferBytes || view.CommandCount == 0)
                {
                    throw new OpenUsdSilkException(OpenUsdNativeStatus.NativeError,
                        "The native command-storage metadata does not match its admitted limits.");
                }
                SilkCommandParser.ValidatePageAbi(view.PageAbiVersion);
                SilkMeshPreparationUsage preparationUsage = ValidatePreparationUsage(in usage, meshLimit);
                storage = new SilkNativeCommandStorage(handle, view.DataSize, commandLimit,
                    storageLimits.TransferBufferBytes);
                handle = 0;
                var page = new OpenUsdSilkPage(view.PageAbiVersion, view.Revision, storage,
                    view.CommandCount, preparationUsage, commandLimit);
                try
                {
                    ThrowIfFailed(NativeMethods.PageAcknowledge(storage.Handle, ref error), message, error);
                    return page;
                }
                catch
                {
                    page.Dispose();
                    throw;
                }
            }
        }
        finally
        {
            storage?.Dispose();
            if (handle != 0)
            {
                NativeMethods.PageRelease(handle);
            }
        }
    }

    private sealed class SilkNativeCommandStorage(
        nint handle, ulong length, int commandBytes, int transferBytes)
        : SilkCommandStorage(length, commandBytes, transferBytes)
    {
        private readonly StoredPageHandle _handle = new(handle);
        internal nint Handle => _handle.DangerousGetHandle();

        protected override void ReadCore(ulong offset, Span<byte> destination)
        {
            bool retained = false;
            try
            {
                _handle.DangerousAddRef(ref retained);
                Span<byte> message = stackalloc byte[ErrorBufferSize];
                fixed (byte* data = destination)
                fixed (byte* pointer = message)
                {
                    var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
                    ThrowIfFailed(NativeMethods.ReadCommandStorage(_handle.DangerousGetHandle(), offset,
                        data, (nuint)destination.Length, ref error), message, error);
                }
            }
            finally
            {
                if (retained)
                {
                    _handle.DangerousRelease();
                }
            }
        }

        protected override void DisposeCore() => _handle.Dispose();
    }

    private sealed class StoredPageHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal StoredPageHandle(nint handle) : base(ownsHandle: true) => SetHandle(handle);

        protected override bool ReleaseHandle()
        {
            NativeMethods.PageRelease(handle);
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCommandStorageLimits
    {
        internal uint StructSize;
        internal uint Version;
        internal ulong MaximumLiveBytes;
        internal ulong MaximumUpdateBytes;
        internal uint MaximumCommandBytes;
        internal uint TransferBufferBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCommandStorageView
    {
        internal uint StructSize;
        internal uint Version;
        internal uint PageAbiVersion;
        internal uint CommandCount;
        internal ulong Revision;
        internal ulong DataSize;
        internal uint MaximumCommandBytes;
        internal uint MaximumTransferBytes;
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_set_command_storage_limits")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetCommandStorageLimits(
            nint session, in NativeCommandStorageLimits limits, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_sync_to_storage")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SyncToStorage(
            nint session, int width, int height, double timeCode, in NativeRenderCamera camera,
            NativeSceneIngestionRequest* request, in NativeMeshPreparationPageLimits limits,
            ref NativeMeshPreparationUsage usage, out nint page, ref NativeCommandStorageView view,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_page_read_storage")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus ReadCommandStorage(
            nint page, ulong offset, byte* destination, nuint count, ref NativeErrorBuffer error);
    }
}
