// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Geom;

/// <summary>Focused UsdGeom schema-definition conveniences for <see cref="UsdStage"/>.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdGeomStageExtensions
{
    /// <summary>Defines a UsdGeomXform.</summary>
    public static UsdGeomXform DefineXform(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineGeomXform(path);
        return new UsdGeomXform(stage, path);
    }

    /// <summary>Defines a UsdGeomMesh.</summary>
    public static UsdGeomMesh DefineMesh(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineGeomMesh(path);
        return new UsdGeomMesh(stage, path);
    }

    /// <summary>Defines a UsdGeomCamera.</summary>
    public static UsdGeomCamera DefineCamera(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineGeomCamera(path);
        return new UsdGeomCamera(stage, path);
    }
}
