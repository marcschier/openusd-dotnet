// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenUsd.Interop;

namespace OpenUsd.Mcp;

internal static class OpenUsdMcpDescriptions
{
    internal const string OpenScene =
        "Open a USD file beneath the configured read-only source root and create the single " +
        "isolated overlay session. " +
        "Preconditions: no session is active; sourcePath is a relative .usd, .usda, .usdc, or .usdz path. " +
        "Effects: creates an overlay, manifest, journal, and output directory; never modifies the source. " +
        "Result bounds: one structured session object and one text summary of at most 2048 characters. " +
        "Errors: invalid_argument for malformed input or a second session; path_denied for " +
        "unavailable, rooted, escaping, or indirect paths; native_failure when OpenUSD " +
        "cannot open the stage. " +
        "Example arguments: {\"request\":{\"sourcePath\":\"assets/robot.usda\"}}.";

    internal const string CloseScene =
        "Close and deterministically dispose the one active scene session. " +
        "Preconditions: sessionId, generation, and stageRevision exactly match the active session. " +
        "Effects: persists the close journal, releases retained render sources and native " +
        "stage state, and permits a later open_scene call. " +
        "Result bounds: one structured close acknowledgement and one text summary of at most 2048 characters. " +
        "Errors: no_session when none is active; stale_session for a foreign sessionId; " +
        "stale_revision for mismatched generation or stageRevision; invalid_argument for " +
        "malformed values; native_failure for teardown or persistence failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<open_scene " +
        "sessionId>\",\"generation\":0,\"stageRevision\":1}}.";

    internal const string GetScene =
        "Return the bounded identity and optimistic revision of the active scene without " +
        "exposing native handles or absolute output paths. " +
        "Preconditions: sessionId, generation, and stageRevision exactly match the active session. " +
        "Effects: read-only; refreshes the native stage revision before comparison. " +
        "Result bounds: one structured session object and one text summary of at most 2048 characters. " +
        "Errors: no_session, stale_session, stale_revision, invalid_argument, or " +
        "native_failure with their documented meanings. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<open_scene " +
        "sessionId>\",\"generation\":0,\"stageRevision\":1}}.";

    internal const string InspectScene =
        "Compute detached scheduler-safe scene statistics and return bounded checkpoint and journal counts. " +
        "Preconditions: sessionId, generation, and stageRevision exactly match the active session. " +
        "Effects: read-only; performs count-only native preflight before bounded " +
        "composed-stage traversal and computes prim, mesh, curve, vertex, face, root, leaf, " +
        "and depth counts. " +
        "Result bounds: one fixed-shape structured object with scalar counts and one text " +
        "summary of at most 2048 characters; no arbitrary USDA, layer contents, native " +
        "handles, or unrestricted paths. " +
        "Errors: quota_exceeded when inspection traversal, retained-path, or geometry " +
        "budgets are exceeded; no_session, stale_session, stale_revision, invalid_argument, " +
        "or native_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<open_scene " +
        "sessionId>\",\"generation\":0,\"stageRevision\":1}}.";

    internal const string ApplyEdits =
        "Atomically apply typed opinions to the session overlay; arbitrary USDA and " +
        "executable code are not accepted. " +
        "Preconditions: exact active revision and 1-128 valid edits. Supported kinds cover " +
        "prim definition/activation, double, bool, int64, string, token, float3, color3f, " +
        "and clearing overlay attribute values. " +
        "Effects: creates a recovery checkpoint, commits all edits or restores all of them, " +
        "then advances generation and stageRevision on success. " +
        "Result bounds: one fixed-shape commit object and one text summary of at most 2048 characters. " +
        "Errors: invalid_argument for an unsupported or malformed edit; no_session, " +
        "stale_session, stale_revision, or native_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":0,\"stageRevi" +
        "sion\":1,\"edits\":[{\"kind\":\"define_prim\",\"primPath\":\"/World/Cube\",\"typeNam" +
        "e\":\"Cube\"}]}}.";

