// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerDocumentRetirementNativeTests
{
    [Test]
    public async Task CloseAndReplacementCannotDiscardHostWritesCompletedDuringQuiescence()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_RETIREMENT_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_RETIREMENT_SMOKE=1 on a Windows desktop.");
        }
        string root = Path.Combine(AppContext.BaseDirectory, "retirement-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        const string source = "#usda 1.0\ndef Xform \"Body\" {}\n";
        await File.WriteAllTextAsync(path, source);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
                        await ExerciseTransitionAsync(
                            Path.Combine(root, "close"), path, source, replace: false, lifetime.Token);
                        await ExerciseTransitionAsync(
                            Path.Combine(root, "replace"), path, source, replace: true, lifetime.Token);
                        await ExerciseRawSchedulerWriterAsync(
                            Path.Combine(root, "raw-edit"), path, useInvoke: false, lifetime.Token);
                        await ExerciseRawSchedulerWriterAsync(
                            Path.Combine(root, "raw-invoke"), path, useInvoke: true, lifetime.Token);
                        completed.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completed.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
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
            Name = "Viewer document retirement native test"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(70));
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

    private static async Task ExerciseTransitionAsync(
        string root, string path, string source, bool replace, CancellationToken timeout)
    {
        var started = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = new TaskCompletionSource<UsdLayerEditResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = path,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = async (session, cancellationToken) =>
            {
                var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() => stopRequested.TrySetResult());
                started.TrySetResult(session);
                await stopRequested.Task.WaitAsync(timeout);
                UsdLayerEditResult result = await session.Scheduler.EditAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    var address = new UsdLayerEditAddress("/Body.review:late", UsdLayerEditField.Default);
                    return review.CompareAndApply(review.CaptureAuthored([address]),
                        [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(23), "double")]);
                }, UsdStageInvalidationKind.Property, CancellationToken.None);
                written.TrySetResult(result);
            }
        });
        using var settings = new ViewerSettingsStore(root);
        var main = new MainWindow(new RecentStageStore(root), settings);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            ViewerStageSession session = await started.Task.WaitAsync(timeout);
            await WaitUntilAsync(() => Required<MenuItem>(main, "ReloadStageMenuItem").IsEnabled, timeout);
            if (replace)
            {
                Required<MenuItem>(main, "OpenSampleMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else
            {
                main.Close();
            }
            await Assert.That((await written.Task.WaitAsync(timeout)).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await WaitUntilAsync(() => closed.Task.IsCompleted ||
                main.OwnedWindows.Any(window => window.Title == "Unsaved document changes") ||
                Required<TextBlock>(main, "StageStatus").Text != "source.usda" ||
                Required<TextBlock>(main, "ViewerStatus").Text?.Contains(
                    "writer changed", StringComparison.OrdinalIgnoreCase) == true, timeout);

            await Assert.That(closed.Task.IsCompleted).IsFalse()
                .Because("a close decision made before the host's last authored write cannot discard that write");
            Window? prompt = main.OwnedWindows.FirstOrDefault(window => window.Title == "Unsaved document changes");
            if (prompt is not null)
            {
                Required<Button>(prompt, "CancelDocumentTransitionButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => !prompt.IsVisible, timeout);
            }
            await Assert.That(main.IsVisible).IsTrue();
            await Assert.That(Required<TextBlock>(main, "StageStatus").Text).IsEqualTo("source.usda");
            double value = await session.Scheduler.InvokeAsync(static stage =>
                stage.GetPrim("/Body").GetDouble("review:late"), timeout);
            await Assert.That(value).IsEqualTo(23d);
            await Assert.That(Required<MenuItem>(main, "ReloadStageMenuItem").IsEnabled).IsTrue();
            TreeView hierarchy = Required<TreeView>(main, "StageHierarchy");
            hierarchy.SelectedItem = hierarchy.Items.OfType<TreeViewItem>().Single();
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window is PropertyEditWindow), timeout);
            PropertyEditWindow property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitUntilAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled, timeout);
            Required<TextBox>(property, "PropertyValue").Text = "29";
            Required<Button>(property, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() =>
                Required<TextBlock>(property, "PropertyEditStatus").Text == "Review edit applied.", timeout);
            await property.CloseAsync();
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:late"), timeout)).IsEqualTo(29d);
            await Assert.That(await File.ReadAllTextAsync(path, timeout)).IsEqualTo(source);
        }
        finally
        {
            main.Close();
            for (int attempt = 0; attempt < 150 && !closed.Task.IsCompleted; attempt++)
            {
                Window? prompt = main.OwnedWindows.FirstOrDefault(
                    window => window.Title == "Unsaved document changes");
                prompt?.FindControl<Button>("DiscardDocumentChangesButton")?
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100, CancellationToken.None);
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private static T Required<T>(Control control, string name) where T : Control =>
        control.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control '{name}'.");

    private static async Task ExerciseRawSchedulerWriterAsync(
        string root, string path, bool useInvoke, CancellationToken timeout)
    {
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = path,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) => { opened.TrySetResult(session); return Task.CompletedTask; }
        });
        using var settings = new ViewerSettingsStore(root);
        var main = new MainWindow(new RecentStageStore(root), settings);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        bool attempted = false;
        bool writerRan = false;
        UsdLayerEditOutcome? lateOutcome = null;
        Exception? refusal = null;
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            await WaitUntilAsync(() => Required<MenuItem>(main, "ReloadStageMenuItem").IsEnabled, timeout);
            main.PropertyChanged += (_, change) =>
            {
                if (change.Property != Avalonia.Input.InputElement.IsEnabledProperty || main.IsEnabled || attempted)
                {
                    return;
                }
                attempted = true;
                try
                {
                    UsdLayerEditOutcome write(UsdStage stage)
                    {
                        writerRan = true;
                        using UsdLayer review = stage.GetUserReviewLayer();
                        var address = new UsdLayerEditAddress("/Body.review:rawLate", UsdLayerEditField.Default);
                        return review.CompareAndApply(review.CaptureAuthored([address]),
                            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(31), "double")]).Outcome;
                    }
                    ValueTask<UsdLayerEditOutcome> operation = useInvoke
                        ? session.Scheduler.InvokeAsync(write, CancellationToken.None)
                        : session.Scheduler.EditAsync(
                            write, UsdStageInvalidationKind.Property, CancellationToken.None);
                    lateOutcome = operation.AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
                {
                    refusal = exception;
                }
            };
            main.Close();
            await WaitUntilAsync(() => closed.Task.IsCompleted ||
                main.OwnedWindows.Any(window => window.Title == "Unsaved document changes"), timeout);
            await Assert.That(attempted).IsTrue();
            await Assert.That(writerRan).IsFalse()
                .Because("raw shared-scheduler admission must not accept a write after the retirement decision");
            await Assert.That(lateOutcome).IsNull();
            await Assert.That(refusal is InvalidOperationException).IsTrue();
            await Assert.That(closed.Task.IsCompleted).IsTrue();
        }
        finally
        {
            main.Close();
            for (int attempt = 0; attempt < 150 && !closed.Task.IsCompleted; attempt++)
            {
                Window? prompt = main.OwnedWindows.FirstOrDefault(
                    window => window.Title == "Unsaved document changes");
                prompt?.FindControl<Button>("DiscardDocumentChangesButton")?
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100, CancellationToken.None);
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken timeout)
    {
        while (!condition())
        {
            await Task.Delay(20, timeout);
        }
    }
}
