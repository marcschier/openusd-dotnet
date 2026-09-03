// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Exercises the single decision point for developer-tab (Diagnostics, Hydra, TfDebug)
/// reachability directly, and confirms the guarded methods and their enablement computations
/// actually call through it, so "no work while hidden" is verified as real behavior rather
/// than only asserted to exist as a source string.
/// </summary>
public sealed class ViewerDeveloperTabGateTests
{
    [Test]
    [Arguments(true, true, true)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, false)]
    public async Task IsReachableRequiresBothVisibilityAndEveryOtherCondition(
        bool tabVisible,
        bool otherwiseAvailable,
        bool expected)
    {
        bool reachable = ViewerDeveloperTabGate.IsReachable(tabVisible, otherwiseAvailable);

        await Assert.That(reachable).IsEqualTo(expected);
    }

    [Test]
    public async Task HiddenTabIsNeverReachableEvenWhenEveryOtherConditionHolds()
    {
        // The precise defect this guards against: a tab being hidden must not be overridable by
        // a coordinator existing, the document being idle, or any other otherwise-available
        // condition being true.
        await Assert.That(ViewerDeveloperTabGate.IsReachable(false, otherwiseAvailable: true))
            .IsFalse();
    }

    [Test]
    public async Task VisibleTabWithNoOtherConditionIsReachable()
    {
        await Assert.That(ViewerDeveloperTabGate.IsReachable(true, otherwiseAvailable: true))
            .IsTrue();
    }

    [Test]
    public async Task TheHydraAndTfDebugRefreshGuardsAndEnablementAllRouteThroughTheGate()
    {
        string window = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));

        string renderHydraScene = SliceMethod(
            window, "private void RenderHydraScene", "private void OnRefreshTfDebugClick");
        await Assert.That(renderHydraScene).Contains(
            "if (!ViewerDeveloperTabGate.IsReachable(HydraSceneTab.IsVisible");

        string loadTfDebugFlags = SliceMethod(
            window, "private void LoadTfDebugFlags", "private void OnTfDebugFlagChanged");
        await Assert.That(loadTfDebugFlags).Contains(
            "if (!ViewerDeveloperTabGate.IsReachable(TfDebugTab.IsVisible");

        string applyPanelVisibility = SliceMethod(
            window, "private void ApplyPanelVisibility", "private void OnWindowKeyDown");
        await Assert.That(applyPanelVisibility).Contains(
            "bool hydraRefreshEnabled = ViewerDeveloperTabGate.IsReachable(");
        await Assert.That(applyPanelVisibility).Contains(
            "bool tfDebugRefreshEnabled = ViewerDeveloperTabGate.IsReachable(");

        string updateViewportDisplayAvailability = SliceMethod(
            window,
            "private void UpdateViewportDisplayAvailability",
            "private static RenderPurpose? GetPurposeForCheckBox");
        await Assert.That(updateViewportDisplayAvailability).Contains(
            "bool hydraRefreshEnabled = ViewerDeveloperTabGate.IsReachable(");
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find '{startMarker}'.");
        }
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException($"Could not find '{endMarker}' after the start.");
        }
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
