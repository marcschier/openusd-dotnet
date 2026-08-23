// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using OpenUsd.Physics;
using OpenUsd.Physics.Baking;
using OpenUsd.Physics.Extraction;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>Reads monotonic wall-clock time for physics pacing.</summary>
/// <remarks>
/// Pacing is injected rather than read from <see cref="Stopwatch"/> directly so the controller's
/// playback, speed, loop, and debounce behaviour can be driven step by step in tests instead of
/// being timed against a real clock, which is the only way those rules can be asserted exactly.
/// </remarks>
internal interface IViewerPhysicsClock
{
    /// <summary>Gets monotonically increasing seconds from an arbitrary origin.</summary>
    double NowSeconds { get; }
}

/// <summary>Reads monotonic time from a process-wide stopwatch.</summary>
internal sealed class ViewerPhysicsStopwatchClock : IViewerPhysicsClock
{
    /// <summary>Gets the shared clock.</summary>
    internal static ViewerPhysicsStopwatchClock Instance { get; } = new();

    /// <inheritdoc/>
    public double NowSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

/// <summary>
/// The physics transport surface the viewer's controller drives.
/// </summary>
/// <remarks>
/// <para>
/// Nothing on this surface carries a USD stage handle, a prim, or a solver handle: the controller
/// exchanges only time codes, whole step counts, and renderer-neutral snapshots. That is what makes
/// it safe for the controller to be driven from the UI thread while a dedicated physics worker owns
/// the retained world, and it is what lets the deterministic tests substitute a fake transport
/// without simulating anything.
/// </para>
/// <para>
/// Every failure is reported as a <see cref="ViewerPhysicsException"/> so a full request queue, a
/// stale world, and a faulted solver reach the UI as diagnostics rather than as transport-specific
/// exception types the shell would have to know about.
/// </para>
/// </remarks>
internal interface IViewerPhysicsTransport : IAsyncDisposable
{
    /// <summary>Gets one atomically consistent view of transport progress.</summary>
    ViewerPhysicsTransportStatus Status { get; }

    /// <summary>Gets the fixed simulation step, in seconds.</summary>
    double FixedStepSeconds { get; }

    /// <summary>Gets the authored start time code.</summary>
    double StartTimeCode { get; }

    /// <summary>Gets the authored end time code.</summary>
    double EndTimeCode { get; }

    /// <summary>Gets what the built world reports it can simulate.</summary>
    IReadOnlyList<ViewerPhysicsCapabilitySupport> Capabilities { get; }

    /// <summary>Gets the diagnostics the most recent operation produced.</summary>
    IReadOnlyList<ViewerPhysicsDiagnosticRow> Diagnostics { get; }

    /// <summary>Reads the identity map that binds simulated objects to authored prims.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identity map of the current stage revision.</returns>
    /// <remarks>
    /// The map is read once per build rather than per frame: it is the product of one stage
    /// traversal, and traversing the stage while frames are being consumed would put USD back on
    /// the render path this whole design exists to keep it off.
    /// </remarks>
    ValueTask<ViewerPhysicsBindingSet> LoadBindingsAsync(CancellationToken cancellationToken);

    /// <summary>Builds or rebuilds the retained world.</summary>
    /// <param name="cancellationToken">Cancels the build.</param>
    Task BuildAsync(CancellationToken cancellationToken);

    /// <summary>Returns the world to the authored start time code.</summary>
    /// <param name="cancellationToken">Cancels the reset.</param>
    Task ResetAsync(CancellationToken cancellationToken);

    /// <summary>Moves the world to an authored time code.</summary>
    /// <param name="timeCode">The authored time code to seek to.</param>
    /// <param name="cancellationToken">Cancels the seek while it replays.</param>
    Task SeekAsync(double timeCode, CancellationToken cancellationToken);

    /// <summary>Advances the world by whole fixed simulation steps.</summary>
    /// <param name="steps">The number of fixed steps to advance.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task StepAsync(int steps, CancellationToken cancellationToken);

    /// <summary>Changes whether playback wraps at the authored end.</summary>
    /// <param name="loop">Whether playback wraps.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task SetLoopAsync(bool loop, CancellationToken cancellationToken);

    /// <summary>Marks the built world stale because a physics-relevant edit changed the stage.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task InvalidateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stages one batch of interactive runtime commands for the next simulation step.
    /// </summary>
    /// <remarks>
    /// The whole batch crosses the boundary once. A viewer that submitted one command per pointer
    /// event would pay a transition per event on the warm interaction path, which is exactly the
    /// per-element interop the scene path forbids.
    /// </remarks>
    /// <param name="commands">The commands to stage, in submission order.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the world accepted and what it refused.</returns>
    Task<ViewerPhysicsCommandOutcome> SubmitCommandsAsync(
        IReadOnlyList<ViewerPhysicsRuntimeCommand> commands,
        CancellationToken cancellationToken);

    /// <summary>Reads every extracted physics object and property for the inspector.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The extracted objects, detached from the stage.</returns>
    /// <remarks>
    /// The document is read on demand rather than per frame. It is the product of one stage
    /// traversal and the inspector only needs it when the user is looking at it.
    /// </remarks>
    ValueTask<ViewerPhysicsExtractionDocument> LoadInspectorAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Copies the latest complete simulation frame into the renderer-neutral channel.
    /// </summary>
    /// <param name="channel">The channel the render bridge consumes from.</param>
    /// <returns><see langword="true"/> when a new complete frame was published.</returns>
    bool TryPublishLatestFrame(PhysicsRenderChannel channel);

