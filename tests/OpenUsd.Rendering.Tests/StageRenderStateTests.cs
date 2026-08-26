// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Tests;

public sealed class StageRenderStateTests
{
    [Test]
    public async Task DefaultStateContainsRendererNeutralDefaults()
    {
        StageRenderState state = StageRenderState.Default;

        await Assert.That(state.Stage).IsEqualTo(StageIdentity.Empty);
        await Assert.That(state.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(state.Camera).IsEqualTo(default(CameraState));
        await Assert.That(state.Camera.Mode).IsEqualTo(CameraMode.Automatic);
        await Assert.That(state.Camera.View).IsEqualTo(default(Matrix4x4));
        await Assert.That(state.Camera.Projection).IsEqualTo(default(Matrix4x4));
        await Assert.That(state.Time).IsEqualTo(StageTime.Default);
        await Assert.That(state.Selection.PrimPaths).IsEmpty();
        await Assert.That(state.Display).IsEqualTo(SceneDisplayState.Default);
        await Assert.That(state.Viewport).IsEqualTo(ViewportDimensions.Empty);
        await Assert.That(state.RenderSettings).IsEqualTo(RenderSettings.Default);
        await Assert.That(state.Diagnostics.Entries).IsEmpty();
        await Assert.That(state.Revision).IsEqualTo(0ul);
    }

    [Test]
    public async Task PresentationDefaultsRetainHighlightsWithoutChangingConformanceDefaults()
    {
        await Assert.That(RenderSettings.Default.OutputTransform)
            .IsEqualTo(RenderOutputTransform.Identity);
        await Assert.That(RenderSettings.Default.Exposure).IsEqualTo(0f);
        await Assert.That(RenderSettings.PresentationDefault.OutputTransform)
            .IsEqualTo(RenderOutputTransform.Reinhard);
        await Assert.That(RenderSettings.PresentationDefault.Exposure).IsEqualTo(-6f);
    }

    [Test]
    public async Task OutputControlsRejectUnknownTransformsAndNonFiniteExposure()
    {
        await Assert.That(() => new RenderSettings(
                1,
                true,
                true,
                Vector4.Zero,
                true,
                true,
                RenderComplexity.Low,
                (RenderOutputTransform)2,
                0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderSettings(
                1,
                true,
                true,
                Vector4.Zero,
                true,
                true,
                RenderComplexity.Low,
                RenderOutputTransform.Reinhard,
                float.NaN))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ExplicitIdentityMatricesRemainDistinctFromAutomaticCamera()
    {
        var explicitIdentity = new CameraState(
            Matrix4x4.Identity,
            Matrix4x4.Identity);
        (Matrix4x4 view, Matrix4x4 projection) = explicitIdentity;

        await Assert.That(CameraState.Default.Mode).IsEqualTo(CameraMode.Automatic);
        await Assert.That(default(CameraState).Mode).IsEqualTo(CameraMode.Automatic);
        await Assert.That(CameraState.Default).IsEqualTo(default(CameraState));
        await Assert.That(explicitIdentity.Mode).IsEqualTo(CameraMode.Matrices);
        await Assert.That(explicitIdentity).IsNotEqualTo(CameraState.Default);
        await Assert.That(view).IsEqualTo(Matrix4x4.Identity);
        await Assert.That(projection).IsEqualTo(Matrix4x4.Identity);
    }

    [Test]
    public async Task CollectionStatesDefensivelyCopyAndUseValueEquality()
    {
        var paths = new List<string> { "/World/Cube" };
        var diagnostic = new RenderDiagnostic(
            RenderDiagnosticSeverity.Warning,
            "stage.warning",
            "Warning");
        var entries = new List<RenderDiagnostic> { diagnostic };

        var selection = new SelectionState(paths);
        var diagnostics = new RenderDiagnosticsState(entries);
        paths.Add("/World/Sphere");
        entries.Clear();

        await Assert.That(selection.PrimPaths).Count().IsEqualTo(1);
        await Assert.That(diagnostics.Entries).Count().IsEqualTo(1);
        await Assert.That(selection).IsEqualTo(new SelectionState(["/World/Cube"]));
        await Assert.That(diagnostics).IsEqualTo(new RenderDiagnosticsState([diagnostic]));
    }

    [Test]
    public async Task UpdatesCreateMonotonicallyRevisedImmutableStates()
    {
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));

        StageRenderState camera = initial.WithCamera(new CameraState(
            Matrix4x4.CreateLookAt(Vector3.One, Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 0.1f, 100)));
        StageRenderState time = camera.WithTime(new StageTime(24));
        StageRenderState selection = time.WithSelection(new SelectionState(["/World/Cube"]));
        StageRenderState display = selection.WithDisplay(new SceneDisplayState(
            RenderPurpose.Default | RenderPurpose.Render,
            RenderVisibility.IncludeInvisible,
            RenderDrawMode.Wireframe));
        StageRenderState viewport = display.WithViewport(new ViewportDimensions(1920, 1080));
        StageRenderState settings = viewport.WithRenderSettings(new RenderSettings(
            samplesPerPixel: 4,
            enableLighting: false,
            enableShadows: false,
            new Vector4(0.1f, 0.2f, 0.3f, 1)));
        StageRenderState diagnostics = settings.WithDiagnostics(new RenderDiagnosticsState(
            [new RenderDiagnostic(RenderDiagnosticSeverity.Information, "frame.ready", "Ready")]));

        await Assert.That(initial.Revision).IsEqualTo(0ul);
        await Assert.That(camera.Revision).IsEqualTo(1ul);
        await Assert.That(time.Revision).IsEqualTo(2ul);
        await Assert.That(selection.Revision).IsEqualTo(3ul);
        await Assert.That(display.Revision).IsEqualTo(4ul);
        await Assert.That(viewport.Revision).IsEqualTo(5ul);
        await Assert.That(settings.Revision).IsEqualTo(6ul);
        await Assert.That(diagnostics.Revision).IsEqualTo(7ul);
        await Assert.That(initial.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(diagnostics.WithDiagnostics(diagnostics.Diagnostics))
            .IsSameReferenceAs(diagnostics);
    }

    [Test]
    public async Task BackendChangePreservesCompleteStateSnapshot()
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("asset.usdz"))
            .WithCamera(new CameraState(
                Matrix4x4.CreateTranslation(1, 2, 3),
                Matrix4x4.CreateOrthographic(20, 10, 0.1f, 100)))
            .WithTime(new StageTime(48))
            .WithSelection(new SelectionState(["/World/Hero", "/World/Prop"]))
            .WithDisplay(new SceneDisplayState(
                RenderPurpose.Default | RenderPurpose.Proxy,
                RenderVisibility.RespectAuthored,
                RenderDrawMode.Bounds))
            .WithViewport(new ViewportDimensions(1280, 720))
            .WithRenderSettings(new RenderSettings(
                samplesPerPixel: 8,
                enableLighting: true,
                enableShadows: false,
                new Vector4(0.25f, 0.5f, 0.75f, 1)))
            .WithDiagnostics(new RenderDiagnosticsState(
                [new RenderDiagnostic(RenderDiagnosticSeverity.Warning, "asset.pending", "Pending")]));
        var storm = new TestBackend(RenderBackendKind.Storm);
        var vulkan = new TestBackend(RenderBackendKind.Vulkan);

        storm.Accept(state);
        vulkan.Accept(storm.Snapshot!);

        await Assert.That(vulkan.Snapshot).IsSameReferenceAs(state);
        await Assert.That(vulkan.Snapshot).IsEqualTo(storm.Snapshot);
        await Assert.That(vulkan.Snapshot!.Revision).IsEqualTo(7ul);
        await Assert.That(vulkan.Snapshot.Stage.Identifier).IsEqualTo("asset.usdz");
        await Assert.That(vulkan.Snapshot.Selection.PrimPaths).Count().IsEqualTo(2);
        await Assert.That(vulkan.Snapshot.Viewport).IsEqualTo(new ViewportDimensions(1280, 720));
        await Assert.That(storm.Kind).IsNotEqualTo(vulkan.Kind);
    }

