// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class NativeStringListMutationTests
{
    private const int MutationCount = 1_000;
    private const int MutationSeed = 0x51A7_2026;

    [Test]
    public async Task PackingRoundTripsSeededBoundedUnicodeLists()
    {
        var random = new Random(MutationSeed);
        const int caseCount = 256;
        int completed = 0;
        for (int testCase = 0; testCase < caseCount; testCase++)
        {
            var values = new string[random.Next(0, 9)];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = CreateValue(random);
            }

            (byte[] data, nuint[] offsets) = NativeStringListPacking.Pack(values);
            string[] decoded = NativePackedStringListDecoder.Decode(
                data,
                offsets,
                "seeded packed string list");
            if (!decoded.SequenceEqual(values, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Packed string round-trip {testCase} changed its values.");
            }
            completed++;
        }

        await Assert.That(completed).IsEqualTo(caseCount);
    }

    [Test]
    public async Task DecoderRejectsBoundaryTerminatorUtf8AndCountMismatches()
    {
        string[] boundaryValues = NativePackedStringListDecoder.Decode(
            [0, (byte)'a', 0],
            [(nuint)0, (nuint)1],
            "boundary test string list");
        await Assert.That(boundaryValues.SequenceEqual(["", "a"]))
            .IsTrue();

        var malformed = new (byte[] Data, nuint[] Offsets)[]
        {
            ([(byte)'a'], [(nuint)0]),
            ([(byte)'a', 0, 0], [(nuint)0]),
            ([(byte)'a', 0, (byte)'b'], [(nuint)0, (nuint)2]),
            ([(byte)'a', 0], [(nuint)0, (nuint)2]),
            ([(byte)'a', 0], [(nuint)0, (nuint)3]),
            ([0xc3, 0], [(nuint)0]),
            ([0xe2, 0x28, 0xa1, 0], [(nuint)0]),
            ([(byte)'a', 0], []),
            ([], [(nuint)0]),
            ([(byte)'a', 0], [(nuint)1]),
            ([(byte)'a', 0], [(nuint)0, nuint.MaxValue]),
        };

        foreach ((byte[] data, nuint[] offsets) in malformed)
        {
            OpenUsdNativeException exception = CaptureNativeFailure(
                () => NativePackedStringListDecoder.Decode(
                    data,
                    offsets,
                    "boundary test string list"));
            await Assert.That(exception.Status)
                .IsEqualTo(OpenUsdNativeStatus.NativeError);
        }

        (byte[] arcData, nuint[] arcOffsets) =
            NativeStringListPacking.Pack(["asset.usda", "/Model", "root.usda"]);
        foreach (nuint count in new nuint[] { 0, 2, nuint.MaxValue })
        {
            OpenUsdNativeException exception = CaptureNativeFailure(
                () => NativePayloadArcListDecoder.Decode(
                    NativePayloadArcListDecoder.Version,
                    count,
                    arcData,
                    arcOffsets));
            await Assert.That(exception.Status)
                .IsEqualTo(OpenUsdNativeStatus.NativeError);
        }

        OpenUsdNativeException shortOffsets = CaptureNativeFailure(
            () => NativePayloadArcListDecoder.Decode(
                NativePayloadArcListDecoder.Version,
                1,
                arcData,
                arcOffsets.AsSpan(0, 2)));
        await Assert.That(shortOffsets.Status)
            .IsEqualTo(OpenUsdNativeStatus.NativeError);
    }

    [Test]
    public async Task SeededPackedBufferMutationsAreCanonicalOrFailWithNativeError()
    {
        (byte[] packedData, nuint[] packedOffsets) =
            NativeStringListPacking.Pack(["/World/A", "élan", "", "世界"]);
        var random = new Random(MutationSeed);
        int accepted = 0;
        int rejected = 0;

        for (int mutation = 0; mutation < MutationCount; mutation++)
        {
            byte[] data = (byte[])packedData.Clone();
            nuint[] offsets = (nuint[])packedOffsets.Clone();
            ApplyMutation(random, mutation, ref data, ref offsets);

            try
            {
                string[] decoded = NativePackedStringListDecoder.Decode(
                    data,
                    offsets,
                    "mutated packed string list");
                (byte[] canonicalData, nuint[] canonicalOffsets) =
                    NativeStringListPacking.Pack(decoded);
                if (!canonicalData.AsSpan().SequenceEqual(data) ||
                    !canonicalOffsets.AsSpan().SequenceEqual(offsets))
                {
                    throw new InvalidOperationException(
                        $"Mutation {mutation} decoded to a non-canonical string-list shape.");
                }
                accepted++;
            }
            catch (OpenUsdNativeException exception)
                when (exception.Status == OpenUsdNativeStatus.NativeError)
            {
                rejected++;
            }
        }

        await Assert.That(accepted + rejected).IsEqualTo(MutationCount);
        await Assert.That(accepted).IsGreaterThan(0);
        await Assert.That(rejected).IsGreaterThan(0);
    }

    private static string CreateValue(Random random)
    {
        string[] fragments = ["", "alpha", "é", "世界", "🙂", "/World", "_9"];
        var builder = new StringBuilder();
        int fragmentCount = random.Next(0, 5);
        for (int index = 0; index < fragmentCount; index++)
        {
            builder.Append(fragments[random.Next(fragments.Length)]);
        }
        return builder.ToString();
    }

    private static void ApplyMutation(
        Random random,
        int mutation,
        ref byte[] data,
        ref nuint[] offsets)
    {
        switch (mutation % 10)
        {
            case 0:
                int byteIndex = random.Next(data.Length);
                data[byteIndex] ^= checked((byte)random.Next(1, 256));
                break;
            case 1:
                data = RemoveAt(data, random.Next(data.Length));
                break;
            case 2:
                data = InsertAt(
                    data,
                    random.Next(data.Length + 1),
                    checked((byte)random.Next(0, 256)));
                break;
            case 3:
                int offsetIndex = random.Next(offsets.Length);
                nuint[] boundaries =
                [
                    0,
                    (nuint)data.Length,
                    (nuint)data.Length + 1,
                    (nuint)int.MaxValue,
                    nuint.MaxValue,
                ];
                nuint mutatedOffset;
                do
                {
                    mutatedOffset = boundaries[random.Next(boundaries.Length)];
                }
                while (mutatedOffset == offsets[offsetIndex]);
                offsets[offsetIndex] = mutatedOffset;
                break;
            case 4:
                offsets = RemoveAt(offsets, random.Next(offsets.Length));
                break;
            case 5:
                offsets = InsertAt(
                    offsets,
                    random.Next(offsets.Length + 1),
                    (nuint)random.Next(data.Length + 2));
                break;
            case 6:
                data[^1] = checked((byte)random.Next(1, 256));
                break;
            case 7:
                data = [.. data, 0];
                break;
            case 8:
                int terminator = Array.IndexOf(data, (byte)0);
                data[terminator - 1] = 0xc3;
                break;
            default:
                offsets[0] = 1;
                break;
        }
    }

    private static byte[] InsertAt(byte[] values, int index, byte value)
    {
        var result = new byte[values.Length + 1];
        values.AsSpan(0, index).CopyTo(result);
        result[index] = value;
        values.AsSpan(index).CopyTo(result.AsSpan(index + 1));
        return result;
    }

    private static nuint[] InsertAt(nuint[] values, int index, nuint value)
    {
        var result = new nuint[values.Length + 1];
        values.AsSpan(0, index).CopyTo(result);
        result[index] = value;
        values.AsSpan(index).CopyTo(result.AsSpan(index + 1));
        return result;
    }

    private static byte[] RemoveAt(byte[] values, int index)
    {
        var result = new byte[values.Length - 1];
        values.AsSpan(0, index).CopyTo(result);
        values.AsSpan(index + 1).CopyTo(result.AsSpan(index));
        return result;
    }

    private static nuint[] RemoveAt(nuint[] values, int index)
    {
        var result = new nuint[values.Length - 1];
        values.AsSpan(0, index).CopyTo(result);
        values.AsSpan(index + 1).CopyTo(result.AsSpan(index));
        return result;
    }

    private static OpenUsdNativeException CaptureNativeFailure(Action action)
    {
        try
        {
            action();
        }
        catch (OpenUsdNativeException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected an OpenUsdNativeException.");
    }
}
