// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerThemeCommandTests
{
    [Test]
    public async Task AnUnrelatedCommandCannotResetTheSelectedTheme()
    {
        var viewer = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark };
        ViewerSettings settings = ViewerSettings.Default with
        {
            ThemePreference = ViewerThemePreference.Dark
        };

        await Assert.That(() => ViewerTheme.ExecuteCommand(
            viewer, settings, ViewerCommandIds.RenderRendererStorm)).Throws<ArgumentException>();
        await Assert.That(viewer.RequestedThemeVariant).IsEqualTo(ThemeVariant.Dark);
        await Assert.That(settings.ThemePreference).IsEqualTo(ViewerThemePreference.Dark);
    }

    [Test]
    public async Task ThemeCommandsOnlyChangeTheirScopeAndSystemKeepsFollowingTheHost()
    {
        var viewerContent = new Border();
        var viewer = new ThemeVariantScope { Child = viewerContent };
        var unrelatedContent = new Border();
        var host = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Light,
            Child = new StackPanel { Children = { viewer, unrelatedContent } }
        };
        ViewerSettings original = ViewerSettings.Default with
        {
            RendererPreference = "Storm",
            StagePanelWidth = 224,
            InspectorPanelVisible = false,
            PickTarget = "face",
            SelectionMode = "xray",
            ColorManagement = new ViewerColorManagement
            {
                Enabled = true,
                ConfigPath = Path.Combine(AppContext.BaseDirectory, "theme.ocio"),
                SourceColorSpace = "ACEScg",
                Display = "sRGB",
                View = "Film"
            }
        };

        ViewerSettings dark = ViewerTheme.ExecuteCommand(
            viewer, original, ViewerCommandIds.ViewThemeDark);
        ThemeVariant darkContentTheme = viewerContent.ActualThemeVariant;
        ThemeVariant unaffectedHostTheme = host.ActualThemeVariant;
        ThemeVariant unaffectedContentTheme = unrelatedContent.ActualThemeVariant;
        host.RequestedThemeVariant = ThemeVariant.Dark;
        ViewerSettings light = ViewerTheme.ExecuteCommand(
            viewer, dark, ViewerCommandIds.ViewThemeLight);
        ThemeVariant lightContentTheme = viewerContent.ActualThemeVariant;
        ViewerSettings system = ViewerTheme.ExecuteCommand(
            viewer, light, ViewerCommandIds.ViewThemeSystem);
        ThemeVariant inheritedDark = viewerContent.ActualThemeVariant;
        host.RequestedThemeVariant = ThemeVariant.Light;
        ThemeVariant inheritedLight = viewerContent.ActualThemeVariant;

        await Assert.That(darkContentTheme).IsEqualTo(ThemeVariant.Dark);
        await Assert.That(unaffectedHostTheme).IsEqualTo(ThemeVariant.Light);
        await Assert.That(unaffectedContentTheme).IsEqualTo(ThemeVariant.Light);
        await Assert.That(lightContentTheme).IsEqualTo(ThemeVariant.Light);
        await Assert.That(inheritedDark).IsEqualTo(ThemeVariant.Dark);
        await Assert.That(inheritedLight).IsEqualTo(ThemeVariant.Light);
        await Assert.That(viewer.RequestedThemeVariant).IsEqualTo(ThemeVariant.Default);
        await Assert.That(dark).IsEqualTo(
            original with { ThemePreference = ViewerThemePreference.Dark });
        await Assert.That(light).IsEqualTo(
            original with { ThemePreference = ViewerThemePreference.Light });
        await Assert.That(system).IsEqualTo(original);
    }

    [Test]
    [Arguments("view.theme.system", "Viewer theme: Follow system")]
    [Arguments("view.theme.light", "Viewer theme: Light")]
    [Arguments("view.theme.dark", "Viewer theme: Dark")]
    public async Task ThemeChoicesAreAccessibleViewCommandsInOneRadioGroup(
        string commandId,
        string accessibleName)
    {
        ViewerCommandDescriptor command = ViewerCommandCatalog.Get(commandId);

        await Assert.That(command.Group).IsEqualTo(ViewerCommandGroup.View);
        await Assert.That(command.CheckKind).IsEqualTo(ViewerCommandCheckKind.Radio);
        await Assert.That(command.RadioGroup).IsEqualTo("view.theme");
        await Assert.That(command.AccessibleName).IsEqualTo(accessibleName);
    }
}
