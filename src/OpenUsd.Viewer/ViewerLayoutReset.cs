// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Everything that must agree about colour management at any instant: the committed
/// model the menu shows, the cache key the Viewer believes the authoritative state
/// carries, the transform that state actually carries, and the requests that have not
/// reached any of them yet.
/// </summary>
/// <param name="Committed">The committed choice the menu and the settings file show.</param>
/// <param name="CommittedTransformKey">
/// The cache key of the display transform the Viewer last confirmed in the authoritative
/// state, or <see langword="null"/> when it confirmed none.
/// </param>
/// <param name="StateTransform">
/// The display transform the coordinator's published state carries right now, or
/// <see langword="null"/> when the viewport is untransformed.
/// </param>
/// <param name="PendingRequest">
/// The request whose validation or mutation is still in flight, or
/// <see langword="null"/> when none is. It has committed nothing yet, but it is about to.
/// </param>
/// <param name="DeferredRequest">
/// The request that could not reach the authoritative state and is waiting to be
/// replayed, or <see langword="null"/> when none is waiting.
/// </param>
/// <param name="HasUncommittedGeneration">
/// Whether the request pipeline's newest generation has not reached the authoritative
/// state. This is the pipeline's own account of the same question, and it survives the
/// window clearing its pending marker across a document open.
/// </param>
internal readonly record struct ViewerColorManagementView(
    ViewerColorManagement Committed,
    string? CommittedTransformKey,
    RenderDisplayTransform? StateTransform,
    ViewerColorManagement? PendingRequest = null,
    ViewerColorManagement? DeferredRequest = null,
    bool HasUncommittedGeneration = false)
{
    /// <summary>
    /// Gets whether anything still claims or carries a colour-managed transform.
    /// </summary>
    /// <remarks>
    /// All three are consulted rather than only the model. The image is what the user
    /// sees, so a state that still carries a transform is "active" even when the model
    /// has already disowned it -- that disagreement is exactly the condition a reset must
    /// resolve rather than paper over.
    /// </remarks>
    internal bool HasActiveDisplayTransform =>
        Committed.Enabled ||
        CommittedTransformKey is not null ||
        StateTransform is not null;

    /// <summary>
    /// Gets whether a request exists that has decided nothing yet but still can.
    /// </summary>
    /// <remarks>
    /// The pipeline's generation is consulted as well as the window's own markers,
    /// because a document open clears the pending marker while the validation it belongs
    /// to is still running: the generation is what still remembers that the request
    /// exists.
    /// </remarks>
    internal bool HasOutstandingRequest =>
        PendingRequest is not null ||
        DeferredRequest is not null ||
        HasUncommittedGeneration;

    /// <summary>
    /// Gets whether a reset must drive a clear through the request pipeline.
    /// </summary>
    /// <remarks>
    /// An outstanding request counts even though it has committed nothing: an enable
    /// whose OpenColorIO bake is still running, or one deferred because there was no
    /// coordinator, would otherwise land <em>after</em> the reset finished and colour
    /// manage a viewport the reset just declared clean. Only entering the pipeline can
    /// stop it, because that is what takes the newer generation the older request is then
    /// discarded against -- so the clear is requested even when nothing is committed yet
    /// and there is, at this instant, no transform to remove.
    /// </remarks>
    internal bool RequiresClear => HasActiveDisplayTransform || HasOutstandingRequest;

    /// <summary>Gets whether no transform is claimed, cached, carried, or coming.</summary>
    /// <remarks>
    /// A still-outstanding request only contradicts "cleared" when it would enable a
    /// transform. A deferred <em>clear</em> -- what a reset whose mutation was refused
    /// leaves behind -- is the reset's own request waiting to be replayed onto an image
    /// that already carries nothing, so it does not make the viewport colour managed and
    /// must not be reported as though it had. An uncommitted generation is judged the
    /// same way and for the same reason: after the clear it is either that clear or a
    /// request the clear already superseded, and a superseded request can no longer
    /// commit anything.
    /// </remarks>
    internal bool IsCleared =>
        !HasActiveDisplayTransform &&
        PendingRequest is not { Enabled: true } &&
        DeferredRequest is not { Enabled: true };
}

