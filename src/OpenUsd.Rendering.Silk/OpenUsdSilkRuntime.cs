// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Creates and synchronizes native hdSilk sessions.
/// </summary>
public static unsafe partial class OpenUsdSilkRuntime
{
    private const string LibraryName = "openusd_hdsilk";
    private const int ErrorBufferSize = 4096;

    /// <summary>Creates an hdSilk session for a stage.</summary>
    public static OpenUsdSilkSession Create(string pluginPath, string stagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.Create(
                pluginPath,
                stagePath,
                out nint session,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return CreateSession(session, null);
        }
    }

    /// <summary>Creates an hdSilk session for the exact stage retained by a render source.</summary>
    public static OpenUsdSilkSession Create(
        string pluginPath,
        UsdStageRenderSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentNullException.ThrowIfNull(source);

        UsdStageRenderLease lease = source.AcquireLease();
        try
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.CreateFromStage(
                    pluginPath,
                    lease.DangerousGetHandle(),
                    out nint session,
                    ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CreateSession(session, lease);
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static OpenUsdSilkSession CreateSession(
        nint session,
        UsdStageRenderLease? lease)
    {
        try
        {
            return new OpenUsdSilkSession(session, lease);
        }
        catch
        {
            NativeMethods.SessionRelease(session);
            throw;
        }
    }

    internal static OpenUsdSilkPage Sync(
        nint session,
        int width,
        int height,
        double timeCode,
        CameraState camera,
        RenderComplexity complexity)
    {
        ValidateComplexity(complexity);
        var view = new NativePageView
        {
            StructSize = (uint)sizeof(NativePageView)
        };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = InvokeSync<NativeSyncCall>(
            session,
            width,
            height,
            timeCode,
            camera,
            complexity,
            ref view,
            out nint page,
            errorBytes,
            out nuint errorRequired);
        ThrowIfFailed(
            status,
            errorBytes,
            (nuint)errorBytes.Length,
            errorRequired);
        if (page == 0 || view.DataSize > int.MaxValue || (view.Data == 0 && view.DataSize != 0))
        {
            if (page != 0)
            {
                NativeMethods.PageRelease(page);
            }
            throw new OpenUsdSilkException(
                OpenUsdNativeStatus.NativeError,
                "The native renderer returned an invalid command page.");
        }
        try
        {
            SilkCommandParser.ValidatePageAbi(view.AbiVersion);
            byte[] data = view.DataSize == 0
                ? []
                : new ReadOnlySpan<byte>((void*)view.Data, (int)view.DataSize).ToArray();
            return new OpenUsdSilkPage(
                view.AbiVersion,
                view.Revision,
                data,
                view.CommandCount);
        }
        finally
        {
            NativeMethods.PageRelease(page);
        }
    }

    internal static OpenUsdNativeStatus InvokeSync<TCall>(
        nint session,
        int width,
        int height,
        double timeCode,
        CameraState camera,
        RenderComplexity complexity,
        ref NativePageView view,
        out nint page,
        Span<byte> errorBytes,
        out nuint errorRequired)
        where TCall : struct, ISyncCall
    {
        var nativeCamera = new NativeRenderCamera(camera);
        return TCall.Invoke(
            session,
            width,
            height,
            timeCode,
            in nativeCamera,
            complexity,
            out page,
            ref view,
            errorBytes,
            out errorRequired);
    }

    private static void ValidateComplexity(RenderComplexity complexity)
    {
        if (complexity is < RenderComplexity.Low or > RenderComplexity.VeryHigh)
        {
            throw new ArgumentOutOfRangeException(nameof(complexity));
        }
    }

    internal static void Destroy(nint session)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.SessionDestroy(session, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void ReleaseSession(nint session) => NativeMethods.SessionRelease(session);

    internal static (long ManagedSessions, long NativeSessions, long NativePeakSessions,
        long ManagedPages, long NativePages, long NativePeakPages,
        long GpuScenes, long GpuMeshes) GetDiagnostics() =>
        (
            SilkManagedDiagnostics.LiveSessions,
            checked((long)NativeMethods.GetLiveSessionCount()),
            checked((long)NativeMethods.GetPeakSessionCount()),
            SilkManagedDiagnostics.LivePages,
            checked((long)NativeMethods.GetLivePageCount()),
            checked((long)NativeMethods.GetPeakPageCount()),
            SilkManagedDiagnostics.LiveGpuSceneResources,
            SilkManagedDiagnostics.LiveGpuMeshes
        );

    internal static void ResetDiagnosticPeaks() => NativeMethods.ResetPeakCounts();

    private static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error) =>
        ThrowIfFailed(
            status,
            errorBytes,
            error.Capacity,
            error.Required);

    private static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        nuint errorCapacity,
        nuint errorRequired)
    {
        if (status == OpenUsdNativeStatus.Ok)
        {
            return;
        }

        int terminator = errorBytes.IndexOf((byte)0);
        int length = terminator >= 0 ? terminator : errorBytes.Length;
        string message = length == 0
            ? $"hdSilk operation failed with status {status}."
            : Encoding.UTF8.GetString(errorBytes[..length]);
        if (errorRequired > errorCapacity)
        {
            message += $" The full native diagnostic required {errorRequired} bytes.";
        }
        throw new OpenUsdSilkException(status, message);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePageView
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal ulong Revision;
        internal nint Data;
        internal nuint DataSize;
        internal uint CommandCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeErrorBuffer
    {
        internal NativeErrorBuffer(byte* data, nuint capacity)
        {
            Data = data;
            Capacity = capacity;
            Required = 0;
        }

        internal readonly byte* Data;
        internal readonly nuint Capacity;
        internal readonly nuint Required;
    }

    internal interface ISyncCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint session,
            int width,
            int height,
            double timeCode,
            in NativeRenderCamera camera,
            RenderComplexity complexity,
            out nint page,
            ref NativePageView view,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    private readonly struct NativeSyncCall : ISyncCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint session,
            int width,
            int height,
            double timeCode,
            in NativeRenderCamera camera,
            RenderComplexity complexity,
            out nint page,
            ref NativePageView view,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            if (complexity != RenderComplexity.Low)
            {
                return NativeSyncWithComplexityCall.Invoke(
                    session,
                    width,
                    height,
                    timeCode,
                    in camera,
                    complexity,
                    out page,
                    ref view,
                    errorBytes,
                    out errorRequired);
            }
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.Sync(
                    session,
                    width,
                    height,
                    timeCode,
                    in camera,
                    out page,
                    ref view,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }

        private readonly struct NativeSyncWithComplexityCall : ISyncCall
        {
            public static OpenUsdNativeStatus Invoke(
                nint session,
                int width,
                int height,
                double timeCode,
                in NativeRenderCamera camera,
                RenderComplexity complexity,
                out nint page,
                ref NativePageView view,
                Span<byte> errorBytes,
                out nuint errorRequired)
            {
                fixed (byte* errorPointer = errorBytes)
                {
                    var error = new NativeErrorBuffer(
                        errorPointer,
                        (nuint)errorBytes.Length);
                    OpenUsdNativeStatus status = NativeMethods.SyncWithComplexity(
                        session,
                        width,
                        height,
                        timeCode,
                        in camera,
                        (uint)complexity,
                        out page,
                        ref view,
                        ref error);
                    errorRequired = error.Required;
                    return status;
                }
            }
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_session_create",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Create(
            string pluginPath,
            string stagePath,
            out nint session,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_session_create_from_stage",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CreateFromStage(
            string pluginPath,
            nint stage,
            out nint session,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void SessionRelease(nint session);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SessionDestroy(
            nint session,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_sync")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Sync(
            nint session,
            int width,
            int height,
            double timeCode,
            in NativeRenderCamera camera,
            out nint page,
            ref NativePageView view,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_sync_with_complexity")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SyncWithComplexity(
            nint session,
            int width,
            int height,
            double timeCode,
            in NativeRenderCamera camera,
            uint complexity,
            out nint page,
            ref NativePageView view,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_page_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void PageRelease(nint page);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_diagnostic_get_live_session_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetLiveSessionCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_diagnostic_get_peak_session_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetPeakSessionCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_diagnostic_get_live_page_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetLivePageCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_diagnostic_get_peak_page_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetPeakPageCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_silk_diagnostic_reset_peak_counts")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void ResetPeakCounts();
    }
}
