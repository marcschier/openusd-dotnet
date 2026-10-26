// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCompletedJobHistoryTests
{
    private static readonly string[] HistoryActionNames = ["HistoryViewButton", "HistoryCloseButton"];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HistoricalGalleryShowsTheSelectedOriginalSimulationPublicationWithoutRendering(bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = files.CreateJob();
        Guid epoch = Guid.Parse("b319e47c-33c8-4e19-9521-ed77ab669b82");
        var first = new RenderSimulationFrameIdentity(epoch, "warehouse fixture", 7, 11, 12,
            0.2, 0, 1d / 60, 5, 9, 2, "review-v3");
        var second = new RenderSimulationFrameIdentity(epoch, "warehouse fixture", 7, 13, 14,
            0.23333333333333334, 1, 1d / 60, 6, 10, 3);
        RenderDiskFrameResult[] frames = original.Frames.Select((frame, index) =>
            new RenderDiskFrameResult(frame.Index, frame.State, frame.FileName, frame.Bytes, frame.Sha256,
                simulation: index == 0 ? first : index == 1 ? second : null)).ToArray();
        var job = new RenderDiskJobResult(original.OutputDirectory, frames, original.TotalBytes, []);
        var history = new ViewerCompletedJobHistory();
        history.Publish(job, "Retired simulation document");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedJobHistoryWindow(history)
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                CompletedRenderJobWindow preview = await OpenAsync(window);
                preview.Width = 520;
                preview.Height = 460;
                preview.UpdateLayout();
                TextBox context = Required<TextBox>(preview, "ResultsFrameContext");
                await Assert.That(context.IsReadOnly).IsTrue();
                await Assert.That(context.Text).Contains("Epoch: b319e47c-33c8-4e19-9521-ed77ab669b82");
                await Assert.That(context.Text).Contains("Scenario: warehouse fixture");
                await Assert.That(context.Text).Contains("Source revision: 7");
                await Assert.That(context.Text).Contains("Publication revision: 11");
                await Assert.That(context.Text).Contains("Step: 12");
                await Assert.That(context.Text).Contains("Simulation seconds: 0.2");
                await Assert.That(context.Text).Contains("Time code: 0");
                await Assert.That(context.Text).Contains("Fixed step seconds: 0.016666666666666666");
                await Assert.That(context.Text).Contains("Binding revision: 5");
                await Assert.That(context.Text).Contains("Bodies: 9");
                await Assert.That(context.Text).Contains("Deformations: 2");
                await Assert.That(context.Text).Contains("Review: review-v3");
                await Assert.That(context.Text).Contains("Host attestations; not independent source verification.");
                ListBox selected = Required<ListBox>(preview, "ResultsFrames");
                await Assert.That(selected.Bounds.Height).IsGreaterThan(0);
                Bitmap bitmap = ((CompletedRenderJobTile)selected.Items[0]!).Image;
                await Assert.That(Convert.ToHexString(ViewerCompletedJobPreviewJourney.ReadPixels(bitmap)
                    .AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                Button close = Required<Button>(preview, "ResultsCloseButton");
                Point closeOrigin = close.TranslatePoint(default, preview) ??
                    throw new InvalidOperationException("Completed results Close has no window coordinates.");
                await Assert.That(closeOrigin.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(closeOrigin.Y + close.Bounds.Height).IsLessThanOrEqualTo(preview.ClientSize.Height);
                selected.SelectedIndex = 1;
                await Assert.That(context.Text).Contains("Publication revision: 13");
                await Assert.That(context.Text).Contains("Step: 14");
                await Assert.That(context.Text).Contains("Simulation seconds: 0.23333333333333334");
                await Assert.That(context.Text).Contains("Time code: 1");
                await Assert.That(context.Text).Contains("Binding revision: 6");
                await Assert.That(context.Text).Contains("Bodies: 10");
                await Assert.That(context.Text).Contains("Deformations: 3");
                await Assert.That(context.Text).Contains("Review: not recorded");
                await Assert.That(context.Text).DoesNotContain("review-v3");
                selected.SelectedIndex = 2;
                await Assert.That(context.Text).IsEqualTo("No simulation publication was recorded for this frame.");
                selected.SelectedIndex = -1;
                await Assert.That(context.Text).IsEqualTo("Select a completed frame to inspect its recorded context.");
                await window.CloseAsync().WaitAsync(lifetime.Token);
                await Assert.That(context.Text).DoesNotContain("warehouse fixture");
                await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(bitmap))
                    .Throws<ObjectDisposedException>();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(520, 440, false)]
    [Arguments(520, 440, true)]
    [Arguments(740, 560, false)]
    [Arguments(740, 560, true)]
    public async Task HistoryKeepsActionsAndFocusedOutputReachableWithLongHistoricalContext(
        int width, int height, bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob();
        RenderDiskJobResult older = files.CreateJob(1);
        string description = string.Join(' ', Enumerable.Repeat("Original historical renderer context", 128));
        var history = new ViewerCompletedJobHistory();
        history.Publish(older, "Older historical output");
        history.Publish(job, description);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedJobHistoryWindow(history)
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Required<TextBlock>(window, "HistoryDescription").Text).IsEqualTo(description);
                ListBox jobs = Required<ListBox>(window, "HistoryJobs");
                await Assert.That(jobs.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(jobs.SelectedIndex).IsEqualTo(0);
                jobs.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
                await Assert.That(jobs.SelectedIndex).IsEqualTo(1);
                await Assert.That(Required<TextBox>(window, "HistoryOutput").Text).IsEqualTo(older.OutputDirectory);
                jobs.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
                await Assert.That(jobs.SelectedIndex).IsEqualTo(0);
                await Assert.That(jobs.Bounds.Height).IsGreaterThanOrEqualTo(100);
                foreach (string name in HistoryActionNames)
                {
                    Button button = Required<Button>(window, name);
                    Point origin = button.TranslatePoint(default, window) ??
                        throw new InvalidOperationException($"History action {name} has no window coordinates.");
                    await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Y + button.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                    await Assert.That(origin.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.X + button.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
                }
                TextBox output = Required<TextBox>(window, "HistoryOutput");
                await Assert.That(output.Focus()).IsTrue();
                output.BringIntoView();
                window.UpdateLayout();
                Point location = output.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Historical output has no window coordinates.");
                await Assert.That(location.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(location.Y + output.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(output.Text).IsEqualTo(job.OutputDirectory);
                await Assert.That(Required<TextBlock>(window, "HistoryStatus").Text).Contains("not the current scene");
                output.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    public async Task HistoryKeepsEightNewestOriginalJobsWithoutMutatingSnapshotsOrDeletingFiles()
    {
        using var files = new ViewerCompletedJobTestFiles();
        var history = new ViewerCompletedJobHistory();
        await Assert.That(history.Snapshot()).IsEmpty();
        RenderDiskJobResult first = files.CreateJob();
        history.Publish(first, "Original closed document");
        IReadOnlyList<ViewerCompletedJobHistoryEntry> snapshot = history.Snapshot();
        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(snapshot[0].Job).IsSameReferenceAs(first);
        var jobs = new List<RenderDiskJobResult> { first };
        for (int index = 1; index <= ViewerCompletedJobHistory.MaximumJobs; index++)
        {
            RenderDiskJobResult job = files.CreateJob();
            jobs.Add(job);
            history.Publish(job, $"Document {index}");
            await Assert.That(history.Snapshot().Count)
                .IsEqualTo(Math.Min(index + 1, ViewerCompletedJobHistory.MaximumJobs));
        }
        IReadOnlyList<ViewerCompletedJobHistoryEntry> latest = history.Snapshot();
        await Assert.That(latest.Select(entry => entry.Job).SequenceEqual(jobs.Skip(1).Reverse())).IsTrue();
        await Assert.That(latest[0].Description).IsEqualTo("Document 8");
        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(snapshot[0].Description).IsEqualTo("Original closed document");
        await Assert.That(File.Exists(Path.Combine(first.OutputDirectory, first.Frames[0].FileName))).IsTrue();
    }

    [Test]
    public async Task HistoryReopensTheSelectedJobAfterPublisherClosesAndRejectsChangedOrMissingFiles()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult first = files.CreateJob(1);
        RenderDiskJobResult second = files.CreateJob(2);
        var history = new ViewerCompletedJobHistory();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var publisher = new Window();
            var action = new Button();
            publisher.Show();
            var results = new ViewerCompletedJobResults(publisher, action, history.Publish);
            var window = new CompletedJobHistoryWindow(history) { Width = 520, Height = 440 };
            window.Show();
            try
            {
                Button view = Required<Button>(window, "HistoryViewButton");
                ListBox jobs = Required<ListBox>(window, "HistoryJobs");
                await Assert.That(view.IsEnabled).IsFalse();
                await Assert.That(jobs.ItemCount).IsEqualTo(0);
                var emptyEnter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                jobs.RaiseEvent(emptyEnter);
                await Assert.That(emptyEnter.Handled).IsFalse();
                await Assert.That(window.OwnedWindows).IsEmpty();
                results.Publish(first, "Closed first document");
                results.Publish(second, "Closed second document");
                await results.ClearAsync();
                publisher.Close();
                window.Refresh();
                await Assert.That(view.IsEnabled).IsTrue();
                await Assert.That(jobs.ItemCount).IsEqualTo(2);
                await Assert.That(jobs.SelectedIndex).IsEqualTo(0);
                object? selection = jobs.SelectedItem;
                window.Refresh();
                await Assert.That(jobs.SelectedItem).IsSameReferenceAs(selection);
                await Assert.That(Required<TextBox>(window, "HistoryOutput").Text).IsEqualTo(second.OutputDirectory);
                await Assert.That(Required<TextBlock>(window, "HistoryStatus").Text).Contains("not the current scene");
                window.UpdateLayout();
                Point? close = Required<Button>(window, "HistoryCloseButton").TranslatePoint(default, window);
                await Assert.That(close).IsNotNull();
                await Assert.That(close!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(close.Value.Y + Required<Button>(window, "HistoryCloseButton").Bounds.Height)
                    .IsLessThanOrEqualTo(window.ClientSize.Height);

                CompletedRenderJobWindow newer = await OpenAsync(window);
                await Assert.That(Required<ListBox>(newer, "ResultsFrames").ItemCount).IsEqualTo(2);
                Bitmap image = ((CompletedRenderJobTile)Required<ListBox>(newer, "ResultsFrames").Items[0]!).Image;
                jobs.SelectedIndex = 1;
                CompletedRenderJobWindow older = await OpenAsync(window, keyboard: true);
                await Assert.That(newer.IsVisible).IsFalse();
                await Assert.That(Required<ListBox>(newer, "ResultsFrames").ItemCount).IsEqualTo(0);
                await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(image))
                    .Throws<ObjectDisposedException>();
                await Assert.That(Required<TextBox>(older, "ResultsOutputLocation").Text)
                    .IsEqualTo(first.OutputDirectory);
                await Assert.That(Required<ListBox>(older, "ResultsFrames").ItemCount).IsEqualTo(1);

                File.Delete(Path.Combine(first.OutputDirectory, first.Frames[0].FileName));
                CompletedRenderJobWindow missing = await OpenAsync(window, keyboard: true);
                await Assert.That(Required<TextBlock>(missing, "ResultsStatus").Text)
                    .StartsWith("Could not preview results:");
                await Assert.That(Required<ListBox>(missing, "ResultsFrames").ItemCount).IsEqualTo(0);
                string path = Path.Combine(second.OutputDirectory, second.Frames[0].FileName);
                byte[] png = await File.ReadAllBytesAsync(path);
                png[^1] ^= 1;
                await File.WriteAllBytesAsync(path, png);
                jobs.SelectedIndex = 0;
                CompletedRenderJobWindow changed = await OpenAsync(window);
                await Assert.That(Required<TextBlock>(changed, "ResultsStatus").Text)
                    .StartsWith("Could not preview results:");
                await Assert.That(Required<ListBox>(changed, "ResultsFrames").ItemCount).IsEqualTo(0);
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(changed.IsVisible).IsFalse();
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                await window.CloseAsync();
                publisher.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    public async Task PhysicsRecordingPublishesItsOriginalCompletedJobWithoutViewportRendering()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob();
        var history = new ViewerCompletedJobHistory();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            int physicsCalls = 0;
            var window = new RenderImageSequenceWindow(0, 1, "Physics fixture", static _ => null,
                static (_, _, _, _, _) => throw new InvalidOperationException("Viewport capture was not requested."),
                (_, _, _) =>
                {
                    physicsCalls++;
                    return Task.FromResult(job);
                }, static _ => null, physicsMode: true, completed: history.Publish);
            window.Show();
            try
            {
                Required<TextBox>(window, "SequenceOutputFolder").Text = files.Root;
                Button render = Required<Button>(window, "SequenceRenderButton");
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => render.IsEnabled);
                render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => Required<Button>(window, "SequenceViewResultsButton").IsEnabled);
                await Assert.That(physicsCalls).IsEqualTo(1);
                await Assert.That(history.Snapshot().Count).IsEqualTo(1);
                await Assert.That(history.Snapshot()[0].Job).IsSameReferenceAs(job);
                await Assert.That(history.Snapshot()[0].Description).Contains("Physics");
            }
            finally
            {
                await window.CloseAsync();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    public async Task HistoryCloseCancelsAndDrainsAnActivePreviewBeforeReleasingItsWindow()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob();
        var history = new ViewerCompletedJobHistory();
        history.Publish(job, "Delayed history output");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            using ViewerCompletedJobPreview delayed = await ViewerCompletedJobPreview.LoadAsync(job, default);
            ViewerCompletedFramePreview[] frames = delayed.Frames.ToArray();
            var release = new TaskCompletionSource<ViewerCompletedJobPreview>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken captured = default;
            var window = new CompletedJobHistoryWindow(history, (_, token) =>
            {
                captured = token;
                return release.Task;
            });
            window.Show();
            try
            {
                ListBox jobs = Required<ListBox>(window, "HistoryJobs");
                var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                jobs.RaiseEvent(enter);
                await Assert.That(enter.Handled).IsTrue();
                CompletedRenderJobWindow preview = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                Required<Button>(window, "HistoryCloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Task close = window.CloseAsync();
                window.Close();
                await Assert.That(captured.IsCancellationRequested).IsTrue();
                await Assert.That(close.IsCompleted).IsFalse();
                await Assert.That(window.IsVisible).IsTrue();
                var closingEnter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                jobs.RaiseEvent(closingEnter);
                await Assert.That(closingEnter.Handled).IsFalse();
                await Assert.That(window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single())
                    .IsSameReferenceAs(preview);
                release.SetResult(delayed);
                await close.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(preview.IsVisible).IsFalse();
                foreach (ViewerCompletedFramePreview frame in frames)
                {
                    await Assert.That(frame.Pixels).IsEmpty();
                }
            }
            finally
            {
                release.TrySetResult(delayed);
                await window.CloseAsync();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static async Task<CompletedRenderJobWindow> OpenAsync(
        CompletedJobHistoryWindow window, bool keyboard = false)
    {
        if (keyboard)
        {
            ListBox jobs = Required<ListBox>(window, "HistoryJobs");
            await Assert.That(jobs.Focus()).IsTrue();
            var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
            jobs.RaiseEvent(enter);
            await Assert.That(enter.Handled).IsTrue();
        }
        else
        {
            Required<Button>(window, "HistoryViewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
            () => window.OwnedWindows.OfType<CompletedRenderJobWindow>().Any() &&
                Required<Button>(window, "HistoryViewButton").IsEnabled);
        CompletedRenderJobWindow preview = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
        await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
            () => !Required<ProgressBar>(preview, "ResultsLoading").IsVisible);
        return preview;
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing history control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
