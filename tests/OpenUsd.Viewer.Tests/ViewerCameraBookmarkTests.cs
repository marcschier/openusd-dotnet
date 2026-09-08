// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json.Nodes;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCameraBookmarkTests
{
    private static readonly string SourceStamp = new('A', 64);

    [Test]
    public async Task FreeViewRecordRetainsItsExactCameraTimeIdentityAndName()
    {
        var viewport = new ViewportDimensions(800, 600);
        var navigation = new ViewerCameraNavigationState(
            false, new Vector3(1, 2, 3), 5, 0.5f, 0.2f, ViewerCameraProjectionMode.Perspective,
            0.7f, 3, 0.1f, 100, 800f / 600);
        Guid id = Guid.Parse("a77aa89c-c77e-45fb-a057-5298f4688853");
        ViewerCameraBookmark original = ViewerCameraBookmark.CreateFree(
            id, "  Hero _north  ", SourceStamp, 12.5, viewport, navigation);

        string[] encoded = ViewerCameraBookmarkCodec.EncodeCatalog([original]);
        ViewerCameraBookmark decoded = ViewerCameraBookmarkCodec.DecodeCatalog(encoded, SourceStamp).Single();

        await Assert.That(decoded.Id).IsEqualTo(id);
        await Assert.That(decoded.Name).IsEqualTo("Hero _north");
        await Assert.That(decoded.SourceStamp).IsEqualTo(SourceStamp);
        await Assert.That(decoded.TimeCode).IsEqualTo(12.5);
        await Assert.That(decoded.Viewport).IsEqualTo(viewport);
        await Assert.That(decoded.FreeCamera).IsEqualTo(navigation);
        await Assert.That(decoded.Camera).IsEqualTo(navigation.CreateCameraState());
        await Assert.That(ViewerCameraBookmarkCodec.EncodeCatalog([decoded]).SequenceEqual(encoded)).IsTrue();
    }

    [Test]
    [Arguments(UsdGeomCameraProjection.Perspective)]
    [Arguments(UsdGeomCameraProjection.Orthographic)]
    public async Task AuthoredViewRetainsItsFullRolledSampleAndShiftedProjection(UsdGeomCameraProjection projection)
    {
        var viewport = new ViewportDimensions(800, 600);
        var optics = new UsdGeomCameraState(
            projection, -0.7, 1.3, -0.5, 0.5, 0.1, 100, 50, 36, 24, 2, -1, 12, 2.8);
        var transform = new UsdMatrix4d(0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 2, 3, 8, 1);
        ViewerStageCameraSnapshot sample =
            ViewerStageCameraSnapshotFactory.Create("/World/HeroCamera", 12.5, transform, optics);
        ViewerCameraBookmark original = ViewerCameraBookmark.CreateStage(
            Guid.NewGuid(), "Authored", SourceStamp, viewport, sample);

        ViewerCameraBookmark decoded = ViewerCameraBookmarkCodec.DecodeCatalog(
            ViewerCameraBookmarkCodec.EncodeCatalog([original]), SourceStamp).Single();

        await Assert.That(decoded.StageCamera).IsEqualTo(sample);
        await Assert.That(decoded.Camera).IsEqualTo(
            StageCameraProjectionMath.CreateCameraState(sample.WorldToView, optics, viewport));
        await Assert.That(decoded.TimeCode).IsEqualTo(12.5);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("bad\nname")]
    [Arguments("bad\0name")]
    public async Task InvalidNamesCannotBecomeSavedViews(string name)
    {
        await Assert.That(() => CreateFree(name)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Utf8NamesAndDuplicateIdentitiesAreBoundedWithoutTruncation()
    {
        await Assert.That(() => CreateFree(new string('é', 65))).Throws<ArgumentException>();
        ViewerCameraBookmark bookmark = CreateFree(new string('é', 64));
        await Assert.That(bookmark.Name.Length).IsEqualTo(64);
        await Assert.That(() => ViewerCameraBookmarkCodec.EncodeCatalog([bookmark, bookmark]))
            .Throws<InvalidDataException>();
        await Assert.That(() => ViewerCameraBookmarkCodec.EncodeCatalog([CreateFree("Hero"), CreateFree(" hero ")]))
            .Throws<InvalidDataException>();
        await Assert.That(() => ViewerCameraBookmarkCodec.EncodeCatalog(
            Enumerable.Range(0, 33).Select(index => CreateFree($"View {index}")).ToArray()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CatalogByteBudgetIncludesNativeArrayAndStringFraming()
    {
        string[] exact = [.. Enumerable.Repeat(new string(' ', 4092), 31), new string(' ', 4084)];
        ViewerCameraBookmarkCodec.ValidateStorage(exact);
        exact[^1] += " ";
        await Assert.That(() => ViewerCameraBookmarkCodec.ValidateStorage(exact)).Throws<InvalidDataException>();
        await Assert.That(() => ViewerCameraBookmarkCodec.ValidateStorage([new string('é', 2049)]))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("version")]
    [Arguments("matrix")]
    [Arguments("distance")]
    [Arguments("pitch")]
    [Arguments("aspect")]
    [Arguments("trim")]
    public async Task MalformedOrNormalizedPayloadsAreNotSilentlyRewritten(string corruption)
    {
        string encoded = ViewerCameraBookmarkCodec.EncodeCatalog([CreateFree("Hero")])[0];
        JsonNode root = JsonNode.Parse(encoded)!;
        switch (corruption)
        {
            case "unknown":
                root["extra"] = true;
                break;
            case "version":
                root["v"] = 2;
                break;
            case "matrix":
                root["projection"]![0] = 0;
                break;
            case "distance":
                root["free"]![3] = 0;
                break;
            case "pitch":
                root["free"]![5] = 100;
                break;
            case "aspect":
                root["width"] = 100;
                break;
            case "trim":
                root["name"] = " Hero ";
                break;
        }
        await Assert.That(() => ViewerCameraBookmarkCodec.DecodeCatalog([root.ToJsonString()], SourceStamp))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task DuplicateJsonFieldsAndWrongSourceStampRefuseTheWholeCatalog()
    {
        string encoded = ViewerCameraBookmarkCodec.EncodeCatalog([CreateFree("Hero")])[0];
        string duplicate = encoded.Replace("\"v\":1", "\"v\":1,\"v\":1", StringComparison.Ordinal);
        await Assert.That(() => ViewerCameraBookmarkCodec.DecodeCatalog([duplicate], SourceStamp))
            .Throws<InvalidDataException>();
        await Assert.That(() => ViewerCameraBookmarkCodec.DecodeCatalog([encoded], new string('B', 64)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AutomaticAndNonFiniteTimeCannotClaimExactSavedViews()
    {
        await Assert.That(() => ViewerCameraBookmark.CreateFree(Guid.NewGuid(), "Automatic", SourceStamp, 0,
            new ViewportDimensions(800, 600), ViewerCameraNavigationState.CreateLegacy(true, 800f / 600)))
            .Throws<NotSupportedException>();
        await Assert.That(() => ViewerCameraBookmark.CreateFree(Guid.NewGuid(), "Invalid", SourceStamp, double.NaN,
            new ViewportDimensions(800, 600), ViewerCameraNavigationState.CreateLegacy(false, 800f / 600)))
            .Throws<ArgumentException>();
    }

    private static ViewerCameraBookmark CreateFree(string name) => ViewerCameraBookmark.CreateFree(
        Guid.NewGuid(), name, SourceStamp, 12.5, new ViewportDimensions(800, 600),
        ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
}