    /// <summary>Applies or clears simulated poses in the session overlay.</summary>
    /// <param name="enabled">Whether preview opinions are authored.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the preview did, including the authored change it produced.</returns>
    Task<ViewerPhysicsPreviewOutcome> ApplyPreviewAsync(
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>Bakes simulated poses into a file-backed destination layer.</summary>
    /// <param name="request">The validated bake request.</param>
    /// <param name="progress">Receives bounded progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the bake, which rolls the destination back.</param>
    /// <returns>What the bake did.</returns>
    Task<ViewerPhysicsBakeOutcome> BakeAsync(
        ViewerPhysicsBakeRequest request,
        IProgress<ViewerPhysicsBakeProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Creates the physics transport for one open document, on demand.</summary>
/// <remarks>
/// Creation is deferred until a user asks for physics: a viewer that built a physics world for
/// every opened stage would pay extraction, solver, and worker-thread cost for the majority of
/// stages that carry no physics at all.
/// </remarks>
internal interface IViewerPhysicsTransportFactory
{
    /// <summary>Creates a transport for the current document.</summary>
    /// <param name="cancellationToken">Cancels creation.</param>
    ValueTask<IViewerPhysicsTransport> CreateAsync(CancellationToken cancellationToken);
}

/// <summary>Creates transports bound to one stage scheduler.</summary>
internal sealed class ViewerPhysicsTransportFactory(UsdStageScheduler scheduler)
    : IViewerPhysicsTransportFactory
{
    /// <inheritdoc/>
    public async ValueTask<IViewerPhysicsTransport> CreateAsync(
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        UsdPhysicsTransport transport;
        try
        {
            transport = await UsdPhysicsTransport
                .CreateAsync(scheduler, options: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw ViewerPhysicsTransportAdapter.Translate(exception);
        }

        return new ViewerPhysicsTransportAdapter(scheduler, transport);
    }
}

/// <summary>
/// Adapts the retained <see cref="UsdPhysicsTransport"/> to the viewer's transport surface.
/// </summary>
/// <remarks>
/// The adapter is the only place in the viewer that touches physics or USD types for simulation. It
/// copies every published frame into a renderer-neutral snapshot and detaches every bake batch, so
/// no frame lease, stage handle, or solver handle ever crosses into the render loop or the UI.
/// </remarks>
internal sealed class ViewerPhysicsTransportAdapter : IViewerPhysicsTransport
{
    private readonly UsdStageScheduler _scheduler;
    private readonly UsdPhysicsTransport _transport;
    private readonly ViewerPhysicsMetadataCache _metadata = new();
    private UsdPhysicsBakeBindings _bindings = UsdPhysicsBakeBindings.Empty;
    private UsdPhysicsExtractionPage? _extraction;
    private UsdPhysicsPreviewApplier? _preview;
    private ulong _publishedRevision;
    private float[] _vertexScratch = [];
    private int _disposed;

    /// <summary>Initializes an adapter over a created transport.</summary>
    /// <param name="scheduler">The scheduler owning the stage.</param>
    /// <param name="transport">The retained transport to drive.</param>
    internal ViewerPhysicsTransportAdapter(
        UsdStageScheduler scheduler,
        UsdPhysicsTransport transport)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(transport);
        _scheduler = scheduler;
        _transport = transport;
    }

    /// <inheritdoc/>
    public ViewerPhysicsTransportStatus Status
    {
        get
        {
            UsdPhysicsTransportStatus status = _transport.Status;
            return new ViewerPhysicsTransportStatus(
                MapState(status.State),
                status.Revision,
                status.StepIndex,
                status.TimeCode,
                status.SimulationSeconds,
                status.BacklogSeconds,
                status.LoopCount,
                status.QueueDepth);
        }
    }

    /// <inheritdoc/>
    public double FixedStepSeconds => _transport.FixedStep.Seconds;

    /// <inheritdoc/>
    public double StartTimeCode => _transport.Timeline.StartTimeCode;

    /// <inheritdoc/>
    public double EndTimeCode => _transport.Timeline.EndTimeCode;

    /// <inheritdoc/>
    /// <remarks>
    /// The matrix is read once per painted frame, so the rows are cached against the capability
    /// flags they were built from and the same instance is returned until a flag moves.
    /// </remarks>
    public IReadOnlyList<ViewerPhysicsCapabilitySupport> Capabilities =>
        _metadata.GetCapabilities(_transport.Capabilities.Features);

    /// <inheritdoc/>
    /// <remarks>
    /// Diagnostics are read as often as the capability matrix is, and the retained set is immutable,
    /// so an unchanged set is recognised by its identity and a rebuilt set carrying the same entries
    /// is recognised by comparing the entries themselves.
    /// </remarks>
    public IReadOnlyList<ViewerPhysicsDiagnosticRow> Diagnostics =>
        _metadata.GetDiagnostics(_transport.Diagnostics);

    /// <inheritdoc/>
    /// <remarks>
    /// A build composes the stage as it is right now, so the stage is extracted and attached in the
    /// same operation. Extracting once here and reusing the page for the binding table and the
    /// inspector keeps one build to one stage traversal, and guarantees the world, the bindings,
    /// and the inspector all describe the same revision of the scene.
    /// </remarks>
    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        UsdPhysicsExtractionPage page = await ExtractAsync(cancellationToken).ConfigureAwait(false);
        _extraction = page;
        await RunAsync(() => _transport.AttachExtractionAsync(page, cancellationToken))
            .ConfigureAwait(false);
        await RunAsync(() => _transport.BuildAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task ResetAsync(CancellationToken cancellationToken) =>
        RunAsync(() => _transport.ResetAsync(cancellationToken));

    /// <inheritdoc/>
    public Task SeekAsync(double timeCode, CancellationToken cancellationToken) =>
        RunAsync(() => _transport.SeekAsync(timeCode, cancellationToken));

    /// <inheritdoc/>
    public Task StepAsync(int steps, CancellationToken cancellationToken) =>
        RunAsync(() => _transport.StepAsync(steps, cancellationToken));

    /// <inheritdoc/>
    public Task SetLoopAsync(bool loop, CancellationToken cancellationToken) =>
        RunAsync(() => _transport.SetLoopAsync(loop, cancellationToken));

    /// <inheritdoc/>
    public Task InvalidateAsync(CancellationToken cancellationToken) =>
        RunAsync(() => _transport.InvalidateAsync(
            UsdPhysicsInvalidationReason.External,
            cancellationToken));

    /// <inheritdoc/>
    public async Task<ViewerPhysicsCommandOutcome> SubmitCommandsAsync(
        IReadOnlyList<ViewerPhysicsRuntimeCommand> commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            return ViewerPhysicsCommandOutcome.None;
        }

        var translated = new List<UsdPhysicsCommand>(commands.Count);
        var refused = 0;
        string refusal = string.Empty;
        for (int index = 0; index < commands.Count; index++)
        {
            if (TryTranslateCommand(commands[index], out UsdPhysicsCommand? command, out string why))
            {
                translated.Add(command!);
                continue;
            }

            refused++;
            if (refusal.Length == 0)
            {
                refusal = why;
            }
        }

        if (translated.Count == 0)
        {
            return new ViewerPhysicsCommandOutcome(
                0,
                refused,
                refusal.Length == 0 ? "No interactive command was usable." : refusal);
        }

        try
        {
            UsdPhysicsCommandSubmission submission = await _transport
                .SubmitCommandsAsync(translated, cancellationToken)
                .ConfigureAwait(false);
            return new ViewerPhysicsCommandOutcome(
                submission.Accepted,
                submission.Rejected + refused,
                refused == 0
                    ? submission.Message
                    : submission.Message + " " + refusal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ViewerPhysicsExtractionDocument> LoadInspectorAsync(
        CancellationToken cancellationToken)
    {
        UsdPhysicsExtractionPage page = await ExtractAsync(cancellationToken)
            .ConfigureAwait(false);
        return ReadInspector(page);
    }

    /// <summary>Extracts the stage, translating every failure into a viewer diagnostic.</summary>
    private async ValueTask<UsdPhysicsExtractionPage> ExtractAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await UsdPhysicsStageExtractor
                .ExtractAsync(_scheduler, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }
    }

    /// <summary>Takes the page the most recent build attached, or extracts a fresh one.</summary>
    /// <remarks>
    /// The bindings must describe the world that was just built, not the stage as it is a moment
    /// later, so the page the build composed from is reused when it is still the newest one. The
    /// page is consumed rather than cached indefinitely because a later read must see later edits.
    /// </remarks>
    private async ValueTask<UsdPhysicsExtractionPage> TakeExtractionAsync(
        CancellationToken cancellationToken)
    {
        UsdPhysicsExtractionPage? cached = _extraction;
        _extraction = null;
        return cached ?? await ExtractAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<ViewerPhysicsBindingSet> LoadBindingsAsync(
        CancellationToken cancellationToken)
    {
        UsdPhysicsExtractionPage page = await TakeExtractionAsync(cancellationToken)
            .ConfigureAwait(false);
        var viewerBindings = new List<ViewerPhysicsBinding>(page.ObjectCount);
        var bakeBindings = new List<UsdPhysicsBakeBinding>(page.ObjectCount);
        var bound = new HashSet<ulong>();
        int skipped = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            UsdPhysicsObjectKind kind = MapExtractionKind(item.Kind);
            if (!CarriesPose(kind))
            {
                skipped++;
                continue;
            }

            string path = item.Path;
            if (path.Length == 0 || path[0] != '/')
            {
                skipped++;
                continue;
            }

            UsdPhysicsObjectId id;
            try
            {
                // A controller is published under its own composed address, not under the plain
                // prim identity, so a binding built from the prim path alone would never receive a
                // pose. A vehicle publishes no pose of its own: its chassis actor carries it, and
                // that actor is addressed by the plain prim path.
                id = UsdPhysicsIdentities.ForSimulatedObject(
                    path,
                    kind == UsdPhysicsObjectKind.Vehicle ? UsdPhysicsObjectKind.RigidBody : kind);
            }
            catch (ArgumentException)
            {
                skipped++;
                continue;
            }

            if (id.IsNone)
            {
                skipped++;
                continue;
            }

            // The same prim can be extracted as several records; records that resolve to the same
            // composed address describe the same simulated object, so the first one wins and the
            // rest would only rebind the same identity to itself.
            if (!bound.Add(id.Value))
            {
                continue;
            }

            bool simulated = item.IsEnabled;
            viewerBindings.Add(new ViewerPhysicsBinding(
                id.Value,
                MapKind(kind),
                path,
                0,
                simulated,
                simulated
                    ? $"{kind} extracted from {path}."
                    : $"{kind} at {path} is disabled and is not simulated."));
            bakeBindings.Add(new UsdPhysicsBakeBinding(id, path));
        }

        ulong revision = page.FingerprintLow;
        _bindings = viewerBindings.Count == 0
            ? UsdPhysicsBakeBindings.Empty
            : new UsdPhysicsBakeBindings(revision, bakeBindings);
        return new ViewerPhysicsBindingSet(
            revision,
            viewerBindings,
            skipped,
            viewerBindings.Count == 0
                ? "The stage carries no simulated object that can drive a rendered prim."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Extracted {viewerBindings.Count} simulated identities from the stage."));
    }

    /// <inheritdoc/>
    public bool TryPublishLatestFrame(PhysicsRenderChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!_transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease))
        {
            return false;
        }

        try
        {
            UsdPhysicsFrame frame = lease.Frame;
            if (frame.Revision == _publishedRevision)
            {
                return false;
            }

            PhysicsRenderSnapshot? snapshot = channel.TryBeginWrite();
            if (snapshot is null)
            {
                return false;
            }

            // The identity revision names the simulated object set and its topology, which changes
            // only when the bindings are rebuilt. Publishing the per frame revision here instead
            // told the interpolator that identities had been rebuilt on every single frame, so it
            // snapped every frame and never blended one.
            snapshot.BeginWrite(
                frame.StepIndex,
                _bindings.IdentityRevision,
                frame.SimulationSeconds,
                frame.TimeCode,
                _transport.FixedStep.Seconds);
            ReadOnlySpan<UsdPhysicsBodyPose> bodies = frame.Bodies;
            for (int index = 0; index < bodies.Length; index++)
            {
                ref readonly UsdPhysicsBodyPose body = ref bodies[index];
                _ = snapshot.TryAddBody(new PhysicsRenderBodyState(
                    new PhysicsRenderObjectId(body.Id.Value, MapKind(body.Id.Kind)),
                    body.Position,
                    new PhysicsRenderOrientation(
                        body.Orientation.X,
                        body.Orientation.Y,
                        body.Orientation.Z,
                        body.Orientation.W),
                    body.IsSleeping,
                    body.IsKinematic));
            }

            // A deformable domain publishes per vertex geometry rather than a
            // pose, so the same frame carries both halves and both are staged
            // into the same snapshot. A region the bounded staging cannot hold
            // is refused whole by the snapshot and reported as a dropped domain
            // entry, so a backend never receives half a body.
            ReadOnlySpan<UsdPhysicsDeformation> deformations = frame.Deformations;
            ReadOnlySpan<UsdVec3d> vertices = frame.DeformationVertices;
            for (int index = 0; index < deformations.Length; index++)
            {
                ref readonly UsdPhysicsDeformation region = ref deformations[index];
                if (region.VertexCount <= 0 || region.VertexOffset < 0 ||
                    region.VertexOffset > vertices.Length - region.VertexCount)
                {
                    continue;
                }

                _ = snapshot.TryAddDeformable(
                    new PhysicsRenderObjectId(region.Id.Value, MapKind(region.Id.Kind)),
                    MapDeformationDomain(region.Kind),
                    Components(vertices.Slice(region.VertexOffset, region.VertexCount)),
                    frame.Revision);
            }

            snapshot.EndWrite();
            _ = channel.Publish(snapshot);
            _publishedRevision = frame.Revision;
            return true;
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>Maps one published deformation window onto the render domain that draws it.</summary>
    private static PhysicsRenderDomain MapDeformationDomain(UsdPhysicsDeformationKind kind) => kind switch
    {
        UsdPhysicsDeformationKind.Surface => PhysicsRenderDomain.Cloth,
        UsdPhysicsDeformationKind.Volume => PhysicsRenderDomain.Deformable,
        _ => PhysicsRenderDomain.Particles
    };

    /// <summary>Narrows one published vertex window into the render component layout.</summary>
    /// <remarks>
    /// The simulation publishes double precision world positions while a render snapshot carries
    /// single precision components, which is the precision every backend vertex buffer has. The
    /// scratch buffer grows to the largest region seen and is then reused, so a steady state pump
    /// does not allocate.
    /// </remarks>
    private ReadOnlySpan<float> Components(ReadOnlySpan<UsdVec3d> vertices)
    {
        int required = checked(vertices.Length * 3);
        if (_vertexScratch.Length < required)
        {
            _vertexScratch = new float[required];
        }

        for (int index = 0; index < vertices.Length; index++)
        {
            UsdVec3d vertex = vertices[index];
            _vertexScratch[index * 3] = (float)vertex.X;
            _vertexScratch[(index * 3) + 1] = (float)vertex.Y;
            _vertexScratch[(index * 3) + 2] = (float)vertex.Z;
        }

        return _vertexScratch.AsSpan(0, required);
    }

    /// <inheritdoc/>
    public async Task<ViewerPhysicsPreviewOutcome> ApplyPreviewAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!UsdPhysicsPreviewApplier.IsSupported)
        {
            throw new ViewerPhysicsException(
                ViewerPhysicsFailureKind.Rejected,
                "The loaded native runtime does not provide batched physics authoring, so the " +
                "physics preview cannot be applied.");
        }

        UsdPhysicsPreviewApplier applier;
        try
        {
            applier = await GetPreviewApplierAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }

        if (!enabled)
        {
            UsdPhysicsPreviewClearResult cleared;
            try
            {
                cleared = await applier.ClearAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Translate(exception);
            }

            if (cleared.Status is not UsdPhysicsBakeStatus.Completed and
                not UsdPhysicsBakeStatus.NotSupported)
            {
                throw new ViewerPhysicsException(
                    ViewerPhysicsFailureKind.Faulted,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The physics preview could not be cleared ({cleared.Status})."));
            }

            return new ViewerPhysicsPreviewOutcome(
                cleared.MigratedUserOpinions
                    ? "Physics preview cleared from the session overlay; opinions authored while " +
                        "it was active were migrated into the user layer."
                    : "Physics preview cleared from the session overlay.",
                TranslateEdits(cleared.Edits),
                0);
        }

        if (_bindings.Count == 0)
        {
            throw new ViewerPhysicsException(
                ViewerPhysicsFailureKind.Rejected,
                "No simulated identity is bound to an authored prim, so a preview would author " +
                "nothing into the session overlay.");
        }

        UsdPhysicsResultBatch? batch = TryCaptureBatch();
        if (batch is null)
        {
            throw new ViewerPhysicsException(
                ViewerPhysicsFailureKind.InvalidState,
                "No complete simulation frame has been published yet, so there is nothing to " +
                "preview.");
        }

        UsdPhysicsPreviewResult result;
        try
        {
            result = await applier
                .ApplyAsync(batch, _bindings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }

        if (result.Status != UsdPhysicsBakeStatus.Completed)
        {
            throw new ViewerPhysicsException(
                result.Status == UsdPhysicsBakeStatus.NotSupported
                    ? ViewerPhysicsFailureKind.Rejected
                    : ViewerPhysicsFailureKind.Faulted,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The physics preview did not complete ({result.Status}): applied " +
                    $"{result.AppliedCount}, skipped {result.SkippedCount}, rejected " +
                    $"{result.RejectedCount}. {DescribeFirstDiagnostic(result.Diagnostics)}"),
                TranslateEdits(result.Edits));
        }

        return new ViewerPhysicsPreviewOutcome(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Physics preview applied {result.AppliedCount} poses " +
                $"(skipped {result.SkippedCount}, rejected {result.RejectedCount})."),
            TranslateEdits(result.Edits),
            result.AppliedCount);
    }

    /// <inheritdoc/>
    public async Task<ViewerPhysicsBakeOutcome> BakeAsync(
        ViewerPhysicsBakeRequest request,
        IProgress<ViewerPhysicsBakeProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!UsdPhysicsBaker.IsSupported)
        {
            return new ViewerPhysicsBakeOutcome(
                false,
                false,
                0,
                "The loaded native runtime does not provide batched physics authoring, so the " +
                "bake was refused before it authored anything.");
        }

        var options = new UsdPhysicsBakeOptions
        {
            ExistingSamplePolicy = request.Policy switch
            {
                ViewerPhysicsBakePolicy.Skip => UsdPhysicsBakeExistingSamplePolicy.Skip,
                ViewerPhysicsBakePolicy.Reject => UsdPhysicsBakeExistingSamplePolicy.Reject,
                _ => UsdPhysicsBakeExistingSamplePolicy.Overwrite,
            }
        };
        var spec = new UsdPhysicsBakeSpec(
            request.DestinationLayerIdentifier,
            request.StartTimeCode,
            request.EndTimeCode,
            request.SampleStride,
            options,
            request.Save);
        using var baker = new UsdPhysicsBaker(_scheduler);
        var source = new ReplaySource(this, cancellationToken);
        IProgress<UsdPhysicsBakeProgress>? bakeProgress = progress is null
            ? null
            : new BakeProgressAdapter(progress);
        UsdPhysicsBakeTransactionResult result;
        try
        {
            result = await baker
                .BakeAsync(spec, source, _bindings, bakeProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }

        bool succeeded = !result.WasRolledBack &&
            result.Status == UsdPhysicsBakeStatus.Completed;
        return new ViewerPhysicsBakeOutcome(
            succeeded,
            result.WasSaved,
            result.SampleCount,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Bake {result.Status} into '{result.Layer.Identifier}': " +
                $"{result.SampleCount} samples, {result.RecordCount} records, " +
                $"rolled back {result.WasRolledBack}, saved {result.WasSaved}."));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _preview?.Dispose();
        _preview = null;
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Translates a physics or USD failure into a viewer-level failure.</summary>
    /// <param name="exception">The failure to translate.</param>
    /// <returns>The translated failure.</returns>
    internal static ViewerPhysicsException Translate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UsdPhysicsTransportQueueFullException full => new ViewerPhysicsException(
                ViewerPhysicsFailureKind.QueueFull,
                "The physics worker queue is full; the request was refused instead of growing " +
                "the queue without bound.",
                full),
            UsdPhysicsTransportStateException state => new ViewerPhysicsException(
                ViewerPhysicsFailureKind.InvalidState,
                $"The physics world is {state.State} and cannot run the request.",
                state),
            ObjectDisposedException disposed => new ViewerPhysicsException(
                ViewerPhysicsFailureKind.InvalidState,
                "The physics world was disposed while the request was pending.",
                disposed),
            _ => new ViewerPhysicsException(
                ViewerPhysicsFailureKind.Faulted,
                exception.Message,
                exception),
        };
    }

    private static async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }
    }

