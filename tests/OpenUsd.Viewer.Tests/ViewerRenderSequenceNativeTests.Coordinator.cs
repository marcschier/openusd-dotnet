// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseDisplaySettingsAtCoordinatorAsync(string root)
    {
        string work = Path.Combine(root, "controller-display");
        Directory.CreateDirectory(work);
        string stagePath = Path.Combine(work, "emission.usda");
        await File.WriteAllTextAsync(stagePath, EmissiveSequenceStage);
        string config = FindSequenceOcioConfig();
        await AssertSequenceOcioFixtureAsync(config);
        var identityDisplay = new RenderDisplayTransform(config, "linear", "TestDisplay", "IdentityView");
        var displayAndLook = new RenderDisplayTransform(config, "linear", "TestDisplay", "TestView", "TestLook");
        var viewport = new RendererSwitchingViewport();
        var window = new Window { Width = 320, Height = 260, Content = viewport };
        try
        {
            window.Show();
            await using ViewerRenderCoordinator coordinator = await ViewerRenderCoordinator.OpenAsync(
                stagePath,
                (scheduler, source) => new AvaloniaViewerRenderBackendHost(
                    viewport, scheduler, source, Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH")!, _ => { }),
                ViewerNativeCaptureBackend.Kind);
            await Assert.That(coordinator.ActiveBackend?.Kind).IsEqualTo(ViewerNativeCaptureBackend.Kind);
            window.UpdateLayout();
            ViewportDimensions dimensions = ViewportPixelMath.ToPixels(
                viewport.Bounds.Width, viewport.Bounds.Height, window.RenderScaling);
            var cameras = new ViewerSchedulerStageCameraSource(coordinator.Scheduler);
            ViewerStageCameraQueryResult camera = await cameras.QueryAsync(
                new ViewerStageCameraRequest("/World/Camera", 1), CancellationToken.None);
            await Assert.That(camera.Outcome).IsEqualTo(ViewerStageCameraQueryOutcome.Ready);
            _ = await coordinator.MutateStateAsync(state => state.WithViewport(dimensions)
                .WithTime(new StageTime(1))
                .WithCamera(StageCameraProjectionMath.CreateCameraState(
                    camera.Snapshot.WorldToView, camera.Snapshot.Optics, dimensions)).AdvanceRevision());
            _ = await coordinator.RenderAsync();
            await Assert.That(coordinator.CanCaptureFrame).IsTrue();
            string? previous = null;
            (RenderOutputTransform Transform, float Exposure, RenderDisplayTransform? Display, bool Selected)[] cases =
            {
                (RenderOutputTransform.Reinhard, -6f, null, false),
                (RenderOutputTransform.Identity, -6f, null, false),
                (RenderOutputTransform.Identity, -8f, null, false),
                (RenderOutputTransform.Identity, -6f, identityDisplay, false),
                (RenderOutputTransform.Identity, -8f, displayAndLook, false),
                (RenderOutputTransform.Identity, -8f, displayAndLook, true)
            };
            foreach ((RenderOutputTransform transform, float exposure, RenderDisplayTransform? display,
                bool selected) in cases)
            {
                RenderSettings current = coordinator.CurrentState.RenderSettings;
                var settings = new RenderSettings(
                    current.SamplesPerPixel, current.EnableLighting, current.EnableShadows, current.ClearColor,
                    current.BackfaceCulling, current.UseSceneMaterials, current.Complexity, transform, exposure)
                {
                    DisplayTransform = display
                };
                _ = await coordinator.MutateStateAsync(state => state.WithRenderSettings(settings)
                    .WithSelection(selected
                        ? new SelectionState([new SelectionItem("/World/Animated")]) : SelectionState.Empty)
                    .AdvanceRevision());
                StageRenderState before = coordinator.CurrentState;
                var range = new ViewerRenderSequenceRange(1, 3, 1);
                RenderDiskJobResult data = await coordinator.RenderSequenceAsync(
                    range, work, new ViewerRenderSequenceOutputOptions(true, true), "/World/Camera",
                    _ => { }, CancellationToken.None);
                string[] dataHashes = await AssertRawSequenceAsync(data.OutputDirectory, depth: true, hdr: true);
                if (previous is not null)
                {
                    await AssertRawInvarianceAsync(previous, data.OutputDirectory);
                }
                previous = data.OutputDirectory;
                RenderDiskJobResult png = await coordinator.RenderSequenceAsync(
                    range, work, default, "/World/Camera", _ => { }, CancellationToken.None);
                string[] pngHashes = await AssertRawSequenceAsync(png.OutputDirectory, depth: false, hdr: false);
                await Assert.That(dataHashes.SequenceEqual(pngHashes)).IsTrue()
                    .Because("the existing display path must remain exact at each transform and exposure");
                await Assert.That(coordinator.CurrentState.RenderSettings).IsEqualTo(settings);
                await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(before.Camera);
                await Assert.That(coordinator.CurrentState.Time).IsEqualTo(before.Time);
                foreach (RenderDiskFrameResult frame in data.Frames)
                {
                    await Assert.That(frame.State.RenderSettings).IsEqualTo(settings);
                    await Assert.That(frame.State.Selection).IsEqualTo(before.Selection);
                }
            }
            await ExerciseGpuTransformFailureAsync(coordinator, work, previous!);
        }
        finally
        {
            window.Close();
            await AssertSequenceOcioFixtureAsync(config);
        }
    }

    private static async Task ExerciseGpuTransformFailureAsync(
        ViewerRenderCoordinator coordinator, string work, string validOutput)
    {
        StageRenderState valid = coordinator.CurrentState;
        RenderSettings invalid = valid.RenderSettings with
        {
            DisplayTransform = new RenderDisplayTransform(
                Path.Combine(work, "missing.ocio"), "linear", "TestDisplay", "IdentityView")
        };
        string failed = Path.Combine(work, "failed-transform");
        Directory.CreateDirectory(failed);
        var range = new ViewerRenderSequenceRange(1, 3, 1);
        var outputs = new ViewerRenderSequenceOutputOptions(true, true);
        try
        {
            _ = await coordinator.MutateStateAsync(state => state.WithRenderSettings(invalid).AdvanceRevision());
            StageRenderState before = coordinator.CurrentState;
            await Assert.That(async () => await coordinator.RenderSequenceAsync(
                range, failed, outputs, "/World/Camera", _ => { }, CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(Directory.EnumerateFileSystemEntries(failed)).IsEmpty()
                .Because("a failed GPU transform must not publish stale HDR from an earlier successful frame");
            await Assert.That(coordinator.CurrentState.Time).IsEqualTo(before.Time);
            await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(before.Camera);
            await Assert.That(coordinator.CurrentState.RenderSettings).IsEqualTo(invalid);
        }
        finally
        {
            _ = await coordinator.MutateStateAsync(state =>
                state.WithRenderSettings(valid.RenderSettings).AdvanceRevision());
        }
        RenderDiskJobResult retry = await coordinator.RenderSequenceAsync(
            range, work, outputs, "/World/Camera", _ => { }, CancellationToken.None);
        _ = await AssertRawSequenceAsync(retry.OutputDirectory, depth: true, hdr: true);
        for (int index = 0; index < 3; index++)
        {
            foreach (string extension in new[] { "png", "hdr.rgba16f", "device-depth.f32" })
            {
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(
                    validOutput, $"frame-{index:D6}.{extension}"));
                byte[] actual = await File.ReadAllBytesAsync(Path.Combine(
                    retry.OutputDirectory, $"frame-{index:D6}.{extension}"));
                await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            }
        }
    }
}
