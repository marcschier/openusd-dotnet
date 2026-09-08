// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerThemeResourceTests
{
    [Test]
    public async Task EverySemanticRoleAndFluentAliasResolvesInBothContrastModes()
    {
        var viewer = new ThemeVariantScope();
        viewer.Styles.Add(new ViewerStyles());
        var contrast = new ViewerThemeContrast(viewer);
        string[] roles =
        [
            "SurfaceCanvas", "SurfaceChrome", "SurfacePanel", "SurfaceRaised",
            "TextPrimary", "TextSecondary", "TextDisabled", "TextOnAccent", "TextSelected",
            "Accent", "AccentHover", "AccentPressed", "Selection", "Border", "ControlBorder",
            "Focus", "Warning", "Error", "Success"
        ];
        foreach (ColorContrastPreference preference in new[]
        {
            ColorContrastPreference.NoPreference, ColorContrastPreference.High
        })
        {
            contrast.Apply(preference);
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                viewer.RequestedThemeVariant = variant;
                foreach (string role in roles)
                {
                    await Assert.That(BrushColor(viewer, $"Viewer{role}Brush").A).IsEqualTo(byte.MaxValue);
                }
                Color panel = BrushColor(viewer, "ViewerSurfacePanelBrush");
                foreach (string role in new[] { "TextPrimary", "TextSecondary", "Warning", "Error", "Success" })
                {
                    await Assert.That(Contrast(BrushColor(viewer, $"Viewer{role}Brush"), panel))
                        .IsGreaterThanOrEqualTo(4.5);
                }
                await Assert.That(BrushColor(viewer, "AccentButtonBackground"))
                    .IsEqualTo(BrushColor(viewer, "ViewerAccentBrush"));
                await Assert.That(BrushColor(viewer, "TextControlBorderBrushFocused"))
                    .IsEqualTo(BrushColor(viewer, "ViewerFocusBrush"));
                await Assert.That(BrushColor(viewer, "TreeViewItemBackgroundSelected"))
                    .IsEqualTo(BrushColor(viewer, "ViewerSelectionBrush"));
            }
        }
    }

    [Test]
    public async Task HighContrastUpdatesScopedBrushesWithoutChangingTheThemeChoice()
    {
        var viewer = new ThemeVariantScope
        {
            Classes = { "openusd-viewer" },
            RequestedThemeVariant = ThemeVariant.Dark
        };
        viewer.Styles.Add(new ViewerStyles());
        var contrast = new ViewerThemeContrast(viewer);
        contrast.Apply(ColorContrastPreference.High);
        Color highDarkPanel = BrushColor(viewer, "ViewerSurfacePanelBrush");
        Color highDarkText = BrushColor(viewer, "ViewerTextPrimaryBrush");
        Color highDarkBorder = BrushColor(viewer, "ViewerControlBorderBrush");
        ThemeVariant? darkPreference = viewer.RequestedThemeVariant;
        viewer.RequestedThemeVariant = ThemeVariant.Light;
        Color highLightPanel = BrushColor(viewer, "ViewerSurfacePanelBrush");
        Color highLightText = BrushColor(viewer, "ViewerTextPrimaryBrush");
        Color highLightBorder = BrushColor(viewer, "ViewerControlBorderBrush");
        contrast.Apply(ColorContrastPreference.NoPreference);
        Color restoredLightPanel = BrushColor(viewer, "ViewerSurfacePanelBrush");

        await Assert.That(highDarkPanel).IsEqualTo(Colors.Black);
        await Assert.That(highLightPanel).IsEqualTo(Colors.White);
        await Assert.That(Contrast(highDarkText, highDarkPanel)).IsGreaterThanOrEqualTo(7);
        await Assert.That(Contrast(highLightText, highLightPanel)).IsGreaterThanOrEqualTo(7);
        await Assert.That(Contrast(highDarkBorder, highDarkPanel)).IsGreaterThanOrEqualTo(3);
        await Assert.That(Contrast(highLightBorder, highLightPanel)).IsGreaterThanOrEqualTo(3);
        await Assert.That(darkPreference).IsEqualTo(ThemeVariant.Dark);
        await Assert.That(viewer.RequestedThemeVariant).IsEqualTo(ThemeVariant.Light);
        await Assert.That(restoredLightPanel).IsEqualTo(Color.Parse("#FAFAF8"));
    }

    [Test]
    public async Task ScopedStylesResolveBothPalettesWithoutRestylingHostControls()
    {
        var button = new Button { Content = "Open" };
        var panel = new Border { Classes = { "viewer-panel" }, Child = button };
        var viewer = new ThemeVariantScope
        {
            Classes = { "openusd-viewer" },
            RequestedThemeVariant = ThemeVariant.Light,
            Child = panel
        };
        var unrelated = new Button { Content = "Host action" };
        var host = new StackPanel { Children = { viewer, unrelated } };
        double hostMinimumHeight = unrelated.MinHeight;
        viewer.Styles.Add(new ViewerStyles());
        host.ApplyStyling();
        viewer.ApplyStyling();
        panel.ApplyStyling();
        button.ApplyStyling();
        unrelated.ApplyStyling();

        Color lightPanel = BrushColor(viewer, "ViewerSurfacePanelBrush");
        Color lightText = BrushColor(viewer, "ViewerTextPrimaryBrush");
        Color lightSecondary = BrushColor(viewer, "ViewerTextSecondaryBrush");
        Color lightFocus = BrushColor(viewer, "ViewerFocusBrush");
        Color lightAccent = BrushColor(viewer, "ViewerAccentBrush");
        Color lightOnAccent = BrushColor(viewer, "ViewerTextOnAccentBrush");
        Color? lightPanelBackground = (panel.Background as ISolidColorBrush)?.Color;
        viewer.RequestedThemeVariant = ThemeVariant.Dark;
        Color darkPanel = BrushColor(viewer, "ViewerSurfacePanelBrush");
        Color darkText = BrushColor(viewer, "ViewerTextPrimaryBrush");
        Color darkSecondary = BrushColor(viewer, "ViewerTextSecondaryBrush");
        Color darkFocus = BrushColor(viewer, "ViewerFocusBrush");
        Color darkAccent = BrushColor(viewer, "ViewerAccentBrush");
        Color darkOnAccent = BrushColor(viewer, "ViewerTextOnAccentBrush");

        await Assert.That(lightPanel).IsEqualTo(Color.Parse("#FAFAF8"));
        await Assert.That(darkPanel).IsEqualTo(Color.Parse("#202A31"));
        await Assert.That(lightPanelBackground).IsEqualTo(Color.Parse("#FAFAF8"));
        await Assert.That(button.MinHeight).IsEqualTo(28d);
        await Assert.That(unrelated.MinHeight).IsEqualTo(hostMinimumHeight);
        await Assert.That(unrelated.TryFindResource("ViewerSurfacePanelBrush", out _)).IsFalse();
        await Assert.That(Contrast(lightText, lightPanel)).IsGreaterThanOrEqualTo(4.5);
        await Assert.That(Contrast(lightSecondary, lightPanel)).IsGreaterThanOrEqualTo(4.5);
        await Assert.That(Contrast(darkText, darkPanel)).IsGreaterThanOrEqualTo(4.5);
        await Assert.That(Contrast(darkSecondary, darkPanel)).IsGreaterThanOrEqualTo(4.5);
        await Assert.That(Contrast(lightFocus, lightPanel)).IsGreaterThanOrEqualTo(3);
        await Assert.That(Contrast(darkFocus, darkPanel)).IsGreaterThanOrEqualTo(3);
        await Assert.That(Contrast(lightOnAccent, lightAccent)).IsGreaterThanOrEqualTo(4.5);
        await Assert.That(Contrast(darkOnAccent, darkAccent)).IsGreaterThanOrEqualTo(4.5);
    }

    private static Color BrushColor(Control scope, string key)
    {
        // There is no headless message loop to deliver queued resource-binding updates.
        scope.Dispatcher.RunJobs();
        return scope.FindResource(scope.ActualThemeVariant, key) is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"The Viewer brush '{key}' did not resolve.");
    }

    private static double Contrast(Color first, Color second)
    {
        static double linear(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double luminance(Color color) =>
            0.2126 * linear(color.R) + 0.7152 * linear(color.G) + 0.0722 * linear(color.B);

        double a = luminance(first);
        double b = luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

}
