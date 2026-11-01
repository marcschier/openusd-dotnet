// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class VolumeRenderingConformanceTests
{
    [Test]
    public async Task SparseFieldMatchesDenseReferenceAndRecoversOnD3D12Warp()
    {
        await VerifySparseField(SilkGraphicsBackend.D3D12).ConfigureAwait(false);
    }

    [Test]
    public async Task SparseFieldMatchesDenseReferenceAndRecoversOnVulkan()
    {
        await VerifySparseField(SilkGraphicsBackend.Vulkan).ConfigureAwait(false);
    }

    [Test]
    public async Task SparseFieldMatchesDenseReferenceAndRecoversOnMetal()
    {
        await VerifySparseField(SilkGraphicsBackend.Metal).ConfigureAwait(false);
    }

    private static async Task VerifySparseField(SilkGraphicsBackend backend)
    {
        RequireFieldBoundsBackend(backend);
        PrependHdSilkNativeSearchPath();
        string asset = ResolveOpenVdbAsset("sampled_density_sparse.vdb")
            .Replace("\\", "/", StringComparison.Ordinal);
        string missing = Path.Combine(StageDirectory, "missing-sparse-field.vdb")
            .Replace("\\", "/", StringComparison.Ordinal);
        Directory.CreateDirectory(StageDirectory);
        string stagePath = Path.Combine(StageDirectory, $"sparse-field-{Guid.NewGuid():N}.usda");
        File.WriteAllText(stagePath, $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 3
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
                                1: @{{asset}}@,
                                2: @{{missing}}@,
                                3: @{{asset}}@
                            }
                            token fieldName = "density"
                        }
                    }
                }
            }
            """, new UTF8Encoding(false));
        byte[] source = File.ReadAllBytes(stagePath);
        byte[] expectedDensity = CreateSparseDensityReference();
        string densePath = Path.Combine(StageDirectory, $"sparse-reference-{Guid.NewGuid():N}.r32");
        File.WriteAllBytes(densePath, expectedDensity);
        ulong fieldBytes = (ulong)expectedDensity.Length;
        using OpenUsdSilkSession session = CreateAvailableFieldSession(stagePath);
        using var budget = new SilkVolumeFieldBudget(2 * fieldBytes);
        session.ConfigureVolumeFieldBudget(budget);
        var camera = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []);
        using OpenUsdSilkPage firstPage = session.Sync(Width, Height, 1, camera);
        using var firstScene = new SilkSceneState();
        _ = firstScene.Apply(firstPage);
        const string materialPath = "/World/Vdb/Volume/__hdSilkVolumeMaterial";
        SilkMaterialTexture? firstTexture = firstScene.Materials[materialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity);
        if (firstTexture is null)
        {
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        await Assert.That(firstTexture.UvPrimvar).IsEqualTo("9,9,9");
        await Assert.That(ReadVolumeDensity(firstTexture).SequenceEqual(expectedDensity)).IsTrue()
            .Because("dense storage must include the background, inactive values, and all active sparse voxels.");
        await Assert.That(firstScene.Meshes.Values.Single().Transform.ToArray()
            .SequenceEqual(SilkMeshRendererConformance.Identity())).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(fieldBytes);

        Func<ISilkGraphicsDevice> createDevice = () => SilkDepthCaptureConformance.CreateDevice(backend);
        using OpenUsdSilkPage expectedPage = CreateIndependentFieldPage(
            densePath, SilkMeshRendererConformance.Identity(), firstScene.Frame, "9,9,9");
        ParityImage reference = CaptureIndependentFieldPage(expectedPage, createDevice);
        ParityImage background = CaptureStage(
            WriteVdbStage($"sparse-{backend}-background", asset, 0, 0.125, sampleDensity: false),
            createDevice);
        ParityImage uniformFallback = CaptureStage(
            WriteVdbStage($"sparse-{backend}-fallback", asset, 0, 1, sampleDensity: false),
            createDevice);
        await Assert.That(ImageDelta.Compare(reference, background).MaximumChannelDelta)
            .IsGreaterThanOrEqualTo((byte)12)
            .Because("the sparse active voxels must affect the image independently of the background.");

        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width, Height, SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);

        ParityImage render(OpenUsdSilkPage page)
        {
            _ = renderer.ApplyAndRender(page, color, depth, options);
            _ = renderer.ApplyAndRender(page, color, depth, options);
            byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
            color.ReadbackForTesting(pixels);
            return new ParityImage(Width, Height, pixels);
        }

        await Assert.That(ImageDelta.Compare(reference, render(firstPage)).MaximumChannelDelta)
            .IsLessThanOrEqualTo((byte)1);
        using OpenUsdSilkPage fallback = session.Sync(Width, Height, 2, camera);
        await Assert.That(ImageDelta.Compare(uniformFallback, render(fallback)).MaximumChannelDelta)
            .IsLessThanOrEqualTo((byte)1);
        await Assert.That(renderer.Scene.Materials[materialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)).IsNull();
        await Assert.That(renderer.GpuResources.Statistics.TextureResidentGpuBytes).IsEqualTo(0UL);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(fieldBytes)
            .Because("the first page and scene still retain the original sparse field.");

        using OpenUsdSilkPage recovered = session.Sync(Width, Height, 3, camera);
        await Assert.That(ImageDelta.Compare(reference, render(recovered)).MaximumChannelDelta)
            .IsLessThanOrEqualTo((byte)1);
        SilkMaterialTexture recoveredTexture = renderer.Scene.Materials[materialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)!;
        await Assert.That(ReadVolumeDensity(recoveredTexture).SequenceEqual(expectedDensity)).IsTrue();
        await Assert.That(ReadVolumeDensity(firstTexture).SequenceEqual(expectedDensity)).IsTrue();
        await Assert.That(ImageDelta.Compare(reference, CapturePage(firstPage, createDevice)).MaximumChannelDelta)
            .IsLessThanOrEqualTo((byte)1);
        await Assert.That(File.ReadAllBytes(stagePath).SequenceEqual(source)).IsTrue();

        renderer.Dispose();
        recovered.Dispose();
        fallback.Dispose();
        session.Dispose();
        firstPage.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(fieldBytes);
        firstScene.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0UL);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0UL);
    }

    private static byte[] CreateSparseDensityReference()
    {
        const int dimension = 9;
        byte[] result = new byte[dimension * dimension * dimension * sizeof(float)];
        int offset = 0;
        for (int z = -2; z <= 6; z++)
        {
            for (int y = -3; y <= 5; y++)
            {
                for (int x = -4; x <= 4; x++)
                {
                    float value = 0.125f;
                    if (x == -4 && y == -3 && z == -2)
                    {
                        value = 0;
                    }
                    else if (x == 4 && y == 5 && z == 6)
                    {
                        value = 1;
                    }
                    else if (x >= -1 && x <= 1 && y >= 0 && y <= 1)
                    {
                        value = 0.75f;
                    }
                    else if (x == 3 && y == 3 && z == 4)
                    {
                        value = 0.5f;
                    }
                    BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(offset), value);
                    offset += sizeof(float);
                }
            }
        }
        return result;
    }
}
