// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Describes a viewport pick resolved by the viewer's own input, DPI, render-state,
/// and stale-retry path.
/// </summary>
public sealed class ViewerPickEventArgs : EventArgs
{
    internal ViewerPickEventArgs(RenderPickResult result)
    {
        Item = result.Status == RenderPickStatus.Hit ? result.Item : null;
        PrimPath = result.Status == RenderPickStatus.Hit ? result.PrimPath : null;
        InstancerPath = result.InstancerPath;
        InstanceIndex = result.InstanceIndex;
        ElementIndex = result.ElementIndex;
        ElementKind = Item?.ElementKind ?? SelectionElementKind.None;
        WorldPosition = result.WorldPosition;
        WorldNormal = result.WorldNormal;
        Status = result.Status;
        StaleReasons = result.StaleReasons;
        RequestedTarget = result.Request.Target;
        PixelX = result.Request.X;
        PixelY = result.Request.Y;
        Viewport = result.Request.Viewport;
        StateRevision = result.StateRevision;
        SceneRevision = result.SceneRevision;
    }

    /// <summary>
    /// Gets the complete resolved selection identity, or <see langword="null"/>
    /// for a non-hit result.
    /// </summary>
    /// <remarks>
    /// This is the only place the full identity survives. The flattened
    /// properties below are convenience views of it, and two of them are lossy
    /// by construction: <see cref="InstancerPath"/> and
    /// <see cref="InstanceIndex"/> report the innermost instancing level only,
    /// so a hit inside a nested instancer describes one level of a chain the
    /// host cannot reconstruct from them. A host that cares about nested
    /// instancing must read <see cref="SelectionItem.InstancerContext"/> here,
    /// which is ordered outermost to innermost and is the only complete
    /// description of the instance.
    /// </remarks>
    public SelectionItem? Item { get; }

    /// <summary>
    /// Gets the scene element the pick was requested for.
    /// </summary>
    /// <remarks>
    /// A host that fixed its own target through
    /// <see cref="ViewerHostOptions.PickTarget"/> sees exactly that value on
    /// every callback, whatever the operator later chooses in the Tools menu. A
    /// host that set <see cref="ViewerHostOptions.FollowViewerPickTarget"/> sees
    /// the operator's current choice, which is what makes the two modes
    /// distinguishable from inside the callback.
    /// </remarks>
    public RenderPickTarget RequestedTarget { get; }

    /// <summary>Gets the hit prim path, or <see langword="null"/> for non-hit results.</summary>
    public string? PrimPath { get; }

    /// <summary>
    /// Gets the innermost hit instancer path, when the hit resolves an instance.
    /// </summary>
    /// <remarks>
    /// This is a convenience view of the last <see cref="Item"/> instancing
    /// level. For the overwhelmingly common single-level scene it is the whole
    /// truth; for a nested instancer it names one level of a chain, and the
    /// outer levels exist only in <see cref="SelectionItem.InstancerContext"/>.
    /// </remarks>
    public string? InstancerPath { get; }

    /// <summary>
    /// Gets the zero-based hit instance index inside <see cref="InstancerPath"/>,
    /// when available.
    /// </summary>
    /// <remarks>
    /// It is the innermost level's own index, which is what
    /// <see cref="InstancerPath"/> names. It is deliberately not a composed
    /// mixed-radix ordinal across the chain: an index from one level reported
    /// beside a path from another describes an instance that does not exist.
    /// </remarks>
    public int? InstanceIndex { get; }

    /// <summary>Gets the zero-based hit subprim element index, when available.</summary>
    public int? ElementIndex { get; }

    /// <summary>
    /// Gets what <see cref="ElementIndex"/> identifies, so a bare index never
    /// reaches a host without the kind that interprets it.
    /// </summary>
    public SelectionElementKind ElementKind { get; }

    /// <summary>Gets the world-space hit point, when the backend provides one.</summary>
    public Vector3? WorldPosition { get; }

    /// <summary>Gets the world-space hit normal, when the backend provides one.</summary>
    public Vector3? WorldNormal { get; }

    /// <summary>Gets the pick result status.</summary>
    public RenderPickStatus Status { get; }

    /// <summary>Gets stale reasons for stale picks, or none otherwise.</summary>
    public RenderPickStaleReason StaleReasons { get; }

    /// <summary>Gets the physical-pixel X coordinate used for picking.</summary>
    public int PixelX { get; }

    /// <summary>Gets the physical-pixel Y coordinate used for picking.</summary>
    public int PixelY { get; }

    /// <summary>Gets the viewport dimensions used by the pick request.</summary>
    public ViewportDimensions Viewport { get; }

    /// <summary>Gets the render-state revision answered by the backend.</summary>
    public ulong StateRevision { get; }

    /// <summary>Gets the scene revision answered by the backend, when available.</summary>
    public ulong? SceneRevision { get; }
}

/// <summary>
/// Describes a framework-neutral viewport pointer event in physical pixels.
/// </summary>
public sealed class ViewerViewportPointerEventArgs : EventArgs
{
    internal ViewerViewportPointerEventArgs(
        int pixelX,
        int pixelY,
        ViewportDimensions viewport,
        ViewerPointerButtons buttons,
        ViewerInputModifiers modifiers)
    {
        PixelX = pixelX;
        PixelY = pixelY;
        Viewport = viewport;
        Buttons = buttons;
        Modifiers = modifiers;
    }

    /// <summary>Gets the physical-pixel X coordinate inside the viewport.</summary>
    public int PixelX { get; }

    /// <summary>Gets the physical-pixel Y coordinate inside the viewport.</summary>
    public int PixelY { get; }

    /// <summary>Gets the viewport dimensions associated with the pointer event.</summary>
    public ViewportDimensions Viewport { get; }

    /// <summary>Gets the pressed pointer buttons.</summary>
    public ViewerPointerButtons Buttons { get; }

    /// <summary>Gets the active keyboard modifiers.</summary>
    public ViewerInputModifiers Modifiers { get; }
}

/// <summary>
/// Describes a viewer selection change delivered to an embedding host.
/// </summary>
public sealed class ViewerSelectionChangedEventArgs : EventArgs
{
    internal ViewerSelectionChangedEventArgs(IReadOnlyList<string> primPaths)
    {
        PrimPaths = primPaths;
    }

    /// <summary>Gets selected prim paths in stable viewer selection order.</summary>
    public IReadOnlyList<string> PrimPaths { get; }
}

/// <summary>
/// Describes the viewport camera and the viewport dimensions its projection assumes.
/// </summary>
public readonly record struct ViewerCameraState(
    CameraState Camera,
    ViewportDimensions Viewport);

/// <summary>Identifies framework-neutral keyboard modifiers.</summary>
[Flags]
public enum ViewerInputModifiers
{
    /// <summary>No keyboard modifier is active.</summary>
    None = 0,

    /// <summary>The Alt modifier is active.</summary>
    Alt = 1,

    /// <summary>The Control modifier is active.</summary>
    Control = 2,

    /// <summary>The Shift modifier is active.</summary>
    Shift = 4,

    /// <summary>The platform command modifier is active.</summary>
    Meta = 8
}
