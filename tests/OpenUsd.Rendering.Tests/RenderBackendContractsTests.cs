// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderBackendContractsTests
{
    [Test]
    public async Task DiagnosticsDefensivelyCopyAndUseValueEquality()
    {
        var diagnostic = new RenderBackendDiagnostic(
            RenderBackendKind.Storm,
            RenderDiagnosticSeverity.Warning,
            RenderBackendDiagnosticCategory.Initialization,
            "storm.device",
            "Device unavailable");
        var entries = new List<RenderBackendDiagnostic> { diagnostic };

        var diagnostics = new RenderBackendDiagnostics(entries);
        entries.Clear();

        await Assert.That(diagnostics.Entries).Count().IsEqualTo(1);
        await Assert.That(diagnostics)
            .IsEqualTo(new RenderBackendDiagnostics([diagnostic]));
    }

    [Test]
    public async Task ProbeAndInitializationResultsRetainTypedFailures()
    {
        RenderBackendProbeResult probe = RenderBackendProbeResult.Unavailable(
            RenderBackendProbeFailureKind.IncompatibleDriver);
        RenderBackendInitializationResult initialization =
            RenderBackendInitializationResult.Failed(
                RenderBackendInitializationFailureKind.DeviceCreationFailed);

        await Assert.That(probe.IsAvailable).IsFalse();
        await Assert.That(probe.Failure)
            .IsEqualTo(RenderBackendProbeFailureKind.IncompatibleDriver);
        await Assert.That(initialization.IsSuccess).IsFalse();
        await Assert.That(initialization.Failure)
            .IsEqualTo(RenderBackendInitializationFailureKind.DeviceCreationFailed);
    }

    [Test]
    public async Task BackendSwitchPreservesExactStageStateSnapshot()
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("switch.usda"))
            .WithCamera(new CameraState(
                Matrix4x4.CreateTranslation(4, 5, 6),
                Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, 0.1f, 100)))
            .WithTime(new StageTime(24))
            .WithSelection(new SelectionState(["/World/Hero"]))
            .WithViewport(new ViewportDimensions(1600, 900));
        await using var storm = new TestBackend(RenderBackendKind.Storm);
        await using var d3d12 = new TestBackend(RenderBackendKind.D3D12);

        _ = await storm.InitializeAsync(state);
        _ = await d3d12.InitializeAsync(storm.Snapshot!);

        await Assert.That(storm.Snapshot).IsSameReferenceAs(state);
        await Assert.That(d3d12.Snapshot).IsSameReferenceAs(state);
        await Assert.That(d3d12.Snapshot!.Revision).IsEqualTo(state.Revision);
        await Assert.That(d3d12.Snapshot.Selection).IsEqualTo(state.Selection);
    }

    [Test]
    public async Task FrameResultReportsConsumedRevisionStatisticsAndDeviceLoss()
    {
        var statistics = new RenderFrameStatistics(
            TimeSpan.FromMilliseconds(4),
            TimeSpan.FromMilliseconds(2),
            drawCalls: 12,
            triangles: 2048);

        RenderFrameResult rendered = RenderFrameResult.Rendered(42, statistics);
        RenderFrameResult lost = RenderFrameResult.LostDevice(
            42,
            RenderDeviceLossKind.Removed);

        await Assert.That(rendered.Status).IsEqualTo(RenderFrameStatus.Rendered);
        await Assert.That(rendered.StateRevision).IsEqualTo(42ul);
        await Assert.That(rendered.Statistics).IsEqualTo(statistics);
        await Assert.That(lost.Status).IsEqualTo(RenderFrameStatus.DeviceLost);
        await Assert.That(lost.DeviceLoss).IsEqualTo(RenderDeviceLossKind.Removed);
    }

    private sealed class TestBackend(RenderBackendKind kind) : IRenderBackend
    {
        public RenderBackendIdentity Identity { get; } = new(kind, kind.ToString());

        public RenderBackendCapabilities Capabilities { get; } = new(
            RenderBackendCapability.Presentation |
            RenderBackendCapability.Offscreen |
            RenderBackendCapability.DeviceLossDetection,
            maxSamplesPerPixel: 8,
            isSoftware: false);

        internal StageRenderState? Snapshot { get; private set; }

        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RenderBackendProbeResult.Available());

        public ValueTask<RenderBackendInitializationResult> InitializeAsync(
            StageRenderState initialState,
            CancellationToken cancellationToken = default)
        {
            Snapshot = initialState;
            return ValueTask.FromResult(RenderBackendInitializationResult.Success());
        }

        public ValueTask UpdateStateAsync(
            StageRenderState state,
            CancellationToken cancellationToken = default)
        {
            Snapshot = state;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(
            ViewportDimensions viewport,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<RenderFrameResult> RenderAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RenderFrameResult.Rendered(
                Snapshot?.Revision ?? 0,
                RenderFrameStatistics.Empty));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
