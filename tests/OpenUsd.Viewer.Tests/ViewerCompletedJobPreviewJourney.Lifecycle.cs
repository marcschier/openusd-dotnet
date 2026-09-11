// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

internal static partial class ViewerCompletedJobPreviewJourney
{
    internal static async Task ExerciseLifetimesAsync()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult first = files.CreateJob(16);
        RenderDiskJobResult second = files.CreateJob(2);
        var owner = new Window
        {
            Width = 700,
            Height = 560,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        owner.Show();
        try
        {
            await CloseDrainsLateLoadingAsync(owner, first, fail: false);
            await CloseDrainsLateLoadingAsync(owner, first, fail: true);
            await CloseDrainsLateLoadingAsync(owner, first, fail: false, inlineClose: true);
            await ExerciseCallerAsync(owner, first, second, files.Root, product: false);
            await ExerciseCallerAsync(owner, first, second, files.Root, product: true);
            await InlineJobCloseDrainsBeforeDisposalAsync(owner, first, files.Root, product: false);
            await InlineJobCloseDrainsBeforeDisposalAsync(owner, first, files.Root, product: true);
            await TamperedLaterFileShowsOnlyAnErrorAsync(owner, files.CreateJob());
        }
        finally
        {
            owner.Close();
        }
    }

    private static async Task CloseDrainsLateLoadingAsync(
        Window owner, RenderDiskJobResult job, bool fail, bool inlineClose = false)
    {
        using ViewerCompletedJobPreview delayed = await ViewerCompletedJobPreview.LoadAsync(job, default);
        ViewerCompletedFramePreview[] frames = delayed.Frames.ToArray();
        var release = new TaskCompletionSource<ViewerCompletedJobPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        CompletedRenderJobWindow? window = null;
        window = new CompletedRenderJobWindow(job, "Delayed completed output", (_, token) =>
        {
            entered.TrySetResult(token);
            if (inlineClose)
            {
                window?.Close();
            }
            return release.Task;
        });
        try
        {
            window.Show(owner);
            CancellationToken token = await entered.Task;
            Task close = window.CloseAsync();
            window.Close();
            await Assert.That(token.IsCancellationRequested).IsTrue();
            await Assert.That(close.IsCompleted).IsFalse();
            await Assert.That(window.IsVisible).IsTrue();
            bool dispatched = false;
            await Dispatcher.UIThread.InvokeAsync(() => dispatched = true, DispatcherPriority.Background);
            await Assert.That(dispatched).IsTrue();
            await Assert.That(close.IsCompleted).IsFalse();
            if (fail)
            {
                release.SetException(new IOException("Late completed-PNG read failure after close."));
            }
            else
            {
                release.SetResult(delayed);
            }
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(window.IsVisible).IsFalse();
            await Assert.That(Required<ListBox>(window, "ResultsFrames").ItemCount).IsEqualTo(0);
            if (!fail)
            {
                foreach (ViewerCompletedFramePreview frame in frames)
                {
                    await Assert.That(frame.Pixels).IsEmpty();
                }
            }
        }
        finally
        {
            release.TrySetResult(delayed);
            await window.CloseAsync();
        }
    }

