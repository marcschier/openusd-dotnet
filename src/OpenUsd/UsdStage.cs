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
    private OpenUsdNativeStage? _native;

    private UsdStage(OpenUsdNativeStage native)
        : this(native, ownsNative: true)
    {
    }

    private UsdStage(OpenUsdNativeStage native, bool ownsNative)
    {
        _native = native;
        _ownsNative = ownsNative;
    }

    /// <summary>Opens an existing stage.</summary>
    public static UsdStage Open(string path) => new(OpenUsdNativeRuntime.OpenStage(path));

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

    /// <summary>Gets an owned root-layer view.</summary>
    public UsdLayer GetRootLayer() => new(Native.GetRootLayer());

    /// <summary>Gets an owned session-layer view.</summary>
    public UsdLayer GetSessionLayer() => new(Native.GetSessionLayer());

    /// <summary>Sets the root layer as the current edit target.</summary>
    public void SetEditTargetToRootLayer() => Native.SetEditTargetToRootLayer();

    /// <summary>Sets the session layer as the current edit target.</summary>
    public void SetEditTargetToSessionLayer() => Native.SetEditTargetToSessionLayer();

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

        _native?.Dispose();
        _native = null;
    }

    internal UsdStage Borrow() => new(Native, ownsNative: false);

    internal OpenUsdNativeStage Native =>
        _native ?? throw new ObjectDisposedException(nameof(UsdStage));
}
