// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

/// <summary>
/// How the Viewer composites the outline of a selected prim, instance, or
/// subprim.
/// </summary>
internal enum ViewerSelectionMode
{
    /// <summary>
    /// Only unoccluded selected fragments are outlined, which is the
    /// depth-tested behaviour the Viewer has always had.
    /// </summary>
    VisibleOnly,

    /// <summary>
    /// Occluded selected fragments are outlined too, in a distinct style, so a
    /// selection behind geometry stays locatable. The visible part keeps
    /// exactly the visible-only outline.
    /// </summary>
    XRay
}

/// <summary>
/// The one selection-outline policy every hdSilk presentation renderer in the
/// Viewer composites with.
/// </summary>
/// <remarks>
/// <para>
/// It is process-wide state for the same reason
/// <see cref="ViewerStartupOptions"/> is: the Viewer runs one window, the three
/// presentation renderers are constructed independently of it by the backend
/// host, and all of them must composite the same selection style or a capture
/// would be evidence of an image the user never saw.
/// </para>
/// <para>
/// Reads and writes are volatile because the UI thread sets the policy while a
/// render thread reads it. The settings object is immutable, so a reader sees
/// either the previous policy or the new one and never a half-applied mixture.
/// </para>
/// </remarks>
internal static class ViewerSelectionOutlinePolicy
{
    private static SilkSelectionOutlineSettings _current =
        SilkSelectionOutlineSettings.Default;

    /// <summary>Gets or sets the applied selection-outline policy.</summary>
    internal static SilkSelectionOutlineSettings Current
    {
        get => Volatile.Read(ref _current);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Volatile.Write(ref _current, value);
        }
    }

    /// <summary>Restores the shared default, which is the visible-only policy.</summary>
    internal static void Reset() => Current = SilkSelectionOutlineSettings.Default;

    /// <summary>Applies one selection mode, leaving every other property alone.</summary>
    internal static void SetMode(ViewerSelectionMode mode)
    {
        SilkSelectionOutlineSettings current = Current;
        Current = new SilkSelectionOutlineSettings(
            current.Enabled,
            current.Color,
            current.Width,
            mode == ViewerSelectionMode.XRay
                ? SilkSelectionOutlineMode.XRay
                : SilkSelectionOutlineMode.VisibleOnly,
            current.OccludedColor);
    }
}

/// <summary>
/// Maps the Viewer's Tools-menu pick-target and selection-mode controls onto
/// stable command identities, persisted tokens, and renderer-neutral values.
/// </summary>
/// <remarks>
/// <para>
/// The command identities are the contract the accessibility surface, the
/// shortcut catalog, and the settings file all key on, so they are defined once
/// here rather than derived from a menu item's position or its label. A stable
/// identity is what lets a persisted profile survive a menu reordering and a
/// screen reader announce the same control after a relabelling.
/// </para>
/// <para>
/// Both controls live in the Tools menu only. Neither is promoted to the
/// toolbar: they change what a later click means rather than performing an
/// action, and a toolbar full of modal state is exactly the clutter the Viewer's
/// menu-first policy exists to avoid.
/// </para>
/// </remarks>
internal static class ViewerPickTargetPolicy
{
    /// <summary>The pick target a profile with no persisted value uses.</summary>
    internal const RenderPickTarget DefaultTarget = RenderPickTarget.Primitive;

    /// <summary>The selection mode a profile with no persisted value uses.</summary>
    internal const ViewerSelectionMode DefaultSelectionMode =
        ViewerSelectionMode.VisibleOnly;

    /// <summary>Gets the persisted token of one pick target.</summary>
    internal static string ToToken(RenderPickTarget target) => target switch
    {
        RenderPickTarget.Face => "face",
        RenderPickTarget.Edge => "edge",
        RenderPickTarget.Point => "point",
        _ => "primitive"
    };

    /// <summary>
    /// Resolves a persisted pick-target token, falling back to the default for a
    /// token this build does not know.
    /// </summary>
    /// <remarks>
    /// An unknown token is a forward-compatibility case rather than corruption:
    /// a profile written by a later build may name a target this one cannot
    /// answer, and silently falling back to the prim target is both safe and
    /// reversible, where refusing the whole profile would discard the user's
    /// unrelated layout.
    /// </remarks>
    internal static RenderPickTarget FromToken(string? token) => token switch
    {
        "face" => RenderPickTarget.Face,
        "edge" => RenderPickTarget.Edge,
        "point" => RenderPickTarget.Point,
        _ => DefaultTarget
    };

    /// <summary>Gets the persisted token of one selection mode.</summary>
    internal static string ToToken(ViewerSelectionMode mode) =>
        mode == ViewerSelectionMode.XRay ? "xray" : "visibleOnly";

    /// <summary>
    /// Names one selection element kind for a status line, so a reported index
    /// is never an anonymous "subprim" number the user cannot interpret.
    /// </summary>
    internal static string DescribeElementKind(SelectionElementKind kind) => kind switch
    {
        SelectionElementKind.Face => "face",
        SelectionElementKind.Edge => "edge",
        SelectionElementKind.Point => "point",
        _ => "subprim"
    };

    /// <summary>Resolves a persisted selection-mode token.</summary>
    internal static ViewerSelectionMode SelectionModeFromToken(string? token) =>
        string.Equals(token, "xray", StringComparison.Ordinal)
            ? ViewerSelectionMode.XRay
            : DefaultSelectionMode;

