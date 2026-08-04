// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>OpenUSD authored prim specifier values.</summary>
public enum UsdPrimSpecifier
{
    /// <summary>The specifier is unavailable or unknown.</summary>
    Unknown = 0,
    /// <summary>A concrete defining specifier.</summary>
    Def = 1,
    /// <summary>An over specifier.</summary>
    Over = 2,
    /// <summary>A class specifier.</summary>
    Class = 3
}

/// <summary>Detached composed classification flags for one prim.</summary>
public readonly record struct UsdPrimClassification(
    bool IsDefined,
    bool IsAbstract,
    bool IsInPrototype,
    UsdPrimSpecifier Specifier)
{
    internal static UsdPrimClassification FromNative(OpenUsdNativePrimClassification value) =>
        new(
            value.IsDefined != 0,
            value.IsAbstract != 0,
            value.IsInPrototype != 0,
            (UsdPrimSpecifier)value.Specifier);
}
