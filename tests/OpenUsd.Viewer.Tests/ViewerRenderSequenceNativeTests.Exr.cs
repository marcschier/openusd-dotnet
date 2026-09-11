// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerRenderSequenceNativeTests
{
    private static async Task ExerciseExrFormatAsync(
        MainWindow window, ViewerStageSession session, string root, string rawReference)
    {
        StageRenderState before = session.CurrentRenderState;
        foreach (bool depth in new[] { false, true })
        {
            string parent = Directory.CreateDirectory(Path.Combine(root, $"depth-{depth}")).FullName;
            Window sequence = await OpenSequenceAsync(window, parent);
            try
            {
                CheckBox hdrChoice = Required<CheckBox>(sequence, "SequenceHdrColorData");
                ComboBox format = Required<ComboBox>(sequence, "SequenceHdrFormat");
                await Assert.That(hdrChoice.IsChecked).IsFalse();
                await Assert.That(format.SelectedIndex).IsEqualTo(0);
                await Assert.That(format.IsEnabled).IsFalse();
                await Assert.That(AutomationProperties.GetName(format)).IsEqualTo("HDR color output format");
                hdrChoice.IsChecked = true;
                await Assert.That(format.IsEnabled).IsTrue();
                format.SelectedIndex = 1;
                Required<CheckBox>(sequence, "SequenceDepthData").IsChecked = depth;
                await Assert.That(Required<TextBlock>(sequence, "SequenceOutputScope").Text).Contains(".hdr.exr");
                await Assert.That(Required<TextBlock>(sequence, "SequenceLimits").Text).Contains("20 bytes/pixel");
                await Assert.That(Required<TextBlock>(sequence, "SequenceLimits").Text)
                    .Contains("outside these quotas");

                TextBlock status = Required<TextBlock>(sequence, "SequenceStatus");
                bool locked = false;
                void changeFormat(object? sender, AvaloniaPropertyChangedEventArgs args)
                {
                    if (!locked && args.Property == TextBlock.TextProperty &&
                        status.Text?.StartsWith("Rendering: 1/3", StringComparison.Ordinal) == true)
                    {
                        locked = !format.IsEffectivelyEnabled;
                        format.SelectedIndex = 0;
                    }
                }
                status.PropertyChanged += changeFormat;
                try
                {
                    await Assert.That(Required<Button>(sequence, "SequenceRenderButton").IsEnabled).IsTrue();
                    Required<Button>(sequence, "SequenceRenderButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitUntilAsync(() => !((RenderImageSequenceWindow)sequence).IsRunning);
                }
                finally
                {
                    status.PropertyChanged -= changeFormat;
                }
                await Assert.That(locked).IsTrue();
                await Assert.That(status.Text).StartsWith("Completed");
                await Assert.That(status.Text).Contains("3 HDR EXR");
                string output = Required<TextBox>(sequence, "SequenceOutputLocation").Text ??
                    throw new InvalidOperationException("The EXR sequence did not publish an output.");
                await AssertExrMatchesRawAsync(rawReference, output, depth);
                await AssertRestoredAsync(session, before);
            }
            finally
            {
                await ((RenderImageSequenceWindow)sequence).CloseAsync();
            }
        }
    }

    private static async Task AssertExrMatchesRawAsync(string raw, string exr, bool depth)
    {
        using JsonDocument reference = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(raw, "manifest.json")));
        using JsonDocument actual = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(exr, "manifest.json")));
        JsonElement expectedFrames = reference.RootElement.GetProperty("frames");
        JsonElement frames = actual.RootElement.GetProperty("frames");
        await Assert.That(frames.GetArrayLength()).IsEqualTo(3);
        await Assert.That(Directory.GetFiles(exr).Length).IsEqualTo(depth ? 10 : 7);
        for (int index = 0; index < 3; index++)
        {
            JsonElement expected = expectedFrames[index];
            JsonElement frame = frames[index];
            await Assert.That(frame.GetProperty("sha256").GetString())
                .IsEqualTo(expected.GetProperty("sha256").GetString());
            await Assert.That(frame.GetProperty("timeCode").GetDouble())
                .IsEqualTo(expected.GetProperty("timeCode").GetDouble());
            foreach (string field in new[] { "camera", "display", "settings", "selection" })
            {
                await Assert.That(frame.GetProperty(field).GetRawText())
                    .IsEqualTo(expected.GetProperty(field).GetRawText());
            }
            JsonElement hdr = frame.GetProperty("hdrColor");
            _ = await AssertPlaneFileAsync(exr, hdr, null);
            await Assert.That(hdr.GetProperty("format").GetString()).IsEqualTo("openexr-rgba16float");
            await Assert.That(hdr.GetProperty("rowOrder").GetString()).IsEqualTo("top-down");
            await Assert.That(hdr.GetProperty("displaySelectionIncluded").GetBoolean()).IsFalse();
            await Assert.That(hdr.GetProperty("primaries").GetString()).IsEqualTo("unspecified");
            await Assert.That(hdr.GetProperty("alphaAssociation").GetString()).IsEqualTo("unspecified");
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(exr, hdr.GetProperty("file").GetString()!));
            byte[] decoded = ExrScanlineOracle.Read(encoded,
                frame.GetProperty("width").GetInt32(), frame.GetProperty("height").GetInt32());
            byte[] half = await File.ReadAllBytesAsync(Path.Combine(raw, $"frame-{index:D6}.hdr.rgba16f"));
            await Assert.That(decoded.SequenceEqual(half)).IsTrue()
                .Because("the Viewer EXR must retain every raw half bit, including row order and stored alpha");
            await Assert.That(frame.TryGetProperty("deviceDepth", out _)).IsEqualTo(depth);
            if (depth)
            {
                await Assert.That(frame.GetProperty("deviceDepth").GetProperty("sha256").GetString())
                    .IsEqualTo(expected.GetProperty("deviceDepth").GetProperty("sha256").GetString());
            }
        }
    }
}
