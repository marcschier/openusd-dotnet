// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPtexMaterialSamplingConformanceTests
{
    private static readonly float[] ConstantControlUv = [0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f];

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task MissingPtexRetryPublishesSampledPixelsAndClearsUnsupportedDiagnostics(
        SilkGraphicsBackend backend)
    {
        RequireNativePtex();
        string asset = Path.Combine(Path.GetTempPath(), $"openusd-ptex-retry-{Guid.NewGuid():N}.ptx");
        try
        {
            using var actual = new Fixture(backend);
            using var expected = new Fixture(backend);
            using OpenUsdSilkPage missing = CreateMappedPage(
                ptex: true, swapFaces: false, revision: 1, assetPath: asset);
            using OpenUsdSilkPage control = CreateMappedPage(ptex: false, swapFaces: false, revision: 1);
            _ = actual.Render(missing);
            _ = expected.Render(control);
            byte[] fallback = actual.ReadColor();
            byte[] controlPixels = expected.ReadColor();
            await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
                entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
            await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
                entry.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)).IsFalse();
            await Assert.That(Math.Abs((int)ReadRedHalf(fallback, 24, 16) -
                ReadRedHalf(controlPixels, 24, 16))).IsGreaterThan(1);
            await Assert.That(actual.DecodedAssets.Count).IsEqualTo(0);

            File.Copy(FindPtexAsset(), asset);
            actual.Renderer.GpuResources.RetryFailedTextures();
            _ = actual.Renderer.Render(actual.ColorTarget, actual.DepthTarget);
            byte[] recovered = actual.ReadColor();
            await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(
                ReadColorRegion(recovered, 5, 11, 12, 20), ReadColorRegion(controlPixels, 5, 11, 12, 20));
            await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(
                ReadColorRegion(recovered, 21, 27, 12, 20), ReadColorRegion(controlPixels, 21, 27, 12, 20));
            await Assert.That(recovered.SequenceEqual(fallback)).IsFalse();
            await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
            await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
                entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsFalse();
            await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
                entry.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)).IsFalse();
            await Assert.That(actual.DecodedAssets.Count).IsEqualTo(0);
        }
        finally
        {
            if (File.Exists(asset))
            {
                File.Delete(asset);
            }
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task MappedPtexFacesSampleThroughProductionSceneResourcesAndTrackMappingReplacement(
        SilkGraphicsBackend backend)
    {
        RequireNativePtex();
        using var actual = new Fixture(backend);
        using var expected = new Fixture(backend);
        using OpenUsdSilkPage ptexPage = CreateMappedPage(ptex: true, swapFaces: false, revision: 1);
        using OpenUsdSilkPage controlPage = CreateMappedPage(ptex: false, swapFaces: false, revision: 1);
        await Assert.That(actual.Render(ptexPage).DrawCount).IsEqualTo(2);
        await Assert.That(expected.Render(controlPage).DrawCount).IsEqualTo(2);
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)).IsFalse();

        byte[] original = actual.ReadColor();
        byte[] controlPixels = expected.ReadColor();
        await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(
            ReadColorRegion(original, 5, 11, 12, 20), ReadColorRegion(controlPixels, 5, 11, 12, 20));
        await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(
            ReadColorRegion(original, 21, 27, 12, 20), ReadColorRegion(controlPixels, 21, 27, 12, 20));
        await Assert.That(Math.Abs((int)ReadRedHalf(original, 8, 16) -
            ReadRedHalf(original, 24, 16))).IsGreaterThan(1);
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
        await Assert.That(actual.DecodedAssets.Count).IsEqualTo(0);
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsFalse();

        // The same material and source now select different faces on each mesh.
        // Geometry/texture caches must not return their earlier face association.
        using OpenUsdSilkPage replacement = CreateMappedPage(ptex: true, swapFaces: true, revision: 2);
        using OpenUsdSilkPage replacementControl = CreateMappedPage(ptex: false, swapFaces: true, revision: 2);
        _ = actual.Render(replacement);
        _ = expected.Render(replacementControl);
        byte[] replaced = actual.ReadColor();
        byte[] replacementPixels = expected.ReadColor();
        await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(
            ReadColorRegion(replaced, 5, 11, 12, 20), ReadColorRegion(replacementPixels, 5, 11, 12, 20));
        await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(
            ReadColorRegion(replaced, 21, 27, 12, 20), ReadColorRegion(replacementPixels, 21, 27, 12, 20));
        await Assert.That(Math.Abs((int)ReadRedHalf(replaced, 8, 16) -
            ReadRedHalf(original, 8, 16))).IsGreaterThan(1);
        await Assert.That(Math.Abs((int)ReadRedHalf(replaced, 24, 16) -
            ReadRedHalf(original, 24, 16))).IsGreaterThan(1);
        await Assert.That(replaced.SequenceEqual(original)).IsFalse();
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)).IsFalse();
        await Assert.That(actual.DecodedAssets.Count).IsEqualTo(0);
        _ = actual.Renderer.Render(actual.ColorTarget, actual.DepthTarget);
        await Assert.That(actual.ReadColor().SequenceEqual(replaced)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task PtexWithOrdinaryTextureReportsOnlyMissingOrdinaryCoordinates(
        SilkGraphicsBackend backend, bool hasOrdinaryCoordinates)
    {
        RequireNativePtex();
        using var actual = new Fixture(backend);
        using var expected = new Fixture(backend);
        using OpenUsdSilkPage page = CreateMixedPtexPage(
            ptex: true, hasPrimary: hasOrdinaryCoordinates, hasSecondary: false, twoOrdinarySets: false);
        using OpenUsdSilkPage control = CreateMixedPtexPage(
            ptex: false, hasPrimary: hasOrdinaryCoordinates, hasSecondary: false, twoOrdinarySets: false);

        await Assert.That(actual.Render(page).DrawCount).IsEqualTo(1);
        await Assert.That(expected.Render(control).DrawCount).IsEqualTo(1);
        var warnings = actual.Renderer.GpuResources.Diagnostics.Entries
            .Where(static entry => entry.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)
            .ToArray();
        await Assert.That(warnings.Length).IsEqualTo(hasOrdinaryCoordinates ? 0 : 1);
        if (!hasOrdinaryCoordinates)
        {
            await Assert.That(warnings[0].Message.Contains("no 'st' texture coordinates", StringComparison.Ordinal))
                .IsTrue();
        }
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsFalse();
        await Assert.That(actual.ReadDepth()[(16 * 32) + 16]).IsLessThan(1f);
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
        await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(actual.ReadColor(), expected.ReadColor());
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task PtexThirdSetFallbackPreservesOrdinarySecondaryCoordinates(
        SilkGraphicsBackend backend, bool hasSecondaryCoordinates)
    {
        RequireNativePtex();
        using var actual = new Fixture(backend);
        using var expected = new Fixture(backend);
        using OpenUsdSilkPage ptexPage = CreateMixedPtexPage(
            ptex: true, hasPrimary: true, hasSecondary: hasSecondaryCoordinates, twoOrdinarySets: true);
        using OpenUsdSilkPage controlPage = CreateMixedPtexPage(
            ptex: false, hasPrimary: true, hasSecondary: hasSecondaryCoordinates, twoOrdinarySets: true);

        await Assert.That(actual.Render(ptexPage).DrawCount).IsEqualTo(1);
        await Assert.That(expected.Render(controlPage).DrawCount).IsEqualTo(1);
        var diagnostics = actual.Renderer.GpuResources.Diagnostics.Entries;
        await Assert.That(diagnostics.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported &&
            entry.Message.Contains("third surface texture-coordinate set", StringComparison.Ordinal))).IsTrue();
        await Assert.That(diagnostics.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable &&
            entry.Message.Contains("no 'st1' texture coordinates", StringComparison.Ordinal)))
            .IsEqualTo(!hasSecondaryCoordinates);
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
        await Assert.That(actual.ReadColor().SequenceEqual(expected.ReadColor())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PtexThirdSetFallbackRetryRetainsDiagnosticsAndOrdinaryCoordinates(
        SilkGraphicsBackend backend)
    {
        RequireNativePtex();
        using var actual = new Fixture(backend);
        using var expected = new Fixture(backend);
        using OpenUsdSilkPage ptexPage = CreateMixedPtexPage(
            ptex: true, hasPrimary: true, hasSecondary: true, twoOrdinarySets: true);
        using OpenUsdSilkPage controlPage = CreateMixedPtexPage(
            ptex: false, hasPrimary: true, hasSecondary: true, twoOrdinarySets: true);

        _ = actual.Render(ptexPage);
        _ = expected.Render(controlPage);
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();

        actual.Renderer.GpuResources.RetryFailedTextures(actual.Renderer.Scene);
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Meshes.Values.Single().Geometry.Key.PtexSamplingIdentity)
            .IsNotEqualTo(0ul);
        _ = actual.Renderer.Render(actual.ColorTarget, actual.DepthTarget);
        await Assert.That(actual.ReadColor().SequenceEqual(expected.ReadColor())).IsTrue();
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PtexThirdSetFallbackAndRecoveryRebuildCoordinateStreams(
        SilkGraphicsBackend backend)
    {
        RequireNativePtex();
        using var actual = new Fixture(backend);
        using var expected = new Fixture(backend);
        using OpenUsdSilkPage fallback = CreateMixedPtexPage(
            ptex: true, hasPrimary: true, hasSecondary: true, twoOrdinarySets: true, ptexFace: 3);
        using OpenUsdSilkPage fallbackControl = CreateMixedPtexPage(
            ptex: false, hasPrimary: true, hasSecondary: true, twoOrdinarySets: true);
        _ = actual.Render(fallback);
        _ = expected.Render(fallbackControl);
        byte[] fallbackPixels = actual.ReadColor();
        await Assert.That(fallbackPixels.SequenceEqual(expected.ReadColor())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();

        using OpenUsdSilkPage recovered = CreateMixedPtexPage(
            ptex: true, hasPrimary: true, hasSecondary: false, twoOrdinarySets: false, revision: 2, ptexFace: 3);
        using OpenUsdSilkPage recoveredControl = CreateMixedPtexPage(
            ptex: false, hasPrimary: true, hasSecondary: false, twoOrdinarySets: false,
            revision: 2, controlDiffuseAsset: "blue");
        _ = actual.Render(recovered);
        _ = expected.Render(recoveredControl);
        byte[] recoveredPixels = actual.ReadColor();
        await SilkMaterialStreamingTests.AssertPixelsMatchWithinHalfUlp(recoveredPixels, expected.ReadColor());
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
        await Assert.That(Math.Abs((int)ReadRedHalf(recoveredPixels, 16, 16) -
            ReadRedHalf(fallbackPixels, 16, 16))).IsGreaterThan(1);
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code is SilkRenderDiagnosticCodes.TexturePtexUnsupported or
                SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable)).IsFalse();

        using OpenUsdSilkPage restored = CreateMixedPtexPage(
            ptex: true, hasPrimary: true, hasSecondary: true, twoOrdinarySets: true, revision: 3, ptexFace: 3);
        using OpenUsdSilkPage restoredControl = CreateMixedPtexPage(
            ptex: false, hasPrimary: true, hasSecondary: true, twoOrdinarySets: true, revision: 3);
        _ = actual.Render(restored);
        _ = expected.Render(restoredControl);
        await Assert.That(actual.ReadColor().SequenceEqual(expected.ReadColor())).IsTrue();
        await Assert.That(actual.ReadDepth().SequenceEqual(expected.ReadDepth())).IsTrue();
        await Assert.That(actual.Renderer.GpuResources.Diagnostics.Entries.Any(static entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PtexSourceRetentionDoesNotChangeStandardUvPixelsOnProductionBackends(
        SilkGraphicsBackend backend)
    {
        using var withPtex = new Fixture(backend);
        using var standardOnly = new Fixture(backend);
        using OpenUsdSilkPage mixedPage = CreatePage(includePtexMesh: true);
        using OpenUsdSilkPage standardPage = CreatePage(includePtexMesh: false);

        SilkMeshRenderResult mixedResult = withPtex.Render(mixedPage);
        SilkMeshRenderResult standardResult = standardOnly.Render(standardPage);
        byte[] mixedPixels = withPtex.ReadColor();
        byte[] standardPixels = standardOnly.ReadColor();
        float[] mixedDepth = withPtex.ReadDepth();
        float[] standardDepth = standardOnly.ReadDepth();

        await Assert.That(mixedResult.DrawCount).IsEqualTo(2);
        await Assert.That(standardResult.DrawCount).IsEqualTo(1);
        await Assert.That(ReadColorRegion(mixedPixels, 18, 30, 5, 27)
            .SequenceEqual(ReadColorRegion(standardPixels, 18, 30, 5, 27))).IsTrue();
        await Assert.That(ReadDepthRegion(mixedDepth, 18, 30, 5, 27)
            .SequenceEqual(ReadDepthRegion(standardDepth, 18, 30, 5, 27))).IsTrue();
        await Assert.That(mixedDepth[(16 * 32) + 24]).IsLessThan(1f);
        await Assert.That(ReadRedHalf(mixedPixels, 19, 16) == ReadRedHalf(mixedPixels, 28, 16))
            .IsFalse();
        await Assert.That(withPtex.DecodedAssets.SequenceEqual(["shared"])).IsTrue();
    }

    private static OpenUsdSilkPage CreatePage(bool includePtexMesh)
    {
        List<byte[]> commands =
        [
            SilkMeshRendererConformance.CreateFrameCommand(
                32, 32, SilkMeshRendererConformance.Identity())
        ];
        if (includePtexMesh)
        {
            commands.Add(SilkMaterialStreamingTests.Material(
                'P',
                false,
                assetPath: "shared",
                uvPrimvar: "",
                sourceKind: SilkTextureSourceKind.Ptex));
        }
        commands.Add(SilkMaterialStreamingTests.Material(
            'S',
            false,
            assetPath: "shared",
            uvPrimvar: "st"));

        if (includePtexMesh)
        {
            byte[] ptexMesh = SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/PtexMesh",
                [-0.9f, -0.7f, -0.5f, -0.1f, -0.7f, -0.5f, -0.1f, 0.7f, -0.5f, -0.9f, 0.7f, -0.5f],
                [0, 1, 2, 0, 2, 3]);
            commands.Add(VolumeCommandAuthoring.WithMaterialBinding(ptexMesh, "/P"));
        }

        byte[] standardMesh = SilkMeshRendererConformance.CreateMeshCommand(
            2,
            "/StandardMesh",
            [0.1f, -0.7f, -0.5f, 0.9f, -0.7f, -0.5f, 0.9f, 0.7f, -0.5f, 0.1f, 0.7f, -0.5f],
            [0, 1, 2, 0, 2, 3],
            0,
            0,
            null,
            1,
            null,
            null,
            null,
            [
                ("st", (uint)SilkAttributeSemantic.TexCoord, 2,
                    [0, 0, 1, 0, 1, 1, 0, 1])
            ]);
        commands.Add(VolumeCommandAuthoring.WithMaterialBinding(standardMesh, "/S"));

        return new(
            SilkCommandParser.PageAbiVersion,
            1,
            commands.SelectMany(static command => command).ToArray(),
            checked((uint)commands.Count));
    }

    private static byte[] ReadColorRegion(byte[] pixels, int left, int right, int top, int bottom)
    {
        var result = new byte[(right - left) * (bottom - top) * 8];
        int destination = 0;
        for (int y = top; y < bottom; y++)
        {
            int source = ((y * 32) + left) * 8;
            int length = (right - left) * 8;
            pixels.AsSpan(source, length).CopyTo(result.AsSpan(destination, length));
            destination += length;
        }
        return result;
    }

    private static float[] ReadDepthRegion(float[] values, int left, int right, int top, int bottom)
    {
        var result = new float[(right - left) * (bottom - top)];
        int destination = 0;
        for (int y = top; y < bottom; y++)
        {
            values.AsSpan((y * 32) + left, right - left)
                .CopyTo(result.AsSpan(destination, right - left));
            destination += right - left;
        }
        return result;
    }

    private static ushort ReadRedHalf(byte[] pixels, int x, int y) =>
        BinaryPrimitives.ReadUInt16LittleEndian(pixels.AsSpan(((y * 32) + x) * 8, sizeof(ushort)));

    private static OpenUsdSilkPage CreateMappedPage(
        bool ptex, bool swapFaces, ulong revision, string? assetPath = null)
    {
        List<byte[]> commands =
        [
            SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity())
        ];
        if (ptex)
        {
            commands.Add(SilkMaterialStreamingTests.Material('P', false,
                assetPath: assetPath ?? FindPtexAsset(), uvPrimvar: "", sourceKind: SilkTextureSourceKind.Ptex));
        }
        else
        {
            commands.Add(SilkMaterialStreamingTests.Material('W', false, assetPath: "white"));
            commands.Add(SilkMaterialStreamingTests.Material('B', false, assetPath: "blue"));
        }
        for (int mesh = 0; mesh < 2; mesh++)
        {
            bool blue = (mesh == 1) != swapFaces;
            float left = mesh == 0 ? -0.9f : 0.1f;
            byte[] command = SilkMeshRendererConformance.CreateMeshCommand(
                (ulong)(mesh + 1), $"/Mapped{mesh}",
                [left, -0.7f, -0.5f, left + 0.8f, -0.7f, -0.5f,
                    left + 0.8f, 0.7f, -0.5f, left, 0.7f, -0.5f],
                [0, 1, 2, 0, 2, 3], 0, 0, null, 1, null, null, null,
                ptex ? null : [("st", (uint)SilkAttributeSemantic.TexCoord, 2, ConstantControlUv)]);
            command = VolumeCommandAuthoring.WithMaterialBinding(command, ptex ? "/P" : blue ? "/B" : "/W");
            if (ptex)
            {
                command = WithFaceMappings(command, blue ? 3u : 0u);
            }
            commands.Add(command);
        }
        return new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, revision,
            commands.SelectMany(static command => command).ToArray(), checked((uint)commands.Count));
    }

    private static OpenUsdSilkPage CreateMixedPtexPage(
        bool ptex, bool hasPrimary, bool hasSecondary, bool twoOrdinarySets,
        ulong revision = 1, uint ptexFace = 0, string controlDiffuseAsset = "white")
    {
        List<(string Name, uint Semantic, uint ComponentCount, float[] Data)> attributes = [];
        if (hasPrimary)
        {
            attributes.Add(("st", (uint)SilkAttributeSemantic.TexCoord, 2, ConstantControlUv));
        }
        if (hasSecondary)
        {
            attributes.Add(("st1", (uint)SilkAttributeSemantic.TexCoord, 2,
                [0.1f, 0.1f, 0.9f, 0.1f, 0.9f, 0.9f, 0.1f, 0.9f]));
        }
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            1, "/MixedMesh",
            [-0.9f, -0.7f, -0.5f, 0.9f, -0.7f, -0.5f,
                0.9f, 0.7f, -0.5f, -0.9f, 0.7f, -0.5f],
            [0, 1, 2, 0, 2, 3], 0, 0, null, 1, null, null, null, attributes.ToArray());
        mesh = VolumeCommandAuthoring.WithMaterialBinding(mesh, "/M");
        if (ptex)
        {
            mesh = WithFaceMappings(mesh, ptexFace);
        }
        byte[][] commands =
        [
            SilkMeshRendererConformance.CreateFrameCommand(32, 32, SilkMeshRendererConformance.Identity()),
            SilkMaterialStreamingTests.Material(
                'M', false, multipleMaps: true, assetPath: "shared",
                diffuseAssetPath: ptex ? FindPtexAsset() : controlDiffuseAsset,
                uvPrimvar: ptex ? "" : "st", secondaryUvPrimvar: "st",
                tertiaryUvPrimvar: twoOrdinarySets ? "st1" : "st",
                diffuseSourceKind: ptex ? SilkTextureSourceKind.Ptex : SilkTextureSourceKind.Standard),
            mesh
        ];
        return new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, revision,
            commands.SelectMany(static command => command).ToArray(), checked((uint)commands.Length));
    }

    private static byte[] WithFaceMappings(byte[] command, uint face)
    {
        byte[] result = new byte[command.Length + (2 * 28)];
        command.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(268), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(272), (uint)SilkPtexMappingStatus.Mapped);
        float[][] coordinates = [[0.25f, 0.25f, 0.75f, 0.25f, 0.75f, 0.75f],
            [0.25f, 0.25f, 0.75f, 0.75f, 0.25f, 0.75f]];
        for (int triangle = 0; triangle < 2; triangle++)
        {
            int offset = command.Length + (triangle * 28);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), face);
            for (int component = 0; component < 6; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    result.AsSpan(offset + 4 + (component * 4)), coordinates[triangle][component]);
            }
        }
        return result;
    }

    private static string FindPtexAsset()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the OpenUsd repository root."),
            "test-assets", "native-profile", "ptex", "quad-colors.ptx");
    }

    private static void RequireNativePtex()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with the matching native Ptex runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ISilkGraphicsDevice _device;
        private readonly SilkMeshRenderer _renderer;
        private readonly ISilkGraphicsTexture _color;
        private readonly ISilkGraphicsTexture _depth;

        internal List<string> DecodedAssets { get; } = [];
        internal SilkMeshRenderer Renderer => _renderer;
        internal ISilkGraphicsTexture ColorTarget => _color;
        internal ISilkGraphicsTexture DepthTarget => _depth;

        internal Fixture(SilkGraphicsBackend backend)
        {
            _device = SilkDepthCaptureConformance.CreateDevice(backend);
            _renderer = new SilkMeshRenderer(
                _device,
                backend switch
                {
                    SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
                    SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
                    SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
                    _ => throw new ArgumentOutOfRangeException(nameof(backend))
                },
                imageDecoder: Decode);
            _color = _device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(32, 32));
            _depth = _device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(32, 32));
        }

        internal SilkMeshRenderResult Render(OpenUsdSilkPage page) =>
            _renderer.ApplyAndRender(page, _color, _depth);

        internal byte[] ReadColor()
        {
            byte[] pixels = new byte[32 * 32 * 8];
            _color.ReadbackForTesting(pixels);
            return pixels;
        }

        internal float[] ReadDepth()
        {
            float[] values = new float[32 * 32];
            _depth.ReadbackForTesting(values);
            return values;
        }

        private SilkDecodedImage Decode(string asset, bool _)
        {
            DecodedAssets.Add(asset);
            if (asset is "white" or "blue")
            {
                return new SilkDecodedImage(1, 1, asset == "white"
                    ? [255, 255, 255, 255] : [0, 0, 255, 255]);
            }
            byte[] pixels = new byte[4 * 4 * 4];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int offset = ((y * 4) + x) * 4;
                    pixels[offset] = (byte)(32 + (x * 64));
                    pixels[offset + 1] = (byte)(48 + (y * 48));
                    pixels[offset + 2] = 192;
                    pixels[offset + 3] = 255;
                }
            }
            return new SilkDecodedImage(4, 4, pixels);
        }

        public void Dispose()
        {
            try
            {
                _renderer.Dispose();
            }
            finally
            {
                try
                {
                    _color.Dispose();
                }
                finally
                {
                    try
                    {
                        _depth.Dispose();
                    }
                    finally
                    {
                        _device.Dispose();
                    }
                }
            }
        }
    }
}
