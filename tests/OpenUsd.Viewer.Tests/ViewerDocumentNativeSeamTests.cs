// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentNativeSeamTests
{
    [Test]
    public async Task ComposedAuthorshipDoesNotEstablishAnOpinionInTheSessionTarget()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "document-seam", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.usda");
        string target = Path.Combine(root, "session.usda");
        const string original = """
            #usda 1.0
            def Xform "Body"
            {
                custom double review:value = 7
            }
            """;
        try
        {
            await File.WriteAllTextAsync(source, original);
            await using (UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(source))
            {
                bool composedAuthored = await scheduler.InvokeAsync(stage =>
                {
                    stage.SetEditTargetToSessionLayer();
                    using UsdLayer session = stage.GetSessionLayer();
                    session.Export(target);
                    return stage.GetPrim("/Body").GetAttribute("review:value").IsAuthored;
                });
                await Assert.That(composedAuthored).IsTrue();
            }
            await using (UsdStageScheduler targetOnly = ViewerNativeTestStages.OpenSchedulerOrSkip(target))
            {
                bool targetHasPrim = await targetOnly.InvokeAsync(static stage => stage.HasPrim("/Body"));
                await Assert.That(targetHasPrim).IsFalse()
                    .Because("the target layer is empty even though the composed attribute reports authored");
            }
            await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
