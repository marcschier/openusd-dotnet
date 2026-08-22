// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace OpenUsd.Mcp;

internal static class FinalizationReportWriter
{
    internal const string ContainmentCaveat =
        "Source and output path containment covers authored workspace files only; " +
        "composed asset dependencies may resolve outside those roots and are not copied.";

    internal static byte[] CreateAnalysisJson(
        WorkspaceSessionSnapshot session,
        FinalizationAnalysis analysis,
        IEnumerable<FinalizationArtifactRecord> artifacts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(artifacts);
        using var stream = new MemoryStream();
        using (var writer = CreateWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            WriteSession(writer, session);
            writer.WriteString("containmentCaveat", ContainmentCaveat);
            WriteValidation(writer, analysis.Validation);
            WriteStatistics(writer, analysis.Statistics);
            WriteAppliedProposals(writer, analysis.AppliedProposalIds);
            WriteArtifacts(writer, artifacts);
            writer.WriteEndObject();
        }

        return AppendNewline(stream);
    }

    internal static byte[] CreateMarkdown(
        WorkspaceSessionSnapshot session,
        FinalizationAnalysis analysis,
        IEnumerable<FinalizationArtifactRecord> artifacts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(artifacts);
        FinalizationArtifactRecord[] orderedArtifacts = OrderArtifacts(artifacts);
        var builder = new StringBuilder()
            .AppendLine("# OpenUSD Finalization Report")
            .AppendLine()
            .Append("Session: `").Append(session.Session.SessionId).AppendLine("`")
            .Append("Generation: ").Append(session.Session.Generation).AppendLine()
            .Append("Stage revision: ").Append(session.Session.StageRevision).AppendLine()
            .AppendLine()
            .AppendLine("## Containment")
            .AppendLine()
            .AppendLine(ContainmentCaveat)
            .AppendLine()
            .AppendLine("## Validation")
            .AppendLine();
        AppendItems(
            builder,
            analysis.Validation.Select(
                static finding =>
                    $"[{finding.Severity}] {finding.Code}: {finding.Message}"),
            "No validation findings were recorded.");
        builder.AppendLine()
            .AppendLine("## Statistics")
            .AppendLine();
        AppendItems(
            builder,
            analysis.Statistics.Select(
                static statistic => $"{statistic.Name}: {statistic.Value}"),
            "No statistics were recorded.");
        builder.AppendLine()
            .AppendLine("## Applied Proposals")
            .AppendLine();
        AppendItems(
            builder,
            analysis.AppliedProposalIds,
            "No applied proposals were recorded.");
        builder.AppendLine()
            .AppendLine("## Artifacts")
            .AppendLine();
        AppendItems(
            builder,
            orderedArtifacts.Select(FormatArtifact),
            "No artifacts were recorded.");
        return Encoding.UTF8.GetBytes(builder.ToString().ReplaceLineEndings("\n"));
    }

