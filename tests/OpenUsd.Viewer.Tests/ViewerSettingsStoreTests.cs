// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerSettingsStoreTests
{
    [Test]
    public async Task SaveIsAtomicSerializedAndRoundTripsAllValues()
    {
        string root = CreateTestRoot();
        try
        {
            using var store = new ViewerSettingsStore(root);
            ViewerSettings first = ViewerSettings.Default with
            {
                WindowWidth = 1200,
                WindowHeight = 700,
                RendererPreference = "Storm",
                SelectedInspectorTab = 1,
                StagePanelVisible = false,
                SnapTimelineToFrames = true
            };
            ViewerSettings second = ViewerSettings.Default with
            {
                WindowWidth = 1600,
                WindowHeight = 1000,
                StagePanelWidth = 320,
                InspectorPanelWidth = 420,
                RendererPreference = "Vulkan",
                SelectedInspectorTab = 3,
                InspectorPanelVisible = false,
                TimelineVisible = false,
                DiagnosticsVisible = false
            };

            await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(second));
            ViewerSettingsLoadResult loaded = await store.LoadAsync();

            await Assert.That(loaded.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
            await Assert.That(loaded.Settings == first || loaded.Settings == second).IsTrue();
            await Assert.That(Directory.GetFiles(root, "*.tmp")).IsEmpty();
            string[] lines = await File.ReadAllLinesAsync(store.StorePath);
            await Assert.That(lines[0]).IsEqualTo("openusd-viewer-settings=1");
            await Assert.That(lines.Any(line => line.StartsWith(
                "snapTimelineToFrames=",
                StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Test]
    public async Task OversizedSettingsAreRejectedWithoutReadingOrRewriting()
    {
        string root = CreateTestRoot();
        try
        {
            using var store = new ViewerSettingsStore(root);
            await File.WriteAllTextAsync(
                store.StorePath,
                new string('x', ViewerSettingsStore.MaximumFileBytes + 1));

            ViewerSettingsLoadResult result = await store.LoadAsync();

            await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Malformed);
            await Assert.That(result.Settings).IsEqualTo(ViewerSettings.Default);
            await Assert.That(result.Diagnostic).Contains("safety limit");
            await Assert.That(new FileInfo(store.StorePath).Length)
                .IsGreaterThan(ViewerSettingsStore.MaximumFileBytes);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("openusd-viewer-settings=9\nwindowWidth=1200")]
    [Arguments("openusd-viewer-settings=1\nwindowWidth=nope")]
    [Arguments("openusd-viewer-settings=1\ndiagnosticsVisible=maybe")]
    [Arguments("openusd-viewer-settings=1\nrenderer=Unknown")]
    [Arguments("openusd-viewer-settings=1\nwindowWidth=1200\nwindowWidth=1300")]
    public async Task MalformedSettingsAreExplicitlyIgnored(string content)
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(content);

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Malformed);
        await Assert.That(result.Settings).IsEqualTo(ViewerSettings.Default);
        await Assert.That(result.Diagnostic).IsNotNull();
    }

    [Test]
    public async Task LegacySettingsAreMigratedWithNewFieldsDefaulted()
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "version=0\nwidth=1280\nheight=720\nrenderer=D3D12\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(result.Settings.WindowWidth).IsEqualTo(1280);
        await Assert.That(result.Settings.WindowHeight).IsEqualTo(720);
        await Assert.That(result.Settings.RendererPreference).IsEqualTo("D3D12");
        await Assert.That(result.Settings.DiagnosticsVisible).IsTrue();
        await Assert.That(result.Settings.SnapTimelineToFrames).IsFalse();
    }

    [Test]
    public async Task PersistenceIoFailureIsSurfaced()
    {
        string root = CreateTestRoot();
        try
        {
            string rootFile = Path.Combine(root, "occupied");
            await File.WriteAllTextAsync(rootFile, "not a directory");
            using var store = new ViewerSettingsStore(rootFile);

            await Assert.That(() => store.SaveAsync(ViewerSettings.Default))
                .Throws<IOException>();
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    private static string CreateTestRoot()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "viewer-settings-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTestRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
