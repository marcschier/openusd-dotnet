// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Guards every hand-written MESH_UPSERT encoder in the repository against
/// drifting from the parser's fixed header size.
/// </summary>
/// <remarks>
/// The page ABI 5 to 6 bump grew the mesh command header from 216 to 224
/// bytes, the ABI 19 to 20 bump grew it again from 224 to 236 for the
/// deformation block descriptor, the ABI 21 to 22 bump grew it from 236 to
/// 260 for the subprim-identity table descriptors, and the authoritative
/// instancer path lifted it from 260 to 268.
/// The encoder is hand-written in nine places, and three of them were missed:
/// seven copies in the conformance project (caught locally), the NativeAOT RHI
/// probe and the benchmark data builder. The latter two are only executed by the
/// <c>RHI Linux NativeAOT</c> and <c>performance safety</c> workflows, so they
/// failed in CI after everything local was green.
///
/// This test runs in ordinary CI and names the offending file, so the next wire
/// change is caught in seconds rather than after a hosted round trip.
///
/// The expected size is <b>parsed out of the parser</b> rather than restated
/// here. An earlier version of this guard hard-coded 224 and recognised drift
/// only by a hand-maintained list of the specific offsets that moved in the 216
/// to 224 bump. That construction fails open: once the header moves again, every
/// correctly-updated encoder stops mentioning the stale size, the denylist no
/// longer matches anything, and the guard passes while proving nothing. Deriving
/// the size from <see cref="SilkMeshUpsertCommand"/> means the guard's
/// expectation cannot drift from the type that defines it, and requiring each
/// encoder to reference that derived size catches drift by absence rather than
/// by pattern.
/// </remarks>
public sealed class SilkMeshCommandEncoderContractTests
{
    private const string ThisFileName = "SilkMeshCommandEncoderContractTests.cs";

    private const string ParserRelativePath =
        "src/OpenUsd.Rendering.Silk/SilkCommand.cs";

    private static readonly string[] SearchRoots =
    [
        "src",
        "tests",
        "benchmarks",
        "samples"
    ];

    /// <summary>
    /// A file that writes the command type into a byte buffer encodes the
    /// command; a file that only switches on it or calls
    /// <c>AsMeshUpsert()</c> consumes one and carries no header literal.
    /// </summary>
    private static readonly string[] EncoderMarkers =
    [
        "(uint)SilkCommandType.MeshUpsert",
        "(int)SilkCommandType.MeshUpsert"
    ];

