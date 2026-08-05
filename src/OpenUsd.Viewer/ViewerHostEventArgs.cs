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
        PrimPath = result.Status == RenderPickStatus.Hit ? result.PrimPath : null;
        InstancerPath = result.InstancerPath;
        InstanceIndex = result.InstanceIndex;
        ElementIndex = result.ElementIndex;
        WorldPosition = result.WorldPosition;
        WorldNormal = result.WorldNormal;
        Status = result.Status;
        StaleReasons = result.StaleReasons;
        PixelX = result.Request.X;
        PixelY = result.Request.Y;
        Viewport = result.Request.Viewport;
        StateRevision = result.StateRevision;
        SceneRevision = result.SceneRevision;
    }

    /// <summary>Gets the hit prim path, or <see langword="null"/> for non-hit results.</summary>
    public string? PrimPath { get; }

    /// <summary>Gets the hit instancer path, when the hit resolves an instance.</summary>
    public string? InstancerPath { get; }

    /// <summary>Gets the zero-based hit instance index, when available.</summary>
    public int? InstanceIndex { get; }

    /// <summary>Gets the zero-based hit subprim element index, when available.</summary>
    public int? ElementIndex { get; }

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
