// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the arithmetic the checked mesh fragment was actually compiled to,
/// rather than the arithmetic its source describes.
/// </summary>
/// <remarks>
/// <para>
/// The GGX denominator is written as <c>n2 * a2 + (1 - n2)</c> rather than Storm's
/// <c>n2 * (a2 - 1) + 1</c>. The two are the same expression in exact arithmetic,
/// which is precisely the problem: reassociating one into the other is a legal
/// transformation under fast floating-point rules, and doing so reintroduces a
/// cancellation that returns three million times the peak of the lobe at
/// <c>n.h = 1</c>. Source review cannot establish that it did not happen; only the
/// compiled artifact can.
/// </para>
/// <para>
/// Every value on the chain carries the <c>precise</c> qualifier, which is the
/// HLSL and Slang contract forbidding reassociation and contraction along it.
/// These tests check that the contract survived into each shipped artifact: as an
/// instruction pattern in SPIR-V, and verbatim in the Metal shading language
/// source. The DXIL half is covered by execution instead --
/// <c>SilkEnvironmentLightingConformance.TheSpecularLobeReturnsItsPeakAtExactAlignment</c>
/// renders the exact-alignment case on D3D12 WARP and Vulkan SwiftShader, where a
/// reassociated denominator saturates the frame and the correct one does not.
/// </para>
/// </remarks>
public sealed class SilkCheckedShaderPrecisionTests
{
    private const uint OpConstant = 43;
    private const uint OpDecorate = 71;
    private const uint OpFAdd = 129;
    private const uint OpFSub = 131;
    private const uint OpFMul = 133;
    private const uint OpFDiv = 136;
    private const uint OpExtInst = 12;
    private const uint SpirvMagic = 0x07230203;

    /// <summary>The fragment permutations that shade a lit surface.</summary>
    public static IEnumerable<string> LitFragmentPermutations() =>
    [
        "mesh.fragment",
        "mesh.fragment.uv",
        "mesh.fragment.uv+material",
        "mesh.fragment.uv+material+normal",
        "mesh.fragment.uv+normal",
    ];

    [Test]
    [MethodDataSource(nameof(LitFragmentPermutations))]
    public async Task TheCompiledSpirvKeepsTheStableGgxDenominatorGrouping(string program)
    {
        // The stable grouping compiles to an OpFAdd whose operands are an OpFMul
        // and an OpFSub of the form (1 - x), where the same x feeds the multiply.
        // The cancelling grouping compiles to an OpFAdd against the constant one
        // over an OpFMul of an OpFSub of the form (x - 1), and matches nothing
        // here. Verified against a deliberately reassociated build: this count is
        // two for the shipped artifact -- the base lobe and the clearcoat lobe --
        // and zero for the reassociated one.
        byte[] spirv = ReadCheckedArtifact($"{program}.spv");
        int matches = CountStableDenominatorPattern(spirv);

        await Assert.That(matches)
            .IsEqualTo(2)
            .Because(
                $"{program}.spv must evaluate the GGX denominator as " +
                "n2 * a2 + (1 - n2) for the base lobe and the clearcoat lobe, " +
                $"and it matched {matches} such groupings.");
    }

    [Test]
    [MethodDataSource(nameof(LitFragmentPermutations))]
    public async Task TheCompiledMetalSourceKeepsThePreciseQualifiers(string program)
    {
        // Slang emits `precise` into Metal shading language verbatim, so the
        // contract is readable in the shipped source rather than inferred from it.
        // Metal is otherwise gated by source and translation coverage only -- no
        // device is created and no metallib is linked in this repository -- which
        // makes this the whole of the evidence for that backend.
        string metal = Encoding.UTF8.GetString(ReadCheckedArtifact($"{program}.metal"));

        foreach (string declaration in new[]
        {
            "precise float lobeCosineSquared",
            "precise float scaledLobe",
            "precise float lobeComplement",
            "precise float denominator",
            "precise float scaled",
        })
        {
            await Assert.That(metal)
                .Contains(declaration)
                .Because(
                    $"{program}.metal must carry the precise contract on the GGX " +
                    $"denominator chain, and '{declaration}' was not translated.");
        }

        // And the grouping itself, which is what precise is protecting.
        await Assert.That(metal)
            .Contains("scaledLobe_0 + lobeComplement_0")
            .Because(
                $"{program}.metal must add the scaled lobe to the complement " +
                "rather than reassociating through (alphaSquared - 1).");
        await Assert.That(metal)
            .Contains("1.0f - lobeCosineSquared_0")
            .Because(
                $"{program}.metal must form the complement as (1 - n2) rather " +
                "than subtracting one from alphaSquared.");
    }

