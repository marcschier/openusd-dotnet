// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Covers the resolution the interaction controls run through when the operator drives the
/// selected object.
/// </summary>
/// <remarks>
/// One prim commonly produces several sections, each addressing a different composed object. These
/// tests pin the rule that a command is only ever built from the address the composer gave the
/// selected section, and only when that section really accepts the command family.
/// </remarks>
public sealed class ViewerPhysicsCommandTargetTests
{
    private const ulong BodyId = 0x1111_1111UL;
    private const ulong VehicleId = 0x2222_2222UL;
    private const ulong ControllerId = 0x3333_3333UL;

    [Test]
    public async Task TwoSectionsOfOnePrimResolveToTheirOwnTargets()
    {
        ViewerPhysicsObjectSection body = Section(
            "/Car", "RigidBody", BodyId, "/Car", ViewerPhysicsCommandability.Body);
        ViewerPhysicsObjectSection vehicle = Section(
            "/Car", "Vehicle", VehicleId, "/Car", ViewerPhysicsCommandability.Vehicle);

        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                body, ViewerPhysicsCommandability.Body))
            .IsEqualTo(BodyId);
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                vehicle, ViewerPhysicsCommandability.Vehicle))
            .IsEqualTo(VehicleId);

        // A vehicle input sent to the chassis actor and a force sent to the vehicle are both
        // addresses the world holds in the wrong map, so both are refused before they are built.
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                body, ViewerPhysicsCommandability.Vehicle))
            .IsEqualTo(0UL);
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                vehicle, ViewerPhysicsCommandability.Body))
            .IsEqualTo(0UL);
    }

    [Test]
    public async Task AControllerSectionOnlyAcceptsMoves()
    {
        ViewerPhysicsObjectSection walker = Section(
            "/Walker",
            "CharacterController",
            ControllerId,
            "/Walker",
            ViewerPhysicsCommandability.Controller);

        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                walker, ViewerPhysicsCommandability.Controller))
            .IsEqualTo(ControllerId);
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                walker, ViewerPhysicsCommandability.Body))
            .IsEqualTo(0UL);
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                walker, ViewerPhysicsCommandability.Vehicle))
            .IsEqualTo(0UL);
    }

    [Test]
    public async Task ASectionWithNoComposedAddressDrivesNothing()
    {
        ViewerPhysicsObjectSection joint = Section(
            "/Car/Joint", "Joint", 0UL, string.Empty, ViewerPhysicsCommandability.None);

        foreach (ViewerPhysicsCommandability required in new[]
        {
            ViewerPhysicsCommandability.Body,
            ViewerPhysicsCommandability.Controller,
            ViewerPhysicsCommandability.Vehicle,
            ViewerPhysicsCommandability.Scene,
        })
        {
            await Assert.That(joint.Accepts(required)).IsFalse();
            await Assert.That(ViewerPhysicsController.ResolveCommandTarget(joint, required))
                .IsEqualTo(0UL);
        }
    }

    [Test]
    public async Task ASectionThatClaimsAFamilyButCarriesNoAddressStillDrivesNothing()
    {
        // A zero identity is the world's reserved sentinel, so it must never be submitted even if
        // the classification says the family is right.
        ViewerPhysicsObjectSection broken = Section(
            "/Car", "Vehicle", 0UL, "/Car", ViewerPhysicsCommandability.Vehicle);

        await Assert.That(broken.Accepts(ViewerPhysicsCommandability.Vehicle)).IsFalse();
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                broken, ViewerPhysicsCommandability.Vehicle))
            .IsEqualTo(0UL);
    }

    [Test]
    public async Task AColliderSectionCarriesTheOwningBodyAddress()
    {
        // The projection resolves this; the section only has to keep carrying it, because that is
        // what makes a drag started on a collider push the body rather than nothing at all.
        ViewerPhysicsObjectSection collider = Section(
            "/Box", "Collider", BodyId, "/Box", ViewerPhysicsCommandability.Body);

        await Assert.That(collider.TargetPath).IsEqualTo("/Box");
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                collider, ViewerPhysicsCommandability.Body))
            .IsEqualTo(BodyId);
    }

    private static ViewerPhysicsObjectSection Section(
        string primPath,
        string kind,
        ulong targetId,
        string targetPath,
        ViewerPhysicsCommandability commandability) =>
        new(
            ViewerPhysicsTestSections.ExtractionId(primPath, kind),
            primPath,
            kind,
            $"{kind} at {primPath} is simulated.",
            [],
            [],
            targetId,
            targetPath,
            commandability);
}

/// <summary>Builds extraction identities that are distinct from every simulation identity.</summary>
internal static class ViewerPhysicsTestSections
{
    /// <summary>Hashes a path and a kind the way the extractor does, for section fixtures.</summary>
    internal static ulong ExtractionId(string primPath, string kind)
    {
        ulong hash = 0xCBF2_9CE4_8422_2325UL;
        foreach (char character in primPath + "|" + kind)
        {
            hash ^= character;
            hash *= 0x0000_0100_0000_01B3UL;
        }

        return hash == 0UL ? 1UL : hash;
    }
}
