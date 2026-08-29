// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>Stable codes emitted when hdSilk degrades rendered material output.</summary>
public static class SilkRenderDiagnosticCodes
{
    /// <summary>A mesh references a material that is absent from retained scene state.</summary>
    public const string MaterialUnresolved = "OPENUSD_SILK_MATERIAL_UNRESOLVED";

    /// <summary>A retained material uses a surface network hdSilk cannot shade.</summary>
    public const string MaterialUnsupported = "OPENUSD_SILK_MATERIAL_UNSUPPORTED";

    /// <summary>A referenced texture asset could not be found.</summary>
    public const string TextureAssetNotFound = "OPENUSD_SILK_TEXTURE_ASSET_NOT_FOUND";

    /// <summary>A referenced texture asset could not be decoded.</summary>
    public const string TextureDecodeFailed = "OPENUSD_SILK_TEXTURE_DECODE_FAILED";

    /// <summary>An authored texture fallback value was used.</summary>
    public const string TextureFallbackUsed = "OPENUSD_SILK_TEXTURE_FALLBACK_USED";

    /// <summary>Additional diagnostics were omitted to keep the snapshot bounded.</summary>
    public const string CapacityExceeded = "OPENUSD_SILK_DIAGNOSTIC_CAPACITY_EXCEEDED";
}
