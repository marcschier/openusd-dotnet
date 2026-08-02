// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Fails when a hand-written frame constants buffer no longer matches the size
/// the checked mesh shaders read.
/// </summary>
/// <remarks>
/// The shader's <c>FrameParameters</c> block is written for real by
/// <c>SilkFrameUniformWriter</c>, but two test harnesses build it by hand
/// because they exercise the RHI directly without a scene: the offscreen RHI
/// conformance cases and the NativeAOT RHI probe.
///
/// Page ABI 9 grew that block from 208 to 544 bytes by adding per-frame
/// lighting, which moved <c>eyeToWorld</c> to offset 480. Both hand-written
/// copies still allocated 208 and read past the end. D3D12 and Vulkan on
/// Windows returned values that happened to render correctly, and SwiftShader
/// on Linux returned zeros, so a lit triangle came back unlit and only the
/// Linux leg of CI failed — roughly twenty minutes after a push, with
/// everything green locally.
///
/// This is the same class of defect as MESH_UPSERT encoder drift, which
/// <see cref="SilkMeshCommandEncoderContractTests"/> already guards. This test
/// is that guard for the frame block: it reads the size out of the shader
/// source and requires every hand-written copy to agree, so the next time the
/// block grows the failure is local and immediate.
/// </remarks>
public sealed class FrameConstantsSizeContractTests
{
    private static readonly string[] HandWrittenCopies =
    [
        Path.Combine("tests", "OpenUsd.Rendering.ConformanceTests", "OffscreenRhiConformance.cs"),
        Path.Combine("tests", "OpenUsd.RhiProbe", "Program.cs")
    ];

    [Test]
    public async Task HandWrittenFrameConstantsMatchTheShaderBlockSize()
    {
        string root = FindRepositoryRoot();
        int shaderSize = ReadShaderFrameParametersByteSize(root);

        // Non-vacuity: if the parser stops finding fields the size collapses and
        // every comparison below would trivially agree on a wrong number.
        await Assert.That(shaderSize).IsGreaterThan(256);

        List<string> mismatches = [];
        foreach (string relative in HandWrittenCopies)
        {
            string path = Path.Combine(root, relative);
            await Assert.That(File.Exists(path))
                .IsTrue()
                .Because($"The hand-written frame constants copy {relative} was not found.");

            string text = await File.ReadAllTextAsync(path);
            Match declared = Regex.Match(
                text,
                @"FrameConstantsByteSize\s*=\s*(?<size>\d+)",
                RegexOptions.CultureInvariant);
            if (!declared.Success)
            {
                mismatches.Add($"{relative} declares no FrameConstantsByteSize");
                continue;
            }

            int size = int.Parse(declared.Groups["size"].Value, CultureInfo.InvariantCulture);
            if (size != shaderSize)
            {
                mismatches.Add($"{relative} declares {size}, shader needs {shaderSize}");
            }
        }

        await Assert.That(mismatches)
            .IsEmpty()
            .Because(
                "A hand-written frame constants buffer is smaller than the block " +
                "the checked shaders read, so the shader reads out of bounds. That " +
                "renders correctly on D3D12 and Vulkan and returns zeros on " +
                "SwiftShader, so it fails only on Linux CI. " +
                string.Join("; ", mismatches));
    }

    /// <summary>
    /// Computes the byte size of <c>FrameParameters</c> from the shader source so
    /// the expectation is derived from the shader rather than restated.
    /// </summary>
    private static int ReadShaderFrameParametersByteSize(string root)
    {
        string source = File.ReadAllText(
            Path.Combine(root, "eng", "shaders", "sources", "mesh.slang"));
        Match block = Regex.Match(
            source,
            @"struct\s+FrameParameters\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!block.Success)
        {
            throw new InvalidOperationException(
                "FrameParameters was not found in mesh.slang.");
        }

        int total = 0;
        foreach (Match field in Regex.Matches(
            block.Groups["body"].Value,
            @"(?<type>float4x4|float4|uint4)\s+\w+(\[(?<count>\d+)\])?\s*;",
            RegexOptions.CultureInvariant))
        {
            int elementSize = field.Groups["type"].Value == "float4x4" ? 64 : 16;
            int count = field.Groups["count"].Success
                ? int.Parse(field.Groups["count"].Value, CultureInfo.InvariantCulture)
                : 1;
            total += elementSize * count;
        }
        return total;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The repository root was not found.");
    }
}
