// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

internal sealed class AnalysisProposalService
{
    private readonly IProposalAnalyzer[] _analyzers;

    public AnalysisProposalService(IEnumerable<IProposalAnalyzer> analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        _analyzers = analyzers
            .OrderBy(static analyzer => analyzer.Category)
            .ThenBy(static analyzer => analyzer.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<AnalysisProposal> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        AnalysisProposal[] proposals = _analyzers
            .SelectMany(analyzer => analyzer.Analyze(input)
                ?? throw new InvalidOperationException(
                    $"Analyzer '{analyzer.GetType().FullName}' returned null."))
            .Select(draft => ProposalFactory.Create(input.Coordinates, draft))
            .OrderBy(static proposal => proposal.Category)
            .ThenBy(static proposal => proposal.Code, StringComparer.Ordinal)
            .ThenBy(static proposal => proposal.Id, StringComparer.Ordinal)
            .ToArray();

        return Array.AsReadOnly(proposals);
    }

    public static ProposalStaleness Validate(
        AnalysisProposal proposal,
        AnalysisCoordinates currentCoordinates)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(currentCoordinates);

        string? reason = proposal.Coordinates.SessionGeneration != currentCoordinates.SessionGeneration
            ? "The session generation changed."
            : proposal.Coordinates.StageRevision != currentCoordinates.StageRevision
                ? "The stage revision changed."
                : null;

        return new ProposalStaleness(
            reason is not null,
            proposal.Coordinates,
            currentCoordinates,
            reason);
    }

    public static IReadOnlyList<ProposalPayload> SelectOverlayPayloads(
        IEnumerable<AnalysisProposal> proposals,
        IEnumerable<string> selectedProposalIds,
        AnalysisCoordinates currentCoordinates)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(selectedProposalIds);
        ArgumentNullException.ThrowIfNull(currentCoordinates);

        HashSet<string> selected = selectedProposalIds.ToHashSet(StringComparer.Ordinal);
        AnalysisProposal[] matches = proposals
            .Where(proposal => selected.Contains(proposal.Id))
            .OrderBy(static proposal => proposal.Id, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length != selected.Count)
        {
            throw new ArgumentException(
                "One or more selected proposal IDs were not found.",
                nameof(selectedProposalIds));
        }

        AnalysisProposal? stale = matches.FirstOrDefault(
            proposal => Validate(proposal, currentCoordinates).IsStale);
        if (stale is not null)
        {
            throw new InvalidOperationException($"Proposal '{stale.Id}' is stale.");
        }

        AnalysisProposal? inapplicable = matches.FirstOrDefault(
            static proposal => proposal.Applicability != ProposalApplicability.OverlayApplicable);
        if (inapplicable is not null)
        {
            throw new InvalidOperationException(
                $"Proposal '{inapplicable.Id}' is not overlay-applicable.");
        }

        foreach (AnalysisProposal proposal in matches)
        {
            ProposalSupportedOperations.ValidateOverlayPayload(proposal.Payload);
        }

        return Array.AsReadOnly(matches.Select(static proposal => proposal.Payload).ToArray());
    }
}

internal static class ProposalFactory
{
    internal static AnalysisProposal Create(
        AnalysisCoordinates coordinates,
        ProposalDraft draft)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(draft);
        ProposalSupportedOperations.Validate(draft);

        string id = ProposalSerialization.ComputeId(coordinates, draft);
        return new AnalysisProposal(
            id,
            coordinates,
            draft.Category,
            draft.Code,
            draft.Title,
            draft.Applicability,
            draft.Risk,
            draft.Explanation,
            draft.Payload,
            draft.Evidence,
            draft.ExpectedMetrics,
            draft.Advisory,
            draft.RendererId);
    }
}
