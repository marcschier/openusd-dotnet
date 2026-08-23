// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Reports how much of one result page was discarded because a fixed capacity was reached.
/// </summary>
/// <remarks>
/// Overflow is bounded and always reported: the runtime keeps the deterministic prefix of every
/// section and counts the remainder, so a busy step degrades into fewer events instead of an
/// unbounded allocation or a silently truncated result.
/// </remarks>
internal readonly record struct PhysxOverflowReport(
    PhysxOverflowFlags Flags,
    uint DroppedEvents,
    uint DroppedDiagnostics,
    uint DroppedDebugLines)
{
    /// <summary>Gets an empty report.</summary>
    internal static PhysxOverflowReport None => default;

    /// <summary>Gets a value indicating whether anything was dropped.</summary>
    internal bool IsOverflowed => Flags != PhysxOverflowFlags.None;

    /// <summary>Gets a value indicating whether body states were truncated.</summary>
    internal bool BodyStatesTruncated => (Flags & PhysxOverflowFlags.BodyStates) != 0;
}

/// <summary>
/// Carries one fully detached copy of a result page.
/// </summary>
/// <remarks>
/// Every array is an immutable managed copy taken while the native buffers were still leased, so the
/// snapshot stays valid and thread safe after the lease ends and after the buffers are reused by the
/// next step.
/// </remarks>
internal sealed record PhysxResultSnapshot(
    ulong Revision,
    ulong StepIndex,
    double SimulationTime,
    double LastStepSeconds,
    double TotalStepSeconds,
    PhysxWorldState State,
    ImmutableArray<PhysxBodyState> BodyStates,
    ImmutableArray<PhysxDebugLine> DebugLines,
    UsdPhysicsEventBatch Events,
    UsdPhysicsDiagnostics Diagnostics,
    PhysxOverflowReport Overflow)
{
    /// <summary>Gets an empty snapshot.</summary>
    internal static PhysxResultSnapshot Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        PhysxWorldState.Empty,
        [],
        [],
        UsdPhysicsEventBatch.Empty,
        UsdPhysicsDiagnostics.Empty,
        PhysxOverflowReport.None);
}

/// <summary>
/// Owns the caller-allocated, fixed-capacity buffers one result page is written into.
/// </summary>
/// <remarks>
/// The buffers are allocated once, from the capacities the build page declares, and are pinned for
/// their whole lifetime, so stepping never allocates and never moves a buffer while native code
/// writes into it. A capacity of zero is represented by a null pointer, which is exactly what the
/// ABI requires. The pointers never leave this type: callers receive a
/// <see cref="PhysxResultPage"/> that is only valid while these buffers are alive, and receive
/// results as an immutable <see cref="PhysxResultSnapshot"/>.
/// </remarks>
internal sealed unsafe class PhysxResultBuffers : IDisposable
{
    private readonly PhysxBodyState[] _bodyStates;
    private readonly PhysxEventRecord[] _events;
    private readonly PhysxDiagnosticRecord[] _diagnostics;
    private readonly PhysxDebugLine[] _debugLines;
    private readonly PhysxBodyState* _bodyStatePointer;
    private readonly PhysxEventRecord* _eventPointer;
    private readonly PhysxDiagnosticRecord* _diagnosticPointer;
    private readonly PhysxDebugLine* _debugLinePointer;
    private bool _disposed;

    /// <summary>Allocates buffers for the capacities a build page declares.</summary>
    internal PhysxResultBuffers(PhysxResultCapacities capacities)
    {
        Capacities = capacities;
        _bodyStates = Allocate<PhysxBodyState>(capacities.MaxBodyStates, out _bodyStatePointer);
        _events = Allocate<PhysxEventRecord>(capacities.MaxEvents, out _eventPointer);
        _diagnostics = Allocate<PhysxDiagnosticRecord>(capacities.MaxDiagnostics, out _diagnosticPointer);
        _debugLines = Allocate<PhysxDebugLine>(capacities.MaxDebugLines, out _debugLinePointer);
    }

    /// <summary>Gets the capacities these buffers were sized for.</summary>
    internal PhysxResultCapacities Capacities { get; }

    /// <summary>Gets the number of body state slots.</summary>
    internal int BodyStateCapacity => _bodyStates.Length;

