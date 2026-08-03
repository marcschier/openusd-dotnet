// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

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

    public static UsdGeomSubset DefineSubset(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Subset);
        return new UsdGeomSubset(stage, path);
    }

    public static UsdGeomBasisCurves DefineBasisCurves(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.BasisCurves);
        return new UsdGeomBasisCurves(stage, path);
    }

    public static UsdGeomNurbsCurves DefineNurbsCurves(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.NurbsCurves);
        return new UsdGeomNurbsCurves(stage, path);
    }

    public static UsdGeomHermiteCurves DefineHermiteCurves(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.HermiteCurves);
        return new UsdGeomHermiteCurves(stage, path);
    }

    public static UsdGeomNurbsPatch DefineNurbsPatch(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.NurbsPatch);
        return new UsdGeomNurbsPatch(stage, path);
    }

    public static UsdGeomPoints DefinePoints(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Points);
        return new UsdGeomPoints(stage, path);
    }

    public static UsdGeomPointInstancer DefinePointInstancer(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.PointInstancer);
        return new UsdGeomPointInstancer(stage, path);
    }

    public static UsdGeomCapsule DefineCapsule(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Capsule);
        return new UsdGeomCapsule(stage, path);
    }

    public static UsdGeomCone DefineCone(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Cone);
        return new UsdGeomCone(stage, path);
    }

    public static UsdGeomCube DefineCube(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Cube);
        return new UsdGeomCube(stage, path);
    }

    public static UsdGeomCylinder DefineCylinder(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Cylinder);
        return new UsdGeomCylinder(stage, path);
    }

    public static UsdGeomSphere DefineSphere(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Sphere);
        return new UsdGeomSphere(stage, path);
    }

    public static UsdGeomPlane DefinePlane(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.Plane);
        return new UsdGeomPlane(stage, path);
    }

    public static UsdGeomTetMesh DefineTetMesh(this UsdStage stage, string path)
    {
        UsdGeomFacade.Define(stage, path, UsdGeomSchemaKind.TetMesh);
        return new UsdGeomTetMesh(stage, path);
    }
}
