// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Fails when a hand-written surface constants buffer no longer matches the
/// block the checked mesh shaders read.
/// </summary>
/// <remarks>
/// <see cref="SilkSurfaceUniformWriter"/> writes the real block, but two
/// harnesses build it by hand because they drive the RHI without a scene: the
/// offscreen RHI conformance cases and the NativeAOT RHI probe. Page ABI 14
/// grew the block from 144 to 176 bytes by appending the two folded UV
/// transform rows, and a hand-written copy that stops at 144 is worse than one
/// that is merely short: the shader then reads an all-zero affine and every
/// texture coordinate collapses onto a single texel, which renders as a plausible
/// flat-shaded image rather than as an obvious failure.
///
/// This is the surface-block counterpart of
/// <see cref="FrameConstantsSizeContractTests"/>, and it checks the written
/// element count rather than only the declared size, because the probe that
/// prompted it allocated the right number of bytes and filled two rows fewer.
/// </remarks>
public sealed class SurfaceConstantsSizeContractTests
{
    private static readonly string[] HandWrittenCopies =
    [
        Path.Combine("tests", "OpenUsd.Rendering.ConformanceTests", "OffscreenRhiConformance.cs"),
        Path.Combine("tests", "OpenUsd.RhiProbe", "Program.cs")
    ];

    [Test]
    public async Task WriterMatchesTheShaderSurfaceBlockSize()
    {
        int shaderSize = ReadShaderSurfaceParametersByteSize(FindRepositoryRoot());

        // Non-vacuity: a parser that stopped matching fields would collapse the
        // size and make every comparison below trivially agree on a wrong number.
        await Assert.That(shaderSize).IsGreaterThan(128);
        int writerSize = SilkSurfaceUniformWriter.ByteSize;
        await Assert.That(writerSize).IsEqualTo(shaderSize);
    }

    [Test]
    public async Task HandWrittenSurfaceConstantsFillTheWholeShaderBlock()
    {
        string root = FindRepositoryRoot();
        int shaderSize = ReadShaderSurfaceParametersByteSize(root);
        int expectedFloats = shaderSize / sizeof(float);

        List<string> mismatches = [];
        foreach (string relative in HandWrittenCopies)
        {
            string path = Path.Combine(root, relative);
            await Assert.That(File.Exists(path))
                .IsTrue()
                .Because($"The hand-written surface constants copy {relative} was not found.");

            string text = await File.ReadAllTextAsync(path);
            Match method = Regex.Match(
                text,
                @"CreateSurfaceConstants\(ISilkGraphicsDevice device\)\s*\{(?<body>.*?)return buffer;",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!method.Success)
            {
                mismatches.Add($"{relative} declares no CreateSurfaceConstants body");
                continue;
            }

            string body = method.Groups["body"].Value;
            Match literal = Regex.Match(
                body,
                @"MemoryMarshal\.AsBytes<float>\(\s*\[(?<values>[^\]]*)\]",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!literal.Success)
            {
                mismatches.Add($"{relative} writes no float initializer");
                continue;
            }

            int written = literal.Groups["values"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;
            if (written != expectedFloats)
            {
                mismatches.Add(
                    $"{relative} writes {written} floats, the shader block holds {expectedFloats}");
            }

            // The probe cannot reference the internal writer, so it restates the
            // size as a row count. When it does, that count must agree too.
            Match declared = Regex.Match(
                body,
                @"surfaceConstantsByteSize\s*=\s*(?<rows>\d+)\s*\*\s*4\s*\*\s*sizeof\(float\)",
                RegexOptions.CultureInvariant);
            if (declared.Success)
            {
                int rows = int.Parse(declared.Groups["rows"].Value, CultureInfo.InvariantCulture);
                if (rows * 4 * sizeof(float) != shaderSize)
                {
                    mismatches.Add(
                        $"{relative} declares {rows} rows, the shader block holds " +
                        $"{shaderSize / (4 * sizeof(float))}");
                }
            }
        }

        await Assert.That(mismatches)
            .IsEmpty()
            .Because(
                "A hand-written surface constants buffer no longer fills the block " +
                "the checked shaders read, so the shader samples an unwritten UV " +
                "transform. " + string.Join("; ", mismatches));
    }

    /// <summary>
    /// Computes the byte size of <c>SurfaceParameters</c> from the shader source
    /// so the expectation is derived from the shader rather than restated.
    /// </summary>
    private static int ReadShaderSurfaceParametersByteSize(string root)
    {
        string source = File.ReadAllText(
            Path.Combine(root, "eng", "shaders", "sources", "mesh.slang"));
        Match block = Regex.Match(
            source,
            @"struct\s+SurfaceParameters\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!block.Success)
        {
            throw new InvalidOperationException(
                "SurfaceParameters was not found in mesh.slang.");
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
