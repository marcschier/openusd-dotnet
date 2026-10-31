// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class MainWindowSourceSavingTests
{
    [Test]
    public async Task SourceCommandWithoutADocumentIsDisabledAndExplainsTheMissingVerifiedOrigin()
    {
        string root = CreateTemporaryRoot();
        try
        {
            await using HeadlessUnitTestSession session =
                HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await session.Dispatch(async () =>
            {
                var window = new MainWindow(
                    new RecentStageStore(Path.Combine(root, "recent")),
                    new ViewerSettingsStore(Path.Combine(root, "settings")));
                try
                {
                    MenuItem sourceSave = Required<MenuItem>(window, "SaveSourceMenuItem");
                    MenuItem reviewSave = Required<MenuItem>(window, "SaveReviewMenuItem");

                    await Assert.That(sourceSave.IsEnabled).IsFalse();
                    await Assert.That(ToolTip.GetTip(sourceSave))
                        .IsEqualTo("Source publication requires a verified source origin.");
                    await Assert.That(reviewSave.IsEnabled).IsFalse();
                    await Assert.That(ToolTip.GetTip(sourceSave))
                        .IsNotEqualTo(ToolTip.GetTip(reviewSave));
                    await Assert.That(Required<TextBlock>(window, "DocumentEditStatus").Text)
                        .IsEqualTo("Review: no document");
                }
                finally
                {
                    window.Dispose();
                }

                return 0;
            }, lifetime.Token).WaitAsync(lifetime.Token);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task WiredSourceCommandRefusesWithoutADocumentAndLeavesAnUnrelatedSourceFileUntouched()
    {
        string root = CreateTemporaryRoot();
        Directory.CreateDirectory(root);
        string sourcePath = Path.Combine(root, "unbound-source.usda");
        byte[] sourceBytes = System.Text.Encoding.UTF8.GetBytes(
            "#usda 1.0\n def Xform \"Untouched\" {}\n");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        try
        {
            await using HeadlessUnitTestSession session =
                HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await session.Dispatch(async () =>
            {
                var window = new MainWindow(
                    new RecentStageStore(Path.Combine(root, "recent")),
                    new ViewerSettingsStore(Path.Combine(root, "settings")));
                try
                {
                    MenuItem sourceSave = Required<MenuItem>(window, "SaveSourceMenuItem");
                    await Assert.That(sourceSave.IsEnabled).IsFalse();

                    // Raising Click directly exercises the command subscription even though
                    // the UI correctly disables it while no document is open.
                    sourceSave.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                    await Assert.That(Required<TextBlock>(window, "ViewerStatus").Text)
                        .IsEqualTo(
                            "Error: Source layer was not published: " +
                            "Open a document before publishing its source layer.");
                    byte[] currentSourceBytes = await File.ReadAllBytesAsync(sourcePath);
                    await Assert.That(currentSourceBytes.SequenceEqual(sourceBytes)).IsTrue();
                    await Assert.That(sourceSave.IsEnabled).IsFalse();
                    await Assert.That(Required<MenuItem>(window, "SaveReviewMenuItem").IsEnabled).IsFalse();
                }
                finally
                {
                    window.Dispose();
                }

                return 0;
            }, lifetime.Token).WaitAsync(lifetime.Token);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "openusd-main-window-source-saving", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
