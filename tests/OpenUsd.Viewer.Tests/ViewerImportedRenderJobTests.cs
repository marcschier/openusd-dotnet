// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerImportedRenderJobTests
{
    [Test]
    [Arguments(1)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(20)]
    public async Task EmittedManifestReopensAfterProducerRetirementWithoutInventingLiveState(int count)
    {
        using var files = new ViewerCompletedJobTestFiles();
        var source = new Source();
        RenderDiskJobResult original = Emit(files.Root, count, source);
        source.Closed = true;
        string manifest = Path.Combine(original.OutputDirectory, "manifest.json");
        byte[] originalManifest = await File.ReadAllBytesAsync(manifest);
        ViewerImportedRenderJob job = await ViewerImportedRenderJob.ReadAsync(manifest, default);
        await Assert.That(job.Frames.Count).IsEqualTo(count);
        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadImportedAsync(job, default);
        int[] expected = Enumerable.Range(0, Math.Min(count, 16))
            .Select(index => count == 1 ? 0 : index * (count - 1) / (Math.Min(count, 16) - 1)).ToArray();
        await Assert.That(preview.Frames.Select(frame => frame.Index).SequenceEqual(expected)).IsTrue();
        await Assert.That(preview.Frames[0].Index).IsEqualTo(0);
        await Assert.That(preview.Frames[^1].Index).IsEqualTo(count - 1);
        foreach (ViewerCompletedFramePreview frame in preview.Frames)
        {
            await Assert.That(frame.RequestedState).IsNull();
            await Assert.That(frame.Camera).IsNull();
            await Assert.That(frame.Simulation).IsNull();
            await Assert.That(frame.ImportedFrame).IsSameReferenceAs(job.Frames[frame.Index]);
            await Assert.That(frame.StageIdentifier).Contains("unverified manifest claim: persisted-source");
            await Assert.That(frame.TimeCode).IsEqualTo(frame.Index + 0.25);
            await Assert.That(frame.SourceDimensions).IsEqualTo(new ViewportDimensions(2, 1));
            await Assert.That(Convert.ToHexString(frame.Pixels.AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
            await Assert.That(Convert.ToHexString(frame.Pixels.AsSpan((191 * 256 + 255) * 4, 4)))
                .IsEqualTo("0000FF40");
            ViewerImportedRenderFrame recorded = job.Frames[frame.Index];
            await Assert.That(recorded.RequestClaim).Contains("\"stateRevision\"");
            await Assert.That(recorded.RequestClaim).Contains("\"settings\"");
            await Assert.That(recorded.RequestClaim).Contains("\"selection\"");
            await Assert.That(recorded.RequestClaim).Contains("absent fields are unknown");
            await Assert.That(recorded.CameraClaim).Contains("\"view\"");
            await Assert.That(recorded.CameraClaim).Contains("unverified manifest claim");
            await Assert.That(recorded.SimulationClaim).IsEqualTo("simulation: not recorded in this manifest.");
        }
        await Assert.That(source.Calls).IsEqualTo(count);
        await Assert.That((await File.ReadAllBytesAsync(manifest)).SequenceEqual(originalManifest)).IsTrue();
        foreach (RenderDiskFrameResult frame in original.Frames)
        {
            string hash = Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(original.OutputDirectory, frame.FileName))));
            await Assert.That(hash).IsEqualTo(frame.Sha256.ToUpperInvariant());
        }
        ViewerCompletedFramePreview released = preview.Frames[0];
        preview.Dispose();
        await Assert.That(preview.Frames).IsEmpty();
        await Assert.That(released.Pixels).IsEmpty();
    }

    [Test]
    [Arguments("version")]
    [Arguments("kind")]
    [Arguments("empty")]
    [Arguments("too-many")]
    [Arguments("file")]
    [Arguments("index")]
    [Arguments("time")]
    [Arguments("width")]
    [Arguments("height")]
    [Arguments("bytes")]
    [Arguments("hash")]
    [Arguments("stage")]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("nested-duplicate")]
    [Arguments("frame-metadata")]
    [Arguments("job-metadata")]
    [Arguments("malformed")]
    public async Task UnsupportedOrMalformedManifestIsRefusedBeforeReadingAnyPng(string change)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonObject frame = root["frames"]![0]!.AsObject();
        switch (change)
        {
            case "version":
                root["schemaVersion"] = 2;
                break;
            case "kind":
                root["kind"] = "executable-scene";
                break;
            case "empty":
                root["frames"] = new JsonArray();
                break;
            case "too-many":
                root["frames"] = new JsonArray(Enumerable.Range(0, 4097)
                    .Select(_ => (JsonNode?)frame.DeepClone()).ToArray());
                break;
            case "file":
                frame["file"] = "..\\escape.png";
                break;
            case "index":
                frame["index"] = 1;
                break;
            case "time":
                frame["timeCode"] = "NaN";
                break;
            case "width":
                frame["width"] = 8193;
                break;
            case "height":
                frame["height"] = 0;
                break;
            case "bytes":
                frame["bytes"] = RenderDiskJobLimits.Default.MaximumFrameBytes + 1;
                break;
            case "hash":
                frame["sha256"] = new string('z', 64);
                break;
            case "stage":
                frame["stage"] = new string('s', 4097);
                break;
            case "missing":
                frame.Remove("timeCode");
                break;
            case "frame-metadata":
                frame["extra"] = new string('x', 32769);
                break;
            case "job-metadata":
                root["extra"] = new string('x', 32769);
                break;
        }
        string json = root.ToJsonString();
        json = change switch
        {
            "duplicate" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1",
                StringComparison.Ordinal),
            "nested-duplicate" => json.Replace("\"camera\":{", "\"camera\":{\"mode\":\"Matrices\",",
                StringComparison.Ordinal),
            "malformed" => "{not json",
            _ => json
        };
        await File.WriteAllTextAsync(path, json);
        File.Delete(Path.Combine(original.OutputDirectory, original.Frames[0].FileName));
        if (change is "version" or "kind")
        {
            await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
                .Throws<NotSupportedException>();
        }
        else
        {
            await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
                .Throws<InvalidDataException>();
        }
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(json);
    }

    [Test]
    [Arguments("manifest")]
    [Arguments("extent")]
    [Arguments("pixels")]
    [Arguments("dimensions")]
    [Arguments("missing")]
    public async Task ChangedOrMissingHistoricalFilesNeverPublishAPartialPreviewAndExplicitRetryWorks(string change)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 2, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        byte[] manifest = await File.ReadAllBytesAsync(path);
        string pngPath = Path.Combine(original.OutputDirectory, original.Frames[^1].FileName);
        byte[] png = await File.ReadAllBytesAsync(pngPath);
        ViewerImportedRenderJob job = await ViewerImportedRenderJob.ReadAsync(path, default);
        if (change == "manifest")
        {
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path))
                .Replace("persisted-source", "different-source", StringComparison.Ordinal));
        }
        else if (change == "dimensions")
        {
            var root = JsonNode.Parse(manifest)!.AsObject();
            root["frames"]![1]!["width"] = 3;
            await File.WriteAllTextAsync(path, root.ToJsonString());
            job = await ViewerImportedRenderJob.ReadAsync(path, default);
        }
        else if (change == "missing")
        {
            File.Delete(pngPath);
        }
        else
        {
            byte[] changed = change == "extent" ? [.. png, 0] : png.ToArray();
            changed[^1] ^= 1;
            await File.WriteAllBytesAsync(pngPath, changed);
        }
        if (change == "missing")
        {
            await Assert.That(async () => await ViewerCompletedJobPreview.LoadImportedAsync(job, default))
                .Throws<FileNotFoundException>();
        }
        else
        {
            await Assert.That(async () => await ViewerCompletedJobPreview.LoadImportedAsync(job, default))
                .Throws<InvalidDataException>();
        }
        await File.WriteAllBytesAsync(path, manifest);
        await File.WriteAllBytesAsync(pngPath, png);
        ViewerImportedRenderJob retried = await ViewerImportedRenderJob.ReadAsync(path, default);
        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadImportedAsync(retried, default);
        await Assert.That(preview.Frames.Count).IsEqualTo(2);
        await Assert.That(Convert.ToHexString(preview.Frames[1].Pixels.AsSpan(64 * 256 * 4, 4)))
            .IsEqualTo("FF000080");
    }

    [Test]
    [Arguments("manifest-before")]
    [Arguments("manifest-after")]
    [Arguments("frame")]
    [Arguments("directory")]
    public async Task ImportedManifestFramesAndAncestorsRefuseFilesystemAliases(string kind)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string manifest = Path.Combine(original.OutputDirectory, "manifest.json");
        ViewerImportedRenderJob job = await ViewerImportedRenderJob.ReadAsync(manifest, default);
        string alias = kind == "directory" ? Path.Combine(files.Root, "alias") :
            kind == "frame" ? Path.Combine(original.OutputDirectory, original.Frames[0].FileName) : manifest;
        string backup = alias + ".original";
        if (kind != "directory")
        {
            File.Move(alias, backup);
        }
        if (OperatingSystem.IsWindows())
        {
            await ViewerPortableReviewFixture.CreateJunctionAsync(alias, original.OutputDirectory);
        }
        else if (kind == "directory")
        {
            Directory.CreateSymbolicLink(alias, original.OutputDirectory);
        }
        else
        {
            File.CreateSymbolicLink(alias, backup);
        }
        try
        {
            if (kind is "manifest-before" or "directory")
            {
                string path = kind == "directory" ? Path.Combine(alias, "manifest.json") : manifest;
                await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
                    .Throws<NotSupportedException>();
            }
            else
            {
                await Assert.That(async () => await ViewerCompletedJobPreview.LoadImportedAsync(job, default))
                    .Throws<NotSupportedException>();
            }
        }
        finally
        {
            if (OperatingSystem.IsWindows() || kind == "directory")
            {
                Directory.Delete(alias);
            }
            else
            {
                File.Delete(alias);
            }
            if (kind != "directory")
            {
                File.Move(backup, alias);
            }
        }
    }

    [Test]
    [Arguments("manifest.json")]
    [Arguments(@"\\server\share\manifest.json")]
    [Arguments("//server/share/manifest.json")]
    [Arguments("bad\0manifest.json")]
    [Arguments("oversized")]
    public async Task NonLocalOrUnboundedManifestPathsAreRefusedWithoutFilesystemAccess(string path)
    {
        if (path == "oversized")
        {
            path = new string('x', 4097);
        }
        await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(30)]
    [Arguments(31)]
    [Arguments(32)]
    public async Task ManifestNestingAdmitsDepth32ButRefusesDepth33(int arrayDepth)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonNode nested = JsonValue.Create(1);
        for (int index = 0; index < arrayDepth; index++)
        {
            nested = new JsonArray(nested);
        }
        root["extra"] = nested;
        await File.WriteAllTextAsync(path, root.ToJsonString());
        if (arrayDepth <= 31)
        {
            ViewerImportedRenderJob accepted = await ViewerImportedRenderJob.ReadAsync(path, default);
            await Assert.That(accepted.Frames.Count).IsEqualTo(1);
            await Assert.That(accepted.MetadataClaim).Contains(nested.ToJsonString());
        }
        else
        {
            await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
                .Throws<InvalidDataException>();
        }
    }

    [Test]
    [Arguments("frame")]
    [Arguments("job")]
    [Arguments("stage")]
    public async Task HistoricalMetadataTextAdmitsItsExactCeilingAndRejectsOneMoreCharacter(string kind)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonObject frame = root["frames"]![0]!.AsObject();
        JsonObject target = kind == "job" ? root : frame;
        string key = kind == "stage" ? "stage" : "extra";
        target[key] = "";
        await File.WriteAllTextAsync(path, root.ToJsonString());
        int length;
        if (kind == "job")
        {
            ViewerImportedRenderJob baseline = await ViewerImportedRenderJob.ReadAsync(path, default);
            length = 32768 - baseline.MetadataClaim.Length;
        }
        else
        {
            length = kind == "stage" ? 4096 : 32768 - frame.ToJsonString().Length;
        }
        target[key] = new string('x', length);
        await File.WriteAllTextAsync(path, root.ToJsonString());
        ViewerImportedRenderJob exact = await ViewerImportedRenderJob.ReadAsync(path, default);
        await Assert.That(exact.Frames.Count).IsEqualTo(1);
        if (kind == "stage")
        {
            await Assert.That(exact.Frames[0].StageClaim.Length).IsEqualTo(4096);
        }
        else if (kind == "job")
        {
            await Assert.That(exact.MetadataClaim.Length).IsEqualTo(32768);
        }
        else
        {
            using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            await Assert.That(json.RootElement.GetProperty("frames")[0].GetRawText().Length).IsEqualTo(32768);
        }
        target[key] = new string('x', length + 1);
        await File.WriteAllTextAsync(path, root.ToJsonString());
        await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ManifestByteAndFrameCountCeilingsAdmitExactBoundariesAndRejectOneBeyond()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        byte[] padded = Enumerable.Repeat((byte)' ', ViewerImportedRenderJob.MaximumManifestBytes).ToArray();
        bytes.CopyTo(padded, 0);
        await File.WriteAllBytesAsync(path, padded);
        ViewerImportedRenderJob exactBytes = await ViewerImportedRenderJob.ReadAsync(path, default);
        await Assert.That(exactBytes.Frames.Count).IsEqualTo(1);
        exactBytes.VerifyManifest(default);
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            file.SetLength(ViewerImportedRenderJob.MaximumManifestBytes + 1L);
        }
        await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
            .Throws<InvalidDataException>();
        var root = JsonNode.Parse(bytes)!.AsObject();
        JsonObject template = root["frames"]![0]!.AsObject();
        var frames = new JsonArray();
        for (int index = 0; index < 4096; index++)
        {
            JsonObject next = template.DeepClone().AsObject();
            next["index"] = index;
            next["file"] = FormattableString.Invariant($"frame-{index:D6}.png");
            frames.Add(next);
        }
        root["frames"] = frames;
        await File.WriteAllTextAsync(path, root.ToJsonString());
        ViewerImportedRenderJob exactFrames = await ViewerImportedRenderJob.ReadAsync(path, default);
        await Assert.That(exactFrames.Frames.Count).IsEqualTo(4096);
        await Assert.That(exactFrames.Frames[^1].Index).IsEqualTo(4095);
        frames.Add(template.DeepClone());
        await File.WriteAllTextAsync(path, root.ToJsonString());
        await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(path, default))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task MissingMetadataRemainsUnknownAndCancelledReadsDoNotTouchFiles()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonObject frame = root["frames"]![0]!.AsObject();
        foreach (string key in new[] { "camera", "display", "settings", "selection", "stateRevision" })
        {
            frame.Remove(key);
        }
        await File.WriteAllTextAsync(path, root.ToJsonString());
        ViewerImportedRenderJob job = await ViewerImportedRenderJob.ReadAsync(path, default);
        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadImportedAsync(job, default);
        ViewerImportedRenderFrame recorded = preview.Frames.Single().ImportedFrame ??
            throw new InvalidOperationException("The imported descriptor was not retained.");
        await Assert.That(recorded.CameraClaim).IsEqualTo("camera: not recorded in this manifest.");
        await Assert.That(recorded.RequestClaim).Contains("absent fields are unknown");
        await Assert.That(recorded.RequestClaim.Contains("\"settings\"", StringComparison.Ordinal)).IsFalse();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.That(async () => await ViewerImportedRenderJob.ReadAsync(
            Path.Combine(files.Root, "missing", "manifest.json"), cancelled.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(async () => await ViewerCompletedJobPreview.LoadImportedAsync(job, cancelled.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task EmptyRestartHistoryImportsReadOnlyClaimsAndPixelsWithKeyboardContextAndCloseDrain(
        bool dark, bool largeMetadata)
    {
        using var files = new ViewerCompletedJobTestFiles();
        var source = new Source();
        RenderDiskJobResult original = Emit(files.Root, 2, source);
        source.Closed = true;
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        if (largeMetadata)
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            root["extra"] = new string('j', 30000);
            foreach (JsonNode? frame in root["frames"]!.AsArray())
            {
                frame!["extra"] = new string('f', 30000);
            }
            await File.WriteAllTextAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        byte[] before = await File.ReadAllBytesAsync(path);
        var history = new ViewerCompletedJobHistory();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedJobHistoryWindow(history)
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                Width = 520,
                Height = 440
            };
            window.Show();
            try
            {
                Button import = Required<Button>(window, "HistoryImportButton");
                await Assert.That(import.IsEnabled).IsTrue();
                await Assert.That(AutomationProperties.GetName(import))
                    .IsEqualTo("Import historical render job manifest");
                import.Focus();
                window.UpdateLayout();
                Rect bounds = import.GetTransformedBounds()!.Value.Bounds;
                Point point = import.TranslatePoint(new Point(0, 0), window)!.Value;
                await Assert.That(point.Y + bounds.Height <= window.ClientSize.Height).IsTrue();
                await Assert.That(import.IsKeyboardFocusWithin).IsTrue();
                await window.ImportManifestAsync(path);
                CompletedRenderJobWindow gallery = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(gallery, "ResultsLoading").IsVisible);
                gallery.Width = 520;
                gallery.Height = 460;
                Dispatcher.UIThread.RunJobs();
                gallery.UpdateLayout();
                await Assert.That(Required<TextBlock>(gallery, "ResultsDescription").Text)
                    .Contains("No scene, camera or effective renderer state has been reconstructed");
                await Assert.That(Required<TextBlock>(gallery, "ResultsSource").Text)
                    .Contains("unverified manifest claim: persisted-source");
                ListBox frames = Required<ListBox>(gallery, "ResultsFrames");
                await Assert.That(frames.ItemCount).IsEqualTo(2);
                var tile = (CompletedRenderJobTile)frames.Items[0]!;
                var image = tile.Image;
                await Assert.That(Convert.ToHexString(ViewerCompletedJobPreviewJourney.ReadPixels(image)
                    .AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                TextBox context = Required<TextBox>(gallery, "ResultsRequestContext");
                await Assert.That(context.IsReadOnly).IsTrue();
                await Assert.That(context.Focus()).IsTrue();
                context.BringIntoView();
                await AssertVisibleInMetadataAsync(gallery, context);
                gallery.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                gallery.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                await Assert.That(context.SelectedText).IsEqualTo(context.Text);
                gallery.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                gallery.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                await Assert.That(context.CaretIndex).IsEqualTo(context.Text!.Length);
                await AssertVisibleInMetadataAsync(gallery, context);
                gallery.KeyPress(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                gallery.KeyRelease(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                await Assert.That(context.CaretIndex).IsEqualTo(0);
                await Assert.That(context.IsKeyboardFocusWithin).IsTrue();
                await AssertVisibleInMetadataAsync(gallery, context);
                if (largeMetadata)
                {
                    await Assert.That(context.Text).Contains(new string('f', 30000));
                    await Assert.That(context.Text).Contains(new string('j', 30000));
                }
                TextBox camera = Required<TextBox>(gallery, "ResultsCameraContext");
                await Assert.That(camera.Focus()).IsTrue();
                camera.BringIntoView();
                await AssertVisibleInMetadataAsync(gallery, camera);
                gallery.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                gallery.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                await Assert.That(camera.CaretIndex).IsEqualTo(camera.Text!.Length);
                await AssertVisibleInMetadataAsync(gallery, camera);
                gallery.KeyPress(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                gallery.KeyRelease(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                await Assert.That(camera.CaretIndex).IsEqualTo(0);
                await Assert.That(camera.IsKeyboardFocusWithin).IsTrue();
                await AssertVisibleInMetadataAsync(gallery, camera);
                Button close = Required<Button>(gallery, "ResultsCloseButton");
                Point closePoint = close.TranslatePoint(new Point(0, 0), gallery)!.Value;
                await Assert.That(closePoint.Y >= 0 &&
                    closePoint.Y + close.Bounds.Height <= gallery.ClientSize.Height).IsTrue();
                frames.SelectedIndex = 1;
                await Assert.That(context.Text).Contains("\"timeCode\": 1.25");
                await window.ImportManifestAsync(Path.Combine(files.Root, "missing", "manifest.json"));
                await Assert.That(window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single())
                    .IsSameReferenceAs(gallery);
                await Assert.That(Required<TextBlock>(window, "HistoryStatus").Text)
                    .Contains("Could not import render manifest");
                await Assert.That(import.IsEnabled).IsTrue();
                await Assert.That(history.Snapshot()).IsEmpty();
                await Assert.That(source.Calls).IsEqualTo(2);
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(gallery.IsVisible).IsFalse();
                await Assert.That(frames.ItemCount).IsEqualTo(0);
                await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(image))
                    .Throws<ObjectDisposedException>();
            }
            finally
            {
                await window.CloseAsync();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(before)).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task GalleriesRestoreTheirOriginatingHistoryActionsOrCloseWhenUnavailable(bool dark, bool deselect)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        var history = new ViewerCompletedJobHistory();
        history.Publish(original, "Original process-local result");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedJobHistoryWindow(history)
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                ListBox jobs = Required<ListBox>(window, "HistoryJobs");
                object? selected = jobs.SelectedItem;
                Button import = Required<Button>(window, "HistoryImportButton");
                Button view = Required<Button>(window, "HistoryViewButton");
                await Assert.That(import.Focus()).IsTrue();
                await window.ImportManifestAsync(Path.Combine(original.OutputDirectory, "manifest.json"));
                CompletedRenderJobWindow gallery = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(gallery, "ResultsLoading").IsVisible);
                await Assert.That(Required<TextBlock>(gallery, "ResultsDescription").Text)
                    .Contains("Imported historical manifest");
                await gallery.CloseAsync();
                await Assert.That(import.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(jobs.SelectedItem).IsSameReferenceAs(selected);
                await Assert.That(jobs.Focus()).IsTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                gallery = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(gallery, "ResultsLoading").IsVisible);
                await Assert.That(Required<TextBlock>(gallery, "ResultsDescription").Text)
                    .IsEqualTo("Original process-local result");
                if (deselect)
                {
                    jobs.SelectedIndex = -1;
                    await Assert.That(view.IsEnabled).IsFalse();
                }
                await gallery.CloseAsync();
                await Assert.That((deselect ? Required<Button>(window, "HistoryCloseButton") : view)
                    .IsKeyboardFocusWithin).IsTrue();
                if (deselect)
                {
                    await Assert.That(jobs.SelectedItem).IsNull();
                }
                else
                {
                    await Assert.That(jobs.SelectedItem).IsSameReferenceAs(selected);
                }
                await Assert.That(history.Snapshot().Count).IsEqualTo(1);
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task GalleryClosePreservesFocusMovedToHistoryOrAnotherWindow(bool dark, bool external)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        var history = new ViewerCompletedJobHistory();
        history.Publish(original, "Original process-local result");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedJobHistoryWindow(history)
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var otherEditor = new TextBox();
            var otherWindow = new Window { Content = otherEditor };
            window.Show();
            try
            {
                await Assert.That(Required<Button>(window, "HistoryImportButton").Focus()).IsTrue();
                await window.ImportManifestAsync(Path.Combine(original.OutputDirectory, "manifest.json"));
                CompletedRenderJobWindow gallery = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(gallery, "ResultsLoading").IsVisible);
                await Assert.That(Required<Button>(gallery, "ResultsCloseButton").IsKeyboardFocusWithin).IsTrue();
                TextBox target = external ? otherEditor : Required<TextBox>(window, "HistoryOutput");
                if (external)
                {
                    otherWindow.Show();
                }
                else
                {
                    window.Activate();
                }
                await Assert.That(target.Focus()).IsTrue();
                await Assert.That(gallery.IsKeyboardFocusWithin).IsFalse();

                await gallery.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

                await Assert.That(target.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(gallery.IsVisible).IsFalse();
                await Assert.That(window.OwnedWindows.OfType<CompletedRenderJobWindow>()).IsEmpty();
                await Assert.That(history.Snapshot().Count).IsEqualTo(1);
                await Assert.That(Required<Button>(window, "HistoryImportButton").IsEnabled).IsTrue();
            }
            finally
            {
                otherWindow.Close();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedManifestImportRestoresKeyboardRetryWithoutStealingMovedFocus(bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        string missing = Path.Combine(files.Root, "missing", "manifest.json");
        var history = new ViewerCompletedJobHistory();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new CompletedJobHistoryWindow(history, readManifest: async (manifest, token) =>
            {
                await release.Task.WaitAsync(token);
                return await ViewerImportedRenderJob.ReadAsync(manifest, token);
            })
            {
                Width = 520,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                Button import = Required<Button>(window, "HistoryImportButton");
                Button close = Required<Button>(window, "HistoryCloseButton");
                await Assert.That(import.Focus()).IsTrue();
                Task importing = window.ImportManifestAsync(missing);
                await Assert.That(import.IsEnabled).IsFalse();
                await Assert.That(close.IsEnabled).IsTrue();
                await Assert.That(close.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(importing.IsCompleted).IsFalse();
                release.TrySetResult();
                await importing;
                await Assert.That(import.IsEnabled).IsTrue();
                await Assert.That(Required<TextBlock>(window, "HistoryStatus").Text)
                    .Contains("Could not import render manifest");
                await Assert.That(import.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(window.OwnedWindows).IsEmpty();
                release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                importing = window.ImportManifestAsync(missing);
                TextBox output = Required<TextBox>(window, "HistoryOutput");
                await Assert.That(output.Focus()).IsTrue();
                release.TrySetResult();
                await importing;
                await Assert.That(output.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(import.Focus()).IsTrue();
                release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                importing = window.ImportManifestAsync(missing);
                var otherEditor = new TextBox();
                var otherWindow = new Window { Content = otherEditor };
                otherWindow.Show();
                try
                {
                    await Assert.That(otherEditor.Focus()).IsTrue();
                    release.TrySetResult();
                    await importing;
                    await Assert.That(otherEditor.IsKeyboardFocusWithin).IsTrue();
                    await Assert.That(window.IsKeyboardFocusWithin).IsFalse();
                }
                finally
                {
                    otherWindow.Close();
                }
                await Assert.That(import.Focus()).IsTrue();
                await window.ImportManifestAsync(path);
                CompletedRenderJobWindow gallery = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(gallery, "ResultsLoading").IsVisible);
                await Assert.That(Required<ListBox>(gallery, "ResultsFrames").ItemCount).IsEqualTo(1);
                await Assert.That(Required<Button>(gallery, "ResultsCloseButton").IsKeyboardFocusWithin).IsTrue();
                await Assert.That(Required<TextBox>(gallery, "ResultsOutputLocation").Text)
                    .IsEqualTo(original.OutputDirectory);
                await gallery.CloseAsync();
                await Assert.That(import.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(history.Snapshot()).IsEmpty();
            }
            finally
            {
                release.TrySetResult();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    public async Task ClosingDuringManifestReadCancelsAndDrainsBeforeAnyGalleryCanPublish()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        ViewerImportedRenderJob job = await ViewerImportedRenderJob.ReadAsync(path, default);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken loading = default;
            var window = new CompletedJobHistoryWindow(
                new ViewerCompletedJobHistory(), readManifest: async (_, token) =>
            {
                loading = token;
                await release.Task;
                return job;
            });
            window.Show();
            Task importing = window.ImportManifestAsync(path);
            try
            {
                await Assert.That(Required<Button>(window, "HistoryImportButton").IsEnabled).IsFalse();
                Task closing = window.CloseAsync();
                await Assert.That(loading.IsCancellationRequested).IsTrue();
                await Assert.That(closing.IsCompleted).IsFalse();
                await Assert.That(window.OwnedWindows).IsEmpty();
                release.TrySetResult();
                await Task.WhenAll(importing, closing).WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(window.OwnedWindows).IsEmpty();
            }
            finally
            {
                release.TrySetResult();
                await window.CloseAsync();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static async Task AssertVisibleInMetadataAsync(Window window, TextBox editor)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        ScrollViewer metadata = Required<ScrollViewer>(window, "ResultsMetadataScroll");
        Point position = editor.TranslatePoint(default, metadata) ??
            throw new InvalidOperationException("Imported metadata has no viewport coordinates.");
        await Assert.That(position.Y).IsGreaterThanOrEqualTo(0);
        await Assert.That(position.Y + editor.Bounds.Height).IsLessThanOrEqualTo(metadata.Viewport.Height);
    }

    private static T Required<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control {name}.");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    private static RenderDiskJobResult Emit(string root, int count, Source source)
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("persisted-source"))
            .WithViewport(new ViewportDimensions(2, 1));
        return RenderDiskJob.Execute(new RenderDiskJobRequest(Path.Combine(root, "sequence"),
            Enumerable.Range(0, count).Select(index => state.WithTime(new StageTime(index + 0.25))).ToArray()), source);
    }

    private sealed class Source : IRenderJobFrameSource
    {
        internal bool Closed { get; set; }
        internal int Calls { get; private set; }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            if (Closed)
            {
                throw new InvalidOperationException("The producer has retired.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return new RenderJobImage(2, 1, new byte[] { 255, 0, 0, 128, 0, 0, 255, 64 }, Rgba8RowOrder.TopDown);
        }
    }
}
