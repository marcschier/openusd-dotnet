// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class NativeStringListPackingTests
{
    [Test]
    public async Task PackEmptySpanReturnsEmptyBuffers()
    {
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack([]);

        await Assert.That(data).IsEmpty();
        await Assert.That(offsets).IsEmpty();
    }

    [Test]
    public async Task PackSingleValueNullTerminatesTheEntry()
    {
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(["/World/A"]);

        await Assert.That(offsets).IsEquivalentTo([(nuint)0]);
        await Assert.That(data.Length).IsEqualTo("/World/A".Length + 1);
        await Assert.That(data[^1]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task PackMultipleValuesRecordsContiguousOffsets()
    {
        string[] values = ["/World/A", "/World/BB", "/World/CCC"];

        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(values);

        await Assert.That(offsets.Length).IsEqualTo(values.Length);
        await Assert.That(offsets[0]).IsEqualTo((nuint)0);

        for (int i = 0; i < values.Length; i++)
        {
            int start = (int)offsets[i];
            int terminator = Array.IndexOf(data, (byte)0, start);
            string decoded = System.Text.Encoding.UTF8.GetString(data, start, terminator - start);
            await Assert.That(decoded).IsEqualTo(values[i]);
        }

        int expectedLength = values.Sum(value => value.Length + 1);
        await Assert.That(data.Length).IsEqualTo(expectedLength);
    }

    [Test]
    public async Task PackRejectsNullEntries()
    {
        await Assert.That(() => NativeStringListPacking.Pack([null!]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PackRejectsEmbeddedNullEntries()
    {
        await Assert.That(() => NativeStringListPacking.Pack(["/World\0/Hidden"]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PackPreservesUnicodeUtf8()
    {
        const string value = "/世界/Albédo";
        (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack([value]);

        string decoded = System.Text.Encoding.UTF8.GetString(data.AsSpan(0, data.Length - 1));
        await Assert.That(offsets).IsEquivalentTo([(nuint)0]);
        await Assert.That(decoded).IsEqualTo(value);
    }
}
