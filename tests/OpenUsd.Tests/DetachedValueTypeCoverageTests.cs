// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Shade;
using OpenUsd.Skel;

namespace OpenUsd.Tests;

public sealed class DetachedValueTypeCoverageTests
{
    [Test]
    public async Task JointInfluencesPreserveArrayIdentityAndRecordValues()
    {
        int[] indices = [0, 4, 7, 9];
        float[] weights = [0.1f, 0.2f, 0.3f, 0.4f];
        var value = new UsdSkelJointInfluences(
            indices,
            weights,
            2,
            UsdSkelInterpolation.Vertex);
        var same = new UsdSkelJointInfluences(
            indices,
            weights,
            2,
            UsdSkelInterpolation.Vertex);
        var clonedArrays = new UsdSkelJointInfluences(
            (int[])indices.Clone(),
            (float[])weights.Clone(),
            2,
            UsdSkelInterpolation.Vertex);

        await Assert.That(value.JointIndices).IsSameReferenceAs(indices);
        await Assert.That(value.JointWeights).IsSameReferenceAs(weights);
        await Assert.That(value.ElementSize).IsEqualTo(2);
        await Assert.That(value.Interpolation).IsEqualTo(UsdSkelInterpolation.Vertex);
        await Assert.That(value).IsEqualTo(same);
        await Assert.That(value).IsNotEqualTo(clonedArrays);
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());
    }

    [Test]
    public async Task DetachedRecordStructsUseComponentValueSemantics()
    {
        var extent = new UsdExtent3f(
            new UsdVec3f(-1, -2, -3),
            new UsdVec3f(4, 5, 6));
        var sameExtent = new UsdExtent3f(
            new UsdVec3f(-1, -2, -3),
            new UsdVec3f(4, 5, 6));
        var state = new UsdAttributeValueState(
            HasAuthoredValueOpinion: true,
            IsBlocked: false);
        var connection = new UsdShadeConnection(
            "/World/Material",
            "surface",
            UsdShadeAttributeType.Output);

        await Assert.That(extent).IsEqualTo(sameExtent);
        await Assert.That(extent.Minimum).IsEqualTo(new UsdVec3f(-1, -2, -3));
        await Assert.That(extent.Maximum).IsEqualTo(new UsdVec3f(4, 5, 6));
        await Assert.That(state.HasAuthoredValueOpinion).IsTrue();
        await Assert.That(state.IsBlocked).IsFalse();
        await Assert.That(connection.SourcePrimPath).IsEqualTo("/World/Material");
        await Assert.That(connection.SourceName).IsEqualTo("surface");
        await Assert.That(connection.SourceType).IsEqualTo(UsdShadeAttributeType.Output);
    }

    [Test]
    public async Task Vec3dUsesGeneratedRecordValueSemantics()
    {
        var value = new UsdVec3d(1.25, -2.5, 3.75);
        var same = new UsdVec3d(1.25, -2.5, 3.75);

        await Assert.That(value.X).IsEqualTo(1.25);
        await Assert.That(value.Y).IsEqualTo(-2.5);
        await Assert.That(value.Z).IsEqualTo(3.75);
        await Assert.That(value).IsEqualTo(same);
        await Assert.That(value).IsNotEqualTo(value with { Z = 4 });
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(value.ToString()).IsEqualTo("(1.25, -2.5, 3.75)");
    }

    [Test]
    public async Task Vec2fCoversObjectEqualityOperatorsAndNativeRoundTrip()
    {
        var value = new UsdVec2f(1.25f, -2.5f);
        var same = new UsdVec2f(1.25f, -2.5f);

        await Assert.That(value.Equals(same)).IsTrue();
        await Assert.That(value.Equals((object)same)).IsTrue();
        await Assert.That(value.Equals(null)).IsFalse();
        await Assert.That(value.Equals("not a vector")).IsFalse();
        await Assert.That(value.Equals(new UsdVec2f(9, -2.5f))).IsFalse();
        await Assert.That(value.Equals(new UsdVec2f(1.25f, 9))).IsFalse();
        await Assert.That(value == same).IsTrue();
        await Assert.That(value != same).IsFalse();
        await Assert.That(value == new UsdVec2f(9, 9)).IsFalse();
        await Assert.That(value != new UsdVec2f(9, 9)).IsTrue();
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());

        OpenUsdNativeVec2f native = value.ToNative();
        await Assert.That(native.X).IsEqualTo(1.25f);
        await Assert.That(native.Y).IsEqualTo(-2.5f);
        await Assert.That(UsdVec2f.FromNative(native)).IsEqualTo(value);
    }

    [Test]
    public async Task Vec3fCoversEveryEqualityBranchAndNativeRoundTrip()
    {
        var value = new UsdVec3f(1.25f, -2.5f, 3.75f);
        var same = new UsdVec3f(1.25f, -2.5f, 3.75f);

        await Assert.That(value.Equals(same)).IsTrue();
        await Assert.That(value.Equals((object)same)).IsTrue();
        await Assert.That(value.Equals(null)).IsFalse();
        await Assert.That(value.Equals("not a vector")).IsFalse();
        await Assert.That(value.Equals(new UsdVec3f(9, -2.5f, 3.75f))).IsFalse();
        await Assert.That(value.Equals(new UsdVec3f(1.25f, 9, 3.75f))).IsFalse();
        await Assert.That(value.Equals(new UsdVec3f(1.25f, -2.5f, 9))).IsFalse();
        await Assert.That(value == same).IsTrue();
        await Assert.That(value != same).IsFalse();
        await Assert.That(value == new UsdVec3f(9, 9, 9)).IsFalse();
        await Assert.That(value != new UsdVec3f(9, 9, 9)).IsTrue();
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());

        OpenUsdNativeVec3f native = value.ToNative();
        await Assert.That(native.X).IsEqualTo(1.25f);
        await Assert.That(native.Y).IsEqualTo(-2.5f);
        await Assert.That(native.Z).IsEqualTo(3.75f);
        await Assert.That(UsdVec3f.FromNative(native)).IsEqualTo(value);
    }

    [Test]
    public async Task QuatfCoversEveryEqualityBranchAndNativeRoundTrip()
    {
        var value = new UsdQuatf(0.5f, 0.1f, 0.2f, 0.3f);
        var same = new UsdQuatf(0.5f, 0.1f, 0.2f, 0.3f);

        await Assert.That(value.Equals(same)).IsTrue();
        await Assert.That(value.Equals((object)same)).IsTrue();
        await Assert.That(value.Equals(null)).IsFalse();
        await Assert.That(value.Equals("not a quaternion")).IsFalse();
        await Assert.That(value.Equals(new UsdQuatf(9, 0.1f, 0.2f, 0.3f))).IsFalse();
        await Assert.That(value.Equals(new UsdQuatf(0.5f, 9, 0.2f, 0.3f))).IsFalse();
        await Assert.That(value.Equals(new UsdQuatf(0.5f, 0.1f, 9, 0.3f))).IsFalse();
        await Assert.That(value.Equals(new UsdQuatf(0.5f, 0.1f, 0.2f, 9))).IsFalse();
        await Assert.That(value == same).IsTrue();
        await Assert.That(value != same).IsFalse();
        await Assert.That(value == UsdQuatf.Identity).IsFalse();
        await Assert.That(value != UsdQuatf.Identity).IsTrue();
        await Assert.That(value.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(UsdQuatf.Identity).IsEqualTo(new UsdQuatf(1, 0, 0, 0));

        OpenUsdNativeQuatf native = value.ToNative();
        await Assert.That(native.Real).IsEqualTo(0.5f);
        await Assert.That(native.X).IsEqualTo(0.1f);
        await Assert.That(native.Y).IsEqualTo(0.2f);
        await Assert.That(native.Z).IsEqualTo(0.3f);
        await Assert.That(UsdQuatf.FromNative(native)).IsEqualTo(value);
    }
}
