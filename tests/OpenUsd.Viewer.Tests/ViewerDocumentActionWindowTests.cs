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
public sealed class ViewerDocumentActionWindowTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DecisionDetailsSupportKeyboardPagingWithoutAcceptingTheAction(bool dark, bool alternate)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            string details = string.Join(Environment.NewLine, Enumerable.Range(0, 80)
                .Select(index => $"Original review detail {index}: retain this complete path and reason."));
            var window = new DocumentActionWindow("Choose how to reopen the original review.", details,
                "Open verified review", alternate ? "Open session-only" : null)
            {
                Width = 480,
                Height = 280,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                Button cancel = Required<Button>(window, "CancelDocumentActionButton");
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                ScrollViewer body = window.GetVisualDescendants().OfType<ScrollViewer>().Single();
                await Assert.That(body.Extent.Height).IsGreaterThan(body.Viewport.Height);
                for (int stop = 0; stop < (alternate ? 3 : 2); stop++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                    window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                }
                await Assert.That(body.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(body.Focus()).IsTrue();
                await Assert.That(AutomationProperties.GetName(body)).IsEqualTo("Document decision details");
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
                await Assert.That(Required<TextBlock>(window, "DocumentActionDetails").Text).IsEqualTo(details);
                foreach (Button action in new[]
                {
                    Required<Button>(window, "AcceptDocumentActionButton"),
                    Required<Button>(window, "AlternateDocumentActionButton"), cancel
                }.Where(static button => button.IsVisible))
                {
                    Point origin = action.TranslatePoint(default, window) ??
                        throw new InvalidOperationException("Decision action has no window coordinates.");
                    await Assert.That(action.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(action.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(origin.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.X + action.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Y + action.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(window.Choice).IsEqualTo(ViewerDocumentActionChoice.Cancel);
                body.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(window.Choice).IsEqualTo(ViewerDocumentActionChoice.Cancel);
            }
            finally
            {
                window.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing decision control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
