// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdVec3fTests
{
    [Test]
    public async Task ConstructorAssignsComponents()
    {
        var vector = new UsdVec3f(1.5f, -2f, 3.25f);

        await Assert.That(vector.X).IsEqualTo(1.5f);
        await Assert.That(vector.Y).IsEqualTo(-2f);
        await Assert.That(vector.Z).IsEqualTo(3.25f);
    }

    [Test]
    public async Task EqualityComparesComponentwise()
    {
        var left = new UsdVec3f(1f, 2f, 3f);
        var right = new UsdVec3f(1f, 2f, 3f);
        var different = new UsdVec3f(1f, 2f, 4f);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left == right).IsTrue();
        await Assert.That(left != different).IsTrue();
        await Assert.That(left.Equals((object)right)).IsTrue();
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task ToStringFormatsComponents()
    {
        var vector = new UsdVec3f(1f, 2f, 3f);

        await Assert.That(vector.ToString()).IsEqualTo("(1, 2, 3)");
    }
}
