// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Reflection;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Round-trips the page ABI 24 lighting variant of the frame command.
/// </summary>
/// <remarks>
/// The frame command keeps the 272-byte viewport/matrix and 536-byte clip
/// prefixes, with 23096 bytes adding the direct-light table and ambient term
/// and 23368 bytes including domes. Before these tests the lighting variant had
/// no managed coverage at all -- every hand-written encoder in the repository
/// builds a 272 or 536 byte frame, and the lighting layout was exercised only
/// end to end through real hdSilk pages in the parity harness.
///
/// That is the same shape as the defect that shipped with the lighting work
/// itself: the GPU frame constants buffer grew from 208 bytes and two
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
    private const int LightingSize = 23096;
    private const int LightCountOffset = ExtendedSize;
    private const int LightTableOffset = ExtendedSize + 16;
    private const int LightEntrySize = 176;
    private const int MaximumLights = 128;
    private const int AmbientOffset = LightTableOffset + (MaximumLights * LightEntrySize);

    [Test]
    public async Task LightingFrameRoundTripsEveryLightFieldAtItsOwnIndex()
    {
        byte[] page = CreateLightingFrame(lightCount: 7);

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

        await Assert.That(lightCount).IsEqualTo(7u);
        for (int i = 0; i < MaximumLights; i++)
        {
            await Assert.That(types[i]).IsEqualTo((uint)((i % 5) + 1));
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

        // The ambient term sits immediately after 128 full light entries, so
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
        byte[] page = CreateLightingFrame(lightCount: 129);

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
        // Read compiled values so a named constant expression or an internal
        // visibility change cannot make the drift guard misparse the layout.
        // The independent byte-level cases below still verify real reads.
        Dictionary<string, int> constants = [];
        foreach (FieldInfo field in typeof(SilkFrameCommand).GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.IsLiteral && field.FieldType == typeof(int))
            {
                constants.Add(field.Name, (int)field.GetRawConstantValue()!);
            }
        }

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
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry, 4), (uint)((light % 5) + 1));
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

    [Test]
    [Arguments(272, true)]
    [Arguments(536, true)]
    [Arguments(23096, true)]
    [Arguments(23368, true)]
    [Arguments(271, false)]
    [Arguments(273, false)]
    [Arguments(535, false)]
    [Arguments(537, false)]
    [Arguments(1976, false)]
    [Arguments(2248, false)]
    [Arguments(23095, false)]
    [Arguments(23097, false)]
    [Arguments(23367, false)]
    [Arguments(23369, false)]
    public async Task FrameAcceptsOnlyDeclaredVariants(int size, bool accepted)
    {
        byte[] template = CreateWideLightingFrame(0);
        byte[] page = new byte[size];
        template.AsSpan(0, Math.Min(size, template.Length)).CopyTo(page);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(4), (uint)size);

        if (!accepted)
        {
            await Assert.That(() => ReadWideFrameState(page)).Throws<InvalidDataException>();
            SilkFrameState control = ReadWideFrameState(template);
            await Assert.That(control.Width).IsEqualTo(640);
            await Assert.That(control.AmbientLight.W).IsEqualTo(0.875f);
            return;
        }

        SilkFrameState state = ReadWideFrameState(page);
        await Assert.That(state.Width).IsEqualTo(640);
        await Assert.That(state.Height).IsEqualTo(360);
        await Assert.That(state.View.ToArray()[12]).IsEqualTo(712.25d);
        await Assert.That(state.Projection.ToArray()[14]).IsEqualTo(928.5d);
        await Assert.That(state.LightCount).IsEqualTo(0u);
        await Assert.That(state.DomeCount).IsEqualTo(0u);
        await Assert.That(state.ClipPlaneCount).IsEqualTo(size >= 536 ? 2u : 0u);
        await Assert.That(state.AmbientLight.W).IsEqualTo(size >= 23096 ? 0.875f : 0f);
        if (size < 23096)
        {
            SilkFrameLight[] lights = state.Lights.ToArray();
            await Assert.That(lights.Length).IsEqualTo(128);
            await Assert.That(lights[127].Transform)
                .IsEqualTo(System.Numerics.Matrix4x4.Identity);
            await Assert.That(lights[127].Intensity).IsEqualTo(0f);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(97)]
    [Arguments(128)]
    [Arguments(129)]
    [Arguments(130)]
    public async Task FrameLightCountPartitions(int count)
    {
        byte[] page = CreateWideLightingFrame((uint)count);
        if (count > 128)
        {
            await Assert.That(() => ReadWideFrameState(page)).Throws<InvalidDataException>();
            SilkFrameState control = ReadWideFrameState(CreateWideLightingFrame(128));
            await Assert.That(control.LightCount).IsEqualTo(128u);
            await Assert.That(control.Lights.ToArray()[127].ShapeX).IsEqualTo(129.25f);
            return;
        }

        SilkFrameState state = ReadWideFrameState(page);
        SilkFrameLight[] lights = state.Lights.ToArray();
        uint maximumLights = SilkFrameCommand.MaximumLights;
        uint maximumDomes = SilkFrameCommand.MaximumDomes;
        await Assert.That(maximumLights).IsEqualTo(128u);
        await Assert.That(maximumDomes).IsEqualTo(8u);
        await Assert.That(state.LightCount).IsEqualTo((uint)count);
        await Assert.That(lights.Length).IsEqualTo(128);
        await Assert.That(state.AmbientLight)
            .IsEqualTo(new System.Numerics.Vector4(0.25f, 0.5f, 0.75f, 0.875f));
        await Assert.That(state.ClipPlaneCount).IsEqualTo(2u);
        if (count > 0)
        {
            await Assert.That(lights[count - 1].Type).IsEqualTo((uint)(1 + ((count - 1) % 5)));
            await Assert.That(lights[count - 1].Intensity).IsEqualTo(count - 1 + 4.25f);
            await Assert.That(lights[count - 1].Radius).IsEqualTo(count - 1 + 0.75f);
        }
        if (count < 128)
        {
            await Assert.That(lights[count].Type).IsEqualTo(0u);
            await Assert.That(lights[count].Intensity).IsEqualTo(0f);
        }
    }

    [Test]
    public async Task FrameLightingHeaderAndEveryEntryFieldUseDeclaredOffsets()
    {
        byte[] page = CreateWideLightingFrame(128);
        uint count;
        uint clipCount;
        uint[] types = new uint[128];
        uint[] shadows = new uint[128];
        float[,] fields = new float[128, 10];
        double[,] transforms = new double[128, 16];
        double[] view = new double[16];
        double[] projection = new double[16];
        double[] clipPlanes = new double[32];
        float[] ambient = new float[4];
        bool finished;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1, 24u);
            if (!commands.MoveNext())
            {
                throw new InvalidDataException("The wide frame is missing.");
            }
            SilkFrameCommand frame = commands.Current.AsFrame();
            count = frame.LightCount;
            clipCount = frame.ClipPlaneCount;
            for (int element = 0; element < 16; element++)
            {
                view[element] = frame.GetViewElement(element);
                projection[element] = frame.GetProjectionElement(element);
            }
            for (int component = 0; component < 32; component++)
            {
                clipPlanes[component] = frame.GetClipPlaneElement(component / 4, component % 4);
            }
            for (int light = 0; light < 128; light++)
            {
                types[light] = frame.GetLightType(light);
                shadows[light] = frame.GetLightShadowEnabled(light);
                fields[light, 0] = frame.GetLightShapeX(light);
                fields[light, 1] = frame.GetLightShapeY(light);
                fields[light, 2] = frame.GetLightColor(light, 0);
                fields[light, 3] = frame.GetLightColor(light, 1);
                fields[light, 4] = frame.GetLightColor(light, 2);
                fields[light, 5] = frame.GetLightIntensity(light);
                fields[light, 6] = frame.GetLightExposure(light);
                fields[light, 7] = frame.GetLightDiffuse(light);
                fields[light, 8] = frame.GetLightSpecular(light);
                fields[light, 9] = frame.GetLightRadius(light);
                for (int element = 0; element < 16; element++)
                {
                    transforms[light, element] = frame.GetLightTransformElement(light, element);
                }
            }
            for (int component = 0; component < 3; component++)
            {
                ambient[component] = frame.GetAmbientColor(component);
            }
            ambient[3] = frame.AmbientIntensity;
            finished = !commands.MoveNext();
        }

        await Assert.That(page.Length).IsEqualTo(23096);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(536)))
            .IsEqualTo(128u);
        foreach (int offset in new[] { 540, 544, 548 })
        {
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(offset)))
                .IsEqualTo(0u);
        }
        await Assert.That(count).IsEqualTo(128u);
        await Assert.That(clipCount).IsEqualTo(2u);
        await Assert.That(finished).IsTrue();
        for (int element = 0; element < 16; element++)
        {
            await Assert.That(view[element]).IsEqualTo(700.25d + element);
            await Assert.That(projection[element]).IsEqualTo(900.5d + (element * 2));
        }
        for (int component = 0; component < 32; component++)
        {
            await Assert.That(clipPlanes[component])
                .IsEqualTo(component < 8 ? 10.125d + component : 0d);
        }
        for (int light = 0; light < 128; light++)
        {
            await Assert.That(types[light]).IsEqualTo((uint)(1 + (light % 5)));
            await Assert.That(shadows[light]).IsEqualTo((uint)(light % 2));
            float[] expected =
            [
                light + 2.25f, light + 3.5f,
                light + 0.125f, light + 0.25f, light + 0.5f,
                light + 4.25f, -1.5f + (light / 16f),
                0.25f + (light / 256f), 0.5f + (light / 512f), light + 0.75f,
            ];
            for (int field = 0; field < expected.Length; field++)
            {
                await Assert.That(fields[light, field]).IsEqualTo(expected[field])
                    .Because($"Light {light}, scalar {field} has its own wire field.");
            }
            for (int element = 0; element < 16; element++)
            {
                await Assert.That(transforms[light, element])
                    .IsEqualTo(100.125000001d + (light * 16) + element);
            }
        }
        float[] expectedAmbient = [0.25f, 0.5f, 0.75f, 0.875f];
        for (int component = 0; component < expectedAmbient.Length; component++)
        {
            await Assert.That(ambient[component]).IsEqualTo(expectedAmbient[component]);
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(
                page.AsSpan(23080 + (component * 4)))).IsEqualTo(expectedAmbient[component]);
        }
    }

    [Test]
    [Arguments(0, 544)]
    [Arguments(0, 548)]
    [Arguments(128, 544)]
    [Arguments(128, 548)]
    public async Task FrameLightingReservedHeaderWordsMustBeZero(int count, int offset)
    {
        byte[] page = CreateWideLightingFrame((uint)count);
        SilkFrameState control = ReadWideFrameState(page);
        await Assert.That(control.LightCount).IsEqualTo((uint)count);
        await Assert.That(control.AmbientLight.Z).IsEqualTo(0.75f);

        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(offset), 0x8000_0001u);
        await Assert.That(() => ReadWideFrameState(page)).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(128)]
    [Arguments(int.MinValue)]
    [Arguments(int.MaxValue)]
    public async Task WideFrameLightAccessorsGuardTheFixedTable(int index)
    {
        byte[] page = CreateWideLightingFrame(128);
        await Assert.That(ReadWideLightType(page, 127)).IsEqualTo(3u);
        await Assert.That(() => ReadWideLightType(page, index))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static uint ReadWideLightType(byte[] page, int index)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1, 24u);
        _ = commands.MoveNext();
        return commands.Current.AsFrame().GetLightType(index);
    }

    private static SilkFrameState ReadWideFrameState(byte[] page)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1, 24u);
        if (!commands.MoveNext())
        {
            throw new InvalidDataException("The wide frame is missing.");
        }
        var state = new SilkFrameState();
        state.Update(commands.Current.AsFrame());
        if (commands.MoveNext())
        {
            throw new InvalidDataException("The fixture contains more than one frame.");
        }
        return state;
    }

    private static byte[] CreateWideLightingFrame(uint lightCount)
    {
        byte[] bytes = new byte[23096];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 23096u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 640);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 360);
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (element * 8)), 700.25d + element);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (element * 8)), 900.5d + (element * 2));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(272), 2u);
        for (int component = 0; component < 8; component++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(280 + (component * 8)), 10.125d + component);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), lightCount);
        for (int light = 0; light < Math.Min(lightCount, 128u); light++)
        {
            int entry = 552 + (light * 176);
            // Cycle the legal distant/sphere/rect/disk/cylinder discriminators.
            // The slot is not a type: slot 127 still carries the legal rect value.
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), (uint)(1 + (light % 5)));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), (uint)(light % 2));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 8), light + 2.25f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 12), light + 3.5f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 16), light + 0.125f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 20), light + 0.25f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 24), light + 0.5f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28), light + 4.25f);
            for (int element = 0; element < 16; element++)
            {
                // Keep a sub-float fraction: a reader that narrows wire doubles
                // before returning them must not pass the matrix oracle.
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)),
                    100.125000001d + (light * 16) + element);
            }
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 160), -1.5f + (light / 16f));
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 164), 0.25f + (light / 256f));
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 168), 0.5f + (light / 512f));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 172), light + 0.75f);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(23080), 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(23084), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(23088), 0.75f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(23092), 0.875f);
        return bytes;
    }
}
