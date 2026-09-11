// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdLayerEditCodecTests
{
    [Test]
    public async Task PacketsAreLittleEndianAndMutationsMatchTheNativeContract()
    {
        var address = new UsdLayerEditAddress("/World.weight", UsdLayerEditField.Default);
        byte[] addresses = UsdLayerEditCodec.EncodeAddresses([address]);
        await Assert.That(Convert.ToHexString(addresses)).IsEqualTo(
            "554544310100000001000000010000000D0000002F576F726C642E776569676874000000000000000000000000");
        UsdLayerAuthoredSnapshot expected = UsdLayerEditCodec.DecodeSnapshot(SnapshotPacket(absent: true));
        byte[] mutation = UsdLayerEditCodec.EncodeEdits(expected,
            [UsdLayerEdit.Set(expected.Addresses[0], UsdLayerEditValue.FromDouble(47), "double")]);
        var reader = new UsdEditReader(mutation);
        reader.Header(3);
        int count = reader.Count(256, 1);
        UsdLayerEditAddress actualAddress = reader.Address();
        uint operation = reader.U32();
        string type = reader.Text();
        uint variability = reader.U32();
        bool custom = reader.Boolean();
        UsdLayerEditValue value = reader.Value();
        reader.End();
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(actualAddress).IsEqualTo(expected.Addresses[0]);
        await Assert.That(operation).IsEqualTo(1u);
        await Assert.That(type).IsEqualTo("double");
        await Assert.That(variability).IsEqualTo(0u);
        await Assert.That(custom).IsFalse();
        await Assert.That(value.AsDouble()).IsEqualTo(47d);
        await Assert.That(() => UsdLayerEditCodec.EncodeEdits(expected, [])).Throws<ArgumentException>();
        await Assert.That(() => UsdLayerEditCodec.EncodeEdits(expected, [UsdLayerEdit.Clear(address)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SnapshotSummariesRetainDeclarationAbsenceAndCopyAllStorage()
    {
        byte[] packet = SnapshotPacket(absent: false, declarationsAbsent: true);
        UsdLayerAuthoredSnapshot snapshot = UsdLayerEditCodec.DecodeSnapshot(packet);
        byte[] original = (byte[])packet.Clone();
        packet.AsSpan().Clear();
        snapshot.CopyBytes().AsSpan().Clear();
        await Assert.That(snapshot.Identity).IsEqualTo(new UsdLayerIdentity(1, 2, 3));
        await Assert.That(snapshot.Revision).IsEqualTo(4ul);
        await Assert.That(snapshot.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Attribute);
        await Assert.That(snapshot.Opinions[0].TypeName).IsNull();
        await Assert.That(snapshot.Opinions[0].Variability).IsNull();
        await Assert.That(snapshot.Opinions[0].Custom).IsNull();
        await Assert.That(snapshot.Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(snapshot.CopyBytes().SequenceEqual(original)).IsTrue();
        await Assert.That(() => ((IList<UsdLayerEditAddress>)snapshot.Addresses)[0] = default)
            .Throws<NotSupportedException>();
        await Assert.That(() => ((IList<UsdLayerAuthoredOpinion>)snapshot.Opinions)[0] = null!)
            .Throws<NotSupportedException>();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(snapshot);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(snapshot.Opinions[0]);

        UsdLayerAuthoredSnapshot absent = UsdLayerEditCodec.DecodeSnapshot(SnapshotPacket(absent: true));
        await Assert.That(absent.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(absent.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
    }

    [Test]
    public async Task SnapshotStorageMetadataComparesTheEntireImmutablePacket()
    {
        UsdLayerAuthoredSnapshot snapshot = UsdLayerEditCodec.DecodeSnapshot(SnapshotPacket());
        UsdLayerAuthoredSnapshot identical = UsdLayerEditCodec.DecodeSnapshot(SnapshotPacket());
        byte[] revisedPacket = SnapshotPacket();
        BinaryPrimitives.WriteUInt64LittleEndian(revisedPacket.AsSpan(36), 5);
        UsdLayerAuthoredSnapshot revised = UsdLayerEditCodec.DecodeSnapshot(revisedPacket);
        byte[] replacedPacket = SnapshotPacket();
        BinaryPrimitives.WriteUInt64LittleEndian(replacedPacket.AsSpan(28), 6);
        UsdLayerAuthoredSnapshot replaced = UsdLayerEditCodec.DecodeSnapshot(replacedPacket);
        byte[] changedPacket = SnapshotPacket();
        BinaryPrimitives.WriteDoubleLittleEndian(changedPacket.AsSpan(106), 53);
        UsdLayerAuthoredSnapshot changed = UsdLayerEditCodec.DecodeSnapshot(changedPacket);

        await Assert.That(snapshot.ByteLength).IsEqualTo(114);
        await Assert.That(snapshot.HasSamePayload(snapshot)).IsTrue();
        await Assert.That(snapshot.HasSamePayload(identical)).IsTrue();
        await Assert.That(snapshot.HasSamePayload(null)).IsFalse();
        await Assert.That(snapshot.HasSamePayload(revised)).IsFalse();
        await Assert.That(snapshot.HasSamePayload(replaced)).IsFalse();
        await Assert.That(snapshot.HasSamePayload(changed)).IsFalse();
    }

    [Test]
    [Arguments("magic")]
    [Arguments("version")]
    [Arguments("kind")]
    [Arguments("stage-id")]
    [Arguments("layer-id")]
    [Arguments("generation")]
    [Arguments("zero-count")]
    [Arguments("over-count")]
    [Arguments("overflow-count")]
    [Arguments("truncated")]
    [Arguments("trailing")]
    [Arguments("text-budget")]
    [Arguments("invalid-utf8")]
    [Arguments("nul")]
    [Arguments("relative-path")]
    [Arguments("field")]
    [Arguments("time")]
    [Arguments("property-kind")]
    [Arguments("wrong-property-kind")]
    [Arguments("absent-spec-with-fields")]
    [Arguments("type-name-tag")]
    [Arguments("variability-flag")]
    [Arguments("custom-flag")]
    [Arguments("value-tag")]
    [Arguments("duplicate-address")]
    public async Task MalformedSnapshotsNeverEscapeTheBoundedCodec(string corruption)
    {
        await Assert.That(() => UsdLayerEditCodec.DecodeSnapshot(CorruptSnapshot(corruption)))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task EveryTruncatedSnapshotFailsAndOversizeIsRejectedAtTheHeader()
    {
        byte[] bytes = SnapshotPacket();
        for (int length = 0; length < bytes.Length; length++)
        {
            int capturedLength = length;
            await Assert.That(() => UsdLayerEditCodec.DecodeSnapshot(bytes.AsSpan(0, capturedLength)))
                .Throws<OpenUsdNativeException>();
        }
        await Assert.That(() => UsdLayerEditCodec.DecodeSnapshot(new byte[(4 * 1024 * 1024) + 1]))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    [Arguments(17)]
    [Arguments(18)]
    [Arguments(19)]
    [Arguments(20)]
    [Arguments(21)]
    [Arguments(22)]
    [Arguments(23)]
    public async Task NativeMetadataTagsRemainDistinctAndOpaqueInCheckpoints(int tag)
    {
        byte[] value = MetadataValue(tag);
        UsdLayerEditValue decoded = DecodeValue(value);
        await Assert.That((int)decoded.Kind).IsEqualTo(tag);
        byte[] checkpointBytes = CheckpointPacket(value);
        UsdLayerCheckpoint checkpoint = UsdLayerEditCodec.DecodeCheckpoint(checkpointBytes);
        await Assert.That(checkpoint.CopyBytes().SequenceEqual(checkpointBytes)).IsTrue();
        checkpointBytes.AsSpan().Clear();
        checkpoint.CopyBytes().AsSpan().Clear();
        await Assert.That(checkpoint.CopyBytes()[0]).IsEqualTo((byte)0x55);
        await Assert.That(checkpoint.Identifier).IsEqualTo("anon:review.usda");
        await Assert.That(checkpoint.AssetAnchor).IsEqualTo("");
        await Assert.That(checkpoint.Identity).IsEqualTo(new UsdLayerIdentity(1, 2, 3));
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(checkpoint);
    }

    [Test]
    public async Task CheckpointStorageMetadataComparesTheEntireImmutablePacket()
    {
        byte[] packet = CheckpointPacket(UsdLayerEditValue.FromDouble(47).Payload.ToArray());
        UsdLayerCheckpoint checkpoint = UsdLayerEditCodec.DecodeCheckpoint(packet);
        UsdLayerCheckpoint identical = UsdLayerEditCodec.DecodeCheckpoint(packet);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(36), 5);
        UsdLayerCheckpoint revised = UsdLayerEditCodec.DecodeCheckpoint(packet);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(36), 4);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(28), 6);
        UsdLayerCheckpoint replaced = UsdLayerEditCodec.DecodeCheckpoint(packet);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(28), 3);
        BinaryPrimitives.WriteDoubleLittleEndian(packet.AsSpan(113), 53);
        UsdLayerCheckpoint changed = UsdLayerEditCodec.DecodeCheckpoint(packet);

        await Assert.That(checkpoint.ByteLength).IsEqualTo(121);
        await Assert.That(checkpoint.HasSamePayload(checkpoint)).IsTrue();
        await Assert.That(checkpoint.HasSamePayload(identical)).IsTrue();
        await Assert.That(checkpoint.HasSamePayload(null)).IsFalse();
        await Assert.That(checkpoint.HasSamePayload(revised)).IsFalse();
        await Assert.That(checkpoint.HasSamePayload(replaced)).IsFalse();
        await Assert.That(checkpoint.HasSamePayload(changed)).IsFalse();
    }

    [Test]
    [Arguments("bool")]
    [Arguments("bool-array")]
    [Arguments("array-count")]
    [Arguments("array-truncated")]
    [Arguments("int64-truncated")]
    [Arguments("utf8")]
    [Arguments("unknown-tag")]
    [Arguments("specifier")]
    [Arguments("variability")]
    [Arguments("list-flag")]
    [Arguments("mixed-list")]
    [Arguments("duplicate-list")]
    [Arguments("duplicate-dictionary")]
    [Arguments("duplicate-time")]
    [Arguments("nonfinite-time")]
    [Arguments("empty-sample")]
    [Arguments("depth")]
    [Arguments("trailing")]
    public async Task MalformedTaggedValuesFailClosed(string corruption)
    {
        await Assert.That(() => DecodeValue(CorruptValue(corruption))).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task RecursiveBoundaryAndAssetCachesArePreservedWithoutReinterpretation()
    {
        UsdLayerEditValue nested = DecodeValue(NestedValue(15));
        await Assert.That(nested.Kind).IsEqualTo(UsdLayerEditValueKind.Dictionary);
        var writer = new UsdEditWriter();
        writer.U32(9);
        writer.Text("authored");
        writer.Text("evaluated");
        writer.Text("resolved");
        UsdLayerAssetPath asset = DecodeValue(writer.Written.ToArray()).AsAssetPath();
        await Assert.That(asset).IsEqualTo(new UsdLayerAssetPath("authored", "evaluated", "resolved"));
    }

    [Test]
    [Arguments("kind")]
    [Arguments("flags")]
    [Arguments("role")]
    [Arguments("trailing")]
    [Arguments("text")]
    public async Task MalformedStateIsRejected(string corruption)
    {
        var writer = Packet(5);
        writer.U32(corruption == "role" ? 5u : 2u);
        writer.U32(corruption == "flags" ? 64u : 63u);
        writer.Text(corruption == "text" ? "" : "anon:review.usda");
        writer.Text("");
        writer.Text("");
        writer.Text("");
        byte[] bytes = writer.Written.ToArray();
        if (corruption == "kind")
        {
            bytes[8] = 6;
        }
        if (corruption == "trailing")
        {
            bytes = [.. bytes, 0];
        }
        await Assert.That(() => UsdLayerEditCodec.DecodeState(bytes)).Throws<OpenUsdNativeException>();
    }

    [Test]
    [Arguments(1u)]
    [Arguments(2u)]
    [Arguments(4u)]
    [Arguments(8u)]
    [Arguments(16u)]
    [Arguments(32u)]
    public async Task EachNativeStateFlagIsIndependent(uint flag)
    {
        UsdEditWriter writer = Packet(5);
        writer.U32(4);
        writer.U32(flag);
        writer.Text("identifier");
        writer.Text("real");
        writer.Text("resolved");
        writer.Text("anchor");
        UsdLayerEditingState state = UsdLayerEditCodec.DecodeState(writer.Written);
        await Assert.That(state.Role).IsEqualTo(UsdLayerRole.Local);
        await Assert.That(state.IsAnonymous).IsEqualTo(flag == 1);
        await Assert.That(state.IsDirty).IsEqualTo(flag == 2);
        await Assert.That(state.NativeIsDirty).IsEqualTo(flag == 4);
        await Assert.That(state.PermissionToEdit).IsEqualTo(flag == 8);
        await Assert.That(state.PermissionToSave).IsEqualTo(flag == 16);
        await Assert.That(state.CurrentlyLocal).IsEqualTo(flag == 32);
        await Assert.That(state.Identifier).IsEqualTo("identifier");
        await Assert.That(state.RealPath).IsEqualTo("real");
        await Assert.That(state.ResolvedPath).IsEqualTo("resolved");
        await Assert.That(state.AssetAnchor).IsEqualTo("anchor");
    }

    [Test]
    [Arguments("missing-root")]
    [Arguments("wrong-root-type")]
    [Arguments("spec-count")]
    [Arguments("field-count")]
    [Arguments("empty-field")]
    [Arguments("absent-field")]
    [Arguments("duplicate-field")]
    [Arguments("duplicate-spec")]
    [Arguments("bad-field-kind")]
    [Arguments("trailing")]
    public async Task MalformedCheckpointInventoriesFailBeforeUse(string corruption)
    {
        await Assert.That(() => UsdLayerEditCodec.DecodeCheckpoint(CorruptCheckpoint(corruption)))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task CheckpointAtTheSpecLimitValidatesChildInventoriesWithoutReparsingThemPerChild()
    {
        byte[] packet = CheckpointWithChildren(4095, "");
        UsdLayerCheckpoint checkpoint = UsdLayerEditCodec.DecodeCheckpoint(packet);
        await Assert.That(checkpoint.CopyBytes().SequenceEqual(packet)).IsTrue();
        await Assert.That(() => UsdLayerEditCodec.DecodeCheckpoint(CheckpointWithChildren(4096, "")))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    [Arguments("duplicate-child")]
    [Arguments("missing-child")]
    [Arguments("missing-parent")]
    [Arguments("invalid-child-name")]
    public async Task CheckpointChildInventoriesMustMatchExactSpecTopology(string corruption)
    {
        await Assert.That(() => UsdLayerEditCodec.DecodeCheckpoint(CheckpointWithChildren(2, corruption)))
            .Throws<OpenUsdNativeException>();
    }

    private static UsdEditWriter Packet(uint kind)
    {
        var writer = new UsdEditWriter();
        writer.Header(kind);
        writer.U64(1);
        writer.U64(2);
        writer.U64(3);
        writer.U64(4);
        return writer;
    }

    private static byte[] SnapshotPacket(bool absent = false, bool declarationsAbsent = false)
    {
        UsdEditWriter writer = Packet(2);
        writer.U32(1);
        writer.Address(new UsdLayerEditAddress("/P.v", UsdLayerEditField.Default));
        writer.U32(absent ? 0u : 1u);
        if (absent || declarationsAbsent)
        {
            writer.U32(0);
            writer.U32(0);
            writer.U32(0);
        }
        else
        {
            writer.U32(7);
            writer.Text("double");
            writer.U32(20);
            writer.U32(0);
            writer.U32(2);
            writer.U32(0);
        }
        writer.Bytes(absent ? UsdLayerEditValue.Absent.Payload : UsdLayerEditValue.FromDouble(47).Payload);
        return writer.Written.ToArray();
    }

    private static byte[] CorruptSnapshot(string corruption)
    {
        byte[] bytes = SnapshotPacket();
        switch (corruption)
        {
            case "magic":
                bytes[0] = 0;
                break;
            case "version":
                bytes[4] = 2;
                break;
            case "kind":
                bytes[8] = 1;
                break;
            case "stage-id":
                bytes.AsSpan(12, 8).Clear();
                break;
            case "layer-id":
                bytes.AsSpan(20, 8).Clear();
                break;
            case "generation":
                bytes.AsSpan(28, 8).Clear();
                break;
            case "zero-count":
                bytes[44] = 0;
                break;
            case "over-count":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 257);
                break;
            case "overflow-count":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), uint.MaxValue);
                break;
            case "truncated":
                return bytes[..^1];
            case "trailing":
                return [.. bytes, 0];
            case "text-budget":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 4097);
                break;
            case "invalid-utf8":
                bytes[52] = 0xff;
                break;
            case "nul":
                bytes[52] = 0;
                break;
            case "relative-path":
                bytes[52] = (byte)'x';
                break;
            case "field":
                bytes[56] = 4;
                break;
            case "time":
                bytes[67] = 0x7f;
                break;
            case "property-kind":
                bytes[68] = 3;
                break;
            case "wrong-property-kind":
                bytes[68] = 2;
                break;
            case "absent-spec-with-fields":
                bytes[68] = 0;
                break;
            case "type-name-tag":
                bytes[72] = 8;
                break;
            case "variability-flag":
                bytes[90] = 2;
                break;
            case "custom-flag":
                bytes[98] = 2;
                break;
            case "value-tag":
                bytes[102] = 24;
                break;
            case "duplicate-address":
                bytes = [.. bytes, .. bytes.AsSpan(48)];
                bytes[44] = 2;
                break;
        }
        return bytes;
    }

    private static UsdLayerEditValue DecodeValue(byte[] bytes)
    {
        var reader = new UsdEditReader(bytes);
        UsdLayerEditValue value = reader.Value();
        reader.End();
        return value;
    }

    private static byte[] MetadataValue(int tag)
    {
        var writer = new UsdEditWriter();
        writer.U32((uint)tag);
        switch (tag)
        {
            case 17:
            case 18:
                writer.U32(2);
                writer.Text("one");
                writer.Text("two");
                break;
            case 19:
            case 20:
                writer.U32(1);
                break;
            case 21:
                writer.U32(1);
                writer.F64(2.5);
                writer.Bytes(UsdLayerEditValue.FromDouble(BitConverter.UInt64BitsToDouble(0x7ff8000000001234)).Payload);
                break;
            case 22:
                writer.U32(1);
                writer.Text("exact:name");
                writer.Bytes(MetadataValue(21));
                break;
            case 23:
                writer.U32(1);
                writer.U32(1);
                writer.Text("token");
                for (int bucket = 1; bucket < 6; bucket++)
                {
                    writer.U32(0);
                }
                break;
        }
        return writer.Written.ToArray();
    }

    private static byte[] NestedValue(int depth)
    {
        var writer = new UsdEditWriter();
        for (int index = 0; index < depth; index++)
        {
            writer.U32(22);
            writer.U32(1);
            writer.Text("nested");
        }
        writer.U32(0);
        return writer.Written.ToArray();
    }

    private static byte[] CorruptValue(string corruption)
    {
        var writer = new UsdEditWriter();
        switch (corruption)
        {
            case "bool":
                writer.U32(2);
                writer.U32(2);
                break;
            case "bool-array":
                writer.U32(258);
                writer.U32(1);
                writer.U32(2);
                break;
            case "array-count":
                writer.U32(259);
                writer.U32(uint.MaxValue);
                break;
            case "array-truncated":
                writer.U32(259);
                writer.U32(1);
                break;
            case "int64-truncated":
                writer.U32(260);
                writer.U32(1);
                writer.U32(0);
                break;
            case "utf8":
                writer.U32(8);
                writer.U32(1);
                writer.Bytes([0xff]);
                break;
            case "unknown-tag":
                writer.U32(24);
                break;
            case "specifier":
                writer.U32(19);
                writer.U32(3);
                break;
            case "variability":
                writer.U32(20);
                writer.U32(2);
                break;
            case "list-flag":
                writer.U32(16);
                writer.U32(2);
                break;
            case "mixed-list":
            case "duplicate-list":
                writer.U32(16);
                writer.U32(corruption == "mixed-list" ? 0u : 1u);
                writer.U32(2);
                writer.Text("/A");
                writer.Text(corruption == "mixed-list" ? "/B" : "/A");
                for (int bucket = 1; bucket < 6; bucket++)
                {
                    writer.U32(0);
                }
                break;
            case "duplicate-dictionary":
                writer.U32(22);
                writer.U32(2);
                writer.Text("key");
                writer.U32(0);
                writer.Text("key");
                writer.U32(0);
                break;
            case "duplicate-time":
            case "nonfinite-time":
            case "empty-sample":
                writer.U32(21);
                writer.U32(2);
                writer.F64(corruption == "nonfinite-time" ? double.PositiveInfinity : -0.0);
                writer.Bytes(corruption == "empty-sample"
                    ? UsdLayerEditValue.Absent.Payload : UsdLayerEditValue.FromDouble(1).Payload);
                writer.F64(0.0);
                writer.Bytes(UsdLayerEditValue.FromDouble(2).Payload);
                break;
            case "depth":
                return NestedValue(16);
            case "trailing":
                writer.U32(0);
                writer.U32(0);
                break;
        }
        return writer.Written.ToArray();
    }

    private static byte[] CheckpointPacket(byte[] value)
    {
        UsdEditWriter writer = Packet(4);
        writer.Text("anon:review.usda");
        writer.Text("");
        writer.U32(1);
        writer.Text("/");
        writer.U32(7);
        writer.U32(1);
        writer.Text("unrecognizedMetadata");
        writer.Bytes(value);
        return writer.Written.ToArray();
    }

    private static byte[] CorruptCheckpoint(string corruption)
    {
        UsdEditWriter writer = Packet(4);
        writer.Text("anon:review.usda");
        writer.Text("");
        writer.U32(corruption switch
        {
            "missing-root" => 0,
            "spec-count" => uint.MaxValue,
            "duplicate-spec" => 2,
            _ => 1
        });
        int copies = corruption == "duplicate-spec" ? 2 : 1;
        for (int spec = 0; spec < copies; spec++)
        {
            writer.Text("/");
            writer.U32(corruption == "wrong-root-type" ? 6u : 7u);
            writer.U32(corruption == "field-count" ? 129u : corruption == "duplicate-field" ? 2u : 1u);
            writer.Text(corruption == "empty-field" ? "" : corruption == "bad-field-kind" ? "primChildren" : "unknown");
            writer.Bytes(corruption == "absent-field"
                ? UsdLayerEditValue.Absent.Payload : UsdLayerEditValue.FromInt32(1).Payload);
            if (corruption == "duplicate-field")
            {
                writer.Text("unknown");
                writer.Bytes(UsdLayerEditValue.FromInt32(2).Payload);
            }
        }
        if (corruption == "trailing")
        {
            writer.U32(0);
        }
        return writer.Written.ToArray();
    }

    private static byte[] CheckpointWithChildren(int children, string corruption)
    {
        UsdEditWriter writer = Packet(4);
        writer.Text("anon:review.usda");
        writer.Text("");
        writer.U32((uint)children + 1);
        writer.Text("/");
        writer.U32(7);
        writer.U32(1);
        writer.Text("primChildren");
        writer.U32(17);
        writer.U32((uint)children);
        for (int index = 0; index < children; index++)
        {
            string name = corruption switch
            {
                "duplicate-child" => "P0",
                "missing-child" when index == 0 => "Missing",
                "invalid-child-name" when index == 0 => "P0/Invalid",
                _ => $"P{index}"
            };
            writer.Text(name);
        }
        for (int index = 0; index < children; index++)
        {
            writer.Text(corruption == "missing-parent" && index == 0 ? "/Missing/Child" : $"/P{index}");
            writer.U32(6);
            writer.U32(0);
        }
        return writer.Written.ToArray();
    }
}
