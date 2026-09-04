// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Xml.Linq;
using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the menu-first shell contract from the Viewer redesign: one compact always-visible
/// toolbar row, a physics transport strip that is absent while physics is off and holds only
/// the transport controls once enabled, every catalogued command backed by a live, accessible
/// control, developer tabs hidden by default, and the removed Settings tab gone for good.
/// </summary>
public sealed class ViewerMenuShellTests
{
    [Test]
    public async Task EveryMenuInputGestureParsesWithTheRuntimeAvaloniaVersion()
    {
        XDocument document = await LoadMainWindowMarkupDocumentAsync();
        string[] gestures =
        [
            .. document
                .Descendants()
                .Attributes("InputGesture")
                .Select(static attribute => attribute.Value),
        ];

        await Assert.That(gestures).IsNotEmpty();
        foreach (string gesture in gestures)
        {
            await Assert.That(() => KeyGesture.Parse(gesture))
                .ThrowsNothing()
                .Because($"the Viewer loads '{gesture}' while constructing MainWindow");
        }
    }

    [Test]
    public async Task TheDefaultHeaderIsOneCompactAlwaysVisibleRow()
    {
        string markup = await LoadMainWindowMarkupAsync();

        await Assert.That(markup).Contains("x:Name=\"ViewerToolbarGrid\"");
        await Assert.That(markup).Contains("ColumnDefinitions=\"Auto,Auto,Auto,Auto,*,Auto\"");
        await Assert.That(markup).Contains("x:Name=\"OpenStageButton\"");
        await Assert.That(markup).Contains("x:Name=\"ReloadStageButton\"");
        await Assert.That(markup).Contains("x:Name=\"RendererSelector\"");
        await Assert.That(markup).Contains("x:Name=\"FrameSelectedButton\"");

        // The row that carries Menu/Open/Reload/Renderer/Frame Selected must not be split across
        // several rows the way the old three-row toolbar was.
        await Assert.That(markup).DoesNotContain("RowDefinitions=\"Auto,Auto,Auto\"");
    }

    [Test]
    public async Task EveryPlannedTopLevelMenuExists()
    {
        string markup = await LoadMainWindowMarkupAsync();

        foreach (string header in new[]
        {
            "_File", "_View", "_Render", "_Camera", "_Physics", "_Tools", "_Help",
        })
        {
            await Assert.That(markup).Contains($"Header=\"{header}\"");
        }
    }

    [Test]
    public async Task ThePhysicsTransportStripIsHiddenByDefault()
    {
        XDocument markup = await LoadMainWindowMarkupDocumentAsync();
        XElement strip = FindByName(markup, "PhysicsToolbarGrid");

        await Assert.That(strip.Attribute("IsVisible")?.Value).IsEqualTo("False");
    }

    [Test]
    public async Task ThePhysicsTransportStripHoldsOnlyPlayPauseStopStepScrubberAndStatus()
    {
        XDocument markup = await LoadMainWindowMarkupDocumentAsync();
        XElement strip = FindByName(markup, "PhysicsToolbarGrid");

        HashSet<string> names = [.. strip.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Name.NamespaceName.Contains("xaml", StringComparison.Ordinal))
            .Select(attribute => attribute.Value)];

        string[] expected =
        [
            "PhysicsCommandBar",
            "PhysicsPlayPauseButton",
            "PhysicsStopButton",
            "PhysicsStepButton",
            "PhysicsOverflowButton",
            "PhysicsBakeProgressPanel",
            "PhysicsBakeProgressBar",
            "PhysicsBakeCancelButton",
            "PhysicsScrubber",
            "PhysicsStatus",
        ];
        foreach (string name in expected)
        {
            await Assert.That(names.Contains(name)).IsTrue().Because($"missing '{name}'");
        }

