// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class SilkNativePtexDecoderTests
{
    private static readonly string QuadColorsPath = FindRepositoryAsset(
        Path.Combine("native-profile", "ptex", "quad-colors.ptx"));
    private static readonly string TetrahedronPath = FindRepositoryAsset(
        Path.Combine("native-profile", "ptex", "tetrahedron.ptx"));

    [Test]
    public async Task DescribeMatchesDecodeForOrderedAndDuplicateFaces()
    {
        RequireNativePtex();
        uint[] faceIds = [3, 1, 1, 0];
        SilkPtexFaceDescription description = SilkNativePtexDecoder.Describe(
            QuadColorsPath, faceIds, 8, 192);
        using SilkDecodedPtexFaces decoded = SilkNativePtexDecoder.Decode(
            QuadColorsPath, faceIds, 8, 192);

        await Assert.That(description.Info.StructSize).IsEqualTo(decoded.Info.StructSize);
        await Assert.That(description.Info.Version).IsEqualTo(decoded.Info.Version);
        await Assert.That(description.Info.MeshType).IsEqualTo(decoded.Info.MeshType);
        await Assert.That(description.Info.DataType).IsEqualTo(decoded.Info.DataType);
        await Assert.That(description.Info.ChannelCount).IsEqualTo(decoded.Info.ChannelCount);
        await Assert.That(description.Info.AlphaChannel).IsEqualTo(decoded.Info.AlphaChannel);
        await Assert.That(description.Info.FileFaceCount).IsEqualTo(decoded.Info.FileFaceCount);
        await Assert.That(description.Info.RequestedFaceCount).IsEqualTo(4u);
        await Assert.That(description.Info.RequiredByteCount).IsEqualTo(192ul);
        await Assert.That(description.Faces.Length).IsEqualTo(faceIds.Length);
        for (int index = 0; index < faceIds.Length; index++)
        {
            await Assert.That(description.Faces[index].FaceId).IsEqualTo(faceIds[index]);
            await AssertSameLayout(description.Faces[index], decoded.Faces[index]);
        }
    }

    [Test]
    public async Task SamplingNeighborhoodDecodesDeduplicatedFacesAndSharesTheAtlasBudget()
    {
        RequireNativePtex();
        var budget = new SilkCpuTextureBudget(384);
        using SilkDecodedPtexFaces decoded = SilkNativePtexDecoder.DecodeSamplingNeighborhood(
            QuadColorsPath, [3, 0, 3], 8, 96, budget);

        await Assert.That(decoded.Faces.Length).IsEqualTo(2);
        await Assert.That(decoded.Faces[0].FaceId).IsEqualTo(0u);
        await Assert.That(decoded.Faces[1].FaceId).IsEqualTo(3u);
        await Assert.That(decoded.Pixels.Length).IsEqualTo(96);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(96ul);
        using (SilkPackedPtexAtlas atlas = SilkPtexAtlasPacker.Pack(decoded, [0, 3], 12, budget))
        {
            await Assert.That(atlas.Tiles.Count).IsEqualTo(2);
            await Assert.That(atlas.Pixels.Length).IsEqualTo(288);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(384ul);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(96ul);
        decoded.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task DescribePreservesReciprocalInteriorFaceAdjacency()
    {
        RequireNativePtex();
        SilkPtexFaceDescription empty = SilkNativePtexDecoder.Describe(TetrahedronPath, [], 4096, 0);
        uint[] faceIds = Enumerable.Range(0, checked((int)empty.Info.FileFaceCount))
            .Select(static faceId => (uint)faceId)
            .ToArray();
        SilkPtexFaceDescription description = SilkNativePtexDecoder.Describe(
            TetrahedronPath, faceIds, 4096, 8 * 1024 * 1024);
        using SilkDecodedPtexFaces decoded = SilkNativePtexDecoder.Decode(
            TetrahedronPath, faceIds, 4096, 8 * 1024 * 1024);

        int adjacentEdges = 0;
        for (int index = 0; index < description.Faces.Length; index++)
        {
            SilkPtexFaceLayout face = description.Faces[index];
            await AssertSameLayout(face, decoded.Faces[index]);
            for (int edge = 0; edge < 4; edge++)
            {
                int adjacentFaceId = face.GetAdjacentFaceId(edge);
                if (adjacentFaceId < 0)
                {
                    continue;
                }

                int adjacentEdge = checked((int)face.GetAdjacentEdgeId(edge));
                SilkPtexFaceLayout adjacent = description.Faces[adjacentFaceId];
                await Assert.That(adjacent.GetAdjacentFaceId(adjacentEdge)).IsEqualTo((int)face.FaceId);
                await Assert.That(adjacent.GetAdjacentEdgeId(adjacentEdge)).IsEqualTo((uint)edge);
                adjacentEdges++;
            }
        }
        await Assert.That(adjacentEdges).IsGreaterThan(0);
    }

    [Test]
    public async Task DescribeEmptySelectionReturnsFileMetadataWithoutFaces()
    {
        RequireNativePtex();
        SilkPtexFaceDescription description = SilkNativePtexDecoder.Describe(
            QuadColorsPath, [], 8, 0);

        await Assert.That(description.Info.FileFaceCount).IsEqualTo(4u);
        await Assert.That(description.Info.RequestedFaceCount).IsEqualTo(0u);
        await Assert.That(description.Info.RequiredByteCount).IsEqualTo(0ul);
        await Assert.That(description.Faces.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DescribeAcceptsMaximumFaceCount()
    {
        RequireNativePtex();
        uint[] faceIds = new uint[65_536];
        const ulong byteCount = 65_536ul * 48;
        SilkPtexFaceDescription description = SilkNativePtexDecoder.Describe(
            QuadColorsPath, faceIds, 8, byteCount);

        await Assert.That(description.Info.RequestedFaceCount).IsEqualTo(65_536u);
        await Assert.That(description.Info.RequiredByteCount).IsEqualTo(byteCount);
        await Assert.That(description.Faces.Length).IsEqualTo(faceIds.Length);
        await Assert.That(description.Faces[0].FaceId).IsEqualTo(0u);
        await Assert.That(description.Faces[^1].FaceId).IsEqualTo(0u);
        await Assert.That(description.Faces[^1].DataOffsetBytes).IsEqualTo(byteCount - 48);
    }

    [Test]
    public async Task DescribeRejectsInvalidManagedArgumentsBeforeNativeCall()
    {
        await Assert.That(() => SilkNativePtexDecoder.Describe(null!, [], 8, 0))
            .Throws<ArgumentNullException>();
        await Assert.That(() => SilkNativePtexDecoder.Describe(" ", [], 8, 0))
            .Throws<ArgumentException>();
        await Assert.That(() => SilkNativePtexDecoder.Describe("asset.ptx", [], 0, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkNativePtexDecoder.Describe("asset.ptx", new uint[65_537], 8, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DescribeRefusesInvalidFacesMissingAssetsAndExceededLimits()
    {
        RequireNativePtex();
        OpenUsdSilkException? invalidFace = await Assert.That(() =>
            SilkNativePtexDecoder.Describe(QuadColorsPath, [4], 8, 48))
            .Throws<OpenUsdSilkException>();
        await Assert.That(invalidFace!.Status).IsEqualTo(OpenUsdNativeStatus.InvalidArgument);

        OpenUsdSilkException? missing = await Assert.That(() =>
            SilkNativePtexDecoder.Describe(QuadColorsPath + ".missing", [0], 8, 48))
            .Throws<OpenUsdSilkException>();
        await Assert.That(missing!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);

        OpenUsdSilkException? dimensionLimit = await Assert.That(() =>
            SilkNativePtexDecoder.Describe(QuadColorsPath, [0], 2, 48))
            .Throws<OpenUsdSilkException>();
        await Assert.That(dimensionLimit!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);

        OpenUsdSilkException? byteLimit = await Assert.That(() =>
            SilkNativePtexDecoder.Describe(QuadColorsPath, [0], 8, 47))
            .Throws<OpenUsdSilkException>();
        await Assert.That(byteLimit!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);
    }

    [Test]
    public async Task DecodePreservesRequestedFaceOrderPixelsMetadataAndBudgetOwnership()
    {
        RequireNativePtex();
        uint[] faceIds = [3, 1, 1, 0];
        var budget = new SilkCpuTextureBudget(192);
        using SilkDecodedPtexFaces decoded = SilkNativePtexDecoder.Decode(
            QuadColorsPath, faceIds, 8, 192, budget);

        await Assert.That(decoded.Info.MeshType).IsEqualTo(1u);
        await Assert.That(decoded.Info.DataType).IsEqualTo(0u);
        await Assert.That(decoded.Info.ChannelCount).IsEqualTo(3u);
        await Assert.That(decoded.Info.AlphaChannel).IsEqualTo(-1);
        await Assert.That(decoded.Info.FileFaceCount).IsEqualTo(4u);
        await Assert.That(decoded.Info.RequestedFaceCount).IsEqualTo(4u);
        await Assert.That(decoded.Info.RequiredByteCount).IsEqualTo(192ul);
        await Assert.That(decoded.Faces.Length).IsEqualTo(faceIds.Length);
        await Assert.That(decoded.Pixels.Length).IsEqualTo(192);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(192ul);

        byte[][] colors = [[0, 0, 255], [255, 0, 0], [255, 0, 0], [255, 255, 255]];
        for (int index = 0; index < decoded.Faces.Length; index++)
        {
            SilkPtexFaceLayout face = decoded.Faces[index];
            await Assert.That(face.FaceId).IsEqualTo(faceIds[index]);
            await Assert.That(face.Width).IsEqualTo(4u);
            await Assert.That(face.Height).IsEqualTo(4u);
            await Assert.That(face.RowStrideBytes).IsEqualTo(12u);
            await Assert.That(face.DataOffsetBytes).IsEqualTo((ulong)(index * 48));
            for (int edge = 0; edge < 4; edge++)
            {
                await Assert.That(face.GetAdjacentFaceId(edge)).IsEqualTo(-1);
            }

            byte[] expected = new byte[48];
            for (int pixel = 0; pixel < 16; pixel++)
            {
                colors[index].CopyTo(expected, pixel * 3);
            }
            await Assert.That(decoded.Pixels.AsSpan(index * 48, 48).SequenceEqual(expected)).IsTrue();
        }

        decoded.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task EmptyFaceSelectionReturnsEmptyOutputWithoutBudgetReservation()
    {
        RequireNativePtex();
        var budget = new SilkCpuTextureBudget(1);
        using SilkDecodedPtexFaces decoded = SilkNativePtexDecoder.Decode(
            QuadColorsPath, [], 8, 0, budget);

        await Assert.That(decoded.Info.FileFaceCount).IsEqualTo(4u);
        await Assert.That(decoded.Info.RequestedFaceCount).IsEqualTo(0u);
        await Assert.That(decoded.Info.RequiredByteCount).IsEqualTo(0ul);
        await Assert.That(decoded.Faces.Length).IsEqualTo(0);
        await Assert.That(decoded.Pixels.Length).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task BudgetAndNativeDecodeLimitsRefuseWithoutRetainingReservations()
    {
        RequireNativePtex();
        var budget = new SilkCpuTextureBudget(191);
        SilkCpuTextureBudgetExceededException? failure = await Assert.That(() =>
            SilkNativePtexDecoder.Decode(QuadColorsPath, [0, 1, 2, 3], 8, 192, budget))
            .Throws<SilkCpuTextureBudgetExceededException>();
        await Assert.That(failure!.RequestedBytes).IsEqualTo(192ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);

        OpenUsdSilkException? decodeFailure = await Assert.That(() =>
            SilkNativePtexDecoder.Decode(QuadColorsPath, [0], 2, 48))
            .Throws<OpenUsdSilkException>();
        await Assert.That(decodeFailure!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);

        OpenUsdSilkException? byteLimitFailure = await Assert.That(() =>
            SilkNativePtexDecoder.Decode(QuadColorsPath, [0], 8, 47, budget))
            .Throws<OpenUsdSilkException>();
        await Assert.That(byteLimitFailure!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task InvalidFaceAndMissingAssetFailExplicitly()
    {
        RequireNativePtex();
        OpenUsdSilkException? invalidFace = await Assert.That(() =>
            SilkNativePtexDecoder.Decode(QuadColorsPath, [4], 8, 48))
            .Throws<OpenUsdSilkException>();
        await Assert.That(invalidFace!.Status).IsEqualTo(OpenUsdNativeStatus.InvalidArgument);

        OpenUsdSilkException? missing = await Assert.That(() =>
            SilkNativePtexDecoder.Decode(QuadColorsPath + ".missing", [0], 8, 48))
            .Throws<OpenUsdSilkException>();
        await Assert.That(missing!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);
    }

    [Test]
    public async Task DecodePreservesReciprocalInteriorFaceAdjacency()
    {
        RequireNativePtex();
        using SilkDecodedPtexFaces metadata = SilkNativePtexDecoder.Decode(TetrahedronPath, [], 4096, 0);
        uint[] faceIds = Enumerable.Range(0, checked((int)metadata.Info.FileFaceCount))
            .Select(static faceId => (uint)faceId)
            .ToArray();
        using SilkDecodedPtexFaces decoded = SilkNativePtexDecoder.Decode(
            TetrahedronPath, faceIds, 4096, 8 * 1024 * 1024);

        int adjacentEdges = 0;
        foreach (SilkPtexFaceLayout face in decoded.Faces)
        {
            for (int edge = 0; edge < 4; edge++)
            {
                int adjacentFaceId = face.GetAdjacentFaceId(edge);
                if (adjacentFaceId < 0)
                {
                    continue;
                }

                uint adjacentEdgeId = face.GetAdjacentEdgeId(edge);
                SilkPtexFaceLayout adjacentFace = decoded.Faces[adjacentFaceId];
                await Assert.That(adjacentFace.GetAdjacentFaceId((int)adjacentEdgeId))
                    .IsEqualTo((int)face.FaceId);
                await Assert.That(adjacentFace.GetAdjacentEdgeId((int)adjacentEdgeId))
                    .IsEqualTo((uint)edge);
                adjacentEdges++;
            }
        }
        await Assert.That(adjacentEdges).IsGreaterThan(0);
    }

    private static async Task AssertSameLayout(SilkPtexFaceLayout actual, SilkPtexFaceLayout expected)
    {
        await Assert.That(actual.StructSize).IsEqualTo(expected.StructSize);
        await Assert.That(actual.FaceId).IsEqualTo(expected.FaceId);
        await Assert.That(actual.Width).IsEqualTo(expected.Width);
        await Assert.That(actual.Height).IsEqualTo(expected.Height);
        await Assert.That(actual.RowStrideBytes).IsEqualTo(expected.RowStrideBytes);
        await Assert.That(actual.Flags).IsEqualTo(expected.Flags);
        await Assert.That(actual.DataOffsetBytes).IsEqualTo(expected.DataOffsetBytes);
        for (int edge = 0; edge < 4; edge++)
        {
            await Assert.That(actual.GetAdjacentFaceId(edge)).IsEqualTo(expected.GetAdjacentFaceId(edge));
            await Assert.That(actual.GetAdjacentEdgeId(edge)).IsEqualTo(expected.GetAdjacentEdgeId(edge));
        }
    }

    private static string FindRepositoryAsset(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate the OpenUsd repository root.");
        }
        return Path.Combine(directory.FullName, "test-assets", relativePath);
    }

    private static void RequireNativePtex()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with the matching native Ptex runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }
}
