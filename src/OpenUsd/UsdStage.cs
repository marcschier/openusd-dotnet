// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Presents a composed OpenUSD stage through an idiomatic managed API.
/// </summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdStage : IDisposable, IUsdStageBound
{
    private readonly bool _ownsNative;

    // A borrowed facade is a second object over the same native stage, so overlay state cannot
    // live on whichever facade happened to create it: the scheduler hands callbacks a borrowed
    // facade, the overlay is normalized on that one, and the owning facade - the only one that can
    // be disposed - would never see it. Every facade therefore reads and writes the owner's slot.
    private readonly UsdStage? _owner;
    private OpenUsdNativeStage? _native;
    private UsdSessionOverlay? _activeOverlay;

    private UsdStage(OpenUsdNativeStage native)
        : this(native, ownsNative: true)
    {
    }

    private UsdStage(OpenUsdNativeStage native, bool ownsNative, UsdStage? owner = null)
    {
        _native = native;
        _ownsNative = ownsNative;
        _owner = owner;
    }

    private UsdSessionOverlay? ActiveOverlay
    {
        get => _owner is null ? _activeOverlay : _owner.ActiveOverlay;
        set
        {
            if (_owner is null)
            {
                _activeOverlay = value;
            }
            else
            {
                _owner.ActiveOverlay = value;
            }
        }
    }

    /// <summary>Opens an existing stage.</summary>
    public static UsdStage Open(string path) => new(OpenUsdNativeRuntime.OpenStage(path));

    /// <summary>Opens an existing stage whose asset resolution uses a resolver context.</summary>
    /// <remarks>
    /// The context is owned by the stage for the lifetime of its composition, so callers do not
    /// have to bind it on the calling thread.
    /// </remarks>
    public static UsdStage Open(string path, UsdResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new UsdStage(OpenUsdNativeRuntime.OpenStageWithContext(path, context.Native));
    }

    /// <summary>Opens an existing stage with a bulk population mask.</summary>
    public static UsdStage OpenMasked(string path, ReadOnlySpan<string> maskPaths)
    {
        for (int i = 0; i < maskPaths.Length; i++)
        {
            UsdPath.ValidateAbsolutePrimPath(maskPaths[i], nameof(maskPaths));
        }
        return new UsdStage(OpenUsdNativeRuntime.OpenStageMasked(path, maskPaths));
    }

    /// <summary>Creates a new file-backed stage.</summary>
    public static UsdStage Create(string path) => new(OpenUsdNativeRuntime.CreateStage(path));

    /// <summary>Gets the root layer identifier.</summary>
    public string RootLayerIdentifier => Native.RootLayerIdentifier;

    /// <summary>Gets the session layer identifier.</summary>
    public string SessionLayerIdentifier => Native.SessionLayerIdentifier;

    /// <summary>Gets the current edit-target layer identifier.</summary>
    public string EditTargetLayerIdentifier => Native.EditTargetLayerIdentifier;

    /// <summary>Gets the serial advanced by OpenUSD object-change notices.</summary>
    public ulong ChangeSerial => Native.ChangeSerial;

    /// <summary>Gets or sets the composed start time code.</summary>
    public double StartTimeCode
    {
        get => Native.StartTimeCode;
        set => Native.StartTimeCode = value;
    }

    /// <summary>Gets or sets the composed end time code.</summary>
    public double EndTimeCode
    {
        get => Native.EndTimeCode;
        set => Native.EndTimeCode = value;
    }

    /// <summary>Gets or sets the advisory playback rate.</summary>
    public double FramesPerSecond
    {
        get => Native.FramesPerSecond;
        set => Native.FramesPerSecond = value;
    }

    /// <summary>Gets or sets the number of time codes per second.</summary>
    public double TimeCodesPerSecond
    {
        get => Native.TimeCodesPerSecond;
        set => Native.TimeCodesPerSecond = value;
    }

    /// <summary>Gets stage world bounds at default time for the selected purposes.</summary>
    /// <remarks>An empty stage returns <see cref="UsdBounds3d.Empty"/>.</remarks>
    public UsdBounds3d GetWorldBounds(
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All) =>
        UsdBounds3d.FromNative(
            Native.GetWorldBounds(
                null,
                UsdBounds3d.ValidatePurposeMask(purposeMask)));

    /// <summary>Gets stage world bounds at a numeric time for the selected purposes.</summary>
    public UsdBounds3d GetWorldBounds(
        double timeCode,
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All) =>
        UsdBounds3d.FromNative(
            Native.GetWorldBounds(
                null,
                UsdBounds3d.ValidatePurposeMask(purposeMask),
                UsdBounds3d.ValidateTimeCode(timeCode)));

    /// <summary>Gets stage world oriented bounds at default time for the selected purposes.</summary>
    public UsdOrientedBounds3d GetWorldOrientedBounds(
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All) =>
        UsdOrientedBounds3d.FromNative(
            Native.GetWorldOrientedBounds(
                null,
                UsdOrientedBounds3d.ValidatePurposeMask(purposeMask)));

    /// <summary>Gets stage world oriented bounds at a numeric time for the selected purposes.</summary>
    public UsdOrientedBounds3d GetWorldOrientedBounds(
        double timeCode,
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All) =>
        UsdOrientedBounds3d.FromNative(
            Native.GetWorldOrientedBounds(
                null,
                UsdOrientedBounds3d.ValidatePurposeMask(purposeMask),
                UsdOrientedBounds3d.ValidateTimeCode(timeCode)));

    /// <summary>Gets an owned root-layer view.</summary>
    public UsdLayer GetRootLayer() => new(Native.GetRootLayer());

    /// <summary>Gets an owned session-layer view.</summary>
    public UsdLayer GetSessionLayer() => new(Native.GetSessionLayer());

    /// <summary>
    /// Normalizes the session layer into a simulation overlay topology with a strong
    /// physics overlay and a weaker user-edit layer.
    /// </summary>
    /// <returns>The overlay handle that manages physics-layer lifetime.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a session overlay is already active on this stage.
    /// </exception>
    public UsdSessionOverlay NormalizeSessionOverlay()
    {
        if (ActiveOverlay is { IsDisposed: false })
        {
            throw new InvalidOperationException(
                "A session overlay is already active on this stage.");
        }
        var overlay = UsdSessionOverlay.Normalize(Native, this);
        ActiveOverlay = overlay;
        return overlay;
    }

    /// <summary>Sets the root layer as the current edit target.</summary>
    public void SetEditTargetToRootLayer() => Native.SetEditTargetToRootLayer();

    /// <summary>
    /// Sets the session layer as the current edit target. While a session overlay is active,
    /// this redirects to the user-edit layer so physics results always compose above user edits.
    /// </summary>
    public void SetEditTargetToSessionLayer()
    {
        if (ActiveOverlay is { IsDisposed: false } overlay &&
            overlay.TryRedirectSessionEditTarget(Native))
        {
            return;
        }
        Native.SetEditTargetToSessionLayer();
    }

    /// <summary>Sets an owned layer from this stage's local layer stack as the edit target.</summary>
    public void SetEditTarget(UsdLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Native.SetEditTarget(layer.Native);
    }

    /// <summary>Gets local layer-stack identifiers in strong-to-weak order.</summary>
    public string[] GetLayerStackIdentifiers() => Native.GetLayerStackIdentifiers();

    /// <summary>Mutes a local layer by identifier.</summary>
    public void MuteLayer(string layerIdentifier) => Native.MuteLayer(layerIdentifier);

    /// <summary>Unmutes a layer by identifier.</summary>
    public void UnmuteLayer(string layerIdentifier) => Native.UnmuteLayer(layerIdentifier);

    /// <summary>Returns whether a known layer identifier is muted.</summary>
    public bool IsLayerMuted(string layerIdentifier) => Native.IsLayerMuted(layerIdentifier);

    /// <summary>Gets the valid default prim.</summary>
    public UsdPrim GetDefaultPrim() => new(this, Native.GetDefaultPrimPath());

    /// <summary>Sets the default prim by absolute path.</summary>
    public void SetDefaultPrim(string path)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        Native.SetDefaultPrim(path);
    }

    /// <summary>Clears the authored default prim.</summary>
    public void ClearDefaultPrim() => Native.ClearDefaultPrim();

    /// <summary>Defines a prim and returns its managed path view.</summary>
    public UsdPrim DefinePrim(string path, string? typeName = null)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        Native.DefinePrim(path, typeName);
        return new UsdPrim(this, path);
    }

    /// <summary>Authors an override prim and returns its managed path view.</summary>
    public UsdPrim OverridePrim(string path)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        Native.OverridePrim(path);
        return new UsdPrim(this, path);
    }

    /// <summary>Authors a class prim at a root prim path and returns its managed path view.</summary>
    public UsdPrim CreateClassPrim(string path)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        Native.CreateClassPrim(path);
        return new UsdPrim(this, path);
    }

    /// <summary>Gets a path-based prim view.</summary>
    public UsdPrim GetPrim(string path)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        return new UsdPrim(this, path);
    }

    /// <summary>Returns whether a prim exists at the given absolute path.</summary>
    public bool HasPrim(string path)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        return Native.HasPrim(path);
    }

    /// <summary>Removes a prim and all of its descendants.</summary>
    public void RemovePrim(string path)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        Native.RemovePrim(path);
    }

    /// <summary>Returns composed prims in OpenUSD traversal order.</summary>
    public IReadOnlyList<UsdPrim> Traverse()
    {
        string[] paths = Native.GetPrimPaths();
        var prims = new UsdPrim[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            prims[i] = new UsdPrim(this, paths[i]);
        }
        return prims;
    }

    /// <summary>Saves the root layer.</summary>
    public void Save() => Native.Save();

    /// <summary>Reloads all non-session layers contributing to the stage.</summary>
    public void Reload() => Native.Reload();

    /// <summary>Exports the composed stage as a flattened layer.</summary>
    public void Export(string path) => Native.Export(path);

    /// <inheritdoc/>
    /// <exception cref="UsdStageOwnershipException">
    /// This instance is a borrowed scheduler callback facade.
    /// </exception>
    public void Dispose()
    {
        if (!_ownsNative)
        {
            throw new UsdStageOwnershipException();
        }

        try
        {
            if (ActiveOverlay is { IsDisposed: false } overlay)
            {
                overlay.Dispose();
            }
            ActiveOverlay = null;
        }
        finally
        {
            _native?.Dispose();
            _native = null;
        }
    }

    internal void ClearActiveOverlay(UsdSessionOverlay overlay)
    {
        if (ReferenceEquals(ActiveOverlay, overlay))
        {
            ActiveOverlay = null;
        }
    }

    internal UsdStage Borrow() => new(Native, ownsNative: false, owner: this);

    internal OpenUsdNativeStage Native =>
        _native ?? throw new ObjectDisposedException(nameof(UsdStage));
}