    private static ViewerPhysicsRunState MapState(UsdPhysicsTransportState state) => state switch
    {
        UsdPhysicsTransportState.Building => ViewerPhysicsRunState.Busy,
        UsdPhysicsTransportState.Paused => ViewerPhysicsRunState.Paused,
        UsdPhysicsTransportState.Playing => ViewerPhysicsRunState.Playing,
        UsdPhysicsTransportState.Ended => ViewerPhysicsRunState.Ended,
        UsdPhysicsTransportState.Invalidated => ViewerPhysicsRunState.Invalidated,
        UsdPhysicsTransportState.Faulted => ViewerPhysicsRunState.Faulted,
        UsdPhysicsTransportState.Disposed => ViewerPhysicsRunState.Disabled,
        _ => ViewerPhysicsRunState.Paused,
    };

    private static PhysicsRenderObjectKind MapKind(UsdPhysicsObjectKind kind) => kind switch
    {
        UsdPhysicsObjectKind.Scene => PhysicsRenderObjectKind.Scene,
        UsdPhysicsObjectKind.RigidBody => PhysicsRenderObjectKind.RigidBody,
        UsdPhysicsObjectKind.StaticBody => PhysicsRenderObjectKind.StaticBody,
        UsdPhysicsObjectKind.Collider => PhysicsRenderObjectKind.Collider,
        UsdPhysicsObjectKind.Joint => PhysicsRenderObjectKind.Joint,
        UsdPhysicsObjectKind.Articulation => PhysicsRenderObjectKind.Articulation,
        UsdPhysicsObjectKind.ArticulationLink => PhysicsRenderObjectKind.ArticulationLink,
        UsdPhysicsObjectKind.Controller => PhysicsRenderObjectKind.Controller,
        UsdPhysicsObjectKind.Vehicle => PhysicsRenderObjectKind.Vehicle,
        UsdPhysicsObjectKind.ParticleSystem => PhysicsRenderObjectKind.ParticleSystem,
        UsdPhysicsObjectKind.Deformable => PhysicsRenderObjectKind.Deformable,
        _ => PhysicsRenderObjectKind.Unknown,
    };

