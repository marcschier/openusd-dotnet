// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using OpenUsd.Editing;

namespace OpenUsd.NativeProbe;

internal static class ReviewDocumentProbe
{
    private const string SourceText =
        """
        #usda 1.0
        (
            defaultPrim = "World"
            startTimeCode = 2
            endTimeCode = 24
            framesPerSecond = 48
            timeCodesPerSecond = 120
            customLayerData = { string marker = "original portable review source" }
            subLayers = [@sublayer.usda@]
        )
        def Xform "World"
        {
            double weight = 7
            asset texture = @texture.bin@
        }
        def Xform "OnlySource" {}
        """;
    private const string SublayerText = "#usda 1.0\ndef Xform \"FromSublayer\" {}\n";
    private const string AssetText = "portable-review-original-concrete-asset";

    internal static bool IsMode(string mode) =>
        mode is "--review-document" or "--review-document-source" or "--review-document-reader" or
            "--review-document-import";

    internal static void Run(string mode, string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        switch (mode)
        {
            case "--review-document":
                fullDirectory = Path.Combine(fullDirectory, $"same-process-{Guid.NewGuid():N}");
                RunSource(fullDirectory);
                RunReader(fullDirectory);
                RunImport(fullDirectory);
                Console.WriteLine($"PORTABLE_REVIEW_SAME_PROCESS_OK: {fullDirectory}");
                break;
            case "--review-document-source":
                RunSource(fullDirectory);
                break;
            case "--review-document-reader":
                RunReader(fullDirectory);
                break;
            case "--review-document-import":
                RunImport(fullDirectory);
                break;
            default:
                throw new ArgumentException("Unknown portable review probe mode.", nameof(mode));
        }
    }

    private static void RunSource(string directory)
    {
        Directory.CreateDirectory(directory);
        WriteNew(Path.Combine(directory, "source.usda"), Encoding.UTF8.GetBytes(SourceText));
        WriteNew(Path.Combine(directory, "sublayer.usda"), Encoding.UTF8.GetBytes(SublayerText));
        WriteNew(Path.Combine(directory, "texture.bin"), Encoding.UTF8.GetBytes(AssetText));
        string sourcePath = Path.Combine(directory, "source.usda");
        string documentPath = DocumentPath(directory);
        UsdReviewDocument captured;
        using (UsdStage stage = UsdStage.OpenForReview(sourcePath))
        {
            UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
            Require(source.Dependencies.Any(dependency =>
                Path.GetFileName(dependency.Path) == "sublayer.usda" &&
                dependency.Kind == UsdReviewDependencyKind.Layer), "Source binding lost its sublayer.");
            Require(source.Dependencies.Any(dependency =>
                Path.GetFileName(dependency.Path) == "texture.bin" &&
                dependency.Kind == UsdReviewDependencyKind.Asset), "Source binding lost its concrete asset.");
            using UsdLayer review = stage.GetUserReviewLayer();
            AuthorReview(review);
            captured = review.CaptureReviewDocument(source, documentPath);
            Require(!File.Exists(documentPath), "Native capture published a file.");
            Require(captured.SourceRootPath.Length != 0 && captured.SourceFingerprint.Length == 64 &&
                captured.AssetAnchor.Length != 0, "Capture omitted verified provenance.");
            byte[] bytes = captured.CopyBytes();
            Require(bytes.Length == captured.ByteLength && bytes.Length > 0, "Capture returned empty portable bytes.");
            captured.CopyBytes().AsSpan().Clear();
            Require(bytes.AsSpan().SequenceEqual(captured.CopyBytes()), "Capture exposed mutable portable storage.");
            PublishNew(documentPath, bytes);
            Require(review.AcknowledgeSaved(captured) && !review.GetEditingState().IsDirty,
                "Published capture was not conditionally acknowledged.");
            UsdReviewDocument read = UsdReviewDocument.Read(File.ReadAllBytes(documentPath), sourcePath);
            Require(captured.HasSamePayload(read) && !review.AcknowledgeSaved(read),
                "Reading changed portable bytes or manufactured a save receipt.");
        }
        Require(captured.CopyBytes().Length == captured.ByteLength, "Detached capture did not survive stage disposal.");
        VerifyOriginals(directory);
        Console.WriteLine($"PORTABLE_REVIEW_SOURCE_OK: {documentPath}");
    }

