// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

public sealed partial class OpenUsdStormChildSession
{
    /// <summary>Renders native viewport appearance and typed AOVs together on the child's render thread.</summary>
    /// <param name="request">Immutable request with framebuffer zero and dimensions matching the child.</param>
    /// <param name="cancellationToken">Cancels admission or copying, not an in-flight native driver wait.</param>
    /// <returns>Detached native RGBA8 and immutable AOV storage independent of child lifetime.</returns>
    /// <remarks>
    /// Selection, resize and disposal are serialized with capture. The request's native and managed
    /// ceilings also cover its RGBA8 companion. Native AOV limits are at most 4096 per dimension and
    /// 1,048,576 pixels. macOS Metal Storm does not yet implement this OpenGL AOV route.
    /// Failure invalidates the managed pick binding. No caller GL context is required.
    /// </remarks>
    public OpenUsdStormChildAovCapture RenderAovs(
        StormAovRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            nint handle = GetHandleLocked();
            if (OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException(
                    "Storm child AOV capture currently requires the Windows or Linux OpenGL renderer.");
            }
            _hasRequestedFrame = false;
            OpenUsdStormChildAovCapture capture = OpenUsdStormChildRuntime.RenderAovs(
                handle, request, _hasDisplaySelection, cancellationToken);
            OpenUsdStormChildDiagnostics diagnostics = OpenUsdStormChildRuntime.GetDiagnostics(handle);
            _contextGeneration = diagnostics.ContextGeneration;
            _width = diagnostics.Width;
            _height = diagnostics.Height;
            _latestFrame = new StormFrameBinding(
                request.Width, request.Height, request.TimeCode, request.FrameCamera,
                request.CallerStateRevision, request.CallerSceneRevision, _contextGeneration);
            _hasRequestedFrame = true;
            return capture;
        }
    }
}

public static unsafe partial class OpenUsdStormChildRuntime
{
    internal static OpenUsdStormChildAovCapture RenderAovs(
        nint child,
        StormAovRequest request,
        bool hasDisplaySelection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StormAovRequest admitted = AdmitAovCapture(request);
        int byteCount = GetCaptureByteCount(request.Width, request.Height);
        var rgba = new byte[byteCount];
        StormAovNative.Request nativeRequest = StormAovNative.Request.Create(request);
        NativeFramebufferCapture framebuffer = default;
        using var owner = new StormAovOwnerHandle<OpenUsdStormRuntime.NativeAovCall>();
        Span<byte> error = stackalloc byte[ErrorBufferSize];
        error.Clear();
        OpenUsdNativeStatus status;
        NativeErrorBuffer nativeError;
        nuint written;
        fixed (byte* errorPointer = error)
        fixed (byte* rgbaPointer = rgba)
        {
            nativeError = new NativeErrorBuffer(errorPointer, (nuint)error.Length);
            status = NativeMethods.CaptureAovs(
                child, in nativeRequest, out nint handle,
                rgbaPointer, (nuint)rgba.Length, out written, ref framebuffer, ref nativeError);
            owner.Initialize(handle);
        }
        if (status != OpenUsdNativeStatus.Ok && !owner.IsInvalid)
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError, "A failed child AOV capture incorrectly published an owner.");
        }
        ThrowIfFailed(status, error, nativeError);
        if (owner.IsInvalid || written != (nuint)byteCount ||
            framebuffer.Width != request.Width || framebuffer.Height != request.Height ||
            framebuffer.PixelCount != (ulong)(byteCount / 4))
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError, "The child returned incomplete same-frame AOV or RGBA8 output.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        StormAovSnapshot snapshot = OpenUsdStormRuntime.ReadAovOwner(
            owner, admitted, hasDisplaySelection);
        cancellationToken.ThrowIfCancellationRequested();
        return new OpenUsdStormChildAovCapture(framebuffer.ToManaged(rgba), snapshot);
    }

    internal static StormAovRequest AdmitAovCapture(StormAovRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Framebuffer != 0)
        {
            throw new ArgumentException(
                "Storm child capture owns its framebuffer; the request framebuffer must be zero.", nameof(request));
        }
        ulong bytes = checked((ulong)GetCaptureByteCount(request.Width, request.Height));
        ulong managedCompanion = bytes + OpenUsdStormChildAovCapture.ManagedCompanionAllowance;
        ulong nativeCompanion = bytes + OpenUsdStormChildAovCapture.NativeCompanionAllowance;
        StormAovLimits limits = request.Limits;
        if (limits.ManagedByteLimit < managedCompanion + StormAovDecoder.MinimumManagedStorage ||
            limits.NativeWorkingByteLimit <= nativeCompanion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), "The AOV capture budget cannot admit the native viewport companion and snapshot.");
        }
        return new StormAovRequest(
            request.Width, request.Height, 0, request.Outputs, request.FrameCamera,
            request.TimeCode, request.CallerStateRevision, request.CallerSceneRevision,
            request.IncludeIdentities, request.UseSceneLights,
            new StormAovLimits(
                limits.PixelLimit, limits.IdentityLimit, limits.InstanceContextLimit,
                limits.PathByteLimit, limits.NativeWorkingByteLimit - nativeCompanion,
                limits.ManagedByteLimit - managedCompanion));
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_capture_aovs")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CaptureAovs(
            nint child, in StormAovNative.Request request, out nint owner,
            byte* rgba, nuint rgbaCapacity, out nuint rgbaRequired,
            ref NativeFramebufferCapture capture, ref NativeErrorBuffer error);
    }
}
