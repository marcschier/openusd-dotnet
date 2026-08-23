// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives simulated deformable geometry through the production render path: the transport publishes
/// a frame, the bridge stages it, a backend applies it, and a retained hdSilk mesh ends up carrying
/// the simulated points.
/// </summary>
/// <remarks>
/// <para>
/// The defect this pins was invisible from either end. The physics frame carried deformation, the
/// render snapshot could store it, and both halves had unit tests - but the viewer sized its render
/// staging with zero deformable capacity and its publish step copied only body poses, so no
/// simulated vertex ever reached a renderer. Testing the snapshot in isolation cannot see that, so
/// every assertion here goes through the same objects the viewer uses at runtime.
/// </para>
/// <para>
/// A CUDA device is not required. Deformation windows are published by the retained world, so this
/// suite drives the production render path with authored windows rather than requiring a device,
/// which is what makes it run everywhere while still exercising the code the viewer runs.
/// </para>
/// </remarks>
public sealed class ViewerPhysicsDeformationRenderTests
{
    private const string ClothPath = "/World/Cloth";

    [Test]
    public async Task TheDefaultRenderCapacitiesStageDeformableGeometry()
    {
        PhysicsRenderCapacities capacities = ViewerPhysicsRenderCapacities.Default;

        // A zero capacity here is what silently dropped every simulated vertex:
        // the snapshot refuses a region it has no room for, and the refusal is
        // indistinguishable from a stage that authored no deformable at all.
        await Assert.That(capacities.DeformableCapacity).IsGreaterThan(0);
        await Assert.That(capacities.DeformableVertexCapacity).IsGreaterThan(0);
    }

    [Test]
    public async Task TheRenderCapacitiesAreSizedFromWhatAWorldCanPublish()
    {
        PhysicsRenderCapacities sized = ViewerPhysicsRenderCapacities.ForWorld(4, 96);

        await Assert.That(sized.DeformableCapacity).IsEqualTo(4);
        await Assert.That(sized.DeformableVertexCapacity).IsEqualTo(96);

        // A world that publishes nothing must not reserve staging for geometry
        // that can never arrive, and one that publishes more than the viewer
        // budgets for is clamped rather than trusted.
        await Assert.That(ViewerPhysicsRenderCapacities.ForWorld(0, 0).DeformableCapacity).IsEqualTo(0);
        await Assert.That(
                ViewerPhysicsRenderCapacities.ForWorld(int.MaxValue, int.MaxValue).DeformableCapacity)
            .IsEqualTo(ViewerPhysicsRenderCapacities.DeformableCapacity);
    }

