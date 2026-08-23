// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Tests;

/// <summary>
/// Keeps the recorded PhysxSchema licensing decision and the contents of
/// <c>schemas/third-party/</c> in agreement.
/// </summary>
/// <remarks>
/// <para>
/// The plan asked for NVIDIA's PhysxSchema codeless artifacts to be vendored from the
/// PhysX baseline pinned in <c>eng/physx.lock.json</c>. Verification against the upstream
/// repository showed that no schema artifacts exist at that commit at all, so the
/// BSD-3-Clause SDK licence there cannot cover them and nothing was vendored. The
/// evidence is recorded in <c>schemas/third-party/physxSchema/provenance.json</c>.
/// </para>
/// <para>
/// Two version identifiers are involved and are easy to conflate: <c>106.4-physx-5.5.0</c>
/// is an SDK repository tag, while the PhysxSchema USD line declares <c>25.11.1</c> in its
/// own <c>VERSION</c> file. "PhysxSchema 106.4" names nothing. One test below keeps the two
/// recorded separately so a later edit cannot quietly relabel the schema version.
/// </para>
/// <para>
/// The decision itself is easy to undo by accident: dropping a <c>plugInfo.json</c> and a
/// <c>generatedSchema.usda</c> into the tree is a small, plausible-looking commit, and
/// the licensing problem is invisible in the diff. These tests fail if third-party
/// schema payload appears without the provenance record being updated to allow it, and
/// if the recorded baseline drifts away from <c>eng/physx.lock.json</c>.
/// </para>
/// </remarks>
public sealed class PhysxSchemaProvenanceContractTests
{
    private static readonly string[] AllowedEvidenceFiles = ["provenance.json", "PROVENANCE.md"];

    [Test]
    public async Task ProvenanceRecordsAVerifiedDecision()
    {
        using JsonDocument provenance = ReadProvenance();
        JsonElement root = provenance.RootElement;

        await Assert.That(root.GetProperty("component").GetString()).IsEqualTo("PhysxSchema");
        await Assert.That(root.GetProperty("decision").GetString()).IsEqualTo("not-vendored");
        await Assert.That(root.GetProperty("decisionSummary").GetString()).IsNotEmpty();
        await Assert.That(root.GetProperty("verificationMethod").GetString()).IsNotEmpty();
    }

    [Test]
    public async Task ProvenanceRecordsTheApprovedNonVendoringDecision()
    {
        using JsonDocument provenance = ReadProvenance();
        JsonElement decision = provenance.RootElement.GetProperty("approvedDecision");

        await Assert.That(decision.GetProperty("option").GetString()).IsEqualTo("B");
        await Assert.That(decision.GetProperty("status").GetString()).IsEqualTo("approved");
        await Assert.That(decision.GetProperty("title").GetString()).IsNotEmpty();
        await Assert.That(decision.GetProperty("consequences").GetArrayLength()).IsGreaterThan(0)
            .Because("an approved decision must state what it commits the repository to.");
    }

    [Test]
    public async Task ProvenanceKeepsTheSdkTagAndTheSchemaVersionApart()
    {
        using JsonDocument provenance = ReadProvenance();
        JsonElement identifiers = provenance.RootElement.GetProperty("versionIdentifiers");
        JsonElement baseline = identifiers.GetProperty("repositoryCompatibilityBaseline");
        JsonElement schemaVersion = identifiers.GetProperty("embeddedSchemaVersion");

        await Assert.That(baseline.GetProperty("tag").GetString()).IsEqualTo("106.4-physx-5.5.0");
        await Assert.That(baseline.GetProperty("carriesUsdSchemaArtifacts").GetBoolean()).IsFalse();

        await Assert.That(schemaVersion.GetProperty("versionFile").GetString()).IsEqualTo("schemas/physx/VERSION");
        string value = schemaVersion.GetProperty("value").GetString()!;
        await Assert.That(value).IsEqualTo("25.11.1");
        await Assert.That(value).DoesNotContain("106.4")
            .Because("106.4 versions the PhysX SDK tag, never the PhysxSchema USD schema line.");
        await Assert.That(schemaVersion.GetProperty("observedCommit").GetString())
            .IsNotEqualTo(baseline.GetProperty("resolvedCommit").GetString())
            .Because("the schema line and the pinned SDK tag are different revisions.");
    }

