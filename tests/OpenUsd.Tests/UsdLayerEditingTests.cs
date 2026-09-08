// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Editing;

namespace OpenUsd.Tests;

public sealed class UsdLayerEditingTests
{
    [Test]
    public async Task ValuesCopyArraysAndRetainExactIeeeBits()
    {
        double nan = BitConverter.UInt64BitsToDouble(0x7ff8000000001234);
        float singleNan = BitConverter.UInt32BitsToSingle(0x7fc01234);
        double[] doubles = [nan, -0.0, double.PositiveInfinity];
        float[] singles = [singleNan, -0.0f];
        string[] strings = ["one", "two"];
        bool[] booleans = [true, false];
        int[] integers = [int.MinValue, int.MaxValue];
        long[] longs = [long.MinValue, long.MaxValue];
        UsdLayerEditValue doubleValue = UsdLayerEditValue.FromDoubleArray(doubles);
        UsdLayerEditValue singleValue = UsdLayerEditValue.FromFloatArray(singles);
        UsdLayerEditValue tokenValue = UsdLayerEditValue.FromTokenArray(strings);
        UsdLayerEditValue stringValue = UsdLayerEditValue.FromStringArray(strings);
        UsdLayerEditValue boolValue = UsdLayerEditValue.FromBooleanArray(booleans);
        UsdLayerEditValue intValue = UsdLayerEditValue.FromInt32Array(integers);
        UsdLayerEditValue longValue = UsdLayerEditValue.FromInt64Array(longs);
        doubles[0] = 0;
        singles[0] = 0;
        strings[0] = "changed";
        booleans[0] = false;
        integers[0] = 0;
        longs[0] = 0;
        doubleValue.AsDoubleArray()[0] = 1;
        tokenValue.AsTokenArray()[0] = "mutated";

        await Assert.That(BitConverter.DoubleToUInt64Bits(doubleValue.AsDoubleArray()[0]))
            .IsEqualTo(0x7ff8000000001234ul);
        await Assert.That(BitConverter.DoubleToUInt64Bits(doubleValue.AsDoubleArray()[1]))
            .IsEqualTo(0x8000000000000000ul);
        await Assert.That(BitConverter.SingleToUInt32Bits(singleValue.AsFloatArray()[0])).IsEqualTo(0x7fc01234u);
        await Assert.That(BitConverter.SingleToUInt32Bits(singleValue.AsFloatArray()[1])).IsEqualTo(0x80000000u);
        await Assert.That(tokenValue.AsTokenArray()[0]).IsEqualTo("one");
        await Assert.That(stringValue.AsStringArray()[0]).IsEqualTo("one");
        await Assert.That(boolValue.AsBooleanArray()[0]).IsTrue();
        await Assert.That(intValue.AsInt32Array()[0]).IsEqualTo(int.MinValue);
        await Assert.That(longValue.AsInt64Array()[0]).IsEqualTo(long.MinValue);
        await Assert.That(UsdLayerEditValue.FromDouble(-0.0).Equals(UsdLayerEditValue.FromDouble(0.0))).IsFalse();
        await Assert.That(UsdLayerEditValue.FromDouble(nan).Equals(UsdLayerEditValue.FromDouble(nan))).IsTrue();
        await Assert.That(UsdLayerEditValue.FromDouble(nan).GetHashCode())
            .IsEqualTo(UsdLayerEditValue.FromDouble(nan).GetHashCode());
        await Assert.That(UsdLayerEditValue.FromFloat(singleNan).Equals(UsdLayerEditValue.FromFloat(float.NaN)))
            .IsFalse();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(doubleValue);
    }

