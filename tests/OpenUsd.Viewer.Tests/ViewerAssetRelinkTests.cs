// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Shade;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerAssetRelinkTests
{
    [Test]
    public async Task RelinkingATextureUsesExactReviewHistoryAndReopensWithoutChangingItsSourceGraph()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        string replacement = Path.Combine(files.Root, "replacement.png");
        await File.WriteAllBytesAsync(replacement, ViewerPortableReviewFixture.AssetBytes);
        UsdReviewDocument saved;
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            ViewerPropertyEditCapture property = await editor.CapturePropertyAsync(
                "/Material/Texture", "inputs:file", UsdLayerEditField.Default, 0);
            await Assert.That(property.TypeName).IsEqualTo("asset");
            await Assert.That(property.Capture.Snapshot.Opinions[0].Value.Kind)
                .IsEqualTo(UsdLayerEditValueKind.Absent);
            await Assert.That(ViewerPropertyEditParser.TryParse("asset", "replacement.png",
                out UsdLayerEditValue? value, out _)).IsTrue();
            UsdLayerEditAddress address = property.Capture.Snapshot.Addresses[0];

            ViewerAuthoredEditResult result = await editor.ApplyAsync(property.Capture,
                [UsdLayerEdit.Set(address, value!, property.TypeName, property.Variability, property.Custom)],
                "Relink texture", Guid.NewGuid());
            await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(TexturePath)).IsEqualTo("replacement.png");
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await editor.UndoAsync();
            await Assert.That(await scheduler.InvokeAsync(TexturePath)).IsEqualTo("texture.png");
            await editor.RedoAsync();
            await Assert.That(await scheduler.InvokeAsync(TexturePath)).IsEqualTo("replacement.png");
            ViewerReviewSaveResult publication =
                await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
            await Assert.That(publication.Acknowledged).IsTrue();
            saved = publication.Document;
            await Assert.That(saved.Dependencies.Any(dependency =>
                Path.GetFullPath(dependency.Path) == replacement)).IsTrue();
        }

        await using ViewerPreparedDocument reopened = await ViewerPreparedDocument.OpenReviewAsync(
            saved, files.SourcePath, null, recovery: false, CancellationToken.None);
        await Assert.That(await reopened.Scheduler.InvokeAsync(TexturePath)).IsEqualTo("replacement.png");
        await Assert.That(await reopened.Scheduler.InvokeAsync(static stage =>
            UsdShadeShader.Wrap(stage.GetPrim("/Material/Texture")).SourceId)).IsEqualTo("UsdUVTexture");
        await Assert.That(await reopened.Scheduler.InvokeAsync(static stage =>
        {
            UsdShadeConnection connection = UsdShadeShader.Wrap(stage.GetPrim("/Material/Preview"))
                .GetInput("diffuseColor").GetConnectedSource();
            return (connection.SourcePrimPath, connection.SourceName);
        })).IsEqualTo(("/Material/Texture", "rgb"));
        await files.AssertOriginalsUnchangedAsync();
        byte[] replacementBytes = await File.ReadAllBytesAsync(replacement);
        await Assert.That(replacementBytes.SequenceEqual(ViewerPortableReviewFixture.AssetBytes)).IsTrue();
    }

    private static string TexturePath(UsdStage stage) =>
        UsdShadeShader.Wrap(stage.GetPrim("/Material/Texture")).GetInput("file").GetAssetPath().Path;
}
