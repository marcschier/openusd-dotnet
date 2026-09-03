// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>
/// The single decision point for whether a developer tab's (Diagnostics, Hydra, TfDebug)
/// background refresh/formatting work may run and whether its refresh command may be
/// activated right now.
/// </summary>
/// <remarks>
/// A hidden developer tab must do no background sampling or refresh work: nothing would be
/// visible, and hiding the tab must fully stop its activity rather than merely disable one
/// entry point while a menu-driven or programmatic path still reaches it. Every guard and
/// every enablement computation for these tabs' refresh commands is expressed through
/// <see cref="IsReachable"/> so the rule cannot drift out of step between the toolbar button,
/// the mirrored Tools menu item, and the method that actually does the work.
/// </remarks>
internal static class ViewerDeveloperTabGate
{
    /// <summary>
    /// Whether a developer tab's refresh work may run or its refresh command may be enabled.
    /// </summary>
    /// <param name="tabVisible">Whether the owning developer tab is currently visible.</param>
    /// <param name="otherwiseAvailable">
    /// Every other condition the command already requires (for example, a coordinator existing
    /// and the document not being busy), independent of tab visibility. Pass
    /// <see langword="true"/> when the command has no other condition.
    /// </param>
    internal static bool IsReachable(bool tabVisible, bool otherwiseAvailable) =>
        tabVisible && otherwiseAvailable;
}