    [Test]
    public async Task TypedAccessorsDoNotCoerceAndMatricesRemainRowMajor()
    {
        await Assert.That(UsdLayerEditValue.FromBoolean(true).AsBoolean()).IsTrue();
        await Assert.That(UsdLayerEditValue.FromInt32(-47).AsInt32()).IsEqualTo(-47);
        await Assert.That(UsdLayerEditValue.FromInt64(long.MinValue).AsInt64()).IsEqualTo(long.MinValue);
        await Assert.That(UsdLayerEditValue.FromFloat(1.25f).AsFloat()).IsEqualTo(1.25f);
        await Assert.That(UsdLayerEditValue.FromDouble(1.25).AsDouble()).IsEqualTo(1.25);
        await Assert.That(UsdLayerEditValue.FromToken("render").AsToken()).IsEqualTo("render");
        await Assert.That(UsdLayerEditValue.FromString("text").AsString()).IsEqualTo("text");
        UsdLayerAssetPath asset = UsdLayerEditValue.FromAssetPath("authored.usda").AsAssetPath();
        await Assert.That(asset.AuthoredPath).IsEqualTo("authored.usda");
        await Assert.That(asset.EvaluatedPath).IsEqualTo("");
        await Assert.That(asset.ResolvedPath).IsEqualTo("");
        await Assert.That(UsdLayerEditValue.FromVec2f(new(1, 2)).AsVec2f()).IsEqualTo(new UsdVec2f(1, 2));
        await Assert.That(UsdLayerEditValue.FromVec3f(new(1, 2, 3)).AsVec3f()).IsEqualTo(new UsdVec3f(1, 2, 3));
        await Assert.That(UsdLayerEditValue.FromVec3d(new(1, 2, 3)).AsVec3d()).IsEqualTo(new UsdVec3d(1, 2, 3));
        await Assert.That(UsdLayerEditValue.FromVec4f(new(1, 2, 3, 4)).AsVec4f())
            .IsEqualTo(new UsdVec4f(1, 2, 3, 4));
        await Assert.That(UsdLayerEditValue.FromQuatf(new(4, 1, 2, 3)).AsQuatf()).IsEqualTo(new UsdQuatf(4, 1, 2, 3));
        var matrix = new UsdMatrix4d(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
        UsdMatrix4d actual = UsdLayerEditValue.FromMatrix4d(matrix).AsMatrix4d();
        await Assert.That(actual.M03).IsEqualTo(3d);
        await Assert.That(actual.M30).IsEqualTo(12d);
        await Assert.That(actual).IsEqualTo(matrix);
        byte[] matrixPacket = UsdLayerEditValue.FromMatrix4d(matrix).Payload.ToArray();
        await Assert.That(BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(matrixPacket.AsSpan(4 + (3 * 8))))).IsEqualTo(3d);
        await Assert.That(BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(matrixPacket.AsSpan(4 + (12 * 8))))).IsEqualTo(12d);
        byte[] quaternionPacket = UsdLayerEditValue.FromQuatf(new(4, 1, 2, 3)).Payload.ToArray();
        await Assert.That(BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(quaternionPacket.AsSpan(4)))).IsEqualTo(4f);
        await Assert.That(() => UsdLayerEditValue.FromInt32(1).AsDouble()).Throws<InvalidOperationException>();
        await Assert.That(() => UsdLayerEditValue.FromToken("a").AsString()).Throws<InvalidOperationException>();
        await Assert.That(() => UsdLayerEditValue.Absent.AsBoolean()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PathListBucketsAreDeeplyImmutableAndModesStayDistinct()
    {
        string[] added = ["/Added"];
        string[] prepended = ["/Prepended"];
        string[] appended = ["/Appended"];
        string[] deleted = ["/Deleted"];
        string[] ordered = ["/Ordered"];
        var list = new UsdPathListEdit(false, addedItems: added, prependedItems: prepended,
            appendedItems: appended, deletedItems: deleted, orderedItems: ordered);
        UsdLayerEditValue value = UsdLayerEditValue.FromPathList(list);
        added[0] = prepended[0] = appended[0] = deleted[0] = ordered[0] = "/Changed";
        UsdPathListEdit captured = value.AsPathList();
        await Assert.That(captured.AddedItems[0]).IsEqualTo("/Added");
        await Assert.That(captured.PrependedItems[0]).IsEqualTo("/Prepended");
        await Assert.That(captured.AppendedItems[0]).IsEqualTo("/Appended");
        await Assert.That(captured.DeletedItems[0]).IsEqualTo("/Deleted");
        await Assert.That(captured.OrderedItems[0]).IsEqualTo("/Ordered");
        await Assert.That(() => ((IList<string>)list.AddedItems)[0] = "/Changed").Throws<NotSupportedException>();
        string[] explicitItems = ["/One"];
        var explicitList = new UsdPathListEdit(true, explicitItems);
        explicitItems[0] = "/Changed";
        await Assert.That(explicitList.ExplicitItems[0]).IsEqualTo("/One");
        await Assert.That(UsdLayerEditValue.FromPathList(new UsdPathListEdit(true))
            .Equals(UsdLayerEditValue.FromPathList(new UsdPathListEdit(false)))).IsFalse();
        await Assert.That(UsdLayerEditValue.FromPathList(new UsdPathListEdit(true)).Equals(UsdLayerEditValue.Absent))
            .IsFalse();
        await Assert.That(() => new UsdPathListEdit(true, addedItems: ["/A"])).Throws<ArgumentException>();
        await Assert.That(() => new UsdPathListEdit(false, ["/A"])).Throws<ArgumentException>();
        await Assert.That(() => new UsdPathListEdit(false, addedItems: ["/A", "/A"])).Throws<ArgumentException>();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(captured);
    }

    [Test]
    [Arguments("")]
    [Arguments("/")]
    [Arguments("World.value")]
    [Arguments("/World")]
    [Arguments("/World.")]
    [Arguments("/World..value")]
    [Arguments("/World{variant=x}.value")]
    [Arguments("/World.value[/Target]")]
    [Arguments("/World.value.child")]
    [Arguments("/World.1value")]
    [Arguments("/World.ns::value")]
    [Arguments("/World.value/child")]
    public async Task InvalidPropertyAddressesAreRejectedBeforeNativeAccess(string path)
    {
        await Assert.That(() => new UsdLayerEditAddress(path, UsdLayerEditField.Default)).Throws<ArgumentException>();
    }

    [Test]
    public async Task AddressAndCollectionBoundsAreValidatedWithCanonicalSampleZero()
    {
        var negative = new UsdLayerEditAddress("/World.ns:value", UsdLayerEditField.TimeSample, -0.0);
        var positive = new UsdLayerEditAddress("/World.ns:value", UsdLayerEditField.TimeSample, 0);
        await Assert.That(negative).IsEqualTo(positive);
        await Assert.That(BitConverter.DoubleToUInt64Bits(negative.TimeCode)).IsEqualTo(0ul);
        await Assert.That(() => UsdLayerEditCodec.EncodeAddresses([negative, positive])).Throws<ArgumentException>();
        await Assert.That(() => new UsdLayerEditAddress("/World.value", UsdLayerEditField.Default, 1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdLayerEditAddress("/World.value", UsdLayerEditField.TimeSample, double.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdLayerEditAddress("/World.value", (UsdLayerEditField)4))
            .Throws<ArgumentOutOfRangeException>();
        string prim = string.Concat(Enumerable.Repeat("/A", 31));
        _ = new UsdLayerEditAddress(prim + ".value", UsdLayerEditField.Default);
        await Assert.That(() => new UsdLayerEditAddress(prim + "/B.value", UsdLayerEditField.Default))
            .Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditCodec.EncodeAddresses([])).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdLayerEditCodec.EncodeAddresses(new UsdLayerEditAddress[257]))
            .Throws<ArgumentOutOfRangeException>();
        UsdLayerEditAddress[] maximumAddresses = Enumerable.Range(0, 256)
            .Select(index => new UsdLayerEditAddress($"/World.value{index}", UsdLayerEditField.Default)).ToArray();
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            UsdLayerEditCodec.EncodeAddresses(maximumAddresses).AsSpan(12))).IsEqualTo(256u);
        await Assert.That(() => UsdLayerEditCodec.EncodeAddresses([default])).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditValue.FromDoubleArray(new double[4097]))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UsdLayerEditValue.FromString(new string('x', 4097))).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditValue.FromToken(new string('\u00e9', 2049))).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditValue.FromString("a\0b")).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditValue.FromString("\ud800")).Throws<ArgumentException>();
        await Assert.That(UsdLayerEditValue.FromString(new string('x', 4096)).AsString().Length).IsEqualTo(4096);
        await Assert.That(UsdLayerEditValue.FromInt32Array(new int[4096]).AsInt32Array().Length).IsEqualTo(4096);
        await Assert.That(() => UsdLayerEditValue.FromStringArray(Enumerable.Repeat(new string('x', 4096), 1024)
            .ToArray())).Throws<ArgumentException>();
    }

    [Test]
    public async Task MutationsUseClosedValuesAndStrictFieldPathDomains()
    {
        var scalar = new UsdLayerEditAddress("/World.value", UsdLayerEditField.Default);
        var targets = new UsdLayerEditAddress("/World.targets", UsdLayerEditField.RelationshipTargets);
        var connections = new UsdLayerEditAddress("/World.input", UsdLayerEditField.AttributeConnections);
        UsdLayerEdit set = UsdLayerEdit.Set(scalar, UsdLayerEditValue.FromVec3f(new(1, 2, 3)), "color3f",
            UsdLayerEditVariability.Uniform, creationCustom: true);
        await Assert.That(set.CreationTypeName).IsEqualTo("color3f");
        await Assert.That(set.CreationVariability).IsEqualTo(UsdLayerEditVariability.Uniform);
        await Assert.That(set.CreationCustom).IsTrue();
        await Assert.That(UsdLayerEdit.Clear(scalar).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(UsdLayerEdit.Block(scalar).Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(() => UsdLayerEdit.Set(scalar, UsdLayerEditValue.Absent)).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEdit.Set(scalar, UsdLayerEditValue.Block)).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEdit.Set(targets, UsdLayerEditValue.FromDouble(1))).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEdit.Block(targets)).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEdit.Set(connections,
            UsdLayerEditValue.FromPathList(new UsdPathListEdit(true, ["/Prim"])))).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEdit.Set(targets,
            UsdLayerEditValue.FromPathList(new UsdPathListEdit(true, ["relative"])))).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEdit.Set(targets,
            UsdLayerEditValue.FromPathList(new UsdPathListEdit(true)), "double")).Throws<ArgumentException>();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(set);
        await Assert.That(() => UsdStageBoundResultGuard.ThrowIfForbiddenType(typeof(UsdLayer)))
            .Throws<UsdStageBoundResultException>();
    }

    [Test]
    public async Task NativePermissionDoesNotAdmitGenericLayersAndDetachedReviewIsNotEditable()
    {
        var identity = new UsdLayerIdentity(1, 2, 3);
        var generic = new UsdLayerEditingState(identity, 4, UsdLayerRole.Root, 8 | 16 | 32, "root", "", "", "");
        var denied = new UsdLayerEditingState(identity, 4, UsdLayerRole.UserReview, 32, "user", "", "", "");
        var detached = new UsdLayerEditingState(identity, 4, UsdLayerRole.UserReview, 8, "user", "", "", "");
        var admitted = new UsdLayerEditingState(identity, 4, UsdLayerRole.UserReview, 1 | 8 | 32, "user", "", "", "");
        await Assert.That(generic.PermissionToEdit).IsTrue();
        await Assert.That(generic.CanAttemptAuthoredEdits).IsFalse();
        await Assert.That(generic.EditingRestriction).Contains("capture-only");
        await Assert.That(denied.CanAttemptAuthoredEdits).IsFalse();
        await Assert.That(denied.EditingRestriction).Contains("permission");
        await Assert.That(detached.CanAttemptAuthoredEdits).IsFalse();
        await Assert.That(detached.EditingRestriction).Contains("detached");
        await Assert.That(admitted.CanAttemptAuthoredEdits).IsTrue();
        await Assert.That(admitted.EditingRestriction).IsNull();
    }
}
