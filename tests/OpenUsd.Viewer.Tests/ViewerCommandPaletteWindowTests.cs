// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCommandPaletteWindowTests
{
    [Test]
    public async Task KeyboardSelectionSkipsUnavailableCommandsAndRunsTheOriginalAction()
    {
        await RunUiAsync(async () =>
        {
            using var commands = new ViewerCommandDispatcher();
            var first = new Button();
            var disabled = new Button { IsEnabled = false };
            var last = new Button();
            int firstCalls = 0;
            int lastCalls = 0;
            first.Click += (_, _) => firstCalls++;
            last.Click += (_, _) => lastCalls++;
            commands.Register(ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenStage), first);
            commands.Register(ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenReview), disabled);
            commands.Register(ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenSample), last);
            var window = new CommandPaletteWindow(commands,
                _ => throw new InvalidOperationException("Unexpected error."));
            window.Show();
            try
            {
                TextBox search = Required<TextBox>(window, "CommandSearch");
                ListBox results = Required<ListBox>(window, "CommandResults");
                await Assert.That(search.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(results.ItemCount).IsEqualTo(3);
                ListBoxItem unavailable = (ListBoxItem)results.Items[1]!;
                await Assert.That(unavailable.IsEnabled).IsFalse();
                await Assert.That(AutomationProperties.GetHelpText(unavailable)).Contains("Unavailable");
                await Assert.That(AutomationProperties.GetName(unavailable)).IsEqualTo(
                    ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenReview).AccessibleName);
                await Assert.That(results.SelectedIndex).IsEqualTo(0);

                Press(search, Key.Down);
                await Assert.That(results.SelectedIndex).IsEqualTo(2);
                Press(search, Key.Up);
                await Assert.That(results.SelectedIndex).IsEqualTo(0);
                Press(search, Key.Down);
                Press(search, Key.Enter);
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(firstCalls).IsEqualTo(0);
                await Assert.That(lastCalls).IsEqualTo(1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Test]
    public async Task SearchReportsNoMatchesAndRefusesAnActionDisabledAfterSelection()
    {
        await RunUiAsync(async () =>
        {
            using var commands = new ViewerCommandDispatcher();
            var action = new Button();
            int calls = 0;
            action.Click += (_, _) => calls++;
            commands.Register(ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenStage), action);
            var window = new CommandPaletteWindow(commands,
                _ => throw new InvalidOperationException("Unexpected error."));
            window.Show();
            try
            {
                TextBox search = Required<TextBox>(window, "CommandSearch");
                ListBox results = Required<ListBox>(window, "CommandResults");
                Button run = Required<Button>(window, "RunCommandButton");
                search.Text = "no-matching-viewer-command";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(results.ItemCount).IsEqualTo(0);
                await Assert.That(run.IsEnabled).IsFalse();
                await Assert.That(Required<TextBlock>(window, "CommandSearchStatus").Text).StartsWith("No matching");
                Press(search, Key.Enter);
                await Assert.That(calls).IsEqualTo(0);
                await Assert.That(window.IsVisible).IsTrue();

                search.Text = "open";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(results.ItemCount).IsEqualTo(1);
                await Assert.That(run.IsEnabled).IsTrue();
                action.IsEnabled = false;
                Press(search, Key.Enter);
                await Assert.That(calls).IsEqualTo(0);
                await Assert.That(window.IsVisible).IsTrue();
                await Assert.That(run.IsEnabled).IsFalse();
                await Assert.That(((ListBoxItem)results.Items[0]!).IsEnabled).IsFalse();
                action.IsEnabled = true;
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => run.IsEnabled);
                await Assert.That(results.SelectedIndex).IsEqualTo(0);
                Press(search, Key.Escape);
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(calls).IsEqualTo(0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MinimumPaletteKeepsSearchResultsAndActionsVisibleInBothThemes(bool dark)
    {
        await RunUiAsync(async () =>
        {
            using var commands = new ViewerCommandDispatcher();
            commands.Register(ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenStage), new Button());
            commands.Register(ViewerCommandCatalog.Get(ViewerCommandIds.FileOpenReview), new Button());
            var window = new CommandPaletteWindow(commands, _ => { })
            {
                Width = 400,
                Height = 280,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                foreach (Control control in new Control[]
                {
                    Required<TextBox>(window, "CommandSearch"), Required<ListBox>(window, "CommandResults"),
                    Required<Button>(window, "RunCommandButton"), Required<Button>(window, "ClosePaletteButton")
                })
                {
                    Point? origin = control.TranslatePoint(default, window);
                    await Assert.That(origin).IsNotNull();
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(control.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(origin!.Value.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.X + control.Bounds.Width)
                        .IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Value.Y + control.Bounds.Height)
                        .IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                await Assert.That(Required<TextBox>(window, "CommandSearch").IsKeyboardFocusWithin).IsTrue();
                Press(Required<TextBox>(window, "CommandSearch"), Key.Escape);
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Press(Control target, Key key) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing palette control: {name}");

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

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
