// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OpenUsd.Mcp;

internal static class OpenUsdMcpLimits
{
    internal const int MaximumArtifactCount = 16;
    internal const int MaximumEditCount = 128;
    internal const int MaximumIdentifierLength = 128;
    internal const int MaximumIssueCount = 128;
    internal const int MaximumPathLength = 1024;
    internal const int MaximumProposalCount = 128;
    internal const int MaximumTextLength = 4096;
    internal const int MaximumViewCount = 16;
}

internal sealed record OpenUsdMcpProtocolOptions(
    int MaximumTextContentLength = 2048,
    int MaximumContentBlockCount = 17,
    int InlineImageMaximumBytes = 32 * 1024);

internal interface IOpenUsdMcpOutput
{
    string Summary { get; }
}

internal interface IOpenUsdMcpArtifactOutput
{
    IReadOnlyList<ArtifactResourceDescriptor> ArtifactResources { get; }
}

internal sealed class OpenSceneRequest
{
    [JsonPropertyName("sourcePath")]
    [Required]
    [MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description(
        "Relative USD file path under the configured read-only source root, for example \"assets/robot.usda\". " +
        "Supported extensions: .usd, .usda, .usdc, and .usdz. " +
        "Absolute paths, traversal outside the root, control characters, and paths longer than 1024 " +
        "characters are rejected.")]
    public required string SourcePath { get; init; }
}

internal sealed class SceneRevisionRequest
{
    [JsonPropertyName("sessionId")]
    [Required]
    [MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Exact active session identifier returned by open_scene; 1-128 non-control characters. " +
        "A different identifier produces stale_session.")]
    public required string SessionId { get; init; }

    [JsonPropertyName("generation")]
    [Range(0, long.MaxValue)]
    [Description(
        "Exact non-negative transactional generation returned by the preceding scene-mutating result. " +
        "An older or newer value produces stale_revision.")]
    public long Generation { get; init; }

    [JsonPropertyName("stageRevision")]
    [Description(
        "Exact unsigned native stage revision returned by the preceding result. " +
        "A mismatched value produces stale_revision.")]
    public ulong StageRevision { get; init; }
}

internal sealed class ApplyEditsRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("edits")]
    [Required]
    [MinLength(1)]
    [MaxLength(OpenUsdMcpLimits.MaximumEditCount)]
    [Description(
        "Atomic list of 1-128 typed overlay edits. The whole batch commits or rolls back. " +
        "Each element uses kind-specific fields; arbitrary USDA and code are not accepted.")]
    public required IReadOnlyList<WorkspaceEditDto> Edits { get; init; }
}

internal sealed class RollbackSceneRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("checkpointId")]
    [Required]
    [MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Existing checkpoint identifier returned by checkpoint_scene or apply_edits; " +
        "1-128 non-control characters.")]
    public required string CheckpointId { get; init; }
}

internal sealed class RenderPreviewRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("kind")]
    [Required]
    [MaxLength(32)]
    [AllowedValues("still", "contact_sheet", "turntable")]
    [Description(
        "Capture mode. still requires exactly one view; contact_sheet combines all views into one image; " +
        "turntable emits one image per view.")]
    public required string Kind { get; init; }

    [JsonPropertyName("width")]
    [Range(1, 4096)]
    [DefaultValue(1024)]
    [Description(
        "Output width in pixels, inclusive range 1-4096. Contact-sheet width is divided among its view columns.")]
    public int Width { get; init; } = 1024;

    [JsonPropertyName("height")]
    [Range(1, 4096)]
    [DefaultValue(1024)]
    [Description(
        "Output height in pixels, inclusive range 1-4096. Contact-sheet height is divided among its view rows.")]
    public int Height { get; init; } = 1024;

    [JsonPropertyName("views")]
    [Required]
    [MinLength(1)]
    [MaxLength(OpenUsdMcpLimits.MaximumViewCount)]
    [Description(
        "List of 1-16 automatic-camera views. still requires one item; contact_sheet and turntable preserve " +
        "list order.")]
    public required IReadOnlyList<CaptureViewDto> Views { get; init; }
}

internal sealed class AnalyzeSceneRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("observations")]
    [Required]
    [Description(
        "Detached, bounded technical observations used by deterministic analyzers. " +
        "These are metrics only; USDA text, source code, and native handles are not accepted.")]
    public required AnalysisObservationsDto Observations { get; init; }
}

