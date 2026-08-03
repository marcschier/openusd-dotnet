// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>Focused UsdShade schema conveniences for <see cref="UsdStage"/>.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdShadeStageExtensions
{
    /// <summary>Defines a UsdShadeMaterial.</summary>
    public static UsdShadeMaterial DefineMaterial(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineShadeMaterial(path);
        return new UsdShadeMaterial(stage, path);
    }

    /// <summary>Defines a UsdShadeShader.</summary>
    public static UsdShadeShader DefineShader(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineShadeShader(path);
        return new UsdShadeShader(stage, path);
    }

    /// <summary>Defines a UsdShadeNodeGraph.</summary>
    public static UsdShadeNodeGraph DefineNodeGraph(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineShadeNodeGraph(path);
        return new UsdShadeNodeGraph(stage, path);
    }

    /// <summary>Gets the directly bound material for a prim.</summary>
    public static UsdShadeMaterial GetDirectlyBoundMaterial(
        this UsdStage stage,
        UsdPrim prim)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdShadeSchema.ValidateSameStage(stage, prim.OwningStage);
        string path = stage.Native.GetDirectMaterialPath(prim.Path);
        return new UsdShadeMaterial(stage, path);
    }
}
