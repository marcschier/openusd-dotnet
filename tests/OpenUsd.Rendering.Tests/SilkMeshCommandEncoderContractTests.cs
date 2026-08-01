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

    private const string ThisFileName = "SilkMeshCommandEncoderContractTests.cs";

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

                // This test names the stale patterns as string literals, so it
                // would otherwise report itself.
                if (Path.GetFileName(file) == ThisFileName)
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

    [Test]
    public async Task StaleMeshUpsertEncoderGuardRejectsRealDriftMarkers()
    {
        await Assert.That(ContainsStaleHeaderLiteral("path.CopyTo(bytes, 216);"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("var bytes = new byte[216 + path.Length];"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral(
                "BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(200), hash);"))
            .IsTrue();
    }

    [Test]
    public async Task StaleMeshUpsertEncoderGuardAllowsCurrentMaterialOffsets()
    {
        await Assert.That(ContainsStaleHeaderLiteral(
                "BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(208), hash);"))
            .IsFalse();
        await Assert.That(ContainsStaleHeaderLiteral(
                "BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), length);"))
            .IsFalse();
    }

    private static bool ContainsStaleHeaderLiteral(string text)
    {
        // The current 224-byte header ends with the material hash at 208, the
        // material path length at 216 and the attribute count at 220, so 208 and
        // 216 are LEGITIMATE offsets and must never be flagged. What moved when
        // the header grew from 216 to 224 is the size base and the two offsets
        // that sat where the material hash and attribute count now begin.
        string[] stalePatterns =
        [
            // The variable-length section used to start at 216.
            "CopyTo(bytes, 216)",
            // The fixed header used to be 216 bytes, so a size base of 216 is stale.
            "new byte[216 +",
            "= 216 +",
            "216 +\n",
            "216 +\r",
            // The material hash used to sit at 200 and the attribute count at 212.
            "AsSpan(200)",
            "AsSpan(200,",
            "AsSpan(212)",
            "AsSpan(212,"
        ];

        foreach (string pattern in stalePatterns)
        {
            if (text.Contains(pattern, StringComparison.Ordinal))
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
