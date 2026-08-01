// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Fails when a file the checked shader manifest hashes has changed without the
/// payload being regenerated.
/// </summary>
/// <remarks>
/// The checked payload records a SHA-256 for each of its inputs, and several of
/// them are not shader sources at all: <c>ci.yml</c>, <c>release.yml</c>,
/// <c>package.yml</c>, <c>render.yml</c> and
/// <c>tests/OpenUsd.Package.Tests/RuntimePackageTests.cs</c> are hashed too. That
/// is easy to forget, because editing a workflow or a package test does not feel
/// like touching shaders.
///
/// It has now been forgotten twice, and both times the only thing that caught it
/// was <c>shader platform validation</c> failing on every platform after a push,
/// roughly twenty minutes into hosted CI, with
/// <c>ValueError: Checked manifest input hash mismatch</c>.
///
/// <c>eng/shaders/validate-checked-payload.ps1</c> already detects this locally,
/// but it needs Python and nobody runs it as part of an ordinary test pass. This
/// test performs the same input-hash comparison in a few milliseconds with no
/// external tooling, so the failure lands in the normal test run instead.
///
/// It deliberately checks only the recorded input hashes. Validating the compiled
/// programs themselves remains the job of the payload validator and the hosted
/// shader gates.
/// </remarks>
public sealed class CheckedShaderManifestInputContractTests
{
    [Test]
    public async Task EveryCheckedManifestInputMatchesItsRecordedHash()
    {
        string root = FindRepositoryRoot();
        string manifestPath = Path.Combine(
            root, "eng", "shaders", "checked", "manifest.json");
        await Assert.That(File.Exists(manifestPath)).IsTrue();

        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllBytesAsync(manifestPath));
        JsonElement inputs = manifest.RootElement.GetProperty("inputs");

        List<string> stale = [];
        List<string> missing = [];
        int checkedCount = 0;

        foreach (JsonElement input in inputs.EnumerateArray())
        {
            string relativePath = input.GetProperty("path").GetString()!;
            string expected = input.GetProperty("sha256").GetString()!;
            string absolute = Path.Combine(
                root, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(absolute))
            {
                missing.Add(relativePath);
                continue;
            }

            checkedCount++;
            string actual = Convert.ToHexStringLower(
                SHA256.HashData(await File.ReadAllBytesAsync(absolute)));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                stale.Add(relativePath);
            }
        }

        // Non-vacuity: a manifest that listed nothing would pass while proving
        // nothing, and the input list has been 45 entries for a long time.
        await Assert.That(checkedCount).IsGreaterThan(30);
        await Assert.That(missing)
            .IsEmpty()
            .Because(
                "The checked shader manifest references inputs that no longer " +
                "exist: " + string.Join(", ", missing));
        await Assert.That(stale)
            .IsEmpty()
            .Because(
                "These files are hashed inputs of the checked shader payload and " +
                "have changed without it being regenerated, which fails shader " +
                "platform validation on every platform. Run " +
                "eng/shaders/update-checked.ps1 -Offline and commit the manifest. " +
                "Changed: " + string.Join(", ", stale));
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
