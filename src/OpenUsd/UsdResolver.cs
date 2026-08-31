// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Describes one asset path after upstream resolution.
/// </summary>
/// <param name="AssetPath">The requested asset path.</param>
/// <param name="Identifier">The resolver identifier created for the requested path.</param>
/// <param name="ResolvedPath">The resolved path, or an empty string when nothing resolved.</param>
/// <param name="Extension">The extension the resolver reports for the identifier.</param>
/// <param name="AssetVersion">The resolver-reported asset version, when the resolver has one.</param>
/// <param name="AssetName">The resolver-reported asset name, when the resolver has one.</param>
/// <param name="IsResolved">Whether the resolver produced a resolved path.</param>
/// <param name="IsContextDependent">
/// Whether resolution of the path depends on the bound resolver context.
/// </param>
/// <param name="ModificationTime">
/// The resolver-reported modification timestamp, or <see langword="null"/> when the resolver does
/// not report a valid timestamp for the asset.
/// </param>
public readonly record struct UsdResolvedAsset(
    string AssetPath,
    string Identifier,
    string ResolvedPath,
    string Extension,
    string AssetVersion,
    string AssetName,
    bool IsResolved,
    bool IsContextDependent,
    double? ModificationTime);

/// <summary>
/// Provides bulk asset resolution and resolver discovery over the upstream OpenUSD resolver.
/// </summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdResolver
{
    /// <summary>Gets the type name of the primary resolver selected by the loaded plugins.</summary>
    public static string PrimaryTypeName => OpenUsdNativeRuntime.ResolverPrimaryTypeName;

    /// <summary>Enumerates the URI/IRI schemes that have a registered resolver.</summary>
    /// <remarks>
    /// OpenUSD builds its resolver registry once, the first time asset resolution is used. Plugin
    /// trees that register a URI resolver must be registered before that point, so a scheme that
    /// appears here is one the process can actually resolve. This, not
    /// <see cref="GetAvailableResolverTypeNames"/>, is the discovery signal for a third-party URI
    /// resolver.
    /// </remarks>
    public static IReadOnlyList<string> GetRegisteredUriSchemes() =>
        Array.AsReadOnly(OpenUsdNativeRuntime.GetResolverUriSchemes());

    /// <summary>Enumerates the primary-resolver candidates discovered in the plugin trees.</summary>
    /// <remarks>
    /// These are the types OpenUSD considers when it selects the one process-wide primary
    /// resolver, so the list is deliberately narrower than "every registered resolver". A plugin
    /// that declares URI/IRI schemes can never be a primary resolver and never appears here even
    /// when it is loaded and resolving; use <see cref="GetRegisteredUriSchemes"/> to discover
    /// those. <c>ArDefaultResolver</c> is always a candidate, because it is the fallback OpenUSD
    /// uses when no plugin resolver claims the primary role.
    /// </remarks>
    public static IReadOnlyList<string> GetAvailableResolverTypeNames() =>
        Array.AsReadOnly(OpenUsdNativeRuntime.GetResolverAvailableTypeNames());

    /// <summary>Resolves a batch of asset paths in one native call.</summary>
    /// <param name="assetPaths">The asset paths to resolve.</param>
    /// <param name="context">
    /// The resolver context to bind for the whole batch, or <see langword="null"/> to resolve with
    /// whatever context is already bound on the calling thread. A context is always bound, even an
    /// empty one, so passing an empty context deliberately shadows the ambient context and
    /// resolves as if nothing were bound.
    /// </param>
    /// <param name="anchorAssetPath">
    /// The resolved asset path that relative asset paths are anchored to, or
    /// <see langword="null"/> to leave them unanchored.
    /// </param>
    /// <returns>One record per requested path, in request order.</returns>
    public static IReadOnlyList<UsdResolvedAsset> Resolve(
        IReadOnlyList<string> assetPaths,
        UsdResolverContext? context = null,
        string? anchorAssetPath = null)
    {
        ArgumentNullException.ThrowIfNull(assetPaths);
        var paths = new string[assetPaths.Count];
        for (int i = 0; i < paths.Length; i++)
        {
            string path = assetPaths[i];
            ArgumentException.ThrowIfNullOrEmpty(path, nameof(assetPaths));
            paths[i] = path;
        }

        OpenUsdNativeResolvedAsset[] resolved = OpenUsdNativeRuntime.ResolveAssets(
            paths,
            context?.Native,
            anchorAssetPath);
        var result = new UsdResolvedAsset[resolved.Length];
        for (int i = 0; i < result.Length; i++)
        {
            OpenUsdNativeResolvedAsset asset = resolved[i];
            result[i] = new UsdResolvedAsset(
                asset.AssetPath,
                asset.Identifier,
                asset.ResolvedPath,
                asset.Extension,
                asset.AssetVersion,
                asset.AssetName,
                asset.IsResolved,
                asset.IsContextDependent,
                asset.ModificationTime);
        }
        return Array.AsReadOnly(result);
    }
}
