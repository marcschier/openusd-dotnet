// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.Tests;

public sealed class UsdGeomSchemaFacadeRoundTripTests
{
    private const int BulkValueCount = 257;

    private static readonly UsdVec3f[] BulkVec3Values = CreateBulkVec3Values();
    private static readonly UsdVec2f[] BulkVec2Values = CreateBulkVec2Values();
    private static readonly UsdQuatf[] BulkQuatValues = CreateBulkQuatValues();
    private static readonly int[] BulkIntValues = CreateBulkIntValues();
    private static readonly float[] BulkFloatValues = CreateBulkFloatValues();
    private static readonly double[] BulkDoubleValues = CreateBulkDoubleValues();

    private static readonly string[] PrototypeTargets =
    [
        "/PrototypeA",
        "/PrototypeB"
    ];

    private static readonly int[] TetVertexIndices =
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

    private static readonly int[] TetSurfaceFaceVertexIndices =
    [
        0,
        1,
        2,
        2,
        3,
        0
    ];

    [Test]
    public async Task ConcreteSchemaFacadesDefineTypedPrimsAndCanBeWrapped()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;

        UsdGeomSubset subset = stage.DefineSubset("/Subset");
        await Assert.That(subset.Path).IsEqualTo("/Subset");
        await Assert.That(subset.Prim.TypeName).IsEqualTo("GeomSubset");
        await Assert.That(UsdGeomSubset.TryWrap(subset.Prim, out UsdGeomSubset wrappedSubset))
            .IsTrue();
        await Assert.That(wrappedSubset.Path).IsEqualTo(subset.Path);
        await Assert.That(UsdGeomSubset.Wrap(subset.Prim).Imageable.Prim.Path)
            .IsEqualTo(subset.Path);

        UsdGeomBasisCurves basisCurves = stage.DefineBasisCurves("/BasisCurves");
        await Assert.That(basisCurves.Prim.TypeName).IsEqualTo("BasisCurves");
        await Assert.That(UsdGeomBasisCurves.TryWrap(
                basisCurves.Prim,
                out UsdGeomBasisCurves wrappedBasisCurves))
            .IsTrue();
        await Assert.That(wrappedBasisCurves.Xformable.Prim.Path)
            .IsEqualTo(basisCurves.Path);

        UsdGeomNurbsCurves nurbsCurves = stage.DefineNurbsCurves("/NurbsCurves");
        await Assert.That(nurbsCurves.Prim.TypeName).IsEqualTo("NurbsCurves");
        await Assert.That(UsdGeomNurbsCurves.TryWrap(
                nurbsCurves.Prim,
                out UsdGeomNurbsCurves wrappedNurbsCurves))
            .IsTrue();
        await Assert.That(UsdGeomNurbsCurves.Wrap(nurbsCurves.Prim).Path)
            .IsEqualTo(wrappedNurbsCurves.Path);

        UsdGeomHermiteCurves hermiteCurves = stage.DefineHermiteCurves("/HermiteCurves");
        await Assert.That(hermiteCurves.Prim.TypeName).IsEqualTo("HermiteCurves");
        await Assert.That(UsdGeomHermiteCurves.TryWrap(
                hermiteCurves.Prim,
                out UsdGeomHermiteCurves wrappedHermiteCurves))
            .IsTrue();
        await Assert.That(wrappedHermiteCurves.Prim.Path).IsEqualTo(hermiteCurves.Path);

        UsdGeomNurbsPatch nurbsPatch = stage.DefineNurbsPatch("/NurbsPatch");
        await Assert.That(nurbsPatch.Prim.TypeName).IsEqualTo("NurbsPatch");
        await Assert.That(UsdGeomNurbsPatch.TryWrap(
                nurbsPatch.Prim,
                out UsdGeomNurbsPatch wrappedNurbsPatch))
            .IsTrue();
        await Assert.That(wrappedNurbsPatch.Path).IsEqualTo(nurbsPatch.Path);

        UsdGeomPoints points = stage.DefinePoints("/Points");
        await Assert.That(points.Prim.TypeName).IsEqualTo("Points");
        await Assert.That(UsdGeomPoints.TryWrap(points.Prim, out UsdGeomPoints wrappedPoints))
            .IsTrue();
        await Assert.That(wrappedPoints.Path).IsEqualTo(points.Path);

