// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

public static unsafe partial class OpenUsdSilkRuntime
{
    internal interface IPreparedPageCopy
    {
        static abstract NativeMeshPreparationUsage ReadUsage(nint page);
        static abstract byte[] Copy(in NativePageView view);
        static abstract SilkVolumeAssets? RetainVolumeAssets(nint page);
        static abstract void Acknowledge(nint page);
        static abstract void Release(nint page);
    }

    private readonly struct NativePreparedPageCopy : IPreparedPageCopy
    {
        public static NativeMeshPreparationUsage ReadUsage(nint page)
        {
            var usage = new NativeMeshPreparationUsage { StructSize = (uint)sizeof(NativeMeshPreparationUsage) };
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* pointer = errorBytes)
            {
                var error = new NativeErrorBuffer(pointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.PageGetPreparationUsage(page, ref usage, ref error);
                ThrowIfFailed(status, errorBytes, error);
            }
            return usage;
        }

        public static byte[] Copy(in NativePageView view) => view.DataSize == 0
            ? []
            : new ReadOnlySpan<byte>((void*)view.Data, (int)view.DataSize).ToArray();

        public static SilkVolumeAssets? RetainVolumeAssets(nint page) => RetainPageVolumeAssets(page);

        public static void Acknowledge(nint page)
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* pointer = errorBytes)
            {
                var error = new NativeErrorBuffer(pointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.PageAcknowledge(page, ref error);
                ThrowIfFailed(status, errorBytes, error);
            }
        }

        public static void Release(nint page) => NativeMethods.PageRelease(page);
    }

    internal static void ThrowIfSyncFailed<TCopy>(
        OpenUsdNativeStatus status, nint page, ReadOnlySpan<byte> errorBytes, nuint errorCapacity, nuint errorRequired)
        where TCopy : IPreparedPageCopy
    {
        if (status == OpenUsdNativeStatus.Ok)
        {
            return;
        }
        try
        {
            ThrowIfFailed(status, errorBytes, errorCapacity, errorRequired);
        }
        finally
        {
            TCopy.Release(page);
        }
    }

    internal static OpenUsdSilkPage CopyPreparedPage<TCopy>(
        nint page, in NativePageView view, in NativeMeshPreparationUsage usage, ulong? maximum,
        int? maximumPageBytes = null, bool readPageUsage = false)
        where TCopy : IPreparedPageCopy
    {
        try
        {
            if (page == 0 || view.DataSize > int.MaxValue || (view.Data == 0 && view.DataSize != 0))
            {
                throw new OpenUsdSilkException(
                    OpenUsdNativeStatus.NativeError, "The native renderer returned an invalid command page.");
            }
            if (maximumPageBytes is { } byteLimit && (byteLimit <= 0 || view.DataSize > (nuint)byteLimit))
            {
                throw new OpenUsdSilkException(
                    OpenUsdNativeStatus.NativeError, "The native command page exceeded its configured byte limit.");
            }
            SilkCommandParser.ValidatePageAbi(view.AbiVersion);
            SilkMeshPreparationUsage? preparation = null;
            if (maximum is { } limit)
            {
                NativeMeshPreparationUsage actual = readPageUsage ? TCopy.ReadUsage(page) : usage;
                preparation = ValidatePreparationUsage(in actual, limit);
            }
            byte[] data = TCopy.Copy(in view);
            if ((nuint)data.Length != view.DataSize)
            {
                throw new OpenUsdSilkException(
                    OpenUsdNativeStatus.NativeError, "The copied command page does not match its native byte length.");
            }
            var result = new OpenUsdSilkPage(
                view.AbiVersion, view.Revision, data, view.CommandCount, preparation, maximumPageBytes);
            try
            {
                result.SetVolumeAssets(TCopy.RetainVolumeAssets(page));
                TCopy.Acknowledge(page);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }

        }
        finally
        {
            TCopy.Release(page);
        }
    }

    internal static SilkVolumeAssets? RetainPageVolumeAssets(nint page)
    {
        Span<byte> message = stackalloc byte[ErrorBufferSize];
        fixed (byte* pointer = message)
        {
            var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
            OpenUsdNativeStatus status = NativeMethods.RetainVolumeAssets(page, out nint assets, ref error);
            try
            {
                ThrowIfFailed(status, message, error);
                return assets == 0 ? null : new NativeVolumeAssets(assets);
            }
            catch
            {
                if (assets != 0)
                {
                    NativeMethods.ReleaseVolumeAssets(assets);
                }
                throw;
            }
        }
    }

    private sealed class NativeVolumeAssets(nint assets) : SilkVolumeAssets
    {
        private readonly VolumeAssetsHandle _handle = new(assets);

        protected override void CopyCore(string materialPath, Span<byte> destination)
        {
            bool retained = false;
            try
            {
                _handle.DangerousAddRef(ref retained);
                Span<byte> message = stackalloc byte[ErrorBufferSize];
                fixed (byte* bytes = destination)
                fixed (byte* pointer = message)
                {
                    var error = new NativeErrorBuffer(pointer, (nuint)message.Length);
                    OpenUsdNativeStatus status = NativeMethods.CopyVolumeAssets(
                        _handle.DangerousGetHandle(), materialPath, bytes, (nuint)destination.Length, ref error);
                    ThrowIfFailed(status, message, error);
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

    private sealed class VolumeAssetsHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal VolumeAssetsHandle(nint handle) : base(ownsHandle: true) => SetHandle(handle);

        protected override bool ReleaseHandle()
        {
            NativeMethods.ReleaseVolumeAssets(handle);
            return true;
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_page_retain_volume_assets")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus RetainVolumeAssets(
            nint page, out nint assets, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_volume_assets_copy",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CopyVolumeAssets(
            nint assets, string materialPath, byte* destination, nuint count, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_volume_assets_release")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial void ReleaseVolumeAssets(nint assets);
    }

    private static SilkMeshPreparationUsage ValidatePreparationUsage(in NativeMeshPreparationUsage usage, ulong limit)
    {
        if (usage.Version != 1 || usage.MaximumReservedBytes != limit ||
            usage.ReservedBytes > usage.PeakReservedBytes || usage.PeakReservedBytes > limit)
        {
            throw new OpenUsdSilkException(
                OpenUsdNativeStatus.NativeError, "Native mesh preparation accounting is invalid.");
        }
        return new(usage.MaximumReservedBytes, usage.ReservedBytes, usage.PeakReservedBytes);
    }
}
