// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenUsd.Rendering;

/// <summary>Executes a bounded RGBA sequence without retaining encoded images in memory.</summary>
public static partial class RenderDiskJob
{
    /// <summary>Renders and encodes sequentially on the calling thread, then publishes a new directory.</summary>
    /// <remarks>
    /// The caller owns the frame source and must select its renderer thread. No source is disposed,
    /// stage mutated, scene-authored filename honored or command executed by this engine.
    /// A failure/cancellation removes only owned staging output and never publishes a partial job.
    /// The output parent is caller-approved; this is not a sandbox for arbitrary filesystem writers.
    /// </remarks>
    public static RenderDiskJobResult Execute(
        RenderDiskJobRequest request, IRenderJobFrameSource source, CancellationToken cancellationToken = default)
        => Execute(request, source, TimeProvider.System, cancellationToken);

    internal static RenderDiskJobResult Execute(
        RenderDiskJobRequest request, IRenderJobFrameSource source,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ProductPlan is { } product)
        {
            if (source is not IRenderProductFrameSource productSource)
            {
                throw new NotSupportedException("The frame source cannot execute authored product semantics.");
            }
            productSource.ValidateProduct(product, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        string destination = request.OutputDirectory;
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("A render job cannot replace an existing file or directory.");
        }
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException("The render-job output parent must already exist.");
        }
        string staging = Path.Combine(parent, $".openusd-render-{Guid.NewGuid():N}");
        bool owned = false;
        bool published = false;
        try
        {
            Directory.CreateDirectory(staging);
            owned = true;
            var frames = new RenderDiskFrameResult[request.Frames.Count];
            var diagnostics = new List<RenderDiagnostic>();
            long total = 0;
            for (int index = 0; index < frames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long remaining = Math.Min(request.Limits.MaximumFrameBytes, request.Limits.MaximumTotalBytes - total);
                if (remaining <= 0)
                {
                    throw new RenderOutputQuotaExceededException("The render-job output byte budget is exhausted.");
                }
                frames[index] = RenderFrame(
                    staging, index, request.Frames[index], source, diagnostics, request.IncludeDeviceDepth,
                    request.IncludeHdrColor, request.HdrColorFormat, remaining, cancellationToken);
                total += frames[index].Bytes + frames[index].DepthBytes + frames[index].HdrColorBytes;
            }
            total += WriteManifest(
                staging, frames, diagnostics, request.Limits.MaximumTotalBytes - total,
                request.ProductPlan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RetryDirectorySharing(
                () => Directory.Move(DirectoryMovePath(staging), DirectoryMovePath(destination)),
                timeProvider, cancellationToken);
            published = true;
            return new RenderDiskJobResult(destination, frames, total, diagnostics.ToArray());
        }
        finally
        {
            if (owned && !published)
            {
                RetryDirectorySharing(
                    () => Directory.Delete(staging, recursive: true), timeProvider, CancellationToken.None);
            }
        }
    }

