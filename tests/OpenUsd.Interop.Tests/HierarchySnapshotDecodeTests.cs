// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop.Tests;

public sealed class HierarchySnapshotDecodeTests
{
    [Test]
    public async Task AnInstanceWithoutTheNativeInstanceableFlagIsRejected()
    {
        await Assert.That(() => Decode(instanceable: false)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task CanonicalOwnedDataPreservesTopologyVariantsAndChangeSerial()
    {
        OpenUsdNativeHierarchySnapshot snapshot = Decode();
        await Assert.That(snapshot.ChangeSerial).IsEqualTo(42UL);
        await Assert.That(snapshot.IsComplete).IsTrue();
        await Assert.That(snapshot.Entries.Length).IsEqualTo(3);
        await Assert.That(snapshot.Entries[0].Path).IsEqualTo("/Instance");
        await Assert.That(snapshot.Entries[2].Values.ParentIndex).IsEqualTo(1);
        await Assert.That(snapshot.Entries[2].Path).IsEqualTo("/__Prototype_1/Geom");
        await Assert.That(snapshot.Entries[0].VariantSets[0].Selection).IsEqualTo("amber");
        await Assert.That(snapshot.Entries[0].VariantSets[0].VariantNames[0]).IsEqualTo("amber");
        await Assert.That(snapshot.Entries[0].VariantSets[0].VariantNames[1]).IsEqualTo("zebra");
    }

    [Test]
    public async Task LayoutMatchesThePointerSizedCViewAndFixedWidthRecords()
    {
        await Assert.That(Marshal.SizeOf<OpenUsdNativeHierarchyLimits>()).IsEqualTo(32);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeHierarchyEntryRecord>()).IsEqualTo(32);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeHierarchyVariantRecord>()).IsEqualTo(8);
        await Assert.That(Marshal.SizeOf<OpenUsdNativeRuntime.NativeHierarchyView>())
            .IsEqualTo(24 + (11 * IntPtr.Size));
        await Assert.That(Marshal.OffsetOf<OpenUsdNativeRuntime.NativeHierarchyView>("Entries").ToInt32())
            .IsEqualTo(24);
    }

    [Test]
    [Arguments("header-size")]
    [Arguments("version")]
    [Arguments("complete-flag")]
    [Arguments("complete-mismatch")]
    [Arguments("work-budget")]
    [Arguments("entry-budget")]
    [Arguments("variant-budget")]
    [Arguments("text-budget")]
    [Arguments("string-budget")]
    [Arguments("entries-null")]
    [Arguments("variants-null")]
    [Arguments("offsets-null")]
    [Arguments("data-null")]
    [Arguments("entries-alignment")]
    [Arguments("variants-alignment")]
    [Arguments("offsets-alignment")]
    [Arguments("address-overflow")]
    [Arguments("entries-size")]
    [Arguments("variants-size")]
    [Arguments("offsets-size")]
    [Arguments("depth-zero")]
    [Arguments("depth-budget")]
    [Arguments("depth-gap")]
    [Arguments("parent-future")]
    [Arguments("child-count")]
    [Arguments("unknown-flags")]
    [Arguments("instance-proxy")]
    [Arguments("prototype-child")]
    [Arguments("prototype-outside-prototype")]
    [Arguments("scene-in-prototype")]
    [Arguments("entry-string-offset")]
    [Arguments("variant-offset")]
    [Arguments("variant-count")]
    [Arguments("variant-status")]
    [Arguments("deferred-with-variants")]
    [Arguments("variant-string-offset")]
    [Arguments("choice-budget")]
    [Arguments("offset-noncanonical")]
    [Arguments("embedded-nul")]
    [Arguments("unterminated-string")]
    [Arguments("invalid-utf8")]
    [Arguments("trailing-bytes")]
    [Arguments("relative-path")]
    [Arguments("pseudo-root-path")]
    [Arguments("duplicate-path")]
    [Arguments("wrong-parent-path")]
    [Arguments("wrong-name")]
    [Arguments("instance-without-prototype")]
    [Arguments("noninstance-with-prototype")]
    [Arguments("missing-prototype")]
    [Arguments("nonprototype-target")]
    [Arguments("unreferenced-prototype")]
    [Arguments("empty-set-name")]
    [Arguments("empty-choice")]
    [Arguments("duplicate-choice")]
    public async Task MalformedHierarchyDataCannotBecomeAPartialOrUnsafeSnapshot(string corruption)
    {
        await Assert.That(() => Decode(corruption)).Throws<OpenUsdNativeException>();
    }

