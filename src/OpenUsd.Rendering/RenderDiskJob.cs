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
    /// If cleanup also fails, an aggregate preserves both errors. Already-absent staging is treated as cleaned up.
    /// The output parent is caller-approved; this is not a sandbox for arbitrary filesystem writers.
    /// </remarks>
    public static RenderDiskJobResult Execute(
        RenderDiskJobRequest request, IRenderJobFrameSource source, CancellationToken cancellationToken = default)
        => Execute(request, source, TimeProvider.System, cancellationToken);

    internal static RenderDiskJobResult Execute(
        RenderDiskJobRequest request, IRenderJobFrameSource source,
        TimeProvider timeProvider, CancellationToken cancellationToken)
        => Execute(request, source, timeProvider, beforePublish: null, cancellationToken);

    internal static RenderDiskJobResult Execute(
        RenderDiskJobRequest request, IRenderJobFrameSource source,
        TimeProvider timeProvider, Action? beforePublish, CancellationToken cancellationToken)
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
                    request.IncludeHdrColor, request.HdrColorFormat, ProductExrWindows(request, index),
                    request.ProductPlan?.Frames[index].CameraSettings!.LinearExposureScale ?? 1, remaining,
                    request.ProductPlan?.AccumulationStates(index), request.IncludePrimId, request.IncludeInstanceId,
                    request.IncludeNormal, request.IncludeElementId, cancellationToken);
                total += frames[index].Bytes + frames[index].DepthBytes + frames[index].HdrColorBytes +
                    frames[index].PrimIdBytes + frames[index].PrimIdTableBytes + frames[index].InstanceIdBytes +
                    frames[index].NormalBytes + frames[index].ElementIdBytes;
                request.FrameEncoded?.Invoke(index + 1, frames.Length);
            }
            beforePublish?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
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
        catch (Exception operationFailure)
        {
            if (owned && !published)
            {
                try
                {
                    RetryDirectorySharing(
                        () => DeleteOwnedStaging(staging), timeProvider, CancellationToken.None);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "The render job failed and its owned staging output could not be removed.",
                        operationFailure, cleanupFailure);
                }
            }
            throw;
        }
    }

    private static void DeleteOwnedStaging(string staging)
    {
        try
        {
            Directory.Delete(staging, recursive: true);
        }
        catch (DirectoryNotFoundException) when (!Directory.Exists(staging) && !File.Exists(staging))
        {
            // Another filesystem actor may already have removed this owned directory.
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
        RenderHdrColorFormat hdrColorFormat, ExrImageWindows? exrWindows, double hdrExposureScale, long maximumBytes,
        IReadOnlyList<StageRenderState>? shutterStates, bool includePrimId, bool includeInstanceId,
        bool includeNormal, bool includeElementId, CancellationToken cancellationToken)
    {
        RenderJobImage image = (shutterStates is null
            ? source.Render(state, cancellationToken)
            : AccumulateShutter(state, shutterStates, source, includeHdrColor, cancellationToken)) ??
            throw new InvalidOperationException("The render source returned no image.");
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Width != state.Viewport.Width || image.Height != state.Viewport.Height)
        {
            throw new InvalidDataException("The rendered frame does not match the requested dimensions.");
        }
        if (image.Simulation is { } simulation && simulation.TimeCode != state.Time.TimeCode)
        {
            throw new InvalidDataException("The simulation publication does not match the requested frame time.");
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
        if (includePrimId != (image.PrimId is not null))
        {
            throw new NotSupportedException(
                "The render source did not provide exactly the requested prim identity output.");
        }
        if (includeInstanceId != (image.InstanceId is not null))
        {
            throw new NotSupportedException(
                "The render source did not provide exactly the requested instance identity output.");
        }
        if (includeNormal != (image.Normal is not null))
        {
            throw new NotSupportedException(
                "The render source did not provide exactly the requested normal output.");
        }
        if (includeElementId != (image.ElementId is not null))
        {
            throw new NotSupportedException(
                "The render source did not provide exactly the requested element identity output.");
        }
        long depthBytes = includeDeviceDepth ? (long)image.Width * image.Height * sizeof(float) : 0;
        long hdrBytes = includeHdrColor && hdrColorFormat == RenderHdrColorFormat.RawRgba16Float
            ? (long)image.Width * image.Height * 8 : 0;
        long primIdBytes = includePrimId ? (long)image.Width * image.Height * sizeof(int) : 0;
        long instanceIdBytes = includeInstanceId ? (long)image.Width * image.Height * sizeof(int) : 0;
        long normalBytes = includeNormal ? (long)image.Width * image.Height * NormalBytesPerPixel : 0;
        long elementIdBytes = includeElementId ? (long)image.Width * image.Height * sizeof(int) : 0;
        long extraBytes = depthBytes + hdrBytes + primIdBytes + instanceIdBytes + normalBytes + elementIdBytes;
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
            if (diagnostic.Severity is not (RenderDiagnosticSeverity.Information or
                RenderDiagnosticSeverity.Warning or RenderDiagnosticSeverity.Error))
            {
                throw new InvalidDataException("A renderer diagnostic has an unsupported severity.");
            }
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
        string? primIdName = null;
        string? primIdHash = null;
        string? primIdTableName = null;
        string? primIdTableHash = null;
        long primIdTableBytes = 0;
        if (image.PrimId is { } primId)
        {
            if (primId.Width != image.Width || primId.Height != image.Height)
            {
                throw new InvalidDataException("The prim identity plane does not match the color frame.");
            }
            string prefix = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}";
            primIdName = prefix + ".prim-id.i32";
            primIdHash = WriteIndexPlane(
                Path.Combine(directory, primIdName), primId.Values.Span, primId.Identities.Count,
                "A prim identity index is outside its identity table.", cancellationToken);
            primIdTableName = prefix + ".prim-id.json";
            (primIdTableBytes, primIdTableHash) = WritePrimIdTable(
                Path.Combine(directory, primIdTableName), primId.Identities, maximumBytes - extraBytes,
                cancellationToken);
            extraBytes += primIdTableBytes;
        }
        string? instanceIdName = null;
        string? instanceIdHash = null;
        if (image.InstanceId is { } instanceId)
        {
            if (instanceId.Width != image.Width || instanceId.Height != image.Height)
            {
                throw new InvalidDataException("The instance identity plane does not match the color frame.");
            }
            instanceIdName = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.instance-id.i32";
            instanceIdHash = WriteIndexPlane(
                Path.Combine(directory, instanceIdName), instanceId.Values.Span, int.MaxValue,
                "An instance index must be minus one or non-negative.", cancellationToken);
        }
        string? elementIdName = null;
        string? elementIdHash = null;
        if (image.ElementId is { } elementId)
        {
            if (elementId.Width != image.Width || elementId.Height != image.Height)
            {
                throw new InvalidDataException("The element identity plane does not match the color frame.");
            }
            elementIdName = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.element-id.i32";
            elementIdHash = WriteIndexPlane(
                Path.Combine(directory, elementIdName), elementId.Values.Span, int.MaxValue,
                "An element index must be minus one or non-negative.", cancellationToken);
        }
        string? normalName = null;
        string? normalHash = null;
        if (image.Normal is { } normal)
        {
            if (normal.Width != image.Width || normal.Height != image.Height)
            {
                throw new InvalidDataException("The normal plane does not match the color frame.");
            }
            normalName = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.normal.f32x3";
            normalHash = WriteFloatPlane(Path.Combine(directory, normalName), normal.Values.Span, cancellationToken);
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
                    Path.Combine(directory, hdrName), hdr, exrWindows, hdrExposureScale,
                    maximumBytes - written - depthBytes - primIdBytes - primIdTableBytes - instanceIdBytes -
                    normalBytes - elementIdBytes, cancellationToken);
            }
            else
            {
                hdrHash = WriteHdrColor(
                    Path.Combine(directory, hdrName), hdr.Rgba16Float.Span, hdrExposureScale, cancellationToken);
            }
        }
        return new RenderDiskFrameResult(
            index, state, fileName, written, hash, depthName, depthBytes, depthHash,
            hdrName, hdrBytes, hdrHash, hdrColorFormat, image.Simulation)
        {
            PrimIdFileName = primIdName,
            PrimIdBytes = primIdBytes,
            PrimIdSha256 = primIdHash,
            PrimIdTableFileName = primIdTableName,
            PrimIdTableBytes = primIdTableBytes,
            PrimIdTableSha256 = primIdTableHash,
            InstanceIdFileName = instanceIdName,
            InstanceIdBytes = instanceIdBytes,
            InstanceIdSha256 = instanceIdHash,
            NormalFileName = normalName,
            NormalBytes = normalBytes,
            NormalSha256 = normalHash,
            ElementIdFileName = elementIdName,
            ElementIdBytes = elementIdBytes,
            ElementIdSha256 = elementIdHash
        };
    }

    private static ExrImageWindows? ProductExrWindows(RenderDiskJobRequest request, int index)
    {
        if (request.HdrColorFormat != RenderHdrColorFormat.Exr || request.ProductPlan is not { } plan)
        {
            return null;
        }
        RenderPreparedFrame frame = plan.Frames[index];
        ViewportDimensions full = plan.Request.Overrides.Resolution ??
            new ViewportDimensions(plan.Request.Product.Width, plan.Request.Product.Height);
        return ExrImageWindows.FromProductFrame(
            full, frame.DataWindowMinX, frame.DataWindowMinY, frame.OutputDimensions.Height, frame.PixelAspectRatio);
    }

    private static (long Bytes, string Sha256) WriteExrColor(
        string path, RenderJobHdrColor hdr, ExrImageWindows? windows, double exposureScale, long maximumBytes,
        CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> values = hdr.Rgba16Float.Span;
        for (int start = 0; start < values.Length; start += 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateHdrBlock(values.Slice(start, Math.Min(4096, values.Length - start)));
        }
        if (exposureScale != 1)
        {
            // The encoder takes the whole plane, so a camera-exposed product needs one transient copy.
            byte[] exposed = GC.AllocateUninitializedArray<byte>(values.Length);
            ProductCameraExposure.ScaleHdr(values, exposed, exposureScale);
            values = exposed;
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        long written = ExrRgba16FloatWriter.Write(
            output, hdr.Width, hdr.Height, values, Rgba16FloatRowOrder.TopDown, ExrStoredAlphaPolicy.Unspecified,
            windows, maximumBytes, cancellationToken);
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

    private static string WriteHdrColor(
        string path, ReadOnlySpan<byte> values, double exposureScale, CancellationToken cancellationToken)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Span<byte> exposed = stackalloc byte[4096];
        for (int start = 0; start < values.Length; start += 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> block = values.Slice(start, Math.Min(4096, values.Length - start));
            ValidateHdrBlock(block);
            if (exposureScale != 1)
            {
                ProductCameraExposure.ScaleHdr(block, exposed[..block.Length], exposureScale);
                output.Write(exposed[..block.Length]);
            }
            else
            {
                output.Write(block);
            }
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

    private static string WriteFloatPlane(string path, ReadOnlySpan<float> values, CancellationToken cancellationToken)
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
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("A normal component must be finite.");
                }
                BinaryPrimitives.WriteSingleLittleEndian(buffer.Slice(index * sizeof(float), sizeof(float)), value);
            }
            output.Write(buffer[..(count * sizeof(float))]);
        }
        output.Flush(flushToDisk: true);
        output.Position = 0;
        return Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
    }

    private static string WriteIndexPlane(
        string path, ReadOnlySpan<int> values, int exclusiveMaximum, string rangeMessage,
        CancellationToken cancellationToken)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Span<byte> buffer = stackalloc byte[4096];
        for (int start = 0; start < values.Length; start += buffer.Length / sizeof(int))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length / sizeof(int), values.Length - start);
            for (int index = 0; index < count; index++)
            {
                int value = values[start + index];
                if (value < -1 || value >= exclusiveMaximum)
                {
                    throw new InvalidDataException(rangeMessage);
                }
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(index * sizeof(int), sizeof(int)), value);
            }
            output.Write(buffer[..(count * sizeof(int))]);
        }
        output.Flush(flushToDisk: true);
        output.Position = 0;
        return Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
    }

    private static (long Bytes, string Sha256) WritePrimIdTable(
        string path, IReadOnlyList<RenderJobPrimIdentity> identities, long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        long written;
        using (var bounded = new OutputBudgetStream(
            file, maximumBytes, cancellationToken, "A prim identity table exceeds the remaining frame budget."))
        {
            using (var json = new Utf8JsonWriter(bounded, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteNumber("background", -1);
                json.WriteStartArray("identities");
                for (int index = 0; index < identities.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RenderJobPrimIdentity identity = identities[index];
                    json.WriteStartObject();
                    json.WriteNumber("index", index);
                    json.WriteString("path", identity.Path);
                    json.WriteNumber("instanceIndex", identity.InstanceIndex);
                    json.WriteEndObject();
                    json.Flush();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            written = bounded.BytesWritten;
        }
        file.Flush(flushToDisk: true);
        file.Position = 0;
        return (written, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
    }

    private const string InstanceIdConvention = "innermost-instancer-instance-index";
    private const string ElementIdConvention = "authored-face-index";
    private const string NormalConvention = "world-space-unit-normal-float32x3";
    private const string NormalFormat = "float32x3-little-endian";
    private const int NormalBytesPerPixel = 3 * sizeof(float);

    private static void WriteNormalBackground(Utf8JsonWriter json)
    {
        json.WriteStartArray("background");
        json.WriteNumberValue(0);
        json.WriteNumberValue(0);
        json.WriteNumberValue(0);
        json.WriteEndArray();
    }

    private static string HdrColorFormatName(RenderHdrColorFormat? format) => format switch
    {
        RenderHdrColorFormat.Exr => "openexr-rgba16float",
        RenderHdrColorFormat.RawRgba16Float => "rgba16float-little-endian",
        _ => throw new InvalidDataException("A published HDR plane must identify its stored format.")
    };

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
                json.WriteString("format", "png-rgba8");
                json.WriteString("stage", frame.State.Stage.Identifier);
                json.WriteNumber("stateRevision", frame.State.Revision);
                json.WriteNumber("timeCode", frame.State.Time.TimeCode);
                json.WriteNumber("width", frame.State.Viewport.Width);
                json.WriteNumber("height", frame.State.Viewport.Height);
                if (frame.Simulation is { } simulation)
                {
                    WriteSimulationIdentity(json, simulation);
                }
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
                if (frame.PrimIdFileName is { } primIdName)
                {
                    json.WriteStartObject("primId");
                    json.WriteString("file", primIdName);
                    json.WriteString("format", "int32-little-endian");
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", "visible-prim-identity-index");
                    json.WriteNumber("background", -1);
                    json.WriteNumber("bytes", frame.PrimIdBytes);
                    json.WriteString("sha256", frame.PrimIdSha256);
                    json.WriteString("tableFile", frame.PrimIdTableFileName);
                    json.WriteString("tableFormat", "utf8-json");
                    json.WriteNumber("tableBytes", frame.PrimIdTableBytes);
                    json.WriteString("tableSha256", frame.PrimIdTableSha256);
                    json.WriteEndObject();
                }
                if (frame.InstanceIdFileName is { } instanceIdName)
                {
                    json.WriteStartObject("instanceId");
                    json.WriteString("file", instanceIdName);
                    json.WriteString("format", "int32-little-endian");
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", InstanceIdConvention);
                    json.WriteNumber("background", -1);
                    json.WriteNumber("bytes", frame.InstanceIdBytes);
                    json.WriteString("sha256", frame.InstanceIdSha256);
                    json.WriteEndObject();
                }
                if (frame.ElementIdFileName is { } elementIdName)
                {
                    json.WriteStartObject("elementId");
                    json.WriteString("file", elementIdName);
                    json.WriteString("format", "int32-little-endian");
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", ElementIdConvention);
                    json.WriteNumber("background", -1);
                    json.WriteNumber("bytes", frame.ElementIdBytes);
                    json.WriteString("sha256", frame.ElementIdSha256);
                    json.WriteEndObject();
                }
                if (frame.NormalFileName is { } normalName)
                {
                    json.WriteStartObject("normal");
                    json.WriteString("file", normalName);
                    json.WriteString("format", NormalFormat);
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", NormalConvention);
                    WriteNormalBackground(json);
                    json.WriteNumber("bytes", frame.NormalBytes);
                    json.WriteString("sha256", frame.NormalSha256);
                    json.WriteEndObject();
                }
                if (frame.HdrColorFileName is { } hdrName)
                {
                    json.WriteStartObject("hdrColor");
                    json.WriteString("file", hdrName);
                    bool exr = frame.HdrColorFormat == RenderHdrColorFormat.Exr;
                    json.WriteString("format", HdrColorFormatName(frame.HdrColorFormat));
                    json.WriteString("rowOrder", "top-down");
                    json.WriteString("convention", productPlan is null
                        ? "renderer-working-composited-before-exposure-and-display"
                        : "renderer-working-composited-camera-exposed-before-display");
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
        Stream destination, long maximumBytes, CancellationToken cancellationToken,
        string exceededMessage = "The render-job manifest exceeds the remaining output budget.") : Stream
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
                throw new RenderOutputQuotaExceededException(exceededMessage);
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
