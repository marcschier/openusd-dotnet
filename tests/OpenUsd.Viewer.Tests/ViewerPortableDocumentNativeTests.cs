// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPortableDocumentNativeTests
{
    [Test]
    public async Task AVerifiedSourceCanBeEditedSavedAndClosedThroughTheActualMenus()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PORTABLE_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_PORTABLE_SMOKE=1 on a Windows desktop.");
        }
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var dispatcherLifetime = new CancellationTokenSource();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseAsync(files, lifetime.Token);
                        await ExerciseRecoveryAsync(files, lifetime.Token);
                        completed.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completed.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        dispatcherLifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(dispatcherLifetime.Token);
            }
            catch (Exception exception)
            {
                completed.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer portable document native test"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(50));
        }
        finally
        {
            lifetime.Cancel();
            dispatcherLifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
        }
        await Assert.That(stopped).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }

    private static async Task ExerciseAsync(ViewerPortableReviewFixture files, CancellationToken timeout)
    {
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = files.SourcePath,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                Volatile.Read(ref opened).TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        var picker = new FixedPicker(files.DestinationPath);
        string settingsRoot = Path.Combine(files.Root, "settings");
        using var settings = new ViewerSettingsStore(settingsRoot);
        var main = new MainWindow(new RecentStageStore(settingsRoot), settings, picker);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            UsdReviewSourceBinding binding = await session.Scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding(), timeout);
            await Assert.That(Path.GetFullPath(binding.SourceRootPath)).IsEqualTo(files.SourcePath);
            TreeView hierarchy = Required<TreeView>(main, "StageHierarchy");
            hierarchy.SelectedItem = hierarchy.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Body" });
            await WaitAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            ComboBox selector = Required<ComboBox>(property, "PropertySelector");
            await WaitAsync(() => selector.IsEnabled, timeout);
            selector.SelectedItem = selector.Items.OfType<string>()
                .Single(static item => item.StartsWith("review:value (", StringComparison.Ordinal));
            await WaitAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled, timeout);
            Required<TextBox>(property, "PropertyValue").Text = "47";
            Required<Button>(property, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(property, "PropertyEditStatus").Text == "Review edit applied.",
                timeout);
            await property.CloseAsync();
            using var recovery = new ViewerRecoveryStore(Path.Combine(settingsRoot, "review-recovery"));
            string sourceRecovery = recovery.GetCheckpointPath(files.SourcePath.ToUpperInvariant());
            await WaitAsync(() => File.Exists(sourceRecovery), timeout);
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "SaveReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(main, "ViewerStatus").Text == "Review saved.", timeout);
            await Assert.That(picker.SaveCalls).IsEqualTo(1);
            await Assert.That(File.Exists(files.DestinationPath)).IsTrue();
            await Assert.That(File.Exists(sourceRecovery)).IsFalse();
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.GetEditingState().IsDirty;
            }, timeout)).IsFalse();
            main.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.S,
                KeyModifiers = KeyModifiers.Control
            });
            main.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = Key.S,
                KeyModifiers = KeyModifiers.Control
            });
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            await Assert.That(picker.SaveCalls).IsEqualTo(1);
            string copyDirectory = Path.Combine(files.Root, "elsewhere");
            Directory.CreateDirectory(copyDirectory);
            string copyPath = Path.Combine(copyDirectory, "copy.urd");
            picker.NextSavePath = copyPath;
            Required<MenuItem>(main, "SaveReviewAsMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(main, "StageStatus").Text == "copy.urd" &&
                Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            UsdReviewDocument copied = UsdReviewDocument.Read(
                await File.ReadAllBytesAsync(copyPath, timeout), files.SourcePath);
            await Assert.That(copied.AssetAnchor).IsEqualTo(binding.AssetAnchor);
            await Assert.That(picker.SaveCalls).IsEqualTo(2);
            picker.NextSavePath = null;
            Required<MenuItem>(main, "SaveReviewAsMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(main, "ViewerStatus").Text?.Contains(
                "cancelled", StringComparison.OrdinalIgnoreCase) == true, timeout);
            await Assert.That(Required<TextBlock>(main, "StageStatus").Text).IsEqualTo("copy.urd");
            string invalidReview = Path.Combine(files.Root, "invalid.urd");
            await File.WriteAllTextAsync(invalidReview, "not a native review document", timeout);
            picker.NextOpenPath = invalidReview;
            Required<MenuItem>(main, "OpenReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(main, "ViewerStatus").Text?.StartsWith(
                "Error: Could not open", StringComparison.Ordinal) == true, timeout);
            await Assert.That(Required<TextBlock>(main, "StageStatus").Text).IsEqualTo("copy.urd");
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(47d);
            picker.NextOpenPath = files.DestinationPath;
            await session.Scheduler.EditAsync(static stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                var address = new UsdLayerEditAddress("/Body.review:foreign", UsdLayerEditField.Default);
                return review.CompareAndApply(review.CaptureAuthored([address]),
                    [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(77), "double")]);
            }, UsdStageInvalidationKind.Property, timeout);
            string hostRecovery = recovery.GetCheckpointPath(copyPath.ToUpperInvariant());
            await WaitAsync(() => File.Exists(hostRecovery), timeout);
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "SaveReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled &&
                Required<TextBlock>(main, "ViewerStatus").Text == "Review saved.", timeout);
            await Assert.That(File.Exists(hostRecovery)).IsFalse();
            await EditValueAsync(main, "48", timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(48d);
            opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.NextOpenPath = copyPath;
            Required<MenuItem>(main, "OpenReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
            DocumentChangesWindow saveBeforeOpen = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
            Required<Button>(saveBeforeOpen, "SaveDocumentReviewButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentActionWindow>().Any(), timeout);
            DocumentActionWindow reopenIntent = main.OwnedWindows.OfType<DocumentActionWindow>().Single();
            await Assert.That(Required<TextBlock>(reopenIntent, "DocumentActionDetails").Text).Contains(copyPath);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.GetEditingState().IsDirty;
            }, timeout)).IsFalse();
            Required<Button>(reopenIntent, "AcceptDocumentActionButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session = await opened.Task.WaitAsync(timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(48d);
            picker.NextOpenPath = files.DestinationPath;
            opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Required<MenuItem>(main, "OpenReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentActionWindow>().Any(), timeout);
            DocumentActionWindow sourceIntent = main.OwnedWindows.OfType<DocumentActionWindow>().Single();
            await Assert.That(Required<TextBlock>(sourceIntent, "DocumentActionDetails").Text)
                .Contains(files.SourcePath);
            Required<Button>(sourceIntent, "AcceptDocumentActionButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewerStageSession reopened = await opened.Task.WaitAsync(timeout);
            await Assert.That(Required<TextBlock>(main, "StageStatus").Text).IsEqualTo("review.urd");
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsFalse();
            await Assert.That(await reopened.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(47d);
            await Assert.That(await reopened.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer root = stage.GetRootLayer();
                return root.CaptureAuthored(
                    [new("/Material/Texture.inputs:file", UsdLayerEditField.Default)])
                    .Opinions[0].Value.AsAssetPath().AuthoredPath;
            }, timeout)).IsEqualTo("texture.png");
            await Assert.That(Required<MenuItem>(main, "SaveSourceMenuItem").IsEnabled).IsFalse();
            await EditValueAsync(main, "55", timeout);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await Assert.That(Required<MenuItem>(main, "RevertReviewMenuItem").IsEnabled).IsTrue();
            opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Required<MenuItem>(main, "RevertReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
            DocumentChangesWindow revert = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
            Required<Button>(revert, "CancelDocumentTransitionButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !revert.IsVisible, timeout);
            await Assert.That(await reopened.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(55d);
            Required<MenuItem>(main, "RevertReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
            revert = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
            Required<Button>(revert, "DiscardDocumentChangesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewerStageSession reverted = await opened.Task.WaitAsync(timeout);
            await Assert.That(await reverted.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(47d);
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsFalse();
            await EditValueAsync(main, "66", timeout);
            main.Close();
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
            DocumentChangesWindow closeSave = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
            await Assert.That(Required<Button>(closeSave, "SaveDocumentReviewButton").IsEnabled).IsTrue();
            Required<Button>(closeSave, "SaveDocumentReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.Task.WaitAsync(timeout);
            UsdReviewDocument saved = UsdReviewDocument.Read(
                await File.ReadAllBytesAsync(files.DestinationPath, timeout), files.SourcePath);
            await Assert.That(saved.SourceFingerprint).IsEqualTo(binding.SourceFingerprint);
            await using ViewerPreparedDocument final = await ViewerPreparedDocument.OpenReviewAsync(
                saved, files.SourcePath, null, recovery: false, timeout);
            await Assert.That(await final.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(66d);
        }
        finally
        {
            main.Close();
            for (int attempt = 0; attempt < 100 && !closed.Task.IsCompleted; attempt++)
            {
                foreach (Window window in main.OwnedWindows.ToArray())
                {
                    window.FindControl<Button>("DiscardDocumentChangesButton")?
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.FindControl<Button>("CancelDocumentActionButton")?
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await Task.Delay(50, CancellationToken.None);
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control '{name}'.");

    private static async Task ExerciseRecoveryAsync(ViewerPortableReviewFixture files, CancellationToken timeout)
    {
        string settingsRoot = Path.Combine(files.Root, "recovery-settings");
        string cacheRoot = Path.Combine(settingsRoot, "review-recovery");
        Directory.CreateDirectory(cacheRoot);
        using var cache = new ViewerRecoveryStore(cacheRoot);
        string key = files.SourcePath.ToUpperInvariant();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding(), timeout);
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            await editor.ApplyAsync(await editor.CaptureAsync([address], timeout),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(73), "double")], "Recovered",
                cancellationToken: timeout);
            UsdReviewDocument native = await editor.CaptureRecoveryDocumentAsync(
                cache.GetCheckpointPath(key), retiring: false, timeout);
            await cache.SaveVerifiedAsync(ViewerDocumentRecovery.FromNative(native, key), timeout);
        }
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = files.SourcePath,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) => { opened.TrySetResult(session); return Task.CompletedTask; }
        });
        using var settings = new ViewerSettingsStore(settingsRoot);
        var recent = new RecentStageStore(settingsRoot);
        await recent.AddAsync(files.SourcePath, timeout);
        string recoveredPath = Path.Combine(files.Root, "recovered.urd");
        var main = new MainWindow(recent, settings, new FixedPicker(recoveredPath));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentActionWindow>().Any(), timeout);
            DocumentActionWindow prompt = main.OwnedWindows.OfType<DocumentActionWindow>().Single();
            await Assert.That(Required<TextBlock>(prompt, "DocumentActionHeading").Text)
                .IsEqualTo("Recover validated review changes?");
            Required<Button>(prompt, "CancelDocumentActionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !prompt.IsVisible, timeout);
            await Assert.That(opened.Task.IsCompleted).IsFalse();
            await Assert.That(File.Exists(cache.GetCheckpointPath(key))).IsTrue();
            Required<StackPanel>(main, "WelcomeRecentItems").Children.OfType<Button>().Single()
                .Command!.Execute(null);
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentActionWindow>().Any(), timeout);
            prompt = main.OwnedWindows.OfType<DocumentActionWindow>().Single();
            Required<Button>(prompt, "AcceptDocumentActionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(73d);
            await Assert.That(Required<TextBlock>(main, "DocumentEditStatus").Text).Contains("unsaved");
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsFalse();
            main.Close();
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
            DocumentChangesWindow changes = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
            Required<Button>(changes, "SaveDocumentReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.Task.WaitAsync(timeout);
            await Assert.That(File.Exists(recoveredPath)).IsTrue();
            await Assert.That(File.Exists(cache.GetCheckpointPath(key))).IsFalse();
        }
        finally
        {
            main.Close();
            for (int attempt = 0; attempt < 100 && !closed.Task.IsCompleted; attempt++)
            {
                foreach (Window window in main.OwnedWindows.ToArray())
                {
                    window.FindControl<Button>("CancelDocumentActionButton")?
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.FindControl<Button>("DiscardDocumentChangesButton")?
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await Task.Delay(50, CancellationToken.None);
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private static async Task WaitAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task EditValueAsync(MainWindow main, string value, CancellationToken timeout)
    {
        TreeView hierarchy = Required<TreeView>(main, "StageHierarchy");
        hierarchy.SelectedItem = hierarchy.Items.OfType<TreeViewItem>().Single(
            static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Body" });
        await WaitAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled, timeout);
        Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
        PropertyEditWindow property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
        ComboBox selector = Required<ComboBox>(property, "PropertySelector");
        await WaitAsync(() => selector.IsEnabled, timeout);
        selector.SelectedItem = selector.Items.OfType<string>()
            .Single(static item => item.StartsWith("review:value (", StringComparison.Ordinal));
        await WaitAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled, timeout);
        Required<TextBox>(property, "PropertyValue").Text = value;
        Required<Button>(property, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => Required<TextBlock>(property, "PropertyEditStatus").Text == "Review edit applied.",
            timeout);
        await property.CloseAsync();
    }

    private sealed class FixedPicker(string path) : IViewerDocumentFilePicker
    {
        internal int SaveCalls { get; private set; }
        internal string? NextSavePath { get; set; } = path;
        internal string? NextOpenPath { get; set; } = path;

        public Task<string?> SaveReviewAsync(Window owner, string suggestedPath)
        {
            SaveCalls++;
            return Task.FromResult(NextSavePath);
        }

        public Task<string?> OpenReviewAsync(Window owner) => Task.FromResult(NextOpenPath);
    }
}
