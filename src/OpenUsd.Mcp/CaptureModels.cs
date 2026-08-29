// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

public enum CaptureKind
{
    Still,
    CandidateSweep,
    ContactSheet,
    Turntable,
    CameraSweep = CandidateSweep,
}

public sealed record PreviewCaptureLimits(
    int MaximumWidth = 4096,
    int MaximumHeight = 4096,
    int MaximumViews = 16,
    long MaximumTotalArtifactBytes = 64 * 1024 * 1024);

public sealed record CaptureView
{
    public CaptureView(string name, CameraState camera, double timeCode = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The capture time code must be finite.");
        }

        Name = name;
        Camera = camera;
        TimeCode = timeCode;
    }

    public string Name { get; }

    public CameraState Camera { get; }

    public double TimeCode { get; }
}

public sealed record PreviewCaptureRequest
{
    public PreviewCaptureRequest(
        string requestId,
        int width,
        int height,
        CameraState camera = default,
        double timeCode = 0)
        : this(
            requestId,
            CaptureKind.Still,
            width,
            height,
            Array.AsReadOnly([new CaptureView("still", camera, timeCode)]))
    {
    }

    public PreviewCaptureRequest(
        string requestId,
        CaptureKind kind,
        int width,
        int height,
        IReadOnlyList<CaptureView> views)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _ = ImageRgba8.GetByteCount(width, height);
        ArgumentNullException.ThrowIfNull(views);
        if (views.Count == 0)
        {
            throw new ArgumentException("At least one capture view is required.", nameof(views));
        }

        if (views.Any(static view => view is null))
        {
            throw new ArgumentException("Capture views may not contain null.", nameof(views));
        }

        RequestId = requestId;
        Kind = kind;
        Width = width;
        Height = height;
        Views = Array.AsReadOnly(views.ToArray());
    }

    public string RequestId { get; }

    public CaptureKind Kind { get; }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<CaptureView> Views { get; }
}

public sealed record PreviewCaptureResult
{
    public PreviewCaptureResult(
        string requestId,
        CaptureKind kind,
        int width,
        int height,
        IReadOnlyList<ArtifactResourceDescriptor> artifacts,
        IReadOnlyList<RenderDiagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(artifacts);
        RequestId = requestId;
        Kind = kind;
        Width = width;
        Height = height;
        Artifacts = Array.AsReadOnly(artifacts.ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? []).ToArray());
    }

    public string RequestId { get; }

    public CaptureKind Kind { get; }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<ArtifactResourceDescriptor> Artifacts { get; }

    public IReadOnlyList<RenderDiagnostic> Diagnostics { get; }
}
