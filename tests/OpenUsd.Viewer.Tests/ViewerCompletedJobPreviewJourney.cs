// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

internal static partial class ViewerCompletedJobPreviewJourney
{
    internal static async Task<CompletedRenderJobWindow> OpenAsync(
        Window owner, string action, string output, int[] indices, double[] times)
    {
        Button button = Required<Button>(owner, action);
        await Assert.That(button.IsEnabled).IsTrue();
        await Assert.That(AutomationProperties.GetName(button)).Contains("results");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CompletedRenderJobWindow window = owner.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
        await WaitUntilAsync(() => !Required<ProgressBar>(window, "ResultsLoading").IsVisible);
        await Assert.That(Required<TextBlock>(window, "ResultsStatus").Text).StartsWith("Showing");
        await Assert.That(window.Owner).IsSameReferenceAs(owner);
        await Assert.That(window.ActualThemeVariant).IsEqualTo(owner.ActualThemeVariant);
        await Assert.That(Required<TextBox>(window, "ResultsOutputLocation").Text).IsEqualTo(output);
        await Assert.That(Required<TextBlock>(window, "ResultsJobIdentity").Text).IsEqualTo(Path.GetFileName(output));
        CompletedRenderJobTile[] tiles = Required<ListBox>(window, "ResultsFrames").Items
            .Cast<CompletedRenderJobTile>().ToArray();
        await Assert.That(tiles.Select(static tile => tile.FrameIndex).SequenceEqual(indices)).IsTrue();
        await Assert.That(tiles.Select(static tile => tile.TimeCode).SequenceEqual(times)).IsTrue();
        foreach (CompletedRenderJobTile tile in tiles)
        {
            await Assert.That(tile.Image.PixelSize).IsEqualTo(new PixelSize(256, 256));
            byte[] actual = ReadPixels(tile.Image);
            PngRgba8Image original = PngRgba8Reader.Decode(
                await File.ReadAllBytesAsync(Path.Combine(output, tile.FileName)));
            int left = original.Width >= original.Height ? 0 :
                (256 - (int)Math.Round(original.Width * 256d / original.Height)) / 2;
            int top = original.Height >= original.Width ? 0 :
                (256 - (int)Math.Round(original.Height * 256d / original.Width)) / 2;
            await Assert.That(Convert.ToHexString(actual.AsSpan((top * 256 + left) * 4, 4)))
                .IsEqualTo(Convert.ToHexString(original.Pixels.AsSpan(0, 4)));
        }
        return window;
    }

    internal static async Task CloseAsync(CompletedRenderJobWindow window, Window owner, string action)
    {
        Bitmap[] images = Required<ListBox>(window, "ResultsFrames").Items
            .Cast<CompletedRenderJobTile>().Select(static tile => tile.Image).ToArray();
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await AssertReleasedAsync(window, images);
        await WaitUntilAsync(() => Required<Button>(owner, action).IsKeyboardFocusWithin);
    }

    internal static async Task AssertReleasedAsync(CompletedRenderJobWindow window, Bitmap[] images)
    {
        await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(window.IsVisible).IsFalse();
        await Assert.That(Required<ListBox>(window, "ResultsFrames").ItemCount).IsEqualTo(0);
        foreach (Bitmap image in images)
        {
            await Assert.That(() => ReadPixels(image)).Throws<ObjectDisposedException>();
        }
    }

    internal static async Task RefuseTamperedOutputAsync(Window owner, string action, string output, string fileName)
    {
        string path = Path.Combine(output, fileName);
        byte[] original = await File.ReadAllBytesAsync(path);
        byte[] changed = [.. original];
        changed[^1] ^= 1;
        CompletedRenderJobWindow? window = null;
        try
        {
            await File.WriteAllBytesAsync(path, changed);
            Required<Button>(owner, action).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window = owner.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
            await WaitUntilAsync(() => !Required<ProgressBar>(window, "ResultsLoading").IsVisible);
            await Assert.That(Required<TextBlock>(window, "ResultsStatus").Text)
                .StartsWith("Could not preview results:");
            await Assert.That(Required<TextBlock>(window, "ResultsStatus").Text).Contains("SHA256");
            await Assert.That(Required<ListBox>(window, "ResultsFrames").ItemCount).IsEqualTo(0);
        }
        finally
        {
            if (window is not null)
            {
                await window.CloseAsync();
            }
            await File.WriteAllBytesAsync(path, original);
        }
    }

    internal static byte[] ReadPixels(Bitmap bitmap)
    {
        byte[] pixels = new byte[256 * 256 * 4];
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 256, 256), pinned.AddrOfPinnedObject(), pixels.Length, 256 * 4);
            return pixels;
        }
        finally
        {
            pinned.Free();
        }
    }

    internal static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing results control: {name}");

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The completed-results preview did not reach the expected state.");
            }
            await Task.Delay(20);
        }
    }
}
