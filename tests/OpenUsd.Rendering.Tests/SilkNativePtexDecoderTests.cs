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
