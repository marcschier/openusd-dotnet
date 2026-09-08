// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPhysicsHistoryNativeTests
{
    [Test]
    public async Task SharedHistoryRefreshesTheAnchoredPhysicsInspectorWithoutRequiringASolver()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PHYSICS_HISTORY_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_PHYSICS_HISTORY_SMOKE=1 on a Windows desktop.");
        }
        string root = Path.Combine(AppContext.BaseDirectory, "physics-history-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        const string source = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
                startTimeCode = 0
                endTimeCode = 24
            )
            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }
            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                float physics:mass = 4
                rel physics:simulationOwner = </Scene>
                custom double openUsdPhysics:body:sleepThreshold = 0.125
                def Cube "Shape" (
                    prepend apiSchemas = ["PhysicsCollisionAPI"]
                )
                {
                    double size = 1
                }
            }
            """;
        await File.WriteAllTextAsync(path, source);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseAsync(root, path, lifetime.Token);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
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
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer physics history native test"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(100));
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

    private static async Task ExerciseAsync(string root, string path, CancellationToken timeout)
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
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            TreeView hierarchy = Required<TreeView>(main, "StageHierarchy");
            hierarchy.SelectedItem = hierarchy.Items.OfType<TreeViewItem>().Single(
                item => AutomationProperties.GetName(item) == "Prim /Body");
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled, timeout);
            Required<TabControl>(main, "InspectorTabs").SelectedItem = Required<TabItem>(main, "PhysicsTab");
            Required<MenuItem>(main, "PhysicsEnableButton").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => Required<Button>(main, "PhysicsRefreshPropertiesButton").IsEnabled, timeout);
            Required<Button>(main, "PhysicsRefreshPropertiesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ComboBox objects = Required<ComboBox>(main, "PhysicsObjectSelector");
            await WaitUntilAsync(() => objects.Items.OfType<ComboBoxItem>().Any(
                item => item.Content?.ToString()?.Contains("/Body", StringComparison.Ordinal) == true), timeout);
            objects.SelectedItem = objects.Items.OfType<ComboBoxItem>().First(
                item => item.Content?.ToString()?.Contains("/Body", StringComparison.Ordinal) == true);
            ListBox properties = Required<ListBox>(main, "PhysicsPropertyList");
            const string propertyName = "openUsdPhysics:body:sleepThreshold";
            properties.SelectedItem = properties.Items.OfType<ViewerPhysicsPropertyRow>().Single(
                row => row.Name == propertyName);
            TextBox value = Required<TextBox>(main, "PhysicsPropertyValueBox");
            await Assert.That(value.Text).IsEqualTo("0.125");
            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => main.OwnedWindows.Any(window => window is PropertyEditWindow), timeout);
            PropertyEditWindow editor = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitUntilAsync(() => Required<Button>(editor, "RefreshPropertyButton").IsEnabled, timeout);
            ComboBox selectedProperty = Required<ComboBox>(editor, "PropertySelector");
            selectedProperty.SelectedItem = selectedProperty.Items.OfType<string>().Single(
                item => item.StartsWith(propertyName + " (", StringComparison.Ordinal));
            await WaitUntilAsync(() => Required<Button>(editor, "SetPropertyButton").IsEnabled, timeout);
            Required<TextBox>(editor, "PropertyValue").Text = "0.5";
            Required<Button>(editor, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() =>
                Required<TextBlock>(editor, "PropertyEditStatus").Text == "Review edit applied.", timeout);
            await editor.CloseAsync();
            using (var refreshDeadline = CancellationTokenSource.CreateLinkedTokenSource(timeout))
            {
                refreshDeadline.CancelAfter(TimeSpan.FromSeconds(5));
                await WaitUntilAsync(
                    () => properties.SelectedItem is ViewerPhysicsPropertyRow { ValueText: "0.5" },
                    refreshDeadline.Token);
            }

            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(value.Text).IsEqualTo("0.125");
            await Assert.That(properties.SelectedItem is ViewerPhysicsPropertyRow
            { PrimPath: "/Body", Name: propertyName, ValueText: "0.125" }).IsTrue();

            await Assert.That(Required<MenuItem>(main, "PhysicsRedoButton").IsEnabled).IsTrue();
            Required<MenuItem>(main, "PhysicsRedoButton").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await Assert.That(value.Text).IsEqualTo("0.5");
            await Assert.That(properties.SelectedItem is ViewerPhysicsPropertyRow
            { PrimPath: "/Body", Name: propertyName, ValueText: "0.5" }).IsTrue();

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task blocked = session.Scheduler.EditAsync(stage =>
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                using UsdLayer review = stage.GetUserReviewLayer();
                var address = new UsdLayerEditAddress("/Body." + propertyName, UsdLayerEditField.Default);
                return review.CompareAndApply(review.CaptureAuthored([address]),
                    [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(0.75))]);
            }, UsdStageInvalidationKind.Property, timeout).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout);
                object? previousItems = properties.ItemsSource;
                Required<Button>(main, "PhysicsRefreshPropertiesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                properties.SelectedItem = properties.Items.OfType<ViewerPhysicsPropertyRow>().Single(
                    row => row.Name == "physics:mass");
                release.TrySetResult();
                await blocked;
                await WaitUntilAsync(() => !ReferenceEquals(previousItems, properties.ItemsSource), timeout);
                await Assert.That(properties.SelectedItem is ViewerPhysicsPropertyRow
                { PrimPath: "/Body", Name: "physics:mass", ValueText: "4" }).IsTrue();
                await Assert.That(value.Text).IsEqualTo("4");
            }
            finally
            {
                release.TrySetResult();
                await blocked;
            }
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

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken timeout)
    {
        while (!condition())
        {
            await Task.Delay(20, timeout);
        }
    }
}
