// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Covers the Tools-menu pick-target and selection-mode surface: stable command
/// identities, persisted tokens and their migration, backend capability gating,
/// and the shared selection-outline policy the render paths read.
/// </summary>
internal sealed class ViewerPickTargetPolicyTests
{
    [Test]
    [Arguments(RenderPickTarget.Primitive, "primitive", ViewerCommandIds.ToolsPickTargetPrimitive)]
    [Arguments(RenderPickTarget.Face, "face", ViewerCommandIds.ToolsPickTargetFace)]
    [Arguments(RenderPickTarget.Edge, "edge", ViewerCommandIds.ToolsPickTargetEdge)]
    [Arguments(RenderPickTarget.Point, "point", ViewerCommandIds.ToolsPickTargetPoint)]
    public async Task EveryPickTargetRoundTripsItsTokenAndCommandIdentity(
        RenderPickTarget target,
        string token,
        string commandId)
    {
        await Assert.That(ViewerPickTargetPolicy.ToToken(target)).IsEqualTo(token);
        await Assert.That(ViewerPickTargetPolicy.FromToken(token)).IsEqualTo(target);
        await Assert.That(ViewerPickTargetPolicy.ToCommandId(target)).IsEqualTo(commandId);
        await Assert.That(ViewerPickTargetPolicy.FromCommandId(commandId)).IsEqualTo(target);
    }

    [Test]
    [Arguments(ViewerSelectionMode.VisibleOnly, "visibleOnly", ViewerCommandIds.ToolsSelectionVisibleOnly)]
    [Arguments(ViewerSelectionMode.XRay, "xray", ViewerCommandIds.ToolsSelectionXRay)]
    public async Task EverySelectionModeRoundTripsItsTokenAndCommandIdentity(
        ViewerSelectionMode mode,
        string token,
        string commandId)
    {
        await Assert.That(ViewerPickTargetPolicy.ToToken(mode)).IsEqualTo(token);
        await Assert.That(ViewerPickTargetPolicy.SelectionModeFromToken(token)).IsEqualTo(mode);
        await Assert.That(ViewerPickTargetPolicy.ToCommandId(mode)).IsEqualTo(commandId);
        await Assert.That(ViewerPickTargetPolicy.SelectionModeFromCommandId(commandId))
            .IsEqualTo(mode);
    }

    /// <summary>
    /// A token this build does not recognise falls back to the default rather
    /// than rejecting the profile, so a profile written by a later build costs
    /// the user nothing but the one setting it could not apply.
    /// </summary>
    [Test]
    [Arguments("volume")]
    [Arguments("")]
    [Arguments(null)]
    public async Task AnUnknownTokenFallsBackToTheDefault(string? token)
    {
        await Assert.That(ViewerPickTargetPolicy.FromToken(token))
            .IsEqualTo(ViewerPickTargetPolicy.DefaultTarget);
        await Assert.That(ViewerPickTargetPolicy.SelectionModeFromToken(token))
            .IsEqualTo(ViewerPickTargetPolicy.DefaultSelectionMode);
    }

    /// <summary>
    /// Only the hdSilk backends answer subprim targets and composite the
    /// occluded outline; Storm answers prim picks and the visible-only outline.
    /// </summary>
    [Test]
    [Arguments(RenderBackendKind.Storm, false)]
    [Arguments(RenderBackendKind.D3D12, true)]
    [Arguments(RenderBackendKind.Vulkan, true)]
    [Arguments(RenderBackendKind.Metal, true)]
    public async Task SubprimTargetsAndXRayAreGatedOnTheBackend(
        RenderBackendKind backend,
        bool supported)
    {
        await Assert.That(ViewerPickTargetPolicy.SupportsTarget(
            backend,
            RenderPickTarget.Primitive)).IsTrue();
        await Assert.That(ViewerPickTargetPolicy.SupportsSelectionMode(
            backend,
            ViewerSelectionMode.VisibleOnly)).IsTrue();

        foreach (RenderPickTarget target in new[]
        {
            RenderPickTarget.Face,
            RenderPickTarget.Edge,
            RenderPickTarget.Point
        })
        {
            await Assert.That(ViewerPickTargetPolicy.SupportsTarget(backend, target))
                .IsEqualTo(supported);
        }

        await Assert.That(ViewerPickTargetPolicy.SupportsSelectionMode(
            backend,
            ViewerSelectionMode.XRay)).IsEqualTo(supported);
    }

