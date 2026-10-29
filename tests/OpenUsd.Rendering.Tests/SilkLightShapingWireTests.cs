// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the page ABI 28 UsdLux ShapingAPI table that follows the dome table.
/// </summary>
/// <remarks>
/// hdSilk publishes one 16-byte entry per direct-light slot: the cone angle in
/// degrees, the focus exponent and two reserved words. The GPU frame turns that
/// into (cos cone, focus, shaped, 0) for rect and disk lights narrower than 180
/// degrees, which is where Storm's area-light approximation applies a cone.
/// </remarks>
public sealed class SilkLightShapingWireTests
{
    private const int DomeSize = 23368;
    private const int ShapingSize = 25416;
    private const int ShapingOffset = DomeSize;
    private const int LightTableOffset = 552;
    private const int LightEntrySize = 176;
    private const int ShapingUniformOffset = 15296;

    [Test]
    public async Task ShapingTableRoundTripsEveryPublishedSlot()
    {
        byte[] page = CreateFrame(lightCount: 5);
        for (int light = 0; light < 5; light++)
        {
            PutShaping(page, light, 10f * (light + 1), light / 4f);
        }

        SilkFrameState frame = ReadFrame(page);
        SilkFrameLight[] lights = frame.Lights.ToArray();
        for (int light = 0; light < 5; light++)
        {
            await Assert.That(lights[light].ShapingConeAngle).IsEqualTo(10f * (light + 1));
            await Assert.That(lights[light].ShapingFocus).IsEqualTo(light / 4f);
        }
        await Assert.That(lights[5].ShapingConeAngle).IsEqualTo(0f);
        await Assert.That(lights[5].ShapingFocus).IsEqualTo(0f);
    }

    [Test]
    public async Task FrameWithoutShapingTableReadsUnshapedDefaults()
    {
        byte[] page = CreateFrame(lightCount: 2)[..DomeSize];
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(4), DomeSize);

        SilkFrameLight[] lights = ReadFrame(page).Lights.ToArray();

        await Assert.That(lights[0].ShapingConeAngle).IsEqualTo(180f);
        await Assert.That(lights[1].ShapingFocus).IsEqualTo(0f);
    }

    [Test]
    [Arguments(-0.5f, 0f)]
    [Arguments(180.5f, 0f)]
    [Arguments(float.NaN, 0f)]
    [Arguments(float.PositiveInfinity, 0f)]
    [Arguments(90f, -1f)]
    [Arguments(90f, float.NaN)]
    [Arguments(90f, float.PositiveInfinity)]
    public async Task PublishedSlotRejectsOutOfRangeConeOrFocus(float cone, float focus)
    {
        byte[] page = CreateFrame(lightCount: 1);
        PutShaping(page, 0, cone, focus);

        await Assert.That(() => ReadFrame(page)).Throws<InvalidDataException>();

        PutShaping(page, 0, 90f, 2f);
        await Assert.That(ReadFrame(page).Lights.ToArray()[0].ShapingFocus).IsEqualTo(2f);
    }

    [Test]
    [Arguments(0, 8)]
    [Arguments(0, 12)]
    [Arguments(1, 0)]
    [Arguments(1, 4)]
    [Arguments(127, 12)]
    public async Task ReservedWordsAndUnpublishedSlotsMustBeZero(int light, int field)
    {
        byte[] page = CreateFrame(lightCount: 1);
        PutShaping(page, 0, 45f, 1f);
        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(ShapingOffset + (light * 16) + field), 1u);

        await Assert.That(() => ReadFrame(page)).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(25415)]
    [Arguments(25417)]
    public async Task ShapingTierAcceptsOnlyItsExactSize(int size)
    {
        byte[] template = CreateFrame(lightCount: 0);
        byte[] page = new byte[size];
        template.AsSpan(0, Math.Min(size, template.Length)).CopyTo(page);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(4), (uint)size);

        await Assert.That(() => ReadFrame(page)).Throws<InvalidDataException>();
        await Assert.That(ReadFrame(template).Width).IsEqualTo(64);
    }

    [Test]
    public async Task UniformWritesShapedConeOnlyForNarrowRectAndDiskLights()
    {
        // Slot order: rect 45/2, disk 90/0, rect 180, sphere 30/4, distant 10/1.
        byte[] page = CreateFrame(lightCount: 5, types: [3u, 4u, 3u, 2u, 1u]);
        PutShaping(page, 0, 45f, 2f);
        PutShaping(page, 1, 90f, 0f);
        PutShaping(page, 2, 180f, 3f);
        PutShaping(page, 3, 30f, 4f);
        PutShaping(page, 4, 10f, 1f);
        SilkFrameState frame = ReadFrame(page);
        byte[] constants = Enumerable.Repeat((byte)0xE1, SilkFrameUniformWriter.ByteSize).ToArray();

        SilkFrameUniformWriter.Write(frame, constants, false, RenderOutputTransform.Identity, 0);

        await Assert.That(constants.Length).IsEqualTo(17344);
        await Assert.That(ReadVector(constants, 0))
            .IsEqualTo(new Vector4(MathF.Cos(45 * (MathF.PI / 180)), 2, 1, 0));
        await Assert.That(ReadVector(constants, 1))
            .IsEqualTo(new Vector4(MathF.Cos(90 * (MathF.PI / 180)), 0, 1, 0));
        for (int light = 2; light < 128; light++)
        {
            await Assert.That(ReadVector(constants, light)).IsEqualTo(Vector4.Zero);
        }
    }

    private static byte[] CreateFrame(int lightCount, uint[]? types = null)
    {
        var bytes = new byte[ShapingSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), ShapingSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 32);
        for (int element = 0; element < 16; element++)
        {
            double identity = element % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), identity);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), identity);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), (uint)lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(540), lightCount > 0 ? 1u : 0u);
        for (int light = 0; light < 128; light++)
        {
            int entry = LightTableOffset + (light * LightEntrySize);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)), element % 5 == 0 ? 1 : 0);
            }
            if (light >= lightCount)
            {
                continue;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(entry), types?[light] ?? 3u);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 8), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 12), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 16), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28), 1);
            PutShaping(bytes, light, 180, 0);
        }
        return bytes;
    }

    private static void PutShaping(byte[] bytes, int light, float cone, float focus)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(ShapingOffset + (light * 16)), cone);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(ShapingOffset + (light * 16) + 4), focus);
    }

    private static SilkFrameState ReadFrame(byte[] page)
    {
        var frame = new SilkFrameState();
        frame.Update(new SilkFrameCommand(page));
        return frame;
    }

    private static Vector4 ReadVector(byte[] constants, int light)
    {
        int offset = ShapingUniformOffset + (light * 16);
        return new Vector4(
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(offset)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(offset + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(offset + 8)),
            BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(offset + 12)));
    }
}
