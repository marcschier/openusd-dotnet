// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerAuthoredEditControllerAdoptionTests
{
    [Test]
    public async Task AdoptionClearsOldHistoryAndNewEditsUndoAndRedoOnTheAdoptedReviewLayer()
    {
        using AdoptionFiles files = await AdoptionFiles.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(files.SourcePath);
        string sourceIdentifier = await scheduler.InvokeAsync(static stage => stage.RootLayerIdentifier);
        await Assert.That(sourceIdentifier)
            .IsEqualTo(Path.GetFullPath(files.SourcePath).Replace('\\', '/'));
        var sessionAddress = new UsdLayerEditAddress("/Body.sessionValue", UsdLayerEditField.Default);
        var reviewAddress = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await using var editor = new ViewerAuthoredEditController(scheduler);

        ViewerAuthoredEditCapture oldCapture = await editor.CaptureAsync([reviewAddress]);
        ViewerAuthoredEditResult oldEdit = await editor.ApplyAsync(oldCapture,
            [UsdLayerEdit.Set(reviewAddress, UsdLayerEditValue.FromDouble(31d), "double")],
            "Pre-adoption edit");
        string oldReviewIdentifier = oldCapture.LayerIdentifier;
        await Assert.That(oldEdit.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(editor.UndoDepth).IsEqualTo(1);
        await Assert.That(editor.CanAdoptReview).IsTrue();
        await Assert.That(editor.CanSaveReview).IsFalse();

        ViewerDocumentObservation adopted = await editor.AdoptIntoReviewAsync();

        await Assert.That(adopted.State.Review).IsNotNull();
        string adoptedReviewIdentifier = adopted.State.Review!.Identifier;
        await Assert.That(adoptedReviewIdentifier).IsNotEqualTo(oldReviewIdentifier);
        await Assert.That(adopted.State.EditTargetIdentifier).IsEqualTo(adoptedReviewIdentifier);
        await Assert.That(editor.UndoDepth).IsEqualTo(0);
        await Assert.That(editor.RedoDepth).IsEqualTo(0);
        await Assert.That(editor.CanAdoptReview).IsFalse();
        await Assert.That(editor.CanSaveReview).IsTrue();

        ViewerAuthoredEditCapture adoptedCapture =
            await editor.CaptureAsync([reviewAddress, sessionAddress]);
        await Assert.That(adoptedCapture.LayerIdentifier).IsEqualTo(adoptedReviewIdentifier);
        await Assert.That(adoptedCapture.Snapshot.Opinions[0].Value.AsDouble()).IsEqualTo(31d);
        await Assert.That(adoptedCapture.Snapshot.Opinions[1].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(adoptedCapture.Snapshot.Opinions[1].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(await scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(31d);
        await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.NotEditable);
        await Assert.That((await editor.CaptureAsync([reviewAddress])).Snapshot.Opinions[0].Value.AsDouble())
            .IsEqualTo(31d);

        ViewerAuthoredEditCapture nextCapture = await editor.CaptureAsync([reviewAddress]);
        await editor.ApplyAsync(nextCapture,
            [UsdLayerEdit.Set(reviewAddress, UsdLayerEditValue.FromDouble(55d))], "Post-adoption edit");
        await Assert.That(editor.UndoDepth).IsEqualTo(1);
        ViewerAuthoredEditResult undone = await editor.UndoAsync();
        await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(undone.After!.Opinions[0].Value.AsDouble()).IsEqualTo(31d);
        await Assert.That(undone.After.Identity).IsEqualTo(adoptedCapture.Snapshot.Identity);
        await Assert.That(await scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(31d);
        ViewerAuthoredEditResult redone = await editor.RedoAsync();
        await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(redone.After!.Opinions[0].Value.AsDouble()).IsEqualTo(55d);
        await Assert.That(redone.After.Identity).IsEqualTo(adoptedCapture.Snapshot.Identity);
        await Assert.That(await scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(55d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task RefusedAdoptionPreservesDocumentAndHistoryThenCanBeRetriedAfterConflictRemoval()
    {
        using AdoptionFiles files = await AdoptionFiles.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(files.SourcePath);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        ViewerAuthoredEditCapture capture = await editor.CaptureAsync([address]);
        await editor.ApplyAsync(capture,
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(37d), "double")], "Pre-adoption edit");
        string reviewIdentifier = capture.LayerIdentifier;

        string foreignSublayer = Path.Combine(files.Root, "sublayer.usda");
        await scheduler.InvokeAsync(stage =>
        {
            using UsdLayer session = stage.GetSessionLayer();
            session.AddSublayer(foreignSublayer);
        });
        ViewerDocumentObservation beforeRefusal = await editor.ReadDocumentAsync();
        AdoptionSnapshot before = await CaptureAdoptionSnapshotAsync(scheduler, reviewIdentifier, address);

        await Assert.That(async () => await editor.AdoptIntoReviewAsync()).Throws<OpenUsdNativeException>();

        ViewerDocumentObservation afterRefusal = await editor.ReadDocumentAsync();
        AdoptionSnapshot unchanged = await CaptureAdoptionSnapshotAsync(scheduler, reviewIdentifier, address);
        await Assert.That(beforeRefusal.Matches(afterRefusal)).IsTrue();
        await Assert.That(unchanged.LayerIdentifiers.SequenceEqual(before.LayerIdentifiers)).IsTrue();
        await Assert.That(unchanged.EditTarget).IsEqualTo(before.EditTarget);
        await Assert.That(unchanged.SessionIdentity).IsEqualTo(before.SessionIdentity);
        await Assert.That(unchanged.SessionRevision).IsEqualTo(before.SessionRevision);
        await Assert.That(unchanged.SessionRole).IsEqualTo(before.SessionRole);
        await Assert.That(unchanged.SessionIdentifier).IsEqualTo(before.SessionIdentifier);
        await Assert.That(unchanged.SessionSublayers.SequenceEqual(before.SessionSublayers)).IsTrue();
        await Assert.That(unchanged.SessionSublayers).Contains(foreignSublayer);
        await Assert.That(unchanged.ReviewOpinions.SequenceEqual(before.ReviewOpinions)).IsTrue();
        await Assert.That(editor.UndoDepth).IsEqualTo(1);
        await Assert.That(editor.RedoDepth).IsEqualTo(0);
        await Assert.That(editor.CanAdoptReview).IsTrue();
        await Assert.That(editor.CanSaveReview).IsFalse();
        ViewerAuthoredEditResult stillUndoable = await editor.UndoAsync();
        await Assert.That(stillUndoable.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(stillUndoable.After!.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(editor.RedoDepth).IsEqualTo(1);
        ViewerAuthoredEditResult restored = await editor.RedoAsync();
        await Assert.That(restored.After!.Opinions[0].Value.AsDouble()).IsEqualTo(37d);
        await Assert.That(editor.UndoDepth).IsEqualTo(1);

        await scheduler.InvokeAsync(stage =>
        {
            using UsdLayer session = stage.GetSessionLayer();
            session.RemoveSublayer(foreignSublayer);
        });
        ViewerDocumentObservation retried = await editor.AdoptIntoReviewAsync();
        await Assert.That(retried.State.Review).IsNotNull();
        await Assert.That(retried.State.EditTargetIdentifier).IsEqualTo(retried.State.Review!.Identifier);
        await Assert.That(editor.UndoDepth).IsEqualTo(0);
        ViewerAuthoredEditCapture retriedCapture = await editor.CaptureAsync([address]);
        await Assert.That(retriedCapture.Snapshot.Opinions[0].Value.AsDouble()).IsEqualTo(37d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task CancellationWhileWaitingForSchedulerLeavesUnboundDocumentAndHistoryUnchanged()
    {
        using AdoptionFiles files = await AdoptionFiles.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(files.SourcePath);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        ViewerAuthoredEditCapture capture = await editor.CaptureAsync([address]);
        await editor.ApplyAsync(capture,
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(29d), "double")], "Pre-adoption edit");
        ViewerDocumentObservation before = await editor.ReadDocumentAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int> blocker = scheduler.InvokeAsync(_ =>
        {
            entered.Set();
            release.Wait();
            return 1;
        }).AsTask();
        try
        {
            await Task.Run(() =>
            {
                if (!entered.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The scheduler barrier did not start.");
                }
            });
            using var cancellation = new CancellationTokenSource();
            Task<ViewerDocumentObservation> adoption =
                editor.AdoptIntoReviewAsync(cancellation.Token);
            cancellation.Cancel();
            release.Set();

            await Assert.That(async () => await adoption).Throws<OperationCanceledException>();
            await Assert.That(await blocker).IsEqualTo(1);
        }
        finally
        {
            release.Set();
        }

        ViewerDocumentObservation after = await editor.ReadDocumentAsync();
        await Assert.That(before.Matches(after)).IsTrue();
        await Assert.That(editor.CanAdoptReview).IsTrue();
        await Assert.That(editor.CanSaveReview).IsFalse();
        await Assert.That(editor.UndoDepth).IsEqualTo(1);
        await Assert.That((await editor.CaptureAsync([address])).Snapshot.Opinions[0].Value.AsDouble())
            .IsEqualTo(29d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task CancellationRequestedAfterNativeCommitStillReturnsTheAdoptedObservation()
    {
        using AdoptionFiles files = await AdoptionFiles.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(files.SourcePath);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await scheduler.InvokeAsync(stage =>
        {
            using UsdLayer review = stage.GetUserReviewLayer();
            UsdLayerEditResult edit = review.CompareAndApply(review.CaptureAuthored([address]),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(11d), "double")]);
            if (edit.Outcome != UsdLayerEditOutcome.Applied)
            {
                throw new InvalidOperationException($"Could not seed adoption: {edit.Outcome} {edit.Diagnostic}");
            }
        });
        await using var editor = new ViewerAuthoredEditController(scheduler);
        using var cancellation = new CancellationTokenSource();
        editor.Changed += cancellation.Cancel;

        ViewerDocumentObservation adopted = await editor.AdoptIntoReviewAsync(cancellation.Token);

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(adopted.State.Review).IsNotNull();
        await Assert.That(adopted.State.EditTargetIdentifier).IsEqualTo(adopted.State.Review!.Identifier);
        await Assert.That(editor.CanSaveReview).IsTrue();
        await Assert.That(editor.UndoDepth).IsEqualTo(0);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task AdoptedReviewSaveReopensWithExactOpinionsCleanStateAndFreshHistory()
    {
        using AdoptionFiles files = await AdoptionFiles.CreateAsync();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(files.SourcePath))
        {
            var sessionAddress = new UsdLayerEditAddress("/SessionOnly.sessionValue", UsdLayerEditField.Default);
            await scheduler.InvokeAsync(stage =>
            {
                stage.SetEditTargetToSessionLayer();
                stage.DefinePrim("/SessionOnly", "Xform");
                stage.GetPrim("/SessionOnly").SetDouble("sessionValue", 43d);
            });
            await using var editor = new ViewerAuthoredEditController(scheduler);
            ViewerDocumentObservation adopted = await editor.AdoptIntoReviewAsync();
            await Assert.That(adopted.State.Review).IsNotNull();
            ViewerReviewSaveResult saved = await editor.PublishReviewSaveAsync(
                await editor.PrepareReviewSaveAsync(files.DestinationPath));
            await Assert.That(saved.Acknowledged).IsTrue();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await files.AssertOriginalsUnchangedAsync();
        }

        await using ViewerReviewDocumentFile file =
            await ViewerReviewDocumentFile.InspectAsync(files.DestinationPath, CancellationToken.None);
        UsdReviewDocument document = await file.ReadAsync(files.SourcePath, CancellationToken.None);
        await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, file.Identity, recovery: false, CancellationToken.None);
        await prepared.RevalidateSourceAsync(CancellationToken.None);
        await using var reopened = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding);
        reopened.InitializeImportedReview(prepared);

        var reopenedAddress = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        var reopenedSessionAddress = new UsdLayerEditAddress("/SessionOnly.sessionValue", UsdLayerEditField.Default);
        var absentAddress = new UsdLayerEditAddress("/Body.review:absent", UsdLayerEditField.Default);
        ViewerAuthoredEditCapture exact = await reopened.CaptureAsync(
            [reopenedAddress, reopenedSessionAddress, absentAddress]);
        await Assert.That(exact.Snapshot.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(exact.Snapshot.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(exact.Snapshot.Opinions[1].Value.AsDouble()).IsEqualTo(43d);
        await Assert.That(exact.Snapshot.Opinions[2].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(exact.Snapshot.Opinions[2].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(7d);
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/SessionOnly").GetDouble("sessionValue"))).IsEqualTo(43d);
        await Assert.That((await reopened.ReadDocumentAsync()).HasChanges).IsFalse();
        await Assert.That(reopened.UndoDepth).IsEqualTo(0);
        await Assert.That(reopened.RedoDepth).IsEqualTo(0);
        await files.AssertOriginalsUnchangedAsync();
    }

    private static async Task<AdoptionSnapshot> CaptureAdoptionSnapshotAsync(
        UsdStageScheduler scheduler, string reviewIdentifier, UsdLayerEditAddress address) =>
        await scheduler.InvokeAsync(stage =>
        {
            using UsdLayer session = stage.GetSessionLayer();
            using UsdLayer review = stage.GetLocalLayer(reviewIdentifier);
            UsdLayerEditingState sessionState = session.GetEditingState();
            return new AdoptionSnapshot(
                stage.GetLayerStackIdentifiers(),
                stage.EditTargetLayerIdentifier,
                sessionState.Identity,
                sessionState.Revision,
                sessionState.Role,
                sessionState.Identifier,
                session.GetSublayerPaths(),
                review.CaptureAuthored([address]).CopyBytes());
        });

    private sealed record AdoptionSnapshot(
        string[] LayerIdentifiers,
        string EditTarget,
        UsdLayerIdentity SessionIdentity,
        ulong SessionRevision,
        UsdLayerRole SessionRole,
        string SessionIdentifier,
        string[] SessionSublayers,
        byte[] ReviewOpinions) : IUsdDetachedResult;

    private sealed class AdoptionFiles : IDisposable
    {
        private readonly byte[] _sourceBytes;
        private readonly byte[] _sublayerBytes;
        private readonly byte[] _assetBytes;

        private AdoptionFiles(string root, byte[] sourceBytes, byte[] sublayerBytes, byte[] assetBytes)
        {
            Root = root;
            _sourceBytes = sourceBytes;
            _sublayerBytes = sublayerBytes;
            _assetBytes = assetBytes;
        }

        internal string Root { get; }
        internal string SourcePath => Path.Combine(Root, "source.usda");
        internal string DestinationPath => Path.Combine(Root, "review.urd");

        internal static async Task<AdoptionFiles> CreateAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip.Test("Verified portable review adoption currently requires Windows.");
            }
            string plugins = ViewerNativeTestStages.RequirePluginPathOrSkip();
            OpenUsdNativeRuntime.RegisterPlugins(plugins);
            string root = Path.Combine(
                AppContext.BaseDirectory, "adoption-work", $"adoption-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            byte[] source = System.Text.Encoding.UTF8.GetBytes(ViewerPortableReviewFixture.SourceText);
            byte[] sublayer = System.Text.Encoding.UTF8.GetBytes(ViewerPortableReviewFixture.SublayerText);
            byte[] asset = ViewerPortableReviewFixture.AssetBytes;
            await File.WriteAllBytesAsync(Path.Combine(root, "source.usda"), source);
            await File.WriteAllBytesAsync(Path.Combine(root, "sublayer.usda"), sublayer);
            await File.WriteAllBytesAsync(Path.Combine(root, "texture.png"), asset);
            return new AdoptionFiles(root, source, sublayer, asset);
        }

        internal async Task AssertOriginalsUnchangedAsync()
        {
            await Assert.That((await File.ReadAllBytesAsync(SourcePath)).SequenceEqual(_sourceBytes)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(Path.Combine(Root, "sublayer.usda")))
                .SequenceEqual(_sublayerBytes)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(Path.Combine(Root, "texture.png")))
                .SequenceEqual(_assetBytes)).IsTrue();
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