    [Test]
    public async Task TheCheckedSourceDeclaresThePreciseContract()
    {
        // The checked source is itself hash-verified, so this pins the intent the
        // two artifact tests above verify the compilers honoured. Without it, a
        // source edit that dropped `precise` would leave the artifacts unchanged
        // on this toolchain and silently remove the contract for the next one.
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "eng",
            "shaders",
            "sources",
            "mesh.slang"));

        await Assert.That(source).Contains("precise float lobeCosineSquared");
        await Assert.That(source).Contains("precise float scaledLobe");
        await Assert.That(source).Contains("precise float lobeComplement");
        await Assert.That(source).Contains("precise float denominator");
        await Assert.That(source).Contains("precise float scaled");

        // Comments are excluded: the source explains the cancelling form at length
        // so the next reader knows why the grouping is what it is, and a check that
        // could not tell an explanation from an instruction would force that
        // explanation to be deleted.
        string code = string.Join(
            '\n',
            source
                .Split('\n')
                .Where(static line =>
                    !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        await Assert.That(code)
            .DoesNotContain("alphaSquared - 1")
            .Because(
                "The GGX denominator must never be written in the form that " +
                "cancels catastrophically in single precision.");
    }

    /// <summary>
    /// Counts <c>OpFAdd(OpFMul(x, y), OpFSub(1.0, x))</c> groupings.
    /// </summary>
    /// <remarks>
    /// Requiring the same value on both sides is what makes this specific: an
    /// unrelated <c>a * b + (1 - c)</c> elsewhere in the shader does not match, and
    /// the reassociated denominator -- which multiplies by <c>(a2 - 1)</c> and adds
    /// the constant one -- cannot match at all.
    /// </remarks>
    private static int CountStableDenominatorPattern(byte[] spirv)
    {
        (uint Opcode, uint[] Operands)[] instructions = ParseSpirv(spirv);
        var constants = new Dictionary<uint, float>();
        var definitions = new Dictionary<uint, (uint Opcode, uint[] Operands)>();
        foreach ((uint opcode, uint[] operands) in instructions)
        {
            if (opcode == OpConstant && operands.Length >= 3)
            {
                constants[operands[1]] = BitConverter.Int32BitsToSingle(
                    unchecked((int)operands[2]));
            }
            else if (operands.Length >= 2 &&
                opcode is OpFAdd or OpFSub or OpFMul or OpFDiv or OpExtInst)
            {
                definitions[operands[1]] = (opcode, operands[2..]);
            }
        }

        var ones = constants
            .Where(static pair => pair.Value == 1f)
            .Select(static pair => pair.Key)
            .ToHashSet();

        int matches = 0;
        foreach ((uint opcode, uint[] operands) in instructions)
        {
            if (opcode != OpFAdd || operands.Length < 4)
            {
                continue;
            }
            if (isStableGrouping(operands[2], operands[3]) ||
                isStableGrouping(operands[3], operands[2]))
            {
                matches++;
            }
        }
        return matches;

        bool isStableGrouping(uint product, uint complement)
        {
            if (!definitions.TryGetValue(product, out (uint Opcode, uint[] Operands) multiply) ||
                multiply.Opcode != OpFMul ||
                !definitions.TryGetValue(complement, out (uint Opcode, uint[] Operands) subtract) ||
                subtract.Opcode != OpFSub ||
                subtract.Operands.Length < 2 ||
                !ones.Contains(subtract.Operands[0]))
            {
                return false;
            }
            return multiply.Operands.Contains(subtract.Operands[1]);
        }
    }

    private static (uint Opcode, uint[] Operands)[] ParseSpirv(byte[] spirv)
    {
        if (spirv.Length < 20 || spirv.Length % 4 != 0)
        {
            throw new InvalidDataException("The SPIR-V module is truncated.");
        }
        uint[] words = new uint[spirv.Length / 4];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                spirv.AsSpan(index * 4, 4));
        }
        if (words[0] != SpirvMagic)
        {
            throw new InvalidDataException("The SPIR-V magic number is wrong.");
        }

        var instructions = new List<(uint, uint[])>();
        int cursor = 5;
        while (cursor < words.Length)
        {
            uint opcode = words[cursor] & 0xFFFF;
            int length = checked((int)(words[cursor] >> 16));
            if (length <= 0 || cursor + length > words.Length)
            {
                throw new InvalidDataException("The SPIR-V instruction stream is malformed.");
            }
            // The opcode word itself is dropped, so operands[0] is the type id and
            // operands[1] is the result id for every instruction that has them.
            instructions.Add((opcode, words[(cursor + 1)..(cursor + length)]));
            cursor += length;
        }

        // A module that parsed to nothing would make every assertion above pass
        // vacuously, so the shape of the stream is checked rather than assumed.
        if (instructions.Count < 64 ||
            !instructions.Exists(static instruction => instruction.Item1 == OpDecorate))
        {
            throw new InvalidDataException("The SPIR-V module carries no instructions.");
        }
        return [.. instructions];
    }

    private static byte[] ReadCheckedArtifact(string name) =>
        File.ReadAllBytes(Path.Combine(RepositoryRoot(), "eng", "shaders", "checked", name));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
