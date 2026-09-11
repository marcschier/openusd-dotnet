// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed partial class OpenUsdMcpService
{
    public async ValueTask<McpRenderSequenceResultDto> RenderProductAsync(
        RenderProductCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FrameCount is < 1 or > 4096 || !double.IsFinite(request.StartTimeCode) ||
            !double.IsFinite(request.TimeStep) || request.TimeStep == 0 ||
            !double.IsFinite(request.StartTimeCode + (request.FrameCount - 1) * request.TimeStep))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Product frame count or time samples are invalid.");
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
        var times = new double[request.FrameCount];
        for (int index = 0; index < times.Length; index++)
        {
            times[index] = request.StartTimeCode + index * request.TimeStep;
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
                    new RenderProductOverrides(cameraPath: request.CameraPath),
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
                    new RenderDiskJobLimits(maximumTotalBytes: MaximumDiskJobBytes - _publishedDiskBytes));
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
            return RegisterCompletedSequence(revision, id, result, ToAuthoredProduct(plan));
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
            output.Plane == RenderProductPlane.HdrColor ? "hdrColor" : "deviceDepth")).ToArray()));
}
