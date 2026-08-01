// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Guards every hand-written MESH_UPSERT encoder in the repository against
/// drifting from the parser's fixed header size.
/// </summary>
/// <remarks>
/// The page ABI 5 to 6 bump grew the mesh command header from 216 to 224 bytes.
/// The encoder is hand-written in a dozen places, and three of them were missed:
/// seven copies in the conformance project (caught locally), the NativeAOT RHI
/// probe and the benchmark data builder. The latter two are only executed by the
/// <c>RHI Linux NativeAOT</c> and <c>performance safety</c> workflows, so they
/// failed in CI after everything local was green.
///
/// This test runs in ordinary CI and names the offending file, so the next wire
/// change is caught in seconds rather than after a hosted round trip.
/// </remarks>
public sealed class SilkMeshCommandEncoderContractTests
{
    private const int FixedHeaderSize = 224;

    private static readonly string[] SearchRoots =
    [
        "src",
        "tests",
        "benchmarks",
        "samples"
    ];

    [Test]
    public async Task EveryMeshUpsertEncoderUsesTheCurrentFixedHeaderSize()
    {
        string root = FindRepositoryRoot();
        List<string> offenders = [];
        int encoderCount = 0;

        foreach (string relativeRoot in SearchRoots)
        {
            string searchRoot = Path.Combine(root, relativeRoot);
            if (!Directory.Exists(searchRoot))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(
                searchRoot,
                "*.cs",
                SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                string text = await File.ReadAllTextAsync(file);
                if (!text.Contains("SilkCommandType.MeshUpsert", StringComparison.Ordinal))
                {
                    continue;
                }

                encoderCount++;

                // The parser itself and the retained state consume the command
                // rather than encoding one, so they carry no header literal.
                if (!text.Contains($"{FixedHeaderSize}", StringComparison.Ordinal) &&
                    !text.Contains("FixedSize", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ContainsStaleHeaderLiteral(text))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        // Non-vacuity: if this ever stops finding encoders the scan is broken,
        // and an empty scan would pass while proving nothing.
        await Assert.That(encoderCount).IsGreaterThan(8);
        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "These files encode a MESH_UPSERT command with a stale fixed " +
                $"header size. The current size is {FixedHeaderSize} bytes: " +
                string.Join(", ", offenders));
    }

    private static bool ContainsStaleHeaderLiteral(string text)
    {
        foreach (int stale in (int[])[216, 208, 212])
        {
            if (text.Contains($"AsSpan({stale})", StringComparison.Ordinal) ||
                text.Contains($"CopyTo(bytes, {stale})", StringComparison.Ordinal) ||
                text.Contains($"new byte[{stale}", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
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