internal sealed class ApplyProposalsRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("proposalIds")]
    [Required]
    [MinLength(1)]
    [MaxLength(OpenUsdMcpLimits.MaximumProposalCount)]
    [Description(
        "List of 1-128 distinct proposal IDs from the latest analyze_scene result. Every selected proposal " +
        "must be overlay_applicable and match the current generation and stage revision.")]
    public required IReadOnlyList<string> ProposalIds { get; init; }
}

internal sealed class PresentSceneRequest : SceneRevisionRequestBase
{
    [JsonPropertyName("renderer")]
    [Required]
    [MaxLength(16)]
    [AllowedValues("auto", "silk", "storm")]
    [Description("Renderer accepted by the configured Viewer child: auto, silk, or storm.")]
    public required string Renderer { get; init; }

    [JsonPropertyName("cameraPath")]
    [MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description(
        "Optional absolute USD camera prim path such as \"/World/Camera\". This is a prim path, never a " +
        "filesystem path, and must contain valid USD identifiers.")]
    public string? CameraPath { get; init; }
}

internal abstract class SceneRevisionRequestBase
{
    [JsonPropertyName("sessionId")]
    [Required]
    [MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Exact active session identifier returned by open_scene; 1-128 non-control characters. " +
        "A different identifier produces stale_session.")]
    public required string SessionId { get; init; }

    [JsonPropertyName("generation")]
    [Range(0, long.MaxValue)]
    [Description(
        "Exact non-negative transactional generation returned by the preceding scene-mutating result. " +
        "An older or newer value produces stale_revision.")]
    public long Generation { get; init; }

    [JsonPropertyName("stageRevision")]
    [Description(
        "Exact unsigned native stage revision returned by the preceding result. " +
        "A mismatched value produces stale_revision.")]
    public ulong StageRevision { get; init; }

    internal SceneRevisionRequest ToRevisionRequest() =>
        new()
        {
            SessionId = SessionId,
            Generation = Generation,
            StageRevision = StageRevision,
        };
}

internal sealed class WorkspaceEditDto
{
    [JsonPropertyName("kind")]
    [Required]
    [MaxLength(32)]
    [AllowedValues(
        "define_prim",
        "set_active",
        "set_double",
        "clear_overlay_attribute")]
    [Description(
        "Typed edit discriminator. Required companion fields: define_prim may use typeName; set_active " +
        "requires active; set_double requires attributeName and value and may use timeCode; " +
        "clear_overlay_attribute requires attributeName.")]
    public required string Kind { get; init; }

    [JsonPropertyName("primPath")]
    [Required]
    [MaxLength(OpenUsdMcpLimits.MaximumPathLength)]
    [Description("Absolute USD prim path such as /World/Cube; this is not a filesystem path.")]
    public required string PrimPath { get; init; }

    [JsonPropertyName("typeName")]
    [MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Optional USD type identifier for define_prim, for example \"Xform\" or \"Mesh\". Omit for other " +
        "kinds. Maximum 128 characters and valid USD identifier syntax.")]
    public string? TypeName { get; init; }

    [JsonPropertyName("active")]
    [Description(
        "Required only for set_active. true authors activation; false authors deactivation in the " +
        "overlay. Omit for other kinds.")]
    public bool? Active { get; init; }

    [JsonPropertyName("attributeName")]
    [MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Required for set_double and clear_overlay_attribute. USD property name such as " +
        "\"xformOp:translateX\"; maximum 128 characters. Omit for other kinds.")]
    public string? AttributeName { get; init; }

    [JsonPropertyName("value")]
    [Description(
        "Required finite numeric value for set_double. NaN and infinities are rejected. Omit for other kinds.")]
    public double? Value { get; init; }

    [JsonPropertyName("timeCode")]
    [Description(
        "Optional finite time code for set_double. Omit to author the default value; omit for other kinds.")]
    public double? TimeCode { get; init; }
}

internal sealed class CaptureViewDto
{
    [JsonPropertyName("name")]
    [Required]
    [MaxLength(OpenUsdMcpLimits.MaximumIdentifierLength)]
    [Description(
        "Display name used in generated artifact identifiers; 1-128 non-control characters. " +
        "Unsafe filename characters are replaced, not interpreted as paths.")]
    public required string Name { get; init; }

    [JsonPropertyName("timeCode")]
    [DefaultValue(0d)]
    [Description("Finite stage time code rendered for this view. Defaults to 0; NaN and infinities are rejected.")]
    public double TimeCode { get; init; }
}