    internal static byte[] CreateManifestJson(
        WorkspaceSessionSnapshot session,
        IEnumerable<FinalizationArtifactRecord> artifacts,
        IEnumerable<FinalizationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(failures);
        using var stream = new MemoryStream();
        using (var writer = CreateWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("generatedAt", session.Session.CreatedAt);
            WriteSession(writer, session);
            writer.WriteString("containmentCaveat", ContainmentCaveat);
            WriteArtifacts(writer, artifacts);
            writer.WriteStartArray("failures");
            foreach (FinalizationFailure failure in failures
                         .OrderBy(static item => item.Role, StringComparer.Ordinal)
                         .ThenBy(static item => item.Message, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("role", failure.Role);
                writer.WriteString("message", failure.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return AppendNewline(stream);
    }

    private static void AppendItems(
        StringBuilder builder,
        IEnumerable<string> items,
        string emptyMessage)
    {
        string[] detached = items.ToArray();
        if (detached.Length == 0)
        {
            builder.Append("- ").AppendLine(emptyMessage);
            return;
        }

        foreach (string item in detached)
        {
            builder.Append("- ").AppendLine(item.ReplaceLineEndings(" "));
        }
    }

    private static string FormatArtifact(FinalizationArtifactRecord artifact)
    {
        if (artifact.Status == FinalizationArtifactStatus.Failed)
        {
            return $"{artifact.Role}: failed - {artifact.Error}";
        }

        return $"{artifact.Role}: `{artifact.RelativePath}` " +
            $"({artifact.MediaType}, {artifact.ByteLength} bytes, sha256 {artifact.Sha256})";
    }

    private static Utf8JsonWriter CreateWriter(Stream stream) =>
        new(
            stream,
            new JsonWriterOptions
            {
                Indented = true,
            });

    private static byte[] AppendNewline(MemoryStream stream)
    {
        string content = Encoding.UTF8.GetString(
            stream.GetBuffer(),
            0,
            checked((int)stream.Length));
        return Encoding.UTF8.GetBytes(
            string.Concat(content.ReplaceLineEndings("\n"), "\n"));
    }

    private static FinalizationArtifactRecord[] OrderArtifacts(
        IEnumerable<FinalizationArtifactRecord> artifacts) =>
        artifacts.OrderBy(static item => item.Role, StringComparer.Ordinal)
            .ThenBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

    private static void WriteAppliedProposals(
        Utf8JsonWriter writer,
        IEnumerable<string> proposalIds)
    {
        writer.WriteStartArray("appliedProposalIds");
        foreach (string proposalId in proposalIds.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(proposalId);
        }

        writer.WriteEndArray();
    }

    private static void WriteArtifacts(
        Utf8JsonWriter writer,
        IEnumerable<FinalizationArtifactRecord> artifacts)
    {
        writer.WriteStartArray("artifacts");
        foreach (FinalizationArtifactRecord artifact in OrderArtifacts(artifacts))
        {
            writer.WriteStartObject();
            writer.WriteString("role", artifact.Role);
            writer.WriteString("path", artifact.RelativePath);
            writer.WriteString("mediaType", artifact.MediaType);
            writer.WriteString(
                "status",
                artifact.Status == FinalizationArtifactStatus.Created
                    ? "created"
                    : "failed");
            if (artifact.ByteLength is long byteLength)
            {
                writer.WriteNumber("byteLength", byteLength);
            }

            if (artifact.Sha256 is not null)
            {
                writer.WriteString("sha256", artifact.Sha256);
            }

            if (artifact.ResourceUri is not null)
            {
                writer.WriteString("resourceUri", artifact.ResourceUri.AbsoluteUri);
            }

            if (artifact.Error is not null)
            {
                writer.WriteString("error", artifact.Error);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSession(
        Utf8JsonWriter writer,
        WorkspaceSessionSnapshot session)
    {
        writer.WriteStartObject("session");
        writer.WriteString("id", session.Session.SessionId);
        writer.WriteNumber("generation", session.Session.Generation);
        writer.WriteNumber("stageRevision", session.Session.StageRevision);
        writer.WriteString("sourcePath", session.Session.SourcePath);
        writer.WriteString("overlayPath", session.Session.OverlayPath);
        writer.WriteEndObject();
    }

    private static void WriteStatistics(
        Utf8JsonWriter writer,
        IEnumerable<FinalizationStatistic> statistics)
    {
        writer.WriteStartArray("statistics");
        foreach (FinalizationStatistic statistic in statistics
                     .OrderBy(static item => item.Name, StringComparer.Ordinal)
                     .ThenBy(static item => item.Value, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", statistic.Name);
            writer.WriteString("value", statistic.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValidation(
        Utf8JsonWriter writer,
        IEnumerable<FinalizationValidationFinding> validation)
    {
        writer.WriteStartArray("validation");
        foreach (FinalizationValidationFinding finding in validation
                     .OrderBy(static item => item.Code, StringComparer.Ordinal)
                     .ThenBy(static item => item.Severity, StringComparer.Ordinal)
                     .ThenBy(static item => item.Message, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("code", finding.Code);
            writer.WriteString("severity", finding.Severity);
            writer.WriteString("message", finding.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
