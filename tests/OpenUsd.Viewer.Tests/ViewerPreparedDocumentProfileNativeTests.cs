// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPreparedDocumentProfileNativeTests
{
    private const string SourceText = """
        #usda 1.0
        def Xform "Body"
        {
            double review:value = 21
        }
        """;

    [Test]
    public async Task CrateUsdSourceOpensSessionOnlyWithoutReviewBindingOrSourceWrites()
    {
        string root = CreateNativeRoot();
        try
        {
            string cratePath = await CreateCrateUsdAsync(root);
            byte[] original = await File.ReadAllBytesAsync(cratePath);
            await Assert.That(original.AsSpan(0, 8).SequenceEqual("PXR-USDC"u8)).IsTrue();

            await using (ViewerPreparedDocument prepared =
                await ViewerPreparedDocument.OpenSourceAsync(cratePath, null, CancellationToken.None))
            {
                await Assert.That(prepared.SessionOnlyReason)
                    .IsEqualTo(ViewerPreparedDocument.GetSessionOnlyReason(cratePath));
                await Assert.That(prepared.SessionOnlyReason!).Contains("Crate sources remain available for viewing");
                await Assert.That(prepared.SourceBinding).IsNull();
                await Assert.That(prepared.SourcePath).IsEqualTo(Path.GetFullPath(cratePath));
                await Assert.That(await prepared.Scheduler.InvokeAsync(
                    static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(21d);
                await prepared.RevalidateSourceAsync(CancellationToken.None);
            }

            await Assert.That((await File.ReadAllBytesAsync(cratePath)).SequenceEqual(original)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CrateSessionOnlyEditsKeepSharedHistoryButRefuseReviewSaveAndRecovery()
    {
        string root = CreateNativeRoot();
        try
        {
            string cratePath = await CreateCrateUsdAsync(root);
            byte[] original = await File.ReadAllBytesAsync(cratePath);
            string destination = Path.Combine(root, "review.urd");
            string recovery = Path.Combine(root, "recovery.urd");
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);

            await using (ViewerPreparedDocument prepared =
                await ViewerPreparedDocument.OpenSourceAsync(cratePath, null, CancellationToken.None))
            await using (var editor = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding))
            {
                await Assert.That(editor.CanSaveReview).IsFalse();
                await editor.ApplyAsync(await editor.CaptureAsync([address]),
                    [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");

                await Assert.That(await ReadValueAsync(prepared)).IsEqualTo(47d);
                await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
                await Assert.That(editor.UndoDepth).IsEqualTo(1);
                await Assert.That(async () => await editor.PrepareReviewSaveAsync(destination))
                    .Throws<NotSupportedException>();
                await Assert.That(async () => await editor.CaptureRecoveryDocumentAsync(
                    recovery, retiring: false, CancellationToken.None)).Throws<NotSupportedException>();
                await Assert.That(editor.SavedReviewPath).IsNull();

                await editor.UndoAsync();
                await Assert.That(await ReadValueAsync(prepared)).IsEqualTo(21d);
                await Assert.That(editor.RedoDepth).IsEqualTo(1);
                await editor.RedoAsync();
                await Assert.That(await ReadValueAsync(prepared)).IsEqualTo(47d);
                await Assert.That(editor.UndoDepth).IsEqualTo(1);
            }

            await Assert.That(File.Exists(destination)).IsFalse();
            await Assert.That(File.Exists(recovery)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(cratePath)).SequenceEqual(original)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TextUsdSourceOpensWithVerifiedReviewBinding()
    {
        string root = CreateNativeRoot();
        try
        {
            string textPath = Path.Combine(root, "text.usd");
            await File.WriteAllTextAsync(textPath, SourceText);

            await using (ViewerPreparedDocument prepared =
                await ViewerPreparedDocument.OpenSourceAsync(textPath, null, CancellationToken.None))
            {
                await Assert.That(prepared.SessionOnlyReason).IsNull();
                UsdReviewSourceBinding binding = prepared.SourceBinding ??
                    throw new InvalidOperationException("A text-USD source must receive a verified review binding.");
                await Assert.That(Path.GetFullPath(binding.SourceRootPath)).IsEqualTo(Path.GetFullPath(textPath));
                await Assert.That(await prepared.Scheduler.InvokeAsync(
                    static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(21d);
            }

            await Assert.That(await File.ReadAllTextAsync(textPath)).IsEqualTo(SourceText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReloadingWithRetainedSessionOnlyReasonDoesNotUpgradeAnEligibleSource()
    {
        string root = CreateNativeRoot();
        try
        {
            string textPath = Path.Combine(root, "text.usda");
            await File.WriteAllTextAsync(textPath, SourceText);
            const string retained = "Session-only source: retained from the open document.";

            await using (ViewerPreparedDocument prepared =
                await ViewerPreparedDocument.OpenSourceAsync(textPath, retained, CancellationToken.None))
            {
                await Assert.That(ViewerPreparedDocument.GetSessionOnlyReason(textPath)).IsNull();
                await Assert.That(prepared.SessionOnlyReason).IsEqualTo(retained);
                await Assert.That(prepared.SourceBinding).IsNull();
                await Assert.That(await prepared.Scheduler.InvokeAsync(
                    static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(21d);
            }

            await Assert.That(await File.ReadAllTextAsync(textPath)).IsEqualTo(SourceText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ValueTask<double> ReadValueAsync(ViewerPreparedDocument prepared) =>
        prepared.Scheduler.InvokeAsync(static stage => stage.GetPrim("/Body").GetDouble("review:value"));

    private static async Task<string> CreateCrateUsdAsync(string root)
    {
        string textPath = Path.Combine(root, "authoring.usda");
        string exportPath = Path.Combine(root, "exported.usdc");
        string cratePath = Path.Combine(root, "crate.usd");
        await File.WriteAllTextAsync(textPath, SourceText);
        using (UsdStage stage = UsdStage.Open(textPath))
        {
            stage.Export(exportPath);
        }
        File.Move(exportPath, cratePath);
        return cratePath;
    }

    private static string CreateNativeRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The portable source profile uses Windows file identity.");
        }
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Provide a matching native runtime and OPENUSD_TEST_PLUGIN_PATH.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
        string parent = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ?? Path.GetTempPath();
        if (!Path.IsPathFullyQualified(parent))
        {
            throw new InvalidOperationException("The portable profile test root must be an absolute path.");
        }
        string root = Path.Combine(parent, $"viewer-profile-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