internal sealed class AnalysisObservationsDto
{
    [JsonPropertyName("viewportWidth")]
    [Range(1, 4096)]
    [DefaultValue(1024)]
    [Description("Observed viewport width in pixels, inclusive range 1-4096.")]
    public int ViewportWidth { get; init; } = 1024;

    [JsonPropertyName("viewportHeight")]
    [Range(1, 4096)]
    [DefaultValue(1024)]
    [Description("Observed viewport height in pixels, inclusive range 1-4096.")]
    public int ViewportHeight { get; init; } = 1024;

    [JsonPropertyName("subjectCoverage")]
    [Range(0d, 1d)]
    [DefaultValue(0.5d)]
    [Description("Fraction of the viewport covered by the subject, inclusive range 0-1.")]
    public double SubjectCoverage { get; init; } = 0.5;

    [JsonPropertyName("nearClip")]
    [Range(double.Epsilon, double.MaxValue)]
    [DefaultValue(0.1d)]
    [Description("Positive near clipping distance. Must be finite and less than farClip.")]
    public double NearClip { get; init; } = 0.1;

    [JsonPropertyName("farClip")]
    [Range(double.Epsilon, double.MaxValue)]
    [DefaultValue(1000d)]
    [Description("Positive far clipping distance. Must be finite and greater than nearClip.")]
    public double FarClip { get; init; } = 1000;

    [JsonPropertyName("nearestGeometryDistance")]
    [Range(0d, double.MaxValue)]
    [DefaultValue(1d)]
    [Description("Finite non-negative distance to the nearest observed geometry.")]
    public double NearestGeometryDistance { get; init; } = 1;

    [JsonPropertyName("farthestGeometryDistance")]
    [Range(0d, double.MaxValue)]
    [DefaultValue(100d)]
    [Description("Finite distance to the farthest observed geometry; must be at least nearestGeometryDistance.")]
    public double FarthestGeometryDistance { get; init; } = 100;

    [JsonPropertyName("samplesPerPixel")]
    [Range(1, 65536)]
    [DefaultValue(1)]
    [Description("Positive observed render samples per pixel, inclusive range 1-65536.")]
    public int SamplesPerPixel { get; init; } = 1;

    [JsonPropertyName("lightingEnabled")]
    [DefaultValue(true)]
    [Description("Whether scene lighting was enabled for the observation.")]
    public bool LightingEnabled { get; init; } = true;

    [JsonPropertyName("shadowsEnabled")]
    [DefaultValue(true)]
    [Description("Whether shadow rendering was enabled for the observation.")]
    public bool ShadowsEnabled { get; init; } = true;

    [JsonPropertyName("qualityPreset")]
    [Required]
    [MaxLength(16)]
    [AllowedValues("draft", "balanced", "final")]
    [DefaultValue("balanced")]
    [Description("Observed render quality preset: draft, balanced, or final.")]
    public string QualityPreset { get; init; } = "balanced";

    [JsonPropertyName("frameMilliseconds")]
    [Range(0d, double.MaxValue)]
    [DefaultValue(0d)]
    [Description("Finite non-negative observed frame duration in milliseconds.")]
    public double FrameMilliseconds { get; init; }

    [JsonPropertyName("drawSucceeded")]
    [DefaultValue(true)]
    [Description("Whether the observed frame completed drawing successfully.")]
    public bool DrawSucceeded { get; init; } = true;

    [JsonPropertyName("finitePixelRatio")]
    [Range(0d, 1d)]
    [DefaultValue(1d)]
    [Description("Fraction of observed pixels containing finite values, inclusive range 0-1.")]
    public double FinitePixelRatio { get; init; } = 1;

    [JsonPropertyName("backgroundPixelRatio")]
    [Range(0d, 1d)]
    [DefaultValue(0d)]
    [Description("Fraction of observed pixels matching the background, inclusive range 0-1.")]
    public double BackgroundPixelRatio { get; init; }

    [JsonPropertyName("drawCalls")]
    [Range(0, long.MaxValue)]
    [DefaultValue(0L)]
    [Description("Observed non-negative draw-call count.")]
    public long DrawCalls { get; init; }

    [JsonPropertyName("triangleCount")]
    [Range(0, long.MaxValue)]
    [DefaultValue(0L)]
    [Description("Observed non-negative triangle count.")]
    public long TriangleCount { get; init; }

