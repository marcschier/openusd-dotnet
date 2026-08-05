// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenUsd.Native.Tests;

/// <summary>
/// Requires the OpenUSD install cache that <c>ci.yml</c>'s coverage gate
/// restores to be saved by <c>native.yml</c> under a character-identical key.
/// </summary>
/// <remarks>
/// The coverage gate needs a native runtime, because every schema facade lives
/// in <c>OpenUsd.dll</c> and a facade can only be exercised against a real
/// stage. Building the locked OpenUSD install takes about ninety minutes, so
/// the gate restores it from a cache and builds only the cheap shim fresh from
/// the commit under test.
///
/// That arrangement shipped broken. <c>native.yml</c> saved
/// <c>native-&lt;rid&gt;-&lt;hash&gt;</c> over the lock, the build scripts and
/// every native source file; the coverage gate restored
/// <c>openusd-install-linux-x64-&lt;hash&gt;</c> over the lock and the build
/// scripts alone. Different prefix, different inputs, different paths, so the
/// restore missed on every single run, hit the deliberate <c>exit 1</c>, and
/// failed the gate permanently -- while its own remediation message advised
/// running a pipeline that produced no cache the gate could ever find.
///
/// A cache restore that fails closed is right. "Fails closed forever" is
/// indistinguishable from broken, so the hit path needs a mechanical guard
/// rather than a comment: <c>hashFiles</c> over a different file list yields a
/// different digest, and nothing else in CI would notice.
///
/// Both sides are parsed from the workflow files rather than restated here, so
/// this test cannot agree with a stale copy of the expectation. That failure
/// mode is not hypothetical: the Metal library contract once passed while
/// proving nothing because the test duplicated the validator's own allowlist.
/// </remarks>
public sealed class CoverageCacheKeyContractTests
{
    private const string CiWorkflow = ".github/workflows/ci.yml";
    private const string NativeWorkflow = ".github/workflows/native.yml";
    private const string FullLock = "eng/openusd.lock.json";
    private const string InstallLock = "eng/openusd.install.lock.json";

    /// <summary>The literal both workflows must key the shared cache on.</summary>
    private const string CacheKeyPrefix = "openusd-install-linux-x64-";

    [Test]
    public async Task NativeWorkflowSavesTheCacheTheCoverageGateRestores()
    {
        string root = FindRepositoryRoot();
        string ci = await ReadWorkflowAsync(root, CiWorkflow);
        string native = await ReadWorkflowAsync(root, NativeWorkflow);

        IReadOnlyList<string> ciInputs = ExtractHashFileInputs(ci);
        IReadOnlyList<string> nativeInputs = ExtractHashFileInputs(native);
        string ciKey = ExtractSharedCacheKeyExpression(ci);
        string nativeKey = ExtractSharedCacheKeyExpression(native);

        // Non-vacuity: if the anchor stops matching, both lists come back empty
        // and comparing them would succeed while checking nothing.
        await Assert.That(ciInputs)
            .IsNotEmpty()
            .Because(
                $"{CiWorkflow} must key the coverage cache on " +
                $"'{CacheKeyPrefix}' with a hashFiles list");
        await Assert.That(nativeInputs)
            .IsNotEmpty()
            .Because(
                $"{NativeWorkflow} must save the same cache, or the coverage " +
                "gate fails closed on every run");

        await Assert.That(nativeKey)
            .IsEqualTo(ciKey)
            .Because(
                "the shared cache key expressions must stay character-identical; " +
                $"{CiWorkflow} uses [{ciKey}] and {NativeWorkflow} uses [{nativeKey}]");

        await Assert.That(nativeInputs)
            .IsEquivalentTo(ciInputs)
            .Because(
                "hashFiles over a different file list yields a different " +
                $"digest, so the restore in {CiWorkflow} would never hit. " +
                $"{CiWorkflow} hashes [{string.Join(", ", ciInputs)}]; " +
                $"{NativeWorkflow} hashes [{string.Join(", ", nativeInputs)}]");
    }

