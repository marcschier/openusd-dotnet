// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace OpenUsd.Viewer.Tests;

[SupportedOSPlatform("windows")]
public sealed class ViewerSourceLayerPublicationTests
{
    [Test]
    [Arguments(".usda", "#usda 1.0\n# text USD\n", ".usda")]
    [Arguments(".usdc", "PXR-USDC", ".usdc")]
    [Arguments(".usd", "#usda 1.0\n# detected text USD\n", ".usda")]
    [Arguments(".usd", "PXR-USDC", ".usdc")]
    public async Task Begin_SelectsStagingExtensionFromSupportedSourceFormat(
        string sourceExtension, string sourceText, string expectedStagingExtension)
    {
        byte[] sourceBytes = System.Text.Encoding.ASCII.GetBytes(sourceText);
        using var files = await SourceFiles.CreateAsync($"source{sourceExtension}", sourceBytes);

        await using (ViewerSourceLayerPublication publication =
            await ViewerSourceLayerPublication.BeginAsync(
                files.SourcePath, Fingerprint(sourceBytes), CancellationToken.None))
        {
            await Assert.That(Path.GetExtension(publication.StagingPath))
                .IsEqualTo(expectedStagingExtension);
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(sourceBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    [Arguments("source.obj", "not USD")]
    [Arguments("source.usd", "unknown USD encoding")]
    public async Task Begin_RefusesUnsupportedSourceFormatWithoutCreatingOutput(
        string fileName, string sourceText)
    {
        byte[] sourceBytes = System.Text.Encoding.ASCII.GetBytes(sourceText);
        using var files = await SourceFiles.CreateAsync(fileName, sourceBytes);

        await Assert.That(async () => await ViewerSourceLayerPublication.BeginAsync(
            files.SourcePath, Fingerprint(sourceBytes), CancellationToken.None))
            .Throws<NotSupportedException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(sourceBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task Publish_RequiresValidatedStagingAndPreservesSource()
    {
        using var files = await SourceFiles.CreateAsync();
        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            await Assert.That(() => publication.Publish()).Throws<InvalidOperationException>();
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes))
            .IsTrue();
    }

    [Test]
    public async Task Begin_CreatesOwnedStagingBesideSelectedSourceAndDisposeRemovesOnlyIt()
    {
        using var files = await SourceFiles.CreateAsync();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        byte[] foreignBytes = "foreign staging file"u8.ToArray();
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        string stagingPath;

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            stagingPath = publication.StagingPath;
            string stagingDirectory = Path.GetDirectoryName(publication.StagingPath)!;
            await Assert.That(stagingDirectory).IsEqualTo(files.DirectoryPath);
            await Assert.That(Path.GetFileName(publication.StagingPath).StartsWith(
                ".openusd-source-", StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(publication.StagingPath, foreign, StringComparison.OrdinalIgnoreCase))
                .IsFalse();
            await Assert.That(File.Exists(publication.StagingPath)).IsTrue();
        }

        await Assert.That(File.Exists(stagingPath)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
    }

    [Test]
    public async Task Begin_RejectsFingerprintMismatchWithoutChangingSourceOrForeignFiles()
    {
        using var files = await SourceFiles.CreateAsync();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        byte[] foreignBytes = "unowned"u8.ToArray();
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        string incorrectFingerprint = Fingerprint("not the source bytes"u8.ToArray());

        await Assert.That(async () => await ViewerSourceLayerPublication.BeginAsync(
            files.SourcePath, incorrectFingerprint, CancellationToken.None)).Throws<IOException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task Begin_AlreadyCanceledLeavesNoPartialOutputAndPreservesSource()
    {
        using var files = await SourceFiles.CreateAsync();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        byte[] foreignBytes = "keep this unrelated file"u8.ToArray();
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await ViewerSourceLayerPublication.BeginAsync(
            files.SourcePath, Fingerprint(files.SourceBytes), cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task CompleteStaging_ValidatesPayloadAndPublishReplacesOnlySelectedSource()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] payload = "#usda 1.0\n# selected source payload\n"u8.ToArray();
        string unrelated = Path.Combine(files.DirectoryPath, "neighbor.usda");
        byte[] unrelatedBytes = "#usda 1.0\n# leave neighbor unchanged\n"u8.ToArray();
        await File.WriteAllBytesAsync(unrelated, unrelatedBytes);

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            await File.WriteAllBytesAsync(publication.StagingPath, payload);
            await publication.CompleteStagingAsync(CancellationToken.None);
            ViewerFileIdentity published = publication.Publish();

            await Assert.That(published.FullPath).IsEqualTo(files.SourcePath);
            await Assert.That(published.Exists).IsTrue();
            await Assert.That(published.Length).IsEqualTo(payload.LongLength);
            await Assert.That(published.Sha256).IsEqualTo(Fingerprint(payload));
            await Assert.That(File.Exists(publication.StagingPath)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(unrelated)).SequenceEqual(unrelatedBytes)).IsTrue();
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(payload)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task CompleteStaging_CancellationCleansOwnedOutputOnDisposeAndPreservesForeignFiles()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] foreignBytes = "not owned by the publication"u8.ToArray();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        string stagingPath;

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            stagingPath = publication.StagingPath;
            await File.WriteAllBytesAsync(stagingPath, "staged payload"u8.ToArray());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.That(async () => await publication.CompleteStagingAsync(cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(File.Exists(stagingPath)).IsTrue();
        }

        await Assert.That(File.Exists(stagingPath)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task CompleteStaging_RejectsEmptyPayloadAndDisposePreservesSourceAndForeignFiles()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] foreignBytes = "foreign"u8.ToArray();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        string stagingPath;

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            stagingPath = publication.StagingPath;
            await Assert.That(async () => await publication.CompleteStagingAsync(CancellationToken.None))
                .Throws<InvalidDataException>();
        }

        await Assert.That(File.Exists(stagingPath)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task Publish_ExternalSourceReplacementIsRefusedWithoutOverwritingCurrentBytes()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] externalBytes = "#usda 1.0\n# external change wins\n"u8.ToArray();
        byte[] payload = "#usda 1.0\n# staged change must not publish\n"u8.ToArray();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        byte[] foreignBytes = "foreign"u8.ToArray();
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        string stagingPath;

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            stagingPath = publication.StagingPath;
            await File.WriteAllBytesAsync(stagingPath, payload);
            await publication.CompleteStagingAsync(CancellationToken.None);
            ReplaceFileByHandle(files.SourcePath, externalBytes);

            await Assert.That(() => publication.Publish()).Throws<IOException>();
            await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(externalBytes)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(externalBytes)).IsTrue();
        await Assert.That(File.Exists(stagingPath)).IsFalse();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task Rollback_AfterPublicationRestoresExactOriginalBytesAndRemovesOwnedStaging()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] foreignBytes = "foreign file remains"u8.ToArray();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        await File.WriteAllBytesAsync(foreign, foreignBytes);
        byte[] payload = "#usda 1.0\n# rollback must restore old source\n"u8.ToArray();
        string stagingPath;

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            stagingPath = publication.StagingPath;
            await File.WriteAllBytesAsync(stagingPath, payload);
            await publication.CompleteStagingAsync(CancellationToken.None);
            _ = publication.Publish();

            publication.Rollback();

            await Assert.That(File.Exists(stagingPath)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task Rollback_BeforePublicationLeavesSourceAndForeignFileUntouched()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] foreignBytes = "not owned"u8.ToArray();
        string foreign = Path.Combine(files.DirectoryPath, ".openusd-source-foreign.usda");
        await File.WriteAllBytesAsync(foreign, foreignBytes);

        await using (ViewerSourceLayerPublication publication = await BeginAsync(files))
        {
            publication.Rollback();
            await Assert.That(File.Exists(publication.StagingPath)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(files.SourceBytes)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(foreign)).SequenceEqual(foreignBytes)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    [Test]
    public async Task RepeatedStaleFingerprintAttemptsAreRefusedAndFreshAttemptPublishesCurrentSource()
    {
        using var files = await SourceFiles.CreateAsync();
        byte[] currentBytes = "#usda 1.0\n# current external source\n"u8.ToArray();
        byte[] payload = "#usda 1.0\n# fresh operation payload\n"u8.ToArray();
        string externalPath = Path.Combine(files.DirectoryPath, "external.usda");
        await File.WriteAllBytesAsync(externalPath, currentBytes);
        File.Move(externalPath, files.SourcePath, overwrite: true);
        string staleFingerprint = Fingerprint(files.SourceBytes);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            await Assert.That(async () => await ViewerSourceLayerPublication.BeginAsync(
                files.SourcePath, staleFingerprint, CancellationToken.None)).Throws<IOException>();
            await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(currentBytes)).IsTrue();
            await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
        }

        await using (ViewerSourceLayerPublication publication =
            await ViewerSourceLayerPublication.BeginAsync(
                files.SourcePath, Fingerprint(currentBytes), CancellationToken.None))
        {
            await File.WriteAllBytesAsync(publication.StagingPath, payload);
            await publication.CompleteStagingAsync(CancellationToken.None);
            _ = publication.Publish();
        }

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(payload)).IsTrue();
        await Assert.That(OwnedStagingFiles(files.DirectoryPath)).IsEmpty();
    }

