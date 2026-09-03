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
                SelectedTabId = ViewerInspectorLayoutPolicy.ValueTabId,
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
                SelectedTabId = ViewerInspectorLayoutPolicy.CompositionTabId,
                InspectorPanelVisible = false,
                TimelineVisible = false,
                DiagnosticsVisible = true,
                HydraVisible = true,
                TfDebugVisible = true
            };

            await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(second));
            ViewerSettingsLoadResult loaded = await store.LoadAsync();

            await Assert.That(loaded.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
            await Assert.That(loaded.Settings == first || loaded.Settings == second).IsTrue();
            await Assert.That(Directory.GetFiles(root, "*.tmp")).IsEmpty();
            string[] lines = await File.ReadAllLinesAsync(store.StorePath);
            await Assert.That(lines[0]).IsEqualTo("openusd-viewer-settings=3");
            await Assert.That(lines.Any(line => line.StartsWith(
                "snapTimelineToFrames=",
                StringComparison.Ordinal))).IsTrue();
            await Assert.That(lines.Any(line => line.StartsWith(
                "selectedTabId=",
                StringComparison.Ordinal))).IsTrue();
            await Assert.That(lines.Any(line => line.StartsWith(
                "hydraVisible=",
                StringComparison.Ordinal))).IsTrue();
            await Assert.That(lines.Any(line => line.StartsWith(
                "tfDebugVisible=",
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
    [Arguments("openusd-viewer-settings=2\nwindowWidth=nope")]
    [Arguments("openusd-viewer-settings=2\ndiagnosticsVisible=maybe")]
    [Arguments("openusd-viewer-settings=2\nrenderer=Unknown")]
    [Arguments("openusd-viewer-settings=2\nwindowWidth=1200\nwindowWidth=1300")]
    [Arguments("openusd-viewer-settings=2\nselectedTabId=settings")]
    [Arguments("openusd-viewer-settings=2\nselectedTabId=bogus-tab")]
    [Arguments("openusd-viewer-settings=1\nselectedTab=10")]
    [Arguments("openusd-viewer-settings=1\nselectedTab=-1")]
    [Arguments("openusd-viewer-settings=1\nselectedTab=nope")]
    public async Task MalformedSettingsAreExplicitlyIgnored(string content)
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(content);

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Malformed);
        await Assert.That(result.Settings).IsEqualTo(ViewerSettings.Default);
        await Assert.That(result.Diagnostic).IsNotNull();
    }

    /// <summary>
    /// A v2 profile has neither pick-target nor selection-mode key, and
    /// migration is exactly that: both fall back to the defaults that reproduce
    /// pre-v3 behaviour, and the profile is rewritten with them on save.
    /// </summary>
    [Test]
    public async Task V2SettingsMigrateToTheDefaultPickTargetAndSelectionMode()
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=2\nwindowWidth=1280\nrenderer=Vulkan\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(result.Settings.PickTarget).IsEqualTo("primitive");
        await Assert.That(result.Settings.SelectionMode).IsEqualTo("visibleOnly");
        await Assert.That(result.Settings.RendererPreference).IsEqualTo("Vulkan");
    }

    /// <summary>
    /// A v3 profile round-trips both keys, and an unrecognised token falls back
    /// to the default rather than rejecting the whole profile.
    /// </summary>
    [Test]
    [Arguments("edge", "xray", "edge", "xray")]
    [Arguments("point", "visibleOnly", "point", "visibleOnly")]
    [Arguments("volume", "hologram", "primitive", "visibleOnly")]
    public async Task V3PickTargetAndSelectionModeRoundTripOrFallBack(
        string persistedTarget,
        string persistedMode,
        string expectedTarget,
        string expectedMode)
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=3\n" +
            $"pickTarget={persistedTarget}\n" +
            $"selectionMode={persistedMode}\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
        await Assert.That(result.Settings.PickTarget).IsEqualTo(expectedTarget);
        await Assert.That(result.Settings.SelectionMode).IsEqualTo(expectedMode);
    }

    /// <summary>
    /// A directly constructed profile carrying an unknown token is refused by
    /// validation, so nothing but the parser's own normalization can produce one.
    /// </summary>
    [Test]
    public async Task AnUnknownPickTargetTokenIsInvalid()
    {
        await Assert.That(
            (ViewerSettings.Default with { PickTarget = "volume" }).IsValid())
            .IsFalse();
        await Assert.That(
            (ViewerSettings.Default with { SelectionMode = "hologram" }).IsValid())
            .IsFalse();
        await Assert.That(ViewerSettings.Default.IsValid()).IsTrue();
    }

    [Test]
    public async Task LegacyV0SettingsPreserveTheAlwaysVisibleDebugTabLayout()
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "version=0\nwidth=1280\nheight=720\nrenderer=D3D12\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(result.Settings.WindowWidth).IsEqualTo(1280);
        await Assert.That(result.Settings.WindowHeight).IsEqualTo(720);
        await Assert.That(result.Settings.RendererPreference).IsEqualTo("D3D12");
        // v0 predates every tab-visibility flag and always showed Diagnostics, Hydra, and
        // TfDebug, so migrating a v0 profile must preserve that layout rather than silently
        // hiding tabs the user had never asked to hide. Only genuinely new (missing-file)
        // profiles get the clean v2 default of hidden.
        await Assert.That(result.Settings.DiagnosticsVisible).IsTrue();
        await Assert.That(result.Settings.HydraVisible).IsTrue();
        await Assert.That(result.Settings.TfDebugVisible).IsTrue();
        await Assert.That(result.Settings.SelectedTabId)
            .IsEqualTo(ViewerInspectorLayoutPolicy.PropertiesTabId);
        await Assert.That(result.Settings.SnapTimelineToFrames).IsFalse();
    }

    [Test]
    [Arguments(0, "properties")]
    [Arguments(1, "value")]
    [Arguments(2, "metadata")]
    [Arguments(3, "composition")]
    [Arguments(4, "layers")]
    [Arguments(5, "diagnostics")]
    [Arguments(6, "validation")]
    [Arguments(7, "hydra")]
    [Arguments(8, "physics")]
    [Arguments(9, "tfdebug")]
    public async Task V1SelectedTabIndexIsTranslatedToTheDocumentedStableId(
        int persistedIndex,
        string expectedTabId)
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            $"openusd-viewer-settings=1\nselectedTab={persistedIndex}\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(result.Settings.SelectedTabId).IsEqualTo(expectedTabId);
    }

    [Test]
    public async Task V1MigrationPreservesDiagnosticsVisibleAndForcesHydraAndTfDebugVisible()
    {
        ViewerSettingsLoadResult hidden = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=1\ndiagnosticsVisible=false\n");
        ViewerSettingsLoadResult shown = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=1\ndiagnosticsVisible=true\n");

        await Assert.That(hidden.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(hidden.Settings.DiagnosticsVisible).IsFalse();
        await Assert.That(hidden.Settings.HydraVisible).IsTrue();
        await Assert.That(hidden.Settings.TfDebugVisible).IsTrue();

        await Assert.That(shown.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(shown.Settings.DiagnosticsVisible).IsTrue();
        await Assert.That(shown.Settings.HydraVisible).IsTrue();
        await Assert.That(shown.Settings.TfDebugVisible).IsTrue();
    }

    [Test]
    public async Task V1MigrationDefaultsDiagnosticsVisibleToTrueWhenMissing()
    {
        // v1 always showed Diagnostics unless the user had explicitly hidden it, so a v1 file
        // that predates the flag altogether preserves that always-on behavior.
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=1\nwindowWidth=1280\n");

        await Assert.That(result.Settings.DiagnosticsVisible).IsTrue();
    }

    [Test]
    public async Task NewProfileDefaultsMatchTheCleanV2Policy()
    {
        ViewerSettings defaults = ViewerSettings.Default;

        await Assert.That(defaults.DiagnosticsVisible).IsFalse();
        await Assert.That(defaults.HydraVisible).IsFalse();
        await Assert.That(defaults.TfDebugVisible).IsFalse();
        await Assert.That(defaults.StagePanelVisible).IsTrue();
        await Assert.That(defaults.InspectorPanelVisible).IsTrue();
        await Assert.That(defaults.TimelineVisible).IsTrue();
        await Assert.That(defaults.SelectedTabId)
            .IsEqualTo(ViewerInspectorLayoutPolicy.PropertiesTabId);
        await Assert.That(ViewerInspectorLayoutPolicy.IsKnownTab(defaults.SelectedTabId)).IsTrue();
    }

    [Test]
    public async Task MissingSelectedTabIdDefaultsToProperties()
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=3\nwindowWidth=1280\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
        await Assert.That(result.Settings.SelectedTabId)
            .IsEqualTo(ViewerInspectorLayoutPolicy.PropertiesTabId);
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

    [Test]
    public async Task ConcurrentSavesNeverInterleaveAndTheLastWinnerLoadsCleanly()
    {
        string root = CreateTestRoot();
        try
        {
            using var store = new ViewerSettingsStore(root);
            ViewerSettings[] revisions =
            [
                .. Enumerable.Range(0, 8).Select(index => ViewerSettings.Default with
                {
                    WindowWidth = 1000 + index * 10,
                    SelectedTabId = ViewerInspectorLayoutPolicy.Tabs[
                        index % ViewerInspectorLayoutPolicy.Tabs.Count].Id
                })
            ];

            await Task.WhenAll(revisions.Select(revision => store.SaveAsync(revision)));
            ViewerSettingsLoadResult loaded = await store.LoadAsync();

            await Assert.That(loaded.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
            await Assert.That(revisions.Contains(loaded.Settings)).IsTrue();
            await Assert.That(Directory.GetFiles(root, "*.tmp")).IsEmpty();
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
