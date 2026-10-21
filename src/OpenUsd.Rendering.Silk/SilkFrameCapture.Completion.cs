// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public static partial class SilkFrameCapture
{
    internal sealed class Completion(
        int width, int height, RenderSettings settings, ulong pageRevision, uint commandCount,
        SilkDepthCaptureRequest? request = null, SilkMeshPreparationUsage? usage = null, int? pageLimit = null,
        SilkOpenColorIoProcessor? processor = null, SilkOpenColorIoDisplayTransform? replayTransform = null)
    {
        private SilkFrameCaptureResult? _result;

        internal SilkDepthCaptureRequest? DisplayRequest => settings.DisplayTransform is null ? null : request;

        internal Completion ForReplay() => new(width, height, settings, pageRevision, commandCount,
            request?.ForReplay(), usage, pageLimit, replayTransform: processor?.Transform ?? replayTransform);

        internal void Complete(
            SilkMeshRenderer renderer, ISilkGraphicsDevice device, ISilkGraphicsTexture color,
            ISilkGraphicsTexture depth, SilkMeshRenderResult rendered, SilkHdrColorCaptureResult? hdr)
        {
            using SilkOpenColorIoProcessor? replayProcessor = processor is null && replayTransform is not null
                ? new SilkOpenColorIoProcessor(replayTransform) : null;
            if (settings.DisplayTransform is not null)
            {
                _result = ReadbackDisplayTransformedFrame(
                    renderer, color, depth, width, height, rendered, pageRevision, commandCount, request, hdr);
            }
            else if ((processor ?? replayProcessor) is { } ocio)
            {
                _result = ReadbackFrameOcio(device, renderer, color, depth, width, height, settings, ocio,
                    rendered, pageRevision, commandCount, renderer.GpuResources.Diagnostics, request);
            }
            else
            {
                _result = ReadbackFrame(device, renderer, color, depth, width, height, settings,
                    rendered, pageRevision, commandCount, renderer.GpuResources.Diagnostics, request);
            }
        }

        internal SilkFrameCaptureResult Finish(SilkMeshRenderer renderer)
        {
            SilkFrameCaptureResult result = _result ??
                throw new InvalidOperationException("The frame capture did not finish before publication.");
            RenderDiagnosticsState diagnostics = settings.DisplayTransform is not null
                ? MergeDisplayTransformDiagnostics(renderer) : renderer.GpuResources.Diagnostics;
            return result.WithCompletion(
                result.RenderResult with { Statistics = renderer.GpuResources.Statistics },
                OpenUsdSilkPage.WithPreparationDiagnostics(diagnostics, usage, pageLimit));
        }
    }
}
