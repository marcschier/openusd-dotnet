// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Interop.Tests;

/// <summary>
/// Friend tests at the Sdr node-definition decoder seam. Constructs
/// <c>OpenUsdNativeRuntime.NativeSdrNodeDefinitionView</c> instances directly -- including
/// deliberately malformed ones -- and proves <c>DecodeSdrNodeDefinitions</c> never lets a bad
/// native page reach unchecked indexing, arithmetic overflow, or an invalid coerced value: it
/// always throws <see cref="OpenUsdNativeException"/> instead.
/// </summary>
public sealed class SdrNodeDefinitionDecodeTests
{
    [Test]
    public async Task WellFormedBaselineDecodesCorrectly()
    {
        Baseline baseline = BuildBaseline();
        OpenUsdNativeSdrNodeDefinitionPage page = Decode(baseline);

        await Assert.That(page.IsTruncated).IsFalse();
        await Assert.That(page.Definitions.Length).IsEqualTo(1);
        OpenUsdNativeSdrNodeDefinition definition = page.Definitions[0];
        await Assert.That(definition.Identifier).IsEqualTo("Identifier");
        await Assert.That(definition.ImplementationName).IsEqualTo("Impl");
        await Assert.That(definition.IsValid).IsTrue();
        await Assert.That(definition.Properties.Length).IsEqualTo(1);
        await Assert.That(definition.Properties[0].Name).IsEqualTo("in");
        await Assert.That(definition.Properties[0].Type).IsEqualTo("float");
        await Assert.That(definition.Properties[0].Direction).IsEqualTo(OpenUsdNativeSdrPropertyDirection.Input);
        await Assert.That(definition.Properties[0].IsConnectable).IsTrue();
    }

    [Test]
    public async Task WellFormedTruncatedFlagDecodesAsTruncated()
    {
        Baseline baseline = BuildBaseline();
        OpenUsdNativeSdrNodeDefinitionPage page = Decode(baseline, flags: 0x1);

        await Assert.That(page.IsTruncated).IsTrue();
        await Assert.That(page.Definitions.Length).IsEqualTo(1);
    }

