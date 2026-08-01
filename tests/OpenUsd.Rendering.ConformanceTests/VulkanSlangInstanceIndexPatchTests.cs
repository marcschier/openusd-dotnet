// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class VulkanSlangInstanceIndexPatchTests
{
    private const uint Magic = 0x07230203;
    private const uint OpDecorate = (4u << 16) | 71u;
    private const uint OpLoad = (4u << 16) | 61u;
    private const uint OpISub = (5u << 16) | 130u;
    private const uint OpCopyObject = (4u << 16) | 83u;
    private const uint OpNop = 1u << 16;
    private const uint DecorationBuiltIn = 11;
    private const uint BuiltInInstanceIndex = 43;
    private const uint BuiltInBaseInstance = 4425;

    [Test]
    public async Task PatchesOnlySlangInstanceIndexMinusBaseInstanceLowering()
    {
        const uint instanceIndexVariable = 10;
        const uint baseInstanceVariable = 11;
        const uint instanceIndexLoad = 20;
        const uint baseInstanceLoad = 21;
        const uint subtractionResult = 22;
        byte[] module = ToBytes(
            Header(),
            DecorateBuiltIn(instanceIndexVariable, BuiltInInstanceIndex),
            DecorateBuiltIn(baseInstanceVariable, BuiltInBaseInstance),
            Load(instanceIndexVariable, instanceIndexLoad),
            Load(baseInstanceVariable, baseInstanceLoad),
            ISub(subtractionResult, instanceIndexLoad, baseInstanceLoad));

        byte[] patched = VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module);
        uint[] patchedWords = ToWords(patched);
        int subtractOffset = patchedWords.Length - 5;

        await Assert.That(patched.Length).IsEqualTo(module.Length);
        await Assert.That(patchedWords[subtractOffset]).IsEqualTo(OpCopyObject);
        await Assert.That(patchedWords[subtractOffset + 1]).IsEqualTo(1u);
        await Assert.That(patchedWords[subtractOffset + 2]).IsEqualTo(subtractionResult);
        await Assert.That(patchedWords[subtractOffset + 3]).IsEqualTo(instanceIndexLoad);
        await Assert.That(patchedWords[subtractOffset + 4]).IsEqualTo(OpNop);
    }

    [Test]
    public async Task LeavesUnrelatedLoadedIntegerSubtractionByteIdentical()
    {
        byte[] module = ToBytes(
            Header(),
            Load(30, 40),
            Load(31, 41),
            ISub(42, 40, 41));

        byte[] patched = VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module);

        await Assert.That(Convert.ToHexString(patched))
            .IsEqualTo(Convert.ToHexString(module));
    }

    [Test]
    public async Task RejectsBaseInstanceModuleWithoutExpectedLowering()
    {
        byte[] module = ToBytes(
            Header(),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            Load(10, 20));

        await Assert.That(() => VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task RejectsMultipleInstanceIndexMinusBaseInstanceLowerings()
    {
        byte[] module = ToBytes(
            Header(),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            ISub(23, 20, 21));

        await Assert.That(() => VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task RejectsMalformedOrTruncatedModule()
    {
        byte[] truncatedHeader = [0x03, 0x02, 0x23];
        byte[] truncatedInstruction = ToBytes(
            Header(),
            [(6u << 16) | 61u, 1u, 2u]);

        await Assert.That(() => VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(truncatedHeader))
            .Throws<InvalidDataException>();
        await Assert.That(() => VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(truncatedInstruction))
            .Throws<InvalidDataException>();
    }

    private static uint[] Header() => [Magic, 0x00010000, 0, 64, 0];

    private static uint[] DecorateBuiltIn(uint target, uint builtIn) =>
        [OpDecorate, target, DecorationBuiltIn, builtIn];

    private static uint[] Load(uint variable, uint result) =>
        [OpLoad, 1, result, variable];

    private static uint[] ISub(uint result, uint left, uint right) =>
        [OpISub, 1, result, left, right];

    private static byte[] ToBytes(params uint[][] chunks)
    {
        int wordCount = 0;
        foreach (uint[] chunk in chunks)
        {
            wordCount += chunk.Length;
        }
        byte[] bytes = new byte[wordCount * sizeof(uint)];
        int offset = 0;
        foreach (uint[] chunk in chunks)
        {
            foreach (uint word in chunk)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), word);
                offset += sizeof(uint);
            }
        }
        return bytes;
    }

    private static uint[] ToWords(byte[] bytes)
    {
        uint[] words = new uint[bytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(i * sizeof(uint), sizeof(uint)));
        }
        return words;
    }
}
