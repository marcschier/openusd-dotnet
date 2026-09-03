// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Options for hosting the viewer shell inside another application. A host supplies the
/// stage to open and, optionally, a callback that runs once that stage is composed and
/// rendering, so the host can author into the viewer's own stage scheduler.
/// </summary>
public sealed class ViewerHostOptions
{
    /// <summary>
    /// Absolute path of the USD stage to open on startup. When <c>null</c> the viewer
    /// starts with no document and the user opens a stage interactively.
    /// </summary>
    public string? StagePath { get; init; }

    /// <summary>
    /// Directory containing the staged USD plugin tree (<c>plugin/usd</c>). Required for
    /// rendering; when <c>null</c> the value of <c>OPENUSD_PLUGIN_PATH</c> is used.
    /// </summary>
    public string? PluginPath { get; init; }

    /// <summary>
    /// Renderer preference: <c>Auto</c>, <c>Storm</c>, <c>D3D12</c>, <c>Vulkan</c>, or
    /// <c>Metal</c>. When <c>null</c> the viewer's own default selection applies.
    /// </summary>
    public string? Renderer { get; init; }

    /// <summary>
    /// Window title. When <c>null</c> the viewer's default title is kept.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Prim path of a <c>UsdGeomCamera</c> in the stage to start on, for example an
    /// overhead camera framing the whole scene. When <c>null</c>, or when the prim is not
    /// a usable camera, the viewer's automatic framing applies.
    /// </summary>
    public string? StageCameraPath { get; init; }

    /// <summary>
    /// Invoked after a viewport click resolves through the viewer-owned picking path.
    /// Misses are reported with <see cref="RenderPickStatus.Miss"/> and a
    /// <see langword="null"/> prim path so hosts can clear their own state.
    /// </summary>
    /// <remarks>
    /// The callback is dispatched off the UI thread and is not awaited by input handling
    /// or rendering.
    /// </remarks>
    public Func<ViewerPickEventArgs, CancellationToken, Task>? PrimPicked { get; init; }

    /// <summary>
    /// Gets the renderer target used for host pick callbacks. Defaults to primitive picks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a fixed concrete request: a host that leaves the default, or that
    /// sets <see cref="RenderPickTarget.Face"/>, gets exactly that target for the
    /// lifetime of the shell, whatever the operator later chooses in
    /// <c>Tools &gt; Pick Target</c>. That is the point of naming one -- a host
    /// that drives selection from prim paths cannot suddenly start receiving face
    /// indices because the user changed a menu item.
    /// </para>
    /// <para>
    /// A host that wants the operator's choice instead sets
    /// <see cref="FollowViewerPickTarget"/>, which is the only way to ask for it.
    /// The mode is a separate property rather than a null target so that stating
    /// the default target stays expressible and stays distinct from stating
    /// nothing.
    /// </para>
    /// </remarks>
    public RenderPickTarget PickTarget { get; init; } = RenderPickTarget.Primitive;

    /// <summary>
    /// Gets whether host pick callbacks follow the operator's own
    /// <c>Tools &gt; Pick Target</c> choice instead of <see cref="PickTarget"/>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>, which keeps the fixed-target
    /// behavior a host has always had. When set, <see cref="PickTarget"/> is
    /// ignored and every callback reports whatever target the operator selected,
    /// including after the operator changes it mid-session.
    /// </remarks>
    public bool FollowViewerPickTarget { get; init; }

    /// <summary>
    /// Invoked when the viewport receives a framework-neutral pointer press inside the
    /// rendered content. Coordinates are already converted to physical pixels.
    /// </summary>
    public Func<ViewerViewportPointerEventArgs, CancellationToken, Task>? ViewportPointerPressed
    {
        get;
        init;
    }

    /// <summary>
    /// Invoked when the viewport receives a framework-neutral pointer move inside the
    /// rendered content. Coordinates are already converted to physical pixels.
    /// </summary>
    public Func<ViewerViewportPointerEventArgs, CancellationToken, Task>? ViewportPointerMoved
    {
        get;
        init;
    }

    /// <summary>
    /// Invoked when the viewport receives a framework-neutral pointer release inside the
    /// rendered content. Coordinates are already converted to physical pixels.
    /// </summary>
    public Func<ViewerViewportPointerEventArgs, CancellationToken, Task>? ViewportPointerReleased
    {
        get;
        init;
    }

    /// <summary>
    /// Invoked when viewer selection changes within <see cref="SelectionChangedPrimSubtree"/>.
    /// </summary>
    /// <remarks>
    /// The subtree filter prevents hosts that animate most of the stage from waking for
    /// unrelated selections. Leave <see cref="SelectionChangedPrimSubtree"/> unset to
    /// observe every selection change.
    /// </remarks>
    public Func<ViewerSelectionChangedEventArgs, CancellationToken, Task>? SelectionChanged
    {
        get;
        init;
    }

    /// <summary>
    /// Absolute prim subtree that scopes <see cref="SelectionChanged"/> notifications.
    /// </summary>
    public string? SelectionChangedPrimSubtree { get; init; }

    /// <summary>
    /// Optional live-bridge connection the host exposes to the operator through
    /// <c>Tools &gt; Connections &gt; Omniverse Bridge</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bridge surface exists only when a host sets this. There is no static registration
    /// and no discovery, so a Viewer that is embedded without one has no bridge menu entry,
    /// no bridge dialog, and no bridge status: opening, rendering, and simulating a local
    /// stage is completely unaffected.
    /// </para>
    /// <para>
    /// The Viewer never learns the transport, endpoint, or credential behind a provider. A
    /// host configures those itself - for example through the optional
    /// <c>OpenUsd.Viewer.Bridge.Grpc</c> package - and the Viewer only ever sees the bounded,
    /// redacted snapshots the provider hands back.
    /// </para>
    /// </remarks>
    public IViewerBridgeConnectionProvider? BridgeConnection { get; init; }

    /// <summary>
    /// Closes the shell when cancelled, so a host that runs the viewport for a bounded
    /// time (or shuts down for its own reasons) does not leave a window behind.
    /// </summary>
    public System.Threading.CancellationToken ShutdownToken { get; init; }

    /// <summary>
    /// Invoked independently of the UI thread after a stage is opened and its render loop
    /// is running. The callback receives the viewer-owned stage session; a host must
    /// author only through <see cref="ViewerStageSession.Scheduler"/> and must never reopen
    /// the stage path, because a second open creates a second native stage identity and
    /// breaks authoring/render synchronisation.
    /// </summary>
    /// <remarks>
    /// The callback may remain active for the document lifetime, including while it
    /// services subscriptions. Closing or replacing the document cancels the supplied
    /// token. Exceptions are surfaced as a viewer error and do not tear down the shell.
    /// </remarks>
    public Func<ViewerStageSession, CancellationToken, Task>? StageReadyAsync { get; init; }
}