    [Test]
    public async Task ThrowsWhenViewReservedIsNonZero()
    {
        Baseline baseline = BuildBaseline();
        await Assert.That(() => Decode(baseline, reserved: 1)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenAnUnknownFlagBitIsSet()
    {
        Baseline baseline = BuildBaseline();
        await Assert.That(() => Decode(baseline, flags: 0x2)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenRecordReservedIsNonZero()
    {
        Baseline baseline = BuildBaseline();
        baseline.Records[0] = baseline.Records[0] with { Reserved = 1 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenRecordIsValidIsNotZeroOrOne()
    {
        Baseline baseline = BuildBaseline();
        baseline.Records[0] = baseline.Records[0] with { IsValid = 2 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenRecordStringCountIsNotEight()
    {
        Baseline baseline = BuildBaseline();
        baseline.Records[0] = baseline.Records[0] with { StringCount = 7 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenRecordStringOffsetIsOutOfRange()
    {
        Baseline baseline = BuildBaseline();
        // Ten strings exist (0..9); offset 3 plus a count of 8 reaches past the end.
        baseline.Records[0] = baseline.Records[0] with { StringOffset = 3 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenRecordStringOffsetIsNearNuintMaxWithoutOverflowing()
    {
        Baseline baseline = BuildBaseline();
        baseline.Records[0] = baseline.Records[0] with { StringOffset = nuint.MaxValue - 2 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenRecordPropertyRangeIsOutOfRange()
    {
        Baseline baseline = BuildBaseline();
        // Only one property record exists (index 0); this asks for two starting at index 1.
        baseline.Records[0] = baseline.Records[0] with { PropertyOffset = 1, PropertyCount = 1 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenPropertyReservedIsNonZero()
    {
        Baseline baseline = BuildBaseline();
        baseline.Properties[0] = baseline.Properties[0] with { Reserved = 1 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenPropertyDirectionIsInvalid()
    {
        Baseline baseline = BuildBaseline();
        baseline.Properties[0] = baseline.Properties[0] with { Direction = 2 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenPropertyIsArrayIsNotZeroOrOne()
    {
        Baseline baseline = BuildBaseline();
        baseline.Properties[0] = baseline.Properties[0] with { IsArray = 5 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenPropertyIsConnectableIsNotZeroOrOne()
    {
        Baseline baseline = BuildBaseline();
        baseline.Properties[0] = baseline.Properties[0] with { IsConnectable = -1 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenPropertyStringCountIsNotTwo()
    {
        Baseline baseline = BuildBaseline();
        baseline.Properties[0] = baseline.Properties[0] with { StringCount = 1 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ThrowsWhenPropertyStringOffsetIsOutOfRange()
    {
        Baseline baseline = BuildBaseline();
        // Ten strings exist (0..9); offset 9 plus a count of 2 reaches past the end.
        baseline.Properties[0] = baseline.Properties[0] with { StringOffset = 9 };
        await Assert.That(() => Decode(baseline)).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ResolveFromAssetReturnsFalseWhenNotFoundAndPageIsEmpty()
    {
        var page = new OpenUsdNativeSdrNodeDefinitionPage([], false);
        bool found = OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
            0, page, out OpenUsdNativeSdrNodeDefinition definition, out bool isTruncated);

        await Assert.That(found).IsFalse();
        await Assert.That(definition).IsEqualTo(default(OpenUsdNativeSdrNodeDefinition));
        await Assert.That(isTruncated).IsFalse();
    }

    [Test]
    public async Task ResolveFromAssetReturnsTrueWhenFoundAndPageHasExactlyOneRecord()
    {
        OpenUsdNativeSdrNodeDefinition expected = BuildBaseline().Definitions[0];
        var page = new OpenUsdNativeSdrNodeDefinitionPage([expected], false);
        bool found = OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
            1, page, out OpenUsdNativeSdrNodeDefinition definition, out bool isTruncated);

        await Assert.That(found).IsTrue();
        await Assert.That(definition.Identifier).IsEqualTo(expected.Identifier);
        await Assert.That(isTruncated).IsFalse();
    }

    [Test]
    public async Task ResolveFromAssetPropagatesIsTruncated()
    {
        var page = new OpenUsdNativeSdrNodeDefinitionPage([], true);
        OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
            0, page, out _, out bool isTruncated);

        await Assert.That(isTruncated).IsTrue();
    }

    [Test]
    public async Task ResolveFromAssetThrowsWhenFoundIsNotZeroOrOne()
    {
        var page = new OpenUsdNativeSdrNodeDefinitionPage([], false);
        await Assert.That(() => OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
                2, page, out _, out _))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ResolveFromAssetThrowsWhenNotFoundButPageHasARecord()
    {
        OpenUsdNativeSdrNodeDefinition unexpected = BuildBaseline().Definitions[0];
        var page = new OpenUsdNativeSdrNodeDefinitionPage([unexpected], false);
        await Assert.That(() => OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
                0, page, out _, out _))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ResolveFromAssetThrowsWhenFoundButPageHasNoRecords()
    {
        var page = new OpenUsdNativeSdrNodeDefinitionPage([], false);
        await Assert.That(() => OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
                1, page, out _, out _))
            .Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task ResolveFromAssetThrowsWhenFoundButPageHasMoreThanOneRecord()
    {
        OpenUsdNativeSdrNodeDefinition one = BuildBaseline().Definitions[0];
        var page = new OpenUsdNativeSdrNodeDefinitionPage([one, one], false);
        await Assert.That(() => OpenUsdNativeRuntime.ResolveSdrNodeDefinitionFromAssetResult(
                1, page, out _, out _))
            .Throws<OpenUsdNativeException>();
    }

    /// <summary>
    /// One well-formed baseline page: one node with one input property. Every malformed-decode
    /// test above corrupts exactly one field of this baseline and asserts the decoder rejects it.
    /// </summary>
    private sealed class Baseline
    {
        public required OpenUsdNativeSdrNodeDefinitionRecord[] Records { get; init; }

        public required OpenUsdNativeSdrPropertyRecord[] Properties { get; init; }

        public required byte[] Data { get; init; }

        public required nuint[] Offsets { get; init; }

        public required OpenUsdNativeSdrNodeDefinition[] Definitions { get; init; }
    }

    private static Baseline BuildBaseline()
    {
        (byte[] data, nuint[] offsets) = PackStrings(
            "Identifier", "Name", "Function", "glslfx", "surface",
            "file:///definition", "file:///implementation", "Impl",
            "in", "float");

        var records = new[]
        {
            new OpenUsdNativeSdrNodeDefinitionRecord(
                IsValid: 1,
                Reserved: 0,
                StringOffset: 0,
                StringCount: 8,
                PropertyOffset: 0,
                PropertyCount: 1)
        };
        var properties = new[]
        {
            new OpenUsdNativeSdrPropertyRecord(
                Direction: 0,
                IsArray: 0,
                IsConnectable: 1,
                Reserved: 0,
                StringOffset: 8,
                StringCount: 2)
        };
        var definitions = new[]
        {
            new OpenUsdNativeSdrNodeDefinition(
                "Identifier",
                "Name",
                "Function",
                "glslfx",
                "surface",
                "file:///definition",
                "file:///implementation",
                "Impl",
                [
                    new OpenUsdNativeSdrProperty(
                        "in", "float", OpenUsdNativeSdrPropertyDirection.Input, false, true)
                ],
                true)
        };
        return new Baseline
        {
            Records = records,
            Properties = properties,
            Data = data,
            Offsets = offsets,
            Definitions = definitions
        };
    }

    private static (byte[] Data, nuint[] Offsets) PackStrings(params string[] values)
    {
        var data = new List<byte>();
        var offsets = new nuint[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            offsets[i] = (nuint)data.Count;
            data.AddRange(Encoding.UTF8.GetBytes(values[i]));
            data.Add(0);
        }
        return ([.. data], offsets);
    }

    private static unsafe OpenUsdNativeSdrNodeDefinitionPage Decode(
        Baseline baseline, uint flags = 0, uint reserved = 0)
    {
        OpenUsdNativeSdrNodeDefinitionRecord[] records = baseline.Records;
        OpenUsdNativeSdrPropertyRecord[] properties = baseline.Properties;
        byte[] data = baseline.Data;
        nuint[] offsets = baseline.Offsets;

        fixed (OpenUsdNativeSdrNodeDefinitionRecord* recordsPtr = records)
        fixed (OpenUsdNativeSdrPropertyRecord* propertiesPtr = properties)
        fixed (byte* dataPtr = data)
        fixed (nuint* offsetsPtr = offsets)
        {
            var view = new OpenUsdNativeRuntime.NativeSdrNodeDefinitionView
            {
                StructSize = (uint)sizeof(OpenUsdNativeRuntime.NativeSdrNodeDefinitionView),
                Version = 1,
                Flags = flags,
                Reserved = reserved,
                Records = recordsPtr,
                RecordsSize = (nuint)(records.Length * sizeof(OpenUsdNativeSdrNodeDefinitionRecord)),
                RecordCount = (nuint)records.Length,
                Properties = propertiesPtr,
                PropertiesSize = (nuint)(properties.Length * sizeof(OpenUsdNativeSdrPropertyRecord)),
                PropertyCount = (nuint)properties.Length,
                Data = data.Length == 0 ? null : dataPtr,
                DataSize = (nuint)data.Length,
                Offsets = offsets.Length == 0 ? null : offsetsPtr,
                OffsetsSize = (nuint)(offsets.Length * sizeof(nuint)),
                StringCount = (nuint)offsets.Length
            };
            return OpenUsdNativeRuntime.DecodeSdrNodeDefinitions(view);
        }
    }
}