    [Test]
    public async Task APublishedDeformationReachesTheBackendThroughTheBridge()
    {
        var bridge = new ViewerPhysicsRenderBridge(ViewerPhysicsRenderCapacities.Default);
        var id = new PhysicsRenderObjectId(0x51DE_C107UL, PhysicsRenderObjectKind.Deformable);
        _ = bridge.Bindings.TryBind(id, ClothPath);

        PhysicsRenderSnapshot snapshot = bridge.Channel.TryBeginWrite()
            ?? throw new InvalidOperationException("The channel refused a write.");
        snapshot.BeginWrite(1, 1, 1.0 / 60, 0, 1.0 / 60);
        float[] vertices = [0, 0, 0, 1, 0, 0, 1, 0, 1];
        await Assert.That(snapshot.TryAddDeformable(id, PhysicsRenderDomain.Cloth, vertices, 7))
            .IsTrue();
        snapshot.EndWrite();
        _ = bridge.Channel.Publish(snapshot);

        var target = new RecordingDeformationTarget();
        ViewerPhysicsFramePumpResult result = bridge.Pump(0.016, target);

        await Assert.That(result.Ingested).IsTrue();
        await Assert.That(target.Regions).IsEqualTo(1);
        await Assert.That(target.Revision).IsEqualTo(bridge.AppliedRevision);
        await Assert.That(bridge.AppliedDeformations).IsEqualTo(1);

        // The vertices must arrive whole and unchanged: a region is never
        // interpolated, so what the solver published is what the backend draws.
        await Assert.That(target.LastVertices).IsEquivalentTo(vertices);
    }
    /// <summary>
    /// Requires the viewer's own backend adapter to forward a deformation batch to its session.
    /// </summary>
    /// <remarks>
    /// The bridge is handed the adapter, never the session, so the adapter is the only object on
    /// the production path. It forwarded the four rigid override members and not this one, which
    /// compiled because the interface used to supply a zero returning default, and every backend
    /// therefore reported "no regions applied" while its session sat underneath fully implemented.
    /// The default is gone, so the omission is now a compile error - and this case proves the
    /// forwarding actually reaches the session rather than merely existing.
    /// </remarks>
    [Test]
    public async Task TheBackendAdapterForwardsDeformationsToItsSession()
    {
        var bridge = new ViewerPhysicsRenderBridge(ViewerPhysicsRenderCapacities.Default);
        var id = new PhysicsRenderObjectId(0x51DE_C108UL, PhysicsRenderObjectKind.Deformable);
        _ = bridge.Bindings.TryBind(id, ClothPath);

        PhysicsRenderSnapshot snapshot = bridge.Channel.TryBeginWrite()
            ?? throw new InvalidOperationException("The channel refused a write.");
        snapshot.BeginWrite(1, 1, 1.0 / 60, 0, 1.0 / 60);
        float[] vertices = [0, 0, 0, 0, 1, 0, 0, 1, 1];
        await Assert.That(snapshot.TryAddDeformable(id, PhysicsRenderDomain.Deformable, vertices, 11))
            .IsTrue();
        snapshot.EndWrite();
        _ = bridge.Channel.Publish(snapshot);

        var host = new DeformationForwardingHost();
        await using var backend = new ViewerRenderBackend(RenderBackendKind.Storm, host);
        RenderBackendInitializationResult initialized = await backend.InitializeAsync(
            StageRenderState.Create(new StageIdentity("stage.usda")));
        await Assert.That(initialized.IsSuccess).IsTrue();

        ViewerPhysicsFramePumpResult result = bridge.Pump(0.016, backend);

        await Assert.That(result.Ingested).IsTrue();
        await Assert.That(host.Session!.DeformationCalls).IsEqualTo(1);
        await Assert.That(host.Session.LastVertices).IsEquivalentTo(vertices);
        await Assert.That(bridge.AppliedDeformations).IsEqualTo(1);
    }

    /// <summary>
    /// Requires the bridge to blend between snapshots when the caller supplies an ordinary
    /// monotonic clock.
    /// </summary>
    /// <remarks>
    /// The viewer pumps with a process-wide performance counter, and the interpolator blends on the
    /// simulated timeline, which starts at zero and restarts on every rebuild. Handing one straight
    /// to the other put the frame time hours past the newest snapshot, so every alpha clamped to
    /// one, every frame snapped, and the whole blending path was dead code that still reported
    /// success. This drives the bridge exactly as the viewer does, with a large clock origin and
    /// two snapshots that share an identity revision, and requires a blended frame.
    /// </remarks>
    [Test]
    public async Task ARenderClockWithALargeOriginStillBlendsBetweenSnapshots()
    {
        var bridge = new ViewerPhysicsRenderBridge(ViewerPhysicsRenderCapacities.Default);
        var id = new PhysicsRenderObjectId(0x0BEEFUL, PhysicsRenderObjectKind.RigidBody);
        _ = bridge.Bindings.TryBind(id, "/World/Body");
        var target = new RecordingDeformationTarget();

        // The viewer's clock is a performance counter, so its origin is the machine uptime.
        const double clockOrigin = 987654.0;
        const double fixedStep = 1.0 / 60.0;

        PublishBody(bridge, id, stepIndex: 1, simulationSeconds: 0.0, x: 0.0);
        _ = bridge.Pump(clockOrigin, target);

        PublishBody(bridge, id, stepIndex: 2, simulationSeconds: fixedStep, x: 1.0);
        _ = bridge.Pump(clockOrigin + 0.001, target);

        // Half a step later, with nothing new published, the frame must sit between the two poses.
        _ = bridge.Pump(clockOrigin + 0.001 + (fixedStep / 2.0), target);

        PhysicsRenderOverrideView view = bridge.Overrides;
        await Assert.That(view.Count).IsEqualTo(1);
        double x = view.Items[0].Position.X;
        await Assert.That(x).IsGreaterThan(0.1);
        await Assert.That(x).IsLessThan(0.9);
    }