    private static UsdPhysicsObjectKind MapExtractionKind(UsdPhysicsExtractionObjectKind kind) =>
        kind switch
        {
            UsdPhysicsExtractionObjectKind.Scene => UsdPhysicsObjectKind.Scene,
            UsdPhysicsExtractionObjectKind.RigidBody => UsdPhysicsObjectKind.RigidBody,
            UsdPhysicsExtractionObjectKind.Collider => UsdPhysicsObjectKind.Collider,
            UsdPhysicsExtractionObjectKind.Joint => UsdPhysicsObjectKind.Joint,
            UsdPhysicsExtractionObjectKind.ArticulationRoot => UsdPhysicsObjectKind.Articulation,
            UsdPhysicsExtractionObjectKind.CharacterController => UsdPhysicsObjectKind.Controller,
            UsdPhysicsExtractionObjectKind.Vehicle => UsdPhysicsObjectKind.Vehicle,
            UsdPhysicsExtractionObjectKind.ParticleSystem => UsdPhysicsObjectKind.ParticleSystem,
            UsdPhysicsExtractionObjectKind.ParticleSet => UsdPhysicsObjectKind.Deformable,
            UsdPhysicsExtractionObjectKind.SurfaceDeformable => UsdPhysicsObjectKind.Deformable,
            UsdPhysicsExtractionObjectKind.VolumeDeformable => UsdPhysicsObjectKind.Deformable,
            _ => UsdPhysicsObjectKind.Unknown,
        };