    [Test]
    public async Task AdvanceRevisionPreservesEveryStateValue()
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("stage.usda"))
            .WithTime(new StageTime(24))
            .WithSelection(new SelectionState(["/World/Cube"]));

        StageRenderState advanced = state.AdvanceRevision();

        await Assert.That(advanced).IsNotSameReferenceAs(state);
        await Assert.That(advanced.Revision).IsEqualTo(state.Revision + 1);
        await Assert.That(advanced.Stage).IsSameReferenceAs(state.Stage);
        await Assert.That(advanced.Selection).IsSameReferenceAs(state.Selection);
        await Assert.That(advanced.Camera).IsEqualTo(state.Camera);
        await Assert.That(advanced.Time).IsEqualTo(state.Time);
        await Assert.That(advanced.Display).IsEqualTo(state.Display);
        await Assert.That(advanced.Viewport).IsEqualTo(state.Viewport);
        await Assert.That(advanced.RenderSettings).IsEqualTo(state.RenderSettings);
        await Assert.That(advanced.Diagnostics).IsSameReferenceAs(state.Diagnostics);
    }

    private sealed class TestBackend(RenderBackendKind kind)
    {
        internal RenderBackendKind Kind { get; } = kind;

        internal StageRenderState? Snapshot { get; private set; }

        internal void Accept(StageRenderState snapshot)
        {
            Snapshot = snapshot;
        }
    }
}
