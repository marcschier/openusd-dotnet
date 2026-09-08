// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class ParityEvidenceInputsTests
{
    [Test]
    public async Task SourceIdentityIncludesPortableRequiredPaths()
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "source"));
            File.WriteAllText(Path.Combine(root, "source", "shader.txt"), "hello\n");

            ParitySourceIdentity identity = ParityEvidenceInputs.CreateTextIdentity(
                root,
                [@"source\shader.txt"]);

            await Assert.That(identity.FileCount).IsEqualTo(1);
            await Assert.That(identity.Files[0].Path).IsEqualTo("source/shader.txt");
            await Assert.That(identity.Files[0].Length).IsEqualTo(6L);
            await Assert.That(identity.Files[0].Sha256).IsEqualTo(
                "5891B5B522D5DF086D0FF0B110FBD9D21BB4FC7163AF34D08286A2E846F6BE03");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TextEvidenceIsIndependentOfCheckoutLineEndings()
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            string path = Path.Combine(root, "source.txt");
            File.WriteAllText(path, "hello\r\n");
            ParitySourceIdentity windows = ParityEvidenceInputs.CreateTextIdentity(root, ["source.txt"]);
            File.WriteAllText(path, "hello\n");
            ParitySourceIdentity unix = ParityEvidenceInputs.CreateTextIdentity(root, ["source.txt"]);

            await Assert.That(windows.Files[0].Sha256).IsEqualTo(
                "5891B5B522D5DF086D0FF0B110FBD9D21BB4FC7163AF34D08286A2E846F6BE03");
            await Assert.That(windows.Files[0].Length).IsEqualTo(6L);
            await Assert.That(windows.Sha256).IsEqualTo(unix.Sha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CanonicalTextRetainsUtf8BomAndUnpairedCarriageReturns()
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "source.txt"), "\ufeffh\u00e9llo\rline\r\nend\r");
            ParitySourceIdentity identity = ParityEvidenceInputs.CreateTextIdentity(root, ["source.txt"]);

            await Assert.That(identity.HashPolicy).IsEqualTo("sha256-crlf-to-lf");
            await Assert.That(identity.Files[0].Length).IsEqualTo(19L);
            await Assert.That(identity.Files[0].Sha256).IsEqualTo(
                "DF176BBB675E19F61E9C3F60595E891CD050D38BC756C02EBDC985DF30B375F0");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(true, 163840L, "804B93931218C026ACAAA3110224BA91B9890AB4E22FD916D691A6D40A7A1555")]
    [Arguments(false, 163845L, "B46E8A4297BB3501C22BA82D810431D5DB31B65280E80C09DE5136E7408053D3")]
    public async Task TextIdentityPreservesCarriageReturnsAcrossReadBoundaries(
        bool splitPair,
        long expectedLength,
        string expectedHash)
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            string text = splitPair
                ? new string('a', 81919) + "\r\n" + new string('b', 81919) + "\r"
                : new string('a', 81919) + "\r" + new string('b', 81920) + "\r\nend\r";
            File.WriteAllText(Path.Combine(root, "source.txt"), text);
            ParitySourceIdentity identity = ParityEvidenceInputs.CreateTextIdentity(root, ["source.txt"]);

            await Assert.That(identity.Files[0].Length).IsEqualTo(expectedLength);
            await Assert.That(identity.Files[0].Sha256).IsEqualTo(expectedHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SourceIdentityRejectsEmptyDuplicateAndEscapingInputSets()
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            string repository = Path.Combine(root, "repository");
            Directory.CreateDirectory(repository);
            File.WriteAllText(Path.Combine(root, "outside.txt"), "outside");
            File.WriteAllText(Path.Combine(repository, "source.txt"), "inside");

            await Assert.That(() => ParityEvidenceInputs.CreateTextIdentity(repository, []))
                .Throws<ArgumentException>();
            await Assert.That(() => ParityEvidenceInputs.CreateTextIdentity(
                repository,
                ["source.txt", "source.txt"]))
                .Throws<ArgumentException>();
            await Assert.That(() => ParityEvidenceInputs.CreateTextIdentity(repository, [@"..\outside.txt"]))
                .Throws<ArgumentException>();
            await Assert.That(() => ParityEvidenceInputs.CreateTextIdentity(
                repository,
                [Path.Combine(repository, "source.txt")]))
                .Throws<ArgumentException>();
            await Assert.That(() => ParityEvidenceInputs.CreateTextIdentity(repository, ["missing.txt"]))
                .Throws<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FixtureEvidenceIncludesRawAssetsAndDetectsChangesWithoutRewritingSource()
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            string assets = Path.Combine(root, "fixtures", "textures");
            Directory.CreateDirectory(assets);
            File.WriteAllText(Path.Combine(root, "fixtures", "scene.usda"), "hello\r\n");
            string texture = Path.Combine(assets, "base.png");
            File.WriteAllBytes(texture, "hello\r\n"u8.ToArray());

            ParitySourceIdentity before = ParityEvidenceInputs.CreateFixtureIdentity(root, ["fixtures"]);

            await Assert.That(before.HashPolicy).IsEqualTo("sha256-per-input");
            await Assert.That(before.Files[0].Path).IsEqualTo("fixtures/scene.usda");
            await Assert.That(before.Files[1].Path).IsEqualTo("fixtures/textures/base.png");
            ParityInputIdentity binary = before.Files.Single(
                static file => file.Path.EndsWith(".png", StringComparison.Ordinal));
            await Assert.That(binary.HashPolicy).IsEqualTo("sha256-raw");
            await Assert.That(binary.Length).IsEqualTo(7L);
            await Assert.That(binary.Sha256).IsEqualTo(
                "CD2ECA3535741F27A8AE40C31B0C41D4057A7A7B912B33B9AED86485D1C84676");
            ParityInputIdentity text = before.Files.Single(
                static file => file.Path.EndsWith(".usda", StringComparison.Ordinal));
            await Assert.That(text.Length).IsEqualTo(6L);
            await Assert.That(text.Sha256).IsEqualTo(
                "5891B5B522D5DF086D0FF0B110FBD9D21BB4FC7163AF34D08286A2E846F6BE03");
            File.WriteAllBytes(texture, "hello\n"u8.ToArray());
            ParitySourceIdentity after = ParityEvidenceInputs.CreateFixtureIdentity(root, ["fixtures"]);
            await Assert.That(after.Sha256).IsNotEqualTo(before.Sha256);
            await Assert.That(() => ParityEvidenceInputs.RequireUnchanged(before, after))
                .Throws<InvalidOperationException>();
            await Assert.That(File.ReadAllText(Path.Combine(root, "fixtures", "scene.usda"))).IsEqualTo("hello\r\n");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AFixtureInventoryCannotGrowPastItsAdmissionBound()
    {
        string root = Directory.CreateTempSubdirectory("openusd-evidence-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "fixtures"));
            File.WriteAllText(Path.Combine(root, "fixtures", "one.usda"), "one");
            File.WriteAllText(Path.Combine(root, "fixtures", "two.usda"), "two");

            await Assert.That(() => ParityEvidenceInputs.CreateFixtureIdentity(
                root, ["fixtures"], maximumEntries: 2)).Throws<InvalidOperationException>();
            await Assert.That(ParityEvidenceInputs.CreateFixtureIdentity(
                root, ["fixtures"], maximumEntries: 3).FileCount)
                .IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
