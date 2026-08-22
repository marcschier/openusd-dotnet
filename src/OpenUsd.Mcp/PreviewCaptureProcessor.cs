// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

public interface IPreviewCaptureProcessor
{
    PreviewCaptureResult Process(
        PreviewCaptureRequest request,
        CancellationToken cancellationToken = default);
}

internal interface IResettablePreviewCaptureProcessor
{
    void Reset();
}

public interface IPreviewFrameSourceFactory
{
    IPreviewFrameSource Create(
        PreviewCaptureRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPreviewFrameSource : IDisposable
{
    ImageRgba8 Capture(CaptureView view, int width, int height);
}

public sealed class PreviewCaptureProcessor :
    IPreviewCaptureProcessor,
    IResettablePreviewCaptureProcessor,
    IDisposable
{
    private readonly IArtifactResourceStore _artifactStore;
    private readonly IPreviewFrameSourceFactory _frameSourceFactory;
    private readonly PreviewCaptureLimits _limits;
    private IPreviewFrameSource? _frameSource;
    private PreviewCaptureProcessorState _state;

    internal bool IsDisposePending =>
        _state == PreviewCaptureProcessorState.DisposePending;

    public PreviewCaptureProcessor(
        IPreviewFrameSourceFactory frameSourceFactory,
        IArtifactResourceStore artifactStore)
        : this(frameSourceFactory, artifactStore, new PreviewCaptureLimits())
    {
    }

    public PreviewCaptureProcessor(
        IPreviewFrameSourceFactory frameSourceFactory,
        IArtifactResourceStore artifactStore,
        PreviewCaptureLimits limits)
    {
        _frameSourceFactory = frameSourceFactory
            ?? throw new ArgumentNullException(nameof(frameSourceFactory));
        _artifactStore = artifactStore
            ?? throw new ArgumentNullException(nameof(artifactStore));
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumViews);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumTotalArtifactBytes);
        _limits = limits;
    }

    public PreviewCaptureResult Process(
        PreviewCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(
            _state is PreviewCaptureProcessorState.DisposePending or
                PreviewCaptureProcessorState.Disposed,
            this);
        if (_state == PreviewCaptureProcessorState.ResetPending)
        {
            throw new InvalidOperationException(
                "The previous preview reset did not complete. Retry reset before capturing.");
        }

        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        IPreviewFrameSource frameSource = _frameSource
            ??= _frameSourceFactory.Create(request, cancellationToken)
                ?? throw new InvalidOperationException("The preview frame source factory returned null.");

        List<EncodedArtifact> encoded = request.Kind == CaptureKind.ContactSheet
            ? EncodeContactSheet(
                frameSource,
                request,
                _limits.MaximumTotalArtifactBytes,
                cancellationToken)
            : EncodeSeparateFrames(
                frameSource,
                request,
                _limits.MaximumTotalArtifactBytes,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ArtifactResourceDescriptor> artifacts =
            _artifactStore.AddRange(
                encoded.Select(artifact => new ArtifactResourceWrite(
                artifact.Id,
                "image/png",
                artifact.Content))
                    .ToArray());

        return new PreviewCaptureResult(
            request.RequestId,
            request.Kind,
            request.Width,
            request.Height,
            artifacts);
    }

    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(
            _state is PreviewCaptureProcessorState.DisposePending or
                PreviewCaptureProcessorState.Disposed,
            this);
        IPreviewFrameSource? frameSource = _frameSource;
        if (frameSource is null)
        {
            _state = PreviewCaptureProcessorState.Active;
            return;
        }

        _state = PreviewCaptureProcessorState.ResetPending;
        frameSource.Dispose();
        _frameSource = null;
        _state = PreviewCaptureProcessorState.Active;
    }

    public void Dispose()
    {
        if (_state == PreviewCaptureProcessorState.Disposed)
        {
            return;
        }

        if (_state != PreviewCaptureProcessorState.DisposePending)
        {
            _state = PreviewCaptureProcessorState.DisposePending;
        }

        IPreviewFrameSource? frameSource = _frameSource;
        if (frameSource is not null)
        {
            frameSource.Dispose();
            _frameSource = null;
        }

        _state = PreviewCaptureProcessorState.Disposed;
        GC.SuppressFinalize(this);
    }

    void IResettablePreviewCaptureProcessor.Reset() => Reset();

