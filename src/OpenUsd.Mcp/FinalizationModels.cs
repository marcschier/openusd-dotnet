// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

public enum FinalizationArtifactStatus
{
    Created,
    Failed,
}

public sealed record FinalizationValidationFinding
{
    public FinalizationValidationFinding(string code, string severity, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Severity = severity;
        Message = message;
    }

    public string Code { get; }

    public string Severity { get; }

    public string Message { get; }
}

public sealed record FinalizationStatistic
{
    public FinalizationStatistic(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}

public sealed record FinalizationAnalysis
{
    public FinalizationAnalysis(
        IEnumerable<FinalizationValidationFinding>? validation = null,
        IEnumerable<FinalizationStatistic>? statistics = null,
        IEnumerable<string>? appliedProposalIds = null)
    {
        Validation = Array.AsReadOnly(
            (validation ?? [])
                .OrderBy(static item => item.Code, StringComparer.Ordinal)
                .ThenBy(static item => item.Severity, StringComparer.Ordinal)
                .ThenBy(static item => item.Message, StringComparer.Ordinal)
                .ToArray());
        Statistics = Array.AsReadOnly(
            (statistics ?? [])
                .OrderBy(static item => item.Name, StringComparer.Ordinal)
                .ThenBy(static item => item.Value, StringComparer.Ordinal)
                .ToArray());
        string[] proposals = (appliedProposalIds ?? []).ToArray();
        if (proposals.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Applied proposal IDs cannot contain null or blank values.",
                nameof(appliedProposalIds));
        }

        AppliedProposalIds = Array.AsReadOnly(
            proposals.Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public IReadOnlyList<FinalizationValidationFinding> Validation { get; }

    public IReadOnlyList<FinalizationStatistic> Statistics { get; }

    public IReadOnlyList<string> AppliedProposalIds { get; }
}

public sealed record FinalizationPreviewOutputs(
    PreviewCaptureResult? HeroStill = null,
    PreviewCaptureResult? ContactSheet = null,
    PreviewCaptureResult? Turntable = null);

public sealed record FinalizationRequest
{
    public FinalizationRequest(
        WorkspaceSessionRevision revision,
        FinalizationAnalysis analysis,
        FinalizationPreviewOutputs? previewOutputs = null)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        PreviewOutputs = previewOutputs ?? new FinalizationPreviewOutputs();
    }

    public WorkspaceSessionRevision Revision { get; }

    public FinalizationAnalysis Analysis { get; }

    public FinalizationPreviewOutputs PreviewOutputs { get; }
}

public sealed record FinalizationOptions(int MaximumTurntableFrames = 16);

public sealed record FinalizationArtifactRecord(
    string Role,
    string RelativePath,
    string MediaType,
    FinalizationArtifactStatus Status,
    long? ByteLength,
    string? Sha256,
    Uri? ResourceUri,
    string? Error);

public sealed record FinalizationFailure(string Role, string Message);

public sealed record FinalizationResult(
    WorkspaceSessionSnapshot Session,
    string OutputDirectory,
    string? FinalStagePath,
    string ManifestPath,
    string JsonReportPath,
    string MarkdownReportPath,
    IReadOnlyList<FinalizationArtifactRecord> Artifacts,
    IReadOnlyList<FinalizationFailure> Failures,
    ArtifactResourceDescriptor? ManifestResource,
    ArtifactResourceDescriptor? JsonReportResource,
    ArtifactResourceDescriptor? MarkdownReportResource)
{
    public bool IsPartial => Failures.Count != 0;
}
