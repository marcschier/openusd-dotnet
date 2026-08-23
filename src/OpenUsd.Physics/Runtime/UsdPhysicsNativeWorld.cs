// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics;

/// <summary>
/// Wires the transport to the retained native physics world across the project-owned C ABI.
/// </summary>
/// <remarks>
/// <para>
/// The world owns exactly one native handle and one set of pinned result buffers, both created once
/// per build. Stepping reuses them, so the warm path performs no managed allocation at all: the
/// result page is a stack structure pointing at buffers that already exist, and body poses are
/// written straight into the caller's preallocated frame.
/// </para>
/// <para>
/// Until retained stage extraction exists, the build page carries only authored timeline metadata and
/// no simulation content, and that gap is reported as a diagnostic rather than hidden. Every other
/// part of the wiring - world creation, page validation, build, reset, step, and result decoding - is
/// the real path the extracted page will flow through unchanged.
/// </para>
/// </remarks>
internal sealed unsafe class UsdPhysicsNativeWorld : IUsdPhysicsWorld
{
    /// <summary>Reported when the retained page carries no extracted simulation content.</summary>
    internal const string ExtractionUnavailableCode = "OPENUSD_PHYSICS_EXTRACTION_UNAVAILABLE";

    /// <summary>Reported once per authored object the composer could not map onto the world ABI.</summary>
    internal const string CompositionSkippedCode = "OPENUSD_PHYSICS_COMPOSITION_SKIPPED";

    /// <summary>Reported with the object counts the composed world actually simulates.</summary>
    internal const string CompositionSummaryCode = "OPENUSD_PHYSICS_COMPOSITION_SUMMARY";

    /// <summary>Reported when the native runtime refused to create or build the world.</summary>
    internal const string BuildFailedCode = "OPENUSD_PHYSICS_WORLD_BUILD_FAILED";

    /// <summary>Reported when the native runtime refused a step.</summary>
    internal const string StepFailedCode = "OPENUSD_PHYSICS_WORLD_STEP_FAILED";

    /// <summary>Reported when the native runtime refused a reset.</summary>
    internal const string ResetFailedCode = "OPENUSD_PHYSICS_WORLD_RESET_FAILED";

    /// <summary>Reported when a staged runtime command was refused before it reached the world.</summary>
    internal const string CommandRejectedCode = "OPENUSD_PHYSICS_COMMAND_REJECTED";

    /// <summary>
    /// The most runtime commands one advance may carry.
    /// </summary>
    /// <remarks>
    /// The budget exists so a caller that submits faster than the world steps - an interaction that
    /// produces one command per input event, for instance - cannot grow the staging buffer without
    /// bound while the worker is busy. Refusing the surplus is honest; dropping the oldest silently
    /// would discard the very input the user just gave.
    /// </remarks>
    internal const int MaxStagedCommands = 4096;

    /// <summary>
    /// The largest number of undrained diagnostics this world keeps.
    /// </summary>
    /// <remarks>
    /// A step reports its diagnostics through the result page, and the transport drains them every
    /// tick. A caller that steps without ever draining would otherwise grow the queue forever, so
    /// the queue is bounded here for the same reason every native result section is bounded: a busy
    /// step degrades into fewer reports rather than into an unbounded allocation.
    /// </remarks>
    internal const int MaxPendingDiagnostics = 1024;

    /// <summary>Reported when a CUDA-backed domain makes results approximate rather than reproducible.</summary>
    internal const string CudaApproximateCode = "OPENUSD_PHYSICS_CUDA_APPROXIMATE";

    /// <summary>Reported when checkpoints were requested but cannot be proven replay-equivalent.</summary>
    internal const string CheckpointNotReplayEquivalentCode =
        "OPENUSD_PHYSICS_CHECKPOINT_NOT_REPLAY_EQUIVALENT";

    private readonly List<UsdPhysicsDiagnostic> _pending = [];