    [JsonPropertyName("resourceCount")]
    [Range(0, long.MaxValue)]
    [DefaultValue(0L)]
    [Description("Observed non-negative renderer resource count.")]
    public long ResourceCount { get; init; }

    [JsonPropertyName("residentBytes")]
    [Range(0, long.MaxValue)]
    [DefaultValue(0L)]
    [Description("Observed non-negative resident GPU byte count.")]
    public long ResidentBytes { get; init; }

    [JsonPropertyName("rendererId")]
    [Required]
    [MaxLength(16)]
    [AllowedValues("silk", "storm")]
    [DefaultValue("silk")]
    [Description("Renderer that produced the observations: silk or storm.")]
    public string RendererId { get; init; } = "silk";

    [JsonPropertyName("validationIssues")]
    [MaxLength(OpenUsdMcpLimits.MaximumIssueCount)]
    [Description(
        "Zero to 128 validation messages, each at most 4096 non-control characters. " +
        "Supply concise findings only; USDA text and source code are rejected by contract.")]
    public IReadOnlyList<string> ValidationIssues { get; init; } = [];
}

internal sealed record McpSessionDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description(
        "Opaque identifier for the one active session; pass unchanged to every revision-bound tool.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description(
        "Current transactional generation; pass unchanged until a mutating tool returns a successor value.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Current native stage revision; pass unchanged until a tool returns a successor value.")]
    ulong StageRevision,
    [property: JsonPropertyName("sourcePath")]
    [property: Description(
        "Original source path relative to the configured source root; never an unrestricted absolute path.")]
    string SourcePath,
    [property: JsonPropertyName("createdAt")]
    [property: Description("UTC timestamp at which this session was created.")]
    DateTimeOffset CreatedAt)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary => $"Session {SessionId} is active at generation {Generation}, revision {StageRevision}.";
}

internal sealed record McpClosedSceneDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Identifier of the session that was closed.")]
    string SessionId,
    [property: JsonPropertyName("closed")]
    [property: Description("Always true after successful deterministic teardown.")]
    bool Closed)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary => $"Session {SessionId} was closed.";
}

internal sealed record McpSceneInspectionDto(
    [property: JsonPropertyName("session")]
    [property: Description("Current revision-bound session state.")]
    McpSessionDto Session,
    [property: JsonPropertyName("checkpointCount")]
    [property: Description("Non-negative number of immutable overlay checkpoints retained by the active session.")]
    int CheckpointCount,
    [property: JsonPropertyName("journalEntryCount")]
    [property: Description("Non-negative number of ordered session journal entries.")]
    int JournalEntryCount,
    [property: JsonPropertyName("latestJournalKind")]
    [property: Description("Most recent journal event name, or null when no event exists.")]
    string? LatestJournalKind,
    [property: JsonPropertyName("defaultPrimPath")]
    [property: Description(
        "Absolute USD path of the stage default prim, or an empty string when no default prim is authored.")]
    string DefaultPrimPath,
    [property: JsonPropertyName("primCount")]
    [property: Description("Non-negative composed prim count.")]
    int PrimCount,
    [property: JsonPropertyName("meshCount")]
    [property: Description("Non-negative composed UsdGeomMesh prim count.")]
    int MeshCount,
    [property: JsonPropertyName("curveVertexCount")]
    [property: Description("Non-negative aggregate vertex count across supported curve schemas.")]
    long CurveVertexCount,
    [property: JsonPropertyName("meshVertexCount")]
    [property: Description("Non-negative aggregate mesh point count.")]
    long MeshVertexCount,
    [property: JsonPropertyName("faceCount")]
    [property: Description("Non-negative aggregate mesh face count.")]
    long FaceCount,
    [property: JsonPropertyName("rootPrimCount")]
    [property: Description("Non-negative count of composed root prims.")]
    int RootPrimCount,
    [property: JsonPropertyName("leafPrimCount")]
    [property: Description("Non-negative count of composed prims with no composed children.")]
    int LeafPrimCount,
    [property: JsonPropertyName("maximumDepth")]
    [property: Description("Maximum zero-based composed hierarchy depth; zero for an empty or root-only stage.")]
    int MaximumDepth)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary =>
        $"Scene has {PrimCount} prims, {MeshCount} meshes, and {CheckpointCount} checkpoints.";
}

