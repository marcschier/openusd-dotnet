// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerBridgeConnectionWindowTests
{
    private static readonly string[] ActionNames =
        ["BridgeConnectButton", "BridgeDisconnectButton", "BridgeResyncButton", "BridgeCloseButton"];

    [Test]
    [Arguments(420, 300, false)]
    [Arguments(420, 300, true)]
    [Arguments(520, 360, false)]
    public async Task CompactBridgeKeepsActionsAndFullRefusalKeyboardReachable(
        int width, int height, bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            using var model = new ViewerBridgeConnectionModel(null, action => action(), _ => { });
            var window = new BridgeConnectionWindow(model)
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                string message = string.Join(' ',
                    Enumerable.Repeat("The requested bridge session is unavailable.", 10));
                window.Apply(ViewerBridgeViewState.Absent with
                {
                    StatusText = message,
                    ErrorMessage = message,
                    CanConnect = true,
                    CanDisconnect = true,
                    CanResync = true
                });
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                foreach (string name in ActionNames)
                {
                    Button button = Required<Button>(window, name);
                    await AssertInsideAsync(button, window, window.ClientSize);
                    await Assert.That(button.Focus()).IsTrue();
                    await Assert.That(button.IsKeyboardFocusWithin).IsTrue();
                }
                TextBlock error = Required<TextBlock>(window, "BridgeErrorText");
                await Assert.That(error.Text).IsEqualTo(message);
                await Assert.That(Required<TextBlock>(window, "BridgeStatusText").Text).IsEqualTo(message);
                error.BringIntoView();
                window.UpdateLayout();
                Point? errorOrigin = error.TranslatePoint(default, window);
                await Assert.That(errorOrigin).IsNotNull();
                await Assert.That(errorOrigin!.Value.Y).IsLessThan(window.ClientSize.Height);
                ScrollViewer body = Required<ScrollViewer>(window, "BridgeBodyScroll");
                await Assert.That(body.Focus()).IsTrue();
                window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                window.UpdateLayout();
                await Assert.That(body.Offset.Y).IsGreaterThan(0);
                Point? origin = error.TranslatePoint(default, body);
                await Assert.That(origin).IsNotNull();
                await Assert.That(origin!.Value.Y + error.Bounds.Height).IsGreaterThan(0);
                await Assert.That(origin.Value.Y + error.Bounds.Height).IsLessThanOrEqualTo(body.Viewport.Height);
                await Assert.That(error.Bounds.Width).IsLessThanOrEqualTo(body.Viewport.Width);
                window.Apply(ViewerBridgeViewState.Absent with { Busy = true });
                await Assert.That(Required<ComboBox>(window, "BridgeSessionSelector").IsEnabled).IsFalse();
                await Assert.That(Required<Button>(window, "BridgeConnectButton").IsEnabled).IsFalse();
                Button close = Required<Button>(window, "BridgeCloseButton");
                await Assert.That(close.IsEnabled).IsTrue();
                close.Focus();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(model.CurrentState).IsEqualTo(ViewerBridgeViewState.Absent);
            }
            finally
            {
                window.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static async Task AssertInsideAsync(Control control, Visual relativeTo, Size size)
    {
        Point? origin = control.TranslatePoint(default, relativeTo);
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.Value.X).IsGreaterThanOrEqualTo(0);
        await Assert.That(origin.Value.Y).IsGreaterThanOrEqualTo(0);
        await Assert.That(origin.Value.X + control.Bounds.Width).IsLessThanOrEqualTo(size.Width);
        await Assert.That(origin.Value.Y + control.Bounds.Height).IsLessThanOrEqualTo(size.Height);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing bridge control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
