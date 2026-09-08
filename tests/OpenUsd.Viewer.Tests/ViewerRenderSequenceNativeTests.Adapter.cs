// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseSequenceAdapterAsync(string root)
    {
        string output = Path.Combine(root, "adapter-sequence");
        StageRenderState state = StageRenderState.Create(new StageIdentity("adapter.usda"))
            .WithViewport(new ViewportDimensions(1, 2));
        StageRenderState[] states = [state, state.WithTime(new StageTime(1))];
        var request = new RenderDiskJobRequest(output, states);
        var captured = new List<StageRenderState>();
        var warning = new RenderDiagnostic(
            RenderDiagnosticSeverity.Warning, "VIEWER_CAPTURE_TEST_DEGRADED", "The first source frame was degraded.");
        bool restored = false;
        async ValueTask<ViewerFrameCaptureResult> capture(StageRenderState frame, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
            captured.Add(frame);
            byte[] bottomUp = frame.Time.TimeCode == 0
                ? [0, 0, 255, 255, 255, 0, 0, 128]
                : [255, 255, 0, 255, 0, 255, 0, 64];
            RenderDiagnosticsState captureDiagnostics = frame.Time.TimeCode == 0
                ? new RenderDiagnosticsState([warning, warning])
                : RenderDiagnosticsState.Empty;
            return new ViewerFrameCaptureResult(1, 2, bottomUp, ViewerFrameRowOrder.BottomUp, captureDiagnostics);
        }
        async ValueTask restore()
        {
            await Task.Yield();
            await Assert.That(Dispatcher.UIThread.CheckAccess()).IsTrue();
            await Assert.That(Directory.Exists(output)).IsFalse();
            restored = true;
        }
        RenderDiskJobResult result = await ViewerRenderSequenceRunner.ExecuteAsync(
            request, capture, restore, _ => { }, CancellationToken.None);
        await Assert.That(restored).IsTrue();
        await Assert.That(captured.SequenceEqual(states)).IsTrue();
        await Assert.That(result.Frames.Count).IsEqualTo(2);
        await Assert.That(result.Diagnostics.Count).IsEqualTo(1)
            .Because("a later clean frame must not erase an earlier capture diagnostic");
        await Assert.That(result.Diagnostics[0]).IsEqualTo(warning);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(output, "manifest.json")));
        JsonElement manifestDiagnostics = manifest.RootElement.GetProperty("diagnostics");
        await Assert.That(manifestDiagnostics.GetArrayLength()).IsEqualTo(1);
        await Assert.That(manifestDiagnostics[0].GetProperty("code").GetString()).IsEqualTo(warning.Code);
        await Assert.That(manifestDiagnostics[0].GetProperty("severity").GetString()).IsEqualTo("Warning");
        ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(
            Path.Combine(output, "frame-000000.png"), Path.Combine(output, "frame-000001.png"),
            CancellationToken.None);
        await Assert.That(pair.Before.Rgba.SequenceEqual(new byte[] { 255, 0, 0, 128, 0, 0, 255, 255 })).IsTrue();
        await Assert.That(pair.After.Rgba.SequenceEqual(new byte[] { 0, 255, 0, 64, 255, 255, 0, 255 })).IsTrue();
        await ExerciseDiagnosticCompletionAsync(root, result);
        await ExerciseSidecarAdapterAsync(root, states);
        await ExercisePlaneFailureAsync(root, states);
        await ExercisePlaneAdmissionAsync(root);
    }

    private static async Task ExercisePlaneFailureAsync(string root, IReadOnlyList<StageRenderState> states)
    {
        string parent = Path.Combine(root, "failed-sidecars");
        Directory.CreateDirectory(parent);
        string output = Path.Combine(parent, "job");
        var request = new RenderDiskJobRequest(output, states, includeDeviceDepth: true, includeHdrColor: true);
        bool restored = false;
        int rendered = 0;
        float[] depth = [0.25f, 1f];
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 255];
        ValueTask<ViewerFrameCaptureResult> capture(StageRenderState frame, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            rendered++;
            byte[] hdr = [0, frame.Time.TimeCode == 0 ? (byte)0x54 : (byte)0x7C, 0, 0, 0, 0, 0, 0x3C,
                0, 0xB8, 0, 0x80, 0, 0x40, 0, 0x38];
            return ValueTask.FromResult(new ViewerFrameCaptureResult(1, 2, rgba, ViewerFrameRowOrder.TopDown)
            {
                DeviceDepth = new RenderJobDeviceDepth(1, 2, depth),
                HdrColor = new RenderJobHdrColor(1, 2, hdr)
            });
        }
        async ValueTask restore()
        {
            await Assert.That(Directory.Exists(output)).IsFalse();
            restored = true;
        }
        await Assert.That(async () => await ViewerRenderSequenceRunner.ExecuteAsync(
            request, capture, restore, _ => { }, CancellationToken.None)).Throws<InvalidDataException>();
        await Assert.That(rendered).IsEqualTo(2);
        await Assert.That(restored).IsTrue();
        await Assert.That(Directory.EnumerateFileSystemEntries(parent)).IsEmpty()
            .Because("a non-finite second HDR plane must remove every PNG/HDR/depth staging file");
    }

    private static async Task ExercisePlaneAdmissionAsync(string root)
    {
        var owner = new Window { Width = 400, Height = 200 };
        var sequence = new RenderImageSequenceWindow(
            0, 1, "Admission-only 4096 x 4096 viewport",
            outputs => outputs.GetAdmissionUnsupportedReason(new ViewportDimensions(4096, 4096)),
            (_, _, _, _, _) => throw new InvalidOperationException("Admission must not render."));
        try
        {
            owner.Show();
            sequence.Show(owner);
            Required<TextBox>(sequence, "SequenceOutputFolder").Text = root;
            await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
            foreach (string name in new[] { "SequenceHdrColorData", "SequenceDepthData" })
            {
                CheckBox choice = Required<CheckBox>(sequence, name);
                choice.IsChecked = true;
                await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsFalse();
                await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).Contains("64 MiB");
                await Assert.That(Required<TextBlock>(sequence, "SequenceStatus").Text).Contains("20 bytes/pixel");
                await Assert.That(Required<TextBlock>(sequence, "SequenceLimits").Text).Contains("20 bytes/pixel");
                await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text).IsNullOrEmpty();
                choice.IsChecked = false;
                await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
                await Assert.That(Required<TextBlock>(sequence, "SequenceLimits").Text).Contains("4 bytes/pixel");
            }
        }
        finally
        {
            await sequence.CloseAsync();
            owner.Close();
        }
    }

    private static async Task ExerciseSidecarAdapterAsync(string root, IReadOnlyList<StageRenderState> states)
    {
        foreach ((bool depth, bool hdr) in new[] { (true, false), (false, true), (true, true) })
        {
            string output = Path.Combine(root, $"adapter-depth-{depth}-hdr-{hdr}");
            var request = new RenderDiskJobRequest(output, states, depth, hdr);
            byte[] rgba = [0, 0, 255, 255, 255, 0, 0, 128];
            byte[] half = [0, 0x54, 0, 0, 0, 0, 0, 0x3C, 0, 0xB8, 0, 0x80, 0, 0x40, 0, 0x38];
            float[] values = [0.25f, 1f];
            bool restored = false;
            ValueTask<ViewerFrameCaptureResult> capture(StageRenderState frame, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                half[0] = (byte)frame.Time.TimeCode;
                values[0] = frame.Time.TimeCode == 0 ? 0.25f : 0.5f;
                return ValueTask.FromResult(new ViewerFrameCaptureResult(
                    1, 2, rgba, ViewerFrameRowOrder.BottomUp)
                {
                    DeviceDepth = depth ? new RenderJobDeviceDepth(1, 2, values) : null,
                    HdrColor = hdr ? new RenderJobHdrColor(1, 2, half) : null
                });
            }
            async ValueTask restore()
            {
                await Assert.That(Directory.Exists(output)).IsFalse();
                restored = true;
            }
            RenderDiskJobResult result = await ViewerRenderSequenceRunner.ExecuteAsync(
                request, capture, restore, _ => { }, CancellationToken.None);
            await Assert.That(restored).IsTrue();
            await Assert.That(result.Frames.Count).IsEqualTo(2);
            await Assert.That(Directory.GetFiles(output).Length).IsEqualTo(1 + (2 * (1 + (depth ? 1 : 0) +
                (hdr ? 1 : 0))));
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(output, "manifest.json")));
            for (int index = 0; index < 2; index++)
            {
                JsonElement frame = manifest.RootElement.GetProperty("frames")[index];
                await Assert.That(frame.TryGetProperty("deviceDepth", out _)).IsEqualTo(depth);
                await Assert.That(frame.TryGetProperty("hdrColor", out _)).IsEqualTo(hdr);
                if (hdr)
                {
                    byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(output, $"frame-{index:D6}.hdr.rgba16f"));
                    byte[] expected =
                        [(byte)index, 0x54, 0, 0, 0, 0, 0, 0x3C, 0, 0xB8, 0, 0x80, 0, 0x40, 0, 0x38];
                    await Assert.That(bytes.SequenceEqual(expected)).IsTrue()
                        .Because("HDR rows stay top-down and borrowed storage is consumed before the next frame");
                }
                if (depth)
                {
                    byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(
                        output, $"frame-{index:D6}.device-depth.f32"));
                    await Assert.That(bytes.SequenceEqual(index == 0
                        ? new byte[] { 0, 0, 0x80, 0x3E, 0, 0, 0x80, 0x3F }
                        : new byte[] { 0, 0, 0, 0x3F, 0, 0, 0x80, 0x3F })).IsTrue();
                }
            }
        }
    }

    private static async Task ExerciseDiagnosticCompletionAsync(string root, RenderDiskJobResult result)
    {
        var owner = new Window { Width = 400, Height = 200 };
        var completion = new TaskCompletionSource<RenderDiskJobResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerRenderSequenceOutputOptions admitted = default;
        var sequence = new RenderImageSequenceWindow(
            0, 1, "Completed diagnostic source; 4096 x 4096 viewport",
            outputs => outputs.GetAdmissionUnsupportedReason(new ViewportDimensions(4096, 4096)),
            (_, _, outputs, _, token) =>
            {
                admitted = outputs;
                return completion.Task.WaitAsync(token);
            });
        try
        {
            owner.Show();
            sequence.Show(owner);
            Required<TextBox>(sequence, "SequenceOutputFolder").Text = root;
            Required<Button>(sequence, "SequenceRenderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Required<CheckBox>(sequence, "SequenceHdrColorData").IsChecked = true;
            await Assert.That(admitted.HasAdditionalPlanes).IsFalse();
            completion.SetResult(result);
            await WaitUntilAsync(() => !sequence.IsRunning);
            sequence.UpdateContext("Completed diagnostic source; 4096 x 4096 viewport");
            TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
            await Assert.That(status.Text).Contains("1 renderer diagnostic");
            await Assert.That(status.Text).Contains("manifest.json");
            await Assert.That(status.Classes.Contains("viewer-warning")).IsTrue();
            await Assert.That(Required<TextBox>(sequence, "SequenceOutputLocation").Text)
                .IsEqualTo(result.OutputDirectory);
            await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsFalse()
                .Because("a control changed during a job must be revalidated for the next job, not change the result");
        }
        finally
        {
            await sequence.CloseAsync();
            owner.Close();
        }
    }
}
