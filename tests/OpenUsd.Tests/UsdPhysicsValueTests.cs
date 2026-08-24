// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;

namespace OpenUsd.Tests;

public sealed class UsdPhysicsValueTests
{
    [Test]
    public async Task MultipleApplyTokensMatchUsdPhysicsSchemaNames()
    {
        await Assert.That(UsdPhysicsTokens.TransX).IsEqualTo("transX");
        await Assert.That(UsdPhysicsTokens.TransY).IsEqualTo("transY");
        await Assert.That(UsdPhysicsTokens.TransZ).IsEqualTo("transZ");
        await Assert.That(UsdPhysicsTokens.RotX).IsEqualTo("rotX");
        await Assert.That(UsdPhysicsTokens.RotY).IsEqualTo("rotY");
        await Assert.That(UsdPhysicsTokens.RotZ).IsEqualTo("rotZ");
        await Assert.That(UsdPhysicsTokens.Linear).IsEqualTo("linear");
        await Assert.That(UsdPhysicsTokens.Angular).IsEqualTo("angular");
        await Assert.That(UsdPhysicsTokens.Distance).IsEqualTo("distance");
    }

    [Test]
    public async Task MultipleApplyInstanceValidationRunsBeforeNativeAccess()
    {
        await Assert.That(() => UsdPhysicsLimitAPI.Apply(default, string.Empty))
            .Throws<ArgumentException>();
        await Assert.That(() => UsdPhysicsLimitAPI.Apply(default, "rot:X"))
            .Throws<ArgumentException>();
        await Assert.That(() => UsdPhysicsDriveAPI.Has(default, "linear/drive"))
            .Throws<ArgumentException>();
        await Assert.That(() => UsdPhysicsDriveAPI.Wrap(default, UsdPhysicsTokens.Angular))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(UsdPhysicsAxis.X, "X")]
    [Arguments(UsdPhysicsAxis.Y, "Y")]
    [Arguments(UsdPhysicsAxis.Z, "Z")]
    public async Task AxisTokensRoundTrip(UsdPhysicsAxis axis, string token)
    {
        await Assert.That(UsdPhysicsTokens.ToToken(axis)).IsEqualTo(token);
        await Assert.That(UsdPhysicsTokens.ToAxis(token)).IsEqualTo(axis);
    }

    [Test]
    [Arguments(UsdPhysicsMeshCollisionApproximation.None, "none")]
    [Arguments(UsdPhysicsMeshCollisionApproximation.ConvexDecomposition, "convexDecomposition")]
    [Arguments(UsdPhysicsMeshCollisionApproximation.ConvexHull, "convexHull")]
    [Arguments(UsdPhysicsMeshCollisionApproximation.BoundingSphere, "boundingSphere")]
    [Arguments(UsdPhysicsMeshCollisionApproximation.BoundingCube, "boundingCube")]
    [Arguments(UsdPhysicsMeshCollisionApproximation.MeshSimplification, "meshSimplification")]
    public async Task MeshCollisionApproximationTokensRoundTrip(
        UsdPhysicsMeshCollisionApproximation approximation,
        string token)
    {
        await Assert.That(UsdPhysicsTokens.ToToken(approximation)).IsEqualTo(token);
        await Assert.That(UsdPhysicsTokens.ToApproximation(token)).IsEqualTo(approximation);
    }

    [Test]
    [Arguments(UsdPhysicsDriveType.Force, "force")]
    [Arguments(UsdPhysicsDriveType.Acceleration, "acceleration")]
    public async Task DriveTypeTokensRoundTrip(UsdPhysicsDriveType driveType, string token)
    {
        await Assert.That(UsdPhysicsTokens.ToToken(driveType)).IsEqualTo(token);
        await Assert.That(UsdPhysicsTokens.ToDriveType(token)).IsEqualTo(driveType);
    }

    [Test]
    public async Task UnknownPhysicsTokensAreRejected()
    {
        await Assert.That(() => UsdPhysicsTokens.ToToken((UsdPhysicsAxis)(-1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdPhysicsTokens.ToAxis("x"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdPhysicsTokens.ToToken((UsdPhysicsMeshCollisionApproximation)(-1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdPhysicsTokens.ToApproximation("triangleMesh"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdPhysicsTokens.ToToken((UsdPhysicsDriveType)(-1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdPhysicsTokens.ToDriveType("velocity"))
            .Throws<ArgumentOutOfRangeException>();
    }
}
