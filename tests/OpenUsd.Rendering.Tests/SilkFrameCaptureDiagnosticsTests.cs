// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkFrameCaptureDiagnosticsTests
{
    private const string MaterialPath = "/World/Materials/Broken";
    private const string CompilationFailure = "Injected material compilation failure.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedCapturePublishesShaderFailureAndKeepsItsSnapshotAfterRecovery(bool includeHdr)
    {
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);
        (SilkMaterialShaderKey key, SilkMaterialShaderRequest failed, SilkMaterialShaderRequest ready) =
            CreateRequests();
        renderer.ObserveMaterialShaderRequest(MaterialPath, key, failed);

        SilkFrameCaptureResult first = capture();
        RenderDiagnostic[] failures = first.Diagnostics.Entries
            .Where(static entry => entry.Code == SilkRenderDiagnosticCodes.MaterialShaderCompilationFailed)
            .ToArray();
        await Assert.That(failures.Length).IsEqualTo(1);
        await Assert.That(failures[0].Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);
        await Assert.That(failures[0].Message).Contains(MaterialPath);
        await Assert.That(failures[0].Message).Contains(key.CacheHash);
        await Assert.That(failures[0].Message).Contains(CompilationFailure);

        renderer.ObserveMaterialShaderRequest(MaterialPath, key, ready);
        SilkFrameCaptureResult recovered = capture();
        await Assert.That(recovered.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.MaterialShaderCompilationFailed)).IsFalse();
        await Assert.That(first.Diagnostics.Entries.Any(entry => ReferenceEquals(entry, failures[0]))).IsTrue();

        SilkFrameCaptureResult capture() => includeHdr
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 1, 1, RenderSettings.Default)
            : SilkFrameCapture.CaptureRetained(renderer, device, 1, 1, RenderSettings.Default);
    }

    [Test]
    public async Task DisplayCapturePreservesShaderAndDisplayFailuresUntilTheirSeparateRecovery()
    {
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);
        (SilkMaterialShaderKey key, SilkMaterialShaderRequest failed, SilkMaterialShaderRequest ready) =
            CreateRequests();
        renderer.ObserveMaterialShaderRequest(MaterialPath, key, failed);
        var settings = RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(
                Path.Combine(Path.GetTempPath(), "unsupported-display.ocio"), "linear")
        };

        SilkFrameCaptureResult display = SilkFrameCapture.CaptureRetained(renderer, device, 1, 1, settings);
        await Assert.That(display.Diagnostics.Entries.Count(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.MaterialShaderCompilationFailed)).IsEqualTo(1);
        await Assert.That(display.Diagnostics.Entries.Count(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported)).IsEqualTo(1);

        SilkFrameCaptureResult withoutDisplay = SilkFrameCapture.CaptureRetained(
            renderer, device, 1, 1, RenderSettings.Default);
        await Assert.That(withoutDisplay.Diagnostics.Entries.Count(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.MaterialShaderCompilationFailed)).IsEqualTo(1);
        await Assert.That(withoutDisplay.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported)).IsFalse();

        renderer.ObserveMaterialShaderRequest(MaterialPath, key, ready);
        SilkFrameCaptureResult recovered = SilkFrameCapture.CaptureRetained(
            renderer, device, 1, 1, RenderSettings.Default);
        await Assert.That(recovered.Diagnostics.Entries.Any(static entry =>
            entry.Code is SilkRenderDiagnosticCodes.MaterialShaderCompilationFailed or
                SilkRenderDiagnosticCodes.DisplayTransformDeviceUnsupported)).IsFalse();
        await Assert.That(display.Diagnostics.Entries.Count).IsEqualTo(2);
    }

    private static (
        SilkMaterialShaderKey Key,
        SilkMaterialShaderRequest Failed,
        SilkMaterialShaderRequest Ready) CreateRequests()
    {
        SilkMaterialShaderKey key = SilkMaterialShaderKey.Create(
            "capture-diagnostic"u8, SilkShaderBinaryFormat.SpirV, "capture-diagnostic-v1");
        SilkMaterialShaderProgram program =
            SilkMaterialShaderCompilerService.CreateCheckedPlaceholder(key.Format, key.CacheHash);
        return (
            key,
            new SilkMaterialShaderRequest(
                SilkMaterialShaderStatus.Failed, program, new InvalidOperationException(CompilationFailure)),
            new SilkMaterialShaderRequest(SilkMaterialShaderStatus.Ready, program, null));
    }
}