    [Test]
    public async Task ProvenanceBaselineMatchesThePinnedPhysxLock()
    {
        using JsonDocument provenance = ReadProvenance();
        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "eng", "physx.lock.json")));

        JsonElement baseline = provenance.RootElement.GetProperty("pinnedBaseline");
        string recordedTag = baseline.GetProperty("tag").GetString()!;
        string recordedRepository = baseline.GetProperty("repository").GetString()!;

        string lockText = lockFile.RootElement.GetRawText();
        await Assert.That(lockText).Contains(recordedTag)
            .Because("the provenance record must describe the revision the repository actually pins.");
        await Assert.That(lockText).Contains(recordedRepository);
        await Assert.That(baseline.GetProperty("resolvedCommit").GetString()).Length().IsEqualTo(40);
        await Assert.That(baseline.GetProperty("schemaArtifactsPresent").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task ProvenanceCitesTheFetchedEvidenceThatBlockedVendoring()
    {
        using JsonDocument provenance = ReadProvenance();
        JsonElement evidence = provenance.RootElement.GetProperty("pinnedBaseline").GetProperty("evidence");

        await Assert.That(evidence.GetArrayLength()).IsGreaterThan(0);

        bool sawMissingSchemaTree = false;
        foreach (JsonElement item in evidence.EnumerateArray())
        {
            string url = item.GetProperty("url").GetString()!;
            await Assert.That(url).StartsWith("https://");
            await Assert.That(item.GetProperty("observed").GetString()).IsNotEmpty();

            if (item.GetProperty("status").GetInt32() == 404)
            {
                sawMissingSchemaTree = true;
            }
        }

        await Assert.That(sawMissingSchemaTree).IsTrue()
            .Because("the blocking fact is that the schema tree is absent at the pinned commit.");
    }

    [Test]
    public async Task NoPhysxSchemaPayloadIsVendoredWhileTheDecisionSaysNotVendored()
    {
        using JsonDocument provenance = ReadProvenance();
        if (provenance.RootElement.GetProperty("decision").GetString() != "not-vendored")
        {
            return;
        }

        string directory = Path.Combine(FindRepositoryRoot(), "schemas", "third-party", "physxSchema");
        string[] unexpected =
        [
            .. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path))
                .Where(relative => !AllowedEvidenceFiles.Contains(relative, StringComparer.Ordinal)),
        ];

        await Assert.That(unexpected).IsEmpty()
            .Because(
                "the recorded decision is not-vendored, so only the evidence files may exist. " +
                $"Unexpected: {string.Join(", ", unexpected)}");
    }

    [Test]
    public async Task NoForeignSchemaPluginIsRegisteredUnderTheSchemasRoot()
    {
        string schemas = Path.Combine(FindRepositoryRoot(), "schemas");
        string[] foreignPlugins =
        [
            .. Directory.EnumerateFiles(schemas, "plugInfo.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(schemas, path).Replace('\\', '/'))
                .Where(relative => !relative.StartsWith("openUsdPhysics/", StringComparison.Ordinal)),
        ];

        await Assert.That(foreignPlugins).IsEmpty()
            .Because(
                "only the project-owned openUsdPhysics plugin may be registered from schemas/. " +
                $"Unexpected: {string.Join(", ", foreignPlugins)}");
    }

    private static JsonDocument ReadProvenance() => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "schemas", "third-party", "physxSchema", "provenance.json")));

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

        throw new InvalidOperationException(
            $"Unable to locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
