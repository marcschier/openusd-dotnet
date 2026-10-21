// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;

namespace OpenUsd.Rendering.Physics;

/// <summary>Pins the host's authored source and scenario identities for one simulation recording.</summary>
/// <remarks>The host retains exclusive stage, transport, binding-table and renderer ownership during recording.</remarks>
public sealed class PhysicsRenderRecordingContext
{
    /// <summary>Creates bounded host-provided recording identities.</summary>
    public PhysicsRenderRecordingContext(string scenarioIdentity, ulong sourceRevision, string? reviewIdentity = null)
    {
        ValidateIdentity(scenarioIdentity, nameof(scenarioIdentity));
        if (reviewIdentity is not null)
        {
            ValidateIdentity(reviewIdentity, nameof(reviewIdentity));
        }
        ScenarioIdentity = scenarioIdentity;
        SourceRevision = sourceRevision;
        ReviewIdentity = reviewIdentity;
    }

    /// <summary>Gets the scenario identity that describes the simulated setup.</summary>
    public string ScenarioIdentity { get; }
    /// <summary>Gets the pinned authored source revision.</summary>
    public ulong SourceRevision { get; }
    /// <summary>Gets an optional host-provided review identity, not an independent origin guarantee.</summary>
    public string? ReviewIdentity { get; }

    private static void ValidateIdentity(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 256 || value.Any(char.IsControl))
        {
            throw new ArgumentException("A recording identity must be bounded text without control characters.", parameter);
        }
    }
}

/// <summary>One exact projected simulation publication, borrowed only for the duration of Capture.</summary>
public sealed class PhysicsRenderRecordingFrame
{
    internal PhysicsRenderRecordingFrame(
        RenderSimulationFrameIdentity identity, PhysicsRenderBindingTable bindings,
        PhysicsRenderOverrideView transforms, PhysicsRenderDeformationView deformations)
    {
        Identity = identity;
        Bindings = bindings;
        Transforms = transforms;
        Deformations = deformations;
    }

    /// <summary>Gets immutable provenance for this publication.</summary>
    public RenderSimulationFrameIdentity Identity { get; }
    /// <summary>Gets the host-owned binding table, immutable for this recording.</summary>
    public PhysicsRenderBindingTable Bindings { get; }
    /// <summary>Gets exact, noninterpolated transform overrides borrowed for this capture.</summary>
    public PhysicsRenderOverrideView Transforms { get; }
    /// <summary>Gets complete deformation regions borrowed for this capture.</summary>
    public PhysicsRenderDeformationView Deformations { get; }
}

/// <summary>Reports the publication and override counts actually consumed by a completed capture.</summary>
public sealed class PhysicsRenderCaptureResult
{
    /// <summary>Creates an acknowledgement without copying renderer-owned image planes.</summary>
    public PhysicsRenderCaptureResult(
        RenderJobImage image, ulong publicationRevision, int appliedBodies, int appliedDeformations)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedBodies);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedDeformations);
        Image = image;
        PublicationRevision = publicationRevision;
        AppliedBodies = appliedBodies;
        AppliedDeformations = appliedDeformations;
    }

    /// <summary>Gets completed image planes, valid until the next Capture or ClearOverrides call.</summary>
    public RenderJobImage Image { get; }
    /// <summary>Gets the physics publication actually rendered, not merely queued.</summary>
    public ulong PublicationRevision { get; }
    /// <summary>Gets the number of body overrides actually resolved and rendered.</summary>
    public int AppliedBodies { get; }
    /// <summary>Gets the number of deformation regions actually resolved and rendered.</summary>
    public int AppliedDeformations { get; }
}

/// <summary>Owns renderer-thread capture while the shared module owns stepping, projection and publication.</summary>
public interface IPhysicsRenderRecordingSource
{
    /// <summary>Requires unchanged source/review ownership and support for the complete request, or throws.</summary>
    /// <remarks>Called before stepping, around capture and before publication. Keep checks lightweight and nonmutating.</remarks>
    void Validate(RenderDiskJobRequest request, PhysicsRenderRecordingContext context, CancellationToken cancellationToken);

    /// <summary>Applies all overrides, drains rendering and captures matching planes, or throws.</summary>
    /// <remarks>
    /// No borrowed frame data may be retained after returning. A queued override is not an acknowledgement:
    /// counts and publication revision must describe what the captured renderer actually consumed.
    /// </remarks>
    PhysicsRenderCaptureResult Capture(
        StageRenderState state, PhysicsRenderRecordingFrame frame, CancellationToken cancellationToken);

