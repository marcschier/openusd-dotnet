// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Interop;

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static OpenUsdImageExrResult EncodeExrRgba16Float(
        FileStream destination,
        ReadOnlySpan<byte> pixels,
        in OpenUsdImageExrRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCompatibleAbi();
        SafeFileHandle handle = destination.SafeFileHandle;
        bool addedReference = false;
        GCHandle callbackRoot = default;
        ExrCancellationState? state = null;
        try
        {
            handle.DangerousAddRef(ref addedReference);
            if (handle.IsInvalid)
            {
                throw new ArgumentException("The caller-owned output handle is invalid.", nameof(destination));
            }

            delegate* unmanaged[Cdecl]<nint, uint, uint, ulong, uint> callback = null;
            nint context = 0;
            if (cancellationToken.CanBeCanceled)
            {
                state = new ExrCancellationState(cancellationToken);
                callbackRoot = GCHandle.Alloc(state);
                context = GCHandle.ToIntPtr(callbackRoot);
                callback = &CancelExrEncoding;
            }

            OpenUsdImageExrRequest nativeRequest = request;
            OpenUsdImageExrResult result = default;
            uint status;
            fixed (byte* input = pixels)
            {
                status = NativeMethods.EncodeExrRgba16Float(
                    &nativeRequest, (uint)sizeof(OpenUsdImageExrRequest),
                    input, (ulong)pixels.Length, (ulong)(nuint)handle.DangerousGetHandle(),
                    callback, context, &result, (uint)sizeof(OpenUsdImageExrResult));
            }

            if (status != (uint)result.Status || status > (uint)OpenUsdImageEncodeStatus.OutOfMemory ||
                result.StructSize != sizeof(OpenUsdImageExrResult) || result.Version != 1 ||
                result.Reserved0 != 0 || result.Reserved1 != 0 ||
                (result.Status == OpenUsdImageEncodeStatus.Ok &&
                    (result.EncodedRows != request.Height || result.EncodedBytes == 0 ||
                        result.EncodedBytes > request.OutputByteLimit)) ||
                (result.Status != OpenUsdImageEncodeStatus.Ok && (result.EncodedRows != 0 || result.EncodedBytes != 0)))
            {
                throw new IOException("The native EXR encoder returned an invalid result contract.");
            }
            if (state?.CallbackFailed == true)
            {
                throw new IOException("The EXR cancellation callback failed.");
            }
            if (result.Status == OpenUsdImageEncodeStatus.Ok)
            {
                try
                {
                    // FileStream has a logical cursor independent of external HANDLE
                    // writes. Failed output must instead be reset/discarded by its owner.
                    if (destination.CanSeek)
                    {
                        destination.Position = checked((long)result.EncodedBytes);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The owner may dispose while the synchronous encode is in flight.
                    // Its raw handle remains protected until DangerousRelease below.
                }
            }
            return result;
        }
        finally
        {
            if (callbackRoot.IsAllocated)
            {
                callbackRoot.Free();
            }
            if (addedReference)
            {
                handle.DangerousRelease();
            }
            GC.KeepAlive(handle);
            GC.KeepAlive(destination);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint CancelExrEncoding(nint context, uint phase, uint completedRows, ulong outputExtent)
    {
        ExrCancellationState? state = null;
        try
        {
            state = (ExrCancellationState?)GCHandle.FromIntPtr(context).Target;
            return state is null || state.Token.IsCancellationRequested ? 1u : 0u;
        }
        catch (Exception)
        {
            // No managed exception is allowed to unwind through a C++ codec frame.
            if (state is not null)
            {
                state.CallbackFailed = true;
            }
            return 1;
        }
    }

    private sealed class ExrCancellationState(CancellationToken token)
    {
        internal CancellationToken Token { get; } = token;
        internal bool CallbackFailed { get; set; }
    }
}
