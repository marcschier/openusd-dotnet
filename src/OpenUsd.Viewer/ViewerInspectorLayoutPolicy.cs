// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>Whether an inspector tab is user-facing or developer-only.</summary>
internal enum ViewerInspectorTabKind
{
    /// <summary>Always available; never sampled or hidden by default.</summary>
    User,

    /// <summary>Diagnostic tooling that stays hidden and idle until shown.</summary>
    Developer,
}

/// <summary>A stable inspector tab identity and its classification.</summary>
/// <param name="Id">
/// A stable, lowercase identity (for example <c>"diagnostics"</c>) that never
/// changes even if the tab moves within the tab strip. Selection and
/// visibility are always keyed by this, never by a visual index.
/// </param>
/// <param name="Kind">Whether the tab is user-facing or developer-only.</param>
internal sealed record ViewerInspectorTabDescriptor(string Id, ViewerInspectorTabKind Kind);

/// <summary>
/// The pure, stable set of Viewer inspector tabs and the policy for which
/// are visible and which one is selected.
/// </summary>
/// <remarks>
/// This module owns exactly one concern: given which tabs exist, which are
/// developer-only, and which are currently hidden, decide what is visible
/// and what the selection should fall back to when the previously selected
/// tab disappears. It knows nothing about <c>TabControl</c>, settings
/// serialization, or visual order, so it can be reasoned about (and tested)
/// without an Avalonia window. Callers translate between this module's
/// stable string identities and whatever visual index or control reference
/// their UI framework needs.
/// </remarks>
internal static class ViewerInspectorLayoutPolicy
{
    internal const string PropertiesTabId = "properties";
    internal const string ValueTabId = "value";
    internal const string MetadataTabId = "metadata";
    internal const string CompositionTabId = "composition";
    internal const string LayersTabId = "layers";
    internal const string DiagnosticsTabId = "diagnostics";
    internal const string ValidationTabId = "validation";
    internal const string HydraTabId = "hydra";
    internal const string PhysicsTabId = "physics";
    internal const string TfDebugTabId = "tfdebug";

    /// <summary>
    /// Every tab the Viewer offers today, in the order they currently appear.
    /// </summary>
    internal static IReadOnlyList<ViewerInspectorTabDescriptor> Tabs { get; } =
    [
        new(PropertiesTabId, ViewerInspectorTabKind.User),
        new(ValueTabId, ViewerInspectorTabKind.User),
        new(MetadataTabId, ViewerInspectorTabKind.User),
        new(CompositionTabId, ViewerInspectorTabKind.User),
        new(LayersTabId, ViewerInspectorTabKind.User),
        new(DiagnosticsTabId, ViewerInspectorTabKind.Developer),
        new(ValidationTabId, ViewerInspectorTabKind.User),
        new(HydraTabId, ViewerInspectorTabKind.Developer),
        new(PhysicsTabId, ViewerInspectorTabKind.User),
        new(TfDebugTabId, ViewerInspectorTabKind.Developer),
    ];

    /// <summary>
    /// The developer tabs a clean profile hides until the user opts in. This
    /// is the target default; today only <see cref="DiagnosticsTabId"/> has a
    /// visibility flag wired up, so callers may apply as much of this set as
    /// their current settings format supports.
    /// </summary>
    internal static IReadOnlyCollection<string> CleanDefaultHiddenTabIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DiagnosticsTabId,
            HydraTabId,
            TfDebugTabId,
        };

    /// <summary>
    /// The tab a clean profile falls back to when the selected tab is hidden
    /// or removed. This is the target fallback for a clean default; a caller
    /// preserving legacy behavior may resolve against a different fallback.
    /// </summary>
    internal const string CleanDefaultFallbackTabId = PropertiesTabId;

    private static readonly Dictionary<string, ViewerInspectorTabKind> KindById =
        Tabs.ToDictionary(static tab => tab.Id, static tab => tab.Kind, StringComparer.Ordinal);

    internal static bool IsKnownTab(string tabId)
    {
        ArgumentNullException.ThrowIfNull(tabId);
        return KindById.ContainsKey(tabId);
    }

    internal static ViewerInspectorTabKind KindOf(string tabId)
    {
        ArgumentNullException.ThrowIfNull(tabId);
        return KindById[tabId];
    }

    internal static bool IsDeveloperTab(string tabId) =>
        KindOf(tabId) == ViewerInspectorTabKind.Developer;

    /// <summary>
    /// Returns every known tab id that is not in <paramref name="hiddenTabIds"/>,
    /// in stable order.
    /// </summary>
    internal static IReadOnlyList<string> ComputeVisibleTabIds(
        IReadOnlyCollection<string> hiddenTabIds)
    {
        ArgumentNullException.ThrowIfNull(hiddenTabIds);
        return [.. Tabs
            .Select(static tab => tab.Id)
            .Where(id => !hiddenTabIds.Contains(id))];
    }

    /// <summary>
    /// Resolves which tab id should be selected given the previously selected
    /// tab, the currently visible tabs, and a preferred fallback.
    /// </summary>
    /// <remarks>
    /// The previously selected tab is kept whenever it is still visible.
    /// Otherwise <paramref name="fallbackTabId"/> is used if it is visible.
    /// Otherwise the first visible user tab in stable order is used, so the
    /// result never depends on which tab happened to be selected or on any
    /// visual index. If nothing is visible, <paramref name="fallbackTabId"/>
    /// is returned regardless, so callers always get a deterministic answer.
    /// <para>
    /// Visibility is defined purely by known stable tab identities: any id in
    /// <paramref name="visibleTabIds"/> that is not one of <see cref="Tabs"/>
    /// is never treated as visible, so a caller that leaks stale or malformed
    /// data (for example a settings file from a future schema version) cannot
    /// have an unrecognized id accepted as the selection just because it
    /// happens to appear in that collection. <paramref name="fallbackTabId"/>
    /// is caller-controlled rather than external data, so an unknown fallback
    /// is a programming error and throws immediately instead of being
    /// silently sanitized.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="fallbackTabId"/> is not a known tab.
    /// </exception>
    internal static string ResolveSelectedTabId(
        string? selectedTabId,
        IReadOnlyCollection<string> visibleTabIds,
        string fallbackTabId)
    {
        ArgumentNullException.ThrowIfNull(visibleTabIds);
        ArgumentNullException.ThrowIfNull(fallbackTabId);
        if (!IsKnownTab(fallbackTabId))
        {
            throw new ArgumentException(
                $"'{fallbackTabId}' is not a known inspector tab.", nameof(fallbackTabId));
        }

        var knownVisibleTabIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string tabId in visibleTabIds)
        {
            if (IsKnownTab(tabId))
            {
                knownVisibleTabIds.Add(tabId);
            }
        }

        if (selectedTabId is not null &&
            IsKnownTab(selectedTabId) &&
            knownVisibleTabIds.Contains(selectedTabId))
        {
            return selectedTabId;
        }
        if (knownVisibleTabIds.Contains(fallbackTabId))
        {
            return fallbackTabId;
        }
        foreach (ViewerInspectorTabDescriptor tab in Tabs)
        {
            if (tab.Kind == ViewerInspectorTabKind.User && knownVisibleTabIds.Contains(tab.Id))
            {
                return tab.Id;
            }
        }
        foreach (ViewerInspectorTabDescriptor tab in Tabs)
        {
            if (knownVisibleTabIds.Contains(tab.Id))
            {
                return tab.Id;
            }
        }
        return fallbackTabId;
    }
}
