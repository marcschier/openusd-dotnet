// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Owns a managed view of an OpenUSD layer.
/// </summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdLayer : IDisposable, IUsdStageBound
{
    private OpenUsdNativeLayer? _native;

    internal UsdLayer(OpenUsdNativeLayer native)
    {
        _native = native;
    }

    /// <summary>Gets the layer identifier.</summary>
    public string Identifier => Native.Identifier;

    /// <summary>Saves the layer.</summary>
    public void Save() => Native.Save();

    /// <summary>Reloads the layer, returning whether content was re-read.</summary>
    public bool Reload(bool force = false) => Native.Reload(force);

    /// <summary>Exports a copy of the layer.</summary>
    public void Export(string path) => Native.Export(path);

    /// <summary>Adds a sublayer path.</summary>
    public void AddSublayer(string sublayerPath) => Native.AddSublayer(sublayerPath);

    /// <summary>Removes a sublayer path.</summary>
    public void RemoveSublayer(string sublayerPath) => Native.RemoveSublayer(sublayerPath);

    /// <summary>Gets the ordered sublayer paths.</summary>
    public string[] GetSublayerPaths() => Native.GetSublayerPaths();

    /// <summary>Sets a string entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, string value) => Native.SetMetadata(key, value);

    /// <summary>Sets a bool entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, bool value) => Native.SetMetadata(key, value);

    /// <summary>Sets an int64 entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, long value) => Native.SetMetadata(key, value);

    /// <summary>Sets a double entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, double value) => Native.SetMetadata(key, value);

    /// <summary>Gets a string entry from the layer's customLayerData dictionary.</summary>
    public string GetMetadataString(string key) => Native.GetMetadataString(key);

    /// <summary>Gets a bool entry from the layer's customLayerData dictionary.</summary>
    public bool GetMetadataBool(string key) => Native.GetMetadataBool(key);

    /// <summary>Gets an int64 entry from the layer's customLayerData dictionary.</summary>
    public long GetMetadataInt64(string key) => Native.GetMetadataInt64(key);

    /// <summary>Gets a double entry from the layer's customLayerData dictionary.</summary>
    public double GetMetadataDouble(string key) => Native.GetMetadataDouble(key);

    /// <summary>Clears an entry from the layer's customLayerData dictionary.</summary>
    public void ClearMetadata(string key) => Native.ClearMetadata(key);

    /// <inheritdoc/>
    public void Dispose()
    {
        _native?.Dispose();
        _native = null;
    }

    internal OpenUsdNativeLayer Native =>
        _native ?? throw new ObjectDisposedException(nameof(UsdLayer));
}
