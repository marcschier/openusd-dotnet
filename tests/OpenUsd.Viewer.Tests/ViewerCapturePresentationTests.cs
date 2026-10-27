// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCapturePresentationTests
{
    private static readonly byte[] BeforeRgba =
    [
        255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64,
        10, 20, 30, 40, 200, 100, 50, 0, 1, 2, 3, 255,
    ];

    private static readonly byte[] AfterRgba = [20, 40, 80, 255, 200, 100, 0, 255];
    private static readonly string[] ComparisonActionNames =
    [
        "ComparisonMode", "OpenPresentationButton", "SavePresentationButton",
        "CompareAgainButton", "CancelComparisonButton", "CloseComparisonButton"
    ];

    [Test]
    [NotInParallel]
    [Arguments(false, "success", "cancel")]
    [Arguments(true, "success", "cancel")]
    [Arguments(false, "success", "close")]
    [Arguments(true, "success", "close")]
    [Arguments(false, "success", "status")]
    [Arguments(true, "success", "status")]
    [Arguments(false, "success", "other")]
    [Arguments(true, "success", "other")]
    [Arguments(false, "cancelled", "cancel")]
    [Arguments(true, "cancelled", "cancel")]
    [Arguments(false, "cancelled", "close")]
    [Arguments(true, "cancelled", "close")]
    [Arguments(false, "cancelled", "status")]
    [Arguments(true, "cancelled", "status")]
    [Arguments(false, "cancelled", "other")]
    [Arguments(true, "cancelled", "other")]
    [Arguments(false, "failure", "cancel")]
    [Arguments(true, "failure", "cancel")]
    [Arguments(false, "failure", "close")]
    [Arguments(true, "failure", "close")]
    [Arguments(false, "failure", "status")]
    [Arguments(true, "failure", "status")]
    [Arguments(false, "failure", "other")]
    [Arguments(true, "failure", "other")]
    public async Task ComparisonCompletionPreservesMovedFocusAndRestoresOnlyOwnedCancelFocus(
        bool dark, string outcome, string destination)
    {
        using var fixture = await Fixture.CreateAsync();
        await RunUiAsync(async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var window = new CaptureComparisonWindow(async (before, after, token) =>
            {
                if (++calls == 1)
                {
                    entered.TrySetResult();
                    await release.Task;
                    token.ThrowIfCancellationRequested();
                    if (outcome == "failure")
                    {
                        throw new IOException("Controlled delayed comparison refusal.");
                    }
                }
                return await ViewerCaptureComparison.LoadAsync(before, after, token);
            })
            {
                Width = 600,
                Height = 360,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var otherButton = new Button { Content = "Other window action" };
            var other = new Window { Content = otherButton, Width = 240, Height = 160 };
            window.Show();
            Task? pending = null;
            try
            {
                pending = window.LoadPairAsync(fixture.Before, fixture.After);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Button cancel = Required<Button>(window, "CancelComparisonButton");
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                if (outcome == "cancelled")
                {
                    cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Assert.That(pending.IsCompleted).IsFalse();
                }
                Control expectedFocus = destination switch
                {
                    "cancel" => Required<Button>(window, "CompareAgainButton"),
                    "close" => Required<Button>(window, "CloseComparisonButton"),
                    "status" => Required<ScrollViewer>(window, "ComparisonStatusScroll"),
                    "other" => otherButton,
                    _ => throw new ArgumentOutOfRangeException(nameof(destination))
                };
                if (destination == "other")
                {
                    other.Show();
                    await Assert.That(otherButton.Focus()).IsTrue();
                }
                else if (destination != "cancel")
                {
                    await Assert.That(expectedFocus.Focus()).IsTrue();
                }
                release.TrySetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                window.UpdateLayout();
                await Assert.That(expectedFocus.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(cancel.IsEnabled).IsFalse();
                if (outcome == "success")
                {
                    await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
                        .IsEqualTo(Convert.ToHexString(BeforeRgba));
                }
                else
                {
                    await AssertNoPairAsync(window);
                    await Assert.That(Required<TextBlock>(window, "ComparisonStatus").Text)
                        .Contains(outcome == "cancelled" ? "cancelled" : "Controlled delayed comparison refusal.");
                }
                other.Close();
                await Assert.That(Required<Button>(window, "CompareAgainButton").Focus()).IsTrue();
                await window.LoadPairAsync(fixture.Before, fixture.After);
                await Assert.That(Required<Button>(window, "CompareAgainButton").IsKeyboardFocusWithin).IsTrue();
                await Assert.That(calls).IsEqualTo(2);
                await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
                    .IsEqualTo(Convert.ToHexString(BeforeRgba));
                await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"), 2, 1))
                    .IsEqualTo(Convert.ToHexString(AfterRgba));
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Directory.GetFiles(Path.Combine(fixture.Root, "out"))).IsEmpty();
            }
            finally
            {
                release.TrySetResult();
                if (pending is not null)
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(5));
                }
                other.Close();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(Required<Image>(window, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(window, "AfterImage").Source).IsNull();
        });
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LongComparisonRefusalKeepsActionsAndFullContextKeyboardReachableThenRetries(bool dark)
    {
        using var fixture = await Fixture.CreateAsync();
        string refusal = string.Join("\n", Enumerable.Range(0, 40)
            .Select(index => $"Capture reader refusal line {index:D2}; original files are unchanged."));
        int calls = 0;
        await RunUiAsync(async () =>
        {
            var window = new CaptureComparisonWindow((before, after, token) =>
                ++calls == 1
                    ? Task.FromException<ViewerCapturePair>(new IOException(refusal))
                    : ViewerCaptureComparison.LoadAsync(before, after, token))
            {
                Width = 600,
                Height = 360,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await window.LoadPairAsync(fixture.Before, fixture.After);
                window.UpdateLayout();
                TextBlock status = Required<TextBlock>(window, "ComparisonStatus");
                await Assert.That(status.Text).IsEqualTo($"Could not compare captures: {refusal}");
                foreach (string name in ComparisonActionNames)
                {
                    Control action = Required<Control>(window, name);
                    Point? origin = action.TranslatePoint(default, window);
                    await Assert.That(origin).IsNotNull();
                    await Assert.That(origin!.Value.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.X + action.Bounds.Width)
                        .IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Value.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.Y + action.Bounds.Height)
                        .IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                ScrollViewer details = Required<ScrollViewer>(window, "ComparisonStatusScroll");
                await Assert.That(Required<Button>(window, "OpenPresentationButton").Focus()).IsTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                await Assert.That(ReferenceEquals(window.FocusManager?.GetFocusedElement(), details)).IsTrue();
                await Assert.That(details.Viewport.Height).IsGreaterThan(0);
                await Assert.That(details.Viewport.Height).IsLessThanOrEqualTo(80);
                int pages = (int)Math.Ceiling(details.Extent.Height / details.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
                    window.UpdateLayout();
                }
                await Assert.That(details.Offset.Y).IsEqualTo(0);
                double previous = details.Offset.Y;
                for (int page = 0; page < pages; page++)
                {
                    window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                    window.UpdateLayout();
                    await Assert.That(details.Offset.Y).IsGreaterThanOrEqualTo(previous);
                    await Assert.That(details.Offset.Y - previous).IsLessThanOrEqualTo(details.Viewport.Height);
                    previous = details.Offset.Y;
                }
                await Assert.That(details.Offset.Y).IsEqualTo(details.Extent.Height - details.Viewport.Height);
                await Assert.That(status.Text).IsEqualTo($"Could not compare captures: {refusal}");
                await AssertNoPairAsync(window);
                await Assert.That(calls).IsEqualTo(1);
                await window.LoadPairAsync(fixture.Before, fixture.After);
                window.UpdateLayout();
                await Assert.That(details.Offset.Y).IsEqualTo(0);
                await Assert.That(status.Classes.Contains("viewer-error")).IsFalse();
                await Assert.That(calls).IsEqualTo(2);
                await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
                    .IsEqualTo(Convert.ToHexString(BeforeRgba));
                await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"), 2, 1))
                    .IsEqualTo(Convert.ToHexString(AfterRgba));
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(Required<Image>(window, "BeforeImage").Source).IsNull();
                await Assert.That(Required<Image>(window, "AfterImage").Source).IsNull();
                await Assert.That(Directory.GetFiles(Path.Combine(fixture.Root, "out"))).IsEmpty();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        });
        ViewerCapturePair retained = await ViewerCaptureComparison.LoadAsync(
            fixture.Before, fixture.After, CancellationToken.None);
        await Assert.That(retained.Before.Rgba.SequenceEqual(BeforeRgba)).IsTrue();
        await Assert.That(retained.After.Rgba.SequenceEqual(AfterRgba)).IsTrue();
    }

    [Test]
    public async Task VersionTwoReviewNotesSurviveOpeningAndResavingTheVerifiedPair()
    {
        using var fixture = await Fixture.CreateAsync();
        JsonObject manifest = ValidManifest(fixture.Before, fixture.After, PixelHash(BeforeRgba), PixelHash(AfterRgba));
        const string notes = "Review A -> B.\n<plain text> \u00e9 \ud83d\ude80";
        manifest["version"] = 2;
        manifest["notes"] = notes;
        await File.WriteAllTextAsync(fixture.Manifest, manifest.ToJsonString());

        ViewerCapturePresentation reopened =
            await ViewerCapturePresentationFile.LoadAsync(fixture.Manifest, CancellationToken.None);
        string saved = Path.Combine(fixture.Root, "out", "reviewed.usdcompare.json");
        await ViewerCapturePresentationFile.SaveAsync(saved, reopened, CancellationToken.None);

        JsonObject resaved = JsonNode.Parse(await File.ReadAllTextAsync(saved))!.AsObject();
        await Assert.That(resaved["notes"]!.GetValue<string>()).IsEqualTo(notes);
        await Assert.That(resaved["before"]!["pixelSha256"]!.GetValue<string>()).IsEqualTo(PixelHash(BeforeRgba));
        await Assert.That(resaved["after"]!["pixelSha256"]!.GetValue<string>()).IsEqualTo(PixelHash(AfterRgba));
    }

    [Test]
    public async Task VersionOnePresentationReopensWithEmptyNotesAndUpgradesWithoutChangingPixels()
    {
        using var fixture = await Fixture.CreateAsync();
        JsonObject manifest = ValidManifest(fixture.Before, fixture.After, PixelHash(BeforeRgba), PixelHash(AfterRgba));
        await File.WriteAllTextAsync(fixture.Manifest, manifest.ToJsonString());

        ViewerCapturePresentation opened =
            await ViewerCapturePresentationFile.LoadAsync(fixture.Manifest, CancellationToken.None);
        string upgraded = Path.Combine(fixture.Root, "out", "upgraded.usdcompare.json");
        await ViewerCapturePresentationFile.SaveAsync(upgraded, opened, CancellationToken.None);
        ViewerCapturePresentation reopened =
            await ViewerCapturePresentationFile.LoadAsync(upgraded, CancellationToken.None);

        await Assert.That(opened.Notes).IsEqualTo(string.Empty);
        await Assert.That(reopened.Notes).IsEqualTo(string.Empty);
        await Assert.That(Convert.ToHexString(reopened.Images.Before.Rgba)).IsEqualTo(Convert.ToHexString(BeforeRgba));
        await Assert.That(Convert.ToHexString(reopened.Images.After.Rgba)).IsEqualTo(Convert.ToHexString(AfterRgba));
        JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(upgraded))!.AsObject();
        await Assert.That(saved["version"]!.GetValue<int>()).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2047)]
    [Arguments(2048)]
    public async Task ReviewNotesRoundTripThroughTheInclusiveLengthBoundary(int length)
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation presentation = await fixture.LoadPresentationAsync(1);
        string notes = new('\u00e9', length);
        await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, presentation with { Notes = notes }, CancellationToken.None);
        ViewerCapturePresentation reopened =
            await ViewerCapturePresentationFile.LoadAsync(fixture.Manifest, CancellationToken.None);

        await Assert.That(reopened.Notes).IsEqualTo(notes);
        await Assert.That(reopened.Notes.Length).IsEqualTo(length);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("null")]
    [Arguments("number")]
    [Arguments("oversized")]
    [Arguments("nul")]
    [Arguments("duplicate")]
    [Arguments("high-surrogate")]
    [Arguments("low-surrogate")]
    [Arguments("legacy-with-notes")]
    public async Task InvalidManifestNotesAreRejectedBeforeImageLoading(string defect)
    {
        using var fixture = await Fixture.CreateAsync();
        JsonObject manifest = ValidManifest(
            Path.Combine(fixture.Root, "absent.png"), fixture.After, PixelHash(BeforeRgba), PixelHash(AfterRgba));
        manifest["version"] = 2;
        manifest["notes"] = "plain";
        switch (defect)
        {
            case "missing":
                manifest.Remove("notes");
                break;
            case "null":
                manifest["notes"] = null;
                break;
            case "number":
                manifest["notes"] = 42;
                break;
            case "oversized":
                manifest["notes"] = new string('x', 2049);
                break;
            case "nul":
                manifest["notes"] = "before\0after";
                break;
            case "legacy-with-notes":
                manifest["version"] = 1;
                break;
        }
        string json = manifest.ToJsonString();
        if (defect == "duplicate")
        {
            json = json.Replace("\"notes\":\"plain\"", "\"notes\":\"plain\",\"notes\":\"duplicate\"",
                StringComparison.Ordinal);
        }
        else if (defect is "high-surrogate" or "low-surrogate")
        {
            json = json.Replace("\"notes\":\"plain\"",
                defect == "high-surrogate" ? "\"notes\":\"\\uD800\"" : "\"notes\":\"\\uDC00\"",
                StringComparison.Ordinal);
        }
        await File.WriteAllTextAsync(fixture.Manifest, json);

        await Assert.That(async () => await ViewerCapturePresentationFile.LoadAsync(
            fixture.Manifest, CancellationToken.None)).ThrowsExactly<InvalidDataException>();
    }

    [Test]
    [Arguments("oversized")]
    [Arguments("nul")]
    [Arguments("high-surrogate")]
    [Arguments("low-surrogate")]
    [Arguments("reversed-surrogates")]
    [Arguments("null")]
    public async Task InvalidInMemoryNotesPublishNoFile(string defect)
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation presentation = await fixture.LoadPresentationAsync(0);
        string notes = defect switch
        {
            "oversized" => new string('x', 2049),
            "nul" => "before\0after",
            "high-surrogate" => "\ud800",
            "low-surrogate" => "\udc00",
            "reversed-surrogates" => "\udc00\ud800",
            _ => null!
        };

        await Assert.That(async () => await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, presentation with { Notes = notes }, CancellationToken.None))
            .ThrowsExactly<InvalidDataException>();
        await Assert.That(fixture.ManifestDirectoryEntries()).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SavedPresentationReopensExactDecodedPairAndModeFromSeparateDirectories(int mode)
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation presentation = await fixture.LoadPresentationAsync(mode);
        byte[] beforeFile = await File.ReadAllBytesAsync(fixture.Before);
        byte[] afterFile = await File.ReadAllBytesAsync(fixture.After);

        await ViewerCapturePresentationFile.SaveAsync(fixture.Manifest, presentation, CancellationToken.None);
        ViewerCapturePresentation reopened =
            await ViewerCapturePresentationFile.LoadAsync(fixture.Manifest, CancellationToken.None);

        await Assert.That(Path.GetDirectoryName(fixture.Before)).IsNotEqualTo(Path.GetDirectoryName(fixture.After));
        await Assert.That(reopened.DisplayMode).IsEqualTo(mode);
        await Assert.That(reopened.BeforePath).IsEqualTo(fixture.Before);
        await Assert.That(reopened.AfterPath).IsEqualTo(fixture.After);
        await Assert.That(reopened.Images.Before.Width).IsEqualTo(3);
        await Assert.That(reopened.Images.Before.Height).IsEqualTo(2);
        await Assert.That(reopened.Images.After.Width).IsEqualTo(2);
        await Assert.That(reopened.Images.After.Height).IsEqualTo(1);
        await Assert.That(Convert.ToHexString(reopened.Images.Before.Rgba)).IsEqualTo(Convert.ToHexString(BeforeRgba));
        await Assert.That(Convert.ToHexString(reopened.Images.After.Rgba)).IsEqualTo(Convert.ToHexString(AfterRgba));

        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Manifest))!.AsObject();
        await Assert.That(manifest["version"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(manifest["notes"]!.GetValue<string>()).IsEqualTo(string.Empty);
        await Assert.That(manifest["displayMode"]!.GetValue<int>()).IsEqualTo(mode);
        await Assert.That(manifest["before"]!["path"]!.GetValue<string>()).IsEqualTo(fixture.Before);
        await Assert.That(manifest["after"]!["width"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(manifest["after"]!["height"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(manifest["before"]!["pixelSha256"]!.GetValue<string>()).IsEqualTo(PixelHash(BeforeRgba));
        await Assert.That(manifest["before"]!["pixelSha256"]!.GetValue<string>())
            .IsNotEqualTo(Convert.ToHexString(SHA256.HashData(beforeFile)));
        await Assert.That(manifest["after"]!["pixelSha256"]!.GetValue<string>()).IsEqualTo(PixelHash(AfterRgba));

        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(fixture.Before)))
            .IsEqualTo(Convert.ToHexString(beforeFile));
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(fixture.After)))
            .IsEqualTo(Convert.ToHexString(afterFile));
        await Assert.That(fixture.ManifestDirectoryEntries()).IsEquivalentTo(["comparison.usdcompare.json"]);
    }

    [Test]
    [Arguments("before-pixel")]
    [Arguments("after-pixel")]
    [Arguments("before-dimension-same-rgba")]
    [Arguments("after-dimension-same-rgba")]
    public async Task ReopenRejectsHistoricalCaptureWhosePixelsOrDimensionsChanged(string change)
    {
        using var fixture = await Fixture.CreateAsync();
        await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, await fixture.LoadPresentationAsync(0), CancellationToken.None);
        byte[] mutated = change.StartsWith("before", StringComparison.Ordinal) ? [.. BeforeRgba] : [.. AfterRgba];
        if (change.EndsWith("pixel", StringComparison.Ordinal))
        {
            mutated[^2] ^= 1;
        }
        switch (change)
        {
            case "before-pixel":
                WritePng(fixture.Before, 3, 2, mutated);
                break;
            case "after-pixel":
                ViewerFrameBitmapWriter.WriteBmp(fixture.After, 2, 1, mutated);
                break;
            case "before-dimension-same-rgba":
                WritePng(fixture.Before, 2, 3, mutated);
                break;
            default:
                ViewerFrameBitmapWriter.WriteBmp(fixture.After, 1, 2, mutated);
                break;
        }

        InvalidDataException? failure = await LoadFailureAsync<InvalidDataException>(fixture.Manifest);

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("historical capture changed");
    }

    [Test]
    public async Task ReopenAcceptsSameDecodedRgbaEncodedWithDifferentFileBytes()
    {
        using var fixture = await Fixture.CreateAsync();
        await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, await fixture.LoadPresentationAsync(1), CancellationToken.None);
        byte[] original = await File.ReadAllBytesAsync(fixture.Before);
        byte[] reencoded = SplitIdatIntoSingleByteChunks(original);
        await File.WriteAllBytesAsync(fixture.Before, reencoded);

        ViewerCapturePresentation reopened =
            await ViewerCapturePresentationFile.LoadAsync(fixture.Manifest, CancellationToken.None);

        await Assert.That(reencoded.Length).IsGreaterThan(original.Length);
        await Assert.That(reopened.DisplayMode).IsEqualTo(1);
        await Assert.That(Convert.ToHexString(reopened.Images.Before.Rgba)).IsEqualTo(Convert.ToHexString(BeforeRgba));
        await Assert.That(Convert.ToHexString(reopened.Images.After.Rgba)).IsEqualTo(Convert.ToHexString(AfterRgba));
    }

    [Test]
    public async Task ReopenWithMissingHistoricalCaptureFailsWithIOException()
    {
        using var fixture = await Fixture.CreateAsync();
        await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, await fixture.LoadPresentationAsync(0), CancellationToken.None);
        File.Delete(fixture.After);

        IOException? failure = await LoadFailureAsync<IOException>(fixture.Manifest);

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("after.bmp");
    }

    [Test]
    [Arguments("not-json")]
    [Arguments("array-root")]
    [Arguments("version-3")]
    [Arguments("version-string")]
    [Arguments("mode-3")]
    [Arguments("mode-negative")]
    [Arguments("mode-fraction")]
    [Arguments("unknown-root-field")]
    [Arguments("duplicate-root-field")]
    [Arguments("missing-root-field")]
    [Arguments("capture-not-object")]
    [Arguments("unknown-capture-field")]
    [Arguments("duplicate-capture-field")]
    [Arguments("missing-checksum")]
    [Arguments("null-path")]
    [Arguments("numeric-path")]
    [Arguments("relative-path")]
    [Arguments("nul-path")]
    [Arguments("oversized-path")]
    [Arguments("network-path")]
    [Arguments("device-path")]
    [Arguments("zero-width")]
    [Arguments("zero-height")]
    [Arguments("oversized-width")]
    [Arguments("oversized-height")]
    [Arguments("pixel-budget")]
    [Arguments("string-width")]
    [Arguments("short-checksum")]
    [Arguments("non-hex-checksum")]
    [Arguments("numeric-checksum")]
    [Arguments("empty-file")]
    public async Task MalformedManifestFailsExplicitlyBeforeLoadingAnyCapture(string defect)
    {
        using var fixture = await Fixture.CreateAsync();
        // Capture paths do not exist: reaching image loading would surface FileNotFoundException instead.
        string missingBefore = Path.Combine(fixture.Root, "missing", "before.png");
        string missingAfter = Path.Combine(fixture.Root, "missing", "after.bmp");
        JsonObject root = ValidManifest(missingBefore, missingAfter, PixelHash(BeforeRgba), PixelHash(AfterRgba));
        JsonObject before = root["before"]!.AsObject();
        string? text = null;
        switch (defect)
        {
            case "not-json":
                text = "{\"version\":1,";
                break;
            case "array-root":
                text = "[]";
                break;
            case "version-3":
                root["version"] = 3;
                break;
            case "version-string":
                root["version"] = "1";
                break;
            case "mode-3":
                root["displayMode"] = 3;
                break;
            case "mode-negative":
                root["displayMode"] = -1;
                break;
            case "mode-fraction":
                root["displayMode"] = 1.5;
                break;
            case "unknown-root-field":
                root["note"] = "x";
                break;
            case "duplicate-root-field":
                text = root.ToJsonString().Replace("\"displayMode\":0,", "\"displayMode\":0,\"displayMode\":0,",
                    StringComparison.Ordinal);
                break;
            case "missing-root-field":
                root.Remove("after");
                break;
            case "capture-not-object":
                root["before"] = 5;
                break;
            case "unknown-capture-field":
                before["mtime"] = 0;
                break;
            case "duplicate-capture-field":
                text = root.ToJsonString().Replace(
                    "\"width\":3,", "\"width\":3,\"width\":3,", StringComparison.Ordinal);
                break;
            case "missing-checksum":
                root["after"]!.AsObject().Remove("pixelSha256");
                break;
            case "null-path":
                before["path"] = null;
                break;
            case "numeric-path":
                before["path"] = 7;
                break;
            case "relative-path":
                before["path"] = Path.Combine("captures", "before.png");
                break;
            case "nul-path":
                before["path"] = missingBefore + "\0.png";
                break;
            case "oversized-path":
                before["path"] = PathOfLength(fixture.Root, 4097);
                break;
            case "network-path":
                before["path"] = @"\\invalid.example\share\capture.png";
                break;
            case "device-path":
                before["path"] = @"\\.\pipe\capture.png";
                break;
            case "zero-width":
                before["width"] = 0;
                break;
            case "zero-height":
                before["height"] = 0;
                break;
            case "oversized-width":
                before["width"] = ViewerCaptureComparison.MaximumDimension + 1;
                break;
            case "oversized-height":
                before["height"] = ViewerCaptureComparison.MaximumDimension + 1;
                break;
            case "pixel-budget":
                before["width"] = 4097;
                before["height"] = 4096;
                break;
            case "string-width":
                before["width"] = "3";
                break;
            case "short-checksum":
                before["pixelSha256"] = PixelHash(BeforeRgba)[1..];
                break;
            case "non-hex-checksum":
                before["pixelSha256"] = "G" + PixelHash(BeforeRgba)[1..];
                break;
            case "numeric-checksum":
                before["pixelSha256"] = 1;
                break;
            case "empty-file":
                text = string.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect));
        }
        await File.WriteAllTextAsync(fixture.Manifest, text ?? root.ToJsonString());

        Exception? failure = await LoadFailureAsync<Exception>(fixture.Manifest);

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.GetType()).IsEqualTo(typeof(InvalidDataException));
    }

    [Test]
    [Arguments(4096, true)]
    [Arguments(4097, false)]
    public async Task ManifestCapturePathLengthBoundaryIsAdmittedBeforeImageLoading(int length, bool admitted)
    {
        using var fixture = await Fixture.CreateAsync();
        string longPath = PathOfLength(fixture.Root, length);
        JsonObject root = ValidManifest(longPath, fixture.After, PixelHash(BeforeRgba), PixelHash(AfterRgba));
        await File.WriteAllTextAsync(fixture.Manifest, root.ToJsonString());

        Exception? failure = await LoadFailureAsync<Exception>(fixture.Manifest);

        await Assert.That(longPath.Length).IsEqualTo(length);
        await Assert.That(failure is InvalidDataException).IsEqualTo(!admitted);
        await Assert.That(failure is IOException).IsEqualTo(admitted);
    }

    [Test]
    [Arguments(ViewerCapturePresentationFile.MaximumBytes, true)]
    [Arguments(ViewerCapturePresentationFile.MaximumBytes + 1, false)]
    public async Task ManifestSizeLimitIsInclusiveAndChecksumsAreCaseInsensitive(int size, bool admitted)
    {
        using var fixture = await Fixture.CreateAsync();
        string json = ValidManifest(fixture.Before, fixture.After,
            PixelHash(BeforeRgba).ToLowerInvariant(), PixelHash(AfterRgba).ToLowerInvariant()).ToJsonString();
        byte[] bytes = Encoding.UTF8.GetBytes(json.PadRight(size));
        await File.WriteAllBytesAsync(fixture.Manifest, bytes);

        Exception? failure = await LoadFailureAsync<Exception>(fixture.Manifest);

        await Assert.That(bytes.Length).IsEqualTo(size);
        if (admitted)
        {
            await Assert.That(failure).IsNull();
        }
        else
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.GetType()).IsEqualTo(typeof(InvalidDataException));
            await Assert.That(failure.Message).Contains("32 KiB");
        }
    }

    [Test]
    [Arguments("mode-3")]
    [Arguments("mode-negative")]
    [Arguments("relative-before")]
    [Arguments("relative-after")]
    [Arguments("rgba-length")]
    [Arguments("zero-width")]
    [Arguments("zero-height")]
    [Arguments("oversized-width")]
    [Arguments("oversized-height")]
    [Arguments("pixel-budget")]
    public async Task InvalidInMemoryPresentationIsRejectedWithoutWritingAnyFile(string defect)
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation valid = await fixture.LoadPresentationAsync(0);
        ViewerCapturePair images = valid.Images;
        ViewerCapturePresentation invalid = defect switch
        {
            "mode-3" => valid with { DisplayMode = 3 },
            "mode-negative" => valid with { DisplayMode = -1 },
            "relative-before" => valid with { BeforePath = "before.png" },
            "relative-after" => valid with { AfterPath = Path.Combine("b", "after.bmp") },
            "rgba-length" => valid with
            {
                Images = images with { After = images.After with { Rgba = AfterRgba[..^1] } },
            },
            "zero-width" => valid with { Images = images with { Before = new ViewerCaptureImage(0, 2, []) } },
            "zero-height" => valid with { Images = images with { Before = new ViewerCaptureImage(2, 0, []) } },
            "oversized-width" => valid with { Images = images with { Before = new ViewerCaptureImage(8193, 1, []) } },
            "oversized-height" => valid with { Images = images with { Before = new ViewerCaptureImage(1, 8193, []) } },
            _ => valid with { Images = images with { Before = new ViewerCaptureImage(4097, 4096, []) } },
        };

        await Assert.That(async () => await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, invalid, CancellationToken.None)).ThrowsExactly<InvalidDataException>();

        await Assert.That(fixture.ManifestDirectoryEntries()).IsEmpty();
    }

    [Test]
    public async Task EscapedPathsCannotPublishAnOversizedManifest()
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation valid = await fixture.LoadPresentationAsync(0);
        string prefix = Path.GetPathRoot(fixture.Before)!;
        string escapedPath = prefix + new string('\u00e9', 4096 - prefix.Length);

        await Assert.That(async () => await ViewerCapturePresentationFile.SaveAsync(fixture.Manifest,
            valid with { BeforePath = escapedPath, AfterPath = escapedPath }, CancellationToken.None))
            .ThrowsExactly<InvalidDataException>();

        await Assert.That(fixture.ManifestDirectoryEntries()).IsEmpty();
    }

    [Test]
    public async Task SaveNeverReplacesAnExistingDestinationOrLeavesTemporaryFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(fixture.Manifest, "previous comparison");

        await Assert.That(async () => await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, await fixture.LoadPresentationAsync(2), CancellationToken.None))
            .Throws<IOException>();

        await Assert.That(await File.ReadAllTextAsync(fixture.Manifest)).IsEqualTo("previous comparison");
        await Assert.That(fixture.ManifestDirectoryEntries()).IsEquivalentTo(["comparison.usdcompare.json"]);
    }

    [Test]
    public async Task PreCancelledSaveAndLoadPublishNothing()
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation presentation = await fixture.LoadPresentationAsync(1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.That(async () => await ViewerCapturePresentationFile.SaveAsync(
            fixture.Manifest, presentation, cancelled.Token)).Throws<OperationCanceledException>();
        await Assert.That(fixture.ManifestDirectoryEntries()).IsEmpty();

        await ViewerCapturePresentationFile.SaveAsync(fixture.Manifest, presentation, CancellationToken.None);
        string saved = await File.ReadAllTextAsync(fixture.Manifest);
        await Assert.That(async () => await ViewerCapturePresentationFile.LoadAsync(
            fixture.Manifest, cancelled.Token)).Throws<OperationCanceledException>();
        await Assert.That(await File.ReadAllTextAsync(fixture.Manifest)).IsEqualTo(saved);
    }

    [Test]
    public async Task SaveRecordsTheDisplayedPairRatherThanRehashingChangedCaptureFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        ViewerCapturePresentation displayed = await fixture.LoadPresentationAsync(0);
        byte[] changed = [.. BeforeRgba];
        changed[0] = 254;
        WritePng(fixture.Before, 3, 2, changed);

        await ViewerCapturePresentationFile.SaveAsync(fixture.Manifest, displayed, CancellationToken.None);
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Manifest))!.AsObject();
        InvalidDataException? failure = await LoadFailureAsync<InvalidDataException>(fixture.Manifest);

        await Assert.That(manifest["before"]!["pixelSha256"]!.GetValue<string>()).IsEqualTo(PixelHash(BeforeRgba));
        await Assert.That(manifest["before"]!["pixelSha256"]!.GetValue<string>()).IsNotEqualTo(PixelHash(changed));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("historical capture changed");
    }

    [Test]
    [NotInParallel]
    [Arguments("unchanged", false)]
    [Arguments("reencoded", false)]
    [Arguments("before-pixels", false)]
    [Arguments("after-pixels", false)]
    [Arguments("dimensions", false)]
    [Arguments("path", false)]
    [Arguments("swapped", false)]
    [Arguments("presentation", false)]
    [Arguments("unchanged", true)]
    [Arguments("reencoded", true)]
    [Arguments("before-pixels", true)]
    [Arguments("after-pixels", true)]
    [Arguments("dimensions", true)]
    [Arguments("path", true)]
    [Arguments("swapped", true)]
    [Arguments("presentation", true)]
    public async Task RecompareKeepsEditedNotesOnlyForTheSameVerifiedCapturePair(string change, bool dark)
    {
        using var fixture = await Fixture.CreateAsync();
        await RunUiAsync(async () =>
        {
            var window = new CaptureComparisonWindow
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await window.LoadPairAsync(fixture.Before, fixture.After);
                TextBox notes = Required<TextBox>(window, "ComparisonNotes");
                notes.Text = "Unsaved review of these exact pixels.\nKeep the alpha evidence.";
                Required<Expander>(window, "ComparisonNotesExpander").IsExpanded = true;
                Required<ComboBox>(window, "ComparisonMode").SelectedIndex = 2;
                await window.SavePresentationAsync(fixture.Manifest);
                notes.Text += "\nEdited after saving.";
                string edited = notes.Text!;
                string before = fixture.Before;
                string after = fixture.After;
                if (change == "reencoded")
                {
                    byte[] original = await File.ReadAllBytesAsync(before);
                    byte[] reencoded = SplitIdatIntoSingleByteChunks(original);
                    await Assert.That(reencoded.SequenceEqual(original)).IsFalse();
                    await File.WriteAllBytesAsync(before, reencoded);
                }
                else if (change == "before-pixels")
                {
                    byte[] changed = [.. BeforeRgba];
                    changed[7] = 127;
                    WritePng(before, 3, 2, changed);
                }
                else if (change == "after-pixels")
                {
                    WritePng(after, 2, 1, [20, 40, 81, 255, 200, 100, 0, 255]);
                }
                else if (change == "dimensions")
                {
                    WritePng(before, 2, 3, BeforeRgba);
                }
                else if (change == "path")
                {
                    before = Path.Combine(fixture.Root, "a", "identical.png");
                    File.Copy(fixture.Before, before);
                }
                else if (change == "swapped")
                {
                    (before, after) = (after, before);
                }
                if (change == "presentation")
                {
                    await window.LoadPresentationAsync(fixture.Manifest);
                    await Assert.That(notes.Text).IsEqualTo(
                        "Unsaved review of these exact pixels.\nKeep the alpha evidence.");
                }
                else
                {
                    Required<Button>(window, "CompareAgainButton")
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                        () => Required<Button>(window, "CompareAgainButton").IsEnabled);
                    if (change is "path" or "swapped")
                    {
                        await Assert.That(notes.Text).IsEqualTo(edited);
                        await window.LoadPairAsync(before, after);
                    }
                    await Assert.That(notes.Text)
                        .IsEqualTo(change is "unchanged" or "reencoded" ? edited : string.Empty);
                }
                await Assert.That(Required<Expander>(window, "ComparisonNotesExpander").IsExpanded)
                    .IsEqualTo(change is "unchanged" or "reencoded" or "presentation");
                ViewerCapturePair expected = await ViewerCaptureComparison.LoadAsync(
                    before, after, CancellationToken.None);
                await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"),
                    expected.Before.Width, expected.Before.Height))
                    .IsEqualTo(Convert.ToHexString(expected.Before.Rgba));
                await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"),
                    expected.After.Width, expected.After.Height)).IsEqualTo(Convert.ToHexString(expected.After.Rgba));
                await Assert.That(Required<ComboBox>(window, "ComparisonMode").SelectedIndex).IsEqualTo(2);
                string saved = Path.Combine(fixture.Root, "out", "recompared.usdcompare.json");
                await window.SavePresentationAsync(saved);
                ViewerCapturePresentation reopened = await ViewerCapturePresentationFile.LoadAsync(
                    saved, CancellationToken.None);
                await Assert.That(reopened.Notes).IsEqualTo(notes.Text);
                await Assert.That(reopened.Images.Before.Rgba.SequenceEqual(expected.Before.Rgba)).IsTrue();
                await Assert.That(reopened.Images.After.Rgba.SequenceEqual(expected.After.Rgba)).IsTrue();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(Required<Image>(window, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(window, "AfterImage").Source).IsNull();
        });
    }

    [Test]
    [NotInParallel]
    public async Task ComparisonWindowSavesAndReopensHistoricalPairWithoutPublishingPartialState()
    {
        using var fixture = await Fixture.CreateAsync();
        string malformed = Path.Combine(fixture.Root, "out", "malformed.usdcompare.json");
        await File.WriteAllTextAsync(malformed, "{\"version\":1}");
        await RunUiAsync(async () =>
        {
            var first = new CaptureComparisonWindow();
            first.Show();
            try
            {
                await Assert.That(Required<Button>(first, "SavePresentationButton").IsEnabled).IsFalse();
                await Assert.That(Required<TextBox>(first, "ComparisonNotes").IsEnabled).IsFalse();
                await Assert.That(Required<TextBox>(first, "ComparisonNotes").MaxLength).IsEqualTo(2048);
                await Assert.That(() => first.SavePresentationAsync(fixture.Manifest))
                    .ThrowsExactly<InvalidOperationException>();
                await first.LoadPairAsync(fixture.Before, fixture.After).WaitAsync(TimeSpan.FromSeconds(5));
                Required<ComboBox>(first, "ComparisonMode").SelectedIndex = 2;
                Required<TextBox>(first, "ComparisonNotes").Text =
                    "Check the revised lighting.\nHistorical pixels only.";
                await Assert.That(Required<Button>(first, "SavePresentationButton").IsEnabled).IsTrue();
                await first.SavePresentationAsync(fixture.Manifest).WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(Required<TextBlock>(first, "ComparisonStatus").Text).StartsWith("Comparison saved.");
            }
            finally
            {
                await first.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new CaptureComparisonWindow(async (_, _, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("The blocked loader cannot complete.");
            });
            second.Show();
            try
            {
                await second.LoadPresentationAsync(fixture.Manifest).WaitAsync(TimeSpan.FromSeconds(5));
                await AssertRestoredAsync(second);

                const string editedNotes = "Unsaved review changes survive a failed save.";
                Required<TextBox>(second, "ComparisonNotes").Text = editedNotes;
                await second.SavePresentationAsync(fixture.Manifest).WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(Required<TextBlock>(second, "ComparisonStatus").Text)
                    .Contains("Choose a new destination");
                await AssertRestoredAsync(second, editedNotes);

                await second.LoadPresentationAsync(malformed).WaitAsync(TimeSpan.FromSeconds(5));
                await AssertNoPairAsync(second);
                await Assert.That(Required<TextBlock>(second, "ComparisonStatus").Classes).Contains("viewer-error");

                await second.LoadPresentationAsync(fixture.Manifest).WaitAsync(TimeSpan.FromSeconds(5));
                await AssertRestoredAsync(second);

                Task cancelledLoad = second.LoadPairAsync(fixture.Before, fixture.After);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(Required<Button>(second, "SavePresentationButton").IsEnabled).IsFalse();
                await Assert.That(Required<Button>(second, "OpenPresentationButton").IsEnabled).IsFalse();
                await Assert.That(() => second.LoadPresentationAsync(fixture.Manifest))
                    .ThrowsExactly<InvalidOperationException>();
                Required<Button>(second, "CancelComparisonButton")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await cancelledLoad.WaitAsync(TimeSpan.FromSeconds(5));
                await AssertNoPairAsync(second);
                await Assert.That(Required<TextBlock>(second, "ComparisonStatus").Text).Contains("cancelled");

                entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task pendingOnClose = second.LoadPairAsync(fixture.Before, fixture.After);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await second.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(pendingOnClose.IsCompleted).IsTrue();
                await Assert.That(second.IsVisible).IsFalse();
            }
            finally
            {
                await second.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        });

        await Assert.That(
            Directory.GetFiles(Path.Combine(fixture.Root, "out")).Select(Path.GetFileName).OfType<string>())
            .IsEquivalentTo(["comparison.usdcompare.json", "malformed.usdcompare.json"]);
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ComparisonKeyboardEscapeCancelsAndDrainsBeforeDisposingTheWindow(bool dark)
    {
        using var fixture = await Fixture.CreateAsync();
        await RunUiAsync(async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempt = 0;
            var window = new CaptureComparisonWindow(async (before, after, token) =>
            {
                if (++attempt == 2)
                {
                    return await ViewerCaptureComparison.LoadAsync(before, after, token);
                }
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                    throw new InvalidOperationException("The blocked reader cannot finish normally.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancelled.TrySetResult();
                    await release.Task;
                    throw;
                }
            })
            {
                Width = 600,
                Height = 360,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await Assert.That(Required<Button>(window, "ChooseBeforeButton").IsKeyboardFocusWithin).IsTrue();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                Task load = window.LoadPairAsync(fixture.Before, fixture.After);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Button cancel = Required<Button>(window, "CancelComparisonButton");
                await Assert.That(cancel.IsEnabled).IsTrue();
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(load.IsCompleted).IsFalse();
                release.TrySetResult();
                await load.WaitAsync(TimeSpan.FromSeconds(5));
                Button retry = Required<Button>(window, "CompareAgainButton");
                await Assert.That(retry.IsKeyboardFocusWithin).IsTrue();
                await AssertNoPairAsync(window);
                await window.LoadPairAsync(fixture.Before, fixture.After).WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(retry.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
                    .IsEqualTo(Convert.ToHexString(BeforeRgba));
                await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"), 2, 1))
                    .IsEqualTo(Convert.ToHexString(AfterRgba));

                entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                load = window.LoadPairAsync(fixture.Before, fixture.After);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                cancel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsTrue();
                await Assert.That(load.IsCompleted).IsFalse();
                await Assert.That(Required<Button>(window, "SavePresentationButton").IsEnabled).IsFalse();
                release.TrySetResult();
                await load.WaitAsync(TimeSpan.FromSeconds(5));
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(Required<Image>(window, "BeforeImage").Source).IsNull();
                await Assert.That(Required<Image>(window, "AfterImage").Source).IsNull();
                await Assert.That(Directory.GetFiles(Path.Combine(fixture.Root, "out"))).IsEmpty();
            }
            finally
            {
                release.TrySetResult();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        });
    }

    [Test]
    [NotInParallel]
    [Arguments(600, 360, false)]
    [Arguments(600, 360, true)]
    [Arguments(1040, 700, false)]
    [Arguments(1040, 700, true)]
    public async Task ComparisonActionsRemainVisibleAtMinimumSizeWithReviewNotes(
        int width, int height, bool expanded)
    {
        using var fixture = await Fixture.CreateAsync();
        await RunUiAsync(async () =>
        {
            var window = new CaptureComparisonWindow { Width = width, Height = height };
            window.Show();
            try
            {
                await window.LoadPairAsync(fixture.Before, fixture.After);
                Required<TextBox>(window, "ComparisonNotes").Text = new string('x', 2048);
                Required<Expander>(window, "ComparisonNotesExpander").IsExpanded = expanded;
                window.UpdateLayout();

                foreach (string name in ComparisonActionNames)
                {
                    Control control = Required<Control>(window, name);
                    Point? origin = control.TranslatePoint(default, window);
                    await Assert.That(origin).IsNotNull();
                    await Assert.That(control.IsVisible).IsTrue();
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(control.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(origin!.Value.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.X + control.Bounds.Width)
                        .IsLessThanOrEqualTo(window.ClientSize.Width);
                    await Assert.That(origin.Value.Y + control.Bounds.Height)
                        .IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                ScrollViewer body = Required<ScrollViewer>(window, "ComparisonBodyScroll");
                if (expanded)
                {
                    TextBox notes = Required<TextBox>(window, "ComparisonNotes");
                    await Assert.That(notes.Focus()).IsTrue();
                    notes.BringIntoView();
                    window.UpdateLayout();
                    Point? origin = notes.TranslatePoint(default, body);
                    await Assert.That(origin).IsNotNull();
                    await Assert.That(origin!.Value.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(origin.Value.Y + notes.Bounds.Height)
                        .IsLessThanOrEqualTo(body.Viewport.Height);
                    await Assert.That(notes.IsKeyboardFocusWithin).IsTrue();
                }
                if (height == 700)
                {
                    await Assert.That(body.Extent.Height).IsEqualTo(body.Viewport.Height);
                }
                await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
                    .IsEqualTo(Convert.ToHexString(BeforeRgba));
                await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"), 2, 1))
                    .IsEqualTo(Convert.ToHexString(AfterRgba));
                await Assert.That(Required<TextBox>(window, "ComparisonNotes").Text!.Length).IsEqualTo(2048);
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        });
    }

    [Test]
    [NotInParallel]
    [Arguments(600, 360, false)]
    [Arguments(600, 360, true)]
    [Arguments(1040, 700, false)]
    [Arguments(1040, 700, true)]
    public async Task ReviewNotesKeyboardEndpointsStayVisibleAndEditsReopenWithoutChangingPixels(
        int width, int height, bool dark)
    {
        using var fixture = await Fixture.CreateAsync();
        string original = string.Join('\n', Enumerable.Range(0, 40)
            .Select(index => $"Review line {index:D2}: historical pixels only."));
        await RunUiAsync(async () =>
        {
            var window = new CaptureComparisonWindow
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await window.LoadPairAsync(fixture.Before, fixture.After);
                TextBox notes = Required<TextBox>(window, "ComparisonNotes");
                notes.Text = original;
                Required<Expander>(window, "ComparisonNotesExpander").IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(notes.Focus()).IsTrue();
                notes.BringIntoView();
                await AssertNotesVisibleAsync(window, notes);
                window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                window.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                await Assert.That(notes.CaretIndex).IsEqualTo(original.Length);
                await AssertNotesVisibleAsync(window, notes);
                ScrollViewer editorScroll = notes.GetVisualDescendants().OfType<ScrollViewer>().Single();
                await Assert.That(editorScroll.Extent.Height).IsGreaterThan(editorScroll.Viewport.Height);
                await Assert.That(editorScroll.Offset.Y).IsGreaterThan(0);
                window.KeyTextInput(" END");
                await Assert.That(notes.Text).IsEqualTo(original + " END");
                window.KeyPress(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                window.KeyRelease(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                await Assert.That(notes.CaretIndex).IsEqualTo(0);
                await AssertNotesVisibleAsync(window, notes);
                await Assert.That(editorScroll.Offset.Y).IsEqualTo(0);
                window.KeyTextInput("START ");
                string edited = "START " + original + " END";
                await Assert.That(notes.Text).IsEqualTo(edited);
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                await Assert.That(notes.SelectedText).IsEqualTo(edited);
                await AssertNotesVisibleAsync(window, notes);
                await Assert.That(notes.IsKeyboardFocusWithin).IsTrue();
                await window.SavePresentationAsync(fixture.Manifest);
                await window.LoadPresentationAsync(fixture.Manifest);
                await Assert.That(notes.Text).IsEqualTo(edited);
                await Assert.That(Required<Expander>(window, "ComparisonNotesExpander").IsExpanded).IsTrue();
                await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
                    .IsEqualTo(Convert.ToHexString(BeforeRgba));
                await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"), 2, 1))
                    .IsEqualTo(Convert.ToHexString(AfterRgba));
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(Required<Image>(window, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(window, "AfterImage").Source).IsNull();
        });
    }

    private static async Task AssertNotesVisibleAsync(CaptureComparisonWindow window, TextBox notes)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        ScrollViewer body = Required<ScrollViewer>(window, "ComparisonBodyScroll");
        Point position = notes.TranslatePoint(default, body) ??
            throw new InvalidOperationException("Comparison notes have no viewport coordinates.");
        await Assert.That(position.Y).IsGreaterThanOrEqualTo(0);
        await Assert.That(position.Y + notes.Bounds.Height).IsLessThanOrEqualTo(body.Viewport.Height);
    }

    private static async Task AssertRestoredAsync(
        CaptureComparisonWindow window, string notes = "Check the revised lighting.\nHistorical pixels only.")
    {
        await Assert.That(Required<ComboBox>(window, "ComparisonMode").SelectedIndex).IsEqualTo(2);
        await Assert.That(Required<Border>(window, "BeforePane").IsVisible).IsFalse();
        await Assert.That(Required<Border>(window, "AfterPane").IsVisible).IsTrue();
        await Assert.That(Required<TextBlock>(window, "BeforeName").Text).IsEqualTo("before.png");
        await Assert.That(Required<TextBlock>(window, "AfterName").Text).IsEqualTo("after.bmp");
        await Assert.That(Required<Button>(window, "SavePresentationButton").IsEnabled).IsTrue();
        await Assert.That(Required<TextBox>(window, "ComparisonNotes").Text)
            .IsEqualTo(notes);
        await Assert.That(Required<TextBox>(window, "ComparisonNotes").IsEnabled).IsTrue();
        await Assert.That(Required<Expander>(window, "ComparisonNotesExpander").IsExpanded).IsTrue();
        await Assert.That(ReadPixels(Required<Image>(window, "BeforeImage"), 3, 2))
            .IsEqualTo(Convert.ToHexString(BeforeRgba));
        await Assert.That(ReadPixels(Required<Image>(window, "AfterImage"), 2, 1))
            .IsEqualTo(Convert.ToHexString(AfterRgba));
    }

    private static async Task AssertNoPairAsync(CaptureComparisonWindow window)
    {
        await Assert.That(Required<Image>(window, "BeforeImage").Source).IsNull();
        await Assert.That(Required<Image>(window, "AfterImage").Source).IsNull();
        await Assert.That(Required<Button>(window, "SavePresentationButton").IsEnabled).IsFalse();
        await Assert.That(Required<TextBox>(window, "ComparisonNotes").Text).IsEqualTo(string.Empty);
        await Assert.That(Required<TextBox>(window, "ComparisonNotes").IsEnabled).IsFalse();
        await Assert.That(Required<ComboBox>(window, "ComparisonMode").IsEnabled).IsFalse();
        await Assert.That(Required<Button>(window, "OpenPresentationButton").IsEnabled).IsTrue();
    }

    private static string ReadPixels(Image image, int width, int height)
    {
        var bitmap = (WriteableBitmap)(image.Source ?? throw new InvalidOperationException("No image is displayed."));
        if (bitmap.PixelSize != new PixelSize(width, height))
        {
            return $"size {bitmap.PixelSize}";
        }
        using ILockedFramebuffer buffer = bitmap.Lock();
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(buffer.Address + (y * buffer.RowBytes), pixels, y * width * 4, width * 4);
        }
        return Convert.ToHexString(pixels);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing comparison control: {name}");

    private static async Task RunUiAsync(Func<Task> action)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            await action();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    private static async Task<T?> LoadFailureAsync<T>(string manifest) where T : Exception
    {
        try
        {
            _ = await ViewerCapturePresentationFile.LoadAsync(manifest, CancellationToken.None);
            return null;
        }
        catch (T exception)
        {
            return exception;
        }
    }

    private static JsonObject ValidManifest(string before, string after, string beforeHash, string afterHash) => new()
    {
        ["version"] = 1,
        ["displayMode"] = 0,
        ["before"] = new JsonObject
        {
            ["path"] = before,
            ["width"] = 3,
            ["height"] = 2,
            ["pixelSha256"] = beforeHash,
        },
        ["after"] = new JsonObject
        {
            ["path"] = after,
            ["width"] = 2,
            ["height"] = 1,
            ["pixelSha256"] = afterHash,
        },
    };

    private static string PixelHash(byte[] rgba) => Convert.ToHexString(SHA256.HashData(rgba));

    private static string PathOfLength(string root, int length)
    {
        var path = new StringBuilder(root);
        while (path.Length < length)
        {
            int remaining = length - path.Length;
            int segment = Math.Min(100, remaining - 1);
            if (remaining - 1 - segment == 1)
            {
                segment--;
            }
            path.Append(Path.DirectorySeparatorChar).Append('d', segment);
        }
        return path.ToString();
    }

    private static void WritePng(string path, int width, int height, byte[] rgba)
    {
        using FileStream stream = File.Create(path);
        _ = PngRgba8Writer.Write(stream, width, height, rgba, 1024);
    }

    private static byte[] SplitIdatIntoSingleByteChunks(byte[] png)
    {
        using var rebuilt = new MemoryStream();
        rebuilt.Write(png.AsSpan(0, 33));
        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33));
        for (int index = 0; index < length; index++)
        {
            WriteChunk(rebuilt, "IDAT"u8, png.AsSpan(41 + index, 1));
        }
        WriteChunk(rebuilt, "IEND"u8, []);
        return rebuilt.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length);
        stream.Write(size);
        stream.Write(type);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(size, PngCrc32.Calculate(type, data));
        stream.Write(size);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root)
        {
            Root = root;
            Before = Path.Combine(root, "a", "before.png");
            After = Path.Combine(root, "b", "after.bmp");
            Manifest = Path.Combine(root, "out", "comparison.usdcompare.json");
        }

        internal string Root { get; }
        internal string Before { get; }
        internal string After { get; }
        internal string Manifest { get; }

        internal static Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(Directory.CreateTempSubdirectory("viewer-presentation-").FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.Before)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.After)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.Manifest)!);
            WritePng(fixture.Before, 3, 2, BeforeRgba);
            ViewerFrameBitmapWriter.WriteBmp(fixture.After, 2, 1, AfterRgba);
            return Task.FromResult(fixture);
        }

        internal async Task<ViewerCapturePresentation> LoadPresentationAsync(int mode) =>
            new(Before, After, await ViewerCaptureComparison.LoadAsync(Before, After, CancellationToken.None), mode);

        internal string[] ManifestDirectoryEntries() =>
            [.. Directory.GetFileSystemEntries(Path.GetDirectoryName(Manifest)!).Select(Path.GetFileName)!];

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