    /// <summary>
    /// An unsupported combination is explained rather than silently ignored, and
    /// the explanation names both the refused target and the backends that would
    /// answer it.
    /// </summary>
    [Test]
    public async Task AnUnsupportedCombinationIsExplained()
    {
        string target = ViewerPickTargetPolicy.DescribeUnsupportedTarget(
            RenderBackendKind.Storm,
            RenderPickTarget.Edge);
        await Assert.That(target).Contains("Edge");
        await Assert.That(target).Contains("Storm");
        await Assert.That(target).Contains("D3D12");

        string mode = ViewerPickTargetPolicy.DescribeUnsupportedSelectionMode(
            RenderBackendKind.Storm,
            ViewerSelectionMode.XRay);
        await Assert.That(mode).Contains("XRay");
        await Assert.That(mode).Contains("Storm");
    }

    /// <summary>
    /// The shared policy every hdSilk render path reads applies exactly the
    /// requested mode and leaves the rest of the outline style alone.
    /// </summary>
    [Test]
    public async Task TheSharedPolicyAppliesOnlyTheMode()
    {
        try
        {
            SilkSelectionOutlineSettings before = ViewerSelectionOutlinePolicy.Current;
            ViewerSelectionOutlinePolicy.SetMode(ViewerSelectionMode.XRay);
            SilkSelectionOutlineSettings after = ViewerSelectionOutlinePolicy.Current;

            await Assert.That(after.Mode).IsEqualTo(SilkSelectionOutlineMode.XRay);
            await Assert.That(after.Color).IsEqualTo(before.Color);
            await Assert.That(after.Width).IsEqualTo(before.Width);
            await Assert.That(after.Enabled).IsEqualTo(before.Enabled);
            await Assert.That(after.OccludedColor).IsEqualTo(before.OccludedColor);

            ViewerSelectionOutlinePolicy.SetMode(ViewerSelectionMode.VisibleOnly);
            await Assert.That(ViewerSelectionOutlinePolicy.Current.Mode)
                .IsEqualTo(SilkSelectionOutlineMode.VisibleOnly);
        }
        finally
        {
            ViewerSelectionOutlinePolicy.Reset();
        }
    }

    /// <summary>
    /// Both new commands are declared in the catalog with an accessible name and
    /// the radio semantics their menu group uses, so the accessibility surface
    /// has one source.
    /// </summary>
    [Test]
    public async Task BothNewCommandGroupsAreDeclaredAsAccessibleRadioItems()
    {
        string[] pickTargets =
        [
            ViewerCommandIds.ToolsPickTargetPrimitive,
            ViewerCommandIds.ToolsPickTargetFace,
            ViewerCommandIds.ToolsPickTargetEdge,
            ViewerCommandIds.ToolsPickTargetPoint
        ];
        string[] selectionModes =
        [
            ViewerCommandIds.ToolsSelectionVisibleOnly,
            ViewerCommandIds.ToolsSelectionXRay
        ];

        foreach (string id in pickTargets)
        {
            ViewerCommandDescriptor descriptor = Find(id);
            await Assert.That(descriptor.Group).IsEqualTo(ViewerCommandGroup.Tools);
            await Assert.That(descriptor.CheckKind).IsEqualTo(ViewerCommandCheckKind.Radio);
            await Assert.That(descriptor.RadioGroup).IsEqualTo("tools.pickTarget");
            await Assert.That(descriptor.AccessibleName.Trim()).IsNotEmpty();
        }

        foreach (string id in selectionModes)
        {
            ViewerCommandDescriptor descriptor = Find(id);
            await Assert.That(descriptor.Group).IsEqualTo(ViewerCommandGroup.Tools);
            await Assert.That(descriptor.CheckKind).IsEqualTo(ViewerCommandCheckKind.Radio);
            await Assert.That(descriptor.RadioGroup).IsEqualTo("tools.selection");
            await Assert.That(descriptor.AccessibleName.Trim()).IsNotEmpty();
        }
    }

