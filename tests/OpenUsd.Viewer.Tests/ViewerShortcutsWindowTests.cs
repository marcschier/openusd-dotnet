// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerShortcutsWindowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ShortcutHelpSupportsKeyboardPagingAndEscapeAtMinimumSize(bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new ShortcutsWindow
            {
                Width = 480,
                Height = 320,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                ScrollViewer body = window.GetVisualDescendants().OfType<ScrollViewer>().Single();
                await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                await Assert.That(body.IsKeyboardFocusWithin).IsTrue();
                body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
                window.UpdateLayout();
                await Assert.That(body.Offset.Y).IsGreaterThan(0);
                int pages = (int)Math.Ceiling(body.Extent.Height / body.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(body.Extent.Height - body.Viewport.Height);
                for (int page = 0; page < pages; page++)
                {
                    body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(0);
                ItemsControl items = window.FindControl<ItemsControl>("ShortcutItems") ??
                    throw new InvalidOperationException("The shortcut catalog is missing.");
                await Assert.That(items.ItemsSource ??
                    throw new InvalidOperationException("The shortcut catalog is empty."))
                    .IsSameReferenceAs(ViewerShortcutCatalog.All);
                Button close = window.FindControl<Button>("CloseButton") ??
                    throw new InvalidOperationException("Shortcut Close is missing.");
                Point origin = close.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Shortcut Close has no window coordinates.");
                await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(origin.Y + close.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                window.Close();
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
