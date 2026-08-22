// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Mcp;

internal sealed class AnalysisValidationAnalyzer : IProposalAnalyzer
{
    public AnalysisCategory Category => AnalysisCategory.Validation;

    public IEnumerable<ProposalDraft> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        int index = 0;
        foreach (string issue in input.Scene.ValidationIssues.Order(StringComparer.Ordinal))
        {
            yield return new ProposalDraft(
                AnalysisCategory.Validation,
                $"validation.issue.{index.ToString("D4", CultureInfo.InvariantCulture)}",
                "Review validation finding",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Medium,
                issue,
                new ProposalPayload(
                    "inspect-validation-finding",
                    [new("finding", issue)]),
                [new("finding", issue)]);
            index++;
        }
    }
}
