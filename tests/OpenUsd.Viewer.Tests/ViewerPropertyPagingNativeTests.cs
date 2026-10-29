// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUsd.Geom;
using OpenUsd.Lux;
using OpenUsd.Shade;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerPropertyPagingNativeTests
{
    [Test]
    public async Task SearchAndPagingKeepLargePrimPropertiesReachableAndEditTheExactSelectedProperty()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PROPERTY_PAGING_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_PROPERTY_PAGING_SMOKE=1 on a Windows desktop.");
        }
        string parent = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ?? Path.GetTempPath();
        if (!Path.IsPathFullyQualified(parent))
        {
            throw new InvalidOperationException("The property-page test root must be an absolute physical path.");
        }
        string root = Directory.CreateDirectory(
            Path.Combine(parent, $"openusd-property-paging-{Guid.NewGuid():N}")).FullName;
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(Path.Combine(root, "reference.usda"),
            "#usda 1.0\ndef Xform \"Source\"\n{\n custom double details:reference = 42\n" +
            " custom int[] details:array = [" + string.Join(", ", Enumerable.Range(1, 1000)) + "]\n}\n");
        await File.WriteAllBytesAsync(Path.Combine(root, "texture.bin"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(root, "shader.opaque"), "Unresolved source asset.");
        var source = new StringBuilder("""
            #usda 1.0
            (
                startTimeCode = 5
                endTimeCode = 10
            )
            def Xform "World" (
                prepend references = @reference.usda@</Source>
                prepend apiSchemas = ["MaterialBindingAPI", "CollectionAPI:lights"]
            )
            {
                custom double details:reference
                custom double details:animated.timeSamples = { 0: 0, 10: 20 }
                custom double details:blocked = None
                custom asset details:texture = @texture.bin@
                custom float details:connected = 4
                custom float details:connected.connect = </Graph.outputs:value>
                double3 xformOp:translate = (1, 2, 3)
                uniform token[] xformOpOrder = ["xformOp:translate"]
                rel material:binding = </Looks/Material>
                color3f[] primvars:displayColor = [(1, 0, 0)]
                rel collection:lights:includes = </Other>
                uniform token purpose = "default"
                rel proxyPrim = </Other>
                custom float physics:mass = 2

            """);
        for (int index = 0; index < 257; index++)
        {
            source.Append(FormattableString.Invariant($"    custom double reviewTest:a{index:D3} = {index}\n"));
        }
        source.Append("""
                rel reviewTest:link = </Other>
            }
            def Xform "Other"
            {
                custom point3f[] points = [(0, 0, 0)]
            }
            def Mesh "Grid"
            {
                point3f[] points = [(0, 0, 0), (1, 0, 0), (1, 1, 0)]
                int[] faceVertexCounts = [3]
                int[] faceVertexIndices = [0, 1, 2]
                custom float pointsExtra = 1
                color3f[] primvars:displayColor = [(0, 1, 0)]
                uniform token skel:skinningMethod = "classicLinear"
                int[] primvars:skel:jointIndices = [0, 0, 0]
                rel skel:skeleton = </Other>
                uniform token model:drawMode = "bounds"
            }
            def Shader "Surface"
            {
                uniform token info:id = "UsdPreviewSurface"
                float inputs:roughness = 0.5
                color3f inputs:diffuseColor.connect = </Graph.outputs:value>
                custom float inputs:custom = 7
                token outputs:surface
                custom float focalLength = 100
            }
            def Shader "AssetSurface"
            {
                uniform token info:implementationSource = "sourceAsset"
                uniform token info:id = "UsdPreviewSurface"
                uniform asset info:custom:sourceAsset = @shader.opaque@
                float inputs:roughness = 0.75
            }
            def Shader "CodeSurface"
            {
                uniform token info:implementationSource = "sourceCode"
                uniform token info:id = None
                float inputs:roughness = 0.6
            }
            def Shader "NoIdSurface"
            {
                float inputs:roughness = 0.25
            }
            def Shader "BlockedIdSurface"
            {
                uniform token info:id = None
                float inputs:roughness = 0.35
            }
            def Camera "Camera" {}
            def RectLight "Light" (prepend apiSchemas = ["ShapingAPI"]) {}
            def Scope "Components"
            {
                custom float2 review:pair = (1, 2)
                custom vector3d review:direction = (1, 2, 3)
                custom color4f review:tint = (1, 1, 1, 1)
                custom quatf review:rotation = (1, 0, 0, 0)
                custom matrix4d review:matrix = ((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))
            }
            def Scope "Looks"
            {
                def Material "Material" {}
                def Material "Alternate" {}
            }
            """);
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
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "shader.opaque")))
                .IsEqualTo("Unresolved source asset.");
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
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Surface" });
            Required<TabControl>(main, "InspectorTabs").SelectedItem = Required<TabItem>(main, "ValueTab");
            TextBox shaderQuery = Required<TextBox>(main, "InspectorPropertyQuery");
            await WaitAsync(() => shaderQuery.IsEnabled, timeout);
            Button registry = Required<Button>(main, "InspectorShaderRegistryRead");
            await WaitAsync(() => registry.IsVisible && registry.IsEnabled, timeout);
            await Assert.That(registry.IsVisible && registry.IsEnabled).IsTrue();
            await Assert.That(AutomationProperties.GetAutomationId(registry))
                .IsEqualTo("inspector.shaderRegistryRead");
            await FilterAttributeAsync(main, shaderQuery, "inputs:roughness", timeout, "/Surface");
            await Assert.That(ValueText(main)).DoesNotContain("Sdr input");
            registry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => registry.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text)
                .StartsWith("Sdr definition: UsdPreviewSurface");
            await Assert.That(ValueText(main)).Contains("Sdr input; type: float");
            await Assert.That(ValueText(main)).Contains("connectable: Yes");
            ComboBox propertyGroup = Required<ComboBox>(main, "InspectorPropertyGroup");
            await Assert.That(AutomationProperties.GetAutomationId(propertyGroup))
                .IsEqualTo("inspector.propertyGroup");
            await FilterAttributeAsync(main, shaderQuery, "inputs:custom", timeout, "/Surface");
            await Assert.That(ValueText(main)).Contains("No matching property in this Sdr definition");
            await Assert.That(AutomationProperties.GetAutomationId(PropertyButtons(main).Single()))
                .IsEqualTo("edit.property:/Surface.inputs:custom");
            await FilterAttributeAsync(main, shaderQuery, "inputs:diffuseColor", timeout, "/Surface");
            await ExpandPropertyDetailsAsync(main, timeout);
            await Assert.That(ValueText(main)).Contains("/Graph.outputs:value");
            Click(main, "InspectorPropertyCurrentTime");
            await WaitAsync(() => registry.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text)
                .StartsWith("Read the Sdr registry explicitly");
            await Assert.That(ValueText(main)).DoesNotContain("Sdr input");
            registry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => registry.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text)
                .StartsWith("Sdr definition: UsdPreviewSurface");

            var shaderReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var releaseShaderRead = new ManualResetEventSlim())
            {
                Task held = session.Scheduler.InvokeAsync(_ =>
                {
                    shaderReadEntered.TrySetResult();
                    releaseShaderRead.Wait(timeout);
                    return 0;
                }, timeout).AsTask();
                try
                {
                    await shaderReadEntered.Task.WaitAsync(timeout);
                    propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.ShaderOutputs;
                    shaderQuery.Text = string.Empty;
                    await WaitAsync(() => Required<TextBlock>(main, "InspectorPropertyPageState").Text ==
                        "Properties 1-1 of 1 matches", timeout);
                    await Assert.That(ValueText(main)).Contains("outputs:surface");
                    await Assert.That(ValueText(main)).DoesNotContain("inputs:roughness");
                    propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.ShaderInputs;
                    await FilterAttributeAsync(main, shaderQuery, "inputs:roughness", timeout, "/Surface");
                    await Assert.That(ValueText(main)).Contains("Sdr input; type: float");
                    registry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Assert.That(registry.IsEnabled).IsFalse();
                    await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text)
                        .IsEqualTo("Reading bounded Sdr registry snapshot...");
                    tree.SelectedItem = null;
                    await Assert.That(registry.IsVisible).IsFalse();
                }
                finally
                {
                    releaseShaderRead.Set();
                    await held.WaitAsync(timeout);
                }
            }
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Surface" });
            await WaitAsync(() => registry.IsVisible && registry.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text)
                .StartsWith("Read the Sdr registry explicitly");
            await Assert.That(ValueText(main)).DoesNotContain("Sdr input");
            registry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => registry.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text)
                .StartsWith("Sdr definition: UsdPreviewSurface");
            await Assert.That(ValueText(main)).Contains("Sdr input; type: float");
            Click(main, "InspectorPropertyDefaultTime");
            await WaitAsync(() => registry.IsEnabled, timeout);
            await Assert.That(ValueText(main)).DoesNotContain("Sdr input");
            foreach ((string primPath, string reason, string value) in new[]
            {
                ("/AssetSurface", "sourceAsset", "0.75"),
                ("/CodeSurface", "sourceCode", "0.6"),
                ("/NoIdSurface", "No shader info:id", "0.25"),
                ("/BlockedIdSurface", "info:id is blocked", "0.35")
            })
            {
                tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                    item => item.Tag is ViewerHierarchyTreeNode node && node.Entry.Path == primPath);
                await WaitAsync(() => registry.IsVisible && registry.IsEnabled, timeout);
                await FilterAttributeAsync(main, shaderQuery, "inputs:roughness", timeout, primPath);
                registry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(() => registry.IsEnabled, timeout);
                await Assert.That(Required<TextBlock>(main, "InspectorShaderRegistryState").Text).Contains(reason);
                await Assert.That(ValueText(main)).DoesNotContain("Sdr input");
                await Assert.That(ValueText(main)).Contains($"Value: {value}");
            }
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Camera" });
            await WaitAsync(() => shaderQuery.IsEnabled && !registry.IsVisible, timeout);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Camera;
            await FilterAttributeAsync(main, shaderQuery, "focalLength", timeout, "/Camera");
            await Assert.That(ValueText(main)).Contains("Value: 50");
            await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow cameraEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(cameraEdit, "SetPropertyButton").IsEnabled ||
                Required<TextBlock>(cameraEdit, "PropertyEditStatus").Text?.StartsWith(
                    "Review edit unavailable:", StringComparison.Ordinal) == true, timeout);
            await Assert.That(Required<TextBlock>(cameraEdit, "PropertyEditStatus").Text)
                .DoesNotContain("Review edit unavailable:");
            await Assert.That(Required<ComboBox>(cameraEdit, "PropertySelector").SelectedItem?.ToString())
                .IsEqualTo("focalLength (float)");
            Required<TextBox>(cameraEdit, "PropertyValue").Text = "60";
            Click(cameraEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(cameraEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await cameraEdit.CloseAsync();
            await WaitAsync(() => ValueText(main).Contains("Value: 60", StringComparison.Ordinal), timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdGeomCamera.Wrap(stage.GetPrim("/Camera")).FocalLength, timeout)).IsEqualTo(60f);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => ValueText(main).Contains("Value: 50", StringComparison.Ordinal), timeout);
            await Assert.That(propertyGroup.SelectedIndex).IsEqualTo((int)ViewerPropertyGroup.Camera);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdGeomCamera.Wrap(stage.GetPrim("/Camera")).FocalLength, timeout)).IsEqualTo(50f);
            await ExerciseCameraOpticsAsync(main, session, shaderQuery, propertyGroup, timeout);
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Grid" });
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Mesh;
            shaderQuery.Text = string.Empty;
            TextBlock meshState = Required<TextBlock>(main, "InspectorPropertyPageState");
            await WaitAsync(() => shaderQuery.IsEnabled && meshState.Text?.StartsWith(
                "Properties 1-", StringComparison.Ordinal) == true &&
                PropertyButtons(main).Any(static button => AutomationProperties.GetAutomationId(button) ==
                    "edit.property:/Grid.faceVertexIndices"), timeout);
            string[] meshProperties = PropertyButtons(main)
                .Select(static button => AutomationProperties.GetAutomationId(button)!).ToArray();
            await Assert.That(meshProperties).Contains("edit.property:/Grid.points");
            await Assert.That(meshProperties).Contains("edit.property:/Grid.faceVertexCounts");
            await Assert.That(meshProperties).DoesNotContain("edit.property:/Grid.pointsExtra");
            await Assert.That(meshProperties).DoesNotContain("edit.property:/Grid.primvars:displayColor");
            await Assert.That(meshProperties).DoesNotContain("edit.property:/Grid.visibility");
            await Assert.That(meshProperties).DoesNotContain("edit.property:/Grid.skel:skinningMethod");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Skinning;
            await WaitAsync(() => meshState.Text == "Properties 1-2 of 2 matches" &&
                Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                    .Any(static row => row.Text == "skel:skeleton: /Other"), timeout);
            await Assert.That(ValueText(main)).Contains("skel:skinningMethod");
            await Assert.That(ValueText(main)).DoesNotContain("primvars:skel:jointIndices");
            await Assert.That(ValueText(main)).DoesNotContain("model:drawMode");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Model;
            await WaitAsync(() => meshState.Text == "Properties 1-1 of 1 matches" &&
                PropertyButtons(main).Any(static button => AutomationProperties.GetAutomationId(button) ==
                    "edit.property:/Grid.model:drawMode"), timeout);
            await Assert.That(ValueText(main)).DoesNotContain("skel:skinningMethod");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Mesh;
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Other" });
            await WaitAsync(() => shaderQuery.IsEnabled &&
                meshState.Text == "No properties match the current filter.", timeout);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Other;
            await WaitAsync(() => PropertyButtons(main).Any(static button =>
                AutomationProperties.GetAutomationId(button) == "edit.property:/Other.points"), timeout);
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Light" });
            await WaitAsync(() => shaderQuery.IsEnabled && !registry.IsVisible, timeout);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Light;
            shaderQuery.Text = string.Empty;
            await WaitAsync(() => Required<TextBlock>(main, "InspectorPropertyPageState").Text ==
                "Properties 1-14 of 14 matches", timeout);
            foreach (string lightControl in new[]
            {
                "inputs:intensity",
                "inputs:exposure",
                "inputs:diffuse",
                "inputs:specular",
                "inputs:colorTemperature",
                "inputs:enableColorTemperature",
                "inputs:normalize",
                "inputs:color",
                "inputs:width",
                "inputs:height",
                "inputs:texture:file",
                "inputs:shaping:focus",
                "inputs:shaping:cone:angle",
                "inputs:shaping:cone:softness"
            })
            {
                await Assert.That(ValueText(main)).Contains(lightControl);
            }
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.ShaderInputs;
            await FilterAttributeAsync(main, shaderQuery, "inputs:intensity", timeout, "/Light");
            float originalIntensity = await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).Light.Intensity, timeout);
            await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow lightEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(lightEdit, "SetPropertyButton").IsEnabled, timeout);
            await Assert.That(Required<ComboBox>(lightEdit, "PropertySelector").SelectedItem?.ToString())
                .IsEqualTo("inputs:intensity (float)");
            Required<TextBox>(lightEdit, "PropertyValue").Text = "42";
            Click(lightEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(lightEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await lightEdit.CloseAsync();
            await WaitAsync(() => ValueText(main).Contains("Value: 42", StringComparison.Ordinal), timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).Light.Intensity, timeout)).IsEqualTo(42f);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).Light.Intensity, timeout))
                .IsEqualTo(originalIntensity);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Light;
            await FilterAttributeAsync(main, shaderQuery, "inputs:width", timeout, "/Light");
            float originalWidth = await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).Width, timeout);
            await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow widthEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(widthEdit, "SetPropertyButton").IsEnabled, timeout);
            await Assert.That(Required<ComboBox>(widthEdit, "PropertySelector").SelectedItem?.ToString())
                .IsEqualTo("inputs:width (float)");
            Required<TextBox>(widthEdit, "PropertyValue").Text = "3";
            Click(widthEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(widthEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await widthEdit.CloseAsync();
            await WaitAsync(() => ValueText(main).Contains("Value: 3", StringComparison.Ordinal), timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).Width, timeout)).IsEqualTo(3f);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(
                () => ValueText(main).Contains($"Value: {originalWidth:R}", StringComparison.Ordinal),
                timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).Width, timeout)).IsEqualTo(originalWidth);
            await FilterAttributeAsync(main, shaderQuery, "inputs:texture:file", timeout, "/Light");
            await WaitAsync(() => PropertyButtons(main).Single().IsEnabled, timeout);
            PropertyButtons(main).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow textureEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(textureEdit, "SetPropertyButton").IsEnabled, timeout);
            await Assert.That(Required<ComboBox>(textureEdit, "PropertySelector").SelectedItem?.ToString())
                .IsEqualTo("inputs:texture:file (asset)");
            Required<TextBox>(textureEdit, "PropertyValue").Text = "texture.bin";
            Click(textureEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(textureEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await textureEdit.CloseAsync();
            await WaitAsync(() => ValueText(main).Contains("texture.bin", StringComparison.Ordinal), timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => UsdLuxRectLight.Wrap(stage.GetPrim("/Light")).TextureFile.Path, timeout))
                .IsEqualTo("texture.bin");
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/Light").GetAttribute("inputs:texture:file").IsAuthored, timeout))
                .IsFalse();
            await ExerciseBooleanPropertiesAsync(main, session, shaderQuery, timeout);
            await ExerciseComponentPropertiesAsync(main, session, tree, shaderQuery, propertyGroup, timeout);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Camera;
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Surface" });
            await WaitAsync(() => shaderQuery.IsEnabled && registry.IsVisible, timeout);
            await WaitAsync(() => Required<TextBlock>(main, "InspectorPropertyPageState").Text ==
                "No properties match the current filter.", timeout);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.All;
            await FilterAttributeAsync(main, shaderQuery, "focalLength", timeout, "/Surface");
            await Assert.That(ValueText(main)).Contains("Value: 100");
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(
                static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World" });
            Required<TabControl>(main, "InspectorTabs").SelectedItem = Required<TabItem>(main, "ValueTab");
            TextBox query = Required<TextBox>(main, "InspectorPropertyQuery");
            TextBlock state = Required<TextBlock>(main, "InspectorPropertyPageState");
            await WaitAsync(() => query.IsEnabled && !registry.IsVisible, timeout);
            await Assert.That(registry.IsVisible).IsFalse();
            query.Text = string.Empty;
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Transform;
            await WaitAsync(() => state.Text == "Properties 1-2 of 2 matches", timeout);
            await Assert.That(ValueText(main)).Contains("xformOp:translate");
            await Assert.That(ValueText(main)).Contains("xformOpOrder");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Primvars;
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(ValueText(main)).Contains("primvars:displayColor");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Collections;
            await WaitAsync(() => state.Text?.StartsWith("Properties 1-", StringComparison.Ordinal) == true &&
                Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                    .Any(static row => row.Text == "collection:lights:includes: /Other"), timeout);
            await Assert.That(ValueText(main)).DoesNotContain("primvars:displayColor");
            await Assert.That(ValueText(main)).DoesNotContain("material:binding");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Imageable;
            await WaitAsync(() => state.Text?.StartsWith("Properties 1-", StringComparison.Ordinal) == true &&
                Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                    .Any(static row => row.Text == "proxyPrim: /Other"), timeout);
            await Assert.That(ValueText(main)).Contains("purpose");
            await Assert.That(ValueText(main)).DoesNotContain("physics:mass");
            await Assert.That(ValueText(main)).DoesNotContain("collection:lights:includes");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Physics;
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(ValueText(main)).Contains("physics:mass");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.MaterialBindings;
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                .Any(static row => row.Text == "material:binding: /Looks/Material")).IsTrue();
            await Assert.That(PropertyButtons(main)).IsEmpty();
            Button bindingButton = main.GetVisualDescendants().OfType<Button>().Single(
                static button => AutomationProperties.GetAutomationId(button) ==
                    "edit.binding:/World.material:binding");
            await WaitAsync(() => bindingButton.IsEnabled, timeout);
            bindingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow bindingEdit = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            await WaitAsync(() => Required<Button>(bindingEdit, "SetPropertyButton").IsEnabled, timeout);
            Required<TextBox>(bindingEdit, "PropertyValue").Text = "/Looks/Alternate";
            Click(bindingEdit, "SetPropertyButton");
            await WaitAsync(() => Required<TextBlock>(bindingEdit, "PropertyEditStatus").Text ==
                "Review edit applied.", timeout);
            await bindingEdit.CloseAsync();
            await WaitAsync(() => Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                .Any(static row => row.Text == "material:binding: /Looks/Alternate"), timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetBoundMaterial(stage.GetPrim("/World")).Path, timeout))
                .IsEqualTo("/Looks/Alternate");
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<StackPanel>(main, "ValueRows").Children.OfType<TextBlock>()
                .Any(static row => row.Text == "material:binding: /Looks/Material"), timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetBoundMaterial(stage.GetPrim("/World")).Path, timeout))
                .IsEqualTo("/Looks/Material");
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Other;
            query.Text = "reviewTest:";
            ComboBox propertyFilter = Required<ComboBox>(main, "InspectorPropertyFilter");
            await Assert.That(propertyFilter.IsEnabled).IsTrue();
            await Assert.That(AutomationProperties.GetAutomationId(propertyFilter))
                .IsEqualTo("inspector.propertyFilter");
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.AuthoredAttributes;
            await WaitAsync(() => state.Text == "Properties 1-32 of 257 matches", timeout);
            Click(main, "InspectorPropertyNext");
            await Assert.That(state.Text).IsEqualTo("Properties 33-64 of 257 matches");
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.Relationships;
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(PropertyButtons(main)).IsEmpty();
            await Assert.That(Required<Button>(main, "InspectorPropertyPrevious").IsEnabled).IsFalse();
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.UnauthoredAttributes;
            await WaitAsync(() => state.Text == "No properties match the current filter.", timeout);
            await Assert.That(PropertyButtons(main)).IsEmpty();
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Imageable;
            query.Text = "visibility";
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(ValueText(main)).Contains("inherited");
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.AuthoredAttributes;
            await WaitAsync(() => state.Text == "No properties match the current filter.", timeout);
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.Other;
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.BlockedAttributes;
            query.Text = "details:blocked";
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(AutomationProperties.GetAutomationId(PropertyButtons(main).Single()))
                .IsEqualTo("edit.property:/World.details:blocked");
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.TimeSampledAttributes;
            query.Text = "details:animated";
            await WaitAsync(() => state.Text == "Properties 1-1 of 1 matches", timeout);
            await Assert.That(AutomationProperties.GetAutomationId(PropertyButtons(main).Single()))
                .IsEqualTo("edit.property:/World.details:animated");
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.UnknownAuthoredState;
            await WaitAsync(() => state.Text == "No properties match the current filter.", timeout);
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.All;
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
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.AuthoredAttributes;
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
            await Assert.That(propertyFilter.SelectedIndex).IsEqualTo((int)ViewerPropertyFilter.AuthoredAttributes);
            await Assert.That(propertyGroup.SelectedIndex).IsEqualTo((int)ViewerPropertyGroup.Other);
            await Assert.That(await session.Scheduler.InvokeAsync(
                static stage => stage.GetPrim("/World").GetDouble("reviewTest:a256"), timeout)).IsEqualTo(256d);
            await WaitAsync(() => query.IsEnabled, timeout);
            propertyFilter.SelectedIndex = (int)ViewerPropertyFilter.All;
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
            propertyGroup.SelectedIndex = (int)ViewerPropertyGroup.All;
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
        MainWindow main, TextBox query, string name, CancellationToken timeout, string primPath = "/World")
    {
        query.Text = name;
        await WaitAsync(() => PropertyButtons(main) is [Button button] &&
            AutomationProperties.GetAutomationId(button) == $"edit.property:{primPath}.{name}", timeout);
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
