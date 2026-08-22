// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenUsd.Mcp;

internal static class ProposalSerialization
{
    public static string Serialize(IEnumerable<AnalysisProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (AnalysisProposal proposal in proposals
                         .OrderBy(static item => item.Category)
                         .ThenBy(static item => item.Code, StringComparer.Ordinal)
                         .ThenBy(static item => item.Id, StringComparer.Ordinal))
            {
                WriteProposal(writer, proposal);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string ComputeId(
        AnalysisCoordinates coordinates,
        ProposalDraft draft)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sessionGeneration", coordinates.SessionGeneration);
            writer.WriteNumber("stageRevision", coordinates.StageRevision);
            writer.WriteString("category", Format(draft.Category));
            writer.WriteString("code", draft.Code);
            writer.WriteString("title", draft.Title);
            writer.WriteString("applicability", Format(draft.Applicability));
            writer.WriteString("risk", Format(draft.Risk));
            writer.WriteString("explanation", draft.Explanation);
            writer.WriteBoolean("advisory", draft.Advisory);
            if (draft.RendererId is not null)
            {
                writer.WriteString("rendererId", draft.RendererId);
            }

            WritePayload(writer, draft.Payload);
            WriteEvidence(writer, draft.Evidence);
            WriteExpectedMetrics(writer, draft.ExpectedMetrics);
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))
            .ToLowerInvariant();
    }

    private static void WriteProposal(Utf8JsonWriter writer, AnalysisProposal proposal)
    {
        writer.WriteStartObject();
        writer.WriteString("id", proposal.Id);
        writer.WriteNumber("sessionGeneration", proposal.Coordinates.SessionGeneration);
        writer.WriteNumber("stageRevision", proposal.Coordinates.StageRevision);
        writer.WriteString("category", Format(proposal.Category));
        writer.WriteString("code", proposal.Code);
        writer.WriteString("title", proposal.Title);
        writer.WriteString("applicability", Format(proposal.Applicability));
        writer.WriteString("risk", Format(proposal.Risk));
        writer.WriteString("explanation", proposal.Explanation);
        writer.WriteBoolean("advisory", proposal.Advisory);
        if (proposal.RendererId is not null)
        {
            writer.WriteString("rendererId", proposal.RendererId);
        }

        writer.WritePropertyName("payload");
        WritePayloadValue(writer, proposal.Payload);
        WriteEvidence(writer, proposal.Evidence);
        WriteExpectedMetrics(writer, proposal.ExpectedMetrics);
        writer.WriteEndObject();
    }

    private static void WriteEvidence(
        Utf8JsonWriter writer,
        IEnumerable<ProposalEvidence> evidenceItems)
    {
        writer.WriteStartArray("evidence");
        foreach (ProposalEvidence evidence in evidenceItems
                     .OrderBy(static item => item.Name, StringComparer.Ordinal)
                     .ThenBy(static item => item.Value, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", evidence.Name);
            writer.WriteString("value", evidence.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteExpectedMetrics(
        Utf8JsonWriter writer,
        IEnumerable<ProposalExpectedMetric> expectedMetrics)
    {
        writer.WriteStartArray("expectedMetrics");
        foreach (ProposalExpectedMetric metric in expectedMetrics
                     .OrderBy(static item => item.Name, StringComparer.Ordinal)
                     .ThenBy(static item => item.Unit, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", metric.Name);
            writer.WriteNumber("current", metric.Current);
            writer.WriteNumber("expected", metric.Expected);
            writer.WriteString("unit", metric.Unit);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePayload(Utf8JsonWriter writer, ProposalPayload payload)
    {
        writer.WritePropertyName("payload");
        WritePayloadValue(writer, payload);
    }

    private static void WritePayloadValue(Utf8JsonWriter writer, ProposalPayload payload)
    {
        writer.WriteStartObject();
        writer.WriteString("operation", payload.Operation);
        writer.WriteStartObject("arguments");
        foreach ((string key, string value) in payload.Arguments
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string Format(AnalysisCategory value) => value switch
    {
        AnalysisCategory.Camera => "camera",
        AnalysisCategory.Lighting => "lighting",
        AnalysisCategory.RenderSettings => "render-settings",
        AnalysisCategory.Performance => "performance",
        AnalysisCategory.Composition => "composition",
        AnalysisCategory.Validation => "validation",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Format(ProposalApplicability value) => value switch
    {
        ProposalApplicability.OverlayApplicable => "overlay-applicable",
        ProposalApplicability.FlattenOnly => "flatten-only",
        ProposalApplicability.DiagnosticOnly => "diagnostic-only",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Format(ProposalRisk value) => value switch
    {
        ProposalRisk.Low => "low",
        ProposalRisk.Medium => "medium",
        ProposalRisk.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
