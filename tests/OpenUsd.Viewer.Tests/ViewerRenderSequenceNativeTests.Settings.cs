// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task AssertSequenceOcioFixtureAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        await Assert.That(Convert.ToHexString(SHA256.HashData(bytes)))
            .IsEqualTo("D1D878D47CFDF347F30E012B3B469DD6C054FCBD20332710A46BFA14D600BB7D");
    }

    private static string FindSequenceOcioConfig()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return Path.Combine(directory.FullName, "test-assets", "ocio-test-config.ocio");
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            "The source-gated sequence workflow requires the repository OCIO fixture.");
    }

    private static async Task ConfigureSequenceDisplayAsync(MainWindow window, ViewerStageSession session)
    {
        await WaitUntilAsync(() => !window.HasPendingColorManagementRequest);
        await Assert.That(session.CurrentRenderState.RenderSettings.DisplayTransform).IsNotNull();
        Required<MenuItem>(window, "RenderDrawModeWireframeOnSurfaceMenuItem")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Required<MenuItem>(window, "RenderBackgroundColorWhiteMenuItem")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        foreach (string name in new[]
        {
            "RenderSceneLightingMenuItem", "RenderSceneMaterialsMenuItem", "RenderBackfaceCullingMenuItem"
        })
        {
            MenuItem item = Required<MenuItem>(window, name);
            item.IsChecked = false;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        await WaitUntilAsync(() => session.CurrentRenderState is
        {
            Display.DrawMode: RenderDrawMode.WireframeOnSurface,
            RenderSettings:
            {
                EnableLighting: false,
                UseSceneMaterials: false,
                BackfaceCulling: false,
                ClearColor.X: 1
            }
        });
    }

    private static async Task AssertExactManifestAsync(JsonElement frames, ViewportDimensions viewport, string config)
    {
        double aspect = (double)viewport.Width / viewport.Height;
        await Assert.That(aspect).IsLessThan(1.5);
        double[] focalScales = [1.9444444444444444, 2.5, 3.0555555555555554];
        double[] translations = [0, -0.25, -0.5];
        for (int index = 0; index < 3; index++)
        {
            JsonElement camera = frames[index].GetProperty("camera");
            await Assert.That(camera.GetProperty("mode").GetString()).IsEqualTo("Matrices");
            JsonElement view = camera.GetProperty("view");
            JsonElement projection = camera.GetProperty("projection");
            await Assert.That(view[12].GetDouble()).IsEqualTo(translations[index]);
            await Assert.That(view[14].GetDouble()).IsEqualTo(-8d);
            await Assert.That(Math.Abs(projection[0].GetDouble() - focalScales[index])).IsLessThan(0.00001);
            await Assert.That(Math.Abs(projection[5].GetDouble() - (focalScales[index] * aspect))).IsLessThan(0.00001);
            await Assert.That(Math.Abs(projection[8].GetDouble() - 0.1111111111111111)).IsLessThan(0.00001);
            await Assert.That(projection[11].GetDouble()).IsEqualTo(-1d);
            await Assert.That(Math.Abs(projection[10].GetDouble() + 1.002002002002002)).IsLessThan(0.00001);
            await Assert.That(Math.Abs(projection[14].GetDouble() + 0.2002002002002002)).IsLessThan(0.00001);
            JsonElement display = frames[index].GetProperty("display");
            await Assert.That(display.GetProperty("purposes").GetInt32()).IsEqualTo(7);
            await Assert.That(display.GetProperty("visibility").GetString()).IsEqualTo("RespectAuthored");
            await Assert.That(display.GetProperty("drawMode").GetString()).IsEqualTo("WireframeOnSurface");
            JsonElement settings = frames[index].GetProperty("settings");
            await Assert.That(settings.GetProperty("samplesPerPixel").GetInt32()).IsEqualTo(1);
            await Assert.That(settings.GetProperty("lighting").GetBoolean()).IsFalse();
            await Assert.That(settings.GetProperty("sceneMaterials").GetBoolean()).IsFalse();
            await Assert.That(settings.GetProperty("backfaceCulling").GetBoolean()).IsFalse();
            await Assert.That(settings.GetProperty("outputTransform").GetString()).IsEqualTo("Identity");
            await Assert.That(settings.GetProperty("exposure").GetDouble()).IsEqualTo(-6d);
            await Assert.That(settings.GetProperty("clearColor").EnumerateArray()
                .Select(static value => value.GetDouble()))
                .IsEquivalentTo([1d, 1d, 1d, 1d]);
            JsonElement transform = settings.GetProperty("displayTransform");
            await Assert.That(transform.GetProperty("configPath").GetString()).IsEqualTo(config);
            await Assert.That(transform.GetProperty("sourceColorSpace").GetString()).IsEqualTo("linear");
            await Assert.That(transform.GetProperty("display").GetString()).IsEqualTo("TestDisplay");
            await Assert.That(transform.GetProperty("view").GetString()).IsEqualTo("IdentityView");
            await Assert.That(transform.GetProperty("look").GetString()).IsEqualTo("TestLook");
            await Assert.That(frames[index].GetProperty("selection").GetArrayLength()).IsEqualTo(0);
        }
    }

    private static async Task ExerciseUnsupportedPurposeAsync(MainWindow window, ViewerStageSession session)
    {
        MenuItem guide = Required<MenuItem>(window, "RenderPurposeGuideMenuItem");
        guide.IsChecked = true;
        guide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitUntilAsync(() => (session.CurrentRenderState.Display.Purposes & RenderPurpose.Guide) != 0);
        Required<MenuItem>(window, "RenderImageSequenceMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        RenderImageSequenceWindow sequence = window.OwnedWindows.OfType<RenderImageSequenceWindow>().Single();
        try
        {
            await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsFalse();
            await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).Contains("custom purpose");
            await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsNullOrEmpty();
        }
        finally
        {
            await sequence.CloseAsync();
            guide.IsChecked = false;
            guide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        await WaitUntilAsync(() => session.CurrentRenderState.Display.Purposes == SceneDisplayState.Default.Purposes);
    }

    private static async Task ExerciseGpuSequencePlanesAsync(
        MainWindow window, ViewerStageSession session, string root, string colorOnlyOutput)
    {
        StageRenderState before = session.CurrentRenderState;
        string? allPlanesOutput = null;
        foreach ((bool depth, bool hdr) in new[] { (true, false), (false, true), (true, true) })
        {
            string parent = Path.Combine(root, $"gpu-depth-{depth}-hdr-{hdr}");
            Directory.CreateDirectory(parent);
            Window sequence = await OpenSequenceAsync(window, parent);
            try
            {
                Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = hdr;
                Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = depth;
                await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
                TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
                bool locked = false;
                void changeControls(object? sender, AvaloniaPropertyChangedEventArgs args)
                {
                    if (!locked && hdr && depth && args.Property == TextBlock.TextProperty &&
                        status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
                    {
                        CheckBox hdrChoice = Required<CheckBox>(sequence, "SequenceHdrColorData");
                        CheckBox depthChoice = Required<CheckBox>(sequence, "SequenceDepthData");
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
                if (hdr && depth)
                {
                    await Assert.That(locked).IsTrue();
                }
                await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
                string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text!;
                if (depth && hdr)
                {
                    allPlanesOutput = output;
                }
                using JsonDocument original = JsonDocument.Parse(await File.ReadAllTextAsync(
                    Path.Combine(colorOnlyOutput, "manifest.json")));
                using JsonDocument captured = JsonDocument.Parse(await File.ReadAllTextAsync(
                    Path.Combine(output, "manifest.json")));
                await Assert.That(Directory.GetFiles(output).Length)
                    .IsEqualTo(4 + (depth ? 3 : 0) + (hdr ? 3 : 0));
                for (int index = 0; index < 3; index++)
                {
                    JsonElement expected = original.RootElement.GetProperty("frames")[index];
                    JsonElement actual = captured.RootElement.GetProperty("frames")[index];
                    await Assert.That(actual.GetProperty("sha256").GetString())
                        .IsEqualTo(expected.GetProperty("sha256").GetString());
                    foreach (string field in new[] { "camera", "display", "settings", "selection" })
                    {
                        await Assert.That(actual.GetProperty(field).GetRawText())
                            .IsEqualTo(expected.GetProperty(field).GetRawText());
                    }
                    await Assert.That(actual.TryGetProperty("deviceDepth", out _)).IsEqualTo(depth);
                    await Assert.That(actual.TryGetProperty("hdrColor", out _)).IsEqualTo(hdr);
                    if (depth)
                    {
                        _ = await AssertPlaneFileAsync(output, actual.GetProperty("deviceDepth"),
                            before.Viewport.Width * before.Viewport.Height * 4);
                        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(
                            output, actual.GetProperty("deviceDepth").GetProperty("file").GetString()!));
                        int surfacePixels = 0;
                        for (int offset = 0; offset < bytes.Length; offset += 4)
                        {
                            if (BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, 4)) < 1)
                            {
                                surfacePixels++;
                            }
                        }
                        await Assert.That(surfacePixels).IsGreaterThan(100);
                    }
                    if (hdr)
                    {
                        _ = await AssertPlaneFileAsync(output, actual.GetProperty("hdrColor"),
                            before.Viewport.Width * before.Viewport.Height * 8);
                        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(
                            output, actual.GetProperty("hdrColor").GetProperty("file").GetString()!));
                        await Assert.That(Convert.ToHexString(bytes.AsSpan(0, 8))).IsEqualTo("003C003C003C003C")
                            .Because("the GPU HDR clear must be actual white before the -6-stop display exposure");
                    }
                }
                await AssertRestoredAsync(session, before);
            }
            finally
            {
                await ((RenderImageSequenceWindow)sequence).CloseAsync();
            }
        }
        await ExerciseExrFormatAsync(window, session, Path.Combine(root, "gpu-exr"), allPlanesOutput!);
    }
}
