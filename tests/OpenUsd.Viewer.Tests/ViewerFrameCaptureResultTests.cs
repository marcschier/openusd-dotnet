// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerFrameCaptureResultTests
{
    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(-1, 1)]
    [Arguments(1, -1)]
    [Arguments(4096, 4097)]
    [Arguments(int.MaxValue, int.MaxValue)]
    public async Task InvalidOrOversizedCaptureDimensionsAreRefusedBeforeAllocation(int width, int height)
    {
        await Assert.That(() => ViewerFrameCaptureResult.GetByteCount(width, height))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ExactBudgetAndCallerOwnedRowOrderArePreserved()
    {
        await Assert.That(ViewerFrameCaptureResult.GetByteCount(4096, 4096)).IsEqualTo(64 * 1024 * 1024);
        byte[] pixels = [10, 20, 30, 255, 40, 50, 60, 255];
        var captured = new ViewerFrameCaptureResult(1, 2, pixels, ViewerFrameRowOrder.BottomUp);
        await Assert.That(captured.Width).IsEqualTo(1);
        await Assert.That(captured.Height).IsEqualTo(2);
        await Assert.That(captured.RowOrder).IsEqualTo(ViewerFrameRowOrder.BottomUp);
        await Assert.That(captured.Rgba.Equals((ReadOnlyMemory<byte>)pixels)).IsTrue();
        await Assert.That(() => new ViewerFrameCaptureResult(1, 2, pixels.AsMemory(1), ViewerFrameRowOrder.TopDown))
            .Throws<ArgumentException>();
        await Assert.That(() => new ViewerFrameCaptureResult(1, 2, pixels, (ViewerFrameRowOrder)99))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CaptureKeepsItsDetachedDiagnosticsRatherThanConsultingALaterFrame()
    {
        byte[] pixels = [10, 20, 30, 255];
        var warning = new RenderDiagnostic(RenderDiagnosticSeverity.Warning, "CAPTURE_WARNING", "Degraded capture.");
        var diagnostics = new RenderDiagnosticsState([warning]);
        var degraded = new ViewerFrameCaptureResult(1, 1, pixels, ViewerFrameRowOrder.TopDown, diagnostics);
        var clean = new ViewerFrameCaptureResult(1, 1, pixels, ViewerFrameRowOrder.TopDown);

        await Assert.That(degraded.Diagnostics).IsSameReferenceAs(diagnostics);
        await Assert.That(degraded.Diagnostics.Entries.Single()).IsEqualTo(warning);
        await Assert.That(clean.Diagnostics).IsSameReferenceAs(RenderDiagnosticsState.Empty);
        await Assert.That(clean.DeviceDepth).IsNull();
        await Assert.That(clean.HdrColor).IsNull();
        await Assert.That(degraded.Diagnostics.Entries.Count).IsEqualTo(1);
    }
}