    /// <summary>
    /// The desired pick target survives a session on a backend that cannot
    /// answer it, and is restored the moment a capable backend attaches.
    /// </summary>
    /// <remarks>
    /// This is the restart and backend-switch case: a profile that asked for
    /// edge picking must still say "edge" after the Viewer opened on Storm,
    /// and the effective target must become edge again on D3D12 without the
    /// user re-choosing it.
    /// </remarks>
    [Test]
    public async Task ADesiredTargetSurvivesAnIncapableBackendAndIsRestoredByACapableOne()
    {
        const RenderPickTarget desired = RenderPickTarget.Edge;

        RenderPickTarget onStorm = ViewerPickTargetPolicy.ResolveEffectiveTarget(
            RenderBackendKind.Storm,
            desired);
        RenderPickTarget onD3D12 = ViewerPickTargetPolicy.ResolveEffectiveTarget(
            RenderBackendKind.D3D12,
            desired);
        RenderPickTarget backOnStorm = ViewerPickTargetPolicy.ResolveEffectiveTarget(
            RenderBackendKind.Storm,
            desired);

        await Assert.That(onStorm).IsEqualTo(RenderPickTarget.Primitive);
        await Assert.That(onD3D12).IsEqualTo(desired);
        await Assert.That(backOnStorm).IsEqualTo(RenderPickTarget.Primitive);

        // The persisted token is written from the desired value, so the round
        // trip through a Storm-only session is lossless.
        string token = ViewerPickTargetPolicy.ToToken(desired);
        await Assert.That(ViewerPickTargetPolicy.FromToken(token)).IsEqualTo(desired);
    }

    /// <summary>The x-ray mode is remembered on the same terms as the pick target.</summary>
    [Test]
    public async Task ADesiredXRayModeSurvivesAnIncapableBackendAndIsRestoredByACapableOne()
    {
        const ViewerSelectionMode desired = ViewerSelectionMode.XRay;

        await Assert.That(ViewerPickTargetPolicy.ResolveEffectiveSelectionMode(
                RenderBackendKind.Storm,
                desired))
            .IsEqualTo(ViewerSelectionMode.VisibleOnly);
        await Assert.That(ViewerPickTargetPolicy.ResolveEffectiveSelectionMode(
                RenderBackendKind.Vulkan,
                desired))
            .IsEqualTo(desired);
        await Assert.That(ViewerPickTargetPolicy.ToToken(desired)).IsEqualTo("xray");
    }

    /// <summary>
    /// A supported request resolves to itself on every backend that answers it,
    /// so reconciliation is idempotent and a switch between capable backends
    /// changes nothing.
    /// </summary>
    [Test]
    [Arguments(RenderBackendKind.D3D12)]
    [Arguments(RenderBackendKind.Vulkan)]
    [Arguments(RenderBackendKind.Metal)]
    public async Task ReconciliationIsIdempotentOnCapableBackends(RenderBackendKind backend)
    {
        foreach (RenderPickTarget target in Enum.GetValues<RenderPickTarget>())
        {
            RenderPickTarget once =
                ViewerPickTargetPolicy.ResolveEffectiveTarget(backend, target);
            await Assert.That(
                    ViewerPickTargetPolicy.ResolveEffectiveTarget(backend, once))
                .IsEqualTo(once);
            await Assert.That(once).IsEqualTo(target);
        }
    }

