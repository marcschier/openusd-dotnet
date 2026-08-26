// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Mcp;

public sealed class FinalizationService
{
    private const int FileBufferSize = 64 * 1024;
    private const string FinalStageRelativePath = "final-stage.usda";
    private const string FinalizationsDirectoryName = "finalizations";
    private const string JsonReportRelativePath = "analysis-report.json";
    private const string ManifestRelativePath = "finalization-manifest.json";
    private const string MarkdownReportRelativePath = "analysis-report.md";
    private const string OverlayRelativePath = "overlay.usda";
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly IArtifactResourceStore _artifactStore;
    private readonly int _maximumTurntableFrames;
    private readonly McpSessionWorkspace _workspace;

    public FinalizationService(
        McpSessionWorkspace workspace,
        IArtifactResourceStore artifactStore,
        FinalizationOptions? options = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        FinalizationOptions resolvedOptions = options ?? new FinalizationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            resolvedOptions.MaximumTurntableFrames);
        _maximumTurntableFrames = resolvedOptions.MaximumTurntableFrames;
    }

    public async ValueTask<FinalizationResult> FinalizeAsync(
        FinalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePreviewOutputs(request.PreviewOutputs);

        WorkspaceSessionSnapshot session = await _workspace.GetSnapshotAsync(
                request.Revision,
                cancellationToken)
            .ConfigureAwait(false);
        string sessionOutputDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(session.Session.OutputDirectory));
        WorkspacePathContainment.RejectReparsePoints(
            sessionOutputDirectory,
            sessionOutputDirectory);

        string revisionDirectory = CreateRevisionDirectory(
            sessionOutputDirectory,
            session.Session.Generation,
            session.Session.StageRevision);
        string? stagingDirectory = WorkspacePathContainment.CreateContainedDirectory(
            sessionOutputDirectory,
            Path.Combine(
                sessionOutputDirectory,
                string.Concat(".staging-", Guid.NewGuid().ToString("N"))));
        try
        {
            var artifacts = new List<FinalizationArtifactRecord>();
            var failures = new List<FinalizationFailure>();
            bool finalStageCreated = false;
            string stagingFinalStagePath = Path.Combine(
                stagingDirectory,
                FinalStageRelativePath);
            try
            {
                WorkspaceFinalStageResult export = await _workspace.ExportFinalStageAsync(
                        request.Revision,
                        Path.GetRelativePath(
                            sessionOutputDirectory,
                            stagingFinalStagePath),
                        cancellationToken)
                    .ConfigureAwait(false);
                session = export.Snapshot;
                if (!string.Equals(
                        Path.GetFullPath(export.FinalStagePath),
                        stagingFinalStagePath,
                        PathComparison))
                {
                    throw new InvalidOperationException(
                        "The workspace exported the final stage to an unexpected path.");
                }

                WorkspacePathContainment.RejectReparsePoints(
                    sessionOutputDirectory,
                    stagingFinalStagePath);
                if (File.Exists(stagingFinalStagePath))
                {
                    artifacts.Add(await CreateFileRecordAsync(
                            "final-stage",
                            stagingDirectory,
                            stagingFinalStagePath,
                            "model/vnd.usda",
                            cancellationToken)
                        .ConfigureAwait(false));
                    finalStageCreated = true;
                }
                else
                {
                    AddFailure(
                        artifacts,
                        failures,
                        "final-stage",
                        FinalStageRelativePath,
                        "model/vnd.usda",
                        "The expected file was not created.");
                }
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException and
                not WorkspacePathContainmentException)
            {
                DeleteFileIfPresent(
                    sessionOutputDirectory,
                    stagingFinalStagePath);
                AddFailure(
                    artifacts,
                    failures,
                    "final-stage",
                    FinalStageRelativePath,
                    "model/vnd.usda",
                    exception.Message);
            }

            await PersistExistingFileAsync(
                    "overlay",
                    sessionOutputDirectory,
                    session.Session.OverlayPath,
                    OverlayRelativePath,
                    stagingDirectory,
                    artifacts,
                    failures,
                    cancellationToken)
                .ConfigureAwait(false);

            await PersistPreviewAsync(
                    request.PreviewOutputs.HeroStill,
                    0,
                    "hero-still",
                    "presentation/hero-still.png",
                    sessionOutputDirectory,
                    stagingDirectory,
                    artifacts,
                    failures,
                    cancellationToken)
                .ConfigureAwait(false);
            await PersistPreviewAsync(
                    request.PreviewOutputs.ContactSheet,
                    0,
                    "contact-sheet",
                    "presentation/contact-sheet.png",
                    sessionOutputDirectory,
                    stagingDirectory,
                    artifacts,
                    failures,
                    cancellationToken)
                .ConfigureAwait(false);
            if (request.PreviewOutputs.Turntable is { Artifacts.Count: > 0 } turntable)
            {
                for (int index = 0; index < turntable.Artifacts.Count; index++)
                {
                    await PersistPreviewAsync(
                            turntable,
                            index,
                            "turntable-frame",
                            FormattableString.Invariant(
                                $"presentation/turntable/frame-{index:D2}.png"),
                            sessionOutputDirectory,
                            stagingDirectory,
                            artifacts,
                            failures,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                AddFailure(
                    artifacts,
                    failures,
                    "turntable",
                    "presentation/turntable",
                    "image/png",
                    "No preview output was supplied.");
            }

            byte[] jsonReport = FinalizationReportWriter.CreateAnalysisJson(
                session,
                request.Analysis,
                artifacts);
            byte[] markdownReport = FinalizationReportWriter.CreateMarkdown(
                session,
                request.Analysis,
                artifacts);
            string stagingJsonReportPath = Path.Combine(
                stagingDirectory,
                JsonReportRelativePath);
            string stagingMarkdownReportPath = Path.Combine(
                stagingDirectory,
                MarkdownReportRelativePath);
            await WriteAtomicAsync(
                    sessionOutputDirectory,
                    stagingJsonReportPath,
                    jsonReport,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteAtomicAsync(
                    sessionOutputDirectory,
                    stagingMarkdownReportPath,
                    markdownReport,
                    cancellationToken)
                .ConfigureAwait(false);
            FinalizationArtifactRecord jsonReportRecord = CreateContentRecord(
                "analysis-json",
                JsonReportRelativePath,
                "application/json",
                jsonReport,
                null);
            ArtifactResourceDescriptor? jsonReportResource =
                await TryPublishFileAsync(
                        CreateArtifactId(
                            session.Session,
                            "analysis-report",
                            "json",
                            jsonReport),
                        "analysis-json",
                        "application/json",
                        stagingJsonReportPath,
                        jsonReportRecord.ByteLength!.Value,
                        jsonReportRecord.Sha256!,
                        failures,
                        cancellationToken)
                    .ConfigureAwait(false);
            FinalizationArtifactRecord markdownReportRecord = CreateContentRecord(
                "analysis-markdown",
                MarkdownReportRelativePath,
                "text/markdown",
                markdownReport,
                null);
            ArtifactResourceDescriptor? markdownReportResource =
                await TryPublishFileAsync(
                        CreateArtifactId(
                            session.Session,
                            "analysis-report",
                            "md",
                            markdownReport),
                        "analysis-markdown",
                        "text/markdown",
                        stagingMarkdownReportPath,
                        markdownReportRecord.ByteLength!.Value,
                        markdownReportRecord.Sha256!,
                        failures,
                        cancellationToken)
                    .ConfigureAwait(false);
            artifacts.Add(jsonReportRecord with
            {
                ResourceUri = jsonReportResource?.ResourceUri,
            });
            artifacts.Add(markdownReportRecord with
            {
                ResourceUri = markdownReportResource?.ResourceUri,
            });

            string stagingManifestPath = Path.Combine(
                stagingDirectory,
                ManifestRelativePath);
            byte[] manifest = FinalizationReportWriter.CreateManifestJson(
                session,
                artifacts,
                failures);
            await WriteAtomicAsync(
                    sessionOutputDirectory,
                    stagingManifestPath,
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            ArtifactResourceDescriptor? manifestResource =
                await TryPublishFileAsync(
                        CreateArtifactId(
                            session.Session,
                            "finalization-manifest",
                            "json",
                            manifest),
                        "manifest",
                        "application/json",
                        stagingManifestPath,
                        manifest.LongLength,
                        Hash(manifest),
                        failures,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (manifestResource is null)
            {
                manifest = FinalizationReportWriter.CreateManifestJson(
                    session,
                    artifacts,
                    failures);
                await WriteAtomicAsync(
                        sessionOutputDirectory,
                        stagingManifestPath,
                        manifest,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            string publicationId = Convert.ToHexString(SHA256.HashData(manifest))
                .ToLowerInvariant();
            string publishedDirectory = await PublishStagingDirectoryAsync(
                    sessionOutputDirectory,
                    revisionDirectory,
                    stagingDirectory,
                    publicationId,
                    manifest,
                    artifacts,
                    cancellationToken)
                .ConfigureAwait(false);
            stagingDirectory = null;

            string? finalStagePath = finalStageCreated
                ? Path.Combine(publishedDirectory, FinalStageRelativePath)
                : null;
            return new FinalizationResult(
                session,
                publishedDirectory,
                finalStagePath,
                Path.Combine(publishedDirectory, ManifestRelativePath),
                Path.Combine(publishedDirectory, JsonReportRelativePath),
                Path.Combine(publishedDirectory, MarkdownReportRelativePath),
                Array.AsReadOnly(
                    artifacts.OrderBy(static item => item.Role, StringComparer.Ordinal)
                        .ThenBy(static item => item.RelativePath, StringComparer.Ordinal)
                        .ToArray()),
                Array.AsReadOnly(
                    failures.OrderBy(static item => item.Role, StringComparer.Ordinal)
                        .ThenBy(static item => item.Message, StringComparer.Ordinal)
                        .ToArray()),
                manifestResource,
                jsonReportResource,
                markdownReportResource);
        }
        finally
        {
            if (stagingDirectory is not null && Directory.Exists(stagingDirectory))
            {
                DeleteOwnedDirectory(sessionOutputDirectory, stagingDirectory);
            }
        }
    }

    private static void AddFailure(
        List<FinalizationArtifactRecord> artifacts,
        List<FinalizationFailure> failures,
        string role,
        string relativePath,
        string mediaType,
        string message)
    {
        failures.Add(new FinalizationFailure(role, message));
        artifacts.Add(new FinalizationArtifactRecord(
            role,
            NormalizeRelativePath(relativePath),
            mediaType,
            FinalizationArtifactStatus.Failed,
            null,
            null,
            null,
            message));
    }

    private static string CreateRevisionDirectory(
        string outputDirectory,
        long generation,
        ulong stageRevision)
    {
        string relativePath = Path.Combine(
            FinalizationsDirectoryName,
            FormattableString.Invariant($"generation-{generation:D20}"),
            FormattableString.Invariant($"revision-{stageRevision:D20}"));
        string directory = WorkspacePathContainment.ResolveContainedPath(
            outputDirectory,
            relativePath);
        return WorkspacePathContainment.CreateContainedDirectory(
            outputDirectory,
            directory);
    }

    private static FinalizationArtifactRecord CreateContentRecord(
        string role,
        string relativePath,
        string mediaType,
        ReadOnlySpan<byte> content,
        Uri? resourceUri) =>
        new(
            role,
            NormalizeRelativePath(relativePath),
            mediaType,
            FinalizationArtifactStatus.Created,
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            resourceUri,
            null);

    private static async ValueTask<FinalizationArtifactRecord> CreateFileRecordAsync(
        string role,
        string outputDirectory,
        string path,
        string mediaType,
        CancellationToken cancellationToken)
    {
        FileContentMetadata metadata = await ReadFileMetadataAsync(
                outputDirectory,
                path,
                cancellationToken)
            .ConfigureAwait(false);
        return new FinalizationArtifactRecord(
            role,
            NormalizeRelativePath(Path.GetRelativePath(outputDirectory, path)),
            mediaType,
            FinalizationArtifactStatus.Created,
            metadata.ByteLength,
            metadata.Sha256,
            null,
            null);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static string CreateArtifactId(
        WorkspaceSessionInfo session,
        string role,
        string extension,
        ReadOnlySpan<byte> content) =>
        string.Concat(
            session.SessionId,
            "-generation-",
            session.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-revision-",
            session.StageRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-",
            role,
            "-",
            Hash(content),
            ".",
            extension);

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static void DeleteFileIfPresent(string root, string path)
    {
        WorkspacePathContainment.CreateContainedDirectory(
            root,
            Path.GetDirectoryName(path)!);
        WorkspacePathContainment.RejectReparsePoints(root, path);
        if (File.Exists(path))
        {
            File.Delete(path);
            WorkspacePathContainment.RejectReparsePoints(root, Path.GetDirectoryName(path)!);
        }
    }

    private static void DeleteOwnedDirectory(string root, string path)
    {
        WorkspacePathContainment.CreateContainedDirectory(root, path);
        WorkspacePathContainment.RejectReparsePoints(root, path);
        RejectTreeReparsePoints(root, path);
        Directory.Delete(path, recursive: true);
        WorkspacePathContainment.RejectReparsePoints(root, Path.GetDirectoryName(path)!);
    }

    private static async ValueTask<string> PublishStagingDirectoryAsync(
        string sessionOutputDirectory,
        string revisionDirectory,
        string stagingDirectory,
        string publicationId,
        ReadOnlyMemory<byte> manifest,
        IReadOnlyList<FinalizationArtifactRecord> artifacts,
        CancellationToken cancellationToken)
    {
        await VerifyPublishedDirectoryAsync(
                stagingDirectory,
                manifest,
                artifacts,
                cancellationToken)
            .ConfigureAwait(false);
        WorkspacePathContainment.CreateContainedDirectory(
            sessionOutputDirectory,
            revisionDirectory);
        string publishedDirectory = Path.Combine(
            revisionDirectory,
            string.Concat("finalization-", publicationId));
        WorkspacePathContainment.RejectReparsePoints(
            sessionOutputDirectory,
            publishedDirectory);
        if (Directory.Exists(publishedDirectory))
        {
            WorkspacePathContainment.CreateContainedDirectory(
                sessionOutputDirectory,
                publishedDirectory);
            await VerifyPublishedDirectoryAsync(
                    publishedDirectory,
                    manifest,
                    artifacts,
                    cancellationToken)
                .ConfigureAwait(false);
            DeleteOwnedDirectory(sessionOutputDirectory, stagingDirectory);
            return publishedDirectory;
        }

        if (File.Exists(publishedDirectory))
        {
            throw new IOException(
                $"A file blocks finalization publication at '{publishedDirectory}'.");
        }

        WorkspacePathContainment.RejectReparsePoints(
            sessionOutputDirectory,
            stagingDirectory);
        WorkspacePathContainment.RejectReparsePoints(
            sessionOutputDirectory,
            publishedDirectory);
        Directory.Move(stagingDirectory, publishedDirectory);
        WorkspacePathContainment.CreateContainedDirectory(
            sessionOutputDirectory,
            publishedDirectory);
        await VerifyPublishedDirectoryAsync(
                publishedDirectory,
                manifest,
                artifacts,
                cancellationToken)
            .ConfigureAwait(false);
        return publishedDirectory;
    }

    private static void RejectTreeReparsePoints(string root, string directory)
    {
        WorkspacePathContainment.RejectReparsePoints(root, directory);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new WorkspacePathContainmentException(
                    $"Reparse points are not allowed in finalization outputs: '{entry}'.");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                RejectTreeReparsePoints(root, entry);
            }
        }
    }

    private static async ValueTask PersistExistingFileAsync(
        string role,
        string sessionOutputDirectory,
        string sourcePath,
        string relativePath,
        string stagingDirectory,
        List<FinalizationArtifactRecord> artifacts,
        List<FinalizationFailure> failures,
        CancellationToken cancellationToken)
    {
        WorkspacePathContainment.RejectReparsePoints(
            sessionOutputDirectory,
            sourcePath);
        if (!File.Exists(sourcePath))
        {
            AddFailure(
                artifacts,
                failures,
                role,
                relativePath,
                "model/vnd.usda",
                "The expected file was not created.");
            return;
        }

        string destinationPath = Path.Combine(
            stagingDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        FileContentMetadata metadata = await CopyFileAndHashAsync(
                sessionOutputDirectory,
                sourcePath,
                destinationPath,
                cancellationToken)
            .ConfigureAwait(false);
        artifacts.Add(new FinalizationArtifactRecord(
            role,
            NormalizeRelativePath(relativePath),
            "model/vnd.usda",
            FinalizationArtifactStatus.Created,
            metadata.ByteLength,
            metadata.Sha256,
            null,
            null));
    }

    private static async ValueTask VerifyPublishedDirectoryAsync(
        string directory,
        ReadOnlyMemory<byte> manifest,
        IReadOnlyList<FinalizationArtifactRecord> artifacts,
        CancellationToken cancellationToken)
    {
        string canonicalDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directory));
        WorkspacePathContainment.CreateDirectorySafely(canonicalDirectory);
        WorkspacePathContainment.RejectReparsePoints(
            canonicalDirectory,
            canonicalDirectory);
        RejectTreeReparsePoints(canonicalDirectory, canonicalDirectory);

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            ManifestRelativePath,
        };
        foreach (FinalizationArtifactRecord artifact in artifacts)
        {
            if (artifact.Status == FinalizationArtifactStatus.Created &&
                !expectedFiles.Add(artifact.RelativePath))
            {
                throw new IOException(
                    $"Finalization contains duplicate output path '{artifact.RelativePath}'.");
            }
        }

        var actualFiles = new HashSet<string>(
            Directory.EnumerateFiles(
                    canonicalDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(
                    Path.GetRelativePath(canonicalDirectory, path))),
            StringComparer.Ordinal);
        if (!actualFiles.SetEquals(expectedFiles))
        {
            throw new IOException(
                $"Finalization output '{canonicalDirectory}' is incomplete or contains stale files.");
        }

        string manifestPath = Path.Combine(
            canonicalDirectory,
            ManifestRelativePath);
        WorkspacePathContainment.RejectReparsePoints(
            canonicalDirectory,
            manifestPath);
        FileContentMetadata manifestMetadata = await ReadFileMetadataAsync(
                canonicalDirectory,
                manifestPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (manifestMetadata.ByteLength != manifest.Length ||
            !string.Equals(
                manifestMetadata.Sha256,
                Hash(manifest.Span),
                StringComparison.Ordinal))
        {
            throw new IOException(
                $"Finalization manifest '{manifestPath}' does not match its publication ID.");
        }

        foreach (FinalizationArtifactRecord artifact in artifacts)
        {
            if (artifact.Status != FinalizationArtifactStatus.Created)
            {
                continue;
            }

            string artifactPath = WorkspacePathContainment.ResolveContainedPath(
                canonicalDirectory,
                artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            WorkspacePathContainment.RejectReparsePoints(
                canonicalDirectory,
                artifactPath);
            FileContentMetadata metadata = await ReadFileMetadataAsync(
                    canonicalDirectory,
                    artifactPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (artifact.ByteLength != metadata.ByteLength ||
                !string.Equals(
                    artifact.Sha256,
                    metadata.Sha256,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    $"Finalization artifact '{artifact.RelativePath}' failed integrity validation.");
            }
        }
    }

    private static async ValueTask<FileContentMetadata> ReadFileMetadataAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        WorkspacePathContainment.RejectReparsePoints(root, path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The finalization artifact does not exist.",
                path);
        }

        long expectedLength = file.Length;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedLength)
        {
            throw new IOException(
                $"Finalization artifact '{path}' changed before it could be hashed.");
        }

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        if (stream.Position != expectedLength || stream.Length != expectedLength)
        {
            throw new IOException(
                $"Finalization artifact '{path}' changed while it was hashed.");
        }

        WorkspacePathContainment.RejectReparsePoints(root, path);
        return new FileContentMetadata(
            expectedLength,
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static async ValueTask<FileContentMetadata> CopyFileAndHashAsync(
        string root,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        WorkspacePathContainment.RejectReparsePoints(root, sourcePath);
        WorkspacePathContainment.CreateContainedDirectory(
            root,
            Path.GetDirectoryName(destinationPath)!);
        WorkspacePathContainment.RejectReparsePoints(root, destinationPath);
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException(
                "The finalization source artifact does not exist.",
                sourcePath);
        }

        long expectedLength = sourceInfo.Length;
        string temporaryPath = string.Concat(destinationPath, ".new");
        WorkspacePathContainment.RejectReparsePoints(root, temporaryPath);
        bool moved = false;
        try
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != expectedLength)
            {
                throw new IOException(
                    $"Finalization source artifact '{sourcePath}' changed before it could be copied.");
            }

            string hash;
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             FileBufferSize,
                             FileOptions.Asynchronous |
                             FileOptions.SequentialScan |
                             FileOptions.WriteThrough))
            {
                using SHA256 hashAlgorithm = SHA256.Create();
                using (var hashingStream = new CryptoStream(
                           destination,
                           hashAlgorithm,
                           CryptoStreamMode.Write,
                           leaveOpen: true))
                {
                    await source.CopyToAsync(
                            hashingStream,
                            FileBufferSize,
                            cancellationToken)
                        .ConfigureAwait(false);
                    hashingStream.FlushFinalBlock();
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
                if (source.Position != expectedLength ||
                    source.Length != expectedLength ||
                    destination.Length != expectedLength)
                {
                    throw new IOException(
                        $"Finalization source artifact '{sourcePath}' changed while it was copied.");
                }

                hash = Convert.ToHexString(hashAlgorithm.Hash!)
                    .ToLowerInvariant();
            }

            WorkspacePathContainment.RejectReparsePoints(root, temporaryPath);
            WorkspacePathContainment.RejectReparsePoints(root, destinationPath);
            File.Move(temporaryPath, destinationPath);
            moved = true;
            WorkspacePathContainment.RejectReparsePoints(root, destinationPath);
            return new FileContentMetadata(expectedLength, hash);
        }
        catch
        {
            if (moved)
            {
                DeleteFileIfPresent(root, destinationPath);
            }

            throw;
        }
        finally
        {
            DeleteFileIfPresent(root, temporaryPath);
        }
    }

    private static async ValueTask WriteNewFileAsync(
        string root,
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        WorkspacePathContainment.CreateContainedDirectory(
            root,
            Path.GetDirectoryName(path)!);
        WorkspacePathContainment.RejectReparsePoints(root, path);
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            DeleteFileIfPresent(root, path);
            throw;
        }

        WorkspacePathContainment.RejectReparsePoints(root, path);
    }

    private static async ValueTask WriteAtomicAsync(
        string root,
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        WorkspacePathContainment.CreateContainedDirectory(
            root,
            Path.GetDirectoryName(path)!);
        WorkspacePathContainment.RejectReparsePoints(root, path);
        string temporaryPath = string.Concat(path, ".new");
        WorkspacePathContainment.RejectReparsePoints(root, temporaryPath);
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            WorkspacePathContainment.RejectReparsePoints(root, temporaryPath);
            WorkspacePathContainment.RejectReparsePoints(root, path);
            File.Move(temporaryPath, path, overwrite: true);
            WorkspacePathContainment.RejectReparsePoints(root, path);
        }
        finally
        {
            DeleteFileIfPresent(root, temporaryPath);
        }
    }

    private async ValueTask PersistPreviewAsync(
        PreviewCaptureResult? preview,
        int artifactIndex,
        string role,
        string relativePath,
        string sessionOutputDirectory,
        string stagingDirectory,
        List<FinalizationArtifactRecord> artifacts,
        List<FinalizationFailure> failures,
        CancellationToken cancellationToken)
    {
        if (preview is null)
        {
            AddFailure(
                artifacts,
                failures,
                role,
                relativePath,
                "image/png",
                "No preview output was supplied.");
            return;
        }

        if (artifactIndex >= preview.Artifacts.Count)
        {
            AddFailure(
                artifacts,
                failures,
                role,
                relativePath,
                "image/png",
                "The preview output does not contain the requested artifact.");
            return;
        }

        ArtifactResourceDescriptor expected = preview.Artifacts[artifactIndex];
        ArtifactResourceContent? resource = await _artifactStore.ReadAsync(
                expected.ResourceUri,
                cancellationToken)
            .ConfigureAwait(false);
        if (resource is null)
        {
            AddFailure(
                artifacts,
                failures,
                role,
                relativePath,
                expected.MediaType,
                $"Preview resource '{expected.ResourceUri}' is unavailable.");
            return;
        }

        ArtifactResourceDescriptor actual = resource.Descriptor;
        ReadOnlyMemory<byte> content = resource.Content;
        string hash = Hash(content.Span);
        if (actual.ByteLength != content.Length ||
            !string.Equals(actual.Sha256, hash, StringComparison.Ordinal) ||
            !string.Equals(actual.MediaType, "image/png", StringComparison.Ordinal))
        {
            AddFailure(
                artifacts,
                failures,
                role,
                relativePath,
                expected.MediaType,
                "The preview resource metadata or content hash is inconsistent.");
            return;
        }

        string path = Path.Combine(
            stagingDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        await WriteNewFileAsync(
                sessionOutputDirectory,
                path,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        artifacts.Add(CreateContentRecord(
            role,
            relativePath,
            actual.MediaType,
            content.Span,
            actual.ResourceUri));
    }

    private async ValueTask<ArtifactResourceDescriptor?> TryPublishFileAsync(
        string artifactId,
        string role,
        string mediaType,
        string path,
        long byteLength,
        string sha256,
        List<FinalizationFailure> failures,
        CancellationToken cancellationToken)
    {
        Uri resourceUri = ArtifactResourceUri.Create(artifactId);
        if (_artifactStore.TryGetDescriptor(
                resourceUri,
                out ArtifactResourceDescriptor? existing))
        {
            if (MatchesDescriptor(
                    resourceUri,
                    mediaType,
                    existing,
                    byteLength,
                    sha256))
            {
                return existing;
            }

            failures.Add(new FinalizationFailure(
                role,
                $"Artifact resource publication failed: '{resourceUri}' already contains " +
                "different content."));
            return null;
        }

        try
        {
            return await _artifactStore.AddFileAsync(
                    artifactId,
                    mediaType,
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or IOException)
        {
            if (_artifactStore.TryGetDescriptor(
                    resourceUri,
                    out existing) &&
                MatchesDescriptor(
                    resourceUri,
                    mediaType,
                    existing,
                    byteLength,
                    sha256))
            {
                return existing;
            }

            failures.Add(new FinalizationFailure(
                role,
                $"Artifact resource publication failed: {exception.Message}"));
            return null;
        }
    }

    private static bool MatchesDescriptor(
        Uri resourceUri,
        string mediaType,
        ArtifactResourceDescriptor? descriptor,
        long byteLength,
        string sha256) =>
        descriptor is not null &&
            descriptor.ByteLength == byteLength &&
            descriptor.ResourceUri == resourceUri &&
            string.Equals(descriptor.MediaType, mediaType, StringComparison.Ordinal) &&
            string.Equals(descriptor.Sha256, sha256, StringComparison.Ordinal);

    private void ValidatePreviewOutputs(FinalizationPreviewOutputs outputs)
    {
        if (outputs.HeroStill is not null &&
            outputs.HeroStill.Kind != CaptureKind.Still)
        {
            throw new ArgumentException(
                "The hero still output must come from a still capture.",
                nameof(outputs));
        }

        if (outputs.ContactSheet is not null &&
            outputs.ContactSheet.Kind != CaptureKind.ContactSheet)
        {
            throw new ArgumentException(
                "The contact sheet output must come from a contact-sheet capture.",
                nameof(outputs));
        }

        if (outputs.Turntable is not null)
        {
            if (outputs.Turntable.Kind != CaptureKind.Turntable)
            {
                throw new ArgumentException(
                    "Turntable outputs must come from a turntable capture.",
                    nameof(outputs));
            }

            if (outputs.Turntable.Artifacts.Count > _maximumTurntableFrames)
            {
                throw new ArgumentException(
                    $"Turntable outputs may contain at most {_maximumTurntableFrames} frames.",
                    nameof(outputs));
            }
        }
    }

    private sealed record FileContentMetadata(long ByteLength, string Sha256);
}