    private PhysxWorldHandle? _handle;
    private PhysxBodyState[] _bodyStates = [];
    private PhysxEventRecord[] _events = [];
    private PhysxDiagnosticRecord[] _diagnostics = [];
    private PhysxCommand[] _stagedCommands = [];
    private int _stagedCommandCount;
    private PhysxBodyState* _bodyStatePointer;
    private PhysxEventRecord* _eventPointer;
    private PhysxDiagnosticRecord* _diagnosticPointer;
    private PhysxDeformationState[] _deformations = [];
    private PhysxVec3f[] _deformationPoints = [];
    private PhysxDeformationState* _deformationPointer;
    private PhysxVec3f* _deformationPointPointer;
    private bool _faulted;
    private bool _disposed;

    /// <summary>Gets a value indicating whether the world rejected an operation and needs a rebuild.</summary>
    internal bool IsFaulted => _faulted;

    /// <summary>
    /// Gets or sets the extraction page the next build composes its simulation content from.
    /// </summary>
    /// <remarks>
    /// A null page keeps the timeline only build the transport uses when no stage is attached, and
    /// that gap is still reported as a diagnostic rather than hidden.
    /// </remarks>
    internal UsdPhysicsExtractionPage? ExtractionPage { get; set; }

    /// <inheritdoc/>
    public void AttachExtraction(UsdPhysicsExtractionPage? page) => ExtractionPage = page;

    /// <summary>Gets the report of the composition the last successful build ran, if any.</summary>
    internal UsdPhysicsCompositionReport? LastComposition { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The build is transactional. A new native world, its build page, and its result buffers are
    /// created first, and the retained world is replaced only once every one of those steps has
    /// succeeded. A rejected page, a native failure, a cancellation, or an exception therefore
    /// leaves the previously built world exactly as it was, still stepping the content it was
    /// built from, rather than leaving the caller with no world at all.
    /// </remarks>
    public UsdPhysicsWorldBuildResult Build(
        UsdPhysicsTimeline timeline,
        UsdPhysicsFixedStep step,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var pending = new List<UsdPhysicsDiagnostic>();

        PhysxRuntimeInfo runtime = PhysxRuntime.Info;
        if (!runtime.IsAvailable)
        {
            return Reject(pending, runtime.Diagnostics.Entries);
        }

        pending.AddRange(runtime.Diagnostics.Entries);
        cancellationToken.ThrowIfCancellationRequested();

        byte* errorStorage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(errorStorage, PhysxErrorScope.DefaultCapacity);

        var desc = new PhysxWorldDesc
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxWorldDesc>(),
            AbiVersion = PhysxAbi.Version,
            WorkerThreadCount = 0,
            Flags = (uint)(PhysxWorldFlags.EnableEvents | PhysxWorldFlags.Deterministic),
            Reserved0 = 0,
            Reserved1 = 0
        };

        PhysxStatus status = PhysxNativeMethods.WorldCreate(ref desc, out PhysxWorldHandle handle, ref error);
        if (status != PhysxStatus.Ok || handle.IsInvalid)
        {
            handle.Dispose();
            return Reject(pending, status, in error, UsdPhysicsDiagnosticCategory.Build, BuildFailedCode);
        }

        UsdPhysicsCompositionReport? composition = null;
        ResultBuffers buffers = default;
        bool committed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (PhysxBuildPage page = CreateBuildPage(timeline, step, options, out composition))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validation = new PhysxPageValidation { StructSize = (uint)Unsafe.SizeOf<PhysxPageValidation>() };
                using PhysxPageLease lease = page.Lease();
                status = PhysxNativeMethods.WorldBuild(
                    handle,
                    lease.Pointer,
                    (nuint)lease.ByteLength,
                    ref validation,
                    ref error);
            }

            if (status != PhysxStatus.Ok)
            {
                return Reject(pending, status, in error, UsdPhysicsDiagnosticCategory.Build, BuildFailedCode);
            }

            var info = new PhysxWorldStatusInfo { StructSize = (uint)Unsafe.SizeOf<PhysxWorldStatusInfo>() };
            status = PhysxNativeMethods.WorldGetStatus(handle, ref info, ref error);
            if (status != PhysxStatus.Ok)
            {
                return Reject(pending, status, in error, UsdPhysicsDiagnosticCategory.Build, BuildFailedCode);
            }

