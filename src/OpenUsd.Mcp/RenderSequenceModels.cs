// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Text.Json.Serialization;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal sealed class RenderSequenceRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("width"), Range(1, 4096), DefaultValue(1024)]
    [Description("Output width, 1-4096 pixels.")]
    public int Width { get; init; } = 1024;

    [JsonPropertyName("height"), Range(1, 4096), DefaultValue(1024)]
    [Description("Output height, 1-4096 pixels.")]
    public int Height { get; init; } = 1024;

    [JsonPropertyName("frameCount"), Range(1, 4096), DefaultValue(24)]
    [Description("Number of PNG frames written to disk, 1-4096; does not raise the 16-view preview limit.")]
    public int FrameCount { get; init; } = 24;

    [JsonPropertyName("startTimeCode")]
    [Description("Finite USD time code of the first frame.")]
    public double StartTimeCode { get; init; }

    [JsonPropertyName("timeStep"), DefaultValue(1d)]
    [Description("Finite nonzero time-code increment, positive or negative. The final sample must remain finite.")]
    public double TimeStep { get; init; } = 1;

    [JsonPropertyName("cameraPath"), MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description("Optional absolute UsdGeomCamera prim path, sampled at every frame time.")]
    public string? CameraPath { get; init; }

    [JsonPropertyName("includeDeviceDepth"), DefaultValue(false)]
    [Description(
        "Also capture real normalized device depth as top-down little-endian float32. " +
        "Near=0, far/clear=1; not metric camera distance or independent coverage. " +
        "Color/depth captures charge 20 managed bytes per pixel within the 64-MiB frame limit.")]
    public bool IncludeDeviceDepth { get; init; }

    [JsonPropertyName("includeHdrColor"), DefaultValue(false)]
    [Description(
        "Also capture actual pre-exposure/display framebuffer color as top-down little-endian RGBA binary16. " +
        "Stored framebuffer alpha and unknown renderer-working primaries are preserved. " +
        "HDR captures charge 20 managed bytes per pixel, with or without device depth.")]
    public bool IncludeHdrColor { get; init; }

    [JsonPropertyName("hdrColorFormat"), MaxLength(3), DefaultValue("raw")]
    [Description(
        "HDR container: 'raw' (default packed RGBA binary16) or 'exr' (lossless half EXR). " +
        "'exr' requires includeHdrColor=true and the Windows x64 Core encoder. " +
        "No color/alpha conversion; the encoded-byte quota does not bound native codec heap.")]
    public string HdrColorFormat { get; init; } = "raw";
}

internal sealed class ReadSequenceFrameRequest
{
    [JsonPropertyName("jobId"), Required, MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Exact completed job identifier returned by render_sequence, render_product or render_physics_sequence " +
            "in this process.")]
    public required string JobId { get; init; }

    [JsonPropertyName("frameIndex"), Range(0, 4095)]
    [Description("Zero-based frame index within that completed job.")]
    public int FrameIndex { get; init; }
}

internal sealed record McpRenderSequenceResultDto(
    [property: Description("Session that produced this immutable completed sequence.")]
    string SessionId,
    [property: Description("Unchanged workspace generation used for every sequence frame.")]
    long Generation,
    [property: Description("Native stage revision accepted for the sequence.")]
    ulong StageRevision,
    [property: Description("Generated identifier of the completed disk job.")]
    string JobId,
    [property: Description("Completed directory relative to the configured writable output root.")]
    string OutputDirectory,
    [property: Description("Request and image-hash manifest path relative to the configured output root.")]
    string ManifestPath,
    [property: Description("Number of completed PNG files; at most 4096.")]
    int FrameCount,
    [property: Description("Total encoded bytes including the manifest; charged to the process disk quota.")]
    long TotalBytes,
    [property: Description("Distinct renderer degradation diagnostics across the job, bounded to 128.")]
    IReadOnlyList<McpRenderDiagnosticDto> Diagnostics) : IOpenUsdMcpOutput
{
    [Description("Bounded human-readable completion summary.")]
    public string Summary =>
        $"Rendered {FrameCount} PNG frames to {OutputDirectory}; {TotalBytes} bytes including the manifest.";

    [Description("Optional authored-product selection and exact variable bindings; null for viewport sequences.")]
    public McpAuthoredProductDto? AuthoredProduct { get; init; }
    [Description("Exact fixed-step simulation identity; null for authored-time rendering.")]
    public McpSimulationRecordingDto? Simulation { get; init; }
}