        UsdGeomPointInstancer instancer = stage.DefinePointInstancer("/Instancer");
        await Assert.That(instancer.Prim.TypeName).IsEqualTo("PointInstancer");
        await Assert.That(UsdGeomPointInstancer.TryWrap(
                instancer.Prim,
                out UsdGeomPointInstancer wrappedInstancer))
            .IsTrue();
        await Assert.That(wrappedInstancer.Path).IsEqualTo(instancer.Path);

        UsdGeomCapsule capsule = stage.DefineCapsule("/Capsule");
        await Assert.That(capsule.Prim.TypeName).IsEqualTo("Capsule");
        await Assert.That(UsdGeomCapsule.TryWrap(
                capsule.Prim,
                out UsdGeomCapsule wrappedCapsule))
            .IsTrue();
        await Assert.That(wrappedCapsule.Path).IsEqualTo(capsule.Path);

        UsdGeomCone cone = stage.DefineCone("/Cone");
        await Assert.That(cone.Prim.TypeName).IsEqualTo("Cone");
        await Assert.That(UsdGeomCone.TryWrap(cone.Prim, out UsdGeomCone wrappedCone))
            .IsTrue();
        await Assert.That(wrappedCone.Path).IsEqualTo(cone.Path);

        UsdGeomCube cube = stage.DefineCube("/Cube");
        await Assert.That(cube.Prim.TypeName).IsEqualTo("Cube");
        await Assert.That(UsdGeomCube.TryWrap(cube.Prim, out UsdGeomCube wrappedCube))
            .IsTrue();
        await Assert.That(wrappedCube.Path).IsEqualTo(cube.Path);

        UsdGeomCylinder cylinder = stage.DefineCylinder("/Cylinder");
        await Assert.That(cylinder.Prim.TypeName).IsEqualTo("Cylinder");
        await Assert.That(UsdGeomCylinder.TryWrap(
                cylinder.Prim,
                out UsdGeomCylinder wrappedCylinder))
            .IsTrue();
        await Assert.That(wrappedCylinder.Path).IsEqualTo(cylinder.Path);

        UsdGeomSphere sphere = stage.DefineSphere("/Sphere");
        await Assert.That(sphere.Prim.TypeName).IsEqualTo("Sphere");
        await Assert.That(UsdGeomSphere.TryWrap(sphere.Prim, out UsdGeomSphere wrappedSphere))
            .IsTrue();
        await Assert.That(wrappedSphere.Path).IsEqualTo(sphere.Path);

        UsdGeomPlane plane = stage.DefinePlane("/Plane");
        await Assert.That(plane.Prim.TypeName).IsEqualTo("Plane");
        await Assert.That(UsdGeomPlane.TryWrap(plane.Prim, out UsdGeomPlane wrappedPlane))
            .IsTrue();
        await Assert.That(wrappedPlane.Path).IsEqualTo(plane.Path);