    internal static void RetryDirectorySharing(
        Action operation, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(timeProvider);
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                operation();
                return;
            }
            catch (IOException exception) when (
                OperatingSystem.IsWindows() && attempt < 4 &&
                (exception.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                // Short-lived readers without delete sharing can delay an otherwise valid rename or cleanup.
                Task.Delay(TimeSpan.FromMilliseconds(20 << attempt), timeProvider, cancellationToken)
                    .GetAwaiter().GetResult();
            }
        }
    }

    private static string DirectoryMovePath(string path)
    {
        if (!OperatingSystem.IsWindows() || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    private static RenderDiskFrameResult RenderFrame(
        string directory, int index, StageRenderState state, IRenderJobFrameSource source,
        List<RenderDiagnostic> diagnostics, bool includeDeviceDepth, bool includeHdrColor,
        RenderHdrColorFormat hdrColorFormat, long maximumBytes, CancellationToken cancellationToken)
    {
        RenderJobImage image = source.Render(state, cancellationToken) ??
            throw new InvalidOperationException("The render source returned no image.");
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Width != state.Viewport.Width || image.Height != state.Viewport.Height)
        {
            throw new InvalidDataException("The rendered frame does not match the requested dimensions.");
        }
        if (includeDeviceDepth != (image.DeviceDepth is not null))
        {
            throw new NotSupportedException(
                "The render source did not provide exactly the requested color/depth output set.");
        }
        if (includeHdrColor != (image.HdrColor is not null))
        {
            throw new NotSupportedException("The render source did not provide exactly the requested HDR output.");
        }
        long depthBytes = includeDeviceDepth ? (long)image.Width * image.Height * sizeof(float) : 0;
        long hdrBytes = includeHdrColor && hdrColorFormat == RenderHdrColorFormat.RawRgba16Float
            ? (long)image.Width * image.Height * 8 : 0;
        long extraBytes = depthBytes + hdrBytes;
        if (extraBytes >= maximumBytes)
        {
            throw new RenderOutputQuotaExceededException(
                "The remaining job budget cannot contain all requested planes.");
        }
        ArgumentNullException.ThrowIfNull(image.Diagnostics);
        if (image.Diagnostics.Entries.Count > 128)
        {
            throw new RenderOutputQuotaExceededException("A rendered frame exceeds the diagnostic count limit.");
        }
        foreach (RenderDiagnostic diagnostic in image.Diagnostics.Entries)
        {
            if (diagnostic.Code.Length > 128 || diagnostic.Message.Length > 4096)
            {
                throw new RenderOutputQuotaExceededException("A renderer diagnostic exceeds its text limit.");
            }
            if (!diagnostics.Contains(diagnostic))
            {
                if (diagnostics.Count == 128)
                {
                    throw new RenderOutputQuotaExceededException("The job exceeds its distinct diagnostic limit.");
                }
                diagnostics.Add(diagnostic);
            }
        }
        string fileName = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.png";
        using var file = new FileStream(Path.Combine(directory, fileName),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        long written = PngRgba8Writer.Write(file, image.Width, image.Height,
            image.Rgba.Span, image.RowOrder, maximumBytes - extraBytes, cancellationToken);
        file.Flush(flushToDisk: true);
        file.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        string? depthName = null;
        string? depthHash = null;
        if (image.DeviceDepth is { } depth)
        {
            if (depth.Width != image.Width || depth.Height != image.Height)
            {
                throw new InvalidDataException("The device-depth plane does not match the color frame.");
            }
            depthName = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.device-depth.f32";
            depthHash = WriteDepth(Path.Combine(directory, depthName), depth.Values.Span, cancellationToken);
        }
        string? hdrName = null;
        string? hdrHash = null;
        if (image.HdrColor is { } hdr)
        {
            if (hdr.Width != image.Width || hdr.Height != image.Height)
            {
                throw new InvalidDataException("The HDR plane does not match the display frame.");
            }
            string suffix = hdrColorFormat == RenderHdrColorFormat.Exr ? "exr" : "rgba16f";
            hdrName = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.hdr.{suffix}";
            if (hdrColorFormat == RenderHdrColorFormat.Exr)
            {
                (hdrBytes, hdrHash) = WriteExrColor(
                    Path.Combine(directory, hdrName), hdr, maximumBytes - written - depthBytes, cancellationToken);
            }
            else
            {
                hdrHash = WriteHdrColor(Path.Combine(directory, hdrName), hdr.Rgba16Float.Span, cancellationToken);
            }
        }
        return new RenderDiskFrameResult(
            index, state, fileName, written, hash, depthName, depthBytes, depthHash,
            hdrName, hdrBytes, hdrHash, hdrColorFormat);
    }

    private static (long Bytes, string Sha256) WriteExrColor(
        string path, RenderJobHdrColor hdr, long maximumBytes, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> values = hdr.Rgba16Float.Span;
        for (int start = 0; start < values.Length; start += 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateHdrBlock(values.Slice(start, Math.Min(4096, values.Length - start)));
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        long written = ExrRgba16FloatWriter.Write(
            output, hdr.Width, hdr.Height, values, maximumBytes, cancellationToken);
        output.Flush(flushToDisk: true);
        output.Position = 0;
        return (written, Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant());
    }

    private static void ValidateHdrBlock(ReadOnlySpan<byte> block)
    {
        for (int index = 0; index < block.Length; index += sizeof(ushort))
        {
            if ((BinaryPrimitives.ReadUInt16LittleEndian(block[index..]) & 0x7C00) == 0x7C00)
            {
                throw new InvalidDataException("All stored HDR color channels must be finite.");
            }
        }
    }

    private static string WriteHdrColor(string path, ReadOnlySpan<byte> values, CancellationToken cancellationToken)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        for (int start = 0; start < values.Length; start += 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> block = values.Slice(start, Math.Min(4096, values.Length - start));
            ValidateHdrBlock(block);
            output.Write(block);
        }
        output.Flush(flushToDisk: true);
        output.Position = 0;
        return Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
    }

    private static string WriteDepth(string path, ReadOnlySpan<float> values, CancellationToken cancellationToken)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Span<byte> buffer = stackalloc byte[4096];
        for (int start = 0; start < values.Length; start += buffer.Length / sizeof(float))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length / sizeof(float), values.Length - start);
            for (int index = 0; index < count; index++)
            {
                float value = values[start + index];
                if (!float.IsFinite(value) || value is < 0 or > 1)
                {
                    throw new InvalidDataException("Normalized device-depth samples must be finite and within [0, 1].");
                }
                BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(index * sizeof(float), sizeof(float)), value);
            }
            output.Write(buffer[..(count * sizeof(float))]);
        }
        output.Flush(flushToDisk: true);
        output.Position = 0;
        return Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
    }

    private static long WriteManifest(
        string directory, IReadOnlyList<RenderDiskFrameResult> frames,
        IReadOnlyList<RenderDiagnostic> diagnostics, long maximumBytes,
        RenderProductJobPlan? productPlan, CancellationToken cancellationToken)
    {
        using var file = new FileStream(Path.Combine(directory, "manifest.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var bounded = new OutputBudgetStream(file, maximumBytes, cancellationToken);
        using (var json = new Utf8JsonWriter(bounded, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", 1);
            json.WriteString("kind", "rgba8-sequence");
            if (productPlan is not null)
            {
                WriteProductRequest(json, productPlan);
            }
            json.WriteStartArray("frames");
            foreach (RenderDiskFrameResult frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                json.WriteStartObject();
                json.WriteNumber("index", frame.Index);
                json.WriteString("file", frame.FileName);
                json.WriteString("stage", frame.State.Stage.Identifier);
                json.WriteNumber("stateRevision", frame.State.Revision);
                json.WriteNumber("timeCode", frame.State.Time.TimeCode);
                json.WriteNumber("width", frame.State.Viewport.Width);
                json.WriteNumber("height", frame.State.Viewport.Height);
                if (productPlan is not null)
                {
                    WriteProductFrame(json, productPlan, frame);
                }
                if (frame.DepthFileName is { } depthName)
                {
                    json.WriteStartObject("deviceDepth");
                    json.WriteString("file", depthName);
                    json.WriteString("format", "float32-little-endian");
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", "normalized-device-depth-zero-to-one");
                    json.WriteNumber("clearValue", 1);
                    json.WriteBoolean("independentCoverage", false);
                    json.WriteNumber("bytes", frame.DepthBytes);
                    json.WriteString("sha256", frame.DepthSha256);
                    json.WriteEndObject();
                }
                if (frame.HdrColorFileName is { } hdrName)
                {
                    json.WriteStartObject("hdrColor");
                    json.WriteString("file", hdrName);
                    bool exr = frame.HdrColorFormat == RenderHdrColorFormat.Exr;
                    json.WriteString("format", exr ? "openexr-rgba16float" : "rgba16float-little-endian");
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", "renderer-working-composited-before-exposure-and-display");
                    json.WriteString("alpha", "stored-framebuffer");
                    json.WriteBoolean("displaySelectionIncluded", false);
                    if (exr)
                    {
                        json.WriteString("compression", "zips");
                        json.WriteString("primaries", "unspecified");
                        json.WriteString("alphaAssociation", "unspecified");
                        json.WriteString("windows", "origin-zero-equal-full-raster");
                        json.WriteNumber("pixelAspectRatio", 1);
                    }
                    json.WriteNumber("bytes", frame.HdrColorBytes);
                    json.WriteString("sha256", frame.HdrColorSha256);
                    json.WriteEndObject();
                }
                WriteRequestedState(json, frame.State);
                json.WriteNumber("bytes", frame.Bytes);
                json.WriteString("sha256", frame.Sha256);
                json.WriteEndObject();
                json.Flush();
            }
            json.WriteEndArray();
            json.WriteStartArray("diagnostics");
            foreach (RenderDiagnostic diagnostic in diagnostics)
            {
                json.WriteStartObject();
                json.WriteString("severity", diagnostic.Severity.ToString());
                json.WriteString("code", diagnostic.Code);
                json.WriteString("message", diagnostic.Message);
                json.WriteEndObject();
                json.Flush();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        file.Flush(flushToDisk: true);
        return bounded.BytesWritten;
    }

    private sealed class OutputBudgetStream(
        Stream destination, long maximumBytes, CancellationToken cancellationToken) : Stream
    {
        internal long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > maximumBytes - BytesWritten)
            {
                throw new RenderOutputQuotaExceededException(
                    "The render-job manifest exceeds the remaining output budget.");
            }
            destination.Write(buffer);
            BytesWritten += buffer.Length;
        }
        public override void Flush() => destination.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
