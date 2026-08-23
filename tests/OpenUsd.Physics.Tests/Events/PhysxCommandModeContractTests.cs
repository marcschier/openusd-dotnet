// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Covers the two contracts that keep a malformed command batch from reaching the solver: the
/// force-mode modifiers an application point can carry, and the atomicity of whole-batch
/// validation.
/// </summary>
/// <remarks>
/// <see cref="PxRigidBodyExt"/> delivers an application point by converting the force into a torque
/// about the centre of mass, and it documents that only the plain force and impulse modes are
/// supported. Accepting an acceleration or a velocity change there would hand the SDK a mode it
/// cannot honour, so both the managed adapter and the native validator have to refuse it, and they
/// have to refuse it identically or a batch would behave differently depending on which side saw it
/// first.
/// </remarks>
public sealed class PhysxCommandModeContractTests
{
    private static readonly UsdPhysicsObjectId Target =
        UsdPhysicsIdentities.FromPrimPath("/Body", UsdPhysicsObjectKind.RigidBody);

    [Test]
    public async Task AnApplicationPointRefusesEveryForceModeModifier()
    {
        foreach (PhysxCommandType type in new[]
        {
            PhysxCommandType.AddForceAtPoint,
            PhysxCommandType.AddImpulseAtPoint,
        })
        {
            PhysxCommandFlags allowed = PhysxCommandAdapter.AllowedFlags(type);
            await Assert.That(allowed.HasFlag(PhysxCommandFlags.ModeAcceleration)).IsFalse();
            await Assert.That(allowed.HasFlag(PhysxCommandFlags.ModeVelocityChange)).IsFalse();

            // The point modifiers and the magnitude are still accepted; only the force mode is not.
            await Assert.That(allowed.HasFlag(PhysxCommandFlags.PointLocal)).IsTrue();
            await Assert.That(allowed.HasFlag(PhysxCommandFlags.PointCenterOfMass)).IsTrue();
            await Assert.That(allowed.HasFlag(PhysxCommandFlags.Magnitude)).IsTrue();
        }

        // The equivalent request without a point is unrestricted, so nothing expressible is lost.
        await Assert.That(PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddForce)
            .HasFlag(PhysxCommandFlags.ModeAcceleration)).IsTrue();
        await Assert.That(PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddImpulse)
            .HasFlag(PhysxCommandFlags.ModeVelocityChange)).IsTrue();
    }

    [Test]
    public async Task AMalformedCommandIsRefusedBeforeAnythingIsStaged()
    {
        var command = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.Force,
            Target,
            new UsdVec3d(1, 0, 0))
        {
            Point = new UsdVec3d(0.5, 0, 0),
            Application = UsdPhysicsApplicationPoint.World,
            Mode = UsdPhysicsForceMode.Acceleration,
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection))
            .IsFalse();
        await Assert.That(rejection).IsNotNull();
        await Assert.That(rejection!).IsNotEmpty();
    }

    [Test]
    public async Task AMixedBatchStagesNothingWhenOneCommandIsMalformed()
    {
        var good = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.Force,
            Target,
            new UsdVec3d(1, 0, 0));
        var bad = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.Impulse,
            Target,
            new UsdVec3d(1, 0, 0))
        {
            Point = new UsdVec3d(0.5, 0, 0),
            Application = UsdPhysicsApplicationPoint.World,
            Mode = UsdPhysicsForceMode.VelocityChange,
        };

        // The malformed command sits in the middle, so a validator that staged as it went would
        // already have mutated the buffer by the time it noticed.
        UsdPhysicsCommand[] batch = [good, bad, good];
        var staged = new PhysxCommand[batch.Length];
        for (int index = 0; index < staged.Length; index++)
        {
            staged[index] = new PhysxCommand { TargetId = 0xDEADUL };
        }

        bool accepted = PhysxCommandAdapter.TryTranslateBatch(
            batch, staged, out int count, out int failedIndex, out string? rejection);

        await Assert.That(accepted).IsFalse();
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(failedIndex).IsEqualTo(1);
        await Assert.That(rejection).IsNotNull();

        // Zero mutation: every staged slot still holds the sentinel it started with.
        for (int index = 0; index < staged.Length; index++)
        {
            await Assert.That(staged[index].TargetId).IsEqualTo(0xDEADUL);
        }
    }

    [Test]
    public async Task TheSameMalformedBatchIsRefusedTheSameWayEveryTime()
    {
        var bad = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.Force,
            Target,
            new UsdVec3d(0, 1, 0))
        {
            Point = new UsdVec3d(0, 0.5, 0),
            Application = UsdPhysicsApplicationPoint.World,
            Mode = UsdPhysicsForceMode.Acceleration,
        };

        string? first = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var staged = new PhysxCommand[1];
            bool accepted = PhysxCommandAdapter.TryTranslateBatch(
                [bad], staged, out int count, out int failedIndex, out string? rejection);

            await Assert.That(accepted).IsFalse();
            await Assert.That(count).IsEqualTo(0);
            await Assert.That(failedIndex).IsEqualTo(0);
            first ??= rejection;

            // Deterministic: the diagnostic is a property of the command, not of the attempt.
            await Assert.That(rejection).IsEqualTo(first);
        }
    }
}
