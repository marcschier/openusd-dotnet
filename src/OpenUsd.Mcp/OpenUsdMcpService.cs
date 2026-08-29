// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal interface IOpenUsdMcpService
{
    ValueTask<McpSessionDto> OpenSceneAsync(OpenSceneRequest request, CancellationToken cancellationToken);

    ValueTask<McpClosedSceneDto> CloseSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpSessionDto> GetSceneAsync(SceneRevisionRequest request, CancellationToken cancellationToken);

    ValueTask<McpSceneInspectionDto> InspectSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpEditResultDto> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken cancellationToken);

    ValueTask<McpCheckpointResultDto> CheckpointSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpRollbackResultDto> RollbackSceneAsync(
        RollbackSceneRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpCaptureResultDto> RenderPreviewAsync(
        RenderPreviewRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpAnalysisResultDto> AnalyzeSceneAsync(AnalyzeSceneRequest request, CancellationToken cancellationToken);

    ValueTask<McpApplyProposalsResultDto> ApplyProposalsAsync(
        ApplyProposalsRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpFinalizationResultDto> FinalizeSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken);

    ValueTask<McpPresentationResultDto> PresentSceneAsync(
        PresentSceneRequest request,
        CancellationToken cancellationToken);
}

internal sealed class OpenUsdMcpService(
    McpSessionWorkspace workspace,
    IServiceProvider services,
    OpenUsdMcpApplicationOptions options) : IOpenUsdMcpService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _appliedProposalIds = [];
    private readonly int _maximumAppliedProposalHistoryCount =
        ValidateAppliedProposalHistoryCount(options.MaximumAppliedProposalHistoryCount);
    private IReadOnlyList<AnalysisProposal> _analysisProposals = [];
    private CaptureWorker? _captureWorker;
    private AnalysisInput? _lastAnalysisInput;
    private PreviewCaptureResult? _lastContactSheet;
    private FinalizationResult? _lastFinalization;
    private PreviewCaptureResult? _lastStill;
    private PreviewCaptureResult? _lastTurntable;
    private string? _sourcePath;

    public async ValueTask<McpSessionDto> OpenSceneAsync(
        OpenSceneRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOpenRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionStatus status = await workspace.GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            if (status.IsActive)
            {
                throw new ArgumentException(
                    "Only one scene session may be active.",
                    nameof(request));
            }

            WorkspaceSessionInfo info = await workspace
                .StartAsync(request.SourcePath, cancellationToken)
                .ConfigureAwait(false);
            _sourcePath = request.SourcePath.Replace('\\', '/');
            ResetDerivedState();
            return ToSession(info, _sourcePath);
        }
        catch (Exception exception) when (
            exception is WorkspacePathContainmentException or
            UnauthorizedAccessException or
            DirectoryNotFoundException or
            FileNotFoundException)
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.PathDenied,
                "The source path is unavailable or outside the configured source root.",
                exception);
        }
        catch (WorkspaceSourceCompositionException exception)
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.NativeFailure,
                exception.Message,
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpClosedSceneDto> CloseSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            if (_captureWorker is not null)
            {
                await _captureWorker.ResetAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await workspace.CloseAsync(revision, cancellationToken).ConfigureAwait(false);
            ResetDerivedState();
            _sourcePath = null;
            return new McpClosedSceneDto(request.SessionId, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpSessionDto> GetSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionSnapshot snapshot = await GetSnapshotAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return ToSession(snapshot.Session, _sourcePath ?? Path.GetFileName(snapshot.Session.SourcePath));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpSceneInspectionDto> InspectSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionSnapshot snapshot = await GetSnapshotAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            WorkspaceSessionManifest manifest = snapshot.Manifest;
            WorkspaceSceneStatistics statistics = await workspace.InspectSceneAsync(
                    new WorkspaceSessionRevision(
                        request.SessionId,
                        request.Generation,
                        request.StageRevision),
                    cancellationToken)
                .ConfigureAwait(false);
            return new McpSceneInspectionDto(
                ToSession(snapshot.Session, _sourcePath ?? Path.GetFileName(snapshot.Session.SourcePath)),
                manifest.Checkpoints.Length,
                manifest.Journal.Length,
                manifest.Journal.LastOrDefault()?.Kind.ToString(),
                BoundString(statistics.DefaultPrimPath, OpenUsdMcpLimits.MaximumTextLength),
                statistics.PrimCount,
                statistics.MeshCount,
                statistics.CurveVertexCount,
                statistics.MeshVertexCount,
                statistics.FaceCount,
                statistics.RootPrimCount,
                statistics.LeafPrimCount,
                statistics.MaximumDepth);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpEditResultDto> ApplyEditsAsync(
        ApplyEditsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCount(request.Edits, 1, OpenUsdMcpLimits.MaximumEditCount, nameof(request.Edits));
        WorkspaceEditOperation[] operations = request.Edits.Select(CreateEdit).ToArray();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request.ToRevisionRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            WorkspaceEditResult result = await workspace
                .EditAsync(revision, new WorkspaceEditBatch(operations), cancellationToken)
                .ConfigureAwait(false);
            InvalidateDerivedState();
            return new McpEditResultDto(
                result.SessionId,
                result.Generation,
                result.StageRevision,
                result.Checkpoint.CheckpointId,
                result.OperationCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpCheckpointResultDto> CheckpointSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            WorkspaceCheckpointResult result = await workspace
                .CheckpointAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            return new McpCheckpointResultDto(
                result.SessionId,
                result.Generation,
                result.StageRevision,
                result.Checkpoint.CheckpointId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpRollbackResultDto> RollbackSceneAsync(
        RollbackSceneRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentifier(request.CheckpointId, nameof(request.CheckpointId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request.ToRevisionRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            WorkspaceRollbackResult result = await workspace
                .RollbackAsync(revision, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false);
            InvalidateDerivedState();
            return new McpRollbackResultDto(
                result.SessionId,
                result.Generation,
                result.StageRevision,
                result.CheckpointId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpCaptureResultDto> RenderPreviewAsync(
        RenderPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRenderRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request.ToRevisionRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            string requestId = string.Concat(
                revision.SessionId,
                "-",
                Guid.NewGuid().ToString("N"));
            CaptureKind kind = ParseCaptureKind(request.Kind);
            IReadOnlyList<CameraState> cameras = await workspace
                .CreatePreviewCamerasAsync(
                    revision,
                    request.CameraPath,
                    kind == CaptureKind.Turntable && request.CameraPath is null,
                    request.Width,
                    request.Height,
                    request.Views.Select(static view => view.TimeCode).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            var captureRequest = new PreviewCaptureRequest(
                requestId,
                kind,
                request.Width,
                request.Height,
                Array.AsReadOnly(
                    request.Views.Select(
                            (view, index) => new CaptureView(
                                view.Name,
                                cameras[index],
                                view.TimeCode))
                        .ToArray()));
            PreviewCaptureResult result;
            try
            {
                _captureWorker ??= services.GetRequiredService<CaptureWorker>();
                result = await _captureWorker
                    .CaptureAsync(captureRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException and
                not CaptureQueueFullException and
                not CaptureAdmissionTimeoutException and
                not ArtifactResourceStoreCapacityException)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.RenderFailure,
                    "Preview rendering failed.",
                    exception);
            }

            switch (kind)
            {
                case CaptureKind.Still:
                    _lastStill = result;
                    break;
                case CaptureKind.ContactSheet:
                    _lastContactSheet = result;
                    break;
                case CaptureKind.Turntable:
                    _lastTurntable = result;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported capture kind.");
            }

            return ToCapture(revision, result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpAnalysisResultDto> AnalyzeSceneAsync(
        AnalyzeSceneRequest request,
        CancellationToken cancellationToken)
    {
        ValidateAnalysisRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request.ToRevisionRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            AnalysisInput input = CreateAnalysisInput(revision, request.Observations);
            var analysisService = new AnalysisProposalService(AnalysisDefaults.CreateAnalyzers());
            _analysisProposals = analysisService.Analyze(input);
            _lastAnalysisInput = input;
            return new McpAnalysisResultDto(
                revision.SessionId,
                revision.Generation,
                revision.StageRevision,
                Array.AsReadOnly(_analysisProposals
                    .Take(OpenUsdMcpLimits.MaximumProposalCount)
                    .Select(ToProposal)
                    .ToArray()));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpApplyProposalsResultDto> ApplyProposalsAsync(
        ApplyProposalsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCount(
            request.ProposalIds,
            1,
            OpenUsdMcpLimits.MaximumProposalCount,
            nameof(request.ProposalIds));
        foreach (string proposalId in request.ProposalIds)
        {
            ValidateIdentifier(proposalId, nameof(request.ProposalIds));
        }
        if (request.ProposalIds.Distinct(StringComparer.Ordinal).Count() !=
            request.ProposalIds.Count)
        {
            throw new ArgumentException(
                "Proposal IDs must be distinct.",
                nameof(request));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request.ToRevisionRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (_analysisProposals.Count == 0)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.ProposalStale,
                    "No current analysis proposals are available.");
            }

            EnsureAppliedProposalCapacity(
                _appliedProposalIds.Count,
                request.ProposalIds.Count,
                _maximumAppliedProposalHistoryCount);

            IReadOnlyList<ProposalPayload> payloads;
            try
            {
                payloads = AnalysisProposalService.SelectOverlayPayloads(
                    _analysisProposals,
                    request.ProposalIds,
                    new AnalysisCoordinates(revision.Generation, checked((long)revision.StageRevision)));
            }
            catch (InvalidOperationException exception)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.ProposalStale,
                    exception.Message,
                    exception);
            }

            WorkspaceEditOperation[] operations = payloads.Select(CreateEdit).ToArray();
            string[] appliedIds = request.ProposalIds
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            WorkspaceEditResult result = await workspace
                .EditAsync(revision, new WorkspaceEditBatch(operations), cancellationToken)
                .ConfigureAwait(false);
            _appliedProposalIds.AddRange(appliedIds);
            _analysisProposals = [];
            _lastAnalysisInput = null;
            _lastFinalization = null;
            InvalidatePreviewState();
            return new McpApplyProposalsResultDto(
                result.SessionId,
                result.Generation,
                result.StageRevision,
                Array.AsReadOnly(appliedIds),
                result.Checkpoint.CheckpointId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpFinalizationResultDto> FinalizeSceneAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            FinalizationAnalysis analysis = CreateFinalizationAnalysis();
            var finalizationRequest = new FinalizationRequest(
                revision,
                analysis,
                new FinalizationPreviewOutputs(
                    _lastStill,
                    _lastContactSheet,
                    _lastTurntable));
            _lastFinalization = await services.GetRequiredService<FinalizationService>()
                .FinalizeAsync(finalizationRequest, cancellationToken)
                .ConfigureAwait(false);
            return ToFinalization(_lastFinalization);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<McpPresentationResultDto> PresentSceneAsync(
        PresentSceneRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePresentationRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceSessionRevision revision = await RequireRevisionAsync(
                    request.ToRevisionRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (_lastFinalization?.FinalStagePath is not { } stagePath ||
                _lastFinalization.Session.Session.Generation != revision.Generation ||
                _lastFinalization.Session.Session.StageRevision != revision.StageRevision)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.LaunchFailure,
                    "The current scene must be finalized successfully before presentation.");
            }

            try
            {
                ViewerProcessMetadata process = services.GetRequiredService<ViewerChildLauncher>()
                    .Launch(new ViewerLaunchRequest(
                        stagePath,
                        options.PluginPath,
                        request.Renderer,
                        request.CameraPath));
                return new McpPresentationResultDto(
                    revision.SessionId,
                    process.ProcessId,
                    process.StartedAt,
                    request.Renderer);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new OpenUsdMcpFailureException(
                    OpenUsdMcpErrorCodes.LaunchFailure,
                    "Viewer launch failed.",
                    exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static AnalysisInput CreateAnalysisInput(
        WorkspaceSessionRevision revision,
        AnalysisObservationsDto observations) =>
        new(
            new AnalysisCoordinates(revision.Generation, checked((long)revision.StageRevision)),
            new SceneAnalysisSnapshot(
                observations.ViewportWidth,
                observations.ViewportHeight,
                new CameraTechnicalSnapshot(
                    observations.SubjectCoverage,
                    observations.NearClip,
                    observations.FarClip,
                    observations.NearestGeometryDistance,
                    observations.FarthestGeometryDistance),
                new RenderSettingsSnapshot(
                    observations.SamplesPerPixel,
                    observations.LightingEnabled,
                    observations.ShadowsEnabled,
                    observations.QualityPreset),
                observations.ValidationIssues),
            new PerformanceSnapshot(
                observations.FrameMilliseconds,
                observations.DrawSucceeded,
                observations.FinitePixelRatio,
                observations.BackgroundPixelRatio,
                observations.DrawCalls,
                observations.TriangleCount,
                observations.ResourceCount,
                observations.ResidentBytes),
            new CompositionSnapshot(),
            observations.RendererId);

    internal static void EnsureAppliedProposalCapacity(
        int currentCount,
        int incomingCount,
        int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(incomingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        if (currentCount > maximumCount - incomingCount)
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.QuotaExceeded,
                $"The applied proposal history quota of {maximumCount} was exceeded.");
        }
    }

    private static WorkspaceEditOperation CreateEdit(WorkspaceEditDto edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ValidateString(edit.Kind, 32, nameof(edit.Kind));
        ValidateString(edit.PrimPath, OpenUsdMcpLimits.MaximumPathLength, nameof(edit.PrimPath));
        if (edit.TypeName is not null)
        {
            ValidateString(
                edit.TypeName,
                OpenUsdMcpLimits.MaximumIdentifierLength,
                nameof(edit.TypeName));
        }

        if (edit.AttributeName is not null)
        {
            ValidateString(
                edit.AttributeName,
                OpenUsdMcpLimits.MaximumIdentifierLength,
                nameof(edit.AttributeName));
        }

        return edit.Kind switch
        {
            "define_prim" => new DefinePrimWorkspaceEdit(edit.PrimPath, edit.TypeName),
            "set_active" when edit.Active is bool active =>
                new SetActiveWorkspaceEdit(edit.PrimPath, active),
            "set_double" when edit.AttributeName is not null && edit.Value is double value =>
                new SetDoubleWorkspaceEdit(
                    edit.PrimPath,
                    edit.AttributeName,
                    value,
                    edit.TimeCode),
            "set_bool" when edit.AttributeName is not null &&
                edit.BoolValue is bool boolValue =>
                new SetBoolWorkspaceEdit(
                    edit.PrimPath,
                    edit.AttributeName,
                    boolValue,
                    edit.TimeCode),
            "set_int64" when edit.AttributeName is not null &&
                edit.Int64Value is long int64Value =>
                new SetInt64WorkspaceEdit(
                    edit.PrimPath,
                    edit.AttributeName,
                    int64Value,
                    edit.TimeCode),
            "set_string" when edit.AttributeName is not null &&
                edit.StringValue is not null =>
                new SetStringWorkspaceEdit(
                    edit.PrimPath,
                    edit.AttributeName,
                    edit.StringValue,
                    edit.TimeCode),
            "set_token" when edit.AttributeName is not null &&
                edit.StringValue is not null =>
                new SetTokenWorkspaceEdit(
                    edit.PrimPath,
                    edit.AttributeName,
                    edit.StringValue,
                    edit.TimeCode),
            "set_float3" when edit.AttributeName is not null &&
                edit.VectorValue is not null =>
                CreateFloat3Edit(edit, color: false),
            "set_color3f" when edit.AttributeName is not null &&
                edit.VectorValue is not null =>
                CreateFloat3Edit(edit, color: true),
            "clear_overlay_attribute" when edit.AttributeName is not null =>
                new ClearOverlayAttributeWorkspaceEdit(edit.PrimPath, edit.AttributeName),
            _ => throw new ArgumentException(
                $"Edit kind '{edit.Kind}' is unsupported or is missing required fields.",
                nameof(edit)),
        };
    }

    private static WorkspaceEditOperation CreateFloat3Edit(
        WorkspaceEditDto edit,
        bool color)
    {
        if (edit.VectorValue is null || edit.VectorValue.Count != 3)
        {
            throw new ArgumentException(
                "Float3 and color3f edits require exactly three components.",
                nameof(edit));
        }

        float x = checked((float)edit.VectorValue[0]);
        float y = checked((float)edit.VectorValue[1]);
        float z = checked((float)edit.VectorValue[2]);
        return color
            ? new SetColor3fWorkspaceEdit(
                edit.PrimPath,
                edit.AttributeName!,
                x,
                y,
                z,
                edit.TimeCode)
            : new SetFloat3WorkspaceEdit(
                edit.PrimPath,
                edit.AttributeName!,
                x,
                y,
                z,
                edit.TimeCode);
    }

    private static WorkspaceEditOperation CreateEdit(ProposalPayload payload) =>
        payload.Operation switch
        {
            "define-prim" => new DefinePrimWorkspaceEdit(
                payload.Arguments["primPath"],
                payload.Arguments.GetValueOrDefault("typeName")),
            "set-active" => new SetActiveWorkspaceEdit(
                payload.Arguments["primPath"],
                bool.Parse(payload.Arguments["active"])),
            "set-double" => new SetDoubleWorkspaceEdit(
                payload.Arguments["primPath"],
                payload.Arguments["attributeName"],
                double.Parse(payload.Arguments["value"], CultureInfo.InvariantCulture),
                payload.Arguments.TryGetValue("timeCode", out string? timeCode)
                    ? double.Parse(timeCode, CultureInfo.InvariantCulture)
                    : null),
            "clear-overlay-attribute" => new ClearOverlayAttributeWorkspaceEdit(
                payload.Arguments["primPath"],
                payload.Arguments["attributeName"]),
            _ => throw new InvalidOperationException(
                $"Proposal operation '{payload.Operation}' is not supported."),
        };

    private static void ValidateOpenRequest(OpenSceneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateString(
            request.SourcePath,
            OpenUsdMcpLimits.MaximumPathLength,
            nameof(request.SourcePath));
        if (Path.IsPathRooted(request.SourcePath))
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.PathDenied,
                "Source paths must be relative to the configured source root.");
        }

        string extension = Path.GetExtension(request.SourcePath);
        if (extension is not (".usd" or ".usda" or ".usdc" or ".usdz"))
        {
            throw new ArgumentException(
                "Source paths must use a supported USD extension.",
                nameof(request));
        }
    }

    private static void ValidateRenderRequest(RenderPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = ParseCaptureKind(request.Kind);
        if (request.Width is < 1 or > 4096 || request.Height is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Preview dimensions must be between 1 and 4096 pixels.");
        }

        ValidateCount(request.Views, 1, OpenUsdMcpLimits.MaximumViewCount, nameof(request.Views));
        if (request.Kind == "still" && request.Views.Count != 1)
        {
            throw new ArgumentException("Still previews require exactly one view.", nameof(request));
        }

        foreach (CaptureViewDto view in request.Views)
        {
            ArgumentNullException.ThrowIfNull(view);
            ValidateString(view.Name, OpenUsdMcpLimits.MaximumIdentifierLength, nameof(view.Name));
            if (!double.IsFinite(view.TimeCode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "View time codes must be finite.");
            }

        }

        if (request.CameraPath is not null)
        {
            ValidateString(
                request.CameraPath,
                OpenUsdMcpLimits.MaximumPathLength,
                nameof(request.CameraPath));
            WorkspaceEditValidation.ValidatePrimPath(
                request.CameraPath,
                nameof(request.CameraPath));
        }
    }

    private static void ValidateAnalysisRequest(AnalyzeSceneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Observations);
        AnalysisObservationsDto value = request.Observations;
        if (value.ViewportWidth is < 1 or > 4096 ||
            value.ViewportHeight is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Observation viewport dimensions must be between 1 and 4096 pixels.");
        }

        if (value.SamplesPerPixel is < 1 or > 65536)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Observation samplesPerPixel must be between 1 and 65536.");
        }

        ValidateString(value.QualityPreset, 16, nameof(value.QualityPreset));
        if (value.QualityPreset is not ("draft" or "balanced" or "final"))
        {
            throw new ArgumentException("Quality preset must be draft, balanced, or final.");
        }

        ValidateString(value.RendererId, 16, nameof(value.RendererId));
        if (value.RendererId is not ("silk" or "storm"))
        {
            throw new ArgumentException("Renderer ID must be silk or storm.");
        }

        ValidateCount(
            value.ValidationIssues,
            0,
            OpenUsdMcpLimits.MaximumIssueCount,
            nameof(value.ValidationIssues));
        foreach (string issue in value.ValidationIssues)
        {
            ValidateString(issue, OpenUsdMcpLimits.MaximumTextLength, nameof(value.ValidationIssues));
        }

        _ = CreateAnalysisInput(
            new WorkspaceSessionRevision(request.SessionId, request.Generation, request.StageRevision),
            value);
    }

    private static void ValidatePresentationRequest(PresentSceneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateString(request.Renderer, 16, nameof(request.Renderer));
        if (request.Renderer is not ("auto" or "silk" or "storm"))
        {
            throw new ArgumentException("Renderer must be auto, silk, or storm.");
        }

        if (request.CameraPath is not null)
        {
            ValidateString(
                request.CameraPath,
                OpenUsdMcpLimits.MaximumPathLength,
                nameof(request.CameraPath));
            WorkspaceEditValidation.ValidatePrimPath(request.CameraPath, nameof(request.CameraPath));
        }
    }

    private static CaptureKind ParseCaptureKind(string kind)
    {
        ValidateString(kind, 32, nameof(kind));
        return kind switch
        {
            "still" => CaptureKind.Still,
            "contact_sheet" => CaptureKind.ContactSheet,
            "turntable" => CaptureKind.Turntable,
            _ => throw new ArgumentException(
                "Capture kind must be still, contact_sheet, or turntable.",
                nameof(kind)),
        };
    }

    private static McpSessionDto ToSession(WorkspaceSessionInfo info, string sourcePath) =>
        new(
            info.SessionId,
            info.Generation,
            info.StageRevision,
            sourcePath.Replace('\\', '/'),
            info.CreatedAt);

    private static McpCaptureResultDto ToCapture(
        WorkspaceSessionRevision revision,
        PreviewCaptureResult capture) =>
        new(
            revision.SessionId,
            revision.Generation,
            revision.StageRevision,
            capture.RequestId,
            capture.Kind switch
            {
                CaptureKind.Still => "still",
                CaptureKind.ContactSheet => "contact_sheet",
                CaptureKind.Turntable => "turntable",
                _ => throw new InvalidOperationException("Unsupported capture result kind."),
            },
            capture.Width,
            capture.Height,
            Array.AsReadOnly(capture.Artifacts.Select(ToArtifact).ToArray()),
            Array.AsReadOnly(capture.Diagnostics.Select(ToDiagnostic).ToArray()),
            capture.Artifacts);

    private static McpArtifactDto ToArtifact(ArtifactResourceDescriptor artifact) =>
        new(
            artifact.Id,
            artifact.ResourceUri.AbsoluteUri,
            artifact.MediaType,
            artifact.ByteLength,
            artifact.Sha256,
            artifact.IsInline);

    private static McpRenderDiagnosticDto ToDiagnostic(RenderDiagnostic diagnostic) =>
        new(
            diagnostic.Severity switch
            {
                RenderDiagnosticSeverity.Information => "info",
                RenderDiagnosticSeverity.Warning => "warning",
                RenderDiagnosticSeverity.Error => "error",
                _ => throw new InvalidOperationException(
                    "Unsupported renderer diagnostic severity."),
            },
            diagnostic.Code,
            diagnostic.Message);

    private static McpProposalDto ToProposal(AnalysisProposal proposal) =>
        new(
            proposal.Id,
            proposal.Category.ToString().ToLowerInvariant(),
            proposal.Code,
            proposal.Title,
            proposal.Applicability switch
            {
                ProposalApplicability.OverlayApplicable => "overlay_applicable",
                ProposalApplicability.FlattenOnly => "flatten_only",
                ProposalApplicability.DiagnosticOnly => "diagnostic_only",
                _ => throw new InvalidOperationException("Unsupported proposal applicability."),
            },
            proposal.Risk.ToString().ToLowerInvariant(),
            proposal.Explanation);

    private static McpFinalizationResultDto ToFinalization(FinalizationResult result)
    {
        ArtifactResourceDescriptor[] resources =
        [
            .. new[]
            {
                result.ManifestResource,
                result.JsonReportResource,
                result.MarkdownReportResource,
            }.OfType<ArtifactResourceDescriptor>(),
        ];
        return new McpFinalizationResultDto(
            result.Session.Session.SessionId,
            result.Session.Session.Generation,
            result.Session.Session.StageRevision,
            result.IsPartial,
            result.FinalStagePath is not null,
            Array.AsReadOnly(resources.Select(ToArtifact).ToArray()),
            Array.AsReadOnly(result.Failures
                .Take(OpenUsdMcpLimits.MaximumArtifactCount)
                .Select(failure => BoundString(
                    string.Concat(failure.Role, ": ", failure.Message),
                    OpenUsdMcpLimits.MaximumTextLength))
                .ToArray()),
            Array.AsReadOnly(resources));
    }

    private FinalizationAnalysis CreateFinalizationAnalysis()
    {
        FinalizationValidationFinding[] findings = _analysisProposals
            .Where(static proposal => proposal.Category == AnalysisCategory.Validation)
            .Select(proposal => new FinalizationValidationFinding(
                proposal.Code,
                proposal.Risk.ToString().ToLowerInvariant(),
                proposal.Explanation))
            .ToArray();
        var statistics = new List<FinalizationStatistic>();
        if (_lastAnalysisInput is { } input)
        {
            statistics.Add(new FinalizationStatistic(
                "frameMilliseconds",
                input.Performance.FrameMilliseconds.ToString("G17", CultureInfo.InvariantCulture)));
            statistics.Add(new FinalizationStatistic(
                "drawCalls",
                input.Performance.DrawCalls.ToString(CultureInfo.InvariantCulture)));
            statistics.Add(new FinalizationStatistic(
                "triangleCount",
                input.Performance.TriangleCount.ToString(CultureInfo.InvariantCulture)));
        }

        return new FinalizationAnalysis(findings, statistics, _appliedProposalIds);
    }

    private async ValueTask<WorkspaceSessionSnapshot> GetSnapshotAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        WorkspaceSessionRevision revision = await RequireRevisionAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await workspace.GetSnapshotAsync(revision, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WorkspaceSessionRevision> RequireRevisionAsync(
        SceneRevisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentifier(request.SessionId, nameof(request.SessionId));
        ArgumentOutOfRangeException.ThrowIfNegative(request.Generation);
        WorkspaceSessionStatus status = await workspace.GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!status.IsActive || status.Session is null)
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.NoSession,
                "No scene session is active.");
        }

        if (!string.Equals(request.SessionId, status.Session.SessionId, StringComparison.Ordinal))
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.StaleSession,
                "The session identifier is not the active session.");
        }

        if (request.Generation != status.Session.Generation ||
            request.StageRevision != status.Session.StageRevision)
        {
            throw new OpenUsdMcpFailureException(
                OpenUsdMcpErrorCodes.StaleRevision,
                "The generation or stage revision is stale.");
        }

        return new WorkspaceSessionRevision(
            request.SessionId,
            request.Generation,
            request.StageRevision);
    }

    private static void ValidateIdentifier(string value, string parameterName) =>
        ValidateString(value, OpenUsdMcpLimits.MaximumIdentifierLength, parameterName);

    private static void ValidateString(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The value must contain 1 to {maximumLength} non-control characters.",
                parameterName);
        }
    }

    private static string BoundString(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 3), "...");

    private static int ValidateAppliedProposalHistoryCount(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value;
    }

    private static void ValidateCount<T>(
        IReadOnlyCollection<T> values,
        int minimum,
        int maximum,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count < minimum || values.Count > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The collection must contain between {minimum} and {maximum} items.");
        }
    }

    private void InvalidateDerivedState()
    {
        _analysisProposals = [];
        _lastAnalysisInput = null;
        _lastFinalization = null;
        InvalidatePreviewState();
    }

    private void InvalidatePreviewState()
    {
        _lastContactSheet = null;
        _lastStill = null;
        _lastTurntable = null;
    }

    private void ResetDerivedState()
    {
        InvalidateDerivedState();
        _appliedProposalIds.Clear();
    }
}

internal static class OpenUsdMcpErrorCodes
{
    internal const string InvalidArgument = "invalid_argument";
    internal const string LaunchFailure = "launch_failure";
    internal const string NativeFailure = "native_failure";
    internal const string NoSession = "no_session";
    internal const string PathDenied = "path_denied";
    internal const string ProposalStale = "proposal_stale";
    internal const string QuotaExceeded = "quota_exceeded";
    internal const string RenderFailure = "render_failure";
    internal const string StaleRevision = "stale_revision";
    internal const string StaleSession = "stale_session";
}

internal sealed class OpenUsdMcpFailureException(
    string code,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    internal string Code { get; } = code;
}