        // Loop, speed, preview, bake trigger, gizmo, snap, undo/redo, and enable moved into the
        // Physics menu: none of them may leak back into the always-shown-when-enabled strip.
        string[] moved =
        [
            "PhysicsEnableButton",
            "PhysicsLoopCheckBox",
            "PhysicsSpeedSelector",
            "PhysicsPreviewCheckBox",
            "PhysicsBakeButton",
            "PhysicsGizmoSelector",
            "PhysicsSnapCheckBox",
            "PhysicsUndoButton",
            "PhysicsRedoButton",
        ];
        foreach (string name in moved)
        {
            await Assert.That(names.Contains(name)).IsFalse().Because($"'{name}' leaked into the strip");
        }
    }

    [Test]
    public async Task HiddenLegacyStateControlsAreMarkedNotVisible()
    {
        XDocument markup = await LoadMainWindowMarkupDocumentAsync();
        XElement host = FindByName(markup, "LegacyStateHost");

        await Assert.That(host.Attribute("IsVisible")?.Value).IsEqualTo("False");

        string[] namesInsideHost =
        [
            "PickModeSelector",
            "ViewportDrawModeSelector",
            "PurposeDefaultCheckBox",
            "PurposeProxyCheckBox",
            "PurposeRenderCheckBox",
            "PurposeGuideCheckBox",
            "SceneLightingCheckBox",
            "SceneShadowsCheckBox",
            "BackfaceCullingCheckBox",
            "SceneMaterialsCheckBox",
            "BackgroundColorSelector",
            "PhysicsSpeedSelector",
            "PhysicsGizmoSelector",
            "PhysicsLoopCheckBox",
            "PhysicsPreviewCheckBox",
            "PhysicsSnapCheckBox",
        ];
        HashSet<string> hostedNames = [.. host.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Name.NamespaceName.Contains("xaml", StringComparison.Ordinal))
            .Select(attribute => attribute.Value)];
        foreach (string name in namesInsideHost)
        {
            await Assert.That(hostedNames.Contains(name)).IsTrue().Because($"missing '{name}'");
        }
    }

    [Test]
    public async Task DeveloperTabsDefaultToHiddenInMarkup()
    {
        XDocument markup = await LoadMainWindowMarkupDocumentAsync();

        foreach (string tabName in new[] { "DiagnosticsTab", "HydraSceneTab", "TfDebugTab" })
        {
            XElement tab = FindByName(markup, tabName);
            await Assert.That(tab.Attribute("IsVisible")?.Value).IsEqualTo("False");
        }
    }

    [Test]
    public async Task TheSettingsTabAndItsControlsAreGoneForGood()
    {
        string markup = await LoadMainWindowMarkupAsync();

        await Assert.That(markup).DoesNotContain("x:Name=\"SettingsTab\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"SettingsState\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"StagePanelCheckBox\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"InspectorPanelCheckBox\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"TimelineCheckBox\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"DiagnosticsCheckBox\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"CaptureFrameButton\"");
    }

    [Test]
    public async Task TheCameraOrbitButtonClusterAndDuplicateCameraButtonsAreRemoved()
    {
        string markup = await LoadMainWindowMarkupAsync();

        await Assert.That(markup).DoesNotContain("x:Name=\"CameraOrbitLeftButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"CameraOrbitRightButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"CameraOrbitUpButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"CameraOrbitDownButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"ResetCameraAutomaticButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"ResetCameraLegacyButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"ToggleCameraProjectionButton\"");
        await Assert.That(markup).DoesNotContain("x:Name=\"UseSelectedCameraButton\"");

        // Frame Selected is the one camera command the plan keeps in the compact toolbar.
        await Assert.That(markup).Contains("x:Name=\"FrameSelectedButton\"");
    }

    [Test]
    public async Task EveryCatalogCommandIsBackedByALiveControlInTheMenuWiring()
    {
        // The shell's command surface is the menu wiring plus the one dialog that owns
        // commands of its own. The bridge dialog is a separate window rather than a menu
        // item, so requiring its three commands to appear in MainWindow.Menus.cs would force
        // a control into a menu purely to satisfy this test; requiring them to appear in a
        // shell source that actually applies the catalog's accessible name does not.
        string wiring = string.Concat(
            await File.ReadAllTextAsync(Path.Combine(
                FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.Menus.cs")),
            await File.ReadAllTextAsync(Path.Combine(
                FindRepositoryRoot(), "src", "OpenUsd.Viewer", "BridgeConnectionWindow.axaml.cs")));

        List<string> missing = [];
        foreach (FieldInfo field in typeof(ViewerCommandIds).GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType != typeof(string))
            {
                continue;
            }
            var id = (string)field.GetValue(null)!;
            if (!wiring.Contains($"ViewerCommandIds.{field.Name}", StringComparison.Ordinal))
            {
                missing.Add(id);
            }
        }

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task ResetLayoutAppliesTheCleanV2DefaultsTransactionally()
    {
        string wiring = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.Menus.cs"));
        string method = SliceMethod(
            wiring,
            "private async void OnResetLayoutClick",
            "private void OnPhysicsShowInspectorClick");

        // The clean defaults still come from ViewerSettings.Default, but they are applied
        // through the reset that first clears an active OpenColorIO display transform from
        // the coordinator, so the profile can never claim an untransformed image the
        // viewport is not showing.
        await Assert.That(method).Contains("await ResetLayoutAsync();");
        await Assert.That(wiring).DoesNotContain("ApplySettings(ViewerSettings.Default);");

        string colorManagement = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));
        await Assert.That(colorManagement).Contains("internal async Task ResetLayoutAsync()");
        await Assert.That(colorManagement).Contains("ViewerLayoutReset.RunAsync(");
        await Assert.That(colorManagement).Contains("ViewerSettings.Default,");
    }

    [Test]
    public async Task ShowPhysicsInspectorRevealsTheInspectorPanelAndSelectsPhysics()
    {
        // The regression this guards against: selecting the Physics tab inside a panel that is
        // still hidden achieves nothing visible, so the handler must also reveal the panel.
        string wiring = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.Menus.cs"));
        string method = SliceMethod(
            wiring,
            "private void OnPhysicsShowInspectorClick",
            "private void OnRenderRendererMenuClick");

        await Assert.That(method).Contains("InspectorPanelMenuItem.IsChecked = true;");
        await Assert.That(method).Contains("inspectorVisible: true,");
        await Assert.That(method).Contains(
            "selectedTabId: ViewerInspectorLayoutPolicy.PhysicsTabId);");
        await Assert.That(wiring).Contains(
            "PhysicsShowInspectorMenuItem.Click += OnPhysicsShowInspectorClick;");
    }

    [Test]
    public async Task HydraSceneRenderingIsSkippedEntirelyWhileTheTabIsHidden()
    {
        string window = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));

        await Assert.That(window).Contains(
            "result.Frame is { Status: RenderFrameStatus.Rendered } &&\n" +
            "                    HydraSceneTab.IsVisible &&");
    }

    [Test]
    public async Task DiagnosticsCaptureIsSkippedEntirelyWhileTheTabIsHidden()
    {
        string window = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));
        string method = SliceMethod(
            window, "private void CaptureDiagnostics", "private void RenderDiagnostics");

        await Assert.That(method).Contains("!DiagnosticsTab.IsVisible");
    }

    [Test]
    public async Task TheOmniverseBridgeMenuEntryShipsHiddenAndIsDrivenOnlyByAnInjectedProvider()
    {
        // The entry still ships hidden and disabled in markup: that is the state a Viewer with
        // no host-injected provider keeps forever. What changed is that a provider - and only a
        // provider handed to ViewerHostOptions.BridgeConnection - can now reveal it. The
        // adapter that does so lives in MainWindow.Bridge.cs and reads a computed view state
        // rather than deciding visibility for itself.
        XDocument markup = await LoadMainWindowMarkupDocumentAsync();
        XElement bridge = FindByName(markup, "ToolsOmniverseBridgeMenuItem");

        await Assert.That(bridge.Attribute("IsEnabled")?.Value).IsEqualTo("False");
        await Assert.That(bridge.Attribute("IsVisible")?.Value).IsEqualTo("False");

        string wiring = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.Menus.cs"));
        await Assert.That(wiring).DoesNotContain("ToolsOmniverseBridgeMenuItem.IsEnabled =");
        await Assert.That(wiring).DoesNotContain("ToolsOmniverseBridgeMenuItem.IsVisible =");
        await Assert.That(wiring).Contains("WireBridgeConnection();");

        string adapter = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.Bridge.cs"));
        await Assert.That(adapter).Contains(
            "ToolsOmniverseBridgeMenuItem.IsVisible = state.MenuVisible;");
        await Assert.That(adapter).Contains(
            "ToolsOmniverseBridgeMenuItem.IsEnabled = state.MenuEnabled;");
        await Assert.That(adapter).Contains("ViewerStartupOptions.BridgeConnection");

        // There is no static registration and no discovery: the only way in is the host option.
        string startup = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "ViewerStartupOptions.cs"));
        await Assert.That(startup).Contains("BridgeConnection = options.BridgeConnection;");
        await Assert.That(startup).Contains("BridgeConnection = null;");
    }

    private static async Task<string> LoadMainWindowMarkupAsync() =>
        await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Viewer", "MainWindow.axaml"));

    private static async Task<XDocument> LoadMainWindowMarkupDocumentAsync() =>
        XDocument.Parse(await LoadMainWindowMarkupAsync());

    private static XElement FindByName(XDocument markup, string name)
    {
        foreach (XElement element in markup.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.Name.LocalName == "Name" &&
                    attribute.Name.NamespaceName.Contains("xaml", StringComparison.Ordinal) &&
                    attribute.Value == name)
                {
                    return element;
                }
            }
        }

        throw new InvalidOperationException($"MainWindow.axaml does not declare '{name}'.");
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find '{startMarker}'.");
        }
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException($"Could not find '{endMarker}' after the start.");
        }
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
