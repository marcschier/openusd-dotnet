// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    internal static async Task ExercisePhysicsRecordingUiAsync(string root)
    {
        string work = Path.Combine(root, "physics-recording-ui");
        Directory.CreateDirectory(work);
        string stagePath = Path.Combine(work, "physics.usda");
        int bodyStart = PhysicsRecordingStage.IndexOf("def Mesh \"Body\"", StringComparison.Ordinal);
        int materialStart = PhysicsRecordingStage.IndexOf("def Material \"Material\"", StringComparison.Ordinal);
        string source = PhysicsRecordingStage[..bodyStart] + CompoundRecordingBody +
            PhysicsRecordingStage[materialStart..];
        await File.WriteAllTextAsync(stagePath, source);
        var ready = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = stagePath,
            StageCameraPath = "/Camera",
            Renderer = ViewerNativeCaptureBackend.Kind.ToString(),
            FullScenePhysics = true,
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                ready.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var settings = new ViewerSettingsStore(Path.Combine(work, "settings"));
        var window = new MainWindow(new RecentStageStore(work), settings) { Width = 960, Height = 600 };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            ViewerStageSession session = await ready.Task.WaitAsync(TimeSpan.FromSeconds(35));
            await WaitUntilAsync(() => Required<MenuItem>(window, "CaptureFrameMenuItem").IsEnabled);
            ViewerNativeCaptureBackend.Require(session);
            Required<MenuItem>(window, "PhysicsEnableButton").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => Required<MenuItem>(window, "PhysicsStepMenuItem").IsEnabled);
            MenuItem record = Required<MenuItem>(window, "PhysicsRecordSequenceMenuItem");
            await Assert.That(record.IsEnabled).IsTrue();
            await Assert.That(AutomationProperties.GetName(record)).IsEqualTo("Record physics sequence");
            Required<MenuItem>(window, "CommandPaletteMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Window palette = window.OwnedWindows.Single(child => child.Title == "Search commands");
            Required<TextBox>(palette, "CommandSearch").Text = "record physics sequence";
            await WaitUntilAsync(() => Required<ListBox>(palette, "CommandResults").SelectedItem is
                ListBoxItem { Tag: ViewerCommandIds.PhysicsRecordSequence });
            palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => window.OwnedWindows.OfType<RenderImageSequenceWindow>().Any());
            RenderImageSequenceWindow sequence = window.OwnedWindows.OfType<RenderImageSequenceWindow>().Single();
            await Assert.That(Required<CheckBox>(sequence, "SequencePhysicsMode").IsChecked).IsTrue();
            Required<TextBox>(sequence, "SequencePhysicsFrames").Text = "3";
            Required<TextBox>(sequence, "SequencePhysicsSteps").Text = "6";
            Required<TextBox>(sequence, "SequencePhysicsScenario").Text = "viewer-ui-recording";
            Required<TextBox>(sequence, "SequenceOutputFolder").Text = work;
            Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = true;
            Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = true;
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !sequence.IsRunning);
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
            ViewerNativeCaptureBackend.RecordComposition(
                window, session, Path.Combine(work, "physics-recording-composition.json"));
            string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text!;
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,
                "manifest.json")));
            JsonElement frames = manifest.RootElement.GetProperty("frames");
            await Assert.That(frames.GetArrayLength()).IsEqualTo(3);
            for (int index = 0; index < 3; index++)
            {
                await Assert.That(frames[index].GetProperty("simulation").GetProperty("stepIndex").GetUInt64())
                    .IsEqualTo((ulong)(index + 1) * 6);
            }
            CompletedRenderJobWindow results = await ViewerCompletedJobPreviewJourney.OpenAsync(
                sequence, "SequenceViewResultsButton", output, [0, 1, 2], [6, 12, 18]);
            await ViewerCompletedJobPreviewJourney.CloseAsync(results, sequence, "SequenceViewResultsButton");
            await ExercisePhysicsRecordingCancellationUiAsync(sequence, work);
            await sequence.CloseAsync();
            await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(source);
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static async Task ExercisePhysicsRecordingCancellationUiAsync(
        RenderImageSequenceWindow sequence, string work)
    {
        string cancelled = Path.Combine(work, "cancelled");
        Directory.CreateDirectory(cancelled);
        Required<TextBox>(sequence, "SequenceOutputFolder").Text = cancelled;
        TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
        bool cancelledAfterCapture = false;
        void cancel(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!cancelledAfterCapture && args.Property == TextBlock.TextProperty &&
                status.Text?.StartsWith("Encoding: 1/", StringComparison.Ordinal) == true)
            {
                cancelledAfterCapture = true;
                Required<Button>(sequence, "SequenceCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
        status.PropertyChanged += cancel;
        try
        {
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !sequence.IsRunning);
        }
        finally
        {
            status.PropertyChanged -= cancel;
        }
        await Assert.That(cancelledAfterCapture).IsTrue();
        await Assert.That(status.Text).StartsWith("Cancelled");
        await Assert.That(Directory.EnumerateFileSystemEntries(cancelled)).IsEmpty();
        await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsNullOrEmpty();
        await Assert.That(Required<Button>(sequence, "SequenceViewResultsButton").IsEnabled).IsFalse();

        string recovery = Path.Combine(work, "recovery");
        Directory.CreateDirectory(recovery);
        Required<TextBox>(sequence, "SequenceOutputFolder").Text = recovery;
        Required<TextBox>(sequence, "SequencePhysicsFrames").Text = "2";
        Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() => !sequence.IsRunning);
        await Assert.That(status.Text).StartsWith("Completed");
        string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text!;
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,
            "manifest.json")));
        JsonElement frames = manifest.RootElement.GetProperty("frames");
        await Assert.That(frames.GetArrayLength()).IsEqualTo(2);
        await Assert.That(frames[0].GetProperty("simulation").GetProperty("stepIndex").GetUInt64()).IsEqualTo(30ul);
        await Assert.That(frames[1].GetProperty("simulation").GetProperty("stepIndex").GetUInt64()).IsEqualTo(36ul);
        CompletedRenderJobWindow results = await ViewerCompletedJobPreviewJourney.OpenAsync(
            sequence, "SequenceViewResultsButton", output, [0, 1], [30, 36]);
        await ViewerCompletedJobPreviewJourney.CloseAsync(results, sequence, "SequenceViewResultsButton");
    }

    internal static async Task ExercisePhysicsRecordingAtCoordinatorAsync(string root)
    {
        string work = Path.Combine(root, "physics-recording");
        Directory.CreateDirectory(work);
        string stagePath = Path.Combine(work, "physics.usda");
        await File.WriteAllTextAsync(stagePath, PhysicsRecordingStage);
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
            window.UpdateLayout();
            ViewportDimensions dimensions = ViewportPixelMath.ToPixels(
                viewport.Bounds.Width, viewport.Bounds.Height, window.RenderScaling);
            var cameras = new ViewerSchedulerStageCameraSource(coordinator.Scheduler);
            ViewerStageCameraQueryResult camera = await cameras.QueryAsync(
                new ViewerStageCameraRequest("/Camera", 0), CancellationToken.None);
            await Assert.That(camera.Outcome).IsEqualTo(ViewerStageCameraQueryOutcome.Ready);
            _ = await coordinator.MutateStateAsync(state => state.WithViewport(dimensions)
                .WithTime(new StageTime(0))
                .WithCamera(StageCameraProjectionMath.CreateCameraState(
                    camera.Snapshot.WorldToView, camera.Snapshot.Optics, dimensions)).AdvanceRevision());
            _ = await coordinator.RenderAsync();
            await using var physics = new ViewerPhysicsController(
                new ViewerPhysicsTransportFactory(coordinator.Scheduler),
                ViewerPhysicsStopwatchClock.Instance, ViewerPhysicsRenderCapacities.Default);
            await physics.EnableAsync();
            await Assert.That(physics.Snapshot.Error).IsEmpty();
            StageRenderState before = coordinator.CurrentState;
            ulong serial = await coordinator.Scheduler.InvokeAsync(static stage => stage.ChangeSerial);
            var request = new ViewerPhysicsSequenceRequest(3, 6, "viewer-moving-body", work,
                new ViewerRenderSequenceOutputOptions(true, true));
            var livePumps = new List<ViewerPhysicsFramePumpResult>();
            Task<ViewerPhysicsCommandOutcome>? submitted = null;
            IViewerPhysicsOverrideTarget target = coordinator.CapturePhysicsOverrideTarget(out _)!;
            RenderDiskJobResult result = await coordinator.RenderPhysicsSequenceAsync(
                physics, request, "/Camera", update =>
                {
                    if (update.Phase is "Rendering" or "Encoding")
                    {
                        livePumps.Add(physics.PumpRenderFrame(0, target));
                        submitted ??= physics.SubmitCommandsAsync(
                            [new ViewerPhysicsRuntimeCommand(ViewerPhysicsRuntimeCommandKind.LinearVelocity,
                                physics.ResolveIdentity("/Body"), new ViewerPhysicsVector3(10000, 0, 0))]);
                    }
                }, CancellationToken.None);
            await Assert.That(result.Frames.Count).IsEqualTo(3);
            await Assert.That(livePumps.Count).IsEqualTo(6);
            await Assert.That(livePumps.All(pump => !pump.Ingested && pump.Applied == 0)).IsTrue();
            ViewerPhysicsCommandOutcome refusal = await submitted!;
            await Assert.That(refusal.Accepted).IsEqualTo(0);
            await Assert.That(refusal.Rejected).IsEqualTo(1);
            await Assert.That(refusal.Message).Contains("recording");
            double previous = -1;
            for (int index = 0; index < 3; index++)
            {
                RenderDiskFrameResult frame = result.Frames[index];
                await Assert.That(frame.Simulation).IsNotNull();
                await Assert.That(frame.Simulation!.StepIndex).IsEqualTo((ulong)(index + 1) * 6);
                await Assert.That(frame.Simulation.BodyCount).IsEqualTo(1);
                await Assert.That(frame.Simulation.SourceRevision).IsEqualTo(serial);
                byte[] hdr = await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory,
                    frame.HdrColorFileName!));
                (int count, double x) = MeasurePhysicsEmission(hdr, dimensions.Width);
                await Assert.That(count).IsGreaterThan(100);
                if (index > 0)
                {
                    await Assert.That(x - previous).IsGreaterThan(15);
                }
                previous = x;
            }
            await Assert.That(physics.Snapshot.Status.StepIndex).IsEqualTo(18ul);
            await Assert.That(physics.IsPlaying).IsFalse();
            await Assert.That(physics.IsBusy).IsFalse();
            await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(before.Camera);
            await Assert.That(coordinator.CurrentState.Time).IsEqualTo(before.Time);
            await Assert.That(await coordinator.Scheduler.InvokeAsync(static stage =>
                stage.ChangeSerial)).IsEqualTo(serial);
            await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(PhysicsRecordingStage);
            await ExercisePublishedJobCancellationAsync(window, work, result);
            string rejected = Path.Combine(work, "rejected");
            Directory.CreateDirectory(rejected);
            await physics.SetLoopAsync(true);
            await Assert.That(async () => await coordinator.RenderPhysicsSequenceAsync(
                physics, request with { OutputParent = rejected }, "/Camera", _ => { }, CancellationToken.None))
                .Throws<InvalidOperationException>();
            await physics.SetLoopAsync(false);
            await Assert.That(async () => await coordinator.RenderPhysicsSequenceAsync(
                physics, request with { OutputParent = rejected, FrameCount = 4096 }, "/Camera", _ => { },
                CancellationToken.None)).Throws<ArgumentOutOfRangeException>();
            await coordinator.Scheduler.InvokeAsync(stage => stage.GetPrim("/Body").SetBool(
                "physics:rigidBodyEnabled", false));
            await Assert.That(async () => await coordinator.RenderPhysicsSequenceAsync(
                physics, request with { OutputParent = rejected }, "/Camera", _ => { }, CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(physics.Snapshot.Status.StepIndex).IsEqualTo(18ul);
            await Assert.That(physics.IsBusy).IsFalse();
            await Assert.That(Directory.EnumerateFileSystemEntries(rejected)).IsEmpty();
            await coordinator.Scheduler.InvokeAsync(stage =>
            {
                UsdPrim body = stage.DefinePrim("/Body", "Mesh");
                body.SetBool("physics:rigidBodyEnabled", true);
                body.SetToken("physics:approximation", "sdf");
                body.SetVec3fArray("points",
                    [new UsdVec3f(0, 0, 0), new UsdVec3f(1, 0, 0), new UsdVec3f(0, 1, 0), new UsdVec3f(0, 0, 1)]);
                body.SetInt32Array("faceVertexCounts", [3, 3, 3, 3]);
                body.SetInt32Array("faceVertexIndices", [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3]);
            });
            await physics.RebuildAsync();
            await Assert.That(physics.Diagnostics.Any(entry =>
                entry.Code == "OPENUSD_PHYSICS_COMPOSITION_SKIPPED")).IsTrue();
            await Assert.That(physics.RecordingUnsupportedReason).Contains("sdf");
            await Assert.That(async () => await coordinator.RenderPhysicsSequenceAsync(
                physics, request with { OutputParent = rejected, FrameCount = 1 }, "/Camera", _ => { },
                CancellationToken.None)).Throws<InvalidOperationException>();
            await Assert.That(physics.Snapshot.Status.StepIndex).IsEqualTo(0ul);
            await Assert.That(Directory.EnumerateFileSystemEntries(rejected)).IsEmpty();
            await physics.StepOneFrameAsync();
            await Assert.That(physics.Snapshot.Status.StepIndex).IsEqualTo(1ul);
            await Assert.That(physics.RecordingUnsupportedReason).Contains("sdf");
            await Assert.That(async () => await coordinator.RenderPhysicsSequenceAsync(
                physics, request with { OutputParent = rejected, FrameCount = 1 }, "/Camera", _ => { },
                CancellationToken.None)).Throws<InvalidOperationException>();
            await Assert.That(physics.Snapshot.Status.StepIndex).IsEqualTo(1ul);
            await Assert.That(Directory.EnumerateFileSystemEntries(rejected)).IsEmpty();
            await coordinator.Scheduler.InvokeAsync(stage => stage.GetPrim("/Body")
                .SetToken("physics:approximation", "convexHull"));
            await physics.RebuildAsync();
            await Assert.That(physics.RecordingUnsupportedReason).IsNull();
            RenderDiskJobResult repaired = await coordinator.RenderPhysicsSequenceAsync(
                physics, request with { OutputParent = rejected, FrameCount = 1 }, "/Camera", _ => { },
                CancellationToken.None);
            await Assert.That(repaired.Frames.Count).IsEqualTo(1);
            await Assert.That(repaired.Frames[0].Simulation!.StepIndex).IsEqualTo(6ul);
            await Assert.That(repaired.Frames[0].Simulation!.BodyCount).IsEqualTo(1);
            await Assert.That(physics.IsBusy).IsFalse();
            await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(PhysicsRecordingStage);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task ExercisePublishedJobCancellationAsync(Window owner, string work,
        RenderDiskJobResult result)
    {
        RenderImageSequenceWindow? sequence = null;
        sequence = new RenderImageSequenceWindow(0, 0, "Completed job", _ => null,
            (_, _, _, _, _) => throw new InvalidOperationException("The physics source must own this job."),
            (_, _, _) =>
            {
                sequence!.Cancel();
                return Task.FromResult(result);
            }, _ => null, physicsMode: true);
        try
        {
            sequence.Show(owner);
            Required<TextBox>(sequence, "SequenceOutputFolder").Text = work;
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !sequence.IsRunning);
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
            await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsEqualTo(
                result.OutputDirectory);
            await Assert.That(Required<Button>(sequence, "SequenceViewResultsButton").IsEnabled).IsTrue();
        }
        finally
        {
            await sequence.CloseAsync();
        }
    }

    private static (int Count, double X) MeasurePhysicsEmission(byte[] color, int width)
    {
        int count = 0;
        double x = 0;
        for (int pixel = 0; pixel < color.Length / 8; pixel++)
        {
            ReadOnlySpan<byte> value = color.AsSpan(pixel * 8, 8);
            float r = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(value));
            float g = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(value[2..]));
            float b = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(value[4..]));
            float a = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(value[6..]));
            // The cube's lit faces can add tiny non-red specular values; the emission itself remains exact.
            if (r == 64 && Math.Abs(g) < 0.001f && Math.Abs(b) < 0.001f && a == 1)
            {
                count++;
                x += pixel % width;
            }
        }
        return (count, x / Math.Max(1, count));
    }

    private const string CompoundRecordingBody = """
        def Xform "Body" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI", "MaterialBindingAPI"]) {
            double3 xformOp:translate = (0,0,-4)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            vector3f physics:velocity = (20,0,0)
            float physics:mass = 1
            rel material:binding = </Material>
            def Cube "Left" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double size = 2.5
                double3 xformOp:translate = (-1.5,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Cube "Right" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double size = 2.5
                double3 xformOp:translate = (1.5,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        def Xform "StaticPrototype" {
            def Cube "Collider" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double3 xformOp:translate = (100,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        def Xform "StaticInstance" (references = </StaticPrototype>; instanceable = true) {
            double3 xformOp:translate = (200,0,0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        """;

    private const string PhysicsRecordingStage = $$"""
        #usda 1.0
        (
            metersPerUnit = 0.01
            upAxis = "Y"
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 48
        )
        def PhysicsScene "Physics" {
            float physics:gravityMagnitude = 0
        }
        def Camera "Camera" {
            token projection = "orthographic"
            float horizontalAperture = 800
            float verticalAperture = 800
            float2 clippingRange = (1, 10)
        }
        {{"def Mesh \"Body\" (prepend apiSchemas = [\"PhysicsRigidBodyAPI\", \"PhysicsCol" +
            "lisionAPI\", \"PhysicsMeshCollisionAPI\", \"PhysicsMassAPI\", \"MaterialBindin" +
            "gAPI\"]) {"}}
            uniform token subdivisionScheme = "none"
            token physics:approximation = "convexHull"
            point3f[] points = [(-2,-2,-2),(2,-2,-2),(2,2,-2),(-2,2,-2),(-2,-2,2),(2,-2,2),(2,2,2),(-2,2,2)]
            int[] faceVertexCounts = [4,4,4,4,4,4]
            int[] faceVertexIndices = [0,3,2,1,4,5,6,7,0,1,5,4,3,7,6,2,0,4,7,3,1,2,6,5]
            double3 xformOp:translate = (0,0,-4)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            vector3f physics:velocity = (20,0,0)
            float physics:mass = 1
            rel material:binding = </Material>
        }
        def Material "Material" {
            token outputs:surface.connect = </Material/Shader.outputs:surface>
            def Shader "Shader" {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor = (0,0,0)
                color3f inputs:emissiveColor = (64,0,0)
                int inputs:useSpecularWorkflow = 1
                color3f inputs:specularColor = (0,0,0)
                token outputs:surface
            }
        }
        """;
}
