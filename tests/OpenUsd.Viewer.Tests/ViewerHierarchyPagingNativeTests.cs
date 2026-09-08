// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerHierarchyPagingNativeTests
{
    [Test]
    public async Task WideBranchesRemainBrowsableWithoutLosingSelectionOrMaterializingEveryPrim()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_HIERARCHY_PAGING_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_HIERARCHY_PAGING_SMOKE=1 on a Windows desktop.");
        }
        string root = Directory.CreateTempSubdirectory("openusd-hierarchy-paging-").FullName;
        string path = Path.Combine(root, "source.usda");
        string source = CreateScene();
        await File.WriteAllTextAsync(path, source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
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
                        await ExerciseAsync(root, path, timeout.Token);
                        await ExerciseCrateMetadataAsync(root, path, timeout.Token);
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
            Name = "Viewer hierarchy paging native test"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(90));
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
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
        string settingsRoot = Path.Combine(root, "settings");
        using var settings = new ViewerSettingsStore(settingsRoot);
        var main = new MainWindow(new RecentStageStore(settingsRoot), settings);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            TreeView tree = Required<TreeView>(main, "StageHierarchy");
            TextBlock page = Required<TextBlock>(main, "HierarchyRootPageState");
            await WaitAsync(() => page.Text == "Roots 1-64 of 145", timeout);
            await Assert.That(PrimItems(tree).Length).IsEqualTo(64);
            Click(main, "HierarchyRootNext");
            await Assert.That(page.Text).IsEqualTo("Roots 65-128 of 145");
            Click(main, "HierarchyRootNext");
            await Assert.That(page.Text).IsEqualTo("Roots 129-145 of 145");
            await Assert.That(PrimItems(tree).Length).IsEqualTo(17);
            await Assert.That(Required<Button>(main, "HierarchyRootNext").IsEnabled).IsFalse();
            TreeViewItem longName = PrimItems(tree).Single(item =>
                ((ViewerHierarchyTreeNode)item.Tag!).Entry.Name.Length == 600);
            TextBlock longLabel = ((Control)longName.Header!).GetVisualDescendants().OfType<TextBlock>().First();
            await Assert.That(longLabel.Text!.Length).IsLessThanOrEqualTo(256);
            await Assert.That(longLabel.Text).EndsWith("...");
            await Assert.That(AutomationProperties.GetName(longName)).IsEqualTo($"Prim /{new string('L', 600)}");
            tree.SelectedItem = PrimItems(tree).Single(item => PathOf(item) == "/Root144");
            await WaitAsync(() => InspectorHasPath(main, "/Root144"), timeout);
            Click(main, "HierarchyRootPrevious");
            await Assert.That(InspectorHasPath(main, "/Root144")).IsTrue();
            await Assert.That(session.CurrentRenderState.Selection.Items.Single().PrimPath).IsEqualTo("/Root144");
            await Assert.That(tree.SelectedItem).IsNull();
            Click(main, "HierarchyRootPrevious");

            TreeViewItem wide = PrimItems(tree).Single(item => PathOf(item) == "/Root000");
            wide.IsExpanded = true;
            await WaitAsync(() => PrimItems(wide).Length == 64, timeout);
            await Assert.That(PathOf(PrimItems(wide)[0])).IsEqualTo("/Root000/Child000");
            BranchButton(wide, "hierarchy.next:/Root000").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(PathOf(PrimItems(wide)[0])).IsEqualTo("/Root000/Child064");
            BranchButton(wide, "hierarchy.next:/Root000").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(PrimItems(wide).Length).IsEqualTo(17);
            tree.SelectedItem = PrimItems(wide).Single(item => PathOf(item) == "/Root000/Child144");
            await WaitAsync(() => InspectorHasPath(main, "/Root000/Child144"), timeout);
            BranchButton(wide, "hierarchy.previous:/Root000").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(InspectorHasPath(main, "/Root000/Child144")).IsTrue();
            await Assert.That(session.CurrentRenderState.Selection.Items.Single().PrimPath)
                .IsEqualTo("/Root000/Child144");
            await Assert.That(tree.SelectedItem).IsNull();

            Required<TextBox>(main, "HierarchyFilter").Text = "Child144";
            await WaitAsync(() => PrimItems(tree).Length == 1, timeout);
            wide = PrimItems(tree).Single();
            wide.IsExpanded = true;
            await WaitAsync(() => PrimItems(wide).Length == 1, timeout);
            await Assert.That(PathOf(PrimItems(wide).Single())).IsEqualTo("/Root000/Child144");
            Required<TextBox>(main, "HierarchyFilter").Text = string.Empty;
            await WaitAsync(() => PrimItems(tree).Length == 64, timeout);
            Required<TextBox>(main, "HierarchyExpandDepthInput").Text = "99";
            await WaitAsync(() => Required<TextBlock>(main, "HierarchyState").IsVisible &&
                Required<TextBlock>(main, "HierarchyState").Text?.Contains(
                    "Automatic expansion", StringComparison.Ordinal) == true, timeout);
            await Assert.That(CountMaterialized(tree)).IsLessThanOrEqualTo(512);
            TreeViewItem dense = PrimItems(tree).Single(item => PathOf(item) == "/Root001");
            int beforeCollapse = CountMaterialized(tree);
            dense.IsExpanded = false;
            await Assert.That(CountMaterialized(tree)).IsLessThan(beforeCollapse);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.HasPrim("/Root001/Branch63/Leaf069"), timeout)).IsTrue();
            await Assert.That(InspectorHasPath(main, "/Root000/Child144")).IsTrue();

            Required<TextBox>(main, "HierarchyExpandDepthInput").Text = "0";
            await WaitAsync(() => !ReferenceEquals(
                PrimItems(tree).Single(item => PathOf(item) == "/Root001"), dense), timeout);
            dense = PrimItems(tree).Single(item => PathOf(item) == "/Root001");
            dense.IsExpanded = true;
            foreach (TreeViewItem branch in PrimItems(dense))
            {
                branch.IsExpanded = true;
            }
            await Assert.That(CountMaterialized(tree)).IsLessThanOrEqualTo(4096);
            await Assert.That(Required<TextBlock>(main, "HierarchyState").Text).Contains("display limit");
            TreeViewItem deferred = PrimItems(dense).Last(item => !item.IsExpanded);
            PrimItems(dense).First(item => item.IsExpanded).IsExpanded = false;
            deferred.IsExpanded = true;
            await Assert.That(deferred.IsExpanded).IsTrue();
            await Assert.That(PrimItems(deferred).Length).IsEqualTo(64);
            await Assert.That(CountMaterialized(tree)).IsLessThanOrEqualTo(4096);
            await Assert.That(InspectorHasPath(main, "/Root000/Child144")).IsTrue();
            await Assert.That(session.CurrentRenderState.Selection.Items.Single().PrimPath)
                .IsEqualTo("/Root000/Child144");

            TreeViewItem smallPage = PrimItems(dense).First(item => item.IsExpanded);
            BranchButton(smallPage, $"hierarchy.next:{PathOf(smallPage)}")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(PrimItems(smallPage).Length).IsEqualTo(6);
            smallPage.IsExpanded = false;
            PrimItems(dense).First(item => !item.IsExpanded && !ReferenceEquals(item, smallPage)).IsExpanded = true;
            await Assert.That(4096 - CountMaterialized(tree)).IsLessThan(64);
            smallPage.IsExpanded = true;
            await Assert.That(smallPage.IsExpanded).IsTrue()
                .Because("the remembered final page needs six containers, not the maximum sibling page size");
            await Assert.That(PrimItems(smallPage).Length).IsEqualTo(6);
            await Assert.That(CountMaterialized(tree)).IsLessThanOrEqualTo(4096);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            main.Close();
            await closed.Task.WaitAsync(cleanup.Token);
        }
    }

    private static string CreateScene()
    {
        var text = new StringBuilder("#usda 1.0\n");
        for (int root = 0; root < 145; root++)
        {
            string name = root == 143 ? new string('L', 600) : FormattableString.Invariant($"Root{root:D3}");
            text.Append("def Xform \"").Append(name).Append("\"\n");
            if (root == 2)
            {
                text.Append("(\n    variants = { string look = \"blue\" }\n    prepend variantSets = \"look\"\n)\n");
            }
            text.Append("{\n");
            if (root == 0)
            {
                for (int child = 0; child < 145; child++)
                {
                    text.Append(FormattableString.Invariant($"    def Scope \"Child{child:D3}\" {{}}\n"));
                }
            }
            else if (root == 1)
            {
                for (int branch = 0; branch < 64; branch++)
                {
                    text.Append(FormattableString.Invariant($"    def Scope \"Branch{branch:D2}\"\n    {{\n"));
                    for (int leaf = 0; leaf < 70; leaf++)
                    {
                        text.Append(FormattableString.Invariant($"        def Scope \"Leaf{leaf:D3}\" {{}}\n"));
                    }
                    text.Append("    }\n");
                }
            }
            else if (root == 2)
            {
                text.Append("    variantSet \"look\" = {\n        \"red\" {}\n        \"blue\" {}\n    }\n");
            }
            text.Append("}\n");
        }
        return text.ToString();
    }

    private static async Task ExerciseCrateMetadataAsync(string root, string textPath, CancellationToken timeout)
    {
        string crate = Path.Combine(root, "scene.usdc");
        using (UsdStage text = UsdStage.Open(textPath))
        using (UsdLayer layer = text.GetRootLayer())
        {
            layer.Export(crate);
        }
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = crate,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        string settingsRoot = Path.Combine(root, "crate-settings");
        using var settings = new ViewerSettingsStore(settingsRoot);
        var main = new MainWindow(new RecentStageStore(settingsRoot), settings);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            _ = await opened.Task.WaitAsync(timeout);
            TreeViewItem deferred = PrimItems(Required<TreeView>(main, "StageHierarchy"))
                .Single(item => PathOf(item) == "/Root002");
            await Assert.That(((Control)deferred.Header!).GetVisualDescendants().OfType<TextBlock>()
                .Any(static label => label.Text?.Contains("variants deferred", StringComparison.Ordinal) == true))
                .IsTrue();
            await Assert.That(ToolTip.GetTip(deferred)?.ToString()).Contains("not available");
            await Assert.That(((Control)deferred.Header!).GetVisualDescendants().OfType<ComboBox>()).IsEmpty();
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            main.Close();
            await closed.Task.WaitAsync(cleanup.Token);
        }
    }

    private static bool InspectorHasPath(MainWindow window, string path) =>
        Required<StackPanel>(window, "InspectorRows").Children.OfType<TextBlock>()
            .Any(row => row.Text == $"Path: {path}");

    private static TreeViewItem[] PrimItems(ItemsControl control) =>
        control.Items.OfType<TreeViewItem>().Where(static item => item.Tag is ViewerHierarchyTreeNode).ToArray();

    private static string PathOf(TreeViewItem item) => ((ViewerHierarchyTreeNode)item.Tag!).Entry.Path;

    private static int CountMaterialized(ItemsControl control) =>
        PrimItems(control).Sum(item => 1 + CountMaterialized(item));

    private static Button BranchButton(TreeViewItem item, string id) =>
        ((Control)item.Header!).GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == id);

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private static void Click(Control owner, string name) =>
        Required<Button>(owner, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitAsync(
        Func<bool> ready,
        CancellationToken timeout,
        [CallerArgumentExpression(nameof(ready))] string? condition = null)
    {
        long start = Stopwatch.GetTimestamp();
        while (!ready())
        {
            if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(15))
            {
                throw new TimeoutException($"Viewer did not reach state: {condition}");
            }
            await Task.Delay(20, timeout);
        }
    }
}
