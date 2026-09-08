// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPropertyPagingNativeTests
{
    [Test]
    public async Task SearchAndPagingKeepLargePrimPropertiesReachableAndEditTheExactSelectedProperty()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PROPERTY_PAGING_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_PROPERTY_PAGING_SMOKE=1 on a Windows desktop.");
        }
        string root = Directory.CreateTempSubdirectory("openusd-property-paging-").FullName;
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(Path.Combine(root, "reference.usda"),
            "#usda 1.0\ndef Xform \"Source\"\n{\n custom double details:reference = 42\n" +
            " custom int[] details:array = [" + string.Join(", ", Enumerable.Range(1, 1000)) + "]\n}\n");
        await File.WriteAllBytesAsync(Path.Combine(root, "texture.bin"), [1, 2, 3, 4]);
        var source = new StringBuilder("""
            #usda 1.0
            (
                startTimeCode = 5
                endTimeCode = 10
            )
            def Xform "World" (
                prepend references = @reference.usda@</Source>
            )
            {
                custom double details:reference
                custom double details:animated.timeSamples = { 0: 0, 10: 20 }
                custom asset details:texture = @texture.bin@
                custom float details:connected = 4
                custom float details:connected.connect = </Graph.outputs:value>

            """);
        for (int index = 0; index < 257; index++)
        {
            source.Append(FormattableString.Invariant($"    custom double reviewTest:a{index:D3} = {index}\n"));
        }
        source.Append("    rel reviewTest:link = </Other>\n}\ndef Xform \"Other\" {}\n");
        string original = source.ToString();
        await File.WriteAllTextAsync(path, original);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        using var dispatcherLifetime = new CancellationTokenSource();
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
                        await ExerciseAsync(root, path, timeout.Token);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
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
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer property paging native test"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(original);
        }
        finally
        {
            timeout.Cancel();
            dispatcherLifetime.Cancel();
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
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var settings = new ViewerSettingsStore(Path.Combine(root, "settings"));
        var main = new MainWindow(new RecentStageStore(Path.Combine(root, "settings")), settings);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            TreeView tree = Required<TreeView>(main, "StageHierarchy");
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World" });
            Required<TabControl>(main, "InspectorTabs").SelectedItem = Required<TabItem>(main, "ValueTab");
            TextBox query = Required<TextBox>(main, "InspectorPropertyQuery");
            TextBlock state = Required<TextBlock>(main, "InspectorPropertyPageState");
            await WaitAsync(() => query.IsEnabled, timeout);
            query.Text = "reviewTest:";
            await WaitAsync(() => state.Text == "Properties 1-32 of 258 matches", timeout);
            await Assert.That(PropertyButtons(main).Length).IsEqualTo(32);
            Expander[] disclosures = PropertyDetails(main);
            await Assert.That(disclosures.Length).IsEqualTo(32);
            await Assert.That(disclosures.All(static details => !details.IsExpanded && details.Content is null))
                .IsTrue();
            disclosures[0].IsExpanded = true;
            await Assert.That(disclosures[0].Content).IsNotNull();
            disclosures[1].IsExpanded = true;
            await Assert.That(disclosures[0].IsExpanded).IsFalse();
            await Assert.That(disclosures[0].Content).IsNull();
            await Assert.That(disclosures[1].Content).IsNotNull();
            disclosures[1].IsExpanded = false;
            await Assert.That(disclosures[1].Content).IsNull();
            await Assert.That(AutomationProperties.GetAutomationId(PropertyButtons(main)[0]))
                .IsEqualTo("edit.property:/World.reviewTest:a000");
            Click(main, "InspectorPropertyNext");
            await Assert.That(state.Text).IsEqualTo("Properties 33-64 of 258 matches");
            await Assert.That(AutomationProperties.GetAutomationId(PropertyButtons(main)[0]))
                .IsEqualTo("edit.property:/World.reviewTest:a032");
            Click(main, "InspectorPropertyPrevious");
            await Assert.That(state.Text).IsEqualTo("Properties 1-32 of 258 matches");
            for (int page = 1; page < 9; page++)
            {
                Click(main, "InspectorPropertyNext");
                await Assert.That(PropertyButtons(main).Length).IsLessThanOrEqualTo(32);
            }
            await Assert.That(state.Text).IsEqualTo("Properties 257-258 of 258 matches");
            await Assert.That(Required<Button>(main, "InspectorPropertyNext").IsEnabled).IsFalse();
            await Assert.That(AutomationProperties.GetAutomationId(PropertyButtons(main).Single()))
                .IsEqualTo("edit.property:/World.reviewTest:a256");

            query.Text = "REVIEWTEST:a256";
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(state.Text).IsEqualTo("Properties 1-1 of 1 matches");
            await Assert.That(Required<Button>(main, "InspectorPropertyPrevious").IsEnabled).IsFalse();
            Button edit = PropertyButtons(main).Single();
            await Assert.That(edit.IsEnabled).IsTrue();
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled, timeout);
            await Assert.That(Required<ComboBox>(property, "PropertySelector").SelectedItem?.ToString())
                .IsEqualTo("reviewTest:a256 (double)");
            Required<TextBox>(property, "PropertyValue").Text = "1024";
            Click(property, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(property, "PropertyEditStatus").Text == "Review edit applied.",
                timeout);
            await property.CloseAsync();
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").GetDouble("reviewTest:a256"), timeout)).IsEqualTo(1024d);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(query.Text).IsEqualTo("REVIEWTEST:a256");
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").GetDouble("reviewTest:a256"), timeout)).IsEqualTo(256d);
            await WaitAsync(() => query.IsEnabled, timeout);
            query.Text = "relationship";
            await WaitAsync(() => PropertyButtons(main).Length == 0 &&
                Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                    .Any(static row => row.Text == "reviewTest:link: /Other"), timeout);
            await Assert.That(PropertyButtons(main)).IsEmpty();
            await Assert.That(Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                .Any(static row => row.Text == "reviewTest:link: /Other")).IsTrue();

            await FilterAttributeAsync(main, query, "details:reference", timeout);
            await ExpandPropertyDetailsAsync(main, timeout);
            await Assert.That(ValueText(main)).Contains("Winning value spec: /Source.details:reference");
            await Assert.That(ValueText(main)).Contains("reference.usda");
            await Assert.That(ValueText(main)).Contains("source: Default");
            await Assert.That(Required<TextBlock>(main, "InspectorPropertyTimeState").Text)
                .IsEqualTo("Default time (not timeline)");
            await FilterAttributeAsync(main, query, "details:array", timeout);
            await Assert.That(ValueText(main)).Contains("Truncated");
            await Assert.That(ValueText(main)).Contains("1000");
            await Assert.That(ValueText(main)).Contains("1, 2, 3");
            await FilterAttributeAsync(main, query, "details:texture", timeout);
            await ExpandPropertyDetailsAsync(main, timeout);
            await Assert.That(ValueText(main)).Contains("Authored asset: texture.bin");
            await Assert.That(ValueText(main)).Contains("Asset status: Resolved");
            await Assert.That(ValueText(main)).Contains("Asset anchor:");
            PropertyDetails(main).Single().IsExpanded = false;
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow missingAssetEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(missingAssetEdit, "SetPropertyButton").IsEnabled, timeout);
            Required<TextBox>(missingAssetEdit, "PropertyValue").Text = "missing.bin";
            Click(missingAssetEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(missingAssetEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await missingAssetEdit.CloseAsync();
            await WaitAsync(() => ValueText(main).Contains("Value: missing.bin", StringComparison.Ordinal), timeout);
            await Assert.That(PropertyDetails(main).Single().IsExpanded).IsFalse();
            await Assert.That(PropertyDetails(main).Single().Content).IsNull();
            await Assert.That(ValueText(main)).Contains("Missing asset");
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => ValueText(main).Contains("Value: texture.bin", StringComparison.Ordinal), timeout);
            await Assert.That(ValueText(main)).DoesNotContain("Missing asset");
            await FilterAttributeAsync(main, query, "details:connected", timeout);
            await ExpandPropertyDetailsAsync(main, timeout);
            await Assert.That(ValueText(main)).Contains("/Graph.outputs:value");
            await Assert.That(ValueText(main)).Contains("Value: 4");
            await FilterAttributeAsync(main, query, "details:animated", timeout);
            await Assert.That(ValueText(main)).Contains("Value: <unset>");
            await WaitAsync(() => Required<Button>(main, "InspectorPropertyCurrentTime").IsEnabled, timeout);
            Click(main, "InspectorPropertyCurrentTime");
            await WaitAsync(() => query.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "InspectorPropertyTimeState").Text)
                .IsEqualTo("Time 5 snapshot (not live)");
            await Assert.That(ValueText(main)).Contains("Value: 10");
            Required<Slider>(main, "TimelineSlider").Value = 10;
            await WaitAsync(() => Required<TextBox>(main, "CurrentTimeInput").Text == "10", timeout);
            await Assert.That(ValueText(main)).Contains("Value: 10");
            await Assert.That(Required<TextBlock>(main, "InspectorPropertyTimeState").Text)
                .IsEqualTo("Time 5 snapshot (not live)");
            Click(main, "InspectorPropertyCurrentTime");
            await WaitAsync(() => query.IsEnabled, timeout);
            await Assert.That(ValueText(main)).Contains("Value: 20");
            await Assert.That(Required<TextBlock>(main, "InspectorPropertyTimeState").Text)
                .IsEqualTo("Time 10 snapshot (not live)");

            await FilterAttributeAsync(main, query, "details:reference", timeout);
            await ExpandPropertyDetailsAsync(main, timeout);
            await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow referencedEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(referencedEdit, "SetPropertyButton").IsEnabled, timeout);
            string targetLine = Required<TextBlock>(referencedEdit, "PropertyTargetState").Text!.Split('\n')[0];
            await Assert.That(targetLine).StartsWith("Review target: ");
            string reviewTarget = targetLine["Review target: ".Length..];
            Required<TextBox>(referencedEdit, "PropertyValue").Text = "84";
            Click(referencedEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(referencedEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await referencedEdit.CloseAsync();
            await WaitAsync(() => ValueText(main).Contains("Value: 84", StringComparison.Ordinal), timeout);
            await Assert.That(PropertyDetails(main).Single().IsExpanded).IsTrue();
            await Assert.That(ValueText(main)).Contains($"Winning value layer: {reviewTarget}");
            await Assert.That(Required<TextBlock>(main, "InspectorPropertyTimeState").Text)
                .IsEqualTo("Time 10 snapshot (not live)");
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => ValueText(main).Contains("Value: 42", StringComparison.Ordinal), timeout);
            await Assert.That(PropertyDetails(main).Single().IsExpanded).IsTrue();
            await Assert.That(ValueText(main)).Contains("Winning value spec: /Source.details:reference");
            await Assert.That(ValueText(main)).Contains("reference.usda");
            await Assert.That(Required<TextBlock>(main, "InspectorPropertyTimeState").Text)
                .IsEqualTo("Time 10 snapshot (not live)");

            await FilterAttributeAsync(main, query, "details:animated", timeout);
            await WaitAsync(() => Required<Button>(main, "InspectorPropertyDefaultTime").IsEnabled, timeout);
            Click(main, "InspectorPropertyDefaultTime");
            await WaitAsync(() => query.IsEnabled, timeout);
            await Assert.That(ValueText(main)).Contains("Value: <unset>");
            await Assert.That(query.Text).IsEqualTo("details:animated");

            await WaitAsync(() => Required<Button>(main, "InspectorPropertyCurrentTime").IsEnabled, timeout);
            bool targetIsSession = await session.Scheduler.InvokeAsync(
                static stage => stage.EditTargetLayerIdentifier == stage.SessionLayerIdentifier, timeout);
            string targetButton = targetIsSession ? "SetRootEditTargetButton" : "SetSessionEditTargetButton";
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var release = new ManualResetEventSlim())
            {
                Task held = session.Scheduler.InvokeAsync(_ =>
                {
                    entered.TrySetResult();
                    release.Wait(timeout);
                    return 0;
                }, timeout).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout);
                    Expander cachedDetails = PropertyDetails(main).Single();
                    cachedDetails.IsExpanded = true;
                    await Assert.That(cachedDetails.Content).IsNotNull();
                    await Assert.That(ValueText(main)).Contains("Sample preview: Complete; 2 sample(s)");
                    cachedDetails.IsExpanded = false;
                    await Assert.That(cachedDetails.Content).IsNull();
                    await Assert.That(Required<Button>(main, targetButton).IsEnabled).IsTrue();
                    Click(main, targetButton);
                    await Assert.That(Required<Button>(main, "InspectorPropertyCurrentTime").IsEnabled).IsFalse();
                    await Assert.That(Required<Button>(main, "InspectorPropertyDefaultTime").IsEnabled).IsFalse();
                }
                finally
                {
                    release.Set();
                    await held.WaitAsync(timeout);
                }
            }
            if (targetIsSession)
            {
                await WaitAsync(() => Required<Button>(main, "SetSessionEditTargetButton").IsEnabled, timeout);
                Click(main, "SetSessionEditTargetButton");
            }
            await WaitAsync(() => Required<Button>(main, "InspectorPropertyCurrentTime").IsEnabled, timeout);

            query.Text = "does-not-exist";
            await WaitAsync(() => state.Text == "No properties match the current filter.", timeout);
            await Assert.That(state.Text).IsEqualTo("No properties match the current filter.");
            await Assert.That(Required<Button>(main, "InspectorPropertyNext").IsEnabled).IsFalse();
            await Assert.That(PropertyButtons(main)).IsEmpty();
            query.Text = string.Empty;
            await WaitAsync(() => Required<Button>(main, "InspectorPropertyNext").IsEnabled, timeout);
            Click(main, "InspectorPropertyNext");
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Other" });
            await WaitAsync(() => query.IsEnabled && state.Text?.StartsWith(
                "Properties 1-", StringComparison.Ordinal) == true &&
                PropertyButtons(main).All(button => AutomationProperties.GetAutomationId(button)!.StartsWith(
                    "edit.property:/Other.", StringComparison.Ordinal)), timeout);
            await Assert.That(Required<Button>(main, "InspectorPropertyPrevious").IsEnabled).IsFalse();
            await Assert.That(PropertyButtons(main).All(button =>
                AutomationProperties.GetAutomationId(button)!.StartsWith("edit.property:/Other.",
                    StringComparison.Ordinal))).IsTrue();
            query.Text = "visibility";
            await WaitAsync(() => PropertyButtons(main) is [Button visibility] &&
                AutomationProperties.GetAutomationId(visibility) == "edit.property:/Other.visibility", timeout);
            await ExpandPropertyDetailsAsync(main, timeout);
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World" });
            await WaitAsync(() => query.IsEnabled && PropertyButtons(main) is [Button visibility] &&
                AutomationProperties.GetAutomationId(visibility) == "edit.property:/World.visibility", timeout);
            await Assert.That(PropertyDetails(main).Single().IsExpanded).IsFalse();
            await Assert.That(PropertyDetails(main).Single().Content).IsNull();
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            foreach (PropertyEditWindow property in main.OwnedWindows.OfType<PropertyEditWindow>().ToArray())
            {
                await property.CloseAsync().WaitAsync(cleanup.Token);
            }
            main.Close();
            await WaitAsync(() => closed.Task.IsCompleted ||
                main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), cleanup.Token);
            if (!closed.Task.IsCompleted)
            {
                Click(main.OwnedWindows.OfType<DocumentChangesWindow>().Single(), "DiscardDocumentChangesButton");
            }
            await closed.Task.WaitAsync(cleanup.Token);
        }
    }

    private static Button[] PropertyButtons(MainWindow main) =>
        Required<StackPanel>(main, "ValueRows").Children.OfType<StackPanel>()
            .SelectMany(static row => row.Children.OfType<Button>())
            .Where(static button => AutomationProperties.GetAutomationId(button)?.StartsWith(
                "edit.property:", StringComparison.Ordinal) == true).ToArray();

    private static string ValueText(MainWindow main) => string.Join("\n",
        Required<StackPanel>(main, "ValueRows").Children.OfType<StackPanel>()
            .SelectMany(static row => row.Children.OfType<TextBlock>().Concat(
                row.Children.OfType<Expander>().Where(static details => details.IsExpanded)
                    .Select(static details => details.Content).OfType<StackPanel>()
                    .SelectMany(static content => content.Children.OfType<TextBlock>())))
            .Select(static text => text.Text));

    private static Expander[] PropertyDetails(MainWindow main) =>
        Required<StackPanel>(main, "ValueRows").Children.OfType<StackPanel>()
            .SelectMany(static row => row.Children.OfType<Expander>()).ToArray();

    private static async Task ExpandPropertyDetailsAsync(MainWindow main, CancellationToken timeout)
    {
        Expander details = PropertyDetails(main).Single();
        details.IsExpanded = true;
        await WaitAsync(() => details.Content is not null, timeout);
    }

    private static async Task FilterAttributeAsync(
        MainWindow main, TextBox query, string name, CancellationToken timeout)
    {
        query.Text = name;
        await WaitAsync(() => PropertyButtons(main) is [Button button] &&
            AutomationProperties.GetAutomationId(button) == $"edit.property:/World.{name}", timeout);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private static void Click(Control owner, string name) =>
        Required<Button>(owner, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitAsync(
        Func<bool> ready,
        CancellationToken timeout,
        [CallerArgumentExpression(nameof(ready))] string? condition = null)
    {
        long started = Stopwatch.GetTimestamp();
        while (!ready())
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException($"Viewer state did not become ready: {condition}");
            }
            await Task.Delay(20, timeout);
        }
    }
}
