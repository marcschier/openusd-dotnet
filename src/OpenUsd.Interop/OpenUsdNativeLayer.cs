// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Interop;

/// <summary>
/// Owns a native OpenUSD layer handle.
/// </summary>
internal sealed class OpenUsdNativeLayer : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OpenUsdNativeLayer(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    /// <summary>Gets the layer identifier.</summary>
    public string Identifier => OpenUsdNativeRuntime.GetLayerIdentifier(this);

    /// <summary>Saves the layer.</summary>
    public void Save() => OpenUsdNativeRuntime.SaveLayer(this);

    /// <summary>Reloads the layer, returning whether content was re-read.</summary>
    public bool Reload(bool force = false) => OpenUsdNativeRuntime.ReloadLayer(this, force);

    /// <summary>Exports a copy of the layer.</summary>
    public void Export(string path) => OpenUsdNativeRuntime.ExportLayer(this, path);

    /// <summary>Adds a sublayer path.</summary>
    public void AddSublayer(string sublayerPath) => OpenUsdNativeRuntime.AddSublayer(this, sublayerPath);

    /// <summary>Removes a sublayer path.</summary>
    public void RemoveSublayer(string sublayerPath) =>
        OpenUsdNativeRuntime.RemoveSublayer(this, sublayerPath);

    /// <summary>Gets the ordered sublayer paths.</summary>
    public string[] GetSublayerPaths() => OpenUsdNativeRuntime.GetSublayerPaths(this);

    /// <summary>Sets a string entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, string value) => OpenUsdNativeRuntime.SetMetadataString(this, key, value);

    /// <summary>Sets a bool entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, bool value) => OpenUsdNativeRuntime.SetMetadataBool(this, key, value);

    /// <summary>Sets an int64 entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, long value) => OpenUsdNativeRuntime.SetMetadataInt64(this, key, value);

    /// <summary>Sets a double entry in the layer's customLayerData dictionary.</summary>
    public void SetMetadata(string key, double value) => OpenUsdNativeRuntime.SetMetadataDouble(this, key, value);

    /// <summary>Gets a string entry from the layer's customLayerData dictionary.</summary>
    public string GetMetadataString(string key) => OpenUsdNativeRuntime.GetMetadataString(this, key);

    /// <summary>Gets a bool entry from the layer's customLayerData dictionary.</summary>
    public bool GetMetadataBool(string key) => OpenUsdNativeRuntime.GetMetadataBool(this, key);

    /// <summary>Gets an int64 entry from the layer's customLayerData dictionary.</summary>
    public long GetMetadataInt64(string key) => OpenUsdNativeRuntime.GetMetadataInt64(this, key);

    /// <summary>Gets a double entry from the layer's customLayerData dictionary.</summary>
    public double GetMetadataDouble(string key) => OpenUsdNativeRuntime.GetMetadataDouble(this, key);

    /// <summary>Clears an entry from the layer's customLayerData dictionary.</summary>
    public void ClearMetadata(string key) => OpenUsdNativeRuntime.ClearMetadata(this, key);

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        OpenUsdNativeRuntime.ReleaseLayer(handle);
        return true;
    }
}