    private static void RunReader(string directory)
    {
        string sourcePath = Path.Combine(directory, "source.usda");
        byte[] bytes = File.ReadAllBytes(DocumentPath(directory));
        UsdReviewDocumentInfo inspection;
        using (var unavailableSource = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            inspection = UsdReviewDocument.Inspect(bytes);
        }
        Require(inspection.DocumentByteLength == bytes.Length &&
            string.Equals(Path.GetFullPath(inspection.SourceRootPath), Path.GetFullPath(sourcePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
            "Inspection did not discover the caller-approved source without source access.");
        UsdReviewDocument document = UsdReviewDocument.Read(bytes, sourcePath);
        bytes.AsSpan().Clear();
        Require(document.ByteLength > 0 && document.Version == 1 &&
            Guid.TryParseExact(document.DocumentId, "D", out _), "Read lost document metadata.");
        using (UsdStage stage = UsdStage.OpenForReview(sourcePath))
        using (UsdLayer review = stage.GetUserReviewLayer())
        {
            Require(!review.AcknowledgeSaved(document), "A reader created a same-process save receipt.");
            Require(review.CaptureAuthored(Addresses()).Opinions.All(opinion =>
                opinion.PropertyKind == UsdLayerPropertyKind.Absent), "Reading imported review opinions.");
            VerifyComposition(stage, expectedWeight: 7);
        }
        VerifyOriginals(directory);
        Console.WriteLine($"PORTABLE_REVIEW_READER_OK: metadata-only inspection, explicit read, {document.DocumentId}");
    }

    private static void RunImport(string directory)
    {
        string sourcePath = Path.Combine(directory, "source.usda");
        UsdReviewDocument document = UsdReviewDocument.Read(File.ReadAllBytes(DocumentPath(directory)), sourcePath);
        using (UsdStage stage = UsdStage.OpenForReview(sourcePath))
        {
            UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
            UsdReviewDocumentImportResult result = stage.ImportReviewDocument(document, source);
            Require(result.Outcome == UsdLayerEditOutcome.Applied && result.AfterState is not null,
                $"Portable import failed: {result.Outcome}: {result.Diagnostic}");
            using UsdLayer review = stage.GetUserReviewLayer();
            Require(result.AfterState!.Identity == review.GetEditingState().Identity &&
                result.AfterState.CanAttemptAuthoredEdits, "Import did not return its actual new history target.");
            VerifyOpinions(review.CaptureAuthored(Addresses()));
            VerifyComposition(stage, expectedWeight: 47);
            Require(!review.AcknowledgeSaved(document), "Imported bytes manufactured a save receipt.");
            UsdLayerAuthoredSnapshot before = review.CaptureAuthored(Addresses());
            UsdReviewDocumentImportResult repeated = stage.ImportReviewDocument(document, source);
            Require(repeated.Outcome != UsdLayerEditOutcome.Applied && repeated.AfterState is null &&
                before.HasSamePayload(review.CaptureAuthored(Addresses())),
                "Repeated import replaced non-pristine review data.");
        }
        VerifyOriginals(directory);
        Console.WriteLine(
            "PORTABLE_REVIEW_IMPORT_OK: exact typed opinions, asset anchor, new history, " +
            "original root metadata and files");
    }

    private static void AuthorReview(UsdLayer review)
    {
        UsdLayerEditAddress[] addresses = Addresses();
        UsdLayerEditResult result = review.CompareAndApply(review.CaptureAuthored(addresses),
        [
            UsdLayerEdit.Set(addresses[0], UsdLayerEditValue.FromDouble(47), "double"),
            UsdLayerEdit.Set(addresses[1], UsdLayerEditValue.FromTokenArray(["first", "caf\u00e9"]), "token[]"),
            UsdLayerEdit.Block(addresses[2], "double"),
            UsdLayerEdit.Set(addresses[3], UsdLayerEditValue.FromPathList(new UsdPathListEdit(false,
                addedItems: ["/Added"], prependedItems: ["/Prepended"], appendedItems: ["/Appended"],
                deletedItems: ["/Deleted"], orderedItems: ["/Ordered"]))),
            UsdLayerEdit.Set(addresses[4], UsdLayerEditValue.FromAssetPath("texture.bin"), "asset"),
            UsdLayerEdit.Set(addresses[5], UsdLayerEditValue.FromDouble(-0.0), "double")
        ]);
        Require(result.Outcome == UsdLayerEditOutcome.Applied && result.AfterSnapshot is not null,
            $"Probe authoring failed: {result.Outcome}: {result.Diagnostic}");
        VerifyOpinions(result.AfterSnapshot!);
    }

    private static UsdLayerEditAddress[] Addresses() =>
    [
        new("/World.weight", UsdLayerEditField.Default),
        new("/World.tokens", UsdLayerEditField.Default),
        new("/World.weight", UsdLayerEditField.TimeSample, 1.25),
        new("/World.targets", UsdLayerEditField.RelationshipTargets),
        new("/World.texture", UsdLayerEditField.Default),
        new("/World.bits", UsdLayerEditField.Default)
    ];

    private static void VerifyOpinions(UsdLayerAuthoredSnapshot snapshot)
    {
        Require(snapshot.Opinions.Count == 6 && snapshot.Opinions[0].Value.AsDouble() == 47 &&
            snapshot.Opinions[0].TypeName == "double", "Portable review lost the authored double/declaration.");
        Require(snapshot.Opinions[1].Value.AsTokenArray().SequenceEqual(["first", "caf\u00e9"]),
            "Portable review lost Unicode token-array contents.");
        Require(snapshot.Opinions[2].Value.Kind == UsdLayerEditValueKind.Block &&
            snapshot.Addresses[2].TimeCode == 1.25, "Portable review lost an exact-time block.");
        UsdPathListEdit list = snapshot.Opinions[3].Value.AsPathList();
        Require(!list.IsExplicit && list.AddedItems.SequenceEqual(["/Added"]) &&
            list.PrependedItems.SequenceEqual(["/Prepended"]) && list.AppendedItems.SequenceEqual(["/Appended"]) &&
            list.DeletedItems.SequenceEqual(["/Deleted"]) && list.OrderedItems.SequenceEqual(["/Ordered"]),
            "Portable review lost list-op buckets.");
        Require(snapshot.Opinions[4].Value.AsAssetPath().AuthoredPath == "texture.bin",
            "Portable review rewrote its relative asset.");
        Require(BitConverter.DoubleToUInt64Bits(snapshot.Opinions[5].Value.AsDouble()) == 0x8000000000000000ul,
            "Portable review lost signed-zero IEEE bits.");
    }

    private static void VerifyComposition(UsdStage stage, double expectedWeight)
    {
        Require(stage.HasPrim("/OnlySource") && stage.HasPrim("/FromSublayer") &&
            stage.GetPrim("/World").GetDouble("weight") == expectedWeight,
            "Review composition lost original source or sublayer content.");
        Require(stage.GetDefaultPrim().Path == "/World" && stage.StartTimeCode == 2 && stage.EndTimeCode == 24 &&
            stage.FramesPerSecond == 48 && stage.TimeCodesPerSecond == 120, "Original root metadata changed.");
        using UsdLayer root = stage.GetRootLayer();
        Require(root.GetMetadataString("marker") == "original portable review source" &&
            root.CaptureAuthored([Addresses()[0]]).Opinions[0].Value.AsDouble() == 7,
            "Review was flattened into or replaced the source root.");
    }

    private static void VerifyOriginals(string directory)
    {
        VerifyFile(Path.Combine(directory, "source.usda"), SourceText);
        VerifyFile(Path.Combine(directory, "sublayer.usda"), SublayerText);
        VerifyFile(Path.Combine(directory, "texture.bin"), AssetText);
    }

    private static void VerifyFile(string path, string original)
    {
        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(original));
        byte[] actual = SHA256.HashData(File.ReadAllBytes(path));
        Require(actual.AsSpan().SequenceEqual(expected), $"Original source/dependency was modified: {path}");
    }

    private static string DocumentPath(string directory) => Path.Combine(directory, "published", "review.urd");

    private static void WriteNew(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void PublishNew(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string stagingPath = $"{path}.{Guid.NewGuid():N}.publish";
        bool ownsStaging = false;
        try
        {
            using (var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsStaging = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(stagingPath, path);
            ownsStaging = false;
        }
        finally
        {
            if (ownsStaging)
            {
                File.Delete(stagingPath);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
