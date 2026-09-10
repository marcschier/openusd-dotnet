// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed partial class OpenUsdMcpService
{
    public async ValueTask<McpSequenceSheetResultDto> ReadSequenceSheetAsync(
        ReadSequenceSheetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateString(request.JobId, OpenUsdMcpLimits.MaximumIdentifierLength, nameof(request.JobId));
        if (request.Width is < 4 or > 1024 || request.Height is < 4 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Sheet dimensions must be between 4 and 1024.");
        }
        IReadOnlyList<int>? requestedIndices = request.FrameIndices;
        int[]? indices = null;
        if (requestedIndices is not null)
        {
            int count = requestedIndices.Count;
            if (count is < 1 or > 16)
            {
                throw new ArgumentException("Choose 1-16 frame indices.", nameof(request));
            }
            indices = new int[count];
            for (int index = 0; index < count; index++)
            {
                indices[index] = requestedIndices[index];
            }
        }
        if (indices is not null &&
            (indices.Any(static index => index < 0) || indices.Distinct().Count() != indices.Length))
        {
            throw new ArgumentException("Choose 1-16 unique nonnegative frame indices.", nameof(request));
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sequences.TryGetValue(request.JobId, out CompletedSequence? sequence))
            {
                throw new OpenUsdMcpFailureException("artifact_not_found", "No completed sequence has that job ID.");
            }
            int totalFrames = sequence.Result.Frames.Count;
            if (indices is null)
            {
                int count = Math.Min(16, totalFrames);
                indices = Enumerable.Range(0, count)
                    .Select(index => count == 1 ? 0 : index * (totalFrames - 1) / (count - 1)).ToArray();
            }
            if (indices.Any(index => index >= totalFrames))
            {
                throw new ArgumentOutOfRangeException(nameof(request), "A selected frame is outside this completed job.");
            }
            int columns = (int)Math.Ceiling(Math.Sqrt(indices.Length));
            int rows = (indices.Length + columns - 1) / columns;
            int width = request.Width / columns;
            int height = request.Height / rows;
            var views = new CaptureView[indices.Length];
            var tiles = new McpSequenceSheetTileDto[indices.Length];
            var frames = new Dictionary<string, RenderDiskFrameResult>(StringComparer.Ordinal);
            for (int index = 0; index < indices.Length; index++)
            {
                RenderDiskFrameResult frame = sequence.Result.Frames[indices[index]];
                string name = frame.Index.ToString(CultureInfo.InvariantCulture);
                frames.Add(name, frame);
                views[index] = new CaptureView(name, CameraState.Default, frame.State.Time.TimeCode);
                tiles[index] = new McpSequenceSheetTileDto(frame.Index, frame.State.Time.TimeCode,
                    index % columns * width, index / columns * height, width, height);
            }
            IArtifactResourceStore store = services.GetRequiredService<IArtifactResourceStore>();
            var factory = new CompletedFrameSheetSource(
                options.OutputRoot, sequence.Result.OutputDirectory, frames, cancellationToken);
            using var processor = new PreviewCaptureProcessor(
                factory, store, new PreviewCaptureLimits(1024, 1024, 16, 8 * 1024 * 1024));
            var capture = new PreviewCaptureRequest(
                $"sequence-sheet-{Guid.NewGuid():N}", CaptureKind.ContactSheet,
                request.Width, request.Height, views);
            PreviewCaptureResult result = await Task.Run(
                () => processor.Process(capture, cancellationToken), cancellationToken).ConfigureAwait(false);
            ArtifactResourceDescriptor artifact = result.Artifacts.Single();
            return new McpSequenceSheetResultDto(
                sequence.Revision.SessionId, sequence.Revision.Generation, sequence.Revision.StageRevision,
                request.JobId, request.Width, request.Height, ToArtifact(artifact),
                new OwnedReadOnlyList<McpSequenceSheetTileDto>(tiles),
                new OwnedReadOnlyList<ArtifactResourceDescriptor>(result.Artifacts.ToArray()));
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class CompletedFrameSheetSource(
        string outputRoot, string directory, IReadOnlyDictionary<string, RenderDiskFrameResult> frames,
        CancellationToken cancellationToken) : IPreviewFrameSourceFactory, IPreviewFrameSource
    {
        public IPreviewFrameSource Create(PreviewCaptureRequest request, CancellationToken cancellationToken) => this;

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderDiskFrameResult frame = frames[view.Name];
            string path = Path.Combine(directory, frame.FileName);
            WorkspacePathContainment.RejectReparsePoints(outputRoot, path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                PngRgba8Image image = CompletedRenderFrameReader.ReadThumbnail(
                    input, frame, width, height, cancellationToken);
                return new ImageRgba8(image.Width, image.Height, image.Pixels);
            }
            catch (InvalidDataException exception)
            {
                throw new ArtifactResourceIntegrityException(exception.Message);
            }
        }

        public void Dispose()
        {
        }
    }
}