    /// <summary>Drains in-flight work and restores authored render state, without advancing or resetting physics.</summary>
    /// <remarks>Cleanup is not cancelled. A failure prevents publication and remains visible to the caller.</remarks>
    void ClearOverrides();
}

/// <summary>Records fixed-step simulation through the shared atomic disk-job engine.</summary>
public static class PhysicsRenderRecording
{
    /// <summary>Steps, projects, captures and atomically publishes the exact requested simulation frames.</summary>
    /// <remarks>
    /// Executes on the caller-selected renderer thread, never the physics worker. The caller must exclusively
    /// own the paused, nonlooping transport, stage, bindings and source for this operation.
    /// Requested times must match successive exact fixed-step results. No source asset is authored.
    /// </remarks>
    public static RenderDiskJobResult Execute(
        RenderDiskJobRequest request, UsdPhysicsTransport transport, PhysicsRenderBindingTable bindings,
        PhysicsRenderCapacities capacities, IPhysicsRenderRecordingSource source,
        PhysicsRenderRecordingContext context, int stepsPerFrame = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepsPerFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stepsPerFrame, transport.Options.MaxCatchUpSubSteps);
        cancellationToken.ThrowIfCancellationRequested();
        UsdPhysicsTransportStatus status = transport.Status;
        if (status.State != UsdPhysicsTransportState.Paused || transport.Loop || status.BacklogSeconds != 0 ||
            status.QueueDepth != 0)
        {
            throw new InvalidOperationException("Recording requires an exclusively owned paused, nonlooping transport.");
        }
        if (bindings.RefusedBindings != 0)
        {
            throw new InvalidDataException("Recording cannot use a truncated binding table.");
        }
        StageIdentity stage = request.Frames[0].Stage;
        for (int index = 0; index < request.Frames.Count; index++)
        {
            StageRenderState state = request.Frames[index];
            ulong step = checked(status.StepIndex + (ulong)(index + 1) * (ulong)stepsPerFrame);
            double seconds = step * transport.FixedStep.Seconds;
            if (state.Stage != stage || state.Time.TimeCode != transport.Timeline.ToTimeCode(seconds))
            {
                throw new ArgumentException("All recording states must identify the same stage and exact fixed-step times.",
                    nameof(request));
            }
            if (transport.Timeline.HasAuthoredRange &&
                seconds > transport.Timeline.DurationSeconds + transport.FixedStep.Seconds * 1e-6)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Recording would advance beyond the authored end.");
            }
        }
        if (!transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease initial))
        {
            throw new InvalidOperationException("Recording requires an initial complete publication.");
        }
        RecordingAdapter adapter;
        using (initial)
        {
            RequireComplete(initial.Frame);
            if (initial.Frame.Revision != status.Revision || initial.Frame.StepIndex != status.StepIndex ||
                !capacities.Contains(new PhysicsRenderCapacities(initial.Frame.BodyCapacity,
                    initial.Frame.DeformationCapacity, initial.Frame.DeformationVertexCapacity)))
            {
                throw new InvalidOperationException("Recording projection cannot reserve the complete publication capacity.");
            }
            source.Validate(request, context, cancellationToken);
            adapter = new RecordingAdapter(request, transport, bindings, capacities, source, context,
                stepsPerFrame, status, cancellationToken);
            adapter.ValidateInitial(initial.Frame, cancellationToken);
        }
        try
        {
            return RenderDiskJob.Execute(request, adapter, TimeProvider.System, adapter.BeforePublish, cancellationToken);
        }
        catch (Exception operationFailure)
        {
            try
            {
                adapter.ClearOverrides();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Simulation recording failed and render overrides could not be cleared.",
                    operationFailure, cleanupFailure);
            }
            throw;
        }
    }

    private static void RequireComplete(UsdPhysicsFrame frame)
    {
        if (frame.HasOverflow || frame.BodiesTruncated || frame.DeformationsTruncated || frame.DroppedEventCount != 0)
        {
            throw new InvalidDataException("Recording refuses an incomplete physics publication.");
        }
    }

    private sealed class RecordingAdapter : IRenderProductFrameSource
    {
        private readonly RenderDiskJobRequest _request;
        private readonly UsdPhysicsTransport _transport;
        private readonly PhysicsRenderBindingTable _bindings;
        private readonly IPhysicsRenderRecordingSource _source;
        private readonly PhysicsRenderRecordingContext _context;
        private readonly PhysicsRenderTransformOverride[] _transforms;
        private readonly PhysicsRenderDeformableRegion[] _deformations;
        private readonly float[] _vertices;
        private readonly HashSet<PhysicsRenderBinding> _targets;
        private readonly int _steps;
        private readonly ulong _bindingRevision;
        private readonly Guid _epoch = Guid.NewGuid();
        private readonly CancellationToken _cancellationToken;
        private ulong _revision;
        private ulong _step;
        private int _index;
        private bool _captureAttempted;
        private bool _clearAttempted;

        internal RecordingAdapter(
            RenderDiskJobRequest request, UsdPhysicsTransport transport, PhysicsRenderBindingTable bindings,
            PhysicsRenderCapacities capacities, IPhysicsRenderRecordingSource source,
            PhysicsRenderRecordingContext context, int steps, UsdPhysicsTransportStatus status,
            CancellationToken cancellationToken)
        {
            _request = request;
            _transport = transport;
            _bindings = bindings;
            _source = source;
            _context = context;
            _steps = steps;
            _revision = status.Revision;
            _step = status.StepIndex;
            _bindingRevision = bindings.Revision;
            _transforms = new PhysicsRenderTransformOverride[capacities.BodyCapacity];
            _deformations = new PhysicsRenderDeformableRegion[capacities.DeformableCapacity];
            _vertices = new float[checked(capacities.DeformableVertexCapacity * 3)];
            _targets = new HashSet<PhysicsRenderBinding>(Math.Max(capacities.BodyCapacity, capacities.DeformableCapacity));
            _cancellationToken = cancellationToken;
        }

        public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(plan, _request.ProductPlan))
            {
                throw new InvalidOperationException("The recording product changed.");
            }
            _source.Validate(_request, _context, cancellationToken);
        }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            ValidateContinuity(cancellationToken);
            if (_index >= _request.Frames.Count || !ReferenceEquals(state, _request.Frames[_index]))
            {
                throw new InvalidOperationException("The recording frame order changed.");
            }
            using UsdPhysicsFrameLease lease = _transport.StepAndAcquireFrameAsync(_steps, cancellationToken)
                .GetAwaiter().GetResult();
            UsdPhysicsFrame frame = lease.Frame;
            RequireComplete(frame);
            if (frame.Revision != checked(_revision + 1) || frame.StepIndex != checked(_step + (ulong)_steps) ||
                frame.TimeCode != state.Time.TimeCode || frame.SubStepCount != _steps ||
                frame.SimulationSeconds != frame.StepIndex * _transport.FixedStep.Seconds)
            {
                throw new InvalidOperationException("The captured physics publication is discontinuous.");
            }
            int nextVertex = Project(frame, cancellationToken);
            _revision = frame.Revision;
            _step = frame.StepIndex;
            var identity = new RenderSimulationFrameIdentity(
                _epoch, _context.ScenarioIdentity, _context.SourceRevision, frame.Revision, frame.StepIndex,
                frame.SimulationSeconds, frame.TimeCode, _transport.FixedStep.Seconds, _bindingRevision,
                frame.BodyCount, frame.DeformationCount, _context.ReviewIdentity);
            var projection = new PhysicsRenderRecordingFrame(identity, _bindings,
                new PhysicsRenderOverrideView(_transforms.AsMemory(0, frame.BodyCount), frame.Revision),
                new PhysicsRenderDeformationView(_deformations.AsMemory(0, frame.DeformationCount),
                    _vertices.AsMemory(0, checked(nextVertex * 3)), frame.Revision));
            ValidateContinuity(cancellationToken);
            _captureAttempted = true;
            PhysicsRenderCaptureResult capture = _source.Capture(state, projection, cancellationToken) ??
                throw new InvalidOperationException("The recording source returned no capture.");
            ValidateContinuity(cancellationToken);
            if (capture.PublicationRevision != frame.Revision || capture.AppliedBodies != frame.BodyCount ||
                capture.AppliedDeformations != frame.DeformationCount)
            {
                throw new InvalidDataException("The renderer did not acknowledge the complete exact physics publication.");
            }
            if (capture.Image.Simulation is { } supplied && supplied != identity)
            {
                throw new InvalidDataException("The renderer returned a different simulation identity.");
            }
            _index++;
            RenderJobImage image = capture.Image;
            return new RenderJobImage(image.Width, image.Height, image.Rgba, image.RowOrder)
            {
                DeviceDepth = image.DeviceDepth,
                HdrColor = image.HdrColor,
                Diagnostics = image.Diagnostics,
                Simulation = identity
            };
        }

        internal void ValidateInitial(UsdPhysicsFrame frame, CancellationToken cancellationToken)
        {
            _ = Project(frame, cancellationToken);
            ValidateContinuity(cancellationToken);
        }

        private int Project(UsdPhysicsFrame frame, CancellationToken cancellationToken)
        {
            RequireComplete(frame);
            if (frame.BodyCount > _transforms.Length || frame.DeformationCount > _deformations.Length ||
                frame.DeformationVertexCount > _vertices.Length / 3)
            {
                throw new InvalidDataException("The complete physics frame does not fit recording projection.");
            }
            _targets.Clear();
            for (int index = 0; index < frame.BodyCount; index++)
            {
                ref readonly UsdPhysicsBodyPose body = ref frame.Bodies[index];
                PhysicsRenderObjectId id = ToRenderId(body.Id);
                if (!_bindings.TryResolve(id, out PhysicsRenderBinding binding) || !_targets.Add(binding))
                {
                    throw new InvalidDataException("A recording body has an unresolved or conflicting render binding.");
                }
                UsdVec3d position = body.Position;
                UsdPhysicsOrientation orientation = body.Orientation;
                double length = orientation.X * orientation.X + orientation.Y * orientation.Y +
                    orientation.Z * orientation.Z + orientation.W * orientation.W;
                if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z) ||
                    !double.IsFinite(length) || length <= 0)
                {
                    throw new InvalidDataException("A recording body pose is not finite and orientable.");
                }
                var rotation = new PhysicsRenderOrientation(orientation.X, orientation.Y, orientation.Z, orientation.W);
                _transforms[index] = new PhysicsRenderTransformOverride(id, position,
                    rotation.Normalized().Canonical(), Snapped: true);
            }
            _targets.Clear();
            int nextVertex = 0;
            for (int index = 0; index < frame.DeformationCount; index++)
            {
                ref readonly UsdPhysicsDeformation region = ref frame.Deformations[index];
                PhysicsRenderObjectId id = ToRenderId(region.Id);
                if (region.VertexOffset != nextVertex || region.VertexCount <= 0 ||
                    region.VertexCount > frame.DeformationVertexCount - nextVertex ||
                    !_bindings.TryResolve(id, out PhysicsRenderBinding binding) || !_targets.Add(binding))
                {
                    throw new InvalidDataException("A recording deformation has an invalid window or render binding.");
                }
                PhysicsRenderDomain domain = region.Kind switch
                {
                    UsdPhysicsDeformationKind.Particles or UsdPhysicsDeformationKind.Fluid => PhysicsRenderDomain.Particles,
                    UsdPhysicsDeformationKind.Surface => PhysicsRenderDomain.Cloth,
                    UsdPhysicsDeformationKind.Volume => PhysicsRenderDomain.Deformable,
                    _ => throw new InvalidDataException("A recording deformation has an unknown domain.")
                };
                _deformations[index] = new PhysicsRenderDeformableRegion(
                    id, domain, nextVertex, region.VertexCount, frame.Revision);
                nextVertex += region.VertexCount;
            }
            if (nextVertex != frame.DeformationVertexCount)
            {
                throw new InvalidDataException("The recording deformation frame contains unaddressed vertices.");
            }
            for (int index = 0; index < nextVertex; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                UsdVec3d vertex = frame.DeformationVertices[index];
                float x = (float)vertex.X;
                float y = (float)vertex.Y;
                float z = (float)vertex.Z;
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                {
                    throw new InvalidDataException("A recording deformation cannot be represented as finite render vertices.");
                }
                _vertices[index * 3] = x;
                _vertices[index * 3 + 1] = y;
                _vertices[index * 3 + 2] = z;
            }
            return nextVertex;
        }

        internal void BeforePublish()
        {
            ValidateContinuity(_cancellationToken);
            ClearOverrides();
            ValidateContinuity(_cancellationToken);
        }

        internal void ClearOverrides()
        {
            if (_captureAttempted && !_clearAttempted)
            {
                _clearAttempted = true;
                _source.ClearOverrides();
            }
        }

        private void ValidateContinuity(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UsdPhysicsTransportStatus status = _transport.Status;
            if (status.State is not (UsdPhysicsTransportState.Paused or UsdPhysicsTransportState.Ended) ||
                _transport.Loop || status.Revision != _revision || status.StepIndex != _step ||
                status.QueueDepth != 0 || _bindings.Revision != _bindingRevision || _bindings.RefusedBindings != 0)
            {
                throw new InvalidOperationException("The recording transport or bindings changed outside the job.");
            }
            _source.Validate(_request, _context, cancellationToken);
        }

        private static PhysicsRenderObjectId ToRenderId(UsdPhysicsObjectId id)
        {
            var kind = (PhysicsRenderObjectKind)(int)id.Kind;
            if (id.Value == 0 || !Enum.IsDefined(kind))
            {
                throw new InvalidDataException("The recording frame contains an invalid object identity.");
            }
            return new PhysicsRenderObjectId(id.Value, kind);
        }
    }
}
