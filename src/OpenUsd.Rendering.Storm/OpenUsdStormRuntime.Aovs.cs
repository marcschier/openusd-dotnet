// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

public static unsafe partial class OpenUsdStormRuntime
{
    internal static StormAovSnapshot RenderAovs(nint renderer, StormAovRequest request) =>
        RenderAovs<NativeAovCall>(renderer, request);

    internal static StormAovSnapshot RenderAovs<TCall>(nint renderer, StormAovRequest request)
        where TCall : struct, IStormAovCall
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limits.ManagedByteLimit < StormAovDecoder.MinimumManagedStorage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), "The managed budget cannot cover snapshot bookkeeping.");
        }
        StormAovNative.Request nativeRequest = StormAovNative.Request.Create(request);
        using var owner = new StormAovOwnerHandle<TCall>();
        Span<byte> error = stackalloc byte[ErrorBufferSize];
        error.Clear();
        OpenUsdNativeStatus status = TCall.Capture(
            renderer, in nativeRequest, out nint handle, error, out nuint errorRequired);
        owner.Initialize(handle);
        if (status != OpenUsdNativeStatus.Ok && !owner.IsInvalid)
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError, "A failed native capture incorrectly published an owner.");
        }
        ThrowIfFailed(status, error, (nuint)error.Length, errorRequired);
        if (owner.IsInvalid)
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError, "Native capture succeeded without a valid owner.");
        }

        // The native view borrows memory beyond the get-view call. Keep a
        // SafeHandle reference through validation and every managed copy.
        bool borrowed = false;
        try
        {
            owner.DangerousAddRef(ref borrowed);
            StormAovNative.View view = StormAovNative.View.Create();
            error.Clear();
            status = TCall.GetView(owner.DangerousGetHandle(), ref view, error, out errorRequired);
            ThrowIfFailed(status, error, (nuint)error.Length, errorRequired);
            return StormAovDecoder.Decode(in view, request);
        }
        finally
        {
            if (borrowed)
            {
                owner.DangerousRelease();
            }
        }
    }

    internal interface IStormAovCall
    {
        static abstract OpenUsdNativeStatus Capture(
            nint renderer, in StormAovNative.Request request, out nint owner,
            Span<byte> error, out nuint required);

        static abstract OpenUsdNativeStatus GetView(
            nint owner, ref StormAovNative.View view, Span<byte> error, out nuint required);

        static abstract void Release(nint owner);
    }

    private readonly struct NativeAovCall : IStormAovCall
    {
        public static OpenUsdNativeStatus Capture(
            nint renderer, in StormAovNative.Request request, out nint owner,
            Span<byte> error, out nuint required)
        {
            fixed (byte* errorPointer = error)
            {
                var buffer = new NativeErrorBuffer(errorPointer, (nuint)error.Length);
                OpenUsdNativeStatus status = NativeMethods.CaptureAovs(
                    renderer, in request, out owner, ref buffer);
                required = buffer.Required;
                return status;
            }
        }

        public static OpenUsdNativeStatus GetView(
            nint owner, ref StormAovNative.View view, Span<byte> error, out nuint required)
        {
            fixed (byte* errorPointer = error)
            {
                var buffer = new NativeErrorBuffer(errorPointer, (nuint)error.Length);
                OpenUsdNativeStatus status = NativeMethods.GetAovView(owner, ref view, ref buffer);
                required = buffer.Required;
                return status;
            }
        }

        public static void Release(nint owner) => NativeMethods.ReleaseAovOwner(owner);
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_aov_capture")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CaptureAovs(
            nint renderer, in StormAovNative.Request request, out nint owner, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_aov_get_view")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetAovView(
            nint owner, ref StormAovNative.View view, ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_aov_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void ReleaseAovOwner(nint owner);
    }
}
