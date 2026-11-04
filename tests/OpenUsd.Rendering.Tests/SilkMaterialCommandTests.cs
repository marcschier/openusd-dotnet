// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Proves the ABI 5 material wire format round-trips and that a malformed table
/// is rejected at construction rather than at an arbitrary later accessor.
/// </summary>
public sealed class SilkMaterialCommandTests
{
    [Test]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, true, true)]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    public async Task PackedUdimPreservesSparseRoutingAndConstantTiles(bool floatingPoint, bool constant, bool scalar)
    {
        var budget = new SilkCpuTextureBudget(4096);
        using var device = new TextureGraphicsDevice { SupportsRowUploads = true, MaxTextureDimension2D = 16 };
        float gain = floatingPoint ? 2 : 1;
        SilkMaterialParameter parameter = scalar ? SilkMaterialParameter.Roughness : SilkMaterialParameter.DiffuseColor;
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert("/Atlas", SilkSurfaceKind.PreviewSurface, [],
        [
            new TextureSpec(parameter, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                SilkColorSpace.Raw, scalar ? 1 : 3, [gain, gain, gain, 1], [0, 0, 0, 0], [1, 1, 1, 1],
                    "<UDIM>.png", "st")
        ]));
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unowned decoding is forbidden."),
            _ => [new SilkUdimTile(1001, "A"), new SilkUdimTile(1003, "B")],
            new SilkTextureResidencyOptions(4096, 4096, true), cpuTextureBudget: budget,
            ownedImageDecoder: (asset, _, pool) =>
            {
                IDisposable reservation = pool.Reserve(64);
                byte[] pixels = new byte[64];
                for (int index = 0; index < 16; index++)
                {
                    pixels[index * 4] = (byte)((asset == "A" ? 64 : 128) + (constant ? 0 : index));
                    pixels[index * 4 + 1] = (byte)(index * 7);
                    pixels[index * 4 + 2] = (byte)(255 - pixels[index * 4]);
                    pixels[index * 4 + 3] = 255;
                }
                var image = new SilkDecodedImage(4, 4, pixels);
                image.OwnReservation(reservation);
                return image;
            });
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, parameter);
        SilkTextureDescriptor descriptor = device.CreatedTextures.Single();
        await Assert.That(descriptor.Format).IsEqualTo(scalar
            ? floatingPoint ? SilkTextureFormat.R32Float : SilkTextureFormat.R8Unorm
            : floatingPoint ? SilkTextureFormat.Rgba32Float : SilkTextureFormat.Rgba8Unorm);
        byte[] pixels = device.RowUploads.SelectMany(static row => row.Pixels).ToArray();
        int components = scalar ? 1 : 4;
        float read(int index, int component = 0) => floatingPoint
            ? BitConverter.ToSingle(pixels, (index * components + component) * 4)
            : pixels[index * components + component] / 255f;
        int metadata(int index) => (int)MathF.Round(read(index) * 255);
        await Assert.That(metadata(0)).IsEqualTo(255);
        await Assert.That(metadata(4)).IsEqualTo(3);
        await Assert.That(metadata(5)).IsEqualTo(1);
        await Assert.That(metadata(8)).IsEqualTo(2);
        await Assert.That(metadata(10)).IsEqualTo(1);
        await Assert.That(metadata(12)).IsEqualTo(0);
        await Assert.That(metadata(14)).IsEqualTo(2);
        await Assert.That(read(1)).IsEqualTo(gain);
        int columns = metadata(6);
        int rows = (2 + columns - 1) / columns;
        int width = (int)descriptor.Width;
        int metadataRows = (16 + width - 1) / width;
        int cellWidth = width / columns;
        int cellHeight = ((int)descriptor.Height - metadataRows) / rows;
        await Assert.That(cellWidth).IsEqualTo(constant ? 3 : 6);
        await Assert.That(cellHeight).IsEqualTo(constant ? 3 : 6);
        for (int tile = 0; tile < 2; tile++)
        {
            for (int y = 0; y < cellHeight; y++)
            {
                for (int x = 0; x < cellWidth; x++)
                {
                    int sourceX = constant ? 0 : Math.Clamp(x - 1, 0, 3);
                    int sourceY = constant ? 0 : 3 - Math.Clamp(y - 1, 0, 3);
                    float expected = ((tile == 0 ? 64 : 128) + sourceY * 4 + sourceX) / 255f * gain;
                    int pixel = (metadataRows + tile / columns * cellHeight + y) * width +
                        tile % columns * cellWidth + x;
                    await Assert.That(read(pixel)).IsEqualTo(expected);
                    if (!scalar)
                    {
                        await Assert.That(read(pixel, 1)).IsEqualTo((sourceY * 4 + sourceX) * 7 / 255f * gain);
                        await Assert.That(read(pixel, 2)).IsEqualTo(
                            (255 - (tile == 0 ? 64 : 128) - sourceY * 4 - sourceX) / 255f * gain);
                        await Assert.That(read(pixel, 3)).IsEqualTo(1f);
                    }
                }
            }
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 2)]
    [Arguments(false, 4)]
    [Arguments(true, 4)]
    public async Task StreamedUdimBandsPreserveEverySparsePixelAndGutter(bool floatingPoint, int bandRows)
    {
        int pixelBytes = floatingPoint ? 16 : 4;
        var budget = new SilkCpuTextureBudget(checked((ulong)((32 + 2 * bandRows * 18) * pixelBytes)));
        using var device = new TextureGraphicsDevice { SupportsRowUploads = true };
        float gain = floatingPoint ? 2 : 1;
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert("/Atlas", SilkSurfaceKind.PreviewSurface, [],
        [
            new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                SilkColorSpace.Raw, 4, [gain, gain, gain, 1], [0, 0, 0, 0], [0, 0, 1, 1], "<UDIM>.png", "st")
        ]));
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unowned decoding is forbidden."),
            _ => [new SilkUdimTile(1012, "A"), new SilkUdimTile(1034, "B")],
            new SilkTextureResidencyOptions(4096, 4096, true), cpuTextureBudget: budget,
            ownedImageDecoder: (asset, _, pool) =>
            {
                IDisposable reservation = pool.Reserve(64);
                byte[] pixels = new byte[64];
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int offset = (y * 4 + x) * 4;
                        pixels[offset] = (byte)(16 + 20 * x);
                        pixels[offset + 1] = (byte)(32 + 20 * y);
                        pixels[offset + 2] = asset == "A" ? (byte)64 : (byte)104;
                        pixels[offset + 3] = 255;
                    }
                }
                var image = new SilkDecodedImage(4, 4, pixels);
                image.OwnReservation(reservation);
                return image;
            });
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        await Assert.That(device.CreatedTextures.Single().Width).IsEqualTo(18u);
        await Assert.That(device.CreatedTextures.Single().Height).IsEqualTo(19u);
        await Assert.That(device.RowUploads.Count).IsEqualTo((19 + bandRows - 1) / bandRows);
        byte[] actual = device.RowUploads.SelectMany(static row => row.Pixels).ToArray();
        byte[] expected = new byte[18 * 19 * pixelBytes];
        for (int y = 0; y < 19; y++)
        {
            for (int x = 0; x < 18; x++)
            {
                float[] pixel = [0, 0, gain, 1];
                int cellX = x / 6;
                int cellY = (y - 1) / 6;
                if (y > 0 && ((cellX == 0 && cellY == 0) || (cellX == 2 && cellY == 2)))
                {
                    int u = Math.Clamp(x % 6 - 1, 0, 3);
                    int v = 3 - Math.Clamp((y - 1) % 6 - 1, 0, 3);
                    pixel = [(16 + 20 * u) / 255f * gain, (32 + 20 * v) / 255f * gain,
                        (cellX == 0 ? 64 : 104) / 255f * gain, 1];
                }
                if (x == 0 && y == 0)
                {
                    pixel = [1 / 255f, 1 / 255f, 3 / 255f, 3 / 255f];
                }
                int offset = (y * 18 + x) * pixelBytes;
                if (floatingPoint)
                {
                    MemoryMarshal.AsBytes(pixel.AsSpan()).CopyTo(expected.AsSpan(offset, 16));
                }
                else
                {
                    for (int component = 0; component < 4; component++)
                    {
                        expected[offset + component] = (byte)MathF.Round(pixel[component] * 255);
                    }
                }
            }
        }
        for (int index = 0; index < device.RowUploads.Count; index++)
        {
            await Assert.That(device.RowUploads[index].Region.FirstRow).IsEqualTo((uint)(index * bandRows));
            await Assert.That(device.RowUploads[index].Region.RowCount)
                .IsEqualTo((uint)Math.Min(bandRows, 19 - index * bandRows));
        }
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(budget.MaximumBytes);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(commands.UploadCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DetailNormalCarriesIndependentCoordinatesWithoutUsingTheComposite(bool transformed)
    {
        (SilkMaterialParameter, float[])[] rows = transformed
            ? [(SilkMaterialParameter.DetailUvRow0, [0, 2, 0.25f]),
                (SilkMaterialParameter.DetailUvRow1, [-3, 0, -0.5f])] : [];
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Detail", SilkSurfaceKind.MdlDistilled, rows,
            [DetailTexture()], uvTransform: [4, 0, 0, 5, 0.1f, 0.2f]));
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv | SilkShaderFeatures.DetailNormalMap);
        await Assert.That(material.GetCompositeTexture()).IsNull();
        await Assert.That(material.GetTexture(SilkMaterialParameter.Normal)).IsNull();
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(144))).IsEqualTo(4f);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(164))).IsEqualTo(5f);
        float[] expected = transformed ? [0, 2, 0.25f, 0, -3, 0, -0.5f, 0] : [1, 0, 0, 0, 0, 1, 0, 0];
        await Assert.That(MemoryMarshal.Cast<byte, float>(constants.AsSpan(240, 32)).SequenceEqual(expected)).IsTrue();
        var permutation = new SilkShaderPermutationId(SilkShaderFeatures.Uv | material.GetTextureFeatures());
        await Assert.That(permutation.MeshVertexArtifactName).IsEqualTo("mesh.vertex.uv+normal");
        await Assert.That(permutation.MeshFragmentArtifactName).IsEqualTo("mesh.fragment.uv+material+normal");
        SilkBindingLayoutDescriptor layout = permutation.CreateMeshBindingLayout();
        await Assert.That(layout.MaterialSlots.Count(slot => slot.Binding == 9)).IsEqualTo(1);
        await Assert.That(layout.MaterialSlots.Count(slot => slot.Binding == 13)).IsEqualTo(1);
    }

    [Test]
    [Arguments("shape")]
    [Arguments("nan")]
    [Arguments("missing-row")]
    [Arguments("duplicate-row")]
    [Arguments("scalar-normal")]
    [Arguments("duplicate-normal")]
    [Arguments("composite")]
    [Arguments("volume")]
    public async Task DetailNormalRejectsAmbiguousOrInvalidInputs(string failure)
    {
        (SilkMaterialParameter, float[])[] rows =
            [(SilkMaterialParameter.DetailUvRow0, failure == "shape" ? [1, 0] :
                [failure == "nan" ? float.NaN : 1, 0, 0]),
             (SilkMaterialParameter.DetailUvRow1, [0, 1, 0])];
        if (failure == "missing-row")
        {
            rows = [rows[0]];
        }
        if (failure == "duplicate-row")
        {
            rows = [.. rows, rows[0]];
        }
        if (failure == "scalar-normal")
        {
            rows = [(SilkMaterialParameter.DetailNormal, [0, 0, 1])];
        }
        TextureSpec detail = DetailTexture();
        TextureSpec[] textures = failure == "duplicate-normal" ? [detail, detail] :
            [failure == "composite" ? detail with { CompositeOperator = SilkCompositeOperator.Multiply } : detail];
        byte[] page = CreateMaterialUpsert("/Detail",
            failure == "volume" ? SilkSurfaceKind.VolumeDensity : SilkSurfaceKind.MdlDistilled, rows, textures);
        await Assert.That(() => CopyMaterial(page)).Throws<InvalidDataException>();
    }

    private static TextureSpec DetailTexture() =>
        new(SilkMaterialParameter.DetailNormal, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
            SilkColorSpace.Raw, 3, [1, 1, 1, 1], [0, 0, 0, 0], [0.5f, 0.5f, 1, 1], "detail.png", "st");

    [Test]
    [Arguments("normal", "st1", true)]
    [Arguments("detail-beside-primary-normal", "st1", false)]
    [Arguments("detail-only", "st1", true)]
    [Arguments("shared", "", false)]
    public async Task SurfaceTexturesOnASecondPrimvarSelectTheSecondSet(
        string shape, string expectedSecondary, bool expectedTangentsFromSecondary)
    {
        static TextureSpec texture(SilkMaterialParameter parameter, int components, string primvar) =>
            new(parameter, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat, SilkColorSpace.Raw, components,
                [1, 1, 1, 1], [0, 0, 0, 0], [0.5f, 0.5f, 1, 1], $"{parameter}.png", primvar);
        TextureSpec diffuse = texture(SilkMaterialParameter.DiffuseColor, 3, "st");
        TextureSpec[] textures = shape switch
        {
            "normal" =>
            [
                diffuse,
                texture(SilkMaterialParameter.Normal, 3, "st1"),
                texture(SilkMaterialParameter.Roughness, 1, "st1"),
            ],
            "detail-beside-primary-normal" =>
            [
                diffuse,
                texture(SilkMaterialParameter.Normal, 3, "st"),
                DetailTexture() with { UvPrimvar = "st1" },
            ],
            "detail-only" => [diffuse, DetailTexture() with { UvPrimvar = "st1" }],
            _ => [diffuse, texture(SilkMaterialParameter.Normal, 3, "st")],
        };
        SilkShaderFeatures expectedMask = shape switch
        {
            "normal" => SilkShaderFeatures.NormalMap | SilkShaderFeatures.RoughnessMetallicMap,
            "shared" => SilkShaderFeatures.None,
            _ => SilkShaderFeatures.DetailNormalMap,
        };
        (SilkMaterialParameter, float[])[] rows = shape.StartsWith("detail", StringComparison.Ordinal)
            ? [(SilkMaterialParameter.DetailUvRow0, [1, 0, 0]), (SilkMaterialParameter.DetailUvRow1, [0, 1, 0])]
            : [];
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Sets", SilkSurfaceKind.PreviewSurface, rows, textures));
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        await Assert.That(material.GetPrimaryUvPrimvar()).IsEqualTo("st");
        await Assert.That(material.GetSecondaryUvPrimvar()).IsEqualTo(expectedSecondary);
        await Assert.That(material.GetSecondaryUvTextureMask()).IsEqualTo(expectedMask);
        await Assert.That(material.TangentsFromSecondaryUv()).IsEqualTo(expectedTangentsFromSecondary);
        // The mask rides in the w lane of the second UV transform row, which the
        // shader reads to pick a coordinate set per sampled slot.
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(172)))
            .IsEqualTo((float)(uint)expectedMask);
    }

    [Test]
    [Arguments(0f)]
    [Arguments(0.5f)]
    [Arguments(1f)]
    public async Task SpecularLevelCarriesASeparateDielectricCurveWeight(float level)
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert("/Material", SilkSurfaceKind.MdlDistilled,
            [(SilkMaterialParameter.OmniPbrSpecularLevel, [level])], []));
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(76))).IsEqualTo(level);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(140))).IsEqualTo(1f);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(44))).IsEqualTo(1.5f);
    }

    [Test]
    [Arguments("negative")]
    [Arguments("above-one")]
    [Arguments("nan")]
    [Arguments("infinite")]
    [Arguments("shape")]
    [Arguments("duplicate")]
    public async Task SpecularLevelRejectsMalformedOrAmbiguousConstants(string failure)
    {
        float value = failure switch
        {
            "negative" => -0.01f,
            "above-one" => 1.01f,
            "nan" => float.NaN,
            "infinite" => float.PositiveInfinity,
            _ => 0.5f
        };
        (SilkMaterialParameter, float[])[] scalars = failure == "duplicate"
            ? [(SilkMaterialParameter.OmniPbrSpecularLevel, [value]),
                (SilkMaterialParameter.OmniPbrSpecularLevel, [value])]
            : [(SilkMaterialParameter.OmniPbrSpecularLevel, failure == "shape" ? [value, value] : [value])];
        byte[] page = CreateMaterialUpsert("/Material", SilkSurfaceKind.MdlDistilled, scalars, []);
        await Assert.That(() => CopyMaterial(page)).Throws<InvalidDataException>();
    }

    /// <summary>The affine hdSilk publishes when no place2d node was folded.</summary>
    private static readonly float[] IdentityUvTransform = [1f, 0f, 0f, 1f, 0f, 0f];

    /// <summary>A place2d fold that doubles the coordinate and shifts it.</summary>
    private static readonly float[] PlacedUvTransform = [2f, 0f, 0f, 2f, -0.25f, 0.5f];
    private static readonly float[] PrecisionSourceColor = [0.25f, 0.5f, 0.75f, 1f];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MaterialRequestsPrecisionBeforeDecodingWithTheSharedOwner(bool extended)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(32);
        cpu.ConfigureDevice(device);
        bool? requestedFloat = null;
        bool? requestedLinear = null;
        SilkCpuTextureBudget? suppliedOwner = null;
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("The legacy decoder must not be used."),
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, _) =>
                throw new InvalidOperationException("The precision-unaware owner must not be used."),
            precisionImageDecoder: (_, linear, preferred, owner) =>
            {
                requestedFloat = preferred;
                requestedLinear = linear;
                suppliedOwner = owner;
                IDisposable reservation = owner!.Reserve(preferred ? 16ul : 4ul);
                byte[] pixels = preferred
                    ? MemoryMarshal.AsBytes(PrecisionSourceColor.AsSpan()).ToArray()
                    : [64, 128, 192, 255];
                var image = new SilkDecodedImage(1, 1, pixels,
                    preferred ? SilkTextureFormat.Rgba32Float : SilkTextureFormat.Rgba8Unorm);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.EmissiveColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                SilkColorSpace.Srgb, 3, [extended ? 2f : 1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f],
                [1f, 0f, 1f, 1f], "source.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor);
        await Assert.That(requestedFloat).IsEqualTo(extended);
        await Assert.That(requestedLinear).IsTrue();
        await Assert.That(suppliedOwner).IsSameReferenceAs(cpu);
        await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(extended ? 32ul : 8ul);
        resources.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkTextureChannel.AverageRgb, 31ul, "ordinary")]
    [Arguments(SilkTextureChannel.AverageRgb, 32ul, "ordinary")]
    [Arguments(SilkTextureChannel.LuminanceRgb, 31ul, "ordinary")]
    [Arguments(SilkTextureChannel.LuminanceRgb, 32ul, "ordinary")]
    [Arguments(SilkTextureChannel.MaximumRgb, 31ul, "ordinary")]
    [Arguments(SilkTextureChannel.MaximumRgb, 32ul, "ordinary")]
    [Arguments(SilkTextureChannel.AverageRgb, 32ul, "fallback")]
    [Arguments(SilkTextureChannel.LuminanceRgb, 32ul, "fallback")]
    [Arguments(SilkTextureChannel.MaximumRgb, 32ul, "fallback")]
    [Arguments(SilkTextureChannel.AverageRgb, 415ul, "udim")]
    [Arguments(SilkTextureChannel.AverageRgb, 416ul, "udim")]
    [Arguments(SilkTextureChannel.LuminanceRgb, 416ul, "udim")]
    [Arguments(SilkTextureChannel.MaximumRgb, 416ul, "udim")]
    public async Task OpacityRgbReductionRunsAfterAffineAndRetainsExactOwnedFloatStorage(
        SilkTextureChannel channel, ulong maximum, string kind)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(maximum);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded decoding is forbidden."),
            _ => [new SilkUdimTile(1001, "1001.png"), new SilkUdimTile(1002, "1002.png")],
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, budget) =>
            {
                if (kind == "fallback")
                {
                    throw new FileNotFoundException("missing");
                }
                IDisposable reservation = budget.Reserve(4);
                var image = new SilkDecodedImage(1, 1, [255, 128, 0, 64]);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.Opacity, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                SilkColorSpace.Raw, 1, [0.5f, 1f, 1f, 1f], [0f, 0.25f, 0f, 0f],
                [1f, 128f / 255f, 0f, 64f / 255f],
                kind == "udim" ? "<UDIM>.png" : "opacity.png", "st", Channel: channel)]));
        using var commands = new TextureCommandList();
        if (maximum is 31 or 415)
        {
            await Assert.That(() => resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Opacity))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(device.CreatedTextureFormats).IsEmpty();
            await Assert.That(commands.Uploads).IsEmpty();
        }
        else
        {
            resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Opacity);
            float green = (128f / 255f) + 0.25f;
            float expected = channel switch
            {
                SilkTextureChannel.AverageRgb => (0.5f + green) / 3f,
                SilkTextureChannel.LuminanceRgb => (0.5f * 0.212671f) + (green * 0.715160f),
                _ => green
            };
            float[] pixels = MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray();
            int offset = kind == "udim" ? (2 * 6 + 1) * 4 : 0;
            await Assert.That(pixels.AsSpan(offset, 4)
                .SequenceEqual(new[] { expected, expected, expected, expected })).IsTrue();
            await Assert.That(device.CreatedTextureFormats.Single()).IsEqualTo(SilkTextureFormat.Rgba32Float);
            await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(kind == "udim" ? 416ul : 32ul);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(kind == "udim" ? 384ul : 16ul);
        }
        resources.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkTextureChannel.AverageRgb)]
    [Arguments(SilkTextureChannel.LuminanceRgb)]
    [Arguments(SilkTextureChannel.MaximumRgb)]
    public async Task OpacityRgbReductionCannotSilentlyReachDisplacementOrColor(SilkTextureChannel channel)
    {
        foreach (SilkMaterialParameter parameter in new[]
            { SilkMaterialParameter.Displacement, SilkMaterialParameter.VolumeDensity,
                SilkMaterialParameter.DiffuseColor })
        {
            byte[] page = CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [],
                [new TextureSpec(parameter, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 1, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 0f, 1f],
                    "texture.png", "st", Channel: channel)]);
            await Assert.That(() => CopyMaterial(page)).Throws<InvalidDataException>();
        }
    }

    [Test]
    [Arguments(0f, "ordinary")]
    [Arguments(0.25f, "ordinary")]
    [Arguments(1f, "ordinary")]
    [Arguments(-0.25f, "ordinary")]
    [Arguments(1.25f, "ordinary")]
    [Arguments(0.25f, "fallback")]
    [Arguments(1f, "fallback")]
    [Arguments(0.25f, "udim")]
    [Arguments(1f, "udim")]
    public async Task LuminanceMixPrecedesAffineAcrossOwnedImagePathsAndPreservesAlpha(float amount, string kind)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(4096);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded decoding is forbidden."),
            _ => [new SilkUdimTile(1001, "1001.png"), new SilkUdimTile(1002, "1002.png")],
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, budget) =>
            {
                if (kind == "fallback")
                {
                    throw new FileNotFoundException("missing");
                }
                IDisposable reservation = budget.Reserve(4);
                var image = new SilkDecodedImage(1, 1, [255, 0, 0, 128]);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface,
            [(SilkMaterialParameter.DiffuseTextureLuminanceMix, [0.25f, 0.5f, 0.25f, amount])],
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                SilkColorSpace.Raw, 3, [2f, 0.5f, 1f, 1f], [0.125f, 0.25f, -0.125f, 0f],
                [1f, 0f, 0f, 128f / 255f], kind == "udim" ? "<UDIM>.png" : "texture.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        await Assert.That(device.CreatedTextureFormats.Single()).IsEqualTo(SilkTextureFormat.Rgba32Float);
        float[] pixels = MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray();
        int offset = kind == "udim" ? (2 * 6 + 1) * 4 : 0;
        float[] expected = [2.125f - (1.5f * amount), 0.25f + (0.125f * amount),
            -0.125f + (0.25f * amount), 128f / 255f];
        await Assert.That(pixels.AsSpan(offset, 4).SequenceEqual(expected)).IsTrue();
        if (kind == "udim")
        {
            await Assert.That(pixels.AsSpan((2 * 6 + 4) * 4, 4).SequenceEqual(expected)).IsTrue();
        }
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsEqualTo(kind == "fallback");
        resources.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments("shape")]
    [Arguments("negative")]
    [Arguments("sum")]
    [Arguments("nan")]
    [Arguments("infinite")]
    [Arguments("amount-nan")]
    [Arguments("amount-infinite")]
    [Arguments("duplicate")]
    public async Task LuminanceMixRejectsMalformedScalarRecordsBeforeRetention(string kind)
    {
        float[] values = [0.25f, 0.5f, 0.25f, 0.5f];
        switch (kind)
        {
            case "shape":
                values = [0.5f];
                break;
            case "negative":
                values[0] = -0.25f;
                break;
            case "sum":
                values[0] = 0.5f;
                break;
            case "nan":
                values[0] = float.NaN;
                break;
            case "infinite":
                values[2] = float.PositiveInfinity;
                break;
            case "amount-nan":
                values[3] = float.NaN;
                break;
            case "amount-infinite":
                values[3] = float.PositiveInfinity;
                break;
        }
        (SilkMaterialParameter, float[])[] scalars = kind == "duplicate"
            ? [(SilkMaterialParameter.DiffuseTextureLuminanceMix, values),
               (SilkMaterialParameter.DiffuseTextureLuminanceMix, values)]
            : [(SilkMaterialParameter.DiffuseTextureLuminanceMix, values)];
        byte[] page = CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, scalars, []);
        await Assert.That(() => CopyMaterial(page)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task LuminanceMixIsScalarOnlyAndOldPagesAreRefused()
    {
        byte[] page = CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.DiffuseTextureLuminanceMix,
                SilkTextureWrap.Repeat, SilkTextureWrap.Repeat, SilkColorSpace.Raw, 3,
                [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "texture.png", "st")]);
        await Assert.That(() => CopyMaterial(page)).Throws<InvalidDataException>();
        await Assert.That(() => SilkCommandParser.ValidatePageAbi(24)).Throws<InvalidDataException>();
        await Assert.That(() => SilkCommandParser.ValidatePageAbi(29)).Throws<InvalidDataException>();
        await Assert.That(() => SilkCommandParser.ValidatePageAbi(32)).Throws<InvalidDataException>();
        SilkCommandParser.ValidatePageAbi(SilkCommandParser.PageAbiVersion);
    }

    [Test]
    [Arguments(0f, SilkMaterialParameter.DiffuseColor)]
    [Arguments(1f, SilkMaterialParameter.DiffuseColor)]
    [Arguments(0f, SilkMaterialParameter.EmissiveColor)]
    [Arguments(1f, SilkMaterialParameter.EmissiveColor)]
    public async Task LuminanceMixPromotesOnlyActiveDiffuseTextures(float amount, SilkMaterialParameter parameter)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(32);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded decoding is forbidden."),
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, budget) =>
            {
                IDisposable reservation = budget.Reserve(4);
                var image = new SilkDecodedImage(1, 1, [255, 0, 0, 128]);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface,
            [(SilkMaterialParameter.DiffuseTextureLuminanceMix, [0.25f, 0.5f, 0.25f, amount])],
            [new TextureSpec(parameter, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat, SilkColorSpace.Raw, 3,
                [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 0f, 1f], "texture.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, parameter);
        bool active = amount != 0 && parameter == SilkMaterialParameter.DiffuseColor;
        await Assert.That(device.CreatedTextureFormats.Single())
            .IsEqualTo(active ? SilkTextureFormat.Rgba32Float : SilkTextureFormat.Rgba8Unorm);
        if (active)
        {
            await Assert.That(MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray()
                .SequenceEqual(new[] { 0.25f, 0.25f, 0.25f, 128f / 255f })).IsTrue();
        }
        else
        {
            await Assert.That(commands.Uploads.Single().SequenceEqual(new byte[] { 255, 0, 0, 128 })).IsTrue();
        }
        await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(active ? 32ul : 8ul);
        resources.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task AdmittedSurfaceReplacementRefusalKeepsThePreviousConstantsAndPage()
    {
        static byte[] material(float value) => CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [value])], textures: []);
        using var device = new TextureGraphicsDevice();
        new SilkGpuTextureBudget(1024).ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device, SilkShaderBinaryFormat.Dxil,
            (_, _) => throw new InvalidOperationException("No image is used."));
        using var first = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. material(0.25f), .. CreateMeshUpsert("/Mesh", "/Material")], 2);
        renderer.ApplyPage(first);
        TextureGraphicsBuffer[] frames =
            [.. device.Buffers.Where(buffer => buffer.Size == SilkFrameUniformWriter.ByteSize)];
        await Assert.That(frames.Length).IsEqualTo(2);
        TextureGraphicsBuffer preparedFrame = frames[0];
        await Assert.That(preparedFrame.Writes).IsEqualTo(0);
        renderer.GpuResources.RequireFrameBuffer(renderer.Scene, RenderOutputTransform.Identity, 0);
        await Assert.That(preparedFrame.Writes).IsEqualTo(1);
        renderer.GpuResources.RequireFrameBuffer(renderer.Scene, RenderOutputTransform.Identity, 0);
        await Assert.That(preparedFrame.Writes).IsEqualTo(1);
        SilkMeshData mesh = renderer.Scene.Meshes.Values.Single();
        ISilkGraphicsBuffer original = renderer.GpuResources.RequireSurfaceBuffer(
            renderer.Scene, mesh, RenderHeadlight.Deterministic);
        byte[] before = new byte[checked((int)original.Size)];
        original.ReadbackForTesting(before);
        ulong revision = renderer.Scene.Revision;
        int live = device.Buffers.Count(buffer => !buffer.Released);
        using var update = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, material(0.75f), 1);
        device.RefuseBufferSize = original.Size;
        await Assert.That(() => renderer.ApplyPage(update)).Throws<InvalidOperationException>();
        device.RefuseBufferSize = null;
        await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        await Assert.That(device.Buffers.Count(buffer => !buffer.Released)).IsEqualTo(live);
        ISilkGraphicsBuffer after = renderer.GpuResources.RequireSurfaceBuffer(
            renderer.Scene, mesh, RenderHeadlight.Deterministic);
        await Assert.That(after).IsSameReferenceAs(original);
        byte[] bytes = new byte[before.Length];
        after.ReadbackForTesting(bytes);
        await Assert.That(bytes.SequenceEqual(before)).IsTrue();
        renderer.ApplyPage(update);
        ISilkGraphicsBuffer replacement = renderer.GpuResources.RequireSurfaceBuffer(
            renderer.Scene, renderer.Scene.Meshes.Values.Single(), RenderHeadlight.Deterministic);
        await Assert.That(replacement).IsNotSameReferenceAs(original);
        replacement.ReadbackForTesting(bytes);
        await Assert.That(bytes.SequenceEqual(before)).IsFalse();
        await Assert.That(((TextureGraphicsBuffer)original).Released).IsTrue();
    }

    [Test]
    public async Task ParsesScalarAndTextureParameters()
    {
        byte[] command = CreateMaterialUpsert(
            "/World/Materials/Brick",
            SilkSurfaceKind.PreviewSurface,
            scalars:
            [
                (SilkMaterialParameter.Roughness, [0.25f]),
                (SilkMaterialParameter.DiffuseColor, [0.1f, 0.2f, 0.3f]),
            ],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Clamp,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [2f, 3f, 4f, 5f],
                    Bias: [0.5f, 0.25f, 0.125f, 0f],
                    Fallback: [1f, 0f, 1f, 1f],
                    Asset: "textures/brick.png",
                    UvPrimvar: "st0"),
            ]);

        string path;
        SilkSurfaceKind kind;
        int scalarCount;
        int textureCount;
        ulong hash;
        SilkMaterialParameter firstScalar;
        float roughness;
        SilkMaterialParameter secondScalar;
        float diffuseZ;
        SilkMaterialParameter textureParameter;
        SilkTextureWrap wrapS;
        SilkTextureWrap wrapT;
        SilkColorSpace colorSpace;
        int textureComponents;
        float scaleY;
        float biasZ;
        float fallbackX;
        string asset;
        string uvPrimvar;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                command, 1, SilkCommandParser.PageAbiVersion);
            if (!commands.MoveNext())
            {
                throw new InvalidDataException("Missing material command.");
            }
            SilkMaterialUpsertCommand material = commands.Current.AsMaterialUpsert();
            path = material.Path;
            kind = material.SurfaceKind;
            scalarCount = material.ScalarCount;
            textureCount = material.TextureCount;
            hash = material.StableHash;

            SilkMaterialScalarEntry first = material.GetScalar(0);
            firstScalar = first.Parameter;
            roughness = first.GetComponent(0);
            SilkMaterialScalarEntry second = material.GetScalar(1);
            secondScalar = second.Parameter;
            diffuseZ = second.GetComponent(2);

            SilkMaterialTextureEntry texture = material.GetTexture(0);
            textureParameter = texture.Parameter;
            wrapS = texture.WrapS;
            wrapT = texture.WrapT;
            colorSpace = texture.SourceColorSpace;
            textureComponents = texture.ComponentCount;
            scaleY = texture.GetScale(1);
            biasZ = texture.GetBias(2);
            fallbackX = texture.GetFallback(0);
            asset = texture.Asset;
            uvPrimvar = texture.UvPrimvar;
        }

        await Assert.That(path).IsEqualTo("/World/Materials/Brick");
        await Assert.That(kind).IsEqualTo(SilkSurfaceKind.PreviewSurface);
        await Assert.That(scalarCount).IsEqualTo(2);
        await Assert.That(textureCount).IsEqualTo(1);
        await Assert.That(hash).IsEqualTo(
            ComputeStableHash("/World/Materials/Brick"));

        // The second scalar is only reachable if the first was skipped by its own
        // declared component count, so this proves the variable-stride walk.
        await Assert.That(firstScalar).IsEqualTo(SilkMaterialParameter.Roughness);
        await Assert.That(roughness).IsEqualTo(0.25f);
        await Assert.That(secondScalar).IsEqualTo(SilkMaterialParameter.DiffuseColor);
        await Assert.That(diffuseZ).IsEqualTo(0.3f);

        await Assert.That(textureParameter).IsEqualTo(SilkMaterialParameter.DiffuseColor);
        await Assert.That(wrapS).IsEqualTo(SilkTextureWrap.Repeat);
        await Assert.That(wrapT).IsEqualTo(SilkTextureWrap.Clamp);
        await Assert.That(colorSpace).IsEqualTo(SilkColorSpace.Srgb);
        await Assert.That(textureComponents).IsEqualTo(3);
        await Assert.That(scaleY).IsEqualTo(3f);
        await Assert.That(biasZ).IsEqualTo(0.125f);
        await Assert.That(fallbackX).IsEqualTo(1f);
        await Assert.That(asset).IsEqualTo("textures/brick.png");
        await Assert.That(uvPrimvar).IsEqualTo("st0");
    }

    [Test]
    public async Task PublishesAnUnsupportedSurfaceWithEmptyTables()
    {
        byte[] command = CreateMaterialUpsert(
            "/World/Materials/Exotic",
            SilkSurfaceKind.Unsupported,
            scalars: [],
            textures: []);

        SilkSurfaceKind kind;
        int scalarCount;
        int textureCount;
        string path;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                command, 1, SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            SilkMaterialUpsertCommand material = commands.Current.AsMaterialUpsert();
            kind = material.SurfaceKind;
            scalarCount = material.ScalarCount;
            textureCount = material.TextureCount;
            path = material.Path;
        }

        // An unsupported graph must still arrive, or the consumer cannot say
        // which material it failed to shade.
        await Assert.That(kind).IsEqualTo(SilkSurfaceKind.Unsupported);
        await Assert.That(scalarCount).IsEqualTo(0);
        await Assert.That(textureCount).IsEqualTo(0);
        await Assert.That(path).IsEqualTo("/World/Materials/Exotic");
    }

    [Test]
    public async Task DistilledMdlMaterialIsShadeableAndKeepsItsProvenance()
    {
        byte[] command = CreateMaterialUpsert(
            "/World/Looks/OmniPbrMat",
            SilkSurfaceKind.MdlDistilled,
            scalars:
            [
                (SilkMaterialParameter.DiffuseColor, [0.72f, 0.28f, 0.12f]),
                (SilkMaterialParameter.Roughness, [0.35f]),
            ],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.Normal,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 1f, 1f],
                    Asset: "textures/mdl-normal.png",
                    UvPrimvar: "st"),
            ]);

        SilkMaterialData material;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                command, 1, SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            material = SilkMaterialData.CopyFrom(commands.Current.AsMaterialUpsert());
        }

        // A distilled MDL material is shaded by the same PBR path a
        // UsdPreviewSurface takes; the kind is provenance and cache identity,
        // not a request for a second shading model or a runtime shader.
        await Assert.That(material.SurfaceKind).IsEqualTo(SilkSurfaceKind.MdlDistilled);
        await Assert.That(material.IsSupported).IsTrue();
        await Assert.That(material.IsMdlUnavailable).IsFalse();
        await Assert.That(material.Scalars.Count).IsEqualTo(2);
        await Assert.That(material.Textures[0].UvPrimvar).IsEqualTo("st");
    }

    [Test]
    public async Task UndistilledMdlMaterialIsReportedByNameRatherThanShaded()
    {
        byte[] command = CreateMaterialUpsert(
            "/World/Looks/OmniPbrMat",
            SilkSurfaceKind.MdlUnavailable,
            scalars: [],
            textures: []);

        SilkMaterialData material;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                command, 1, SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            material = SilkMaterialData.CopyFrom(commands.Current.AsMaterialUpsert());
        }

        // The record must arrive with empty tables and must not be shadeable.
        // A shadeable record with nothing in it is exactly the silent default
        // grey the MDL surface kinds exist to remove.
        await Assert.That(material.SurfaceKind).IsEqualTo(SilkSurfaceKind.MdlUnavailable);
        await Assert.That(material.IsSupported).IsFalse();
        await Assert.That(material.IsMdlUnavailable).IsTrue();
        await Assert.That(material.Scalars.Count).IsEqualTo(0);
        await Assert.That(material.Textures.Count).IsEqualTo(0);
        await Assert.That(material.Path).IsEqualTo("/World/Looks/OmniPbrMat");
    }

    [Test]
    public async Task ParsesMaterialRemoval()
    {
        byte[] command = CreateMaterialRemove("/World/Materials/Brick");

        string path;
        ulong hash;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                command, 1, SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            SilkMaterialRemoveCommand removal = commands.Current.AsMaterialRemove();
            path = removal.Path;
            hash = removal.StableHash;
        }

        await Assert.That(path).IsEqualTo("/World/Materials/Brick");
        await Assert.That(hash).IsEqualTo(
            ComputeStableHash("/World/Materials/Brick"));
    }

    [Test]
    public async Task RejectsAnInconsistentParameterTable()
    {
        byte[] command = CreateMaterialUpsert(
            "/World/Materials/Brick",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [0.25f])],
            textures: []);

        // Claim a second scalar that is not there. The command must fail while
        // being constructed, not when the missing entry is first read.
        byte[] truncated = (byte[])command.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(32, 4), 2);

        await Assert.That(() =>
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                truncated, 1, SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            _ = commands.Current.AsMaterialUpsert();
        }).Throws<InvalidDataException>();

        // Trailing bytes the tables do not account for are equally rejected.
        byte[] padded = [.. command, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(
            padded.AsSpan(4, 4), (uint)padded.Length);
        await Assert.That(() =>
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                padded, 1, SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            _ = commands.Current.AsMaterialUpsert();
        }).Throws<InvalidDataException>();
    }

    [Test]
    public async Task RejectsAPageFromThePreviousAbi()
    {
        // The stale-combination failure is explicit rather than a mis-parse.
        await Assert.That(() => SilkCommandParser.Enumerate([], 0, 4))
            .Throws<InvalidDataException>();
        await Assert.That(() => SilkCommandParser.Enumerate([], 0, SilkCommandParser.PageAbiVersion - 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task PtexSourceIsRetainedButNeverDecodedAsAnOrdinaryImage()
    {
        TextureSpec ptex = new(
            SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
            SilkColorSpace.Raw, 3, [1, 1, 1, 1], [0, 0, 0, 0], [0, 0, 0, 1],
            "same.ptx", "", SourceKind: SilkTextureSourceKind.Ptex);
        TextureSpec ordinary = ptex with
        {
            Parameter = SilkMaterialParameter.EmissiveColor,
            UvPrimvar = "st1",
            SourceKind = SilkTextureSourceKind.Standard
        };
        byte[] command = CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface,
            [(SilkMaterialParameter.Roughness, [0.4f])], [ptex, ordinary]);
        SilkMaterialData material = CopyMaterial(command);

        await Assert.That(material.GetTexture(SilkMaterialParameter.DiffuseColor)!.SourceKind)
            .IsEqualTo(SilkTextureSourceKind.Ptex);
        await Assert.That(material.GetTexture(SilkMaterialParameter.EmissiveColor)!.SourceKind)
            .IsEqualTo(SilkTextureSourceKind.Standard);
        await Assert.That(material.GetPrimaryUvPrimvar()).IsEqualTo("st1");
        await Assert.That(material.GetTextureFeatures())
            .IsEqualTo(SilkShaderFeatures.BaseColorMap | SilkShaderFeatures.EmissiveMap | SilkShaderFeatures.Uv);
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 0)).IsEqualTo(0.18f);
        await Assert.That((uint)ReadSingle(constants, 128))
            .IsEqualTo((uint)(SilkShaderFeatures.BaseColorMap | SilkShaderFeatures.EmissiveMap | SilkShaderFeatures.Uv));

        using var device = new TextureGraphicsDevice();
        var gpuBudget = new SilkGpuTextureBudget(64);
        gpuBudget.ConfigureDevice(device);
        int decodes = 0;
        using var renderer = new SilkMeshRenderer(device, SilkShaderBinaryFormat.Dxil, (_, _) =>
        {
            decodes++;
            return new SilkDecodedImage(1, 1, [255, 0, 0, 255]);
        });
        using var page = new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion, 1, [.. command, .. CreateMeshUpsert("/Mesh", "/Material")], 2);
        renderer.ApplyPage(page);
        await Assert.That(decodes).IsEqualTo(1);
        _ = renderer.GpuResources.RequireSurfaceBuffer(
            renderer.Scene, renderer.Scene.Meshes.Values.Single(), RenderHeadlight.Deterministic);
        RenderDiagnostic diagnostic = renderer.GpuResources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported);
        await Assert.That(diagnostic.Message).Contains("/Material").And.Contains("DiffuseColor");
        using var commands = new TextureCommandList();
        await Assert.That(() => renderer.GpuResources.BindMaterialTexture(
            commands, renderer.Scene.Materials["/Material"], SilkMaterialParameter.DiffuseColor))
            .Throws<InvalidDataException>();
        await Assert.That(decodes).IsEqualTo(1);

        byte[] replacement = CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [],
            [ptex with { SourceKind = SilkTextureSourceKind.Standard, UvPrimvar = "st1" }, ordinary]);
        using var replaced = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, replacement, 1);
        renderer.ApplyPage(replaced);
        await Assert.That(renderer.Scene.Materials["/Material"].GetTextureFeatures())
            .IsEqualTo(SilkShaderFeatures.BaseColorMap | SilkShaderFeatures.EmissiveMap | SilkShaderFeatures.Uv);
        await Assert.That(renderer.GpuResources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsFalse();

        using var restored = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3, command, 1);
        renderer.ApplyPage(restored);
        _ = renderer.GpuResources.RequireSurfaceBuffer(
            renderer.Scene, renderer.Scene.Meshes.Values.Single(), RenderHeadlight.Deterministic);
        await Assert.That(renderer.Scene.Materials["/Material"].GetTextureFeatures())
            .IsEqualTo(SilkShaderFeatures.BaseColorMap | SilkShaderFeatures.EmissiveMap | SilkShaderFeatures.Uv);
        await Assert.That(renderer.GpuResources.Diagnostics.Entries.Count(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsEqualTo(1);
    }

    [Test]
    public async Task PtexSourceRefusesMalformedOrAmbiguousWireEntries()
    {
        TextureSpec ptex = new(
            SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
            SilkColorSpace.Raw, 3, [1, 1, 1, 1], [0, 0, 0, 0], [0, 0, 0, 1],
            "face.png", "", SourceKind: SilkTextureSourceKind.Ptex);
        TextureSpec composite = ptex with
        {
            SourceKind = SilkTextureSourceKind.Standard,
            UvPrimvar = "st",
            CompositeOperator = SilkCompositeOperator.Multiply
        };
        byte[][] malformed =
        [
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [],
                [ptex with { SourceKind = (SilkTextureSourceKind)2 }]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.MaterialXProjected, [], [ptex]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [], [ptex with { UvPrimvar = "st" }]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [],
                [ptex with { CompositeOperator = SilkCompositeOperator.Multiply }]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [],
                [ptex with { Parameter = SilkMaterialParameter.Displacement }]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface,
                [(SilkMaterialParameter.DiffuseColor, [0.2f, 0.3f, 0.4f])], [ptex]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [], [ptex, composite]),
            CreateMaterialUpsert("/Material", SilkSurfaceKind.PreviewSurface, [], [composite, ptex]),
        ];
        foreach (byte[] bytes in malformed)
        {
            await Assert.That(() => CopyMaterial(bytes)).Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task RetainsAndRemovesMaterialsInSceneState()
    {
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Brick",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [0.4f])],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/brick.png",
                    UvPrimvar: "st"),
            ]);

        SilkSceneState state = new();

        // Before this landed, Apply threw on any command it did not know, so a
        // real stage with a bound material would have failed the whole page.
        _ = state.Apply(upsert, 1, 1);

        await Assert.That(state.Materials.Count).IsEqualTo(1);
        SilkMaterialData material = state.Materials["/World/Materials/Brick"];
        await Assert.That(material.IsSupported).IsTrue();
        await Assert.That(material.GetScalar(SilkMaterialParameter.Roughness)[0])
            .IsEqualTo(0.4f);
        await Assert.That(material.GetScalar(SilkMaterialParameter.Metallic).Length)
            .IsEqualTo(0);
        SilkMaterialTexture? diffuse =
            material.GetTexture(SilkMaterialParameter.DiffuseColor);
        await Assert.That(diffuse).IsNotNull();
        await Assert.That(diffuse!.Asset).IsEqualTo("textures/brick.png");
        await Assert.That(diffuse.UvPrimvar).IsEqualTo("st");
        await Assert.That(material.GetTexture(SilkMaterialParameter.Roughness))
            .IsNull();

        byte[] remove = CreateMaterialRemove("/World/Materials/Brick");
        _ = state.Apply(remove, 1, 2);
        await Assert.That(state.Materials.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RetainsAnUnsupportedMaterialSoItCanBeDiagnosed()
    {
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Exotic",
            SilkSurfaceKind.Unsupported,
            scalars: [],
            textures: []);

        SilkSceneState state = new();
        _ = state.Apply(upsert, 1, 1);

        SilkMaterialData material = state.Materials["/World/Materials/Exotic"];
        await Assert.That(material.IsSupported).IsFalse();
        await Assert.That(material.Scalars.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RetainsGeneratedMaterialXFragmentShader()
    {
        byte[] spirv = [0x03, 0x02, 0x23, 0x07, 1, 0, 0, 0];
        byte[] msl = Encoding.UTF8.GetBytes("fragment float4 main() { return float4(1); }");
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Generated",
            SilkSurfaceKind.MaterialXGenerated,
            scalars: [],
            textures: [],
            generatedFragmentSpirV: spirv,
            generatedFragmentMslSource: msl);

        SilkSceneState state = new();
        _ = state.Apply(upsert, 1, 1);

        SilkMaterialData material = state.Materials["/World/Materials/Generated"];
        await Assert.That(material.IsSupported).IsTrue();
        await Assert.That(material.GeneratedFragmentSpirV.ToArray()).IsEquivalentTo(spirv);
        await Assert.That(material.GeneratedFragmentMslSource.ToArray()).IsEquivalentTo(msl);
    }

    [Test]
    public async Task RetainsTheTwoImageCompositeOperandAndItsOperator()
    {
        // A base colour driven by two images publishes two entries for one
        // parameter: the primary and the composite operand the shader combines
        // with it per pixel.
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Composited",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/first.png",
                    UvPrimvar: "st"),
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [0.5f, 0.5f, 0.5f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/second.png",
                    UvPrimvar: "st",
                    CompositeOperator: SilkCompositeOperator.Mix,
                    CompositeFactor: 0.25f),
            ]);

        SilkSceneState state = new();
        _ = state.Apply(upsert, 1, 1);
        SilkMaterialData material = state.Materials["/World/Materials/Composited"];

        await Assert.That(material.Textures.Count).IsEqualTo(2);

        // GetTexture must return the primary, not whichever entry it reaches
        // first: the composite operand is the second half of one expression, and
        // binding it into the primary slot would sample the wrong image.
        SilkMaterialTexture primary =
            material.GetTexture(SilkMaterialParameter.DiffuseColor)!;
        await Assert.That(primary.Asset).IsEqualTo("textures/first.png");
        await Assert.That(primary.CompositeOperator)
            .IsEqualTo(SilkCompositeOperator.None);

        SilkMaterialTexture composite = material.GetCompositeTexture()!;
        await Assert.That(composite.Asset).IsEqualTo("textures/second.png");
        await Assert.That(composite.CompositeOperator)
            .IsEqualTo(SilkCompositeOperator.Mix);
        await Assert.That(composite.CompositeFactor).IsEqualTo(0.25f);
        await Assert.That(composite.Parameter)
            .IsEqualTo(SilkMaterialParameter.DiffuseColor);

        // Each operand keeps its own folded affine, which is the whole reason two
        // entries exist rather than one entry and a constant.
        await Assert.That(primary.Scale[0]).IsEqualTo(1f);
        await Assert.That(composite.Scale[0]).IsEqualTo(0.5f);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        // The target is written as the same texture-mask bit the slot uses, so the
        // shader compares one value instead of mapping a second identifier space.
        await Assert.That(ReadSingle(constants, 176))
            .IsEqualTo((float)(uint)SilkShaderFeatures.BaseColorMap);
        await Assert.That(ReadSingle(constants, 180))
            .IsEqualTo((float)(uint)SilkCompositeOperator.Mix);
        await Assert.That(ReadSingle(constants, 184)).IsEqualTo(0.25f);
    }

    [Test]
    public async Task CompositeOperandCarriesItsOwnUdimBitRatherThanTheDrivenSlots()
    {
        // The two operands of one surface input are independent assets, and either
        // may be a UDIM set on its own. Reusing the driven slot's UDIM bit sampled
        // a plain image through the atlas path -- which reads its first texel as
        // tile metadata and returns one flat colour -- or sampled an atlas with raw
        // coordinates. The primary here is plain and the composite is a UDIM set,
        // which is exactly the case a shared bit gets wrong.
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/UdimComposite",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/plain.png",
                    UvPrimvar: "st"),
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/tiles.<UDIM>.png",
                    UvPrimvar: "st",
                    CompositeOperator: SilkCompositeOperator.Multiply),
            ]);

        SilkMaterialData material = CopyMaterial(upsert);
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        // The slot's own UDIM mask stays clear, because the primary is not a UDIM
        // set, and the composite's bit is carried separately in compositeControls.w.
        float udimMask = ReadSingle(constants, 132);
        await Assert.That((uint)udimMask & (uint)SilkShaderFeatures.BaseColorMap).IsEqualTo(0u);
        await Assert.That(ReadSingle(constants, 188)).IsEqualTo(1f);
    }

    [Test]
    public async Task ConstantBorderMasksCarryEachSlotAxisAndTheCompositeOperand()
    {
        static TextureSpec spec(
            SilkMaterialParameter parameter,
            SilkTextureWrap wrapS,
            SilkTextureWrap wrapT,
            string asset,
            SilkCompositeOperator composite = SilkCompositeOperator.None) =>
            new(parameter, wrapS, wrapT, SilkColorSpace.Raw, parameter == SilkMaterialParameter.DiffuseColor ? 3 : 1,
                [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [0.5f, 0.5f, 0.5f, 1f], asset, "st",
                CompositeOperator: composite);
        TextureSpec[] textures =
        [
            spec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Constant, SilkTextureWrap.Repeat, "base.png"),
            spec(SilkMaterialParameter.Roughness, SilkTextureWrap.Repeat, SilkTextureWrap.Constant, "rough.png"),
            spec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Constant, SilkTextureWrap.Constant,
                "operand.png", SilkCompositeOperator.Multiply),
            // A UDIM set is an atlas, so a ring around the whole atlas would not
            // border any one tile; its constant wrap must not claim a mask bit.
            spec(SilkMaterialParameter.Opacity, SilkTextureWrap.Constant, SilkTextureWrap.Constant,
                "tiles.<UDIM>.png"),
        ];
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Bordered", SilkSurfaceKind.MaterialXProjected, scalars: [], textures));
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        uint baseColor = (uint)SilkShaderFeatures.BaseColorMap;
        uint roughness = (uint)SilkShaderFeatures.RoughnessMetallicMap;
        uint composite = (uint)SilkShaderFeatures.Uv;
        await Assert.That(ReadSingle(constants, 196)).IsEqualTo((float)(baseColor | composite));
        await Assert.That(ReadSingle(constants, 200)).IsEqualTo((float)(roughness | composite));
        await Assert.That(ReadSingle(constants, 204)).IsEqualTo(0f);

        // A volume draw never samples these slots through the surface path, so
        // its masks stay clear even when the material authors constant wraps.
        SilkMaterialData volume = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Volume", SilkSurfaceKind.VolumeDensity, scalars: [], [textures[0]]));
        byte[] volumeConstants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(volume, RenderHeadlight.Deterministic, volumeConstants);
        await Assert.That(ReadSingle(volumeConstants, 196)).IsEqualTo(0f);
        await Assert.That(ReadSingle(volumeConstants, 200)).IsEqualTo(0f);
    }

    [Test]
    [Arguments(SilkTextureWrap.Constant, SilkTextureWrap.Constant)]
    [Arguments(SilkTextureWrap.Constant, SilkTextureWrap.Clamp)]
    [Arguments(SilkTextureWrap.Repeat, SilkTextureWrap.Constant)]
    public async Task ConstantWrapSurroundsTheImageWithARingOfTheAuthoredDefault(
        SilkTextureWrap wrapS, SilkTextureWrap wrapT)
    {
        byte[] source = [10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255];
        byte[] reference = UploadBorderedImage(SilkTextureWrap.Clamp, SilkTextureWrap.Clamp, source, out _, out _);
        byte[] padded = UploadBorderedImage(wrapS, wrapT, source, out uint width, out uint height);
        bool padS = wrapS == SilkTextureWrap.Constant;
        bool padT = wrapT == SilkTextureWrap.Constant;
        await Assert.That(width).IsEqualTo(padS ? 4u : 2u);
        await Assert.That(height).IsEqualTo(padT ? 4u : 2u);
        byte[] ring = [255, 0, 255, 255];
        int inside = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceX = x - (padS ? 1 : 0);
                int sourceY = y - (padT ? 1 : 0);
                byte[] actual = padded.AsSpan(((y * (int)width) + x) * 4, 4).ToArray();
                if (sourceX is >= 0 and < 2 && sourceY is >= 0 and < 2)
                {
                    inside++;
                    await Assert.That(actual)
                        .IsEquivalentTo(reference.AsSpan(((sourceY * 2) + sourceX) * 4, 4).ToArray());
                }
                else
                {
                    await Assert.That(actual).IsEquivalentTo(ring);
                }
            }
        }
        // Non-vacuity: every authored texel is present exactly once.
        await Assert.That(inside).IsEqualTo(4);
    }

    [Test]
    public async Task AFailedConstantBorderTextureStillCarriesItsRing()
    {
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new FileNotFoundException("missing"));
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Constant, SilkTextureWrap.Constant,
                SilkColorSpace.Raw, 3, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "missing.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // The fallback texel is surrounded by its own default, so a constant axis
        // reads the default on both sides of the unit range.
        SilkTextureDescriptor descriptor = device.CreatedTextures.Single();
        await Assert.That(descriptor.Width).IsEqualTo(3u);
        await Assert.That(descriptor.Height).IsEqualTo(3u);
        byte[] pixels = commands.Uploads[0];
        for (int pixel = 0; pixel < 9; pixel++)
        {
            await Assert.That(pixels.AsSpan(pixel * 4, 4).ToArray()).IsEquivalentTo(new byte[] { 255, 0, 255, 255 });
        }
    }

    [Test]
    public async Task AConstantBorderPastTheDeviceLimitFallsBackToTheDefault()
    {
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 3 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => new SilkDecodedImage(2, 2, new byte[16]));
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Constant, SilkTextureWrap.Constant,
                SilkColorSpace.Raw, 3, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "large.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // A 2x2 image would need 4x4 with its ring, past the limit of 3, so the
        // texture reports a decode failure and binds the bordered default.
        SilkTextureDescriptor descriptor = device.CreatedTextures.Single();
        await Assert.That(descriptor.Width).IsEqualTo(3u);
        await Assert.That(descriptor.Height).IsEqualTo(3u);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureFallbackUsed);
    }

    [Test]
    [Arguments(SilkTextureWrap.Constant, SilkSamplerAddressMode.ClampToEdge)]
    [Arguments(SilkTextureWrap.Black, SilkSamplerAddressMode.ClampToBorder)]
    public async Task ConstantWrapClampsInsideItsRingWhileBlackKeepsTheTransparentBorder(
        SilkTextureWrap wrap, SilkSamplerAddressMode expected)
    {
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => new SilkDecodedImage(1, 1, [1, 2, 3, 255]));
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, wrap, wrap,
                SilkColorSpace.Raw, 3, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "image.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(expected);
        await Assert.That(sampler.AddressV).IsEqualTo(expected);
        // Black never grows the image: its border is the sampler's, not a ring.
        await Assert.That(device.CreatedTextures.Single().Width)
            .IsEqualTo(wrap == SilkTextureWrap.Constant ? 3u : 1u);
    }

    private static byte[] UploadBorderedImage(
        SilkTextureWrap wrapS, SilkTextureWrap wrapT, byte[] source, out uint width, out uint height)
    {
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => new SilkDecodedImage(2, 2, source.ToArray()));
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, [],
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, wrapS, wrapT,
                SilkColorSpace.Raw, 3, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "image.png", "st")]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        SilkTextureDescriptor descriptor = device.CreatedTextures.Single();
        width = descriptor.Width;
        height = descriptor.Height;
        return commands.Uploads[0];
    }

    [Test]
    public async Task UncompositedMaterialsTargetNoTextureSlot()
    {
        // Zero matches no slot bit, so the shader's composite hook is inert for
        // every material that binds no second image. Without this the hook would
        // have to be permutation-gated, which is exactly what the single
        // universal slot exists to avoid.
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Plain",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [0.25f])],
            textures: []));

        await Assert.That(material.GetCompositeTexture()).IsNull();

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 176)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 180)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 184)).IsEqualTo(0f);
    }

    [Test]
    public async Task RejectsAMaterialUpsertWithATruncatedCompositeOperand()
    {
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Truncated",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/first.png",
                    UvPrimvar: "st"),
            ]);
        // Removing the trailing composite floats of the one texture entry leaves a
        // page a stale native library could plausibly produce.
        byte[] truncated = upsert[..^sizeof(float)];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4), (uint)truncated.Length);

        SilkSceneState state = new();
        await Assert.That(() => state.Apply(truncated, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task RetainsTheFoldedMaterialXUvTransform()
    {
        // A place2d that halves the scale and offsets by a quarter, folded by
        // hdSilk into the affine the shader applies once per fragment.
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Placed",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/placed.png",
                    UvPrimvar: "st"),
            ],
            uvTransform: PlacedUvTransform);

        SilkSceneState state = new();
        _ = state.Apply(upsert, 1, 1);

        SilkMaterialData material = state.Materials["/World/Materials/Placed"];
        await Assert.That(material.UvTransform.ToArray())
            .IsEquivalentTo(PlacedUvTransform);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        // Row 0 is (m00, m01, tx, 0) and row 1 is (m10, m11, ty, 0), which is the
        // packing the mesh fragment shader reads before any texture sample.
        await Assert.That(ReadSingle(constants, 144)).IsEqualTo(2f);
        await Assert.That(ReadSingle(constants, 148)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 152)).IsEqualTo(-0.25f);
        await Assert.That(ReadSingle(constants, 160)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 164)).IsEqualTo(2f);
        await Assert.That(ReadSingle(constants, 168)).IsEqualTo(0.5f);
    }

    [Test]
    public async Task WritesTheIdentityUvTransformForAnUntransformedMaterial()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Plain",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [0.25f])],
            textures: []));

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        await Assert.That(material.UvTransform.ToArray())
            .IsEquivalentTo(IdentityUvTransform);
        await Assert.That(ReadSingle(constants, 144)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, 148)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 152)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 160)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 164)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, 168)).IsEqualTo(0f);
    }

    [Test]
    public async Task UnshadedMaterialsKeepTheIdentityUvTransform()
    {
        // The shared default block a mesh with no supported material bound uses
        // must not rotate or scale texture coordinates that nothing authored.
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(null, RenderHeadlight.Deterministic, constants);

        await Assert.That(ReadSingle(constants, 144)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, 164)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, 152)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 168)).IsEqualTo(0f);
    }

    [Test]
    public async Task RejectsAMaterialUpsertWithATruncatedUvTransform()
    {
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Truncated",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures: [],
            omitAlternateUv: true);
        byte[] truncated = upsert[..^sizeof(float)];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4), (uint)truncated.Length);

        SilkSceneState state = new();
        await Assert.That(() => state.Apply(truncated, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(sizeof(uint))]
    [Arguments(sizeof(uint) + (5 * sizeof(float)))]
    public async Task RejectsAMaterialUpsertWithATruncatedAlternateUvTail(int keptTailBytes)
    {
        // A page from an ABI 29 library ends after the primary affine; any cut
        // inside the mask or the alternate affine must not parse as a material.
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Truncated",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures: []);
        const int tailBytes = sizeof(uint) + (6 * sizeof(float));
        byte[] truncated = upsert[..^(tailBytes - keptTailBytes)];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4), (uint)truncated.Length);

        SilkSceneState state = new();
        await Assert.That(() => state.Apply(truncated, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task RetainsTheAlternateUvTransformForTheMaskedTexture()
    {
        // Base color samples through the primary place2d and roughness through a
        // second one, which is what hdSilk publishes for two differing place2d nodes.
        float[] alternate = [0.5f, 0f, 0f, 0.5f, 0.25f, 0.75f];
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/TwoPlacements",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                AlternateUvTexture(SilkMaterialParameter.DiffuseColor, "textures/base.png", 3),
                AlternateUvTexture(SilkMaterialParameter.Roughness, "textures/rough.png", 1),
            ],
            uvTransform: PlacedUvTransform,
            alternateUvMask: 1u << (int)SilkMaterialParameter.Roughness,
            alternateUvTransform: alternate));

        await Assert.That(material.AlternateUvParameterMask)
            .IsEqualTo(1u << (int)SilkMaterialParameter.Roughness);
        await Assert.That(material.AlternateUvTransform.ToArray()).IsEquivalentTo(alternate);
        await Assert.That(material.GetUvTransformFor(SilkMaterialParameter.Roughness).ToArray())
            .IsEquivalentTo(alternate);
        await Assert.That(material.GetUvTransformFor(SilkMaterialParameter.DiffuseColor).ToArray())
            .IsEquivalentTo(PlacedUvTransform);
        await Assert.That(material.GetAlternateUvTextureMask())
            .IsEqualTo(SilkShaderFeatures.RoughnessMetallicMap);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        await Assert.That(ReadSingle(constants, 204))
            .IsEqualTo((float)(uint)SilkShaderFeatures.RoughnessMetallicMap);
        await Assert.That(ReadSingle(constants, 144)).IsEqualTo(2f);
        await Assert.That(ReadSingle(constants, 152)).IsEqualTo(-0.25f);
        await Assert.That(ReadSingle(constants, 272)).IsEqualTo(0.5f);
        await Assert.That(ReadSingle(constants, 276)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 280)).IsEqualTo(0.25f);
        await Assert.That(ReadSingle(constants, 284)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 288)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 292)).IsEqualTo(0.5f);
        await Assert.That(ReadSingle(constants, 296)).IsEqualTo(0.75f);
        await Assert.That(ReadSingle(constants, 300)).IsEqualTo(0f);
    }

    [Test]
    public async Task WritesTheIdentityAlternateUvTransformWithoutAMaskedTexture()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/OnePlacement",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures: [AlternateUvTexture(SilkMaterialParameter.DiffuseColor, "textures/base.png", 3)],
            uvTransform: PlacedUvTransform));

        await Assert.That(material.AlternateUvParameterMask).IsEqualTo(0u);
        await Assert.That(material.GetAlternateUvTextureMask()).IsEqualTo(SilkShaderFeatures.None);
        foreach (SilkMaterialData? candidate in new[] { material, null })
        {
            byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
            SilkSurfaceUniformWriter.Write(candidate, RenderHeadlight.Deterministic, constants);
            await Assert.That(ReadSingle(constants, 204)).IsEqualTo(0f);
            await Assert.That(ReadSingle(constants, 272)).IsEqualTo(1f);
            await Assert.That(ReadSingle(constants, 276)).IsEqualTo(0f);
            await Assert.That(ReadSingle(constants, 280)).IsEqualTo(0f);
            await Assert.That(ReadSingle(constants, 288)).IsEqualTo(0f);
            await Assert.That(ReadSingle(constants, 292)).IsEqualTo(1f);
            await Assert.That(ReadSingle(constants, 296)).IsEqualTo(0f);
        }
    }

    [Test]
    [Arguments(SilkMaterialParameter.Normal, SilkMaterialParameter.DiffuseColor)]
    [Arguments(SilkMaterialParameter.DetailNormal, SilkMaterialParameter.DetailNormal)]
    [Arguments(SilkMaterialParameter.Displacement, SilkMaterialParameter.Displacement)]
    public async Task RejectsAnAlternateUvMaskThatNamesNoMaskableTexture(
        SilkMaterialParameter masked,
        SilkMaterialParameter authored)
    {
        // The detail normal has its own affine and displacement samples in the
        // vertex stage through the primary one, so neither can be masked; a bit
        // with no texture behind it is a forged page.
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Forged",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures: [AlternateUvTexture(authored, "textures/authored.png", 3)],
            alternateUvMask: 1u << (int)masked,
            alternateUvTransform: [0.5f, 0f, 0f, 0.5f, 0f, 0f]);

        SilkSceneState state = new();
        await Assert.That(() => state.Apply(upsert, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task TheRuntimeShaderKeyTracksTheAlternateUvTransform()
    {
        static byte[] key(uint mask, float[] alternate) => CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Keyed",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                AlternateUvTexture(SilkMaterialParameter.DiffuseColor, "textures/base.png", 3),
                AlternateUvTexture(SilkMaterialParameter.Roughness, "textures/rough.png", 1),
            ],
            alternateUvMask: mask,
            alternateUvTransform: alternate)).CreateRuntimeShaderKeyBytes();

        uint roughness = 1u << (int)SilkMaterialParameter.Roughness;
        byte[] baseline = key(roughness, [0.5f, 0f, 0f, 0.5f, 0f, 0f]);
        await Assert.That(key(roughness, [0.5f, 0f, 0f, 0.5f, 0f, 0f]).SequenceEqual(baseline)).IsTrue();
        await Assert.That(key(roughness, [0.5f, 0f, 0f, 0.5f, 0.1f, 0f]).SequenceEqual(baseline)).IsFalse();
        await Assert.That(key(0, [0.5f, 0f, 0f, 0.5f, 0f, 0f]).SequenceEqual(baseline)).IsFalse();
    }

    private static TextureSpec AlternateUvTexture(
        SilkMaterialParameter parameter,
        string asset,
        int components) =>
        new(
            parameter,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: components,
            Scale: [1f, 1f, 1f, 1f],
            Bias: [0f, 0f, 0f, 0f],
            Fallback: [0f, 0f, 0f, 1f],
            Asset: asset,
            UvPrimvar: "st");

    [Test]
    public async Task RejectsAMaterialWhoseHashDoesNotMatchItsPath()
    {
        byte[] upsert = CreateMaterialUpsert(
            "/World/Materials/Brick",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [0.4f])],
            textures: []);
        // Corrupt the index hash. It is only an index, but a mismatch means the
        // page is inconsistent rather than merely colliding.
        BinaryPrimitives.WriteUInt64LittleEndian(upsert.AsSpan(8, 8), 1234);

        SilkSceneState state = new();
        await Assert.That(() => state.Apply(upsert, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task MissingTextureFallbackIsDiagnosedAndExplicitlyRetryable()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Missing",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [1f, 0f, 1f, 1f],
                    Asset: "missing.png",
                    UvPrimvar: "st"),
            ]));
        int attempts = 0;
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new FileNotFoundException("Texture is absent.", "missing.png");
                }
                return new SilkDecodedImage(1, 1, [10, 20, 30, 255]);
            });
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.DiffuseColor);
        resources.UploadMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.DiffuseColor);

        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureAssetNotFound);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureFallbackUsed);

        resources.RetryFailedTextures();
        resources.UploadMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.DiffuseColor);

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(resources.Diagnostics.Entries).IsEmpty();
        await Assert.That(commands.UploadCount).IsEqualTo(2);
    }

    [Test]
    [Arguments((int)SilkImageAddressObservation.ClampToEdge, SilkSamplerAddressMode.ClampToEdge)]
    [Arguments((int)SilkImageAddressObservation.Repeat, SilkSamplerAddressMode.Repeat)]
    [Arguments((int)SilkImageAddressObservation.MirrorRepeat, SilkSamplerAddressMode.MirrorRepeat)]
    [Arguments((int)SilkImageAddressObservation.ClampToBorder, SilkSamplerAddressMode.ClampToBorder)]
    public async Task UseMetadataUsesObservedImageWrap(
        int imageWrapValue,
        SilkSamplerAddressMode expectedAddress)
    {
        SilkImageAddressObservation imageWrap = (SilkImageAddressObservation)imageWrapValue;
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/MetadataWrap",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.UseMetadata,
                    SilkTextureWrap.UseMetadata,
                    SilkColorSpace.Raw,
                    ComponentCount: 4,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "metadata-wrap.png",
                    UvPrimvar: "st"),
            ]));
        using var device = new TextureGraphicsDevice();
        int descriptionCalls = 0;
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]),
            imageDescriber: _ =>
            {
                descriptionCalls++;
                return new SilkImageDescription(
                    1,
                    1,
                    SilkTextureFormat.Rgba8Unorm,
                    Observed: SilkImageObservation.Queried |
                        SilkImageObservation.AddressU |
                        SilkImageObservation.AddressV,
                    AddressU: imageWrap,
                    AddressV: imageWrap);
            });
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(expectedAddress);
        await Assert.That(sampler.AddressV).IsEqualTo(expectedAddress);
        await Assert.That(descriptionCalls).IsEqualTo(1);
    }

    [Test]
    public async Task UseMetadataResolvesEachAxisIndependently()
    {
        SilkMaterialData material = CreateUseMetadataMaterial();
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]),
            imageDescriber: _ => new SilkImageDescription(
                1,
                1,
                SilkTextureFormat.Rgba8Unorm,
                Observed: SilkImageObservation.Queried |
                    SilkImageObservation.AddressU |
                    SilkImageObservation.AddressV,
                AddressU: SilkImageAddressObservation.Repeat,
                AddressV: SilkImageAddressObservation.ClampToEdge));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(SilkSamplerAddressMode.Repeat);
        await Assert.That(sampler.AddressV).IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedTextureDoesNotResolveWrapMetadataUntilRecovery(bool streaming)
    {
        SilkMaterialData material = CreateUseMetadataMaterial();
        using var device = new TextureGraphicsDevice { SupportsRowUploads = streaming };
        int decodeCalls = 0;
        int descriptionCalls = 0;
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) =>
            {
                decodeCalls++;
                if (decodeCalls == 1)
                {
                    throw new FileNotFoundException("Texture is absent.", asset);
                }
                return new SilkDecodedImage(1, 1, [255, 255, 255, 255]);
            },
            residencyOptions: new SilkTextureResidencyOptions(4096, 4096, streaming),
            imageDescriber: _ =>
            {
                descriptionCalls++;
                return new SilkImageDescription(
                    1,
                    1,
                    SilkTextureFormat.Rgba8Unorm,
                    Observed: SilkImageObservation.Queried |
                        SilkImageObservation.AddressU |
                        SilkImageObservation.AddressV,
                    AddressU: SilkImageAddressObservation.Repeat,
                    AddressV: SilkImageAddressObservation.MirrorRepeat);
            });
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(decodeCalls).IsEqualTo(1);
        await Assert.That(descriptionCalls).IsEqualTo(0);
        await Assert.That(device.CreatedSamplers.Single().AddressU)
            .IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(device.CreatedSamplers.Single().AddressV)
            .IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(resources.Diagnostics.Entries.Count(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureAssetNotFound)).IsEqualTo(1);
        await Assert.That(resources.Diagnostics.Entries.Count(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsEqualTo(1);

        resources.RetryFailedTextures();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(decodeCalls).IsEqualTo(2);
        await Assert.That(descriptionCalls).IsEqualTo(1);
        await Assert.That(device.CreatedSamplers.Last().AddressU)
            .IsEqualTo(SilkSamplerAddressMode.Repeat);
        await Assert.That(device.CreatedSamplers.Last().AddressV)
            .IsEqualTo(SilkSamplerAddressMode.MirrorRepeat);
        await Assert.That(resources.Diagnostics.Entries.Any(entry =>
            entry.Code is SilkRenderDiagnosticCodes.TextureAssetNotFound or
                SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsFalse();
    }

    [Test]
    public async Task UseMetadataWithoutImageWrapMetadataUsesBlackBorder()
    {
        SilkMaterialData material = CreateUseMetadataMaterial();
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]),
            imageDescriber: _ => new SilkImageDescription(
                1,
                1,
                SilkTextureFormat.Rgba8Unorm,
                Observed: SilkImageObservation.Queried));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(sampler.AddressV).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(resources.Diagnostics.Entries
            .Any(entry => entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsFalse();
    }

    [Test]
    public async Task UseMetadataWithoutImageDescriberUsesBlackBorderAndReportsFallback()
    {
        SilkMaterialData material = CreateUseMetadataMaterial();
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(sampler.AddressV).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(resources.Diagnostics.Entries.Any(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed &&
            entry.Message.Contains("image library did not provide metadata", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task UseMetadataWhenImageDescriptionFailsUsesBlackBorderAndReportsFallback()
    {
        SilkMaterialData material = CreateUseMetadataMaterial();
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]),
            imageDescriber: _ => throw new InvalidDataException("Image metadata is unreadable."));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(sampler.AddressV).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(resources.Diagnostics.Entries.Any(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed &&
            entry.Message.Contains("Image metadata is unreadable.", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task UseMetadataUnsupportedAxisFallsBackWithoutChangingOtherAxis()
    {
        SilkMaterialData material = CreateUseMetadataMaterial();
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]),
            imageDescriber: _ => new SilkImageDescription(
                1,
                1,
                SilkTextureFormat.Rgba8Unorm,
                Observed: SilkImageObservation.Queried |
                    SilkImageObservation.AddressU |
                    SilkImageObservation.AddressV,
                AddressU: SilkImageAddressObservation.MirrorClampToEdge,
                AddressV: SilkImageAddressObservation.Repeat));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        SilkSamplerDescriptor sampler = device.CreatedSamplers.Single();
        await Assert.That(sampler.AddressU).IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
        await Assert.That(sampler.AddressV).IsEqualTo(SilkSamplerAddressMode.Repeat);
        await Assert.That(resources.Diagnostics.Entries.Any(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed &&
            entry.Message.Contains("addressing on U", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ChangedLocalTextureIsDecodedAndUploadedAgain()
    {
        string asset = Path.GetTempFileName();
        try
        {
            SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
                "/World/Materials/Reload",
                SilkSurfaceKind.PreviewSurface,
                scalars: [],
                textures:
                [
                    new TextureSpec(
                        SilkMaterialParameter.DiffuseColor,
                        SilkTextureWrap.Repeat,
                        SilkTextureWrap.Repeat,
                        SilkColorSpace.Raw,
                        ComponentCount: 4,
                        Scale: [1f, 1f, 1f, 1f],
                        Bias: [0f, 0f, 0f, 0f],
                        Fallback: [1f, 0f, 1f, 1f],
                        Asset: asset,
                        UvPrimvar: "st"),
                ]));
            int attempts = 0;
            using var device = new TextureGraphicsDevice();
            using var resources = new SilkSceneGpuResources(
                device,
                (_, _) =>
                {
                    attempts++;
                    return new SilkDecodedImage(
                        1,
                        1,
                        [checked((byte)attempts), 0, 0, 255]);
                });
            using var commands = new TextureCommandList();

            resources.UploadMaterialTexture(
                commands,
                material,
                SilkMaterialParameter.DiffuseColor);
            await File.WriteAllTextAsync(asset, "changed-size");
            File.SetLastWriteTimeUtc(asset, DateTime.UtcNow.AddMinutes(1));
            resources.UploadMaterialTexture(
                commands,
                material,
                SilkMaterialParameter.DiffuseColor);

            await Assert.That(attempts).IsEqualTo(2);
            await Assert.That(commands.Uploads.Select(upload => upload[0]))
                .IsEquivalentTo(new byte[] { 1, 2 });
        }
        finally
        {
            File.Delete(asset);
        }
    }

    [Test]
    public async Task CorruptTextureProducesDecodeDiagnostic()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Corrupt",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "corrupt.png",
                    UvPrimvar: "st"),
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => throw new InvalidDataException("Invalid PNG stream."));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.DiffuseColor);

        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureDecodeFailed);
    }

    [Test]
    public async Task MaterialTextureSlotsBindIndependentSamplerState()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/MixedWrap",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Clamp,
                    SilkColorSpace.Srgb,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 1f, 1f, 1f],
                    "base.png",
                    "st"),
                new TextureSpec(
                    SilkMaterialParameter.Normal,
                    SilkTextureWrap.Mirror,
                    SilkTextureWrap.Black,
                    SilkColorSpace.Raw,
                    3,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [0.5f, 0.5f, 1f, 1f],
                    "normal.png",
                    "st"),
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.DiffuseColor);
        resources.BindMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.Normal);

        await Assert.That(commands.SamplerBindings.Select(binding => binding.Binding))
            .IsEquivalentTo(new uint[] { 1, 10 });
        await Assert.That(commands.TextureBindings)
            .IsEquivalentTo(new uint[] { 2, 3, 37, 39 });
        await Assert.That(commands.SamplerBindings[0].Descriptor.AddressU)
            .IsEqualTo(SilkSamplerAddressMode.Repeat);
        await Assert.That(commands.SamplerBindings[0].Descriptor.AddressV)
            .IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
        await Assert.That(commands.SamplerBindings[1].Descriptor.AddressU)
            .IsEqualTo(SilkSamplerAddressMode.MirrorRepeat);
        await Assert.That(commands.SamplerBindings[1].Descriptor.AddressV)
            .IsEqualTo(SilkSamplerAddressMode.ClampToBorder);
    }

    [Test]
    [Arguments(103ul, false)]
    [Arguments(104ul, true)]
    public async Task CpuUdimPreparationCountsLiveTilesAndAtlasUntilOwnershipTransfers(ulong maximum, bool admitted)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(maximum);
        cpu.ConfigureDevice(device);
        static SilkDecodedImage decode(string _, bool __, SilkCpuTextureBudget budget)
        {
            IDisposable reservation = budget.Reserve(4);
            var image = new SilkDecodedImage(1, 1, [255, 0, 0, 255]);
            image.OwnReservation(reservation);
            return image;
        }
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded UDIM decoding is forbidden."),
            _ => [new SilkUdimTile(1001, "1001.png"), new SilkUdimTile(1002, "1002.png")],
            cpuTextureBudget: cpu, ownedImageDecoder: decode);
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "<UDIM>.png", "st"),
            ]));
        using var commands = new TextureCommandList();
        if (admitted)
        {
            resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(96ul);
            await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(104ul);
            await Assert.That(cpu.Usage.ReservationCount).IsEqualTo(1ul);
            resources.Dispose();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        else
        {
            await Assert.That(() => resources.BindMaterialTexture(
                commands, material, SilkMaterialParameter.DiffuseColor))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(cpu.Usage.ReservationCount).IsEqualTo(0ul);
            await Assert.That(device.TextureOwners).IsEmpty();
        }
    }

    [Test]
    [Arguments(35ul, false)]
    [Arguments(36ul, true)]
    public async Task CpuPreparationCountsBasePixelsAndMipChainOverlap(ulong maximum, bool admitted)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(maximum);
        cpu.ConfigureDevice(device);
        static SilkDecodedImage decode(string _, bool __, SilkCpuTextureBudget budget)
        {
            IDisposable reservation = budget.Reserve(16);
            var image = new SilkDecodedImage(2, 2,
                [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255]);
            image.OwnReservation(reservation);
            return image;
        }
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("The unbounded decoder must not run."),
            cpuTextureBudget: cpu, ownedImageDecoder: decode);
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "pixels.png", "st"),
            ]));
        using var commands = new TextureCommandList();
        if (admitted)
        {
            resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
            await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(36ul);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(20ul);
            await Assert.That(device.CreatedTextures.Single().MipLevelCount).IsEqualTo(2u);
            resources.Dispose();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        else
        {
            await Assert.That(() => resources.BindMaterialTexture(
                commands, material, SilkMaterialParameter.DiffuseColor))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(device.TextureOwners).IsEmpty();
            await Assert.That(resources.Diagnostics.Entries.Any(
                entry => entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsFalse();
        }
    }

    [Test]
    public async Task QuietPageHotReloadRetainsThePreviousTextureWhenReplacementIsRefused()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "before");
            byte[] material = CreateMaterialUpsert(
                "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
                textures:
                [
                    new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                        SilkColorSpace.Raw, 4, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], path, "st"),
                ]);
            using var device = new TextureGraphicsDevice();
            var budget = new SilkGpuTextureBudget(64);
            budget.ConfigureDevice(device);
            using var renderer = new SilkMeshRenderer(device, SilkShaderBinaryFormat.Dxil, (asset, _) =>
                new SilkDecodedImage(1, 1, File.ReadAllText(asset) == "before"
                    ? [255, 0, 0, 255] : [0, 0, 255, 255]));
            using var first = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
                [.. material, .. CreateMeshUpsert("/Mesh", "/Material")], 2);
            renderer.ApplyPage(first);
            Texture previous = device.TextureOwners.Single(texture => texture.Format == SilkTextureFormat.Rgba8Unorm);
            ulong reserved = budget.Usage.ReservedBytes;
            await File.WriteAllTextAsync(path, "after-longer");
            using var quiet = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, [], 0);
            using (IDisposable pressure = budget.Reserve(
                new SilkTextureDescriptor(checked((uint)((budget.MaximumBytes - reserved) / 4)),
                    1, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled), 1))
            {
                await Assert.That(() => renderer.ApplyPage(quiet)).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(previous.Released).IsFalse();
                await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(budget.MaximumBytes);
                using var commands = new TextureCommandList();
                renderer.GpuResources.BindMaterialTexture(
                    commands, renderer.Scene.Materials["/Material"], SilkMaterialParameter.DiffuseColor);
                await Assert.That(commands.UploadCount).IsEqualTo(0);
                await Assert.That(previous.Released).IsFalse();
            }
            renderer.ApplyPage(quiet);
            await Assert.That(previous.Released).IsTrue();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
            renderer.Dispose();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task DisabledMaterialPreparationDoesNotDecodeOrAllocateMaterialTextures()
    {
        byte[] material = CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f],
                    "not-needed.png", "st"),
            ]);
        using var device = new TextureGraphicsDevice();
        var budget = new SilkGpuTextureBudget(64);
        budget.ConfigureDevice(device);
        int decodes = 0;
        using var renderer = new SilkMeshRenderer(device, SilkShaderBinaryFormat.Dxil, (_, _) =>
        {
            decodes++;
            throw new InvalidOperationException("Disabled material textures must not be decoded.");
        });
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. material, .. CreateMeshUpsert("/First", "/Material")], 2);
        renderer.ApplyPage(page, useSceneMaterials: false);
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(1);
        await Assert.That(decodes).IsEqualTo(0);
        await Assert.That(device.TextureOwners.Count).IsEqualTo(2);
        await Assert.That(device.CreatedTextureFormats).DoesNotContain(SilkTextureFormat.Rgba8Unorm);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(12ul);
    }

    [Test]
    [Arguments("texture", false)]
    [Arguments("upload", false)]
    [Arguments("cpu", false)]
    [Arguments("texture", true)]
    [Arguments("upload", true)]
    [Arguments("cpu", true)]
    public async Task MaterialPageRefusalRetainsPublishedTexturesAndMeshesUntilSuccessfulRetry(
        string refusal, bool luminanceOnly)
    {
        bool refuseUpload = refusal == "upload";
        static byte[] material(string asset, bool mixed = false) => CreateMaterialUpsert(
            "/Material",
            SilkSurfaceKind.PreviewSurface,
            scalars: mixed ? [(SilkMaterialParameter.DiffuseTextureLuminanceMix, [0.25f, 0.5f, 0.25f, 1f])] : [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [1f, 1f, 1f, 1f], [0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], asset, "st"),
            ]);
        using var device = new TextureGraphicsDevice();
        var budget = new SilkGpuTextureBudget(64);
        budget.ConfigureDevice(device);
        SilkCpuTextureBudget? cpu = refusal == "cpu" ? new SilkCpuTextureBudget(64) : null;
        cpu?.ConfigureDevice(device);
        int decodes = 0;
        SilkDecodedImage decode(string asset, bool _)
        {
            decodes++;
            return new SilkDecodedImage(1, 1, asset == "before.png" ? [255, 0, 0, 255] : [0, 0, 255, 255]);
        }
        using var renderer = new SilkMeshRenderer(device, SilkShaderBinaryFormat.Dxil,
            decode, cpuTextureBudget: cpu, ownedImageDecoder: (asset, linearize, owner) =>
            {
                IDisposable reservation = owner.Reserve(4);
                SilkDecodedImage image = decode(asset, linearize);
                image.OwnReservation(reservation);
                return image;
            });
        using var first = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. material("before.png"), .. CreateMeshUpsert("/First", "/Material"),
                .. CreateMeshUpsert("/Removed", "/Material", 8)], 3);
        renderer.ApplyPage(first);
        // Exercise the public material-binding seam too: the prepared texture must be the cached one.
        using (var commands = new TextureCommandList())
        {
            renderer.GpuResources.BindMaterialTexture(commands, renderer.Scene.Materials["/Material"],
                SilkMaterialParameter.DiffuseColor);
            await Assert.That(commands.UploadCount).IsEqualTo(0);
        }
        Texture original = device.TextureOwners.Single(texture => texture.Format == SilkTextureFormat.Rgba8Unorm);
        SilkMaterialData originalMaterial = renderer.Scene.Materials["/Material"];
        Dictionary<ulong, SilkMeshGpuResource> originalMeshes = renderer.GpuResources.Meshes.ToDictionary();
        ulong revision = renderer.GpuResources.Revision;
        ulong charged = budget.Usage.ReservedBytes;
        int priorDecodes = decodes;
        string nextAsset = luminanceOnly ? "before.png" : "after.png";
        using var replacement = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            [.. material(nextAsset, luminanceOnly), .. CreateMeshRemove("/Removed"),
                .. CreateMeshUpsert("/Added", "/Material", 9)], 3);
        int originalOwners = device.TextureOwners.Count(texture => !texture.Released);
        IDisposable? pressure = refusal == "cpu" ? cpu!.Reserve(56) : refuseUpload ? null : budget.Reserve(
            new SilkTextureDescriptor(checked((uint)((budget.MaximumBytes - charged) / 4)),
                1, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled), 1);
        device.RefuseSubmission = refuseUpload;
        try
        {
            if (refusal == "cpu")
            {
                await Assert.That(() => renderer.ApplyPage(replacement))
                    .Throws<SilkCpuTextureBudgetExceededException>();
            }
            else if (refuseUpload)
            {
                await Assert.That(() => renderer.ApplyPage(replacement))
                    .Throws<SilkGpuStagingBudgetExceededException>();
            }
            else
            {
                await Assert.That(() => renderer.ApplyPage(replacement))
                    .Throws<SilkGpuTextureBudgetExceededException>();
            }
            await Assert.That(renderer.Scene.Materials["/Material"]).IsSameReferenceAs(originalMaterial);
            await Assert.That(renderer.GpuResources.Revision).IsEqualTo(revision);
            await Assert.That(renderer.GpuResources.Meshes.Keys).IsEquivalentTo(originalMeshes.Keys);
            foreach ((ulong id, SilkMeshGpuResource mesh) in originalMeshes)
            {
                await Assert.That(renderer.GpuResources.Meshes[id]).IsSameReferenceAs(mesh);
            }
            await Assert.That(original.Released).IsFalse();
            await Assert.That(device.TextureOwners.Count(texture => !texture.Released)).IsEqualTo(originalOwners);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(refusal == "texture" ? 64ul : charged);
            if (cpu is not null)
            {
                await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(60ul);
            }
            using var oldCommands = new TextureCommandList();
            renderer.GpuResources.BindMaterialTexture(
                oldCommands, originalMaterial, SilkMaterialParameter.DiffuseColor);
            await Assert.That(oldCommands.UploadCount).IsEqualTo(0);
            await Assert.That(decodes).IsEqualTo(priorDecodes + 1);
        }
        finally
        {
            pressure?.Dispose();
            device.RefuseSubmission = false;
        }
        renderer.ApplyPage(replacement);
        await Assert.That(original.Released).IsTrue();
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(2);
        await Assert.That(renderer.Scene.Meshes.Values.Any(mesh => mesh.Path == "/First")).IsTrue();
        await Assert.That(renderer.Scene.Meshes.Values.Any(mesh => mesh.Path == "/Added")).IsTrue();
        await Assert.That(renderer.Scene.Materials["/Material"].Textures.Single().Asset).IsEqualTo(nextAsset);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(charged + (luminanceOnly ? 12ul : 0ul));
        await Assert.That(device.TextureOwners.Count(texture => !texture.Released)).IsEqualTo(originalOwners);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        if (cpu is not null)
        {
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    public async Task SparseUdimTilesUseBoundedAtlasWithAuthoredFallback()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Udim",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 0f, 1f, 1f],
                    "tiles.<UDIM>.png",
                    "st"),
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => asset.Contains("1001", StringComparison.Ordinal)
                ? new SilkDecodedImage(2, 1, [255, 0, 0, 255, 255, 0, 0, 255])
                : new SilkDecodedImage(2, 1, [0, 0, 255, 255, 0, 0, 255, 255]),
            _ =>
            [
                new SilkUdimTile(1001, "tiles.1001.png"),
                new SilkUdimTile(1003, "tiles.1003.png"),
            ]);
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.DiffuseColor);

        await Assert.That(device.CreatedTextures.Single().Width).IsEqualTo(12u);
        await Assert.That(device.CreatedTextures.Single().Height).IsEqualTo(4u);
        byte[] atlas = commands.Uploads.Single();
        await Assert.That(atlas.AsSpan(0, 4).ToArray())
            .IsEquivalentTo(new byte[] { 0, 0, 3, 1 });
        await Assert.That(atlas.AsSpan((2 * 12 + 1) * 4, 4).ToArray())
            .IsEquivalentTo(new byte[] { 255, 0, 0, 255 });
        await Assert.That(atlas.AsSpan((2 * 12 + 5) * 4, 4).ToArray())
            .IsEquivalentTo(new byte[] { 255, 0, 255, 255 });
        await Assert.That(atlas.AsSpan((2 * 12 + 9) * 4, 4).ToArray())
            .IsEquivalentTo(new byte[] { 0, 0, 255, 255 });
        await Assert.That(commands.SamplerBindings.Single().Descriptor.AddressU)
            .IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
        await Assert.That(commands.SamplerBindings.Single().Descriptor.AddressV)
            .IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
    }

    [Test]
    [Arguments(1843u, 3, 85, true)]
    [Arguments(1634u, 4, 64, true)]
    [Arguments(1853u, 3, 86, false)]
    [Arguments(1999u, 9, 100, false)]
    public async Task UdimAtlasCellLimitIsCheckedBeforeDecodingTiles(
        uint lastTile, int columns, int rows, bool admitted)
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/BoundedUdim",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 0f, 1f, 1f],
                    "tiles.<UDIM>.png",
                    "st"),
            ]));
        int decodes = 0;
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device, (_, _) =>
        {
            decodes++;
            return new SilkDecodedImage(1, 1, [255, 0, 0, 255]);
        }, _ =>
        [
            new SilkUdimTile(1001, "tiles.1001.png"),
            new SilkUdimTile(lastTile, $"tiles.{lastTile}.png"),
        ]);
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(decodes).IsEqualTo(admitted ? 2 : 0);
        await Assert.That(device.CreatedTextures.Count).IsEqualTo(1);
        await Assert.That(device.CreatedTextures.Single().Width).IsEqualTo(admitted ? (uint)(columns * 3) : 1u);
        await Assert.That(device.CreatedTextures.Single().Height).IsEqualTo(admitted ? (uint)(1 + rows * 3) : 1u);
        byte[] pixels = commands.Uploads.Single();
        if (admitted)
        {
            await Assert.That(pixels[0]).IsEqualTo((byte)0);
            await Assert.That(pixels[1]).IsEqualTo((byte)0);
            await Assert.That(pixels[2]).IsEqualTo((byte)columns);
            await Assert.That(pixels[3]).IsEqualTo((byte)rows);
            await Assert.That(resources.Diagnostics.Entries).IsEmpty();
        }
        else
        {
            await Assert.That(pixels.SequenceEqual(new byte[] { 255, 0, 255, 255 })).IsTrue();
            await Assert.That(resources.Diagnostics.Entries.Single(
                entry => entry.Code == SilkRenderDiagnosticCodes.TextureDecodeFailed).Message)
                .Contains("spans more than 256 atlas cells");
            await Assert.That(resources.Diagnostics.Entries.Count(
                entry => entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task FloatTextureDecodePreservesHdrValuesAndAppliesScaleBias()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Hdr",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.EmissiveColor,
                    SilkTextureWrap.Clamp,
                    SilkTextureWrap.Clamp,
                    SilkColorSpace.Raw,
                    4,
                    [2f, 1f, 0.5f, 1f],
                    [0.5f, 0.25f, 0f, 0f],
                    [0f, 0f, 0f, 1f],
                    "emissive.exr",
                    "st"),
            ]));
        float[] source =
        [
            1f, 2f, 3f, 1f,
            4f, 5f, 6f, 1f,
            7f, 8f, 9f, 1f,
            10f, 11f, 12f, 1f,
        ];
        byte[] sourceBytes = MemoryMarshal.AsBytes(source.AsSpan()).ToArray();
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(
                2,
                2,
                sourceBytes,
                SilkTextureFormat.Rgba32Float));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(
            commands,
            material,
            SilkMaterialParameter.EmissiveColor);

        await Assert.That(device.CreatedTextureFormats)
            .Contains(SilkTextureFormat.Rgba32Float);
        float[] uploaded = MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray();
        // The base 2x2 level is followed by its CPU-generated 1x1 mip: an ordinary component
        // average of the four base texels (this is not a normal-map slot).
        await Assert.That(uploaded)
            .IsEquivalentTo(
            [
                14.5f, 8.25f, 4.5f, 1f,
                20.5f, 11.25f, 6f, 1f,
                2.5f, 2.25f, 1.5f, 1f,
                8.5f, 5.25f, 3f, 1f,
                11.5f, 6.75f, 3.75f, 1f,
            ]);
    }

    [Test]
    [Arguments(19ul, false, 4ul)]
    [Arguments(31ul, false, 16ul)]
    [Arguments(32ul, true, 16ul)]
    public async Task ByteTextureAffinePromotesWithoutClippingAndAdmitsAllLiveCopies(
        ulong maximum, bool admitted, ulong reservedAtFailure)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(maximum);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded decoding is forbidden."),
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, budget) =>
            {
                IDisposable reservation = budget.Reserve(4);
                var image = new SilkDecodedImage(1, 1, [255, 0, 128, 255]);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.EmissiveColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [3f, 2f, 1f, 1f], [-1f, -0.5f, 0f, 0f], [1f, 0f, 1f, 1f],
                    "affine.png", "st"),
            ]));
        using var commands = new TextureCommandList();
        if (admitted)
        {
            resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor);
            await Assert.That(device.CreatedTextureFormats.Single()).IsEqualTo(SilkTextureFormat.Rgba32Float);
            float[] values = MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray();
            await Assert.That(values.SequenceEqual(new[] { 2f, -0.5f, 128f / 255f, 1f })).IsTrue();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(16ul);
            await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(32ul);
            resources.Dispose();
        }
        else
        {
            SilkCpuTextureBudgetExceededException? error = await Assert.That(() =>
                resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(error!.RequestedBytes).IsEqualTo(16ul);
            await Assert.That(error.ReservedBytes).IsEqualTo(reservedAtFailure);
            await Assert.That(device.CreatedTextureFormats).IsEmpty();
            await Assert.That(commands.Uploads).IsEmpty();
        }
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(143ul, false)]
    [Arguments(144ul, true)]
    public async Task PromotedNormalTextureMipsPreserveUnclippedDirectionAndAlpha(ulong maximum, bool admitted)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(maximum);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded decoding is forbidden."),
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, budget) =>
            {
                IDisposable reservation = budget.Reserve(16);
                var image = new SilkDecodedImage(2, 2,
                    [255, 255, 255, 255, 255, 0, 255, 128, 255, 255, 255, 64, 0, 255, 255, 0]);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.Normal, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 3, [3f, 3f, 1f, 1f], [-1f, -1f, 0f, 0f], [0.5f, 0.5f, 1f, 1f],
                    "normal.png", "st"),
            ]));
        using var commands = new TextureCommandList();
        if (admitted)
        {
            resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Normal);
            await Assert.That(device.CreatedTextureFormats.Single()).IsEqualTo(SilkTextureFormat.Rgba32Float);
            float[] values = MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray();
            await Assert.That(values.Length).IsEqualTo(20);
            await Assert.That(values.Take(16).SequenceEqual(new[]
                { 2f, 2f, 1f, 64f / 255f, -1f, 2f, 1f, 0f, 2f, 2f, 1f, 1f, 2f, -1f, 1f, 128f / 255f }))
                .IsTrue();
            float[] expected = [0.5f + 1.5f / MathF.Sqrt(22), 0.5f + 1.5f / MathF.Sqrt(22),
                0.5f + 1f / MathF.Sqrt(22), (1f + 128f / 255f + 64f / 255f) / 4f];
            for (int component = 0; component < 4; component++)
            {
                await Assert.That(MathF.Abs(values[16 + component] - expected[component])).IsLessThan(0.000001f);
            }
            await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(144ul);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(80ul);
            resources.Dispose();
        }
        else
        {
            SilkCpuTextureBudgetExceededException? failure = await Assert.That(() =>
                resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Normal))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(failure!.RequestedBytes).IsEqualTo(80ul);
            await Assert.That(failure.ReservedBytes).IsEqualTo(64ul);
            await Assert.That(device.CreatedTextureFormats).IsEmpty();
            await Assert.That(commands.Uploads).IsEmpty();
        }
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(415ul, false)]
    [Arguments(416ul, true)]
    public async Task PromotedUdimTilesAndAtlasKeepExactOverlapOwnership(ulong maximum, bool admitted)
    {
        using var device = new TextureGraphicsDevice();
        var cpu = new SilkCpuTextureBudget(maximum);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Unbounded decoding is forbidden."),
            _ => [new SilkUdimTile(1001, "1001.png"), new SilkUdimTile(1002, "1002.png")],
            cpuTextureBudget: cpu, ownedImageDecoder: (_, _, budget) =>
            {
                IDisposable reservation = budget.Reserve(4);
                var image = new SilkDecodedImage(1, 1, [255, 0, 0, 255]);
                image.OwnReservation(reservation);
                return image;
            });
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.EmissiveColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [3f, 1f, 1f, 1f], [-1f, 0f, 0f, 0f], [1f, 0f, 1f, 1f],
                    "<UDIM>.png", "st"),
            ]));
        using var commands = new TextureCommandList();
        if (admitted)
        {
            resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor);
            await Assert.That(device.CreatedTextureFormats.Single()).IsEqualTo(SilkTextureFormat.Rgba32Float);
            float[] values = MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray();
            await Assert.That(values[(2 * 6 + 1) * 4]).IsEqualTo(2f);
            await Assert.That(values[(2 * 6 + 4) * 4]).IsEqualTo(2f);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(384ul);
            await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(416ul);
            resources.Dispose();
        }
        else
        {
            await Assert.That(() =>
                resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(device.CreatedTextureFormats).IsEmpty();
            await Assert.That(commands.Uploads).IsEmpty();
        }
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FloatAffineFallbackIsUnclippedWhileUnitRangeKeepsByteStorage(bool extended)
    {
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device, (_, _) => throw new FileNotFoundException("missing"));
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/Material", SilkSurfaceKind.PreviewSurface, scalars: [],
            textures:
            [
                new TextureSpec(SilkMaterialParameter.EmissiveColor, SilkTextureWrap.Repeat, SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw, 4, [extended ? 3f : 1f, 1f, 1f, 1f],
                    [extended ? -1f : 0f, 0f, 0f, 0f], [1f, 0f, 1f, 1f], "missing.png", "st"),
            ]));
        using var commands = new TextureCommandList();
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor);
        await Assert.That(device.CreatedTextureFormats.Single())
            .IsEqualTo(extended ? SilkTextureFormat.Rgba32Float : SilkTextureFormat.Rgba8Unorm);
        if (extended)
        {
            await Assert.That(MemoryMarshal.Cast<byte, float>(commands.Uploads.Single()).ToArray()
                .SequenceEqual(new[] { 2f, 0f, 1f, 1f })).IsTrue();
        }
        else
        {
            await Assert.That(commands.Uploads.Single().SequenceEqual(new byte[] { 255, 0, 255, 255 })).IsTrue();
        }
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsTrue();
    }

    [Test]
    public async Task UnresolvedAndUnsupportedMaterialsUseDistinctDiagnostics()
    {
        using var device = new TextureGraphicsDevice();

        var unresolvedScene = new SilkSceneState();
        using (var unresolvedResources = new SilkSceneGpuResources(device))
        {
            byte[] mesh = CreateMeshUpsert(
                "/World/Unresolved",
                "/World/Materials/Missing");
            SilkSceneDelta delta = unresolvedScene.Apply(mesh, 1, 1);
            unresolvedResources.Apply(unresolvedScene, delta);
            _ = unresolvedResources.RequireSurfaceBuffer(
                unresolvedScene,
                unresolvedScene.Meshes.Values.Single(),
                RenderHeadlight.Deterministic);

            await Assert.That(unresolvedResources.Diagnostics.Entries.Select(
                    entry => entry.Code))
                .Contains(SilkRenderDiagnosticCodes.MaterialUnresolved);

            byte[] remove = CreateMeshRemove("/World/Unresolved");
            SilkSceneDelta removal = unresolvedScene.Apply(remove, 1, 2);
            unresolvedResources.Apply(unresolvedScene, removal);
            await Assert.That(unresolvedResources.Diagnostics.Entries.Select(
                    entry => entry.Code))
                .DoesNotContain(SilkRenderDiagnosticCodes.MaterialUnresolved);
        }

        var unsupportedScene = new SilkSceneState();
        using var unsupportedResources = new SilkSceneGpuResources(device);
        byte[] material = CreateMaterialUpsert(
            "/World/Materials/Exotic",
            SilkSurfaceKind.Unsupported,
            scalars: [],
            textures: []);
        byte[] unsupportedMesh = CreateMeshUpsert(
            "/World/Unsupported",
            "/World/Materials/Exotic");
        byte[] page = [.. material, .. unsupportedMesh];
        SilkSceneDelta unsupportedDelta = unsupportedScene.Apply(page, 2, 1);
        unsupportedResources.Apply(unsupportedScene, unsupportedDelta);
        _ = unsupportedResources.RequireSurfaceBuffer(
            unsupportedScene,
            unsupportedScene.Meshes.Values.Single(),
            RenderHeadlight.Deterministic);

        await Assert.That(unsupportedResources.Diagnostics.Entries.Select(
                entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.MaterialUnsupported);
        await Assert.That(unsupportedResources.Meshes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MaterialDiagnosticsAreDeduplicatedAndBounded()
    {
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => throw new FileNotFoundException("Missing.", asset));
        using var commands = new TextureCommandList();

        for (int index = 0; index < 130; index++)
        {
            SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
                $"/World/Materials/Missing{index}",
                SilkSurfaceKind.PreviewSurface,
                scalars: [],
                textures:
                [
                    new TextureSpec(
                        SilkMaterialParameter.DiffuseColor,
                        SilkTextureWrap.Repeat,
                        SilkTextureWrap.Repeat,
                        SilkColorSpace.Srgb,
                        ComponentCount: 3,
                        Scale: [1f, 1f, 1f, 1f],
                        Bias: [0f, 0f, 0f, 0f],
                        Fallback: [1f, 0f, 1f, 1f],
                        Asset: $"missing{index}.png",
                        UvPrimvar: "st"),
                ]));
            resources.UploadMaterialTexture(
                commands,
                material,
                SilkMaterialParameter.DiffuseColor);
        }

        await Assert.That(resources.Diagnostics.Entries.Count).IsEqualTo(128);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.CapacityExceeded);
    }

    [Test]
    public async Task SharedMissingAssetPreservesEachMaterialsFallback()
    {
        SilkMaterialData red = CreateMissingMaterial(
            "/World/Materials/Red",
            [1f, 0f, 0f, 1f]);
        SilkMaterialData blue = CreateMissingMaterial(
            "/World/Materials/Blue",
            [0f, 0f, 1f, 1f]);
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => throw new FileNotFoundException("Missing.", asset));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(
            commands,
            red,
            SilkMaterialParameter.DiffuseColor);
        resources.UploadMaterialTexture(
            commands,
            blue,
            SilkMaterialParameter.DiffuseColor);

        await Assert.That(commands.Uploads[0])
            .IsEquivalentTo(new byte[] { 255, 0, 0, 255 });
        await Assert.That(commands.Uploads[1])
            .IsEquivalentTo(new byte[] { 0, 0, 255, 255 });
    }

    [Test]
    public async Task RemovingLastMaterialUserPrunesTextureFailureDiagnostics()
    {
        const string materialPath = "/World/Materials/Missing";
        byte[] material = CreateMaterialUpsert(
            materialPath,
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [1f, 0f, 1f, 1f],
                    Asset: "missing.png",
                    UvPrimvar: "st"),
            ]);
        byte[] mesh = CreateMeshUpsert("/World/Mesh", materialPath);
        var scene = new SilkSceneState();
        SilkSceneDelta first = scene.Apply([.. material, .. mesh], 2, 1);
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => throw new FileNotFoundException("Missing.", asset));
        using var commands = new TextureCommandList();
        resources.Apply(scene, first);
        resources.UploadMaterialTexture(
            commands,
            scene.Materials[materialPath],
            SilkMaterialParameter.DiffuseColor);

        SilkSceneDelta removal = scene.Apply(
            CreateMeshRemove("/World/Mesh"),
            1,
            2);
        resources.Apply(scene, removal);

        await Assert.That(resources.Diagnostics.Entries).IsEmpty();
    }

    [Test]
    [Arguments(0f)]
    [Arguments(-1f)]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task SamplerDescriptorValidateRejectsNonFiniteOrSubOneAnisotropy(float maxAnisotropy)
    {
        var descriptor = new SilkSamplerDescriptor(
            SilkSamplerFilter.Linear,
            SilkSamplerFilter.Linear,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            maxAnisotropy);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => descriptor.Validate()));
    }

    [Test]
    public async Task SamplerDescriptorValidateAcceptsAnisotropyAtOrAboveOne()
    {
        var oneX = new SilkSamplerDescriptor(
            SilkSamplerFilter.Linear,
            SilkSamplerFilter.Linear,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge);
        var sixteenX = oneX with { MaxAnisotropy = 16f };

        oneX.Validate();
        sixteenX.Validate();

        await Assert.That(oneX.MaxAnisotropy).IsEqualTo(1f);
        await Assert.That(sixteenX.MaxAnisotropy).IsEqualTo(16f);
    }

    [Test]
    public async Task SamplerDescriptorValidateWithCapabilityRejectsRequestAboveDeviceMax()
    {
        var capabilities = new SilkGraphicsCapabilities(
            "test",
            "1.0",
            SupportsCompute: false,
            IsSoftware: true)
        {
            MaxSamplerAnisotropy = 4f,
        };
        var descriptor = new SilkSamplerDescriptor(
            SilkSamplerFilter.Linear,
            SilkSamplerFilter.Linear,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            8f);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => descriptor.Validate(capabilities)));
    }

    [Test]
    public async Task SamplerDescriptorValidateWithCapabilityAcceptsRequestAtOrBelowDeviceMax()
    {
        var capabilities = new SilkGraphicsCapabilities(
            "test",
            "1.0",
            SupportsCompute: false,
            IsSoftware: true)
        {
            MaxSamplerAnisotropy = 8f,
        };
        var atMax = new SilkSamplerDescriptor(
            SilkSamplerFilter.Linear,
            SilkSamplerFilter.Linear,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            8f);
        var belowMax = atMax with { MaxAnisotropy = 2f };

        atMax.Validate(capabilities);
        belowMax.Validate(capabilities);

        await Assert.That(atMax.MaxAnisotropy).IsEqualTo(8f);
        await Assert.That(belowMax.MaxAnisotropy).IsEqualTo(2f);
    }

    [Test]
    public async Task SamplerDescriptorValidateWithDefaultCapabilityPreservesIsotropicOnlyDevices()
    {
        // The default capability (1x) is what every backend advertised before this slice; a
        // descriptor asking for more than 1x must still be rejected against that default so
        // existing callers that never opt in keep their prior isotropic-only behavior.
        var capabilities = new SilkGraphicsCapabilities("test", "1.0", false, false);
        var descriptor = new SilkSamplerDescriptor(
            SilkSamplerFilter.Linear,
            SilkSamplerFilter.Linear,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            SilkSamplerAddressMode.ClampToEdge,
            2f);

        await Assert.That(capabilities.MaxSamplerAnisotropy).IsEqualTo(1f);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => descriptor.Validate(capabilities)));
    }

    [Test]
    public async Task SamplerDescriptorsDifferingOnlyByAnisotropyAreDistinctCacheKeys()
    {
        var isotropic = SilkSamplerDescriptor.LinearClamp;
        var anisotropic = isotropic with { MaxAnisotropy = 8f };

        await Assert.That(isotropic).IsNotEqualTo(anisotropic);

        var cache = new Dictionary<SilkSamplerDescriptor, int>
        {
            [isotropic] = 1,
            [anisotropic] = 2,
        };

        await Assert.That(cache).Count().IsEqualTo(2);
        await Assert.That(cache[isotropic]).IsEqualTo(1);
        await Assert.That(cache[anisotropic]).IsEqualTo(2);
    }

    [Test]
    public async Task BindMaterialTextureBoundsAnisotropyToDefaultWhenDeviceExceedsIt()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/AnisoHighCap",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 1f, 1f, 1f],
                    "aniso-high.png",
                    "st"),
            ]));
        using var device = new TextureGraphicsDevice { MaxSamplerAnisotropy = 16f };
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, new byte[2 * 2 * 4]));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // Bounded default policy: min(device max, 8), even though the device advertises 16x.
        await Assert.That(commands.SamplerBindings.Single().Descriptor.MaxAnisotropy)
            .IsEqualTo(8f);
    }

    [Test]
    public async Task BindMaterialTextureBoundsAnisotropyToDeviceMaxWhenBelowDefault()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/AnisoLowCap",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 1f, 1f, 1f],
                    "aniso-low.png",
                    "st"),
            ]));
        using var device = new TextureGraphicsDevice { MaxSamplerAnisotropy = 4f };
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, new byte[2 * 2 * 4]));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(commands.SamplerBindings.Single().Descriptor.MaxAnisotropy)
            .IsEqualTo(4f);
    }

    [Test]
    public async Task BindMaterialTextureKeepsIsotropicSamplingWhenDeviceLacksAnisotropySupport()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/AnisoUnsupported",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 1f, 1f, 1f],
                    "aniso-unsupported.png",
                    "st"),
            ]));
        // Default TextureGraphicsDevice capability (1x) mirrors every backend before this
        // slice; a mipmapped, linearly filtered, non-UDIM texture must still stay isotropic.
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, new byte[2 * 2 * 4]));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(commands.SamplerBindings.Single().Descriptor.MaxAnisotropy)
            .IsEqualTo(1f);
    }

    [Test]
    public async Task BindMaterialTextureKeepsSingleMipTextureIsotropicEvenWithAnisotropySupport()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/AnisoSingleMip",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 1f, 1f, 1f],
                    "aniso-single-mip.png",
                    "st"),
            ]));
        using var device = new TextureGraphicsDevice { MaxSamplerAnisotropy = 16f };
        using var resources = new SilkSceneGpuResources(
            device,
            // A 1x1 image never produces more than a single mip level.
            (_, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(commands.SamplerBindings.Single().Descriptor.MaxAnisotropy)
            .IsEqualTo(1f);
    }

    [Test]
    public async Task BindMaterialTextureKeepsNearestFloatSamplingIsotropicEvenWithAnisotropySupport()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/AnisoFloat",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.EmissiveColor,
                    SilkTextureWrap.Clamp,
                    SilkTextureWrap.Clamp,
                    SilkColorSpace.Raw,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [0f, 0f, 0f, 1f],
                    "aniso-float.exr",
                    "st"),
            ]));
        float[] source =
        [
            1f, 2f, 3f, 1f,
            4f, 5f, 6f, 1f,
            7f, 8f, 9f, 1f,
            10f, 11f, 12f, 1f,
        ];
        byte[] sourceBytes = MemoryMarshal.AsBytes(source.AsSpan()).ToArray();
        using var device = new TextureGraphicsDevice { MaxSamplerAnisotropy = 16f };
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, sourceBytes, SilkTextureFormat.Rgba32Float));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.EmissiveColor);

        // Rgba32Float is always sampled with a Nearest filter; anisotropic filtering only
        // applies to Linear-filtered sampling, so this must stay at 1x regardless of device
        // capability or the real (>1) mip chain this HDR image generates.
        await Assert.That(commands.SamplerBindings.Single().Descriptor.MaxAnisotropy)
            .IsEqualTo(1f);
    }

    [Test]
    public async Task BindMaterialTextureKeepsUdimAtlasIsotropicEvenWithAnisotropySupport()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/AnisoUdim",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    4,
                    [1f, 1f, 1f, 1f],
                    [0f, 0f, 0f, 0f],
                    [1f, 0f, 1f, 1f],
                    "aniso-tiles.<UDIM>.png",
                    "st"),
            ]));
        using var device = new TextureGraphicsDevice { MaxSamplerAnisotropy = 16f };
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => asset.Contains("1001", StringComparison.Ordinal)
                ? new SilkDecodedImage(2, 1, [255, 0, 0, 255, 255, 0, 0, 255])
                : new SilkDecodedImage(2, 1, [0, 0, 255, 255, 0, 0, 255, 255]),
            _ =>
            [
                new SilkUdimTile(1001, "aniso-tiles.1001.png"),
                new SilkUdimTile(1003, "aniso-tiles.1003.png"),
            ]);
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // UDIM atlases always stay single-level (gutter/fallback correctness), so they must
        // stay isotropic even when the device advertises anisotropic filtering support.
        await Assert.That(commands.SamplerBindings.Single().Descriptor.MaxAnisotropy)
            .IsEqualTo(1f);
    }

    [Test]
    public async Task RoughnessAndMetallicTexturesSelectIndependentFeatureBits()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/IndependentRoughMetal",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.R),
                ScalarTexture(SilkMaterialParameter.Metallic, "metal.png", SilkTextureChannel.R),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.RoughnessMetallicMap |
            SilkShaderFeatures.MetallicMap);
    }

    [Test]
    public async Task PackedRoughnessAndMetallicChannelsOfOneFileSelectBothFeatureBits()
    {
        // A packed occlusion/roughness/metallic file is authored as one UsdUVTexture
        // prim with two output connections. The asset is identical; only the channel
        // tells the two inputs apart, and both must still light their own bit.
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/PackedRoughMetal",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "orm.png", SilkTextureChannel.G),
                ScalarTexture(SilkMaterialParameter.Metallic, "orm.png", SilkTextureChannel.B),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.RoughnessMetallicMap |
            SilkShaderFeatures.MetallicMap);
        await Assert.That(material.GetTexture(SilkMaterialParameter.Roughness)!.Channel)
            .IsEqualTo(SilkTextureChannel.G);
        await Assert.That(material.GetTexture(SilkMaterialParameter.Metallic)!.Channel)
            .IsEqualTo(SilkTextureChannel.B);
    }

    [Test]
    public async Task RoughnessOnlyTextureLeavesMetallicUntextured()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/RoughOnly",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Metallic, [0.75f])],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.G),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv | SilkShaderFeatures.RoughnessMetallicMap);

        // The authored metallic constant survives untouched: a roughness texture must
        // not consume, replace, or gate the other input.
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 48)).IsEqualTo(0.75f);
    }

    [Test]
    public async Task MetallicOnlyTextureLeavesRoughnessUntextured()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/MetalOnly",
            SilkSurfaceKind.PreviewSurface,
            scalars: [(SilkMaterialParameter.Roughness, [0.125f])],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Metallic, "metal.png", SilkTextureChannel.B),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv | SilkShaderFeatures.MetallicMap);

        // The authored roughness constant survives untouched, which the previous
        // shared-slot design could not do: it bound the metallic asset to the
        // roughness slot and read roughness out of it.
        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 52)).IsEqualTo(0.125f);
    }

    [Test]
    public async Task UdimMaskGivesRoughnessAndMetallicSeparateBits()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/UdimRoughMetal",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(
                    SilkMaterialParameter.Roughness, "rough.<UDIM>.png", SilkTextureChannel.G),
                ScalarTexture(
                    SilkMaterialParameter.Metallic, "metal.png", SilkTextureChannel.B),
            ]));
        SilkMaterialData metallicUdim = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/UdimMetalOnly",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(
                    SilkMaterialParameter.Metallic, "metal.<UDIM>.png", SilkTextureChannel.B),
            ]));

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 128)).IsEqualTo(41f);
        // The runtime mask uses the public shader-feature bits directly, so roughness
        // and metallic remain independent without maintaining a second bit mapping.
        await Assert.That(ReadSingle(constants, 132)).IsEqualTo(8f);

        SilkSurfaceUniformWriter.Write(metallicUdim, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 128)).IsEqualTo(33f);
        await Assert.That(ReadSingle(constants, 132)).IsEqualTo(32f);
    }

    [Test]
    public async Task OpacityAndOcclusionTexturesUseIndependentRuntimeAndUdimBits()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/OpacityOcclusion",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Opacity, "opacity.png", SilkTextureChannel.A),
                ScalarTexture(
                    SilkMaterialParameter.Occlusion,
                    "occlusion.<UDIM>.png",
                    SilkTextureChannel.R),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.OpacityMap |
            SilkShaderFeatures.OcclusionMap);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 128)).IsEqualTo(193f);
        await Assert.That(ReadSingle(constants, 132)).IsEqualTo(128f);
    }

    [Test]
    public async Task SpecularColorTextureUsesItsOwnRuntimeAndUdimBit()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/SpecularWorkflow",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.SpecularColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "specular.<UDIM>.png",
                    UvPrimvar: "st",
                    Channel: SilkTextureChannel.Rgb),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.SpecularColorMap);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 128)).IsEqualTo(257f);
        await Assert.That(ReadSingle(constants, 132)).IsEqualTo(256f);
    }

    [Test]
    public async Task SpecularColorAutoColorSpaceDecodesAsSrgb()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/SpecularAuto",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.SpecularColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Auto,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "specular.png",
                    UvPrimvar: "st",
                    Channel: SilkTextureChannel.Rgb),
            ]));
        bool? decodedAsSrgb = null;
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, srgb) =>
            {
                decodedAsSrgb = srgb;
                return new SilkDecodedImage(1, 1, [10, 20, 30, 255]);
            });
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.SpecularColor);

        await Assert.That(decodedAsSrgb).IsTrue();
    }

    [Test]
    public async Task ClearcoatAndIorTexturesUseIndependentRuntimeAndUdimBits()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Clearcoat",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(
                    SilkMaterialParameter.Clearcoat,
                    "clearcoat.png",
                    SilkTextureChannel.R),
                ScalarTexture(
                    SilkMaterialParameter.ClearcoatRoughness,
                    "clearcoat-roughness.png",
                    SilkTextureChannel.R),
                ScalarTexture(
                    SilkMaterialParameter.Ior,
                    "ior.<UDIM>.png",
                    SilkTextureChannel.R),
            ]));

        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.ClearcoatMap |
            SilkShaderFeatures.ClearcoatRoughnessMap |
            SilkShaderFeatures.IorMap);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);
        await Assert.That(ReadSingle(constants, 128)).IsEqualTo(3585f);
        await Assert.That(ReadSingle(constants, 132)).IsEqualTo(2048f);
    }

    /// <summary>
    /// Proves that the page a MaterialX OpenPBR projection publishes reaches every
    /// surface uniform the renderer binds.
    /// </summary>
    /// <remarks>
    /// hdSilk projects <c>ND_open_pbr_surface_surfaceshader</c> onto the same
    /// renderer-neutral parameters UsdPreviewSurface uses, so the only thing that
    /// can silently go missing is a parameter the native side publishes and the
    /// managed writer never reads. The scalars below are exactly the ones the
    /// native projection emits for a constant OpenPBR material -- base_color,
    /// base_metalness, specular_roughness, specular_ior, coat_weight,
    /// coat_roughness, geometry_opacity, and emission_color scaled by
    /// emission_luminance -- and each is asserted at its documented offset.
    /// </remarks>
    [Test]
    public async Task ProjectedOpenPbrScalarsReachEverySurfaceUniform()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/OpenPbr",
            SilkSurfaceKind.MaterialXProjected,
            scalars:
            [
                (SilkMaterialParameter.DiffuseColor, [0.2f, 0.4f, 0.6f]),
                (SilkMaterialParameter.EmissiveColor, [1f, 0.5f, 0.25f]),
                (SilkMaterialParameter.Metallic, [0.75f]),
                (SilkMaterialParameter.Roughness, [0.25f]),
                (SilkMaterialParameter.Ior, [1.45f]),
                (SilkMaterialParameter.Clearcoat, [0.5f]),
                (SilkMaterialParameter.ClearcoatRoughness, [0.125f]),
                (SilkMaterialParameter.Opacity, [0.375f]),
            ],
            textures: []));

        await Assert.That(material.IsSupported).IsTrue();
        await Assert.That(material.SurfaceKind)
            .IsEqualTo(SilkSurfaceKind.MaterialXProjected);
        // No image is bound, so the projection must not select a texture
        // permutation: a constant OpenPBR material shades from uniforms alone.
        await Assert.That(material.GetTextureFeatures())
            .IsEqualTo(SilkShaderFeatures.None);

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        await Assert.That(ReadSingle(constants, 0)).IsEqualTo(0.2f);
        await Assert.That(ReadSingle(constants, 4)).IsEqualTo(0.4f);
        await Assert.That(ReadSingle(constants, 8)).IsEqualTo(0.6f);
        await Assert.That(ReadSingle(constants, 12)).IsEqualTo(0.375f);
        await Assert.That(ReadSingle(constants, 16)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, 20)).IsEqualTo(0.5f);
        await Assert.That(ReadSingle(constants, 24)).IsEqualTo(0.25f);
        await Assert.That(ReadSingle(constants, 44)).IsEqualTo(1.45f);
        await Assert.That(ReadSingle(constants, 48)).IsEqualTo(0.75f);
        await Assert.That(ReadSingle(constants, 52)).IsEqualTo(0.25f);
        await Assert.That(ReadSingle(constants, 64)).IsEqualTo(0.5f);
        await Assert.That(ReadSingle(constants, 68)).IsEqualTo(0.125f);
        // The shaded flag: a projected MaterialX surface shades from the material
        // rather than falling back to the scene display colour.
        await Assert.That(ReadSingle(constants, 72)).IsEqualTo(1f);
    }

    /// <summary>
    /// Proves that a MaterialX surface whose lobes this projection cannot carry
    /// still leaves the renderer at its own documented defaults rather than at a
    /// value borrowed from an unrelated input.
    /// </summary>
    [Test]
    public async Task ProjectedMaterialWithNoCarriedLobesKeepsRendererDefaults()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Transmissive",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures: []));

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(material, RenderHeadlight.Deterministic, constants);

        // Opacity stays fully opaque: a transmission weight the projection drops
        // must not arrive as transparency, which would be a different picture.
        await Assert.That(ReadSingle(constants, 12)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, 44)).IsEqualTo(1.5f);
        await Assert.That(ReadSingle(constants, 64)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 16)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 20)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, 24)).IsEqualTo(0f);
    }

    /// <summary>
    /// Proves the expanded MaterialX projection selects only shader permutations
    /// the repository already ships compiled for both D3D12 and Vulkan.
    /// </summary>
    /// <remarks>
    /// The broader OpenPBR and standard_surface tables add no new texture slots:
    /// every image they can bind is one UsdPreviewSurface already had. That claim
    /// is only worth making if it is checked, because a projection that selected an
    /// uncompiled permutation would fail at pipeline creation on a user's machine
    /// rather than here. Both backend artifacts are asserted, so the claim covers
    /// the D3D12 (DXIL) and Vulkan (SPIR-V) paths and not just one of them.
    /// </remarks>
    [Test]
    public async Task ProjectedMaterialTexturesSelectCheckedBackendPermutations()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/OpenPbrTextured",
            SilkSurfaceKind.MaterialXProjected,
            scalars: [],
            textures:
            [
                // The scale a constant base_weight folds into the image, which is
                // the one thing the weight projection changes about the entry.
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [0.5f, 0.5f, 0.5f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "textures/openpbr-basecolor.png",
                    UvPrimvar: "uvSet0"),
                new TextureSpec(
                    SilkMaterialParameter.Normal,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 1f, 1f],
                    Asset: "textures/openpbr-normal.png",
                    UvPrimvar: "uvSet0"),
                ScalarTexture(
                    SilkMaterialParameter.Opacity,
                    "textures/openpbr-opacity.png",
                    SilkTextureChannel.R),
                ScalarTexture(
                    SilkMaterialParameter.Clearcoat,
                    "textures/openpbr-coat.png",
                    SilkTextureChannel.R),
                ScalarTexture(
                    SilkMaterialParameter.Ior,
                    "textures/openpbr-ior.png",
                    SilkTextureChannel.R),
            ]));

        SilkShaderFeatures features = material.GetTextureFeatures();
        await Assert.That(features).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.BaseColorMap |
            SilkShaderFeatures.NormalMap |
            SilkShaderFeatures.OpacityMap |
            SilkShaderFeatures.ClearcoatMap |
            SilkShaderFeatures.IorMap);

        var permutation = new SilkShaderPermutationId(features);
        await Assert.That(permutation.MeshFragmentArtifactName)
            .IsEqualTo("mesh.fragment.uv+material+normal");

        string root = FindRepositoryRoot();
        foreach (string extension in new[] { "dxil", "spv" })
        {
            string artifact = Path.Combine(
                root,
                "eng",
                "shaders",
                "checked",
                $"{permutation.MeshFragmentArtifactName}.{extension}");
            await Assert.That(File.Exists(artifact))
                .IsTrue()
                .Because(
                    "The expanded MaterialX projection must only select shader " +
                    "permutations that are already compiled for every backend; " +
                    artifact + " is missing.");
        }

        // The folded weight travels on the entry the renderer uploads, so the
        // native scale is what the managed side must still see.
        SilkMaterialTexture? baseColor =
            material.GetTexture(SilkMaterialParameter.DiffuseColor);
        await Assert.That(baseColor).IsNotNull();
        await Assert.That(baseColor!.Scale[0]).IsEqualTo(0.5f);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The repository root was not found.");
    }

    [Test]
    public async Task NativeVolumeRequiresItsPageAndRetainsDataAfterPageDisposal()
    {
        const string path = "/World/Materials/Volume";
        byte[] native = CreateVolumeMaterial(path, "hdsilk-volume:/density.vdb");
        byte[] synthetic = CreateVolumeMaterial(path, "density.raw");
        using var scene = new SilkSceneState();

        await Assert.That(() => CopyMaterial(native)).Throws<InvalidDataException>()
            .WithMessageContaining("owning OpenUsdSilkPage");
        await Assert.That(() => scene.Apply(native, 1, 1)).Throws<InvalidDataException>()
            .WithMessageContaining("owning OpenUsdSilkPage");
        _ = scene.Apply(synthetic, 1, 1);
        await Assert.That(scene.Materials[path].GetTexture(SilkMaterialParameter.VolumeDensity)!.Asset)
            .IsEqualTo("density.raw");

        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, native, 1);
        await Assert.That(() => scene.Apply(page)).Throws<InvalidDataException>()
            .WithMessageContaining("decoded-field lease");
        var assets = new TrackedVolumeAssets(path, [1, 2, 3, 4]);
        page.SetVolumeAssets(assets);
        _ = scene.Apply(page);
        page.Dispose();

        SilkMaterialTexture texture = scene.Materials[path].GetTexture(SilkMaterialParameter.VolumeDensity)!;
        byte[] copied = new byte[4];
        await Assert.That(texture.TryCopyVolumeAssets(copied)).IsTrue();
        await Assert.That(copied.SequenceEqual(new byte[] { 1, 2, 3, 4 })).IsTrue();
        await Assert.That(assets.Disposed).IsFalse();
        scene.Dispose();
        await Assert.That(assets.Disposed).IsTrue();
        await Assert.That(() => texture.TryCopyVolumeAssets(new byte[4])).Throws<ObjectDisposedException>();
        await Assert.That(() => scene.RestoreAuthoredPoints(1)).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task VolumeReplayRollbackReplacementAndRemovalReleaseTheirOwnLeases()
    {
        const string path = "/World/Materials/Volume";
        byte[] command = CreateVolumeMaterial(path, "hdsilk-volume:/density.vdb");
        var originalAssets = new TrackedVolumeAssets(path, [1, 1, 1, 1]);
        var replacementAssets = new TrackedVolumeAssets(path, [2, 2, 2, 2]);
        using var scene = new SilkSceneState();
        using var original = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1, command, 1);
        original.SetVolumeAssets(originalAssets);
        _ = scene.Apply(original);
        original.Dispose();

        using var update = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, [.. command, .. command], 2);
        update.SetVolumeAssets(replacementAssets);
        using (OpenUsdSilkPage.ReplaySnapshot snapshot = update.CaptureReplay())
        {
            update.Dispose();
            using OpenUsdSilkPage replay = snapshot.CreatePage();
            byte[] during = new byte[4];
            bool copied;
            using (SilkSceneState.PreparedPage transaction = scene.PreparePage(replay))
            {
                copied = scene.Materials[path].GetTexture(SilkMaterialParameter.VolumeDensity)!
                    .TryCopyVolumeAssets(during);
            }
            await Assert.That(copied).IsTrue();
            await Assert.That(during.SequenceEqual(new byte[] { 2, 2, 2, 2 })).IsTrue();
            byte[] restored = new byte[4];
            await Assert.That(scene.Materials[path].GetTexture(SilkMaterialParameter.VolumeDensity)!
                .TryCopyVolumeAssets(restored)).IsTrue();
            await Assert.That(restored.SequenceEqual(new byte[] { 1, 1, 1, 1 })).IsTrue();
            await Assert.That(originalAssets.Disposed).IsFalse();
            await Assert.That(replacementAssets.Disposed).IsFalse();

            _ = scene.Apply(replay);
            await Assert.That(originalAssets.Disposed).IsTrue();
            replay.Dispose();
            _ = scene.Apply(CreateMaterialRemove(path), 1, 3);
            await Assert.That(replacementAssets.Disposed).IsFalse();
        }
        await Assert.That(replacementAssets.Disposed).IsTrue();
    }

    internal static byte[] CreateVolumeMaterial(string path, string asset) =>
        CreateMaterialUpsert(path, SilkSurfaceKind.VolumeDensity, [],
        [
            new TextureSpec(SilkMaterialParameter.VolumeDensity, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp,
                SilkColorSpace.Raw, 1, [1, 1, 1, 1], [0, 0, 0, 0], [0, 0, 0, 1], asset, "1,1,1"),
        ]);

    internal sealed class TrackedVolumeAssets(string path, byte[] bytes) : SilkVolumeAssets
    {
        internal bool Disposed { get; private set; }

        protected override void CopyCore(string materialPath, Span<byte> destination)
        {
            if (materialPath != path || destination.Length != bytes.Length)
            {
                throw new InvalidDataException("Unexpected decoded-field request.");
            }
            bytes.CopyTo(destination);
        }

        protected override void DisposeCore() => Disposed = true;
    }

    [Test]
    public async Task ReplaySnapshotCleanupGuardsPhysicsCheckpointConstruction()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "OpenUsd.Rendering.Silk", "SilkMeshRenderer.cs"));
        int capture = source.IndexOf(
            "OpenUsdSilkPage.ReplaySnapshot snapshot = page.CaptureReplay();", StringComparison.Ordinal);
        await Assert.That(capture).IsGreaterThanOrEqualTo(0);
        int guardedTry = source.IndexOf("try", capture, StringComparison.Ordinal);
        int checkpoint = source.IndexOf("pendingOverrides.PrepareState()", capture, StringComparison.Ordinal);
        int prepareScene = source.IndexOf("Scene.PreparePage(page)", capture, StringComparison.Ordinal);
        await Assert.That(guardedTry).IsGreaterThan(capture);
        await Assert.That(checkpoint).IsGreaterThan(guardedTry);
        await Assert.That(prepareScene).IsGreaterThan(checkpoint);
        await Assert.That(source[prepareScene..]).Contains("snapshot.Dispose();");
    }

    [Test]
    [Arguments(3, 0.1, false)]
    [Arguments(7, 0.1, false)]
    [Arguments(11, 0.1, false)]
    [Arguments(15, 2.0, false)]
    [Arguments(3, 0.1, true)]
    [Arguments(7, 0.1, true)]
    [Arguments(11, 0.1, true)]
    [Arguments(15, 2.0, true)]
    public async Task ProjectiveVolumeRefusalDoesNotBlockCorrectedRendererPages(
        int element, double value, bool stored)
    {
        byte[] material = AdmissionVolumeMaterial();
        byte[] mesh = CreateMeshUpsert("/Volume", "/AdmissionVolume");
        using var device = new TextureGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1, [.. material, .. mesh], 2);
        renderer.ApplyPage(initial, useSceneMaterials: false);
        byte[] invalid = (byte[])mesh.Clone();
        BinaryPrimitives.WriteDoubleLittleEndian(invalid.AsSpan(80 + element * sizeof(double)), value);
        SilkCommandStorageTests.MemoryStorage? storage = null;
        using OpenUsdSilkPage bad = stored
            ? SilkCommandStorageTests.Stored(invalid, 1, 31, out storage)
            : new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 7, invalid, 1);
        InvalidDataException? error = await Assert.That(() => renderer.ApplyPage(bad, useSceneMaterials: false))
            .Throws<InvalidDataException>();
        await Assert.That(error!.Message).Contains("affine matrix");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        bad.Dispose();
        if (storage is not null)
        {
            await Assert.That(storage.Disposals).IsEqualTo(1);
        }
        using var repaired = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 8, mesh, 1);
        renderer.ApplyPage(repaired, useSceneMaterials: false);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(8UL);
    }

    [Test]
    public async Task RendererPhysicsRefusalAndLaterGpuFailureRestoreOverridesAndPermitCorrectedPages()
    {
        using var device = new TextureGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. AdmissionVolumeMaterial(), .. CreateMeshUpsert("/Volume", "/AdmissionVolume")], 2);
        renderer.ApplyPage(initial, useSceneMaterials: false);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Volume");
        var overrides = new SilkPhysicsTransformOverrides(1);
        renderer.PhysicsOverrides = overrides;
        PhysicsRenderOverrideView Pose(double x, ulong revision) => new(
            new[] { new PhysicsRenderTransformOverride(
                id, new UsdVec3d(x, 0, 0), PhysicsRenderOrientation.Identity, true) }, revision);
        _ = overrides.Refresh(renderer.Scene, bindings, Pose(1, 1));
        ulong overrideRevision = overrides.Revision;
        overrides.Stage(bindings, Pose(double.MaxValue, 2));
        using var bad = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2, [], 0);
        await Assert.That(() => renderer.ApplyPage(bad, useSceneMaterials: false))
            .Throws<InvalidDataException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(overrides.Revision).IsEqualTo(overrideRevision);

        overrides.Stage(bindings, Pose(2, 3));
        using var corrected = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 3,
            CreateMeshUpsert("/Added", "/AdmissionVolume", 8), 1);
        device.RefuseBufferSize = SilkSceneUniformWriter.ByteSize;
        await Assert.That(() => renderer.ApplyPage(corrected, useSceneMaterials: false))
            .Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
        await Assert.That(overrides.Revision).IsEqualTo(overrideRevision);
        await Assert.That(overrides.SourceRevision).IsEqualTo(1UL);
        await Assert.That(overrides.GetTransform(renderer.Scene.Meshes.Values.Single().Id)[12]).IsEqualTo(1.0);
        device.RefuseBufferSize = null;
        renderer.ApplyPage(corrected, useSceneMaterials: false);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3UL);
        await Assert.That(overrides.SourceRevision).IsEqualTo(3UL);
        await Assert.That(overrides.GetTransform(renderer.Scene.MeshesByPath[("/Volume", 0)].Id)[12])
            .IsEqualTo(2.0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DroppedOrSupersededInvalidVolumeOverridesDoNotRefuseAcceptedResults(bool duplicate)
    {
        using var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. AdmissionVolumeMaterial(), .. CreateMeshUpsert("/First", "/AdmissionVolume", 7),
             .. CreateMeshUpsert("/Second", "/AdmissionVolume", 8)], 3, 1);
        var first = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var second = new PhysicsRenderObjectId(102, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(2);
        _ = bindings.TryBind(first, "/First");
        _ = bindings.TryBind(second, "/Second");
        static PhysicsRenderTransformOverride Pose(PhysicsRenderObjectId id, double x) =>
            new(id, new UsdVec3d(x, 0, 0), PhysicsRenderOrientation.Identity, true);
        PhysicsRenderTransformOverride[] values = duplicate
            ? [Pose(first, double.MaxValue), Pose(first, 1)]
            : [Pose(first, 1), Pose(second, double.MaxValue)];
        var overrides = new SilkPhysicsTransformOverrides(1);
        _ = overrides.Refresh(scene, bindings, new PhysicsRenderOverrideView(values, 1));
        await Assert.That(overrides.Count).IsEqualTo(1);
        await Assert.That(overrides.GetTransform(7)[12]).IsEqualTo(1.0);
        await Assert.That(overrides.Contains(8)).IsFalse();
        await Assert.That(overrides.DroppedOverrides).IsEqualTo(duplicate ? 0L : 1L);
    }

    private static byte[] AdmissionVolumeMaterial() => CreateMaterialUpsert(
        "/AdmissionVolume", SilkSurfaceKind.VolumeDensity, scalars: [],
        textures: [new TextureSpec(
            SilkMaterialParameter.VolumeDensity, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp,
            SilkColorSpace.Raw, ComponentCount: 1,
            Scale: [1f, 1f, 1f, 1f], Bias: [0f, 0f, 0f, 0f],
            Fallback: [0f, 0f, 0f, 1f], Asset: "density.raw", UvPrimvar: "4,4,4")]);

    [Test]
    [Arguments(0, false)]
    [Arguments(3, false)]
    [Arguments(0, true)]
    public async Task VolumePhysicsRefusalPreservesOverrideStateAndAllowsRetryAndReversion(
        int instanceIndex, bool subtree)
    {
        const string path = "/World/PhysicsVolume";
        const string materialPath = "/World/PhysicsMaterial";
        byte[] material = CreateMaterialUpsert(
            materialPath, SilkSurfaceKind.VolumeDensity, scalars: [],
            textures: [new TextureSpec(
                SilkMaterialParameter.VolumeDensity, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp,
                SilkColorSpace.Raw, ComponentCount: 1,
                Scale: [1f, 1f, 1f, 1f], Bias: [0f, 0f, 0f, 0f],
                Fallback: [0f, 0f, 0f, 1f], Asset: "density.raw", UvPrimvar: "4,4,4")]);
        byte[] mesh = CreateMeshUpsert(path, materialPath);
        BinaryPrimitives.WriteInt32LittleEndian(mesh.AsSpan(24), instanceIndex);
        using var scene = new SilkSceneState();
        _ = scene.Apply([.. material, .. mesh], 2, 1);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        if (subtree)
        {
            _ = bindings.TryBindSubtree(id, "/World", default, PhysicsRenderOrientation.Identity);
        }
        else
        {
            _ = bindings.TryBind(id, path, instanceIndex);
        }
        PhysicsRenderOverrideView View(double x, ulong revision) => new(
            new[] { new PhysicsRenderTransformOverride(
                id, new UsdVec3d(x, 0, 0), PhysicsRenderOrientation.Identity, true) }, revision);
        var overrides = new SilkPhysicsTransformOverrides(1);
        _ = overrides.Refresh(scene, bindings, View(1, 1));
        double[] before = overrides.GetTransform(scene.Meshes.Values.Single().Id).ToArray();
        ulong beforeRevision = overrides.Revision;
        overrides.Stage(bindings, View(double.MaxValue, 2));
        await Assert.That(() => overrides.RefreshStaged(scene)).Throws<InvalidDataException>();
        await Assert.That(overrides.Revision).IsEqualTo(beforeRevision);
        await Assert.That(overrides.SourceRevision).IsEqualTo(1UL);
        await Assert.That(overrides.GetTransform(scene.Meshes.Values.Single().Id).ToArray())
            .IsEquivalentTo(before);
        overrides.Stage(bindings, View(2, 3));
        _ = overrides.RefreshStaged(scene);
        scene.ValidateVolumeTransforms(overrides);
        await Assert.That(overrides.GetTransform(scene.Meshes.Values.Single().Id)[12]).IsEqualTo(2.0);
        _ = overrides.Refresh(scene, bindings, new PhysicsRenderOverrideView(
            ReadOnlyMemory<PhysicsRenderTransformOverride>.Empty, 4));
        scene.ValidateVolumeTransforms(overrides);
        await Assert.That(overrides.Count).IsEqualTo(0);
        await Assert.That(scene.Meshes.Values.Single().Transform.Span[12]).IsEqualTo(0.0);
    }

    [Test]
    [Arguments(false, 0, 0.5)]
    [Arguments(false, 3, 0.5)]
    [Arguments(true, 0, 0.5)]
    [Arguments(true, 3, 0.5)]
    [Arguments(false, 0, 0.0)]
    [Arguments(true, 3, 0.0)]
    public async Task UnstableVolumeTransformsRefuseUpdatesAndMaterialChangesTransactionally(
        bool materialChange, int instanceIndex, double thickness)
    {
        const string path = "/World/UnstableVolume";
        const string materialPath = "/World/VolumeMaterial";
        byte[] Material(SilkSurfaceKind kind) => CreateMaterialUpsert(
            materialPath, kind, scalars: [], textures: kind == SilkSurfaceKind.VolumeDensity
                ? [new TextureSpec(
                    SilkMaterialParameter.VolumeDensity, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp,
                    SilkColorSpace.Raw, ComponentCount: 1,
                    Scale: [1f, 1f, 1f, 1f], Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f], Asset: "density.raw", UvPrimvar: "4,4,4")]
                : []);
        byte[] Mesh(bool unstable)
        {
            byte[] command = CreateMeshUpsert(path, materialPath);
            BinaryPrimitives.WriteInt32LittleEndian(command.AsSpan(24), instanceIndex);
            if (unstable)
            {
                // A nonzero world volume with x/y extent around one million and
                // thickness 0.5: its angular basis ratio is still below 1e-6.
                double[] basis = [1e6, 0, 0, 0, 1e6, thickness, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
                for (int i = 0; i < basis.Length; i++)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(command.AsSpan(80 + i * sizeof(double)), basis[i]);
                }
            }
            return command;
        }

        using var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. Material(materialChange ? SilkSurfaceKind.PreviewSurface : SilkSurfaceKind.VolumeDensity),
             .. Mesh(materialChange)], 2, 1);
        SilkMeshData retainedMesh = scene.Meshes.Values.Single();
        SilkMaterialData retainedMaterial = scene.Materials[materialPath];
        ulong geometryRevision = scene.GeometryRevision;
        ulong materialRevision = scene.MaterialRevision;
        InvalidDataException? refusal = await Assert.That(() => scene.Apply(
            materialChange ? Material(SilkSurfaceKind.VolumeDensity) : Mesh(true), 1, 2))
            .Throws<InvalidDataException>();
        await Assert.That(refusal!.Message).Contains(path);
        await Assert.That(refusal.Message).Contains($"instance {instanceIndex}");
        await Assert.That(refusal.Message).Contains(materialPath);
        await Assert.That(refusal.Message).Contains("bake the transform");
        await Assert.That(scene.Revision).IsEqualTo(1UL);
        await Assert.That(scene.GeometryRevision).IsEqualTo(geometryRevision);
        await Assert.That(scene.MaterialRevision).IsEqualTo(materialRevision);
        await Assert.That(scene.Meshes.Values.Single()).IsSameReferenceAs(retainedMesh);
        await Assert.That(scene.Materials[materialPath]).IsSameReferenceAs(retainedMaterial);
        await Assert.That(scene.MeshesByPath[(path, instanceIndex)]).IsSameReferenceAs(retainedMesh);

        using var empty = new SilkSceneState();
        await Assert.That(() => empty.Apply(
            [.. Mesh(true), .. Material(SilkSurfaceKind.VolumeDensity)], 2, 1))
            .Throws<InvalidDataException>();
        await Assert.That(empty.Meshes.Count).IsEqualTo(0);
        await Assert.That(empty.Materials.Count).IsEqualTo(0);
        await Assert.That(empty.Revision).IsEqualTo(0UL);

        byte[] repaired = Mesh(true);
        BinaryPrimitives.WriteDoubleLittleEndian(repaired.AsSpan(80 + 5 * sizeof(double)), 2.0);
        _ = scene.Apply([.. repaired, .. Material(SilkSurfaceKind.VolumeDensity)], 2, 3);
        await Assert.That(scene.Revision).IsEqualTo(3UL);
        await Assert.That(scene.Meshes.Values.Single().Transform.Span[5]).IsEqualTo(2.0);
    }

    [Test]
    [Arguments("513,4,4")]
    [Arguments("4,513,4")]
    [Arguments("4,4,513")]
    [Arguments("2048,1,1")]
    public async Task SampledVolumeResolutionIsWrittenAndEveryAxisLimitIsEnforced(string rejectedExtent)
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Volume",
            SilkSurfaceKind.VolumeDensity,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.VolumeDensity,
                    SilkTextureWrap.Clamp,
                    SilkTextureWrap.Clamp,
                    SilkColorSpace.Raw,
                    ComponentCount: 1,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "density.raw",
                    UvPrimvar: "4,4,96"),
            ]));

        byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(
            material,
            RenderHeadlight.Deterministic,
            constants,
            supportsVolumeTextures: true);
        await Assert.That(ReadSingle(constants, 136)).IsEqualTo(96f);

        SilkMaterialData tooDeep = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/TooDeep",
            SilkSurfaceKind.VolumeDensity,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.VolumeDensity,
                    SilkTextureWrap.Clamp,
                    SilkTextureWrap.Clamp,
                    SilkColorSpace.Raw,
                    ComponentCount: 1,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "density.raw",
                    UvPrimvar: rejectedExtent),
            ]));

        await Assert.That(() => SilkSurfaceUniformWriter.Write(
            tooDeep,
            RenderHeadlight.Deterministic,
            constants,
            supportsVolumeTextures: true)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ScalarTextureChannelsSwizzleIntoEveryComponentOfTheUpload()
    {
        // One multichannel image, four inputs, four different output channels. The
        // decoded texel must reach the shader as the selected channel replicated into
        // every component, which is what lets the shader read .r for any scalar map.
        byte[] pixel = [10, 20, 30, 40];
        (SilkMaterialParameter Parameter, SilkTextureChannel Channel, byte Expected)[] cases =
        [
            (SilkMaterialParameter.Roughness, SilkTextureChannel.R, (byte)10),
            (SilkMaterialParameter.Metallic, SilkTextureChannel.G, (byte)20),
            (SilkMaterialParameter.Occlusion, SilkTextureChannel.B, (byte)30),
            (SilkMaterialParameter.Opacity, SilkTextureChannel.A, (byte)40),
            (SilkMaterialParameter.Ior, SilkTextureChannel.R, (byte)10),
        ];
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Swizzle",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                .. cases.Select(entry =>
                    ScalarTexture(entry.Parameter, "packed.png", entry.Channel)),
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [.. pixel]));
        using var commands = new TextureCommandList();

        foreach ((SilkMaterialParameter parameter, _, byte expected) in cases)
        {
            resources.UploadMaterialTexture(commands, material, parameter);
            await Assert.That(commands.Uploads[^1][..4])
                .IsEquivalentTo(new byte[] { expected, expected, expected, expected });
        }

        // One asset, independently connected scalar inputs, one upload per input:
        // correctness over sharing a decode.
        await Assert.That(commands.UploadCount).IsEqualTo(5);
    }

    [Test]
    public async Task ColourTextureChannelKeepsEveryComponentOfTheUpload()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/Colour",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Raw,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "colour.png",
                    UvPrimvar: "st",
                    Channel: SilkTextureChannel.Rgb),
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [10, 20, 30, 40]));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // rgb is not a swizzle: the decoded texture retains its source alpha,
        // although Preview Surface opacity is driven independently.
        await Assert.That(commands.Uploads[0][..4])
            .IsEquivalentTo(new byte[] { 10, 20, 30, 40 });
    }

    [Test]
    public async Task ScalarTextureFallbackUsesTheSelectedChannelOfTheAuthoredFallback()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/FallbackChannel",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Metallic, "missing.png", SilkTextureChannel.B)
                    with
                    {
                        Fallback = [0f, 0f, 1f, 1f],
                    },
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => throw new FileNotFoundException("missing", asset));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.Metallic);

        // The authored fallback is a float4 read through the same output port, so the
        // blue component -- not the red one -- is what the shader must see.
        await Assert.That(commands.Uploads[0][..4])
            .IsEquivalentTo(new byte[] { 255, 255, 255, 255 });
    }

    [Test]
    public async Task RoughnessAndMetallicUploadAndBindTheirOwnSamplerAndTextureSlots()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/BoundRoughMetal",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.R)
                    with
                    {
                        WrapS = SilkTextureWrap.Repeat,
                        WrapT = SilkTextureWrap.Repeat,
                    },
                ScalarTexture(SilkMaterialParameter.Metallic, "metal.png", SilkTextureChannel.R)
                    with
                    {
                        WrapS = SilkTextureWrap.Clamp,
                        WrapT = SilkTextureWrap.Clamp,
                    },
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (asset, _) => new SilkDecodedImage(1, 1, [255, 255, 255, 255]));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.Roughness);
        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.Metallic);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Roughness);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Metallic);

        // Both textures were actually decoded/uploaded -- dropping either would leave
        // only one entry here.
        await Assert.That(commands.UploadCount).IsEqualTo(2);

        // Roughness keeps slots 11/4 and metallic takes its own 14/15, so neither can
        // overwrite the other's descriptor.
        await Assert.That(commands.SamplerBindings.Select(binding => binding.Binding))
            .IsEquivalentTo(new uint[] { 11, 14 });
        await Assert.That(commands.TextureBindings).IsEquivalentTo(new uint[] { 4, 15 });

        // Each sampler keeps its own authored wrap state rather than collapsing to a
        // single shared descriptor.
        await Assert.That(commands.SamplerBindings[0].Descriptor.AddressU)
            .IsEqualTo(SilkSamplerAddressMode.Repeat);
        await Assert.That(commands.SamplerBindings[1].Descriptor.AddressU)
            .IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
    }

    [Test]
    public async Task PackedSameFileRoughnessAndMetallicBindTwoIndependentTextures()
    {
        SilkMaterialData material = CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/PackedBound",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "orm.png", SilkTextureChannel.G),
                ScalarTexture(SilkMaterialParameter.Metallic, "orm.png", SilkTextureChannel.B),
            ]));
        using var device = new TextureGraphicsDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(1, 1, [10, 20, 30, 40]));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.Roughness);
        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.Metallic);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Roughness);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.Metallic);

        // One file, two channels: two swizzled uploads rather than one shared upload
        // the shader would have to unpack by convention.
        await Assert.That(commands.UploadCount).IsEqualTo(2);
        await Assert.That(commands.Uploads[0][..4])
            .IsEquivalentTo(new byte[] { 20, 20, 20, 20 });
        await Assert.That(commands.Uploads[1][..4])
            .IsEquivalentTo(new byte[] { 30, 30, 30, 30 });
        await Assert.That(commands.TextureBindings).IsEquivalentTo(new uint[] { 4, 15 });
    }

    [Test]
    public async Task MalformedTextureChannelsAreRejected()
    {
        byte[] unknownChannel = CreateMaterialUpsert(
            "/World/Materials/UnknownChannel",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.R),
            ]);
        // The channel is the last fixed field of the entry, immediately before the
        // asset bytes: 32 header + path + 80 - 4.
        int channelOffset = 32 + "/World/Materials/UnknownChannel".Length + 76;
        BinaryPrimitives.WriteUInt32LittleEndian(unknownChannel.AsSpan(channelOffset), 5);

        byte[] rgbOnScalar = CreateMaterialUpsert(
            "/World/Materials/RgbOnScalar",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.Rgb),
            ]);
        byte[] scalarOnColour = CreateMaterialUpsert(
            "/World/Materials/ScalarOnColour",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "colour.png",
                    UvPrimvar: "st",
                    Channel: SilkTextureChannel.G),
            ]);

        await Assert.That(() => CopyMaterial(unknownChannel)).Throws<InvalidDataException>();
        await Assert.That(() => CopyMaterial(rgbOnScalar)).Throws<InvalidDataException>();
        await Assert.That(() => CopyMaterial(scalarOnColour)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task PtexThirdUvRefusalPreservesStandardPrimaryAndSecondaryCoordinates()
    {
        TextureSpec ptex = new(
            SilkMaterialParameter.DiffuseColor,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: 3,
            Scale: [1f, 0.8f, 0.6f, 1f],
            Bias: [0.1f, 0.2f, 0.3f, 0f],
            Fallback: [0.25f, 0.5f, 0.75f, 1f],
            Asset: "shared.ptx",
            UvPrimvar: "",
            SourceKind: SilkTextureSourceKind.Ptex);
        TextureSpec roughness = ScalarTexture(
            SilkMaterialParameter.Roughness, "shared.ptx", SilkTextureChannel.R) with
        {
            UvPrimvar = "st",
            Scale = [0.75f, 0.75f, 0.75f, 1f],
            Bias = [0.125f, 0.125f, 0.125f, 0f],
        };
        TextureSpec normal = new(
            SilkMaterialParameter.Normal,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: 3,
            Scale: [1f, 1f, 1f, 1f],
            Bias: [0f, 0f, 0f, 0f],
            Fallback: [0.5f, 0.5f, 1f, 1f],
            Asset: "shared.ptx",
            UvPrimvar: "st1");
        float[] primaryTransform = [2f, 0f, 0f, 3f, -0.25f, 0.5f];
        float[] secondaryTransform = [0.5f, 0f, 0f, 0.25f, 0.125f, -0.5f];
        byte[] materialCommand = CreateMaterialUpsert(
            "/Material",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures: [ptex, roughness, normal],
            uvTransform: primaryTransform,
            alternateUvMask: 1u << (int)SilkMaterialParameter.Normal,
            alternateUvTransform: secondaryTransform);

        using var scene = new SilkSceneState();
        using var page = new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion,
            1,
            [.. materialCommand, .. CreateMeshUpsert("/Mesh", "/Material")],
            2);
        _ = scene.Apply(page);
        SilkMaterialData material = scene.Materials["/Material"];
        SilkMaterialTexture retainedPtex = material.GetTexture(SilkMaterialParameter.DiffuseColor)!;
        SilkMaterialTexture retainedRoughness = material.GetTexture(SilkMaterialParameter.Roughness)!;
        SilkMaterialTexture retainedNormal = material.GetTexture(SilkMaterialParameter.Normal)!;

        await Assert.That(scene.Revision).IsEqualTo(1ul);
        await Assert.That(scene.Meshes.Values.Single().MaterialPath).IsEqualTo("/Material");
        await Assert.That(retainedPtex.SourceKind).IsEqualTo(SilkTextureSourceKind.Ptex);
        await Assert.That(retainedPtex.Asset).IsEqualTo("shared.ptx");
        await Assert.That(material.GetPtexUnsupportedReason(retainedPtex))
            .IsEqualTo("Ptex sampling cannot use a third surface texture-coordinate set.");
        await Assert.That(material.GetRenderableSurfaceTexture(SilkMaterialParameter.DiffuseColor)).IsNotNull();
        await Assert.That(material.GetPrimaryUvPrimvar()).IsEqualTo("st");
        await Assert.That(material.GetSecondaryUvPrimvar()).IsEqualTo("st1");
        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.BaseColorMap |
            SilkShaderFeatures.RoughnessMetallicMap |
            SilkShaderFeatures.NormalMap);
        await Assert.That(material.GetSecondaryUvTextureMask()).IsEqualTo(SilkShaderFeatures.NormalMap);
        await Assert.That(material.TangentsFromSecondaryUv()).IsTrue();
        using var fallbackDevice = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var fallbackResources = new SilkSceneGpuResources(fallbackDevice,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        using var fallbackCommands = new TextureCommandList();
        fallbackResources.BindMaterialTexture(
            fallbackCommands, material, SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.DiffuseColor,
            scene.Meshes.Values.Single());
        float[] expectedFallback = new float[4];
        for (int component = 0; component < expectedFallback.Length; component++)
        {
            expectedFallback[component] =
                (retainedPtex.Fallback[component] * retainedPtex.Scale[component]) +
                retainedPtex.Bias[component];
        }
        byte[] fallbackBytes = fallbackCommands.Uploads.Single();
        float[] actualFallback = new float[4];
        for (int component = 0; component < actualFallback.Length; component++)
        {
            actualFallback[component] = BitConverter.ToSingle(fallbackBytes, component * sizeof(float));
        }
        await Assert.That(actualFallback.SequenceEqual(expectedFallback)).IsTrue();
        await Assert.That(material.GetUvTransformFor(SilkMaterialParameter.Roughness).SequenceEqual(primaryTransform))
            .IsTrue();
        await Assert.That(material.GetUvTransformFor(SilkMaterialParameter.Normal).SequenceEqual(secondaryTransform))
            .IsTrue();
        await Assert.That(retainedRoughness.Channel).IsEqualTo(SilkTextureChannel.R);
        await Assert.That(retainedRoughness.Scale.SequenceEqual(roughness.Scale)).IsTrue();
        await Assert.That(retainedRoughness.Bias.SequenceEqual(roughness.Bias)).IsTrue();
        await Assert.That(retainedNormal.Channel).IsEqualTo(SilkTextureChannel.Rgb);
        await Assert.That(retainedNormal.UvPrimvar).IsEqualTo("st1");
    }

    [Test]
    public async Task PtexOnlyMaterialRetainsItsSupportedSurfaceSourceWithoutClaimingOrdinaryUvNames()
    {
        TextureSpec ptex = new(
            SilkMaterialParameter.DiffuseColor,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: 3,
            Scale: [1f, 0.75f, 0.5f, 1f],
            Bias: [0.125f, 0.25f, 0.375f, 0f],
            Fallback: [0.2f, 0.4f, 0.6f, 1f],
            Asset: "faces.ptx",
            UvPrimvar: "",
            SourceKind: SilkTextureSourceKind.Ptex);
        using var scene = new SilkSceneState();
        using var page = new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion,
            1,
            [
                .. CreateMaterialUpsert(
                    "/Material/PtexOnly",
                    SilkSurfaceKind.PreviewSurface,
                    scalars: [],
                    textures: [ptex]),
                .. CreateMeshUpsert("/Mesh/PtexOnly", "/Material/PtexOnly"),
            ],
            2);
        _ = scene.Apply(page);
        SilkMaterialData material = scene.Materials["/Material/PtexOnly"];
        SilkMaterialTexture retained = material.GetTexture(SilkMaterialParameter.DiffuseColor)!;

        await Assert.That(scene.Revision).IsEqualTo(1ul);
        await Assert.That(scene.Meshes.Values.Single().MaterialPath).IsEqualTo("/Material/PtexOnly");
        await Assert.That(retained.SourceKind).IsEqualTo(SilkTextureSourceKind.Ptex);
        await Assert.That(retained.Asset).IsEqualTo("faces.ptx");
        await Assert.That(material.GetPtexUnsupportedReason(retained)).IsNull();
        await Assert.That(ReferenceEquals(
            material.GetRenderableSurfaceTexture(SilkMaterialParameter.DiffuseColor), retained)).IsTrue();
        await Assert.That(material.GetRenderableTexture(SilkMaterialParameter.DiffuseColor)).IsNull();
        await Assert.That(material.GetPrimaryUvPrimvar()).IsEqualTo(string.Empty);
        await Assert.That(material.GetSecondaryUvPrimvar()).IsEqualTo(string.Empty);
        await Assert.That(material.PtexUsesSecondaryUv).IsFalse();
        await Assert.That(material.GetTextureFeatures()).IsEqualTo(
            SilkShaderFeatures.BaseColorMap | SilkShaderFeatures.Uv);
        await Assert.That(retained.Scale.SequenceEqual(ptex.Scale)).IsTrue();
        await Assert.That(retained.Bias.SequenceEqual(ptex.Bias)).IsTrue();
    }

    [Test]
    [Arguments(SilkPtexMappingStatus.NotRequested)]
    [Arguments(SilkPtexMappingStatus.TextureFaceOutOfRange)]
    public async Task UnmappedPtexSamplingBindsAuthoredFallbackAndDeduplicatesContextualDiagnostics(
        SilkPtexMappingStatus status)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(status);
        _ = scene.Apply(page);
        SilkMaterialData material = scene.Materials["/Material/PtexFallback"];
        SilkMeshData mesh = scene.Meshes.Values.Single();
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        int imageDecodes = 0;
        using var resources = new SilkSceneGpuResources(device, (_, _) =>
        {
            imageDecodes++;
            throw new InvalidOperationException("Ptex must not enter the ordinary image decoder.");
        });
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor,
            SilkMaterialParameter.DiffuseColor, mesh);
        resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor,
            SilkMaterialParameter.DiffuseColor, mesh);

        await Assert.That(imageDecodes).IsEqualTo(0);
        await Assert.That(commands.UploadCount).IsEqualTo(1);
        await Assert.That(commands.Uploads.Single().SequenceEqual(new byte[] { 255, 0, 255, 255 })).IsTrue();
        await Assert.That(device.CreatedTextures.Single().Width).IsEqualTo(1u);
        await Assert.That(device.CreatedTextures.Single().Height).IsEqualTo(1u);
        await Assert.That(commands.TextureBindings.Count).IsEqualTo(4);
        RenderDiagnostic diagnostic = resources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported);
        await Assert.That(diagnostic.Message).Contains("/Material/PtexFallback")
            .And.Contains("/Mesh/PtexFallback").And.Contains("unmapped.ptx")
            .And.Contains("DiffuseColor").And.Contains(status.ToString());
    }

    [Test]
    public async Task PtexFallbackKeepsConstantDisplacementThroughRemovalAndRestoration()
    {
        using var scene = new SilkSceneState();
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex fallback must not decode an ordinary image."));
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(
            SilkPtexMappingStatus.NotRequested, displacement: 0.5f);
        SilkSceneDelta initial = scene.Apply(page);
        resources.Apply(scene, initial);

        await assertVertices(-0.5f);
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.DisplacementApplied)).IsTrue();

        byte[] undisplacedMaterial = CreatePtexFallbackMaterial("unmapped.ptx", displacement: 0);
        resources.Apply(scene, scene.Apply(undisplacedMaterial, 1, 2));
        await assertVertices(0f);

        byte[] restoredMaterial = CreatePtexFallbackMaterial("unmapped.ptx", displacement: 0.5f);
        resources.Apply(scene, scene.Apply(restoredMaterial, 1, 3));
        await assertVertices(-0.5f);
        await Assert.That(resources.Diagnostics.Entries.Count(
            entry => entry.Code == SilkRenderDiagnosticCodes.DisplacementApplied)).IsEqualTo(1);

        async Task assertVertices(float expectedZ)
        {
            SilkMeshGpuResource mesh = resources.Meshes.Values.Single();
            byte[] data = new byte[checked((int)mesh.VertexBuffer.Size)];
            mesh.VertexBuffer.ReadbackForTesting(data);
            for (int corner = 0; corner < 3; corner++)
            {
                int offset = corner * 8 * sizeof(float);
                await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8)))
                    .IsEqualTo(expectedZ);
                await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 24)))
                    .IsEqualTo(0.5f);
                await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 28)))
                    .IsEqualTo(0.5f);
            }
        }
    }

    [Test]
    public async Task ApplyingPtexMaterialRetainsFallbackDiagnosticWithoutTextureAdmission()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested);
        SilkSceneDelta delta = scene.Apply(page);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));

        resources.Apply(scene, delta);

        RenderDiagnostic diagnostic = resources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported);
        await Assert.That(diagnostic.Message).Contains("/Material/PtexFallback")
            .And.Contains("/Mesh/PtexFallback").And.Contains("unmapped.ptx");
    }

    [Test]
    public async Task FailedPtexMeshPreparationRollsBackItsTextureAndDiagnostic()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested);
        SilkSceneDelta delta = scene.Apply(page);
        using var device = new TextureGraphicsDevice
        {
            MaxTextureDimension2D = 4096,
            RefuseBufferSize = 80,
        };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));

        await Assert.That(() => resources.Apply(scene, delta)).Throws<InvalidOperationException>();

        await Assert.That(resources.MeshValues).IsEmpty();
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsFalse();
        await Assert.That(device.TextureOwners.All(static texture => texture.Released)).IsTrue();
    }

    [Test]
    public async Task ParameterlessPtexRetryRebuildsGeometryOnTheNextSceneApply()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested);
        SilkSceneDelta delta = scene.Apply(page);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        resources.Apply(scene, delta);
        SilkMeshGpuResource previous = resources.MeshValues.Single();
        await Assert.That(previous.Geometry.Key.PtexSamplingIdentity).IsNotEqualTo(0ul);

        resources.RetryFailedTextures();
        await Assert.That(resources.TextureRetryRevision).IsEqualTo(1ul);
        resources.SynchronizePtexRetries(scene);

        SilkMeshGpuResource firstRetry = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(firstRetry, previous)).IsFalse();
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();

        resources.RetryFailedTextures();
        resources.SynchronizePtexRetries(scene);
        await Assert.That(resources.TextureRetryRevision).IsEqualTo(2ul);
        await Assert.That(ReferenceEquals(resources.MeshValues.Single(), firstRetry)).IsFalse();
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
    }

    [Test]
    public async Task SceneAwarePtexRetryRebuildsGeometryAndRestoresUnsupportedDiagnostic()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested);
        SilkSceneDelta delta = scene.Apply(page);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        resources.Apply(scene, delta);
        SilkMeshGpuResource previous = resources.MeshValues.Single();

        resources.RetryFailedTextures(scene);

        await Assert.That(ReferenceEquals(resources.MeshValues.Single(), previous)).IsFalse();
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
    }

    [Test]
    public async Task FailedSceneAwarePtexRetryRestoresTextureGeometryAndDiagnostic()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested);
        SilkSceneDelta delta = scene.Apply(page);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        resources.Apply(scene, delta);
        SilkMeshGpuResource previous = resources.MeshValues.Single();
        Texture retainedFallback = device.TextureOwners.Single();
        ulong revision = resources.Revision;
        ulong retryRevision = resources.TextureRetryRevision;
        device.RefuseBufferSize = 80;

        await Assert.That(() => resources.RetryFailedTextures(scene)).Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(resources.MeshValues.Single(), previous)).IsTrue();
        await Assert.That(resources.Revision).IsEqualTo(revision);
        await Assert.That(resources.TextureRetryRevision).IsEqualTo(retryRevision);
        await Assert.That(retainedFallback.Released).IsFalse();
        await Assert.That(device.TextureOwners.Skip(1).All(static texture => texture.Released)).IsTrue();
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
    }

    [Test]
    public async Task ReplacingPtexSourceRebuildsGeometryAndRetainsTheNewDiagnostic()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage first = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested, "before.ptx");
        using OpenUsdSilkPage replacement = CreateUnmappedPtexPage(
            SilkPtexMappingStatus.NotRequested, "after.ptx", revision: 2);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        resources.Apply(scene, scene.Apply(first));
        SilkMeshGpuResource previous = resources.MeshValues.Single();

        resources.Apply(scene, scene.Apply(replacement));

        SilkMeshGpuResource current = resources.MeshValues.Single();
        RenderDiagnostic diagnostic = resources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported);
        await Assert.That(ReferenceEquals(current, previous)).IsFalse();
        await Assert.That(current.Geometry.Key.PtexSamplingIdentity)
            .IsNotEqualTo(previous.Geometry.Key.PtexSamplingIdentity);
        await Assert.That(diagnostic.Message).Contains("after.ptx").And.DoesNotContain("before.ptx");
    }

    [Test]
    public async Task PtexCornerMappingChangesAndRestorationRebuildRetainedGeometry()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage first = CreateUnmappedPtexPage(
            SilkPtexMappingStatus.Mapped, revision: 1, faceIndex: 0);
        using OpenUsdSilkPage replacement = CreateUnmappedPtexPage(
            SilkPtexMappingStatus.Mapped, revision: 2, faceIndex: 1,
            coordinates: [0.1f, 0.2f, 0.6f, 0.8f, 0.9f, 0.3f]);
        using OpenUsdSilkPage restored = CreateUnmappedPtexPage(
            SilkPtexMappingStatus.Mapped, revision: 3, faceIndex: 0);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));

        resources.Apply(scene, scene.Apply(first));
        SilkMeshGpuResource original = resources.MeshValues.Single();
        resources.Apply(scene, scene.Apply(replacement));
        SilkMeshGpuResource changed = resources.MeshValues.Single();
        resources.Apply(scene, scene.Apply(restored));
        SilkMeshGpuResource current = resources.MeshValues.Single();

        await Assert.That(ReferenceEquals(changed, original)).IsFalse();
        await Assert.That(changed.Geometry.Key.PtexSamplingIdentity)
            .IsNotEqualTo(original.Geometry.Key.PtexSamplingIdentity);
        await Assert.That(ReferenceEquals(current, changed)).IsFalse();
        await Assert.That(current.Geometry.Key.PtexSamplingIdentity)
            .IsEqualTo(original.Geometry.Key.PtexSamplingIdentity);
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsTrue();
    }

    [Test]
    public async Task NormalCorrespondenceChangesAndRestorationRebuildGeometryButMetadataDoesNot()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage first = CreateMeshPage(
            CreateMeshUpsert("/Mesh/NormalCorrespondence", ""), 1);
        using OpenUsdSilkPage polygonNormals = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalCorrespondence", ""),
                [0, 1, 2], [0, 1, 2]), 2);
        using OpenUsdSilkPage changedIdentity = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalCorrespondence", ""),
                [0, 0, 2], [0, 1, 2]), 3);
        using OpenUsdSilkPage changedCorners = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalCorrespondence", ""),
                [0, 0, 2], [0, 2, 1]), 4);
        using OpenUsdSilkPage changedExtraPoint = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalCorrespondence", ""),
                [0, 0, 2], [0, 1, 3], [0.25f, 0.25f, 0]), 5);
        byte[] restoredMesh = CreateMeshUpsert("/Mesh/NormalCorrespondence", "");
        using OpenUsdSilkPage restored = CreateMeshPage(restoredMesh, 6);
        byte[] metadataMesh = CreateMeshUpsert("/Mesh/NormalCorrespondence", "");
        BinaryPrimitives.WriteDoubleLittleEndian(metadataMesh.AsSpan(80), 2);
        using OpenUsdSilkPage metadataOnly = CreateMeshPage(metadataMesh, 7);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("No image texture is expected."));

        resources.Apply(scene, scene.Apply(first));
        SilkMeshGpuResource previous = resources.MeshValues.Single();
        resources.Apply(scene, scene.Apply(polygonNormals));
        SilkMeshGpuResource current = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(current, previous)).IsFalse();

        previous = current;
        resources.Apply(scene, scene.Apply(changedIdentity));
        current = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(current, previous)).IsFalse();

        previous = current;
        resources.Apply(scene, scene.Apply(changedCorners));
        current = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(current, previous)).IsFalse();

        previous = current;
        resources.Apply(scene, scene.Apply(changedExtraPoint));
        current = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(current, previous)).IsFalse();

        previous = current;
        resources.Apply(scene, scene.Apply(restored));
        current = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(current, previous)).IsFalse();

        resources.Apply(scene, scene.Apply(metadataOnly));
        await Assert.That(ReferenceEquals(resources.MeshValues.Single(), current)).IsTrue();
        await Assert.That(resources.MeshValues.Single().Mesh.Transform.Span[0]).IsEqualTo(2d);
    }

    [Test]
    public async Task NormalFallbackModeChangesAndRestorationRebuildGeometry()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage polygonNormals = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalMode", ""), [0, 1, 2], [0, 1, 2]), 1);
        using OpenUsdSilkPage triangleNormals = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalMode", ""), [0, 1, 2], [0, 1, 2],
                triangleLocal: true), 2);
        using OpenUsdSilkPage restored = CreateMeshPage(
            WithNormalCorrespondence(
                CreateMeshUpsert("/Mesh/NormalMode", ""), [0, 1, 2], [0, 1, 2]), 3);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("No image texture is expected."));

        resources.Apply(scene, scene.Apply(polygonNormals));
        SilkMeshGpuResource original = resources.MeshValues.Single();
        await Assert.That(original.Geometry.DerivedCornerVertices).IsFalse();

        resources.Apply(scene, scene.Apply(triangleNormals));
        SilkMeshGpuResource changed = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(changed, original)).IsFalse();
        await Assert.That(changed.Geometry.DerivedCornerVertices).IsTrue();

        resources.Apply(scene, scene.Apply(restored));
        SilkMeshGpuResource current = resources.MeshValues.Single();
        await Assert.That(ReferenceEquals(current, changed)).IsFalse();
        await Assert.That(current.Geometry.DerivedCornerVertices).IsFalse();
    }

    [Test]
    public async Task TextureRetryDoesNotRebuildAnUntexturedMeshOnMetadataUpdate()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage first = CreateMeshPage(
            CreateMeshUpsert("/Mesh/Untextured", ""), 1);
        byte[] updatedMesh = CreateMeshUpsert("/Mesh/Untextured", "");
        BinaryPrimitives.WriteDoubleLittleEndian(updatedMesh.AsSpan(80), 2);
        using OpenUsdSilkPage updated = CreateMeshPage(updatedMesh, 2);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("No image texture is expected."));
        resources.Apply(scene, scene.Apply(first));
        SilkMeshGpuResource original = resources.MeshValues.Single();

        resources.RetryFailedTextures();
        resources.Apply(scene, scene.Apply(updated));

        await Assert.That(ReferenceEquals(resources.MeshValues.Single(), original)).IsTrue();
        await Assert.That(original.Mesh.Transform.Span[0]).IsEqualTo(2d);
    }

    [Test]
    [Arguments(SilkMaterialParameter.Normal)]
    [Arguments(SilkMaterialParameter.Opacity)]
    public async Task UnsupportedPtexInputSamplesItsNonDefaultAuthoredFallback(
        SilkMaterialParameter parameter)
    {
        TextureSpec expected = new(
            parameter,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: parameter == SilkMaterialParameter.Normal ? 3 : 4,
            Scale: [1, 1, 1, 1],
            Bias: [0, 0, 0, 0],
            Fallback: [0.11f, 0.23f, 0.47f, 0.71f],
            Asset: $"unsupported-{parameter}.ptx",
            UvPrimvar: "",
            SourceKind: SilkTextureSourceKind.Ptex);
        using var scene = new SilkSceneState();
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. CreateMaterialUpsert("/Material/UnsupportedPtex", SilkSurfaceKind.PreviewSurface, [], [expected]),
             .. CreateMeshUpsert("/Mesh/UnsupportedPtex", "/Material/UnsupportedPtex")], 2);
        _ = scene.Apply(page);
        SilkMaterialData material = scene.Materials["/Material/UnsupportedPtex"];
        SilkMeshData mesh = scene.Meshes.Values.Single();
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, expected.Parameter, expected.Parameter, mesh);

        SilkShaderFeatures expectedFeature = parameter == SilkMaterialParameter.Normal
            ? SilkShaderFeatures.NormalMap : SilkShaderFeatures.OpacityMap;
        await Assert.That(material.GetRenderableSurfaceTexture(parameter)).IsNotNull();
        await Assert.That(material.GetTextureFeatures().HasFlag(expectedFeature)).IsTrue();
        await Assert.That(commands.Uploads.Single().SequenceEqual(new byte[] { 28, 59, 120, 181 })).IsTrue();
        await Assert.That(resources.Diagnostics.Entries.Any(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported &&
            entry.Message.Contains($"unsupported-{parameter}.ptx", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SecondPtexInputUsesItsOwnAuthoredFallbackInsteadOfFirstAtlas()
    {
        TextureSpec first = new(
            SilkMaterialParameter.DiffuseColor,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: 4,
            Scale: [1, 1, 1, 1],
            Bias: [0, 0, 0, 0],
            Fallback: [0.8f, 0.2f, 0.1f, 1],
            Asset: "first.ptx",
            UvPrimvar: "",
            SourceKind: SilkTextureSourceKind.Ptex);
        TextureSpec second = new(
            SilkMaterialParameter.Roughness,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: 1,
            Scale: [1, 1, 1, 1],
            Bias: [0, 0, 0, 0],
            Fallback: [0.17f, 0.34f, 0.51f, 0.68f],
            Asset: "second.ptx",
            UvPrimvar: "",
            SourceKind: SilkTextureSourceKind.Ptex);
        using var scene = new SilkSceneState();
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
            [.. CreateMaterialUpsert("/Material/MultiplePtex", SilkSurfaceKind.PreviewSurface, [],
                  [first, second]),
             .. CreateMeshUpsert("/Mesh/MultiplePtex", "/Material/MultiplePtex")], 2);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMaterialData material = scene.Materials["/Material/MultiplePtex"];
        SilkMaterialTexture retainedSecond = material.GetTexture(second.Parameter)!;
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        using var commands = new TextureCommandList();

        resources.BindMaterialTexture(commands, material, second.Parameter, second.Parameter, mesh);

        await Assert.That(material.GetPtexUnsupportedReason(retainedSecond))
            .Contains("only one Ptex input");
        await Assert.That(material.GetTextureFeatures().HasFlag(SilkShaderFeatures.RoughnessMetallicMap))
            .IsTrue();
        await Assert.That(commands.Uploads.Single().SequenceEqual(new byte[] { 43, 43, 43, 43 })).IsTrue();
        await Assert.That(resources.Diagnostics.Entries.Any(entry =>
            entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported &&
            entry.Message.Contains("second.ptx", StringComparison.Ordinal) &&
            entry.Message.Contains("only one Ptex input", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments(3ul, 4ul, false)]
    [Arguments(4ul, 3ul, false)]
    [Arguments(4ul, 4ul, true)]
    public async Task PtexFallbackAdmissionIsBoundedAndRetryDisposesCachedResources(
        ulong cpuMaximum, ulong gpuMaximum, bool admitted)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.NotRequested);
        _ = scene.Apply(page);
        SilkMaterialData material = scene.Materials["/Material/PtexFallback"];
        SilkMeshData mesh = scene.Meshes.Values.Single();
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        var cpu = new SilkCpuTextureBudget(cpuMaximum);
        var gpu = new SilkGpuTextureBudget(gpuMaximum);
        cpu.ConfigureDevice(device);
        gpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."),
            cpuTextureBudget: cpu,
            ownedImageDecoder: (_, _, _) =>
                throw new InvalidOperationException("Ptex fallback must not decode an ordinary image."));
        using var commands = new TextureCommandList();

        void bind() => resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor,
            SilkMaterialParameter.DiffuseColor, mesh);
        if (!admitted)
        {
            if (cpuMaximum < 4)
            {
                await Assert.That(bind).Throws<SilkCpuTextureBudgetExceededException>();
            }
            else
            {
                await Assert.That(bind).Throws<SilkGpuTextureBudgetExceededException>();
            }
            await Assert.That(commands.UploadCount).IsEqualTo(0);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(0ul);
            return;
        }

        bind();
        Texture first = device.TextureOwners.Single();
        await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(4ul);
        await Assert.That(commands.Uploads.Single().SequenceEqual(new byte[] { 255, 0, 255, 255 })).IsTrue();
        resources.RetryFailedTextures();
        await Assert.That(first.Released).IsTrue();
        await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(resources.Diagnostics.Entries.Any(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsFalse();

        bind();
        await Assert.That(device.TextureOwners.Count).IsEqualTo(2);
        await Assert.That(commands.UploadCount).IsEqualTo(2);
        await Assert.That(resources.Diagnostics.Entries.Count(
            entry => entry.Code == SilkRenderDiagnosticCodes.TexturePtexUnsupported)).IsEqualTo(1);
        resources.Dispose();
        await Assert.That(device.TextureOwners.All(static texture => texture.Released)).IsTrue();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(cpu.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(gpu.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProductionGeometryMapsPtexCornersWithoutChangingTheOrdinaryUvSet(bool ptexSecondary)
    {
        const uint face = 16777217;
        byte[] meshCommand = CreateMeshUpsert("/MappedGeometry", "");
        using var stream = new MemoryStream();
        stream.Write(meshCommand);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)SilkAttributeSemantic.TexCoord);
            writer.Write(2u);
            writer.Write((uint)SilkAttributeInterpolation.Vertex);
            writer.Write(2u);
            writer.Write(3u);
            writer.Write(Encoding.UTF8.GetBytes("st"));
            foreach (float component in new float[] { 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f })
            {
                writer.Write(component);
            }
            writer.Write(face);
            foreach (float component in new float[] { 0, 1, 0.25f, 0.5f, 1, 0 })
            {
                writer.Write(component);
            }
        }
        byte[] command = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(4), checked((uint)command.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(220), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(268), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(272), (uint)SilkPtexMappingStatus.Mapped);
        using var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1, command, 1);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var tile = new SilkPtexAtlasTile(face, 1, 2, 4, 4, 0.125f, 0.25f, 0.625f, 0.75f,
            -1, -1, -1, -1, 0, 0, 0, 0);
        var layout = new SilkPtexAtlasLayout(8, 8, new Dictionary<uint, SilkPtexAtlasTile> { [face] = tile });

        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh,
            ptexSecondary ? "st" : "",
            requireTextureCoordinates: true,
            secondaryUvPrimvar: ptexSecondary ? "" : "st",
            ptexAtlas: layout,
            ptexAsSecondary: ptexSecondary);

        await Assert.That(geometry.VertexLayout.Stride).IsEqualTo(40u);
        await Assert.That(geometry.VertexCount).IsEqualTo(3u);
        await Assert.That(geometry.Indices.SequenceEqual(new uint[] { 0, 1, 2 })).IsTrue();
        await Assert.That(geometry.DerivedCornerVertices).IsTrue();
        await Assert.That(geometry.PtexUvSecondary).IsEqualTo(ptexSecondary);
        int ptexOffset = ptexSecondary ? 8 : 6;
        int uvOffset = ptexSecondary ? 6 : 8;
        float[] expectedPtex = [0.125f, 0.75f, 0.25f, 0.5f, 0.625f, 0.25f];
        float[] expectedUv = [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f];
        for (int corner = 0; corner < 3; corner++)
        {
            await Assert.That(geometry.Vertices[(corner * 10) + ptexOffset])
                .IsEqualTo(expectedPtex[corner * 2]);
            await Assert.That(geometry.Vertices[(corner * 10) + ptexOffset + 1])
                .IsEqualTo(expectedPtex[(corner * 2) + 1]);
            await Assert.That(geometry.Vertices[(corner * 10) + uvOffset])
                .IsEqualTo(expectedUv[corner * 2]);
            await Assert.That(geometry.Vertices[(corner * 10) + uvOffset + 1])
                .IsEqualTo(expectedUv[(corner * 2) + 1]);
            await Assert.That(geometry.Vertices.AsSpan(corner * 10, 3)
                .SequenceEqual(mesh.Points.Span.Slice(corner * 3, 3))).IsTrue();
        }
    }

    [Test]
    [Arguments(252ul, 143ul, false)]
    [Arguments(251ul, 144ul, false)]
    [Arguments(252ul, 144ul, true)]
    public async Task MappedPtexAtlasAdmissionRollsBackAtCpuAndGpuBoundaries(
        ulong cpuMaximum, ulong gpuMaximum, bool admitted)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with the matching native Ptex runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        string asset = Path.Combine(directory?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the OpenUsd repository root."),
            "test-assets", "native-profile", "ptex", "quad-colors.ptx");
        using var page = CreateUnmappedPtexPage(SilkPtexMappingStatus.Mapped, asset);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 4096 };
        var gpu = new SilkGpuTextureBudget(gpuMaximum);
        // Eight bytes retained selection + 196 encoded fixture bytes + 48
        // decoded RGB bytes coexist at the decode peak. The final 6x6 RGBA
        // atlas is 144 bytes and is retained independently on CPU and GPU.
        var cpu = new SilkCpuTextureBudget(cpuMaximum);
        gpu.ConfigureDevice(device);
        cpu.ConfigureDevice(device);
        using var resources = new SilkSceneGpuResources(device,
            (_, _) => throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."),
            cpuTextureBudget: cpu,
            ownedImageDecoder: (_, _, _) =>
                throw new InvalidOperationException("Ptex cannot use the ordinary image decoder."));
        using var commands = new TextureCommandList();
        RenderDiagnosticsState expectedDiagnostics = resources.Diagnostics;
        await Assert.That(expectedDiagnostics.Entries.Select(static entry => entry.Code).SequenceEqual(
            ["HDSILK_CPU_TEXTURE_ADMISSION", "HDSILK_GPU_TEXTURE_ADMISSION"])).IsTrue();
        await Assert.That(expectedDiagnostics.Entries.All(
            static entry => entry.Severity == RenderDiagnosticSeverity.Information)).IsTrue();
        SilkMaterialData material = scene.Materials["/Material/PtexFallback"];
        SilkMeshData mesh = scene.Meshes.Values.Single();
        void bind() => resources.BindMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor,
            SilkMaterialParameter.DiffuseColor, mesh);

        if (!admitted)
        {
            if (cpuMaximum < 252)
            {
                await Assert.That(bind).Throws<SilkCpuTextureBudgetExceededException>();
            }
            else
            {
                await Assert.That(bind).Throws<SilkGpuTextureBudgetExceededException>();
            }
            await Assert.That(commands.UploadCount).IsEqualTo(0);
            await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(resources.Diagnostics).IsEqualTo(expectedDiagnostics);
            return;
        }
        bind();
        bind();
        SilkTextureDescriptor descriptor = device.CreatedTextures.Single();
        await Assert.That(descriptor.Width).IsEqualTo(6u);
        await Assert.That(descriptor.Height).IsEqualTo(6u);
        await Assert.That(descriptor.MipLevelCount).IsEqualTo(1u);
        await Assert.That(commands.UploadCount).IsEqualTo(1);
        await Assert.That(commands.Uploads.Single().Length).IsEqualTo(144);
        await Assert.That(commands.Uploads.Single().AsSpan(((2 * 6) + 2) * 4, 4)
            .SequenceEqual(new byte[] { 255, 255, 255, 255 })).IsTrue();
        await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(144ul);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(144ul);
        await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(252ul);
        await Assert.That(resources.Diagnostics).IsEqualTo(expectedDiagnostics);
        resources.Dispose();
        await Assert.That(device.TextureOwners.Single().Released).IsTrue();
        await Assert.That(gpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(gpu.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(cpu.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexSlotLookupMapsIndependentLayoutsWithoutChangingOrdinaryGeometry()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(
            SilkPtexMappingStatus.Mapped, faceIndex: 7);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry before = SilkMeshGeometryBuilder.Build(mesh);
        var budget = new SilkCpuTextureBudget(1000);
        SilkPtexSlotLayout[] slots =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, CreateLookupLayout(0, 0, 0.5f, 0.5f)),
            new(SilkMaterialParameter.Roughness, 2, CreateLookupLayout(0.5f, 0.25f, 1, 0.5f)),
        ];
        using SilkPtexSlotLookup lookup = SilkPtexSlotLookup.Prepare(mesh, slots, budget);

        await Assert.That(lookup.FaceCount).IsEqualTo(1);
        await Assert.That(lookup.SlotCount).IsEqualTo(2);
        using (SilkPtexSlotLookup.Lease lease = lookup.AcquireLease())
        {
            uint[] cornerFaces = new uint[3];
            lease.CopyCornerFaces(cornerFaces);
            await Assert.That(cornerFaces.AsSpan().SequenceEqual(new uint[] { 0, 0, 0 })).IsTrue();
        }
        for (int corner = 0; corner < 3; corner++)
        {
            float u = mesh.PtexCoordinates[corner * 2];
            float v = mesh.PtexCoordinates[corner * 2 + 1];
            await Assert.That(lookup.MapCorner(0, corner)).IsEqualTo((u * 0.5f, v * 0.5f));
            await Assert.That(lookup.MapCorner(1, corner)).IsEqualTo((0.5f + u * 0.5f, 0.25f + v * 0.25f));
        }
        SilkMeshGeometry after = SilkMeshGeometryBuilder.Build(mesh);
        await Assert.That(after.Vertices.AsSpan().SequenceEqual(before.Vertices)).IsTrue();
        await Assert.That(after.Indices.AsSpan().SequenceEqual(before.Indices)).IsTrue();
        await Assert.That(lookup.HasSameSources(slots)).IsTrue();
        lookup.Dispose();
        lookup.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(() => lookup.MapCorner(0, 0)).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(119ul, false)]
    [Arguments(120ul, true)]
    public async Task CandidatePtexSlotLookupAdmitsCompletePayloadBeforeAllocation(
        ulong limit, bool accepted)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.Mapped, faceIndex: 7);
        _ = scene.Apply(page);
        var budget = new SilkCpuTextureBudget(limit);
        SilkPtexSlotLayout[] slots =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, CreateLookupLayout(0, 0, 1, 1)),
            new(SilkMaterialParameter.Roughness, 2, CreateLookupLayout(0, 0, 1, 1)),
        ];
        if (accepted)
        {
            using SilkPtexSlotLookup lookup = SilkPtexSlotLookup.Prepare(
                scene.Meshes.Values.Single(), slots, budget);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(120ul);
        }
        else
        {
            await Assert.That(() => SilkPtexSlotLookup.Prepare(
                scene.Meshes.Values.Single(), slots, budget)).Throws<SilkCpuTextureBudgetExceededException>();
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexSlotLookupFailedReplacementPreservesPreviousOwnerAndRetryCanRecover()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.Mapped, faceIndex: 7);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var budget = new SilkCpuTextureBudget(1000);
        SilkPtexSlotLayout[] original =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, CreateLookupLayout(0, 0, 1, 1)),
            new(SilkMaterialParameter.Roughness, 2, SilkPtexAtlasLayout.Fallback),
        ];
        using SilkPtexSlotLookup previous = SilkPtexSlotLookup.Prepare(mesh, original, budget);
        SilkPtexSlotLayout[] broken =
        [
            original[0],
            new(SilkMaterialParameter.Roughness, 3,
                new SilkPtexAtlasLayout(1, 1, new Dictionary<uint, SilkPtexAtlasTile>())),
        ];
        await Assert.That(() => SilkPtexSlotLookup.Prepare(mesh, broken, budget))
            .Throws<InvalidDataException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(120ul);
        await Assert.That(previous.MapCorner(1, 0)).IsEqualTo((0.5f, 0.5f));
        await Assert.That(previous.HasSameSources(broken)).IsFalse();
        SilkPtexSlotLayout[] repaired =
        [
            original[0],
            new(SilkMaterialParameter.Roughness, 4, CreateLookupLayout(0.5f, 0, 1, 1)),
        ];
        using SilkPtexSlotLookup replacement = SilkPtexSlotLookup.Prepare(mesh, repaired, budget);
        await Assert.That(replacement.MapCorner(1, 0)).IsEqualTo((0.625f, 0.25f));
        await Assert.That(previous.MapCorner(1, 0)).IsEqualTo((0.5f, 0.5f));
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(240ul);
        previous.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(120ul);
    }

    [Test]
    public async Task CandidatePtexLookupLeaseRetainsAdmissionUntilLastConsumerReleases()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateUnmappedPtexPage(SilkPtexMappingStatus.Mapped, faceIndex: 7);
        _ = scene.Apply(page);
        var budget = new SilkCpuTextureBudget(120);
        SilkPtexSlotLayout[] slots =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, CreateLookupLayout(0, 0, 1, 1)),
            new(SilkMaterialParameter.Roughness, 2, CreateLookupLayout(0.5f, 0, 1, 1)),
        ];
        using SilkPtexSlotLookup lookup = SilkPtexSlotLookup.Prepare(
            scene.Meshes.Values.Single(), slots, budget);
        using SilkPtexSlotLookup.Lease first = lookup.AcquireLease();
        using SilkPtexSlotLookup.Lease second = lookup.AcquireLease();
        lookup.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(120ul);
        await Assert.That(() => lookup.AcquireLease()).Throws<ObjectDisposedException>();
        float[] transforms = new float[8];
        second.CopyTransforms(transforms);
        await Assert.That(transforms.AsSpan().SequenceEqual(
            new float[] { 0, 0, 1, 1, 0.5f, 0, 0.5f, 1 })).IsTrue();
        first.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(120ul);
        float[] coordinates = new float[6];
        second.CopyCornerCoordinates(coordinates);
        await Assert.That(coordinates.AsSpan().SequenceEqual(
            scene.Meshes.Values.Single().PtexCoordinates)).IsTrue();
        second.Dispose();
        second.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(() => second.CopyTransforms(transforms)).Throws<ObjectDisposedException>();
        await Assert.That(transforms[4]).IsEqualTo(0.5f);
    }

    [Test]
    public async Task CandidatePtexVertexIdWirePayloadCarriesTypedFacesAndIndependentSlotTransforms()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        var budget = new SilkCpuTextureBudget(8192);
        using var cache = new SilkPtexSlotCandidateCache();
        using var update = cache.Prepare(mesh, SilkMeshGeometryBuilder.Build(mesh), sources, 16, budget, 0);
        SilkPtexSlotCandidateCache.Candidate candidate = update.Value;
        using SilkPtexSlotLookup.Lease lease = candidate.Lookup.AcquireLease();
        using IDisposable copyReservation = budget.Reserve(checked((ulong)lease.ShaderWordCount * sizeof(uint)));
        uint[] words = new uint[lease.ShaderWordCount];
        lease.CopyShaderWords(words);
        await Assert.That(words.AsSpan(0, 4).SequenceEqual(new uint[] { 6, 2, 2, 28 })).IsTrue();
        await Assert.That(words.Length).IsEqualTo(44);
        for (int corner = 0; corner < 6; corner++)
        {
            int record = 4 + corner * 4;
            await Assert.That(words[record]).IsEqualTo((uint)(corner / 3));
            await Assert.That(BitConverter.UInt32BitsToSingle(words[record + 1]))
                .IsEqualTo(mesh.PtexCoordinates[corner * 2]);
            await Assert.That(BitConverter.UInt32BitsToSingle(words[record + 2]))
                .IsEqualTo(mesh.PtexCoordinates[corner * 2 + 1]);
            await Assert.That(words[record + 3]).IsEqualTo(0u);
            for (int slot = 0; slot < 2; slot++)
            {
                int transform = 28 + (slot * 2 + corner / 3) * 4;
                float u = BitConverter.UInt32BitsToSingle(words[record + 1]);
                float v = BitConverter.UInt32BitsToSingle(words[record + 2]);
                var mapped = (
                    BitConverter.UInt32BitsToSingle(words[transform]) +
                        u * BitConverter.UInt32BitsToSingle(words[transform + 2]),
                    BitConverter.UInt32BitsToSingle(words[transform + 1]) +
                        v * BitConverter.UInt32BitsToSingle(words[transform + 3]));
                await Assert.That(mapped).IsEqualTo(candidate.Lookup.MapCorner(slot, corner));
            }
        }
        await Assert.That(() => lease.CopyShaderWords(new uint[43])).Throws<ArgumentException>();
        await Assert.That(() => lease.CopyShaderWords(new uint[45])).Throws<ArgumentException>();
        await Assert.That(() => SilkPtexCandidateBinding.GetWordCount(int.MaxValue, 2, 2))
            .Throws<OverflowException>();
        SilkPtexCandidateBinding.ValidateDraw(candidate.Geometry, 6, 0, 0, 0, false);
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(candidate.Geometry, 6, 1, 0, 0, false))
            .Throws<InvalidDataException>();
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(candidate.Geometry, 6, 0, 1, 0, false))
            .Throws<InvalidDataException>();
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(candidate.Geometry, 6, 0, 0, 1, false))
            .Throws<InvalidDataException>();
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(candidate.Geometry, 6, 0, 0, 0, true))
            .Throws<InvalidDataException>();
        SilkMeshGeometry reordered = candidate.Geometry with { Indices = [1, 0, 2, 3, 4, 5] };
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(reordered, 6, 0, 0, 0, false))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CandidatePtexPublicationPreservesIndexedAndExpandedCorners(bool expandedSource)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(expandedSource);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh,
            requireTangents: true,
            displacementAmounts: Enumerable.Repeat(0.125f, mesh.Points.Length / 3).ToArray(),
            requireTextureCoordinates: true,
            secondaryUvPrimvar: "candidateSecondUv");
        int stride = checked((int)(ordinary.VertexLayout.Stride / sizeof(float)));
        for (int point = 0; point < ordinary.Vertices.Length / stride; point++)
        {
            ordinary.Vertices[point * stride + 6] = 0.1f * (point + 1);
            ordinary.Vertices[point * stride + 7] = 0.9f - 0.1f * point;
            ordinary.Vertices[(point + 1) * stride - 2] = 0.8f - 0.1f * point;
            ordinary.Vertices[(point + 1) * stride - 1] = 0.2f + 0.1f * point;
        }
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        var budget = new SilkCpuTextureBudget(4096);
        using var cache = new SilkPtexSlotCandidateCache();
        using var update = cache.Prepare(mesh, ordinary, sources, 16, budget, 0);
        SilkPtexSlotCandidateCache.Candidate candidate = update.Value;
        await Assert.That(cache.Current).IsNull();
        await Assert.That(candidate.Geometry.DerivedCornerVertices).IsTrue();
        using (SilkPtexSlotLookup.Lease lease = candidate.Lookup.AcquireLease())
        {
            uint[] cornerFaces = new uint[6];
            lease.CopyCornerFaces(cornerFaces);
            await Assert.That(cornerFaces.AsSpan().SequenceEqual(
                new uint[] { 0, 0, 0, 1, 1, 1 })).IsTrue();
        }
        await Assert.That(candidate.Geometry.Displaced).IsTrue();
        await Assert.That(candidate.Geometry.HasTangents).IsTrue();
        await Assert.That(candidate.Geometry.MaximumDisplacement).IsEqualTo(ordinary.MaximumDisplacement);
        for (int corner = 0; corner < 6; corner++)
        {
            int source = checked((int)mesh.Indices.Span[corner]);
            await Assert.That(candidate.Geometry.Vertices.AsSpan(corner * stride, stride)
                .SequenceEqual(ordinary.Vertices.AsSpan(source * stride, stride))).IsTrue();
            await Assert.That(candidate.Geometry.Indices[corner]).IsEqualTo((uint)corner);
            uint face = checked((uint)mesh.PtexFaceIndices[corner / 3]);
            for (int slot = 0; slot < sources.Length; slot++)
            {
                (float U, float V) expected = candidate.GetAtlas(slot).Layout.Map(face,
                    mesh.PtexCoordinates[corner * 2], mesh.PtexCoordinates[corner * 2 + 1]);
                await Assert.That(candidate.Lookup.MapCorner(slot, corner)).IsEqualTo(expected);
            }
        }
        await Assert.That(candidate.GetAtlas(0).Tiles[7].U1)
            .IsNotEqualTo(candidate.GetAtlas(1).Tiles[7].U1);
        update.Commit();
        await Assert.That(ReferenceEquals(cache.Current, candidate)).IsTrue();
        using var reused = cache.Prepare(mesh, ordinary, sources, 16, budget, 0);
        await Assert.That(ReferenceEquals(reused.Value, candidate)).IsTrue();
        reused.Commit();
        cache.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CandidatePtexPublicationPreservesUnreferencedAuthoredPointForPicking(bool derivedSource)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(
            false, triangleLocal: derivedSource, tail: true, withUv: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var budget = new SilkCpuTextureBudget(16384);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(
            mesh, budget, "st", requireTextureCoordinates: true);
        using SilkPtexCornerCertificate? certificate = built.Certificate;
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        using var cache = new SilkPtexSlotCandidateCache();
        using var update = cache.Prepare(mesh, built.Geometry, sources, 16, budget, 0, certificate);
        SilkMeshGeometry candidate = update.Value.Geometry;

        await Assert.That(SilkSubprimPickGeometry.TryResolveCornerPoints(
            mesh, true, out int[] authoredPoints, out uint[] pointIndices)).IsTrue();
        await Assert.That(authoredPoints[^1]).IsEqualTo(mesh.Points.Length / 3 - 1);
        await Assert.That(pointIndices[^1]).IsEqualTo((uint)mesh.Indices.Length);
        await Assert.That(candidate.VertexCount).IsGreaterThan(pointIndices[^1]);
        int stride = checked((int)(candidate.VertexLayout.Stride / sizeof(float)));
        int picked = checked((int)pointIndices[^1] * stride);
        await Assert.That(candidate.Vertices.AsSpan(picked, 3)
            .SequenceEqual(mesh.Points.Span[^3..])).IsTrue();
        await Assert.That(candidate.Indices.SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 })).IsTrue();
        SilkPtexCandidateBinding.ValidateDraw(candidate, mesh.Indices.Length, 0, 0, 0, false);
        uint[] invalidIndices = candidate.Indices.ToArray();
        invalidIndices[^1] = pointIndices[^1];
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(
            candidate with { Indices = invalidIndices }, mesh.Indices.Length, 0, 0, 0, false))
            .Throws<InvalidDataException>();
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(
            candidate with { Vertices = candidate.Vertices[..((mesh.Indices.Length - 1) * stride)] },
            mesh.Indices.Length, 0, 0, 0, false)).Throws<InvalidDataException>();
        await Assert.That(() => SilkPtexCandidateBinding.ValidateDraw(
            candidate with { Vertices = candidate.Vertices[..^1] },
            mesh.Indices.Length, 0, 0, 0, false)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task PtexSourceSnapshotDetectsPointOriginMutation()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false, tail: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        var budget = new SilkCpuTextureBudget(4096);
        using var snapshot = SilkPtexSourceSnapshot.Capture(mesh, geometry, budget);
        if (!MemoryMarshal.TryGetArray(mesh.PointOrigins, out ArraySegment<int> origins) ||
            origins.Array is null)
        {
            throw new InvalidOperationException("The point-origin fixture does not have an accessible backing array.");
        }
        int index = origins.Offset + origins.Count - 1;
        int original = origins.Array[index];
        origins.Array[index] = -1;
        await Assert.That(snapshot.Matches(mesh, geometry)).IsFalse();
        origins.Array[index] = original;
        await Assert.That(snapshot.Matches(mesh, geometry)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CandidatePtexPointOriginsInvalidatePublicationWithoutLosingPrevious(bool derivedSource)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(
            false, triangleLocal: derivedSource, tail: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var budget = new SilkCpuTextureBudget(16384);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(mesh, budget);
        using SilkPtexCornerCertificate? certificate = built.Certificate;
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        using var cache = new SilkPtexSlotCandidateCache();
        using (var initial = cache.Prepare(mesh, built.Geometry, sources, 16, budget, 0, certificate))
        {
            initial.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate previous = cache.Current!;
        ulong retainedBytes = budget.Usage.ReservedBytes;
        if (!MemoryMarshal.TryGetArray(mesh.PointOrigins, out ArraySegment<int> origins) ||
            origins.Array is null)
        {
            throw new InvalidOperationException("The point-origin fixture does not have an accessible backing array.");
        }
        int index = origins.Offset + origins.Count - 1;
        int original = origins.Array[index];
        origins.Array[index] = -1;
        if (derivedSource)
        {
            await Assert.That(() => cache.Prepare(mesh, built.Geometry, sources, 16, budget, 0, certificate))
                .Throws<InvalidDataException>();
        }
        else
        {
            using var changed = cache.Prepare(mesh, built.Geometry, sources, 16, budget, 0, certificate);
            await Assert.That(ReferenceEquals(changed.Value, previous)).IsFalse();
            await Assert.That(changed.Value.Geometry.VertexCount).IsEqualTo((uint)mesh.Indices.Length);
        }
        await Assert.That(ReferenceEquals(cache.Current, previous)).IsTrue();
        await Assert.That(previous.Geometry.VertexCount).IsEqualTo((uint)mesh.Indices.Length + 1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(retainedBytes);
        origins.Array[index] = original;
        using (var retry = cache.Prepare(mesh, built.Geometry, sources, 16, budget, 0, certificate))
        {
            await Assert.That(ReferenceEquals(retry.Value, previous)).IsTrue();
            retry.Commit();
        }
        cache.Dispose();
        certificate?.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexPickTailHonorsExactCpuAdmissionAndRollback()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(
            false, tail: true, tailCount: 3, lastTailHasNoOrigin: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var sizing = new SilkCpuTextureBudget(16384);
        ulong peak;
        using (var cache = new SilkPtexSlotCandidateCache())
        using (var pending = cache.Prepare(mesh, geometry, sources, 16, sizing, 0))
        {
            await Assert.That(pending.Value.Geometry.VertexCount).IsEqualTo(8u);
            peak = sizing.Usage.PeakReservedBytes;
        }
        await Assert.That(sizing.Usage.ReservedBytes).IsEqualTo(0ul);
        var insufficient = new SilkCpuTextureBudget(peak - 1);
        using (var cache = new SilkPtexSlotCandidateCache())
        {
            await Assert.That(() => cache.Prepare(mesh, geometry, sources, 16, insufficient, 0))
                .Throws<SilkCpuTextureBudgetExceededException>();
            await Assert.That(cache.Current).IsNull();
        }
        await Assert.That(insufficient.Usage.ReservedBytes).IsEqualTo(0ul);
        var exact = new SilkCpuTextureBudget(peak);
        using (var cache = new SilkPtexSlotCandidateCache())
        using (var pending = cache.Prepare(mesh, geometry, sources, 16, exact, 0))
        {
            await Assert.That(pending.Value.Geometry.VertexCount).IsEqualTo(8u);
            await Assert.That(exact.Usage.PeakReservedBytes).IsEqualTo(peak);
        }
        await Assert.That(exact.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CertifiedPtexCornerPrefixPreservesFramesUvsAndPickTail(bool tail)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(
            false, triangleLocal: true, tail: tail, withUv: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var budget = new SilkCpuTextureBudget(16384);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(
            mesh, budget, "st", requireTangents: true, requireTextureCoordinates: true);
        SilkMeshGeometry derived = built.Geometry;
        using var certificate = built.Certificate ??
            throw new InvalidOperationException("The producer did not certify the derived corner stream.");
        float[] vertices = derived.Vertices;
        uint[] indices = derived.Indices;
        int stride = checked((int)(derived.VertexLayout.Stride / sizeof(float)));
        await Assert.That(derived.DerivedCornerVertices).IsTrue();
        await Assert.That(derived.VertexCount).IsEqualTo((uint)(indices.Length + (tail ? 1 : 0)));
        await Assert.That(indices.SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 })).IsTrue();
        for (int corner = 0; corner < indices.Length; corner++)
        {
            int point = checked((int)mesh.Indices.Span[corner]);
            await Assert.That(vertices.AsSpan(corner * stride, 3)
                .SequenceEqual(mesh.Points.Span.Slice(point * 3, 3))).IsTrue();
            await Assert.That(vertices[corner * stride + 5]).IsEqualTo(corner < 3 ? -1f : 1f);
            await Assert.That(vertices[corner * stride + 6])
                .IsEqualTo(mesh.Points.Span[point * 3] + 0.5f);
            await Assert.That(vertices[corner * stride + 7])
                .IsEqualTo(mesh.Points.Span[point * 3 + 1] + 0.5f);
        }
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(4, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        using var cache = new SilkPtexSlotCandidateCache();
        await Assert.That(() => cache.Prepare(mesh, derived, sources, 16, budget, 0))
            .Throws<InvalidDataException>();
        await Assert.That(() => cache.Prepare(mesh, derived,
            [new(SilkMaterialParameter.Normal, 1, decoded)], 16, budget, 0, certificate))
            .Throws<NotSupportedException>();
        using (var update = cache.Prepare(mesh, derived, sources, 16, budget, 0, certificate))
        {
            await Assert.That(update.Value.Geometry.Vertices.AsSpan()
                .SequenceEqual(vertices)).IsTrue();
            await Assert.That(update.Value.Geometry.Indices.AsSpan().SequenceEqual(indices)).IsTrue();
            update.Commit();
        }
        using (var reuse = cache.Prepare(mesh, derived, sources, 16, budget, 0, certificate))
        {
            await Assert.That(ReferenceEquals(reuse.Value, cache.Current)).IsTrue();
            reuse.Commit();
        }
        float saved = vertices[3];
        vertices[3] = -7;
        await Assert.That(() => cache.Prepare(mesh, derived, sources, 16, budget, 0, certificate))
            .Throws<InvalidDataException>();
        await Assert.That(cache.Current!.Geometry.Vertices[3]).IsEqualTo(saved);
        vertices[3] = saved;
        using (var restored = cache.Prepare(mesh, derived, sources, 16, budget, 0, certificate))
        {
            await Assert.That(ReferenceEquals(restored.Value, cache.Current)).IsTrue();
        }
        float local = mesh.PtexCoordinates[0];
        (float U, float V) originalMapping = cache.Current!.Lookup.MapCorner(0, 0);
        mesh.PtexCoordinates[0] = local + 0.125f;
        var rebuilt = SilkPtexCornerCertificate.BuildCandidateGeometry(
            mesh, budget, "st", requireTangents: true, requireTextureCoordinates: true);
        using (var renewed = rebuilt.Certificate ??
            throw new InvalidOperationException("The rebuilt corner stream was not certified."))
        using (var changed = cache.Prepare(mesh, rebuilt.Geometry, sources, 16, budget, 0, renewed))
        {
            await Assert.That(ReferenceEquals(changed.Value, cache.Current)).IsFalse();
            await Assert.That(changed.Value.Lookup.MapCorner(0, 0))
                .IsNotEqualTo(cache.Current!.Lookup.MapCorner(0, 0));
            changed.Commit();
        }
        mesh.PtexCoordinates[0] = local;
        using (var restored = cache.Prepare(mesh, derived, sources, 16, budget, 0, certificate))
        {
            await Assert.That(ReferenceEquals(restored.Value, cache.Current)).IsFalse();
            await Assert.That(restored.Value.Lookup.MapCorner(0, 0)).IsEqualTo(originalMapping);
            restored.Commit();
        }
        cache.Dispose();
        await Assert.That(budget.Usage.ReservedBytes)
            .IsEqualTo(SilkPtexCornerCertificate.RequiredBytes(mesh, derived));
        certificate.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task PtexCornerCertificateRejectsReplacementReorderingMutationAndReleasedOwnership()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false, triangleLocal: true, tail: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry sizing = SilkMeshGeometryBuilder.Build(mesh);
        ulong bytes = SilkPtexCornerCertificate.RequiredBytes(mesh, sizing);
        var insufficient = new SilkCpuTextureBudget(bytes - 1);
        await Assert.That(() =>
            SilkPtexCornerCertificate.BuildCandidateGeometry(mesh, insufficient))
            .Throws<SilkCpuTextureBudgetExceededException>();
        await Assert.That(insufficient.Usage.ReservedBytes).IsEqualTo(0ul);
        var budget = new SilkCpuTextureBudget(bytes);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(mesh, budget);
        SilkMeshGeometry derived = built.Geometry;
        using var certificate = built.Certificate ??
            throw new InvalidOperationException("The producer did not certify the derived corner stream.");
        float[] vertices = derived.Vertices;
        uint[] indices = derived.Indices;
        int stride = checked((int)(derived.VertexLayout.Stride / sizeof(float)));
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes);
        certificate.Validate(mesh, derived, budget);
        await Assert.That(() => certificate.Validate(mesh,
            derived with { Vertices = (float[])vertices.Clone() }, budget)).Throws<InvalidDataException>();
        await Assert.That(() => certificate.Validate(mesh,
            derived with { Indices = (uint[])indices.Clone() }, budget)).Throws<InvalidDataException>();
        await Assert.That(() => certificate.Validate(mesh, derived, new SilkCpuTextureBudget(bytes)))
            .Throws<InvalidDataException>();
        (indices[0], indices[1]) = (indices[1], indices[0]);
        await Assert.That(() => certificate.Validate(mesh, derived, budget)).Throws<InvalidDataException>();
        (indices[0], indices[1]) = (indices[1], indices[0]);
        for (int component = 0; component < stride; component++)
        {
            (vertices[component], vertices[stride * 3 + component]) =
                (vertices[stride * 3 + component], vertices[component]);
        }
        await Assert.That(() => certificate.Validate(mesh, derived, budget)).Throws<InvalidDataException>();
        for (int component = 0; component < stride; component++)
        {
            (vertices[component], vertices[stride * 3 + component]) =
                (vertices[stride * 3 + component], vertices[component]);
        }
        float tail = vertices[^1];
        vertices[^1] = tail + 9;
        await Assert.That(() => certificate.Validate(mesh, derived, budget)).Throws<InvalidDataException>();
        vertices[^1] = tail;
        float coordinate = mesh.PtexCoordinates[0];
        mesh.PtexCoordinates[0] = coordinate + 0.125f;
        await Assert.That(() => certificate.Validate(mesh, derived, budget)).Throws<InvalidDataException>();
        mesh.PtexCoordinates[0] = coordinate;
        certificate.Validate(mesh, derived, budget);
        using var replacementScene = new SilkSceneState();
        using OpenUsdSilkPage replacementPage = CreateCandidateTwoFacePage(
            false, triangleLocal: true, tail: true);
        _ = replacementScene.Apply(replacementPage);
        await Assert.That(() => certificate.Validate(
            replacementScene.Meshes.Values.Single(), derived, budget)).Throws<InvalidDataException>();
        certificate.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(() => certificate.Validate(mesh, derived, budget)).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments("semantic")]
    [Arguments("location")]
    [Arguments("offset")]
    [Arguments("format")]
    [Arguments("count")]
    public async Task PtexSourceSnapshotDetectsVertexLayoutMutation(string change)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        int stride = checked((int)(ordinary.VertexLayout.Stride / sizeof(float)));
        (float[] vertices, uint[] indices) = SilkMeshGeometryBuilder.ExpandTriangleCorners(
            ordinary.Vertices, ordinary.Indices, stride);
        List<SilkVertexAttributeDescriptor> attributes = [.. ordinary.VertexLayout.Attributes];
        SilkMeshGeometry derived = ordinary with
        {
            Vertices = vertices,
            Indices = indices,
            VertexLayout = ordinary.VertexLayout with { Attributes = attributes },
            DerivedCornerVertices = true,
        };
        var budget = new SilkCpuTextureBudget(16384);
        using var snapshot = SilkPtexSourceSnapshot.Capture(mesh, derived, budget);
        SilkVertexAttributeDescriptor original = attributes[0];
        try
        {
            attributes[0] = change switch
            {
                "semantic" => original with { Semantic = SilkVertexSemantic.Normal },
                "location" => original with { Location = original.Location + 4 },
                "offset" => original with { Offset = original.Offset + 4 },
                "format" => original with { Format = SilkVertexFormat.Float2 },
                "count" => original,
                _ => throw new InvalidOperationException($"Unexpected layout change '{change}'."),
            };
            if (change == "count")
            {
                attributes.Add(original);
            }
            await Assert.That(snapshot.Matches(mesh, derived)).IsFalse();
        }
        finally
        {
            if (change == "count")
            {
                attributes.RemoveAt(attributes.Count - 1);
            }
            attributes[0] = original;
        }
        await Assert.That(snapshot.Matches(mesh, derived)).IsTrue();
        await Assert.That(snapshot.Matches(mesh, derived with
        {
            VertexLayout = derived.VertexLayout with { Stride = derived.VertexLayout.Stride + sizeof(float) },
        })).IsFalse();
        snapshot.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task PtexProducerBuildKeepsOrdinaryGeometryUncertified()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var budget = new SilkCpuTextureBudget(16384);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(mesh, budget);

        await Assert.That(built.Geometry.DerivedCornerVertices).IsFalse();
        await Assert.That(built.Geometry.Indices.AsSpan().SequenceEqual(mesh.Indices.Span)).IsTrue();
        await Assert.That(built.Certificate).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task PtexCandidateRetainsVertexLayoutAndDetectsSourceLayoutMutation()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry built = SilkMeshGeometryBuilder.Build(mesh);
        SilkVertexAttributeDescriptor[] attributes = built.VertexLayout.Attributes.ToArray();
        SilkMeshGeometry geometry = built with
        {
            VertexLayout = built.VertexLayout with { Attributes = attributes },
        };
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(4, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var budget = new SilkCpuTextureBudget(16384);
        using var cache = new SilkPtexSlotCandidateCache();
        using (var initial = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            initial.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate original = cache.Current!;
        SilkVertexAttributeDescriptor previous = attributes[0];
        SilkVertexAttributeDescriptor changed = previous with { Location = previous.Location + 4 };
        try
        {
            attributes[0] = changed;
            await Assert.That(original.Geometry.VertexLayout.Attributes[0]).IsEqualTo(previous);
            await Assert.That(() => cache.Prepare(mesh, geometry, sources, 16, budget, 0))
                .Throws<ArgumentException>();
            await Assert.That(ReferenceEquals(cache.Current, original)).IsTrue();
            attributes[0] = previous;
            using var restored = cache.Prepare(mesh, geometry, sources, 16, budget, 0);
            await Assert.That(ReferenceEquals(restored.Value, original)).IsTrue();
        }
        finally
        {
            attributes[0] = previous;
        }
        cache.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments("coordinate")]
    [Arguments("face")]
    [Arguments("vertex")]
    public async Task UncertifiedPtexReuseDetectsSameObjectMutationAndRestoration(string change)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        ulong snapshotBytes = SilkPtexSourceSnapshot.RequiredBytes(mesh, geometry);
        var shortBudget = new SilkCpuTextureBudget(snapshotBytes - 1);
        await Assert.That(() => SilkPtexSourceSnapshot.Capture(mesh, geometry, shortBudget))
            .Throws<SilkCpuTextureBudgetExceededException>();
        await Assert.That(shortBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        var exactBudget = new SilkCpuTextureBudget(snapshotBytes);
        using (var snapshot = SilkPtexSourceSnapshot.Capture(mesh, geometry, exactBudget))
        {
            await Assert.That(snapshot.Matches(mesh, geometry)).IsTrue();
            await Assert.That(exactBudget.Usage.ReservedBytes).IsEqualTo(snapshotBytes);
        }
        await Assert.That(exactBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(4, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var budget = new SilkCpuTextureBudget(16384);
        using var cache = new SilkPtexSlotCandidateCache();
        using (var initial = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            initial.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate original = cache.Current!;
        (float U, float V) originalUv = original.Lookup.MapCorner(0, 0);
        float originalVertex = original.Geometry.Vertices[0];
        ulong originalBytes = budget.Usage.ReservedBytes;
        float coordinate = mesh.PtexCoordinates[0];
        int face = mesh.PtexFaceIndices[0];
        float vertex = geometry.Vertices[0];
        void SetChanged(bool changed)
        {
            mesh.PtexCoordinates[0] = changed && change == "coordinate" ? coordinate + 0.125f : coordinate;
            mesh.PtexFaceIndices[0] = changed && change == "face" ? 11 : face;
            geometry.Vertices[0] = changed && change == "vertex" ? vertex + 0.25f : vertex;
        }
        SetChanged(true);
        using (budget.Reserve(budget.MaximumBytes - originalBytes))
        {
            await Assert.That(() => cache.Prepare(mesh, geometry, sources, 16, budget, 0))
                .Throws<SilkCpuTextureBudgetExceededException>();
        }
        await Assert.That(ReferenceEquals(cache.Current, original)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(originalBytes);
        await Assert.That(original.Lookup.MapCorner(0, 0)).IsEqualTo(originalUv);
        await Assert.That(original.Geometry.Vertices[0]).IsEqualTo(originalVertex);
        using (var abandoned = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(abandoned.Value, original)).IsFalse();
        }
        await Assert.That(ReferenceEquals(cache.Current, original)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(originalBytes);
        SetChanged(false);
        using (var unchanged = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(unchanged.Value, original)).IsTrue();
            unchanged.Commit();
        }
        SetChanged(true);
        using (var update = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(update.Value, original)).IsFalse();
            if (change == "vertex")
            {
                await Assert.That(update.Value.Geometry.Vertices[0]).IsEqualTo(vertex + 0.25f);
            }
            else
            {
                await Assert.That(update.Value.Lookup.MapCorner(0, 0)).IsNotEqualTo(originalUv);
            }
            update.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate changedPublication = cache.Current!;
        SetChanged(false);
        using (var restored = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(restored.Value, changedPublication)).IsFalse();
            await Assert.That(restored.Value.Lookup.MapCorner(0, 0)).IsEqualTo(originalUv);
            await Assert.That(restored.Value.Geometry.Vertices[0]).IsEqualTo(originalVertex);
            restored.Commit();
        }
        cache.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task PtexSameIdentityDecodedChangesReplaceOwnedPublication(bool layout, bool replacement)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(4, 2);
        using SilkDecodedPtexFaces other = CreateCandidateDecoded(4, 2);
        var budget = new SilkCpuTextureBudget(16384);
        ulong bytes = SilkPtexDecodedSnapshot.RequiredBytes(decoded);
        var shortBudget = new SilkCpuTextureBudget(bytes - 1);
        await Assert.That(() => SilkPtexDecodedSnapshot.Capture(decoded, shortBudget))
            .Throws<SilkCpuTextureBudgetExceededException>();
        await Assert.That(shortBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        var exactBudget = new SilkCpuTextureBudget(bytes);
        using (var snapshot = SilkPtexDecodedSnapshot.Capture(decoded, exactBudget))
        {
            await Assert.That(snapshot.Matches(other)).IsTrue();
            await Assert.That(exactBudget.Usage.ReservedBytes).IsEqualTo(bytes);
        }
        await Assert.That(exactBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        using var cache = new SilkPtexSlotCandidateCache();
        using (var initial = cache.Prepare(mesh, geometry, sources, 16, budget, 0))
        {
            initial.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate original = cache.Current!;
        ulong retained = budget.Usage.ReservedBytes;
        using (var equivalent = cache.Prepare(mesh, geometry,
            [new(SilkMaterialParameter.DiffuseColor, 1, other)], 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(equivalent.Value, original)).IsTrue();
        }
        SilkDecodedPtexFaces changed = replacement ? other : decoded;
        if (layout)
        {
            changed.Faces[0].Width = 2;
            changed.Faces[0].Height = 2;
            changed.Faces[0].RowStrideBytes = 8;
        }
        else
        {
            changed.Pixels.AsSpan().Fill(255);
        }
        SilkPtexSlotSource[] changedSources = [new(SilkMaterialParameter.DiffuseColor, 1, changed)];
        using (var abandoned = cache.Prepare(mesh, geometry, changedSources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(abandoned.Value, original)).IsFalse();
        }
        await Assert.That(ReferenceEquals(cache.Current, original)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(retained);
        using (var update = cache.Prepare(mesh, geometry, changedSources, 16, budget, 0))
        {
            if (layout)
            {
                await Assert.That(update.Value.GetAtlas(0).Layout.Map(7, 1, 0))
                    .IsNotEqualTo(original.GetAtlas(0).Layout.Map(7, 1, 0));
            }
            else
            {
                await Assert.That(update.Value.GetAtlas(0).Pixels.AsSpan().Contains((byte)255)).IsTrue();
                await Assert.That(original.GetAtlas(0).Pixels.AsSpan().Contains((byte)255)).IsFalse();
            }
            update.Commit();
        }
        if (layout)
        {
            changed.Faces[0].Width = 4;
            changed.Faces[0].Height = 1;
            changed.Faces[0].RowStrideBytes = 16;
        }
        else
        {
            changed.Pixels.AsSpan().Clear();
        }
        SilkPtexSlotCandidateCache.Candidate changedPublication = cache.Current!;
        using (var restored = cache.Prepare(mesh, geometry, changedSources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(restored.Value, changedPublication)).IsFalse();
            restored.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate restoredPublication = cache.Current!;
        byte[] retainedPixels = changed.Pixels;
        SilkPtexFaceLayout[] retainedFaces = changed.Faces;
        changed.Dispose();
        using (var disposedTemporary = cache.Prepare(mesh, geometry, changedSources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(disposedTemporary.Value, restoredPublication)).IsTrue();
        }
        if (layout)
        {
            retainedFaces[0].Flags ^= 1;
        }
        else
        {
            retainedPixels[0] = 123;
        }
        await Assert.That(() => cache.Prepare(mesh, geometry, changedSources, 16, budget, 0))
            .Throws<ObjectDisposedException>();
        await Assert.That(ReferenceEquals(cache.Current, restoredPublication)).IsTrue();
        if (layout)
        {
            retainedFaces[0].Flags ^= 1;
        }
        else
        {
            retainedPixels[0] = 0;
        }
        using (var restoredAlias = cache.Prepare(mesh, geometry, changedSources, 16, budget, 0))
        {
            await Assert.That(ReferenceEquals(restoredAlias.Value, restoredPublication)).IsTrue();
        }
        cache.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexPublicationRollsBackPartialAtlasesAndUncommittedRetry()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        using SilkDecodedPtexFaces oversized = CreateCandidateDecoded(16, 1);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        var budget = new SilkCpuTextureBudget(8192);
        using var cache = new SilkPtexSlotCandidateCache();
        using (var initial = cache.Prepare(mesh, ordinary, sources, 16, budget, 0))
        {
            initial.Commit();
        }
        SilkPtexSlotCandidateCache.Candidate previous = cache.Current!;
        ulong retainedBytes = budget.Usage.ReservedBytes;
        SilkPtexSlotSource[] broken = [sources[0], new(SilkMaterialParameter.Roughness, 3, oversized)];
        await Assert.That(() => cache.Prepare(mesh, ordinary, broken, 16, budget, 0))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(cache.Current, previous)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(retainedBytes);
        using (var abandoned = cache.Prepare(mesh, ordinary, sources, 16, budget, 1))
        {
            await Assert.That(ReferenceEquals(abandoned.Value, previous)).IsFalse();
            await Assert.That(ReferenceEquals(cache.Current, previous)).IsTrue();
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(retainedBytes);
        using (var retry = cache.Prepare(mesh, ordinary, sources, 16, budget, 1))
        {
            retry.Commit();
        }
        await Assert.That(ReferenceEquals(cache.Current, previous)).IsFalse();
        await Assert.That(() => previous.GetAtlas(0)).Throws<ObjectDisposedException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(retainedBytes);
        SilkPtexSlotCandidateCache.Candidate repaired = cache.Current!;
        SilkPtexSlotSource[] replacement = [sources[0], new(SilkMaterialParameter.Roughness, 4, second)];
        using (var changedSource = cache.Prepare(mesh, ordinary, replacement, 16, budget, 1))
        {
            await Assert.That(ReferenceEquals(changedSource.Value, repaired)).IsFalse();
            changedSource.Commit();
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(retainedBytes);
        cache.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexPublicationRejectsUnprovenDerivedCompositionAndShadowContracts()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 1);
        SilkPtexSlotSource[] surface = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var budget = new SilkCpuTextureBudget(4096);
        using var cache = new SilkPtexSlotCandidateCache();
        await Assert.That(() => cache.Prepare(mesh, ordinary with { DerivedCornerVertices = true },
            surface, 16, budget, 0)).Throws<InvalidDataException>();
        foreach (SilkMaterialParameter parameter in new[]
        {
            SilkMaterialParameter.Normal, SilkMaterialParameter.DetailNormal,
            SilkMaterialParameter.Displacement, SilkMaterialParameter.Opacity,
            SilkMaterialParameter.VolumeDensity,
        })
        {
            SilkPtexSlotSource[] refused = [new(parameter, 1, decoded)];
            await Assert.That(() => cache.Prepare(mesh, ordinary, refused, 16, budget, 0))
                .Throws<NotSupportedException>();
        }
        await Assert.That(cache.Current).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkColorSpace.Auto, SilkMaterialParameter.DiffuseColor, SilkTextureChannel.Rgb)]
    [Arguments(SilkColorSpace.Raw, SilkMaterialParameter.DiffuseColor, SilkTextureChannel.Rgb)]
    [Arguments(SilkColorSpace.Srgb, SilkMaterialParameter.Roughness, SilkTextureChannel.G)]
    [Arguments(SilkColorSpace.Auto, SilkMaterialParameter.Roughness, SilkTextureChannel.A)]
    public async Task CandidatePtexMaterialValuesApplyColorThenScaleBiasThenChannel(
        SilkColorSpace colorSpace, SilkMaterialParameter parameter, SilkTextureChannel channel)
    {
        var texture = new SilkMaterialTexture(parameter,
            SilkTextureWrap.Repeat, SilkTextureWrap.Black, colorSpace,
            channel == SilkTextureChannel.Rgb ? 3 : 1, channel,
            [0.5f, 0.75f, 1.25f, 0.5f], [0.1f, 0.05f, -0.1f, 0.2f],
            [0, 0, 0, 1], "candidate.ptx", "", SilkCompositeOperator.None, 0,
            sourceKind: SilkTextureSourceKind.Ptex);
        byte[] pixels = [128, 64, 192, 100, 255, 0, 32, 200];
        byte[] expected = pixels.ToArray();
        bool srgb = colorSpace == SilkColorSpace.Srgb ||
            (colorSpace == SilkColorSpace.Auto && parameter == SilkMaterialParameter.DiffuseColor);
        for (int texel = 0; texel < expected.Length; texel += 4)
        {
            for (int component = 0; component < 4; component++)
            {
                float value = expected[texel + component] / 255f;
                if (srgb && component < 3)
                {
                    value = value <= 0.04045f ? value / 12.92f :
                        MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
                    value = MathF.Round(value * 255f) / 255f;
                }
                expected[texel + component] = (byte)Math.Clamp(MathF.Round(
                    (value * texture.Scale[component] + texture.Bias[component]) * 255f), 0, 255);
            }
            if (channel != SilkTextureChannel.Rgb)
            {
                expected.AsSpan(texel, 4).Fill(expected[texel + (int)channel]);
            }
        }
        SilkSceneGpuResources.PreparePtexMaterialPixels(pixels, texture);
        await Assert.That(pixels.SequenceEqual(expected)).IsTrue();
        await Assert.That(SilkSceneGpuResources.PtexMaterialSampler.MinFilter).IsEqualTo(SilkSamplerFilter.Linear);
        await Assert.That(SilkSceneGpuResources.PtexMaterialSampler.AddressU).IsEqualTo(SilkSamplerAddressMode.ClampToEdge);
    }

    [Test]
    public async Task CandidatePtexMaterialValuesRejectUnestablishedProcessingWithoutTouchingPixels()
    {
        static SilkMaterialTexture Texture(
            SilkTextureSourceKind kind = SilkTextureSourceKind.Ptex,
            SilkCompositeOperator composite = SilkCompositeOperator.None,
            SilkTextureChannel channel = SilkTextureChannel.Rgb,
            float scale = 1,
            float luminance = 0) => new(
                SilkMaterialParameter.DiffuseColor, SilkTextureWrap.Clamp, SilkTextureWrap.Clamp,
                SilkColorSpace.Raw, 3, channel,
                [scale, 1, 1, 1], [0, 0, 0, 0], [0, 0, 0, 1],
                "candidate.ptx", "", composite, 0,
                new System.Numerics.Vector4(0, 0, 0, luminance), kind);
        foreach (SilkMaterialTexture texture in new[]
        {
            Texture(kind: SilkTextureSourceKind.Standard),
            Texture(composite: SilkCompositeOperator.Mix),
            Texture(channel: SilkTextureChannel.LuminanceRgb),
            Texture(luminance: 1),
        })
        {
            byte[] pixels = [12, 34, 56, 78];
            await Assert.That(() => SilkSceneGpuResources.PreparePtexMaterialPixels(pixels, texture))
                .Throws<NotSupportedException>();
            await Assert.That(pixels.SequenceEqual(new byte[] { 12, 34, 56, 78 })).IsTrue();
        }
        await Assert.That(() => SilkSceneGpuResources.CapturePtexMaterialValue(Texture(scale: float.NaN)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task CandidatePtexGpuMaterialValueChangeReprocessesWithoutMutatingPackedAtlas()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        first.Pixels.AsSpan().Fill(128);
        second.Pixels.AsSpan().Fill(192);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        static SilkMaterialTexture Material(SilkMaterialParameter parameter, float scale) => new(
            parameter, SilkTextureWrap.Repeat, SilkTextureWrap.Mirror, SilkColorSpace.Raw,
            parameter == SilkMaterialParameter.DiffuseColor ? 3 : 1,
            parameter == SilkMaterialParameter.DiffuseColor ? SilkTextureChannel.Rgb : SilkTextureChannel.G,
            [scale, scale, scale, 1], [0.1f, 0.2f, 0.3f, 0],
            [0, 0, 0, 1], "candidate.ptx", "", SilkCompositeOperator.None, 0,
            sourceKind: SilkTextureSourceKind.Ptex);
        SilkMaterialTexture[] materials =
        [
            Material(SilkMaterialParameter.DiffuseColor, 0.5f),
            Material(SilkMaterialParameter.Roughness, 0.75f),
        ];
        var cpu = new SilkCpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        new SilkGpuBufferBudget(65536).ConfigureDevice(device);
        new SilkGpuTextureBudget(65536).ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        using (var pending = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0, materials))
        {
            pending.Commit();
        }
        byte[][] before = device.LastSubmittedUploads.Select(bytes => bytes.ToArray()).ToArray();
        using (var lease = cache.AcquireLease())
        using (var commands = new PtexCandidateCommandList())
        {
            lease.BindAndDraw(commands, 1);
            await Assert.That(commands.SamplerBindings.Select(binding => binding.Binding)
                .SequenceEqual(new uint[] { 1, 11 })).IsTrue();
            await Assert.That(commands.SamplerBindings.All(binding =>
                binding.Descriptor == SilkSceneGpuResources.PtexMaterialSampler)).IsTrue();
        }
        materials[1] = Material(SilkMaterialParameter.Roughness, 0.25f);
        using (var pending = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0, materials))
        {
            pending.Commit();
        }
        await Assert.That(device.Submissions).IsEqualTo(2);
        await Assert.That(device.LastSubmittedUploads[0].SequenceEqual(before[0])).IsTrue();
        await Assert.That(device.LastSubmittedUploads[1].SequenceEqual(before[1])).IsFalse();
        materials[1] = Material(SilkMaterialParameter.Roughness, 0.75f);
        using (var pending = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0, materials))
        {
            pending.Commit();
        }
        await Assert.That(device.LastSubmittedUploads[0].SequenceEqual(before[0])).IsTrue();
        await Assert.That(device.LastSubmittedUploads[1].SequenceEqual(before[1])).IsTrue();
        cache.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexGpuPublicationUploadsMatchingGeometryLookupAndIndependentAtlases()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        var cpu = new SilkCpuTextureBudget(65536);
        byte[][] expectedUploads;
        uint[] expectedWords;
        byte[] expectedVertices;
        using (var sizing = new SilkPtexSlotCandidateCache())
        using (var candidate = sizing.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            expectedUploads =
            [
                candidate.Value.GetAtlas(0).Pixels.ToArray(),
                candidate.Value.GetAtlas(1).Pixels.ToArray(),
            ];
            using var lookup = candidate.Value.Lookup.AcquireLease();
            expectedWords = new uint[lookup.ShaderWordCount];
            lookup.CopyShaderWords(expectedWords);
            expectedVertices = MemoryMarshal.AsBytes(candidate.Value.Geometry.Vertices.AsSpan()).ToArray();
        }
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        await Assert.That(() => cache.AcquireLease()).Throws<InvalidOperationException>();
        using (var pending = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            await Assert.That(() => cache.AcquireLease()).Throws<InvalidOperationException>();
            await Assert.That(device.Submissions).IsEqualTo(1);
            await Assert.That(device.TextureOwners.Count).IsEqualTo(2);
            await Assert.That(device.Buffers.Count).IsEqualTo(3);
            await Assert.That(device.LastSubmittedUploads.Count).IsEqualTo(2);
            await Assert.That(device.LastSubmittedUploads[0].SequenceEqual(expectedUploads[0])).IsTrue();
            await Assert.That(device.LastSubmittedUploads[1].SequenceEqual(expectedUploads[1])).IsTrue();
            await Assert.That(() => cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
                .Throws<InvalidOperationException>();
            pending.Commit();
        }
        using var lease = cache.AcquireLease();
        await Assert.That(lease.SlotCount).IsEqualTo(2);
        await Assert.That(lease.VertexLayout.Matches(ordinary.VertexLayout)).IsTrue();
        await Assert.That(ReferenceEquals(
            lease.VertexLayout.Attributes, ordinary.VertexLayout.Attributes)).IsFalse();
        await Assert.That(lease.GetParameter(0)).IsEqualTo(SilkMaterialParameter.DiffuseColor);
        await Assert.That(lease.GetParameter(1)).IsEqualTo(SilkMaterialParameter.Roughness);
        using var commands = new PtexCandidateCommandList();
        lease.BindAndDraw(commands, 3);
        await Assert.That(commands.LookupBinding).IsEqualTo(49u);
        await Assert.That(commands.IndexCount).IsEqualTo(6u);
        await Assert.That(commands.InstanceCount).IsEqualTo(3u);
        await Assert.That(commands.TextureBindings.SequenceEqual(new uint[] { 2, 4 })).IsTrue();
        uint[] words = new uint[44];
        commands.Lookup!.ReadbackForTesting(MemoryMarshal.AsBytes(words.AsSpan()));
        await Assert.That(words.SequenceEqual(expectedWords)).IsTrue();
        await Assert.That(words.AsSpan(0, 4).SequenceEqual(new uint[] { 6, 2, 2, 28 })).IsTrue();
        await Assert.That(new[] { words[4], words[8], words[12], words[16], words[20], words[24] }
            .SequenceEqual(new uint[] { 0, 0, 0, 1, 1, 1 })).IsTrue();
        uint[] indices = new uint[6];
        commands.Indices!.ReadbackForTesting(MemoryMarshal.AsBytes(indices.AsSpan()));
        await Assert.That(indices.SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 })).IsTrue();
        byte[] vertexBytes = new byte[expectedVertices.Length];
        commands.Vertices!.ReadbackForTesting(vertexBytes);
        await Assert.That(vertexBytes.SequenceEqual(expectedVertices)).IsTrue();
        using (var reused = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            reused.Commit();
        }
        await Assert.That(device.Submissions).IsEqualTo(1);
        await Assert.That(device.Buffers.Count).IsEqualTo(3);
        cache.Dispose();
        await Assert.That(device.Buffers.All(buffer => !buffer.Released)).IsTrue();
        await Assert.That(device.TextureOwners.All(texture => !texture.Released)).IsTrue();
        // The publication retains only GPU payloads and its bounded slot metadata,
        // not an old CPU candidate's geometry or atlas arrays.
        await Assert.That(cpu.Usage.ReservedBytes)
            .IsEqualTo(64ul * ((ulong)IntPtr.Size + sizeof(byte)) +
                2ul * ((ulong)IntPtr.Size + sizeof(uint)) +
                (ulong)lease.VertexLayout.Attributes.Count *
                    (ulong)Unsafe.SizeOf<SilkVertexAttributeDescriptor>());
        lease.BindAndDraw(commands, 2);
        lease.Dispose();
        await Assert.That(() => lease.BindAndDraw(commands, 2)).Throws<ObjectDisposedException>();
        await Assert.That(device.Buffers.All(buffer => buffer.Released)).IsTrue();
        await Assert.That(device.TextureOwners.All(texture => texture.Released)).IsTrue();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CandidatePtexGpuPublishesProducerCertifiedCornersAndPreservesPreviousOnMutation(bool tail)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(
            false, triangleLocal: true, tail: tail, withUv: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var cpu = new SilkCpuTextureBudget(65536);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(
            mesh, cpu, "st", requireTangents: true, requireTextureCoordinates: true);
        SilkMeshGeometry geometry = built.Geometry;
        using var certificate = built.Certificate ??
            throw new InvalidOperationException("The producer did not certify the derived corner stream.");
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        using (var pending = cache.Prepare(mesh, geometry, sources, 16, cpu, 0,
            cornerCertificate: certificate))
        {
            pending.Commit();
        }

        using var lease = cache.AcquireLease();
        using var commands = new PtexCandidateCommandList();
        lease.BindAndDraw(commands, 3);
        await Assert.That(commands.IndexCount).IsEqualTo(6u);
        await Assert.That(commands.InstanceCount).IsEqualTo(3u);
        await Assert.That(commands.TextureBindings.SequenceEqual(new uint[] { 2, 4 })).IsTrue();
        byte[] expected = MemoryMarshal.AsBytes(geometry.Vertices.AsSpan()).ToArray();
        byte[] actual = new byte[expected.Length];
        commands.Vertices!.ReadbackForTesting(actual);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        uint[] indices = new uint[6];
        commands.Indices!.ReadbackForTesting(MemoryMarshal.AsBytes(indices.AsSpan()));
        await Assert.That(indices.SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 })).IsTrue();
        uint[] words = new uint[44];
        commands.Lookup!.ReadbackForTesting(MemoryMarshal.AsBytes(words.AsSpan()));
        await Assert.That(new[] { words[4], words[8], words[12], words[16], words[20], words[24] }
            .SequenceEqual(new uint[] { 0, 0, 0, 1, 1, 1 })).IsTrue();

        float saved = geometry.Vertices[0];
        geometry.Vertices[0] = saved + 0.25f;
        await Assert.That(() => cache.Prepare(mesh, geometry, sources, 16, cpu, 0,
            cornerCertificate: certificate)).Throws<InvalidDataException>();
        await Assert.That(device.Submissions).IsEqualTo(1);
        commands.Vertices.ReadbackForTesting(actual);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        geometry.Vertices[0] = saved;
        using (var reuse = cache.Prepare(mesh, geometry, sources, 16, cpu, 0,
            cornerCertificate: certificate))
        {
            reuse.Commit();
        }
        await Assert.That(device.Submissions).IsEqualTo(1);
        certificate.Dispose();
        await Assert.That(() => cache.Prepare(mesh, geometry, sources, 16, cpu, 0,
            cornerCertificate: certificate)).Throws<ObjectDisposedException>();
        cache.Dispose();
        lease.BindAndDraw(commands, 1);
        lease.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CandidatePtexGpuRetainsMultiplePickVerticesAcrossReplacement(bool derivedSource)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(
            false, triangleLocal: derivedSource, tail: true, withUv: true,
            tailCount: 3, lastTailHasNoOrigin: true);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        var cpu = new SilkCpuTextureBudget(65536);
        var built = SilkPtexCornerCertificate.BuildCandidateGeometry(
            mesh, cpu, "st", requireTextureCoordinates: true);
        using SilkPtexCornerCertificate? certificate = built.Certificate;
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(65536);
        var staging = new SilkGpuStagingBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        using (var pending = cache.Prepare(mesh, built.Geometry, sources, 16, cpu, 0,
            cornerCertificate: certificate))
        {
            pending.Commit();
        }

        using var previous = cache.AcquireLease();
        using var commands = new PtexCandidateCommandList();
        previous.BindAndDraw(commands, 2);
        await Assert.That(commands.IndexCount).IsEqualTo(6u);
        await Assert.That(commands.InstanceCount).IsEqualTo(2u);
        await Assert.That(SilkSubprimPickGeometry.TryResolveCornerPoints(
            mesh, true, out int[] authoredPoints, out uint[] pointIndices)).IsTrue();
        await Assert.That(pointIndices.AsSpan(^2..).SequenceEqual(new uint[] { 6, 7 })).IsTrue();
        await Assert.That(authoredPoints.AsSpan(^2..).SequenceEqual(new int[] { 3, 4 })).IsTrue();
        int stride = checked((int)(previous.VertexLayout.Stride / sizeof(float)));
        float[] vertices = new float[8 * stride];
        await Assert.That(commands.Vertices!.Size).IsEqualTo((nuint)(vertices.Length * sizeof(float)));
        commands.Vertices.ReadbackForTesting(MemoryMarshal.AsBytes(vertices.AsSpan()));
        for (int point = 0; point < 2; point++)
        {
            int vertex = checked((int)pointIndices[^(2 - point)] * stride);
            int source = authoredPoints[^(2 - point)] * 3;
            await Assert.That(vertices.AsSpan(vertex, 3)
                .SequenceEqual(mesh.Points.Span.Slice(source, 3))).IsTrue();
        }

        using (var pending = cache.Prepare(mesh, built.Geometry, sources, 16, cpu, 1,
            cornerCertificate: certificate))
        {
            pending.Commit();
        }
        using var current = cache.AcquireLease();
        await Assert.That(ReferenceEquals(
            previous.VertexLayout.Attributes, current.VertexLayout.Attributes)).IsFalse();
        cache.Dispose();
        previous.BindAndDraw(commands, 1);
        commands.Vertices!.ReadbackForTesting(MemoryMarshal.AsBytes(vertices.AsSpan()));
        current.BindAndDraw(commands, 1);
        commands.Vertices!.ReadbackForTesting(MemoryMarshal.AsBytes(vertices.AsSpan()));
        previous.Dispose();
        current.Dispose();
        certificate?.Dispose();
        await Assert.That(device.Buffers.All(buffer => buffer.Released)).IsTrue();
        await Assert.That(device.TextureOwners.All(texture => texture.Released)).IsTrue();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("abandonment")]
    [Arguments("lease")]
    [Arguments("write")]
    [Arguments("wait")]
    public async Task CandidatePtexCleanupRetainsFailedOwnersAndRetriesWithoutRepeatingSuccess(string failure)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var cpu = new SilkCpuTextureBudget(65536);
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        using (var initial = cache.Prepare(mesh, geometry, sources, 16, cpu, 0))
        {
            initial.Commit();
        }
        ulong initialBuffers = buffers.Usage.ReservedBytes;
        using var lease = cache.AcquireLease();
        if (failure is "write" or "wait")
        {
            lease.Dispose();
            device.RefuseBufferWrites = failure == "write";
            device.RefuseWait = failure == "wait";
            device.RefuseNewBufferDisposal = true;
            AggregateException? reported = null;
            try
            {
                using var unexpected = cache.Prepare(mesh, geometry, sources, 16, cpu, 1);
            }
            catch (AggregateException error)
            {
                reported = error;
            }
            await Assert.That(reported).IsNotNull();
            await Assert.That(reported!.InnerExceptions[0].Message)
                .IsEqualTo(failure == "write"
                    ? "Injected Ptex candidate buffer write failure."
                    : "Injected Ptex candidate upload wait failure.");
            await Assert.That(reported.InnerExceptions[1].Message)
                .IsEqualTo("Injected Ptex candidate buffer disposal failure.");
            await Assert.That(buffers.Usage.ReservedBytes).IsGreaterThan(initialBuffers);
            using var preserved = cache.AcquireLease();
            await Assert.That(preserved.SlotCount).IsEqualTo(1);
        }
        else
        {
            var update = cache.Prepare(mesh, geometry, sources, 16, cpu, 1);
            TextureGraphicsBuffer failed = failure == "abandonment"
                ? device.Buffers[^1] : device.Buffers[2];
            failed.RefuseDisposal = true;
            if (failure == "abandonment")
            {
                await Assert.That(() => update.Dispose()).Throws<InvalidOperationException>();
            }
            else if (failure == "replacement")
            {
                lease.Dispose();
                await Assert.That(() => update.Commit()).Throws<InvalidOperationException>();
            }
            else
            {
                update.Commit();
                await Assert.That(() => lease.Dispose()).Throws<InvalidOperationException>();
            }
            await Assert.That(failed.Released).IsFalse();
            await Assert.That(buffers.Usage.ReservedBytes).IsGreaterThan(initialBuffers);
        }
        int[] completed = device.Buffers.Select(buffer => buffer.SuccessfulDisposals).ToArray();
        device.RefuseWait = false;
        device.RefuseBufferWrites = false;
        device.RefuseNewBufferDisposal = false;
        foreach (TextureGraphicsBuffer buffer in device.Buffers)
        {
            buffer.RefuseDisposal = false;
            buffer.DisposalCallback = () => cache.RetryCleanup();
        }
        await Assert.That(() => cache.RetryCleanup()).Throws<InvalidOperationException>();
        foreach (TextureGraphicsBuffer buffer in device.Buffers)
        {
            buffer.DisposalCallback = null;
        }
        cache.RetryCleanup();
        for (int index = 0; index < completed.Length; index++)
        {
            if (completed[index] != 0)
            {
                await Assert.That(device.Buffers[index].SuccessfulDisposals).IsEqualTo(completed[index]);
            }
        }
        using (var retry = cache.Prepare(mesh, geometry, sources, 16, cpu, 2))
        {
            retry.Commit();
        }
        lease.Dispose();
        cache.Dispose();
        cache.RetryCleanup();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task PtexCleanupOwnerPreservesFailedAndUnvisitedStepsAndRejectsReentry()
    {
        var owner = new SilkPtexCleanupOwner();
        var first = owner.Own(new PtexCleanupTestResource());
        var failed = owner.Own(new PtexCleanupTestResource { Fail = true });
        var last = owner.Own(new PtexCleanupTestResource());
        owner.Retire();
        await Assert.That(() => owner.Drain()).Throws<InvalidOperationException>();
        await Assert.That(last.Successes).IsEqualTo(1);
        var prioritized = new SilkPtexCleanupOwner();
        var payload = prioritized.Own(new PtexCleanupTestResource());
        var command = prioritized.Own(new PtexCleanupTestResource { Fail = true }, beforePayload: true);
        prioritized.Retire();
        await Assert.That(() => prioritized.Drain()).Throws<InvalidOperationException>();
        await Assert.That(payload.Successes).IsEqualTo(0);
        command.Fail = false;
        await Assert.That(prioritized.Drain()).IsTrue();
        await Assert.That(command.Successes).IsEqualTo(1);
        await Assert.That(payload.Successes).IsEqualTo(1);
        await Assert.That(failed.Successes).IsEqualTo(0);
        await Assert.That(first.Successes).IsEqualTo(0);
        failed.Fail = false;
        failed.Callback = () => owner.Drain();
        await Assert.That(() => owner.Drain()).Throws<InvalidOperationException>();
        failed.Callback = null;
        await Assert.That(owner.Drain()).IsTrue();
        await Assert.That(owner.Drain()).IsTrue();
        await Assert.That(first.Successes).IsEqualTo(1);
        await Assert.That(failed.Successes).IsEqualTo(1);
        await Assert.That(last.Successes).IsEqualTo(1);
    }

    private sealed class PtexCleanupTestResource : IDisposable
    {
        internal bool Fail { get; set; }
        internal Action? Callback { get; set; }
        internal int Successes { get; private set; }
        public void Dispose()
        {
            Callback?.Invoke();
            if (Fail)
            {
                throw new InvalidOperationException("Injected cleanup failure.");
            }
            Successes++;
        }
    }

    [Test]
    public async Task CandidatePtexGpuDuplicateBindingsRefuseBeforeAllocationAndAllowRetry()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        var cpu = new SilkCpuTextureBudget(65536);
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        SilkPtexSlotSource[] valid =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, decoded),
            new(SilkMaterialParameter.Roughness, 2, decoded),
        ];
        using (var initial = cache.Prepare(mesh, geometry, valid, 16, cpu, 0))
        {
            initial.Commit();
        }
        using var oldLease = cache.AcquireLease();
        using var oldCommands = new PtexCandidateCommandList();
        oldLease.BindAndDraw(oldCommands, 1);
        ulong cpuBytes = cpu.Usage.ReservedBytes;
        ulong cpuPeak = cpu.Usage.PeakReservedBytes;
        ulong bufferBytes = buffers.Usage.ReservedBytes;
        ulong textureBytes = textures.Usage.ReservedBytes;
        int bufferCount = device.Buffers.Count;
        int textureCount = device.TextureOwners.Count;
        int submissions = device.Submissions;
        await Assert.That(() => cache.Prepare(mesh, geometry,
            [valid[0], new(SilkMaterialParameter.DiffuseColor, 3, decoded)], 16, cpu, 1))
            .Throws<InvalidDataException>();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(cpuBytes);
        await Assert.That(cpu.Usage.PeakReservedBytes).IsEqualTo(cpuPeak);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textureBytes);
        await Assert.That(device.Buffers.Count).IsEqualTo(bufferCount);
        await Assert.That(device.TextureOwners.Count).IsEqualTo(textureCount);
        await Assert.That(device.Submissions).IsEqualTo(submissions);
        using (var preserved = cache.AcquireLease())
        using (var commands = new PtexCandidateCommandList())
        {
            preserved.BindAndDraw(commands, 1);
            await Assert.That(commands.Lookup).IsSameReferenceAs(oldCommands.Lookup);
        }
        using (var retry = cache.Prepare(mesh, geometry, valid, 16, cpu, 1))
        {
            retry.Commit();
        }
        oldLease.Dispose();
        cache.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CandidatePtexGpuPublicationRollbackAndRetryPreserveLeasedPreviousResources(bool failOnWait)
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var cpu = new SilkCpuTextureBudget(65536);
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        using (var initial = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            initial.Commit();
        }
        using var oldLease = cache.AcquireLease();
        using var oldCommands = new PtexCandidateCommandList();
        oldLease.BindAndDraw(oldCommands, 1);
        ulong cpuBytes = cpu.Usage.ReservedBytes;
        ulong bufferBytes = buffers.Usage.ReservedBytes;
        ulong textureBytes = textures.Usage.ReservedBytes;
        device.RefuseSubmission = !failOnWait;
        device.RefuseWait = failOnWait;
        if (failOnWait)
        {
            await Assert.That(() => cache.Prepare(mesh, ordinary, sources, 16, cpu, 1))
                .Throws<InvalidOperationException>();
        }
        else
        {
            await Assert.That(() => cache.Prepare(mesh, ordinary, sources, 16, cpu, 1))
                .Throws<SilkGpuStagingBudgetExceededException>();
        }
        device.RefuseSubmission = false;
        device.RefuseWait = false;
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(cpuBytes);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textureBytes);
        using (var abandoned = cache.Prepare(mesh, ordinary, sources, 16, cpu, 1))
        {
            await Assert.That(cpu.Usage.ReservedBytes).IsGreaterThan(cpuBytes);
        }
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(cpuBytes);
        using (var unchanged = cache.AcquireLease())
        {
            using var commands = new PtexCandidateCommandList();
            unchanged.BindAndDraw(commands, 1);
            await Assert.That(commands.Lookup).IsSameReferenceAs(oldCommands.Lookup);
        }
        using (var retry = cache.Prepare(mesh, ordinary, sources, 16, cpu, 1))
        {
            retry.Commit();
        }
        using (var replacement = cache.AcquireLease())
        {
            using var commands = new PtexCandidateCommandList();
            replacement.BindAndDraw(commands, 2);
            await Assert.That(ReferenceEquals(commands.Lookup, oldCommands.Lookup)).IsFalse();
        }
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(2 * bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(2 * textureBytes);
        oldLease.BindAndDraw(oldCommands, 2);
        oldLease.Dispose();
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textureBytes);
        int bufferCreations = device.Buffers.Count;
        using (var changedSource = cache.Prepare(mesh, ordinary,
            [new SilkPtexSlotSource(SilkMaterialParameter.DiffuseColor, 2, decoded)], 16, cpu, 1))
        {
            changedSource.Commit();
        }
        await Assert.That(device.Buffers.Count).IsEqualTo(bufferCreations + 3);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textureBytes);
        cache.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexGpuPartialAtlasAdmissionRefusalPreservesCurrentAndReleasesReplacement()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces first = CreateCandidateDecoded(1, 2);
        using SilkDecodedPtexFaces second = CreateCandidateDecoded(4, 1);
        SilkPtexSlotSource[] sources =
        [
            new(SilkMaterialParameter.DiffuseColor, 1, first),
            new(SilkMaterialParameter.Roughness, 2, second),
        ];
        var cpu = new SilkCpuTextureBudget(65536);
        ulong atlasBytes;
        using (var sizing = new SilkPtexSlotCandidateCache())
        using (var candidate = sizing.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            atlasBytes = checked((ulong)candidate.Value.GetAtlas(0).Pixels.Length +
                (ulong)candidate.Value.GetAtlas(1).Pixels.Length);
        }
        var buffers = new SilkGpuBufferBudget(65536);
        var textures = new SilkGpuTextureBudget(2 * atlasBytes - 1);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        cpu.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        new SilkGpuStagingBudget(65536).ConfigureDevice(device);
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        using (var initial = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            initial.Commit();
        }
        ulong cpuBytes = cpu.Usage.ReservedBytes;
        ulong bufferBytes = buffers.Usage.ReservedBytes;
        await Assert.That(() => cache.Prepare(mesh, ordinary, sources, 16, cpu, 1))
            .Throws<SilkGpuTextureBudgetExceededException>();
        await Assert.That(device.TextureOwners.Count).IsEqualTo(3);
        await Assert.That(device.TextureOwners[0].Released).IsFalse();
        await Assert.That(device.TextureOwners[1].Released).IsFalse();
        await Assert.That(device.TextureOwners[2].Released).IsTrue();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(cpuBytes);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(atlasBytes);
        using (var reused = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
        {
            reused.Commit();
        }
        await Assert.That(device.Submissions).IsEqualTo(1);
        cache.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task CandidatePtexGpuPublicationRequiresAllAdmissionPoolsBeforeAllocation()
    {
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = CreateCandidateTwoFacePage(false);
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkMeshGeometry ordinary = SilkMeshGeometryBuilder.Build(mesh);
        using SilkDecodedPtexFaces decoded = CreateCandidateDecoded(1, 2);
        SilkPtexSlotSource[] sources = [new(SilkMaterialParameter.DiffuseColor, 1, decoded)];
        var cpu = new SilkCpuTextureBudget(65536);
        using var device = new TextureGraphicsDevice { MaxTextureDimension2D = 16 };
        using var cache = new SilkPtexSlotGpuCandidateCache(device);
        for (int pool = 0; pool < 4; pool++)
        {
            await Assert.That(() => cache.Prepare(mesh, ordinary, sources, 16, cpu, 0))
                .Throws<InvalidOperationException>();
            await Assert.That(device.Buffers.Count).IsEqualTo(0);
            await Assert.That(device.TextureOwners.Count).IsEqualTo(0);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            switch (pool)
            {
                case 0:
                    cpu.ConfigureDevice(device);
                    break;
                case 1:
                    new SilkGpuBufferBudget(65536).ConfigureDevice(device);
                    break;
                case 2:
                    new SilkGpuTextureBudget(65536).ConfigureDevice(device);
                    break;
                case 3:
                    new SilkGpuStagingBudget(65536).ConfigureDevice(device);
                    break;
            }
        }
        await Assert.That(() => cache.Prepare(mesh, ordinary, sources, 32, cpu, 0))
            .Throws<InvalidOperationException>();
        using var pending = cache.Prepare(mesh, ordinary, sources, 16, cpu, 0);
        cache.Dispose();
        await Assert.That(() => pending.Commit()).Throws<ObjectDisposedException>();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(device.Buffers.All(buffer => buffer.Released)).IsTrue();
        await Assert.That(device.TextureOwners.All(texture => texture.Released)).IsTrue();
    }

    private static OpenUsdSilkPage CreateCandidateTwoFacePage(
        bool expanded, bool triangleLocal = false, bool tail = false, bool withUv = false,
        int tailCount = 1, bool lastTailHasNoOrigin = false)
    {
        byte[] mesh = CreateMeshUpsert("/Candidate", "");
        int pathLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(48)));
        int start = 276 + pathLength;
        float[] sharedPoints = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] sharedIndices = [0, 1, 2, 2, 1, 0];
        float[] points = expanded
            ? sharedIndices.SelectMany(index => sharedPoints.Skip((int)index * 3).Take(3)).ToArray()
            : sharedPoints;
        if (tail)
        {
            for (int point = 0; point < tailCount; point++)
            {
                points = [.. points, 0.25f + (point * 0.125f), 0.25f, 0.5f];
            }
        }
        int pointCount = points.Length / 3;
        int uvBytes = withUv ? 20 + 2 + (pointCount * 2 * sizeof(float)) : 0;
        int originBytes = tail ? pointCount * sizeof(uint) : 0;
        int normalBytes = triangleLocal ? 12 + (pointCount * sizeof(uint)) : 0;
        Array.Resize(ref mesh, start + points.Length * 4 + 6 * 4 + 2 * 4 +
            uvBytes + originBytes + 2 * 28 + normalBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(52), (uint)pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(56), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(60), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(220), withUv ? 1u : 0u);
        if (tail)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(236), (uint)SilkSubprimIdentity.Point);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(244), (uint)pointCount);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(256),
                (uint)(pointCount - (lastTailHasNoOrigin ? 1 : 0)));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(268), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(272), (uint)SilkPtexMappingStatus.Mapped);
        int cursor = start;
        foreach (float component in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(mesh.AsSpan(cursor), component);
            cursor += 4;
        }
        for (int corner = 0; corner < 6; corner++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                mesh.AsSpan(cursor), expanded ? (uint)corner : sharedIndices[corner]);
            cursor += 4;
        }
        mesh.AsSpan(cursor, 8).Clear();
        cursor += 8;
        if (withUv)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), (uint)SilkAttributeSemantic.TexCoord);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor + 4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor + 8), (uint)SilkAttributeInterpolation.Vertex);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor + 12), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor + 16), (uint)pointCount);
            cursor += 20;
            mesh[cursor++] = (byte)'s';
            mesh[cursor++] = (byte)'t';
            for (int point = 0; point < pointCount; point++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(mesh.AsSpan(cursor), points[point * 3] + 0.5f);
                BinaryPrimitives.WriteSingleLittleEndian(mesh.AsSpan(cursor + 4), points[point * 3 + 1] + 0.5f);
                cursor += 8;
            }
        }
        if (tail)
        {
            for (int point = 0; point < pointCount; point++)
            {
                uint origin = lastTailHasNoOrigin && point == pointCount - 1 ? uint.MaxValue : (uint)point;
                BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), origin);
                cursor += sizeof(uint);
            }
        }
        foreach (uint face in new uint[] { 7, 11 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), face);
            cursor += 4;
            foreach (float coordinate in new float[] { 0.25f, 0.25f, 0.5f, 0.75f, 0.75f, 0.25f })
            {
                BinaryPrimitives.WriteSingleLittleEndian(mesh.AsSpan(cursor), coordinate);
                cursor += 4;
            }
        }
        if (triangleLocal)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), 0x80000000u | (uint)pointCount);
            cursor += 12;
            for (int point = 0; point < pointCount; point++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), (uint)point);
                cursor += sizeof(uint);
            }
        }
        return CreateMeshPage(mesh, 1);
    }

    private static SilkDecodedPtexFaces CreateCandidateDecoded(uint firstWidth, uint secondWidth)
    {
        uint count = firstWidth + secondWidth;
        var info = new SilkPtexDecodeInfo
        {
            StructSize = (uint)Marshal.SizeOf<SilkPtexDecodeInfo>(),
            Version = 1,
            MeshType = 1,
            DataType = 0,
            ChannelCount = 4,
            AlphaChannel = 3,
            FileFaceCount = 12,
            RequestedFaceCount = 2,
            RequiredByteCount = count * 4,
        };
        static SilkPtexFaceLayout face(uint id, uint width, ulong offset) => new()
        {
            StructSize = (uint)Marshal.SizeOf<SilkPtexFaceLayout>(),
            FaceId = id,
            Width = width,
            Height = 1,
            RowStrideBytes = width * 4,
            DataOffsetBytes = offset,
            AdjacentFaceId0 = -1,
            AdjacentFaceId1 = -1,
            AdjacentFaceId2 = -1,
            AdjacentFaceId3 = -1,
        };
        return new(info, [face(7, firstWidth, 0), face(11, secondWidth, firstWidth * 4)],
            new byte[checked((int)(count * 4))], null);
    }

    private static SilkPtexAtlasLayout CreateLookupLayout(float u0, float v0, float u1, float v1) =>
        new(16, 16, new Dictionary<uint, SilkPtexAtlasTile>
        {
            [7] = new(7, 0, 0, 1, 1, u0, v0, u1, v1, -1, -1, -1, -1, 0, 0, 0, 0)
        });

    private static OpenUsdSilkPage CreateUnmappedPtexPage(
        SilkPtexMappingStatus status,
        string asset = "unmapped.ptx",
        float? displacement = null,
        ulong revision = 1,
        int faceIndex = 0,
        float[]? coordinates = null)
    {
        byte[] mesh = CreateMeshUpsert("/Mesh/PtexFallback", "/Material/PtexFallback");
        if (status == SilkPtexMappingStatus.Mapped)
        {
            int offset = mesh.Length;
            Array.Resize(ref mesh, mesh.Length + 28);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), checked((uint)mesh.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(268), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(offset), checked((uint)faceIndex));
            // The three corners cover the face interior.
            float[] mappingCoordinates = coordinates ?? [0.25f, 0.25f, 0.5f, 0.75f, 0.75f, 0.25f];
            for (int component = 0; component < mappingCoordinates.Length; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    mesh.AsSpan(offset + 4 + (component * 4)), mappingCoordinates[component]);
            }
        }
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(272), (uint)status);
        return new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, revision,
            [
                .. CreatePtexFallbackMaterial(asset, displacement),
                .. mesh
            ], 2);
    }

    private static byte[] CreatePtexFallbackMaterial(string asset, float? displacement)
    {
        (SilkMaterialParameter Parameter, float[] Values)[] scalars = displacement is { } amount
            ? [(SilkMaterialParameter.Displacement, [amount])]
            : [];
        return CreateMaterialUpsert("/Material/PtexFallback", SilkSurfaceKind.PreviewSurface, scalars,
            [new TextureSpec(SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat, SilkTextureWrap.Repeat, SilkColorSpace.Raw,
                3, [1, 1, 1, 1], [0, 0, 0, 0], [1, 0, 1, 1], asset, "",
                SourceKind: SilkTextureSourceKind.Ptex)]);
    }

    private static OpenUsdSilkPage CreateMeshPage(byte[] mesh, ulong revision) =>
        new(SilkCommandParser.PageAbiVersion, revision, mesh, 1);

    private static byte[] WithNormalCorrespondence(
        byte[] mesh,
        int[] identities,
        int[] corners,
        float[]? extraPoints = null,
        bool triangleLocal = false)
    {
        extraPoints ??= [];
        int offset = mesh.Length;
        Array.Resize(ref mesh, checked(offset + 12 +
            (identities.Length * sizeof(uint)) +
            (corners.Length * sizeof(uint)) +
            (extraPoints.Length * sizeof(float))));
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), checked((uint)mesh.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            mesh.AsSpan(offset),
            checked((uint)identities.Length) | (triangleLocal ? 0x80000000u : 0));
        BinaryPrimitives.WriteUInt32LittleEndian(
            mesh.AsSpan(offset + 4), checked((uint)(corners.Length / 3)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            mesh.AsSpan(offset + 8), checked((uint)(extraPoints.Length / 3)));
        int cursor = offset + 12;
        foreach (int identity in identities)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), checked((uint)identity));
            cursor += sizeof(uint);
        }
        foreach (int corner in corners)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(cursor), checked((uint)corner));
            cursor += sizeof(uint);
        }
        foreach (float component in extraPoints)
        {
            BinaryPrimitives.WriteSingleLittleEndian(mesh.AsSpan(cursor), component);
            cursor += sizeof(float);
        }
        return mesh;
    }

    private static float ReadSingle(byte[] constants, int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(constants.AsSpan(offset, sizeof(float))));

    private static TextureSpec ScalarTexture(
        SilkMaterialParameter parameter,
        string asset,
        SilkTextureChannel channel) =>
        new(
            parameter,
            SilkTextureWrap.Repeat,
            SilkTextureWrap.Repeat,
            SilkColorSpace.Raw,
            ComponentCount: 1,
            Scale: [1f, 1f, 1f, 1f],
            Bias: [0f, 0f, 0f, 0f],
            Fallback: [0.5f, 0.5f, 0.5f, 1f],
            Asset: asset,
            UvPrimvar: "st",
            Channel: channel);


    private static SilkMaterialData CreateMissingMaterial(
        string path,
        float[] fallback) =>
        CopyMaterial(CreateMaterialUpsert(
            path,
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.Repeat,
                    SilkTextureWrap.Repeat,
                    SilkColorSpace.Srgb,
                    ComponentCount: 3,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: fallback,
                    Asset: "shared-missing.png",
                    UvPrimvar: "st"),
            ]));

    private static SilkMaterialData CopyMaterial(byte[] command)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
            command,
            1,
            SilkCommandParser.PageAbiVersion);
        _ = commands.MoveNext();
        return SilkMaterialData.CopyFrom(commands.Current.AsMaterialUpsert());
    }

    private static SilkMaterialData CreateUseMetadataMaterial() =>
        CopyMaterial(CreateMaterialUpsert(
            "/World/Materials/MetadataWrap",
            SilkSurfaceKind.PreviewSurface,
            scalars: [],
            textures:
            [
                new TextureSpec(
                    SilkMaterialParameter.DiffuseColor,
                    SilkTextureWrap.UseMetadata,
                    SilkTextureWrap.UseMetadata,
                    SilkColorSpace.Raw,
                    ComponentCount: 4,
                    Scale: [1f, 1f, 1f, 1f],
                    Bias: [0f, 0f, 0f, 0f],
                    Fallback: [0f, 0f, 0f, 1f],
                    Asset: "metadata-wrap.png",
                    UvPrimvar: "st"),
            ]));

    private sealed record TextureSpec(
        SilkMaterialParameter Parameter,
        SilkTextureWrap WrapS,
        SilkTextureWrap WrapT,
        SilkColorSpace ColorSpace,
        int ComponentCount,
        float[] Scale,
        float[] Bias,
        float[] Fallback,
        string Asset,
        string UvPrimvar,
        SilkTextureChannel? Channel = null,
        SilkCompositeOperator CompositeOperator = SilkCompositeOperator.None,
        float CompositeFactor = 0f,
        SilkTextureSourceKind SourceKind = SilkTextureSourceKind.Standard)
    {
        /// <summary>
        /// The channel a page must carry for this entry. Defaulted from the consumed
        /// width so a test that does not care about channel selection still writes a
        /// page hdSilk could have produced.
        /// </summary>
        internal SilkTextureChannel ResolvedChannel =>
            Channel ??
            (ComponentCount >= 3 ? SilkTextureChannel.Rgb : SilkTextureChannel.R);
    }

    private static byte[] CreateMaterialUpsert(
        string path,
        SilkSurfaceKind kind,
        (SilkMaterialParameter Parameter, float[] Values)[] scalars,
        TextureSpec[] textures,
        byte[]? generatedFragmentSpirV = null,
        byte[]? generatedFragmentMslSource = null,
        float[]? uvTransform = null,
        uint alternateUvMask = 0,
        float[]? alternateUvTransform = null,
        bool omitAlternateUv = false)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(ComputeStableHash(path)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(BitConverter.GetBytes((uint)kind));
        payload.AddRange(BitConverter.GetBytes((uint)scalars.Length));
        payload.AddRange(BitConverter.GetBytes((uint)textures.Length));
        payload.AddRange(pathBytes);

        foreach ((SilkMaterialParameter parameter, float[] values) in scalars)
        {
            payload.AddRange(BitConverter.GetBytes((uint)parameter));
            payload.AddRange(BitConverter.GetBytes((uint)values.Length));
            foreach (float value in values)
            {
                payload.AddRange(BitConverter.GetBytes(value));
            }
        }

        foreach (TextureSpec texture in textures)
        {
            byte[] assetBytes = Encoding.UTF8.GetBytes(texture.Asset);
            byte[] uvBytes = Encoding.UTF8.GetBytes(texture.UvPrimvar);
            payload.AddRange(BitConverter.GetBytes((uint)texture.Parameter));
            payload.AddRange(BitConverter.GetBytes((uint)texture.WrapS));
            payload.AddRange(BitConverter.GetBytes((uint)texture.WrapT));
            payload.AddRange(BitConverter.GetBytes((uint)texture.ColorSpace));
            payload.AddRange(BitConverter.GetBytes((uint)assetBytes.Length));
            payload.AddRange(BitConverter.GetBytes((uint)uvBytes.Length));
            payload.AddRange(BitConverter.GetBytes((uint)texture.ComponentCount));
            foreach (float value in texture.Scale)
            {
                payload.AddRange(BitConverter.GetBytes(value));
            }
            foreach (float value in texture.Bias)
            {
                payload.AddRange(BitConverter.GetBytes(value));
            }
            foreach (float value in texture.Fallback)
            {
                payload.AddRange(BitConverter.GetBytes(value));
            }
            payload.AddRange(BitConverter.GetBytes((uint)texture.ResolvedChannel));
            payload.AddRange(BitConverter.GetBytes((uint)texture.CompositeOperator));
            payload.AddRange(BitConverter.GetBytes(texture.CompositeFactor));
            payload.AddRange(BitConverter.GetBytes((uint)texture.SourceKind));
            payload.AddRange(assetBytes);
            payload.AddRange(uvBytes);
        }

        generatedFragmentSpirV ??= [];
        payload.AddRange(BitConverter.GetBytes((uint)generatedFragmentSpirV.Length));
        payload.AddRange(generatedFragmentSpirV);
        generatedFragmentMslSource ??= [];
        payload.AddRange(BitConverter.GetBytes((uint)generatedFragmentMslSource.Length));
        payload.AddRange(generatedFragmentMslSource);
        uvTransform ??= [1f, 0f, 0f, 1f, 0f, 0f];
        foreach (float element in uvTransform)
        {
            payload.AddRange(BitConverter.GetBytes(element));
        }
        if (!omitAlternateUv)
        {
            payload.AddRange(BitConverter.GetBytes(alternateUvMask));
            foreach (float element in alternateUvTransform ?? [1f, 0f, 0f, 1f, 0f, 0f])
            {
                payload.AddRange(BitConverter.GetBytes(element));
            }
        }

        return CreateCommand(4, payload);
    }

    private static byte[] CreateMaterialRemove(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(ComputeStableHash(path)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(pathBytes);
        return CreateCommand(5, payload);
    }

    private static byte[] CreateMeshUpsert(string pathValue, string materialPath, int primId = 7)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        byte[] material = Encoding.UTF8.GetBytes(materialPath);
        float[] points = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] indices = [0, 1, 2];
        int size = 276 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            material.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (i * 4)), 1);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (i * 8)),
                i % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(208),
            SilkWireFormat.ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(216),
            (uint)material.Length);
        path.CopyTo(bytes, 276);
        int pointsOffset = 276 + path.Length;
        for (int i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (i * sizeof(float))),
                points[i]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (i * sizeof(uint))),
                indices[i]);
        }
        int triangleOffset = indicesOffset + (indices.Length * sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(triangleOffset), 0);
        material.CopyTo(bytes, triangleOffset + sizeof(uint));
        return bytes;
    }

    private static byte[] CreateMeshRemove(string pathValue)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        var bytes = new byte[24 + path.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)path.Length);
        path.CopyTo(bytes, 24);
        return bytes;
    }

    private static byte[] CreateCommand(uint type, List<byte> payload)
    {
        byte[] command = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(
            command.AsSpan(4, 4), (uint)command.Length);
        payload.CopyTo(command, 8);
        return command;
    }

    private static ulong ComputeStableHash(string path)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private sealed class TextureGraphicsDevice :
        SilkGraphicsDeviceLifetimeBase, ISilkGraphicsDevice, ISilkTextureAdmissionDevice,
        ISilkCpuTextureAdmissionDevice, ISilkStagingAdmissionDevice
    {
        internal List<SilkTextureFormat> CreatedTextureFormats { get; } = [];
        internal List<SilkTextureDescriptor> CreatedTextures { get; } = [];
        internal List<SilkSamplerDescriptor> CreatedSamplers { get; } = [];
        internal List<Texture> TextureOwners { get; } = [];
        internal bool SupportsRowUploads { get; set; }
        internal List<(SilkTextureRowUpload Region, byte[] Pixels)> RowUploads { get; } = [];
        internal bool RefuseSubmission { get; set; }
        internal bool RefuseWait { get; set; }
        internal bool RefuseBufferWrites { get; set; }
        internal bool RefuseNewBufferDisposal { get; set; }
        internal int Submissions { get; private set; }
        internal List<byte[]> LastSubmittedUploads { get; } = [];
        internal nuint? RefuseBufferSize { get; set; }
        internal List<TextureGraphicsBuffer> Buffers { get; } = [];

        // Defaults to the behavior-preserving 1x capability so every existing test keeps
        // exercising the "device does not advertise anisotropy" path unless a test opts in.
        internal float MaxSamplerAnisotropy { get; set; } = 1f;
        internal uint MaxTextureDimension2D { get; set; }

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

        public SilkGraphicsCapabilities Capabilities => new(
            "Texture diagnostics test device",
            "test",
            SupportsCompute: false,
            IsSoftware: true)
        {
            MaxSamplerAnisotropy = MaxSamplerAnisotropy,
            MaxTextureDimension2D = MaxTextureDimension2D,
        };

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm)
        {
            CreatedTextureFormats.Add(format);
            return new Texture(new SilkTextureDescriptor(
                width, height, format, SilkTextureDescriptor.GetDefaultUsage(format)));
        }

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
        {
            IDisposable? reservation = ReserveTextureAllocation(descriptor);
            var result = new Texture(descriptor);
            result.OwnTextureReservation(reservation);
            CreatedTextureFormats.Add(descriptor.Format);
            CreatedTextures.Add(descriptor);
            TextureOwners.Add(result);
            return result;
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            if (RefuseBufferSize == size)
            {
                throw new InvalidOperationException("Injected surface buffer allocation refusal.");
            }
            IDisposable? reservation = ReserveBufferAllocation(size);
            TextureGraphicsBuffer buffer;
            try
            {
                buffer = new TextureGraphicsBuffer(size, usage, reservation)
                {
                    RefuseWrite = RefuseBufferWrites,
                    RefuseDisposal = RefuseNewBufferDisposal,
                };
            }
            catch
            {
                reservation?.Dispose();
                throw;
            }
            Buffers.Add(buffer);
            return buffer;
        }

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor)
        {
            descriptor.Validate(Capabilities);
            CreatedSamplers.Add(descriptor);
            return new TextureSampler(descriptor);
        }

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            SupportsRowUploads ? new RowTextureCommandList(this) : new TextureCommandList();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
        {
            Submissions++;
            if (RefuseSubmission)
            {
                throw new SilkGpuStagingBudgetExceededException(16, 0, 15);
            }
            LastSubmittedUploads.Clear();
            if (commandList is TextureCommandList textures)
            {
                LastSubmittedUploads.AddRange(textures.Uploads);
            }
            return new TextureSubmission(this);
        }

        private sealed class TextureSubmission(TextureGraphicsDevice device) : ISilkGraphicsSubmission
        {
            public bool IsCompleted => true;
            public void Wait()
            {
                if (device.RefuseWait)
                {
                    throw new InvalidOperationException("Injected Ptex candidate upload wait failure.");
                }
            }
            public void Dispose() { }
        }

        public void WaitIdle()
        {
        }

        private sealed class TextureSampler(SilkSamplerDescriptor descriptor)
            : ISilkGraphicsSampler
        {
            public SilkSamplerDescriptor Descriptor { get; } = descriptor;

            public void Dispose()
            {
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class Texture(SilkTextureDescriptor descriptor) : SilkGraphicsTextureBase(descriptor)
    {
        internal bool Released { get; private set; }
        public override void ReadbackForTesting(Span<byte> destination) =>
            throw new NotSupportedException();

        public override void ReadbackForTesting(Span<float> destination) =>
            throw new NotSupportedException();

        protected override void ReleaseNative() => Released = true;
    }

    private sealed class TextureGraphicsBuffer(nuint size, SilkBufferUsage usage, IDisposable? reservation = null)
        : ISilkGraphicsBuffer
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];
        private IDisposable? _reservation = reservation;
        internal bool Released { get; private set; }
        internal int Writes { get; private set; }
        internal bool RefuseWrite { get; set; }
        internal bool RefuseDisposal { get; set; }
        internal int SuccessfulDisposals { get; private set; }
        internal Action? DisposalCallback { get; set; }

        public nuint Size => size;

        public SilkBufferUsage Usage => usage;

        public void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            if (RefuseWrite)
            {
                throw new InvalidOperationException("Injected Ptex candidate buffer write failure.");
            }
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));
            Writes++;
        }

        public void ReadbackForTesting(Span<byte> destination) =>
            _bytes.AsSpan(0, destination.Length).CopyTo(destination);

        public void Dispose()
        {
            DisposalCallback?.Invoke();
            if (RefuseDisposal)
            {
                throw new InvalidOperationException("Injected Ptex candidate buffer disposal failure.");
            }
            Released = true;
            SuccessfulDisposals++;
            Interlocked.Exchange(ref _reservation, null)?.Dispose();
        }
    }

    private sealed class RowTextureCommandList(TextureGraphicsDevice device)
        : TextureCommandList, ISilkTextureRowUploadCommandList
    {
        private readonly List<IDisposable> _reservations = [];

        public void UploadTextureRows(ISilkGraphicsTexture texture, SilkTextureRowUpload region,
            ReadOnlySpan<byte> source)
        {
            IDisposable? reservation = device.CpuTextureBudget?.Reserve((ulong)source.Length);
            try
            {
                device.RowUploads.Add((region, source.ToArray()));
                if (reservation is not null)
                {
                    _reservations.Add(reservation);
                    reservation = null;
                }
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        public override void Dispose()
        {
            foreach (IDisposable reservation in _reservations)
            {
                reservation.Dispose();
            }
            _reservations.Clear();
            base.Dispose();
        }
    }

    private sealed class PtexCandidateCommandList : TextureCommandList
    {
        internal ISilkGraphicsBuffer? Lookup { get; private set; }
        internal ISilkGraphicsBuffer? Vertices { get; private set; }
        internal ISilkGraphicsBuffer? Indices { get; private set; }
        internal uint LookupBinding { get; private set; }
        internal uint IndexCount { get; private set; }
        internal uint InstanceCount { get; private set; }

        public override void SetVertexBuffer(ISilkGraphicsBuffer buffer) => Vertices = buffer;
        public override void SetIndexBuffer(ISilkGraphicsBuffer buffer) => Indices = buffer;
        public override void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
            LookupBinding = binding;
            Lookup = buffer;
        }
        public override void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
            IndexCount = indexCount;
            InstanceCount = instanceCount;
        }
    }

    private class TextureCommandList : ISilkGraphicsCommandList
    {
        internal List<byte[]> Uploads { get; } = [];

        internal int UploadCount => Uploads.Count;

        internal List<(uint Binding, SilkSamplerDescriptor Descriptor)> SamplerBindings { get; } = [];

        internal List<uint> TextureBindings { get; } = [];

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            Uploads.Add(source.ToArray());

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color) =>
            throw new NotSupportedException();

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor) =>
            throw new NotSupportedException();

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline) =>
            throw new NotSupportedException();

        public void SetViewport(SilkViewport viewport) =>
            throw new NotSupportedException();

        public void SetScissor(SilkScissor scissor) =>
            throw new NotSupportedException();

        public virtual void SetVertexBuffer(ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public virtual void SetIndexBuffer(ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture) =>
            TextureBindings.Add(binding);

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler) =>
            SamplerBindings.Add((binding, sampler.Descriptor));

        public void DrawIndexed(uint indexCount) =>
            throw new NotSupportedException();

        public virtual void DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
            throw new NotSupportedException();

        public void EndRendering() =>
            throw new NotSupportedException();

        public void SetComputePipeline(ISilkComputePipeline pipeline) =>
            throw new NotSupportedException();

        public virtual void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetComputeUniformBuffer(
            uint setIndex,
            uint binding,
            ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void Dispatch(uint elementCount) =>
            throw new NotSupportedException();

        public void BufferBarrier(ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public virtual void Dispose()
        {
        }
    }
}
