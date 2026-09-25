// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

public static unsafe partial class OpenUsdSilkRuntime
{
    internal interface IPreparedPageCopy
    {
        static abstract NativeMeshPreparationUsage ReadUsage(nint page);
        static abstract byte[] Copy(in NativePageView view);
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
                if (actual.Version != 1 || actual.MaximumReservedBytes != limit ||
                    actual.ReservedBytes > actual.PeakReservedBytes || actual.PeakReservedBytes > limit)
                {
                    throw new OpenUsdSilkException(
                        OpenUsdNativeStatus.NativeError, "Native mesh preparation accounting is invalid.");
                }
                preparation = new SilkMeshPreparationUsage(
                    actual.MaximumReservedBytes, actual.ReservedBytes, actual.PeakReservedBytes);
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
}
