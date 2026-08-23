// Copyright (c) marcschier. Licensed under the MIT License.

using System.Xml.Linq;
using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the physics transport's user interface contract: a toolbar that never clips a control,
/// shortcuts that never fire while the user is typing, and markup that names every control for
/// assistive technology.
/// </summary>
public sealed class ViewerPhysicsUiTests
{
    private static readonly ViewerToolbarItem[] PhysicsToolbar =
    [
        new("PhysicsEnableButton", "Physics", 88),
        new("PhysicsPlayPauseButton", "Play", 72),
        new("PhysicsStopButton", "Stop", 72),
        new("PhysicsStepButton", "Step", 72),
        new("PhysicsLoopCheckBox", "Loop", 72),
        new("PhysicsSpeedSelector", "Speed", 96),
        new("PhysicsPreviewCheckBox", "Apply Preview", 132),
        new("PhysicsBakeButton", "Bake...", 84),
    ];

    [Test]
    public async Task AWideToolbarShowsEveryControlInlineWithoutAnOverflowButton()
    {
        ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(PhysicsToolbar, 4000d);

        await Assert.That(plan.Visible.Count).IsEqualTo(PhysicsToolbar.Length);
        await Assert.That(plan.HasOverflow).IsFalse();
        await Assert.That(plan.UsedWidth).IsLessThanOrEqualTo(4000d);
    }

    [Test]
    public async Task NoWidthEverProducesAPartlyDrawnControl()
    {
        // The viewer already shipped one clipped-toolbar bug, so this walks every width the window
        // can pass through rather than sampling a couple of convenient ones.
        for (double width = 0d; width <= 1200d; width += 3d)
        {
            ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(
                PhysicsToolbar,
                width);

            await Assert.That(plan.UsedWidth).IsLessThanOrEqualTo(width);
            await Assert.That(plan.Visible.Count + plan.Overflow.Count)
                .IsEqualTo(PhysicsToolbar.Length);

            double inline = 0d;
            for (int index = 0; index < plan.Visible.Count; index++)
            {
                inline += plan.Visible[index].Width + (index == 0 ? 0d : 4d);
            }

            double reserved =
                plan.HasOverflow && width >= ViewerToolbarOverflowPlanner.OverflowButtonWidth
                    ? ViewerToolbarOverflowPlanner.OverflowButtonWidth
                    : 0d;
            await Assert.That(inline + reserved).IsLessThanOrEqualTo(width + 0.001d);
        }
    }

    [Test]
    public async Task ANarrowToolbarKeepsTheAuthoredOrderAndDefersATail()
    {
        ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(PhysicsToolbar, 260d);

        await Assert.That(plan.HasOverflow).IsTrue();
        await Assert.That(plan.Visible[0].Name).IsEqualTo("PhysicsEnableButton");
        for (int index = 0; index < plan.Visible.Count; index++)
        {
            await Assert.That(plan.IsVisible(index)).IsTrue();
            await Assert.That(plan.Visible[index].Name).IsEqualTo(PhysicsToolbar[index].Name);
        }

        for (int index = 0; index < plan.Overflow.Count; index++)
        {
            await Assert.That(plan.Overflow[index].Name)
                .IsEqualTo(PhysicsToolbar[plan.Visible.Count + index].Name);
            await Assert.That(plan.IsVisible(plan.Visible.Count + index)).IsFalse();
        }
    }

    [Test]
    public async Task AToolbarTooNarrowForAnyControlDefersAllOfThem()
    {
        ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(PhysicsToolbar, 24d);

        await Assert.That(plan.Visible).IsEmpty();
        await Assert.That(plan.Overflow.Count).IsEqualTo(PhysicsToolbar.Length);
        await Assert.That(plan.UsedWidth).IsLessThanOrEqualTo(24d);
    }

    [Test]
    public async Task ShrinkingTheToolbarNeverAddsAControlBack()
    {
        int previousVisible = PhysicsToolbar.Length;
        for (double width = 1200d; width >= 0d; width -= 7d)
        {
            ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(
                PhysicsToolbar,
                width);
            await Assert.That(plan.Visible.Count).IsLessThanOrEqualTo(previousVisible);
            previousVisible = plan.Visible.Count;
        }

        await Assert.That(previousVisible).IsEqualTo(0);
    }

    [Test]
    public async Task PhysicsShortcutsRefuseToFireWhileTheUserIsTyping()
    {
        await Assert.That(ViewerPhysicsShortcutPolicy.Classify(Key.K, KeyModifiers.None, false))
            .IsEqualTo(ViewerPhysicsShortcut.PlayPause);
        await Assert.That(ViewerPhysicsShortcutPolicy.Classify(Key.K, KeyModifiers.None, true))
            .IsEqualTo(ViewerPhysicsShortcut.None);
        await Assert.That(ViewerPhysicsShortcutPolicy.Classify(Key.B, KeyModifiers.Control, false))
            .IsEqualTo(ViewerPhysicsShortcut.None);
    }

    [Test]
    public async Task EveryPhysicsControlIsNamedAndExplainedInTheMarkup()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();

