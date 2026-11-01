// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class VolumeRenderingConformanceTests
{
    [Test]
    public async Task SampledFieldBoundsMatchIndependentProxyOnD3D12Warp()
    {
        await VerifySampledFieldBounds(SilkGraphicsBackend.D3D12, affineField: false).ConfigureAwait(false);
    }

    [Test]
    public async Task SampledFieldBoundsMatchIndependentProxyOnVulkan()
    {
        await VerifySampledFieldBounds(SilkGraphicsBackend.Vulkan, affineField: false).ConfigureAwait(false);
    }

    [Test]
    public async Task SampledFieldBoundsMatchIndependentProxyOnMetal()
    {
        await VerifySampledFieldBounds(SilkGraphicsBackend.Metal, affineField: false).ConfigureAwait(false);
    }

    [Test]
    public async Task OffsetAffineFieldBoundsMatchIndependentProxyOnD3D12Warp()
    {
        await VerifySampledFieldBounds(SilkGraphicsBackend.D3D12, affineField: true).ConfigureAwait(false);
    }

    [Test]
    public async Task OffsetAffineFieldBoundsMatchIndependentProxyOnVulkan()
    {
        await VerifySampledFieldBounds(SilkGraphicsBackend.Vulkan, affineField: true).ConfigureAwait(false);
    }

    [Test]
    public async Task OffsetAffineFieldBoundsMatchIndependentProxyOnMetal()
    {
        await VerifySampledFieldBounds(SilkGraphicsBackend.Metal, affineField: true).ConfigureAwait(false);
    }

    [Test]
    public async Task AnimatedFieldBoundsAndDegenerateFallbackOnD3D12Warp()
    {
        await VerifyAnimatedFieldBounds(SilkGraphicsBackend.D3D12).ConfigureAwait(false);
    }

    [Test]
    public async Task AnimatedFieldBoundsAndDegenerateFallbackOnVulkan()
    {
        await VerifyAnimatedFieldBounds(SilkGraphicsBackend.Vulkan).ConfigureAwait(false);
    }

    [Test]
    public async Task AnimatedFieldBoundsAndDegenerateFallbackOnMetal()
    {
        await VerifyAnimatedFieldBounds(SilkGraphicsBackend.Metal).ConfigureAwait(false);
    }

    [Test]
    public async Task NonFiniteVolumeTransformsRefuseSyncAndRecover()
    {
        PrependHdSilkNativeSearchPath();
        string asset = ResolveOpenVdbAsset("sampled_density_unit.vdb")
            .Replace("\\", "/", StringComparison.Ordinal);
        string stagePath = Path.Combine(StageDirectory, $"invalid-volume-transform-{Guid.NewGuid():N}.usda");
        Directory.CreateDirectory(StageDirectory);
        File.WriteAllText(stagePath, $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 4
            )
            def Xform "World"
            {
                def Volume "Volume"
                {
                    double3 xformOp:translate.timeSamples = {
                        1: (0, 0, 0),
                        2: (nan, 0, 0),
                        3: (0, 0, 0),
                        4: (inf, 0, 0)
                    }
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                    rel field:density = </World/Volume/Density>
                    def OpenVDBAsset "Density"
                    {
                        asset filePath = @{{asset}}@
                        token fieldName = "density"
                    }
                }
            }
            """, new UTF8Encoding(false));
        using var budget = new SilkVolumeFieldBudget(3UL * 32 * 32 * 32 * sizeof(float));
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stagePath);
        session.ConfigureVolumeFieldBudget(budget);
        using OpenUsdSilkPage firstPage = session.Sync(Width, Height, 1);
        using var firstScene = new SilkSceneState();
        _ = firstScene.Apply(firstPage);
        const string materialPath = "/World/Volume/__hdSilkVolumeMaterial";
        SilkMaterialTexture? texture = firstScene.Materials[materialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity);
        if (texture is null)
        {
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        byte[] retained = ReadVolumeDensity(texture);
        foreach (double time in new double[] { 2, 4 })
        {
            OpenUsdSilkException? error = await Assert.That(() =>
                {
                    using OpenUsdSilkPage ignored = session.Sync(Width, Height, time);
                })
                .Throws<OpenUsdSilkException>();
            await Assert.That(error!.Message).Contains("non-finite transform");
            await Assert.That(ReadVolumeDensity(texture).SequenceEqual(retained)).IsTrue();
            using OpenUsdSilkPage recovered = session.Sync(Width, Height, 1);
            using var recoveredScene = new SilkSceneState();
            _ = recoveredScene.Apply(recovered);
            SilkMaterialTexture recoveredTexture = recoveredScene.Materials[materialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity)!;
            await Assert.That(ReadVolumeDensity(recoveredTexture).SequenceEqual(retained)).IsTrue();
        }
        session.Dispose();
        firstPage.Dispose();
        firstScene.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0UL);
    }

    private static async Task VerifySampledFieldBounds(SilkGraphicsBackend backend, bool affineField)
    {
        RequireFieldBoundsBackend(backend);
        PrependHdSilkNativeSearchPath();
        string asset = ResolveOpenVdbAsset(
            affineField ? "sampled_density_offset_affine.vdb" : "sampled_density.vdb")
            .Replace("\\", "/", StringComparison.Ordinal);
        string volumeTransform = affineField
            ? """
              (1, 0, 0, 0),
              (0, 1, 0, 0),
              (0, 0, 1, 0),
              (0, 0, 0, 1)
              """
            : """
              (0.02, 0.006, 0, 0),
              (-0.005, 0.015, 0, 0),
              (0, 0, 0.02, 0),
              (-0.25, -0.3, 0.2, 1)
              """;
        string stagePath = Path.Combine(StageDirectory, $"volume-field-bounds-{Guid.NewGuid():N}.usda");
        Directory.CreateDirectory(StageDirectory);
        File.WriteAllText(
            stagePath,
            $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                upAxis = "Y"
            )
            def Xform "World"
            {
                def Volume "Volume"
                {
                    matrix4d xformOp:transform = (
                        {{volumeTransform}})
                    uniform token[] xformOpOrder = ["xformOp:transform"]
                    rel field:density = </World/Shared/Density>
                }
                def Xform "Shared"
                {
                    double3 xformOp:translate = (30, 40, 50)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                    def OpenVDBAsset "Density"
                    {
                        asset filePath = @{{asset}}@
                        token fieldName = "density"
                    }
                }
            }
            """,
            new UTF8Encoding(false));

        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stagePath);
        using OpenUsdSilkPage page = session.Sync(
            Width,
            Height,
            TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []));
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMaterialTexture? texture = scene.Materials["/World/Volume/__hdSilkVolumeMaterial"]
            .GetTexture(SilkMaterialParameter.VolumeDensity);
        if (texture is null)
        {
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        byte[] density = ReadVolumeDensity(texture);
        await Assert.That(texture.UvPrimvar).IsEqualTo("32,32,32");
        string gridPath = Path.Combine(StageDirectory, $"field-bounds-{Guid.NewGuid():N}.r32");
        File.WriteAllBytes(gridPath, density);

        // Compute these independently from the fixture's active index bounds and
        // authored field and volume matrices, rather than from the native page.
        double[] expectedTransform = affineField
            ?
            [
                0.62, 0.186, 0, 0,
                0.124, -0.465, 0, 0,
                0, 0, 0.62, 0,
                0.3, 0.1975, 0.41, 1,
            ]
            :
            [
                0.62, 0.186, 0, 0,
                -0.155, 0.465, 0, 0,
                0, 0, 0.62, 0,
                -0.0175, 0.0255, 0.51, 1,
            ];
        double[] actualTransform = scene.Meshes.Values.Single().Transform.ToArray();
        for (int index = 0; index < expectedTransform.Length; index++)
        {
            await Assert.That(actualTransform[index])
                .IsEqualTo(expectedTransform[index]).Within(0.0000001)
                .Because($"proxy transform element {index} must map the grid into the volume's space.");
        }

        using OpenUsdSilkPage expectedPage = CreateIndependentFieldPage(gridPath, expectedTransform, scene.Frame);
        using var expectedScene = new SilkSceneState();
        _ = expectedScene.Apply(expectedPage);
        SilkMeshData actualMesh = scene.Meshes.Values.Single();
        SilkMeshData expectedMesh = expectedScene.Meshes.Values.Single();
        await Assert.That(actualMesh.Points.ToArray().SequenceEqual(expectedMesh.Points.ToArray())).IsTrue();
        await Assert.That(actualMesh.Indices.ToArray().SequenceEqual(expectedMesh.Indices.ToArray())).IsTrue();
        await Assert.That(actualMesh.DoubleSided).IsEqualTo(expectedMesh.DoubleSided);
        await Assert.That(actualMesh.CullStyle).IsEqualTo(expectedMesh.CullStyle);
        await Assert.That(actualMesh.DisplayColor.ToArray()
            .SequenceEqual(expectedMesh.DisplayColor.ToArray())).IsTrue();
        await Assert.That(scene.Frame.View.ToArray().SequenceEqual(expectedScene.Frame.View.ToArray())).IsTrue();
        await Assert.That(scene.Frame.Projection.ToArray()
            .SequenceEqual(expectedScene.Frame.Projection.ToArray())).IsTrue();
        SilkMaterialData actualMaterial = scene.Materials.Values.Single();
        SilkMaterialData expectedMaterial = expectedScene.Materials.Values.Single();
        foreach (SilkMaterialParameter parameter in new[]
                 { SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.VolumeDensity })
        {
            await Assert.That(actualMaterial.GetScalar(parameter).ToArray()
                .SequenceEqual(expectedMaterial.GetScalar(parameter).ToArray())).IsTrue();
        }

        Func<ISilkGraphicsDevice> createDevice = () => SilkDepthCaptureConformance.CreateDevice(backend);
        ParityImage expected = CaptureIndependentFieldPage(expectedPage, createDevice);
        ParityImage actual = CapturePage(page, createDevice);
        await Assert.That(expected.Rgba.ToArray()
                .Where((_, index) => index % ParityImage.BytesPerPixel != 3)
                .Count(static value => value > 0))
            .IsGreaterThan(100)
            .Because("the independent proxy must cover a visible area.");
        await Assert.That(ImageDelta.Compare(expected, actual).MaximumChannelDelta)
            .IsLessThanOrEqualTo((byte)1)
            .Because("the sampled field must use its index bounds and volume transform, not the field prim transform.");
    }

    private static async Task VerifyAnimatedFieldBounds(SilkGraphicsBackend backend)
    {
        RequireFieldBoundsBackend(backend);
        PrependHdSilkNativeSearchPath();
        string unit = ResolveOpenVdbAsset("sampled_density_unit.vdb");
        string offset = ResolveOpenVdbAsset("sampled_density_offset_affine.vdb");
        string degenerate = ResolveOpenVdbAsset("sampled_density_single_voxel.vdb");
        Func<ISilkGraphicsDevice> createDevice = () => SilkDepthCaptureConformance.CreateDevice(backend);
        ParityImage unitReference = CaptureStage(
            WriteVdbStage($"animated-bounds-{backend}-unit", unit, 0, 1, sampleDensity: true),
            createDevice, out byte[]? unitDensity);
        if (unitDensity is null)
        {
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        ParityImage offsetReference = CaptureStage(
            WriteVdbStage($"animated-bounds-{backend}-offset", offset, 0, 1, sampleDensity: true),
            createDevice, out byte[]? offsetDensity);
        ParityImage uniformReference = CaptureStage(
            WriteVdbStage($"animated-bounds-{backend}-uniform", unit, 0, 1, sampleDensity: false),
            createDevice);
        await Assert.That(offsetDensity).IsNotNull();
        await Assert.That(offsetDensity!.SequenceEqual(unitDensity)).IsTrue()
            .Because("the offset fixture changes coordinates, not the voxel values.");
        await Assert.That(ImageDelta.Compare(unitReference, offsetReference).MeanChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumTranslatedMeanDelta)
            .Because("different field bounds must produce distinguishable images.");
        await Assert.That(ImageDelta.Compare(unitReference, uniformReference).MaximumChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumSampledVsUniformDelta);

        string stagePath = Path.Combine(StageDirectory, $"animated-field-bounds-{Guid.NewGuid():N}.usda");
        string missing = Path.Combine(StageDirectory, "missing-field-bounds.vdb");
        File.WriteAllText(stagePath, $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 6
            )
            def Xform "World"
            {
                def Xform "Vdb"
                {
                    def Volume "Volume"
                    {
                        rel field:density = </World/Vdb/Volume/Density>
                        def OpenVDBAsset "Density"
                        {
                            asset filePath.timeSamples = {
                                1: @{{unit.Replace("\\", "/", StringComparison.Ordinal)}}@,
                                2: @{{offset.Replace("\\", "/", StringComparison.Ordinal)}}@,
                                3: @{{degenerate.Replace("\\", "/", StringComparison.Ordinal)}}@,
                                4: @{{unit.Replace("\\", "/", StringComparison.Ordinal)}}@,
                                5: @{{missing.Replace("\\", "/", StringComparison.Ordinal)}}@,
                                6: @{{offset.Replace("\\", "/", StringComparison.Ordinal)}}@
                            }
                            token fieldName = "density"
                        }
                    }
                }
            }
            """, new UTF8Encoding(false));
        byte[] source = File.ReadAllBytes(stagePath);
        double[] unitTransform = SilkMeshRendererConformance.Identity();
        double[] offsetTransform =
        [
            0.62, 0.186, 0, 0,
            0.124, -0.465, 0, 0,
            0, 0, 0.62, 0,
            0.3, 0.1975, 0.41, 1,
        ];
        ulong fieldBytes = checked((ulong)unitDensity.Length);
        using var budget = new SilkVolumeFieldBudget(checked(fieldBytes * 3));
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stagePath);
        session.ConfigureVolumeFieldBudget(budget);
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width, Height, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        var camera = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []);
        const string materialPath = "/World/Vdb/Volume/__hdSilkVolumeMaterial";

        async Task assertPage(
            OpenUsdSilkPage page,
            ParityImage expectedImage,
            byte[]? expectedDensity,
            double[] expectedTransform)
        {
            _ = renderer.ApplyAndRender(page, color, depth, options);
            _ = renderer.ApplyAndRender(page, color, depth, options);
            SilkMaterialTexture? texture = renderer.Scene.Materials[materialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity);
            if (expectedDensity is null)
            {
                await Assert.That(texture).IsNull()
                    .Because("a missing or degenerate field must use the uniform fallback.");
            }
            else
            {
                await Assert.That(texture).IsNotNull();
                await Assert.That(ReadVolumeDensity(texture!).SequenceEqual(expectedDensity)).IsTrue();
            }
            double[] actualTransform = renderer.Scene.Meshes.Values.Single().Transform.ToArray();
            for (int index = 0; index < expectedTransform.Length; index++)
            {
                await Assert.That(actualTransform[index])
                    .IsEqualTo(expectedTransform[index]).Within(0.0000001);
            }
            byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
            color.ReadbackForTesting(pixels);
            await Assert.That(ImageDelta.Compare(
                    expectedImage, new ParityImage(Width, Height, pixels)).MaximumChannelDelta)
                .IsEqualTo((byte)0);
        }

        async Task assertAt(
            double time,
            ParityImage expectedImage,
            byte[]? expectedDensity,
            double[] expectedTransform)
        {
            using OpenUsdSilkPage page = session.Sync(Width, Height, time, camera);
            await assertPage(page, expectedImage, expectedDensity, expectedTransform).ConfigureAwait(false);
        }

        using OpenUsdSilkPage firstPage = session.Sync(Width, Height, 1, camera);
        using var firstScene = new SilkSceneState();
        _ = firstScene.Apply(firstPage);
        SilkMaterialTexture retainedTexture = firstScene.Materials[materialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)!;
        await assertPage(firstPage, unitReference, unitDensity, unitTransform).ConfigureAwait(false);
        await assertAt(2, offsetReference, offsetDensity, offsetTransform).ConfigureAwait(false);
        await assertAt(3, uniformReference, null, unitTransform).ConfigureAwait(false);
        await Assert.That(renderer.GpuResources.Statistics.TextureResidentGpuBytes).IsEqualTo(0UL);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(fieldBytes)
            .Because("the earlier page retains its field while the degenerate grid consumes no field budget.");
        await assertAt(4, unitReference, unitDensity, unitTransform).ConfigureAwait(false);
        await assertAt(5, uniformReference, null, unitTransform).ConfigureAwait(false);
        await assertAt(6, offsetReference, offsetDensity, offsetTransform).ConfigureAwait(false);
        await assertAt(1, unitReference, unitDensity, unitTransform).ConfigureAwait(false);
        await Assert.That(ReadVolumeDensity(retainedTexture).SequenceEqual(unitDensity)).IsTrue();
        await Assert.That(ImageDelta.Compare(unitReference, CapturePage(firstPage, createDevice)).MaximumChannelDelta)
            .IsEqualTo((byte)0)
            .Because("later bounds and fallback transitions must not alter an earlier page.");
        await Assert.That(File.ReadAllBytes(stagePath).SequenceEqual(source)).IsTrue();

        renderer.Dispose();
        session.Dispose();
        firstPage.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(fieldBytes)
            .Because("the earlier scene still owns its field lease.");
        firstScene.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0UL);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0UL);
    }

    private static void RequireFieldBoundsBackend(SilkGraphicsBackend backend)
    {
        if ((backend == SilkGraphicsBackend.D3D12 && !OperatingSystem.IsWindows()) ||
            (backend == SilkGraphicsBackend.Vulkan && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) ||
            (backend == SilkGraphicsBackend.Metal && !OperatingSystem.IsMacOS()))
        {
            Skip.Test($"{backend} execution is unavailable on this operating system.");
        }
    }

    private static ParityImage CaptureIndependentFieldPage(
        OpenUsdSilkPage page,
        Func<ISilkGraphicsDevice> createDevice)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width,
                Height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.ApplyAndRender(page, color, depth, options);
        _ = renderer.ApplyAndRender(page, color, depth, options);
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static OpenUsdSilkPage CreateIndependentFieldPage(
        string gridPath,
        double[] transform,
        SilkFrameState frame)
    {
        const string materialPath = "/Reference/Material";
        float[] points =
        [
            -0.5f, -0.5f, -0.5f,
             0.5f, -0.5f, -0.5f,
             0.5f,  0.5f, -0.5f,
            -0.5f,  0.5f, -0.5f,
            -0.5f, -0.5f,  0.5f,
             0.5f, -0.5f,  0.5f,
             0.5f,  0.5f,  0.5f,
            -0.5f,  0.5f,  0.5f,
        ];
        uint[] indices =
        [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        ];
        byte[] mesh = VolumeCommandAuthoring.WithMaterialBinding(
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/Reference/Mesh", points, indices, color: [0.9f, 0.36f, 0.12f, 1]),
            materialPath);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(44), (uint)SilkMeshCullStyle.Nothing);
        for (int index = 0; index < transform.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                mesh.AsSpan(80 + (index * sizeof(double))),
                transform[index]);
        }

        byte[] frameCommand = SilkMeshRendererConformance.CreateFrameCommand(
            Width, Height, frame.View.ToArray());
        double[] projection = frame.Projection.ToArray();
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                frameCommand.AsSpan(144 + (index * sizeof(double))),
                projection[index]);
        }

        return new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion,
            1,
            [
                .. frameCommand,
                .. VolumeCommandAuthoring.CreateVolumeMaterialCommand(materialPath, 1, gridPath, "32,32,32"),
                .. mesh,
            ],
            3);
    }
}
