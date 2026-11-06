// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerRenderCoordinator
{
    internal string? GetPhysicsSequenceUnsupportedReason(ViewerRenderSequenceOutputOptions outputs)
    {
        ViewerRenderBackend? backend = _backendRegistry.CaptureRenderSequenceBackend();
        if (backend is null || backend.Identity.Kind == RenderBackendKind.Storm ||
            !backend.SupportsPhysicsTransformOverrides)
        {
            return "Physics recording requires a completed hdSilk frame with rigid-pose capture support.";
        }
        return backend.GetRenderSequenceUnsupportedReason(CurrentState, outputs);
    }

    internal Task<RenderDiskJobResult> RenderPhysicsSequenceAsync(
        ViewerPhysicsController physics, ViewerPhysicsSequenceRequest request, string? cameraPath,
        Action<ViewerRenderSequenceProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        request.Validate();
        if (GetPhysicsSequenceUnsupportedReason(request.Outputs) is { } unsupported)
        {
            throw new NotSupportedException(unsupported);
        }
        return StartRenderSequence(
            token => physics.RecordAsync(
                (session, physicsToken) => RenderSequenceCoreAsync(
                    session.CreateTimes(request.FrameCount, request.StepsPerFrame),
                    request.OutputParent, request.Outputs, cameraPath, progress, physicsToken, session, request),
                token),
            cancellationToken);
    }
}