    /// <summary>Reports whether an object kind can be moved by a simulated pose.</summary>
    /// <remarks>
    /// Only objects a solver actually drives are bound. Binding a material or a scene would inflate
    /// the bounded binding table and could push a real body out of it, which is the one failure the
    /// table cannot report to the user in terms they can act on. A deformable is bound even though
    /// it carries no rigid pose, because its simulated result is per vertex geometry that has to
    /// resolve to the same authored prim through the same table.
    /// </remarks>
    private static bool CarriesPose(UsdPhysicsObjectKind kind) => kind switch
    {
        UsdPhysicsObjectKind.RigidBody => true,
        UsdPhysicsObjectKind.StaticBody => true,
        UsdPhysicsObjectKind.Articulation => true,
        UsdPhysicsObjectKind.ArticulationLink => true,
        UsdPhysicsObjectKind.Controller => true,
        UsdPhysicsObjectKind.Vehicle => true,
        UsdPhysicsObjectKind.Deformable => true,
        _ => false,
    };

    /// <summary>The retained world's address for one extracted object, and what it accepts.</summary>
    /// <param name="Id">The identity the composer gave the object, or zero.</param>
    /// <param name="Path">The authored prim the identity was composed from.</param>
    /// <param name="Commandability">The command families the identity accepts.</param>
    private readonly record struct ViewerPhysicsCommandAddress(
        ulong Id,
        string Path,
        ViewerPhysicsCommandability Commandability);