internal sealed record McpEditResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Successor generation after the atomic edit commit.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Native stage revision after the atomic edit commit.")]
    ulong StageRevision,
    [property: JsonPropertyName("checkpointId")]
    [property: Description("Recovery checkpoint created immediately before mutation.")]
    string CheckpointId,
    [property: JsonPropertyName("operationCount")]
    [property: Description("Committed edit count, inclusive range 1-128.")]
    int OperationCount)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary =>
        $"Committed {OperationCount} edits at generation {Generation}, revision {StageRevision}.";
}

internal sealed record McpCheckpointResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Unchanged generation at which the checkpoint was captured.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Unchanged native stage revision at which the checkpoint was captured.")]
    ulong StageRevision,
    [property: JsonPropertyName("checkpointId")]
    [property: Description("Opaque immutable checkpoint identifier accepted by rollback_scene.")]
    string CheckpointId)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary => $"Created checkpoint {CheckpointId}.";
}

internal sealed record McpRollbackResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Successor generation after checkpoint restoration.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Native stage revision after checkpoint restoration.")]
    ulong StageRevision,
    [property: JsonPropertyName("checkpointId")]
    [property: Description("Identifier of the checkpoint that was restored.")]
    string CheckpointId)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary =>
        $"Restored checkpoint {CheckpointId} at generation {Generation}, revision {StageRevision}.";
}

internal sealed record McpArtifactDto(
    [property: JsonPropertyName("id")]
    [property: Description("Immutable artifact identifier, unique within this server process.")]
    string Id,
    [property: JsonPropertyName("uri")]
    [property: Description("Read-only openusd://artifact/{id} URI accepted by resources/read.")]
    string Uri,
    [property: JsonPropertyName("mimeType")]
    [property: Description("IANA media type used for inline content and resource reads.")]
    string MimeType,
    [property: JsonPropertyName("byteLength")]
    [property: Description("Exact non-negative decoded content length in bytes.")]
    long ByteLength,
    [property: JsonPropertyName("sha256")]
    [property: Description("Lowercase 64-character SHA-256 digest of the decoded content.")]
    string Sha256,
    [property: JsonPropertyName("inline")]
    [property: Description(
        "True when the artifact is eligible for an inline image content block; " +
        "false means clients should read the URI.")]
    bool Inline);

internal sealed record McpCaptureResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier used for this capture.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Unchanged generation rendered by this read-only operation.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Unchanged native stage revision rendered by this read-only operation.")]
    ulong StageRevision,
    [property: JsonPropertyName("requestId")]
    [property: Description("Unique capture request identifier used as the artifact-name prefix.")]
    string RequestId,
    [property: JsonPropertyName("kind")]
    [property: Description("Completed capture mode: still, contact_sheet, or turntable.")]
    string Kind,
    [property: JsonPropertyName("width")]
    [property: Description("Rendered output width in pixels, inclusive range 1-4096.")]
    int Width,
    [property: JsonPropertyName("height")]
    [property: Description("Rendered output height in pixels, inclusive range 1-4096.")]
    int Height,
    [property: JsonPropertyName("artifacts")]
    [property: MaxLength(OpenUsdMcpLimits.MaximumArtifactCount)]
    [property: Description(
        "Ordered immutable PNG descriptors; at most 16. " +
        "Tool content includes at most 16 corresponding image or resource-link blocks.")]
    IReadOnlyList<McpArtifactDto> Artifacts,
    [property: JsonIgnore] IReadOnlyList<ArtifactResourceDescriptor> ArtifactResources)
    : IOpenUsdMcpOutput, IOpenUsdMcpArtifactOutput
{
    [JsonIgnore]
    public string Summary => $"Created {Artifacts.Count} {Kind} preview artifact(s).";
}

internal sealed record McpProposalDto(
    [property: JsonPropertyName("id")]
    [property: Description("Deterministic revision-bound proposal identifier accepted by apply_proposals.")]
    string Id,
    [property: JsonPropertyName("category")]
    [property: Description(
        "Analyzer category: camera, lighting, rendersettings, performance, composition, or validation.")]
    string Category,
    [property: JsonPropertyName("code")]
    [property: Description("Stable machine-readable finding code.")]
    string Code,
    [property: JsonPropertyName("title")]
    [property: Description("Bounded human-readable finding title.")]
    string Title,
    [property: JsonPropertyName("applicability")]
    [property: Description(
        "overlay_applicable, flatten_only, or diagnostic_only. Only overlay_applicable IDs may be passed " +
        "to apply_proposals.")]
    string Applicability,
    [property: JsonPropertyName("risk")]
    [property: Description("Estimated change risk: low, medium, or high.")]
    string Risk,
    [property: JsonPropertyName("explanation")]
    [property: Description("Bounded explanation of the evidence and recommended action.")]
    string Explanation);

