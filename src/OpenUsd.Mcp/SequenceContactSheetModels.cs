// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OpenUsd.Mcp;

internal sealed class ReadSequenceSheetRequest
{
    [JsonPropertyName("jobId"), Required, MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description("Completed render_sequence or render_product job ID in this process.")]
    public required string JobId { get; init; }

    [JsonPropertyName("frameIndices"), MaxLength(16)]
    [Description("Optional 1-16 unique frame indices in display order; null samples up to 16 evenly spaced frames.")]
    public IReadOnlyList<int>? FrameIndices { get; init; }

    [JsonPropertyName("width"), Range(4, 1024), DefaultValue(1024)]
    [Description("Contact-sheet width, 4-1024 pixels; each source is fitted without cropping.")]
    public int Width { get; init; } = 1024;

    [JsonPropertyName("height"), Range(4, 1024), DefaultValue(1024)]
    [Description("Contact-sheet height, 4-1024 pixels; letterbox and unused cells are transparent.")]
    public int Height { get; init; } = 1024;
}

internal sealed record McpSequenceSheetTileDto(
    [property: Description("Original zero-based completed frame index.")] int FrameIndex,
    [property: Description("Original captured USD time code, not current scene time.")] double TimeCode,
    [property: Description("Left edge of the tile cell in the top-down sheet.")] int X,
    [property: Description("Top edge of the tile cell.")] int Y,
    [property: Description("Cell width, including transparent letterboxing.")] int Width,
    [property: Description("Cell height, including transparent letterboxing.")] int Height);

internal sealed record McpSequenceSheetResultDto(
    [property: Description("Original job session.")] string SessionId,
    [property: Description("Original captured workspace generation.")] long Generation,
    [property: Description("Original job stage revision.")] ulong StageRevision,
    [property: Description("Completed job identifier.")] string JobId,
    [property: Description("Sheet width in pixels.")] int Width,
    [property: Description("Sheet height in pixels.")] int Height,
    [property: Description("Generated immutable PNG resource descriptor.")] McpArtifactDto Artifact,
    [property: Description("At most 16 cells in requested frame order.")] IReadOnlyList<McpSequenceSheetTileDto> Tiles,
    [property: JsonIgnore] IReadOnlyList<ArtifactResourceDescriptor> ArtifactResources)
    : IOpenUsdMcpOutput, IOpenUsdMcpArtifactOutput
{
    [Description("Bounded contact-sheet completion summary.")]
    public string Summary =>
        $"Contact sheet of {Tiles.Count} completed frames from job {JobId}; no scene was rendered.";
}
