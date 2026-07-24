// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdGeometryValueTests
{
    [Test]
    public async Task Vec2fUsesComponentwiseValueSemantics()
    {
        var value = new UsdVec2f(1.5f, -2.0f);
        var same = new UsdVec2f(1.5f, -2.0f);

        await Assert.That(value.X).IsEqualTo(1.5f);
        await Assert.That(value.Y).IsEqualTo(-2.0f);
        await Assert.That(value).IsEqualTo(same);
        await Assert.That(value == same).IsTrue();
        await Assert.That(value != new UsdVec2f(0, 0)).IsTrue();
        await Assert.That(value.ToString()).IsEqualTo("(1.5, -2)");
    }

    [Test]
    public async Task Matrix4dPreservesRowMajorValues()
    {
        var value = new UsdMatrix4d(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16);
        var same = new UsdMatrix4d(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16);

        await Assert.That(value[2, 3]).IsEqualTo(12);
        await Assert.That(value.ToArray()).IsEquivalentTo(
            new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        await Assert.That(value).IsEqualTo(same);
        await Assert.That(value == same).IsTrue();
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(UsdMatrix4d.Identity[3, 3]).IsEqualTo(1);
    }

    [Test]
    public async Task Matrix4dRejectsInvalidIndices()
    {
        UsdMatrix4d value = UsdMatrix4d.Identity;

        await Assert.That(() => value[-1, 0]).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => value[0, 4]).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Matrix4dUsesOpenUsdRowVectorTranslation()
    {
        UsdMatrix4d value = UsdMatrix4d.CreateTranslation(10, 20, 30);

        await Assert.That(value.M03).IsEqualTo(0);
        await Assert.That(value.M13).IsEqualTo(0);
        await Assert.That(value.M23).IsEqualTo(0);
        await Assert.That(value.ExtractTranslation()).IsEqualTo(new UsdVec3d(10, 20, 30));
        await Assert.That(value.TransformPoint(new UsdVec3d(1, 2, 3)))
            .IsEqualTo(new UsdVec3d(11, 22, 33));
    }

    [Test]
    public async Task DefaultScalarValueIsInvalid()
    {
        UsdScalarValue value = default;

        await Assert.That(value.Kind).IsEqualTo(UsdScalarKind.Invalid);
        await Assert.That(() => value.BoolValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Int64Value).Throws<InvalidOperationException>();
        await Assert.That(() => value.DoubleValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.StringValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.TokenValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Vec3fValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Color3fValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Matrix4dValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Int32ArrayValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.FloatArrayValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.DoubleArrayValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Vec2fArrayValue).Throws<InvalidOperationException>();
        await Assert.That(() => value.Vec3fArrayValue).Throws<InvalidOperationException>();
    }
}
