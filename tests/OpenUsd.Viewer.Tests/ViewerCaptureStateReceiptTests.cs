// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCaptureStateReceiptTests
{
    private static readonly RenderBackendIdentity Backend = new(RenderBackendKind.D3D12, "Silk D3D12");

    [Test]
    public async Task SavedCaptureReceiptVerifiesAgainstDecodedPixelsAndRoundTrips()
    {
        using var root = new TemporaryRoot();
        string path = Path.Combine(root.Path, "frame.png");
        StageRenderState state = CreateState(timeCode: 12.5, eyeX: 2);

        ViewerCaptureStateReceipt saved = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(
            path, Frame(10), state, Backend, CancellationToken.None);
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(path, CancellationToken.None);
        ViewerCaptureStateReceiptResult read =
            await ViewerCaptureStateReceiptFile.ReadAsync(path, image, CancellationToken.None);

        await Assert.That(read.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Verified);
        await Assert.That(read.Receipt).IsEqualTo(saved);
        await Assert.That(saved.Stage).IsEqualTo(state.Stage.Identifier);
        await Assert.That(saved.TimeCode).IsEqualTo(12.5);
        await Assert.That(saved.Backend).IsEqualTo("D3D12:Silk D3D12");
        await Assert.That(saved.Camera).StartsWith("Matrices;view=1,0,0,0,");
        await Assert.That(saved.Display).Contains("drawMode=SmoothShaded");
        await Assert.That(saved.RenderSettings).Contains("ocio=none");
        await Assert.That(Directory.GetFiles(root.Path).Select(file => Path.GetFileName(file)!))
            .IsEquivalentTo(["frame.png", "frame.png.usdstate.json"]);
    }

    [Test]
    public async Task ReceiptTextIsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string german = ViewerCaptureStateReceipt.DescribeRenderSettings(RenderSettings.PresentationDefault);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = ViewerCaptureStateReceipt.DescribeRenderSettings(RenderSettings.PresentationDefault);
            await Assert.That(german).IsEqualTo(invariant);
            await Assert.That(invariant).Contains("exposure=-6");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public async Task DifferencesNameEachChangedStateField()
    {
        ViewerCaptureImage image = new(4, 2, new byte[32]);
        StageRenderState state = CreateState(timeCode: 1, eyeX: 0);
        ViewerCaptureStateReceipt first = ViewerCaptureStateReceipt.From(state, Backend, image);

        await Assert.That(ViewerCaptureStateReceipt.Differences(
            first, ViewerCaptureStateReceipt.From(state, Backend, image))).IsEmpty();
        await Assert.That(ViewerCaptureStateReceipt.Differences(first, ViewerCaptureStateReceipt.From(
            CreateState(timeCode: 2, eyeX: 3), Backend, image))).IsEquivalentTo(["time", "camera"]);
        StageRenderState changed = state
            .WithStage(new StageIdentity("other.usda"))
            .WithDisplay(state.Display with { DrawMode = RenderDrawMode.Wireframe })
            .WithRenderSettings(RenderSettings.PresentationDefault);
        await Assert.That(ViewerCaptureStateReceipt.Differences(first, ViewerCaptureStateReceipt.From(
                changed, new RenderBackendIdentity(RenderBackendKind.Storm, "Storm"),
                new ViewerCaptureImage(2, 2, new byte[16]))))
            .IsEquivalentTo(["stage", "display", "render settings", "backend", "size"]);
    }

    [Test]
    public async Task ReplacedPixelsMakeAnOldReceiptStaleAndMissingReceiptsAreReported()
    {
        using var root = new TemporaryRoot();
        string path = Path.Combine(root.Path, "frame.bmp");
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(
            path, Frame(10), CreateState(0, 0), Backend, CancellationToken.None);
        await ViewerFrameFileWriter.WriteAsync(path, Frame(90), CancellationToken.None);
        ViewerCaptureImage replaced = await ViewerCaptureComparison.LoadImageAsync(path, CancellationToken.None);

        ViewerCaptureStateReceiptResult stale =
            await ViewerCaptureStateReceiptFile.ReadAsync(path, replaced, CancellationToken.None);
        await Assert.That(stale.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Stale);
        await Assert.That(stale.Receipt).IsNull();

        File.Delete(ViewerCaptureStateReceiptFile.PathFor(path));
        ViewerCaptureStateReceiptResult missing =
            await ViewerCaptureStateReceiptFile.ReadAsync(path, replaced, CancellationToken.None);
        await Assert.That(missing.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Missing);
        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(missing, stale))
            .IsEqualTo("A has no state receipt. Only pixels are compared.");
        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(
                new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Verified,
                    ViewerCaptureStateReceipt.From(CreateState(0, 0), Backend, replaced)), stale))
            .IsEqualTo("B's state receipt belongs to different pixels. Only pixels are compared.");
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("missing")]
    [Arguments("version")]
    [Arguments("hash")]
    [Arguments("nan")]
    [Arguments("type")]
    [Arguments("truncated")]
    [Arguments("oversized")]
    public async Task MalformedReceiptsAreInvalidRatherThanTrusted(string defect)
    {
        using var root = new TemporaryRoot();
        string path = Path.Combine(root.Path, "frame.png");
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(
            path, Frame(10), CreateState(0, 0), Backend, CancellationToken.None);
        string receiptPath = ViewerCaptureStateReceiptFile.PathFor(path);
        JsonObject json = JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!.AsObject();
        string text;
        switch (defect)
        {
            case "unknown":
                json["extra"] = 1;
                text = json.ToJsonString();
                break;
            case "missing":
                json.Remove("backend");
                text = json.ToJsonString();
                break;
            case "version":
                json["version"] = 2;
                text = json.ToJsonString();
                break;
            case "hash":
                json["pixelSha256"] = "XYZ";
                text = json.ToJsonString();
                break;
            case "nan":
                text = json.ToJsonString().Replace("\"timeCode\":0", "\"timeCode\":\"NaN\"", StringComparison.Ordinal);
                break;
            case "type":
                json["width"] = "8";
                text = json.ToJsonString();
                break;
            case "truncated":
                text = json.ToJsonString()[..20];
                break;
            default:
                json["camera"] = new string('x', ViewerCaptureStateReceiptFile.MaximumBytes);
                text = json.ToJsonString();
                break;
        }
        await File.WriteAllTextAsync(receiptPath, text, new UTF8Encoding(false));
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(path, CancellationToken.None);

        ViewerCaptureStateReceiptResult result =
            await ViewerCaptureStateReceiptFile.ReadAsync(path, image, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerCaptureStateReceiptStatus.Invalid);
        await Assert.That(ViewerCaptureStateReceiptResult.DescribePair(result, result))
            .IsEqualTo("A's state receipt is invalid. Only pixels are compared.");
    }

    [Test]
    [NotInParallel]
    public async Task ComparisonWindowReportsRecordedStateDifferences()
    {
        using var root = new TemporaryRoot();
        string before = Path.Combine(root.Path, "before.png");
        string after = Path.Combine(root.Path, "after.png");
        string plain = Path.Combine(root.Path, "plain.png");
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(
            before, Frame(10), CreateState(1, 0), Backend, CancellationToken.None);
        _ = await ViewerCaptureStateReceiptFile.SaveCaptureAsync(
            after, Frame(20), CreateState(4, 5), Backend, CancellationToken.None);
        await ViewerFrameFileWriter.WriteAsync(plain, Frame(30), CancellationToken.None);

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CaptureComparisonWindow();
            window.Show();
            try
            {
                TextBlock status = window.FindControl<TextBlock>("ComparisonStatus")!;
                await window.LoadPairAsync(before, after);
                await Assert.That(status.Text).Contains("Recorded state differs in time, camera.");
                await window.LoadPairAsync(before, before);
                await Assert.That(status.Text).Contains(
                    "Recorded state matches: stage, time, camera, display, render settings and backend.");
                await window.LoadPairAsync(before, plain);
                await Assert.That(status.Text).Contains("B has no state receipt. Only pixels are compared.");
                await Assert.That(status.Classes.Contains("viewer-error")).IsFalse();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static StageRenderState CreateState(double timeCode, float eyeX) =>
        StageRenderState.Default
            .WithStage(new StageIdentity("warehouse.usda"))
            .WithTime(new StageTime(timeCode))
            .WithViewport(new ViewportDimensions(4, 2))
            .WithCamera(new CameraState(
                Matrix4x4.CreateTranslation(eyeX, 0, -5),
                Matrix4x4.CreatePerspectiveFieldOfView(0.75f, 2, 0.1f, 100)));

    private static ViewerFrameCaptureResult Frame(byte seed)
    {
        byte[] rgba = new byte[4 * 2 * 4];
        for (int index = 0; index < rgba.Length; index++)
        {
            rgba[index] = (byte)(seed + index);
        }
        return new ViewerFrameCaptureResult(4, 2, rgba, ViewerFrameRowOrder.TopDown);
    }

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    private sealed class TemporaryRoot : IDisposable
    {
        internal TemporaryRoot() => Path = Directory.CreateTempSubdirectory("viewer-state-receipt-").FullName;

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