        UsdGeomTetMesh tetMesh = stage.DefineTetMesh("/TetMesh");
        await Assert.That(tetMesh.Prim.TypeName).IsEqualTo("TetMesh");
        await Assert.That(UsdGeomTetMesh.TryWrap(tetMesh.Prim, out UsdGeomTetMesh wrappedTetMesh))
            .IsTrue();
        await Assert.That(wrappedTetMesh.Path).IsEqualTo(tetMesh.Path);
    }

    [Test]
    public async Task CurveFacadesRoundTripBulkArraysAndTokens()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;

        UsdGeomBasisCurves basisCurves = stage.DefineBasisCurves("/BasisCurves");
        basisCurves.SetPoints(BulkVec3Values);
        basisCurves.SetCurveVertexCounts(BulkIntValues);
        basisCurves.SetWidths(BulkFloatValues);
        basisCurves.SetNormals(ScaleVec3Values(2.0f));
        basisCurves.Type = "cubic";
        basisCurves.Basis = "bezier";
        basisCurves.WrapMode = "pinned";

        await AssertArrayCorners(basisCurves.GetPoints(), BulkVec3Values, "basis points");
        await AssertArrayCorners(
            basisCurves.GetCurveVertexCounts(),
            BulkIntValues,
            "basis vertex counts");
        await AssertArrayCorners(basisCurves.GetWidths(), BulkFloatValues, "basis widths");
        await AssertArrayCorners(
            basisCurves.GetNormals(),
            ScaleVec3Values(2.0f),
            "basis normals");
        await Assert.That(basisCurves.Type).IsEqualTo("cubic");
        await Assert.That(basisCurves.Basis).IsEqualTo("bezier");
        await Assert.That(basisCurves.WrapMode).IsEqualTo("pinned");

        UsdGeomNurbsCurves nurbsCurves = stage.DefineNurbsCurves("/NurbsCurves");
        nurbsCurves.SetPoints(ScaleVec3Values(3.0f));
        nurbsCurves.SetCurveVertexCounts(BulkIntValues);
        nurbsCurves.SetOrder([2, 3, 4, 5, 6]);
        nurbsCurves.SetKnots(BulkDoubleValues);
        nurbsCurves.SetWidths(ScaleFloatValues(0.5f));

        await AssertArrayCorners(
            nurbsCurves.GetPoints(),
            ScaleVec3Values(3.0f),
            "nurbs points");
        await AssertArrayCorners(
            nurbsCurves.GetCurveVertexCounts(),
            BulkIntValues,
            "nurbs vertex counts");
        await AssertArrayCorners(nurbsCurves.GetOrder(), [2, 3, 4, 5, 6], "nurbs order");
        await AssertArrayCorners(nurbsCurves.GetKnots(), BulkDoubleValues, "nurbs knots");
        await AssertArrayCorners(
            nurbsCurves.GetWidths(),
            ScaleFloatValues(0.5f),
            "nurbs widths");

        UsdGeomHermiteCurves hermiteCurves = stage.DefineHermiteCurves("/HermiteCurves");
        hermiteCurves.SetPoints(BulkVec3Values);
        hermiteCurves.SetCurveVertexCounts(BulkIntValues);
        hermiteCurves.SetTangents(ScaleVec3Values(-1.0f));
        hermiteCurves.SetWidths(ScaleFloatValues(2.0f));

        await AssertArrayCorners(hermiteCurves.GetPoints(), BulkVec3Values, "hermite points");
        await AssertArrayCorners(
            hermiteCurves.GetCurveVertexCounts(),
            BulkIntValues,
            "hermite vertex counts");
        await AssertArrayCorners(
            hermiteCurves.GetTangents(),
            ScaleVec3Values(-1.0f),
            "hermite tangents");
        await AssertArrayCorners(
            hermiteCurves.GetWidths(),
            ScaleFloatValues(2.0f),
            "hermite widths");

        UsdGeomNurbsPatch nurbsPatch = stage.DefineNurbsPatch("/NurbsPatch");
        nurbsPatch.SetPoints(ScaleVec3Values(4.0f));
        nurbsPatch.UVertexCount = 5;
        nurbsPatch.VVertexCount = 6;
        nurbsPatch.UOrder = 3;
        nurbsPatch.VOrder = 4;
        nurbsPatch.SetUKnots(BulkDoubleValues);
        nurbsPatch.SetVKnots(ScaleDoubleValues(2.0d));

        await AssertArrayCorners(
            nurbsPatch.GetPoints(),
            ScaleVec3Values(4.0f),
            "patch points");
        await Assert.That(nurbsPatch.UVertexCount).IsEqualTo(5);
        await Assert.That(nurbsPatch.VVertexCount).IsEqualTo(6);
        await Assert.That(nurbsPatch.UOrder).IsEqualTo(3);
        await Assert.That(nurbsPatch.VOrder).IsEqualTo(4);
        await AssertArrayCorners(nurbsPatch.GetUKnots(), BulkDoubleValues, "patch u knots");
        await AssertArrayCorners(
            nurbsPatch.GetVKnots(),
            ScaleDoubleValues(2.0d),
            "patch v knots");
    }

    [Test]
    public async Task PointFacadesRoundTripBulkArraysTimeSamplesAndRelationship()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;

        UsdGeomPoints points = stage.DefinePoints("/Points");
        UsdVec3f[] defaultPoints = BulkVec3Values;
        UsdVec3f[] sampledPoints = ScaleVec3Values(10.0f);
        points.SetPoints(defaultPoints);
        points.SetPoints(sampledPoints, timeCode: 24.0d);
        points.SetWidths(BulkFloatValues);
        points.SetNormals(ScaleVec3Values(2.0f));
        points.SetVelocities(ScaleVec3Values(3.0f));
        points.SetAccelerations(ScaleVec3Values(4.0f));
        var extent = new UsdExtent3f(new UsdVec3f(-1, -2, -3), new UsdVec3f(4, 5, 6));
        points.SetExtent(extent);

        await AssertArrayCorners(points.GetPoints(), defaultPoints, "points default");
        await AssertArrayCorners(points.GetPoints(24.0d), sampledPoints, "points sample");
        await AssertArrayCorners(points.GetWidths(), BulkFloatValues, "points widths");
        await AssertArrayCorners(points.GetNormals(), ScaleVec3Values(2.0f), "points normals");
        await AssertArrayCorners(
            points.GetVelocities(),
            ScaleVec3Values(3.0f),
            "points velocities");
        await AssertArrayCorners(
            points.GetAccelerations(),
            ScaleVec3Values(4.0f),
            "points accelerations");
        await AssertExtent(points.GetExtent(), extent);

        UsdGeomPointInstancer instancer = stage.DefinePointInstancer("/Instancer");
        UsdVec3f[] sampledPositions = ScaleVec3Values(-2.0f);
        instancer.SetPositions(BulkVec3Values);
        instancer.SetPositions(sampledPositions, timeCode: 12.0d);
        instancer.SetProtoIndices(BulkIntValues);
        instancer.SetOrientations(BulkQuatValues);
        instancer.SetVelocities(ScaleVec3Values(5.0f));
        instancer.SetScales(ScaleVec3Values(0.25f));
        stage.DefineCube("/PrototypeA");
        stage.DefineSphere("/PrototypeB");
        instancer.Prototypes.SetTargets(PrototypeTargets);

        await AssertArrayCorners(instancer.GetPositions(), BulkVec3Values, "instancer positions");
        await AssertArrayCorners(
            instancer.GetPositions(12.0d),
            sampledPositions,
            "instancer positions sample");
        await AssertArrayCorners(
            instancer.GetProtoIndices(),
            BulkIntValues,
            "instancer proto indices");
        await AssertArrayCorners(
            instancer.GetOrientations(),
            BulkQuatValues,
            "instancer orientations");
        await AssertArrayCorners(
            instancer.GetVelocities(),
            ScaleVec3Values(5.0f),
            "instancer velocities");
        await AssertArrayCorners(
            instancer.GetScales(),
            ScaleVec3Values(0.25f),
            "instancer scales");
        await Assert.That(instancer.Prototypes.GetTargets())
            .IsEquivalentTo(PrototypeTargets);
    }

    [Test]
    public async Task PrimitiveSurfaceFacadesRoundTripScalarsAxesAndExtents()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;

        UsdGeomCapsule capsule = stage.DefineCapsule("/Capsule");
        capsule.Radius = 1.25d;
        capsule.Height = 3.5d;
        capsule.Axis = UsdGeomAxis.X;
        var capsuleExtent = new UsdExtent3f(new UsdVec3f(-1, -2, -3), new UsdVec3f(1, 2, 3));
        capsule.SetExtent(capsuleExtent);
        await Assert.That(capsule.Radius).IsEqualTo(1.25d);
        await Assert.That(capsule.Height).IsEqualTo(3.5d);
        await Assert.That(capsule.Axis).IsEqualTo(UsdGeomAxis.X);
        await AssertExtent(capsule.GetExtent(), capsuleExtent);

        UsdGeomCone cone = stage.DefineCone("/Cone");
        cone.Radius = 2.25d;
        cone.Height = 4.5d;
        cone.Axis = UsdGeomAxis.Y;
        var coneExtent = new UsdExtent3f(new UsdVec3f(-2, -3, -4), new UsdVec3f(2, 3, 4));
        cone.SetExtent(coneExtent);
        await Assert.That(cone.Radius).IsEqualTo(2.25d);
        await Assert.That(cone.Height).IsEqualTo(4.5d);
        await Assert.That(cone.Axis).IsEqualTo(UsdGeomAxis.Y);
        await AssertExtent(cone.GetExtent(), coneExtent);

        UsdGeomCube cube = stage.DefineCube("/Cube");
        cube.Size = 8.75d;
        var cubeExtent = new UsdExtent3f(new UsdVec3f(-4, -4, -4), new UsdVec3f(4, 4, 4));
        cube.SetExtent(cubeExtent);
        await Assert.That(cube.Size).IsEqualTo(8.75d);
        await AssertExtent(cube.GetExtent(), cubeExtent);

        UsdGeomCylinder cylinder = stage.DefineCylinder("/Cylinder");
        cylinder.Radius = 3.25d;
        cylinder.Height = 7.5d;
        cylinder.Axis = UsdGeomAxis.Z;
        var cylinderExtent = new UsdExtent3f(
            new UsdVec3f(-3, -3, -5),
            new UsdVec3f(3, 3, 5));
        cylinder.SetExtent(cylinderExtent);
        await Assert.That(cylinder.Radius).IsEqualTo(3.25d);
        await Assert.That(cylinder.Height).IsEqualTo(7.5d);
        await Assert.That(cylinder.Axis).IsEqualTo(UsdGeomAxis.Z);
        await AssertExtent(cylinder.GetExtent(), cylinderExtent);

        UsdGeomSphere sphere = stage.DefineSphere("/Sphere");
        sphere.Radius = 9.5d;
        var sphereExtent = new UsdExtent3f(new UsdVec3f(-9, -9, -9), new UsdVec3f(9, 9, 9));
        sphere.SetExtent(sphereExtent);
        await Assert.That(sphere.Radius).IsEqualTo(9.5d);
        await AssertExtent(sphere.GetExtent(), sphereExtent);

        UsdGeomPlane plane = stage.DefinePlane("/Plane");
        plane.Axis = UsdGeomAxis.Y;
        var planeExtent = new UsdExtent3f(new UsdVec3f(-5, 0, -6), new UsdVec3f(5, 0, 6));
        plane.SetExtent(planeExtent);
        await Assert.That(plane.Axis).IsEqualTo(UsdGeomAxis.Y);
        await AssertExtent(plane.GetExtent(), planeExtent);
    }

    [Test]
    public async Task SubsetAndTetMeshFacadesRoundTripBulkTopologyArraysAndTokens()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;

        UsdGeomSubset subset = stage.DefineSubset("/Subset");
        subset.SetIndices(BulkIntValues);
        subset.ElementType = "face";
        subset.FamilyName = "materialBind";

        await AssertArrayCorners(subset.GetIndices(), BulkIntValues, "subset indices");
        await Assert.That(subset.ElementType).IsEqualTo("face");
        await Assert.That(subset.FamilyName).IsEqualTo("materialBind");

        UsdGeomTetMesh tetMesh = stage.DefineTetMesh("/TetMesh");
        tetMesh.SetPoints(BulkVec3Values);
        tetMesh.SetTetVertexIndices(TetVertexIndices);
        tetMesh.SetSurfaceFaceVertexIndices(TetSurfaceFaceVertexIndices);

        await AssertArrayCorners(tetMesh.GetPoints(), BulkVec3Values, "tet points");
        await AssertArrayCorners(
            tetMesh.GetTetVertexIndices(),
            TetVertexIndices,
            "tet vertex indices");
        await AssertArrayCorners(
            tetMesh.GetSurfaceFaceVertexIndices(),
            TetSurfaceFaceVertexIndices,
            "tet surface indices");
    }

    [Test]
    public async Task PrimvarsApiRoundTripsMetadataArraysAndIndices()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;
        UsdPrim prim = stage.DefineMesh("/Mesh").Prim;

        await Assert.That(UsdGeomPrimvarsAPI.TryWrap(prim, out UsdGeomPrimvarsAPI wrappedApi))
            .IsTrue();
        await Assert.That(wrappedApi.Path).IsEqualTo("/Mesh");
        UsdGeomPrimvarsAPI api = UsdGeomPrimvarsAPI.Wrap(prim);
        UsdGeomPrimvar displayColor = api.CreatePrimvar(
            "displayColor",
            UsdGeomInterpolation.FaceVarying,
            elementSize: 3);
        displayColor.SetVec3fArray(BulkVec3Values);
        displayColor.SetIndices([4, 3, 2, 1, 0]);
        UsdGeomPrimvar temperature = api.CreatePrimvar(
            "temperature",
            UsdGeomInterpolation.Varying);
        temperature.SetFloatArray(BulkFloatValues);
        UsdGeomPrimvar ids = api.CreatePrimvar("ids", UsdGeomInterpolation.Uniform);
        ids.SetInt32Array(BulkIntValues);
        UsdGeomPrimvar uvs = api.CreatePrimvar(
            "st",
            UsdGeomInterpolation.FaceVarying,
            elementSize: 2);
        uvs.SetVec2fArray(BulkVec2Values);

        await Assert.That(displayColor.PrimPath).IsEqualTo("/Mesh");
        await Assert.That(displayColor.Name).IsEqualTo("displayColor");
        await Assert.That(displayColor.AttributeName).IsEqualTo("primvars:displayColor");
        await Assert.That(displayColor.Interpolation)
            .IsEqualTo(UsdGeomInterpolation.FaceVarying);
        await Assert.That(displayColor.ElementSize).IsEqualTo(3);
        await AssertArrayCorners(
            temperature.GetFloatArray(),
            BulkFloatValues,
            "primvar float values");
        await AssertArrayCorners(
            ids.GetInt32Array(),
            BulkIntValues,
            "primvar int values");
        await AssertArrayCorners(
            uvs.GetVec2fArray(),
            BulkVec2Values,
            "primvar vec2 values");
        await AssertArrayCorners(
            displayColor.GetVec3fArray(),
            BulkVec3Values,
            "primvar vec3 values");
        await AssertArrayCorners(displayColor.GetIndices(), [4, 3, 2, 1, 0], "primvar indices");

        UsdGeomPrimvar samePrimvar = api.GetPrimvar("displayColor");
        await Assert.That(samePrimvar.Interpolation)
            .IsEqualTo(UsdGeomInterpolation.FaceVarying);
        await Assert.That(samePrimvar.ElementSize).IsEqualTo(3);
        await AssertArrayCorners(
            samePrimvar.GetVec3fArray(),
            BulkVec3Values,
            "named primvar vec3 values");
    }

    [Test]
    public async Task ModelApiRoundTripsModelMetadataAndExtentsHint()
    {
        using TempStage temp = TempStage.CreateModelComponent();
        UsdStage stage = temp.Stage;
        UsdPrim prim = stage.GetPrim("/Model");

        await Assert.That(UsdGeomModelAPI.TryWrap(prim, out UsdGeomModelAPI wrappedApi))
            .IsTrue();
        await Assert.That(wrappedApi.Path).IsEqualTo("/Model");
        UsdGeomModelAPI api = UsdGeomModelAPI.Wrap(prim);
        api.DrawMode = "cards";
        api.CardGeometry = "cross";
        api.ApplyDrawMode = true;
        api.SetExtentsHint(BulkVec3Values);

        await Assert.That(api.DrawMode).IsEqualTo("cards");
        await Assert.That(api.CardGeometry).IsEqualTo("cross");
        await Assert.That(api.ApplyDrawMode).IsTrue();
        await AssertArrayCorners(api.GetExtentsHint(), BulkVec3Values, "model extents hint");
        await Assert.That(api.Prim.Path).IsEqualTo("/Model");
    }

    [Test]
    public async Task SchemaFacadesRejectMismatchedMissingDefaultAndInvalidPaths()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;
        UsdPrim xform = stage.DefineXform("/PlainXform").Prim;

        (string Name, Action Wrap, Func<bool> TryWrap)[] mismatched =
        [
            (nameof(UsdGeomSubset),
                () => _ = UsdGeomSubset.Wrap(xform),
                () => UsdGeomSubset.TryWrap(xform, out _)),
            (nameof(UsdGeomBasisCurves),
                () => _ = UsdGeomBasisCurves.Wrap(xform),
                () => UsdGeomBasisCurves.TryWrap(xform, out _)),
            (nameof(UsdGeomNurbsCurves),
                () => _ = UsdGeomNurbsCurves.Wrap(xform),
                () => UsdGeomNurbsCurves.TryWrap(xform, out _)),
            (nameof(UsdGeomHermiteCurves),
                () => _ = UsdGeomHermiteCurves.Wrap(xform),
                () => UsdGeomHermiteCurves.TryWrap(xform, out _)),
            (nameof(UsdGeomNurbsPatch),
                () => _ = UsdGeomNurbsPatch.Wrap(xform),
                () => UsdGeomNurbsPatch.TryWrap(xform, out _)),
            (nameof(UsdGeomPoints),
                () => _ = UsdGeomPoints.Wrap(xform),
                () => UsdGeomPoints.TryWrap(xform, out _)),
            (nameof(UsdGeomPointInstancer),
                () => _ = UsdGeomPointInstancer.Wrap(xform),
                () => UsdGeomPointInstancer.TryWrap(xform, out _)),
            (nameof(UsdGeomCapsule),
                () => _ = UsdGeomCapsule.Wrap(xform),
                () => UsdGeomCapsule.TryWrap(xform, out _)),
            (nameof(UsdGeomCone),
                () => _ = UsdGeomCone.Wrap(xform),
                () => UsdGeomCone.TryWrap(xform, out _)),
            (nameof(UsdGeomCube),
                () => _ = UsdGeomCube.Wrap(xform),
                () => UsdGeomCube.TryWrap(xform, out _)),
            (nameof(UsdGeomCylinder),
                () => _ = UsdGeomCylinder.Wrap(xform),
                () => UsdGeomCylinder.TryWrap(xform, out _)),
            (nameof(UsdGeomSphere),
                () => _ = UsdGeomSphere.Wrap(xform),
                () => UsdGeomSphere.TryWrap(xform, out _)),
            (nameof(UsdGeomPlane),
                () => _ = UsdGeomPlane.Wrap(xform),
                () => UsdGeomPlane.TryWrap(xform, out _)),
            (nameof(UsdGeomTetMesh),
                () => _ = UsdGeomTetMesh.Wrap(xform),
                () => UsdGeomTetMesh.TryWrap(xform, out _))
        ];

        foreach ((string name, Action wrap, Func<bool> tryWrap) in mismatched)
        {
            await Assert.That(tryWrap()).IsFalse().Because(name);
            Exception exception = Capture(wrap);
            await Assert.That(exception).IsTypeOf<ArgumentException>().Because(name);
        }

        UsdPrim missingPrim = stage.GetPrim("/Missing");
        await Assert.That(UsdGeomCube.TryWrap(missingPrim, out _)).IsFalse();

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
            Exception exception = Capture(access);
            await Assert.That(exception).IsTypeOf<InvalidOperationException>().Because(name);
        }

        UsdStage nullStage = null!;
        Exception nullStageException = Capture(() => nullStage.DefineCube("/NullCube"));
        await Assert.That(nullStageException).IsTypeOf<ArgumentNullException>();

        string[] invalidPaths = ["", "relative", "/", "/World//Broken"];
        foreach (string invalidPath in invalidPaths)
        {
            Exception exception = Capture(() => stage.DefineCube(invalidPath));
            await Assert.That(exception is ArgumentException).IsTrue().Because(invalidPath);
        }
    }

    [Test]
    public async Task SchemaFacadesValidatePrimvarNamesEnumsAndExtentShape()
    {
        using TempStage temp = TempStage.Create();
        UsdStage stage = temp.Stage;
        UsdPrim mesh = stage.DefineMesh("/Mesh").Prim;
        UsdGeomPrimvarsAPI api = new(mesh);

        string[] invalidPrimvarNames = ["", " ", "display:color", "display/color"];
        foreach (string invalidName in invalidPrimvarNames)
        {
            Exception exception = Capture(() => _ = api.GetPrimvar(invalidName));
            await Assert.That(exception is ArgumentException).IsTrue().Because(invalidName);
        }

        UsdGeomPrimvar primvar = api.CreatePrimvar("validated");
        Exception interpolationException = Capture(
            () => primvar.Interpolation = (UsdGeomInterpolation)int.MaxValue);
        await Assert.That(interpolationException).IsTypeOf<ArgumentOutOfRangeException>();

        UsdGeomCapsule capsule = stage.DefineCapsule("/Capsule");
        Exception axisException = Capture(() => capsule.Axis = (UsdGeomAxis)int.MaxValue);
        await Assert.That(axisException).IsTypeOf<ArgumentOutOfRangeException>();

        UsdGeomSphere sphere = stage.DefineSphere("/Sphere");
        sphere.Prim.SetVec3fArray("extent", [new UsdVec3f(-1, -1, -1)]);
        Exception extentException = Capture(() => _ = sphere.GetExtent());
        await Assert.That(extentException).IsTypeOf<InvalidOperationException>();
        await Assert.That(extentException.Message)
            .IsEqualTo("A UsdGeom extent must contain exactly two corners.");
    }

    private static async Task AssertArrayCorners<T>(T[] actual, T[] expected, string label)
        where T : notnull
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length).Because(label);
        await Assert.That(actual[0]).IsEqualTo(expected[0]).Because(label + " first");
        await Assert.That(actual[actual.Length / 2])
            .IsEqualTo(expected[expected.Length / 2])
            .Because(label + " middle");
        await Assert.That(actual[^1]).IsEqualTo(expected[^1]).Because(label + " last");
    }

    private static async Task AssertExtent(UsdExtent3f actual, UsdExtent3f expected)
    {
        await Assert.That(actual.Minimum).IsEqualTo(expected.Minimum);
        await Assert.That(actual.Maximum).IsEqualTo(expected.Maximum);
    }

    private static UsdVec3f[] CreateBulkVec3Values()
    {
        var values = new UsdVec3f[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = new UsdVec3f(
                index - 128.5f,
                (index * 2.0f) + 0.25f,
                (index * -3.0f) - 0.75f);
        }

        return values;
    }

    private static UsdVec2f[] CreateBulkVec2Values()
    {
        var values = new UsdVec2f[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = new UsdVec2f(index + 0.5f, -index - 0.25f);
        }

        return values;
    }

    private static UsdQuatf[] CreateBulkQuatValues()
    {
        var values = new UsdQuatf[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (index % 2) == 0
                ? UsdQuatf.Identity
                : new UsdQuatf(0.5f, 0.5f, 0.5f, 0.5f);
        }

        return values;
    }

    private static int[] CreateBulkIntValues()
    {
        var values = new int[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = index % 2;
        }

        return values;
    }

    private static float[] CreateBulkFloatValues()
    {
        var values = new float[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (index * 0.5f) - 64.25f;
        }

        return values;
    }

    private static double[] CreateBulkDoubleValues()
    {
        var values = new double[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (index * 0.125d) - 16.0d;
        }

        return values;
    }

    private static UsdVec3f[] ScaleVec3Values(float scale)
    {
        var values = new UsdVec3f[BulkVec3Values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            UsdVec3f value = BulkVec3Values[index];
            values[index] = new UsdVec3f(value.X * scale, value.Y * scale, value.Z * scale);
        }

        return values;
    }

    private static float[] ScaleFloatValues(float scale)
    {
        var values = new float[BulkFloatValues.Length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BulkFloatValues[index] * scale;
        }

        return values;
    }

    private static double[] ScaleDoubleValues(double scale)
    {
        var values = new double[BulkDoubleValues.Length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BulkDoubleValues[index] * scale;
        }

        return values;
    }

    private static Exception Capture(Action action)
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

    private sealed class TempStage : IDisposable
    {
        private readonly string _path;

        private TempStage(string path)
        {
            _path = path;
            Stage = UsdStage.Create(path);
        }

        public UsdStage Stage { get; }

        public static TempStage Create()
        {
            string directory = GetStageDirectory();
            Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(
                directory,
                "geom-schema-" + Guid.NewGuid().ToString("N") + ".usda");
            return new TempStage(path);
        }

        public static TempStage CreateModelComponent()
        {
            string directory = GetStageDirectory();
            Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(
                directory,
                "geom-model-" + Guid.NewGuid().ToString("N") + ".usda");
            File.WriteAllText(
                path,
                "#usda 1.0\n\ndef Xform \"Model\" (\n    kind = \"component\"\n)\n{\n}\n");
            return new TempStage(path, openExisting: true);
        }

        private static string GetStageDirectory()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new InvalidOperationException("Could not locate the repository root.");
            }

            return System.IO.Path.Combine(directory.FullName, "artifacts", "test-stages", "OpenUsd.Tests");
        }

        private TempStage(string path, bool openExisting)
        {
            _path = path;
            Stage = openExisting ? UsdStage.Open(path) : UsdStage.Create(path);
        }

        public void Dispose()
        {
            Stage.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