    private static Task<ViewerSourceLayerPublication> BeginAsync(SourceFiles files) =>
        ViewerSourceLayerPublication.BeginAsync(
            files.SourcePath, Fingerprint(files.SourceBytes), CancellationToken.None);

    private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string[] OwnedStagingFiles(string directoryPath) =>
        Directory.GetFiles(directoryPath, ".openusd-source-*")
            .Where(path => !Path.GetFileName(path).Equals(
                ".openusd-source-foreign.usda", StringComparison.Ordinal))
            .ToArray();

    internal static void ReplaceFileByHandle(string destination, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(destination)!;
        string stagingName = $".openusd-external-{Guid.NewGuid():N}.tmp";
        string stagingPath = Path.Combine(directory, stagingName);
        using (FileStream output = ViewerWindowsFiles.CreateStaging(directory, stagingName))
        {
            output.Write(bytes);
            output.Flush(flushToDisk: true);
        }

        using var parent = ViewerWindowsFiles.OpenDirectoryForPublication(directory);
        using FileStream staging = ViewerWindowsFiles.OpenReadForReplacement(stagingPath, stagingPath);
        ViewerWindowsFiles.RenameReplacingOpenTarget(
            stagingPath, stagingPath, ViewerWindowsFiles.GetIdentity(staging.SafeFileHandle),
            parent, Path.GetFileName(destination));
    }

    private sealed class SourceFiles : IDisposable
    {
        private readonly string _sourceFileName;

        private SourceFiles(string directoryPath, string sourceFileName, byte[] sourceBytes)
        {
            DirectoryPath = directoryPath;
            _sourceFileName = sourceFileName;
            SourceBytes = sourceBytes;
        }

        internal string DirectoryPath { get; }
        internal string SourcePath => Path.Combine(DirectoryPath, _sourceFileName);
        internal byte[] SourceBytes { get; }

        internal static async Task<SourceFiles> CreateAsync(
            string sourceFileName = "source.usda", byte[]? sourceBytes = null)
        {
            // Windows resolves the machine temp directory through a junction on some
            // hosts. The publication guard intentionally rejects path aliases, so
            // keep fixtures under the already-resolved test output directory.
            string directoryPath = Path.Combine(
                AppContext.BaseDirectory, $"viewer-source-publish-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            sourceBytes ??= "#usda 1.0\n# original source opinion\n"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Combine(directoryPath, sourceFileName), sourceBytes);
            return new SourceFiles(directoryPath, sourceFileName, sourceBytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
