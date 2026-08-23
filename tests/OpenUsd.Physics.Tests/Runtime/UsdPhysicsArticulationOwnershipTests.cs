// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;
using OpenUsd.Physics.Tests.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Covers what the composer does with overlapping and nested articulation roots, and the page
/// validator that catches the result if it ever gets it wrong.
/// </summary>
/// <remarks>
/// Two roots that claim the same body compose that body twice, and both copies are addressed by the
/// same composed identity because the identity is derived from the prim path. A world built from
/// that page holds two links at one address: the command map keeps whichever arrived first, and
/// both publish a pose for the same prim, so a force reaches one link while the viewer draws the
/// other. The composer therefore assigns every body to exactly one articulation, and the page
/// validator refuses a page that says otherwise regardless of who produced it.
/// </remarks>
public sealed class UsdPhysicsArticulationOwnershipTests
{
    private const string NestedStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
        {
            double size = 2
            float3 xformOp:scale = (50, 1, 50)
            double3 xformOp:translate = (0, -1, 0)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }

        def Xform "Outer" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI"]
        )
        {
            def Sphere "Root" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (0, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsFixedJoint "RootAnchor"
            {
                rel physics:body1 = </Outer/Root>
            }

            def Xform "Inner" (
                prepend apiSchemas = ["PhysicsArticulationRootAPI"]
            )
            {
                def Sphere "Link" (
                    prepend apiSchemas = [
                        "PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"
                    ]
                )
                {
                    double radius = 0.2
                    float physics:mass = 1
                    double3 xformOp:translate = (1, 5, 0)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }

            def PhysicsRevoluteJoint "Elbow"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Outer/Root>
                rel physics:body1 = </Outer/Inner/Link>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }
        }
        """;

    [Test]
    public async Task NestedArticulationRootsStageEveryBodyExactlyOnce()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            NestedArticulationRootsStageEveryBodyExactlyOnce));
        fixture.WriteStage(NestedStage);

        UsdPhysicsExtractionPage page = fixture.Extract();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        // The outer root is traversed first, so it owns the shared body and the inner root is
        // refused as a whole rather than composing a second copy of it.
        await Assert.That(report.Articulations).IsEqualTo(1);
        await Assert.That(string.Join(" ", report.Skipped)).Contains("/Outer/Inner");

        // Every simulated body address occurs once. This is the property the duplicate would break.
        using PhysxBuildPage built = builder.Build();
        (List<ulong> bodies, List<ulong> links) = ReadSimulatedBodyIds(built);

        var seen = new HashSet<ulong>();
        foreach (ulong id in bodies)
        {
            await Assert.That(seen.Add(id)).IsTrue();
        }

        foreach (ulong id in links)
        {
            await Assert.That(seen.Add(id)).IsTrue();
        }

        // And no body is orphaned: the shared link is still simulated, by the outer articulation.
        ulong shared = UsdPhysicsIdentities
            .ForSimulatedObject("/Outer/Inner/Link", UsdPhysicsObjectKind.RigidBody).Value;
        await Assert.That(links).Contains(shared);
    }

    /// <summary>Copies every actor and link identity out of a page, away from any await.</summary>
    private static (List<ulong> Actors, List<ulong> Links) ReadSimulatedBodyIds(PhysxBuildPage page)
    {
        var reader = new PhysxPageReader(page.Bytes);
        var actors = new List<ulong>();
        foreach (PhysxActorDesc actor in reader.Actors)
        {
            actors.Add(actor.Id);
        }

        var links = new List<ulong>();
        foreach (PhysxArticulationLinkDesc link in reader.ArticulationLinks)
        {
            links.Add(link.Id);
        }

        return (actors, links);
    }

    [Test]
    public async Task ANestedArticulationStillSimulatesAndPublishesOnePosePerPrim()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ANestedArticulationStillSimulatesAndPublishesOnePosePerPrim));
        fixture.WriteStage(NestedStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        simulation.Step(30);

        // One pose per prim. A duplicated link publishes the same identity twice, which is exactly
        // what makes a viewer draw one body and command another.
        ulong shared = UsdPhysicsIdentities
            .ForSimulatedObject("/Outer/Inner/Link", UsdPhysicsObjectKind.RigidBody).Value;
        var poses = 0;
        foreach (UsdPhysicsBodyPose pose in simulation.Frame.Bodies)
        {
            if (pose.Id.Value == shared)
            {
                poses++;
            }
        }

        await Assert.That(poses).IsEqualTo(1);

        // A command reaches the one link that exists, with no missing target.
        _ = simulation.World.DrainDiagnostics();
        simulation.Step(
            10,
            new[]
            {
                new PhysxCommand
                {
                    TargetId = shared,
                    Type = (uint)PhysxCommandType.AddForce,
                    Flags = 0,
                    Vector = new PhysxVec3f(200.0F, 0.0F, 0.0F),
                    Point = default,
                },
            });

        string codes = string.Join(
            "; ",
            simulation.World.DrainDiagnostics().Entries.Select(entry => entry.Code));
        await Assert.That(codes).DoesNotContain("COMMAND_TARGET_MISSING");
    }

    [Test]
    public async Task ThePageValidatorRefusesTwoSimulatedBodiesThatShareAnIdentity()
    {
        // The canonical page is patched at the byte level rather than composed, because the point
        // is that the page is the contract the world builds from and must not trust whoever
        // produced it - including a composer that got articulation ownership wrong.
        byte[] page = PhysxPageFixture.CreatePageBytes();
        PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
        await Assert.That(header.Actors.Count).IsGreaterThanOrEqualTo(2u);

        int stride = (int)PhysxAbi.RecordSizes.ActorDesc;
        ulong first = BinaryPrimitives.ReadUInt64LittleEndian(
            page.AsSpan((int)header.Actors.Offset));

        // Give the second actor the first actor's identity: one address, two simulated bodies.
        BinaryPrimitives.WriteUInt64LittleEndian(
            page.AsSpan((int)header.Actors.Offset + stride), first);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.DuplicateId);
        await Assert.That(result.Message).IsNotNull();
        await Assert.That(result.Message!).Contains("already declares");
    }
}
