// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerDocumentNativeSmokeTests
{
    [Test]
    public async Task OwnedPropertyWindowAuthorsAnExactReviewEditAndCloseKeepsTheSharedHistory()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_DOCUMENT_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_DOCUMENT_SMOKE=1 on a Windows desktop.");
        }
        string root = Path.Combine(AppContext.BaseDirectory, "document-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        const string source = "#usda 1.0\ndef Xform \"Body\" {\n double3 xformOp:translate = (1, 2, 3)\n}\n";
        await File.WriteAllTextAsync(path, source);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var owner = new Window { Width = 900, Height = 600, RequestedThemeVariant = ThemeVariant.Dark };
                    try
                    {
                        owner.Show();
                        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
                        await using var editor = new ViewerAuthoredEditController(scheduler);
                        var property = new PropertyEditWindow(editor, "/Body",
                        [
                            new("xformOp:translate", "double3", true, false, 0, string.Empty, "(1, 2, 3)")
                        ], 0);
                        try
                        {
                            property.Show(owner);
                            await WaitUntilAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled);
                            await Assert.That(property.Owner).IsSameReferenceAs(owner);
                            await Assert.That(property.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
                            await Assert.That(Required<TextBox>(property, "PropertyValue").IsKeyboardFocusWithin)
                                .IsTrue();
                            await Assert.That(Required<TextBlock>(property, "PropertyTargetState").Text)
                                .Contains("Absent");
                            Required<TextBox>(property, "PropertyValue").Text = "(4, 5, 6)";
                            Required<Button>(property, "SetPropertyButton")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await WaitUntilAsync(() => editor.CanUndo);
                            await Assert.That(editor.UndoDepth).IsEqualTo(1);
                            await property.CloseAsync();
                            await Assert.That((await editor.UndoAsync()).After!.Opinions[0].PropertyKind)
                                .IsEqualTo(OpenUsd.Editing.UsdLayerPropertyKind.Absent);
                            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
                            await ExerciseMainWindowAsync(Path.Combine(root, "main"), path, source);
                            completed.TrySetResult();
                        }
                        finally
                        {
                            await property.CloseAsync();
                        }
                    }
                    catch (Exception exception)
                    {
                        completed.TrySetException(exception);
                    }
                    finally
                    {
                        owner.Close();
                        lifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(lifetime.Token);
            }
            catch (Exception exception)
            {
                completed.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer document native smoke"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(110));
        }
        finally
        {
            lifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
            if (stopped)
            {
                Directory.Delete(root, recursive: true);
            }
        }
        await Assert.That(stopped).IsTrue();
    }

    private static async Task ExerciseMainWindowAsync(string root, string path, string source)
    {
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(root);
        await store.SaveAsync(ViewerSettings.Default with { RendererPreference = "Vulkan" });
        var recent = new RecentStageStore(root);
        await recent.AddAsync(path);
        var main = new MainWindow(recent, store);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            await WaitUntilAsync(() => Required<StackPanel>(main, "WelcomeRecentItems").Children.Count != 0);
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsFalse();
            Required<StackPanel>(main, "WelcomeRecentItems").Children.OfType<Button>().Single()
                .Command!.Execute(null);
            await WaitUntilAsync(() => opened.Task.IsCompleted ||
                main.OwnedWindows.OfType<DocumentActionWindow>().Any());
            if (main.OwnedWindows.OfType<DocumentActionWindow>().FirstOrDefault() is { } origin)
            {
                await Assert.That(Required<TextBlock>(origin, "DocumentActionHeading").Text)
                    .IsEqualTo("Verified review opening was refused");
                Required<Button>(origin, "AcceptDocumentActionButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            ViewerStageSession session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30));
            TreeView hierarchy = Required<TreeView>(main, "StageHierarchy");
            hierarchy.SelectedItem = hierarchy.Items.OfType<TreeViewItem>().Single();
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled);
            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window is PropertyEditWindow));
            var property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitUntilAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled ||
                Required<TextBlock>(property, "PropertyEditStatus").Text?.Contains(
                    "unavailable", StringComparison.Ordinal) == true);
            await Assert.That(Required<Button>(property, "SetPropertyButton").IsEnabled).IsTrue()
                .Because(Required<TextBlock>(property, "PropertyEditStatus").Text ?? "No editor status");
            Required<TextBox>(property, "PropertyValue").Text = "(8, 9, 10)";
            Required<Button>(property, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled);
            await property.CloseAsync();
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled);
            UsdLayerPropertyKind actual = await session.Scheduler.InvokeAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CaptureAuthored(
                    [new("/Body.xformOp:translate", UsdLayerEditField.Default)]).Opinions[0].PropertyKind;
            });
            await Assert.That(actual).IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That(Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled).IsFalse()
                .Because("the already-open legacy source has no verified origin and remains explicitly session-only");

            main.Close();
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window.Title == "Unsaved document changes"));
            Window prompt = main.OwnedWindows.Single(window => window.Title == "Unsaved document changes");
            Required<Button>(prompt, "CancelDocumentTransitionButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !prompt.IsVisible);
            await Assert.That(main.IsVisible).IsTrue();
            await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.HasPrim("/Body"))).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);

            Required<MenuItem>(main, "ReloadStageMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window.Title == "Unsaved document changes"));
            prompt = main.OwnedWindows.Single(window => window.Title == "Unsaved document changes");
            Required<Button>(prompt, "CancelDocumentTransitionButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !prompt.IsVisible && Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.HasPrim("/Body"))).IsTrue();

            Required<MenuItem>(main, "OpenSampleMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window.Title == "Unsaved document changes"));
            prompt = main.OwnedWindows.Single(window => window.Title == "Unsaved document changes");
            Required<Button>(prompt, "CancelDocumentTransitionButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !prompt.IsVisible && Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled);
            await Assert.That(Required<TextBlock>(main, "StageStatus").Text).IsEqualTo("source.usda");
            await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.HasPrim("/Body"))).IsTrue();

            Required<MenuItem>(main, "CommandPaletteMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window.Title == "Search commands"));
            Window palette = main.OwnedWindows.Single(window => window.Title == "Search commands");
            Required<TextBox>(palette, "CommandSearch").Text = "edit.redo";
            await Task.Delay(50);
            palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled);
            await Assert.That(await ReadTranslationAsync(session)).IsEqualTo(new UsdVec3d(8, 9, 10));

            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window is PropertyEditWindow));
            property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitUntilAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled);
            Required<TextBox>(property, "PropertyValue").Text = "(12, 13, 14)";
            Required<Button>(property, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() =>
                Required<TextBlock>(property, "PropertyEditStatus").Text == "Review edit applied.");
            await property.CloseAsync();
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled);
            await Assert.That(Required<Button>(main, "FrameSelectedButton").Focus()).IsTrue();
            for (int repeat = 0; repeat < 2; repeat++)
            {
                main.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Z,
                    KeyModifiers = KeyModifiers.Control
                });
            }
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled);
            main.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = Key.Z,
                KeyModifiers = KeyModifiers.Control
            });
            await Assert.That(await ReadTranslationAsync(session)).IsEqualTo(new UsdVec3d(8, 9, 10))
                .Because("one held shortcut must undo only one review step");
            Required<TextBox>(main, "HierarchyFilter").Focus();
            main.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Z,
                KeyModifiers = KeyModifiers.Control
            });
            await Task.Delay(75);
            await Assert.That(await ReadTranslationAsync(session)).IsEqualTo(new UsdVec3d(8, 9, 10))
                .Because("text editing must retain its own undo shortcut");
        }
        finally
        {
            foreach (PropertyEditWindow property in main.OwnedWindows.OfType<PropertyEditWindow>().ToArray())
            {
                await property.CloseAsync();
            }
            main.Close();
            for (int attempt = 0; attempt < 150 && !closed.Task.IsCompleted; attempt++)
            {
                Window? prompt = main.OwnedWindows.FirstOrDefault(
                    window => window.Title == "Unsaved document changes");
                prompt?.FindControl<Button>("DiscardDocumentChangesButton")?
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (DocumentActionWindow action in main.OwnedWindows.OfType<DocumentActionWindow>().ToArray())
                {
                    action.FindControl<Button>("CancelDocumentActionButton")?
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await Task.Delay(100);
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ViewerStartupOptions.Initialize([]);
        }
        using var reopenedStore = new ViewerSettingsStore(root);
        await Assert.That((await reopenedStore.LoadAsync()).Settings.RendererPreference).IsEqualTo("Vulkan");
    }

    private static T Required<T>(Control control, string name) where T : Control =>
        control.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control '{name}'.");

    private static ValueTask<UsdVec3d> ReadTranslationAsync(ViewerStageSession session) =>
        session.Scheduler.InvokeAsync(stage =>
        {
            using UsdLayer review = stage.GetUserReviewLayer();
            return review.CaptureAuthored(
                [new("/Body.xformOp:translate", UsdLayerEditField.Default)]).Opinions[0].Value.AsVec3d();
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(25, timeout.Token);
        }
    }
}
