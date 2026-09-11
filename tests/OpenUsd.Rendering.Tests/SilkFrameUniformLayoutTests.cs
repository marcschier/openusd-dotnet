// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkFrameUniformLayoutTests
{
    // Numeric wire oracle, not a production accessor. The coordinator must
    // confirm that HAS_AUTHORED_DIRECT_LIGHTS means 0x1 rather than bit ordinal 1.
    private const uint CandidateHasAuthoredDirectLights = 0x1;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EveryFrameBlockWritesAtItsDeclaredOffset(bool flipClipSpaceY)
    {
        byte[] wire = FrameBytes(128, 8);
        // Untextured domes alternate with textured ones. Two textured domes fall
        // back to ambient; the other two retain independently addressable maps.
        for (int dome = 1; dome < 8; dome += 2)
        {
            wire.AsSpan(23112 + (dome * 32), 12).Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(
                wire.AsSpan(23112 + (dome * 32) + 16), 3u);
        }
        var frame = new SilkFrameState();
        frame.Update(new SilkFrameCommand(wire));
        SilkShadowDescriptor[] descriptors = ShadowDescriptors();
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;
        SilkShadowFrameBinding shadows = SilkShadowFrameBinding.Create(descriptors, layout);
        SilkDomeGroupTable groups = SilkDomeGroupTable.Empty
            .WithGroup(5, 2)
            .WithGroup(7, 1);
        var environment = new SilkEnvironmentFrameBinding(
            Enabled: true,
            SpecularSliceCount: 5,
            SpecularSliceHeight: 64,
            AuthoredSceneLighting: true,
            GroupCount: 3,
            ComposedGroup: 0,
            IrradianceSliceHeight: 32,
            DomeGroups: groups);
        SilkDomeAmbientTable fallback = default;
        fallback.AddAmbient(1, new Vector3(0.25f, 0.5f, 1f));
        fallback.AddAmbient(3, new Vector3(0.5f, 1f, 2f));
        fallback.AddUnattributed(new Vector3(0.5f, 0.25f, 0.125f));
        byte[] actual = Enumerable.Repeat((byte)0xA5, 15296).ToArray();

        SilkFrameUniformWriter.Write(
            frame, actual, flipClipSpaceY, RenderOutputTransform.Reinhard, 1.5f,
            environmentAmbient: new Vector3(64, 128, 256),
            shadows: shadows, environment: environment, domeAmbient: fallback);

        // Every byte is independently accounted for. In particular the first
        // two controls are uints, not floats, and the matrices below are literal
        // inverses/transposes of the simple cameras in FrameBytes.
        var expected = new byte[15296];
        PutFloats(expected, 0,
            0.5f, 0, 0, 0,
            0, flipClipSpaceY ? -0.25f : 0.25f, 0, 0,
            0, 0, 0.25f, -0.375f,
            0, 0, 0, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(64), 8u);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(68), 1u);
        PutFloats(expected, 72, 1.5f, 0);
        for (int component = 0; component < 32; component++)
        {
            PutFloats(expected, 80 + (component * 4), component - 15.5f);
        }
        PutFloats(expected, 208, 2.25f, 3.75f, 7.125f, 128);
        for (int light = 0; light < 128; light++)
        {
            PutExpectedLight(expected, light, present: true);
        }
        PutFloats(expected, 12512,
            0.5f, 0, 0, -3,
            0, 0.25f, 0, 2,
            0, 0, 0.125f, -2,
            0, 0, 0, 1);
        PutExpectedShadows(expected, bound: true);
        PutFloats(expected, 15008, 1, 5, 64, 1);
        PutFloats(expected, 15024, 8, 3, 0, 32);
        for (int dome = 0; dome < 8; dome++)
        {
            float t = dome + 1;
            Vector3 color = dome switch
            {
                1 => new Vector3(0.25f, 0.5f, 1),
                3 => new Vector3(0.5f, 1, 2),
                5 or 7 => Vector3.Zero,
                _ => new Vector3(t / 16, t / 8, t / 4),
            };
            PutFloats(expected, 15040 + (dome * 16), color.X, color.Y, color.Z, 1);
            PutFloats(expected, 15168 + (dome * 16),
                dome == 5 ? 2 : dome == 7 ? 1 : -1, 0, 0, 0);
        }

        await AssertBytes(actual, expected, "the complete ABI24 GPU frame");
        await Assert.That(frame.Lights.Length).IsEqualTo(128);
        await Assert.That(frame.LightCount).IsEqualTo(128u);
        await Assert.That(frame.Revision).IsEqualTo(1UL);
        await Assert.That(layout.Edge).IsEqualTo(4096u);
        await Assert.That(shadows.GetSlotForLight(96)).IsEqualTo(2);
        await Assert.That(shadows.GetSlotForLight(127)).IsEqualTo(3);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(1, 1)]
    [Arguments(97, 8)]
    [Arguments(128, 8)]
    public async Task UnusedLightAndDomeSlotsUseDeterministicDefaults(
        int lightCount, int domeCount)
    {
        var frame = new SilkFrameState();
        frame.Update(new SilkFrameCommand(FrameBytes(128, 8)));
        byte[] actual = Enumerable.Repeat((byte)0xCD, 15296).ToArray();
        SilkFrameUniformWriter.Write(
            frame, actual, false, RenderOutputTransform.Identity, 0);
        ulong before = frame.Revision;

        // Reuse both state and destination: a shrink must erase actual earlier
        // light/dome data, not just happen to work on a freshly zeroed buffer.
        frame.Update(new SilkFrameCommand(FrameBytes(lightCount, domeCount)));
        SilkFrameUniformWriter.Write(
            frame, actual, false, RenderOutputTransform.Identity, 0);

        var expectedLights = new byte[15296];
        for (int light = 0; light < 128; light++)
        {
            PutExpectedLight(expectedLights, light, light < lightCount);
        }
        await AssertBytes(
            actual[224..12512], expectedLights[224..12512],
            "all six arrays, including every retired slot and its basis defaults");
        await Assert.That(ReadVector(actual, 208).W).IsEqualTo((float)lightCount);
        await Assert.That(ReadVector(actual, 15024)).IsEqualTo(new Vector4(domeCount, 0, 0, 0));
        for (int dome = 0; dome < 8; dome++)
        {
            float t = dome + 1;
            Vector4 expected = dome < domeCount
                ? new Vector4(t / 16, t / 8, t / 4, 1)
                : Vector4.Zero;
            await Assert.That(ReadVector(actual, 15040 + (dome * 16))).IsEqualTo(expected);
            await Assert.That(ReadVector(actual, 15168 + (dome * 16)))
                .IsEqualTo(new Vector4(-1, 0, 0, 0));
            await Assert.That(frame.Domes[dome].IsPresent).IsEqualTo(dome < domeCount);
        }
        await Assert.That(frame.Revision)
            .IsEqualTo(before + (lightCount == 128 && domeCount == 8 ? 0UL : 1UL));
        await Assert.That(frame.LightCount).IsEqualTo((uint)lightCount);
        await Assert.That(frame.DomeCount).IsEqualTo((uint)domeCount);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(76)))
            .IsEqualTo(0u);
    }

    [Test]
    public async Task HighLightShadowSlotsAndUnboundTailAreWritten()
    {
        var frame = new SilkFrameState();
        frame.Update(new SilkFrameCommand(FrameBytes(128, 0)));
        SilkShadowDescriptor[] descriptors = ShadowDescriptors();
        SilkShadowAtlasLayout layout = SilkShadowAtlasLayout.Create(descriptors)!;
        SilkShadowFrameBinding binding = SilkShadowFrameBinding.Create(descriptors, layout);
        byte[] actual = Enumerable.Repeat((byte)0xA7, 15296).ToArray();
        SilkFrameUniformWriter.Write(
            frame, actual, false, RenderOutputTransform.Identity, 0, shadows: binding);

        var expected = new byte[15296];
        PutExpectedShadows(expected, bound: true);
        await AssertBytes(actual[12576..15008], expected[12576..15008], "all bound shadow blocks");
        await Assert.That(binding.Count).IsEqualTo(4);
        await Assert.That(binding.GetSlotForLight(0)).IsEqualTo(0);
        await Assert.That(binding.GetSlotForLight(31)).IsEqualTo(1);
        await Assert.That(binding.GetSlotForLight(64)).IsEqualTo(-1);
        await Assert.That(binding.GetSlotForLight(96)).IsEqualTo(2);
        await Assert.That(binding.GetSlotForLight(127)).IsEqualTo(3);
        await Assert.That(() => binding.GetSlotForLight(-1)).Throws<IndexOutOfRangeException>();
        await Assert.That(() => binding.GetSlotForLight(128)).Throws<IndexOutOfRangeException>();
        await Assert.That(layout.Tiles[2].PixelX).IsEqualTo(0u);
        await Assert.That(layout.Tiles[2].PixelY).IsEqualTo(2048u);
        await Assert.That(layout.Tiles[3].Resolution).IsEqualTo(2048u);

        SilkFrameUniformWriter.Write(
            frame, actual, false, RenderOutputTransform.Identity, 0,
            shadows: SilkShadowFrameBinding.None);
        PutExpectedShadows(expected, bound: false);
        await AssertBytes(
            actual[12576..15008], expected[12576..15008],
            "retirement clears four matrices/tiles/controls and all 128 slot vectors");
        await Assert.That(SilkShadowFrameBinding.None.Count).IsEqualTo(0);
        for (int light = 0; light < 128; light++)
        {
            await Assert.That(SilkShadowFrameBinding.None.GetSlotForLight(light)).IsEqualTo(-1);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(15295)]
    [Arguments(15296)]
    [Arguments(15297)]
    public async Task FrameWriterRejectsNonExactDestinationLength(int length)
    {
        var frame = new SilkFrameState();
        byte[] destination = Enumerable.Repeat((byte)0xB6, length).ToArray();
        if (length == 15296)
        {
            SilkFrameUniformWriter.Write(
                frame, destination, false, RenderOutputTransform.Identity, 0);
            await Assert.That(ReadVector(destination, 12960 + (127 * 16)))
                .IsEqualTo(new Vector4(-1, 0, 0, 0));
            await Assert.That(ReadVector(destination, 15168 + (7 * 16)))
                .IsEqualTo(new Vector4(-1, 0, 0, 0));
        }
        else
        {
            await Assert.That(() => SilkFrameUniformWriter.Write(
                    frame, destination, false, RenderOutputTransform.Identity, 0))
                .Throws<ArgumentException>();
            await Assert.That(destination.All(value => value == 0xB6))
                .IsTrue().Because("length validation must precede the first write");
        }
        int frameSize = SilkFrameUniformWriter.ByteSize;
        await Assert.That(frameSize).IsEqualTo(15296);
        await Assert.That(frame.Revision).IsEqualTo(0UL);
    }

    [Test]
    [Arguments(23096, 272)]
    [Arguments(23368, 536)]
    public async Task AuthoredDirectFlagsControlHeadlightFallbackBytes(
        int lightingLength, int legacyLength)
    {
        var scene = new SilkSceneState();
        byte[] wire = FrameBytes(0, 0)[..lightingLength];
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), (uint)lightingLength);
        wire.AsSpan(23080, 16).Clear();
        _ = scene.Apply(wire, 1, 10);
        var bytes = new byte[15296];
        SilkFrameUniformWriter.Write(
            scene.Frame, bytes, false, RenderOutputTransform.Identity, 0);
        await Assert.That(ReadVector(bytes, 208)).IsEqualTo(Vector4.Zero);
        await Assert.That(ReadVector(bytes, 15008)).IsEqualTo(Vector4.Zero);
        ulong unlitRevision = scene.Frame.Revision;

        // Only the flags word changes. Count, ambient, camera and the entire
        // light table are identical, so they cannot explain the new control.
        BinaryPrimitives.WriteUInt32LittleEndian(
            wire.AsSpan(540), CandidateHasAuthoredDirectLights);
        _ = scene.Apply(wire, 1, 11);
        SilkFrameUniformWriter.Write(
            scene.Frame, bytes, false, RenderOutputTransform.Identity, 0);
        await Assert.That(scene.Frame.Revision).IsEqualTo(unlitRevision + 1);
        await Assert.That(ReadVector(bytes, 208)).IsEqualTo(Vector4.Zero);
        await Assert.That(ReadVector(bytes, 15008)).IsEqualTo(new Vector4(0, 0, 0, 1));
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(15020)))
            .IsEqualTo(1f);

        _ = scene.Apply(wire, 1, 12);
        await Assert.That(scene.Frame.Revision).IsEqualTo(unlitRevision + 1);
        await Assert.That(scene.Revision).IsEqualTo(12UL);

        byte[] meaningful = FrameBytes(1, 0, CandidateHasAuthoredDirectLights)[..lightingLength];
        BinaryPrimitives.WriteUInt32LittleEndian(meaningful.AsSpan(4), (uint)lightingLength);
        meaningful.AsSpan(23080, 16).Clear();
        _ = scene.Apply(meaningful, 1, 13);
        SilkFrameUniformWriter.Write(
            scene.Frame, bytes, false, RenderOutputTransform.Identity, 0);
        await Assert.That(ReadVector(bytes, 208)).IsEqualTo(new Vector4(0, 0, 0, 1));
        await Assert.That(ReadVector(bytes, 4320)).IsEqualTo(new Vector4(0.125f, 0.25f, 0.5f, 0.125f));
        await Assert.That(ReadVector(bytes, 15008).W).IsEqualTo(1f);

        byte[] legacy = meaningful[..legacyLength];
        BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(4), (uint)legacyLength);
        _ = scene.Apply(legacy, 1, 14);
        SilkFrameUniformWriter.Write(
            scene.Frame, bytes, false, RenderOutputTransform.Identity, 0);
        await Assert.That(ReadVector(bytes, 208)).IsEqualTo(Vector4.Zero);
        await Assert.That(ReadVector(bytes, 15008)).IsEqualTo(Vector4.Zero);
        await Assert.That(ReadVector(bytes, 4320)).IsEqualTo(Vector4.Zero);
        await Assert.That(scene.Frame.Revision).IsEqualTo(unlitRevision + 3);
        await Assert.That(scene.Frame.Width).IsEqualTo(320);
    }

    private static byte[] FrameBytes(int lightCount, int domeCount, uint flags = 0)
    {
        var bytes = new byte[23368];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 23368u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 320);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 180);
        PutDoubles(bytes, 16,
            2, 0, 0, 0, 0, 4, 0, 0, 0, 0, 8, 0, 6, -8, 16, 1);
        PutDoubles(bytes, 144,
            2, 0, 0, 0, 0, 4, 0, 0, 0, 0, 8, 0, 0, 0, 2, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(272), 8u);
        for (int component = 0; component < 32; component++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(280 + (component * 8)), component - 15.5);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), (uint)lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(540), flags);
        for (int light = 0; light < lightCount; light++)
        {
            int entry = 552 + (light * 176);
            float t = light + 1;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), (uint)((light % 5) + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), (uint)(light % 2));
            PutFloats(bytes, entry + 8, t + 0.25f, t + 0.5f);
            PutFloats(bytes, entry + 16, t / 8, t / 4, t / 2, t / 4);
            PutDoubles(bytes, entry + 32,
                0, 2, 0, 0, 0, 0, -3, 0, -4, 0, 0, 0, t, -2 * t, 3 * t, 1);
            PutFloats(bytes, entry + 160,
                (light % 3) - 1, 0.25f + (light / 1024f),
                0.75f - (light / 512f), t / 16);
        }
        PutFloats(bytes, 23080, 0.125f, 0.25f, 0.5f, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23096), (uint)domeCount);
        for (int dome = 0; dome < domeCount; dome++)
        {
            float t = dome + 1;
            PutFloats(bytes, 23112 + (dome * 32), t / 16, t / 8, t / 4);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(23112 + (dome * 32) + 16), 1u);
        }
        return bytes;
    }

    private static SilkShadowDescriptor[] ShadowDescriptors()
    {
        uint[] indices = [0, 31, 96, 127];
        uint[] resolutions = [256, 512, 1024, 2048];
        return Enumerable.Range(0, 4).Select(slot => new SilkShadowDescriptor(
            indices[slot], (uint)slot, resolutions[slot],
            SilkShadowDescriptorOptions.Orthographic,
            (slot + 1) / 1024f, (slot + 1) / 128f, slot)
        {
            View = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0,
                slot + 1, 2 * (slot + 1), 3 * (slot + 1), 1],
            Projection = [2, 0, 0, 0, 0, 4, 0, 0, 0, 0, 8, 0, 0, 0, 0, 1],
        }).ToArray();
    }

    private static void PutExpectedLight(byte[] bytes, int light, bool present)
    {
        int offset = light * 16;
        if (!present)
        {
            PutFloats(bytes, 224 + offset, 0, 0, 0, 0);
            PutFloats(bytes, 2272 + offset, 0, 0, 1, 0);
            PutFloats(bytes, 4320 + offset, 0, 0, 0, 0);
            PutFloats(bytes, 6368 + offset, 0, 0, 0, 0);
            PutFloats(bytes, 8416 + offset, 1, 0, 0, 0);
            PutFloats(bytes, 10464 + offset, 0, 1, 0, 0);
            return;
        }
        float t = light + 1;
        float multiplier = (light % 3) switch { 0 => 0.5f, 1 => 1f, _ => 2f };
        PutFloats(bytes, 224 + offset, t, -2 * t, 3 * t, (light % 5) + 1);
        PutFloats(bytes, 2272 + offset, -1, 0, 0, t / 16);
        PutFloats(bytes, 4320 + offset, t / 8, t / 4, t / 2, t * multiplier / 4);
        PutFloats(bytes, 6368 + offset, 0.25f + (light / 1024f), 0.75f - (light / 512f), light % 2, 0);
        PutFloats(bytes, 8416 + offset, 0, 1, 0, t + 0.25f);
        PutFloats(bytes, 10464 + offset, 0, 0, -1, t + 0.5f);
    }

    private static void PutExpectedShadows(byte[] bytes, bool bound)
    {
        bytes.AsSpan(12576, 384).Clear();
        if (bound)
        {
            float[] scales = [0.0625f, 0.125f, 0.25f, 0.5f];
            for (int slot = 0; slot < 4; slot++)
            {
                float t = slot + 1;
                PutFloats(bytes, 12576 + (slot * 64),
                    2, 0, 0, 2 * t,
                    0, 4, 0, 8 * t,
                    0, 0, 4, (12 * t) + 0.5f,
                    0, 0, 0, 1);
                PutFloats(bytes, 12832 + (slot * 16),
                    (slot % 2) / 2f, (slot / 2) / 2f, scales[slot], scales[slot]);
                PutFloats(bytes, 12896 + (slot * 16), t / 1024, t / 128, slot, 1f / 4096);
            }
        }
        for (int light = 0; light < 128; light++)
        {
            float slot = bound ? light switch { 0 => 0, 31 => 1, 96 => 2, 127 => 3, _ => -1 } : -1;
            PutFloats(bytes, 12960 + (light * 16), slot, 0, 0, 0);
        }
    }

    private static void PutFloats(byte[] bytes, int offset, params float[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + (index * 4)), values[index]);
        }
    }

    private static void PutDoubles(byte[] bytes, int offset, params double[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(offset + (index * 8)), values[index]);
        }
    }

    private static Vector4 ReadVector(byte[] bytes, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset + 4)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset + 8)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset + 12)));

    private static async Task AssertBytes(byte[] actual, byte[] expected, string because)
    {
        // Matrix4x4.Invert may produce either sign of IEEE zero on different
        // SIMD paths. Compare that numeric zero at computed camera coordinates;
        // every reserved word, array value and other byte remains exact.
        if (actual.Length == 15296 && expected.Length == 15296)
        {
            actual = (byte[])actual.Clone();
            foreach (int matrix in new[] { 0, 12512 })
            {
                for (int element = 0; element < 16; element++)
                {
                    int offset = matrix + (element * 4);
                    if (BinaryPrimitives.ReadSingleLittleEndian(actual.AsSpan(offset)) == 0 &&
                        BinaryPrimitives.ReadSingleLittleEndian(expected.AsSpan(offset)) == 0)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(offset), 0u);
                    }
                }
            }
        }
        int mismatch = -1;
        for (int index = 0; index < Math.Min(actual.Length, expected.Length); index++)
        {
            if (actual[index] != expected[index])
            {
                mismatch = index;
                break;
            }
        }
        await Assert.That(actual.AsSpan().SequenceEqual(expected))
            .IsTrue().Because($"{because}; first differing byte: {mismatch}");
    }
}
