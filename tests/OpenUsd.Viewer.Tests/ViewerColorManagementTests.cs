// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

// The OCIO environment variable is process-wide, so these must not overlap.
[NotInParallel]
public sealed class ViewerColorManagementTests
{
    [Test]
    public async Task DisabledColorManagementResolvesToNoTransformAndNoDiagnostic()
    {
        bool resolved = ViewerColorManagement.Default.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);

        await Assert.That(resolved).IsFalse();
        await Assert.That(transform).IsNull();
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EnabledColorManagementWithoutAnyConfigReportsADiagnostic()
    {
        string? previous = Environment.GetEnvironmentVariable(
            ViewerColorManagement.EnvironmentVariable);
        Environment.SetEnvironmentVariable(
            ViewerColorManagement.EnvironmentVariable,
            null);
        try
        {
            var colorManagement = ViewerColorManagement.Default with { Enabled = true };
            bool resolved = colorManagement.TryResolve(
                out RenderDisplayTransform? transform,
                out string? diagnostic);

            await Assert.That(resolved).IsFalse();
            await Assert.That(transform).IsNull();
            await Assert.That(diagnostic).IsNotNull();
            await Assert.That(diagnostic!).Contains("OCIO");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ViewerColorManagement.EnvironmentVariable,
                previous);
        }
    }

    [Test]
    public async Task EnabledColorManagementResolvesTheAuthoredConfigAndNames()
    {
        string configPath = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            "studio.ocio");
        var colorManagement = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = configPath,
            SourceColorSpace = "linear",
            Display = "sRGB",
            View = "Film",
            Look = "Neutral",
        };

        bool resolved = colorManagement.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);

        await Assert.That(resolved).IsTrue();
        await Assert.That(diagnostic).IsNull();
        await Assert.That(transform!.ConfigPath).IsEqualTo(configPath);
        await Assert.That(transform.SourceColorSpace).IsEqualTo("linear");
        await Assert.That(transform.Display).IsEqualTo("sRGB");
        await Assert.That(transform.View).IsEqualTo("Film");
        await Assert.That(transform.Look).IsEqualTo("Neutral");
    }

    [Test]
    public async Task EmptyOptionalNamesResolveToTheConfigDefaults()
    {
        var colorManagement = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = Path.Combine(
                Path.GetFullPath(AppContext.BaseDirectory),
                "studio.ocio"),
        };

        _ = colorManagement.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);

        await Assert.That(diagnostic).IsNull();
        await Assert.That(transform!.Display).IsNull();
        await Assert.That(transform.View).IsNull();
        await Assert.That(transform.Look).IsNull();
    }

    [Test]
    public async Task TheEnvironmentConfigIsUsedWhenNoConfigIsAuthored()
    {
        string? previous = Environment.GetEnvironmentVariable(
            ViewerColorManagement.EnvironmentVariable);
        string configPath = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            "environment.ocio");
        Environment.SetEnvironmentVariable(
            ViewerColorManagement.EnvironmentVariable,
            configPath);
        try
        {
            var colorManagement = ViewerColorManagement.Default with { Enabled = true };
            bool resolved = colorManagement.TryResolve(
                out RenderDisplayTransform? transform,
                out string? diagnostic);

            await Assert.That(resolved).IsTrue();
            await Assert.That(diagnostic).IsNull();
            await Assert.That(transform!.ConfigPath).IsEqualTo(configPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ViewerColorManagement.EnvironmentVariable,
                previous);
        }
    }

    [Test]
    public async Task AnEscapingRelativeConfigPathIsReportedRatherThanResolved()
    {
        var colorManagement = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = Path.Combine("..", "..", "escaped.ocio"),
        };

        bool resolved = colorManagement.TryResolve(
            out RenderDisplayTransform? transform,
            out string? diagnostic);

        await Assert.That(resolved).IsFalse();
        await Assert.That(transform).IsNull();
        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task InvalidPersistedValuesAreRejected()
    {
        await Assert.That(
                (ViewerColorManagement.Default with { SourceColorSpace = "" }).IsValid())
            .IsFalse();
        await Assert.That(
                (ViewerColorManagement.Default with { Display = "a\nb" }).IsValid())
            .IsFalse();
        await Assert.That(
                (ViewerColorManagement.Default with
                {
                    ConfigPath = new string('x', 4096),
                }).IsValid())
            .IsFalse();
        await Assert.That(ViewerColorManagement.Default.IsValid()).IsTrue();
    }

    [Test]
    public async Task SettingsRoundTripTheColorManagementChoice()
    {
        var settings = ViewerSettings.Default with
        {
            ColorManagement = new ViewerColorManagement
            {
                Enabled = true,
                ConfigPath = Path.Combine(
                    Path.GetFullPath(AppContext.BaseDirectory),
                    "studio.ocio"),
                SourceColorSpace = "ACEScg",
                Display = "sRGB",
                View = "Film",
                Look = "Neutral",
            },
        };
        await Assert.That(settings.IsValid()).IsTrue();

        string root = Path.Combine(
            Path.GetFullPath(AppContext.BaseDirectory),
            $"viewer-color-management-{Guid.NewGuid():N}");
        try
        {
            using var store = new ViewerSettingsStore(root);
            await store.SaveAsync(settings);
            ViewerSettingsLoadResult result = await store.LoadAsync();

            await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
            await Assert.That(result.Settings.ColorManagement)
                .IsEqualTo(settings.ColorManagement);
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
    public async Task SettingsWithoutColorManagementKeysLoadTheDisabledDefault()
    {
        ViewerSettingsLoadResult result = ViewerSettingsStore.Parse(
            "openusd-viewer-settings=3\nrenderer=Auto\n");

        await Assert.That(result.Status).IsEqualTo(ViewerSettingsLoadStatus.Loaded);
        await Assert.That(result.Settings.ColorManagement)
            .IsEqualTo(ViewerColorManagement.Default);
        await Assert.That(result.Settings.ColorManagement.Enabled).IsFalse();
    }

    [Test]
    public async Task DisplayTransformMutationClearsTheBuiltInOutputTransform()
    {
        StageRenderState state = StageRenderState.Default.WithRenderSettings(
            RenderSettings.PresentationDefault);
        var transform = new RenderDisplayTransform(
            Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "studio.ocio"),
            "linear");

        StageRenderState applied =
            ViewerViewportStateMutation.WithDisplayTransform(state, transform);
        await Assert.That(applied.RenderSettings.OutputTransform)
            .IsEqualTo(RenderOutputTransform.Identity);
        await Assert.That(applied.RenderSettings.DisplayTransform).IsEqualTo(transform);

        StageRenderState cleared =
            ViewerViewportStateMutation.WithDisplayTransform(applied, null);
        await Assert.That(cleared.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(cleared.RenderSettings.OutputTransform)
            .IsEqualTo(RenderSettings.PresentationDefault.OutputTransform);
    }
}
