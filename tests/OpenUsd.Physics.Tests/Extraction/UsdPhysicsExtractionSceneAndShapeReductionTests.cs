// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Extraction;

/// <summary>
/// Locks that a scene, a collider scale or a mesh topology the runtime cannot take is reduced or
/// dropped on its own instead of failing the whole build page.
/// </summary>
/// <remarks>
/// Every one of these values reaches the build page through a narrowing to a float or through a
/// shared section that the page validator checks as a whole, so a value that only becomes unusable
/// on the way into the page is just as fatal as one that was authored that way.
/// </remarks>
public sealed class UsdPhysicsExtractionSceneAndShapeReductionTests
{
    private const float DefaultGravity = 9.81F;
    private const float DefaultBounceThreshold = 0.2F;

    /// <summary>One authored property with the name the extractor would have produced.</summary>
    private readonly record struct Authored(
        UsdPhysicsExtractionKey Key,
        string Name,
        UsdPhysicsExtractionValueKind Kind,
        double Scalar,
        double[] Values,
        UsdPhysicsExtractionPropertyTraits Traits);

    /// <summary>The parts of a composed scene that an authored value decides.</summary>
    private readonly record struct ComposedScene(
        PhysxVec3f GravityDirection, float GravityMagnitude, float BounceThreshold);

    /// <summary>What one page carries after a mesh collider was composed or dropped.</summary>
    private readonly record struct ComposedMesh(
        int Shapes, int Actors, int MeshPoints, ImmutableArray<string> Skipped);

