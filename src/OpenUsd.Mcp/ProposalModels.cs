// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Mcp;

internal enum ProposalApplicability
{
    OverlayApplicable,
    FlattenOnly,
    DiagnosticOnly,
}

internal enum ProposalRisk
{
    Low,
    Medium,
    High,
}

internal sealed record ProposalEvidence
{
    public ProposalEvidence(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}

internal sealed record ProposalExpectedMetric
{
    public ProposalExpectedMetric(string name, double current, double expected, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        AnalysisNumericValidation.RequireFinite(current, nameof(current));
        AnalysisNumericValidation.RequireFinite(expected, nameof(expected));
        Name = name;
        Current = current;
        Expected = expected;
        Unit = unit;
    }

    public string Name { get; }

    public double Current { get; }

    public double Expected { get; }

    public string Unit { get; }
}

internal sealed record ProposalPayload
{
    public ProposalPayload(string operation, IEnumerable<KeyValuePair<string, string>>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation;
        Arguments = CreateArguments(arguments);
    }

    public string Operation { get; }

    public IReadOnlyDictionary<string, string> Arguments { get; }

    private static System.Collections.ObjectModel.ReadOnlyDictionary<string, string> CreateArguments(
        IEnumerable<KeyValuePair<string, string>>? arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in arguments ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double numericValue) &&
                !double.IsFinite(numericValue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arguments),
                    $"Numeric proposal argument '{key}' must be finite.");
            }

            if (!result.TryAdd(key, value))
            {
                throw new ArgumentException(
                    $"Proposal argument '{key}' is specified more than once.",
                    nameof(arguments));
            }
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            result.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value,
                    StringComparer.Ordinal));
    }
}

internal sealed record ProposalDraft
{
    public ProposalDraft(
        AnalysisCategory category,
        string code,
        string title,
        ProposalApplicability applicability,
        ProposalRisk risk,
        string explanation,
        ProposalPayload payload,
        IEnumerable<ProposalEvidence>? evidence = null,
        IEnumerable<ProposalExpectedMetric>? expectedMetrics = null,
        bool advisory = false,
        string? rendererId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        ArgumentNullException.ThrowIfNull(payload);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        if (!Enum.IsDefined(applicability))
        {
            throw new ArgumentOutOfRangeException(nameof(applicability));
        }

        if (!Enum.IsDefined(risk))
        {
            throw new ArgumentOutOfRangeException(nameof(risk));
        }

        if (advisory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rendererId);
        }
        else if (rendererId is not null)
        {
            throw new ArgumentException(
                "A renderer identifier is valid only for advisory proposals.",
                nameof(rendererId));
        }

        ProposalEvidence[] detachedEvidence = (evidence ?? []).ToArray();
        if (detachedEvidence.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Proposal evidence cannot contain null values.",
                nameof(evidence));
        }

        ProposalExpectedMetric[] detachedMetrics = (expectedMetrics ?? []).ToArray();
        if (detachedMetrics.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Expected metrics cannot contain null values.",
                nameof(expectedMetrics));
        }

        Category = category;
        Code = code;
        Title = title;
        Applicability = applicability;
        Risk = risk;
        Explanation = explanation;
        Payload = payload;
        Evidence = Array.AsReadOnly(
            detachedEvidence
                .OrderBy(static item => item.Name, StringComparer.Ordinal)
                .ThenBy(static item => item.Value, StringComparer.Ordinal)
                .ToArray());
        ExpectedMetrics = Array.AsReadOnly(
            detachedMetrics
                .OrderBy(static item => item.Name, StringComparer.Ordinal)
                .ThenBy(static item => item.Unit, StringComparer.Ordinal)
                .ToArray());
        Advisory = advisory;
        RendererId = rendererId;
    }

    public AnalysisCategory Category { get; }

    public string Code { get; }

    public string Title { get; }

    public ProposalApplicability Applicability { get; }

    public ProposalRisk Risk { get; }

    public string Explanation { get; }

    public ProposalPayload Payload { get; }

    public IReadOnlyList<ProposalEvidence> Evidence { get; }

    public IReadOnlyList<ProposalExpectedMetric> ExpectedMetrics { get; }

    public bool Advisory { get; }

    public string? RendererId { get; }
}

internal sealed record AnalysisProposal(
    string Id,
    AnalysisCoordinates Coordinates,
    AnalysisCategory Category,
    string Code,
    string Title,
    ProposalApplicability Applicability,
    ProposalRisk Risk,
    string Explanation,
    ProposalPayload Payload,
    IReadOnlyList<ProposalEvidence> Evidence,
    IReadOnlyList<ProposalExpectedMetric> ExpectedMetrics,
    bool Advisory,
    string? RendererId);

