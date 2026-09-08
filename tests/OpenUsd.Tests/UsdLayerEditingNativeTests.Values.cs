// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Tests;

public sealed partial class UsdLayerEditingNativeTests
{
    [Test]
    public async Task AllOrdinaryTypedValuesRoundTripInOneNativeTransactionWithoutCoercion()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        double nan = BitConverter.UInt64BitsToDouble(0x7ff8000000001234);
        float singleNan = BitConverter.UInt32BitsToSingle(0x7fc01234);
        (string Type, UsdLayerEditValue Value)[] values =
        [
            ("bool", UsdLayerEditValue.FromBoolean(true)),
            ("int", UsdLayerEditValue.FromInt32(int.MinValue)),
            ("int64", UsdLayerEditValue.FromInt64(long.MinValue)),
            ("float", UsdLayerEditValue.FromFloat(singleNan)),
            ("double", UsdLayerEditValue.FromDouble(nan)),
            ("token", UsdLayerEditValue.FromToken("render")),
            ("string", UsdLayerEditValue.FromString("detached \u00e9\u4e16\u754c")),
            ("asset", UsdLayerEditValue.FromAssetPath(document.Path)),
            ("float2", UsdLayerEditValue.FromVec2f(new(1, -0.0f))),
            ("float3", UsdLayerEditValue.FromVec3f(new(1, 2, 3))),
            ("double3", UsdLayerEditValue.FromVec3d(new(1, 2, -0.0))),
            ("float4", UsdLayerEditValue.FromVec4f(new(1, 2, 3, 4))),
            ("quatf", UsdLayerEditValue.FromQuatf(new(4, 1, 2, 3))),
            ("matrix4d", UsdLayerEditValue.FromMatrix4d(
                new(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15))),
            ("bool[]", UsdLayerEditValue.FromBooleanArray([true, false])),
            ("int[]", UsdLayerEditValue.FromInt32Array([int.MinValue, int.MaxValue])),
            ("int64[]", UsdLayerEditValue.FromInt64Array([long.MinValue, long.MaxValue])),
            ("float[]", UsdLayerEditValue.FromFloatArray([singleNan, -0.0f])),
            ("double[]", UsdLayerEditValue.FromDoubleArray([nan, -0.0])),
            ("token[]", UsdLayerEditValue.FromTokenArray(["one", "two"])),
            ("string[]", UsdLayerEditValue.FromStringArray(["one", "two"])),
            ("color3f", UsdLayerEditValue.FromVec3f(new(0.1f, 0.2f, 0.3f))),
            ("double[]", UsdLayerEditValue.FromDoubleArray([]))
        ];
        var addresses = new UsdLayerEditAddress[values.Length];
        var edits = new UsdLayerEdit[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            addresses[index] = Default($"/Values.v{index}");
            edits[index] = UsdLayerEdit.Set(addresses[index], values[index].Value, values[index].Type,
                creationCustom: true);
        }
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored(addresses);
        UsdLayerEditResult result = review.CompareAndApply(before, edits);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        UsdLayerAuthoredSnapshot captured = review.CaptureAuthored(addresses);
        await Assert.That(captured.CopyBytes().SequenceEqual(result.AfterSnapshot!.CopyBytes())).IsTrue();
        for (int index = 0; index < values.Length; index++)
        {
            await Assert.That(captured.Opinions[index].TypeName).IsEqualTo(values[index].Type);
            await Assert.That(captured.Opinions[index].Value.Equals(values[index].Value)).IsTrue();
            await Assert.That(captured.Opinions[index].Custom).IsTrue();
        }
        UsdLayerCheckpoint checkpoint = review.CaptureCheckpoint();
        await Assert.That(checkpoint.CopyBytes().Length > 0).IsTrue();
        await Assert.That(review.CompareAndRestore(captured, before).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(document.Stage.HasPrim("/Values")).IsFalse();
    }

    [Test]
    public async Task NativeSignedZeroChangesAreNotElidedAsNumericallyEqual()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        var address = Default("/World.zero");
        UsdLayerAuthoredSnapshot plus = ApplyDouble(review, review.CaptureAuthored([address]), 0.0);
        UsdLayerAuthoredSnapshot minus = ApplyDouble(review, plus, -0.0);
        await Assert.That(BitConverter.DoubleToUInt64Bits(review.CaptureAuthored([address]).Opinions[0].Value.AsDouble()))
            .IsEqualTo(0x8000000000000000ul);
        await Assert.That(review.CompareAndRestore(minus, plus).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(BitConverter.DoubleToUInt64Bits(review.CaptureAuthored([address]).Opinions[0].Value.AsDouble()))
            .IsEqualTo(0ul);
        var sample = new UsdLayerEditAddress("/World.zero", UsdLayerEditField.TimeSample, 1);
        plus = ApplyDouble(review, review.CaptureAuthored([sample]), 0.0);
        minus = ApplyDouble(review, plus, -0.0);
        await Assert.That(BitConverter.DoubleToUInt64Bits(minus.Opinions[0].Value.AsDouble()))
            .IsEqualTo(0x8000000000000000ul);
    }

    [Test]
    public async Task CheckpointsAndAuthoredSnapshotsCannotBeReboundToAnotherStage()
    {
        using NativeDocument first = await NativeDocument.OpenAsync();
        using NativeDocument second = await NativeDocument.OpenAsync();
        using UsdLayer firstReview = first.Stage.GetUserReviewLayer();
        using UsdLayer secondReview = second.Stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        UsdLayerAuthoredSnapshot expected = firstReview.CaptureAuthored([address]);
        UsdLayerCheckpoint checkpoint = firstReview.CaptureCheckpoint();
        UsdLayerEditResult edit = secondReview.CompareAndApply(expected,
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")]);
        await Assert.That(edit.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
        await Assert.That(edit.AfterSnapshot).IsNull();
        UsdLayerCheckpointRestoreResult restore = secondReview.RestoreCheckpoint(
            secondReview.CaptureCheckpoint(), checkpoint);
        await Assert.That(restore.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
        await Assert.That(restore.AfterCheckpoint).IsNull();
        await Assert.That(secondReview.AcknowledgeSaved(checkpoint)).IsFalse();
        await Assert.That(secondReview.CaptureAuthored([address]).Opinions[0].PropertyKind)
            .IsEqualTo(UsdLayerPropertyKind.Absent);
    }

    [Test]
    public async Task RootReloadInvalidatesItsCapturedIdentityWithoutAffectingReviewOwnership()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer root = document.Stage.GetRootLayer();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        UsdLayerAuthoredSnapshot before = root.CaptureAuthored([address]);
        UsdLayerIdentity reviewIdentity = review.GetEditingState().Identity;
        await File.WriteAllTextAsync(document.Path,
            """
            #usda 1.0
            def Xform "World"
            {
                double weight = 70
            }
            """);
        await Assert.That(root.Reload(force: true)).IsTrue();
        await Assert.That(root.GetEditingState().Identity.Generation != before.Identity.Generation).IsTrue();
        UsdLayerEditResult result = root.CompareAndRestore(before, before);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
        await Assert.That(root.CaptureAuthored([address]).Opinions[0].Value.AsDouble()).IsEqualTo(70d);
        await Assert.That(review.GetEditingState().Identity).IsEqualTo(reviewIdentity);
    }
}
