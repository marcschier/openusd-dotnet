// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class VolumeRenderingConformanceTests
{
    private const string MultiFieldLeftMaterial = "/World/Left/Volume/__hdSilkVolumeMaterial";
    private const string MultiFieldRightMaterial = "/World/Right/Volume/__hdSilkVolumeMaterial";
    private const ulong MultiFieldGpuLimit = 8UL * 1024 * 1024;

    [Test]
    [MethodDataSource(nameof(MultiFieldBackends))]
    public async Task SeparateVdbDensityVolumesUpdateFallbackAndRestoreIndependently(
        SilkGraphicsBackend backend)
    {
        await VerifyIndependentDensityVolumes(backend, sharedFile: false).ConfigureAwait(false);
    }

    [Test]
    [MethodDataSource(nameof(MultiFieldBackends))]
    public async Task SharedVdbDensityVolumesSelectIndependentGridsAndRecover(
        SilkGraphicsBackend backend)
    {
        await VerifyIndependentDensityVolumes(backend, sharedFile: true).ConfigureAwait(false);
    }

    public static IEnumerable<SilkGraphicsBackend> MultiFieldBackends() =>
        SilkConformanceBackends.Available();

    private static async Task VerifyIndependentDensityVolumes(SilkGraphicsBackend backend, bool sharedFile)
    {
        RequireFieldBoundsBackend(backend);
        PrependHdSilkNativeSearchPath();
        Directory.CreateDirectory(StageDirectory);
        var assets = new MultiFieldAssets(
            ResolveOpenVdbAsset("sampled_density_unit.vdb").Replace("\\", "/", StringComparison.Ordinal),
            ResolveOpenVdbAsset("sampled_density_shifted_unit.vdb").Replace("\\", "/", StringComparison.Ordinal),
            ResolveOpenVdbAsset("sampled_density_grid_variants_unit.vdb")
                .Replace("\\", "/", StringComparison.Ordinal),
            Path.Combine(StageDirectory, $"missing-left-{Guid.NewGuid():N}.vdb")
                .Replace("\\", "/", StringComparison.Ordinal),
            Path.Combine(StageDirectory, $"missing-right-{Guid.NewGuid():N}.vdb")
                .Replace("\\", "/", StringComparison.Ordinal));
        byte[]? unitReference = ReadSingleMultiFieldDensity(assets.Unit);
        if (unitReference is null)
        {
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        byte[]? shiftedReference = ReadSingleMultiFieldDensity(assets.Shifted);
        await Assert.That(shiftedReference).IsNotNull();
        byte[] shifted = shiftedReference
            ?? throw new InvalidOperationException("The shifted reference field was not decoded.");
        byte[] unit = unitReference;
        await Assert.That(unit.Length).IsEqualTo(32 * 32 * 32 * sizeof(float));
        await Assert.That(shifted.Length).IsEqualTo(unit.Length);
        await Assert.That(unit.SequenceEqual(shifted)).IsFalse()
            .Because("the two selected fields must have independently distinguishable voxel values.");

        ulong fieldBytes = checked((ulong)unit.Length);
        ulong fieldLimit = checked(fieldBytes * 6);
        var residency = new SilkTextureResidencyOptions(fieldBytes * 2, MultiFieldGpuLimit);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var gpuBudget = new SilkGpuTextureBudget(MultiFieldGpuLimit);
        gpuBudget.ConfigureDevice(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width, Height, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        CameraState camera = CreateVolumeFixtureCamera();
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);

        ParityImage unitShifted = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Unit, MultiFieldSelection.Shifted,
            device, color, depth, camera, options);
        ParityImage shiftedShifted = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Shifted, MultiFieldSelection.Shifted,
            device, color, depth, camera, options);
        ParityImage shiftedUnit = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Shifted, MultiFieldSelection.Unit,
            device, color, depth, camera, options);
        ParityImage shiftedUniform = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Shifted, MultiFieldSelection.Uniform,
            device, color, depth, camera, options);
        ParityImage uniformUnit = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Uniform, MultiFieldSelection.Unit,
            device, color, depth, camera, options);
        ParityImage unitUnit = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Unit, MultiFieldSelection.Unit,
            device, color, depth, camera, options);
        ParityImage uniformUniform = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Uniform, MultiFieldSelection.Uniform,
            device, color, depth, camera, options);
        ParityImage leftOnly = CaptureStaticMultiField(
            assets, sharedFile, MultiFieldSelection.Unit, MultiFieldSelection.Absent,
            device, color, depth, camera, options);

        await AssertMultiFieldControlDifference(unitShifted, leftOnly, changedRight: true,
            "the second volume must be visible, not silently omitted.").ConfigureAwait(false);
        await AssertMultiFieldControlDifference(unitShifted, unitUnit, changedRight: true,
            "the right grid must differ visibly from an alias of the left grid.").ConfigureAwait(false);
        await AssertMultiFieldControlDifference(unitShifted, shiftedShifted, changedRight: false,
            "changing only the left grid must affect the left footprint.").ConfigureAwait(false);
        await AssertMultiFieldControlDifference(shiftedShifted, shiftedUnit, changedRight: true,
            "changing only the right grid must affect the right footprint.").ConfigureAwait(false);
        await AssertMultiFieldControlDifference(shiftedUnit, shiftedUniform, changedRight: true,
            "the right uniform fallback must be distinguishable from its sampled field.").ConfigureAwait(false);
        await AssertMultiFieldControlDifference(shiftedUnit, uniformUnit, changedRight: false,
            "the left uniform fallback must be distinguishable from its sampled field.").ConfigureAwait(false);
        await AssertMultiFieldControlDifference(uniformUnit, uniformUniform, changedRight: true,
            "the remaining sampled field must be visible before both fields fall back.").ConfigureAwait(false);

        string animatedStage = Path.Combine(StageDirectory, $"multi-fields-{Guid.NewGuid():N}.usda");
        File.WriteAllText(
            animatedStage, CreateAnimatedMultiFieldStage(assets, sharedFile), new UTF8Encoding(false));
        byte[] source = File.ReadAllBytes(animatedStage);
        using var fieldBudget = new SilkVolumeFieldBudget(fieldLimit);
        using OpenUsdSilkSession session = CreateAvailableFieldSession(animatedStage);
        session.ConfigureVolumeFieldBudget(fieldBudget);
        using var renderer = new SilkMeshRenderer(device, residency);
        using OpenUsdSilkPage firstPage = session.Sync(Width, Height, 1, camera);
        using var firstScene = new SilkSceneState();
        _ = firstScene.Apply(firstPage);
        SilkMaterialTexture retainedLeft = firstScene.Materials[MultiFieldLeftMaterial]
            .GetTexture(SilkMaterialParameter.VolumeDensity)
            ?? throw new InvalidOperationException("The first left density field was not retained.");
        SilkMaterialTexture retainedRight = firstScene.Materials[MultiFieldRightMaterial]
            .GetTexture(SilkMaterialParameter.VolumeDensity)
            ?? throw new InvalidOperationException("The first right density field was not retained.");
        await Assert.That(ReadVolumeDensity(retainedLeft).SequenceEqual(unit)).IsTrue();
        await Assert.That(ReadVolumeDensity(retainedRight).SequenceEqual(shifted)).IsTrue();
        if (sharedFile)
        {
            await Assert.That(retainedLeft.Asset).IsEqualTo(retainedRight.Asset)
                .Because("both materials select different grids from the same VDB asset.");
        }
        else
        {
            await Assert.That(retainedLeft.Asset).IsNotEqualTo(retainedRight.Asset);
        }
        await Assert.That(fieldBudget.Usage.ReservedBytes).IsEqualTo(fieldBytes * 2);
        await Assert.That(fieldBudget.Usage.ReservationCount).IsEqualTo(2UL);

        async Task assertField(string materialPath, byte[]? expected)
        {
            SilkMaterialTexture? texture = renderer.Scene.Materials[materialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity);
            if (expected is null)
            {
                await Assert.That(texture).IsNull()
                    .Because("only the volume whose selected field is missing uses uniform density.");
                return;
            }
            await Assert.That(texture).IsNotNull();
            SilkMaterialTexture selected = texture
                ?? throw new InvalidOperationException($"The density field for {materialPath} was not retained.");
            await Assert.That(selected.UvPrimvar).IsEqualTo("32,32,32");
            await Assert.That(ReadVolumeDensity(selected).SequenceEqual(expected)).IsTrue()
                .Because($"{materialPath} must retain its own selected field, not the other volume's field.");
        }

        async Task assertPage(OpenUsdSilkPage page, MultiFieldFrame expected)
        {
            (ParityImage image, int drawCount) = RenderMultiFieldPage(
                page, renderer, color, depth, options);
            await Assert.That(drawCount).IsEqualTo(1);
            await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(2);
            SilkMeshData leftProxy = renderer.Scene.Meshes.Values.Single(
                static mesh => mesh.Path == "/World/Left/Volume/__hdSilkVolumeProxy");
            SilkMeshData rightProxy = renderer.Scene.Meshes.Values.Single(
                static mesh => mesh.Path == "/World/Right/Volume/__hdSilkVolumeProxy");
            double[] leftTransform = leftProxy.Transform.ToArray();
            double[] rightTransform = rightProxy.Transform.ToArray();
            await Assert.That(leftTransform[12] + (Math.Abs(leftTransform[0]) / 2)).IsLessThan(0.0);
            await Assert.That(rightTransform[12] - (Math.Abs(rightTransform[0]) / 2)).IsGreaterThan(0.0)
                .Because("the volume proxies must occupy separate halves of the viewport.");
            await assertField(MultiFieldLeftMaterial, expected.Left).ConfigureAwait(false);
            await assertField(MultiFieldRightMaterial, expected.Right).ConfigureAwait(false);
            await Assert.That(ImageDelta.Compare(expected.Image, image).MaximumChannelDelta)
                .IsLessThanOrEqualTo(1)
                .Because($"time {expected.Time} must match its separately authored static scene.");

            int sampledFields = (expected.Left is null ? 0 : 1) + (expected.Right is null ? 0 : 1);
            SilkSceneGpuStatistics statistics = renderer.GpuResources.Statistics;
            await Assert.That(statistics.TextureResidentGpuBytes).IsEqualTo(0UL)
                .Because("volume fields are packed into the dedicated camera atlas, not ordinary texture entries.");
            await Assert.That(statistics.TextureResidentDecodedBytes).IsEqualTo(0UL)
                .Because("volume field memory is tracked by the volume-field budget.");
            await Assert.That(statistics.TextureCacheEntryCount).IsEqualTo(0)
                .Because("volume fields are not uploaded as per-field texture cache entries.");
            SilkGpuTextureUsage? atlasUsage = renderer.GpuResources.VolumeCameraAtlasGpuUsage;
            if (sampledFields == 0)
            {
                await Assert.That(atlasUsage?.ReservedBytes ?? 0UL).IsEqualTo(0UL);
            }
            else
            {
                await Assert.That(atlasUsage).IsNotNull();
                SilkGpuTextureUsage atlas = atlasUsage
                    ?? throw new InvalidOperationException("The sampled fields did not reserve a volume atlas.");
                await Assert.That(atlas.MaximumBytes).IsEqualTo(MultiFieldGpuLimit);
                await Assert.That(atlas.ReservedBytes).IsGreaterThan(0UL);
                await Assert.That(atlas.ReservedBytes).IsLessThanOrEqualTo(MultiFieldGpuLimit);
                await Assert.That(atlas.ReservationCount).IsEqualTo(1UL);
            }
            SilkVolumeFieldUsage fieldUsage = fieldBudget.Usage;
            await Assert.That(fieldUsage.ReservedBytes).IsGreaterThanOrEqualTo(fieldBytes * 2);
            await Assert.That(fieldUsage.ReservedBytes)
                .IsLessThanOrEqualTo(checked((ulong)(2 + sampledFields) * fieldBytes))
                .Because("only the retained first page and the current sampled fields may own reservations.");
            await Assert.That(fieldUsage.ReservationCount).IsLessThanOrEqualTo(checked((ulong)(2 + sampledFields)));
            await Assert.That(fieldBudget.Usage.PeakReservedBytes).IsLessThanOrEqualTo(fieldLimit);
            await Assert.That(gpuBudget.Usage.ReservedBytes).IsLessThanOrEqualTo(MultiFieldGpuLimit);
            if (sampledFields == 0)
            {
                await Assert.That(fieldUsage.ReservedBytes).IsEqualTo(fieldBytes * 2)
                    .Because("only the earlier page and scene still retain decoded fields.");
                await Assert.That(fieldUsage.ReservationCount).IsEqualTo(2UL);
            }
        }

        await assertPage(firstPage, new MultiFieldFrame(1, unitShifted, unit, shifted)).ConfigureAwait(false);
        MultiFieldFrame[] frames =
        [
            new(2, shiftedShifted, shifted, shifted),
            new(3, shiftedUnit, shifted, unit),
            new(4, shiftedUniform, shifted, null),
            new(5, shiftedUnit, shifted, unit),
            new(6, uniformUnit, null, unit),
            new(7, unitUnit, unit, unit),
            new(8, unitShifted, unit, shifted),
            new(9, uniformUniform, null, null),
            new(10, unitShifted, unit, shifted),
            new(1, unitShifted, unit, shifted),
        ];
        foreach (MultiFieldFrame frame in frames)
        {
            using OpenUsdSilkPage page = session.Sync(Width, Height, frame.Time, camera);
            await assertPage(page, frame).ConfigureAwait(false);
        }

        await Assert.That(ReadVolumeDensity(retainedLeft).SequenceEqual(unit)).IsTrue();
        await Assert.That(ReadVolumeDensity(retainedRight).SequenceEqual(shifted)).IsTrue();
        await Assert.That(File.ReadAllBytes(animatedStage).SequenceEqual(source)).IsTrue();
        renderer.Dispose();
        await Assert.That(renderer.GpuResources.Statistics.TextureResidentGpuBytes).IsEqualTo(0UL);
        await Assert.That(renderer.GpuResources.Statistics.TextureResidentDecodedBytes).IsEqualTo(0UL);
        session.Dispose();
        await Assert.That(fieldBudget.Usage.ReservedBytes).IsEqualTo(fieldBytes * 2);
        await Assert.That(fieldBudget.Usage.ReservationCount).IsEqualTo(2UL);
        using (var replay = new SilkMeshRenderer(device, residency))
        {
            (ParityImage image, int drawCount) = RenderMultiFieldPage(firstPage, replay, color, depth, options);
            await Assert.That(drawCount).IsEqualTo(1);
            await Assert.That(ImageDelta.Compare(unitShifted, image).MaximumChannelDelta).IsLessThanOrEqualTo(1)
                .Because("the earlier page must render both original fields after the session is disposed.");
        }
        firstPage.Dispose();
        await Assert.That(ReadVolumeDensity(retainedLeft).SequenceEqual(unit)).IsTrue();
        await Assert.That(ReadVolumeDensity(retainedRight).SequenceEqual(shifted)).IsTrue();
        await Assert.That(fieldBudget.Usage.ReservedBytes).IsEqualTo(fieldBytes * 2)
            .Because("the earlier scene owns both fields independently of the page.");
        firstScene.Dispose();
        await Assert.That(() => ReadVolumeDensity(retainedLeft)).Throws<ObjectDisposedException>();
        await Assert.That(() => ReadVolumeDensity(retainedRight)).Throws<ObjectDisposedException>();
        await Assert.That(fieldBudget.Usage.ReservedBytes).IsEqualTo(0UL);
        await Assert.That(fieldBudget.Usage.ReservationCount).IsEqualTo(0UL);
        color.Dispose();
        depth.Dispose();
        await Assert.That(gpuBudget.Usage.ReservedBytes).IsEqualTo(0UL);
        await Assert.That(gpuBudget.Usage.ReservationCount).IsEqualTo(0UL);
        await Assert.That(gpuBudget.Usage.PeakReservedBytes).IsGreaterThan(0UL);
        await Assert.That(gpuBudget.Usage.PeakReservedBytes).IsLessThanOrEqualTo(MultiFieldGpuLimit);
    }

    private static byte[]? ReadSingleMultiFieldDensity(string asset)
    {
        string stage = WriteVdbStage($"multi-field-reference-{Guid.NewGuid():N}", asset, 0, 1, sampleDensity: true);
        using OpenUsdSilkSession session = CreateAvailableFieldSession(stage);
        using OpenUsdSilkPage page = session.Sync(Width, Height, TimeCode);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMaterialTexture? texture = scene.Materials["/World/Vdb/Volume/__hdSilkVolumeMaterial"]
            .GetTexture(SilkMaterialParameter.VolumeDensity);
        return texture is null ? null : ReadVolumeDensity(texture);
    }

    private static async Task AssertMultiFieldControlDifference(
        ParityImage first, ParityImage second, bool changedRight, string reason)
    {
        int changedX = changedRight ? Width / 2 : 0;
        int unchangedX = changedRight ? 0 : Width / 2;
        ImageDelta changed = ImageDelta.Compare(
            Crop(first, changedX, 0, Width / 2, Height),
            Crop(second, changedX, 0, Width / 2, Height));
        ImageDelta unchanged = ImageDelta.Compare(
            Crop(first, unchangedX, 0, Width / 2, Height),
            Crop(second, unchangedX, 0, Width / 2, Height));
        await Assert.That(changed.MaximumChannelDelta).IsGreaterThanOrEqualTo(4).Because(reason);
        await Assert.That(changed.MeanChannelDelta).IsGreaterThanOrEqualTo(0.1).Because(reason);
        await Assert.That(unchanged.MaximumChannelDelta).IsLessThanOrEqualTo(1)
            .Because("changing one nonoverlapping volume must not alter the other footprint.");
    }

    private static ParityImage CaptureStaticMultiField(
        MultiFieldAssets assets,
        bool sharedFile,
        MultiFieldSelection left,
        MultiFieldSelection right,
        ISilkGraphicsDevice device,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        CameraState camera,
        SilkMeshRenderOptions options)
    {
        string stage = Path.Combine(StageDirectory, $"static-multi-field-{Guid.NewGuid():N}.usda");
        File.WriteAllText(stage, CreateStaticMultiFieldStage(assets, sharedFile, left, right), new UTF8Encoding(false));
        using OpenUsdSilkSession session = CreateAvailableFieldSession(stage);
        using OpenUsdSilkPage page = session.Sync(Width, Height, TimeCode, camera);
        using var renderer = new SilkMeshRenderer(device);
        return RenderMultiFieldPage(page, renderer, color, depth, options).Image;
    }

    private static (ParityImage Image, int DrawCount) RenderMultiFieldPage(
        OpenUsdSilkPage page,
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkMeshRenderOptions options)
    {
        _ = renderer.ApplyAndRender(page, color, depth, options);
        SilkMeshRenderResult result = renderer.ApplyAndRender(page, color, depth, options);
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return (new ParityImage(Width, Height, pixels), result.DrawCount);
    }

    private static string CreateStaticMultiFieldStage(
        MultiFieldAssets assets, bool sharedFile, MultiFieldSelection left, MultiFieldSelection right) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            upAxis = "Y"
        )
        def Xform "World"
        {
            {{CreateStaticMultiFieldVolume("Left", "-0.5", assets, sharedFile, left)}}
            {{CreateStaticMultiFieldVolume("Right", "0.5", assets, sharedFile, right)}}
        }
        """;

    private static string CreateStaticMultiFieldVolume(
        string name, string x, MultiFieldAssets assets, bool sharedFile, MultiFieldSelection selection)
    {
        if (selection == MultiFieldSelection.Absent)
        {
            return string.Empty;
        }
        string asset = sharedFile
            ? assets.Shared
            : selection == MultiFieldSelection.Shifted ? assets.Shifted : assets.Unit;
        string grid = sharedFile && selection == MultiFieldSelection.Shifted ? "shifted" : "density";
        string sampled = selection == MultiFieldSelection.Uniform ? "false" : "true";
        return $$"""
            def Xform "{{name}}"
            {
                matrix4d xformOp:transform = (
                    (0.65, 0, 0, 0), (0, 0.65, 0, 0),
                    (0, 0, 0.65, 0), ({{x}}, 0, 0, 1))
                uniform token[] xformOpOrder = ["xformOp:transform"]
                def Volume "Volume"
                {
                    custom float hdsilk:density = 1
                    custom bool hdsilk:sampleDensity = {{sampled}}
                    rel field:density = </World/{{name}}/Volume/Density>
                    def OpenVDBAsset "Density"
                    {
                        asset filePath = @{{asset}}@
                        token fieldName = "{{grid}}"
                    }
                }
            }
            """;
    }

    private static string CreateAnimatedMultiFieldStage(MultiFieldAssets assets, bool sharedFile)
    {
        string leftField = sharedFile
            ? $$"""
                asset filePath = @{{assets.Shared}}@
                token fieldName.timeSamples = {
                    1: "density", 2: "shifted", 3: "shifted", 4: "shifted", 5: "shifted",
                    6: "missing-left", 7: "density", 8: "density", 9: "missing-left", 10: "density"
                }
                """
            : $$"""
                asset filePath.timeSamples = {
                    1: @{{assets.Unit}}@, 2: @{{assets.Shifted}}@, 3: @{{assets.Shifted}}@,
                    4: @{{assets.Shifted}}@, 5: @{{assets.Shifted}}@, 6: @{{assets.MissingLeft}}@,
                    7: @{{assets.Unit}}@, 8: @{{assets.Unit}}@, 9: @{{assets.MissingLeft}}@, 10: @{{assets.Unit}}@
                }
                token fieldName = "density"
                """;
        string rightField = sharedFile
            ? $$"""
                asset filePath = @{{assets.Shared}}@
                token fieldName.timeSamples = {
                    1: "shifted", 2: "shifted", 3: "density", 4: "missing-right", 5: "density",
                    6: "density", 7: "density", 8: "shifted", 9: "missing-right", 10: "shifted"
                }
                """
            : $$"""
                asset filePath.timeSamples = {
                    1: @{{assets.Shifted}}@, 2: @{{assets.Shifted}}@, 3: @{{assets.Unit}}@,
                    4: @{{assets.MissingRight}}@, 5: @{{assets.Unit}}@, 6: @{{assets.Unit}}@,
                    7: @{{assets.Unit}}@, 8: @{{assets.Shifted}}@, 9: @{{assets.MissingRight}}@,
                    10: @{{assets.Shifted}}@
                }
                token fieldName = "density"
                """;
        return $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 10
                upAxis = "Y"
            )
            def Xform "World"
            {
                def Xform "Left"
                {
                    matrix4d xformOp:transform = (
                        (0.65, 0, 0, 0), (0, 0.65, 0, 0),
                        (0, 0, 0.65, 0), (-0.5, 0, 0, 1))
                    uniform token[] xformOpOrder = ["xformOp:transform"]
                    def Volume "Volume"
                    {
                        custom float hdsilk:density = 1
                        custom bool hdsilk:sampleDensity = true
                        rel field:density = </World/Left/Volume/Density>
                        def OpenVDBAsset "Density"
                        {
                            {{leftField}}
                        }
                    }
                }
                def Xform "Right"
                {
                    matrix4d xformOp:transform = (
                        (0.65, 0, 0, 0), (0, 0.65, 0, 0),
                        (0, 0, 0.65, 0), (0.5, 0, 0, 1))
                    uniform token[] xformOpOrder = ["xformOp:transform"]
                    def Volume "Volume"
                    {
                        custom float hdsilk:density = 1
                        custom bool hdsilk:sampleDensity = true
                        rel field:density = </World/Right/Volume/Density>
                        def OpenVDBAsset "Density"
                        {
                            {{rightField}}
                        }
                    }
                }
            }
            """;
    }

    private enum MultiFieldSelection
    {
        Unit,
        Shifted,
        Uniform,
        Absent,
    }

    private readonly record struct MultiFieldAssets(
        string Unit, string Shifted, string Shared, string MissingLeft, string MissingRight);

    private readonly record struct MultiFieldFrame(
        double Time, ParityImage Image, byte[]? Left, byte[]? Right);
}
