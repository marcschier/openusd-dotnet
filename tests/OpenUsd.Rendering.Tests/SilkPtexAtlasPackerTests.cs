// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPtexAtlasPackerTests
{
    [Test]
    public async Task Pack_ThreeChannelFaces_ExpandsOpaqueAlphaAndExtrudesEdgesAndCorners()
    {
        byte[] rgb =
        [
            10, 20, 30, 40, 50, 60,
            70, 80, 90, 100, 110, 120
        ];
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 3, -1, new FaceSpec(7, 2, 2, rgb));

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 4);
        SilkPtexAtlasTile tile = atlas.Tiles[7];

        await Assert.That(atlas.Tiles.Count).IsEqualTo(1);
        await Assert.That(tile.FaceId).IsEqualTo(7u);
        await Assert.That(Convert.ToUInt32(tile.ContentWidth)).IsEqualTo(2u);
        await Assert.That(Convert.ToUInt32(tile.ContentHeight)).IsEqualTo(2u);
        await AssertTileExtrusion(atlas, tile, rgb, 3);
    }

    [Test]
    public async Task Pack_FourChannelFaces_PreservesEveryRgbaByteAndExposesNormalizedContentBounds()
    {
        byte[] rgba =
        [
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16
        ];
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 4, 3, new FaceSpec(11, 2, 2, rgba));

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 4);
        SilkPtexAtlasTile tile = atlas.Tiles[11];
        int atlasWidth = Convert.ToInt32(atlas.Width);
        int atlasHeight = Convert.ToInt32(atlas.Height);
        int contentX = Convert.ToInt32(tile.ContentX);
        int contentY = Convert.ToInt32(tile.ContentY);

        await Assert.That(ReadPixel(atlas, contentX, contentY).AsSpan().SequenceEqual(rgba.AsSpan(0, 4))).IsTrue();
        await Assert.That(ReadPixel(atlas, contentX + 1, contentY).AsSpan().SequenceEqual(rgba.AsSpan(4, 4))).IsTrue();
        await Assert.That(ReadPixel(atlas, contentX, contentY + 1).AsSpan().SequenceEqual(rgba.AsSpan(8, 4))).IsTrue();
        byte[] lowerRightPixel = ReadPixel(atlas, contentX + 1, contentY + 1);
        await Assert.That(lowerRightPixel.AsSpan().SequenceEqual(rgba.AsSpan(12, 4))).IsTrue();
        await Assert.That(Convert.ToSingle(tile.U0)).IsEqualTo((float)contentX / atlasWidth);
        await Assert.That(Convert.ToSingle(tile.V0)).IsEqualTo((float)contentY / atlasHeight);
        await Assert.That(Convert.ToSingle(tile.U1)).IsEqualTo((float)(contentX + 2) / atlasWidth);
        await Assert.That(Convert.ToSingle(tile.V1)).IsEqualTo((float)(contentY + 2) / atlasHeight);
    }

    [Test]
    [Arguments(2, 30, 40)]
    [Arguments(1, 40, 20)]
    [Arguments(0, 20, 10)]
    [Arguments(3, 10, 30)]
    public async Task Pack_AdjacentGutterRotatesAcrossPtexEdgeOrientations(
        int adjacentEdge,
        byte expectedFirst,
        byte expectedSecond)
    {
        byte[] sourcePixels = [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255];
        byte[] adjacentPixels =
        [
            10, 0, 0, 255, 20, 0, 0, 255,
            30, 0, 0, 255, 40, 0, 0, 255
        ];
        FaceSpec source = Connect(new FaceSpec(0, 2, 2, sourcePixels), 0, 1, adjacentEdge);
        FaceSpec adjacent = Connect(new FaceSpec(1, 2, 2, adjacentPixels), adjacentEdge, 0, 0);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, 1, source, adjacent);

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 8);
        SilkPtexAtlasTile tile = atlas.Tiles[0];
        int contentX = Convert.ToInt32(tile.ContentX);
        int contentY = Convert.ToInt32(tile.ContentY);

        await Assert.That(ReadPixel(atlas, contentX, contentY - 1)[0]).IsEqualTo(expectedFirst);
        await Assert.That(ReadPixel(atlas, contentX + 1, contentY - 1)[0]).IsEqualTo(expectedSecond);
    }

    [Test]
    public async Task Pack_AdjacentGutterResamplesDifferentFaceResolutions()
    {
        byte[] sourcePixels = new byte[16];
        byte[] adjacentPixels =
        [
            200, 0, 0, 255, 220, 0, 0, 255, 240, 0, 0, 255, 255, 0, 0, 255,
            0, 0, 0, 255, 20, 0, 0, 255, 40, 0, 0, 255, 60, 0, 0, 255
        ];
        FaceSpec source = Connect(new FaceSpec(0, 2, 2, sourcePixels), 0, 1, 2);
        FaceSpec adjacent = Connect(new FaceSpec(1, 4, 2, adjacentPixels), 2, 0, 0);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, 1, source, adjacent);

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 12);
        SilkPtexAtlasTile tile = atlas.Tiles[0];
        int contentX = Convert.ToInt32(tile.ContentX);
        int contentY = Convert.ToInt32(tile.ContentY);

        await Assert.That(ReadPixel(atlas, contentX, contentY - 1)[0]).IsEqualTo((byte)10);
        await Assert.That(ReadPixel(atlas, contentX + 1, contentY - 1)[0]).IsEqualTo((byte)50);
    }

    [Test]
    public async Task Pack_AdjacentCornerGutterTraversesToDiagonalFace()
    {
        FaceSpec source = new(0, 1, 1, [1, 2, 3, 255]);
        FaceSpec left = new(1, 1, 1, [4, 5, 6, 255]);
        FaceSpec bottom = new(2, 1, 1, [7, 8, 9, 255]);
        FaceSpec diagonal = new(3, 1, 1, [90, 80, 70, 255]);
        source = Connect(Connect(source, 0, 2, 2), 3, 1, 1);
        left = Connect(Connect(left, 1, 0, 3), 0, 3, 2);
        bottom = Connect(Connect(bottom, 2, 0, 0), 3, 3, 1);
        diagonal = Connect(Connect(diagonal, 2, 1, 0), 1, 2, 3);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, 1, source, left, bottom, diagonal);

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 16);
        SilkPtexAtlasTile tile = atlas.Tiles[0];

        await Assert.That(ReadPixel(
            atlas,
            Convert.ToInt32(tile.ContentX) - 1,
            Convert.ToInt32(tile.ContentY) - 1).AsSpan().SequenceEqual(new byte[] { 90, 80, 70, 255 })).IsTrue();
    }

    [Test]
    public async Task Pack_AdjacentCornerGutterClampsAtBoundaryAfterCrossingNeighborEdge()
    {
        FaceSpec source = Connect(new FaceSpec(0, 1, 1, [1, 2, 3, 255]), 3, 1, 1);
        FaceSpec adjacent = Connect(new FaceSpec(1, 1, 1, [90, 80, 70, 255]), 1, 0, 3);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, 1, source, adjacent);

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 8);
        SilkPtexAtlasTile tile = atlas.Tiles[0];

        await Assert.That(ReadPixel(
            atlas,
            Convert.ToInt32(tile.ContentX) - 1,
            Convert.ToInt32(tile.ContentY) - 1).AsSpan().SequenceEqual(new byte[] { 90, 80, 70, 255 })).IsTrue();
    }

    [Test]
    public async Task Pack_AdjacentFaceOutsideSelection_RejectsWithoutReservingBudget()
    {
        FaceSpec source = Connect(new FaceSpec(0, 1, 1, [1, 2, 3, 255]), 0, 1, 2);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, 1, source);
        var budget = new SilkCpuTextureBudget(64);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 3, budget))
            .Throws<InvalidDataException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_NonReciprocalAdjacency_RejectsWithoutReservingBudget()
    {
        FaceSpec source = Connect(new FaceSpec(0, 1, 1, [1, 2, 3, 255]), 0, 1, 2);
        FaceSpec adjacent = Connect(new FaceSpec(1, 1, 1, [4, 5, 6, 255]), 2, 0, 1);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, 1, source, adjacent);
        var budget = new SilkCpuTextureBudget(72);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 6, budget))
            .Throws<InvalidDataException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(0u, 0u)]
    [Arguments(1u, 8u)]
    public async Task Pack_TriangleOrSubfaceAdjacency_IsRejectedWithoutReservingBudget(
        uint meshType,
        uint flags)
    {
        FaceSpec source = Connect(
            new FaceSpec(0, 1, 1, [1, 2, 3, 255], Flags: flags),
            0,
            1,
            2);
        FaceSpec adjacent = Connect(
            new FaceSpec(1, 1, 1, [4, 5, 6, 255]),
            2,
            0,
            0);
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 4, 3, meshType, source, adjacent);
        var budget = new SilkCpuTextureBudget(72);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 6, budget))
            .Throws<NotSupportedException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_VariableFaceSizes_UsesUniqueNonOverlappingTilesAndExtrudesEachGutter()
    {
        byte[] face4 = Enumerable.Range(1, 24).Select(static value => (byte)value).ToArray();
        byte[] face1 = [201, 202, 203, 211, 212, 213, 221, 222, 223];
        byte[] face9 = [31, 32, 33, 41, 42, 43];
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0,
            3,
            -1,
            new FaceSpec(4, 2, 4, face4),
            new FaceSpec(1, 1, 3, face1),
            new FaceSpec(9, 2, 1, face9));

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 16);

        await Assert.That(atlas.Tiles.Count).IsEqualTo(3);
        await Assert.That(atlas.Tiles.ContainsKey(4)).IsTrue();
        await Assert.That(atlas.Tiles.ContainsKey(1)).IsTrue();
        await Assert.That(atlas.Tiles.ContainsKey(9)).IsTrue();
        await Assert.That(Convert.ToUInt32(atlas.Width)).IsLessThanOrEqualTo(16u);
        await Assert.That(Convert.ToUInt32(atlas.Height)).IsLessThanOrEqualTo(16u);
        await Assert.That(atlas.Pixels.Length).IsEqualTo(
            checked(Convert.ToInt32(atlas.Width) * Convert.ToInt32(atlas.Height) * 4));
        await Assert.That(Convert.ToUInt32(atlas.Tiles[4].ContentWidth)).IsEqualTo(2u);
        await Assert.That(Convert.ToUInt32(atlas.Tiles[4].ContentHeight)).IsEqualTo(4u);
        await Assert.That(Convert.ToUInt32(atlas.Tiles[1].ContentWidth)).IsEqualTo(1u);
        await Assert.That(Convert.ToUInt32(atlas.Tiles[1].ContentHeight)).IsEqualTo(3u);
        await Assert.That(Convert.ToUInt32(atlas.Tiles[9].ContentWidth)).IsEqualTo(2u);
        await Assert.That(Convert.ToUInt32(atlas.Tiles[9].ContentHeight)).IsEqualTo(1u);

        await AssertNoOverlappingGutterBounds(atlas);
        await AssertTileExtrusion(atlas, atlas.Tiles[4], face4, 3);
        await AssertTileExtrusion(atlas, atlas.Tiles[1], face1, 3);
        await AssertTileExtrusion(atlas, atlas.Tiles[9], face9, 3);
    }

    [Test]
    public async Task Pack_DuplicateRequestedFaceIds_ProducesOneTileAndChargesOneAtlas()
    {
        byte[] rgb = [11, 22, 33];
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0,
            3,
            -1,
            new FaceSpec(5, 1, 1, rgb),
            new FaceSpec(5, 1, 1, rgb));
        var budget = new SilkCpuTextureBudget(36);

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 3, budget);

        await Assert.That(atlas.Tiles.Count).IsEqualTo(1);
        await Assert.That(atlas.Tiles.ContainsKey(5)).IsTrue();
        await Assert.That(atlas.Pixels.Length).IsEqualTo(36);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(36ul);
    }

    [Test]
    public async Task Pack_RequestOrderPermutation_ProducesIdenticalAtlasAndFaceTiles()
    {
        FaceSpec face2 = new(2, 2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        FaceSpec face6 = new(6, 1, 2, [11, 12, 13, 14, 15, 16, 17, 18]);
        FaceSpec face3 = new(3, 1, 1, [21, 22, 23, 24]);
        using SilkDecodedPtexFaces firstInput = CreateDecoded(0, 4, 3, face2, face6, face3);
        using SilkDecodedPtexFaces reorderedInput = CreateDecoded(0, 4, 3, face3, face2, face6);
        using SilkPackedPtexAtlas first = SilkPtexAtlasPacker.Pack(firstInput, 12);
        using SilkPackedPtexAtlas reordered = SilkPtexAtlasPacker.Pack(reorderedInput, 12);

        await Assert.That(reordered.Width).IsEqualTo(first.Width);
        await Assert.That(reordered.Height).IsEqualTo(first.Height);
        await Assert.That(reordered.Pixels.AsSpan().SequenceEqual(first.Pixels)).IsTrue();
        await Assert.That(reordered.Tiles.Count).IsEqualTo(first.Tiles.Count);
        foreach (uint faceId in new uint[] { 2, 3, 6 })
        {
            SilkPtexAtlasTile expected = first.Tiles[faceId];
            SilkPtexAtlasTile actual = reordered.Tiles[faceId];
            await Assert.That(actual.FaceId).IsEqualTo(expected.FaceId);
            await Assert.That(actual.ContentX).IsEqualTo(expected.ContentX);
            await Assert.That(actual.ContentY).IsEqualTo(expected.ContentY);
            await Assert.That(actual.ContentWidth).IsEqualTo(expected.ContentWidth);
            await Assert.That(actual.ContentHeight).IsEqualTo(expected.ContentHeight);
            await Assert.That(actual.U0).IsEqualTo(expected.U0);
            await Assert.That(actual.V0).IsEqualTo(expected.V0);
            await Assert.That(actual.U1).IsEqualTo(expected.U1);
            await Assert.That(actual.V1).IsEqualTo(expected.V1);
        }
    }

    [Test]
    public async Task Pack_EmptyFaceSelection_ReturnsEmptyAtlasWithoutReservingBudget()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 3, -1);
        var budget = new SilkCpuTextureBudget(1);

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 8, budget);

        await Assert.That(atlas.Tiles.Count).IsEqualTo(0);
        await Assert.That(atlas.Pixels).IsEmpty();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_OnePixelFaceAtExactMaximumDimension_IsAccepted()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 4, 3, new FaceSpec(0, 1, 1, [9, 8, 7, 6]));

        using SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 3);

        await Assert.That(Convert.ToUInt32(atlas.Width)).IsEqualTo(3u);
        await Assert.That(Convert.ToUInt32(atlas.Height)).IsEqualTo(3u);
    }

    [Test]
    [Arguments(2u, 1u)]
    [Arguments(1u, 2u)]
    public async Task Pack_FaceGutterExceedsMaximumOnEitherAxis_ThrowsWithoutPublishing(uint faceWidth, uint faceHeight)
    {
        byte[] pixel = new byte[checked((int)(faceWidth * faceHeight * 4))];
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 4, 3, new FaceSpec(0, faceWidth, faceHeight, pixel));
        var budget = new SilkCpuTextureBudget(128);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 3, budget))
            .Throws<InvalidOperationException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_DisposalReleasesExactCpuReservationOnce()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 4, 3, new FaceSpec(0, 1, 1, [1, 2, 3, 4]));
        var budget = new SilkCpuTextureBudget(36);
        SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, 3, budget);

        await Assert.That(atlas.Pixels.Length).IsEqualTo(36);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(36ul);
        atlas.Dispose();
        atlas.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_BudgetRefusalDoesNotLeakReservation()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 4, 3, new FaceSpec(0, 1, 1, [1, 2, 3, 4]));
        var budget = new SilkCpuTextureBudget(35);

        SilkCpuTextureBudgetExceededException? failure = await Assert.That(() =>
            SilkPtexAtlasPacker.Pack(decoded, 3, budget))
            .Throws<SilkCpuTextureBudgetExceededException>();

        await Assert.That(failure!.RequestedBytes).IsEqualTo(36ul);
        await Assert.That(failure.MaximumBytes).IsEqualTo(35ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_BudgetRefusalPreservesEarlierOwnersUnchanged()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 4, 3, new FaceSpec(0, 1, 1, [1, 2, 3, 4]));
        var budget = new SilkCpuTextureBudget(39);
        using IDisposable earlierOwner = budget.Reserve(4);

        SilkCpuTextureBudgetExceededException? failure = await Assert.That(() =>
            SilkPtexAtlasPacker.Pack(decoded, 3, budget))
            .Throws<SilkCpuTextureBudgetExceededException>();

        await Assert.That(failure!.RequestedBytes).IsEqualTo(36ul);
        await Assert.That(failure.ReservedBytes).IsEqualTo(4ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(4ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
    }

    [Test]
    [Arguments(1u, 4u, 3)]
    [Arguments(2u, 3u, -1)]
    [Arguments(3u, 4u, 3)]
    [Arguments(0u, 3u, 0)]
    [Arguments(0u, 3u, 1)]
    [Arguments(0u, 3u, 2)]
    [Arguments(0u, 4u, -1)]
    [Arguments(0u, 4u, 0)]
    [Arguments(0u, 4u, 1)]
    [Arguments(0u, 4u, 2)]
    [Arguments(0u, 1u, -1)]
    [Arguments(0u, 2u, -1)]
    [Arguments(0u, 5u, -1)]
    [Arguments(4u, 3u, -1)]
    public async Task Pack_UnsupportedSamplerFormat_ThrowsNotSupportedException(
        uint dataType,
        uint channels,
        int alphaChannel)
    {
        uint bytesPerChannel = dataType switch { 1 => 2, 2 => 2, 3 => 4, _ => 1 };
        byte[] bytes = new byte[checked((int)(channels * bytesPerChannel))];
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            dataType,
            channels,
            alphaChannel,
            new FaceSpec(0, 1, 1, bytes));
        var budget = new SilkCpuTextureBudget(64);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 8, budget))
            .Throws<NotSupportedException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments("offset")]
    [Arguments("stride")]
    [Arguments("zero-width")]
    [Arguments("zero-height")]
    [Arguments("truncated-pixels")]
    public async Task Pack_MalformedFaceLayout_ThrowsWithoutLeakingBudget(string defect)
    {
        FaceSpec face = defect switch
        {
            "offset" => new FaceSpec(0, 1, 1, [1, 2, 3], DataOffset: 1),
            "stride" => new FaceSpec(0, 1, 1, [1, 2, 3], RowStride: 2),
            "zero-width" => new FaceSpec(0, 0, 1, [1, 2, 3], RowStride: 0),
            "zero-height" => new FaceSpec(0, 1, 0, [1, 2, 3]),
            "truncated-pixels" => new FaceSpec(0, 1, 1, [1, 2]),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using SilkDecodedPtexFaces decoded = CreateDecoded(0, 3, -1, face);
        var budget = new SilkCpuTextureBudget(64);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 8, budget))
            .Throws<InvalidDataException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_NonContiguousOverlappingFaceOffsets_ThrowsWithoutPublishingAtlas()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0,
            3,
            -1,
            new FaceSpec(0, 1, 1, [1, 2, 3]),
            new FaceSpec(1, 1, 1, [4, 5, 6]));
        SilkPtexFaceLayout overlappedFace = decoded.Faces[1];
        overlappedFace.DataOffsetBytes = 0;
        decoded.Faces[1] = overlappedFace;
        var budget = new SilkCpuTextureBudget(64);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 8, budget))
            .Throws<InvalidDataException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_HugeDeclaredFaceDimensionWithInsufficientStorage_RejectsBeforeAllocation()
    {
        uint width = uint.MaxValue / 4;
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0,
            4,
            3,
            new FaceSpec(0, width, 1, [1], RowStride: width * 4));
        var budget = new SilkCpuTextureBudget(64);

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, uint.MaxValue, budget))
            .Throws<InvalidDataException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task Pack_ZeroMaximumDimension_ThrowsArgumentOutOfRangeException()
    {
        using SilkDecodedPtexFaces decoded = CreateDecoded(
            0, 3, -1, new FaceSpec(0, 1, 1, [1, 2, 3]));

        await Assert.That(() => SilkPtexAtlasPacker.Pack(decoded, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Pack_NullDecodedInput_ThrowsArgumentNullException()
    {
        await Assert.That(() => SilkPtexAtlasPacker.Pack(null!, 8))
            .Throws<ArgumentNullException>();
    }

    private static async Task AssertTileExtrusion(
        SilkPackedPtexAtlas atlas,
        SilkPtexAtlasTile tile,
        byte[] sourcePixels,
        int sourceChannels)
    {
        int contentX = Convert.ToInt32(tile.ContentX);
        int contentY = Convert.ToInt32(tile.ContentY);
        int contentWidth = Convert.ToInt32(tile.ContentWidth);
        int contentHeight = Convert.ToInt32(tile.ContentHeight);
        for (int tileY = -1; tileY <= contentHeight; tileY++)
        {
            for (int tileX = -1; tileX <= contentWidth; tileX++)
            {
                int sourceX = Math.Clamp(tileX, 0, contentWidth - 1);
                int sourceY = Math.Clamp(tileY, 0, contentHeight - 1);
                int sourceIndex = (sourceY * contentWidth + sourceX) * sourceChannels;
                byte[] expected = sourceChannels == 3
                    ? [sourcePixels[sourceIndex], sourcePixels[sourceIndex + 1], sourcePixels[sourceIndex + 2], 255]
                    : sourcePixels.AsSpan(sourceIndex, 4).ToArray();
                byte[] actual = ReadPixel(atlas, contentX + tileX, contentY + tileY);
                await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
            }
        }
    }

    private static async Task AssertNoOverlappingGutterBounds(SilkPackedPtexAtlas atlas)
    {
        SilkPtexAtlasTile[] tiles = atlas.Tiles.Values.ToArray();
        for (int left = 0; left < tiles.Length; left++)
        {
            int leftX0 = Convert.ToInt32(tiles[left].ContentX) - 1;
            int leftY0 = Convert.ToInt32(tiles[left].ContentY) - 1;
            int leftX1 = Convert.ToInt32(tiles[left].ContentX) +
                         Convert.ToInt32(tiles[left].ContentWidth);
            int leftY1 = Convert.ToInt32(tiles[left].ContentY) +
                         Convert.ToInt32(tiles[left].ContentHeight);
            for (int right = left + 1; right < tiles.Length; right++)
            {
                int rightX0 = Convert.ToInt32(tiles[right].ContentX) - 1;
                int rightY0 = Convert.ToInt32(tiles[right].ContentY) - 1;
                int rightX1 = Convert.ToInt32(tiles[right].ContentX) +
                              Convert.ToInt32(tiles[right].ContentWidth);
                int rightY1 = Convert.ToInt32(tiles[right].ContentY) +
                              Convert.ToInt32(tiles[right].ContentHeight);
                bool disjoint = leftX1 < rightX0 || rightX1 < leftX0 ||
                                leftY1 < rightY0 || rightY1 < leftY0;
                await Assert.That(disjoint).IsTrue();
            }
        }
    }

    private static byte[] ReadPixel(SilkPackedPtexAtlas atlas, int x, int y)
    {
        int width = Convert.ToInt32(atlas.Width);
        int height = Convert.ToInt32(atlas.Height);
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            throw new InvalidOperationException("The tile gutter extends outside the atlas.");
        }

        int offset = checked((y * width + x) * 4);
        return atlas.Pixels.AsSpan(offset, 4).ToArray();
    }

    private static SilkDecodedPtexFaces CreateDecoded(
        uint dataType,
        uint channelCount,
        int alphaChannel,
        params FaceSpec[] faces)
        => CreateDecoded(dataType, channelCount, alphaChannel, 0, faces);

    private static SilkDecodedPtexFaces CreateDecoded(
        uint dataType,
        uint channelCount,
        int alphaChannel,
        uint meshType,
        params FaceSpec[] faces)
    {
        uint bytesPerChannel = dataType switch { 0 => 1, 1 or 2 => 2, 3 => 4, _ => 1 };
        var layouts = new SilkPtexFaceLayout[faces.Length];
        var pixels = new List<byte>();
        for (int index = 0; index < faces.Length; index++)
        {
            FaceSpec face = faces[index];
            uint calculatedStride = checked((uint)((ulong)face.Width * channelCount * bytesPerChannel));
            layouts[index] = new SilkPtexFaceLayout
            {
                StructSize = checked((uint)Marshal.SizeOf<SilkPtexFaceLayout>()),
                FaceId = face.FaceId,
                Width = face.Width,
                Height = face.Height,
                Flags = face.Flags,
                RowStrideBytes = face.RowStride ?? calculatedStride,
                DataOffsetBytes = face.DataOffset ?? (ulong)pixels.Count,
                AdjacentFaceId0 = face.AdjacentFaceId0,
                AdjacentFaceId1 = face.AdjacentFaceId1,
                AdjacentFaceId2 = face.AdjacentFaceId2,
                AdjacentFaceId3 = face.AdjacentFaceId3,
                AdjacentEdgeId0 = face.AdjacentEdgeId0,
                AdjacentEdgeId1 = face.AdjacentEdgeId1,
                AdjacentEdgeId2 = face.AdjacentEdgeId2,
                AdjacentEdgeId3 = face.AdjacentEdgeId3
            };
            pixels.AddRange(face.Bytes);
        }

        uint fileFaceCount = faces.Length == 0 ? 0 : checked(faces.Max(static face => face.FaceId) + 1);
        SilkPtexDecodeInfo info = SilkPtexDecodeInfo.Create();
        info.MeshType = meshType;
        info.DataType = dataType;
        info.ChannelCount = channelCount;
        info.AlphaChannel = alphaChannel;
        info.FileFaceCount = fileFaceCount;
        info.RequestedFaceCount = checked((uint)faces.Length);
        info.RequiredByteCount = checked((ulong)pixels.Count);
        return new SilkDecodedPtexFaces(info, layouts, pixels.ToArray(), null);
    }

    private static FaceSpec Connect(FaceSpec face, int edge, uint adjacentFaceId, int adjacentEdgeId)
    {
        return edge switch
        {
            0 => face with { AdjacentFaceId0 = checked((int)adjacentFaceId), AdjacentEdgeId0 = (uint)adjacentEdgeId },
            1 => face with { AdjacentFaceId1 = checked((int)adjacentFaceId), AdjacentEdgeId1 = (uint)adjacentEdgeId },
            2 => face with { AdjacentFaceId2 = checked((int)adjacentFaceId), AdjacentEdgeId2 = (uint)adjacentEdgeId },
            3 => face with { AdjacentFaceId3 = checked((int)adjacentFaceId), AdjacentEdgeId3 = (uint)adjacentEdgeId },
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
    }

    private readonly record struct FaceSpec(
        uint FaceId,
        uint Width,
        uint Height,
        byte[] Bytes,
        uint? RowStride = null,
        ulong? DataOffset = null,
        uint Flags = 0,
        int AdjacentFaceId0 = -1,
        int AdjacentFaceId1 = -1,
        int AdjacentFaceId2 = -1,
        int AdjacentFaceId3 = -1,
        uint AdjacentEdgeId0 = 0,
        uint AdjacentEdgeId1 = 0,
        uint AdjacentEdgeId2 = 0,
        uint AdjacentEdgeId3 = 0);
}