    /// <summary>
    /// Resolves the target one click actually requests from the fixed choice a
    /// host made and the operator's Tools-menu choice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A host that named a target is making a concrete request that must survive
    /// every Tools-menu change for the lifetime of the shell: a host driving its
    /// own selection from prim paths cannot start receiving face indices because
    /// the operator opened a menu. A host that opted into the follow-viewer mode
    /// is asking to follow the operator instead, and so is a standalone Viewer
    /// with no embedding host at all -- there the Tools menu is the only request
    /// that exists, so treating its absence of a host as a fixed primitive
    /// request would make the menu inert.
    /// </para>
    /// <para>
    /// The cases are distinguished by an explicit mode rather than by comparing
    /// against <see cref="RenderPickTarget.Primitive"/> or by a null target.
    /// Comparing made an explicit primitive request indistinguishable from no
    /// request at all, so the one host that stated the default was the only host
    /// whose choice was ignored; a null target made the default target
    /// inexpressible and broke every existing source that assigned one.
    /// <see cref="ViewerStartupOptions.ResolveRequestedPickTarget"/> is the
    /// production seam that supplies the mode, and it is set by which initializer
    /// ran rather than by inspecting the target.
    /// </para>
    /// </remarks>
    internal static RenderPickTarget ResolveHostRequestedTarget(
        bool followViewer,
        RenderPickTarget hostTarget,
        RenderPickTarget menuTarget) =>
        followViewer ? menuTarget : hostTarget;

    /// <summary>Gets the stable command identity of one pick target.</summary>
    internal static string ToCommandId(RenderPickTarget target) => target switch
    {
        RenderPickTarget.Face => ViewerCommandIds.ToolsPickTargetFace,
        RenderPickTarget.Edge => ViewerCommandIds.ToolsPickTargetEdge,
        RenderPickTarget.Point => ViewerCommandIds.ToolsPickTargetPoint,
        _ => ViewerCommandIds.ToolsPickTargetPrimitive
    };

    /// <summary>Gets the pick target one stable command identity selects.</summary>
    internal static RenderPickTarget FromCommandId(string commandId) => commandId switch
    {
        ViewerCommandIds.ToolsPickTargetFace => RenderPickTarget.Face,
        ViewerCommandIds.ToolsPickTargetEdge => RenderPickTarget.Edge,
        ViewerCommandIds.ToolsPickTargetPoint => RenderPickTarget.Point,
        _ => RenderPickTarget.Primitive
    };

    /// <summary>Gets the stable command identity of one selection mode.</summary>
    internal static string ToCommandId(ViewerSelectionMode mode) =>
        mode == ViewerSelectionMode.XRay
            ? ViewerCommandIds.ToolsSelectionXRay
            : ViewerCommandIds.ToolsSelectionVisibleOnly;

    /// <summary>Gets the selection mode one stable command identity selects.</summary>
    internal static ViewerSelectionMode SelectionModeFromCommandId(string commandId) =>
        string.Equals(commandId, ViewerCommandIds.ToolsSelectionXRay, StringComparison.Ordinal)
            ? ViewerSelectionMode.XRay
            : ViewerSelectionMode.VisibleOnly;

    /// <summary>
    /// Describes what a backend can answer for one pick target, so an
    /// unsupported combination is stated rather than silently ignored.
    /// </summary>
    /// <remarks>
    /// The Storm backend answers prim picks only, so the subprim targets are
    /// disabled rather than hidden when it is selected: a hidden control makes a
    /// capability difference look like a missing feature, while a disabled one
    /// with an explanatory accessible name says which backend would answer it.
    /// </remarks>
    internal static bool SupportsTarget(
        RenderBackendKind backend,
        RenderPickTarget target) =>
        target == RenderPickTarget.Primitive ||
        backend is RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or
            RenderBackendKind.Metal;

    /// <summary>
    /// Whether one backend composites occluded selection outlines.
    /// </summary>
    internal static bool SupportsSelectionMode(
        RenderBackendKind backend,
        ViewerSelectionMode mode) =>
        mode == ViewerSelectionMode.VisibleOnly ||
        backend is RenderBackendKind.D3D12 or
            RenderBackendKind.Vulkan or
            RenderBackendKind.Metal;

    /// <summary>
    /// Explains why a backend refuses one pick target, for the disabled
    /// control's accessible name and for the diagnostics surface.
    /// </summary>
    internal static string DescribeUnsupportedTarget(
        RenderBackendKind backend,
        RenderPickTarget target) =>
        $"{target} picking is not supported by the {backend} backend; " +
        "use the D3D12, Vulkan, or Metal renderer.";

    /// <summary>Explains why a backend refuses x-ray selection.</summary>
    internal static string DescribeUnsupportedSelectionMode(
        RenderBackendKind backend,
        ViewerSelectionMode mode) =>
        $"{mode} selection is not supported by the {backend} backend; " +
        "use the D3D12, Vulkan, or Metal renderer.";

    /// <summary>
    /// Resolves the pick target one backend actually answers for a desired one.
    /// </summary>
    /// <remarks>
    /// The desired target is what the user asked for and what the profile
    /// stores; the effective one is what the attached backend can answer. They
    /// have to stay separate so a Viewer that starts on Storm, or that has no
    /// backend attached yet, does not write its restrictive fallback back over
    /// a saved edge- or point-picking profile. Switching to a capable backend
    /// re-resolves the desired value without the user asking twice.
    /// </remarks>
    internal static RenderPickTarget ResolveEffectiveTarget(
        RenderBackendKind backend,
        RenderPickTarget desired) =>
        SupportsTarget(backend, desired) ? desired : DefaultTarget;

    /// <summary>
    /// Resolves the selection mode one backend actually composites for a desired
    /// one, on the same terms as <see cref="ResolveEffectiveTarget"/>.
    /// </summary>
    internal static ViewerSelectionMode ResolveEffectiveSelectionMode(
        RenderBackendKind backend,
        ViewerSelectionMode desired) =>
        SupportsSelectionMode(backend, desired) ? desired : DefaultSelectionMode;
}