    /// <summary>
    /// Resolves the identity a runtime command must carry to reach one extracted object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extractor's own identity cannot be used: it hashes the authored path together with the
    /// object type, whereas the composer hashes the composed object's address. The two spaces never
    /// coincide, so a command built from an extraction identity is refused by every world.
    /// </para>
    /// <para>
    /// A collider is not addressable on its own - the solver applies forces to actors, not shapes -
    /// so a collider resolves to the body that owns it, which is exactly the association the
    /// composer makes when it collects an actor's shapes. A collider with no owning body is
    /// composed as a static actor at its own path and stays selectable but not drivable, because a
    /// static actor refuses every force anyway.
    /// </para>
    /// </remarks>
    private static ViewerPhysicsCommandAddress ResolveCommandAddress(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        List<string> articulationRoots)
    {
        UsdPhysicsExtractionObject owner = item;
        UsdPhysicsObjectKind kind = MapExtractionKind(item.Kind);
        if (item.Kind == UsdPhysicsExtractionObjectKind.Collider)
        {
            if (item.ParentBodyIndex >= 0 && item.ParentBodyIndex < page.ObjectCount)
            {
                owner = page.GetObject(item.ParentBodyIndex);
                kind = MapExtractionKind(owner.Kind);
            }
            else
            {
                // The composer turns a collider with no owning body into a static actor at the
                // collider's own path, so that is the identity the world holds for it.
                kind = UsdPhysicsObjectKind.StaticBody;
            }
        }

        string path = owner.Path;
        if (path.Length == 0 || path[0] != '/')
        {
            return new ViewerPhysicsCommandAddress(0UL, string.Empty, ViewerPhysicsCommandability.None);
        }

        UsdPhysicsObjectId id;
        try
        {
            id = UsdPhysicsIdentities.ForSimulatedObject(path, kind);
        }
        catch (ArgumentException)
        {
            return new ViewerPhysicsCommandAddress(0UL, string.Empty, ViewerPhysicsCommandability.None);
        }

        if (id.IsNone)
        {
            return new ViewerPhysicsCommandAddress(0UL, string.Empty, ViewerPhysicsCommandability.None);
        }

        ViewerPhysicsCommandability commandability = Commandability(kind, owner);
        if (commandability == ViewerPhysicsCommandability.Body &&
            !IsUnderArticulationRoot(path, articulationRoots))
        {
            // Only a free rigid actor takes an impulse. A body an articulation claims is simulated
            // as a reduced coordinate link, and PhysX refuses the impulse and velocity change force
            // modes on a link, so the impulse controls stay off for it.
            commandability |= ViewerPhysicsCommandability.Impulse;
        }

        return new ViewerPhysicsCommandAddress(id.Value, path, commandability);
    }

    /// <summary>Lists the prim path of every enabled articulation root in the page.</summary>
    private static List<string> CollectArticulationRootPaths(UsdPhysicsExtractionPage page)
    {
        var roots = new List<string>();
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind == UsdPhysicsExtractionObjectKind.ArticulationRoot &&
                item.IsEnabled &&
                item.Path.Length > 1 &&
                item.Path[0] == '/')
            {
                roots.Add(item.Path);
            }
        }

