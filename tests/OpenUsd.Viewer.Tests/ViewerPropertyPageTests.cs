// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerPropertyPageTests
{
    [Test]
    public async Task EveryAttributeAndRelationshipIsReachableWithoutMaterializingAnUnboundedPage()
    {
        ViewerAttributeSnapshot[] attributes = Enumerable.Range(0, 65)
            .Select(index => new ViewerAttributeSnapshot(
                $"value{index:D2}", "double", true, false, 0, "<none>", "7"))
            .ToArray();
        ViewerRelationshipSnapshot[] relationships =
        [
            new("first", "/World/A"),
            new("second", "/World/B"),
            new("third", "/World/C"),
            new("fourth", "/World/D")
        ];

        ViewerPropertyPage first = ViewerPropertyPage.Create(attributes, relationships, null, 0);
        ViewerPropertyPage middle = ViewerPropertyPage.Create(attributes, relationships, null, 1);
        ViewerPropertyPage last = ViewerPropertyPage.Create(attributes, relationships, null, 2);

        await Assert.That(first.TotalCount).IsEqualTo(69);
        await Assert.That(first.MatchCount).IsEqualTo(69);
        await Assert.That(first.Attributes.Count).IsEqualTo(32);
        await Assert.That(first.Relationships.Count).IsEqualTo(0);
        await Assert.That(first.Attributes[0].Name).IsEqualTo("value00");
        await Assert.That(first.Attributes[^1].Name).IsEqualTo("value31");
        await Assert.That(first.HasPrevious).IsFalse();
        await Assert.That(first.HasNext).IsTrue();
        await Assert.That(middle.Attributes[0].Name).IsEqualTo("value32");
        await Assert.That(middle.Attributes[^1].Name).IsEqualTo("value63");
        await Assert.That(middle.StartIndex).IsEqualTo(32);
        await Assert.That(last.Attributes.Single().Name).IsEqualTo("value64");
        await Assert.That(last.Relationships.Select(item => item.Name))
            .IsEquivalentTo(["first", "second", "third", "fourth"]);
        await Assert.That(last.Count).IsEqualTo(5);
        await Assert.That(last.StartIndex).IsEqualTo(64);
        await Assert.That(last.HasPrevious).IsTrue();
        await Assert.That(last.HasNext).IsFalse();
    }

    [Test]
    public async Task SearchMatchesNamesAndTypesWithoutChangingAuthoredSnapshots()
    {
        ViewerAttributeSnapshot authored = new("inputs:roughness", "float", true, false, 2, "0, 1", "0.5");
        ViewerAttributeSnapshot blocked = new("inputs:color", "color3f", true, true, 0, "<none>", "<blocked>");
        ViewerAttributeSnapshot fallback = new("visibility", "token", false, false, 0, "<none>", "inherited");
        ViewerAttributeSnapshot[] attributes = [authored, blocked, fallback];
        ViewerRelationshipSnapshot relationship = new("material:binding", "/Looks/Material");
        ViewerRelationshipSnapshot[] relationships = [relationship];

        ViewerPropertyPage byName = ViewerPropertyPage.Create(attributes, relationships, "  INPUTS:  ", 0);
        ViewerPropertyPage byType = ViewerPropertyPage.Create(attributes, relationships, "COLOR3F", 0);
        ViewerPropertyPage links = ViewerPropertyPage.Create(attributes, relationships, "relationship", 0);
        ViewerPropertyPage empty = ViewerPropertyPage.Create(attributes, relationships, "not-a-property", 9);

        await Assert.That(byName.Attributes).IsEquivalentTo([authored, blocked]);
        await Assert.That(byName.MatchCount).IsEqualTo(2);
        await Assert.That(byName.TotalCount).IsEqualTo(4);
        await Assert.That(byType.Attributes.Single()).IsSameReferenceAs(blocked);
        await Assert.That(links.Relationships.Single()).IsSameReferenceAs(relationship);
        await Assert.That(empty.Count).IsEqualTo(0);
        await Assert.That(empty.PageIndex).IsEqualTo(0);
        await Assert.That(empty.HasNext).IsFalse();
        await Assert.That(empty.HasPrevious).IsFalse();
        await Assert.That(attributes).IsEquivalentTo([authored, blocked, fallback]);
    }

    [Test]
    public async Task RefreshClampsThePageAndDetachesItsBoundedCollection()
    {
        ViewerAttributeSnapshot original = new("weight", "double", true, false, 0, "<none>", "7");
        ViewerAttributeSnapshot[] attributes = [original];
        ViewerPropertyPage page = ViewerPropertyPage.Create(attributes, [], null, int.MaxValue);
        attributes[0] = original with { Value = "99" };

        await Assert.That(page.PageIndex).IsEqualTo(0);
        await Assert.That(page.StartIndex).IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(1);
        await Assert.That(page.Attributes.Single().Value).IsEqualTo("7");
        await Assert.That(page.HasPrevious).IsFalse();
        await Assert.That(page.HasNext).IsFalse();
        await Assert.That(() => ((IList<ViewerAttributeSnapshot>)page.Attributes).Add(original))
            .Throws<NotSupportedException>();
        await Assert.That(() => ViewerPropertyPage.Create(attributes, [], null, -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ACompletelyEmptyPrimHasNoNavigationOrPhantomFirstResult()
    {
        ViewerPropertyPage page = ViewerPropertyPage.Create([], [], " ", 3);

        await Assert.That(page.TotalCount).IsEqualTo(0);
        await Assert.That(page.MatchCount).IsEqualTo(0);
        await Assert.That(page.PageIndex).IsEqualTo(0);
        await Assert.That(page.StartIndex).IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(0);
        await Assert.That(page.Attributes).IsEmpty();
        await Assert.That(page.Relationships).IsEmpty();
        await Assert.That(page.HasPrevious).IsFalse();
        await Assert.That(page.HasNext).IsFalse();
    }

    [Test]
    public async Task APageFromTenThousandPropertiesCopiesOnlyItsBoundedResults()
    {
        ViewerAttributeSnapshot[] attributes = Enumerable.Range(0, 10_000)
            .Select(index => new ViewerAttributeSnapshot(
                $"weight{index:D5}", "double", true, false, 0, "<none>", "7"))
            .ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        ViewerPropertyPage page = ViewerPropertyPage.Create(attributes, [], "weight", 312);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(page.MatchCount).IsEqualTo(10_000);
        await Assert.That(page.Attributes.Count).IsEqualTo(16);
        await Assert.That(page.Attributes[0].Name).IsEqualTo("weight09984");
        await Assert.That(page.Attributes[^1].Name).IsEqualTo("weight09999");
        await Assert.That(page.HasPrevious).IsTrue();
        await Assert.That(page.HasNext).IsFalse();
        await Assert.That(allocated).IsLessThan(32L * 1024);
    }
}
