// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseRawSequenceAsync(string root)
    {
        string work = Path.Combine(root, "raw-planes");
        Directory.CreateDirectory(work);
        string stagePath = Path.Combine(work, "emission.usda");
        await File.WriteAllTextAsync(stagePath, EmissiveSequenceStage);
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = stagePath,
            StageCameraPath = "/World/Camera",
            Renderer = "D3D12",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(Path.Combine(work, "settings"));
        var window = new MainWindow(new RecentStageStore(work), store) { Width = 960, Height = 600 };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(35));
            await WaitUntilAsync(() => Required<MenuItem>(window, "CaptureFrameMenuItem").IsEnabled);
            await Assert.That(session.CurrentRenderState.RenderSettings.DisplayTransform).IsNull();
            var original = session.CurrentRenderState;
            string[]? pngHashes = null;
            string? allPlanesOutput = null;
            foreach ((bool depth, bool hdr) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                string parent = Path.Combine(work, $"depth-{depth}-hdr-{hdr}");
                Directory.CreateDirectory(parent);
                Window sequence = await OpenSequenceAsync(window, parent);
                try
                {
                    CheckBox hdrChoice = Required<CheckBox>(sequence, "SequenceHdrColorData");
                    CheckBox depthChoice = Required<CheckBox>(sequence, "SequenceDepthData");
                    await Assert.That(hdrChoice.IsChecked).IsFalse();
                    await Assert.That(depthChoice.IsChecked).IsFalse();
                    await Assert.That(AutomationProperties.GetName(hdrChoice)).IsEqualTo("Include HDR color data");
                    await Assert.That(AutomationProperties.GetName(depthChoice)).IsEqualTo("Include device-depth data");
                    await Assert.That(ToolTip.GetTip(hdrChoice)?.ToString()).Contains("choose raw data or EXR");
                    await Assert.That(ToolTip.GetTip(depthChoice)?.ToString()).Contains("not metric distance");
                    hdrChoice.IsChecked = hdr;
                    depthChoice.IsChecked = depth;
                    TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
                    bool locked = false;
                    void changeControls(object? sender, AvaloniaPropertyChangedEventArgs args)
                    {
                        if (!locked && depth && hdr && args.Property == TextBlock.TextProperty &&
                            status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
                        {
                            locked = !hdrChoice.IsEffectivelyEnabled && !depthChoice.IsEffectivelyEnabled;
                            hdrChoice.IsChecked = false;
                            depthChoice.IsChecked = false;
                        }
                    }
                    status.PropertyChanged += changeControls;
                    try
                    {
                        Required<Button>(sequence, "SequenceRenderButton")
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
                    }
                    finally
                    {
                        status.PropertyChanged -= changeControls;
                    }
                    await Assert.That(status.Text).StartsWith("Completed");
                    if (depth && hdr)
                    {
                        await Assert.That(locked).IsTrue();
                        await Assert.That(status.Text).Contains("3 HDR");
                        await Assert.That(status.Text).Contains("3 depth");
                    }
                    string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text ??
                        throw new InvalidOperationException("The raw-plane sequence did not publish an output.");
                    string[] actual = await AssertRawSequenceAsync(output, depth, hdr);
                    if (depth && hdr)
                    {
                        allPlanesOutput = output;
                    }
                    if (pngHashes is null)
                    {
                        pngHashes = actual;
                    }
                    else
                    {
                        await Assert.That(actual.SequenceEqual(pngHashes)).IsTrue()
                            .Because("adding raw planes must preserve the existing display PNG bytes exactly");
                    }
                    await AssertRestoredAsync(session, original);
                }
                finally
                {
                    await ((RenderImageSequenceWindow)sequence).CloseAsync();
                }
            }
            await ExerciseExrFormatAsync(window, session, Path.Combine(work, "exr"), allPlanesOutput!);
            await ExerciseRawSelectionAsync(window, session, work, allPlanesOutput!);
            await ExerciseUnsupportedRendererAsync(window, session);
            await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(EmissiveSequenceStage);
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static async Task<string[]> AssertRawSequenceAsync(string output, bool depth, bool hdr)
    {
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(output, "manifest.json")));
        JsonElement frames = manifest.RootElement.GetProperty("frames");
        await Assert.That(frames.GetArrayLength()).IsEqualTo(3);
        await Assert.That(Directory.GetFiles(output).Length).IsEqualTo(4 + (depth ? 3 : 0) + (hdr ? 3 : 0));
        var hashes = new string[3];
        double previousCenter = -1;
        for (int index = 0; index < 3; index++)
        {
            JsonElement frame = frames[index];
            await Assert.That(frame.GetProperty("timeCode").GetDouble()).IsEqualTo(index + 1d);
            int width = frame.GetProperty("width").GetInt32();
            int height = frame.GetProperty("height").GetInt32();
            hashes[index] = await AssertPlaneFileAsync(output, frame, null);
            await Assert.That(frame.TryGetProperty("hdrColor", out _)).IsEqualTo(hdr);
            await Assert.That(frame.TryGetProperty("deviceDepth", out _)).IsEqualTo(depth);
            byte[]? z = depth
                ? await File.ReadAllBytesAsync(Path.Combine(output, $"frame-{index:D6}.device-depth.f32"))
                : null;
            if (depth)
            {
                JsonElement plane = frame.GetProperty("deviceDepth");
                _ = await AssertPlaneFileAsync(output, plane, width * height * 4);
                await Assert.That(plane.GetProperty("convention").GetString())
                    .IsEqualTo("normalized-device-depth-zero-to-one");
                await Assert.That(plane.GetProperty("clearValue").GetInt32()).IsEqualTo(1);
                await Assert.That(plane.GetProperty("independentCoverage").GetBoolean()).IsFalse();
                await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(z!.AsSpan(0, 4))).IsEqualTo(1f);
            }
            if (hdr)
            {
                JsonElement plane = frame.GetProperty("hdrColor");
                _ = await AssertPlaneFileAsync(output, plane, width * height * 8);
                await Assert.That(plane.GetProperty("convention").GetString())
                    .IsEqualTo("renderer-working-composited-before-exposure-and-display");
                await Assert.That(plane.GetProperty("alpha").GetString()).IsEqualTo("stored-framebuffer");
                await Assert.That(plane.GetProperty("displaySelectionIncluded").GetBoolean()).IsFalse();
                byte[] color = await File.ReadAllBytesAsync(Path.Combine(output, $"frame-{index:D6}.hdr.rgba16f"));
                (int count, double x, double y, bool correctDepth) = MeasureEmission(color, z, width);
                await Assert.That(count).IsGreaterThan(100);
                await Assert.That(y).IsLessThan(height / 2d).Because("the raw plane must be top-down, not flipped");
                await Assert.That(correctDepth).IsTrue();
                if (index > 0)
                {
                    await Assert.That(x - previousCenter).IsGreaterThan(12d);
                }
                previousCenter = x;
            }
        }
        return hashes;
    }

    private static async Task ExerciseRawSelectionAsync(
        MainWindow window, ViewerStageSession session, string work, string unselected)
    {
        TreeView hierarchy = Required<TreeView>(window, "StageHierarchy");
        TreeViewItem world = hierarchy.Items.OfType<TreeViewItem>().Single(
            static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World" });
        world.IsExpanded = true;
        hierarchy.SelectedItem = world.Items.OfType<TreeViewItem>().Single(
            static item => item.Tag is ViewerHierarchyTreeNode { Entry.Path: "/World/Animated" });
        await WaitUntilAsync(() => session.CurrentRenderState.Selection.Items is [{ PrimPath: "/World/Animated" }]);
        StageRenderState before = session.CurrentRenderState;
        string selected = await RenderRawOutputAsync(window, Path.Combine(work, "selected"), includeData: true);
        string[] selectedHashes = await AssertRawSequenceAsync(selected, depth: true, hdr: true);
        await AssertRawInvarianceAsync(unselected, selected);
        string png = await RenderRawOutputAsync(window, Path.Combine(work, "selected-png"), includeData: false);
        string[] pngHashes = await AssertRawSequenceAsync(png, depth: false, hdr: false);
        await Assert.That(selectedHashes.SequenceEqual(pngHashes)).IsTrue();
        await AssertRestoredAsync(session, before);
    }

    private static async Task<string> RenderRawOutputAsync(MainWindow window, string parent, bool includeData)
    {
        Directory.CreateDirectory(parent);
        Window sequence = await OpenSequenceAsync(window, parent);
        try
        {
            Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = includeData;
            Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = includeData;
            await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
            return Required<TextBox>(sequence, "SequenceOutputLocation").Text ??
                throw new InvalidOperationException("The sequence did not publish an output.");
        }
        finally
        {
            await ((RenderImageSequenceWindow)sequence).CloseAsync();
        }
    }

    private static async Task AssertRawInvarianceAsync(string before, string after)
    {
        using JsonDocument left = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(before, "manifest.json")));
        using JsonDocument right = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(after, "manifest.json")));
        for (int index = 0; index < 3; index++)
        {
            foreach (string extension in new[] { "hdr.rgba16f", "device-depth.f32" })
            {
                byte[] original = await File.ReadAllBytesAsync(Path.Combine(before, $"frame-{index:D6}.{extension}"));
                byte[] changed = await File.ReadAllBytesAsync(Path.Combine(after, $"frame-{index:D6}.{extension}"));
                await Assert.That(changed.SequenceEqual(original)).IsTrue()
                    .Because("raw HDR and device depth precede display conversion and selection outlines");
            }
            await Assert.That(left.RootElement.GetProperty("frames")[index].GetProperty("sha256").GetString())
                .IsNotEqualTo(right.RootElement.GetProperty("frames")[index].GetProperty("sha256").GetString());
        }
    }

    private static async Task ExerciseUnsupportedRendererAsync(MainWindow window, ViewerStageSession session)
    {
        Required<ComboBox>(window, "RendererSelector").SelectedIndex = 1;
        await WaitUntilAsync(() => Required<MenuItem>(window, "CaptureFrameMenuItem").IsEnabled &&
            session.PickingBackend is ViewerRenderBackend { Identity.Kind: RenderBackendKind.Storm });
        Required<MenuItem>(window, "RenderImageSequenceMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        RenderImageSequenceWindow sequence = window.OwnedWindows.OfType<RenderImageSequenceWindow>().Single();
        try
        {
            Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = true;
            Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = true;
            await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsFalse();
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).Contains("Direct3D 12");
            await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsNullOrEmpty();
        }
        finally
        {
            await sequence.CloseAsync();
        }
    }

    private static async Task<string> AssertPlaneFileAsync(string output, JsonElement plane, int? expectedBytes)
    {
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(output, plane.GetProperty("file").GetString()!));
        await Assert.That(bytes.LongLength).IsEqualTo(plane.GetProperty("bytes").GetInt64());
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await Assert.That(hash).IsEqualTo(plane.GetProperty("sha256").GetString());
        if (expectedBytes is { } length)
        {
            await Assert.That(bytes.Length).IsEqualTo(length);
            await Assert.That(plane.GetProperty("rowOrder").GetString()).IsEqualTo("top-down");
        }
        return hash;
    }

    private static (int Count, double X, double Y, bool CorrectDepth) MeasureEmission(
        byte[] color, byte[]? depth, int width)
    {
        ReadOnlySpan<byte> emission = [0, 0x54, 0, 0, 0, 0, 0, 0x3C];
        int count = 0;
        double x = 0;
        double y = 0;
        bool correctDepth = true;
        for (int pixel = 0; pixel < color.Length / 8; pixel++)
        {
            if (!color.AsSpan(pixel * 8, 8).SequenceEqual(emission))
            {
                continue;
            }
            count++;
            x += pixel % width;
            y += pixel / width;
            if (depth is not null)
            {
                correctDepth &= Math.Abs(
                    BinaryPrimitives.ReadSingleLittleEndian(depth.AsSpan(pixel * 4, 4)) - (1f / 3)) < 0.00001f;
            }
        }
        return (count, x / Math.Max(1, count), y / Math.Max(1, count), correctDepth);
    }

    private const string EmissiveSequenceStage = """
        #usda 1.0
        (
            defaultPrim = "World"
            startTimeCode = 1
            endTimeCode = 3
            upAxis = "Y"
        )
        def Xform "World"
        {
            def Material "Emission"
            {
                token outputs:surface.connect = </World/Emission/Shader.outputs:surface>
                def Shader "Shader"
                {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (64, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Mesh "Animated" (prepend apiSchemas = ["MaterialBindingAPI"])
            {
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                point3f[] points = [(-0.35, -0.35, 0), (0.35, -0.35, 0), (0.35, 0.35, 0), (-0.35, 0.35, 0)]
                normal3f[] normals = [(0, 0, 1)] (interpolation = "constant")
                uniform token subdivisionScheme = "none"
                rel material:binding = </World/Emission>
                double3 xformOp:translate.timeSamples = { 1: (-0.6, 0.6, 0), 3: (0.6, 0.6, 0) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Camera "Camera"
            {
                token projection = "orthographic"
                float horizontalAperture = 40
                float verticalAperture = 40
                float2 clippingRange = (1, 4)
                double3 xformOp:translate = (0, 0, 2)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        """;
}