    private static async Task ExerciseCallerAsync(
        Window owner, RenderDiskJobResult first, RenderDiskJobResult second, string root, bool product)
    {
        int renders = 0;
        var next = new TaskCompletionSource<RenderDiskJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<RenderDiskJobResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RenderDiskJobResult> renderJobAsync(CancellationToken _)
        {
            renders++;
            return renders switch
            {
                1 => Task.FromResult(first),
                2 => next.Task,
                3 => Task.FromException<RenderDiskJobResult>(new IOException("The newer job failed.")),
                4 => cancelled.Task,
                _ => Task.FromResult(second)
            };
        }
        Window window = CreateCaller(product, renderJobAsync);
        string prefix = product ? "Product" : "Sequence";
        string action = prefix + "ViewResultsButton";
        Button render = Required<Button>(window, prefix + "RenderButton");
        Button resultsAction = Required<Button>(window, action);
        TextBox output = Required<TextBox>(window, prefix + "OutputLocation");
        bool isRunning() => window is RenderImageSequenceWindow sequence
            ? sequence.IsRunning : ((AuthoredRenderProductWindow)window).IsRunning;
        try
        {
            window.Show(owner);
            Required<TextBox>(window, prefix + "OutputFolder").Text = root;
            await WaitUntilAsync(() => render.IsEnabled);
            await Assert.That(resultsAction.IsEnabled).IsFalse();
            render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => resultsAction.IsEnabled);
            resultsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CompletedRenderJobWindow old = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
            await Assert.That(renders).IsEqualTo(1).Because("viewing completed files must not invoke rendering");

            render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(resultsAction.IsEnabled).IsFalse();
            await Assert.That(output.Text).IsNullOrEmpty();
            await old.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(Required<ListBox>(old, "ResultsFrames").ItemCount).IsEqualTo(0);
            next.SetResult(second);
            await WaitUntilAsync(() => !isRunning() && resultsAction.IsEnabled);
            await Assert.That(output.Text).IsEqualTo(second.OutputDirectory);
            CompletedRenderJobWindow current = await OpenAsync(window, action, second.OutputDirectory, [0, 1], [0, 1]);
            await Assert.That(current).IsNotSameReferenceAs(old);
            ListBox list = Required<ListBox>(current, "ResultsFrames");
            foreach (CompletedRenderJobTile tile in list.Items.Cast<CompletedRenderJobTile>())
            {
                await Assert.That(((WriteableBitmap)tile.Image).AlphaFormat).IsEqualTo(AlphaFormat.Unpremul);
                byte[] pixels = ReadPixels(tile.Image);
                await Assert.That(Convert.ToHexString(pixels.AsSpan(0, 4))).IsEqualTo("00000000");
                await Assert.That(Convert.ToHexString(pixels.AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                await Assert.That(Convert.ToHexString(pixels.AsSpan((191 * 256 + 255) * 4, 4))).IsEqualTo("0000FF40");
                await Assert.That(tile.FrameLabel).IsEqualTo($"Frame {tile.FrameIndex}");
                await Assert.That(tile.AccessibleName).Contains($"time code {tile.TimeCode}");
            }
            owner.RequestedThemeVariant = ThemeVariant.Light;
            await WaitUntilAsync(() => current.ActualThemeVariant == ThemeVariant.Light);
            Button closeButton = Required<Button>(current, "ResultsCloseButton");
            await Assert.That(closeButton.IsKeyboardFocusWithin).IsTrue();
            current.UpdateLayout();
            closeButton.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Tab,
                KeyModifiers = KeyModifiers.Shift
            });
            await WaitUntilAsync(() => list.IsKeyboardFocusWithin);
            Control firstTile = list.ContainerFromIndex(0) ??
                throw new InvalidOperationException("The first completed frame must have a keyboard container.");
            firstTile.Focus();
            firstTile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            await WaitUntilAsync(() => list.SelectedIndex == 1);
            await CloseAsync(current, window, action);

            render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !isRunning());
            await Assert.That(resultsAction.IsEnabled).IsFalse();
            await Assert.That(output.Text).IsNullOrEmpty();
            await Assert.That(Required<TextBlock>(window, prefix + "Status").Text).Contains("The newer job failed.");
            render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Required<Button>(window, prefix + "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            cancelled.SetResult(first);
            await WaitUntilAsync(() => !isRunning());
            await Assert.That(resultsAction.IsEnabled).IsFalse();
            await Assert.That(output.Text).IsNullOrEmpty();
            await Assert.That(Required<TextBlock>(window, prefix + "Status").Text).StartsWith("Cancelled.");

            render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => resultsAction.IsEnabled);
            resultsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CompletedRenderJobWindow closing = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
            await ((IAsyncDisposable)window).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await closing.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(window.IsVisible).IsFalse();
            await Assert.That(closing.IsVisible).IsFalse();
            await Assert.That(Required<ListBox>(closing, "ResultsFrames").ItemCount).IsEqualTo(0);
        }
        finally
        {
            next.TrySetResult(second);
            cancelled.TrySetResult(first);
            await ((IAsyncDisposable)window).DisposeAsync();
        }
    }

    private static Window CreateCaller(bool product, Func<CancellationToken, Task<RenderDiskJobResult>> render) =>
        product
            ? new AuthoredRenderProductWindow(0, 1, "Completed fixture",
                static (_, _, _) => ValueTask.FromResult(new ViewerAuthoredRenderProductSnapshot("/Settings",
                    [new ViewerAuthoredRenderProductItem("/Product", "Product", "/Camera",
                        new ViewportDimensions(2, 1), "color", null, null)], null)),
                (_, _, token) => render(token))
            : new RenderImageSequenceWindow(0, 1, "Completed fixture", static _ => null,
                (_, _, _, _, token) => render(token));

    private static async Task InlineJobCloseDrainsBeforeDisposalAsync(
        Window owner, RenderDiskJobResult job, string root, bool product)
    {
        var release = new TaskCompletionSource<RenderDiskJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken renderToken = default;
        Window? window = null;
        window = CreateCaller(product, token =>
        {
            renderToken = token;
            window?.Close();
            return release.Task;
        });
        string prefix = product ? "Product" : "Sequence";
        try
        {
            window.Show(owner);
            Required<TextBox>(window, prefix + "OutputFolder").Text = root;
            Button render = Required<Button>(window, prefix + "RenderButton");
            await WaitUntilAsync(() => render.IsEnabled);
            render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Task close = ((IAsyncDisposable)window).DisposeAsync().AsTask();
            await Assert.That(close.IsCompleted).IsFalse();
            await Assert.That(renderToken.IsCancellationRequested).IsTrue();
            release.SetResult(job);
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(window.IsVisible).IsFalse();
            await Assert.That(Required<Button>(window, prefix + "ViewResultsButton").IsEnabled).IsFalse();
            await Assert.That(Required<TextBox>(window, prefix + "OutputLocation").Text).IsNullOrEmpty();
        }
        finally
        {
            release.TrySetResult(job);
            await ((IAsyncDisposable)window).DisposeAsync();
        }
    }

    private static async Task TamperedLaterFileShowsOnlyAnErrorAsync(Window owner, RenderDiskJobResult job)
    {
        string path = Path.Combine(job.OutputDirectory, job.Frames[^1].FileName);
        byte[] png = await File.ReadAllBytesAsync(path);
        png[^1] ^= 1;
        await File.WriteAllBytesAsync(path, png);
        var window = new CompletedRenderJobWindow(job, "Tampered completed output");
        try
        {
            window.Show(owner);
            await WaitUntilAsync(() => !Required<ProgressBar>(window, "ResultsLoading").IsVisible);
            await Assert.That(Required<TextBlock>(window, "ResultsStatus").Text)
                .StartsWith("Could not preview results:");
            await Assert.That(Required<TextBlock>(window, "ResultsStatus").Classes.Contains("viewer-error")).IsTrue();
            await Assert.That(Required<ListBox>(window, "ResultsFrames").ItemCount).IsEqualTo(0);
        }
        finally
        {
            await window.CloseAsync();
        }
    }
}
