// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;
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
    private const uint OpCapability = (2u << 16) | 17u;
    private const uint OpVariable = (4u << 16) | 59u;
    private const uint DecorationBuiltIn = 11;
    private const uint BuiltInVertexIndex = 42;
    private const uint BuiltInInstanceIndex = 43;
    private const uint BuiltInBaseVertex = 4424;
    private const uint BuiltInBaseInstance = 4425;
    private const uint BuiltInDrawIndex = 4426;
    private const uint CapabilityDrawParameters = 4427;

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
    public async Task PatchesVertexAndInstanceLoweringsAndRemovesBothUnusedBases()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11, 12, 13, 14),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            DecorateBuiltIn(12, BuiltInVertexIndex),
            DecorateBuiltIn(13, BuiltInBaseVertex),
            [(3u << 16) | 71u, 40, 13],
            [(5u << 16) | 72u, 40, 0, 7, 11],
            [(4u << 16) | 43u, 1, 27, 11],
            [OpVariable, 1, 11, 1],
            [OpVariable, 1, 13, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            Load(12, 23),
            Load(13, 24),
            ISub(25, 23, 24),
            Load(30, 31),
            Load(32, 33),
            ISub(34, 31, 33));

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsFalse();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpDecorate &&
            instruction[2] == DecorationBuiltIn &&
            instruction[3] is BuiltInBaseVertex or BuiltInBaseInstance)).IsFalse();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpCopyObject && instruction[2] == 22 && instruction[3] == 20)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpCopyObject && instruction[2] == 25 && instruction[3] == 23)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpISub && instruction[2] == 34 &&
            instruction[3] == 31 && instruction[4] == 33)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 && (instruction[0] & 0xffff) == 43 &&
            instruction[2] == 27 && instruction[3] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 3 && (instruction[0] & 0xffff) == 71 &&
            instruction[1] == 40 && instruction[2] == 13)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 5 && (instruction[0] & 0xffff) == 72 &&
            instruction[1] == 40 && instruction[3] == 7 && instruction[4] == 11)).IsTrue();
        uint[] entryPoint = patched.Single(static instruction => (instruction[0] & 0xffff) == 15);
        await Assert.That(entryPoint.Skip(5).SequenceEqual([10u, 12u, 14u])).IsTrue();
    }

    [Test]
    public async Task RemovesUnusedBaseVertexFromVertexOnlyLowering()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11),
            DecorateBuiltIn(10, BuiltInVertexIndex),
            DecorateBuiltIn(11, BuiltInBaseVertex),
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21));

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsFalse();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 && instruction[0] == OpVariable && instruction[2] == 11)).IsFalse();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpCopyObject && instruction[2] == 22 && instruction[3] == 20)).IsTrue();
    }

    [Test]
    public async Task KeepsBothBasesWhenVertexBaseHasAnotherUse()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11, 12, 13),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            DecorateBuiltIn(12, BuiltInVertexIndex),
            DecorateBuiltIn(13, BuiltInBaseVertex),
            [OpVariable, 1, 11, 1],
            [OpVariable, 1, 13, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            Load(12, 23),
            Load(13, 24),
            ISub(25, 23, 24),
            [OpCopyObject, 1, 26, 24]);

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpVariable && instruction[2] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpVariable && instruction[2] == 13)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpCopyObject && instruction[2] == 26 && instruction[3] == 24)).IsTrue();
    }

    [Test]
    public async Task PreservesOtherDrawBuiltinsWhenBothIndexPatternsMatch()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11, 12, 13, 14),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            DecorateBuiltIn(12, BuiltInVertexIndex),
            DecorateBuiltIn(13, BuiltInBaseVertex),
            DecorateBuiltIn(14, BuiltInDrawIndex),
            [OpVariable, 1, 11, 1],
            [OpVariable, 1, 13, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            Load(12, 23),
            Load(13, 24),
            ISub(25, 23, 24));

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpDecorate &&
            instruction[1] == 14 &&
            instruction[3] == BuiltInDrawIndex)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpVariable && instruction[2] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction[0] == OpVariable && instruction[2] == 13)).IsTrue();
    }

    [Test]
    public async Task LeavesUnmatchedVertexBaseUsageByteIdentical()
    {
        byte[] reversed = ToBytes(
            Header(),
            DecorateBuiltIn(10, BuiltInVertexIndex),
            DecorateBuiltIn(11, BuiltInBaseVertex),
            Load(10, 20),
            Load(11, 21),
            ISub(22, 21, 20));
        byte[] multiple = ToBytes(
            Header(),
            DecorateBuiltIn(10, BuiltInVertexIndex),
            DecorateBuiltIn(11, BuiltInBaseVertex),
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            ISub(23, 20, 21));

        await Assert.That(VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(reversed)
            .AsSpan().SequenceEqual(reversed)).IsTrue();
        await Assert.That(VulkanSilkGraphicsDevice.PatchSlangDrawIndexLowering(multiple)
            .AsSpan().SequenceEqual(multiple)).IsTrue();
    }

    [Test]
    [Arguments("mesh")]
    [Arguments("mesh-uv")]
    [Arguments("mesh-uv-normal")]
    [Arguments("normal")]
    [Arguments("shadow")]
    [Arguments("shadow-mask")]
    public async Task RemovesUnusedDrawParametersFromCheckedInstanceShaders(string shader)
    {
        SilkShaderModuleDescriptor descriptor = shader switch
        {
            "mesh" => SilkCheckedShaderAssets.LoadMeshVertex(SilkShaderBinaryFormat.SpirV),
            "mesh-uv" => SilkCheckedShaderAssets.LoadMeshVertex(
                SilkShaderBinaryFormat.SpirV,
                new SilkShaderPermutationId(SilkShaderFeatures.Uv)),
            "mesh-uv-normal" => SilkCheckedShaderAssets.LoadMeshVertex(
                SilkShaderBinaryFormat.SpirV,
                new SilkShaderPermutationId(SilkShaderFeatures.Uv | SilkShaderFeatures.NormalMap)),
            "normal" => SilkCheckedShaderAssets.LoadNormalVertex(SilkShaderBinaryFormat.SpirV),
            "shadow" => SilkCheckedShaderAssets.LoadShadowVertex(SilkShaderBinaryFormat.SpirV),
            "shadow-mask" => SilkCheckedShaderAssets.LoadMaskedShadowVertex(SilkShaderBinaryFormat.SpirV),
            _ => throw new ArgumentOutOfRangeException(nameof(shader))
        };
        uint[][] original = Instructions(descriptor.Code.ToArray());
        uint baseInstance = original.Single(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpDecorate &&
            instruction[2] == DecorationBuiltIn &&
            instruction[3] == BuiltInBaseInstance)[1];
        uint[][] patched = Instructions(
            VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(descriptor.Code.Span));

        await Assert.That(original.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsFalse();
        await Assert.That(patched.Any(instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpDecorate &&
            instruction[1] == baseInstance)).IsFalse();
        await Assert.That(patched.Any(instruction =>
            instruction.Length >= 4 &&
            (instruction[0] & 0xffff) == 59 &&
            instruction[2] == baseInstance)).IsFalse();
        await Assert.That(patched.Any(instruction =>
            instruction.Length >= 4 &&
            (instruction[0] & 0xffff) == 61 &&
            instruction[3] == baseInstance)).IsFalse();
        await Assert.That(patched.Any(instruction =>
            (instruction[0] & 0xffff) == 15 &&
            instruction.Skip(5).Contains(baseInstance))).IsFalse();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpDecorate &&
            instruction[2] == DecorationBuiltIn &&
            instruction[3] == BuiltInInstanceIndex)).IsTrue();
        await Assert.That(patched.Any(static instruction => instruction[0] == OpCopyObject)).IsTrue();
        int firstFunction = Array.FindIndex(patched, static instruction => (instruction[0] & 0xffff) == 54);
        await Assert.That(firstFunction).IsGreaterThan(0);
        await Assert.That(patched.Take(firstFunction).Any(static instruction => instruction[0] == OpNop)).IsFalse();
    }

    [Test]
    public async Task KeepsDrawParametersWhenAnotherDrawBuiltinIsPresent()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11, 12),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            DecorateBuiltIn(12, BuiltInDrawIndex),
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21));

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpDecorate &&
            instruction[1] == 12 &&
            instruction[3] == BuiltInDrawIndex)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpCopyObject &&
            instruction[2] == 22 &&
            instruction[3] == 20)).IsTrue();
    }

    [Test]
    public async Task RemovesTheUnusedInterfaceFromEveryEntryPointWithoutReordering()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11, 12),
            EntryPoint(11, 10),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21));

        uint[][] entryPoints = Instructions(VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module))
            .Where(static instruction => (instruction[0] & 0xffff) == 15)
            .ToArray();

        await Assert.That(entryPoints.Length).IsEqualTo(2);
        await Assert.That(entryPoints[0].Skip(5).SequenceEqual([10u, 12u])).IsTrue();
        await Assert.That(entryPoints[1].Skip(5).SequenceEqual([10u])).IsTrue();
        await Assert.That(entryPoints[0][0] >> 16).IsEqualTo(7u);
        await Assert.That(entryPoints[1][0] >> 16).IsEqualTo(6u);
    }

    [Test]
    public async Task KeepsDrawParametersWhenAStructMemberUsesAnotherDrawBuiltin()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            [(5u << 16) | 72u, 40, 0, DecorationBuiltIn, BuiltInDrawIndex],
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21));

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 5 &&
            (instruction[0] & 0xffff) == 72 &&
            instruction[4] == BuiltInDrawIndex)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpVariable &&
            instruction[2] == 11)).IsTrue();
    }

    [Test]
    public async Task KeepsBaseInstanceWhenItsLoadedValueHasAnotherUse()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            [OpCopyObject, 1, 23, 21]);

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpLoad &&
            instruction[2] == 21 &&
            instruction[3] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpVariable &&
            instruction[2] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            (instruction[0] & 0xffff) == 15 &&
            instruction.Skip(5).Contains(11u))).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpCopyObject &&
            instruction[2] == 23 &&
            instruction[3] == 21)).IsTrue();
    }

    [Test]
    public async Task KeepsBaseInstanceWhenItsVariableHasAnotherUse()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21),
            [OpCopyObject, 1, 23, 11]);

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpVariable &&
            instruction[2] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpCopyObject &&
            instruction[2] == 23 &&
            instruction[3] == 11)).IsTrue();
    }

    [Test]
    public async Task KeepsDrawParametersWhenGroupedDecorationsPreventUseAnalysis()
    {
        byte[] module = ToBytes(
            Header(),
            [OpCapability, CapabilityDrawParameters],
            EntryPoint(10, 11),
            DecorateBuiltIn(10, BuiltInInstanceIndex),
            DecorateBuiltIn(11, BuiltInBaseInstance),
            [(3u << 16) | 74u, 30, 11],
            [OpVariable, 1, 11, 1],
            Load(10, 20),
            Load(11, 21),
            ISub(22, 20, 21));

        uint[][] patched = Instructions(VulkanSilkGraphicsDevice.PatchSlangInstanceIndexLowering(module));

        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 2 &&
            instruction[0] == OpCapability &&
            instruction[1] == CapabilityDrawParameters)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 3 &&
            (instruction[0] & 0xffff) == 74 &&
            instruction[2] == 11)).IsTrue();
        await Assert.That(patched.Any(static instruction =>
            instruction.Length == 4 &&
            instruction[0] == OpVariable &&
            instruction[2] == 11)).IsTrue();
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

    private static uint[] EntryPoint(params uint[] interfaceIds) =>
        [((uint)(5 + interfaceIds.Length) << 16) | 15, 0, 2, 0x6e69616d, 0, .. interfaceIds];

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

    private static uint[][] Instructions(byte[] module)
    {
        uint[] words = ToWords(module);
        var instructions = new List<uint[]>();
        for (int offset = 5; offset < words.Length;)
        {
            int count = checked((int)(words[offset] >> 16));
            instructions.Add(words.AsSpan(offset, count).ToArray());
            offset += count;
        }
        return [.. instructions];
    }
}