internal sealed record McpSequenceJobsResultDto(
    [property: Description(
        "At most eight original completed-job descriptors in ordinal job-ID order, " +
        "including retired scenes; this process only, with no file-integrity or current-scene claim.")]
    IReadOnlyList<McpRenderSequenceResultDto> Jobs) : IOpenUsdMcpOutput
{
    [Description("Bounded historical job-list summary.")]
    public string Summary => $"{Jobs.Count} completed disk jobs retained by this MCP process.";
}

internal sealed record McpSequenceFrameResultDto(
    [property: Description("Original scene session captured by this completed job.")] string SessionId,
    [property: Description("Original workspace generation, not a claim about the current scene.")] long Generation,
    [property: Description("Original native stage revision accepted for the sequence.")] ulong StageRevision,
    [property: Description("Completed job identifier.")] string JobId,
    [property: Description("Zero-based frame index.")] int FrameIndex,
    [property: Description("Numeric USD time code of this captured frame.")] double TimeCode,
    [property: Description("Width of the selected PNG.")] int Width,
    [property: Description("Height of the selected PNG.")] int Height,
    [property: Description("Immutable hash-verified PNG artifact descriptor.")] McpArtifactDto Artifact,
    [property: JsonIgnore] IReadOnlyList<ArtifactResourceDescriptor> ArtifactResources) :
    IOpenUsdMcpOutput, IOpenUsdMcpArtifactOutput
{
    [Description("Bounded selected-frame summary.")]
    public string Summary => $"Sequence {JobId}, frame {FrameIndex} at time {TimeCode}: {Width} x {Height}.";

    [Description(
        "Optional verified raw device-depth resource: packed top-down float32 little-endian, " +
        "normalized [0,1], clear/far=1, no independent hit coverage.")]
    public McpArtifactDto? DeviceDepth { get; init; }

    [Description(
        "Optional verified HDR color: raw packed RGBA binary16 or lossless half EXR before exposure/display. " +
        "Alpha is stored framebuffer alpha; no named primaries or physical-radiance claim is implied.")]
    public McpArtifactDto? HdrColor { get; init; }

    [Description("HDR container: 'raw' or 'exr', or null when no HDR plane was captured.")]
    public string? HdrColorFormat { get; init; }

    [Description("Original authored-product selection and bindings, independent of later scene edits.")]
    public McpAuthoredProductDto? AuthoredProduct { get; init; }
    [Description("Exact simulation publication captured by this frame; null for authored-time rendering.")]
    public McpSimulationFrameDto? Simulation { get; init; }

    [Description(
        "Original submitted camera request, not effective backend state or source verification. " +
        "Automatic mode has no recorded renderer-selected matrices; null means no request metadata supplied.")]
    public McpCameraRequestDto? CameraRequest { get; init; }

    [Description(
        "Original submitted source-frame display and render settings, not effective backend state, " +
        "source verification or a complete request identity. Null means no request metadata supplied.")]
    public McpRenderRequestDto? RenderRequest { get; init; }
}

internal sealed record McpCameraRequestDto(
    [property: Description("Requested mode: automatic or matrices; not an effective-camera claim.")] string Mode,
    [property: Description("Sixteen row-major world-to-view floats; null for automatic mode.")]
    IReadOnlyList<float>? View,
    [property: Description("Sixteen row-major view-to-clip floats; null for automatic mode.")]
    IReadOnlyList<float>? Projection,
    [property: Description("Zero to eight view-space plane equations, each four floats (x,y,z,w).")]
    IReadOnlyList<IReadOnlyList<float>> ClipPlanes)
{
    internal static McpCameraRequestDto From(CameraState camera) => new(
        camera.Mode == CameraMode.Matrices ? "matrices" : "automatic",
        camera.Mode == CameraMode.Matrices ? CopyMatrix(camera.View) : null,
        camera.Mode == CameraMode.Matrices ? CopyMatrix(camera.Projection) : null,
        new OwnedReadOnlyList<IReadOnlyList<float>>(camera.ClipPlanes.Select(static plane =>
            CopyPlane(plane)).ToArray()));

    private static OwnedReadOnlyList<float> CopyPlane(Vector4 plane) =>
        new OwnedReadOnlyList<float>([plane.X, plane.Y, plane.Z, plane.W]);

    private static OwnedReadOnlyList<float> CopyMatrix(Matrix4x4 matrix) => new(
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ]);
}