        return roots;
    }

    /// <summary>Reports whether a body is at or under an articulation root.</summary>
    /// <remarks>
    /// This decides only whether the impulse controls are offered, so it is deliberately
    /// conservative: a body under a root is treated as a link even when the composer ends up
    /// simulating it as a free actor, because offering an impulse that the world would refuse is
    /// worse than withholding one it would have accepted. A link that a root names through a
    /// relationship from outside its own subtree is not recognised here, and is refused by the
    /// world with a diagnostic instead.
    /// </remarks>
    private static bool IsUnderArticulationRoot(string path, List<string> roots)
    {
        for (int index = 0; index < roots.Count; index++)
        {
            string root = roots[index];
            if (string.Equals(path, root, StringComparison.Ordinal))
            {
                return true;
            }

            if (path.Length > root.Length &&
                path.StartsWith(root, StringComparison.Ordinal) &&
                path[root.Length] == '/')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reports which command families one composed object kind actually accepts.</summary>
    /// <remarks>
    /// <para>
    /// A disabled object is composed but not simulated, and every force, impulse, torque, velocity,
    /// and clear the world carries is refused unless the actor is dynamic, so a static or kinematic
    /// body offers no body interactions. Both stay selectable: the inspector still describes them
    /// and still explains why.
    /// </para>
    /// <para>
    /// An articulation root is not a body. The composer gives it an identity of its own that lives
    /// in the world's articulation table, which is neither the actor table nor the link table, so
    /// no command can reach it. When the root schema sits on a prim that is also a body, that prim
    /// still produces its own rigid-body section and that section carries the interactions, so
    /// refusing them here loses nothing and stops the inspector promising a force that the world
    /// would only ever refuse. Links themselves are extracted as rigid bodies and keep their
    /// interactions.
    /// </para>
    /// </remarks>
    private static ViewerPhysicsCommandability Commandability(
        UsdPhysicsObjectKind kind,
        UsdPhysicsExtractionObject owner)
    {
        if (!owner.IsEnabled)
        {
            return ViewerPhysicsCommandability.None;
        }

        switch (kind)
        {
            case UsdPhysicsObjectKind.Scene:
                return ViewerPhysicsCommandability.Scene;
            case UsdPhysicsObjectKind.Controller:
                return ViewerPhysicsCommandability.Controller;
            case UsdPhysicsObjectKind.Vehicle:
                return ViewerPhysicsCommandability.Vehicle;
            case UsdPhysicsObjectKind.RigidBody:
            case UsdPhysicsObjectKind.ArticulationLink:
                const UsdPhysicsExtractionObjectTraits immovable =
                    UsdPhysicsExtractionObjectTraits.Static |
                    UsdPhysicsExtractionObjectTraits.Kinematic;
                return (owner.Flags & immovable) != 0
                    ? ViewerPhysicsCommandability.None
                    : ViewerPhysicsCommandability.Body;
            default:
                return ViewerPhysicsCommandability.None;
        }
    }

    private static string DescribeFirstDiagnostic(UsdPhysicsDiagnostics diagnostics)
    {
        IReadOnlyList<UsdPhysicsDiagnostic> entries = diagnostics.Entries;
        return entries.Count == 0
            ? "The applier reported no diagnostic."
            : $"{entries[0].Code}: {entries[0].Message}";
    }

    private UsdPhysicsResultBatch? TryCaptureBatch()
    {
        if (!_transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease))
        {
            return null;
        }

        try
        {
            return UsdPhysicsResultBatch.FromFrame(lease.Frame, _bindings.IdentityRevision);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async ValueTask<UsdPhysicsPreviewApplier> GetPreviewApplierAsync(
        CancellationToken cancellationToken)
    {
        if (_preview is { } existing)
        {
            return existing;
        }

        // The overlay is bound to the scheduler-owned stage, so it may never be returned as the
        // result of a scheduled call: the stage-bound result guard rejects it and the preview
        // would fail every time. Capturing it from inside the callback keeps the object on the
        // stage thread's side of the boundary while still handing the applier its overlay.
        UsdSessionOverlay? overlay = null;
        await _scheduler
            .InvokeAsync(
                stage =>
                {
                    overlay = stage.NormalizeSessionOverlay();
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (overlay is null)
        {
            throw new ViewerPhysicsException(
                ViewerPhysicsFailureKind.Faulted,
                "The stage did not produce a session overlay, so the physics preview cannot be " +
                "applied.");
        }

        var applier = new UsdPhysicsPreviewApplier(_scheduler, overlay);
        _preview = applier;
        return applier;
    }

    private static bool TryTranslateCommand(
        ViewerPhysicsRuntimeCommand command,
        out UsdPhysicsCommand? translated,
        out string failure)
    {
        translated = null;
        failure = string.Empty;
        if (command is null)
        {
            failure = "An interactive command was null.";
            return false;
        }

        if (command.TargetId == 0UL)
        {
            failure = "An interactive command targets no simulated identity.";
            return false;
        }

        UsdPhysicsCommandKind kind = command.Kind switch
        {
            ViewerPhysicsRuntimeCommandKind.Force => UsdPhysicsCommandKind.Force,
            ViewerPhysicsRuntimeCommandKind.Impulse => UsdPhysicsCommandKind.Impulse,
            ViewerPhysicsRuntimeCommandKind.Torque => UsdPhysicsCommandKind.Torque,
            ViewerPhysicsRuntimeCommandKind.AngularImpulse => UsdPhysicsCommandKind.AngularImpulse,
            ViewerPhysicsRuntimeCommandKind.LinearVelocity => UsdPhysicsCommandKind.LinearVelocity,
            ViewerPhysicsRuntimeCommandKind.AngularVelocity =>
                UsdPhysicsCommandKind.AngularVelocity,
            ViewerPhysicsRuntimeCommandKind.ClearForce => UsdPhysicsCommandKind.ClearForce,
            ViewerPhysicsRuntimeCommandKind.ClearTorque => UsdPhysicsCommandKind.ClearTorque,
            ViewerPhysicsRuntimeCommandKind.Wake => UsdPhysicsCommandKind.Wake,
            ViewerPhysicsRuntimeCommandKind.Sleep => UsdPhysicsCommandKind.Sleep,
            ViewerPhysicsRuntimeCommandKind.SceneGravity => UsdPhysicsCommandKind.SceneGravity,
            ViewerPhysicsRuntimeCommandKind.ControllerMove => UsdPhysicsCommandKind.ControllerMove,
            _ => UsdPhysicsCommandKind.VehicleInput,
        };

        try
        {
            translated = new UsdPhysicsCommand(
                kind,
                new UsdPhysicsObjectId(command.TargetId),
                new UsdVec3d(command.Vector.X, command.Vector.Y, command.Vector.Z),
                command.Magnitude)
            {
                Mode = command.Mode switch
                {
                    ViewerPhysicsForceMode.Acceleration => UsdPhysicsForceMode.Acceleration,
                    ViewerPhysicsForceMode.VelocityChange => UsdPhysicsForceMode.VelocityChange,
                    _ => UsdPhysicsForceMode.Default,
                },
                Application = command.Application switch
                {
                    ViewerPhysicsApplication.World => UsdPhysicsApplicationPoint.World,
                    ViewerPhysicsApplication.Local => UsdPhysicsApplicationPoint.Local,
                    _ => UsdPhysicsApplicationPoint.CenterOfMass,
                },
                Point = new UsdVec3d(command.Point.X, command.Point.Y, command.Point.Z),
                WakeTarget = command.WakeTarget,
            };
            return true;
        }
        catch (ArgumentException exception)
        {
            // A command the public record itself refuses is a viewer bug or a value the user typed
            // that the runtime cannot accept; either way it becomes a diagnostic, not a crash.
            failure = exception.Message;
            return false;
        }
    }

    /// <summary>Projects one extraction page into the inspector's detached document.</summary>
    /// <remarks>
    /// The page is decoded here, in the one type that is allowed to touch physics types, so the
    /// inspector and its tests only ever see plain records. Nothing that references the page
    /// survives the call, which is what keeps the document safe to hand to the UI thread.
    /// </remarks>
    private static ViewerPhysicsExtractionDocument ReadInspector(UsdPhysicsExtractionPage page)
    {
        var diagnostics = new Dictionary<int, List<string>>();
        for (int index = 0; index < page.DiagnosticCount; index++)
        {
            UsdPhysicsExtractionDiagnostic diagnostic = page.GetDiagnostic(index);
            int owner = diagnostic.ObjectIndex;
            if (owner < 0)
            {
                continue;
            }

            if (!diagnostics.TryGetValue(owner, out List<string>? messages))
            {
                messages = [];
                diagnostics[owner] = messages;
            }

            messages.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{diagnostic.Severity} {diagnostic.Category} {diagnostic.Code}: {diagnostic.Message}"));
        }

        var objects = new List<ViewerPhysicsExtractedObject>(page.ObjectCount);
        List<string> articulationRoots = CollectArticulationRootPaths(page);
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            var properties = new List<ViewerPhysicsExtractedProperty>(item.PropertyCount);
            for (int offset = 0; offset < item.PropertyCount; offset++)
            {
                UsdPhysicsExtractionProperty property =
                    page.GetProperty(item.PropertyStart + offset);
                properties.Add(new ViewerPhysicsExtractedProperty(
                    property.Name,
                    FormatExtractedValue(page, property),
                    property.Source.ToString(),
                    property.Source != UsdPhysicsExtractionSource.Fallback));
            }

            ViewerPhysicsCommandAddress target = ResolveCommandAddress(page, item, articulationRoots);
            objects.Add(new ViewerPhysicsExtractedObject(
                item.Id,
                item.Path,
                item.Kind.ToString(),
                item.IsEnabled,
                properties,
                diagnostics.TryGetValue(index, out List<string>? owned) ? owned : [],
                target.Id,
                target.Path,
                target.Commandability));
        }

        return new ViewerPhysicsExtractionDocument(
            page.FingerprintLow,
            objects,
            objects.Count == 0
                ? "No physics object was extracted from this stage."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Extracted {objects.Count} physics object(s) from the stage."));
    }

    private static string FormatExtractedValue(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionProperty property)
    {
        switch (property.ValueKind)
        {
            case UsdPhysicsExtractionValueKind.None:
                return string.Empty;
            case UsdPhysicsExtractionValueKind.Bool:
                return property.Scalar != 0d ? "true" : "false";
            case UsdPhysicsExtractionValueKind.Integral:
                return ((long)property.Scalar).ToString(CultureInfo.InvariantCulture);
            case UsdPhysicsExtractionValueKind.Real:
                return string.Create(CultureInfo.InvariantCulture, $"{property.Scalar:0.######}");
            case UsdPhysicsExtractionValueKind.Text:
                return property.ValueCount > 0 ? page.GetText(property.ValueStart) : string.Empty;
        }

        if (property.IsText)
        {
            var texts = new string[Math.Min(property.ValueCount, 8)];
            for (int index = 0; index < texts.Length; index++)
            {
                texts[index] = page.GetText(property.ValueStart + index);
            }

            return string.Join(", ", texts) + (property.ValueCount > texts.Length ? ", ..." : "");
        }

        int count = Math.Min(property.ValueCount, 16);
        if (count == 0)
        {
            return string.Empty;
        }

        var numbers = new string[count];
        for (int index = 0; index < count; index++)
        {
            numbers[index] = string.Create(
                CultureInfo.InvariantCulture,
                $"{page.GetNumber(property.ValueStart + index):0.######}");
        }

        string joined = "(" + string.Join(", ", numbers);
        return property.ValueCount > count ? joined + ", ...)" : joined + ")";
    }

    private static ViewerPhysicsStageEdit[] TranslateEdits(
        IReadOnlyList<UsdPhysicsPreviewEdit> edits)
    {
        if (edits.Count == 0)
        {
            return [];
        }

        var translated = new ViewerPhysicsStageEdit[edits.Count];
        for (int index = 0; index < edits.Count; index++)
        {
            UsdPhysicsPreviewEdit edit = edits[index];
            translated[index] = new ViewerPhysicsStageEdit(
                edit.BeforeChangeSerial,
                edit.AfterChangeSerial);
        }

        return translated;
    }

    private sealed class BakeProgressAdapter(IProgress<ViewerPhysicsBakeProgress> inner)
        : IProgress<UsdPhysicsBakeProgress>
    {
        public void Report(UsdPhysicsBakeProgress value) =>
            inner.Report(new ViewerPhysicsBakeProgress(
                value.CompletedSamples,
                value.TotalSamples,
                value.TimeCode));
    }

    /// <summary>
    /// Produces one detached batch per baked time code by replaying the retained world.
    /// </summary>
    /// <remarks>
    /// Replaying through the transport's own seek is what keeps a bake reproducible: the sample a
    /// bake authors for a time code is exactly the pose the interactive viewer would show after
    /// seeking to it, rather than whatever the world happened to hold when the dialog opened.
    /// </remarks>
    private sealed class ReplaySource(
        ViewerPhysicsTransportAdapter owner,
        CancellationToken lifetime) : IUsdPhysicsBakeSource
    {
        public async ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
            await owner._transport.SeekAsync(timeCode, linked.Token).ConfigureAwait(false);
            return owner.TryCaptureBatch();
        }
    }
}
