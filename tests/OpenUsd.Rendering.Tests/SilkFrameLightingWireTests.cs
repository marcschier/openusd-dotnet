// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Round-trips the page ABI 9 lighting variant of the frame command.
/// </summary>
/// <remarks>
/// The frame command has three valid sizes: 272 bytes carrying only the
/// viewport and matrices, 536 adding the clip plane table, and 1272 adding the
/// light table and ambient term. Before these tests the 1272-byte variant had
/// no managed coverage at all -- every hand-written encoder in the repository
/// builds a 272 or 536 byte frame, and the lighting layout was exercised only
/// end to end through real hdSilk pages in the parity harness.
///
/// That is the same shape as the defect that shipped with the lighting work
/// itself: the GPU frame constants buffer grew from 208 to 544 bytes and two
/// hand-written copies still allocated 208, which Windows tolerated by
/// returning values that happened to work while SwiftShader on Linux returned
/// zeros. Only the Linux leg of CI failed. A managed round trip catches an
/// offset error in seconds and on every platform.
///
/// Every field is authored to a distinct value, and each light is given
/// different values from its neighbours, so an indexing error cannot pass by
/// reading the wrong entry and finding the same number there.
/// </remarks>
public sealed class SilkFrameLightingWireTests
{
    private const int MinimumSize = 272;
    private const int ExtendedSize = 536;
    private const int LightingSize = 1272;
    private const int LightCountOffset = ExtendedSize;
    private const int LightTableOffset = ExtendedSize + 16;
    private const int LightEntrySize = 176;
    private const int MaximumLights = 4;
    private const int AmbientOffset = LightTableOffset + (MaximumLights * LightEntrySize);