    /// <summary>Gets the number of event slots.</summary>
    internal int EventCapacity => _events.Length;

    /// <summary>Gets the number of diagnostic slots.</summary>
    internal int DiagnosticCapacity => _diagnostics.Length;

    /// <summary>Gets the number of debug line slots.</summary>
    internal int DebugLineCapacity => _debugLines.Length;

    /// <summary>Creates the result page description the runtime fills.</summary>
    /// <remarks>The returned page is only valid while this instance is alive and not disposed.</remarks>
    internal PhysxResultPage CreatePage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PhysxResultPage
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxResultPage>(),
            AbiVersion = PhysxAbi.Version,
            BodyStates = _bodyStatePointer,
            BodyStateCapacity = (nuint)_bodyStates.Length,
            Events = _eventPointer,
            EventCapacity = (nuint)_events.Length,
            Diagnostics = _diagnosticPointer,
            DiagnosticCapacity = (nuint)_diagnostics.Length,
            DebugLines = _debugLinePointer,
            DebugLineCapacity = (nuint)_debugLines.Length
        };
    }

    /// <summary>Copies one filled result page into immutable managed memory.</summary>
    internal PhysxResultSnapshot Capture(in PhysxResultPage page, double timeCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PhysxResultHeader header = page.Header;
        var overflow = new PhysxOverflowReport(
            (PhysxOverflowFlags)header.OverflowFlags,
            header.DroppedEventCount,
            header.DroppedDiagnosticCount,
            header.DroppedDebugLineCount);

        int bodyStateCount = Clamp(header.BodyStateCount, _bodyStates.Length);
        int eventCount = Clamp(header.EventCount, _events.Length);
        int diagnosticCount = Clamp(header.DiagnosticCount, _diagnostics.Length);
        int debugLineCount = Clamp(header.DebugLineCount, _debugLines.Length);

        return new PhysxResultSnapshot(
            header.Revision,
            header.StepIndex,
            header.SimulationTime,
            header.LastStepSeconds,
            header.TotalStepSeconds,
            (PhysxWorldState)header.State,
            ImmutableArray.Create(_bodyStates, 0, bodyStateCount),
            ImmutableArray.Create(_debugLines, 0, debugLineCount),
            CaptureEvents(_events.AsSpan(0, eventCount), timeCode, overflow.DroppedEvents),
            CaptureDiagnostics(_diagnostics.AsSpan(0, diagnosticCount), overflow),
            overflow);
    }

    /// <inheritdoc/>
    public void Dispose() => _disposed = true;

    /// <summary>Copies the retained event prefix into the public immutable event batch.</summary>
    internal static UsdPhysicsEventBatch CaptureEvents(
        ReadOnlySpan<PhysxEventRecord> events,
        double timeCode,
        uint droppedCount) => PhysxEventAdapter.Detach(events, timeCode, droppedCount);

    /// <summary>Copies the retained diagnostic prefix into the public immutable diagnostic set.</summary>
    internal static UsdPhysicsDiagnostics CaptureDiagnostics(
        ReadOnlySpan<PhysxDiagnosticRecord> diagnostics,
        PhysxOverflowReport overflow)
    {
        if (diagnostics.IsEmpty && !overflow.IsOverflowed)
        {
            return UsdPhysicsDiagnostics.Empty;
        }

        var entries = new List<UsdPhysicsDiagnostic>(diagnostics.Length + 1);
        foreach (PhysxDiagnosticRecord record in diagnostics)
        {
            PhysxDiagnosticCode code = (PhysxDiagnosticCode)record.Code;
            string mappedCode = MapCode(code);
            string message = DecodeMessage(in record.Message);
            entries.Add(new UsdPhysicsDiagnostic(
                MapSeverity((PhysxDiagnosticSeverity)record.Severity),
                MapCategory(code),
                mappedCode,
                string.IsNullOrWhiteSpace(message) ? mappedCode : message,
                record.Id == PhysxAbi.InvalidId ? null : new UsdPhysicsObjectId(record.Id)));
        }

        if (overflow.IsOverflowed)
        {
            entries.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Step,
                MapCode(PhysxDiagnosticCode.ResultOverflow),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The result page dropped {overflow.DroppedEvents} events, " +
                    $"{overflow.DroppedDiagnostics} diagnostics, and " +
                    $"{overflow.DroppedDebugLines} debug lines.")));
        }

        return new UsdPhysicsDiagnostics(entries);
    }

    /// <summary>Decodes one fixed-length native diagnostic message.</summary>
    internal static string DecodeMessage(in PhysxDiagnosticMessage message)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<PhysxDiagnosticMessage, byte>(ref Unsafe.AsRef(in message)),
            PhysxAbi.DiagnosticMessageBytes);
        int terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            bytes = bytes[..terminator];
        }
        return bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Maps a native event type onto the public event kind.</summary>
    internal static UsdPhysicsEventKind MapEventKind(PhysxEventType type) =>
        PhysxEventAdapter.MapKind(type);

    /// <summary>Maps a native diagnostic severity onto the public severity.</summary>
    internal static UsdPhysicsDiagnosticSeverity MapSeverity(PhysxDiagnosticSeverity severity) => severity switch
    {
        PhysxDiagnosticSeverity.Info => UsdPhysicsDiagnosticSeverity.Information,
        PhysxDiagnosticSeverity.Warning => UsdPhysicsDiagnosticSeverity.Warning,
        _ => UsdPhysicsDiagnosticSeverity.Error
    };

    /// <summary>Maps a native diagnostic code onto the public diagnostic category.</summary>
    internal static UsdPhysicsDiagnosticCategory MapCategory(PhysxDiagnosticCode code) => code switch
    {
        PhysxDiagnosticCode.UnsupportedShape or
        PhysxDiagnosticCode.CookingFailed or
        PhysxDiagnosticCode.ActorCreateFailed or
        PhysxDiagnosticCode.JointCreateFailed or
        PhysxDiagnosticCode.GpuUnavailable or
        PhysxDiagnosticCode.GpuObjectSkipped => UsdPhysicsDiagnosticCategory.Build,
        PhysxDiagnosticCode.CommandTargetMissing or
        PhysxDiagnosticCode.CommandRejected => UsdPhysicsDiagnosticCategory.Command,
        PhysxDiagnosticCode.QueryRejected => UsdPhysicsDiagnosticCategory.Query,
        PhysxDiagnosticCode.ResultOverflow => UsdPhysicsDiagnosticCategory.Step,
        _ => UsdPhysicsDiagnosticCategory.General
    };

    /// <summary>Maps a native diagnostic code onto its stable public code string.</summary>
    internal static string MapCode(PhysxDiagnosticCode code) => code switch
    {
        PhysxDiagnosticCode.UnsupportedShape => "OPENUSD_PHYSICS_UNSUPPORTED_SHAPE",
        PhysxDiagnosticCode.CookingFailed => "OPENUSD_PHYSICS_COOKING_FAILED",
        PhysxDiagnosticCode.ActorCreateFailed => "OPENUSD_PHYSICS_ACTOR_CREATE_FAILED",
        PhysxDiagnosticCode.JointCreateFailed => "OPENUSD_PHYSICS_JOINT_CREATE_FAILED",
        PhysxDiagnosticCode.CommandTargetMissing => "OPENUSD_PHYSICS_COMMAND_TARGET_MISSING",
        PhysxDiagnosticCode.CommandRejected => "OPENUSD_PHYSICS_COMMAND_REJECTED",
        PhysxDiagnosticCode.ResultOverflow => "OPENUSD_PHYSICS_RESULT_OVERFLOW",
        PhysxDiagnosticCode.QueryRejected => "OPENUSD_PHYSICS_QUERY_REJECTED",
        PhysxDiagnosticCode.GpuUnavailable => "OPENUSD_PHYSICS_GPU_UNAVAILABLE",
        PhysxDiagnosticCode.GpuObjectSkipped => "OPENUSD_PHYSICS_GPU_OBJECT_SKIPPED",
        _ => "OPENUSD_PHYSICS_RUNTIME_DIAGNOSTIC"
    };

    private static int Clamp(uint reported, int capacity) => (int)Math.Min(reported, (uint)capacity);

    private static T[] Allocate<T>(uint capacity, out T* pointer)
        where T : unmanaged
    {
        if (capacity == 0)
        {
            pointer = null;
            return [];
        }

        T[] array = GC.AllocateArray<T>((int)capacity, pinned: true);
        pointer = (T*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(array));
        return array;
    }
}