internal sealed record McpAnalysisResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier analyzed.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Unchanged generation to which every proposal is bound.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Unchanged native stage revision to which every proposal is bound.")]
    ulong StageRevision,
    [property: JsonPropertyName("proposals")]
    [property: MaxLength(OpenUsdMcpLimits.MaximumProposalCount)]
    [property: Description("Deterministically ordered proposal list, containing at most 128 items.")]
    IReadOnlyList<McpProposalDto> Proposals)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary => $"Analysis produced {Proposals.Count} deterministic proposal(s).";
}

internal sealed record McpApplyProposalsResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Successor generation after all selected proposal edits commit atomically.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Native stage revision after all selected proposal edits commit atomically.")]
    ulong StageRevision,
    [property: JsonPropertyName("appliedProposalIds")]
    [property: MaxLength(OpenUsdMcpLimits.MaximumProposalCount)]
    [property: Description("Distinct, ordinally sorted IDs that were applied; at most 128.")]
    IReadOnlyList<string> AppliedProposalIds,
    [property: JsonPropertyName("checkpointId")]
    [property: Description("Recovery checkpoint created immediately before proposal mutation.")]
    string CheckpointId)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary => $"Applied {AppliedProposalIds.Count} proposal(s).";
}

internal sealed record McpFinalizationResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active session identifier finalized.")]
    string SessionId,
    [property: JsonPropertyName("generation")]
    [property: Description("Unchanged finalized generation.")]
    long Generation,
    [property: JsonPropertyName("stageRevision")]
    [property: Description("Unchanged finalized native stage revision.")]
    ulong StageRevision,
    [property: JsonPropertyName("partial")]
    [property: Description(
        "True when one or more requested finalization artifacts failed while the bounded report set " +
        "was still produced.")]
    bool Partial,
    [property: JsonPropertyName("finalStageCreated")]
    [property: Description("True when the flattened final stage was exported successfully.")]
    bool FinalStageCreated,
    [property: JsonPropertyName("artifacts")]
    [property: MaxLength(3)]
    [property: Description("Published read-only report-resource descriptors; at most three.")]
    IReadOnlyList<McpArtifactDto> Artifacts,
    [property: JsonPropertyName("failures")]
    [property: MaxLength(OpenUsdMcpLimits.MaximumArtifactCount)]
    [property: Description("Bounded role-prefixed failure messages; at most 16. Empty on complete success.")]
    IReadOnlyList<string> Failures,
    [property: JsonIgnore] IReadOnlyList<ArtifactResourceDescriptor> ArtifactResources)
    : IOpenUsdMcpOutput, IOpenUsdMcpArtifactOutput
{
    [JsonIgnore]
    public string Summary =>
        Partial ? $"Finalization completed with {Failures.Count} failure(s)." : "Finalization completed.";
}

internal sealed record McpPresentationResultDto(
    [property: JsonPropertyName("sessionId")]
    [property: Description("Active finalized session identifier.")]
    string SessionId,
    [property: JsonPropertyName("processId")]
    [property: Description("Positive operating-system process identifier of the configured Viewer child.")]
    int ProcessId,
    [property: JsonPropertyName("startedAt")]
    [property: Description("UTC timestamp at which the Viewer child was started.")]
    DateTimeOffset StartedAt,
    [property: JsonPropertyName("renderer")]
    [property: Description("Renderer selection passed to the Viewer child: auto, silk, or storm.")]
    string Renderer)
    : IOpenUsdMcpOutput
{
    [JsonIgnore]
    public string Summary => $"Viewer process {ProcessId} started with renderer {Renderer}.";
}

internal sealed record McpToolErrorEnvelope(
    [property: JsonPropertyName("error")]
    [property: Description(
        "Deterministic tool-execution error. Tool errors set isError=true and do not satisfy the " +
        "success output schema.")]
    McpToolErrorDto Error);

internal sealed record McpToolErrorDto(
    [property: JsonPropertyName("code")]
    [property: Description(
        "Stable code: invalid_argument, path_denied, no_session, stale_session, stale_revision, " +
        "proposal_stale, quota_exceeded, native_failure, render_failure, or launch_failure.")]
    string Code,
    [property: JsonPropertyName("message")]
    [property: Description("Bounded corrective message safe to expose to the client.")]
    string Message);
