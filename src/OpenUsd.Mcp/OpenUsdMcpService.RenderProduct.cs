// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed partial class OpenUsdMcpService
{
    public async ValueTask<McpRenderSequenceResultDto> RenderProductAsync(
        RenderProductCaptureRequest request, CancellationToken cancellationToken, Action<int, int>? frameEncoded = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FrameCount is < 1 or > 4096 || !double.IsFinite(request.StartTimeCode) ||
            !double.IsFinite(request.TimeStep) || request.TimeStep == 0 ||
            !double.IsFinite(SequenceTimeCode(request.StartTimeCode, request.TimeStep, request.FrameCount - 1)))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Product frame count or time samples are invalid.");
        }
        if (request.Width.HasValue != request.Height.HasValue ||
            request.Width is < 1 or > 4096 || request.Height is < 1 or > 4096)
        {
            throw new ArgumentException(
                "Product raster width and height must be supplied together, each within 1-4096 pixels.",
                nameof(request));
        }
        if (request.DataWindowNdc is { Length: not 4 })
        {
            throw new ArgumentException(
                "Product data window must contain four NDC bounds: minX, minY, maxX, maxY.",
                nameof(request));
        }
        foreach (string? path in new[] { request.SettingsPath, request.ProductPath, request.CameraPath })
        {
            if (path is not null)
            {
                ValidateString(path, OpenUsdMcpLimits.MaximumPathLength, nameof(request));
                WorkspaceEditValidation.ValidatePrimPath(path, nameof(request));
            }
        }
        RenderHdrColorFormat format = request.HdrColorFormat switch
        {
            "raw" => RenderHdrColorFormat.RawRgba16Float,
            "exr" => RenderHdrColorFormat.Exr,
            _ => throw new ArgumentOutOfRangeException(nameof(request), "HDR format must be 'raw' or 'exr'.")
        };
        if (format == RenderHdrColorFormat.Exr && !ExrRgba16FloatWriter.IsSupported)
        {
            throw new OpenUsdMcpFailureException(OpenUsdMcpErrorCodes.RenderFailure,
                "EXR output currently requires Windows x64.");
        }
        var rasterOverrides = new RenderProductOverrides(
            cameraPath: request.CameraPath,
            resolution: request.Width is { } width && request.Height is { } height
                ? new ViewportDimensions(width, height)
                : null,
            pixelAspectRatio: request.PixelAspectRatio,
            aspectRatioConformPolicy: request.AspectRatioConformPolicy,
            dataWindowNdc: request.DataWindowNdc is { } dataWindow
                ? new UsdVec4f(dataWindow[0], dataWindow[1], dataWindow[2], dataWindow[3])
                : null);
        var times = new double[request.FrameCount];
        for (int index = 0; index < times.Length; index++)
        {
            times[index] = SequenceTimeCode(request.StartTimeCode, request.TimeStep, index);
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionSnapshot snapshot = await GetSnapshotAsync(
                request.ToRevisionRequest(), cancellationToken).ConfigureAwait(false);
            RequireDiskJobCapacity();
            var revision = new WorkspaceSessionRevision(request.SessionId, request.Generation, request.StageRevision);
            RenderProductJobPlan plan;
            try
            {
                plan = await workspace.PrepareRenderProductAsync(
                    revision, times, request.SettingsPath, request.ProductPath,
                    rasterOverrides,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException exception)
            {
                throw new OpenUsdMcpFailureException(OpenUsdMcpErrorCodes.RenderFailure,
                    $"Requested product semantics are unsupported: {exception.Message}", exception);
            }
            string id = Guid.NewGuid().ToString("N");
            string parent = Path.Combine(snapshot.Session.OutputDirectory, "renders");
            RenderDiskJobRequest job;
            try
            {
                job = plan.CreateJob(Path.Combine(parent, id), format,
                    new RenderDiskJobLimits(maximumTotalBytes: MaximumDiskJobBytes - _publishedDiskBytes),
                    frameEncoded);
            }
            catch (NotSupportedException exception)
            {
                throw new OpenUsdMcpFailureException(OpenUsdMcpErrorCodes.RenderFailure,
                    $"Requested product encoding is unsupported: {exception.Message}", exception);
            }
            _ = WorkspacePathContainment.CreateContainedDirectory(snapshot.Session.OutputDirectory, parent);
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
                throw new OpenUsdMcpFailureException(OpenUsdMcpErrorCodes.RenderFailure,
                    "Authored-product rendering failed.", exception);
            }
            return RegisterCompletedSequence(revision, id, result, ToAuthoredProduct(plan),
                productFrames: new OwnedReadOnlyList<CompletedProductFrame>(
                    plan.Frames.Select(static frame => new CompletedProductFrame(
                        McpProductRasterDto.From(frame), McpProductCameraDto.From(frame))).ToArray()));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static McpAuthoredProductDto ToAuthoredProduct(RenderProductJobPlan plan) => new(
        plan.Request.Specification.SettingsPath, plan.Request.Product.Path, plan.Request.CameraPath,
        "raw-raster-split-planes-v1", plan.MaterialBindingPurpose,
        new OwnedReadOnlyList<McpProductVariableDto>(plan.Outputs.Select(static output => new McpProductVariableDto(
            output.Variable.Path, output.Variable.SourceName, output.Variable.DataType,
            output.Plane switch
            {
                RenderProductPlane.HdrColor => "hdrColor",
                RenderProductPlane.DeviceDepth => "deviceDepth",
                RenderProductPlane.PrimId => "primId",
                RenderProductPlane.InstanceId => "instanceId",
                _ => throw new InvalidDataException("A product output names an unknown plane.")
            })).ToArray()));

    private readonly record struct CompletedProductFrame(McpProductRasterDto Raster, McpProductCameraDto Camera);
}
