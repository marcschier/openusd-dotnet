// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Pins that every simulated point a stage authors arrives on the build page in metres and at the
/// authored size.
/// </summary>
/// <remarks>
/// <para>
/// An extracted point is authored, local, and in stage units, while every other length on the build
/// page - a pose, an extent, a rest offset - is metres. A particle buffer and a deformable vertex
/// buffer are positions rather than shapes, so nothing downstream can apply a stage unit scale or an
/// authored prim scale for them: both have to be baked in during composition or the simulated object
/// is the wrong size and in the wrong place, silently, on any stage that is not authored in metres at
/// unit scale.
/// </para>
/// <para>
/// Each test therefore authors the same geometry twice, once as a rigid analytic collider whose
/// extent extraction already converts, and once as a simulated point set, and requires the two to
/// agree. Comparing against a rigid reference is what makes the assertion about the whole pipeline
/// rather than about one multiplication.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsGpuUnitScaleTests
{
    /// <summary>Centimetre stages, the most common authoring unit after metres.</summary>
    private const double Centimetres = 0.01;

    [Test]
    public async Task ParticleSetPointsAreComposedInMetres()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(ParticleSetPointsAreComposedInMetres));
        fixture.WriteStage(UnitStage(Centimetres, scale: 1.0));

        (PhysxPageBuilder builder, _) = Compose(fixture);
        PhysxVec3f[] points = ParticlePoints(builder);

        // The stage authors the far particle at 300 centimetres, which is three
        // metres, and the rigid reference cube is authored to the same place.
        await Assert.That(points.Length).IsEqualTo(2);
        await Assert.That(Math.Abs(points[0].X - 1.0F)).IsLessThan(1e-4F);
        await Assert.That(Math.Abs(points[1].X - 3.0F)).IsLessThan(1e-4F);
        await Assert.That(Math.Abs(points[1].Y - 2.0F)).IsLessThan(1e-4F);
        await Assert.That(Math.Abs(ReferenceHalfExtentX(builder) - 1.5F)).IsLessThan(1e-4F);
    }

    [Test]
    public async Task ParticleSetPointsAbsorbTheAuthoredScale()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(ParticleSetPointsAbsorbTheAuthoredScale));
        fixture.WriteStage(UnitStage(Centimetres, scale: 2.0));

        (PhysxPageBuilder builder, _) = Compose(fixture);
        PhysxVec3f[] points = ParticlePoints(builder);

        // The prim is scaled by two, so the same authored offsets describe an
        // object twice the size. Nothing downstream can scale a particle buffer,
        // so the scale has to already be in the staged point.
        await Assert.That(Math.Abs(points[0].X - 2.0F)).IsLessThan(1e-4F);
        await Assert.That(Math.Abs(points[1].X - 6.0F)).IsLessThan(1e-4F);
        await Assert.That(Math.Abs(points[1].Y - 4.0F)).IsLessThan(1e-4F);
        await Assert.That(Math.Abs(ReferenceHalfExtentX(builder) - 3.0F)).IsLessThan(1e-4F);
    }

    [Test]
    public async Task SurfaceDeformableVerticesAreComposedInMetresAtTheAuthoredScale()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(SurfaceDeformableVerticesAreComposedInMetresAtTheAuthoredScale));
        fixture.WriteStage(UnitStage(Centimetres, scale: 2.0));

        (PhysxPageBuilder builder, _) = Compose(fixture);
        PhysxVec3f[] points = DeformablePoints(builder, PhysxDeformableKind.Surface);

        // The patch spans one hundred centimetres, which is one metre, doubled by
        // the authored scale.
        await Assert.That(points.Length).IsEqualTo(4);
        float span = Extent(points, static point => point.X);
        await Assert.That(Math.Abs(span - 2.0F)).IsLessThan(1e-4F);
    }

    [Test]
    public async Task VolumeDeformableVerticesAreComposedInMetresAtTheAuthoredScale()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(VolumeDeformableVerticesAreComposedInMetresAtTheAuthoredScale));
        fixture.WriteStage(UnitStage(Centimetres, scale: 2.0));

        (PhysxPageBuilder builder, _) = Compose(fixture);
        PhysxVec3f[] points = DeformablePoints(builder, PhysxDeformableKind.Volume);

        await Assert.That(points.Length).IsEqualTo(8);
        float span = Extent(points, static point => point.Y);
        await Assert.That(Math.Abs(span - 2.0F)).IsLessThan(1e-4F);
    }

    [Test]
    public async Task AnUnusableAuthoredScaleSkipsTheObjectWithItsOwnNote()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(AnUnusableAuthoredScaleSkipsTheObjectWithItsOwnNote));
        fixture.WriteStage(UnitStage(Centimetres, scale: 0.0));

        (PhysxPageBuilder builder, UsdPhysicsCompositionReport report) = Compose(fixture);

        // A collapsed scale is refused rather than replaced by a unit scale,
        // because simulating a particle set at a size its author never wrote is
        // worse than reporting that it cannot be simulated.
        await Assert.That(report.Gpu.ParticleBodies).IsEqualTo(0);
        await Assert.That(report.Gpu.SurfaceDeformables).IsEqualTo(0);
        await Assert.That(report.Gpu.VolumeDeformables).IsEqualTo(0);
        await Assert.That(builder.ParticleBodyCount).IsEqualTo(0);
        await Assert.That(report.Skipped.Any(static note =>
            note.Contains("authored scale or stage units cannot be simulated", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task MeshColliderPointsAreComposedInMetres()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(MeshColliderPointsAreComposedInMetres));
        fixture.WriteStage(MeshColliderStage(Centimetres));

        (PhysxPageBuilder builder, _) = Compose(fixture);

        // A cooked mesh is scaled by the authored collider scale, which is
        // dimensionless, so the staged points carry the stage unit conversion
        // exactly like every analytic extent already does.
        float span = Extent(builder.MeshPoints.ToArray(), static point => point.X);
        await Assert.That(Math.Abs(span - 2.0F)).IsLessThan(1e-4F);
    }

    private static (PhysxPageBuilder Builder, UsdPhysicsCompositionReport Report) Compose(
        CpuDomainFixture fixture)
    {
        var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report =
            UsdPhysicsExtractionComposer.Compose(fixture.Extract(), builder);
        return (builder, report);
    }

    private static PhysxVec3f[] ParticlePoints(PhysxPageBuilder builder)
    {
        PhysxParticleBodyDesc body = builder.ParticleBodies[0];
        return Window(builder, body.PointOffset, body.PointCount);
    }

    private static PhysxVec3f[] DeformablePoints(PhysxPageBuilder builder, PhysxDeformableKind kind)
    {
        foreach (PhysxDeformableDesc deformable in builder.Deformables)
        {
            if (deformable.Kind == (uint)kind)
            {
                return Window(builder, deformable.PointOffset, deformable.PointCount);
            }
        }

        throw new InvalidOperationException($"No {kind} deformable was composed.");
    }

    private static PhysxVec3f[] Window(PhysxPageBuilder builder, uint offset, uint count)
    {
        var window = new PhysxVec3f[count];
        for (uint index = 0; index < count; index++)
        {
            window[index] = builder.MeshPoints[(int)(offset + index)];
        }

        return window;
    }

    private static float ReferenceHalfExtentX(PhysxPageBuilder builder)
    {
        foreach (PhysxShapeDesc shape in builder.Shapes)
        {
            if (shape.Type == (uint)PhysxShapeType.Box)
            {
                return shape.HalfExtents.X * shape.Scale.X;
            }
        }

        throw new InvalidOperationException("No analytic reference collider was composed.");
    }

    private static float Extent(PhysxVec3f[] points, Func<PhysxVec3f, float> select)
    {
        float low = float.MaxValue;
        float high = float.MinValue;
        foreach (PhysxVec3f point in points)
        {
            float value = select(point);
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        return high - low;
    }

    /// <summary>
    /// Authors one stage in centimetres carrying a rigid reference collider and one of every
    /// simulated point domain, all describing the same physical size.
    /// </summary>
    private static string UnitStage(double metersPerUnit, double scale) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = {{metersPerUnit.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}
            upAxis = "Y"
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def Xform "World"
        {
            def PhysicsScene "PhysicsScene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Reference" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 300
                float3 xformOp:scale = ({{Number(scale)}}, {{Number(scale)}}, {{Number(scale)}})
                uniform token[] xformOpOrder = ["xformOp:scale"]
            }

            def Scope "ParticleSystem" (prepend apiSchemas = ["OpenUsdPhysicsParticleSystemAPI"])
            {
                rel openUsdPhysics:particleSystem:simulationOwner = </World/PhysicsScene>
                double openUsdPhysics:particleSystem:particleContactOffset = 6
                double openUsdPhysics:particleSystem:restOffset = 5
            }

            def Points "Granules" (prepend apiSchemas = ["OpenUsdPhysicsParticleSetAPI"])
            {
                point3f[] points = [(100, 0, 0), (300, 200, 0)]
                float3 xformOp:scale = ({{Number(scale)}}, {{Number(scale)}}, {{Number(scale)}})
                uniform token[] xformOpOrder = ["xformOp:scale"]
                rel openUsdPhysics:particleSet:particleSystem = </World/ParticleSystem>
                bool openUsdPhysics:particleSet:fluid = false
            }

            def Material "ClothMaterial" (prepend apiSchemas = ["OpenUsdPhysicsSurfaceDeformableMaterialAPI"])
            {
                double openUsdPhysics:surfaceDeformableMaterial:youngsModulus = 500000
                double openUsdPhysics:surfaceDeformableMaterial:poissonsRatio = 0.45
                double openUsdPhysics:surfaceDeformableMaterial:density = 1000
                double openUsdPhysics:surfaceDeformableMaterial:thickness = 0.1
            }

            def Mesh "Cloth" (prepend apiSchemas = ["OpenUsdPhysicsSurfaceDeformableAPI"])
            {
                int[] faceVertexCounts = [3, 3]
                int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
                point3f[] points = [(0, 0, 0), (100, 0, 0), (100, 0, 100), (0, 0, 100)]
                float3 xformOp:scale = ({{Number(scale)}}, {{Number(scale)}}, {{Number(scale)}})
                uniform token[] xformOpOrder = ["xformOp:scale"]
                rel openUsdPhysics:surfaceDeformable:simulationOwner = </World/PhysicsScene>
                rel openUsdPhysics:surfaceDeformable:material = </World/ClothMaterial>
            }

            def Material "JellyMaterial" (prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableMaterialAPI"])
            {
                double openUsdPhysics:volumeDeformableMaterial:youngsModulus = 50000
                double openUsdPhysics:volumeDeformableMaterial:poissonsRatio = 0.45
                double openUsdPhysics:volumeDeformableMaterial:density = 1000
            }

            def Mesh "Jelly" (prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableAPI"])
            {
                int[] faceVertexCounts = [3, 3]
                int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
                point3f[] points = [(0, 0, 0), (100, 0, 0), (100, 0, 100), (0, 0, 100)]
                float3 xformOp:scale = ({{Number(scale)}}, {{Number(scale)}}, {{Number(scale)}})
                uniform token[] xformOpOrder = ["xformOp:scale"]
                rel openUsdPhysics:volumeDeformable:simulationOwner = </World/PhysicsScene>
                rel openUsdPhysics:volumeDeformable:material = </World/JellyMaterial>
                float3[] openUsdPhysics:volumeDeformable:simulationRestPoints = [
                    (0, 0, 0), (100, 0, 0), (100, 0, 100), (0, 0, 100),
                    (0, 100, 0), (100, 100, 0), (100, 100, 100), (0, 100, 100)]
                int[] openUsdPhysics:volumeDeformable:simulationIndices = [
                    0, 1, 3, 4, 1, 2, 3, 6, 1, 3, 4, 6, 1, 4, 5, 6, 3, 4, 6, 7]
            }
        }
        """;

    /// <summary>Authors a centimetre stage whose only collider is a triangle mesh.</summary>
    private static string MeshColliderStage(double metersPerUnit) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = {{metersPerUnit.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}
            upAxis = "Y"
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def Xform "World"
        {
            def PhysicsScene "PhysicsScene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Mesh "Slab" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                int[] faceVertexCounts = [3, 3]
                int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
                point3f[] points = [(0, 0, 0), (200, 0, 0), (200, 0, 200), (0, 0, 200)]
            }
        }
        """;

    private static string Number(double value) =>
        value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
