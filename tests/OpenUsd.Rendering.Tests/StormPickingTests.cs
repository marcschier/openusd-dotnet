// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
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
    private const uint HasInstanceContext = 8;
    private static int _missCalls;
    private static int _staleCalls;
    private static int _sizingCalls;
    private static int _selectionCalls;
    private static int _selectionItemCount;
    private static string? _selectionPaths;
    private static int[]? _selectionIndices;

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
    public async Task ANestedInstancerContextIsCarriedThroughInNativeOrder()
    {
        RenderPickRequest request = Request(revision: 9, sceneRevision: 12);
        RenderPickResult result = StormPickingInterop.Pick<NestedContextPickCall>(
            (nint)1,
            request,
            Binding(revision: 9, sceneRevision: 12));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.Item).IsNotNull();

        SelectionItem item = result.Item!.Value;
        await Assert.That(item.InstancerContext.Count).IsEqualTo(2);
        await Assert.That(item.InstancerContext[0].InstancerPath)
            .IsEqualTo("/World/Outer");
        await Assert.That(item.InstancerContext[0].InstanceIndex).IsEqualTo(4);
        await Assert.That(item.InstancerContext[1].InstancerPath)
            .IsEqualTo("/World/Outer/Inner");
        await Assert.That(item.InstancerContext[1].InstanceIndex).IsEqualTo(2);

        // The flattened convenience pair reports the innermost level, which is
        // exactly the level the native side derives its own instancer identity
        // from, so the two can never disagree.
        await Assert.That(result.InstancerPath).IsEqualTo("/World/Outer/Inner");
        await Assert.That(result.InstanceIndex).IsEqualTo(2);
    }

    /// <summary>
    /// A nested hit whose flattened instance index differs from the innermost
    /// level's own index is a correct result, not a contradiction.
    /// </summary>
    /// <remarks>
    /// Hydra reports the flattened index of the whole nested instancing in the
    /// hit, while every context entry carries that level's own local index. For
    /// a two-level context the two legitimately disagree, so comparing them
    /// rejected results that were exactly right. The chain is what describes the
    /// instance, and it is reported unchanged.
    /// </remarks>
    [Test]
    public async Task AFlattenedIndexUnequalToTheInnermostLocalIndexIsAccepted()
    {
        RenderPickRequest request = Request(revision: 9, sceneRevision: 12);
        RenderPickResult result =
            StormPickingInterop.Pick<DisagreeingContextPickCall>(
                (nint)1,
                request,
                Binding(revision: 9, sceneRevision: 12));

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.Item).IsNotNull();

        SelectionItem item = result.Item!.Value;
        await Assert.That(item.InstancerContext.Count).IsEqualTo(2);
        await Assert.That(item.InstancerContext[0].InstancerPath)
            .IsEqualTo("/World/Outer");
        await Assert.That(item.InstancerContext[0].InstanceIndex).IsEqualTo(4);
        await Assert.That(item.InstancerContext[1].InstancerPath)
            .IsEqualTo("/World/Outer/Inner");

        // The innermost level keeps its own local index, which is the fixture's
        // 9 and not the flattened 2 the result reports.
        await Assert.That(item.InstancerContext[1].InstanceIndex).IsEqualTo(9);
    }

    /// <summary>
    /// Storm selection highlighting refuses a nested item instead of passing an
    /// innermost local index the packed ABI cannot mean.
    /// </summary>
    /// <remarks>
    /// The packed update carries one (path, index) pair per item, which names a
    /// single instancing level. Sending the innermost level's local index would
    /// highlight some other instance and look like a working selection, so the
    /// operation fails honestly instead.
    /// </remarks>
    [Test]
    public async Task ANestedSelectionItemIsRefusedByStormHighlighting()
    {
        var nested = new SelectionState(
        [
            SelectionItem.FromInstancerContext(
                "/World/Prototypes/Leaf",
                [
                    new SelectionInstancerEntry("/World/Outer", 4),
                    new SelectionInstancerEntry("/World/Outer/Inner", 9)
                ])
        ]);

        await Assert.That(() => StormPickingInterop.SetSelection<AcceptingSelectionCall>(
                (nint)1,
                nested,
                new Vector4(1, 1, 1, 1)))
            .Throws<NotSupportedException>();
    }

    /// <summary>
    /// A single-level item is refused too, and nothing reaches the native
    /// selection ABI.
    /// </summary>
    /// <remarks>
    /// One instancing level looks expressible -- the packed item has exactly one
    /// (path, index) slot -- but the index it carries reaches Hydra's legacy
    /// <c>AddSelected</c>, which addresses a flattened instance ordinal. The
    /// entry's index is that level's own, per-prototype index, and the two only
    /// coincide while an instancer instances exactly one prototype. Sending it
    /// highlights an unrelated instance in every other scene and looks like a
    /// working selection, so instance-specific selection is refused outright
    /// until a context-aware native ABI exists.
    /// </remarks>
    [Test]
    public async Task ASingleLevelSelectionItemIsAlsoRefusedByStormHighlighting()
    {
        var single = new SelectionState(
        [
            new SelectionItem("/World/Prototypes/Leaf", "/World/Instances", 3)
        ]);

        NotSupportedException failure = Assert.Throws<NotSupportedException>(
            () => StormPickingInterop.SetSelection<NeverSelectionCall>(
                (nint)1,
                single,
                new Vector4(1, 1, 1, 1)));

        await Assert.That(failure.Message).Contains("instance identity");
        await Assert.That(failure.Message).Contains("/World/Prototypes/Leaf");
    }

    /// <summary>
    /// A selection call that proves nothing reached the native ABI, without
    /// touching the shared capture counters other tests read.
    /// </summary>
    private readonly struct NeverSelectionCall
        : StormPickingInterop.IStormSelectionCall
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
            _ = items;
            _ = pathBytes;
            _ = color;
            _ = errorBytes;
            errorRequired = 0;
            throw new InvalidOperationException(
                "A refused Storm selection reached the native ABI.");
        }
    }

    /// <summary>
    /// A whole-prim item is unaffected: it carries no index, so nothing about
    /// the flattened-ordinal ambiguity applies to it.
    /// </summary>
    [Test]
    public async Task AWholePrimSelectionItemIsStillAccepted()
    {
        var whole = new SelectionState(
        [
            new SelectionItem("/World/Prototypes/Leaf")
        ]);

        await Assert.That(() => StormPickingInterop.SetSelection<AcceptingSelectionCall>(
                (nint)1,
                whole,
                new Vector4(1, 1, 1, 1)))
            .ThrowsNothing();
    }

    private readonly struct AcceptingSelectionCall
        : StormPickingInterop.IStormSelectionCall
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
            _ = items;
            _ = pathBytes;
            _ = color;
            _ = errorBytes;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
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

    /// <summary>
    /// Two whole-prim items reach the native side in one packed update, and no
    /// packed item carries an instance index.
    /// </summary>
    /// <remarks>
    /// The absent index is the point: no item ever packs one, because
    /// instance-specific selection is refused before packing. That is what makes
    /// it impossible for a level's own per-prototype index to reach a native ABI
    /// that reads it as a flattened ordinal.
    /// </remarks>
    [Test]
    public async Task SelectionUsesOnePackedNativeUpdate()
    {
        Volatile.Write(ref _selectionCalls, 0);
        _selectionPaths = null;
        _selectionIndices = null;
        var selection = new SelectionState(
        [
            new SelectionItem("/World/Cube"),
            new SelectionItem("/World/Instances/Proto"),
        ]);

        StormPickingInterop.SetSelection<CaptureSelectionCall>(
            (nint)1,
            selection,
            new Vector4(1, 0.5f, 0.25f, 1));

        await Assert.That(Volatile.Read(ref _selectionCalls)).IsEqualTo(1);
        await Assert.That(Volatile.Read(ref _selectionItemCount)).IsEqualTo(2);
        await Assert.That(_selectionPaths)
            .IsEqualTo("/World/Cube|/World/Instances/Proto");
        await Assert.That(_selectionIndices).IsEquivalentTo(new[] { -1, -1 });
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

    private readonly struct NestedContextPickCall : StormPickingInterop.IStormPickCall
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
            _ = errorBytes;
            errorRequired = 0;
            return WriteNestedHit(
                request,
                ref result,
                primPath,
                instancerPath,
                instanceContext,
                instanceContextPaths,
                innermostIndex: 2);
        }
    }

    private readonly struct DisagreeingContextPickCall
        : StormPickingInterop.IStormPickCall
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
            _ = errorBytes;
            errorRequired = 0;

            // The innermost level says instance 9 while the result says 2. A
            // consumer would read two different instances from one hit, so the
            // result is incompatible rather than merely surprising.
            return WriteNestedHit(
                request,
                ref result,
                primPath,
                instancerPath,
                instanceContext,
                instanceContextPaths,
                innermostIndex: 9);
        }
    }

    /// <summary>
    /// Writes a two-level nested hit: an outer instancer, an inner one, and the
    /// innermost identity the result reports separately.
    /// </summary>
    private static OpenUsdNativeStatus WriteNestedHit(
        in StormPickingInterop.NativePickRequest request,
        ref StormPickingInterop.NativePickResult result,
        Span<byte> primPath,
        Span<byte> instancerPath,
        Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
        Span<byte> instanceContextPaths,
        int innermostIndex)
    {
        const string prim = "/World/Protos/Leaf";
        const string outer = "/World/Outer";
        const string inner = "/World/Outer/Inner";
        PopulateBinding(request, ref result);
        result.Status = StormPickingInterop.NativePickStatus.Hit;
        result.Flags |= HasInstance | HasInstanceContext;
        result.WorldPoint = new StormPickingInterop.NativeVector3d(1, 2, 3);
        result.WorldNormal = new StormPickingInterop.NativeVector3d(0, 1, 0);
        result.NormalizedDepth = 0.25;
        result.InstanceIndex = 2;
        result.PrimPathRequired =
            checked((uint)Encoding.UTF8.GetByteCount(prim) + 1);
        result.InstancerPathRequired =
            checked((uint)Encoding.UTF8.GetByteCount(inner) + 1);
        result.InstanceContextCount = 2;
        uint outerBytes = checked((uint)Encoding.UTF8.GetByteCount(outer));
        uint innerBytes = checked((uint)Encoding.UTF8.GetByteCount(inner));
        result.InstanceContextPathsRequired = outerBytes + 1 + innerBytes + 1;
        if (primPath.Length < result.PrimPathRequired ||
            instancerPath.Length < result.InstancerPathRequired ||
            instanceContext.Length < 2 ||
            instanceContextPaths.Length < result.InstanceContextPathsRequired)
        {
            return OpenUsdNativeStatus.BufferTooSmall;
        }

        WritePath(prim, primPath);
        WritePath(inner, instancerPath);
        WritePath(outer, instanceContextPaths);
        WritePath(inner, instanceContextPaths[(int)(outerBytes + 1)..]);
        WriteContextEntry(instanceContext, 0, 0, outerBytes, 4);
        WriteContextEntry(instanceContext, 1, outerBytes + 1, innerBytes, innermostIndex);
        return OpenUsdNativeStatus.Ok;
    }

    /// <summary>
    /// Writes one native instancer-context entry through its raw bytes, because
    /// the interop struct is immutable by design.
    /// </summary>
    private static void WriteContextEntry(
        Span<StormPickingInterop.NativePickInstanceContext> entries,
        int index,
        uint pathOffset,
        uint pathLength,
        int instanceIndex)
    {
        Span<byte> bytes =
            MemoryMarshal.AsBytes(entries.Slice(index, 1));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes[4..],
            StormPickingInterop.PickInstanceContextVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], pathOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], pathLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..], 0);
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
            var indices = new int[items.Length];
            for (int index = 0; index < items.Length; index++)
            {
                StormPickingInterop.NativeSelectionItem item = items[index];
                string path = Encoding.UTF8.GetString(pathBytes.Slice(
                    (int)item.PathOffset,
                    (int)item.PathLength));
                values[index] = item.Flags == 0
                    ? path
                    : $"{path}:{item.InstanceIndex}";
                indices[index] = item.InstanceIndex;
            }
            _selectionPaths = string.Join('|', values);
            _selectionIndices = indices;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }
}
