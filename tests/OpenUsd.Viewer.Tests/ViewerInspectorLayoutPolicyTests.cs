// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Requires the inspector layout policy to classify every tab stably, compute
/// visibility from identities rather than position, and resolve a
/// deterministic selection whenever the previously selected tab disappears.
/// </summary>
public sealed class ViewerInspectorLayoutPolicyTests
{
    [Test]
    public async Task EveryTabIdIsUniqueAndNonEmpty()
    {
        List<string> duplicates = [.. ViewerInspectorLayoutPolicy.Tabs
            .GroupBy(tab => tab.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];
        await Assert.That(duplicates).IsEmpty();

        List<string> empty = [.. ViewerInspectorLayoutPolicy.Tabs
            .Where(tab => string.IsNullOrWhiteSpace(tab.Id))
            .Select(tab => tab.Id)];
        await Assert.That(empty).IsEmpty();
    }

    [Test]
    public async Task EveryTabIdIsLowercase()
    {
        List<string> notLowercase = [.. ViewerInspectorLayoutPolicy.Tabs
            .Select(tab => tab.Id)
            .Where(id => !string.Equals(id, id.ToLowerInvariant(), StringComparison.Ordinal))];

        await Assert.That(notLowercase)
            .IsEmpty()
            .Because(
                "stable tab ids must stay lowercase so they read the same as " +
                "settings keys and automation ids: " + string.Join(", ", notLowercase));
    }

    [Test]
    public async Task TfDebugTabIdIsLowercase()
    {
        await Assert.That(ViewerInspectorLayoutPolicy.TfDebugTabId).IsEqualTo("tfdebug");
    }

    [Test]
    public async Task EveryTabClassifiesStably()
    {
        // The public attribute-driven [Arguments] form cannot carry the internal
        // ViewerInspectorTabKind across the test assembly boundary, so the
        // expected table lives in the test body instead of the attribute list.
        Dictionary<string, ViewerInspectorTabKind> expectedKindById = new(StringComparer.Ordinal)
        {
            [ViewerInspectorLayoutPolicy.PropertiesTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.ValueTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.MetadataTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.CompositionTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.LayersTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.ValidationTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.PhysicsTabId] = ViewerInspectorTabKind.User,
            [ViewerInspectorLayoutPolicy.DiagnosticsTabId] = ViewerInspectorTabKind.Developer,
            [ViewerInspectorLayoutPolicy.HydraTabId] = ViewerInspectorTabKind.Developer,
            [ViewerInspectorLayoutPolicy.TfDebugTabId] = ViewerInspectorTabKind.Developer,
        };

        // Non-vacuity: covers every tab the policy declares, not a hand-picked subset.
        await Assert.That(expectedKindById.Count).IsEqualTo(ViewerInspectorLayoutPolicy.Tabs.Count);

        foreach ((string tabId, ViewerInspectorTabKind expected) in expectedKindById)
        {
            await Assert.That(ViewerInspectorLayoutPolicy.IsKnownTab(tabId)).IsTrue();
            await Assert.That(ViewerInspectorLayoutPolicy.KindOf(tabId)).IsEqualTo(expected);
            await Assert.That(ViewerInspectorLayoutPolicy.IsDeveloperTab(tabId))
                .IsEqualTo(expected == ViewerInspectorTabKind.Developer);
        }
    }

    [Test]
    public async Task ExactlyTenTabsExistAndAllAreClassified()
    {
        await Assert.That(ViewerInspectorLayoutPolicy.Tabs.Count).IsEqualTo(10);

        int developerCount = ViewerInspectorLayoutPolicy.Tabs
            .Count(tab => tab.Kind == ViewerInspectorTabKind.Developer);
        int userCount = ViewerInspectorLayoutPolicy.Tabs
            .Count(tab => tab.Kind == ViewerInspectorTabKind.User);
        await Assert.That(developerCount).IsEqualTo(3);
        await Assert.That(userCount).IsEqualTo(7);
    }

    [Test]
    public async Task CleanDefaultHiddenTabIdsAreExactlyTheDeveloperTabs()
    {
        HashSet<string> developerTabIds = [.. ViewerInspectorLayoutPolicy.Tabs
            .Where(tab => tab.Kind == ViewerInspectorTabKind.Developer)
            .Select(tab => tab.Id)];

        await Assert.That(ViewerInspectorLayoutPolicy.CleanDefaultHiddenTabIds.Count)
            .IsEqualTo(developerTabIds.Count);
        foreach (string hiddenTabId in ViewerInspectorLayoutPolicy.CleanDefaultHiddenTabIds)
        {
            await Assert.That(developerTabIds.Contains(hiddenTabId)).IsTrue();
        }
    }

    [Test]
    public async Task ComputeVisibleTabIdsExcludesOnlyTheHiddenSet()
    {
        HashSet<string> hidden = [ViewerInspectorLayoutPolicy.DiagnosticsTabId];
        IReadOnlyList<string> visible = ViewerInspectorLayoutPolicy.ComputeVisibleTabIds(hidden);

        await Assert.That(visible.Contains(ViewerInspectorLayoutPolicy.DiagnosticsTabId)).IsFalse();
        await Assert.That(visible.Count)
            .IsEqualTo(ViewerInspectorLayoutPolicy.Tabs.Count - 1);
        foreach (ViewerInspectorTabDescriptor tab in ViewerInspectorLayoutPolicy.Tabs)
        {
            if (tab.Id != ViewerInspectorLayoutPolicy.DiagnosticsTabId)
            {
                await Assert.That(visible.Contains(tab.Id)).IsTrue();
            }
        }
    }

    [Test]
    public async Task ComputeVisibleTabIdsWithNoHiddenTabsReturnsEveryTab()
    {
        IReadOnlyList<string> visible =
            ViewerInspectorLayoutPolicy.ComputeVisibleTabIds([]);
        await Assert.That(visible.Count).IsEqualTo(ViewerInspectorLayoutPolicy.Tabs.Count);
    }

    [Test]
    public async Task SelectedTabIsKeptWhenStillVisible()
    {
        IReadOnlyList<string> visible = ViewerInspectorLayoutPolicy.ComputeVisibleTabIds([]);
        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            ViewerInspectorLayoutPolicy.CompositionTabId,
            visible,
            ViewerInspectorLayoutPolicy.PropertiesTabId);
        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.CompositionTabId);
    }

    [Test]
    public async Task SelectionFallsBackWhenTheSelectedTabIsHidden()
    {
        HashSet<string> hidden = [ViewerInspectorLayoutPolicy.DiagnosticsTabId];
        IReadOnlyList<string> visible = ViewerInspectorLayoutPolicy.ComputeVisibleTabIds(hidden);

        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: ViewerInspectorLayoutPolicy.DiagnosticsTabId,
            visibleTabIds: visible,
            fallbackTabId: ViewerInspectorLayoutPolicy.ValidationTabId);

        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.ValidationTabId);
    }

    [Test]
    public async Task SelectionFallsBackToTheCleanDefaultWhenConfiguredToDoSo()
    {
        HashSet<string> hidden = [.. ViewerInspectorLayoutPolicy.CleanDefaultHiddenTabIds];
        IReadOnlyList<string> visible = ViewerInspectorLayoutPolicy.ComputeVisibleTabIds(hidden);

        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: ViewerInspectorLayoutPolicy.DiagnosticsTabId,
            visibleTabIds: visible,
            fallbackTabId: ViewerInspectorLayoutPolicy.CleanDefaultFallbackTabId);

        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.PropertiesTabId);
    }

    [Test]
    public async Task SelectionFallsBackToTheFirstVisibleUserTabWhenTheFallbackIsAlsoHidden()
    {
        // Hide every tab except Composition, including the requested fallback
        // (Properties). The resolver must still return something visible and
        // must not depend on where Composition sits in the visual tab order.
        HashSet<string> hidden = [.. ViewerInspectorLayoutPolicy.Tabs
            .Select(tab => tab.Id)
            .Where(id => id != ViewerInspectorLayoutPolicy.CompositionTabId)];
        IReadOnlyList<string> visible = ViewerInspectorLayoutPolicy.ComputeVisibleTabIds(hidden);

        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: ViewerInspectorLayoutPolicy.DiagnosticsTabId,
            visibleTabIds: visible,
            fallbackTabId: ViewerInspectorLayoutPolicy.PropertiesTabId);

        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.CompositionTabId);
    }

    [Test]
    public async Task SelectionIsDeterministicAcrossRepeatedResolutionWithTheSameInputs()
    {
        HashSet<string> hidden = [.. ViewerInspectorLayoutPolicy.CleanDefaultHiddenTabIds];
        IReadOnlyList<string> visible = ViewerInspectorLayoutPolicy.ComputeVisibleTabIds(hidden);

        string first = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            ViewerInspectorLayoutPolicy.TfDebugTabId,
            visible,
            ViewerInspectorLayoutPolicy.CleanDefaultFallbackTabId);
        string second = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            ViewerInspectorLayoutPolicy.TfDebugTabId,
            visible,
            ViewerInspectorLayoutPolicy.CleanDefaultFallbackTabId);

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task SelectionFallsBackToTheFallbackWhenNothingAtAllIsVisible()
    {
        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: ViewerInspectorLayoutPolicy.DiagnosticsTabId,
            visibleTabIds: [],
            fallbackTabId: ViewerInspectorLayoutPolicy.PropertiesTabId);

        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.PropertiesTabId);
    }

    [Test]
    public async Task NoSelectionResolvesTheSameAsAHiddenSelection()
    {
        IReadOnlyList<string> visible =
            ViewerInspectorLayoutPolicy.ComputeVisibleTabIds(
                [ViewerInspectorLayoutPolicy.DiagnosticsTabId]);

        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: null,
            visibleTabIds: visible,
            fallbackTabId: ViewerInspectorLayoutPolicy.ValidationTabId);

        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.ValidationTabId);
    }

    [Test]
    public async Task AnUnknownSelectedIdIsNeverAcceptedEvenIfPresentInTheVisibleSet()
    {
        // Bypasses ComputeVisibleTabIds so the raw collection can carry an id
        // the policy has never heard of, exactly as a stale or forward-versioned
        // settings file might. The resolver must not trust it just because it
        // is "present" in the caller-supplied visible set.
        List<string> visibleWithGarbage =
            [.. ViewerInspectorLayoutPolicy.ComputeVisibleTabIds([]), "bogus-tab"];

        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: "bogus-tab",
            visibleTabIds: visibleWithGarbage,
            fallbackTabId: ViewerInspectorLayoutPolicy.PropertiesTabId);

        await Assert.That(resolved).IsNotEqualTo("bogus-tab");
        await Assert.That(ViewerInspectorLayoutPolicy.IsKnownTab(resolved)).IsTrue();
    }

    [Test]
    public async Task UnknownEntriesInTheVisibleSetAreIgnoredRatherThanTreatedAsVisible()
    {
        // Only an unrecognized id is "visible" here; every real tab is absent.
        // The resolver must still land on the fallback deterministically
        // instead of mistaking the garbage entry for a valid candidate.
        string resolved = ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
            selectedTabId: ViewerInspectorLayoutPolicy.DiagnosticsTabId,
            visibleTabIds: ["bogus-tab", "also-bogus"],
            fallbackTabId: ViewerInspectorLayoutPolicy.PropertiesTabId);

        await Assert.That(resolved).IsEqualTo(ViewerInspectorLayoutPolicy.PropertiesTabId);
    }

    [Test]
    public async Task ResolveSelectedTabIdThrowsForAnUnknownFallback()
    {
        await Assert.That(() => ViewerInspectorLayoutPolicy.ResolveSelectedTabId(
                selectedTabId: ViewerInspectorLayoutPolicy.PropertiesTabId,
                visibleTabIds: ViewerInspectorLayoutPolicy.ComputeVisibleTabIds([]),
                fallbackTabId: "not-a-real-tab"))
            .Throws<ArgumentException>();
    }
}