    [Test]
    public async Task EveryMeshUpsertEncoderUsesTheCurrentFixedHeaderSize()
    {
        string root = FindRepositoryRoot();
        int fixedHeaderSize = ReadParserFixedHeaderSize(root);

        List<string> missingCurrentSize = [];
        List<string> staleOffsets = [];
        int encoderCount = 0;
        int consumerCount = 0;

        foreach (string file in EnumerateSourceFiles(root))
        {
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

            if (!EncoderMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
            {
                consumerCount++;
                continue;
            }

            encoderCount++;
            string relative = Path.GetRelativePath(root, file);

            if (!text.Contains(
                    fixedHeaderSize.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
            {
                missingCurrentSize.Add(relative);
            }

            if (ContainsStaleHeaderLiteral(text))
            {
                staleOffsets.Add(relative);
            }
        }

        // Non-vacuity: if the scan ever stops finding encoders, or stops
        // telling encoders from consumers, it would pass while proving nothing.
        await Assert.That(encoderCount).IsGreaterThan(8);
        await Assert.That(consumerCount).IsGreaterThan(0);

        await Assert.That(missingCurrentSize)
            .IsEmpty()
            .Because(
                "These files encode a MESH_UPSERT command without referencing " +
                $"the parser's current {fixedHeaderSize}-byte fixed header: " +
                string.Join(", ", missingCurrentSize));

        await Assert.That(staleOffsets)
            .IsEmpty()
            .Because(
                "These files encode a MESH_UPSERT command at an offset that " +
                "moved when the header grew to its current size: " +
                string.Join(", ", staleOffsets));
    }

    [Test]
    public async Task ParserFixedHeaderSizeIsDerivedFromTheParserItself()
    {
        string root = FindRepositoryRoot();
        int fixedHeaderSize = ReadParserFixedHeaderSize(root);

        // A parse that silently matched nothing would return zero and make
        // every comparison above trivially satisfied.
        await Assert.That(fixedHeaderSize).IsGreaterThan(216);
        await Assert.That(fixedHeaderSize % 4).IsEqualTo(0);
    }

    [Test]
    public async Task ParserFixedHeaderSizeParserRejectsAMissingConstant()
    {
        await Assert.That(TryParseFixedHeaderSize(
                "public readonly ref struct SilkMeshUpsertCommand\n{\n}\n",
                out _))
            .IsFalse();

        // The frame-constants writer and the mesh header both use 224 for
        // unrelated reasons, so the parser must key off the declaring type.
        await Assert.That(TryParseFixedHeaderSize(
                "int positionOffset = 224 + (index * 16);",
                out _))
            .IsFalse();

        bool parsed = TryParseFixedHeaderSize(
            "public readonly ref struct SilkMeshUpsertCommand\n" +
            "{\n    private const int FixedSize = 232;\n}\n",
            out int size);
        await Assert.That(parsed).IsTrue();
        await Assert.That(size).IsEqualTo(232);
    }

    [Test]
    public async Task StaleMeshUpsertEncoderGuardRejectsRealDriftMarkers()
    {
        await Assert.That(ContainsStaleHeaderLiteral("path.CopyTo(bytes, 216);"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("path.CopyTo(bytes, 224);"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("path.CopyTo(bytes, 236);"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("path.CopyTo(bytes, 260);"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("var bytes = new byte[216 + path.Length];"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("var bytes = new byte[224 + path.Length];"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("var bytes = new byte[236 + path.Length];"))
            .IsTrue();
        await Assert.That(ContainsStaleHeaderLiteral("var bytes = new byte[260 + path.Length];"))
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
        await Assert.That(ContainsStaleHeaderLiteral(
                "BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(232), deformationBytes);"))
            .IsFalse();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
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

                yield return file;
            }
        }
    }

    private static int ReadParserFixedHeaderSize(string root)
    {
        string parserPath = Path.Combine(
            root,
            ParserRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(parserPath);
        if (!TryParseFixedHeaderSize(text, out int size))
        {
            throw new InvalidOperationException(
                $"'{ParserRelativePath}' no longer declares a FixedSize " +
                "constant on SilkMeshUpsertCommand, so the mesh command header " +
                "size can no longer be derived from the parser.");
        }
        return size;
    }

    private static bool TryParseFixedHeaderSize(string text, out int size)
    {
        size = 0;

        // Key off the declaring type, because 224 also appears in the
        // frame-constants writer for an unrelated reason.
        int typeIndex = text.IndexOf(
            "struct SilkMeshUpsertCommand",
            StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            return false;
        }

        Match match = Regex.Match(
            text[typeIndex..],
            @"private\s+const\s+int\s+FixedSize\s*=\s*(?<size>\d+)\s*;",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        if (!match.Success)
        {
            return false;
        }

        size = int.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool ContainsStaleHeaderLiteral(string text)
    {
        // The current 268-byte header ends with the material hash at 208, the
        // material path length at 216, the attribute count at 220, the ABI
        // v20 deformation flags, unsupported reasons and block byte count at
        // 224, 228 and 232, and the ABI v22 subprim identity flags,
        // unsupported reasons, four table counts and the instancer path length
        // at 236 through 260, so every one of those is a LEGITIMATE offset and
        // must never be flagged. What moved when the header grew is the size
        // base and the offsets that sat where those fields now begin.
        //
        // This list recognises drift from the four historical bumps: 216 to
        // 224, 224 to 236, 236 to 260, and 260 to 268. It is a secondary net;
        // the load-bearing check is that every encoder references the size
        // parsed out of the parser.
        string[] stalePatterns =
        [
            // The variable-length section used to start at 216, then at 224,
            // then at 236, then at 260.
            "CopyTo(bytes, 216)",
            "CopyTo(bytes, 224)",
            "CopyTo(bytes, 236)",
            "CopyTo(bytes, 260)",
            // The fixed header used to be 216, 224, 236 and then 260 bytes, so
            // any of those as a size base is stale.
            "new byte[216 +",
            "new byte[224 +",
            "new byte[236 +",
            "new byte[260 +",
            "= 216 +",
            "= 224 +",
            "= 236 +",
            "= 260 +",
            "216 +\n",
            "216 +\r",
            "224 +\n",
            "224 +\r",
            "236 +\n",
            "236 +\r",
            "260 +\n",
            "260 +\r",
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
