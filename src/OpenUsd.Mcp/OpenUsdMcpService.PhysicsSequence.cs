// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Physics;

namespace OpenUsd.Mcp;

internal sealed partial class OpenUsdMcpService
{
    public async ValueTask<McpRenderSequenceResultDto> RenderPhysicsSequenceAsync(
        RenderPhysicsSequenceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateString(request.ScenarioIdentity, 256, nameof(request.ScenarioIdentity));
        if (!double.IsFinite(request.ExpectedMetersPerUnit) || request.ExpectedMetersPerUnit <= 0 ||
            request.ExpectedUpAxis is not ("X" or "Y" or "Z"))
        {
            throw new ArgumentException("Explicit positive workspace units and an X/Y/Z up axis are required.", nameof(request));
        }
        ValidateSequenceRaster(request.Width, request.Height, request.FrameCount,
            request.IncludeDeviceDepth, request.IncludeHdrColor);
        if (request.StepsPerFrame is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Physics captures require 1-8 fixed steps per frame.");
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
            var revision = new WorkspaceSessionRevision(request.SessionId, request.Generation, request.StageRevision);
            WorkspacePhysicsRecording recording = await workspace.PreparePhysicsRecordingAsync(
                revision, request.ExpectedMetersPerUnit, request.ExpectedUpAxis, request.FullScenePhysics, cancellationToken)
                .ConfigureAwait(false);
            McpRenderSequenceResultDto completed;
            try
            {
                var frames = new StageRenderState[request.FrameCount];
                StageRenderState initial = StageRenderState.Create(new StageIdentity($"session:{request.SessionId}"))
                    .WithViewport(new ViewportDimensions(request.Width, request.Height))
                    .WithRenderSettings(RenderSettings.PresentationDefault);
                for (int offset = 0; offset < frames.Length; offset += OpenUsdMcpLimits.MaximumViewCount)
                {
                    int count = Math.Min(OpenUsdMcpLimits.MaximumViewCount, frames.Length - offset);
                    var times = new double[count];
                    for (int index = 0; index < count; index++)
                    {
                        double seconds = ((ulong)(offset + index + 1) * (ulong)request.StepsPerFrame) *
                            recording.Transport.FixedStep.Seconds;
                        if (recording.Transport.Timeline.HasAuthoredRange &&
                            seconds > recording.Transport.Timeline.DurationSeconds +
                                recording.Transport.FixedStep.Seconds * 1e-6)
                        {
                            throw new ArgumentOutOfRangeException(nameof(request), "Recording would pass the authored end.");
                        }
                        times[index] = recording.Transport.Timeline.ToTimeCode(seconds);
                    }
                    IReadOnlyList<CameraState> cameras = await workspace.CreatePreviewCamerasAsync(
                        revision, request.CameraPath, false, request.Width, request.Height, times, cancellationToken)
                        .ConfigureAwait(false);
                    if (cameras.Count != count)
                    {
                        throw new InvalidDataException("Camera preparation did not produce every physics frame.");
                    }
                    for (int index = 0; index < count; index++)
                    {
                        frames[offset + index] = initial.WithCamera(cameras[index]).WithTime(new StageTime(times[index]));
                    }
                }
                string parent = WorkspacePathContainment.CreateContainedDirectory(
                    snapshot.Session.OutputDirectory, Path.Combine(snapshot.Session.OutputDirectory, "renders"));
                string id = Guid.NewGuid().ToString("N");
                var job = new RenderDiskJobRequest(Path.Combine(parent, id), frames,
                    request.IncludeDeviceDepth, request.IncludeHdrColor,
                    new RenderDiskJobLimits(maximumTotalBytes: MaximumDiskJobBytes - _publishedDiskBytes));
                _captureWorker ??= services.GetRequiredService<CaptureWorker>();
                RenderDiskJobResult result;
                try
                {
                    result = await _captureWorker.CapturePhysicsDiskAsync(job, recording,
                        new PhysicsRenderRecordingContext(request.ScenarioIdentity, recording.SourceRevision),
                        request.StepsPerFrame, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception operationFailure)
                {
                    try
                    {
                        await _captureWorker.ResetAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException("Physics capture failed and its renderer could not be reset.",
                            operationFailure, cleanupFailure);
                    }
                    throw;
                }
                completed = RegisterCompletedSequence(revision, id, result);
                completed = completed with
                {
                    Simulation = completed.Simulation! with
                    {
                        MetersPerUnit = request.ExpectedMetersPerUnit,
                        UpAxis = request.ExpectedUpAxis,
                        FullScenePhysics = request.FullScenePhysics
                    }
                };
            }
            catch (Exception operationFailure)
            {
                try
                {
                    await recording.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Physics recording failed and its world could not be disposed.",
                        operationFailure, cleanupFailure);
                }
                throw;
            }
            try
            {
                await recording.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new OpenUsdMcpFailureException(OpenUsdMcpErrorCodes.RenderFailure,
                    $"Physics job '{completed.JobId}' was published, but solver teardown failed.", cleanupFailure);
            }
            return completed;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException)
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.RenderFailure, exception.Message, exception);
        }
        finally
        {
            _gate.Release();
        }
    }
}
