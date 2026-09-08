// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdReviewDocumentNativeTests
{
    [Test]
    public async Task InspectionDiscoversSourceWithoutFilesThenUsesExplicitReadAndImport()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        UsdReviewDocument captured;
        using (UsdStage stage = UsdStage.OpenForReview(files.SourcePath))
        using (UsdLayer review = stage.GetUserReviewLayer())
        {
            _ = AuthorReview(review);
            captured = review.CaptureReviewDocument(stage.CaptureReviewSourceBinding(), files.DocumentPath);
        }
        byte[] bytes = captured.CopyBytes();
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (UsdReviewDependency dependency in captured.Dependencies)
        {
            originals.Add(dependency.Path, await File.ReadAllBytesAsync(dependency.Path));
            File.Delete(dependency.Path);
        }
        UsdReviewDocumentInfo info;
        try
        {
            info = UsdReviewDocument.Inspect(bytes);
            await Assert.That(info.DocumentId).IsEqualTo(captured.DocumentId);
            await Assert.That(info.DocumentByteLength).IsEqualTo(bytes.Length);
            await Assert.That(info.SourceRootPath).IsEqualTo(captured.SourceRootPath);
            await Assert.That(info.AssetAnchor).IsEqualTo(captured.AssetAnchor);
            await Assert.That(info.Dependencies.Count).IsEqualTo(captured.Dependencies.Count);
            await Assert.That(originals.Keys.All(path => !File.Exists(path))).IsTrue();
            await Assert.That(File.Exists(files.DocumentPath)).IsFalse();
            UsdStageBoundResultGuard.ThrowIfForbiddenResult(info);
        }
        finally
        {
            foreach ((string path, byte[] content) in originals)
            {
                await File.WriteAllBytesAsync(path, content);
            }
        }
        UsdReviewDocument read = UsdReviewDocument.Read(bytes, info.SourceRootPath);
        using (UsdStage reopened = UsdStage.OpenForReview(info.SourceRootPath))
        using (UsdLayer review = reopened.GetUserReviewLayer())
        {
            await Assert.That(review.AcknowledgeSaved(read)).IsFalse();
            UsdReviewDocumentImportResult imported = reopened.ImportReviewDocument(
                read, reopened.CaptureReviewSourceBinding());
            await Assert.That(imported.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(reopened.GetPrim("/World").GetDouble("weight")).IsEqualTo(47d);
        }
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task InspectionRetainsChecksumAndStrictInputGuardsWithLockedDependencies()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        UsdReviewDocument captured;
        using (UsdStage stage = UsdStage.OpenForReview(files.SourcePath))
        using (UsdLayer review = stage.GetUserReviewLayer())
        {
            _ = AuthorReview(review);
            captured = review.CaptureReviewDocument(stage.CaptureReviewSourceBinding(), files.DocumentPath);
        }
        using var locked = new FileStream(files.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        byte[] bytes = captured.CopyBytes();
        UsdReviewDocumentInfo info = UsdReviewDocument.Inspect(bytes);
        await Assert.That(info.SourceRootPath).IsEqualTo(captured.SourceRootPath);
        foreach (int index in new[] { 0, 4, bytes.Length / 2, bytes.Length - 1 })
        {
            byte[] damaged = (byte[])bytes.Clone();
            damaged[index] ^= 1;
            await Assert.That(() => UsdReviewDocument.Inspect(damaged)).Throws<OpenUsdNativeException>();
        }
        await Assert.That(() => UsdReviewDocument.Inspect(bytes.AsSpan(0, bytes.Length - 1)))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() => UsdReviewDocument.Inspect([.. bytes, 0])).Throws<OpenUsdNativeException>();
    }

    [Test]
    public async Task PublishedReviewReopensAgainstOriginalSourceWithoutFlatteningOrReceiptReplay()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        UsdReviewDocument captured;
        UsdReviewSourceBinding originalSource;
        UsdLayerAuthoredSnapshot originalOpinions;
        using (UsdStage stage = UsdStage.OpenForReview(files.SourcePath))
        using (UsdLayer review = stage.GetUserReviewLayer())
        {
            originalSource = stage.CaptureReviewSourceBinding();
            originalOpinions = AuthorReview(review);
            captured = review.CaptureReviewDocument(originalSource, files.DocumentPath);
            await Assert.That(File.Exists(files.DocumentPath)).IsFalse();
            await Assert.That(review.GetEditingState().IsDirty).IsTrue();
            Directory.CreateDirectory(Path.GetDirectoryName(files.DocumentPath)!);
            await File.WriteAllBytesAsync(files.DocumentPath, captured.CopyBytes());
            await Assert.That(review.AcknowledgeSaved(captured)).IsTrue();
            await Assert.That(review.GetEditingState().IsDirty).IsFalse();
            UsdReviewDocument readWhileOpen = UsdReviewDocument.Read(
                await File.ReadAllBytesAsync(files.DocumentPath), files.SourcePath);
            await Assert.That(captured.HasSamePayload(readWhileOpen)).IsTrue();
            await Assert.That(review.AcknowledgeSaved(readWhileOpen)).IsFalse();
        }

        UsdReviewDocument read = UsdReviewDocument.Read(
            await File.ReadAllBytesAsync(files.DocumentPath), files.SourcePath);
        captured.CopyBytes().AsSpan().Clear();
        using (UsdStage reopened = UsdStage.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding source = reopened.CaptureReviewSourceBinding();
            await Assert.That(originalSource.HasSamePayload(source)).IsFalse();
            UsdReviewDocumentImportResult result = reopened.ImportReviewDocument(read, source);
            await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            using UsdLayer review = reopened.GetUserReviewLayer();
            UsdLayerEditingState actualState = review.GetEditingState();
            await Assert.That(result.AfterState!.Identity).IsEqualTo(actualState.Identity);
            await Assert.That(actualState.Identity.StageId).IsNotEqualTo(originalOpinions.Identity.StageId);
            await Assert.That(actualState.Role).IsEqualTo(UsdLayerRole.UserReview);
            await Assert.That(actualState.CurrentlyLocal).IsTrue();
            await Assert.That(review.AcknowledgeSaved(read)).IsFalse();
            UsdLayerAuthoredSnapshot actual = review.CaptureAuthored(Addresses());
            await AssertOpinionsAsync(actual);
            await Assert.That(reopened.GetPrim("/World").GetDouble("weight")).IsEqualTo(47d);
            await Assert.That(reopened.HasPrim("/OnlySource")).IsTrue();
            await Assert.That(reopened.HasPrim("/FromSublayer")).IsTrue();
            await Assert.That(reopened.GetDefaultPrim().Path).IsEqualTo("/World");
            await Assert.That(reopened.StartTimeCode).IsEqualTo(2d);
            await Assert.That(reopened.EndTimeCode).IsEqualTo(24d);
            await Assert.That(reopened.FramesPerSecond).IsEqualTo(48d);
            await Assert.That(reopened.TimeCodesPerSecond).IsEqualTo(120d);
            using UsdLayer root = reopened.GetRootLayer();
            await Assert.That(root.GetMetadataString("marker")).IsEqualTo("original source");
            await Assert.That(root.CaptureAuthored([Addresses()[0]]).Opinions[0].Value.AsDouble()).IsEqualTo(7d);
            UsdLayerEditResult stale = review.CompareAndRestore(originalOpinions, originalOpinions);
            await Assert.That(stale.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
            await Assert.That(stale.AfterSnapshot).IsNull();
        }
        await files.AssertOriginalsUnchangedAsync();
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(read);
        UsdStageBoundResultGuard.ThrowIfForbiddenResult(originalSource);
    }

    [Test]
    public async Task ReferencesPayloadsAndUnselectedVariantDependenciesRemainBoundToTheirSource()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync(compositionGraph: true);
        UsdReviewDocument document;
        using (UsdStage stage = UsdStage.OpenForReview(files.SourcePath))
        using (UsdLayer review = stage.GetUserReviewLayer())
        {
            UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
            await Assert.That(source.Dependencies.Any(dependency =>
                Path.GetFileName(dependency.Path) == "reference.usda" &&
                dependency.Kind == UsdReviewDependencyKind.Layer)).IsTrue();
            await Assert.That(source.Dependencies.Any(dependency =>
                Path.GetFileName(dependency.Path) == "payload.usda" &&
                dependency.Kind == UsdReviewDependencyKind.Layer)).IsTrue();
            await Assert.That(source.Dependencies.Any(dependency =>
                Path.GetFileName(dependency.Path) == "variant-only.bin" &&
                dependency.Kind == UsdReviewDependencyKind.Asset)).IsTrue();
            _ = AuthorReview(review);
            document = review.CaptureReviewDocument(source, files.DocumentPath);
        }
        using (UsdStage stage = UsdStage.OpenForReview(files.SourcePath))
        {
            UsdReviewDocumentImportResult result = stage.ImportReviewDocument(document, stage.CaptureReviewSourceBinding());
            await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(stage.HasPrim("/Referenced/ReferenceChild")).IsTrue();
            await Assert.That(stage.HasPrim("/Payload/PayloadChild")).IsTrue();
            await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(47d);
        }
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CaptureReceiptDoesNotAcknowledgeInterveningEditsEvenAfterReverting(bool revert)
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        using UsdStage stage = UsdStage.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
        using UsdLayer review = stage.GetUserReviewLayer();
        UsdLayerAuthoredSnapshot before = AuthorReview(review);
        UsdReviewDocument captured = review.CaptureReviewDocument(source, files.DocumentPath);
        var address = new UsdLayerEditAddress("/World.weight", UsdLayerEditField.Default);
        UsdLayerAuthoredSnapshot single = review.CaptureAuthored([address]);
        UsdLayerEditResult changed = review.CompareAndApply(single,
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(99))]);
        await Assert.That(changed.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        if (revert)
        {
            UsdLayerEditResult restored = review.CompareAndRestore(changed.AfterSnapshot!, single);
            await Assert.That(restored.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(restored.AfterSnapshot!.Opinions[0].Value.AsDouble())
                .IsEqualTo(before.Opinions[0].Value.AsDouble());
        }
        await Assert.That(review.AcknowledgeSaved(captured)).IsFalse();
        await Assert.That(review.GetEditingState().IsDirty).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    [Arguments("source.usda")]
    [Arguments("sublayer.usda")]
    [Arguments("texture.bin")]
    public async Task SameTimestampFilesystemChangesPreventCaptureAndSavedAcknowledgement(string changedFile)
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        using UsdStage stage = UsdStage.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
        using UsdLayer review = stage.GetUserReviewLayer();
        _ = AuthorReview(review);
        UsdReviewDocument captured = review.CaptureReviewDocument(source, files.DocumentPath);
        string path = Path.Combine(files.DirectoryPath, changedFile);
        DateTime lastWrite = File.GetLastWriteTimeUtc(path);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        File.SetLastWriteTimeUtc(path, lastWrite);

        await Assert.That(() => review.CaptureReviewDocument(source, files.DocumentPath))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() => review.AcknowledgeSaved(captured)).Throws<OpenUsdNativeException>();
        await Assert.That(review.GetEditingState().IsDirty).IsTrue();
        await Assert.That(File.Exists(files.DocumentPath)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task InvalidPortableBytesAndWrongExplicitSourceCannotBeRead()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        using UsdStage stage = UsdStage.OpenForReview(files.SourcePath);
        using UsdLayer review = stage.GetUserReviewLayer();
        UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
        _ = AuthorReview(review);
        UsdReviewDocument document = review.CaptureReviewDocument(source, files.DocumentPath);
        byte[] bytes = document.CopyBytes();
        await Assert.That(() => UsdReviewDocument.Read(bytes, Path.Combine(files.DirectoryPath, "sublayer.usda")))
            .Throws<OpenUsdNativeException>();
        foreach (int index in new[] { 0, bytes.Length / 2, bytes.Length - 1 })
        {
            byte[] damaged = (byte[])bytes.Clone();
            damaged[index] ^= 1;
            await Assert.That(() => UsdReviewDocument.Read(damaged, files.SourcePath)).Throws<OpenUsdNativeException>();
        }
        await Assert.That(() => UsdReviewDocument.Read(bytes.AsSpan(0, bytes.Length - 1), files.SourcePath))
            .Throws<OpenUsdNativeException>();
        await Assert.That(() => UsdReviewDocument.Read([.. bytes, 0], files.SourcePath))
            .Throws<OpenUsdNativeException>();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task NonPristineImportPreservesTheExistingReviewAndEditTarget()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        UsdReviewDocument document = Capture(files);
        using UsdStage target = UsdStage.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding source = target.CaptureReviewSourceBinding();
        using UsdLayer review = target.GetUserReviewLayer();
        var address = new UsdLayerEditAddress("/Existing.value", UsdLayerEditField.Default);
        UsdLayerEditResult edit = review.CompareAndApply(review.CaptureAuthored([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(12), "double")]);
        await Assert.That(edit.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        UsdLayerCheckpoint before = review.CaptureCheckpoint();
        string editTarget = target.EditTargetLayerIdentifier;
        UsdReviewDocumentImportResult result = target.ImportReviewDocument(document, source);

        await Assert.That(result.Outcome).IsNotEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(result.AfterState).IsNull();
        await Assert.That(result.Diagnostic).IsNotNull();
        await Assert.That(before.HasSamePayload(review.CaptureCheckpoint())).IsTrue();
        await Assert.That(target.EditTargetLayerIdentifier).IsEqualTo(editTarget);
        await Assert.That(target.GetPrim("/Existing").GetDouble("value")).IsEqualTo(12d);
        await Assert.That(target.GetPrim("/World").GetDouble("weight")).IsEqualTo(7d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task ActivePhysicsIsNeverImportedOverOrAcknowledgedByPortableReview()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        UsdReviewDocument document = Capture(files);
        using UsdStage target = UsdStage.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding source = target.CaptureReviewSourceBinding();
        using UsdSessionOverlay overlay = target.NormalizeSessionOverlay();
        using UsdLayer review = target.GetUserReviewLayer();
        UsdLayerCheckpoint before = review.CaptureCheckpoint();
        string[] stack = target.GetLayerStackIdentifiers();
        UsdReviewDocumentImportResult result = target.ImportReviewDocument(document, source);

        await Assert.That(result.Outcome).IsNotEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(result.AfterState).IsNull();
        await Assert.That(before.HasSamePayload(review.CaptureCheckpoint())).IsTrue();
        await Assert.That(target.GetLayerStackIdentifiers().SequenceEqual(stack)).IsTrue();
        await Assert.That(review.AcknowledgeSaved(document)).IsFalse();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task ForeignVerifiedSourceCannotReceiveAnotherSourcesReview()
    {
        using ReviewFiles original = await ReviewFiles.CreateAsync();
        using ReviewFiles foreign = await ReviewFiles.CreateAsync();
        UsdReviewDocument document = Capture(original);
        using UsdStage target = UsdStage.OpenForReview(foreign.SourcePath);
        UsdReviewSourceBinding source = target.CaptureReviewSourceBinding();
        using UsdLayer review = target.GetUserReviewLayer();
        UsdLayerCheckpoint before = review.CaptureCheckpoint();

        await AssertImportRefusedAsync(target, document, source);
        await Assert.That(before.HasSamePayload(review.CaptureCheckpoint())).IsTrue();
        await Assert.That(target.GetPrim("/World").GetDouble("weight")).IsEqualTo(7d);
        await original.AssertOriginalsUnchangedAsync();
        await foreign.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task DependencyChangeAfterFreshOpenCannotImportOrPartiallyInstallTheReview()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        UsdReviewDocument document = Capture(files);
        using UsdStage target = UsdStage.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding source = target.CaptureReviewSourceBinding();
        using UsdLayer review = target.GetUserReviewLayer();
        UsdLayerCheckpoint before = review.CaptureCheckpoint();
        string[] stack = target.GetLayerStackIdentifiers();
        string assetPath = Path.Combine(files.DirectoryPath, "texture.bin");
        byte[] changed = await File.ReadAllBytesAsync(assetPath);
        changed[0] ^= 1;
        await File.WriteAllBytesAsync(assetPath, changed);

        await AssertImportRefusedAsync(target, document, source);
        await Assert.That(before.HasSamePayload(review.CaptureCheckpoint())).IsTrue();
        await Assert.That(target.GetLayerStackIdentifiers().SequenceEqual(stack)).IsTrue();
        await Assert.That(target.GetPrim("/World").GetDouble("weight")).IsEqualTo(7d);
        await Assert.That((await File.ReadAllBytesAsync(assetPath)).SequenceEqual(changed)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LegacyStageIsNotSilentlyReopenedOrGivenVerifiedProvenance(bool dirty)
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        using UsdStage legacy = UsdStage.Open(files.SourcePath);
        if (dirty)
        {
            legacy.GetPrim("/World").SetDouble("weight", 123);
        }
        await Assert.That(() => legacy.CaptureReviewSourceBinding()).Throws<OpenUsdNativeException>();
        await Assert.That(legacy.GetPrim("/World").GetDouble("weight")).IsEqualTo(dirty ? 123d : 7d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task PortableCrateRefusalDoesNotChangeOrdinaryStageOpening()
    {
        using ReviewFiles files = await ReviewFiles.CreateAsync();
        string path = Path.Combine(files.DirectoryPath, "ordinary.usdc");
        using (UsdStage created = UsdStage.Create(path))
        {
            created.DefinePrim("/Ordinary", "Xform");
            created.Save();
        }
        byte[] original = await File.ReadAllBytesAsync(path);
        await Assert.That(() => UsdStage.OpenForReview(path)).Throws<OpenUsdNativeException>();
        using (UsdStage ordinary = UsdStage.Open(path))
        {
            await Assert.That(ordinary.HasPrim("/Ordinary")).IsTrue();
        }
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(original)).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }

    private static UsdReviewDocument Capture(ReviewFiles files)
    {
        using UsdStage stage = UsdStage.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding source = stage.CaptureReviewSourceBinding();
        using UsdLayer review = stage.GetUserReviewLayer();
        _ = AuthorReview(review);
        return review.CaptureReviewDocument(source, files.DocumentPath);
    }

    private static async Task AssertImportRefusedAsync(
        UsdStage target, UsdReviewDocument document, UsdReviewSourceBinding source)
    {
        try
        {
            UsdReviewDocumentImportResult result = target.ImportReviewDocument(document, source);
            await Assert.That(result.Outcome).IsNotEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(result.AfterState).IsNull();
            await Assert.That(string.IsNullOrWhiteSpace(result.Diagnostic)).IsFalse();
        }
        catch (OpenUsdNativeException exception)
        {
            await Assert.That(exception.Status).IsNotEqualTo(OpenUsdNativeStatus.Ok);
            await Assert.That(string.IsNullOrWhiteSpace(exception.Message)).IsFalse();
        }
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

    private static UsdLayerAuthoredSnapshot AuthorReview(UsdLayer review)
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
        if (result.Outcome != UsdLayerEditOutcome.Applied || result.AfterSnapshot is null)
        {
            throw new InvalidOperationException($"Review fixture authoring failed: {result.Outcome}: {result.Diagnostic}");
        }
        return result.AfterSnapshot;
    }

    private static async Task AssertOpinionsAsync(UsdLayerAuthoredSnapshot actual)
    {
        await Assert.That(actual.Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(actual.Opinions[1].Value.AsTokenArray().SequenceEqual(["first", "caf\u00e9"])).IsTrue();
        await Assert.That(actual.Opinions[2].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
        UsdPathListEdit list = actual.Opinions[3].Value.AsPathList();
        await Assert.That(list.IsExplicit).IsFalse();
        await Assert.That(list.AddedItems.Single()).IsEqualTo("/Added");
        await Assert.That(list.PrependedItems.Single()).IsEqualTo("/Prepended");
        await Assert.That(list.AppendedItems.Single()).IsEqualTo("/Appended");
        await Assert.That(list.DeletedItems.Single()).IsEqualTo("/Deleted");
        await Assert.That(list.OrderedItems.Single()).IsEqualTo("/Ordered");
        await Assert.That(actual.Opinions[4].Value.AsAssetPath().AuthoredPath).IsEqualTo("texture.bin");
        await Assert.That(BitConverter.DoubleToUInt64Bits(actual.Opinions[5].Value.AsDouble()))
            .IsEqualTo(0x8000000000000000ul);
    }

    private sealed class ReviewFiles : IDisposable
    {
        private readonly Dictionary<string, byte[]> _hashes;

        private ReviewFiles(string directoryPath, Dictionary<string, byte[]> hashes)
        {
            DirectoryPath = directoryPath;
            SourcePath = Path.Combine(directoryPath, "source.usda");
            DocumentPath = Path.Combine(directoryPath, "published", "review.urd");
            _hashes = hashes;
        }

        internal string DirectoryPath { get; }
        internal string SourcePath { get; }
        internal string DocumentPath { get; }

        internal static async Task<ReviewFiles> CreateAsync(bool compositionGraph = false)
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH and a matching data ABI runtime for portable review tests.");
            }
            _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
            string workRoot = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
                ?? Path.Combine(AppContext.BaseDirectory, "portable-review-work");
            string directory = Path.GetFullPath(Path.Combine(workRoot, $"review-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(directory);
            var contents = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source.usda"] =
                    """
                    #usda 1.0
                    (
                        defaultPrim = "World"
                        startTimeCode = 2
                        endTimeCode = 24
                        framesPerSecond = 48
                        timeCodesPerSecond = 120
                        customLayerData = { string marker = "original source" }
                        subLayers = [@sublayer.usda@]
                    )
                    def Xform "World"
                    {
                        double weight = 7
                        asset texture = @texture.bin@
                    }
                    def Xform "OnlySource" {}
                    """,
                ["sublayer.usda"] = "#usda 1.0\ndef Xform \"FromSublayer\" {}\n",
                ["texture.bin"] = "original concrete asset"
            };
            if (compositionGraph)
            {
                contents["source.usda"] +=
                    """

                    def Xform "Referenced" ( references = @reference.usda@</Reference> ) {}
                    def Xform "Payload" ( payload = @payload.usda@</PayloadSource> ) {}
                    def Xform "Variants" (
                        variants = { string choice = "first" }
                        prepend variantSets = "choice"
                    )
                    {
                        variantSet "choice" = {
                            "first" {
                                string name = "first"
                            }
                            "second" {
                                asset alternate = @variant-only.bin@
                            }
                        }
                    }
                    """;
                contents["reference.usda"] =
                    "#usda 1.0\ndef Xform \"Reference\"\n{\n    def Xform \"ReferenceChild\" {}\n}\n";
                contents["payload.usda"] =
                    "#usda 1.0\ndef Xform \"PayloadSource\"\n{\n    def Xform \"PayloadChild\" {}\n}\n";
                contents["variant-only.bin"] = "asset in the unselected variant";
            }
            var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach ((string name, string text) in contents)
            {
                string path = Path.Combine(directory, name);
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                await File.WriteAllBytesAsync(path, bytes);
                hashes.Add(path, SHA256.HashData(bytes));
            }
            return new ReviewFiles(directory, hashes);
        }

        internal async Task AssertOriginalsUnchangedAsync()
        {
            foreach ((string path, byte[] hash) in _hashes)
            {
                byte[] actual = SHA256.HashData(await File.ReadAllBytesAsync(path));
                await Assert.That(actual.SequenceEqual(hash)).IsTrue();
            }
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
