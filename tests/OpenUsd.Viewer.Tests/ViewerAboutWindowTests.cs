// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerAboutWindowTests
{
    [Test]
    [Arguments(360, 220, false)]
    [Arguments(360, 220, true)]
    [Arguments(460, 280, false)]
    [Arguments(460, 280, true)]
    public async Task AboutCompatibilityGuidanceSupportsKeyboardPagingAndModalEscape(int width, int height, bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var owner = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            owner.Show();
            var window = new AboutWindow { Width = width, Height = height };
            Task dialog = window.ShowDialog(owner);
            try
            {
                window.UpdateLayout();
                TextBlock text = window.FindControl<TextBlock>("CompatibilityText") ??
                    throw new InvalidOperationException("About compatibility guidance is missing.");
                string? original = text.Text;
                await Assert.That(original).Contains("docs/omniverse-profile.md");
                TextBlock version = window.FindControl<TextBlock>("VersionText") ??
                    throw new InvalidOperationException("About version is missing.");
                await Assert.That(version.Text)
                    .IsEqualTo($"Version: {typeof(AboutWindow).Assembly.GetName().Version}");
                Button close = window.FindControl<Button>("CloseButton") ??
                    throw new InvalidOperationException("About Close is missing.");
                Point origin = close.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("About Close has no window coordinates.");
                await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(origin.Y + close.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(close.Focus()).IsTrue();
                ScrollViewer body = window.GetVisualDescendants().OfType<ScrollViewer>().Single();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                await Assert.That(body.IsFocused).IsTrue();
                await Assert.That(AutomationProperties.GetName(body)).IsEqualTo("Viewer compatibility guidance");
                if (width == 360)
                {
                    await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                }
                int pages = (int)Math.Ceiling(body.Extent.Height / body.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y)
                    .IsEqualTo(Math.Max(0, body.Extent.Height - body.Viewport.Height));
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(0);
                await Assert.That(text.Text).IsEqualTo(original);
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await dialog.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(owner.IsVisible).IsTrue();
                owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            finally
            {
                window.Close();
                owner.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
