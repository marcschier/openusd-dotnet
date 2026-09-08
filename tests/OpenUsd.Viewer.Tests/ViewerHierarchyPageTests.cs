// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerHierarchyPageTests
{
    [Test]
    public async Task WideSiblingListsExposeEveryPrimThroughBoundedPages()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
            Enumerable.Range(0, 145).Select(index => $"/Prim{index:D3}"));
        var source = new ViewerHierarchyTreeSource(snapshot);

        ViewerHierarchyPage first = source.GetRootPage(0);
        ViewerHierarchyPage middle = source.GetRootPage(1);
        ViewerHierarchyPage last = source.GetRootPage(2);

        await Assert.That(source.Roots.Count).IsEqualTo(64);
        await Assert.That(first.TotalCount).IsEqualTo(145);
        await Assert.That(first.Nodes.Count).IsEqualTo(64);
        await Assert.That(first.Nodes[0].Entry.Path).IsEqualTo("/Prim000");
        await Assert.That(first.Nodes[^1].Entry.Path).IsEqualTo("/Prim063");
        await Assert.That(first.HasPrevious).IsFalse();
        await Assert.That(first.HasNext).IsTrue();
        await Assert.That(middle.StartIndex).IsEqualTo(64);
        await Assert.That(middle.Nodes[0].Entry.Path).IsEqualTo("/Prim064");
        await Assert.That(middle.Nodes[^1].Entry.Path).IsEqualTo("/Prim127");
        await Assert.That(last.StartIndex).IsEqualTo(128);
        await Assert.That(last.Nodes.Count).IsEqualTo(17);
        await Assert.That(last.Nodes[^1].Entry.Path).IsEqualTo("/Prim144");
        await Assert.That(last.HasPrevious).IsTrue();
        await Assert.That(last.HasNext).IsFalse();
        await Assert.That(last.Nodes.All(node => !node.IsChildrenMaterialized)).IsTrue();
    }

    [Test]
    public async Task RevealingADescendantFindsTheCorrectPageWithoutMatchingASiblingPrefix()
    {
        string[] paths = ["/World", "/World/Prim1",
            .. Enumerable.Range(0, 100).Select(index => $"/World/Prim{index:D3}"),
            "/World/Prim090/Leaf"];
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(paths);
        var root = new ViewerHierarchyTreeSource(snapshot).Roots.Single();

        ViewerHierarchyPage page = root.GetChildrenPage(0, "/World/Prim090/Leaf");
        ViewerHierarchyPage boundary = root.GetChildrenPage(1, "/World/Prim010");
        ViewerHierarchyPage missing = root.GetChildrenPage(1, "/World/Missing");

        await Assert.That(page.PageIndex).IsEqualTo(1);
        await Assert.That(page.Nodes.Any(node => node.Entry.Path == "/World/Prim090")).IsTrue();
        await Assert.That(page.Nodes.Any(node => node.Entry.Path == "/World/Prim1")).IsFalse();
        await Assert.That(boundary.PageIndex).IsEqualTo(0);
        await Assert.That(boundary.Nodes.Any(node => node.Entry.Path == "/World/Prim010")).IsTrue();
        await Assert.That(missing.PageIndex).IsEqualTo(1);
        await Assert.That(root.IsChildrenMaterialized).IsFalse();
    }

    [Test]
    public async Task EmptyAndShrunkenListsClampPagesAndNeverExposeMutableNodeStorage()
    {
        var empty = new ViewerHierarchyTreeSource(ViewerHierarchySnapshot.Empty);
        ViewerHierarchyPage none = empty.GetRootPage(int.MaxValue);
        await Assert.That(none.Nodes).IsEmpty();
        await Assert.That(none.TotalCount).IsEqualTo(0);
        await Assert.That(none.StartIndex).IsEqualTo(0);
        await Assert.That(none.PageIndex).IsEqualTo(0);
        await Assert.That(none.HasPrevious).IsFalse();
        await Assert.That(none.HasNext).IsFalse();

        var source = new ViewerHierarchyTreeSource(ViewerHierarchySnapshot.Build(["/A", "/B"]));
        ViewerHierarchyPage clamped = source.GetRootPage(int.MaxValue);
        await Assert.That(clamped.PageIndex).IsEqualTo(0);
        await Assert.That(clamped.Nodes.Select(node => node.Entry.Path)).IsEquivalentTo(["/A", "/B"]);
        await Assert.That(() => ((IList<ViewerHierarchyTreeNode>)clamped.Nodes).Clear())
            .Throws<NotSupportedException>();
        await Assert.That(() => source.GetRootPage(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(clamped.Nodes[0].GetChildrenPage(9).Nodes).IsEmpty();
    }

    [Test]
    public async Task APageOfTenThousandRootsDoesNotCopyOrCreateAllTheirNodes()
    {
        ViewerHierarchySnapshot snapshot = ViewerHierarchySnapshot.Build(
            Enumerable.Range(0, 10_000).Select(index => $"/Prim{index:D5}"));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var source = new ViewerHierarchyTreeSource(snapshot);
        ViewerHierarchyPage last = source.GetRootPage(156);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(last.TotalCount).IsEqualTo(10_000);
        await Assert.That(last.Nodes.Count).IsEqualTo(16);
        await Assert.That(last.Nodes[0].Entry.Path).IsEqualTo("/Prim09984");
        await Assert.That(last.Nodes[^1].Entry.Path).IsEqualTo("/Prim09999");
        await Assert.That(last.HasNext).IsFalse();
        await Assert.That(allocated).IsLessThan(64L * 1024);
    }
}
