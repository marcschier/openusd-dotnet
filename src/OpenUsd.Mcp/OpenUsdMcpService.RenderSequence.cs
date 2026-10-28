// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed partial class OpenUsdMcpService
{
    private const long MaximumDiskJobBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumDiskJobs = 8;
    private long _publishedDiskBytes;
    private int _publishedDiskJobs;
    private readonly Dictionary<string, CompletedSequence> _sequences = new(StringComparer.Ordinal);

    public async ValueTask<McpRenderSequenceResultDto> RenderSequenceAsync(
        RenderSequenceRequest request, CancellationToken cancellationToken, Action<int, int>? frameEncoded = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        RenderHdrColorFormat hdrColorFormat = request.HdrColorFormat switch
        {
            "raw" => RenderHdrColorFormat.RawRgba16Float,
            "exr" => RenderHdrColorFormat.Exr,
            _ => throw new ArgumentOutOfRangeException(nameof(request), "HDR format must be 'raw' or 'exr'.")
        };
        if (hdrColorFormat == RenderHdrColorFormat.Exr)
        {
            if (!request.IncludeHdrColor)
            {
                throw new ArgumentException("EXR output requires includeHdrColor=true.", nameof(request));
            }
            if (!ExrRgba16FloatWriter.IsSupported)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.RenderFailure, "EXR sequence output currently requires Windows x64.");
            }
        }
        ValidateSequenceRaster(request.Width, request.Height, request.FrameCount,
            request.IncludeDeviceDepth, request.IncludeHdrColor);
        if (!double.IsFinite(request.StartTimeCode) ||
            !double.IsFinite(request.TimeStep) || request.TimeStep == 0 ||
            !double.IsFinite(SequenceTimeCode(request.StartTimeCode, request.TimeStep, request.FrameCount - 1)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), "Sequence dimensions, frame count or times are invalid.");
        }
        if (request.CameraPath is { } cameraPath)
        {
            ValidateString(cameraPath, OpenUsdMcpLimits.MaximumPathLength, nameof(request.CameraPath));
            WorkspaceEditValidation.ValidatePrimPath(cameraPath, nameof(request.CameraPath));
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionSnapshot snapshot = await GetSnapshotAsync(
                request.ToRevisionRequest(), cancellationToken).ConfigureAwait(false);
            RequireDiskJobCapacity();
            var revision = new WorkspaceSessionRevision(
                request.SessionId, request.Generation, request.StageRevision);
            var frames = new StageRenderState[request.FrameCount];
            StageRenderState initial = StageRenderState.Create(new StageIdentity($"session:{request.SessionId}"))
                .WithViewport(new ViewportDimensions(request.Width, request.Height))
                .WithRenderSettings(RenderSettings.PresentationDefault);
            for (int offset = 0; offset < frames.Length; offset += OpenUsdMcpLimits.MaximumViewCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(OpenUsdMcpLimits.MaximumViewCount, frames.Length - offset);
                var times = new double[count];
                for (int index = 0; index < count; index++)
                {
                    times[index] = SequenceTimeCode(request.StartTimeCode, request.TimeStep, offset + index);
                }
                IReadOnlyList<CameraState> cameras = await workspace.CreatePreviewCamerasAsync(
                    revision, request.CameraPath, false, request.Width, request.Height, times, cancellationToken)
                    .ConfigureAwait(false);
                if (cameras.Count != count)
                {
                    throw new OpenUsdMcpFailureException(
                        OpenUsdMcpErrorCodes.RenderFailure,
                        "Camera preparation did not produce exactly one camera per requested sequence sample.");
                }
                for (int index = 0; index < count; index++)
                {
                    frames[offset + index] = initial.WithCamera(cameras[index]).WithTime(new StageTime(times[index]));
                }
            }
            string parent = WorkspacePathContainment.CreateContainedDirectory(
                snapshot.Session.OutputDirectory, Path.Combine(snapshot.Session.OutputDirectory, "renders"));
            string id = Guid.NewGuid().ToString("N");
            string output = Path.Combine(parent, id);
            var job = new RenderDiskJobRequest(output, frames, request.IncludeDeviceDepth, request.IncludeHdrColor,
                hdrColorFormat,
                new RenderDiskJobLimits(maximumTotalBytes: MaximumDiskJobBytes - _publishedDiskBytes))
            {
                FrameEncoded = frameEncoded
            };
            _captureWorker ??= services.GetRequiredService<CaptureWorker>();
            RenderDiskJobResult result;
            try
            {
                result = await _captureWorker.CaptureDiskAsync(job, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and
                not CaptureQueueFullException and not CaptureAdmissionTimeoutException and
                not RenderOutputQuotaExceededException)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.RenderFailure, "Image-sequence rendering failed.", exception);
            }
            return RegisterCompletedSequence(revision, id, result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpSequenceFrameResultDto> ReadSequenceFrameAsync(
        ReadSequenceFrameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateString(request.JobId, OpenUsdMcpLimits.MaximumIdentifierLength, nameof(request.JobId));
        ArgumentOutOfRangeException.ThrowIfNegative(request.FrameIndex);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sequences.TryGetValue(request.JobId, out CompletedSequence? sequence))
            {
                throw new OpenUsdMcpFailureException("artifact_not_found", "No completed sequence has that job ID.");
            }
            if (request.FrameIndex >= sequence.Result.Frames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "The frame index is outside this sequence.");
            }
            RenderDiskFrameResult frame = sequence.Result.Frames[request.FrameIndex];
            string artifactId = $"sequence-{request.JobId}-{frame.Index:D6}.png";
            IArtifactResourceStore store = services.GetRequiredService<IArtifactResourceStore>();
            var outputs = new List<ArtifactResourceFileWrite>(8)
            {
                new(artifactId, "image/png", Path.Combine(sequence.Result.OutputDirectory, frame.FileName),
                    frame.Bytes, frame.Sha256)
            };
            int depthIndex = -1;
            int hdrIndex = -1;
            int primIdIndex = -1;
            int instanceIdIndex = -1;
            int normalIndex = -1;
            int elementIdIndex = -1;
            if (frame.DepthFileName is { } depthName)
            {
                depthIndex = outputs.Count;
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.device-depth.f32", "application/octet-stream",
                    Path.Combine(sequence.Result.OutputDirectory, depthName), frame.DepthBytes, frame.DepthSha256!));
            }
            if (frame.HdrColorFileName is { } hdrName)
            {
                hdrIndex = outputs.Count;
                (string suffix, string mediaType) = frame.HdrColorFormat switch
                {
                    RenderHdrColorFormat.RawRgba16Float => ("rgba16f", "application/octet-stream"),
                    RenderHdrColorFormat.Exr => ("exr", "image/x-exr"),
                    _ => throw new InvalidDataException("The completed HDR plane has no recognized encoding.")
                };
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.hdr.{suffix}", mediaType,
                    Path.Combine(sequence.Result.OutputDirectory, hdrName),
                    frame.HdrColorBytes, frame.HdrColorSha256!));
            }
            if (frame.PrimIdFileName is { } primIdName)
            {
                string tableName = frame.PrimIdTableFileName ??
                    throw new InvalidDataException("The completed primId plane has no identity table.");
                primIdIndex = outputs.Count;
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.prim-id.i32", "application/octet-stream",
                    Path.Combine(sequence.Result.OutputDirectory, primIdName), frame.PrimIdBytes,
                    frame.PrimIdSha256!));
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.prim-id.json", "application/json",
                    Path.Combine(sequence.Result.OutputDirectory, tableName), frame.PrimIdTableBytes,
                    frame.PrimIdTableSha256!));
            }
            if (frame.InstanceIdFileName is { } instanceIdName)
            {
                instanceIdIndex = outputs.Count;
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.instance-id.i32", "application/octet-stream",
                    Path.Combine(sequence.Result.OutputDirectory, instanceIdName), frame.InstanceIdBytes,
                    frame.InstanceIdSha256!));
            }
            if (frame.NormalFileName is { } normalName)
            {
                normalIndex = outputs.Count;
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.normal.f32x3", "application/octet-stream",
                    Path.Combine(sequence.Result.OutputDirectory, normalName), frame.NormalBytes,
                    frame.NormalSha256!));
            }
            if (frame.ElementIdFileName is { } elementIdName)
            {
                elementIdIndex = outputs.Count;
                outputs.Add(new ArtifactResourceFileWrite(
                    $"sequence-{request.JobId}-{frame.Index:D6}.element-id.i32", "application/octet-stream",
                    Path.Combine(sequence.Result.OutputDirectory, elementIdName), frame.ElementIdBytes,
                    frame.ElementIdSha256!));
            }
            var descriptors = new ArtifactResourceDescriptor[outputs.Count];
            var uncached = new List<ArtifactResourceFileWrite>(outputs.Count);
            for (int index = 0; index < outputs.Count; index++)
            {
                ArtifactResourceFileWrite output = outputs[index];
                if (store.TryGetDescriptor(ArtifactResourceUri.Create(output.Id), out var existing))
                {
                    RequireSequenceResource(existing, output);
                    descriptors[index] = existing!;
                }
                else
                {
                    WorkspacePathContainment.RejectReparsePoints(options.OutputRoot, output.SourcePath);
                    uncached.Add(output);
                }
            }
            if (uncached.Count != 0)
            {
                IReadOnlyList<ArtifactResourceDescriptor> published;
                if (uncached.Count == 1)
                {
                    ArtifactResourceFileWrite output = uncached[0];
                    published = [await store.AddVerifiedFileAsync(output.Id, output.MediaType,
                        output.SourcePath, output.ExpectedByteLength, output.ExpectedSha256, cancellationToken)
                        .ConfigureAwait(false)];
                }
                else
                {
                    published = await store.AddVerifiedFilesAsync(uncached, cancellationToken).ConfigureAwait(false);
                }
                if (published.Count != uncached.Count)
                {
                    throw new ArtifactResourceIntegrityException("The artifact store returned an incomplete frame.");
                }
                int added = 0;
                for (int index = 0; index < descriptors.Length; index++)
                {
                    if (descriptors[index] is null)
                    {
                        RequireSequenceResource(published[added], outputs[index]);
                        descriptors[index] = published[added++];
                    }
                }
            }
            ArtifactResourceDescriptor descriptor = descriptors[0];
            ArtifactResourceDescriptor? depth = depthIndex < 0 ? null : descriptors[depthIndex];
            ArtifactResourceDescriptor? hdr = hdrIndex < 0 ? null : descriptors[hdrIndex];
            ArtifactResourceDescriptor? primId = primIdIndex < 0 ? null : descriptors[primIdIndex];
            ArtifactResourceDescriptor? primIdTable = primIdIndex < 0 ? null : descriptors[primIdIndex + 1];
            ArtifactResourceDescriptor? instanceId = instanceIdIndex < 0 ? null : descriptors[instanceIdIndex];
            ArtifactResourceDescriptor? normal = normalIndex < 0 ? null : descriptors[normalIndex];
            ArtifactResourceDescriptor? elementId = elementIdIndex < 0 ? null : descriptors[elementIdIndex];
            return new McpSequenceFrameResultDto(sequence.Revision.SessionId, sequence.Revision.Generation,
                sequence.Revision.StageRevision, request.JobId, frame.Index, frame.State.Time.TimeCode,
                frame.State.Viewport.Width, frame.State.Viewport.Height, ToArtifact(descriptor),
                new OwnedReadOnlyList<ArtifactResourceDescriptor>(descriptors))
            {
                DeviceDepth = depth is null ? null : ToArtifact(depth),
                HdrColor = hdr is null ? null : ToArtifact(hdr),
                HdrColorFormat = hdr is null ? null : frame.HdrColorFormat == RenderHdrColorFormat.Exr ? "exr" : "raw",
                PrimId = primId is null ? null : ToArtifact(primId),
                PrimIdTable = primIdTable is null ? null : ToArtifact(primIdTable),
                InstanceId = instanceId is null ? null : ToArtifact(instanceId),
                Normal = normal is null ? null : ToArtifact(normal),
                ElementId = elementId is null ? null : ToArtifact(elementId),
                AuthoredProduct = sequence.AuthoredProduct,
                ProductRaster = sequence.ProductFrames?[frame.Index].Raster,
                ProductCamera = sequence.ProductFrames?[frame.Index].Camera,
                Simulation = frame.Simulation is { } simulation ? McpSimulationFrameDto.From(simulation) : null,
                CameraRequest = McpCameraRequestDto.From(frame.State.Camera),
                RenderRequest = McpRenderRequestDto.From(frame.State)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RequireSequenceResource(
        ArtifactResourceDescriptor? descriptor, ArtifactResourceFileWrite expected)
    {
        if (descriptor is null || descriptor.Id != expected.Id ||
            descriptor.ResourceUri != ArtifactResourceUri.Create(expected.Id) ||
            descriptor.MediaType != expected.MediaType || descriptor.ByteLength != expected.ExpectedByteLength ||
            !string.Equals(descriptor.Sha256, expected.ExpectedSha256, StringComparison.Ordinal))
        {
            throw new ArtifactResourceIntegrityException("The sequence resource does not match the completed job.");
        }
    }

    private void RequireDiskJobCapacity()
    {
        if (_publishedDiskJobs >= MaximumDiskJobs || _publishedDiskBytes >= MaximumDiskJobBytes)
        {
            throw new WorkspaceQuotaExceededException("The MCP process has exhausted its 8-job/4-GiB disk quota.");
        }
    }

    private static double SequenceTimeCode(double start, double step, int index)
    {
        double time = start + index * step;
        // Preserve ordinary two-rounding samples; fuse only an otherwise overflowing intermediate.
        return double.IsFinite(time) ? time : Math.FusedMultiplyAdd(index, step, start);
    }

    private static void ValidateSequenceRaster(int width, int height, int frameCount, bool depth, bool hdr)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096 || frameCount is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Sequence dimensions or frame count are invalid.");
        }
        long charge = (long)width * height * (depth
            || hdr ? RenderDiskJobRequest.ExpandedCaptureManagedBytesPerPixel : 4);
        if (charge > RenderDiskJobLimits.Default.MaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(width),
                "The requested raster exceeds the managed frame budget.");
        }
    }

    public async ValueTask<McpSequenceJobsResultDto> ListSequenceJobsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new McpSequenceJobsResultDto(new OwnedReadOnlyList<McpRenderSequenceResultDto>(
                _sequences.OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                    .Select(static entry => entry.Value.Descriptor).ToArray()));
        }
        finally
        {
            _gate.Release();
        }
    }

    private McpRenderSequenceResultDto RegisterCompletedSequence(
        WorkspaceSessionRevision revision, string id, RenderDiskJobResult result,
        McpAuthoredProductDto? authoredProduct = null, RenderPhysicsSequenceRequest? physicsRequest = null,
        IReadOnlyList<CompletedProductFrame>? productFrames = null)
    {
        string relative = Path.GetRelativePath(options.OutputRoot, result.OutputDirectory).Replace('\\', '/');
        var descriptor = new McpRenderSequenceResultDto(revision.SessionId, revision.Generation, revision.StageRevision,
            id, relative, relative + "/manifest.json", result.Frames.Count, result.TotalBytes,
            Array.AsReadOnly(result.Diagnostics.Select(ToDiagnostic).ToArray()))
        {
            AuthoredProduct = authoredProduct,
            Simulation = result.Frames[0].Simulation is { } simulation
                ? new McpSimulationRecordingDto(simulation.Epoch, simulation.ScenarioIdentity, simulation.StepIndex,
                    result.Frames[^1].Simulation!.StepIndex, simulation.FixedStepSeconds) : null
        };
        if (physicsRequest is not null)
        {
            descriptor = descriptor with
            {
                Simulation = descriptor.Simulation! with
                {
                    MetersPerUnit = physicsRequest.ExpectedMetersPerUnit,
                    UpAxis = physicsRequest.ExpectedUpAxis,
                    FullScenePhysics = physicsRequest.FullScenePhysics
                }
            };
        }
        _sequences.Add(id, new CompletedSequence(revision, result, authoredProduct, descriptor)
        {
            ProductFrames = productFrames
        });
        _publishedDiskJobs++;
        _publishedDiskBytes += result.TotalBytes;
        return descriptor;
    }

    private sealed record CompletedSequence(
        WorkspaceSessionRevision Revision, RenderDiskJobResult Result, McpAuthoredProductDto? AuthoredProduct,
        McpRenderSequenceResultDto Descriptor)
    {
        internal IReadOnlyList<CompletedProductFrame>? ProductFrames { get; init; }
    }
}
