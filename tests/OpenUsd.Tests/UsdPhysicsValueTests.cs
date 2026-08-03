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
}
