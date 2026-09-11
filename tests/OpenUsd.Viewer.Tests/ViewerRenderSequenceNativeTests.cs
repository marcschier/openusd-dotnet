// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerRenderSequenceNativeTests
{
    [Test]
    public async Task FileAndPaletteRenderAnimatedFramesAndRestoreTheOperatorsView()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_RENDER_SEQUENCE_SMOKE") != "1")
        {
            Skip.Test("Run this class alone with OPENUSD_VIEWER_RENDER_SEQUENCE_SMOKE=1 on a Windows desktop.");
        }

        string? evidenceRoot = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_SEQUENCE_EVIDENCE_ROOT");
        if (evidenceRoot is not null &&
            (!Path.IsPathFullyQualified(evidenceRoot) || !Directory.Exists(evidenceRoot)))
        {
            throw new ArgumentException("Sequence evidence requires an existing absolute output directory.");
        }
        string root = Path.Combine(
            evidenceRoot ?? Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ?? Path.GetTempPath(),
            $"viewer-sequence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseSequenceAsync(root);
                        await ExerciseCompletedResultsOwnerCloseAsync(root);
                        await ViewerCompletedJobPreviewJourney.ExerciseLifetimesAsync();
                        await ExerciseSequenceAdapterAsync(root);
                        await ExerciseRawSequenceAsync(root);
                        await ExerciseDisplaySettingsAtCoordinatorAsync(root);
                        await ExerciseStormSequenceAsync(root);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        lifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(lifetime.Token);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer image sequence workflow"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(110));
        }
        finally
        {
            lifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
            if (stopped && evidenceRoot is null)
            {
                Directory.Delete(root, recursive: true);
            }
        }
        await Assert.That(stopped).IsTrue();
    }

    private static async Task ExerciseSequenceAsync(string root)
    {
        string stagePath = Path.Combine(root, "animated.usda");
        await File.WriteAllTextAsync(stagePath, AnimatedStage);
        string outputParent = Path.Combine(root, "output");
        Directory.CreateDirectory(outputParent);
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reopened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = stagePath,
            StageCameraPath = "/World/Camera",
            Renderer = ViewerNativeCaptureBackend.Kind.ToString(),
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                if (!opened.TrySetResult(session))
                {
                    reopened.TrySetResult(session);
                }
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(Path.Combine(root, "settings"));
        string ocioConfig = FindSequenceOcioConfig();
        await AssertSequenceOcioFixtureAsync(ocioConfig);
        await store.SaveAsync(ViewerSettings.Default with
        {
            ThemePreference = ViewerThemePreference.Dark,
            ColorManagement = new ViewerColorManagement
            {
                Enabled = true,
                ConfigPath = ocioConfig,
                SourceColorSpace = "linear",
                Display = "TestDisplay",
                View = "IdentityView",
                Look = "TestLook"
            }
        });
        var framePicker = new SequenceFramePicker();
        var window = new MainWindow(new RecentStageStore(root), store, frameFilePicker: framePicker)
        {
            Width = 960,
            Height = 600
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(35));
            await WaitUntilAsync(() => Required<MenuItem>(window, "CaptureFrameMenuItem").IsEnabled);
            ViewerNativeCaptureBackend.Require(session);
            await ConfigureSequenceDisplayAsync(window, session);
            StageRenderState before = session.CurrentRenderState;
            ViewerSettings settingsBefore = (await store.LoadAsync()).Settings;
            string editTarget = await session.Scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier);

            MenuItem action = Required<MenuItem>(window, "RenderImageSequenceMenuItem");
            await Assert.That(action.IsEnabled).IsTrue();
            await Assert.That(AutomationProperties.GetName(action)).IsEqualTo("Render image sequence");
            Required<MenuItem>(window, "CommandPaletteMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Window palette = window.OwnedWindows.Single(child => child.Title == "Search commands");
            Required<TextBox>(palette, "CommandSearch").Text = "render image sequence";
            await WaitUntilAsync(() =>
                Required<TextBox>(palette, "CommandSearch").IsKeyboardFocusWithin &&
                Required<ListBox>(palette, "CommandResults").SelectedItem is
                    ListBoxItem { Tag: ViewerCommandIds.FileRenderImageSequence });
            palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => window.OwnedWindows.Any(child => child.Title == "Render image sequence"));
            Window sequence = window.OwnedWindows.Single(child => child.Title == "Render image sequence");
            await WaitUntilAsync(() => Required<TextBox>(sequence, "SequenceStartTime").IsKeyboardFocusWithin);
            await Assert.That(sequence.Owner).IsSameReferenceAs(window);
            await Assert.That(sequence.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That(Required<TextBox>(sequence, "SequenceStartTime").IsKeyboardFocusWithin).IsTrue();
            await Assert.That(Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked).IsFalse();
            await Assert.That(Required<CheckBox>(sequence, "SequenceDepthData").IsChecked).IsFalse();
            await Assert.That(Required<Button>(sequence, "SequenceViewResultsButton").IsEnabled).IsFalse();
            Required<TextBox>(sequence, "SequenceStartTime").Text = "0";
            Required<TextBox>(sequence, "SequenceEndTime").Text = "4096";
            await WaitUntilAsync(() => !Required<Button>(sequence, "SequenceRenderButton").IsEnabled);
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).Contains("4096");
            Required<TextBox>(sequence, "SequenceStartTime").Text = "1";
            Required<TextBox>(sequence, "SequenceEndTime").Text = "3";
            Required<TextBox>(sequence, "SequenceStep").Text = "1";
            Required<TextBox>(sequence, "SequenceOutputFolder").Text = outputParent;
            Required<Button>(sequence, "SequenceRenderButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() =>
                Required<TextBlock>(sequence, "SequenceStatus").Text?.StartsWith(
                    "Completed", StringComparison.Ordinal) == true ||
                Required<TextBlock>(sequence, "SequenceStatus").Text?.StartsWith(
                    "Cancelled", StringComparison.Ordinal) == true ||
                Required<TextBlock>(sequence, "SequenceStatus").Text?.Contains(
                    "failed", StringComparison.OrdinalIgnoreCase) == true);
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
            string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text ??
                throw new InvalidOperationException("A completed sequence must expose its output location.");
            await Assert.That(Path.GetDirectoryName(output)).IsEqualTo(outputParent);
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(output, "manifest.json")));
            JsonElement frames = manifest.RootElement.GetProperty("frames");
            await Assert.That(frames.GetArrayLength()).IsEqualTo(3);
            await AssertExactManifestAsync(frames, before.Viewport, ocioConfig);
            for (int index = 0; index < 3; index++)
            {
                await Assert.That(frames[index].GetProperty("timeCode").GetDouble()).IsEqualTo(index + 1d);
                await Assert.That(frames[index].GetProperty("width").GetInt32()).IsEqualTo(before.Viewport.Width);
                await Assert.That(frames[index].GetProperty("height").GetInt32()).IsEqualTo(before.Viewport.Height);
                await Assert.That(File.Exists(Path.Combine(output, $"frame-{index:D6}.png"))).IsTrue();
                await Assert.That(frames[index].TryGetProperty("hdrColor", out _)).IsFalse();
                await Assert.That(frames[index].TryGetProperty("deviceDepth", out _)).IsFalse();
            }
            await Assert.That(Directory.GetFiles(output).Length).IsEqualTo(4);
            ViewerNativeCaptureBackend.RecordComposition(
                window, session, Path.Combine(root, "sequence-composition.json"));
            await Assert.That(frames[0].GetProperty("sha256").GetString())
                .IsNotEqualTo(frames[1].GetProperty("sha256").GetString());
            await Assert.That(frames[1].GetProperty("sha256").GetString())
                .IsNotEqualTo(frames[2].GetProperty("sha256").GetString());
            CompletedRenderJobWindow results = await ViewerCompletedJobPreviewJourney.OpenAsync(
                sequence, "SequenceViewResultsButton", output, [0, 1, 2], [1, 2, 3]);
            await ViewerCompletedJobPreviewJourney.CloseAsync(results, sequence, "SequenceViewResultsButton");
            await ViewerCompletedJobPreviewJourney.RefuseTamperedOutputAsync(
                sequence, "SequenceViewResultsButton", output, "frame-000002.png");
            await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
            await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
            await Assert.That(session.CurrentRenderState.Display).IsEqualTo(before.Display);
            await Assert.That(session.CurrentRenderState.Selection).IsEqualTo(before.Selection);
            await Assert.That(session.CurrentRenderState.RenderSettings).IsEqualTo(before.RenderSettings);
            await Assert.That((await store.LoadAsync()).Settings).IsEqualTo(settingsBefore);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier))
                .IsEqualTo(editTarget);
            await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(AnimatedStage);
            await Assert.That(Directory.GetDirectories(outputParent, ".openusd-render-*").Length).IsEqualTo(0);
            sequence.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            await ExerciseUnsupportedPurposeAsync(window, session);
            await ExerciseGpuSequencePlanesAsync(window, session, root, output);
            await ExerciseCancellationAsync(window, session, framePicker, root, outputParent);
            await ExerciseChangedSourceAsync(window, session, framePicker, root, outputParent);
            await ExerciseChangedCameraAsync(window, session, framePicker, root, outputParent);
            await ExercisePendingReloadAsync(window, session, outputParent, reopened.Task);
            await ExercisePendingCloseAsync(window, await reopened.Task, outputParent, closed.Task);
        }
        finally
        {
            window.Close();
            await WaitUntilAsync(() => closed.Task.IsCompleted ||
                window.OwnedWindows.OfType<DocumentChangesWindow>().Any());
            if (window.OwnedWindows.OfType<DocumentChangesWindow>().FirstOrDefault() is { } changes)
            {
                Required<Button>(changes, "DiscardDocumentChangesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertSequenceOcioFixtureAsync(ocioConfig);
        }
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing sequence control: {name}");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The image-sequence workflow did not reach the expected state.");
            }
            await Task.Delay(20);
        }
    }

    private const string AnimatedStage = """
        #usda 1.0
        (
            defaultPrim = "World"
            startTimeCode = 1
            endTimeCode = 3
            timeCodesPerSecond = 24
            upAxis = "Y"
        )
        def Xform "World"
        {
            def Cube "Animated"
            {
                double size = 1.5
                color3f[] primvars:displayColor = [(0.9, 0.15, 0.05)]
                double3 xformOp:translate.timeSamples = { 1: (-1, 0, 0), 3: (1, 0.5, 0) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Camera "Camera"
            {
                float focalLength.timeSamples = { 1: 35, 3: 55 }
                float horizontalAperture = 36
                float verticalAperture = 24
                float horizontalApertureOffset = 2
                float2 clippingRange = (0.1, 100)
                double3 xformOp:translate.timeSamples = { 1: (0, 0, 8), 3: (0.5, 0, 8) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        """;
}