    private static unsafe OpenUsdNativeHierarchySnapshot Decode(string corruption = "", bool instanceable = true)
    {
        string[] fields =
        [
            "/Instance", "Instance", "Xform", "/__Prototype_1",
            "look", "amber", "amber", "zebra",
            "/__Prototype_1", "__Prototype_1", "", "",
            "/__Prototype_1/Geom", "Geom", "Mesh", ""
        ];
        switch (corruption)
        {
            case "relative-path":
                fields[0] = "relative";
                break;
            case "pseudo-root-path":
                fields[0] = "/";
                break;
            case "duplicate-path":
                fields[12] = "/__Prototype_1";
                fields[13] = "__Prototype_1";
                break;
            case "wrong-parent-path":
                fields[12] = "/Other/Geom";
                break;
            case "wrong-name":
                fields[1] = "Mismatch";
                break;
            case "instance-without-prototype":
            case "unreferenced-prototype":
                fields[3] = "";
                break;
            case "missing-prototype":
                fields[3] = "/__Prototype_2";
                break;
            case "nonprototype-target":
                fields[3] = "/__Prototype_1/Geom";
                break;
            case "empty-set-name":
                fields[4] = "";
                break;
            case "empty-choice":
                fields[6] = "";
                break;
            case "duplicate-choice":
                fields[7] = "amber";
                break;
        }
        (byte[] data, nuint[] packedOffsets) = NativeStringListPacking.Pack(fields);
        if (corruption == "trailing-bytes")
        {
            data = [.. data, 1];
        }
        uint[] offsets = packedOffsets.Select(offset => (uint)offset).ToArray();
        OpenUsdNativeHierarchyEntryRecord[] entries =
        [
            new(-1, 1, 0, instanceable ? 583u : 71u, 0, 0, 1, 0),
            new(-1, 1, 1, 55, 8, 1, 0, 0),
            new(1, 2, 0, 39, 12, 1, 0, 0)
        ];
        OpenUsdNativeHierarchyVariantRecord[] variants = [new(4, 2)];
        fixed (byte* dataPointer = data)
        fixed (uint* offsetsPointer = offsets)
        fixed (OpenUsdNativeHierarchyEntryRecord* entryPointer = entries)
        fixed (OpenUsdNativeHierarchyVariantRecord* variantPointer = variants)
        {
            var view = new OpenUsdNativeRuntime.NativeHierarchyView
            {
                StructSize = (uint)sizeof(OpenUsdNativeRuntime.NativeHierarchyView),
                Version = 1,
                ChangeSerial = 42,
                IsComplete = 1,
                MetadataWork = 17,
                Entries = entryPointer,
                EntriesSize = (nuint)(entries.Length * sizeof(OpenUsdNativeHierarchyEntryRecord)),
                EntryCount = (nuint)entries.Length,
                VariantSets = variantPointer,
                VariantSetsSize = (nuint)sizeof(OpenUsdNativeHierarchyVariantRecord),
                VariantSetCount = 1,
                Data = dataPointer,
                DataSize = (nuint)data.Length,
                Offsets = offsetsPointer,
                OffsetsSize = (nuint)(offsets.Length * sizeof(uint)),
                StringCount = (nuint)offsets.Length
            };
            switch (corruption)
            {
                case "header-size":
                    view.StructSize--;
                    break;
                case "version":
                    view.Version++;
                    break;
                case "complete-flag":
                    view.IsComplete = 2;
                    break;
                case "complete-mismatch":
                    view.IsComplete = 0;
                    break;
                case "work-budget":
                    view.MetadataWork = 1025;
                    break;
                case "entry-budget":
                    view.EntryCount = nuint.MaxValue;
                    break;
                case "variant-budget":
                    view.VariantSetCount = nuint.MaxValue;
                    break;
                case "text-budget":
                    view.DataSize = 4097;
                    break;
                case "string-budget":
                    view.StringCount = nuint.MaxValue;
                    break;
                case "entries-null":
                    view.Entries = null;
                    break;
                case "variants-null":
                    view.VariantSets = null;
                    break;
                case "offsets-null":
                    view.Offsets = null;
                    break;
                case "data-null":
                    view.Data = null;
                    break;
                case "entries-alignment":
                    view.Entries = (OpenUsdNativeHierarchyEntryRecord*)((byte*)entryPointer + 1);
                    break;
                case "variants-alignment":
                    view.VariantSets = (OpenUsdNativeHierarchyVariantRecord*)((byte*)variantPointer + 1);
                    break;
                case "offsets-alignment":
                    view.Offsets = (uint*)((byte*)offsetsPointer + 1);
                    break;
                case "address-overflow":
                    view.Data = (byte*)(nuint.MaxValue - 1);
                    break;
                case "entries-size":
                    view.EntriesSize--;
                    break;
                case "variants-size":
                    view.VariantSetsSize--;
                    break;
                case "offsets-size":
                    view.OffsetsSize--;
                    break;
                case "depth-zero":
                    entries[0] = entries[0] with { Depth = 0 };
                    break;
                case "depth-budget":
                    entries[0] = entries[0] with { Depth = 33 };
                    break;
                case "depth-gap":
                    entries[0] = entries[0] with { Depth = 2 };
                    break;
                case "parent-future":
                    entries[2] = entries[2] with { ParentIndex = 2 };
                    break;
                case "child-count":
                    entries[1] = entries[1] with { ChildCount = 2 };
                    break;
                case "unknown-flags":
                    entries[0] = entries[0] with { Flags = uint.MaxValue };
                    break;
                case "instance-proxy":
                    entries[0] = entries[0] with { Flags = 711 };
                    break;
                case "prototype-child":
                    entries[2] = entries[2] with { Flags = 55 };
                    break;
                case "prototype-outside-prototype":
                    entries[1] = entries[1] with { Flags = 23 };
                    break;
                case "scene-in-prototype":
                    entries[0] = entries[0] with { Flags = 615 };
                    break;
                case "entry-string-offset":
                    entries[0] = entries[0] with { StringOffset = 1 };
                    break;
                case "variant-offset":
                    entries[0] = entries[0] with { VariantOffset = 1 };
                    break;
                case "variant-count":
                    entries[0] = entries[0] with { VariantCount = uint.MaxValue };
                    break;
                case "variant-status":
                    entries[0] = entries[0] with { VariantStatus = 2 };
                    break;
                case "deferred-with-variants":
                    entries[0] = entries[0] with { VariantStatus = 1 };
                    break;
                case "variant-string-offset":
                    variants[0] = variants[0] with { StringOffset = 5 };
                    break;
                case "choice-budget":
                    variants[0] = variants[0] with { VariantCount = 33 };
                    break;
                case "offset-noncanonical":
                    offsets[1]++;
                    break;
                case "embedded-nul":
                    data[3] = 0;
                    break;
                case "unterminated-string":
                    data[^1] = 1;
                    break;
                case "invalid-utf8":
                    data[0] = 255;
                    break;
                case "noninstance-with-prototype":
                case "unreferenced-prototype":
                    entries[0] = entries[0] with { Flags = 7 };
                    break;
            }
            return OpenUsdNativeRuntime.DecodeHierarchySnapshot(view, new OpenUsdNativeHierarchyLimits(
                32, 1, 100, 4096, 32, 16, 32, 1024));
        }
    }
}