    [Test]
    public async Task AGravityMagnitudeThatOverflowsTheBuildPageFallsBackToTheDefault()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Scalar(UsdPhysicsExtractionKey.SceneGravityMagnitude, "physics:gravityMagnitude", 1e300));

        await Assert.That(scene.GravityMagnitude).IsEqualTo(DefaultGravity);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("/Scene");
        await Assert.That(report.Skipped[0]).Contains("gravity magnitude");
    }

    [Test]
    public async Task ABounceThresholdThatOverflowsTheBuildPageFallsBackToTheDefault()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Scalar(UsdPhysicsExtractionKey.SceneBounceThreshold, "physics:bounceThreshold", 1e39));

        await Assert.That(scene.BounceThreshold).IsEqualTo(DefaultBounceThreshold);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("bounce threshold");
    }

    [Test]
    public async Task ANegativeSceneNumberFallsBackToTheDefault()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Scalar(UsdPhysicsExtractionKey.SceneGravityMagnitude, "physics:gravityMagnitude", -5.0),
            Scalar(UsdPhysicsExtractionKey.SceneBounceThreshold, "physics:bounceThreshold", -1.0));

        await Assert.That(scene.GravityMagnitude).IsEqualTo(DefaultGravity);
        await Assert.That(scene.BounceThreshold).IsEqualTo(DefaultBounceThreshold);
        await Assert.That(report.Skipped.Length).IsEqualTo(2);
        await Assert.That(report.Skipped[0]).Contains("gravity magnitude");
        await Assert.That(report.Skipped[1]).Contains("bounce threshold");
    }

    [Test]
    public async Task AGravityDirectionWhoseOwnLengthOverflowsIsStillNormalized()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Vector(
                UsdPhysicsExtractionKey.SceneGravityDirection,
                "physics:gravityDirection",
                0.0,
                -1e200,
                0.0));

        // Squaring the components directly would overflow and normalise to a zero direction, which
        // the page validator rejects next to a positive magnitude.
        await Assert.That(scene.GravityDirection).IsEqualTo(new PhysxVec3f(0.0F, -1.0F, 0.0F));
        await Assert.That(scene.GravityMagnitude).IsEqualTo(DefaultGravity);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AGravityDirectionThatUnderflowsIsStillNormalized()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Vector(
                UsdPhysicsExtractionKey.SceneGravityDirection,
                "physics:gravityDirection",
                1e-200,
                0.0,
                0.0));

        await Assert.That(scene.GravityDirection).IsEqualTo(new PhysxVec3f(1.0F, 0.0F, 0.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AGravityDirectionThatCannotBeSimulatedPointsDown()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Vector(
                UsdPhysicsExtractionKey.SceneGravityDirection,
                "physics:gravityDirection",
                double.NaN,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(scene.GravityDirection).IsEqualTo(new PhysxVec3f(0.0F, -1.0F, 0.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("gravity direction");
    }

    [Test]
    public async Task AnUnauthoredGravityDirectionStaysSimulationDownWithoutANote()
    {
        (ComposedScene scene, UsdPhysicsCompositionReport report) = ComposeScene(
            Vector(
                UsdPhysicsExtractionKey.SceneGravityDirection,
                "physics:gravityDirection",
                0.0,
                0.0,
                0.0));

        await Assert.That(scene.GravityDirection).IsEqualTo(new PhysxVec3f(0.0F, -1.0F, 0.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AScaleThatOverflowsTheBuildPageFallsBackToAUnitScale()
    {
        (PhysxVec3f scale, UsdPhysicsCompositionReport report) = ComposeScale((1e300, 1.0, 1.0));

        await Assert.That(scale).IsEqualTo(new PhysxVec3f(1.0F, 1.0F, 1.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("/Body/Shape");
        await Assert.That(report.Skipped[0]).Contains("scale");
    }

    [Test]
    public async Task AScaleThatUnderflowsTheBuildPageFallsBackToAUnitScale()
    {
        (PhysxVec3f scale, UsdPhysicsCompositionReport report) = ComposeScale((1.0, 1e-320, 1.0));

        // The double still holds the value, but the float it is narrowed to collapses to zero,
        // which the page validator rejects as a non positive scale.
        await Assert.That(scale).IsEqualTo(new PhysxVec3f(1.0F, 1.0F, 1.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("scale");
    }

    [Test]
    public async Task AMirroredScaleKeepsItsMagnitudeWithoutANote()
    {
        (PhysxVec3f scale, UsdPhysicsCompositionReport report) = ComposeScale((-2.0, 3.0, 4.0));

        await Assert.That(scale).IsEqualTo(new PhysxVec3f(2.0F, 3.0F, 4.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AMeshColliderWithAPointThatIsNotFiniteIsDroppedAlone()
    {
        ComposedMesh mesh = ComposeMesh(new float[] { float.NaN, 0.5F, 0.25F, -0.5F });

        // The mesh point section is validated as a whole, so the healthy body only survives when
        // the offending points never reach the page.
        await Assert.That(mesh.Shapes).IsEqualTo(1);
        await Assert.That(mesh.Actors).IsEqualTo(1);
        await Assert.That(mesh.MeshPoints).IsEqualTo(0);
        await Assert.That(mesh.Skipped.Length).IsEqualTo(2);
        await Assert.That(mesh.Skipped[0]).Contains("/Mesh/Shape");
        await Assert.That(mesh.Skipped[0]).Contains("not finite");
        await Assert.That(mesh.Skipped[1]).Contains("/Mesh");
        await Assert.That(mesh.Skipped[1]).Contains("no usable collision shape");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task AConvexColliderWithTooFewPointsIsDroppedAlone(int points)
    {
        // The page validator needs four points for a convex hull, so a collider that carries fewer
        // has to be dropped before any of them reach the shared mesh point section.
        ComposedMesh mesh = ComposeMesh(Points(points));

        await Assert.That(mesh.Shapes).IsEqualTo(1);
        await Assert.That(mesh.Actors).IsEqualTo(1);
        await Assert.That(mesh.MeshPoints).IsEqualTo(0);
        await Assert.That(mesh.Skipped.Length).IsEqualTo(2);
        await Assert.That(mesh.Skipped[0]).Contains("/Mesh/Shape");
        await Assert.That(mesh.Skipped[0]).Contains(
            points == 0 ? "no topology" : $"{points} of the 4 points");
        await Assert.That(mesh.Skipped[1]).Contains("no usable collision shape");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ATriangleColliderWithTooFewPointsIsDroppedAlone(int points)
    {
        // The indices stay inside the authored points, so only the point count is at fault.
        ComposedMesh mesh = ComposeMesh(
            Points(points),
            points == 0 ? null : [.. Enumerable.Repeat(0u, 3)],
            UsdPhysicsExtractionGeometryKind.Mesh);

        await Assert.That(mesh.Shapes).IsEqualTo(1);
        await Assert.That(mesh.MeshPoints).IsEqualTo(0);
        await Assert.That(mesh.Skipped[0]).Contains(
            points == 0 ? "no topology" : $"{points} of the 3 points");
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    public async Task ATriangleColliderWithoutWholeTrianglesIsDroppedAlone(int indices)
    {
        ComposedMesh mesh = ComposeMesh(
            Points(4),
            [.. Enumerable.Repeat(0u, indices)],
            UsdPhysicsExtractionGeometryKind.Mesh);

        await Assert.That(mesh.Shapes).IsEqualTo(1);
        await Assert.That(mesh.MeshPoints).IsEqualTo(0);
        await Assert.That(mesh.Skipped[0]).Contains("without whole triangles");
    }

    [Test]
    public async Task AHealthyMeshColliderStillComposes()
    {
        ComposedMesh mesh = ComposeMesh(Points(4));

        await Assert.That(mesh.Shapes).IsEqualTo(2);
        await Assert.That(mesh.Actors).IsEqualTo(2);
        await Assert.That(mesh.MeshPoints).IsEqualTo(4);
        await Assert.That(mesh.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AHealthyTriangleColliderStillComposes()
    {
        ComposedMesh mesh = ComposeMesh(
            Points(3), [0u, 1u, 2u], UsdPhysicsExtractionGeometryKind.Mesh);

        await Assert.That(mesh.Shapes).IsEqualTo(2);
        await Assert.That(mesh.MeshPoints).IsEqualTo(3);
        await Assert.That(mesh.Skipped.Length).IsEqualTo(0);
    }

    private static Authored Scalar(
        UsdPhysicsExtractionKey key,
        string name,
        double value,
        UsdPhysicsExtractionPropertyTraits traits = UsdPhysicsExtractionPropertyTraits.None) =>
        new(key, name, UsdPhysicsExtractionValueKind.Real, value, [], traits);

    private static Authored Vector(
        UsdPhysicsExtractionKey key,
        string name,
        double x,
        double y,
        double z,
        UsdPhysicsExtractionPropertyTraits traits = UsdPhysicsExtractionPropertyTraits.None) =>
        new(key, name, UsdPhysicsExtractionValueKind.Vector3, 0.0, [x, y, z], traits);

    /// <summary>Composes one scene that authors the given properties next to one healthy body.</summary>
    private static (ComposedScene Scene, UsdPhysicsCompositionReport Report) ComposeScene(
        params Authored[] authored)
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();
        int scene = AddScene(fixture);
        AddProperties(fixture, scene, authored);
        AddSphereBody(fixture, 4, "/Body");

        (UsdPhysicsCompositionReport report, PhysxBuildPage built) = Compose(fixture);
        using (built)
        {
            PhysxPageReader reader = built.CreateReader();
            PhysxSceneDesc desc = reader.Scenes[0];
            return (
                new ComposedScene(desc.GravityDirection, desc.GravityMagnitude, desc.BounceThreshold),
                report);
        }
    }

    /// <summary>Composes one collider that carries the given authored scale.</summary>
    private static (PhysxVec3f Scale, UsdPhysicsCompositionReport Report) ComposeScale(
        (double X, double Y, double Z) scale)
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();
        AddScene(fixture);
        int collider = AddSphereBody(fixture, 4, "/Body");
        fixture.SetObjectScale(collider, scale);

        (UsdPhysicsCompositionReport report, PhysxBuildPage built) = Compose(fixture);
        using (built)
        {
            PhysxPageReader reader = built.CreateReader();
            return (reader.Shapes[0].Scale, report);
        }
    }

    /// <summary>
    /// Composes one healthy sphere body next to one mesh collider that carries the given points
    /// and triangle indices.
    /// </summary>
    /// <remarks>
    /// A triangle mesh cannot belong to a movable actor, so a triangle collider is authored on its
    /// own and becomes a static actor, while a convex collider is authored under a dynamic body.
    /// </remarks>
    private static ComposedMesh ComposeMesh(
        float[] points,
        uint[]? indices = null,
        UsdPhysicsExtractionGeometryKind geometry = UsdPhysicsExtractionGeometryKind.ConvexMesh)
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();
        AddScene(fixture);
        AddSphereBody(fixture, 4, "/Body");

        bool underBody = geometry == UsdPhysicsExtractionGeometryKind.ConvexMesh;
        int body = -1;
        if (underBody)
        {
            body = fixture.AddObject(
                8,
                "/Mesh",
                "Mesh",
                UsdPhysicsExtractionObjectKind.RigidBody,
                UsdPhysicsExtractionDomains.RigidBody,
                UsdPhysicsExtractionObjectTraits.Enabled |
                    UsdPhysicsExtractionObjectTraits.Dynamic);
            fixture.SetObjectLinks(body, 0, -1);
            fixture.SetObjectTransform(body, (0.0, 4.0, 0.0), (1, 0, 0, 0), (0, 0, 0));
        }

        int collider = fixture.AddObject(
            9,
            underBody ? "/Mesh/Shape" : "/Mesh",
            "Shape",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            geometry);
        fixture.SetObjectLinks(collider, 0, body);
        fixture.SetObjectTransform(collider, (0.0, 4.0, 0.0), (1, 0, 0, 0), (0.5, 0.5, 0.5));

        int point = -1;
        foreach (float value in points)
        {
            int added = fixture.AddPoint(value, 0.5F, -0.5F);
            point = point < 0 ? added : point;
        }

        int index = -1;
        foreach (uint value in indices ?? [])
        {
            int added = fixture.AddIndex(value);
            index = index < 0 ? added : index;
        }

        fixture.SetObjectGeometry(
            collider,
            point < 0 ? 0 : point,
            points.Length,
            index < 0 ? 0 : index,
            indices?.Length ?? 0);

        (UsdPhysicsCompositionReport report, PhysxBuildPage built) = Compose(fixture);
        using (built)
        {
            PhysxPageReader reader = built.CreateReader();
            return new ComposedMesh(
                report.Shapes, report.Actors, reader.MeshPoints.Length, report.Skipped);
        }
    }

    /// <summary>Four distinct points, of which only the first ones are used.</summary>
    private static float[] Points(int count) =>
        [.. new float[] { 0.5F, -0.25F, 0.75F, -0.5F }.Take(count)];

    private static UsdPhysicsExtractionPageFixture CreateFixture() => new()
    {
        MetersPerUnit = 1.0,
        KilogramsPerUnit = 1.0,
        TimeCodesPerSecond = 24.0,
        EndTimeCode = 1.0,
        UpAxis = (uint)UsdPhysicsExtractionUpAxis.Y,
        DefaultSceneIndex = 0,
    };

    private static int AddScene(UsdPhysicsExtractionPageFixture fixture) => fixture.AddObject(
        1,
        "/Scene",
        "Scene",
        UsdPhysicsExtractionObjectKind.Scene,
        UsdPhysicsExtractionDomains.Scene,
        UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.DefaultScene);

    private static void AddProperties(
        UsdPhysicsExtractionPageFixture fixture, int objectIndex, Authored[] authored)
    {
        foreach (Authored property in authored)
        {
            int start = -1;
            foreach (double value in property.Values)
            {
                int number = fixture.AddNumber(value);
                start = start < 0 ? number : start;
            }

            fixture.AddProperty(
                property.Key,
                property.Name,
                property.Kind,
                UsdPhysicsExtractionSource.Standard,
                property.Scalar,
                start < 0 ? 0 : start,
                property.Values.Length,
                property.Traits);
        }

        fixture.SetObjectRange(objectIndex, 0, authored.Length, 0, 0);
    }

    /// <summary>Adds one dynamic body with one sphere collider, and returns the collider.</summary>
    private static int AddSphereBody(
        UsdPhysicsExtractionPageFixture fixture, ulong id, string path)
    {
        int body = fixture.AddObject(
            id,
            path,
            path[1..],
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);
        fixture.SetObjectLinks(body, 0, -1);
        fixture.SetObjectTransform(body, (0.0, 1.0, 0.0), (1, 0, 0, 0), (0, 0, 0));

        int collider = fixture.AddObject(
            id + 1,
            path + "/Shape",
            "Shape",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.Sphere);
        fixture.SetObjectLinks(collider, 0, body);
        fixture.SetObjectTransform(collider, (0.0, 1.0, 0.0), (1, 0, 0, 0), (0.5, 0, 0));
        return collider;
    }

    /// <summary>Composes the page and builds it, which validates every record it carries.</summary>
    private static (UsdPhysicsCompositionReport Report, PhysxBuildPage Built) Compose(
        UsdPhysicsExtractionPageFixture fixture)
    {
        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        return validation.IsValid
            ? (report, built)
            : throw new InvalidOperationException(validation.Message ?? "The page is not valid.");
    }
}
