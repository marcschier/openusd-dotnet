// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

/// <summary>What the Viewer may honestly claim about colour management right now.</summary>
internal enum ViewerColorManagementState
{
    /// <summary>No colour-managed display transform is requested.</summary>
    Disabled,

    /// <summary>The requested transform is running.</summary>
    Active,

    /// <summary>The requested transform was refused and must not be claimed.</summary>
    Failed,

    /// <summary>
    /// A request is still being validated or applied, so nothing may be concluded.
    /// </summary>
    Pending,
}

/// <summary>The decision produced by reconciling the request with the renderer.</summary>
/// <param name="State">What the Viewer may claim.</param>
/// <param name="Enabled">Whether the toggle should stay on.</param>
/// <param name="ClearTransform">
/// Whether the display transform must be removed from the authoritative render state.
/// </param>
/// <param name="Status">The message to report, or <see langword="null"/> for none.</param>
internal readonly record struct ViewerColorManagementSyncResult(
    ViewerColorManagementState State,
    bool Enabled,
    bool ClearTransform,
    string? Status);

/// <summary>
/// Reconciles what colour management was asked for with what the renderer actually did.
/// </summary>
/// <remarks>
/// <para>
/// A viewer that leaves its "use OpenColorIO display transform" item checked while the
/// renderer has fallen back to untransformed colour is lying about the image on screen,
/// which is exactly the plausible-but-wrong outcome this profile exists to prevent. This
/// is a pure function so the rule is testable without constructing a window, a device, or
/// a stage.
/// </para>
/// <para>
/// Diagnostics are cumulative and are read asynchronously, so they can describe a request
/// the Viewer has already replaced. Every decision is therefore correlated: the renderer
/// publishes the cache key of the transform it evaluated, and a report whose key is not
/// the committed one is ignored rather than acted on. Without that, a slow failure for
/// config A observed after config B succeeded would disable a transform that is running
/// correctly.
/// </para>
/// </remarks>
internal static class ViewerColorManagementSync
{
    /// <summary>Reconciles a backend report against the committed request.</summary>
    /// <param name="requestedEnabled">The committed enabled choice.</param>
    /// <param name="committedRequestKey">
    /// The cache key of the display transform in the authoritative render state, or
    /// <see langword="null"/> when it carries none.
    /// </param>
    /// <param name="hasPendingRequest">
    /// Whether a colour-management request is still being validated or applied. While
    /// one is, the committed state is in motion and no report may be acted on.
    /// </param>
    /// <param name="backendStatus">The renderer's most recent status.</param>
    /// <param name="backendRequestKey">The request that status describes.</param>
    /// <param name="diagnostic">The renderer's bounded reason, if any.</param>
    internal static ViewerColorManagementSyncResult Compute(
        bool requestedEnabled,
        string? committedRequestKey,
        bool hasPendingRequest,
        SilkDisplayTransformStatus? backendStatus,
        string? backendRequestKey,
        RenderDiagnostic? diagnostic)
    {
        if (hasPendingRequest)
        {
            // A selection is in flight. It is not yet the committed state, and the
            // renderer is still reporting on whatever preceded it.
            return new ViewerColorManagementSyncResult(
                ViewerColorManagementState.Pending,
                requestedEnabled,
                ClearTransform: false,
                Status: null);
        }

        if (!requestedEnabled)
        {
            // Disabled is only honest once the transform is actually gone from the
            // authoritative state, so a leftover one is cleared rather than tolerated.
            return new ViewerColorManagementSyncResult(
                ViewerColorManagementState.Disabled,
                Enabled: false,
                ClearTransform: committedRequestKey is not null,
                Status: null);
        }

        if (backendRequestKey is not null &&
            committedRequestKey is not null &&
            !string.Equals(backendRequestKey, committedRequestKey, StringComparison.Ordinal))
        {
            // The renderer is still describing a superseded request. Reporting it would
            // disable a transform that has not been evaluated yet.
            return new ViewerColorManagementSyncResult(
                ViewerColorManagementState.Pending,
                Enabled: true,
                ClearTransform: false,
                Status: null);
        }

        switch (backendStatus)
        {
            case null:
            case SilkDisplayTransformStatus.Inactive:
                // The renderer has not yet been asked, or there is no backend to ask.
                // Silence here is not a claim: the request stands and the next frame
                // reports what actually happened.
                return new ViewerColorManagementSyncResult(
                    ViewerColorManagementState.Active,
                    Enabled: true,
                    ClearTransform: false,
                    Status: null);
            case SilkDisplayTransformStatus.Applied:
                return new ViewerColorManagementSyncResult(
                    ViewerColorManagementState.Active,
                    Enabled: true,
                    ClearTransform: false,
                    Status: null);
            default:
                return new ViewerColorManagementSyncResult(
                    ViewerColorManagementState.Failed,
                    Enabled: false,
                    ClearTransform: true,
                    Status: diagnostic?.Message ??
                        "The colour-managed display transform was not applied, so the " +
                        "viewport shows untransformed colour.");
        }
    }
}
