// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerImportedRenderJobTests
{
    private static readonly string[] RememberedActionNames =
        ["HistoryImportButton", "HistoryViewButton", "HistoryCloseButton"];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RememberedPathsWaitForPlainEnterAndOpenOnlyUnverifiedOriginalOutput(bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        var source = new Source();
        RenderDiskJobResult original = Emit(files.Root, 1, source);
        source.Closed = true;
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        byte[] manifest = await File.ReadAllBytesAsync(path);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            int reads = 0;
            string? remembered = null;
            var history = new ViewerCompletedJobHistory();
            var window = new CompletedJobHistoryWindow(history, readManifest: (selected, token) =>
            {
                reads++;
                return ViewerImportedRenderJob.ReadAsync(selected, token);
            }, rememberedPaths: [path], rememberManifest: selected => remembered = selected)
            {
                Width = 520,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                Required<TabControl>(window, "HistoryTabs").SelectedIndex = 1;
                ListBox paths = Required<ListBox>(window, "HistoryRememberedJobs");
                await Assert.That(paths.ItemCount).IsEqualTo(1);
                await Assert.That(reads).IsEqualTo(0);
                await Assert.That(window.OwnedWindows).IsEmpty();
                await Assert.That(paths.Focus()).IsTrue();
                foreach (RawInputModifiers modifier in new[]
                {
                    RawInputModifiers.Control, RawInputModifiers.Shift, RawInputModifiers.Alt, RawInputModifiers.Meta
                })
                {
                    window.KeyPress(Key.Enter, modifier, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, modifier, PhysicalKey.Enter, null);
                }
                await Assert.That(reads).IsEqualTo(0);
                window.UpdateLayout();
                await Assert.That(paths.Bounds.Height).IsGreaterThanOrEqualTo(100);
                foreach (string name in RememberedActionNames)
                {
                    Button action = Required<Button>(window, name);
                    Point position = action.TranslatePoint(default, window) ??
                        throw new InvalidOperationException("The remembered job action has no coordinates.");
                    await Assert.That(position.Y).IsGreaterThanOrEqualTo(0);
                    await Assert.That(position.Y + action.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                }
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => window.OwnedWindows.OfType<CompletedRenderJobWindow>().Any());
                CompletedRenderJobWindow preview = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(preview, "ResultsLoading").IsVisible);
                await Assert.That(reads).IsEqualTo(1);
                await Assert.That(remembered).IsEqualTo(path);
                await Assert.That(Required<TextBlock>(preview, "ResultsSource").Text)
                    .Contains("unverified manifest claim: persisted-source");
                var tile = (CompletedRenderJobTile)Required<ListBox>(preview, "ResultsFrames").Items[0]!;
                await Assert.That(Convert.ToHexString(ViewerCompletedJobPreviewJourney.ReadPixels(tile.Image)
                    .AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                await Assert.That(history.Snapshot()).IsEmpty();
                await preview.CloseAsync();
                await Assert.That(Required<Button>(window, "HistoryViewButton").IsKeyboardFocusWithin).IsTrue();
                await Assert.That(paths.SelectedItem).IsEqualTo(path);
                await Assert.That(source.Calls).IsEqualTo(1);
            }
            finally
            {
                await window.CloseAsync();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(manifest)).IsTrue();
    }

    [Test]
    public async Task MissingRememberedManifestStaysListedAndReportsFailureWithoutReplacingAGallery()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = Emit(files.Root, 1, new Source());
        string valid = Path.Combine(original.OutputDirectory, "manifest.json");
        string missing = Path.Combine(files.Root, "missing", "manifest.json");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var history = new ViewerCompletedJobHistory();
            var window = new CompletedJobHistoryWindow(history, rememberedPaths: [missing]);
            window.Show();
            try
            {
                await window.ImportManifestAsync(valid);
                CompletedRenderJobWindow gallery = window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                Required<TabControl>(window, "HistoryTabs").SelectedIndex = 1;
                await window.ImportManifestAsync(missing, remembered: true);
                await Assert.That(Required<TextBlock>(window, "HistoryStatus").Text)
                    .Contains("Could not import render manifest");
                await Assert.That(Required<ListBox>(window, "HistoryRememberedJobs").SelectedItem).IsEqualTo(missing);
                await Assert.That(window.OwnedWindows.OfType<CompletedRenderJobWindow>().Single())
                    .IsSameReferenceAs(gallery);
                await Assert.That(history.Snapshot()).IsEmpty();
            }
            finally
            {
                await window.CloseAsync();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MainWindowDrainsCompletedOrImportedPathPersistenceBeforeRestart(bool publishCompletion)
    {
        using var files = new ViewerCompletedJobTestFiles();
        var source = new Source();
        RenderDiskJobResult original = Emit(files.Root, 1, source);
        source.Closed = true;
        string path = Path.Combine(original.OutputDirectory, "manifest.json");
        string profile = Path.Combine(files.Root, "profile");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions { Renderer = "Storm" });
            try
            {
                for (int restart = 0; restart < 2; restart++)
                {
                    using var settings = new ViewerSettingsStore(profile);
                    var main = new MainWindow(new RecentStageStore(Path.Combine(files.Root, "recent")), settings);
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    main.Closed += (_, _) => closed.TrySetResult();
                    main.Show();
                    try
                    {
                        await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                            () => Required<MenuItem>(main, "ThemeMenu").IsEnabled);
                        Required<MenuItem>(main, "CompletedJobsMenuItem")
                            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        var history = main.OwnedWindows.OfType<CompletedJobHistoryWindow>().Single();
                        if (restart == 0)
                        {
                            if (publishCompletion)
                            {
                                MethodInfo publish = typeof(MainWindow).GetMethod("PublishCompletedJob",
                                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                                    throw new InvalidOperationException("Missing completed render publication.");
                                publish.Invoke(main, [original, "Finished fake-source render"]);
                                await Assert.That(Required<ListBox>(history, "HistoryJobs").ItemCount).IsEqualTo(1);
                            }
                            else
                            {
                                await history.ImportManifestAsync(path);
                                await Assert.That(Required<ListBox>(history, "HistoryJobs").ItemCount).IsEqualTo(0);
                            }
                        }
                        else
                        {
                            Required<TabControl>(history, "HistoryTabs").SelectedIndex = 1;
                            await Assert.That(Required<ListBox>(history, "HistoryJobs").ItemCount).IsEqualTo(0);
                            ListBox remembered = Required<ListBox>(history, "HistoryRememberedJobs");
                            await Assert.That(remembered.ItemCount).IsEqualTo(1);
                            await Assert.That(remembered.SelectedItem).IsEqualTo(path);
                            await Assert.That(history.OwnedWindows).IsEmpty();
                            await Assert.That(Required<RendererSwitchingViewport>(main, "ViewportHost")
                                .AttachedControlCount).IsEqualTo(0);
                        }
                    }
                    finally
                    {
                        main.Close();
                        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    using var index = new ViewerRecentRenderManifestStore(profile);
                    await Assert.That((await index.LoadAsync()).Single()).IsEqualTo(path);
                }
                await Assert.That(source.Calls).IsEqualTo(1);
            }
            finally
            {
                ViewerStartupOptions.Initialize([]);
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RememberedIndexFailureKeepsCompletedOutputAndReportsARecoverableStorageWarning(bool malformed)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = Emit(files.Root, 1, new Source());
        string manifest = Path.Combine(job.OutputDirectory, "manifest.json");
        byte[] sourceBytes = await File.ReadAllBytesAsync(manifest);
        string profile = Path.Combine(files.Root, "profile");
        using var index = new ViewerRecentRenderManifestStore(profile);
        await index.AddAsync(Path.Combine(files.Root, "older", "manifest.json"));
        byte[] validIndex = await File.ReadAllBytesAsync(index.StorePath);
        if (malformed)
        {
            await File.WriteAllTextAsync(index.StorePath, "{");
        }
        byte[] before = await File.ReadAllBytesAsync(index.StorePath);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions { Renderer = "Storm" });
            using var settings = new ViewerSettingsStore(profile);
            var main = new MainWindow(new RecentStageStore(Path.Combine(files.Root, "recent")), settings);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Closed += (_, _) => closed.TrySetResult();
            FileStream? blocked = null;
            main.Show();
            try
            {
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => Required<MenuItem>(main, "ThemeMenu").IsEnabled);
                Required<MenuItem>(main, "CompletedJobsMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var history = main.OwnedWindows.OfType<CompletedJobHistoryWindow>().Single();
                if (!malformed)
                {
                    blocked = new FileStream(index.StorePath + ".lock", FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                }
                MethodInfo publish = typeof(MainWindow).GetMethod("PublishCompletedJob",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException("Missing completed render publication.");
                publish.Invoke(main, [job, "Completed output despite unavailable path storage"]);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => Required<TextBlock>(history, "HistoryRememberedStatus").Text?
                        .Contains("could not be saved", StringComparison.Ordinal) == true);
                await Assert.That(Required<ListBox>(history, "HistoryJobs").ItemCount).IsEqualTo(1);
                await Assert.That(Required<TextBlock>(history, "HistoryRememberedStatus").Text)
                    .Contains("Original render files were not changed");
                await Assert.That((await File.ReadAllBytesAsync(index.StorePath)).SequenceEqual(before)).IsTrue();
                blocked?.Dispose();
                blocked = null;
                if (malformed)
                {
                    await File.WriteAllBytesAsync(index.StorePath, validIndex);
                }
                await history.ImportManifestAsync(manifest);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<TextBlock>(history, "HistoryRememberedStatus").IsVisible);
                await Assert.That(Required<ListBox>(history, "HistoryJobs").ItemCount).IsEqualTo(1);
                await Assert.That((await index.LoadAsync())[0]).IsEqualTo(manifest);
            }
            finally
            {
                blocked?.Dispose();
                main.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ViewerStartupOptions.Initialize([]);
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That((await File.ReadAllBytesAsync(manifest)).SequenceEqual(sourceBytes)).IsTrue();
    }
}