    internal const string CheckpointScene =
        "Create an immutable checkpoint of the current overlay without changing scene content. " +
        "Preconditions: sessionId, generation, and stageRevision exactly match the active session. " +
        "Effects: writes one session-confined checkpoint and journal entry; generation and " +
        "stageRevision remain unchanged. " +
        "Result bounds: one fixed-shape checkpoint object and one text summary of at most 2048 characters. " +
        "Errors: no_session, stale_session, stale_revision, invalid_argument, or native_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":0,\"stageRevision\":1}}.";

    internal const string RollbackScene =
        "Restore a checkpoint known to the active session. " +
        "Preconditions: exact active revision and a checkpointId returned by checkpoint_scene or apply_edits. " +
        "Effects: first creates a recovery checkpoint, restores the requested overlay " +
        "atomically, reloads the stage, and advances generation and stageRevision. " +
        "Result bounds: one fixed-shape rollback object and one text summary of at most 2048 characters. " +
        "Errors: invalid_argument for an unknown or malformed checkpoint; no_session, " +
        "stale_session, stale_revision, or native_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":1,\"stageRevi" +
        "sion\":2,\"checkpointId\":\"<checkpointId>\"}}.";

    internal const string RenderPreview =
        "Render the exact active revision using the retained preview pipeline and publish immutable PNG artifacts. " +
        "Preconditions: exact active revision; dimensions are 1-4096; views contain 1-16 " +
        "finite time codes; still has exactly one view; optional cameraPath is an absolute " +
        "UsdGeomCamera prim path. " +
        "Effects: read-only scene access but consumes bounded render and artifact quotas. An " +
        "authored camera is sampled when supplied; otherwise turntable captures orbit the " +
        "scene bounds. Inline image blocks are emitted only at or below the configured byte " +
        "cap; otherwise resource links are emitted. " +
        "Result bounds: at most 16 artifact descriptors plus one text block and at most 16 " +
        "image/resource-link blocks. " +
        "Errors: invalid_argument, no_session, stale_session, stale_revision, quota_exceeded, or render_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":0,\"stageRevi" +
        "sion\":1,\"kind\":\"still\",\"width\":1024,\"height\":1024,\"views\":[{\"name\":\"he" +
        "ro\",\"timeCode\":0}]}}.";

    internal const string AnalyzeScene =
        "Run deterministic camera, lighting, render-setting, performance, composition, and " +
        "validation analyzers over bounded detached observations. " +
        "Preconditions: exact active revision and finite observations satisfying all schema " +
        "ranges and cross-field rules. " +
        "Effects: read-only; replaces the prior proposal set and binds every new proposal to " +
        "this generation and stageRevision. " +
        "Result bounds: at most 128 deterministically ordered proposal objects and one text " +
        "summary of at most 2048 characters. " +
        "Errors: invalid_argument, no_session, stale_session, stale_revision, or native_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":0,\"stageRevi" +
        "sion\":1,\"observations\":{\"rendererId\":\"silk\",\"qualityPreset\":\"balanced\"}}}" +
        ". Omitted observation fields use schema defaults.";

    internal const string ApplyProposals =
        "Atomically apply selected overlay-applicable proposals from the latest analysis. " +
        "Preconditions: exact active revision; 1-128 proposal IDs exist in the latest " +
        "analyze_scene result, remain revision-current, and are all overlay_applicable. " +
        "Effects: creates a recovery checkpoint, commits the derived typed edits, advances " +
        "generation and stageRevision, and invalidates the consumed proposal set. " +
        "Result bounds: at most 128 sorted applied IDs in one fixed-shape object and one " +
        "text summary of at most 2048 characters. " +
        "Errors: proposal_stale for revision-stale, diagnostic_only, or flatten_only " +
        "proposals; quota_exceeded for history or workspace limits; invalid_argument for " +
        "malformed or unknown IDs; no_session, stale_session, stale_revision, or " +
        "native_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":0,\"stageRevi" +
        "sion\":1,\"proposalIds\":[\"<analyze_scene proposal id>\"]}}.";

