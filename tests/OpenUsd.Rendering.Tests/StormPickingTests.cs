// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class StormPickingTests
{
    private const uint HasSceneRevision = 1;
    private const uint HasInstance = 2;
    private static int _missCalls;
    private static int _staleCalls;
    private static int _sizingCalls;
    private static int _selectionCalls;
    private static int _selectionItemCount;
    private static string? _selectionPaths;

    [Test]
    public async Task NativePickLayoutsMatchTheVersionedCAbi()
    {
        await Assert.That(Unsafe.SizeOf<StormPickingInterop.NativePickRequest>())
            .IsEqualTo(608);
        await Assert.That(Marshal.SizeOf<StormPickingInterop.NativePickRequest>())
            .IsEqualTo(608);
        await Assert.That(Unsafe.SizeOf<StormPickingInterop.NativePickResult>())
            .IsEqualTo(136);
        await Assert.That(Unsafe.SizeOf<StormPickingInterop.NativePickInstanceContext>())
            .IsEqualTo(24);
        await Assert.That(Unsafe.SizeOf<StormPickingInterop.NativeSelectionItem>())
            .IsEqualTo(16);
        await Assert.That(Unsafe.SizeOf<StormPickingInterop.NativeSelectionUpdate>())
            .IsEqualTo(56);
        await Assert.That(OffsetOf<StormPickingInterop.NativePickRequest>("TimeCode"))
            .IsEqualTo(48);
        await Assert.That(OffsetOf<StormPickingInterop.NativePickRequest>("Camera"))
            .IsEqualTo(80);
        await Assert.That(OffsetOf<StormPickingInterop.NativePickResult>("WorldPoint"))
            .IsEqualTo(56);
        await Assert.That(OffsetOf<StormPickingInterop.NativePickResult>("NormalizedDepth"))
            .IsEqualTo(104);
        await Assert.That(OffsetOf<StormPickingInterop.NativeSelectionUpdate>("Items"))
            .IsEqualTo(32);
        await Assert.That(
            RuntimeHelpers.IsReferenceOrContainsReferences<
                StormPickingInterop.NativePickRequest>()).IsFalse();
        await Assert.That(
            RuntimeHelpers.IsReferenceOrContainsReferences<
                StormPickingInterop.NativePickResult>()).IsFalse();
    }

    [Test]
    public async Task MissIsAllocationFreeAfterWarmup()
    {
        RenderPickRequest request = Request(revision: 7);
        StormFrameBinding binding = Binding(revision: 7);
        for (int index = 0; index < 32; index++)
        {
            _ = StormPickingInterop.Pick<MissPickCall>((nint)1, request, binding);
        }
        RenderPickResult representative =
            StormPickingInterop.Pick<MissPickCall>((nint)1, request, binding);

        int callsBefore = Volatile.Read(ref _missCalls);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1024; index++)
        {
            RenderPickResult result =
                StormPickingInterop.Pick<MissPickCall>((nint)1, request, binding);
            if (result.Status != RenderPickStatus.Miss)
            {
                throw new InvalidOperationException();
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(Volatile.Read(ref _missCalls) - callsBefore).IsEqualTo(1024);
        await Assert.That(representative.WorldPosition).IsNull();
        await Assert.That(representative.WorldNormal).IsNull();
        await Assert.That(representative.NormalizedDepth).IsNull();
    }

    [Test]
    public async Task HitDecodesStrictUtf8AndInstanceIdentity()
    {
        RenderPickRequest request = Request(revision: 9, sceneRevision: 12);
        RenderPickResult result = StormPickingInterop.Pick<HitPickCall>(
            (nint)1,
            request,
            Binding(revision: 9, sceneRevision: 12));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo("/World/Grüße");
        await Assert.That(result.InstancerPath).IsEqualTo("/World/Instances");
        await Assert.That(result.InstanceIndex).IsEqualTo(2);
        await Assert.That(result.WorldPosition).IsEqualTo(new Vector3(1, 2, 3));
        await Assert.That(result.WorldNormal).IsEqualTo(Vector3.UnitY);
        await Assert.That(result.NormalizedDepth).IsEqualTo(0.25f);
        await Assert.That(result.BackendKind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(result.StateRevision).IsEqualTo(9ul);
        await Assert.That(result.SceneRevision).IsEqualTo(12ul);
    }

    [Test]
    public async Task OversizedHitUsesOneBoundedBufferRetry()
    {
        Volatile.Write(ref _sizingCalls, 0);
        RenderPickResult result = StormPickingInterop.Pick<SizedHitPickCall>(
            (nint)1,
            Request(revision: 4),
            Binding(revision: 4));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath.Length).IsEqualTo(700);
        await Assert.That(Volatile.Read(ref _sizingCalls)).IsEqualTo(2);
    }

    [Test]
    public async Task NonUtf8HitIsRejected()
    {
        await Assert.That(() => StormPickingInterop.Pick<InvalidUtf8PickCall>(
            (nint)1,
            Request(revision: 1),
            Binding(revision: 1))).Throws<OpenUsdStormException>();
    }

    [Test]
    public async Task RevisionMismatchReturnsStaleWithoutCallingNative()
    {
        Volatile.Write(ref _staleCalls, 0);
        RenderPickResult result = StormPickingInterop.Pick<NeverPickCall>(
            (nint)1,
            Request(revision: 3),
            Binding(revision: 8));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(result.StateRevision).IsEqualTo(8ul);
        await Assert.That(result.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.StateRevision);
        await Assert.That(result.WorldPosition).IsNull();
        await Assert.That(result.WorldNormal).IsNull();
        await Assert.That(result.NormalizedDepth).IsNull();
        await Assert.That(Volatile.Read(ref _staleCalls)).IsEqualTo(0);
    }

    [Test]
    public async Task UnsupportedTargetKeepsIdentityAndGeometryEmpty()
    {
        var request = new RenderPickRequest(
            32,
            32,
            new ViewportDimensions(64, 64),
            requestedStateRevision: 3,
            target: RenderPickTarget.Face);
        RenderPickResult result = StormPickingInterop.Pick<UnsupportedPickCall>(
            (nint)1,
            request,
            Binding(revision: 3));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Unsupported);
        await Assert.That(result.Item).IsNull();
        await Assert.That(result.WorldPosition).IsNull();
        await Assert.That(result.WorldNormal).IsNull();
        await Assert.That(result.NormalizedDepth).IsNull();
    }

    [Test]
    public async Task SelectionUsesOnePackedNativeUpdate()
    {
        Volatile.Write(ref _selectionCalls, 0);
        _selectionPaths = null;
        var selection = new SelectionState(
        [
            new SelectionItem("/World/Cube"),
            new SelectionItem("/World/Instances/Proto", "/World/Instances", 3),
        ]);

        StormPickingInterop.SetSelection<CaptureSelectionCall>(
            (nint)1,
            selection,
            new Vector4(1, 0.5f, 0.25f, 1));

        await Assert.That(Volatile.Read(ref _selectionCalls)).IsEqualTo(1);
        await Assert.That(Volatile.Read(ref _selectionItemCount)).IsEqualTo(2);
        await Assert.That(_selectionPaths)
            .IsEqualTo("/World/Cube|/World/Instances/Proto:3");
    }

    [Test]
    public async Task NativeBindingMismatchReasonsDoNotRequireRevisionFabrication()
    {
        RenderPickResult result = StormPickingInterop.Pick<BindingStalePickCall>(
            (nint)1,
            Request(revision: 1),
            Binding(revision: 1));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(result.StateRevision).IsEqualTo(1ul);
        await Assert.That(result.StaleReasons).IsEqualTo(
            RenderPickStaleReason.Camera |
            RenderPickStaleReason.Time |
            RenderPickStaleReason.ContextGeneration);
    }

    [Test]
    public async Task ViewportAndContextMismatchReturnExplicitStaleReasonsBeforeNativeDispatch()
    {
        Volatile.Write(ref _staleCalls, 0);
        RenderPickRequest request = new(
            1,
            1,
            new ViewportDimensions(64, 32),
            requestedStateRevision: 1);

        RenderPickResult result = StormPickingInterop.Pick<NeverPickCall>(
            (nint)1,
            request,
            Binding(revision: 1),
            currentContextGeneration: 4);

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(result.StaleReasons).IsEqualTo(
            RenderPickStaleReason.Viewport |
            RenderPickStaleReason.ContextGeneration);
        await Assert.That(Volatile.Read(ref _staleCalls)).IsEqualTo(0);
    }

    private static RenderPickRequest Request(
        ulong revision,
        ulong? sceneRevision = null) =>
        new(
            32,
            32,
            new ViewportDimensions(64, 64),
            revision,
            sceneRevision);

    private static StormFrameBinding Binding(
        ulong revision,
        ulong? sceneRevision = null) =>
        new(
            64,
            64,
            2.5,
            CameraState.Default,
            revision,
            sceneRevision,
            ContextGeneration: 3);

    private static void PopulateBinding(
        in StormPickingInterop.NativePickRequest request,
        ref StormPickingInterop.NativePickResult result)
    {
        result.StructSize =
            checked((uint)Unsafe.SizeOf<StormPickingInterop.NativePickResult>());
        result.Version = StormPickingInterop.PickResultVersion;
        result.Flags = (request.Flags & HasSceneRevision) != 0
            ? HasSceneRevision
            : 0;
        result.StateRevision = request.StateRevision;
        result.SceneRevision = request.SceneRevision;
        result.ContextGeneration = request.ContextGeneration;
        result.CameraSignature = 1;
        result.TimeCode = request.TimeCode;
        result.InstanceIndex = -1;
        result.ElementIndex = -1;
        result.NormalizedDepth = 1;
    }

    private static void WritePath(string value, Span<byte> destination)
    {
        int written = Encoding.UTF8.GetBytes(value, destination);
        destination[written] = 0;
    }

    private static int OffsetOf<T>(string fieldName) where T : struct =>
        checked((int)Marshal.OffsetOf<T>(fieldName));

    private readonly struct MissPickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = primPath;
            _ = instancerPath;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            Interlocked.Increment(ref _missCalls);
            PopulateBinding(request, ref result);
            result.Status = StormPickingInterop.NativePickStatus.Miss;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct HitPickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            const string prim = "/World/Grüße";
            const string instancer = "/World/Instances";
            PopulateBinding(request, ref result);
            result.Status = StormPickingInterop.NativePickStatus.Hit;
            result.Flags |= HasInstance;
            result.WorldPoint =
                new StormPickingInterop.NativeVector3d(1, 2, 3);
            result.WorldNormal =
                new StormPickingInterop.NativeVector3d(0, 1, 0);
            result.NormalizedDepth = 0.25;
            result.InstanceIndex = 2;
            result.PrimPathRequired =
                checked((uint)Encoding.UTF8.GetByteCount(prim) + 1);
            result.InstancerPathRequired =
                checked((uint)Encoding.UTF8.GetByteCount(instancer) + 1);
            WritePath(prim, primPath);
            WritePath(instancer, instancerPath);
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct SizedHitPickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = instancerPath;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            Interlocked.Increment(ref _sizingCalls);
            string path = "/" + new string('A', 699);
            PopulateBinding(request, ref result);
            result.Status = StormPickingInterop.NativePickStatus.Hit;
            result.WorldPoint =
                new StormPickingInterop.NativeVector3d(4, 5, 6);
            result.WorldNormal =
                new StormPickingInterop.NativeVector3d(0, 1, 0);
            result.NormalizedDepth = 0.5;
            result.PrimPathRequired = checked((uint)path.Length + 1);
            result.InstancerPathRequired = 1;
            if (primPath.Length < path.Length + 1)
            {
                errorRequired = 0;
                return OpenUsdNativeStatus.BufferTooSmall;
            }
            WritePath(path, primPath);
            instancerPath[0] = 0;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct InvalidUtf8PickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            PopulateBinding(request, ref result);
            result.Status = StormPickingInterop.NativePickStatus.Hit;
            result.WorldPoint =
                new StormPickingInterop.NativeVector3d(4, 5, 6);
            result.WorldNormal =
                new StormPickingInterop.NativeVector3d(0, 1, 0);
            result.NormalizedDepth = 0.5;
            result.PrimPathRequired = 3;
            result.InstancerPathRequired = 1;
            primPath[0] = 0xc3;
            primPath[1] = 0x28;
            primPath[2] = 0;
            instancerPath[0] = 0;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct NeverPickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = request;
            _ = result;
            _ = primPath;
            _ = instancerPath;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            Interlocked.Increment(ref _staleCalls);
            errorRequired = 0;
            return OpenUsdNativeStatus.NativeError;
        }
    }

    private readonly struct UnsupportedPickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = primPath;
            _ = instancerPath;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            PopulateBinding(request, ref result);
            result.Status = StormPickingInterop.NativePickStatus.Unsupported;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct BindingStalePickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = primPath;
            _ = instancerPath;
            _ = instanceContext;
            _ = instanceContextPaths;
            _ = errorBytes;
            PopulateBinding(request, ref result);
            result.Status = StormPickingInterop.NativePickStatus.Stale;
            result.Flags |= 0x400u | 0x1000u | 0x2000u;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct CaptureSelectionCall :
        StormPickingInterop.IStormSelectionCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint handle,
            ReadOnlySpan<StormPickingInterop.NativeSelectionItem> items,
            ReadOnlySpan<byte> pathBytes,
            Vector4 color,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = handle;
            _ = color;
            _ = errorBytes;
            Interlocked.Increment(ref _selectionCalls);
            Volatile.Write(ref _selectionItemCount, items.Length);
            var values = new string[items.Length];
            for (int index = 0; index < items.Length; index++)
            {
                StormPickingInterop.NativeSelectionItem item = items[index];
                string path = Encoding.UTF8.GetString(pathBytes.Slice(
                    (int)item.PathOffset,
                    (int)item.PathLength));
                values[index] = item.Flags == 0
                    ? path
                    : $"{path}:{item.InstanceIndex}";
            }
            _selectionPaths = string.Join('|', values);
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }
}
