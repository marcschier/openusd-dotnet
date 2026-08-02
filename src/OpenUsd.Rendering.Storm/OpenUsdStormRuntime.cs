// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

/// <summary>
/// Creates Hydra/Storm renderers while an application OpenGL context is current.
/// </summary>
public static unsafe partial class OpenUsdStormRuntime
{
    private const string LibraryName = "openusd_hydra";
    private const int ErrorBufferSize = 4096;
    private const uint ExpectedAbiVersion = 6;
    private const uint RenderHasSceneRevision = 0x1u;
    private const uint RenderUseSceneLights = 0x2u;

    /// <summary>Gets the native Storm renderer ABI version.</summary>
    public static uint AbiVersion => NativeMethods.GetAbiVersion();

    /// <summary>Gets the deterministic camera-space headlight used by Storm parity renders.</summary>
    public static RenderHeadlight Headlight
    {
        get
        {
            ValidateAbiVersion(AbiVersion);
            var headlight = new NativeRenderHeadlight();
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.GetHeadlight(ref headlight, ref error);
                ThrowIfFailed(status, errorBytes, error);
            }
            return headlight.ToRenderHeadlight();
        }
    }

    /// <summary>Creates a Storm renderer for a stage.</summary>
    public static OpenUsdStormRenderer Create(string pluginPath, string stagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        ValidateAbiVersion(AbiVersion);

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.Create(
                pluginPath,
                stagePath,
                out nint renderer,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            return CreateRenderer(renderer, null);
        }
    }

    /// <summary>Creates a Storm renderer for the exact stage retained by a render source.</summary>
    public static OpenUsdStormRenderer Create(
        string pluginPath,
        UsdStageRenderSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentNullException.ThrowIfNull(source);
        ValidateAbiVersion(AbiVersion);

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
                    out nint renderer,
                    ref error);
                ThrowIfFailed(status, errorBytes, error);
                return CreateRenderer(renderer, lease);
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static bool Render(
        nint renderer,
        int width,
        int height,
        uint framebuffer,
        double timeCode,
        CameraState camera,
        ulong revision,
        ulong? sceneRevision,
        bool useSceneLights) =>
        Render<NativeRenderCall>(
            renderer,
            width,
            height,
            framebuffer,
            timeCode,
            camera,
            revision,
            sceneRevision,
            useSceneLights);

    internal static bool Render<TCall>(
        nint renderer,
        int width,
        int height,
        uint framebuffer,
        double timeCode,
        CameraState camera,
        ulong revision = 0,
        ulong? sceneRevision = null,
        bool useSceneLights = false)
        where TCall : struct, IRenderCall
    {
        var nativeCamera = new NativeRenderCamera(camera);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        uint revisionFlags = sceneRevision.HasValue ? RenderHasSceneRevision : 0u;
        if (useSceneLights)
        {
            revisionFlags |= RenderUseSceneLights;
        }

        OpenUsdNativeStatus status = TCall.Invoke(
            renderer,
            width,
            height,
            framebuffer,
            timeCode,
            in nativeCamera,
            revision,
            sceneRevision.GetValueOrDefault(),
            revisionFlags,
            out int converged,
            errorBytes,
            out nuint errorRequired);
        ThrowIfFailed(
            status,
            errorBytes,
            (nuint)errorBytes.Length,
            errorRequired);
        return converged != 0;
    }

    internal static RenderPickResult Pick(
        nint renderer,
        RenderPickRequest request,
        StormFrameBinding binding) =>
        StormPickingInterop.Pick<NativePickCall>(
            renderer,
            request,
            binding);

    internal static void SetSelection(
        nint renderer,
        SelectionState selection,
        Vector4 color) =>
        StormPickingInterop.SetSelection<NativeSelectionCall>(
            renderer,
            selection,
            color);

    internal static void Destroy(nint renderer)
    {
        InvokeStatus(renderer, NativeMethods.Destroy);
    }

    internal static void Detach(nint renderer)
    {
        InvokeStatus(renderer, NativeMethods.Abandon);
    }

    private static OpenUsdStormRenderer CreateRenderer(
        nint renderer,
        UsdStageRenderLease? lease)
    {
        try
        {
            string name = GetRendererName(renderer);
            return new OpenUsdStormRenderer(renderer, lease, name);
        }
        catch
        {
            NativeMethods.Release(renderer);
            throw;
        }
    }

    private static string GetRendererName(nint renderer)
    {
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.GetRendererName(
                    renderer,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    private static void InvokeStatus(
        nint renderer,
        DestroyOperation operation)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = operation(renderer, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    internal static void Release(nint renderer)
    {
        NativeMethods.Release(renderer);
    }

    internal static (long Managed, long Native, long NativePeak, long Abandoned)
        GetDiagnostics() =>
        (
            OpenUsdStormRenderer.LiveCount,
            checked((long)NativeMethods.GetLiveRendererCount()),
            checked((long)NativeMethods.GetPeakRendererCount()),
            checked((long)NativeMethods.GetAbandonedEngineCount())
        );

    internal static void ResetDiagnosticPeak() => NativeMethods.ResetPeakRendererCount();

    private static void ValidateAbiVersion(uint actual)
    {
        if (actual != ExpectedAbiVersion)
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError,
                $"Storm renderer ABI mismatch: managed={ExpectedAbiVersion}, native={actual}.");
        }
    }

    private static string GetString(NativeStringGetter getter)
    {
        OpenUsdNativeStatus status = getter(null, 0, out nuint required);
        if (status != OpenUsdNativeStatus.BufferTooSmall || required == 0 || required > int.MaxValue)
        {
            throw new OpenUsdStormException(status, "The renderer returned an invalid string length.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
        fixed (byte* pointer = bytes)
        {
            status = getter(pointer, required, out nuint written);
            if (status != OpenUsdNativeStatus.Ok || written != required)
            {
                throw new OpenUsdStormException(status, "The renderer could not return the requested string.");
            }
        }
        return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
    }

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
            ? $"Storm operation failed with status {status}."
            : Encoding.UTF8.GetString(errorBytes[..length]);
        if (errorRequired > errorCapacity)
        {
            message += $" The full native diagnostic required {errorRequired} bytes.";
        }
        throw new OpenUsdStormException(status, message);
    }

    private delegate OpenUsdNativeStatus NativeStringGetter(
        byte* buffer,
        nuint capacity,
        out nuint required);

    private delegate OpenUsdNativeStatus DestroyOperation(
        nint renderer,
        ref NativeErrorBuffer error);

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

    internal interface IRenderCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint renderer,
            int width,
            int height,
            uint framebuffer,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out int converged,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    private readonly struct NativeRenderCall : IRenderCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint renderer,
            int width,
            int height,
            uint framebuffer,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out int converged,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.RenderV2(
                    renderer,
                    width,
                    height,
                    framebuffer,
                    timeCode,
                    in camera,
                    stateRevision,
                    sceneRevision,
                    revisionFlags,
                    out converged,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private readonly struct NativePickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint renderer,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (byte* primPointer = primPath)
            fixed (byte* instancerPointer = instancerPath)
            fixed (StormPickingInterop.NativePickInstanceContext* contextPointer =
                instanceContext)
            fixed (byte* contextPathsPointer = instanceContextPaths)
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.Pick(
                    renderer,
                    in request,
                    ref result,
                    primPointer,
                    checked((uint)primPath.Length),
                    instancerPointer,
                    checked((uint)instancerPath.Length),
                    contextPointer,
                    checked((uint)instanceContext.Length),
                    contextPathsPointer,
                    checked((uint)instanceContextPaths.Length),
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private readonly struct NativeSelectionCall :
        StormPickingInterop.IStormSelectionCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint renderer,
            ReadOnlySpan<StormPickingInterop.NativeSelectionItem> items,
            ReadOnlySpan<byte> pathBytes,
            Vector4 color,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (StormPickingInterop.NativeSelectionItem* itemPointer = items)
            fixed (byte* pathPointer = pathBytes)
            fixed (byte* errorPointer = errorBytes)
            {
                var update = new StormPickingInterop.NativeSelectionUpdate
                {
                    StructSize = checked((uint)Unsafe.SizeOf<
                        StormPickingInterop.NativeSelectionUpdate>()),
                    Version = StormPickingInterop.SelectionUpdateVersion,
                    ItemCount = checked((uint)items.Length),
                    Red = color.X,
                    Green = color.Y,
                    Blue = color.Z,
                    Alpha = color.W,
                    Items = itemPointer,
                    PathBytes = pathPointer,
                    PathBytesSize = checked((uint)pathBytes.Length),
                };
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.SetSelection(
                    renderer,
                    in update,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_get_abi_version")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial uint GetAbiVersion();

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_get_headlight")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetHeadlight(
            ref NativeRenderHeadlight headlight,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_create",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Create(
            string pluginPath,
            string stagePath,
            out nint renderer,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_create_from_stage",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CreateFromStage(
            string pluginPath,
            nint stage,
            out nint renderer,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_release")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void Release(nint renderer);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_diagnostic_get_live_renderer_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetLiveRendererCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_diagnostic_get_peak_renderer_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetPeakRendererCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_diagnostic_get_abandoned_engine_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetAbandonedEngineCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_diagnostic_reset_peak_renderer_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void ResetPeakRendererCount();

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Destroy(
            nint renderer,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_abandon")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Abandon(
            nint renderer,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_render_v2")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus RenderV2(
            nint renderer,
            int width,
            int height,
            uint framebuffer,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out int converged,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_pick")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Pick(
            nint renderer,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            byte* primPath,
            uint primPathCapacity,
            byte* instancerPath,
            uint instancerPathCapacity,
            StormPickingInterop.NativePickInstanceContext* instanceContext,
            uint instanceContextCapacity,
            byte* instanceContextPaths,
            uint instanceContextPathsCapacity,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_set_selection")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetSelection(
            nint renderer,
            in StormPickingInterop.NativeSelectionUpdate update,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_get_renderer_name")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetRendererName(
            nint renderer,
            byte* buffer,
            nuint capacity,
            out nuint required,
            ref NativeErrorBuffer error);
    }
}
