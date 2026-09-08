// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPortableTransitionNativeTests
{
    [Test]
    public async Task PortableTransitionsKeepUnrelatedOpinionsAndResumeRecoveryAfterKeptSave()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PORTABLE_TRANSITION_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_PORTABLE_TRANSITION_SMOKE=1 on a Windows desktop.");
        }
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
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
                        await ExerciseRevertAsync(files, timeout.Token);
                        await ExerciseKeptSaveRecoveryAsync(replace: false, refusedDestination: false, timeout.Token);
                        await ExerciseKeptSaveRecoveryAsync(replace: false, refusedDestination: true, timeout.Token);
                        await ExerciseKeptSaveRecoveryAsync(replace: true, refusedDestination: false, timeout.Token);
                        await ExerciseKeptSaveRecoveryAsync(replace: true, refusedDestination: true, timeout.Token);
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
            Name = "Viewer portable transition native test"
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
            timeout.Cancel();
            dispatcherLifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
        }
        await Assert.That(stopped).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }

    private static async Task ExerciseRevertAsync(
        ViewerPortableReviewFixture files, CancellationToken timeout)
    {
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replaced =
            new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        int openings = 0;
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = files.SourcePath,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                (Interlocked.Increment(ref openings) == 1 ? opened : replaced).TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        string settingsRoot = Path.Combine(files.Root, "transition-settings");
        using var settings = new ViewerSettingsStore(settingsRoot);
        var main = new MainWindow(
            new RecentStageStore(settingsRoot), settings, new FixedPicker(files.DestinationPath));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? blocker = null;
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled &&
                Required<MenuItem>(main, "ReloadStageMenuItem").IsEnabled, timeout);
            await EditValueAsync(main, "47", timeout);
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "SaveReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(main, "ViewerStatus").Text == "Review saved." &&
                Required<MenuItem>(main, "RevertReviewMenuItem").IsEnabled, timeout);

            var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            blocker = session.Scheduler.InvokeAsync(_ =>
            {
                blocked.TrySetResult();
                release.Task.GetAwaiter().GetResult();
            }, timeout).AsTask();
            await blocked.Task.WaitAsync(timeout);
            Required<MenuItem>(main, "RevertReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Task mutation = session.Scheduler.EditAsync(static stage =>
            {
                using UsdLayer container = stage.GetSessionLayer();
                container.SetMetadata("comment", "late unrelated session opinion");
            }, UsdStageInvalidationKind.Composition, timeout).AsTask();
            release.TrySetResult();
            await Task.WhenAll(blocker, mutation).WaitAsync(timeout);
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any() ||
                Required<TextBlock>(main, "ViewerStatus").Text?.Contains("kept", StringComparison.Ordinal) == true,
                timeout);
            if (main.OwnedWindows.OfType<DocumentChangesWindow>().FirstOrDefault() is { } prompt)
            {
                Required<Button>(prompt, "DiscardDocumentChangesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await WaitAsync(() => replaced.Task.IsCompleted ||
                Required<TextBlock>(main, "ViewerStatus").Text?.Contains("kept", StringComparison.Ordinal) == true,
                timeout);

            await Assert.That(replaced.Task.IsCompleted).IsFalse()
                .Because("Revert cannot admit unrelated dirtiness introduced during saved-candidate preparation");
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer container = stage.GetSessionLayer();
                return container.GetMetadataString("comment");
            }, timeout)).IsEqualTo("late unrelated session opinion");
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(47d);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await Assert.That(main.IsVisible).IsTrue();
        }
        finally
        {
            release.TrySetResult();
            if (blocker is not null)
            {
                await blocker.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            await CloseAsync(main, closed.Task);
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

    private static async Task ExerciseKeptSaveRecoveryAsync(
        bool replace, bool refusedDestination, CancellationToken timeout)
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replaced =
            new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        int openings = 0;
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = files.SourcePath,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                (Interlocked.Increment(ref openings) == 1 ? opened : replaced).TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        string settingsRoot = Path.Combine(files.Root, "kept-settings");
        using var settings = new ViewerSettingsStore(settingsRoot);
        var picker = new KeptSavePicker(refusedDestination ? files.SourcePath : null);
        var recent = new RecentStageStore(settingsRoot);
        string nextPath = Path.Combine(files.Root, "next.usda");
        await File.WriteAllTextAsync(nextPath, "#usda 1.0\ndef Xform \"Next\" {}\n", timeout);
        await recent.AddAsync(nextPath, timeout);
        var main = new MainWindow(recent, settings, picker);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        using var cache = new ViewerRecoveryStore(Path.Combine(settingsRoot, "review-recovery"));
        string key = files.SourcePath.ToUpperInvariant();
        string checkpointPath = cache.GetCheckpointPath(key);
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled &&
                Required<MenuItem>(main, "ReloadStageMenuItem").IsEnabled, timeout);
            await EditValueAsync(main, "47", timeout);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await Assert.That(File.Exists(checkpointPath)).IsFalse()
                .Because("the transition must interrupt the first pending debounce, not an existing checkpoint");
            if (replace)
            {
                ReplaceFromRecent(main, nextPath);
            }
            else
            {
                main.Close();
            }
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
            await Assert.That(File.Exists(checkpointPath)).IsFalse()
                .Because("the Save choice must still interrupt the first queued recovery checkpoint");
            DocumentChangesWindow prompt = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
            Required<Button>(prompt, "SaveDocumentReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => picker.SaveCalls == 1 &&
                !main.OwnedWindows.OfType<DocumentChangesWindow>().Any() &&
                Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            await Task.Delay(1600, timeout);

            await Assert.That(File.Exists(checkpointPath)).IsTrue()
                .Because("a kept dirty document must resume recovery without another authored edit");
            await Assert.That(closed.Task.IsCompleted).IsFalse();
            await Assert.That(replaced.Task.IsCompleted).IsFalse();
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsTrue();
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(47d);
            ViewerDocumentRecovery recovery = await cache.LoadAsync(key, timeout) ??
                throw new InvalidOperationException("The kept document has no readable recovery checkpoint.");
            UsdReviewDocument document = UsdReviewDocument.Read(recovery.CopyPayload(), files.SourcePath);
            await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
                document, files.SourcePath, null, recovery: true, timeout);
            await Assert.That(await prepared.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"), timeout)).IsEqualTo(47d);
            if (replace)
            {
                ReplaceFromRecent(main, nextPath);
                await WaitAsync(() => main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), timeout);
                DocumentChangesWindow discard = main.OwnedWindows.OfType<DocumentChangesWindow>().Single();
                Required<Button>(discard, "DiscardDocumentChangesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ViewerStageSession next = await replaced.Task.WaitAsync(timeout);
                await Assert.That(await next.Scheduler.InvokeAsync(
                    static stage => stage.HasPrim("/Next"), timeout)).IsTrue();
                await Assert.That(async () => await session.Scheduler.InvokeAsync(
                    static stage => stage.RootLayerIdentifier, CancellationToken.None))
                    .Throws<ObjectDisposedException>();
            }
            else
            {
                await CloseAsync(main, closed.Task);
            }
            await Task.Delay(1000, timeout);
            await Assert.That(File.Exists(checkpointPath)).IsFalse()
                .Because("a committed retirement must not restart the old document's queued recovery");
            await files.AssertOriginalsUnchangedAsync();
        }
        finally
        {
            await CloseAsync(main, closed.Task);
        }
    }

    private static void ReplaceFromRecent(MainWindow main, string path)
    {
        MenuItem item = Required<MenuItem>(main, "RecentStagesMenu").Items.OfType<MenuItem>()
            .Single(candidate => candidate.Tag is string value && value == path);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static async Task CloseAsync(MainWindow main, Task closed)
    {
        foreach (PropertyEditWindow property in main.OwnedWindows.OfType<PropertyEditWindow>().ToArray())
        {
            await property.CloseAsync();
        }
        main.Close();
        for (int attempt = 0; attempt < 100 && !closed.IsCompleted; attempt++)
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
        await closed.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control '{name}'.");

    private static async Task WaitAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    private sealed class FixedPicker(string path) : IViewerDocumentFilePicker
    {
        public Task<string?> SaveReviewAsync(Window owner, string suggestedPath) => Task.FromResult<string?>(path);
        public Task<string?> OpenReviewAsync(Window owner) => Task.FromResult<string?>(path);
    }

    private sealed class KeptSavePicker(string? path) : IViewerDocumentFilePicker
    {
        internal int SaveCalls { get; private set; }

        public Task<string?> SaveReviewAsync(Window owner, string suggestedPath)
        {
            SaveCalls++;
            return Task.FromResult(path);
        }

        public Task<string?> OpenReviewAsync(Window owner) => Task.FromResult<string?>(null);
    }
}
