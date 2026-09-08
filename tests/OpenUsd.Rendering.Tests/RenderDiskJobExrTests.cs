// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderDiskJobExrTests
{
    [Test]
    public async Task InvalidHdrFormatCannotBeSilentlyIgnored()
    {
        string output = Path.Combine(Path.GetTempPath(), $"exr-admission-{Guid.NewGuid():N}");
        StageRenderState[] frames = [State()];
        await Assert.That(() => new RenderDiskJobRequest(output, frames, false, true, (RenderHdrColorFormat)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new RenderDiskJobRequest(output, frames, false, false, RenderHdrColorFormat.Exr))
            .Throws<ArgumentException>();
        await Assert.That(new RenderDiskJobRequest(output, frames).HdrColorFormat)
            .IsEqualTo(RenderHdrColorFormat.RawRgba16Float);
        await Assert.That(new RenderDiskJobRequest(output, frames, false, true).HdrColorFormat)
            .IsEqualTo(RenderHdrColorFormat.RawRgba16Float);
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    public async Task ExrDoesNotReduceManagedRasterAdmission()
    {
        string output = Path.Combine(Path.GetTempPath(), $"exr-admission-{Guid.NewGuid():N}");
        if (!ExrRgba16FloatWriter.IsSupported)
        {
            await Assert.That(() => new RenderDiskJobRequest(output, [State()], false, true, RenderHdrColorFormat.Exr))
                .Throws<PlatformNotSupportedException>();
            await Assert.That(Directory.Exists(output)).IsFalse();
            return;
        }
        await Assert.That(() => new RenderDiskJobRequest(output, [State()], false, true,
            RenderHdrColorFormat.Exr, new RenderDiskJobLimits(maximumFrameBytes: 79)))
            .Throws<ArgumentOutOfRangeException>();
        var admitted = new RenderDiskJobRequest(output, [State()], false, true,
            RenderHdrColorFormat.Exr, new RenderDiskJobLimits(maximumFrameBytes: 80));
        await Assert.That(admitted.HdrColorFormat).IsEqualTo(RenderHdrColorFormat.Exr);
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeExrPreservesHalfBitsPngDepthAndExactCombinedBudgets(bool includeDepth)
    {
        RequireNativeExr();
        string root = Directory.CreateTempSubdirectory("openusd-job-exr-").FullName;
        const string expectedHalf = "0054008000B8003C00340040004800380030004C002C0000000000540000003C";
        byte[] half = Convert.FromHexString(expectedHalf);
        var source = new ExrSource(half, includeDepth);
        StageRenderState[] frames = [State()];
        try
        {
            RenderDiskJobResult raw = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "raw"), frames, includeDepth, true), source);
            RenderDiskJobResult exr = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "exr"), frames, includeDepth, true, RenderHdrColorFormat.Exr), source);
            RenderDiskFrameResult frame = exr.Frames[0];
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(exr.OutputDirectory, frame.HdrColorFileName!));
            await Assert.That(Convert.ToHexString(ExrScanlineOracle.Read(encoded, 2, 2))).IsEqualTo(expectedHalf);
            await Assert.That(frame.HdrColorFileName).IsEqualTo("frame-000000.hdr.exr");
            await Assert.That(frame.HdrColorFormat).IsEqualTo(RenderHdrColorFormat.Exr);
            await Assert.That(frame.HdrColorBytes).IsEqualTo((long)encoded.Length);
            await Assert.That(frame.HdrColorSha256)
                .IsEqualTo(Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant());
            await Assert.That(frame.Sha256).IsEqualTo(raw.Frames[0].Sha256);
            await Assert.That(frame.DepthSha256).IsEqualTo(raw.Frames[0].DepthSha256);
            await Assert.That(frame.DepthBytes).IsEqualTo(includeDepth ? 16L : 0L);
            await Assert.That(exr.TotalBytes).IsEqualTo(
                Directory.GetFiles(exr.OutputDirectory).Sum(static path => new FileInfo(path).Length));
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(exr.OutputDirectory, "manifest.json")));
            JsonElement metadata = manifest.RootElement.GetProperty("frames")[0].GetProperty("hdrColor");
            await Assert.That(metadata.GetProperty("format").GetString()).IsEqualTo("openexr-rgba16float");
            await Assert.That(metadata.GetProperty("primaries").GetString()).IsEqualTo("unspecified");
            await Assert.That(metadata.GetProperty("alphaAssociation").GetString()).IsEqualTo("unspecified");
            await Assert.That(metadata.GetProperty("pixelAspectRatio").GetInt32()).IsEqualTo(1);
            await Assert.That(metadata.GetProperty("sha256").GetString()).IsEqualTo(frame.HdrColorSha256);

            long frameBytes = frame.Bytes + frame.DepthBytes + frame.HdrColorBytes;
            var exact = new RenderDiskJobRequest(Path.Combine(root, "exact"), frames, includeDepth, true,
                RenderHdrColorFormat.Exr, new RenderDiskJobLimits(
                    maximumFrameBytes: frameBytes, maximumTotalBytes: exr.TotalBytes));
            await Assert.That(RenderDiskJob.Execute(exact, source).TotalBytes).IsEqualTo(exr.TotalBytes);
            var shortFrame = new RenderDiskJobRequest(Path.Combine(root, "short-frame"), frames, includeDepth, true,
                RenderHdrColorFormat.Exr, new RenderDiskJobLimits(maximumFrameBytes: frameBytes - 1));
            await Assert.That(() => RenderDiskJob.Execute(shortFrame, source))
                .Throws<RenderOutputQuotaExceededException>();
            var shortJob = new RenderDiskJobRequest(Path.Combine(root, "short-job"), frames, includeDepth, true,
                RenderHdrColorFormat.Exr, new RenderDiskJobLimits(maximumTotalBytes: exr.TotalBytes - 1));
            await Assert.That(() => RenderDiskJob.Execute(shortJob, source))
                .Throws<RenderOutputQuotaExceededException>();
            await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo(
                [raw.OutputDirectory, exr.OutputDirectory, exact.OutputDirectory]);
            await Assert.That(Convert.ToHexString(source.Half)).IsEqualTo(expectedHalf);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("missing")]
    [Arguments("dimensions")]
    [Arguments("nonfinite")]
    [Arguments("cancel-second-frame")]
    public async Task NativeExrFailureNeverPublishesPartialSequences(string failure)
    {
        RequireNativeExr();
        string root = Directory.CreateTempSubdirectory("openusd-job-exr-failure-").FullName;
        byte[] half = new byte[32];
        if (failure == "nonfinite")
        {
            half[31] = 0x7C;
        }
        using var cancellation = new CancellationTokenSource();
        var source = new ExrSource(failure == "dimensions" ? half[..16] : half, false)
        {
            MissingHdr = failure == "missing",
            HdrWidth = failure == "dimensions" ? 1 : 2,
            CancelSecondFrame = failure == "cancel-second-frame" ? cancellation : null,
            OutputParent = root
        };
        try
        {
            var request = new RenderDiskJobRequest(Path.Combine(root, "job"), [State(), State()], false, true,
                RenderHdrColorFormat.Exr);
            if (failure == "missing")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<NotSupportedException>();
            }
            else if (failure == "cancel-second-frame")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source, cancellation.Token))
                    .Throws<OperationCanceledException>();
                await Assert.That(source.CompletedExrBeforeCancellation).IsTrue();
                await Assert.That(source.Calls).IsEqualTo(2);
            }
            else
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<InvalidDataException>();
            }
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static StageRenderState State() => StageRenderState.Create(new StageIdentity("source.usda"))
        .WithViewport(new ViewportDimensions(2, 2));

    private static void RequireNativeExr()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_EXR_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_EXR_EXECUTION_REQUIRED=1 with the matching Windows x64 native runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        if (!ExrRgba16FloatWriter.IsSupported)
        {
            throw new InvalidOperationException("Required EXR execution needs Windows x64.");
        }
    }

    private sealed class ExrSource(byte[] half, bool depth) : IRenderJobFrameSource
    {
        internal byte[] Half => half;
        internal int HdrWidth { get; init; } = 2;
        internal bool MissingHdr { get; init; }
        internal CancellationTokenSource? CancelSecondFrame { get; init; }
        internal string? OutputParent { get; init; }
        internal int Calls { get; private set; }
        internal bool CompletedExrBeforeCancellation { get; private set; }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 2 && CancelSecondFrame is { } cancellation)
            {
                CompletedExrBeforeCancellation = Directory.EnumerateFiles(
                    OutputParent!, "*.exr", SearchOption.AllDirectories).Any();
                cancellation.Cancel();
            }
            return new RenderJobImage(2, 2, new byte[16], Rgba8RowOrder.TopDown)
            {
                HdrColor = MissingHdr ? null : new RenderJobHdrColor(HdrWidth, 2, half),
                DeviceDepth = depth ? new RenderJobDeviceDepth(2, 2, new float[] { 0, 0.25f, 0.5f, 1 }) : null
            };
        }
    }
}
