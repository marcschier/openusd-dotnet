// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Lists and toggles OpenUSD TfDebug symbols.</summary>
public static class TfDebug
{
    /// <summary>Gets all registered TfDebug symbol names.</summary>
    public static IReadOnlyList<string> GetSymbolNames() =>
        Array.AsReadOnly(OpenUsdNativeRuntime.GetTfDebugSymbolNames());

    /// <summary>Gets a registered TfDebug symbol description.</summary>
    public static string GetSymbolDescription(string name) =>
        OpenUsdNativeRuntime.GetTfDebugSymbolDescription(name);

    /// <summary>Gets whether a registered TfDebug symbol is enabled.</summary>
    public static bool GetSymbolEnabled(string name) =>
        OpenUsdNativeRuntime.GetTfDebugSymbolEnabled(name);

    /// <summary>Enables or disables a registered TfDebug symbol.</summary>
    public static bool SetSymbolEnabled(string name, bool enabled) =>
        OpenUsdNativeRuntime.SetTfDebugSymbol(name, enabled);
}
