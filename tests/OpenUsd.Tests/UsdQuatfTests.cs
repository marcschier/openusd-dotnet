// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdQuatfTests
{
    [Test]
    public async Task QuatfUsesScalarFirstValueSemantics()
    {
        var value = new UsdQuatf(0.5f, 0.1f, 0.2f, 0.3f);
        var same = new UsdQuatf(0.5f, 0.1f, 0.2f, 0.3f);

        await Assert.That(value.Real).IsEqualTo(0.5f);
        await Assert.That(value.X).IsEqualTo(0.1f);
        await Assert.That(value.Y).IsEqualTo(0.2f);
        await Assert.That(value.Z).IsEqualTo(0.3f);
        await Assert.That(value).IsEqualTo(same);
        await Assert.That(value == same).IsTrue();
        await Assert.That(value != UsdQuatf.Identity).IsTrue();
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(value.ToString()).IsEqualTo("(0.5; 0.1, 0.2, 0.3)");
    }

    [Test]
    public async Task IdentityUsesUnitRealComponent()
    {
        await Assert.That(UsdQuatf.Identity).IsEqualTo(new UsdQuatf(1, 0, 0, 0));
    }
}
