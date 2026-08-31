// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Kind reported by the OpenUSD plugin registry for a discovered plugin.</summary>
public enum UsdPluginKind
{
    /// <summary>A plugin backed by a shared library.</summary>
    Library = 0,
    /// <summary>A plugin that only contributes resources.</summary>
    Resource = 1,
    /// <summary>A Python module plugin, which the locked runtime never loads.</summary>
    Python = 2
}

/// <summary>Describes one plugin discovered by the OpenUSD plugin registry.</summary>
/// <param name="Name">The plugin name declared by its plugInfo.json.</param>
/// <param name="Kind">The plugin kind reported by the registry.</param>
/// <param name="IsLoaded">Whether the plugin library has been loaded.</param>
/// <param name="Path">The plugin library or plugInfo path.</param>
/// <param name="ResourcePath">The directory the plugin resolves its resources from.</param>
public readonly record struct UsdPluginInfo(
    string Name,
    UsdPluginKind Kind,
    bool IsLoaded,
    string Path,
    string ResourcePath);

/// <summary>
/// Registers and inspects OpenUSD plugin trees, including third-party resolver plugins.
/// </summary>
/// <remarks>
/// Registration takes the plugInfo tree exactly as it is laid out on disk. The tree is never
/// merged or rewritten, so a vendor plugin keeps its own <c>plugInfo.json</c>, its own resource
/// directory, and its own library path.
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdPluginRegistry
{
    /// <summary>Registers every plugin discovered below a plugInfo file or directory.</summary>
    /// <param name="plugInfoPath">A plugInfo.json file or a directory that contains one.</param>
    /// <returns>The number of newly registered plugins.</returns>
    /// <remarks>
    /// A plugin that registers an <c>ArResolver</c> must be registered before the process first
    /// resolves an asset, because OpenUSD selects and caches its resolver set on first use.
    /// </remarks>
    public static int Register(string plugInfoPath) =>
        checked((int)OpenUsdNativeRuntime.RegisterPlugins(plugInfoPath));

    /// <summary>Enumerates every plugin the registry has discovered, ordered by name.</summary>
    public static IReadOnlyList<UsdPluginInfo> GetRegisteredPlugins()
    {
        OpenUsdNativePlugin[] plugins = OpenUsdNativeRuntime.GetRegisteredPlugins();
        var result = new UsdPluginInfo[plugins.Length];
        for (int i = 0; i < result.Length; i++)
        {
            OpenUsdNativePlugin plugin = plugins[i];
            result[i] = new UsdPluginInfo(
                plugin.Name,
                ParseKind(plugin.Kind),
                plugin.IsLoaded,
                plugin.Path,
                plugin.ResourcePath);
        }
        return Array.AsReadOnly(result);
    }

    private static UsdPluginKind ParseKind(string kind) => kind switch
    {
        "resource" => UsdPluginKind.Resource,
        "python" => UsdPluginKind.Python,
        _ => UsdPluginKind.Library
    };
}
