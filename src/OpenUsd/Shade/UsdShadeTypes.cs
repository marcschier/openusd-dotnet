// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>Identifies the supported typed shading values.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The names mirror OpenUSD's Sdf value type terminology.")]
public enum UsdShadeValueType
{
    /// <summary>A scalar float.</summary>
    Float = 1,
    /// <summary>A role-bearing color3f.</summary>
    Color3f = 2,
    /// <summary>A role-bearing vector3f.</summary>
    Vector3f = 3,
    /// <summary>A role-bearing normal3f.</summary>
    Normal3f = 4,
    /// <summary>A token.</summary>
    Token = 5,
    /// <summary>A string.</summary>
    String = 6,
    /// <summary>An asset path.</summary>
    Asset = 7,
    /// <summary>A roleless three-component float tuple.</summary>
    Float3 = 8
}

/// <summary>Identifies a shading input or output.</summary>
public enum UsdShadeAttributeType
{
    /// <summary>A shading input.</summary>
    Input = 1,
    /// <summary>A shading output.</summary>
    Output = 2
}

/// <summary>Identifies standard material terminal outputs.</summary>
public enum UsdShadeMaterialTerminal
{
    /// <summary>The surface terminal.</summary>
    Surface = 0,
    /// <summary>The displacement terminal.</summary>
    Displacement = 1,
    /// <summary>The volume terminal.</summary>
    Volume = 2
}

/// <summary>Specifies material binding strength relative to descendants.</summary>
public enum UsdShadeBindingStrength
{
    /// <summary>The binding is weaker than descendant bindings.</summary>
    WeakerThanDescendants = 0,
    /// <summary>The binding is stronger than descendant bindings.</summary>
    StrongerThanDescendants = 1
}

/// <summary>Specifies material binding purpose.</summary>
public enum UsdShadeMaterialPurpose
{
    /// <summary>The all-purpose material binding.</summary>
    All = 0,
    /// <summary>The preview material binding.</summary>
    Preview = 1,
    /// <summary>The full-fidelity material binding.</summary>
    Full = 2
}

/// <summary>Describes one connected source of a shading property.</summary>
public readonly record struct UsdShadeConnection(
    string SourcePrimPath,
    string SourceName,
    UsdShadeAttributeType SourceType) : IUsdDetachedResult;