        string[] names =
        [
            "PhysicsEnableButton",
            "PhysicsPlayPauseButton",
            "PhysicsStopButton",
            "PhysicsStepButton",
            "PhysicsLoopCheckBox",
            "PhysicsSpeedSelector",
            "PhysicsPreviewCheckBox",
            "PhysicsBakeButton",
            "PhysicsOverflowButton",
            "PhysicsScrubber",
        ];

        foreach (string name in names)
        {
            XElement element = FindByName(markup, name);
            await Assert.That(element.Attributes()
                .Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name"))
                .IsTrue();
        }
    }

    [Test]
    public async Task ThePhysicsToolbarRowCannotClipItsOwnControls()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();
        XElement row = FindByName(markup, "PhysicsToolbarGrid");
        XElement bar = FindByName(markup, "PhysicsCommandBar");

        await Assert.That(row.Attribute("ClipToBounds")?.Value).IsEqualTo("False");
        await Assert.That(bar.Attribute("ClipToBounds")?.Value).IsEqualTo("False");
        await Assert.That(FindByName(markup, "PhysicsOverflowButton")).IsNotNull();
    }

    [Test]
    public async Task TheInspectorExposesStatusCapabilitiesDiagnosticsAndObjects()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();

        await Assert.That(FindByName(markup, "PhysicsInspectorState")).IsNotNull();
        await Assert.That(FindByName(markup, "PhysicsCapabilityRows")).IsNotNull();
        await Assert.That(FindByName(markup, "PhysicsDiagnosticRows")).IsNotNull();
        await Assert.That(FindByName(markup, "PhysicsObjectRows")).IsNotNull();
    }

    [Test]
    public async Task TheRenderFramePumpNeverSimulatesAndNeverBlocks()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.Physics.cs"));

        int start = source.IndexOf(
            "private void PumpPhysicsRenderFrame()",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..];
        int end = body.IndexOf("private void RenderPhysicsState", StringComparison.Ordinal);
        body = body[..end];

        await Assert.That(body).DoesNotContain("await ");
        await Assert.That(body).DoesNotContain(".Result");
        await Assert.That(body).DoesNotContain(".Wait(");
        await Assert.That(body).Contains("RequestOverrideReplay");
        await Assert.That(body).Contains("PumpRenderFrame");
    }

    [Test]
    public async Task ClosingADocumentDisposesThePhysicsWorkerBeforeTheRendererGoesAway()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        int stop = window.IndexOf(
            "private async Task StopCurrentDocumentAsync()",
            StringComparison.Ordinal);
        await Assert.That(stop).IsGreaterThan(0);
        string body = window[stop..(stop + 400)];
        await Assert.That(body).Contains("await DetachPhysicsAsync();");
        await Assert.That(body.IndexOf("await DetachPhysicsAsync();", StringComparison.Ordinal))
            .IsLessThan(body.IndexOf("StopStormNavigationPolling();", StringComparison.Ordinal) + 1);
    }

    [Test]
    public async Task ARunningBakeShowsProgressAndAnAccessibleCancelControl()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();

        XElement panel = FindByName(markup, "PhysicsBakeProgressPanel");
        await Assert.That(panel.Attribute("IsVisible")?.Value).IsEqualTo("False");

        XElement cancel = FindByName(markup, "PhysicsBakeCancelButton");
        await Assert.That(cancel.Attributes()
            .Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name"))
            .IsTrue();
        await Assert.That(cancel.Attributes()
            .Any(attribute => attribute.Name.LocalName == "ToolTip.Tip"))
            .IsTrue();
        await Assert.That(cancel.Attribute("IsEnabled")?.Value).IsEqualTo("False");
        await Assert.That(FindByName(markup, "PhysicsBakeProgressBar")).IsNotNull();
    }

    [Test]
    public async Task TheBakeCancelHandlerNeverWaitsForTheGateTheBakeIsHolding()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.Physics.cs"));

        int start = source.IndexOf(
            "private void OnPhysicsBakeCancelClick",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..];
        int end = body.IndexOf("private void ShowPhysicsBakeProgress", StringComparison.Ordinal);
        body = body[..end];

        await Assert.That(body).DoesNotContain("await ");
        await Assert.That(body).DoesNotContain("RunCommandAsync");
        await Assert.That(body).Contains("_physicsBakeLifetime.Cancel()");
    }

    [Test]
    public async Task TheInspectorIsOnlyRebuiltForTheTabTheUserCanSee()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.Physics.cs"));

        int start = source.IndexOf(
            "private void RenderPhysicsInspector",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..(start + 2400)];

        // Rebinding three lists on every pumped step churned the whole inspector at ~8ms cadence.
        await Assert.That(body).Contains("PhysicsTab.IsSelected");
        await Assert.That(body).Contains("_physicsInspectorStale");
        await Assert.That(body).Contains("BindingRevision");
        await Assert.That(body).Contains("BindInspectorRows");
    }

    private static async Task<XDocument> LoadMainWindowMarkupAsync()
    {
        string markup = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        return XDocument.Parse(markup);
    }

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
