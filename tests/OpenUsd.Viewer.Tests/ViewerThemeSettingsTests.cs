// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerThemeSettingsTests
{
    [Test]
    public async Task ANewProfileFollowsTheSystemAndKeepsDeveloperTabsHidden()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory, "viewer-theme-tests", Guid.NewGuid().ToString("N"));
        using var store = new ViewerSettingsStore(root);

        ViewerSettingsLoadResult result = await store.LoadAsync();

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Missing);
        await Assert.That(result.Settings.ThemePreference).IsEqualTo(ViewerThemePreference.System);
        await Assert.That(result.Settings.DiagnosticsVisible).IsFalse();
        await Assert.That(result.Settings.HydraVisible).IsFalse();
        await Assert.That(result.Settings.TfDebugVisible).IsFalse();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ProfilesWithoutAThemeMigrateWithoutChangingTheirExistingLayout(int version)
    {
        string header = version == 0 ? "version=0" : $"openusd-viewer-settings={version}";
        string widthKey = version == 0 ? "width" : "windowWidth";
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            $"{header}\n{widthKey}=1280\nrenderer=Vulkan\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Migrated);
        await Assert.That(result.Settings.ThemePreference).IsEqualTo(ViewerThemePreference.System);
        await Assert.That(result.Settings.WindowWidth).IsEqualTo(1280);
        await Assert.That(result.Settings.RendererPreference).IsEqualTo("Vulkan");
        await Assert.That(result.Settings.HydraVisible).IsEqualTo(version < 2);
        await Assert.That(result.Settings.TfDebugVisible).IsEqualTo(version < 2);
    }

    [Test]
    [Arguments("system", (int)ViewerThemePreference.System)]
    [Arguments("light", (int)ViewerThemePreference.Light)]
    [Arguments("dark", (int)ViewerThemePreference.Dark)]
    public async Task PersistedThemeTokensHaveStableMeanings(string token, int expected)
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            $"openusd-viewer-settings=4\ntheme={token}\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
        await Assert.That(result.Settings.ThemePreference).IsEqualTo((ViewerThemePreference)expected);
        await Assert.That(result.Diagnostic).IsNull();
    }

    [Test]
    public async Task InvalidInMemoryThemesCannotOverwriteASavedProfile()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory, "viewer-theme-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ViewerSettingsStore(root);
            ViewerSettings valid = ViewerSettings.Default with
            {
                ThemePreference = ViewerThemePreference.Dark,
                RendererPreference = "Metal"
            };
            await store.SaveAsync(valid);

            await Assert.That(() => store.SaveAsync(
                valid with { ThemePreference = (ViewerThemePreference)99 }))
                .Throws<ArgumentException>();
            await Assert.That((await store.LoadAsync()).Settings).IsEqualTo(valid);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments(0, "sepia")]
    [Arguments(1, "sepia")]
    [Arguments(2, "sepia")]
    [Arguments(3, "sepia")]
    [Arguments(4, "sepia")]
    [Arguments(4, "Dark")]
    [Arguments(4, "")]
    [Arguments(4, "  ")]
    public async Task InvalidThemeFallsBackWithADiagnosticWithoutDiscardingTheProfile(
        int version,
        string token)
    {
        string header = version == 0 ? "version=0" : $"openusd-viewer-settings={version}";
        string widthKey = version == 0 ? "width" : "windowWidth";
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            $"{header}\n{widthKey}=1280\nrenderer=Vulkan\ntheme={token}\n");

        await Assert.That(result.Status).IsEqualTo(
            version == 4 ? ViewerSettingsLoadStatus.Loaded : ViewerSettingsLoadStatus.Migrated);
        await Assert.That(result.Settings.ThemePreference).IsEqualTo(ViewerThemePreference.System);
        await Assert.That(result.Settings.WindowWidth).IsEqualTo(1280);
        await Assert.That(result.Settings.RendererPreference).IsEqualTo("Vulkan");
        await Assert.That(result.Diagnostic).Contains("theme");
    }

    [Test]
    [Arguments((int)ViewerThemePreference.System)]
    [Arguments((int)ViewerThemePreference.Light)]
    [Arguments((int)ViewerThemePreference.Dark)]
    public async Task ThemeRoundTripsWithoutChangingOtherPreferences(
        int preference)
    {
        string root = Path.Combine(
            AppContext.BaseDirectory, "viewer-theme-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ViewerSettingsStore(root);
            ViewerSettings settings = ViewerSettings.Default with
            {
                ThemePreference = (ViewerThemePreference)preference,
                WindowWidth = 1280,
                WindowHeight = 720,
                StagePanelWidth = 240,
                InspectorPanelWidth = 300,
                RendererPreference = "Vulkan",
                SelectedTabId = ViewerInspectorLayoutPolicy.LayersTabId,
                StagePanelVisible = false,
                InspectorPanelVisible = false,
                TimelineVisible = false,
                DiagnosticsVisible = true,
                HydraVisible = true,
                TfDebugVisible = true,
                SnapTimelineToFrames = true,
                PickTarget = "edge",
                SelectionMode = "xray",
                ColorManagement = new ViewerColorManagement
                {
                    Enabled = true,
                    ConfigPath = Path.Combine(root, "studio.ocio"),
                    SourceColorSpace = "ACEScg",
                    Display = "sRGB",
                    View = "Film",
                    Look = "Warm"
                }
            };

            await store.SaveAsync(settings);
            ViewerSettingsLoadResult reloaded = await store.LoadAsync();

            await Assert.That(reloaded.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
            await Assert.That(reloaded.Settings).IsEqualTo(settings);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
