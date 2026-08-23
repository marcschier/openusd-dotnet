// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

internal readonly record struct StormFrameBinding(
    int Width,
    int Height,
    double TimeCode,
    CameraState Camera,
    ulong StateRevision,
    ulong? SceneRevision,
    ulong ContextGeneration);

internal static unsafe class StormPickingInterop
{
    internal const uint PickRequestVersion = 1;
    internal const uint PickResultVersion = 1;
    internal const uint PickInstanceContextVersion = 1;
    internal const uint SelectionUpdateVersion = 1;

    private const uint RequestHasSceneRevision = 1;
    private const uint RequestCullBackFaces = 2;
    private const uint ResultHasSceneRevision = 1;
    private const uint ResultHasInstance = 2;
    private const uint ResultHasElement = 4;
    private const uint ResultHasInstanceContext = 8;
    private const uint ResultStaleStateRevision = 0x100;
    private const uint ResultStaleSceneRevision = 0x200;
    private const uint ResultStaleCamera = 0x400;
    private const uint ResultStaleViewport = 0x800;
    private const uint ResultStaleTime = 0x1000;
    private const uint ResultStaleContextGeneration = 0x2000;
    private const uint ResultStaleBackendState = 0x4000;
    private const uint ResultStaleMask = 0x7f00;
    private const uint SelectionHasInstanceIndex = 1;
    private const int ErrorBufferSize = 4096;
    private const int StackPathBytes = 512;
    private const int StackContextPathBytes = 1024;
    private const int StackContextEntries = 8;
    private const int MaximumPathBytes = 1024 * 1024;
    private const int MaximumContextEntries = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static RenderPickResult Pick<TCall>(
        nint handle,
        in RenderPickRequest request,
        in StormFrameBinding binding,
        ulong? currentContextGeneration = null)
        where TCall : struct, IStormPickCall
    {
        request.Validate();
        RenderPickStaleReason staleReasons =
            request.InferStaleReasons(
                binding.StateRevision,
                binding.SceneRevision);
        if (request.Viewport.Width != binding.Width ||
            request.Viewport.Height != binding.Height)
        {
            staleReasons |= RenderPickStaleReason.Viewport;
        }
        if (currentContextGeneration.GetValueOrDefault(binding.ContextGeneration) !=
            binding.ContextGeneration)
        {
            staleReasons |= RenderPickStaleReason.ContextGeneration;
        }
        if (staleReasons != RenderPickStaleReason.None)
        {
            return RenderPickResult.Stale(
                request,
                binding.StateRevision,
                binding.SceneRevision,
                staleReasons);
        }

        NativePickRequest nativeRequest = NativePickRequest.Create(request, binding);
        Span<byte> primPath = stackalloc byte[StackPathBytes];
        Span<byte> instancerPath = stackalloc byte[StackPathBytes];
        Span<NativePickInstanceContext> context =
            stackalloc NativePickInstanceContext[StackContextEntries];
        Span<byte> contextPaths = stackalloc byte[StackContextPathBytes];
        NativePickResult nativeResult = NativePickResult.Create();

        OpenUsdNativeStatus status = Invoke<TCall>(
            handle,
            in nativeRequest,
            ref nativeResult,
            primPath,
            instancerPath,
            context,
            contextPaths,
            out string? error);
        if (status == OpenUsdNativeStatus.BufferTooSmall)
        {
            ValidateRequiredCapacities(nativeResult);
            byte[] primArray =
                GC.AllocateUninitializedArray<byte>((int)nativeResult.PrimPathRequired);
            byte[] instancerArray =
                GC.AllocateUninitializedArray<byte>((int)nativeResult.InstancerPathRequired);
            NativePickInstanceContext[] contextArray =
                GC.AllocateUninitializedArray<NativePickInstanceContext>(
                    (int)nativeResult.InstanceContextCount);
            byte[] contextPathArray =
                GC.AllocateUninitializedArray<byte>(
                    (int)nativeResult.InstanceContextPathsRequired);
            nativeResult = NativePickResult.Create();
            status = Invoke<TCall>(
                handle,
                in nativeRequest,
                ref nativeResult,
                primArray,
                instancerArray,
                contextArray,
                contextPathArray,
                out error);
            primPath = primArray;
            instancerPath = instancerArray;
            context = contextArray;
            contextPaths = contextPathArray;
        }
        if (status != OpenUsdNativeStatus.Ok)
        {
            throw new OpenUsdStormException(
                status,
                error ?? $"Storm pick failed with status {status}.");
        }
        nativeResult.Validate();

        ulong? sceneRevision =
            (nativeResult.Flags & ResultHasSceneRevision) != 0
                ? nativeResult.SceneRevision
                : null;
        switch (nativeResult.Status)
        {
            case NativePickStatus.Miss:
                ValidateEmptyResult(nativeResult, allowStaleReasons: false);
                return RenderPickResult.Miss(
                    request,
                    nativeResult.StateRevision,
                    sceneRevision);
            case NativePickStatus.Stale:
                ValidateEmptyResult(nativeResult, allowStaleReasons: true);
                return RenderPickResult.Stale(
                    request,
                    nativeResult.StateRevision,
                    sceneRevision,
                    DecodeStaleReasons(nativeResult.Flags));
            case NativePickStatus.Unsupported:
                ValidateEmptyResult(nativeResult, allowStaleReasons: false);
                return RenderPickResult.Unsupported(
                    request,
                    nativeResult.StateRevision,
                    sceneRevision);
            case NativePickStatus.Hit:
                return ToHit(
                    request,
                    nativeResult,
                    sceneRevision,
                    primPath,
                    instancerPath,
                    context,
                    contextPaths);
            case NativePickStatus.Cancelled:
                throw IncompatibleResult("The Storm child cancelled the queued pick.");
            case NativePickStatus.ContextLost:
                throw IncompatibleResult(
                    "The Storm child OpenGL context changed before the queued pick executed.");
            case NativePickStatus.Invalid:
            case NativePickStatus.Error:
            default:
                throw IncompatibleResult("Storm returned an invalid pick result status.");
        }
    }

