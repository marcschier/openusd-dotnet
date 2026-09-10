// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseCompletedResultsOwnerCloseAsync(string root)
    {
        string directory = Path.Combine(root, "completed-owner-close");
        string outputParent = Path.Combine(directory, "output");
        Directory.CreateDirectory(outputParent);
        string stagePath = Path.Combine(directory, "animated.usda");
        await File.WriteAllTextAsync(stagePath, AnimatedStage);
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = stagePath,
            StageCameraPath = "/World/Camera",
            Renderer = ViewerNativeCaptureBackend.Kind.ToString(),
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(Path.Combine(directory, "settings"));
        var owner = new MainWindow(new RecentStageStore(directory), store) { Width = 960, Height = 600 };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Closed += (_, _) => closed.TrySetResult();
        try
        {
            owner.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(35));
            await WaitUntilAsync(() => Required<MenuItem>(owner, "CaptureFrameMenuItem").IsEnabled);
            ViewerNativeCaptureBackend.Require(session);
            Window sequence = await OpenSequenceAsync(owner, outputParent);
            StageRenderState before = session.CurrentRenderState;
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
            await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
            await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
            Required<Button>(sequence, "SequenceViewResultsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CompletedRenderJobWindow results = sequence.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();

            owner.Close();

            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await results.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(results.IsVisible).IsFalse();
            await Assert.That(sequence.IsVisible).IsFalse();
            await Assert.That(Required<ListBox>(results, "ResultsFrames").ItemCount).IsEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(AnimatedStage);
            await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(1);
            await Assert.That(async () => await session.Scheduler.InvokeAsync(static stage => stage.ChangeSerial))
                .Throws<ObjectDisposedException>();
        }
        finally
        {
            owner.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