    private static List<EncodedArtifact> EncodeSeparateFrames(
        IPreviewFrameSource frameSource,
        PreviewCaptureRequest request,
        long maximumTotalArtifactBytes,
        CancellationToken cancellationToken)
    {
        var encoded = new List<EncodedArtifact>(request.Views.Count);
        long totalBytes = 0;
        for (int index = 0; index < request.Views.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureView view = request.Views[index];
            ImageRgba8 image = frameSource.Capture(view, request.Width, request.Height);
            ValidateCapturedDimensions(image, request.Width, request.Height);
            string id = request.Kind == CaptureKind.Still
                ? string.Concat(request.RequestId, ".png")
                : string.Concat(
                    request.RequestId,
                    "-",
                    index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                    "-",
                    SanitizeArtifactName(view.Name),
                    ".png");
            byte[] png = PngRgba8Encoder.Encode(image);
            totalBytes = AddArtifactBytes(totalBytes, png.Length, maximumTotalArtifactBytes);
            encoded.Add(new EncodedArtifact(id, png));
        }

        return encoded;
    }

    private static List<EncodedArtifact> EncodeContactSheet(
        IPreviewFrameSource frameSource,
        PreviewCaptureRequest request,
        long maximumTotalArtifactBytes,
        CancellationToken cancellationToken)
    {
        int columns = checked((int)Math.Ceiling(Math.Sqrt(request.Views.Count)));
        int rows = checked((request.Views.Count + columns - 1) / columns);
        int cellWidth = request.Width / columns;
        int cellHeight = request.Height / rows;
        if (cellWidth == 0 || cellHeight == 0)
        {
            throw new ArgumentException(
                "Contact sheet dimensions are too small for the requested views.",
                nameof(request));
        }

        byte[] sheet = new byte[ImageRgba8.GetByteCount(request.Width, request.Height)];
        for (int index = 0; index < request.Views.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageRgba8 image = frameSource.Capture(
                request.Views[index],
                cellWidth,
                cellHeight);
            ValidateCapturedDimensions(image, cellWidth, cellHeight);
            CopyTile(
                image,
                sheet,
                request.Width,
                (index % columns) * cellWidth,
                (index / columns) * cellHeight);
        }

        var contactSheet = new ImageRgba8(request.Width, request.Height, sheet);
        byte[] png = PngRgba8Encoder.Encode(contactSheet);
        _ = AddArtifactBytes(0, png.Length, maximumTotalArtifactBytes);
        return
        [
            new EncodedArtifact(
                string.Concat(request.RequestId, "-contact-sheet.png"),
                png),
        ];
    }

    private static long AddArtifactBytes(
        long currentBytes,
        int additionalBytes,
        long maximumTotalArtifactBytes)
    {
        long totalBytes = checked(currentBytes + additionalBytes);
        if (totalBytes > maximumTotalArtifactBytes)
        {
            throw new InvalidOperationException(
                $"Capture artifacts exceed the {maximumTotalArtifactBytes}-byte limit.");
        }

        return totalBytes;
    }

    private static void CopyTile(
        ImageRgba8 tile,
        Span<byte> destination,
        int destinationWidth,
        int x,
        int y)
    {
        int tileStride = checked(tile.Width * ImageRgba8.BytesPerPixel);
        int destinationStride = checked(destinationWidth * ImageRgba8.BytesPerPixel);
        for (int row = 0; row < tile.Height; row++)
        {
            tile.Pixels.Span.Slice(row * tileStride, tileStride).CopyTo(
                destination.Slice(
                    checked(((y + row) * destinationStride) + (x * ImageRgba8.BytesPerPixel)),
                    tileStride));
        }
    }

    private static string SanitizeArtifactName(string name)
    {
        char[] characters = name.ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            char character = characters[index];
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.')
            {
                characters[index] = '_';
            }
        }

        return new string(characters);
    }

    private static void ValidateCapturedDimensions(
        ImageRgba8 image,
        int expectedWidth,
        int expectedHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width != expectedWidth || image.Height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"The frame source returned {image.Width}x{image.Height}; " +
                $"expected {expectedWidth}x{expectedHeight}.");
        }
    }

    private void Validate(PreviewCaptureRequest request)
    {
        if (request.Width > _limits.MaximumWidth ||
            request.Height > _limits.MaximumHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Capture dimensions may not exceed {_limits.MaximumWidth}x{_limits.MaximumHeight}.");
        }

        if (request.Views.Count > _limits.MaximumViews)
        {
            throw new ArgumentException(
                $"Capture requests may contain at most {_limits.MaximumViews} views.",
                nameof(request));
        }

        if (request.Kind == CaptureKind.Still && request.Views.Count != 1)
        {
            throw new ArgumentException(
                "Still captures require exactly one view.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private sealed record EncodedArtifact(string Id, byte[] Content);

    private enum PreviewCaptureProcessorState
    {
        Active,
        ResetPending,
        DisposePending,
        Disposed,
    }
}