    private static void PublishBody(
        ViewerPhysicsRenderBridge bridge,
        PhysicsRenderObjectId id,
        ulong stepIndex,
        double simulationSeconds,
        double x)
    {
        PhysicsRenderSnapshot snapshot = bridge.Channel.TryBeginWrite()
            ?? throw new InvalidOperationException("The channel refused a write.");

        // One identity revision for both snapshots: the object set never changed, only its pose.
        snapshot.BeginWrite(stepIndex, 7, simulationSeconds, simulationSeconds, 1.0 / 60.0);
        _ = snapshot.TryAddBody(new PhysicsRenderBodyState(
            id,
            new UsdVec3d(x, 0, 0),
            PhysicsRenderOrientation.Identity,
            IsSleeping: false,
            IsKinematic: false));
        snapshot.EndWrite();
        _ = bridge.Channel.Publish(snapshot);
    }

    /// <summary>A backend that records the deformation batches the bridge hands it.</summary>
    private sealed class RecordingDeformationTarget : IViewerPhysicsOverrideTarget
    {
        public bool SupportsPhysicsTransformOverrides => true;

        public int Regions { get; private set; }

        public ulong Revision { get; private set; }

        public float[] LastVertices { get; private set; } = [];

        public int ApplyPhysicsOverrides(
            in PhysicsRenderOverrideView overrides,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            return overrides.Count;
        }

        public int ApplyPhysicsDeformations(
            in PhysicsRenderDeformationView deformations,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            Regions = deformations.Count;
            Revision = deformations.Revision;
            LastVertices = deformations.Count == 0
                ? []
                : deformations.GetVertices(deformations.Regions[0]).ToArray();
            return Regions;
        }

        public bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report)
        {
            report = default;
            return false;
        }

        public void ClearPhysicsOverrides()
        {
        }
    }

    /// <summary>A host whose session records the deformation batches the adapter forwards.</summary>
    private sealed class DeformationForwardingHost : IViewerRenderBackendHost
    {
        internal ForwardingSession? Session { get; private set; }

        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            RenderBackendKind kind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RenderBackendProbeResult.Available());

        public ValueTask<IViewerRenderBackendSession> AttachAsync(
            RenderBackendKind kind,
            StageRenderState initialState,
            CancellationToken cancellationToken)
        {
            Session = new ForwardingSession(initialState);
            return ValueTask.FromResult<IViewerRenderBackendSession>(Session);
        }
    }

    /// <summary>The session the adapter has to reach for a deformable frame to be drawn.</summary>
    private sealed class ForwardingSession(StageRenderState initialState)
        : IViewerRenderBackendSession, IViewerPhysicsOverrideTarget
    {
        private StageRenderState _state = initialState;

        internal int DeformationCalls { get; private set; }

        internal float[] LastVertices { get; private set; } = [];

        public RenderBackendDiagnostics Diagnostics => RenderBackendDiagnostics.Empty;

        public StageRenderState CurrentState => _state;

        public bool SupportsPhysicsTransformOverrides => true;

        public ValueTask ActivateAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DeactivateAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask UpdateStateAsync(
            StageRenderState state,
            CancellationToken cancellationToken)
        {
            _state = state;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(
            ViewportDimensions viewport,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RenderFrameResult.Rendered(_state.Revision, RenderFrameStatistics.Empty));

        public int ApplyPhysicsOverrides(
            in PhysicsRenderOverrideView overrides,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            return overrides.Count;
        }

        public int ApplyPhysicsDeformations(
            in PhysicsRenderDeformationView deformations,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            DeformationCalls++;
            LastVertices = deformations.Count == 0
                ? []
                : deformations.GetVertices(deformations.Regions[0]).ToArray();
            return deformations.Count;
        }

        public bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report)
        {
            report = default;
            return false;
        }

        public void ClearPhysicsOverrides()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
