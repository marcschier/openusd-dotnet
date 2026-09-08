// Copyright (c) marcschier. Licensed under the MIT License.

using System.Xml.Linq;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerVisualContractTests
{
    [Test]
    public async Task EveryViewerSelectorRequiresTheOptInScope()
    {
        XDocument document = await LoadMarkupAsync("ViewerStyles.axaml");
        string[] selectors = [.. document.Descendants()
            .Attributes("Selector")
            .SelectMany(attribute => attribute.Value.Split(',', StringSplitOptions.TrimEntries))];

        await Assert.That(selectors).IsNotEmpty();
        await Assert.That(selectors.All(selector =>
            selector.StartsWith(".openusd-viewer ", StringComparison.Ordinal) ||
            selector == "Window.openusd-viewer")).IsTrue();
    }

    [Test]
    [Arguments("MainWindow")]
    [Arguments("AboutWindow")]
    [Arguments("ShortcutsWindow")]
    [Arguments("PhysicsBakeWindow")]
    [Arguments("BridgeConnectionWindow")]
    [Arguments("CommandPaletteWindow")]
    [Arguments("CaptureComparisonWindow")]
    public async Task ViewerWindowsOptIntoLocalStylesWithoutAnApplicationWideInclude(string name)
    {
        XDocument document = await LoadMarkupAsync($"{name}.axaml");
        XElement window = document.Root!;
        string[] classes = (window.Attribute("Classes")?.Value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        XElement? styles = window.Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Window.Styles");
        string[] includes = styles?.Descendants()
            .Where(element => element.Name.LocalName == "StyleInclude")
            .Select(element => element.Attribute("Source")?.Value ?? string.Empty)
            .ToArray() ?? [];
        XDocument app = await LoadMarkupAsync("App.axaml");

        await Assert.That(classes).Contains("openusd-viewer");
        await Assert.That(includes).Contains("avares://OpenUsd.Viewer/ViewerStyles.axaml");
        await Assert.That(app.Descendants()
            .Where(element => element.Name.LocalName == "StyleInclude")).IsEmpty();
        await Assert.That(app.Root!.Attribute("RequestedThemeVariant")?.Value).IsEqualTo("Default");
    }

    [Test]
    public async Task TheViewportFrameContainsOnlyTheExistingRendererHost()
    {
        XDocument document = await LoadMarkupAsync("MainWindow.axaml");
        XElement viewport = document.Descendants()
            .Single(element => element.Name.LocalName == "RendererSwitchingViewport");
        XElement frame = viewport.Parent!;

        await Assert.That(frame.Name.LocalName).IsEqualTo("Border");
        await Assert.That(frame.Elements().Count()).IsEqualTo(1);
        await Assert.That(viewport.Attributes().Any(attribute =>
            attribute.Name.LocalName is "Background" or "RequestedThemeVariant")).IsFalse();
    }

    [Test]
    public async Task ThemeChoicesRemainInTheViewMenuRatherThanAddingAChromeRow()
    {
        XDocument document = await LoadMarkupAsync("MainWindow.axaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement? themeMenu = document.Descendants().SingleOrDefault(
            element => element.Attribute(xaml + "Name")?.Value == "ThemeMenu");

        await Assert.That(themeMenu).IsNotNull();
        await Assert.That(themeMenu!.Parent!.Attribute("Header")?.Value).IsEqualTo("_View");
        XElement[] choices = [.. themeMenu.Elements()];
        await Assert.That(choices.Length).IsEqualTo(3);
        await Assert.That(choices.All(choice =>
            choice.Attribute("ToggleType")?.Value == "Radio" &&
            choice.Attribute("GroupName")?.Value == "view.theme")).IsTrue();
    }

    private static async Task<XDocument> LoadMarkupAsync(string name) =>
        XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", name)));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate OpenUsd.slnx.");
    }
}