    internal const string FinalizeScene =
        "Persist the overlay, export a flattened final stage, write deterministic " +
        "JSON/Markdown reports and a manifest, and publish report resources. " +
        "Preconditions: sessionId, generation, and stageRevision exactly match the active " +
        "session. Preview artifacts are optional; missing previews are recorded as partial " +
        "failures rather than invented. " +
        "Effects: writes only inside the session output directory and keeps the session active. " +
        "Result bounds: at most three report-resource descriptors, at most 16 bounded " +
        "failure messages, at most 16 additional content blocks, and one text summary of at " +
        "most 2048 characters. " +
        "Errors: invalid_argument, no_session, stale_session, stale_revision, or " +
        "native_failure. Per-artifact export or publication failures may instead return " +
        "partial=true with bounded failures. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":1,\"stageRevision\":2}}.";

    internal const string PresentScene =
        "Launch only the configured OpenUsd.Viewer.App child for the current successfully finalized stage. " +
        "Preconditions: exact active revision; finalize_scene produced a final stage for " +
        "that same revision; renderer is auto, silk, or storm; optional cameraPath is an " +
        "absolute USD prim path. " +
        "Effects: starts one child process with an argument list and redirected " +
        "protocol-sensitive streams; never executes a caller-supplied executable or " +
        "unrestricted filesystem path. " +
        "Result bounds: one fixed-shape process metadata object and one text summary of at most 2048 characters. " +
        "Errors: invalid_argument, no_session, stale_session, stale_revision, or launch_failure. " +
        "Example arguments: {\"request\":{\"sessionId\":\"<id>\",\"generation\":1,\"stageRevi" +
        "sion\":2,\"renderer\":\"auto\",\"cameraPath\":\"/World/Camera\"}}.";

    internal const string ArtifactResource =
        "Read one immutable artifact previously returned by an OpenUSD MCP tool. " +
        "Preconditions: use an exact openusd://artifact/{id} URI from a tool result; id is " +
        "percent-decoded, contains no path separators or control characters, and is at most " +
        "1024 characters. " +
        "Result: textual MIME types return UTF-8 text content; other MIME types return " +
        "base64 MCP blob content with the stored MIME type. Content size is bounded by the " +
        "configured artifact-store quota and cannot be modified through MCP. " +
        "Errors: invalid_argument for an invalid id, artifact_not_found for an unknown or " +
        "expired process-local artifact, and artifact_invalid_text when a textual artifact " +
        "is not valid UTF-8. " +
        "Example URI: openusd://artifact/<id-from-render_preview-or-finalize_scene>.";
}

internal static class OpenUsdMcpToolRegistration
{
    internal static IMcpServerBuilder WithOpenUsdTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithTools<OpenUsdMcpTools>()
            .WithResources<OpenUsdMcpResources>();
    }
}

