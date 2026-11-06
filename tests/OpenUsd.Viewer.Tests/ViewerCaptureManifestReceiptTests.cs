// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCaptureManifestReceiptTests
{
    [Test]
    public async Task ManifestFramesDescribeTheSameStateAsViewerReceipts()
    {
        using var root = new TemporaryRoot();
        StageRenderState[] states = CreateStates(root.Path);
        RenderDiskJobResult job = Render(root.Path, states);

        for (int index = 0; index < states.Length; index++)
        {
            string path = Path.Combine(job.OutputDirectory, job.Frames[index].FileName);
            ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(path, CancellationToken.None);
            ViewerCaptureStateReceiptResult result = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
                path, image, CancellationToken.None);

            ViewerCaptureStateReceipt expected = ViewerCaptureStateReceipt.From(states[index], null, image) with
            {
                Backend = ViewerCaptureStateReceipt.UnrecordedBackend
            };
            await Assert.That(result.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Verified);
            await Assert.That(result.Receipt).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task ManifestReceiptsCompareAgainstViewerReceiptsWithoutABackend()
    {
        using var root = new TemporaryRoot();
        StageRenderState[] states = CreateStates(root.Path);
        RenderDiskJobResult job = Render(root.Path, states);
        string sequenceFrame = Path.Combine(job.OutputDirectory, job.Frames[1].FileName);
        string capture = Path.Combine(root.Path, "viewer.png");
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(sequenceFrame, CancellationToken.None);
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(capture, Frame(image), states[1],
            new RenderBackendIdentity(RenderBackendKind.D3D12, "Silk D3D12"), CancellationToken.None);

        ViewerCaptureStateReceiptResult manifest = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            sequenceFrame, image, CancellationToken.None);
        ViewerCaptureStateReceiptResult viewer = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            capture, image, CancellationToken.None);
        ViewerCaptureStateReceiptResult first = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            Path.Combine(job.OutputDirectory, job.Frames[0].FileName),
            await ViewerCaptureComparison.LoadImageAsync(
                Path.Combine(job.OutputDirectory, job.Frames[0].FileName), CancellationToken.None),
            CancellationToken.None);

        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(viewer, manifest)).IsEqualTo(
            "Recorded state matches: stage, time, camera, display and render settings. " +
            "The render-job frame does not record its backend.");
        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(first, manifest))
            .IsEqualTo("Recorded state differs in time, camera, display, render settings.");
    }

    [Test]
    public async Task ReplacedFramesAreStaleAndUnlistedOrAbsentManifestsAreMissing()
    {
        using var root = new TemporaryRoot();
        RenderDiskJobResult job = Render(root.Path, CreateStates(root.Path));
        string first = Path.Combine(job.OutputDirectory, job.Frames[0].FileName);
        string second = Path.Combine(job.OutputDirectory, job.Frames[1].FileName);
        File.Copy(second, first, overwrite: true);
        ViewerCaptureImage replaced = await ViewerCaptureComparison.LoadImageAsync(first, CancellationToken.None);

        ViewerCaptureStateReceiptResult stale = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            first, replaced, CancellationToken.None);
        await Assert.That(stale.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Stale);

        string unlisted = Path.Combine(job.OutputDirectory, "copy.png");
        File.Copy(second, unlisted);
        ViewerCaptureStateReceiptResult missing = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            unlisted, replaced, CancellationToken.None);
        await Assert.That(missing.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Missing);

        File.Delete(Path.Combine(job.OutputDirectory, ViewerCaptureManifestReceipt.ManifestName));
        ViewerCaptureStateReceiptResult absent = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            second, replaced, CancellationToken.None);
        await Assert.That(absent.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Missing);
    }

    [Test]
    [Arguments("schema", "\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    [Arguments("mode", "\"mode\": \"Matrices\"", "\"mode\": \"1\"")]
    [Arguments("purpose", "\"purposes\": 9", "\"purposes\": 16")]
    [Arguments("vector", "\"clearColor\": [", "\"clearColor\": [0,")]
    [Arguments("hash", "\"sha256\": \"", "\"sha256\": \"z")]
    [Arguments("truncated", "\"frames\"", "\"frames")]
    public async Task MalformedManifestsAreInvalidRatherThanTrusted(string defect, string original, string replacement)
    {
        using var root = new TemporaryRoot();
        RenderDiskJobResult job = Render(root.Path, CreateStates(root.Path));
        string manifest = Path.Combine(job.OutputDirectory, ViewerCaptureManifestReceipt.ManifestName);
        string text = await File.ReadAllTextAsync(manifest);
        await Assert.That(text).Contains(original);
        int at = text.LastIndexOf(original, StringComparison.Ordinal);
        await File.WriteAllTextAsync(manifest,
            string.Concat(text.AsSpan(0, at), replacement, text.AsSpan(at + original.Length)),
            new UTF8Encoding(false));
        string path = Path.Combine(job.OutputDirectory, job.Frames[1].FileName);
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(path, CancellationToken.None);

        ViewerCaptureStateReceiptResult result = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            path, image, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Invalid).Because(defect);
    }

    [Test]
    public async Task ManifestFramesThatRecordTheBackendCompareItAgainstViewerReceipts()
    {
        using var root = new TemporaryRoot();
        StageRenderState[] states = CreateStates(root.Path);
        var silk = new RenderBackendIdentity(RenderBackendKind.D3D12, "hdSilk / Direct3D 12");
        RenderDiskJobResult job = Render(root.Path, states, silk);
        string sequenceFrame = Path.Combine(job.OutputDirectory, job.Frames[1].FileName);
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(sequenceFrame, CancellationToken.None);
        string same = Path.Combine(root.Path, "same.png");
        string other = Path.Combine(root.Path, "other.png");
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(
            same, Frame(image), states[1], silk, CancellationToken.None);
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(other, Frame(image), states[1],
            new RenderBackendIdentity(RenderBackendKind.Storm, "Storm / OpenGL"), CancellationToken.None);

        ViewerCaptureStateReceiptResult manifest = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            sequenceFrame, image, CancellationToken.None);
        ViewerCaptureStateReceiptResult sameViewer = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            same, image, CancellationToken.None);
        ViewerCaptureStateReceiptResult otherViewer = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            other, image, CancellationToken.None);

        await Assert.That(manifest.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Verified);
        await Assert.That(manifest.Receipt).IsEqualTo(ViewerCaptureStateReceipt.From(states[1], silk, image));
        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(sameViewer, manifest)).IsEqualTo(
            "Recorded state matches: stage, time, camera, display, render settings and backend.");
        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(otherViewer, manifest))
            .IsEqualTo("Recorded state differs in backend.");
    }

    [Test]
    [Arguments("kind", "\"kind\": \"D3D12\"", "\"kind\": \"Direct3D\"")]
    [Arguments("name", "\"name\": \"hdSilk / Direct3D 12\"", "\"name\": \" \"")]
    [Arguments("type", "\"name\": \"hdSilk / Direct3D 12\"", "\"name\": 12")]
    public async Task MalformedManifestBackendsAreInvalidRatherThanTrusted(
        string defect, string original, string replacement)
    {
        using var root = new TemporaryRoot();
        RenderDiskJobResult job = Render(root.Path, CreateStates(root.Path),
            new RenderBackendIdentity(RenderBackendKind.D3D12, "hdSilk / Direct3D 12"));
        string manifest = Path.Combine(job.OutputDirectory, ViewerCaptureManifestReceipt.ManifestName);
        string text = await File.ReadAllTextAsync(manifest);
        await Assert.That(text).Contains(original);
        await File.WriteAllTextAsync(manifest, text.Replace(original, replacement, StringComparison.Ordinal),
            new UTF8Encoding(false));
        string path = Path.Combine(job.OutputDirectory, job.Frames[1].FileName);
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(path, CancellationToken.None);

        ViewerCaptureStateReceiptResult result = await ViewerCaptureStateReceiptFile.ReadForComparisonAsync(
            path, image, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Invalid).Because(defect);
    }

    private static RenderDiskJobResult Render(
        string root, StageRenderState[] states, RenderBackendIdentity? backend = null) =>
        RenderDiskJob.Execute(
            new RenderDiskJobRequest(Path.Combine(root, "job"), states), new SeededSource(backend));

    private static StageRenderState[] CreateStates(string root)
    {
        StageRenderState first = StageRenderState.Default
            .WithStage(new StageIdentity("warehouse.usda"))
            .WithTime(new StageTime(1))
            .WithViewport(new ViewportDimensions(4, 2))
            .WithCamera(new CameraState(
                Matrix4x4.CreateTranslation(0.1f, 0, -5),
                Matrix4x4.CreatePerspectiveFieldOfView(0.75f, 2, 0.1f, 100)));
        StageRenderState second = first
            .WithTime(new StageTime(2.5))
            .WithCamera(new CameraState(
                Matrix4x4.CreateTranslation(1.0f / 3, 0, -5),
                Matrix4x4.CreatePerspectiveFieldOfView(0.75f, 2, 0.1f, 100),
                [new Vector4(0, 0, -1, -0.7f)]))
            .WithDisplay(new SceneDisplayState(
                RenderPurpose.Default | RenderPurpose.Guide, RenderVisibility.IncludeInvisible,
                RenderDrawMode.Wireframe))
            .WithRenderSettings(new RenderSettings(4, enableLighting: true, enableShadows: false,
                new Vector4(0.1f, 0.2f, 0.3f, 1), backfaceCulling: false, useSceneMaterials: true,
                RenderComplexity.Medium, RenderOutputTransform.Identity, exposure: 1.5f)
            {
                DisplayTransform = new RenderDisplayTransform(
                    Path.Combine(root, "config.ocio"), "ACEScg", view: "ACES 1.0 - SDR")
            });
        return [first, second];
    }

    private static ViewerFrameCaptureResult Frame(ViewerCaptureImage image) =>
        new(image.Width, image.Height, image.Rgba, ViewerFrameRowOrder.TopDown);

    private sealed class SeededSource(RenderBackendIdentity? backend) : IRenderJobFrameSource
    {
        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            byte[] rgba = new byte[state.Viewport.Width * state.Viewport.Height * 4];
            for (int index = 0; index < rgba.Length; index++)
            {
                rgba[index] = (byte)((state.Time.TimeCode * 40) + index);
            }
            return new RenderJobImage(state.Viewport.Width, state.Viewport.Height, rgba, Rgba8RowOrder.TopDown)
            {
                Backend = backend
            };
        }
    }

    private sealed class TemporaryRoot : IDisposable
    {
        internal TemporaryRoot() => Path = Directory.CreateTempSubdirectory("viewer-manifest-receipt-").FullName;

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