    [Test]
    public async Task SharedCoverageCacheHashesTheInstallProjectionNotTheAbiLock()
    {
        string root = FindRepositoryRoot();
        string ci = await ReadWorkflowAsync(root, CiWorkflow);
        string native = await ReadWorkflowAsync(root, NativeWorkflow);
        IReadOnlyList<string> ciInputs = ExtractHashFileInputs(ci);
        IReadOnlyList<string> nativeInputs = ExtractHashFileInputs(native);

        foreach (IReadOnlyList<string> inputs in new[] { ciInputs, nativeInputs })
        {
            await Assert.That(inputs)
                .Contains(InstallLock)
                .Because(
                    "the shared expensive cache must be keyed by the install " +
                    "projection so ABI-only bumps do not invalidate the locked " +
                    "OpenUSD install");
            await Assert.That(inputs)
                .DoesNotContain(FullLock)
                .Because(
                    $"{FullLock} carries ABI numbers that do not affect the " +
                    "locked OpenUSD install restored by the coverage gate");
        }
    }

    [Test]
    public async Task InstallLockProjectionTracksEveryNonAbiLockInput()
    {
        string root = FindRepositoryRoot();
        JsonNode fullLock = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, FullLock.Replace('/', Path.DirectorySeparatorChar))))!;
        JsonNode installLock = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, InstallLock.Replace('/', Path.DirectorySeparatorChar))))!;

        await Assert.That(fullLock["abi"]).IsNotNull();
        JsonObject expected = (JsonObject)fullLock.DeepClone();
        expected.Remove("abi");

        string expectedJson = Canonicalize(expected);
        string actualJson = Canonicalize(installLock);

        await Assert.That(actualJson)
            .IsEqualTo(expectedJson)
            .Because(
                $"{InstallLock} must be exactly {FullLock} with only the top-level " +
                "abi section removed. If a future lock key affects the install, " +
                "this test fails until the projection is regenerated instead of " +
                "silently ignoring that new input.");
    }

    [Test]
    public async Task BothWorkflowsCacheTheSamePaths()
    {
        string root = FindRepositoryRoot();
        string ci = await ReadWorkflowAsync(root, CiWorkflow);
        string native = await ReadWorkflowAsync(root, NativeWorkflow);

        IReadOnlyList<string> ciPaths = ExtractCachedPaths(ci);
        IReadOnlyList<string> nativePaths = ExtractCachedPaths(native);
        // Non-vacuity, for the same reason as above.
        await Assert.That(ciPaths).IsNotEmpty();
        await Assert.That(nativePaths).IsNotEmpty();

        await Assert.That(ExtractCachedPathBlock(native))
            .IsEqualTo(ExtractCachedPathBlock(ci))
            .Because(
                "the shared cache path blocks must stay character-identical; " +
                "otherwise the coverage gate can restore a partial install");

        await Assert.That(nativePaths)
            .IsEquivalentTo(ciPaths)
            .Because(
                "a restore only yields what was saved, so a narrower save " +
                "leaves the coverage gate with a partial OpenUSD install. " +
                $"{CiWorkflow} restores [{string.Join(", ", ciPaths)}]; " +
                $"{NativeWorkflow} saves [{string.Join(", ", nativePaths)}]");
    }

    [Test]
    public async Task TheCoverageGateBuildsTheShimFromTheCommitUnderTest()
    {
        string root = FindRepositoryRoot();
        string ci = await ReadWorkflowAsync(root, CiWorkflow);

        // The shim is deliberately excluded from the shared cache and rebuilt,
        // because restoring a shim built from different sources than the commit
        // under test is exactly what produced a spurious "managed=9, native=8"
        // ABI mismatch. Keying the shared cache on native sources instead would
        // make every schema change block on a ninety-minute native run.
        await Assert.That(ExtractCachedPaths(ci))
            .DoesNotContain("native/install/shim/linux-x64")
            .Because(
                "the coverage gate must build the shim fresh; a cached shim " +
                "can disagree with the managed ABI of the commit under test");

        await Assert.That(ci)
            .Contains("cmake --install build/shim/linux-x64")
            .Because(
                "building the shim is not enough -- the probes load the " +
                "installed binary, so a build without an install runs stale");
    }

    [Test]
    public async Task TheNativePipelineIsNotCancelledOnMain()
    {
        string root = FindRepositoryRoot();
        string native = await ReadWorkflowAsync(root, NativeWorkflow);

        // Producing the cache is necessary but not sufficient: a run that is
        // cancelled before it finishes writes nothing. Six consecutive runs
        // were cancelled by successive pushes while landing the schema wave,
        // so the coverage gate stayed red for reasons unrelated to its key.
        Match concurrency = Regex.Match(
            native,
            @"concurrency:.*?cancel-in-progress:\s*(?<value>[^\r\n]+)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        await Assert.That(concurrency.Success)
            .IsTrue()
            .Because($"{NativeWorkflow} should declare its concurrency policy");

        await Assert.That(concurrency.Groups["value"].Value.Trim())
            .IsNotEqualTo("true")
            .Because(
                "an unconditional cancel starves the coverage gate, because " +
                $"{NativeWorkflow} is the only producer of the install cache " +
                "and a full run takes about ninety minutes");

        await Assert.That(concurrency.Groups["value"].Value)
            .Contains("refs/heads/main")
            .Because(
                "cancelling is still correct on branches; only main needs the " +
                "pipeline to be allowed to finish");
    }

    private static async Task<string> ReadWorkflowAsync(string root, string relative)
    {
        string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return await File.ReadAllTextAsync(full);
    }

    /// <summary>
    /// Returns the quoted arguments of the <c>hashFiles</c> call that builds the
    /// shared cache key, in declaration order.
    /// </summary>
    private static IReadOnlyList<string> ExtractHashFileInputs(string workflow)
    {
        Match anchor = Regex.Match(
            workflow,
            Regex.Escape(CacheKeyPrefix) + @"\$\{\{\s*hashFiles\((?<args>[^)]*)\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        if (!anchor.Success)
        {
            return [];
        }

        return [.. Regex.Matches(
            anchor.Groups["args"].Value,
            @"'(?<file>[^']+)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["file"].Value)];
    }

    private static string ExtractSharedCacheKeyExpression(string workflow)
    {
        Match anchor = Regex.Match(
            workflow,
            Regex.Escape(CacheKeyPrefix) +
                @"\$\{\{\s*hashFiles\((?<args>[^)]*)\)\s*\}\}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        return anchor.Success ? anchor.Value : string.Empty;
    }

    /// <summary>
    /// Returns the <c>path:</c> block of the cache step that carries the shared
    /// key, in declaration order.
    /// </summary>
    private static List<string> ExtractCachedPaths(string workflow)
    {
        int anchor = workflow.IndexOf(CacheKeyPrefix, StringComparison.Ordinal);
        if (anchor < 0)
        {
            return [];
        }

        // The path block precedes the key within the same step, so search back
        // from the key for the nearest one rather than forward from the step.
        int block = workflow.LastIndexOf("path: |", anchor, StringComparison.Ordinal);
        if (block < 0)
        {
            return [];
        }

        List<string> paths = [];
        string[] lines = workflow[(block + "path: |".Length)..]
            .Split('\n', StringSplitOptions.None);

        foreach (string line in lines.Skip(1))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            // The block ends at the next mapping key, such as "key:".
            if (trimmed.Contains(':', StringComparison.Ordinal))
            {
                break;
            }

            paths.Add(trimmed);
        }

        return paths;
    }

    private static string ExtractCachedPathBlock(string workflow)
    {
        return string.Join("\n", ExtractCachedPaths(workflow));
    }

    private static string Canonicalize(JsonNode node)
    {
        JsonNode normalized = Normalize(node);
        return normalized.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static JsonNode Normalize(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            JsonObject normalized = [];
            foreach (KeyValuePair<string, JsonNode?> property in obj.OrderBy(
                property => property.Key,
                StringComparer.Ordinal))
            {
                normalized[property.Key] = property.Value is null
                    ? null
                    : Normalize(property.Value);
            }

            return normalized;
        }

        if (node is JsonArray array)
        {
            JsonArray normalized = [];
            foreach (JsonNode? item in array)
            {
                normalized.Add(item is null ? null : Normalize(item));
            }

            return normalized;
        }

        return node.DeepClone();
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
