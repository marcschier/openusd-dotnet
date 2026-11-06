// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerBridgeConnectionWindowTests
{
    private static readonly string[] ActionNames =
        ["BridgeConnectButton", "BridgeDisconnectButton", "BridgeResyncButton", "BridgeCloseButton"];
    private static readonly string[] Commands = ["Connect", "Disconnect", "Resync"];

    [Test]
    [Arguments(false, "success", "owned")]
    [Arguments(true, "success", "owned")]
    [Arguments(false, "success", "body")]
    [Arguments(true, "success", "body")]
    [Arguments(false, "success", "other")]
    [Arguments(true, "success", "other")]
    [Arguments(false, "success", "closed")]
    [Arguments(true, "success", "closed")]
    [Arguments(false, "success", "selector")]
    [Arguments(true, "success", "selector")]
    [Arguments(false, "failure", "owned")]
    [Arguments(true, "failure", "owned")]
    [Arguments(false, "failure", "body")]
    [Arguments(true, "failure", "body")]
    [Arguments(false, "failure", "other")]
    [Arguments(true, "failure", "other")]
    [Arguments(false, "failure", "closed")]
    [Arguments(true, "failure", "closed")]
    [Arguments(false, "cancel", "owned")]
    [Arguments(true, "cancel", "owned")]
    [Arguments(false, "cancel", "body")]
    [Arguments(true, "cancel", "body")]
    [Arguments(false, "cancel", "other")]
    [Arguments(true, "cancel", "other")]
    [Arguments(false, "cancel", "closed")]
    [Arguments(true, "cancel", "closed")]
    public async Task DeferredBridgeCommandsKeepCloseReachableAndRestoreOnlyOwnedFocus(
        bool dark, string outcome, string destination)
    {
        await RunUiAsync(async () =>
        {
            foreach (string command in Commands)
            {
                var provider = new DeferredProvider(command, outcome);
                BridgeConnectionWindow? window = null;
                bool publishedBusy = false;
                using var model = new ViewerBridgeConnectionModel(provider,
                    action => Dispatcher.UIThread.Post(action), state =>
                    {
                        publishedBusy = state.Busy;
                        window?.Apply(state);
                    });
                window = new BridgeConnectionWindow(model)
                {
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
                };
                var otherAction = new Button { Content = "Other window action" };
                var other = new Window { Content = otherAction, Width = 240, Height = 160 };
                window.Show();
                try
                {
                    Button action = Required<Button>(window, $"Bridge{command}Button");
                    Button close = Required<Button>(window, "BridgeCloseButton");
                    ScrollViewer body = Required<ScrollViewer>(window, "BridgeBodyScroll");
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                    await Assert.That(action.IsEnabled).IsTrue();
                    Control initiator = destination == "selector"
                        ? Required<ComboBox>(window, "BridgeSessionSelector") : action;
                    await Assert.That(initiator.Focus()).IsTrue();
                    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitUntilAsync(() => publishedBusy && !action.IsEnabled);
                    await Assert.That(close.IsKeyboardFocusWithin).IsTrue();
                    await Assert.That(Required<ComboBox>(window, "BridgeSessionSelector").IsEnabled).IsFalse();
                    if (destination == "body")
                    {
                        await Assert.That(body.Focus()).IsTrue();
                    }
                    else if (destination is "other" or "closed")
                    {
                        if (destination == "closed")
                        {
                            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                            await Assert.That(window.IsVisible).IsFalse();
                            await Assert.That(model.CurrentState.Busy).IsTrue();
                            await Assert.That(provider.LastToken.IsCancellationRequested).IsFalse();
                        }
                        other.Show();
                        await Assert.That(otherAction.Focus()).IsTrue();
                    }
                    provider.Release.TrySetResult();
                    await WaitUntilAsync(() => !model.CurrentState.Busy && !publishedBusy);
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                    string restored = outcome == "success" && command == "Connect" ? "Disconnect" :
                        outcome == "success" && command == "Disconnect" ? "Connect" : command;
                    Control expected = destination == "body" ? body :
                        destination is "other" or "closed" ? otherAction :
                        destination == "selector" ? initiator : Required<Button>(window, $"Bridge{restored}Button");
                    await Assert.That(expected.IsKeyboardFocusWithin).IsTrue();
                    await Assert.That(provider.Calls).IsEqualTo(1);
                    await Assert.That(provider.LastOperation).IsEqualTo(command);
                    await Assert.That(provider.LastSessionId)
                        .IsEqualTo(command == "Connect" ? "selected-session" : null);
                    await Assert.That(provider.LastToken.IsCancellationRequested).IsFalse();
                    await Assert.That(Required<ComboBox>(window, "BridgeSessionSelector").SelectedIndex).IsEqualTo(0);
                    await Assert.That(window.ActualThemeVariant)
                        .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                    if (outcome == "success")
                    {
                        await Assert.That(model.CurrentState.ErrorMessage).IsNull();
                        await Assert.That(model.CurrentState.Status.State).IsEqualTo(command == "Disconnect"
                            ? ViewerBridgeConnectionState.Disconnected : ViewerBridgeConnectionState.Streaming);
                    }
                    else
                    {
                        await Assert.That(Required<TextBlock>(window, "BridgeErrorText").Text)
                            .Contains(outcome == "cancel" ? "cancelled" : "failed");
                    }
                    if (window.IsVisible)
                    {
                        other.Close();
                        provider.Outcome = "success";
                        provider.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        Button retry = Required<Button>(window, $"Bridge{restored}Button");
                        await Assert.That(retry.Focus()).IsTrue();
                        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await WaitUntilAsync(() => publishedBusy && !retry.IsEnabled);
                        await Assert.That(close.IsKeyboardFocusWithin).IsTrue();
                        provider.Release.TrySetResult();
                        await WaitUntilAsync(() => !model.CurrentState.Busy && !publishedBusy);
                        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                        string next = restored == "Connect" ? "Disconnect" :
                            restored == "Disconnect" ? "Connect" : "Resync";
                        await Assert.That(Required<Button>(window, $"Bridge{next}Button").IsKeyboardFocusWithin)
                            .IsTrue();
                        await Assert.That(model.CurrentState.ErrorMessage).IsNull();
                        await Assert.That(provider.Calls).IsEqualTo(2);
                        await Assert.That(provider.LastOperation).IsEqualTo(restored);
                    }
                }
                finally
                {
                    provider.Release.TrySetResult();
                    await WaitUntilAsync(() => !model.CurrentState.Busy);
                    other.Close();
                    window.Close();
                }
            }
        });
    }

    [Test]
    [Arguments(false, "body")]
    [Arguments(true, "body")]
    [Arguments(false, "other")]
    [Arguments(true, "other")]
    [Arguments(false, "close")]
    [Arguments(true, "close")]
    [Arguments(false, "closed")]
    [Arguments(true, "closed")]
    public async Task BridgeBusyUpdatesPreserveUnownedFocusAndDoNotCancelOnClose(bool dark, string destination)
    {
        await RunUiAsync(async () =>
        {
            var provider = new DeferredProvider("Connect", "success");
            BridgeConnectionWindow? window = null;
            bool publishedBusy = false;
            using var model = new ViewerBridgeConnectionModel(provider,
                action => Dispatcher.UIThread.Post(action), state =>
                {
                    publishedBusy = state.Busy;
                    window?.Apply(state);
                });
            window = new BridgeConnectionWindow(model)
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var otherAction = new Button { Content = "Other window action" };
            var other = new Window { Content = otherAction, Width = 240, Height = 160 };
            window.Show();
            try
            {
                Control expected = destination == "body" ? Required<ScrollViewer>(window, "BridgeBodyScroll") :
                    destination == "close" ? Required<Button>(window, "BridgeCloseButton") : otherAction;
                if (destination is "other" or "closed")
                {
                    other.Show();
                }
                await Assert.That(expected.Focus()).IsTrue();
                Required<Button>(window, "BridgeConnectButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => publishedBusy);
                await Assert.That(expected.IsKeyboardFocusWithin).IsTrue();
                if (destination == "closed")
                {
                    window.Close();
                    await Assert.That(window.IsVisible).IsFalse();
                    await Assert.That(provider.LastToken.IsCancellationRequested).IsFalse();
                    await Assert.That(model.CurrentState.Busy).IsTrue();
                    await Assert.That(expected.Focus()).IsTrue();
                }
                provider.Release.TrySetResult();
                await WaitUntilAsync(() => !model.CurrentState.Busy && !publishedBusy);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                await Assert.That(expected.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(provider.Calls).IsEqualTo(1);
                await Assert.That(provider.LastToken.IsCancellationRequested).IsFalse();
                await Assert.That(model.CurrentState.Status.State).IsEqualTo(ViewerBridgeConnectionState.Streaming);
            }
            finally
            {
                provider.Release.TrySetResult();
                await WaitUntilAsync(() => !model.CurrentState.Busy);
                other.Close();
                window.Close();
            }
        });
    }

    [Test]
    [Arguments(false, "success")]
    [Arguments(true, "success")]
    [Arguments(false, "failure")]
    [Arguments(true, "failure")]
    [Arguments(false, "cancel")]
    [Arguments(true, "cancel")]
    public async Task ImmediatelyCompletedBridgeCommandsKeepAnEnabledActionFocused(bool dark, string outcome)
    {
        await RunUiAsync(async () =>
        {
            foreach (string command in Commands)
            {
                var provider = new DeferredProvider(command, outcome);
                provider.Release.SetResult();
                BridgeConnectionWindow? window = null;
                using var model = new ViewerBridgeConnectionModel(provider,
                    action => Dispatcher.UIThread.Post(action), state => window?.Apply(state));
                window = new BridgeConnectionWindow(model)
                {
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
                };
                window.Show();
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                    Button action = Required<Button>(window, $"Bridge{command}Button");
                    await Assert.That(action.Focus()).IsTrue();
                    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitUntilAsync(() => provider.Calls == 1 && !model.CurrentState.Busy);
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                    string next = outcome == "success" && command == "Connect" ? "Disconnect" :
                        outcome == "success" && command == "Disconnect" ? "Connect" : command;
                    Button expected = Required<Button>(window, $"Bridge{next}Button");
                    await Assert.That(expected.IsEnabled).IsTrue();
                    await Assert.That(expected.IsKeyboardFocusWithin).IsTrue();
                    await Assert.That(Required<Button>(window, "BridgeCloseButton").IsKeyboardFocusWithin).IsFalse();
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

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
                for (int page = 0; page < 64 && body.Offset.Y > 0; page++)
                {
                    window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(0);
                for (int page = 0; page < 64 && body.Offset.Y < body.Extent.Height - body.Viewport.Height; page++)
                {
                    window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(body.Extent.Height - body.Viewport.Height);
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

    private static async Task RunUiAsync(Func<Task> action)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            await action();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The bridge command did not reach the expected state.");
            }
            await Task.Delay(20);
        }
    }

    private sealed class DeferredProvider(string command, string outcome) : IViewerBridgeConnectionProvider
    {
        public string DisplayName => "Deferred bridge";
        public bool IsAvailable => true;
        public TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Outcome { get; set; } = outcome;
        public int Calls { get; private set; }
        public string? LastOperation { get; private set; }
        public string? LastSessionId { get; private set; }
        public CancellationToken LastToken { get; private set; }
        private ViewerBridgeStatus _status = command == "Connect" ? ViewerBridgeStatus.Disconnected :
            ViewerBridgeStatus.Disconnected with { State = ViewerBridgeConnectionState.Streaming };

        public event EventHandler<ViewerBridgeStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public ViewerBridgeStatus GetStatus() => _status;

        public ValueTask<IReadOnlyList<ViewerBridgeSession>> GetSessionsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ViewerBridgeSession>>(
                [new ViewerBridgeSession("selected-session", "Selected session")]);

        public ValueTask ConnectAsync(ViewerBridgeConnectRequest request, CancellationToken cancellationToken = default)
        {
            LastSessionId = request.SessionId;
            return ExecuteAsync("Connect", cancellationToken);
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) =>
            ExecuteAsync("Disconnect", cancellationToken);

        public ValueTask ResyncAsync(CancellationToken cancellationToken = default) =>
            ExecuteAsync("Resync", cancellationToken);

        private async ValueTask ExecuteAsync(string operation, CancellationToken cancellationToken)
        {
            Calls++;
            LastOperation = operation;
            LastToken = cancellationToken;
            await Release.Task;
            if (Outcome == "failure")
            {
                throw new IOException("Controlled bridge refusal.");
            }
            if (Outcome == "cancel")
            {
                throw new OperationCanceledException("Controlled bridge cancellation.");
            }
            _status = operation == "Disconnect" ? ViewerBridgeStatus.Disconnected :
                _status with { State = ViewerBridgeConnectionState.Streaming };
        }
    }

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
