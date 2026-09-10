// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseStormSequenceAsync(string root)
    {
        string stagePath = Path.Combine(root, "storm-sequence.usda");
        string sourceText = AnimatedStage.Replace("1: (-1, 0, 0)", "1: (-1, 0.75, 0)", StringComparison.Ordinal);
        await File.WriteAllTextAsync(stagePath, sourceText);
        string outputParent = Path.Combine(root, "storm-output");
        Directory.CreateDirectory(outputParent);
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloaded = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = stagePath,
            StageCameraPath = "/World/Camera",
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                if (!opened.TrySetResult(session))
                {
                    reloaded.TrySetResult(session);
                }
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(Path.Combine(root, "storm-settings"));
        await store.SaveAsync(ViewerSettings.Default with { WindowWidth = 960, WindowHeight = 600 });
        var window = new MainWindow(new RecentStageStore(root), store)
        {
            Width = 960,
            Height = 600
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(35));
            await WaitUntilAsync(() => Required<MenuItem>(window, "CaptureFrameMenuItem").IsEnabled);
            await Assert.That(session.PickingBackend is ViewerRenderBackend { Identity.Kind: RenderBackendKind.Storm })
                .IsTrue();
            StageRenderState before = session.CurrentRenderState;
            await Assert.That((long)before.Viewport.Width * before.Viewport.Height).IsLessThanOrEqualTo(1_048_576L);
            ulong revision = await session.Scheduler.InvokeAsync(static stage => stage.ChangeSerial);
            Required<MenuItem>(window, "RenderImageSequenceMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            RenderImageSequenceWindow sequence = window.OwnedWindows.OfType<RenderImageSequenceWindow>().Single();
            string pngReference;
            try
            {
                Required<TextBox>(sequence, "SequenceStartTime").Text = "1";
                Required<TextBox>(sequence, "SequenceEndTime").Text = "3";
                Required<TextBox>(sequence, "SequenceOutputFolder").Text = outputParent;
                await WaitUntilAsync(() => Required<TextBox>(sequence, "SequenceStartTime").IsKeyboardFocusWithin);
                await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
                await Assert.That(Required<TextBlock>(sequence, "SequenceRenderer").Text)
                    .Contains("native Storm viewport appearance");
                Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = true;
                await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
                Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = false;
                Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => !sequence.IsRunning);
                await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
                string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text ??
                    throw new InvalidOperationException("Storm did not publish its sequence.");
                pngReference = output;
                using JsonDocument manifest = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json")));
                JsonElement frames = manifest.RootElement.GetProperty("frames");
                await Assert.That(frames.GetArrayLength()).IsEqualTo(3);
                for (int index = 0; index < 3; index++)
                {
                    await Assert.That(frames[index].GetProperty("timeCode").GetDouble()).IsEqualTo(index + 1d);
                    await Assert.That(frames[index].GetProperty("width").GetInt32()).IsEqualTo(before.Viewport.Width);
                    await Assert.That(frames[index].GetProperty("height").GetInt32()).IsEqualTo(before.Viewport.Height);
                    await Assert.That(File.Exists(Path.Combine(output, $"frame-{index:D6}.png"))).IsTrue();
                    await Assert.That(frames[index].TryGetProperty("hdrColor", out _)).IsFalse();
                    await Assert.That(frames[index].TryGetProperty("deviceDepth", out _)).IsFalse();
                }
                await Assert.That(frames[0].GetProperty("sha256").GetString())
                    .IsNotEqualTo(frames[1].GetProperty("sha256").GetString());
                await Assert.That(frames[1].GetProperty("sha256").GetString())
                    .IsNotEqualTo(frames[2].GetProperty("sha256").GetString());
                await Assert.That(Directory.GetFiles(output).Length).IsEqualTo(4);
                await Assert.That(manifest.RootElement.GetProperty("diagnostics").ToString())
                    .Contains("VIEWER_STORM_NATIVE_VIEWPORT_SEQUENCE");
                await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
                await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
                await Assert.That(session.CurrentRenderState.RenderSettings).IsEqualTo(before.RenderSettings);
                await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.ChangeSerial)).IsEqualTo(revision);
                await Assert.That(await File.ReadAllTextAsync(stagePath)).IsEqualTo(sourceText);
                await AssertStormSequenceMatchesRestoredFramebufferAsync(session, before, output);
            }
            finally
            {
                await sequence.CloseAsync();
            }
            await ExerciseStormAovOutputsAsync(window, session, outputParent, pngReference);
            await ExerciseStormSequenceTransitionsAsync(window, session, outputParent, reloaded.Task, closed.Task);
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private static async Task ExerciseStormAovOutputsAsync(
        MainWindow window, ViewerStageSession session, string outputParent, string pngReference)
    {
        StageRenderState before = session.CurrentRenderState;
        string? rawReference = null;
        foreach ((bool depth, bool hdr, bool exr) in new[]
        {
            (true, false, false), (false, true, false), (true, true, false), (true, true, true)
        })
        {
            Window opened = await OpenSequenceAsync(window, outputParent);
            var sequence = (RenderImageSequenceWindow)opened;
            try
            {
                Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = depth;
                Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = hdr;
                Required<ComboBox>(sequence, "SequenceHdrFormat").SelectedIndex = exr ? 1 : 0;
                await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
                Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => !sequence.IsRunning);
                await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).StartsWith("Completed");
                string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text ??
                    throw new InvalidOperationException("Storm did not publish its AOV sequence.");
                using JsonDocument manifest = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json")));
                JsonElement frames = manifest.RootElement.GetProperty("frames");
                await Assert.That(frames.GetArrayLength()).IsEqualTo(3);
                for (int index = 0; index < 3; index++)
                {
                    JsonElement frame = frames[index];
                    int width = frame.GetProperty("width").GetInt32();
                    int height = frame.GetProperty("height").GetInt32();
                    await Assert.That(frame.GetProperty("timeCode").GetDouble()).IsEqualTo(index + 1d);
                    byte[] reference = await File.ReadAllBytesAsync(Path.Combine(pngReference, $"frame-{index:D6}.png"));
                    byte[] png = await File.ReadAllBytesAsync(Path.Combine(output, $"frame-{index:D6}.png"));
                    await Assert.That(png.SequenceEqual(reference)).IsTrue()
                        .Because("adding native AOVs must not change native PNG appearance or orientation");
                    await Assert.That(frame.TryGetProperty("deviceDepth", out _)).IsEqualTo(depth);
                    await Assert.That(frame.TryGetProperty("hdrColor", out _)).IsEqualTo(hdr);
                    if (depth)
                    {
                        _ = await AssertPlaneFileAsync(output, frame.GetProperty("deviceDepth"), width * height * 4);
                        byte[] values = await File.ReadAllBytesAsync(
                            Path.Combine(output, $"frame-{index:D6}.device-depth.f32"));
                        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(values)).IsEqualTo(1f);
                        int foreground = 0;
                        long rows = 0;
                        for (int pixel = 0; pixel < width * height; pixel++)
                        {
                            float value = BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(pixel * 4, 4));
                            if (!float.IsFinite(value) || value is < 0 or > 1)
                            {
                                throw new InvalidDataException("Storm exported invalid normalized device depth.");
                            }
                            if (value < 1)
                            {
                                foreground++;
                                rows += pixel / width;
                            }
                        }
                        await Assert.That(foreground).IsGreaterThan(100);
                        if (index == 0)
                        {
                            await Assert.That((double)rows / foreground).IsLessThan(height / 2d)
                                .Because("the asymmetric depth plane must remain top-down");
                        }
                    }
                    if (hdr && !exr)
                    {
                        _ = await AssertPlaneFileAsync(output, frame.GetProperty("hdrColor"), width * height * 8);
                        await Assert.That(frame.GetProperty("hdrColor").GetProperty("displaySelectionIncluded")
                            .GetBoolean()).IsFalse();
                    }
                }
                await Assert.That(manifest.RootElement.GetProperty("diagnostics").ToString())
                    .Contains("STORM_CHILD_AOV_JOB_IMAGE");
                if (depth && hdr && !exr)
                {
                    rawReference = output;
                }
                if (exr)
                {
                    await AssertExrMatchesRawAsync(
                        rawReference ?? throw new InvalidOperationException("Missing raw Storm reference."),
                        output, depth: true);
                }
                await AssertRestoredAsync(session, before);
            }
            finally
            {
                await sequence.CloseAsync();
            }
        }
    }

    private static async Task AssertStormSequenceMatchesRestoredFramebufferAsync(
        ViewerStageSession session, StageRenderState original, string output)
    {
        await Assert.That(original.Time.TimeCode).IsEqualTo(1d);
        var backend = session.PickingBackend as ViewerRenderBackend ??
            throw new InvalidOperationException("The native Storm backend is absent.");
        ViewerFrameCaptureResult captured = await backend.CaptureFrameAsync(
            original.Viewport.Width, original.Viewport.Height, CancellationToken.None);
        PngRgba8Image png = PngRgba8Reader.Decode(
            await File.ReadAllBytesAsync(Path.Combine(output, "frame-000000.png")));
        await Assert.That(captured.RowOrder).IsEqualTo(ViewerFrameRowOrder.BottomUp);
        int stride = captured.Width * 4;
        bool rowsMatch = true;
        for (int row = 0; row < captured.Height; row++)
        {
            rowsMatch &= captured.Rgba.Span.Slice((captured.Height - row - 1) * stride, stride)
                .SequenceEqual(png.Pixels.AsSpan(row * stride, stride));
        }
        await Assert.That(rowsMatch).IsTrue();
        await Assert.That(captured.Rgba.Span.SequenceEqual(png.Pixels)).IsFalse()
            .Because("the asymmetric native framebuffer must be flipped by row order when encoded");
    }

    private static async Task ExerciseStormSequenceTransitionsAsync(
        MainWindow window, ViewerStageSession session, string outputParent,
        Task<ViewerStageSession> reloaded, Task ownerClosed)
    {
        foreach (string action in new[] { "cancel", "double-close", "reload", "owner-close" })
        {
            Required<MenuItem>(window, "RenderImageSequenceMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            RenderImageSequenceWindow sequence = window.OwnedWindows.OfType<RenderImageSequenceWindow>().Single();
            try
            {
                Required<TextBox>(sequence, "SequenceStartTime").Text = "1";
                Required<TextBox>(sequence, "SequenceEndTime").Text = "17";
                Required<TextBox>(sequence, "SequenceOutputFolder").Text = outputParent;
                Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = true;
                Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = true;
                await WaitUntilAsync(() => Required<TextBox>(sequence, "SequenceStartTime").IsKeyboardFocusWithin);
                int published = Directory.GetDirectories(outputParent).Length;
                StageRenderState before = session.CurrentRenderState;
                bool triggered = false;
                TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
                void OnProgress(object? sender, AvaloniaPropertyChangedEventArgs args)
                {
                    if (triggered || args.Property != TextBlock.TextProperty ||
                        status.Text?.StartsWith("Rendering: 1/", StringComparison.Ordinal) != true)
                    {
                        return;
                    }
                    triggered = true;
                    switch (action)
                    {
                        case "cancel":
                            Required<Button>(sequence, "SequenceCancelButton")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            break;
                        case "double-close":
                            sequence.Close();
                            sequence.Close();
                            break;
                        case "reload":
                            Required<MenuItem>(window, "ReloadStageMenuItem")
                                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                            break;
                        case "owner-close":
                            window.Close();
                            break;
                    }
                }
                status.PropertyChanged += OnProgress;
                try
                {
                    Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitUntilAsync(() => triggered && !sequence.IsRunning);
                    await Assert.That(Directory.GetDirectories(outputParent).Length).IsEqualTo(published);
                    await Assert.That(Directory.GetDirectories(outputParent, ".openusd-render-*")).IsEmpty();
                    if (action is "cancel" or "double-close")
                    {
                        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
                        await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
                    }
                    if (action == "reload")
                    {
                        ViewerStageSession prior = session;
                        session = await reloaded.WaitAsync(TimeSpan.FromSeconds(35));
                        await Assert.That(session).IsNotSameReferenceAs(prior);
                        await Assert.That(sequence.IsVisible).IsFalse();
                        await Assert.That(async () => await prior.Scheduler.InvokeAsync(static stage => stage.ChangeSerial))
                            .Throws<ObjectDisposedException>();
                    }
                    if (action == "owner-close")
                    {
                        await ownerClosed.WaitAsync(TimeSpan.FromSeconds(20));
                        await Assert.That(sequence.IsVisible).IsFalse();
                    }
                }
                finally
                {
                    status.PropertyChanged -= OnProgress;
                }
            }
            finally
            {
                await sequence.CloseAsync();
            }
        }
    }
}
