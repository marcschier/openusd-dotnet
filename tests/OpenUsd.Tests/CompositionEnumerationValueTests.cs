// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class CompositionEnumerationValueTests
{
    [Test]
    public async Task PackedStringDecoderPreservesExactUtf8AndEmptyEntries()
    {
        (byte[] data, nuint[] offsets) =
            NativeStringListPacking.Pack(["textures/élan.usda", "", "/Model"]);

        string[] values = NativePackedStringListDecoder.Decode(
            data,
            offsets,
            "test string list");

        await Assert.That(values.SequenceEqual(["textures/élan.usda", "", "/Model"]))
            .IsTrue();
    }

    [Test]
    public async Task PackedStringDecoderRejectsMalformedOffsetsAndTerminators()
    {
        await Assert.That(() =>
                NativePackedStringListDecoder.Decode(
                    [(byte)'a', 0],
                    [(nuint)1],
                    "test string list"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() =>
                NativePackedStringListDecoder.Decode(
                    [(byte)'a'],
                    [(nuint)0],
                    "test string list"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() =>
                NativePackedStringListDecoder.Decode(
                    [(byte)'a', 0, (byte)'x'],
                    [(nuint)0],
                    "test string list"))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task PackedStringDecoderRejectsInvalidUtf8AndEmbeddedNullData()
    {
        await Assert.That(() =>
                NativePackedStringListDecoder.Decode(
                    [0xc3, 0],
                    [(nuint)0],
                    "test string list"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() =>
                NativePackedStringListDecoder.Decode(
                    [(byte)'a', 0, (byte)'b', 0],
                    [(nuint)0],
                    "test string list"))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task PayloadArcDecoderRequiresVersionedTriples()
    {
        (byte[] data, nuint[] offsets) =
            NativeStringListPacking.Pack(["relative.usda", "", "anon:root"]);

        OpenUsdNativePayloadArc[] arcs = NativePayloadArcListDecoder.Decode(
            NativePayloadArcListDecoder.Version,
            1,
            data,
            offsets);

        await Assert.That(arcs.Length).IsEqualTo(1);
        await Assert.That(arcs[0].AssetPath).IsEqualTo("relative.usda");
        await Assert.That(arcs[0].TargetPrimPath).IsEmpty();
        await Assert.That(arcs[0].SourceLayerIdentifier).IsEqualTo("anon:root");

        await Assert.That(() =>
                NativePayloadArcListDecoder.Decode(2, 1, data, offsets))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() =>
                NativePayloadArcListDecoder.Decode(
                    NativePayloadArcListDecoder.Version,
                    1,
                    data,
                    offsets.AsSpan()[..2]))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task PayloadArcDecoderRejectsMissingSourceIdentifier()
    {
        (byte[] data, nuint[] offsets) =
            NativeStringListPacking.Pack(["asset.usda", "/Model", ""]);

        await Assert.That(() =>
                NativePayloadArcListDecoder.Decode(
                    NativePayloadArcListDecoder.Version,
                    1,
                    data,
                    offsets))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task PayloadArcValueIsImmutableAndValidatesStrings()
    {
        var arc = new UsdPayloadArc("asset.usda", "/Model", "root.usda");

        await Assert.That(arc.AssetPath).IsEqualTo("asset.usda");
        await Assert.That(arc.TargetPrimPath).IsEqualTo("/Model");
        await Assert.That(arc.SourceLayerIdentifier).IsEqualTo("root.usda");
        await Assert.That(() => new UsdPayloadArc(null!, "", "root.usda"))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new UsdPayloadArc("", "", ""))
            .Throws<ArgumentException>();
    }
}