internal sealed record ProposalStaleness(
    bool IsStale,
    AnalysisCoordinates ProposalCoordinates,
    AnalysisCoordinates CurrentCoordinates,
    string? Reason);

internal interface IProposalAnalyzer
{
    AnalysisCategory Category { get; }

    IEnumerable<ProposalDraft> Analyze(AnalysisInput input);
}

internal static class ProposalSupportedOperations
{
    private const string DefinePrim = "define-prim";
    private const string SetActive = "set-active";
    private const string SetDouble = "set-double";
    private const string ClearOverlayAttribute = "clear-overlay-attribute";

    internal static IReadOnlyList<string> OverlayOperations { get; } =
        Array.AsReadOnly(
        [
            ClearOverlayAttribute,
            DefinePrim,
            SetActive,
            SetDouble,
        ]);

    internal static void Validate(ProposalDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Applicability == ProposalApplicability.OverlayApplicable)
        {
            ValidateOverlayPayload(draft.Payload);
        }
    }

    internal static void ValidateOverlayPayload(ProposalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        WorkspaceEditOperation operation = payload.Operation switch
        {
            DefinePrim => CreateDefinePrim(payload.Arguments),
            SetActive => CreateSetActive(payload.Arguments),
            SetDouble => CreateSetDouble(payload.Arguments),
            ClearOverlayAttribute => CreateClearOverlayAttribute(payload.Arguments),
            _ => throw new InvalidOperationException(
                $"Proposal operation '{payload.Operation}' is not a supported workspace edit."),
        };

        operation.Validate();
    }

    private static DefinePrimWorkspaceEdit CreateDefinePrim(
        IReadOnlyDictionary<string, string> arguments)
    {
        ValidateArgumentNames(arguments, ["primPath"], ["typeName"]);
        return new DefinePrimWorkspaceEdit(
            arguments["primPath"],
            arguments.GetValueOrDefault("typeName"));
    }

    private static SetActiveWorkspaceEdit CreateSetActive(
        IReadOnlyDictionary<string, string> arguments)
    {
        ValidateArgumentNames(arguments, ["active", "primPath"], []);
        if (!bool.TryParse(arguments["active"], out bool active))
        {
            throw new ArgumentException(
                "Workspace proposal argument 'active' must be a Boolean.",
                nameof(arguments));
        }

        return new SetActiveWorkspaceEdit(arguments["primPath"], active);
    }

    private static SetDoubleWorkspaceEdit CreateSetDouble(
        IReadOnlyDictionary<string, string> arguments)
    {
        ValidateArgumentNames(
            arguments,
            ["attributeName", "primPath", "value"],
            ["timeCode"]);
        return new SetDoubleWorkspaceEdit(
            arguments["primPath"],
            arguments["attributeName"],
            ParseFinite(arguments["value"], "value"),
            arguments.TryGetValue("timeCode", out string? timeCode)
                ? ParseFinite(timeCode, "timeCode")
                : null);
    }

    private static ClearOverlayAttributeWorkspaceEdit CreateClearOverlayAttribute(
        IReadOnlyDictionary<string, string> arguments)
    {
        ValidateArgumentNames(arguments, ["attributeName", "primPath"], []);
        return new ClearOverlayAttributeWorkspaceEdit(
            arguments["primPath"],
            arguments["attributeName"]);
    }

    private static void ValidateArgumentNames(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyList<string> required,
        IReadOnlyList<string> optional)
    {
        foreach (string name in required)
        {
            if (!arguments.ContainsKey(name))
            {
                throw new ArgumentException(
                    $"Workspace proposal argument '{name}' is required.",
                    nameof(arguments));
            }
        }

        HashSet<string> supported = required
            .Concat(optional)
            .ToHashSet(StringComparer.Ordinal);
        string? unsupported = arguments.Keys.FirstOrDefault(key => !supported.Contains(key));
        if (unsupported is not null)
        {
            throw new ArgumentException(
                $"Workspace proposal argument '{unsupported}' is not supported.",
                nameof(arguments));
        }
    }

    private static double ParseFinite(string value, string argumentName)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result) ||
            !double.IsFinite(result))
        {
            throw new ArgumentException(
                $"Workspace proposal argument '{argumentName}' must be a finite number.",
                nameof(value));
        }

        return result;
    }
}