    /// <summary>
    /// The Viewer persists what the user asked for, not what the attached
    /// backend allowed, and re-resolves it whenever the backend changes.
    /// </summary>
    [Test]
    public async Task TheWindowPersistsTheDesiredPolicyAndReconcilesOnBackendChange()
    {
        string menus = await ReadViewerSourceAsync("MainWindow.Menus.cs");
        string window = await ReadViewerSourceAsync("MainWindow.axaml.cs");

        await Assert.That(menus).Contains("internal RenderPickTarget DesiredPickTarget");
        await Assert.That(menus).Contains(
            "internal ViewerSelectionMode DesiredSelectionMode");
        await Assert.That(menus).Contains("DesiredPickTarget = target;");
        await Assert.That(menus).Contains("DesiredSelectionMode = mode;");
        await Assert.That(menus).Contains("internal void ReconcilePickPolicyWithBackend()");
        await Assert.That(window).Contains(
            "PickTarget = ViewerPickTargetPolicy.ToToken(DesiredPickTarget)");
        await Assert.That(window).Contains(
            "SelectionMode = ViewerPickTargetPolicy.ToToken(DesiredSelectionMode)");
        await Assert.That(window).Contains("ReconcilePickPolicyWithBackend();");
    }

    /// <summary>
    /// A reported element index is named by its kind, so the status line says
    /// "face=7" rather than an anonymous "subprim=7" the user cannot interpret.
    /// </summary>
    [Test]
    [Arguments(SelectionElementKind.Face, "face")]
    [Arguments(SelectionElementKind.Edge, "edge")]
    [Arguments(SelectionElementKind.Point, "point")]
    [Arguments(SelectionElementKind.None, "subprim")]
    [Arguments(SelectionElementKind.Unspecified, "subprim")]
    public async Task EveryElementKindIsNamedForTheStatusLine(
        SelectionElementKind kind,
        string expected)
    {
        await Assert.That(ViewerPickTargetPolicy.DescribeElementKind(kind))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// A hit for one pick target must carry the matching element kind, so a
    /// face index can never be delivered as an answer to an edge request.
    /// </summary>
    [Test]
    [Arguments(RenderPickTarget.Primitive, SelectionElementKind.None)]
    [Arguments(RenderPickTarget.Face, SelectionElementKind.Face)]
    [Arguments(RenderPickTarget.Edge, SelectionElementKind.Edge)]
    [Arguments(RenderPickTarget.Point, SelectionElementKind.Point)]
    public async Task EveryPickTargetRequiresItsOwnElementKind(
        RenderPickTarget target,
        SelectionElementKind kind)
    {
        await Assert.That(RenderPickResult.ExpectedElementKind(target))
            .IsEqualTo(kind);

        var request = new RenderPickRequest(
            1,
            1,
            new ViewportDimensions(8, 8),
            requestedStateRevision: 1,
            requestedSceneRevision: null,
            target);
        var correct = new SelectionItem(
            "/World/Cube",
            instancerPath: null,
            instanceIndex: null,
            elementIndex: kind == SelectionElementKind.None ? null : 3,
            elementKind: kind);
        RenderPickResult hit = RenderPickResult.Hit(request, 1, null, correct);
        await Assert.That(hit.Item!.Value.ElementKind).IsEqualTo(kind);

        SelectionElementKind wrongKind = kind == SelectionElementKind.Face
            ? SelectionElementKind.Edge
            : SelectionElementKind.Face;
        var wrong = new SelectionItem(
            "/World/Cube",
            instancerPath: null,
            instanceIndex: null,
            elementIndex: 3,
            elementKind: wrongKind);
        _ = Assert.Throws<ArgumentException>(
            () => RenderPickResult.Hit(request, 1, null, wrong));
    }

    private static Task<string> ReadViewerSourceAsync(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        string root = directory?.FullName ??
            throw new InvalidOperationException("Could not locate repository root.");
        return File.ReadAllTextAsync(
            Path.Combine(root, "src", "OpenUsd.Viewer", name));
    }

    private static ViewerCommandDescriptor Find(string id) =>
        ViewerCommandCatalog.Get(id);
}