    internal static void SetSelection<TCall>(
        nint handle,
        SelectionState selection,
        Vector4 color)
        where TCall : struct, IStormSelectionCall
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!IsFiniteUnitColor(color))
        {
            throw new ArgumentOutOfRangeException(
                nameof(color),
                "Selection color components must be finite values in [0, 1].");
        }
        int count = selection.Items.Count;
        if (count > MaximumContextEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection),
                "Storm selection is limited to 4096 packed items.");
        }

        int byteCount = 0;
        for (int index = 0; index < count; index++)
        {
            SelectionItem item = selection.Items[index];
            item.Validate(nameof(selection));
            if (item.ElementIndex.HasValue)
            {
                throw new NotSupportedException(
                    "Storm selection highlighting does not support face, edge, or point elements.");
            }
            byteCount = checked(byteCount + Encoding.UTF8.GetByteCount(item.PrimPath));
        }
        if (byteCount > MaximumPathBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection),
                "Storm selection path data exceeds the 1 MiB packed-update limit.");
        }

        NativeSelectionItem[]? itemArray = null;
        byte[]? pathArray = null;
        Span<NativeSelectionItem> nativeItems =
            count <= 32
                ? stackalloc NativeSelectionItem[count]
                : (itemArray = GC.AllocateUninitializedArray<NativeSelectionItem>(count));
        Span<byte> pathBytes =
            byteCount <= 4096
                ? stackalloc byte[byteCount]
                : (pathArray = GC.AllocateUninitializedArray<byte>(byteCount));
        int offset = 0;
        for (int index = 0; index < count; index++)
        {
            SelectionItem item = selection.Items[index];
            int written = Encoding.UTF8.GetBytes(item.PrimPath, pathBytes[offset..]);
            nativeItems[index] = new NativeSelectionItem
            {
                PathOffset = checked((uint)offset),
                PathLength = checked((uint)written),
                InstanceIndex = item.InstanceIndex ?? -1,
                Flags = item.InstanceIndex.HasValue ? SelectionHasInstanceIndex : 0,
            };
            offset += written;
        }

        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            nativeItems,
            pathBytes,
            color,
            errorBytes,
            out nuint errorRequired);
        ThrowIfFailed(status, errorBytes, errorRequired, "Storm selection update");
        GC.KeepAlive(itemArray);
        GC.KeepAlive(pathArray);
    }

    private static OpenUsdNativeStatus Invoke<TCall>(
        nint handle,
        in NativePickRequest request,
        ref NativePickResult result,
        Span<byte> primPath,
        Span<byte> instancerPath,
        Span<NativePickInstanceContext> context,
        Span<byte> contextPaths,
        out string? error)
        where TCall : struct, IStormPickCall
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            in request,
            ref result,
            primPath,
            instancerPath,
            context,
            contextPaths,
            errorBytes,
            out nuint errorRequired);
        error = status is OpenUsdNativeStatus.Ok or OpenUsdNativeStatus.BufferTooSmall
            ? null
            : DecodeError(errorBytes, errorRequired);
        return status;
    }

    private static RenderPickResult ToHit(
        in RenderPickRequest request,
        in NativePickResult result,
        ulong? sceneRevision,
        ReadOnlySpan<byte> primPathBytes,
        ReadOnlySpan<byte> instancerPathBytes,
        ReadOnlySpan<NativePickInstanceContext> context,
        ReadOnlySpan<byte> contextPaths)
    {
        ValidateRequiredCapacities(result);
        string primPath = DecodePath(
            primPathBytes,
            result.PrimPathRequired,
            mustExist: true);
        string? instancerPath = null;
        int? instanceIndex = null;
        if ((result.Flags & ResultHasInstance) != 0)
        {
            instancerPath = DecodePath(
                instancerPathBytes,
                result.InstancerPathRequired,
                mustExist: true);
            if (result.InstanceIndex < 0)
            {
                throw IncompatibleResult("Storm returned a negative hit instance index.");
            }
            instanceIndex = result.InstanceIndex;
        }
        else if (result.InstanceIndex != -1)
        {
            throw IncompatibleResult("Storm returned unflagged instance identity.");
        }

        int? elementIndex = null;
        if ((result.Flags & ResultHasElement) != 0)
        {
            if (result.ElementIndex < 0)
            {
                throw IncompatibleResult("Storm returned a negative hit element index.");
            }
            elementIndex = result.ElementIndex;
        }
        else if (result.ElementIndex != -1)
        {
            throw IncompatibleResult("Storm returned unflagged element identity.");
        }
        ValidateContext(result, context, contextPaths);

        Vector3 point = result.WorldPoint.ToVector3();
        Vector3 normal = result.WorldNormal.ToVector3();
        float depth = checked((float)result.NormalizedDepth);
        var item = new SelectionItem(
            primPath,
            instancerPath,
            instanceIndex,
            elementIndex);
        return RenderPickResult.Hit(
            request,
            result.StateRevision,
            sceneRevision,
            item,
            point,
            normal,
            depth,
            RenderBackendKind.Storm);
    }

    private static void ValidateContext(
        in NativePickResult result,
        ReadOnlySpan<NativePickInstanceContext> context,
        ReadOnlySpan<byte> contextPaths)
    {
        bool hasContext = (result.Flags & ResultHasInstanceContext) != 0;
        if (hasContext != (result.InstanceContextCount != 0) ||
            result.InstanceContextCount > (uint)context.Length ||
            result.InstanceContextPathsRequired > (uint)contextPaths.Length)
        {
            throw IncompatibleResult("Storm returned invalid instancer-context sizing.");
        }
        for (int index = 0; index < result.InstanceContextCount; index++)
        {
            NativePickInstanceContext entry = context[index];
            entry.Validate();
            uint end = checked(entry.PathOffset + entry.PathLength + 1);
            if (end > result.InstanceContextPathsRequired ||
                contextPaths[(int)end - 1] != 0)
            {
                throw IncompatibleResult("Storm returned an invalid instancer-context path range.");
            }
            try
            {
                _ = StrictUtf8.GetCharCount(
                    contextPaths.Slice((int)entry.PathOffset, (int)entry.PathLength));
            }
            catch (DecoderFallbackException exception)
            {
                throw new OpenUsdStormException(
                    OpenUsdNativeStatus.NativeError,
                    $"Storm returned a non-UTF-8 instancer-context path: {exception.Message}");
            }
        }
    }

    private static string DecodePath(
        ReadOnlySpan<byte> bytes,
        uint required,
        bool mustExist)
    {
        if (required == 0)
        {
            if (mustExist)
            {
                throw IncompatibleResult("Storm omitted a required hit path.");
            }
            return string.Empty;
        }
        if (required > bytes.Length || bytes[(int)required - 1] != 0)
        {
            throw IncompatibleResult("Storm returned an invalid UTF-8 path buffer size.");
        }
        ReadOnlySpan<byte> value = bytes[..((int)required - 1)];
        if (value.IndexOf((byte)0) >= 0)
        {
            throw IncompatibleResult("Storm returned an embedded NUL in a hit path.");
        }
        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError,
                $"Storm returned a non-UTF-8 hit path: {exception.Message}");
        }
    }

    private static void ValidateRequiredCapacities(in NativePickResult result)
    {
        if (result.Status != NativePickStatus.Hit ||
            result.PrimPathRequired is 0 or > MaximumPathBytes ||
            result.InstancerPathRequired > MaximumPathBytes ||
            result.InstanceContextCount > MaximumContextEntries ||
            result.InstanceContextPathsRequired > MaximumPathBytes)
        {
            throw IncompatibleResult("Storm returned pick buffer requirements outside bounded limits.");
        }
    }

    private static RenderPickStaleReason DecodeStaleReasons(uint flags)
    {
        RenderPickStaleReason reasons = RenderPickStaleReason.None;
        if ((flags & ResultStaleStateRevision) != 0)
        {
            reasons |= RenderPickStaleReason.StateRevision;
        }
        if ((flags & ResultStaleSceneRevision) != 0)
        {
            reasons |= RenderPickStaleReason.SceneRevision;
        }
        if ((flags & ResultStaleCamera) != 0)
        {
            reasons |= RenderPickStaleReason.Camera;
        }
        if ((flags & ResultStaleViewport) != 0)
        {
            reasons |= RenderPickStaleReason.Viewport;
        }
        if ((flags & ResultStaleTime) != 0)
        {
            reasons |= RenderPickStaleReason.Time;
        }
        if ((flags & ResultStaleContextGeneration) != 0)
        {
            reasons |= RenderPickStaleReason.ContextGeneration;
        }
        if ((flags & ResultStaleBackendState) != 0)
        {
            reasons |= RenderPickStaleReason.BackendState;
        }
        return reasons;
    }

    private static void ValidateEmptyResult(
        in NativePickResult result,
        bool allowStaleReasons)
    {
        uint allowedFlags = ResultHasSceneRevision;
        if (allowStaleReasons)
        {
            allowedFlags |= ResultStaleMask;
        }
        if ((result.Flags &
             ~allowedFlags) != 0 ||
            result.PrimPathRequired != 0 ||
            result.InstancerPathRequired != 0 ||
            result.InstanceContextCount != 0 ||
            result.InstanceContextPathsRequired != 0 ||
            result.InstanceIndex != -1 ||
            result.ElementIndex != -1 ||
            !result.WorldPoint.IsZero ||
            !result.WorldNormal.IsZero ||
            result.NormalizedDepth != 1)
        {
            throw IncompatibleResult("Storm returned non-empty identity for a non-hit result.");
        }
    }

    private static bool IsFiniteUnitColor(Vector4 color) =>
        float.IsFinite(color.X) && color.X is >= 0 and <= 1 &&
        float.IsFinite(color.Y) && color.Y is >= 0 and <= 1 &&
        float.IsFinite(color.Z) && color.Z is >= 0 and <= 1 &&
        float.IsFinite(color.W) && color.W is >= 0 and <= 1;

    internal static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        nuint errorRequired,
        string operation)
    {
        if (status == OpenUsdNativeStatus.Ok)
        {
            return;
        }
        throw new OpenUsdStormException(
            status,
            DecodeError(errorBytes, errorRequired) ??
                $"{operation} failed with status {status}.");
    }

    private static string? DecodeError(
        ReadOnlySpan<byte> errorBytes,
        nuint errorRequired)
    {
        int terminator = errorBytes.IndexOf((byte)0);
        int length = terminator >= 0 ? terminator : errorBytes.Length;
        if (length == 0)
        {
            return null;
        }
        string message = StrictUtf8.GetString(errorBytes[..length]);
        if (errorRequired > (nuint)errorBytes.Length)
        {
            message += $" The full native diagnostic required {errorRequired} bytes.";
        }
        return message;
    }

    internal static OpenUsdStormException IncompatibleResult(string message) =>
        new(OpenUsdNativeStatus.NativeError, message);

    internal interface IStormPickCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint handle,
            in NativePickRequest request,
            ref NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    internal interface IStormSelectionCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint handle,
            ReadOnlySpan<NativeSelectionItem> items,
            ReadOnlySpan<byte> pathBytes,
            Vector4 color,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    internal enum NativePickStatus : uint
    {
        Invalid = 0,
        Miss = 1,
        Hit = 2,
        Stale = 3,
        Unsupported = 4,
        Cancelled = 5,
        ContextLost = 6,
        Error = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativePickRequest
    {
        internal static NativePickRequest Create(
            in RenderPickRequest request,
            in StormFrameBinding binding) =>
            new(
                checked((uint)Unsafe.SizeOf<NativePickRequest>()),
                PickRequestVersion,
                request.X,
                request.Y,
                request.Width,
                request.Height,
                request.Viewport.Width,
                request.Viewport.Height,
                (uint)request.Target,
                0,
                (request.RequestedSceneRevision.HasValue ? RequestHasSceneRevision : 0) |
                    ((request.Flags & RenderPickOptions.CullBackFaces) != 0
                        ? RequestCullBackFaces
                        : 0),
                0,
                binding.TimeCode,
                request.RequestedStateRevision,
                request.RequestedSceneRevision.GetValueOrDefault(),
                binding.ContextGeneration,
                new NativeRenderCamera(binding.Camera));

        private NativePickRequest(
            uint structSize,
            uint version,
            int x,
            int y,
            int width,
            int height,
            int viewportWidth,
            int viewportHeight,
            uint target,
            uint resolveMode,
            uint flags,
            uint reserved,
            double timeCode,
            ulong stateRevision,
            ulong sceneRevision,
            ulong contextGeneration,
            NativeRenderCamera camera)
        {
            StructSize = structSize;
            Version = version;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
            Target = target;
            ResolveMode = resolveMode;
            Flags = flags;
            Reserved = reserved;
            TimeCode = timeCode;
            StateRevision = stateRevision;
            SceneRevision = sceneRevision;
            ContextGeneration = contextGeneration;
            Camera = camera;
        }

        internal readonly uint StructSize;
        internal readonly uint Version;
        internal readonly int X;
        internal readonly int Y;
        internal readonly int Width;
        internal readonly int Height;
        internal readonly int ViewportWidth;
        internal readonly int ViewportHeight;
        internal readonly uint Target;
        internal readonly uint ResolveMode;
        internal readonly uint Flags;
        internal readonly uint Reserved;
        internal readonly double TimeCode;
        internal readonly ulong StateRevision;
        internal readonly ulong SceneRevision;
        internal readonly ulong ContextGeneration;
        internal readonly NativeRenderCamera Camera;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePickResult
    {
        internal static NativePickResult Create() => new()
        {
            StructSize = checked((uint)Unsafe.SizeOf<NativePickResult>()),
            Version = PickResultVersion,
            Status = NativePickStatus.Invalid,
            NormalizedDepth = 1,
            InstanceIndex = -1,
            ElementIndex = -1,
        };

        internal uint StructSize;
        internal uint Version;
        internal NativePickStatus Status;
        internal uint Flags;
        internal ulong StateRevision;
        internal ulong SceneRevision;
        internal ulong ContextGeneration;
        internal ulong CameraSignature;
        internal double TimeCode;
        internal NativeVector3d WorldPoint;
        internal NativeVector3d WorldNormal;
        internal double NormalizedDepth;
        internal int InstanceIndex;
        internal int ElementIndex;
        internal uint InstanceContextCount;
        internal uint PrimPathRequired;
        internal uint InstancerPathRequired;
        internal uint InstanceContextPathsRequired;

        internal readonly void Validate()
        {
            if (StructSize != Unsafe.SizeOf<NativePickResult>() ||
                Version != PickResultVersion ||
                (Flags &
                 ~(ResultHasSceneRevision |
                   ResultHasInstance |
                   ResultHasElement |
                   ResultHasInstanceContext |
                   ResultStaleMask)) != 0 ||
                !double.IsFinite(TimeCode) ||
                !WorldPoint.IsFinite ||
                !WorldNormal.IsFinite ||
                !double.IsFinite(NormalizedDepth) ||
                NormalizedDepth is < 0 or > 1)
            {
                throw IncompatibleResult("Storm returned an incompatible pick result.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativeVector3d
    {
        internal NativeVector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal readonly double X;
        internal readonly double Y;
        internal readonly double Z;

        internal bool IsFinite =>
            double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

        internal bool IsZero => X == 0 && Y == 0 && Z == 0;

        internal Vector3 ToVector3()
        {
            var value = new Vector3((float)X, (float)Y, (float)Z);
            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z))
            {
                throw IncompatibleResult(
                    "Storm returned a world-space pick vector outside managed float range.");
            }
            return value;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativePickInstanceContext
    {
        internal readonly uint StructSize;
        internal readonly uint Version;
        internal readonly uint PathOffset;
        internal readonly uint PathLength;
        internal readonly int InstanceIndex;
        internal readonly uint Reserved;

        internal void Validate()
        {
            if (StructSize != Unsafe.SizeOf<NativePickInstanceContext>() ||
                Version != PickInstanceContextVersion ||
                InstanceIndex < 0 ||
                Reserved != 0)
            {
                throw IncompatibleResult(
                    "Storm returned an incompatible instancer-context entry.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSelectionItem
    {
        internal uint PathOffset;
        internal uint PathLength;
        internal int InstanceIndex;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSelectionUpdate
    {
        internal uint StructSize;
        internal uint Version;
        internal uint ItemCount;
        internal uint Flags;
        internal float Red;
        internal float Green;
        internal float Blue;
        internal float Alpha;
        internal NativeSelectionItem* Items;
        internal byte* PathBytes;
        internal uint PathBytesSize;
        internal uint Reserved;
    }
}