            buffers = AllocateResultBuffers(in info.Capacities, options);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                // The candidate world never became the retained world, so it is released here and
                // the world this instance already owns is left untouched.
                handle.Dispose();
            }
        }

        // Every fallible step has succeeded, so the retained world is replaced in one move and the
        // world it replaces is released only afterwards.
        PhysxWorldHandle? previous = _handle;
        _handle = handle;
        _bodyStates = buffers.BodyStates;
        _events = buffers.Events;
        _diagnostics = buffers.Diagnostics;
        _bodyStatePointer = buffers.BodyStatePointer;
        _eventPointer = buffers.EventPointer;
        _diagnosticPointer = buffers.DiagnosticPointer;
        _deformations = buffers.Deformations;
        _deformationPoints = buffers.DeformationPoints;
        _deformationPointer = buffers.DeformationPointer;
        _deformationPointPointer = buffers.DeformationPointPointer;
        _faulted = false;
        _stagedCommandCount = 0;
        LastComposition = composition;
        previous?.Dispose();

        UsdPhysicsCapabilities capabilities = runtime.ManagedCapabilities;
        bool approximate = capabilities.Supports(UsdPhysicsCapability.Cuda);
        if (approximate)
        {
            pending.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Information,
                UsdPhysicsDiagnosticCategory.Capability,
                CudaApproximateCode,
                "A CUDA-backed domain is available, so simulation results are approximate and are not " +
                "guaranteed to reproduce bit-for-bit across runs or devices."));
        }

        if (options.CheckpointInterval > 0 && options.MaxCheckpoints > 0)
        {
            pending.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Information,
                UsdPhysicsDiagnosticCategory.Seek,
                CheckpointNotReplayEquivalentCode,
                "Restoring a native checkpoint cannot be proven to reproduce the trajectory produced by " +
                "replaying from the authored start, so seeking replays canonically from the authored start."));
        }

        if (composition is null)
        {
            pending.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Build,
                ExtractionUnavailableCode,
                "No extracted stage is attached, so the built world carries authored timeline metadata " +
                "only and simulates no authored bodies."));
        }
        else
        {
            pending.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Information,
                UsdPhysicsDiagnosticCategory.Build,
                CompositionSummaryCode,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"The composed world simulates {composition.Scenes} scene(s), {composition.Actors} actor(s), " +
                    $"{composition.Shapes} shape(s), {composition.Joints} joint(s), " +
                    $"{composition.FilterPairs} suppressed collision pair(s), " +
                    $"{composition.Gpu.ParticleSystems} particle system(s), " +
                    $"{composition.Gpu.ParticleBodies} particle body(s), " +
                    $"{composition.Gpu.SurfaceDeformables} surface deformable(s), and " +
                    $"{composition.Gpu.VolumeDeformables} volume deformable(s).")));

            // One note per unmapped object, so a stage that authors something the world ABI
            // cannot carry loses exactly that object and says so, instead of failing the build.
            foreach (string note in composition.Skipped)
            {
                pending.Add(new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Warning,
                    UsdPhysicsDiagnosticCategory.Build,
                    CompositionSkippedCode,
                    note));
            }
        }

        _pending.AddRange(pending);
        return new UsdPhysicsWorldBuildResult(
            true,
            capabilities,
            DrainDiagnostics(),
            _bodyStates.Length,
            SupportsReplayEquivalentCheckpoints: false,
            approximate,
            _deformations.Length,
            _deformationPoints.Length);
    }

    /// <inheritdoc/>
    public void ResetToStart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stagedCommandCount = 0;
        if (_handle is not { IsInvalid: false } handle)
        {
            return;
        }

        byte* errorStorage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(errorStorage, PhysxErrorScope.DefaultCapacity);
        var desc = new PhysxResetDesc
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxResetDesc>(),
            Flags = 0,
            SimulationTime = 0,
            BodyStates = null,
            BodyStateCount = 0
        };

        PhysxStatus status = PhysxNativeMethods.WorldReset(handle, ref desc, ref error);
        if (status != PhysxStatus.Ok)
        {
            _faulted = true;
            _pending.Add(PhysxErrorScope.ToDiagnostic(
                status,
                UsdPhysicsDiagnosticCategory.Reset,
                ResetFailedCode,
                in error));
        }
    }

    /// <inheritdoc/>
    public bool TryStep(double fixedSeconds, int subSteps, UsdPhysicsFrame destination)
    {
        bool advanced = TryStep(
            fixedSeconds,
            subSteps,
            _stagedCommands.AsSpan(0, _stagedCommandCount),
            destination);

        // The batch is consumed whether or not the advance succeeded. A failed step faults the
        // world, and replaying the same forces into whatever world replaces it would apply an
        // input the user asked for exactly once a second time.
        _stagedCommandCount = 0;
        return advanced;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every command is translated and validated before it is staged, so a batch that contains one
    /// malformed command still stages the commands that preceded it and reports the rest instead of
    /// costing an interop transition that the native validator would reject wholesale.
    /// </remarks>
    public UsdPhysicsCommandStaging StageCommands(IReadOnlyList<UsdPhysicsCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (commands.Count == 0)
        {
            return new UsdPhysicsCommandStaging(0, 0, "The command batch was empty.");
        }

        if (_handle is not { IsInvalid: false } || _faulted)
        {
            return new UsdPhysicsCommandStaging(
                0,
                commands.Count,
                "The retained world is not built, so runtime commands cannot be applied.");
        }

        int accepted = 0;
        int rejected = 0;
        string? firstRejection = null;
        for (int index = 0; index < commands.Count; index++)
        {
            UsdPhysicsCommand command = commands[index];
            if (command is null)
            {
                rejected++;
                firstRejection ??= "The command batch carries a null command.";
                continue;
            }

            if (_stagedCommandCount == MaxStagedCommands)
            {
                rejected += commands.Count - index;
                firstRejection ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"The staged command budget of {MaxStagedCommands} is full.");
                break;
            }

            if (!PhysxCommandAdapter.TryTranslate(command, out PhysxCommand native, out string? why))
            {
                rejected++;
                firstRejection ??= why;
                continue;
            }

            EnsureStagedCapacity(_stagedCommandCount + 1);
            _stagedCommands[_stagedCommandCount++] = native;
            accepted++;
        }

        if (rejected != 0)
        {
            _pending.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Step,
                CommandRejectedCode,
                firstRejection ?? "A runtime command was refused."));
        }

        return new UsdPhysicsCommandStaging(
            accepted,
            rejected,
            rejected == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Staged {accepted} runtime command(s) for the next simulation step.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Staged {accepted} runtime command(s); {rejected} refused: {firstRejection}"));
    }

    /// <inheritdoc/>
    public void DiscardStagedCommands() => _stagedCommandCount = 0;

    private void EnsureStagedCapacity(int required)
    {
        if (_stagedCommands.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(8, _stagedCommands.Length * 2));
        Array.Resize(ref _stagedCommands, Math.Min(capacity, MaxStagedCommands));
    }

    /// <summary>Advances the world after applying one caller owned command batch.</summary>
    /// <remarks>
    /// The batch is pinned for the duration of the call only, so a warm step that reuses one
    /// command buffer allocates nothing.
    /// </remarks>
    internal bool TryStep(
        double fixedSeconds,
        int subSteps,
        ReadOnlySpan<PhysxCommand> commands,
        UsdPhysicsFrame destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted || _handle is not { IsInvalid: false } handle)
        {
            return false;
        }

        byte* errorStorage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(errorStorage, PhysxErrorScope.DefaultCapacity);
        fixed (PhysxCommand* batch = commands)
        {
            var desc = new PhysxStepDesc
            {
                StructSize = (uint)Unsafe.SizeOf<PhysxStepDesc>(),
                Flags = 0,
                FixedTimeStep = fixedSeconds,
                SubstepCount = (uint)Math.Max(subSteps, 1),
                Reserved = 0,
                Commands = commands.IsEmpty ? null : batch,
                CommandCount = (uint)commands.Length
            };

            PhysxResultPage page = CreateResultPage();
            PhysxStatus status = PhysxNativeMethods.WorldStep(handle, ref desc, ref page, ref error);
            if (status != PhysxStatus.Ok)
            {
                _faulted = true;
                _pending.Add(PhysxErrorScope.ToDiagnostic(
                    status,
                    UsdPhysicsDiagnosticCategory.Step,
                    StepFailedCode,
                    in error));
                return false;
            }

            Fill(destination, in page);
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryFetch(UsdPhysicsFrame destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle is not { IsInvalid: false } handle)
        {
            return false;
        }

        byte* errorStorage = stackalloc byte[PhysxErrorScope.DefaultCapacity];
        var error = new PhysxErrorBuffer(errorStorage, PhysxErrorScope.DefaultCapacity);
        PhysxResultPage page = CreateResultPage();
        PhysxStatus status = PhysxNativeMethods.WorldFetchResults(handle, ref page, ref error);
        if (status != PhysxStatus.Ok)
        {
            _pending.Add(PhysxErrorScope.ToDiagnostic(
                status,
                UsdPhysicsDiagnosticCategory.Step,
                StepFailedCode,
                in error));
            return false;
        }

        Fill(destination, in page);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The native world is never captured: restoring poses and velocities cannot reproduce the
    /// solver's internal state, so a restored world would silently diverge from a canonical replay.
    /// </remarks>
    public int CaptureState(Span<UsdPhysicsBodyPose> destination) => -1;

    /// <inheritdoc/>
    public bool TryRestoreState(ReadOnlySpan<UsdPhysicsBodyPose> state, double simulationSeconds) => false;

    /// <inheritdoc/>
    public UsdPhysicsDiagnostics DrainDiagnostics()
    {
        if (_pending.Count == 0)
        {
            return UsdPhysicsDiagnostics.Empty;
        }

        var diagnostics = new UsdPhysicsDiagnostics(_pending);
        _pending.Clear();
        return diagnostics;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseWorld();
    }

    private PhysxBuildPage CreateBuildPage(
        UsdPhysicsTimeline timeline,
        UsdPhysicsFixedStep step,
        UsdPhysicsSessionOptions options,
        out UsdPhysicsCompositionReport? composition)
    {
        using var builder = new PhysxPageBuilder
        {
            Revision = 1,
            TimeCodesPerSecond = timeline.TimeCodesPerSecond,
            StartTimeCode = timeline.StartTimeCode,
            EndTimeCode = timeline.EndTimeCode,
            SimulationRateHz = (uint)Math.Clamp(
                Math.Round(step.FrequencyHz),
                PhysxAbi.MinSimulationRateHz,
                PhysxAbi.MaxSimulationRateHz),
            MaxSubsteps = (uint)Math.Clamp(
                options.MaxSubStepsPerTick,
                1,
                UsdPhysicsTransportOptions.MaxCatchUpSubStepLimit)
        };

        // Composition runs after the timeline defaults so an extracted stage decides the units and
        // the authored time range, while the resolved fixed step still decides the simulation rate.
        UsdPhysicsExtractionPage? extraction = ExtractionPage;
        composition = extraction is null
            ? null
            : UsdPhysicsExtractionComposer.Compose(extraction, builder);

        return builder.Build();
    }

    /// <summary>Carries one candidate build's result buffers until the build commits.</summary>
    private readonly struct ResultBuffers(
        PhysxBodyState[] bodyStates,
        PhysxEventRecord[] events,
        PhysxDiagnosticRecord[] diagnostics,
        PhysxBodyState* bodyStatePointer,
        PhysxEventRecord* eventPointer,
        PhysxDiagnosticRecord* diagnosticPointer,
        PhysxDeformationState[] deformations,
        PhysxVec3f[] deformationPoints,
        PhysxDeformationState* deformationPointer,
        PhysxVec3f* deformationPointPointer)
    {
        public PhysxBodyState[] BodyStates { get; } = bodyStates;

        public PhysxEventRecord[] Events { get; } = events;

        public PhysxDiagnosticRecord[] Diagnostics { get; } = diagnostics;

        public PhysxBodyState* BodyStatePointer { get; } = bodyStatePointer;

        public PhysxEventRecord* EventPointer { get; } = eventPointer;

        public PhysxDiagnosticRecord* DiagnosticPointer { get; } = diagnosticPointer;

        public PhysxDeformationState[] Deformations { get; } = deformations;

        public PhysxVec3f[] DeformationPoints { get; } = deformationPoints;

        public PhysxDeformationState* DeformationPointer { get; } = deformationPointer;

        public PhysxVec3f* DeformationPointPointer { get; } = deformationPointPointer;
    }

    private static ResultBuffers AllocateResultBuffers(
        in PhysxResultCapacities capacities, UsdPhysicsSessionOptions options)
    {
        int bodies = ClampCapacity(capacities.MaxBodyStates, options.MaxRigidBodies);
        int events = ClampCapacity(capacities.MaxEvents, options.MaxEventsPerStep);
        int diagnostics = ClampCapacity(capacities.MaxDiagnostics, int.MaxValue);
        // The deformation buffers are sized from what the built world reports it
        // will publish, so a CPU only build allocates nothing at all and a build
        // whose GPU objects were skipped allocates nothing either.
        int deformationBodies = ClampCapacity(capacities.MaxDeformationBodies, int.MaxValue);
        int deformationPoints = ClampCapacity(capacities.MaxDeformationPoints, int.MaxValue);
        if (deformationBodies == 0 || deformationPoints == 0)
        {
            deformationBodies = 0;
            deformationPoints = 0;
        }

        PhysxBodyState[] bodyStates = AllocatePinned<PhysxBodyState>(bodies, out PhysxBodyState* bodyPointer);
        PhysxEventRecord[] eventRecords = AllocatePinned<PhysxEventRecord>(events, out PhysxEventRecord* eventPointer);
        PhysxDiagnosticRecord[] diagnosticRecords =
            AllocatePinned<PhysxDiagnosticRecord>(diagnostics, out PhysxDiagnosticRecord* diagnosticPointer);
        PhysxDeformationState[] deformationRecords = AllocatePinned<PhysxDeformationState>(
            deformationBodies, out PhysxDeformationState* deformationPointer);
        PhysxVec3f[] deformationVertices =
            AllocatePinned<PhysxVec3f>(deformationPoints, out PhysxVec3f* deformationPointPointer);
        return new ResultBuffers(
            bodyStates,
            eventRecords,
            diagnosticRecords,
            bodyPointer,
            eventPointer,
            diagnosticPointer,
            deformationRecords,
            deformationVertices,
            deformationPointer,
            deformationPointPointer);
    }

    private static int ClampCapacity(uint declared, int requested)
    {
        int value = declared > int.MaxValue ? int.MaxValue : (int)declared;
        return requested <= 0 ? value : Math.Min(value, requested);
    }

    private static T[] AllocatePinned<T>(int count, out T* pointer)
        where T : unmanaged
    {
        if (count <= 0)
        {
            pointer = null;
            return [];
        }

        T[] buffer = GC.AllocateArray<T>(count, pinned: true);
        pointer = (T*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer));
        return buffer;
    }

    private PhysxResultPage CreateResultPage() => new()
    {
        StructSize = (uint)Unsafe.SizeOf<PhysxResultPage>(),
        AbiVersion = PhysxAbi.Version,
        BodyStates = _bodyStatePointer,
        BodyStateCapacity = (nuint)_bodyStates.Length,
        Events = _eventPointer,
        EventCapacity = (nuint)_events.Length,
        Diagnostics = _diagnosticPointer,
        DiagnosticCapacity = (nuint)_diagnostics.Length,
        DebugLines = null,
        DebugLineCapacity = 0,
        Deformations = _deformationPointer,
        DeformationCapacity = (nuint)_deformations.Length,
        DeformationPoints = _deformationPointPointer,
        DeformationPointCapacity = (nuint)_deformationPoints.Length
    };

    private void Fill(UsdPhysicsFrame destination, in PhysxResultPage page)
    {
        uint reported = page.Header.BodyStateCount;
        int limit = Math.Min(destination.BodyCapacity, _bodyStates.Length);
        int count = reported > (uint)limit ? limit : (int)reported;

        Span<UsdPhysicsBodyPose> bodies = destination.BodyBuffer;
        for (int index = 0; index < count; index++)
        {
            ref readonly PhysxBodyState state = ref _bodyStatePointer[index];
            bodies[index] = new UsdPhysicsBodyPose(
                new UsdPhysicsObjectId(state.Id, UsdPhysicsObjectKind.RigidBody),
                new UsdVec3d(state.Pose.Position.X, state.Pose.Position.Y, state.Pose.Position.Z),
                new UsdPhysicsOrientation(
                    state.Pose.Rotation.X,
                    state.Pose.Rotation.Y,
                    state.Pose.Rotation.Z,
                    state.Pose.Rotation.W),
                new UsdVec3d(state.LinearVelocity.X, state.LinearVelocity.Y, state.LinearVelocity.Z),
                new UsdVec3d(state.AngularVelocity.X, state.AngularVelocity.Y, state.AngularVelocity.Z),
                (state.Flags & (uint)PhysxBodyStateFlags.Sleeping) != 0,
                (state.Flags & (uint)PhysxBodyStateFlags.Kinematic) != 0);
        }

        destination.SetBodyCount(count);
        FillDeformation(destination, in page);
        DrainNativeDiagnostics(in page);
        destination.DroppedEventCount = page.Header.DroppedEventCount > int.MaxValue
            ? int.MaxValue
            : (int)page.Header.DroppedEventCount;
        destination.HasOverflow = page.Header.OverflowFlags != 0;
        destination.BodiesTruncated = reported > (uint)count;
    }

    /// <summary>Moves the diagnostics one result page carries into the managed queue.</summary>
    /// <remarks>
    /// The retained world reports per object build failures - a collider it could not cook, a GPU
    /// object it could not create on a machine without a device - through the result page rather
    /// than through the build call, because they are discovered while the page is being applied.
    /// Draining them here is what makes them reachable through
    /// <see cref="DrainDiagnostics"/> instead of being overwritten by the next step.
    /// The warm path stays allocation free: a step that reports nothing does no work at all.
    /// </remarks>
    private void DrainNativeDiagnostics(in PhysxResultPage page)
    {
        uint reported = page.Header.DiagnosticCount;
        if (reported == 0 || _diagnostics.Length == 0 || _pending.Count >= MaxPendingDiagnostics)
        {
            return;
        }

        int count = (int)Math.Min(reported, (uint)_diagnostics.Length);
        count = Math.Min(count, MaxPendingDiagnostics - _pending.Count);
        for (int index = 0; index < count; index++)
        {
            ref readonly PhysxDiagnosticRecord record = ref _diagnosticPointer[index];
            var code = (PhysxDiagnosticCode)record.Code;
            string mapped = PhysxResultBuffers.MapCode(code);
            string message = PhysxResultBuffers.DecodeMessage(in record.Message);
            _pending.Add(new UsdPhysicsDiagnostic(
                PhysxResultBuffers.MapSeverity((PhysxDiagnosticSeverity)record.Severity),
                PhysxResultBuffers.MapCategory(code),
                mapped,
                string.IsNullOrWhiteSpace(message) ? mapped : message,
                record.Id == PhysxAbi.InvalidId ? null : new UsdPhysicsObjectId(record.Id)));
        }
    }

    /// <summary>Copies one result page's deformation windows into the frame.</summary>
    /// <remarks>
    /// A window is copied only when its whole vertex range fits, because a half copied body would
    /// let a consumer read vertices that belong to the previous step. A body that does not fit is
    /// dropped and the frame reports the truncation instead.
    /// </remarks>
    private void FillDeformation(UsdPhysicsFrame destination, in PhysxResultPage page)
    {
        if (destination.DeformationCapacity == 0 || _deformations.Length == 0)
        {
            destination.SetDeformationCounts(0, 0, page.Header.DeformationBodyCount != 0);
            return;
        }

        uint reportedBodies = Math.Min(page.Header.DeformationBodyCount, (uint)_deformations.Length);
        Span<UsdPhysicsDeformation> bodies = destination.DeformationBuffer;
        Span<UsdVec3d> vertices = destination.DeformationVertexBuffer;
        int writtenBodies = 0;
        int writtenVertices = 0;
        bool truncated = page.Header.DroppedDeformationBodyCount != 0 ||
            page.Header.DeformationBodyCount > (uint)_deformations.Length;

        for (uint index = 0; index < reportedBodies; index++)
        {
            ref readonly PhysxDeformationState state = ref _deformationPointer[index];
            long end = (long)state.PointOffset + state.PointCount;
            if (state.PointCount == 0 || end > _deformationPoints.Length ||
                writtenBodies >= bodies.Length ||
                (long)writtenVertices + state.PointCount > vertices.Length)
            {
                truncated = true;
                continue;
            }

            for (uint point = 0; point < state.PointCount; point++)
            {
                ref readonly PhysxVec3f source = ref _deformationPointPointer[state.PointOffset + point];
                vertices[writtenVertices + (int)point] = new UsdVec3d(source.X, source.Y, source.Z);
            }

            bodies[writtenBodies] = new UsdPhysicsDeformation(
                new UsdPhysicsObjectId(state.Id, MapDeformationOwner((PhysxDeformationKind)state.Kind)),
                MapDeformationKind((PhysxDeformationKind)state.Kind),
                writtenVertices,
                (int)state.PointCount,
                (state.Flags & (uint)PhysxDeformationFlags.Sleeping) != 0);
            writtenVertices += (int)state.PointCount;
            writtenBodies++;
        }

        destination.SetDeformationCounts(writtenBodies, writtenVertices, truncated);
    }

    private static UsdPhysicsDeformationKind MapDeformationKind(PhysxDeformationKind kind) => kind switch
    {
        PhysxDeformationKind.Fluid => UsdPhysicsDeformationKind.Fluid,
        PhysxDeformationKind.Surface => UsdPhysicsDeformationKind.Surface,
        PhysxDeformationKind.Volume => UsdPhysicsDeformationKind.Volume,
        _ => UsdPhysicsDeformationKind.Particles
    };

    private static UsdPhysicsObjectKind MapDeformationOwner(PhysxDeformationKind kind) => kind switch
    {
        PhysxDeformationKind.Surface or PhysxDeformationKind.Volume => UsdPhysicsObjectKind.Deformable,
        _ => UsdPhysicsObjectKind.ParticleSystem
    };

    private static UsdPhysicsWorldBuildResult Reject(
        List<UsdPhysicsDiagnostic> pending,
        IEnumerable<UsdPhysicsDiagnostic> entries)
    {
        // A failed build never touches the retained world, so the diagnostics it produced are
        // reported to the caller only and are never mixed into the live world's queue.
        pending.AddRange(entries);
        return new UsdPhysicsWorldBuildResult(
            false,
            UsdPhysicsCapabilities.None,
            new UsdPhysicsDiagnostics(pending),
            0,
            false,
            false);
    }

    private static UsdPhysicsWorldBuildResult Reject(
        List<UsdPhysicsDiagnostic> pending,
        PhysxStatus status,
        in PhysxErrorBuffer error,
        UsdPhysicsDiagnosticCategory category,
        string code)
    {
        pending.Add(PhysxErrorScope.ToDiagnostic(status, category, code, in error));
        return new UsdPhysicsWorldBuildResult(
            false,
            UsdPhysicsCapabilities.None,
            new UsdPhysicsDiagnostics(pending),
            0,
            false,
            false);
    }

    private void ReleaseWorld()
    {
        _handle?.Dispose();
        _handle = null;
        _bodyStates = [];
        _events = [];
        _diagnostics = [];
        _bodyStatePointer = null;
        _eventPointer = null;
        _diagnosticPointer = null;
    }
}
