// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentModelTests
{
    [Test]
    public async Task HierarchyBuildsParentsDepthAndChildCountsFromFlatTraversal()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
        [
            "/World",
            "/World/Looks",
            "/World/Looks/Material",
            "/World/Geometry",
            "/World/Geometry/Cube"
        ]);

        ViewerHierarchyEntry world = snapshot.Entries.Single(entry => entry.Path == "/World");
        ViewerHierarchyEntry material =
            snapshot.Entries.Single(entry => entry.Path == "/World/Looks/Material");

        await Assert.That(world.Name).IsEqualTo("World");
        await Assert.That(world.TypeName).IsEqualTo(string.Empty);
        await Assert.That(world.ParentPath).IsNull();
        await Assert.That(world.Depth).IsEqualTo(0);
        await Assert.That(world.ChildCount).IsEqualTo(2);
        await Assert.That(material.ParentPath).IsEqualTo("/World/Looks");
        await Assert.That(material.Depth).IsEqualTo(2);
    }

    [Test]
    public async Task FilteringPreservesMatchingAncestorsAndDropsUnrelatedBranches()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
        [
            "/World",
            "/World/Looks",
            "/World/Looks/BlueMaterial",
            "/World/Geometry",
            "/World/Geometry/Cube"
        ]);

        ViewerHierarchySnapshot filtered = snapshot.Filter("blue");

        await Assert.That(filtered.Entries.Select(entry => entry.Path))
            .IsEquivalentTo(["/World", "/World/Looks", "/World/Looks/BlueMaterial"]);
        await Assert.That(filtered.Contains("/World/Geometry")).IsFalse();
    }

    [Test]
    public async Task FilteringMatchesPrimTypesAndKeepsFirstMiddleAndLastMatches()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
        [
            new ViewerHierarchySourceEntry("/World", "Xform"),
            new ViewerHierarchySourceEntry("/World/FirstMesh", "Mesh"),
            new ViewerHierarchySourceEntry("/World/Scope", "Scope"),
            new ViewerHierarchySourceEntry("/World/Scope/MiddleMesh", "Mesh"),
            new ViewerHierarchySourceEntry("/World/Scope/Camera", "Camera"),
            new ViewerHierarchySourceEntry("/World/LastMesh", "Mesh")
        ]);

        ViewerHierarchySnapshot filtered = snapshot.Filter(new ViewerHierarchyFilter(
            NameQuery: null,
            TypeQuery: "mesh"));

        await Assert.That(filtered.Entries.Select(entry => entry.Path))
            .IsEquivalentTo(
            [
                "/World",
                "/World/FirstMesh",
                "/World/Scope",
                "/World/Scope/MiddleMesh",
                "/World/LastMesh"
            ]);
        await Assert.That(filtered.Entries[1].TypeName).IsEqualTo("Mesh");
        await Assert.That(filtered.Entries[3].Name).IsEqualTo("MiddleMesh");
        await Assert.That(filtered.Entries[^1].Path).IsEqualTo("/World/LastMesh");
        await Assert.That(filtered.Contains("/World/Scope/Camera")).IsFalse();
    }

    [Test]
    public async Task FilteringCombinesNameAndTypePredicates()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
        [
            new ViewerHierarchySourceEntry("/World", "Xform"),
            new ViewerHierarchySourceEntry("/World/Looks", "Scope"),
            new ViewerHierarchySourceEntry("/World/Looks/BlueMaterial", "Material"),
            new ViewerHierarchySourceEntry("/World/Geometry", "Scope"),
            new ViewerHierarchySourceEntry("/World/Geometry/BlueCube", "Mesh")
        ]);

        ViewerHierarchySnapshot filtered = snapshot.Filter(new ViewerHierarchyFilter("blue", "mesh"));

        await Assert.That(filtered.Entries.Select(entry => entry.Path))
            .IsEquivalentTo(["/World", "/World/Geometry", "/World/Geometry/BlueCube"]);
        await Assert.That(filtered.Contains("/World/Looks/BlueMaterial")).IsFalse();
    }

    [Test]
    public async Task ExpandDepthMaterializesOnlyRequestedDepthUnlessSelectionNeedsAncestors()
    {
        ViewerHierarchyEntry root = new("/World", "World", "Xform", null, Depth: 0, ChildCount: 2);
        ViewerHierarchyEntry child = new("/World/Geom", "Geom", "Scope", "/World", Depth: 1, ChildCount: 1);
        ViewerHierarchyEntry leaf = new("/World/Geom/Cube", "Cube", "Mesh", "/World/Geom", Depth: 2, ChildCount: 0);

        await Assert.That(ViewerHierarchyExpansionPolicy.ShouldMaterializeChildren(
            root,
            expandDepth: 1,
            containsSelection: false)).IsTrue();
        await Assert.That(ViewerHierarchyExpansionPolicy.ShouldMaterializeChildren(
            child,
            expandDepth: 1,
            containsSelection: false)).IsFalse();
        await Assert.That(ViewerHierarchyExpansionPolicy.ShouldMaterializeChildren(
            child,
            expandDepth: 1,
            containsSelection: true)).IsTrue();
        await Assert.That(ViewerHierarchyExpansionPolicy.ShouldMaterializeChildren(
            leaf,
            expandDepth: 99,
            containsSelection: true)).IsFalse();
    }

    [Test]
    public async Task TreeSourceMaterializesChildrenOnlyWhenRequested()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
        [
            "/World",
            "/World/Geometry",
            "/World/Geometry/Cube"
        ]);
        var source = new ViewerHierarchyTreeSource(snapshot);
        ViewerHierarchyTreeNode world = source.Roots.Single();

        await Assert.That(world.IsChildrenMaterialized).IsFalse();
        await Assert.That(world.Children.Single().Entry.Path).IsEqualTo("/World/Geometry");
        await Assert.That(world.IsChildrenMaterialized).IsTrue();
        await Assert.That(world.Children.Single().IsChildrenMaterialized).IsFalse();
    }

    [Test]
    public async Task ScalarTextFormattingIsBoundedAndMarksTruncation()
    {
        string formatted = ViewerScalarFormatter.Bound(new string('x', 80), 20);

        await Assert.That(formatted.Length).IsEqualTo(20);
        await Assert.That(formatted).EndsWith("...");
        await Assert.That(ViewerScalarFormatter.Bound("short", 20)).IsEqualTo("short");
    }

    [Test]
    public async Task VariantSnapshotDetachesNamesAndPreservesExplicitOptionOrder()
    {
        string[] names = ["warm", string.Empty, "cool"];
        ViewerVariantSetSnapshot snapshot =
            ViewerVariantSetSnapshot.Create("look", names, selection: null);
        names[0] = "changed";

        ViewerVariantSelectionOption[] options =
            ViewerVariantSelectionOption.Create(snapshot);

        await Assert.That(snapshot is IUsdDetachedResult).IsTrue();
        await Assert.That(snapshot.VariantNames.Count).IsEqualTo(3);
        await Assert.That(snapshot.VariantNames[0]).IsEqualTo("warm");
        await Assert.That(snapshot.VariantNames[1]).IsEqualTo(string.Empty);
        await Assert.That(snapshot.VariantNames[2]).IsEqualTo("cool");
        await Assert.That(options.Length).IsEqualTo(4);
        await Assert.That(options[0].Selection).IsNull();
        await Assert.That(options[0].DisplayName).IsEqualTo("<no selection>");
        await Assert.That(options[1].Selection).IsEqualTo("warm");
        await Assert.That(options[2].DisplayName).IsEqualTo("<empty variant name>");
        await Assert.That(options[3].Selection).IsEqualTo("cool");
    }

    [Test]
    public async Task EmptyVariantAndPayloadSnapshotsRemainExplicitAndDetached()
    {
        ViewerVariantSetSnapshot variant =
            ViewerVariantSetSnapshot.Create(string.Empty, [], selection: null);
        ViewerVariantSelectionOption[] options =
            ViewerVariantSelectionOption.Create(variant);
        ViewerPayloadArcSnapshot[] payloads =
            ViewerPayloadArcSnapshot.Create(Array.Empty<UsdPayloadArc>());

        await Assert.That(variant.Name).IsEqualTo(string.Empty);
        await Assert.That(variant.Selection).IsNull();
        await Assert.That(variant.VariantNames).IsEmpty();
        await Assert.That(options.Length).IsEqualTo(1);
        await Assert.That(options[0].Selection).IsNull();
        await Assert.That(payloads).IsEmpty();
    }

    [Test]
    public async Task PayloadSnapshotsPreserveOrderAndFormattingLabelsAuthoredValues()
    {
        UsdPayloadArc[] arcs =
        [
            new("assets/first.usda", "/World/First", "layers/root.usda"),
            new(string.Empty, string.Empty, "anon:0x123:session.usda")
        ];

        ViewerPayloadArcSnapshot[] snapshots = ViewerPayloadArcSnapshot.Create(arcs);
        arcs[0] = new UsdPayloadArc("changed.usda", "/Changed", "changed.usda");

        string boundedAsset = ViewerPayloadArcFormatter.FormatAssetPath(
            $"assets/{new string('x', 80)}.usda",
            maximumLength: 64);
        string anonymousSource = ViewerPayloadArcFormatter.FormatSourceLayerIdentifier(
            snapshots[1].SourceLayerIdentifier);
        string boundedAnonymousSource = ViewerPayloadArcFormatter.FormatSourceLayerIdentifier(
            $"anon:{new string('x', 80)}",
            maximumLength: 64);

        await Assert.That(snapshots[0] is IUsdDetachedResult).IsTrue();
        await Assert.That(snapshots[0].AssetPath).IsEqualTo("assets/first.usda");
        await Assert.That(snapshots[1].AssetPath).IsEqualTo(string.Empty);
        await Assert.That(boundedAsset.Length).IsEqualTo(64);
        await Assert.That(boundedAsset).StartsWith("[relative authored asset path]");
        await Assert.That(boundedAsset).EndsWith("...");
        await Assert.That(ViewerPayloadArcFormatter.FormatTargetPrimPath(string.Empty))
            .IsEqualTo("<target layer default prim>");
        await Assert.That(anonymousSource).Contains("anonymous source layer");
        await Assert.That(anonymousSource).Contains("process-local");
        await Assert.That(boundedAnonymousSource.Length).IsEqualTo(64);
        await Assert.That(boundedAnonymousSource).EndsWith("...");
        await Assert.That(ViewerPayloadArcFormatter.FormatSourceLayerIdentifier(
            snapshots[0].SourceLayerIdentifier))
            .StartsWith("[relative source-layer identifier]");
    }

    [Test]
    public async Task CompositionFormatterShowsNodeDetailsAndErrors()
    {
        var composition = new PcpPrimIndex(
            [
                CreateCompositionNode(PcpArcType.Root, "/World", []),
                CreateCompositionNode(PcpArcType.Reference, "/World/Ref", ["root.usda", "asset.usda"])
            ],
            ["unresolved asset"]);

        string summary = ViewerCompositionFormatter.FormatSummary(composition);
        string first = ViewerCompositionFormatter.FormatNode(composition.Nodes[0], 0);
        string last = ViewerCompositionFormatter.FormatNode(composition.Nodes[1], 1);

        await Assert.That(summary).IsEqualTo("2 nodes; 1 errors");
        await Assert.That(first).Contains("#0: Root");
        await Assert.That(first).Contains("layers=0");
        await Assert.That(last).Contains("#1: Reference");
        await Assert.That(last).Contains("site=/World/Ref");
        await Assert.That(last).Contains("layers=2");
        await Assert.That(composition.Errors[0]).IsEqualTo("unresolved asset");
    }

    [Test]
    public async Task LayerStackPreservesStrengthOrderRolesAndMuteEligibility()
    {
        ViewerLayerStackSnapshot snapshot = ViewerLayerStackSnapshot.Create(
            "root.usda",
            "session.usda",
            "root.usda",
            ["session.usda", "look.usda", "root.usda"],
            ["look.usda"]);

        await Assert.That(snapshot.LocalLayerIdentifiers[0]).IsEqualTo("session.usda");
        await Assert.That(snapshot.LocalLayerIdentifiers[1]).IsEqualTo("look.usda");
        await Assert.That(snapshot.LocalLayerIdentifiers[2]).IsEqualTo("root.usda");
        await Assert.That(snapshot.Layers[0].StrengthIndex).IsEqualTo(0);
        await Assert.That(snapshot.Layers[1].StrengthIndex).IsEqualTo(1);
        await Assert.That(snapshot.Layers[2].StrengthIndex).IsEqualTo(2);
        ViewerLayerSnapshot session = snapshot.Layers[0];
        ViewerLayerSnapshot local = snapshot.Layers[1];
        ViewerLayerSnapshot root = snapshot.Layers[2];
        await Assert.That(session.IsSession).IsTrue();
        await Assert.That(session.CanChangeMuted).IsFalse();
        await Assert.That(local.Role).IsEqualTo(ViewerLayerRole.Local);
        await Assert.That(local.IsMuted).IsTrue();
        await Assert.That(local.CanChangeMuted).IsTrue();
        await Assert.That(root.IsRoot).IsTrue();
        await Assert.That(root.IsEditTarget).IsTrue();
        await Assert.That(root.CanChangeMuted).IsFalse();
    }

    [Test]
    public async Task MutedLocalLayerRetainsItsPreviousStrengthPosition()
    {
        ViewerLayerStackSnapshot previous = ViewerLayerStackSnapshot.Create(
            "root.usda",
            "session.usda",
            "session.usda",
            ["session.usda", "look.usda", "root.usda"],
            []);
        var muted = new HashSet<string>(["look.usda"], StringComparer.Ordinal);

        string[] merged = ViewerLayerStackSnapshot.PreserveMutedOrder(
            ["session.usda", "root.usda"],
            previous,
            muted);

        await Assert.That(merged[0]).IsEqualTo("session.usda");
        await Assert.That(merged[1]).IsEqualTo("look.usda");
        await Assert.That(merged[2]).IsEqualTo("root.usda");
    }

    [Test]
    public async Task RemovedUnmutedLayerIsNotRetained()
    {
        ViewerLayerStackSnapshot previous = ViewerLayerStackSnapshot.Create(
            "root.usda",
            "session.usda",
            "session.usda",
            ["session.usda", "removed.usda", "root.usda"],
            []);

        string[] merged = ViewerLayerStackSnapshot.PreserveMutedOrder(
            ["session.usda", "root.usda"],
            previous,
            new HashSet<string>(StringComparer.Ordinal));

        await Assert.That(merged).IsEquivalentTo(["session.usda", "root.usda"]);
    }

    private static PcpPrimIndexNode CreateCompositionNode(
        PcpArcType arcType,
        string sitePath,
        IReadOnlyList<string> layers) =>
        new(
            ParentIndex: -1,
            ArcType: arcType,
            IsCulled: false,
            IsInert: false,
            IsDueToAncestor: false,
            HasSpecs: true,
            CanContributeSpecs: true,
            NamespaceDepth: 1,
            DepthBelowIntroduction: 0,
            SiblingIndexAtOrigin: 0,
            SitePath: sitePath,
            IntroPath: sitePath,
            PathAtIntroduction: sitePath,
            PathAtOriginRootIntroduction: sitePath,
            LayerStackIdentifier: "root.usda",
            LayerIdentifiers: layers);
}