[McpServerToolType]
internal sealed class OpenUsdMcpTools(
    IOpenUsdMcpService service,
    OpenUsdMcpProtocolOptions protocolOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(
        Name = "open_scene",
        Title = "Open OpenUSD Scene",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpSessionDto))]
    [Description(OpenUsdMcpDescriptions.OpenScene)]
    public ValueTask<CallToolResult> OpenSceneAsync(
        [Description(
            "Request object containing the confined sourcePath. See nested schema descriptions " +
            "and the tool example.")] OpenSceneRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.OpenSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "close_scene",
        Title = "Close OpenUSD Scene",
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpClosedSceneDto))]
    [Description(OpenUsdMcpDescriptions.CloseScene)]
    public ValueTask<CallToolResult> CloseSceneAsync(
        [Description(
            "Exact optimistic revision returned by the preceding successful scene tool.")]
        SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.CloseSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "get_scene",
        Title = "Get OpenUSD Scene",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpSessionDto))]
    [Description(OpenUsdMcpDescriptions.GetScene)]
    public ValueTask<CallToolResult> GetSceneAsync(
        [Description("Current optimistic scene revision.")] SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.GetSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "inspect_scene",
        Title = "Inspect OpenUSD Scene",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpSceneInspectionDto))]
    [Description(OpenUsdMcpDescriptions.InspectScene)]
    public ValueTask<CallToolResult> InspectSceneAsync(
        [Description("Current optimistic scene revision.")] SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.InspectSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "apply_edits",
        Title = "Apply Typed OpenUSD Edits",
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpEditResultDto))]
    [Description(OpenUsdMcpDescriptions.ApplyEdits)]
    public ValueTask<CallToolResult> ApplyEditsAsync(
        [Description(
            "Exact optimistic revision plus an atomic list of 1-128 typed edits. See nested " +
            "kind-specific field semantics.")] ApplyEditsRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.ApplyEditsAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "checkpoint_scene",
        Title = "Checkpoint OpenUSD Scene",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpCheckpointResultDto))]
    [Description(OpenUsdMcpDescriptions.CheckpointScene)]
    public ValueTask<CallToolResult> CheckpointSceneAsync(
        [Description("Current optimistic scene revision.")] SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.CheckpointSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "rollback_scene",
        Title = "Rollback OpenUSD Scene",
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpRollbackResultDto))]
    [Description(OpenUsdMcpDescriptions.RollbackScene)]
    public ValueTask<CallToolResult> RollbackSceneAsync(
        [Description(
            "Exact optimistic revision and an existing checkpointId from this active session.")]
        RollbackSceneRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.RollbackSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "render_preview",
        Title = "Render OpenUSD Preview",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpCaptureResultDto))]
    [Description(OpenUsdMcpDescriptions.RenderPreview)]
    public ValueTask<CallToolResult> RenderPreviewAsync(
        [Description(
            "Exact optimistic revision, capture mode, bounded dimensions, and 1-16 ordered views.")]
        RenderPreviewRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.RenderPreviewAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "analyze_scene",
        Title = "Analyze OpenUSD Scene",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpAnalysisResultDto))]
    [Description(OpenUsdMcpDescriptions.AnalyzeScene)]
    public ValueTask<CallToolResult> AnalyzeSceneAsync(
        [Description(
            "Exact optimistic revision and bounded finite detached technical observations.")]
        AnalyzeSceneRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.AnalyzeSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "apply_proposals",
        Title = "Apply OpenUSD Proposals",
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpApplyProposalsResultDto))]
    [Description(OpenUsdMcpDescriptions.ApplyProposals)]
    public ValueTask<CallToolResult> ApplyProposalsAsync(
        [Description(
            "Exact optimistic revision and 1-128 IDs from the latest analyze_scene result.")]
        ApplyProposalsRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.ApplyProposalsAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "finalize_scene",
        Title = "Finalize OpenUSD Scene",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpFinalizationResultDto))]
    [Description(OpenUsdMcpDescriptions.FinalizeScene)]
    public ValueTask<CallToolResult> FinalizeSceneAsync(
        [Description("Current optimistic scene revision.")] SceneRevisionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.FinalizeSceneAsync(request, token), cancellationToken);

    [McpServerTool(
        Name = "present_scene",
        Title = "Present OpenUSD Scene",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpPresentationResultDto))]
    [Description(OpenUsdMcpDescriptions.PresentScene)]
    public ValueTask<CallToolResult> PresentSceneAsync(
        [Description(
            "Exact optimistic revision plus constrained renderer and optional USD camera prim " +
            "path.")] PresentSceneRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(token => service.PresentSceneAsync(request, token), cancellationToken);

    private async ValueTask<CallToolResult> InvokeAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
        where T : IOpenUsdMcpOutput
    {
        try
        {
            T output = await operation(cancellationToken).ConfigureAwait(false);
            JsonElement structured = JsonSerializer.SerializeToElement(output, JsonOptions);
            var content = new List<ContentBlock>
            {
                new TextContentBlock
                {
                    Text = Bound(output.Summary, protocolOptions.MaximumTextContentLength),
                },
            };
            if (output is IOpenUsdMcpArtifactOutput artifacts)
            {
                AddArtifactBlocks(content, artifacts.ArtifactResources);
            }

            return new CallToolResult
            {
                Content = content,
                StructuredContent = structured,
                IsError = false,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            (string code, string message) = Classify(exception);
            message = Bound(message, OpenUsdMcpLimits.MaximumTextLength);
            var error = new McpToolErrorEnvelope(new McpToolErrorDto(code, message));
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = Bound(
                            string.Concat(code, ": ", message),
                            protocolOptions.MaximumTextContentLength),
                    },
                ],
                StructuredContent = JsonSerializer.SerializeToElement(error, JsonOptions),
                IsError = true,
            };
        }
    }

    private void AddArtifactBlocks(
        List<ContentBlock> content,
        IReadOnlyList<ArtifactResourceDescriptor> artifacts)
    {
        int maximumArtifacts = Math.Min(
            OpenUsdMcpLimits.MaximumArtifactCount,
            protocolOptions.MaximumContentBlockCount - content.Count);
        foreach (ArtifactResourceDescriptor artifact in artifacts.Take(maximumArtifacts))
        {
            if (artifact.IsInline &&
                artifact.MediaType.StartsWith("image/", StringComparison.Ordinal) &&
                artifact.ByteLength <= protocolOptions.InlineImageMaximumBytes)
            {
                content.Add(ImageContentBlock.FromBytes(
                    Convert.FromBase64String(artifact.InlineBase64!),
                    artifact.MediaType));
                continue;
            }

            content.Add(new ResourceLinkBlock
            {
                Uri = artifact.ResourceUri.AbsoluteUri,
                Name = artifact.Id,
                Title = artifact.Id,
                Description = "Read-only OpenUSD MCP artifact.",
                MimeType = artifact.MediaType,
                Size = artifact.ByteLength,
            });
        }
    }

    private static (string Code, string Message) Classify(Exception exception)
    {
        Exception effective = exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;
        return effective switch
        {
            OpenUsdMcpFailureException failure => (failure.Code, failure.Message),
            WorkspaceSessionRevisionException => (
                OpenUsdMcpErrorCodes.StaleRevision,
                "The generation or stage revision is stale."),
            WorkspacePathContainmentException => (
                OpenUsdMcpErrorCodes.PathDenied,
                "The requested path is unavailable or outside the configured roots."),
            WorkspaceQuotaExceededException => (
                OpenUsdMcpErrorCodes.QuotaExceeded,
                effective.Message),
            StageStatisticsQuotaExceededException => (
                OpenUsdMcpErrorCodes.QuotaExceeded,
                effective.Message),
            ArtifactResourceStoreCapacityException or
            CaptureQueueFullException or
            CaptureAdmissionTimeoutException => (
                OpenUsdMcpErrorCodes.QuotaExceeded,
                effective.Message),
            UnauthorizedAccessException or
            DirectoryNotFoundException or
            FileNotFoundException or
            PathTooLongException => (
                OpenUsdMcpErrorCodes.PathDenied,
                "The requested path is unavailable or outside the configured roots."),
            OpenUsdNativeException native => (
                OpenUsdMcpErrorCodes.NativeFailure,
                NativeFailureMessage(native)),
            DllNotFoundException => (
                OpenUsdMcpErrorCodes.NativeFailure,
                "A required native library could not be loaded. Verify the staged runtime and loader path."),
            BadImageFormatException => (
                OpenUsdMcpErrorCodes.NativeFailure,
                "A native library has the wrong architecture or format for this process."),
            EntryPointNotFoundException => (
                OpenUsdMcpErrorCodes.NativeFailure,
                "The native runtime ABI is incompatible with this managed MCP version."),
            ArgumentException or FormatException or OverflowException => (
                OpenUsdMcpErrorCodes.InvalidArgument,
                effective.Message),
            InvalidOperationException when effective.Message.Contains(
                "No workspace session",
                StringComparison.Ordinal) => (
                    OpenUsdMcpErrorCodes.NoSession,
                    "No scene session is active."),
            _ => (
                OpenUsdMcpErrorCodes.NativeFailure,
                "The OpenUSD operation failed."),
        };
    }

    private static string NativeFailureMessage(OpenUsdNativeException exception)
    {
        string detail = exception.Message.Trim();
        if (detail.Length == 0 ||
            detail.Contains(Path.DirectorySeparatorChar) ||
            detail.Contains(Path.AltDirectorySeparatorChar) ||
            detail.Contains("://", StringComparison.Ordinal))
        {
            return $"OpenUSD returned native status {exception.Status}.";
        }

        return $"OpenUSD returned native status {exception.Status}: {detail}";
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength - 3), "...");
}