/// <summary>What a View &gt; Reset Layout run actually did.</summary>
/// <param name="ClearAttempted">
/// Whether anything claimed, carried, or could still produce a colour-managed transform,
/// so a transactional clear was requested before any default was committed.
/// </param>
/// <param name="Cleared">Whether the viewport ended up untransformed.</param>
/// <param name="Applied">The settings profile that was actually applied.</param>
/// <param name="ColorManagement">The colour-management view after the attempt.</param>
internal readonly record struct ViewerLayoutResetOutcome(
    bool ClearAttempted,
    bool Cleared,
    ViewerSettings Applied,
    ViewerColorManagementView ColorManagement)
{
    /// <summary>Gets the status line describing the run.</summary>
    internal string Status => Cleared
        ? "Viewer layout reset to clean defaults."
        : "Viewer layout reset to clean defaults, except colour management: the " +
            "OpenColorIO display transform is still applied to the viewport, so the " +
            "menu keeps claiming it and the reset is retried.";

    /// <summary>
    /// Gets whether the applied profile agrees with the image.
    /// </summary>
    /// <remarks>
    /// A reset that could not clear the transform must leave the committed choice
    /// standing, because a profile that says colour management is off while the viewport
    /// is still colour managed is the drift the whole transactional pipeline exists to
    /// prevent.
    /// </remarks>
    internal bool IsConsistent => Cleared
        ? !Applied.ColorManagement.Enabled && ColorManagement.IsCleared
        : ReferenceEquals(Applied.ColorManagement, ColorManagement.Committed) ||
            Applied.ColorManagement == ColorManagement.Committed;
}

/// <summary>
/// Runs View &gt; Reset Layout: an active colour-managed display transform is cleared
/// through the ordinary request/mutation pipeline <em>before</em> any part of the default
/// profile is committed, and the default colour-management choice is committed only once
/// the coordinator has actually published an untransformed state.
/// </summary>
/// <remarks>
/// <para>
/// Reset Layout used to apply the whole default profile in one synchronous step, which
/// committed the default colour-management model, menu, key, and settings while the
/// coordinator was still presenting an OpenColorIO transform. That unchecked the menu
/// item and disabled the reconciliation poll loop -- the two things that would have
/// noticed -- so the viewport stayed colour managed with nothing left claiming it.
/// </para>
/// <para>
/// The clear therefore goes through the very same pipeline an interactive
/// "Clear OpenColorIO Config" goes through, so a busy document, a cancelled lifetime, or
/// a backend that refuses is handled by the existing deferral semantics: nothing is
/// committed, the previous commit stands, the request is recorded for the next open, and
/// the poll loop stays armed so the repair is attempted again. Only the layout half of
/// the profile is applied in that case, which leaves the menu, the model, the key, and
/// the image still agreeing with each other.
/// </para>
/// <para>
/// The clear is also what supersedes colour management's in-flight work. A request that
/// has not committed anything -- an enable whose OpenColorIO bake is still running, or
/// one deferred because there was no coordinator -- is invisible to the committed model,
/// the cached key, and the state's transform alike, so a reset that consulted only those
/// three would skip the pipeline, report success, and then be contradicted when the older
/// request landed and colour managed the viewport it had just declared clean. Requesting
/// the default clear takes a newer generation, which cancels that request and discards
/// its result, and replaces any deferred one, so nothing older can commit or be replayed
/// afterwards.
/// </para>
/// <para>
/// The orchestration is a free function over three seams rather than window code, so the
/// production path is exercisable against a real request pipeline without constructing a
/// window, a device, or a stage.
/// </para>
/// </remarks>
internal static class ViewerLayoutReset
{
    /// <summary>Runs one reset.</summary>
    /// <param name="defaults">The clean default profile to restore.</param>
    /// <param name="readColorManagement">
    /// Reads the committed colour-management view, including the transform the
    /// authoritative state currently carries. It is read again after the clear, so the
    /// decision is made against what the coordinator published rather than what was
    /// requested.
    /// </param>
    /// <param name="clearColorManagementAsync">
    /// Applies one colour-management request through the transactional pipeline.
    /// </param>
    /// <param name="applySettings">Applies a settings profile to the shell.</param>
    internal static async Task<ViewerLayoutResetOutcome> RunAsync(
        ViewerSettings defaults,
        Func<ViewerColorManagementView> readColorManagement,
        Func<ViewerColorManagement, Task> clearColorManagementAsync,
        Action<ViewerSettings> applySettings)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(readColorManagement);
        ArgumentNullException.ThrowIfNull(clearColorManagementAsync);
        ArgumentNullException.ThrowIfNull(applySettings);

        ViewerColorManagementView before = readColorManagement();
        bool attempted = before.RequiresClear;
        if (attempted)
        {
            // Transactional, and first: the default menu, model, key, and persisted
            // settings may not be committed until the coordinator has published a state
            // without the transform. The request is made even when nothing is committed
            // yet, because entering the pipeline is what supersedes a pending or deferred
            // request that would otherwise commit after this reset reported success.
            await clearColorManagementAsync(defaults.ColorManagement);
        }

        ViewerColorManagementView after = attempted ? readColorManagement() : before;
        bool cleared = after.IsCleared;
        ViewerSettings applied = cleared
            ? defaults
            : defaults with { ColorManagement = after.Committed };
        applySettings(applied);
        return new ViewerLayoutResetOutcome(attempted, cleared, applied, after);
    }
}
