// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class UsdGeomSchemaFacadeNativeCoverageTests
{
    private const int UsdGeomSchemaBulkValueCount = 257;

    private static readonly UsdVec3f[] UsdGeomSchemaBulkVec3Values =
        CreateUsdGeomSchemaBulkVec3Values();
    private static readonly UsdVec2f[] UsdGeomSchemaBulkVec2Values =
        CreateUsdGeomSchemaBulkVec2Values();
    private static readonly UsdQuatf[] UsdGeomSchemaBulkQuatValues =
        CreateUsdGeomSchemaBulkQuatValues();
    private static readonly int[] UsdGeomSchemaBulkIntValues =
        CreateUsdGeomSchemaBulkIntValues();
    private static readonly float[] UsdGeomSchemaBulkFloatValues =
        CreateUsdGeomSchemaBulkFloatValues();
    private static readonly double[] UsdGeomSchemaBulkDoubleValues =
        CreateUsdGeomSchemaBulkDoubleValues();

    private static readonly string[] UsdGeomSchemaPrototypeTargets =
    [
        "/PrototypeA",
        "/PrototypeB"
    ];

    private static readonly int[] UsdGeomSchemaTetVertexIndices =
    [
        0,
        1,
        2,
        3,
        4,
        5,
        6,
        7
    ];

    private static readonly int[] UsdGeomSchemaTetSurfaceFaceVertexIndices =
    [
        0,
        1,
        2,
        2,
        3,
        0
    ];


    [Test]
    public void ConcreteSchemaFacadesDefineTypedPrimsAndCanBeWrapped()
    {
        string directory = CreateDirectory(nameof(ConcreteSchemaFacadesDefineTypedPrimsAndCanBeWrapped));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-concrete");

        UsdGeomSubset subset = stage.DefineSubset("/Subset");
        RequireEqual(subset.Path, "/Subset", "Subset path");
        RequireEqual(subset.Prim.TypeName, "GeomSubset", "Subset type");
        Require(UsdGeomSubset.TryWrap(subset.Prim, out UsdGeomSubset wrappedSubset), "Subset TryWrap failed.");
        RequireEqual(wrappedSubset.Path, subset.Path, "Wrapped subset path");
        RequireEqual(
            UsdGeomSubset.Wrap(subset.Prim).Imageable.Prim.Path,
            subset.Path,
            "Wrapped subset imageable path");

        UsdGeomBasisCurves basisCurves = stage.DefineBasisCurves("/BasisCurves");
        RequireEqual(basisCurves.Prim.TypeName, "BasisCurves", "BasisCurves type");
        Require(
            UsdGeomBasisCurves.TryWrap(basisCurves.Prim, out UsdGeomBasisCurves wrappedBasisCurves),
            "BasisCurves TryWrap failed.");
        RequireEqual(wrappedBasisCurves.Xformable.Prim.Path, basisCurves.Path, "BasisCurves xformable path");

        UsdGeomNurbsCurves nurbsCurves = stage.DefineNurbsCurves("/NurbsCurves");
        RequireEqual(nurbsCurves.Prim.TypeName, "NurbsCurves", "NurbsCurves type");
        Require(
            UsdGeomNurbsCurves.TryWrap(nurbsCurves.Prim, out UsdGeomNurbsCurves wrappedNurbsCurves),
            "NurbsCurves TryWrap failed.");
        RequireEqual(UsdGeomNurbsCurves.Wrap(nurbsCurves.Prim).Path, wrappedNurbsCurves.Path, "NurbsCurves wrap path");

        UsdGeomHermiteCurves hermiteCurves = stage.DefineHermiteCurves("/HermiteCurves");
        RequireEqual(hermiteCurves.Prim.TypeName, "HermiteCurves", "HermiteCurves type");
        Require(
            UsdGeomHermiteCurves.TryWrap(hermiteCurves.Prim, out UsdGeomHermiteCurves wrappedHermiteCurves),
            "HermiteCurves TryWrap failed.");
        RequireEqual(wrappedHermiteCurves.Prim.Path, hermiteCurves.Path, "HermiteCurves wrap path");

        UsdGeomNurbsPatch nurbsPatch = stage.DefineNurbsPatch("/NurbsPatch");
        RequireEqual(nurbsPatch.Prim.TypeName, "NurbsPatch", "NurbsPatch type");
        Require(
            UsdGeomNurbsPatch.TryWrap(nurbsPatch.Prim, out UsdGeomNurbsPatch wrappedNurbsPatch),
            "NurbsPatch TryWrap failed.");
        RequireEqual(wrappedNurbsPatch.Path, nurbsPatch.Path, "NurbsPatch wrap path");

        UsdGeomPoints points = stage.DefinePoints("/Points");
        RequireEqual(points.Prim.TypeName, "Points", "Points type");
        Require(UsdGeomPoints.TryWrap(points.Prim, out UsdGeomPoints wrappedPoints), "Points TryWrap failed.");
        RequireEqual(wrappedPoints.Path, points.Path, "Points wrap path");

        UsdGeomPointInstancer instancer = stage.DefinePointInstancer("/Instancer");
        RequireEqual(instancer.Prim.TypeName, "PointInstancer", "PointInstancer type");
        Require(
            UsdGeomPointInstancer.TryWrap(instancer.Prim, out UsdGeomPointInstancer wrappedInstancer),
            "PointInstancer TryWrap failed.");
        RequireEqual(wrappedInstancer.Path, instancer.Path, "PointInstancer wrap path");

        UsdGeomCapsule capsule = stage.DefineCapsule("/Capsule");
        RequireEqual(capsule.Prim.TypeName, "Capsule", "Capsule type");
        Require(UsdGeomCapsule.TryWrap(capsule.Prim, out UsdGeomCapsule wrappedCapsule), "Capsule TryWrap failed.");
        RequireEqual(wrappedCapsule.Path, capsule.Path, "Capsule wrap path");

        UsdGeomCone cone = stage.DefineCone("/Cone");
        RequireEqual(cone.Prim.TypeName, "Cone", "Cone type");
        Require(UsdGeomCone.TryWrap(cone.Prim, out UsdGeomCone wrappedCone), "Cone TryWrap failed.");
        RequireEqual(wrappedCone.Path, cone.Path, "Cone wrap path");

        UsdGeomCube cube = stage.DefineCube("/Cube");
        RequireEqual(cube.Prim.TypeName, "Cube", "Cube type");
        Require(UsdGeomCube.TryWrap(cube.Prim, out UsdGeomCube wrappedCube), "Cube TryWrap failed.");
        RequireEqual(wrappedCube.Path, cube.Path, "Cube wrap path");

        UsdGeomCylinder cylinder = stage.DefineCylinder("/Cylinder");
        RequireEqual(cylinder.Prim.TypeName, "Cylinder", "Cylinder type");
        Require(
            UsdGeomCylinder.TryWrap(cylinder.Prim, out UsdGeomCylinder wrappedCylinder),
            "Cylinder TryWrap failed.");
        RequireEqual(wrappedCylinder.Path, cylinder.Path, "Cylinder wrap path");

        UsdGeomSphere sphere = stage.DefineSphere("/Sphere");
        RequireEqual(sphere.Prim.TypeName, "Sphere", "Sphere type");
        Require(UsdGeomSphere.TryWrap(sphere.Prim, out UsdGeomSphere wrappedSphere), "Sphere TryWrap failed.");
        RequireEqual(wrappedSphere.Path, sphere.Path, "Sphere wrap path");

        UsdGeomPlane plane = stage.DefinePlane("/Plane");
        RequireEqual(plane.Prim.TypeName, "Plane", "Plane type");
        Require(UsdGeomPlane.TryWrap(plane.Prim, out UsdGeomPlane wrappedPlane), "Plane TryWrap failed.");
        RequireEqual(wrappedPlane.Path, plane.Path, "Plane wrap path");

        UsdGeomTetMesh tetMesh = stage.DefineTetMesh("/TetMesh");
        RequireEqual(tetMesh.Prim.TypeName, "TetMesh", "TetMesh type");
        Require(UsdGeomTetMesh.TryWrap(tetMesh.Prim, out UsdGeomTetMesh wrappedTetMesh), "TetMesh TryWrap failed.");
        RequireEqual(wrappedTetMesh.Path, tetMesh.Path, "TetMesh wrap path");
    }

    [Test]
    public void CurveFacadesRoundTripBulkArraysAndTokens()
    {
        string directory = CreateDirectory(nameof(CurveFacadesRoundTripBulkArraysAndTokens));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-curves");

        UsdGeomBasisCurves basisCurves = stage.DefineBasisCurves("/BasisCurves");
        basisCurves.SetPoints(UsdGeomSchemaBulkVec3Values);
        basisCurves.SetCurveVertexCounts(UsdGeomSchemaBulkIntValues);
        basisCurves.SetWidths(UsdGeomSchemaBulkFloatValues);
        basisCurves.SetNormals(ScaleUsdGeomSchemaVec3Values(2.0f));
        basisCurves.Type = "cubic";
        basisCurves.Basis = "bezier";
        basisCurves.WrapMode = "pinned";

        RequireArrayCorners(basisCurves.GetPoints(), UsdGeomSchemaBulkVec3Values, "basis points");
        RequireArrayCorners(basisCurves.GetCurveVertexCounts(), UsdGeomSchemaBulkIntValues, "basis vertex counts");
        RequireArrayCorners(basisCurves.GetWidths(), UsdGeomSchemaBulkFloatValues, "basis widths");
        RequireArrayCorners(basisCurves.GetNormals(), ScaleUsdGeomSchemaVec3Values(2.0f), "basis normals");
        RequireEqual(basisCurves.Type, "cubic", "basis type");
        RequireEqual(basisCurves.Basis, "bezier", "basis token");
        RequireEqual(basisCurves.WrapMode, "pinned", "basis wrap mode");

        UsdGeomNurbsCurves nurbsCurves = stage.DefineNurbsCurves("/NurbsCurves");
        nurbsCurves.SetPoints(ScaleUsdGeomSchemaVec3Values(3.0f));
        nurbsCurves.SetCurveVertexCounts(UsdGeomSchemaBulkIntValues);
        nurbsCurves.SetOrder([2, 3, 4, 5, 6]);
        nurbsCurves.SetKnots(UsdGeomSchemaBulkDoubleValues);
        nurbsCurves.SetWidths(ScaleUsdGeomSchemaFloatValues(0.5f));

        RequireArrayCorners(nurbsCurves.GetPoints(), ScaleUsdGeomSchemaVec3Values(3.0f), "nurbs points");
        RequireArrayCorners(nurbsCurves.GetCurveVertexCounts(), UsdGeomSchemaBulkIntValues, "nurbs vertex counts");
        RequireArrayCorners(nurbsCurves.GetOrder(), [2, 3, 4, 5, 6], "nurbs order");
        RequireArrayCorners(nurbsCurves.GetKnots(), UsdGeomSchemaBulkDoubleValues, "nurbs knots");
        RequireArrayCorners(nurbsCurves.GetWidths(), ScaleUsdGeomSchemaFloatValues(0.5f), "nurbs widths");

        UsdGeomHermiteCurves hermiteCurves = stage.DefineHermiteCurves("/HermiteCurves");
        hermiteCurves.SetPoints(UsdGeomSchemaBulkVec3Values);
        hermiteCurves.SetCurveVertexCounts(UsdGeomSchemaBulkIntValues);
        hermiteCurves.SetTangents(ScaleUsdGeomSchemaVec3Values(-1.0f));
        hermiteCurves.SetWidths(ScaleUsdGeomSchemaFloatValues(2.0f));

        RequireArrayCorners(hermiteCurves.GetPoints(), UsdGeomSchemaBulkVec3Values, "hermite points");
        RequireArrayCorners(
            hermiteCurves.GetCurveVertexCounts(),
            UsdGeomSchemaBulkIntValues,
            "hermite vertex counts");
        RequireArrayCorners(hermiteCurves.GetTangents(), ScaleUsdGeomSchemaVec3Values(-1.0f), "hermite tangents");
        RequireArrayCorners(hermiteCurves.GetWidths(), ScaleUsdGeomSchemaFloatValues(2.0f), "hermite widths");

        UsdGeomNurbsPatch nurbsPatch = stage.DefineNurbsPatch("/NurbsPatch");
        nurbsPatch.SetPoints(ScaleUsdGeomSchemaVec3Values(4.0f));
        nurbsPatch.UVertexCount = 5;
        nurbsPatch.VVertexCount = 6;
        nurbsPatch.UOrder = 3;
        nurbsPatch.VOrder = 4;
        nurbsPatch.SetUKnots(UsdGeomSchemaBulkDoubleValues);
        nurbsPatch.SetVKnots(ScaleUsdGeomSchemaDoubleValues(2.0d));

        RequireArrayCorners(nurbsPatch.GetPoints(), ScaleUsdGeomSchemaVec3Values(4.0f), "patch points");
        RequireEqual(nurbsPatch.UVertexCount, 5, "patch u vertex count");
        RequireEqual(nurbsPatch.VVertexCount, 6, "patch v vertex count");
        RequireEqual(nurbsPatch.UOrder, 3, "patch u order");
        RequireEqual(nurbsPatch.VOrder, 4, "patch v order");
        RequireArrayCorners(nurbsPatch.GetUKnots(), UsdGeomSchemaBulkDoubleValues, "patch u knots");
        RequireArrayCorners(nurbsPatch.GetVKnots(), ScaleUsdGeomSchemaDoubleValues(2.0d), "patch v knots");
    }

    [Test]
    public void PointFacadesRoundTripBulkArraysTimeSamplesAndRelationship()
    {
        string directory = CreateDirectory(nameof(PointFacadesRoundTripBulkArraysTimeSamplesAndRelationship));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-points");

        UsdGeomPoints points = stage.DefinePoints("/Points");
        UsdVec3f[] sampledPoints = ScaleUsdGeomSchemaVec3Values(10.0f);
        points.SetPoints(UsdGeomSchemaBulkVec3Values);
        points.SetPoints(sampledPoints, timeCode: 24.0d);
        points.SetWidths(UsdGeomSchemaBulkFloatValues);
        points.SetNormals(ScaleUsdGeomSchemaVec3Values(2.0f));
        points.SetVelocities(ScaleUsdGeomSchemaVec3Values(3.0f));
        points.SetAccelerations(ScaleUsdGeomSchemaVec3Values(4.0f));
        var extent = new UsdExtent3f(new UsdVec3f(-1, -2, -3), new UsdVec3f(4, 5, 6));
        points.SetExtent(extent);

        RequireArrayCorners(points.GetPoints(), UsdGeomSchemaBulkVec3Values, "points default");
        RequireArrayCorners(points.GetPoints(24.0d), sampledPoints, "points sample");
        RequireArrayCorners(points.GetWidths(), UsdGeomSchemaBulkFloatValues, "points widths");
        RequireArrayCorners(points.GetNormals(), ScaleUsdGeomSchemaVec3Values(2.0f), "points normals");
        RequireArrayCorners(points.GetVelocities(), ScaleUsdGeomSchemaVec3Values(3.0f), "points velocities");
        RequireArrayCorners(points.GetAccelerations(), ScaleUsdGeomSchemaVec3Values(4.0f), "points accelerations");
        RequireExtent(points.GetExtent(), extent, "points extent");

        UsdGeomPointInstancer instancer = stage.DefinePointInstancer("/Instancer");
        UsdVec3f[] sampledPositions = ScaleUsdGeomSchemaVec3Values(-2.0f);
        instancer.SetPositions(UsdGeomSchemaBulkVec3Values);
        instancer.SetPositions(sampledPositions, timeCode: 12.0d);
        instancer.SetProtoIndices(UsdGeomSchemaBulkIntValues);
        instancer.SetOrientations(UsdGeomSchemaBulkQuatValues);
        instancer.SetVelocities(ScaleUsdGeomSchemaVec3Values(5.0f));
        instancer.SetScales(ScaleUsdGeomSchemaVec3Values(0.25f));
        stage.DefineCube("/PrototypeA");
        stage.DefineSphere("/PrototypeB");
        instancer.Prototypes.SetTargets(UsdGeomSchemaPrototypeTargets);

        RequireArrayCorners(instancer.GetPositions(), UsdGeomSchemaBulkVec3Values, "instancer positions");
        RequireArrayCorners(instancer.GetPositions(12.0d), sampledPositions, "instancer positions sample");
        RequireArrayCorners(instancer.GetProtoIndices(), UsdGeomSchemaBulkIntValues, "instancer proto indices");
        RequireArrayCorners(instancer.GetOrientations(), UsdGeomSchemaBulkQuatValues, "instancer orientations");
        RequireArrayCorners(instancer.GetVelocities(), ScaleUsdGeomSchemaVec3Values(5.0f), "instancer velocities");
        RequireArrayCorners(instancer.GetScales(), ScaleUsdGeomSchemaVec3Values(0.25f), "instancer scales");
        RequireSetEqual(instancer.Prototypes.GetTargets(), UsdGeomSchemaPrototypeTargets, "instancer prototypes");
    }

    [Test]
    public void PrimitiveSurfaceFacadesRoundTripScalarsAxesAndExtents()
    {
        string directory = CreateDirectory(nameof(PrimitiveSurfaceFacadesRoundTripScalarsAxesAndExtents));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-surfaces");

        UsdGeomCapsule capsule = stage.DefineCapsule("/Capsule");
        capsule.Radius = 1.25d;
        capsule.Height = 3.5d;
        capsule.Axis = UsdGeomAxis.X;
        var capsuleExtent = new UsdExtent3f(new UsdVec3f(-1, -2, -3), new UsdVec3f(1, 2, 3));
        capsule.SetExtent(capsuleExtent);
        RequireEqual(capsule.Radius, 1.25d, "capsule radius");
        RequireEqual(capsule.Height, 3.5d, "capsule height");
        RequireEqual(capsule.Axis, UsdGeomAxis.X, "capsule axis");
        RequireExtent(capsule.GetExtent(), capsuleExtent, "capsule extent");

        UsdGeomCone cone = stage.DefineCone("/Cone");
        cone.Radius = 2.25d;
        cone.Height = 4.5d;
        cone.Axis = UsdGeomAxis.Y;
        var coneExtent = new UsdExtent3f(new UsdVec3f(-2, -3, -4), new UsdVec3f(2, 3, 4));
        cone.SetExtent(coneExtent);
        RequireEqual(cone.Radius, 2.25d, "cone radius");
        RequireEqual(cone.Height, 4.5d, "cone height");
        RequireEqual(cone.Axis, UsdGeomAxis.Y, "cone axis");
        RequireExtent(cone.GetExtent(), coneExtent, "cone extent");

        UsdGeomCube cube = stage.DefineCube("/Cube");
        cube.Size = 8.75d;
        var cubeExtent = new UsdExtent3f(new UsdVec3f(-4, -4, -4), new UsdVec3f(4, 4, 4));
        cube.SetExtent(cubeExtent);
        RequireEqual(cube.Size, 8.75d, "cube size");
        RequireExtent(cube.GetExtent(), cubeExtent, "cube extent");

        UsdGeomCylinder cylinder = stage.DefineCylinder("/Cylinder");
        cylinder.Radius = 3.25d;
        cylinder.Height = 7.5d;
        cylinder.Axis = UsdGeomAxis.Z;
        var cylinderExtent = new UsdExtent3f(new UsdVec3f(-3, -3, -5), new UsdVec3f(3, 3, 5));
        cylinder.SetExtent(cylinderExtent);
        RequireEqual(cylinder.Radius, 3.25d, "cylinder radius");
        RequireEqual(cylinder.Height, 7.5d, "cylinder height");
        RequireEqual(cylinder.Axis, UsdGeomAxis.Z, "cylinder axis");
        RequireExtent(cylinder.GetExtent(), cylinderExtent, "cylinder extent");

        UsdGeomSphere sphere = stage.DefineSphere("/Sphere");
        sphere.Radius = 9.5d;
        var sphereExtent = new UsdExtent3f(new UsdVec3f(-9, -9, -9), new UsdVec3f(9, 9, 9));
        sphere.SetExtent(sphereExtent);
        RequireEqual(sphere.Radius, 9.5d, "sphere radius");
        RequireExtent(sphere.GetExtent(), sphereExtent, "sphere extent");

        UsdGeomPlane plane = stage.DefinePlane("/Plane");
        plane.Axis = UsdGeomAxis.Y;
        var planeExtent = new UsdExtent3f(new UsdVec3f(-5, 0, -6), new UsdVec3f(5, 0, 6));
        plane.SetExtent(planeExtent);
        RequireEqual(plane.Axis, UsdGeomAxis.Y, "plane axis");
        RequireExtent(plane.GetExtent(), planeExtent, "plane extent");
    }

    [Test]
    public void SubsetAndTetMeshFacadesRoundTripBulkTopologyArraysAndTokens()
    {
        string directory = CreateDirectory(nameof(SubsetAndTetMeshFacadesRoundTripBulkTopologyArraysAndTokens));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-subset-tet");

        UsdGeomSubset subset = stage.DefineSubset("/Subset");
        subset.SetIndices(UsdGeomSchemaBulkIntValues);
        subset.ElementType = "face";
        subset.FamilyName = "materialBind";

        RequireArrayCorners(subset.GetIndices(), UsdGeomSchemaBulkIntValues, "subset indices");
        RequireEqual(subset.ElementType, "face", "subset element type");
        RequireEqual(subset.FamilyName, "materialBind", "subset family name");

        UsdGeomTetMesh tetMesh = stage.DefineTetMesh("/TetMesh");
        tetMesh.SetPoints(UsdGeomSchemaBulkVec3Values);
        tetMesh.SetTetVertexIndices(UsdGeomSchemaTetVertexIndices);
        tetMesh.SetSurfaceFaceVertexIndices(UsdGeomSchemaTetSurfaceFaceVertexIndices);

        RequireArrayCorners(tetMesh.GetPoints(), UsdGeomSchemaBulkVec3Values, "tet points");
        RequireArrayCorners(tetMesh.GetTetVertexIndices(), UsdGeomSchemaTetVertexIndices, "tet vertex indices");
        RequireArrayCorners(
            tetMesh.GetSurfaceFaceVertexIndices(),
            UsdGeomSchemaTetSurfaceFaceVertexIndices,
            "tet surface indices");
    }

    [Test]
    public void PrimvarsApiRoundTripsMetadataArraysAndIndices()
    {
        string directory = CreateDirectory(nameof(PrimvarsApiRoundTripsMetadataArraysAndIndices));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-primvars");
        UsdPrim prim = stage.DefineMesh("/Mesh").Prim;

        Require(
            UsdGeomPrimvarsAPI.TryWrap(prim, out UsdGeomPrimvarsAPI wrappedApi),
            "PrimvarsAPI TryWrap failed.");
        RequireEqual(wrappedApi.Path, "/Mesh", "PrimvarsAPI path");
        UsdGeomPrimvarsAPI api = UsdGeomPrimvarsAPI.Wrap(prim);
        UsdGeomPrimvar displayColor = api.CreatePrimvar(
            "displayColor",
            UsdGeomInterpolation.FaceVarying,
            elementSize: 3);
        displayColor.SetVec3fArray(UsdGeomSchemaBulkVec3Values);
        displayColor.SetIndices([4, 3, 2, 1, 0]);
        UsdGeomPrimvar temperature = api.CreatePrimvar("temperature", UsdGeomInterpolation.Varying);
        temperature.SetFloatArray(UsdGeomSchemaBulkFloatValues);
        UsdGeomPrimvar ids = api.CreatePrimvar("ids", UsdGeomInterpolation.Uniform);
        ids.SetInt32Array(UsdGeomSchemaBulkIntValues);
        UsdGeomPrimvar uvs = api.CreatePrimvar("st", UsdGeomInterpolation.FaceVarying, elementSize: 2);
        uvs.SetVec2fArray(UsdGeomSchemaBulkVec2Values);

        RequireEqual(displayColor.PrimPath, "/Mesh", "displayColor prim path");
        RequireEqual(displayColor.Name, "displayColor", "displayColor name");
        RequireEqual(displayColor.AttributeName, "primvars:displayColor", "displayColor attribute");
        RequireEqual(displayColor.Interpolation, UsdGeomInterpolation.FaceVarying, "displayColor interpolation");
        RequireEqual(displayColor.ElementSize, 3, "displayColor element size");
        RequireArrayCorners(temperature.GetFloatArray(), UsdGeomSchemaBulkFloatValues, "primvar float values");
        RequireArrayCorners(ids.GetInt32Array(), UsdGeomSchemaBulkIntValues, "primvar int values");
        RequireArrayCorners(uvs.GetVec2fArray(), UsdGeomSchemaBulkVec2Values, "primvar vec2 values");
        RequireArrayCorners(displayColor.GetVec3fArray(), UsdGeomSchemaBulkVec3Values, "primvar vec3 values");
        RequireArrayCorners(displayColor.GetIndices(), [4, 3, 2, 1, 0], "primvar indices");

        UsdGeomPrimvar samePrimvar = api.GetPrimvar("displayColor");
        RequireEqual(samePrimvar.Interpolation, UsdGeomInterpolation.FaceVarying, "same primvar interpolation");
        RequireEqual(samePrimvar.ElementSize, 3, "same primvar element size");
        RequireArrayCorners(samePrimvar.GetVec3fArray(), UsdGeomSchemaBulkVec3Values, "named primvar vec3 values");
    }

    [Test]
    public void ModelApiRoundTripsModelMetadataAndExtentsHint()
    {
        string directory = CreateDirectory(nameof(ModelApiRoundTripsModelMetadataAndExtentsHint));
        string path = CreateUsdGeomSchemaModelComponentPath(directory);
        using UsdStage stage = UsdStage.Open(path);
        UsdPrim prim = stage.GetPrim("/Model");

        Require(UsdGeomModelAPI.TryWrap(prim, out UsdGeomModelAPI wrappedApi), "ModelAPI TryWrap failed.");
        RequireEqual(wrappedApi.Path, "/Model", "ModelAPI path");
        UsdGeomModelAPI api = UsdGeomModelAPI.Wrap(prim);
        api.DrawMode = "cards";
        api.CardGeometry = "cross";
        api.ApplyDrawMode = true;
        api.SetExtentsHint(UsdGeomSchemaBulkVec3Values);

        RequireEqual(api.DrawMode, "cards", "model draw mode");
        RequireEqual(api.CardGeometry, "cross", "model card geometry");
        Require(api.ApplyDrawMode, "model applyDrawMode was not true.");
        RequireArrayCorners(api.GetExtentsHint(), UsdGeomSchemaBulkVec3Values, "model extents hint");
        RequireEqual(api.Prim.Path, "/Model", "ModelAPI prim path");
    }

    [Test]
    public void SchemaFacadesRejectMismatchedMissingDefaultAndInvalidPaths()
    {
        string directory = CreateDirectory(nameof(SchemaFacadesRejectMismatchedMissingDefaultAndInvalidPaths));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-rejections");
        UsdPrim xform = stage.DefineXform("/PlainXform").Prim;

        (string Name, Action Wrap, Func<bool> TryWrap)[] mismatched =
        [
            (nameof(UsdGeomSubset), () => _ = UsdGeomSubset.Wrap(xform), () => UsdGeomSubset.TryWrap(xform, out _)),
            (nameof(UsdGeomBasisCurves), () => _ = UsdGeomBasisCurves.Wrap(xform),
                () => UsdGeomBasisCurves.TryWrap(xform, out _)),
            (nameof(UsdGeomNurbsCurves), () => _ = UsdGeomNurbsCurves.Wrap(xform),
                () => UsdGeomNurbsCurves.TryWrap(xform, out _)),
            (nameof(UsdGeomHermiteCurves), () => _ = UsdGeomHermiteCurves.Wrap(xform),
                () => UsdGeomHermiteCurves.TryWrap(xform, out _)),
            (nameof(UsdGeomNurbsPatch), () => _ = UsdGeomNurbsPatch.Wrap(xform),
                () => UsdGeomNurbsPatch.TryWrap(xform, out _)),
            (nameof(UsdGeomPoints), () => _ = UsdGeomPoints.Wrap(xform), () => UsdGeomPoints.TryWrap(xform, out _)),
            (nameof(UsdGeomPointInstancer), () => _ = UsdGeomPointInstancer.Wrap(xform),
                () => UsdGeomPointInstancer.TryWrap(xform, out _)),
            (nameof(UsdGeomCapsule), () => _ = UsdGeomCapsule.Wrap(xform), () => UsdGeomCapsule.TryWrap(xform, out _)),
            (nameof(UsdGeomCone), () => _ = UsdGeomCone.Wrap(xform), () => UsdGeomCone.TryWrap(xform, out _)),
            (nameof(UsdGeomCube), () => _ = UsdGeomCube.Wrap(xform), () => UsdGeomCube.TryWrap(xform, out _)),
            (nameof(UsdGeomCylinder), () => _ = UsdGeomCylinder.Wrap(xform),
                () => UsdGeomCylinder.TryWrap(xform, out _)),
            (nameof(UsdGeomSphere), () => _ = UsdGeomSphere.Wrap(xform), () => UsdGeomSphere.TryWrap(xform, out _)),
            (nameof(UsdGeomPlane), () => _ = UsdGeomPlane.Wrap(xform), () => UsdGeomPlane.TryWrap(xform, out _)),
            (nameof(UsdGeomTetMesh), () => _ = UsdGeomTetMesh.Wrap(xform), () => UsdGeomTetMesh.TryWrap(xform, out _))
        ];

        foreach ((string name, Action wrap, Func<bool> tryWrap) in mismatched)
        {
            Require(!tryWrap(), $"{name} TryWrap unexpectedly accepted an Xform prim.");
            RequireException<ArgumentException>(wrap, name);
        }

        UsdPrim missingPrim = stage.GetPrim("/Missing");
        Require(!UsdGeomCube.TryWrap(missingPrim, out _), "Cube TryWrap unexpectedly accepted a missing prim.");

        (string Name, Action Access)[] detachedAccessors =
        [
            (nameof(UsdGeomSubset), () => _ = default(UsdGeomSubset).Prim),
            (nameof(UsdGeomBasisCurves), () => _ = default(UsdGeomBasisCurves).Prim),
            (nameof(UsdGeomNurbsCurves), () => _ = default(UsdGeomNurbsCurves).Prim),
            (nameof(UsdGeomHermiteCurves), () => _ = default(UsdGeomHermiteCurves).Prim),
            (nameof(UsdGeomNurbsPatch), () => _ = default(UsdGeomNurbsPatch).Prim),
            (nameof(UsdGeomPoints), () => _ = default(UsdGeomPoints).Prim),
            (nameof(UsdGeomPointInstancer), () => _ = default(UsdGeomPointInstancer).Prim),
            (nameof(UsdGeomCapsule), () => _ = default(UsdGeomCapsule).Prim),
            (nameof(UsdGeomCone), () => _ = default(UsdGeomCone).Prim),
            (nameof(UsdGeomCube), () => _ = default(UsdGeomCube).Prim),
            (nameof(UsdGeomCylinder), () => _ = default(UsdGeomCylinder).Prim),
            (nameof(UsdGeomSphere), () => _ = default(UsdGeomSphere).Prim),
            (nameof(UsdGeomPlane), () => _ = default(UsdGeomPlane).Prim),
            (nameof(UsdGeomTetMesh), () => _ = default(UsdGeomTetMesh).Prim),
            (nameof(UsdGeomModelAPI), () => _ = default(UsdGeomModelAPI).Prim),
            (nameof(UsdGeomPrimvarsAPI), () => _ = default(UsdGeomPrimvarsAPI).Prim),
            (nameof(UsdGeomPrimvar), () => _ = default(UsdGeomPrimvar).Prim)
        ];

        foreach ((string name, Action access) in detachedAccessors)
        {
            RequireException<InvalidOperationException>(access, name);
        }

        UsdStage nullStage = null!;
        RequireException<ArgumentNullException>(() => nullStage.DefineCube("/NullCube"), "null stage DefineCube");

        string[] invalidPaths = ["", "relative", "/", "/World//Broken"];
        foreach (string invalidPath in invalidPaths)
        {
            Exception exception = CaptureUsdGeomSchemaException(() => stage.DefineCube(invalidPath));
            Require(exception is ArgumentException, $"Invalid DefineCube path was not rejected: {invalidPath}");
        }
    }

    [Test]
    public void SchemaFacadesValidatePrimvarNamesEnumsAndExtentShape()
    {
        string directory = CreateDirectory(nameof(SchemaFacadesValidatePrimvarNamesEnumsAndExtentShape));
        using UsdStage stage = CreateUsdGeomSchemaStage(directory, "geom-schema-validation");
        UsdPrim mesh = stage.DefineMesh("/Mesh").Prim;
        UsdGeomPrimvarsAPI api = new(mesh);

        string[] invalidPrimvarNames = ["", " ", "display:color", "display/color"];
        foreach (string invalidName in invalidPrimvarNames)
        {
            Exception exception = CaptureUsdGeomSchemaException(() => _ = api.GetPrimvar(invalidName));
            Require(exception is ArgumentException, $"Invalid primvar name was not rejected: {invalidName}");
        }

        UsdGeomPrimvar primvar = api.CreatePrimvar("validated");
        RequireException<ArgumentOutOfRangeException>(
            () => primvar.Interpolation = (UsdGeomInterpolation)int.MaxValue,
            "primvar interpolation");

        UsdGeomCapsule capsule = stage.DefineCapsule("/Capsule");
        RequireException<ArgumentOutOfRangeException>(
            () => capsule.Axis = (UsdGeomAxis)int.MaxValue,
            "capsule axis");

        UsdGeomSphere sphere = stage.DefineSphere("/Sphere");
        sphere.Prim.SetVec3fArray("extent", [new UsdVec3f(-1, -1, -1)]);
        Exception extentException = CaptureUsdGeomSchemaException(() => _ = sphere.GetExtent());
        Require(
            extentException is InvalidOperationException,
            $"Invalid extent shape threw {extentException.GetType().Name}, not InvalidOperationException.");
        RequireEqual(
            extentException.Message,
            "A UsdGeom extent must contain exactly two corners.",
            "invalid extent message");
    }

    private static string CreateDirectory(string testName) =>
        NativeCoverageRuntime.CreateTempDirectory(testName);

    private static UsdStage CreateUsdGeomSchemaStage(string directory, string name)
    {
        string path = Path.Combine(directory, name + ".usda");
        File.Delete(path);
        return UsdStage.Create(path);
    }

    private static string CreateUsdGeomSchemaModelComponentPath(string directory)
    {
        string path = Path.Combine(directory, "geom-schema-model.usda");
        File.Delete(path);
        File.WriteAllText(
            path,
            "#usda 1.0\n\ndef Xform \"Model\" (\n    kind = \"component\"\n)\n{\n}\n");
        return path;
    }

    private static void RequireEqual<T>(T actual, T expected, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireArrayCorners<T>(T[] actual, T[] expected, string label)
        where T : notnull
    {
        RequireEqual(actual.Length, expected.Length, label + " length");
        RequireEqual(actual[0], expected[0], label + " first");
        RequireEqual(actual[actual.Length / 2], expected[expected.Length / 2], label + " middle");
        RequireEqual(actual[^1], expected[^1], label + " last");
    }

    private static void RequireExtent(UsdExtent3f actual, UsdExtent3f expected, string label)
    {
        RequireEqual(actual.Minimum, expected.Minimum, label + " minimum");
        RequireEqual(actual.Maximum, expected.Maximum, label + " maximum");
    }

    private static void RequireSetEqual(string[] actual, string[] expected, string label)
    {
        Require(
            actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    private static void RequireException<TException>(Action action, string label)
        where TException : Exception
    {
        Exception exception = CaptureUsdGeomSchemaException(action);
        Require(
            exception is TException,
            $"{label}: expected {typeof(TException).Name}, got {exception.GetType().Name}.");
    }

    private static UsdVec3f[] CreateUsdGeomSchemaBulkVec3Values()
    {
        var values = new UsdVec3f[UsdGeomSchemaBulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = new UsdVec3f(
                index - 128.5f,
                (index * 2.0f) + 0.25f,
                (index * -3.0f) - 0.75f);
        }
        return values;
    }

    private static UsdVec2f[] CreateUsdGeomSchemaBulkVec2Values()
    {
        var values = new UsdVec2f[UsdGeomSchemaBulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = new UsdVec2f(index + 0.5f, -index - 0.25f);
        }
        return values;
    }

    private static UsdQuatf[] CreateUsdGeomSchemaBulkQuatValues()
    {
        var values = new UsdQuatf[UsdGeomSchemaBulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (index % 2) == 0
                ? UsdQuatf.Identity
                : new UsdQuatf(0.5f, 0.5f, 0.5f, 0.5f);
        }
        return values;
    }

    private static int[] CreateUsdGeomSchemaBulkIntValues()
    {
        var values = new int[UsdGeomSchemaBulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = index % 2;
        }
        return values;
    }

    private static float[] CreateUsdGeomSchemaBulkFloatValues()
    {
        var values = new float[UsdGeomSchemaBulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (index * 0.5f) - 64.25f;
        }
        return values;
    }

    private static double[] CreateUsdGeomSchemaBulkDoubleValues()
    {
        var values = new double[UsdGeomSchemaBulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (index * 0.125d) - 16.0d;
        }
        return values;
    }

    private static UsdVec3f[] ScaleUsdGeomSchemaVec3Values(float scale)
    {
        var values = new UsdVec3f[UsdGeomSchemaBulkVec3Values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            UsdVec3f value = UsdGeomSchemaBulkVec3Values[index];
            values[index] = new UsdVec3f(value.X * scale, value.Y * scale, value.Z * scale);
        }
        return values;
    }

    private static float[] ScaleUsdGeomSchemaFloatValues(float scale)
    {
        var values = new float[UsdGeomSchemaBulkFloatValues.Length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = UsdGeomSchemaBulkFloatValues[index] * scale;
        }
        return values;
    }

    private static double[] ScaleUsdGeomSchemaDoubleValues(double scale)
    {
        var values = new double[UsdGeomSchemaBulkDoubleValues.Length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = UsdGeomSchemaBulkDoubleValues[index] * scale;
        }
        return values;
    }

    private static Exception CaptureUsdGeomSchemaException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected an exception.");
    }
}