    [Test]
    public async Task LightingFrameRoundTripsEveryLightFieldAtItsOwnIndex()
    {
        byte[] page = CreateLightingFrame(lightCount: 3);

        uint lightCount;
        uint[] types = new uint[MaximumLights];
        uint[] shadows = new uint[MaximumLights];
        float[] intensities = new float[MaximumLights];
        float[] exposures = new float[MaximumLights];
        float[] radii = new float[MaximumLights];
        float[] greenChannels = new float[MaximumLights];
        double[] translations = new double[MaximumLights];
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                page,
                1,
                SilkCommandParser.PageAbiVersion);
            if (!commands.MoveNext())
            {
                throw new InvalidDataException("Missing frame command.");
            }
            SilkFrameCommand frame = commands.Current.AsFrame();
            lightCount = frame.LightCount;
            for (int i = 0; i < MaximumLights; i++)
            {
                types[i] = frame.GetLightType(i);
                shadows[i] = frame.GetLightShadowEnabled(i);
                intensities[i] = frame.GetLightIntensity(i);
                exposures[i] = frame.GetLightExposure(i);
                radii[i] = frame.GetLightRadius(i);
                greenChannels[i] = frame.GetLightColor(i, 1);

                // Element 12 is the row-major translation X, the last row of
                // the transform, so it also proves the 128-byte matrix is
                // read at the right base.
                translations[i] = frame.GetLightTransformElement(i, 12);
            }
        }

        await Assert.That(lightCount).IsEqualTo(3u);
        for (int i = 0; i < MaximumLights; i++)
        {
            await Assert.That(types[i]).IsEqualTo((uint)(i + 1));
            await Assert.That(shadows[i]).IsEqualTo((uint)(i % 2));
            await Assert.That(intensities[i]).IsEqualTo(10f + i);
            await Assert.That(exposures[i]).IsEqualTo(20f + i);
            await Assert.That(radii[i]).IsEqualTo(30f + i);
            await Assert.That(greenChannels[i]).IsEqualTo(0.5f + i);
            await Assert.That(translations[i]).IsEqualTo(100d + i);
        }
    }

    [Test]
    public async Task LightingFrameRoundTripsTheAmbientTermAfterTheWholeLightTable()
    {
        byte[] page = CreateLightingFrame(lightCount: 1);

        float red;
        float green;
        float blue;
        float intensity;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                page,
                1,
                SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            SilkFrameCommand frame = commands.Current.AsFrame();
            red = frame.GetAmbientColor(0);
            green = frame.GetAmbientColor(1);
            blue = frame.GetAmbientColor(2);
            intensity = frame.AmbientIntensity;
        }

        // The ambient term sits immediately after four full light entries, so
        // reading it proves the light entry size and the table length at once.
        await Assert.That(red).IsEqualTo(0.25f);
        await Assert.That(green).IsEqualTo(0.5f);
        await Assert.That(blue).IsEqualTo(0.75f);
        await Assert.That(intensity).IsEqualTo(0.875f);
    }

    [Test]
    public async Task FramesWithoutTheLightingSectionReportNoLightsAndIdentityTransforms()
    {
        // The 272 and 536 byte variants are still valid, and every accessor
        // must fall back rather than read past the end of the command.
        foreach (int size in new[] { MinimumSize, ExtendedSize })
        {
            byte[] page = CreateFrame(size);
            uint lightCount;
            float intensity;
            float ambient;
            double diagonal;
            double offDiagonal;
            {
                using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                    page,
                    1,
                    SilkCommandParser.PageAbiVersion);
                _ = commands.MoveNext();
                SilkFrameCommand frame = commands.Current.AsFrame();
                lightCount = frame.LightCount;
                intensity = frame.GetLightIntensity(0);
                ambient = frame.AmbientIntensity;
                diagonal = frame.GetLightTransformElement(0, 0);
                offDiagonal = frame.GetLightTransformElement(0, 1);
            }

            await Assert.That(lightCount).IsEqualTo(0u);
            await Assert.That(intensity).IsEqualTo(0f);
            await Assert.That(ambient).IsEqualTo(0f);
            await Assert.That(diagonal).IsEqualTo(1d);
            await Assert.That(offDiagonal).IsEqualTo(0d);
        }
    }

    [Test]
    public async Task LightingFrameRejectsALightCountAboveTheTableLength()
    {
        byte[] page = CreateLightingFrame(lightCount: 5);

        await Assert.That(() =>
            {
                using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                    page,
                    1,
                    SilkCommandParser.PageAbiVersion);
                _ = commands.MoveNext();
                _ = commands.Current.AsFrame();
            })
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task FrameCommandRejectsASizeBetweenTheDeclaredVariants()
    {
        // A size the parser does not know must fail loudly rather than be read
        // with whichever offsets happen to fit.
        byte[] page = CreateFrame(LightingSize - 8);

        await Assert.That(() =>
            {
                using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                    page,
                    1,
                    SilkCommandParser.PageAbiVersion);
                _ = commands.MoveNext();
                _ = commands.Current.AsFrame();
            })
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LightingLayoutConstantsMirrorTheParser()
    {
        // These tests hand-build the wire payload, so their offsets are a
        // second copy of the parser's layout. Read the parser's own constants
        // and require them to agree, otherwise this file silently goes stale
        // the moment the light entry grows and every round trip above starts
        // proving nothing.
        Dictionary<string, int> declared = ReadFrameCommandConstants();

        await Assert.That(declared["MinimumSize"]).IsEqualTo(MinimumSize);
        await Assert.That(declared["ExtendedSize"]).IsEqualTo(ExtendedSize);
        await Assert.That(declared["LightingSize"]).IsEqualTo(LightingSize);
        await Assert.That(declared["LightEntrySize"]).IsEqualTo(LightEntrySize);

        // If the light entry ever grows without the command size following,
        // the ambient term would overlap the last light.
        int ambient = declared["ExtendedSize"] + 16 +
            (MaximumLights * declared["LightEntrySize"]);
        await Assert.That(ambient + 16).IsEqualTo(declared["LightingSize"]);
    }

    private static Dictionary<string, int> ReadFrameCommandConstants()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("The repository root was not found.");
        }

        string text = File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "OpenUsd.Rendering.Silk",
            "SilkCommand.cs"));
        int typeIndex = text.IndexOf("struct SilkFrameCommand", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            throw new InvalidOperationException("SilkFrameCommand was not found.");
        }

        Dictionary<string, int> constants = [];
        foreach (Match match in Regex.Matches(
            text[typeIndex..],
            @"private\s+const\s+int\s+(?<name>\w+)\s*=\s*(?<value>\d+)\s*;",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)))
        {
            constants.TryAdd(
                match.Groups["name"].Value,
                int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture));
        }

        // A regex that matched nothing would make every comparison below fail
        // with a missing key rather than pass, but state the expectation.
        if (constants.Count < 4)
        {
            throw new InvalidOperationException(
                "SilkFrameCommand no longer declares its layout as int constants.");
        }
        return constants;
    }

    private static byte[] CreateFrame(int size)
    {
        byte[] bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 160);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 128);
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8), 8), i % 5 == 0 ? 1 : 0);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8), 8), i % 5 == 0 ? 1 : 0);
        }
        return bytes;
    }

    private static byte[] CreateLightingFrame(uint lightCount)
    {
        byte[] bytes = CreateFrame(LightingSize);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(LightCountOffset, 4),
            lightCount);

        for (int light = 0; light < MaximumLights; light++)
        {
            int entry = LightTableOffset + (light * LightEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry, 4), (uint)(light + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4, 4), (uint)(light % 2));
            for (int component = 0; component < 3; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(entry + 16 + (component * 4), 4),
                    light + (component * 0.5f));
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28, 4), 10f + light);
            for (int element = 0; element < 16; element++)
            {
                double value = element switch
                {
                    12 => 100d + light,
                    _ => element % 5 == 0 ? 1 : 0
                };
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8), 8),
                    value);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 160, 4), 20f + light);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 164, 4), 40f + light);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 168, 4), 50f + light);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 172, 4), 30f + light);
        }

        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset, 4), 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset + 4, 4), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset + 8, 4), 0.75f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset + 12, 4), 0.875f);
        return bytes;
    }
}
