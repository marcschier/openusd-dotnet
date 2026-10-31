// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkWarehouseMdlWrapperTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true, true)]
    public async Task TwoActiveNormalsMatchAnIndependentRnmReference(
        SilkGraphicsBackend backend, bool rotatedWithEmission, bool streamed, bool antipodal)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the ABI-7 adapter and page-27 runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-detail-rnm-").FullName;
        try
        {
            string normal = Path.Combine(root, "normal.png");
            string detail = Path.Combine(root, "detail.png");
            string referenceNormal = Path.Combine(root, "reference.png");
            string emission = Path.Combine(root, "emission.png");
            string mask = Path.Combine(root, "mask.png");
            WriteSolid(normal, [192, 128, 224, 255]);
            WriteSolid(detail, [128, 176, 244, 255]);
            WriteSolid(referenceNormal, [128, 128, 255, 255]);
            WriteSolid(emission, [64, 128, 192, 255]);
            WriteSolid(mask, [128, 64, 192, 255]);
            Vector3 primary = antipodal ? -Vector3.UnitZ : Vector3.Normalize(new Vector3(129, 1, 193) / 255f);
            Vector3 secondary = Vector3.Normalize(new Vector3(1, 97, 233) / 255f);
            if (rotatedWithEmission)
            {
                secondary = new Vector3(-secondary.Y, secondary.X, secondary.Z);
            }
            // Rotate the detail vector from +Z onto the primary normal using Rodrigues' formula.
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, primary);
            Vector3 combined = antipodal ? new Vector3(secondary.X, -secondary.Y, -secondary.Z) :
                Vector3.Normalize(secondary + Vector3.Cross(axis, secondary) +
                    Vector3.Cross(axis, Vector3.Cross(axis, secondary)) / (1 + primary.Z));
            float[] referencePixels = new float[16];
            float[]? primaryPixels = antipodal ? new float[16] : null;
            for (int pixel = 0; pixel < 4; pixel++)
            {
                referencePixels[pixel * 4] = (combined.X + 1) * 0.5f;
                referencePixels[(pixel * 4) + 1] = (combined.Y + 1) * 0.5f;
                referencePixels[(pixel * 4) + 2] = (combined.Z + 1) * 0.5f;
                referencePixels[(pixel * 4) + 3] = 1;
                if (primaryPixels is not null)
                {
                    primaryPixels[pixel * 4] = primaryPixels[(pixel * 4) + 1] = 0.5f;
                    primaryPixels[(pixel * 4) + 3] = 1;
                }
            }
            string materialText(string baseAsset, int detailStrength) => Scene(Material($$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @OmniPBR.mdl@
                uniform token info:mdl:sourceAsset:subIdentifier = "OmniPBR"
                color3f inputs:diffuse_color_constant = (0.2, 0.4, 0.6)
                float inputs:reflection_roughness_constant = 0.6
                float inputs:metallic_constant = 0
                asset inputs:normalmap_texture = @{{baseAsset.Replace('\\', '/')}}@ (colorSpace = "raw")
                float inputs:bump_factor = 1
                bool inputs:flip_tangent_u = false
                bool inputs:flip_tangent_v = false
                asset inputs:detail_normalmap_texture = @{{detail.Replace('\\', '/')}}@ (colorSpace = "raw")
                float inputs:detail_bump_factor = {{detailStrength}}
                float2 inputs:detail_texture_scale = (2, 3)
                float inputs:detail_texture_rotate = {{(rotatedWithEmission ? 90 : 0)}}
                float2 inputs:detail_texture_translate = (0.2, -0.4)
                bool inputs:enable_emission = {{(rotatedWithEmission ? "true" : "false")}}
                float inputs:emissive_intensity = 0.3
                asset inputs:emissive_color_texture = @{{emission.Replace('\\', '/')}}@ (colorSpace = "raw")
                asset inputs:emissive_mask_texture = @{{mask.Replace('\\', '/')}}@ (colorSpace = "raw")
                token outputs:out
                """, "mdl:surface", "out"));
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "base-only.usda");
            await File.WriteAllTextAsync(projected, materialText(normal, 1));
            await File.WriteAllTextAsync(reference, materialText(referenceNormal, 0));
            await File.WriteAllTextAsync(control, materialText(normal, 0));
            SilkMaterialData data = ReadMaterial(plugins, projected);
            await Assert.That(data.Textures.Count).IsEqualTo(rotatedWithEmission ? 4 : 2);
            await Assert.That(data.GetTexture(SilkMaterialParameter.DetailNormal)!.Asset).IsEqualTo(detail);
            await Assert.That(data.GetCompositeTexture()?.Parameter)
                .IsEqualTo(rotatedWithEmission ? SilkMaterialParameter.EmissiveColor : (SilkMaterialParameter?)null);
            using ISilkGraphicsDevice device = CreateDetailDevice(backend);
            new SilkGpuBufferBudget(1_048_576).ConfigureDevice(device);
            new SilkTexturePreparationBudgets(new(1_048_576), new(1_048_576), new(1_048_576)).ConfigureDevice(device);
            byte[] actual = Capture(device, plugins, projected, primaryPixels, normal, streamed);
            byte[] expected = Capture(device, plugins, reference, referencePixels, referenceNormal, streamed);
            byte[] baseOnly = Capture(device, plugins, control, primaryPixels, normal, streamed);
            if (!antipodal)
            {
                await Assert.That(actual.SequenceEqual(baseOnly)).IsFalse();
            }
            float maximumError = 0;
            for (int offset = 0; offset < actual.Length; offset += 2)
            {
                float a = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(
                    actual.AsSpan(offset)));
                float e = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(
                    expected.AsSpan(offset)));
                await Assert.That(float.IsFinite(a) && float.IsFinite(e)).IsTrue();
                maximumError = MathF.Max(maximumError, MathF.Abs(a - e));
            }
            await Assert.That(maximumError).IsLessThanOrEqualTo(0.002f);
        }
        finally { Directory.Delete(root, recursive: true); }

    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task IndependentDetailCoordinatesMatchAuthoredUvCoordinates(SilkGraphicsBackend backend, bool udim)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the ABI-7 adapter and page-27 runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-detail-uv-").FullName;
        try
        {
            string normal = Path.Combine(root, "normal.png");
            string detail = Path.Combine(root, udim ? "detail.<UDIM>.png" : "detail.png");
            WriteSolid(normal, [192, 128, 224, 255]);
            using (FileStream image = File.Create(udim ? detail.Replace("<UDIM>", "1001",
                StringComparison.Ordinal) : detail))
            {
                _ = PngRgba8Writer.Write(image, 2, 2,
                    [220, 128, 240, 255, 80, 128, 250, 255, 128, 200, 244, 255, 128, 72, 246, 255], 1024);
            }
            if (udim)
            {
                WriteSolid(detail.Replace("<UDIM>", "1002", StringComparison.Ordinal), [192, 96, 224, 255]);
            }
            string body = Scene(Material($$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @OmniPBR.mdl@
                uniform token info:mdl:sourceAsset:subIdentifier = "OmniPBR"
                color3f inputs:diffuse_color_constant = (0.2, 0.4, 0.6)
                float inputs:reflection_roughness_constant = 0.6
                asset inputs:normalmap_texture = @{{normal.Replace('\\', '/')}}@ (colorSpace = "raw")
                asset inputs:detail_normalmap_texture = @{{detail.Replace('\\', '/')}}@ (colorSpace = "raw")
                float inputs:bump_factor = 1
                float inputs:detail_bump_factor = 1
                bool inputs:flip_tangent_u = false
                bool inputs:flip_tangent_v = false
                __DETAIL_COORDINATES__
                token outputs:out
                """, "mdl:surface", "out"));
            string transform = udim
                ? "float2 inputs:detail_texture_scale = (1.5, 0.75)\nfloat2 inputs:detail_texture_translate = " +
                    "(0.25, 0.125)"
                : "float2 inputs:detail_texture_scale = (2, 3)\nfloat2 inputs:detail_texture_translate = (0.2, -0.4)";
            string coordinates = udim
                ? "[(0.25, 0.125), (1.75, 0.125), (1.75, 0.875), (0.25, 0.875)]"
                : "[(0.2, -0.4), (2.2, -0.4), (2.2, 2.6), (0.2, 2.6)]";
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "authored-uv.usda");
            string control = Path.Combine(root, "identity-uv.usda");
            string identity = body.Replace("__DETAIL_COORDINATES__", "", StringComparison.Ordinal);
            await File.WriteAllTextAsync(projected, body.Replace("__DETAIL_COORDINATES__", transform,
                StringComparison.Ordinal));
            await File.WriteAllTextAsync(reference, identity.Replace(
                "[(0, 0), (1, 0), (1, 1), (0, 1)]", coordinates, StringComparison.Ordinal));
            await File.WriteAllTextAsync(control, identity);
            using ISilkGraphicsDevice device = CreateDetailDevice(backend);
            new SilkGpuBufferBudget(1_048_576).ConfigureDevice(device);
            new SilkTexturePreparationBudgets(new(1_048_576), new(1_048_576), new(1_048_576)).ConfigureDevice(device);
            byte[] actual = Capture(device, plugins, projected, streamMaterials: true);
            byte[] expected = Capture(device, plugins, reference, streamMaterials: true);
            byte[] untransformed = Capture(device, plugins, control, streamMaterials: true);
            await Assert.That(actual.SequenceEqual(untransformed)).IsFalse();
            float maximumError = 0;
            for (int offset = 0; offset < actual.Length; offset += 2)
            {
                float a = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(
                    actual.AsSpan(offset)));
                float e = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(
                    expected.AsSpan(offset)));
                await Assert.That(float.IsFinite(a) && float.IsFinite(e)).IsTrue();
                maximumError = MathF.Max(maximumError, MathF.Abs(a - e));
            }
            await Assert.That(maximumError).IsLessThanOrEqualTo(0.002f);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void WriteSolid(string path, byte[] pixel)
    {
        byte[] pixels = new byte[16];
        for (int index = 0; index < 4; index++)
        {
            pixel.CopyTo(pixels, index * 4);
        }
        using FileStream output = File.Create(path);
        _ = PngRgba8Writer.Write(output, 2, 2, pixels, 1024);
    }

    private static ISilkGraphicsDevice CreateDetailDevice(SilkGraphicsBackend backend)
    {
        bool hardware = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_HARDWARE_REQUIRED") == "1";
        ISilkGraphicsDevice device = hardware && backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows()
            ? D3D12SilkGraphicsDevice.Create(useWarp: false)
            : SilkDepthCaptureConformance.CreateDevice(backend);
        if (hardware && device.Capabilities.IsSoftware)
        {
            string name = device.Capabilities.DeviceName;
            device.Dispose();
            throw new InvalidOperationException($"Required hardware, but {backend} selected '{name}'.");
        }
        Console.WriteLine(
            $"DETAIL_NORMAL_DEVICE backend={backend} name={device.Capabilities.DeviceName} " +
                $"software={device.Capabilities.IsSoftware}");
        return device;
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task ZeroAoWeightPreservesNeutralPixelsAndOtherOrmChannels(SilkGraphicsBackend backend, bool authored)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the SDK adapter, modules and matched runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-zero-ao-").FullName;
        try
        {
            string image = Path.Combine(root, "maps.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2,
                    [64, 120, 60, 255, 64, 180, 40, 255, 64, 80, 200, 255, 64, 40, 80, 255], 1024);
            }
            string asset = image.Replace('\\', '/');
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string active = Path.Combine(root, "active.usda");
            string zeroWeight = authored ? "float inputs:ao_to_diffuse = 0" : "";
            await File.WriteAllTextAsync(projected, Scene(Material($$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                bool inputs:enable_ORM_texture = true
                asset inputs:ORM_texture = @{{asset}}@
                float inputs:reflection_roughness_constant = 0.2
                float inputs:reflection_roughness_texture_influence = 0.25
                float inputs:metallic_constant = 0.8
                float inputs:metallic_texture_influence = 0.5
                {{zeroWeight}}
                token outputs:out
                """, "mdl:surface", "out")));
            await File.WriteAllTextAsync(reference, Scene(ScalarMapPreview(asset, true, false,
                includeOcclusion: false)));
            await File.WriteAllTextAsync(active, Scene(ScalarMapPreview(asset, true, false)));
            SilkMaterialData material = ReadMaterial(plugins, projected);
            await Assert.That(material.Textures.Count).IsEqualTo(2);
            await Assert.That(material.GetTexture(SilkMaterialParameter.Occlusion)).IsNull();
            await Assert.That(material.GetTexture(SilkMaterialParameter.Roughness)!.Channel).IsEqualTo(
                SilkTextureChannel.G);
            await Assert.That(material.GetTexture(SilkMaterialParameter.Metallic)!.Channel).IsEqualTo(
                SilkTextureChannel.B);
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            byte[] pixels = Capture(device, plugins, projected);
            await Assert.That(pixels.SequenceEqual(Capture(device, plugins, reference))).IsTrue();
            await Assert.That(pixels.SequenceEqual(Capture(device, plugins, active))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task SdkSpecularLevelReachesHdrAsAWeightedDielectricLayer(
        SilkGraphicsBackend backend, bool environment)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the ABI-7 adapter and page-27 runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-specular-").FullName;
        try
        {
            if (environment)
            {
                byte[] header = System.Text.Encoding.ASCII.GetBytes(
                    "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 4\n");
                byte[] sky = new byte[header.Length + 32];
                header.CopyTo(sky, 0);
                for (int index = header.Length; index < sky.Length; index += 4)
                {
                    sky[index] = sky[index + 1] = sky[index + 2] = 128;
                    sky[index + 3] = 129;
                }
                await File.WriteAllBytesAsync(Path.Combine(root, "sky.hdr"), sky);
            }
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304)).ConfigureDevice(device);
            byte[][] images = new byte[3][];
            float[] levels = [0, 1, 0.25f];
            for (int index = 0; index < levels.Length; index++)
            {
                float level = levels[index];
                string text = Scene(Material(
                    $$"""
                    uniform token info:implementationSource = "sourceAsset"
                    uniform asset info:mdl:sourceAsset = @OmniPBR.mdl@
                    uniform token info:mdl:sourceAsset:subIdentifier = "OmniPBR"
                    color3f inputs:diffuse_color_constant = (0.2,0.4,0.6)
                    float inputs:reflection_roughness_constant = 0.6
                    float inputs:metallic_constant = 0.35
                    float inputs:specular_level = {{level.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
                    token outputs:out
                    """, "mdl:surface", "out"));
                if (environment)
                {
                    text += "\ndef DomeLight \"Sky\" {\nasset inputs:texture:file = @sky.hdr@\n" +
                        "token inputs:texture:format = \"latlong\"\nfloat inputs:intensity = 1\n}\n";
                }
                string path = Path.Combine(root, $"level-{index}.usda");
                await File.WriteAllTextAsync(path, text);
                SilkMaterialData material = ReadMaterial(plugins, path);
                await Assert.That(material.SurfaceKind).IsEqualTo(SilkSurfaceKind.MdlDistilled);
                await Assert.That(material.GetScalar(SilkMaterialParameter.OmniPbrSpecularLevel)[0]).IsEqualTo(level);
                images[index] = Capture(device, plugins, path);
                await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(text);
            }
            await Assert.That(images[0].SequenceEqual(images[1])).IsFalse();
            for (int offset = 0; offset < images[0].Length; offset += 2)
            {
                float zero = (float)BinaryPrimitives.ReadHalfLittleEndian(images[0].AsSpan(offset));
                float one = (float)BinaryPrimitives.ReadHalfLittleEndian(images[1].AsSpan(offset));
                float quarter = (float)BinaryPrimitives.ReadHalfLittleEndian(images[2].AsSpan(offset));
                await Assert.That(quarter).IsEqualTo(zero * 0.75f + one * 0.25f).Within(0.001f);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task HighRangeSrgbTextureRetainsLinearFloatPrecision(SilkGraphicsBackend backend)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the matched imaging runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string root = Directory.CreateTempSubdirectory("srgb-float-precision-").FullName;
        try
        {
            byte[] colors = [4, 12, 64, 64, 32, 128, 192, 128, 15, 75, 200, 192, 255, 220, 180, 255];
            string image = Path.Combine(root, "srgb.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2, colors, 1024);
            }
            float[] linear = new float[16];
            for (int index = 0; index < colors.Length; index++)
            {
                float value = colors[index] / 255f;
                linear[index] = index % 4 == 3 ? value : value <= 0.04045f
                    ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
            }
            string material = TexturedPreview(image, "1000, 1000, 1000, 1")
                .Replace("inputs:diffuseColor.connect", "inputs:emissiveColor.connect", StringComparison.Ordinal);
            string projected = Path.Combine(root, "srgb.usda");
            string reference = Path.Combine(root, "linear.usda");
            await File.WriteAllTextAsync(projected, Scene(material.Replace("\"raw\"", "\"sRGB\"",
                StringComparison.Ordinal)));
            await File.WriteAllTextAsync(reference, Scene(material));
            await Assert.That(ReadMaterial(plugins, projected).Textures.Single().SourceColorSpace)
                .IsEqualTo(SilkColorSpace.Srgb);
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] expected = Capture(device, plugins, reference, linear);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "constant")]
    [Arguments(SilkGraphicsBackend.Vulkan, "constant")]
    [Arguments(SilkGraphicsBackend.D3D12, "texture")]
    [Arguments(SilkGraphicsBackend.Vulkan, "texture")]
    [Arguments(SilkGraphicsBackend.D3D12, "mask")]
    [Arguments(SilkGraphicsBackend.Vulkan, "mask")]
    [Arguments(SilkGraphicsBackend.D3D12, "both")]
    [Arguments(SilkGraphicsBackend.Vulkan, "both")]
    public async Task DiffuseEmissionRadianceAndMasksMatchIndependentHdrControls(
        SilkGraphicsBackend backend, string kind)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the ABI-5 adapter and matched imaging runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-emission-").FullName;
        try
        {
            byte[] colors = [255, 0, 0, 64, 0, 255, 0, 128, 0, 0, 255, 192, 255, 255, 255, 255];
            byte[] maskPixels = [128, 255, 64, 0, 128, 255, 64, 0, 128, 255, 64, 0, 128, 255, 64, 0];
            string image = Path.Combine(root, "emission.png");
            string mask = Path.Combine(root, "mask.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2, colors, 1024);
            }
            using (var output = File.Create(mask))
            {
                _ = PngRgba8Writer.Write(output, 2, 2, maskPixels, 1024);
            }
            bool hasTexture = kind is "texture" or "both";
            bool hasMask = kind is "mask" or "both";
            string textureInput = hasTexture
                ? $"asset inputs:emissive_color_texture = @{image.Replace('\\', '/')}@ (colorSpace = \"raw\")" : "";
            string maskInput = hasMask
                ? $"asset inputs:emissive_mask_texture = @{mask.Replace('\\', '/')}@ (colorSpace = \"raw\")" : "";
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "unlit.usda");
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                bool inputs:enable_emission = true
                color3f inputs:emissive_color = (0.25, 0.5, 1)
                float inputs:emissive_intensity = 1000
                {{textureInput}}
                {{maskInput}}
                token outputs:out
                """, "mdl:surface", "out")));
            const float inversePi = 0.31830988618379067154f;
            float[] radiance = [250f * inversePi, 500f * inversePi, 1000f * inversePi];
            float[] referenceTexels = new float[16];
            for (int pixel = 0; pixel < 4; pixel++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = hasTexture
                        ? colors[(pixel * 4) + channel] / 255f * (1000f * inversePi) : radiance[channel];
                    referenceTexels[(pixel * 4) + channel] = kind == "mask"
                        ? value * (maskPixels[channel] / 255f) : value;
                }
                referenceTexels[(pixel * 4) + 3] = 1;
            }
            string plain = Preview("0.12, 0.34, 0.56", "0.21", "0.84");
            string expected = kind == "constant" ? plain.Replace("float inputs:roughness",
                string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"color3f inputs:emissiveColor = ({radiance[0]:R}, {radiance[1]:R}, {radiance[2]:R})\n") +
                    "float inputs:roughness", StringComparison.Ordinal)
                : kind == "both" ? EmissionProductReference(image, mask)
                : TexturedPreview(image, "1, 1, 1, 1")
                    .Replace("inputs:diffuseColor.connect", "inputs:emissiveColor.connect", StringComparison.Ordinal)
                    .Replace("float inputs:roughness",
                        "color3f inputs:diffuseColor = (0.12, 0.34, 0.56)\nfloat inputs:roughness",
                        StringComparison.Ordinal);
            await File.WriteAllTextAsync(reference, Scene(expected));
            await File.WriteAllTextAsync(control, Scene(plain));
            SilkMaterialData data = ReadMaterial(plugins, projected);
            await Assert.That(data.Textures.Count).IsEqualTo(kind == "both" ? 2 : kind == "constant" ? 0 : 1);
            if (kind == "both")
            {
                await Assert.That(data.Textures.Count(t => t.CompositeOperator == SilkCompositeOperator.Multiply))
                    .IsEqualTo(1);
                await Assert.That(ReadMaterial(plugins, reference).Textures.Count).IsEqualTo(2);
            }
            byte[] source = await File.ReadAllBytesAsync(projected);
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            var budgets = new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304));
            budgets.ConfigureDevice(device);
            byte[] expectedPixels = Capture(device, plugins, reference, kind == "constant" ? null : referenceTexels,
                kind == "both" ? image : null);
            byte[] unlit = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(expectedPixels)).IsTrue();
            await Assert.That(actual.SequenceEqual(unlit)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(projected)).SequenceEqual(source)).IsTrue();
            await Assert.That(budgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budgets.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budgets.GpuStagingBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task CutoutThresholdEqualityPreservesFullyOpaqueCoverage(SilkGraphicsBackend backend)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the matched imaging runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string root = Directory.CreateTempSubdirectory("preview-cutout-boundary-").FullName;
        try
        {
            string expected = Path.Combine(root, "opaque.usda");
            string equal = Path.Combine(root, "equal.usda");
            string below = Path.Combine(root, "below.usda");
            string material = Preview("0.12, 0.34, 0.56", "0.21", "0.84");
            await File.WriteAllTextAsync(expected, Scene(material));
            string thresholded = material.Replace("float inputs:roughness",
                "float inputs:opacityThreshold = 0.5\nfloat inputs:opacity = 0.5\nfloat inputs:roughness",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(equal, Scene(thresholded));
            await File.WriteAllTextAsync(below, Scene(thresholded.Replace(
                "inputs:opacity = 0.5", "inputs:opacity = 0.49", StringComparison.Ordinal)));
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] opaque = Capture(device, plugins, expected);
            await Assert.That(Capture(device, plugins, equal).SequenceEqual(opaque)).IsTrue();
            await Assert.That(Capture(device, plugins, below).SequenceEqual(opaque)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 1, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 2, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 2, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 3, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 3, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 1, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1, true)]
    public async Task OpacityMonoModesAndCutoutMatchIndependentScalarTexels(
        SilkGraphicsBackend backend, int mode, bool cutout)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with matching optional MDL and imaging runtimes.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-opacity-").FullName;
        try
        {
            byte[] texels = [255, 0, 0, 0, 255, 255, 0, 255, 0, 0, 255, 0, 255, 255, 255, 255];
            string image = Path.Combine(root, "opacity.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2, texels, 1024);
            }
            float[] selected = mode switch
            {
                0 => [0, 1, 0, 1],
                1 => [1f / 3f, 2f / 3f, 1f / 3f, 1],
                2 => [0.212671f, 0.212671f + 0.715160f, 0.072169f, 1],
                _ => [1, 1, 1, 1]
            };
            float[] referenceTexels = new float[16];
            for (int pixel = 0; pixel < 4; pixel++)
            {
                float value = cutout ? selected[pixel] >= 0.5f ? 1 : 0 : selected[pixel];
                Array.Fill(referenceTexels, value, pixel * 4, 4);
            }
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string threshold = cutout ? "0.5" : "0";
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                bool inputs:enable_opacity = true
                bool inputs:enable_opacity_texture = true
                asset inputs:opacity_texture = @{{image.Replace('\\', '/')}}@ (colorSpace = "raw")
                int inputs:opacity_mode = {{mode}}
                float inputs:opacity_threshold = {{threshold}}
                token outputs:out
                """, "mdl:surface", "out")));
            string channel = mode == 0 ? "a" : "r";
            string preview = TexturedPreview(image, "1, 1, 1, 1").Replace(
                "color3f inputs:diffuseColor.connect = </World/Material/Texture.outputs:rgb>",
                "color3f inputs:diffuseColor = (0.12, 0.34, 0.56)\n" +
                $"float inputs:opacity.connect = </World/Material/Texture.outputs:{channel}>\n" +
                $"float inputs:opacityThreshold = {threshold}", StringComparison.Ordinal)
                .Replace("float3 outputs:rgb", $"float outputs:{channel}", StringComparison.Ordinal);
            await File.WriteAllTextAsync(reference, Scene(preview));
            SilkTextureChannel[] channels = [SilkTextureChannel.A, SilkTextureChannel.AverageRgb,
                SilkTextureChannel.LuminanceRgb, SilkTextureChannel.MaximumRgb];
            await Assert.That(ReadMaterial(plugins, projected).Textures.Single().Channel).IsEqualTo(channels[mode]);
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] expected = Capture(device, plugins, reference, mode == 0 ? null : referenceTexels);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0.25f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.25f)]
    [Arguments(SilkGraphicsBackend.D3D12, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, -0.3f)]
    [Arguments(SilkGraphicsBackend.Vulkan, -0.3f)]
    [Arguments(SilkGraphicsBackend.D3D12, 1.3f)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1.3f)]
    public async Task DesaturationAndGradingMatchIndependentlyPremixedFloatTexels(
        SilkGraphicsBackend backend, float amount)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the current adapter and matched imaging runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-desaturation-").FullName;
        try
        {
            byte[] texels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
            string image = Path.Combine(root, "albedo.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2, texels, 1024);
            }
            float[] referenceTexels = new float[16];
            float[] luminance = [0.212671f, 0.715160f, 0.072169f, 1f];
            for (int pixel = 0; pixel < 4; pixel++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    referenceTexels[(pixel * 4) + channel] = (texels[(pixel * 4) + channel] / 255f * (1 - amount)) +
                        (luminance[pixel] * amount);
                }
                referenceTexels[(pixel * 4) + 3] = 1;
            }
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string factor = amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                asset inputs:diffuse_texture = @{{image.Replace('\\', '/')}}@ (colorSpace = "raw")
                float inputs:albedo_desaturation = {{factor}}
                float inputs:albedo_brightness = 2
                float inputs:albedo_add = -0.25
                color3f inputs:diffuse_tint = (0.5, 0.25, 1.5)
                token outputs:out
                """, "mdl:surface", "out")));
            await File.WriteAllTextAsync(reference, Scene(TexturedPreview(image, "1, 0.5, 3, 1").Replace(
                "float4 inputs:scale = (1, 0.5, 3, 1)",
                "float4 inputs:scale = (1, 0.5, 3, 1)\nfloat4 inputs:bias = (-0.125, -0.0625, -0.375, 0)",
                StringComparison.Ordinal)));
            float[] mix = ReadMaterial(plugins, projected).GetScalar(
                SilkMaterialParameter.DiffuseTextureLuminanceMix).ToArray();
            await Assert.That(mix.SequenceEqual(new[] { 0.212671f, 0.715160f, 0.072169f, amount })).IsTrue();
            byte[] source = await File.ReadAllBytesAsync(projected);
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            var budgets = new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304));
            budgets.ConfigureDevice(device);
            byte[] expected = Capture(device, plugins, reference, referenceTexels);
            byte[] unmixed = Capture(device, plugins, reference);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            await Assert.That(actual.SequenceEqual(unmixed)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(projected)).SequenceEqual(source)).IsTrue();
            await Assert.That(budgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budgets.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budgets.GpuStagingBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 0.25f, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.25f, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 3f, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 3f, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 0.25f, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 0.25f, true)]
    [Arguments(SilkGraphicsBackend.D3D12, 3f, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 3f, true)]
    public async Task NormalStrengthAndFlipsMatchExplicitUnclippedNormalAffine(
        SilkGraphicsBackend backend, float strength, bool multicolor)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the SDK adapter and matched runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-normal-strength-").FullName;
        try
        {
            string image = Path.Combine(root, "normal.png");
            using (var output = File.Create(image))
            {
                if (multicolor)
                {
                    _ = PngRgba8Writer.Write(output, 2, 2,
                        [220, 160, 230, 255, 70, 180, 230, 255, 180, 70, 230, 255, 80, 60, 230, 255], 1024);
                }
                else
                {
                    _ = PngRgba8Writer.Write(output, 1, 1, [220, 160, 230, 255], 1024);
                }
            }
            string asset = image.Replace('\\', '/');
            string factor = strength.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string projected = Path.Combine(root, "projected.usda");
            string expected = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "unmodified.usda");
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                asset inputs:normalmap_texture = @{{asset}}@ (colorSpace = "raw")
                float inputs:bump_factor = {{factor}}
                bool inputs:flip_tangent_u = true
                bool inputs:flip_tangent_v = false
                token outputs:out
                """, "mdl:surface", "out")));
            string reference = TexturedPreview(image, "2, 2, 2, 1").Replace(
                "inputs:diffuseColor.connect", "inputs:normal.connect", StringComparison.Ordinal)
                .Replace("float inputs:roughness", "color3f inputs:diffuseColor = (0.12, 0.34, 0.56)\n" +
                    "float inputs:roughness", StringComparison.Ordinal);
            // UsdPreviewSurface normal maps author their decode to [-1, 1]; hdSilk folds it into the
            // encoded space the MDL projection publishes directly.
            await File.WriteAllTextAsync(control, Scene(reference.Replace("float4 inputs:scale = (2, 2, 2, 1)",
                "float4 inputs:scale = (2, 2, 2, 1)\nfloat4 inputs:bias = (-1, -1, -1, 0)",
                StringComparison.Ordinal)));
            string scale = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"float4 inputs:scale = ({-2f * strength:R}, {2f * strength:R}, 2, 1)");
            string bias = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"float4 inputs:bias = ({strength:R}, {-strength:R}, -1, 0)");
            reference = reference.Replace("float4 inputs:scale = (2, 2, 2, 1)", scale + "\n" + bias,
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(expected, Scene(reference));
            SilkMaterialTexture normal = ReadMaterial(plugins, projected).Textures.Single();
            await Assert.That(normal.Parameter).IsEqualTo(SilkMaterialParameter.Normal);
            await Assert.That(normal.Scale.SequenceEqual(new[] { -strength, strength, 1f, 1f })).IsTrue();
            await Assert.That(normal.Bias.SequenceEqual(new[]
                { 0.5f + 0.5f * strength, 0.5f - 0.5f * strength, 0, 0 })).IsTrue();
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] expectedPixels = Capture(device, plugins, expected);
            byte[] originalPixels = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(expectedPixels)).IsTrue();
            await Assert.That(actual.SequenceEqual(originalPixels)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task RotatedNormalMapBasisMatchesBakedTextureCoordinates(SilkGraphicsBackend backend, bool mirrored)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the current SDK adapter and matched runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-uv-normal-").FullName;
        try
        {
            string image = Path.Combine(root, "normal.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 1, 1, [204, 153, 230, 255], 1024);
            }
            string asset = image.Replace('\\', '/');
            string projected = Path.Combine(root, "projected.usda");
            string expected = Path.Combine(root, "baked.usda");
            string control = Path.Combine(root, "unrotated.usda");
            string material = Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                asset inputs:normalmap_texture = @{{asset}}@ (colorSpace = "raw")
                __TRANSFORM__
                token outputs:out
                """, "mdl:surface", "out");
            string transform = mirrored ? "float2 inputs:texture_scale = (-2, 3)" :
                "float2 inputs:texture_scale = (2, 3)\nfloat inputs:texture_rotate = 90";
            await File.WriteAllTextAsync(projected, Scene(material.Replace("__TRANSFORM__", transform,
                StringComparison.Ordinal)));
            string identity = Scene(material.Replace("__TRANSFORM__", "", StringComparison.Ordinal));
            await File.WriteAllTextAsync(control, identity);
            float radians = mirrored ? 0 : 90f / 180f * MathF.PI;
            float cosine = MathF.Cos(radians);
            float sine = MathF.Sin(radians);
            float sx = mirrored ? -2 : 2;
            string bakedUvs = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[(0, 0), ({sx * cosine:R}, {-3 * sine:R}), " +
                $"({sx * cosine + sx * sine:R}, {-3 * sine + 3 * cosine:R}), ({sx * sine:R}, {3 * cosine:R})]");
            await File.WriteAllTextAsync(expected, identity.Replace(
                "[(0, 0), (1, 0), (1, 1), (0, 1)]", bakedUvs,
                StringComparison.Ordinal));
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] reference = Capture(device, plugins, expected);
            byte[] unrotated = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(reference)).IsTrue();
            await Assert.That(actual.SequenceEqual(unrotated)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task AuthoredUvTransformMatchesClockwiseThenScaleTextureCoordinates(SilkGraphicsBackend backend)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the current SDK adapter and matched hdSilk loader.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-uv-").FullName;
        try
        {
            string image = Path.Combine(root, "uv.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2,
                    [255, 30, 0, 255, 10, 255, 40, 255, 20, 0, 255, 255, 255, 220, 10, 255], 1024);
            }
            string projected = Path.Combine(root, "projected.usda");
            string expected = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "identity.usda");
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                asset inputs:diffuse_texture = @{{image.Replace('\\', '/')}}@ (colorSpace = "raw")
                float2 inputs:texture_scale = (2, 3)
                float inputs:texture_rotate = 90
                float2 inputs:texture_translate = (0.25, -0.5)
                token outputs:out
                """, "mdl:surface", "out")));
            string preview = TexturedPreview(image, "1, 1, 1, 1");
            string nodes = """
                def Shader "Rotate" {
                    uniform token info:id = "UsdTransform2d"
                    float inputs:rotation = -90
                    float2 inputs:in.connect = </World/Material/St.outputs:result>
                    float2 outputs:result
                }
                def Shader "Place" {
                    uniform token info:id = "UsdTransform2d"
                    float2 inputs:scale = (2, 3)
                    float2 inputs:translation = (0.25, -0.5)
                    float2 inputs:in.connect = </World/Material/Rotate.outputs:result>
                    float2 outputs:result
                }
                """;
            string transformed = preview.Replace(
                "float2 inputs:st.connect = </World/Material/St.outputs:result>",
                "float2 inputs:st.connect = </World/Material/Place.outputs:result>", StringComparison.Ordinal);
            transformed = transformed.Insert(transformed.LastIndexOf('}'), "\n" + nodes + "\n");
            await File.WriteAllTextAsync(expected, Scene(transformed));
            await File.WriteAllTextAsync(control, Scene(preview));
            SilkMaterialData material = ReadMaterial(plugins, projected);
            SilkMaterialData referenceMaterial = ReadMaterial(plugins, expected);
            for (int index = 0; index < 6; index++)
            {
                await Assert.That(MathF.Abs(material.UvTransform[index] - referenceMaterial.UvTransform[index]))
                    .IsLessThan(0.00001f);
            }
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] reference = Capture(device, plugins, expected);
            byte[] identity = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(reference)).IsTrue();
            await Assert.That(actual.SequenceEqual(identity)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task ScalarTextureInfluenceReachesHdrWithoutDuplicateOrmInputs(
        SilkGraphicsBackend backend, bool packed)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the SDK adapter, modules and matched runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-influence-").FullName;
        try
        {
            string image = Path.Combine(root, "maps.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2,
                    [180, 120, 60, 255, 120, 180, 40, 255, 40, 80, 200, 255, 200, 40, 80, 255], 1024);
            }
            string projected = Path.Combine(root, "projected.usda");
            string expected = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "full-strength.usda");
            string asset = image.Replace('\\', '/');
            string orm = packed
                ? $"bool inputs:enable_ORM_texture = true\nasset inputs:ORM_texture = @{asset}@\nfloat " +
                    $"inputs:ao_to_diffuse = 1"
                : "";
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                float inputs:reflection_roughness_constant = 0.2
                float inputs:reflection_roughness_texture_influence = 0.25
                asset inputs:reflectionroughness_texture = @{{asset}}@
                float inputs:metallic_constant = 0.8
                float inputs:metallic_texture_influence = 0.5
                asset inputs:metallic_texture = @{{asset}}@
                {{orm}}
                token outputs:out
                """, "mdl:surface", "out")));
            await File.WriteAllTextAsync(expected, Scene(ScalarMapPreview(asset, packed, false)));
            await File.WriteAllTextAsync(control, Scene(ScalarMapPreview(asset, packed, true)));
            SilkMaterialData actualMaterial = ReadMaterial(plugins, projected);
            SilkMaterialData referenceMaterial = ReadMaterial(plugins, expected);
            await Assert.That(actualMaterial.Textures.Count).IsEqualTo(packed ? 3 : 2);
            foreach (SilkMaterialTexture referenceTexture in referenceMaterial.Textures)
            {
                SilkMaterialTexture texture = actualMaterial.Textures.Single(
                    entry => entry.Parameter == referenceTexture.Parameter);
                await Assert.That(texture.Scale.SequenceEqual(referenceTexture.Scale)).IsTrue();
                await Assert.That(texture.Bias.SequenceEqual(referenceTexture.Bias)).IsTrue();
                await Assert.That(texture.Channel).IsEqualTo(referenceTexture.Channel);
                await Assert.That(texture.SourceColorSpace).IsEqualTo(referenceTexture.SourceColorSpace);
            }
            byte[] authored = await File.ReadAllBytesAsync(projected);
            using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] reference = Capture(device, plugins, expected);
            byte[] full = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.SequenceEqual(reference)).IsTrue();
            await Assert.That(actual.SequenceEqual(full)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(projected)).SequenceEqual(authored)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, true)]
    public async Task DiffuseTintReachesConstantAndTexturedHdrPixels(
        SilkGraphicsBackend backend, bool textured, bool hardware, bool graded)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the SDK adapter, modules and matched runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-tint-").FullName;
        try
        {
            string image = Path.Combine(root, "albedo.png");
            using (var output = File.Create(image))
            {
                _ = PngRgba8Writer.Write(output, 2, 2,
                    [180, 120, 60, 255, 120, 180, 40, 255, 40, 80, 200, 255, 200, 40, 80, 255], 1024);
            }
            string projected = Path.Combine(root, "projected.usda");
            string expected = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "untinted.usda");
            string texture = textured
                ? $"asset inputs:diffuse_texture = @{image.Replace('\\', '/')}@ (colorSpace = \"raw\")"
                : string.Empty;
            const string tint = "0.5, 0.25, 1.5";
            string grading = graded ? "float inputs:albedo_brightness = 2\nfloat inputs:albedo_add = -0.25" : "";
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                color3f inputs:diffuse_tint = ({{tint}})
                {{grading}}
                {{texture}}
                token outputs:out
                """, "mdl:surface", "out")));
            string tinted = textured ? TexturedPreview(image, "0.5, 0.25, 1.5, 1")
                : Preview("0.06, 0.085, 0.84", "0.21", "0.84");
            string referenceMaterial = graded && textured ? TexturedPreview(image, "1, 0.5, 3, 1").Replace(
                "float4 inputs:scale = (1, 0.5, 3, 1)",
                "float4 inputs:scale = (1, 0.5, 3, 1)\n" +
                "float4 inputs:bias = (-0.125, -0.0625, -0.375, 0)", StringComparison.Ordinal) : tinted;
            await File.WriteAllTextAsync(expected, Scene(referenceMaterial));
            await File.WriteAllTextAsync(control, Scene(textured
                ? graded ? tinted : TexturedPreview(image, "1, 1, 1, 1")
                : graded ? tinted : Preview("0.12, 0.34, 0.56", "0.21", "0.84")));
            if (textured)
            {
                SilkMaterialTexture projectedTexture = ReadMaterial(plugins, projected).Textures.Single();
                SilkMaterialTexture referenceTexture = ReadMaterial(plugins, expected).Textures.Single();
                await Assert.That(projectedTexture.Scale.SequenceEqual(referenceTexture.Scale)).IsTrue();
                await Assert.That(projectedTexture.Bias.SequenceEqual(referenceTexture.Bias)).IsTrue();
                await Assert.That(projectedTexture.SourceColorSpace).IsEqualTo(referenceTexture.SourceColorSpace);
                await Assert.That(projectedTexture.WrapS).IsEqualTo(referenceTexture.WrapS);
                await Assert.That(projectedTexture.WrapT).IsEqualTo(referenceTexture.WrapT);
                await Assert.That(projectedTexture.UvPrimvar).IsEqualTo(referenceTexture.UvPrimvar);
                await Assert.That(projectedTexture.Asset).IsEqualTo(referenceTexture.Asset);
            }
            byte[] authored = await File.ReadAllBytesAsync(projected);
            using ISilkGraphicsDevice device = hardware && OperatingSystem.IsWindows()
                ? D3D12SilkGraphicsDevice.Create(useWarp: false)
                : SilkDepthCaptureConformance.CreateDevice(backend);
            await Assert.That(device.Capabilities.IsSoftware).IsEqualTo(!hardware);
            Console.WriteLine(
                $"MDL_TINT_DEVICE={device.Capabilities.DeviceName}; software={device.Capabilities.IsSoftware}");
            new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304))
                .ConfigureDevice(device);
            byte[] reference = Capture(device, plugins, expected);
            byte[] untinted = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(actual.Length).IsEqualTo(32 * 32 * 8);
            await Assert.That(actual.SequenceEqual(reference)).IsTrue()
                .Because("tint must reach the GPU through the existing scalar or texture-scale record");
            await Assert.That(actual.SequenceEqual(untinted)).IsEqualTo(graded && !textured);
            await Assert.That((await File.ReadAllBytesAsync(projected)).SequenceEqual(authored)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SdkVariantConstantsReachHydraAndTheRenderedHdrPixels()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test(
                "Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the qualified SDK adapter, modules and native runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        if (!Path.IsPathFullyQualified(wrapper) || !File.Exists(wrapper))
        {
            throw new ArgumentException(
                "Use the existing absolute SDK variant fixture, not a synthesized base module.");
        }
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-render-").FullName;
        try
        {
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "base-defaults.usda");
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                token outputs:out
                """, "mdl:surface", "out")));
            await File.WriteAllTextAsync(reference, Scene(Preview("0.12, 0.34, 0.56", "0.21", "0.84")));
            await File.WriteAllTextAsync(control, Scene(Preview("0.2, 0.2, 0.2", "0.5", "0")));
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: false);
            Console.WriteLine(
                $"WAREHOUSE_MDL_RENDER_DEVICE name={device.Capabilities.DeviceName} " +
                $"software={device.Capabilities.IsSoftware}");
            byte[] expected = Capture(device, plugins, reference);
            byte[] defaults = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(device.Capabilities.IsSoftware).IsFalse();
            await Assert.That(actual.Length).IsEqualTo(32 * 32 * 8);
            await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue()
                .Because("SDK-proven body constants must survive Hydra translation " +
                    "and match the literal PreviewSurface control");
            await Assert.That(actual.AsSpan().SequenceEqual(defaults)).IsFalse()
                .Because("silently replacing variant constants with root defaults must change this fixture");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Capture(
        ISilkGraphicsDevice device, string plugins, string path, float[]? decodedReference = null,
        string? decodedReferenceAsset = null, bool streamMaterials = false)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
        var camera = new CameraState(Matrix4x4.Identity, new Matrix4x4(
            0.5f, 0, 0, 0, 0, 0.5f, 0, 0,
            0, 0, -0.2f, 0, 0, 0, -1.2f, 1));
        SilkTextureResidencyOptions? residency = streamMaterials
            ? new(1_048_576, 1_048_576, true) : null;
        using var renderer = decodedReference is null
            ? residency is null ? new SilkMeshRenderer(device) : new SilkMeshRenderer(device, residency)
            : new SilkMeshRenderer(device,
                device.Backend == SilkGraphicsBackend.D3D12
                    ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
                (_, _) => throw new InvalidOperationException("The reference image must be budgeted."),
                textureResidencyOptions: residency,
                ownedImageDecoder: (asset, linear, budget) =>
                {
                    if (decodedReferenceAsset is not null &&
                        !string.Equals(Path.GetFullPath(asset), Path.GetFullPath(decodedReferenceAsset),
                            StringComparison.Ordinal))
                    {
                        return SilkNativeImageDecoder.Decode(asset, linear, budget);
                    }
                    IDisposable reservation = budget.Reserve(64);
                    var image = new SilkDecodedImage(2, 2,
                        MemoryMarshal.AsBytes(decodedReference.AsSpan()).ToArray(), SilkTextureFormat.Rgba32Float);
                    image.OwnReservation(reservation);
                    return image;
                });
        using OpenUsdSilkPage page = session.Sync(32, 32, camera: camera);
        using OpenUsdSilkPage referenceCurve = WithReferenceDielectricCurve(page);
        renderer.ApplyPage(referenceCurve);
        SilkFrameCaptureResult result = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 32, 32, RenderSettings.PresentationDefault);
        foreach (var entry in result.Diagnostics.Entries)
        {
            if (entry.Code is SilkRenderDiagnosticCodes.TextureAssetNotFound or
                SilkRenderDiagnosticCodes.TextureDecodeFailed or SilkRenderDiagnosticCodes.TextureFallbackUsed)
            {
                throw new InvalidOperationException($"The pixel fixture did not decode its texture: {entry.Message}");
            }
        }
        return (result.HdrColor ??
            throw new InvalidOperationException("The retained raw color plane is absent.")).Rgba16Float.ToArray();
    }

    private static OpenUsdSilkPage WithReferenceDielectricCurve(OpenUsdSilkPage page)
    {
        using OpenUsdSilkPage.ReplaySnapshot snapshot = page.CaptureReplay();
        byte[] source = snapshot.Data ?? throw new InvalidOperationException(
            "The small control page must be contiguous.");
        using var output = new MemoryStream();
        for (int offset = 0; offset < source.Length;)
        {
            int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 4)));
            ReadOnlySpan<byte> command = source.AsSpan(offset, size);
            if (BinaryPrimitives.ReadUInt32LittleEndian(command) == (uint)SilkCommandType.MaterialUpsert &&
                new SilkMaterialUpsertCommand(command).SurfaceKind is SilkSurfaceKind.PreviewSurface or
                    SilkSurfaceKind.MaterialXProjected)
            {
                // The texture/UV/emission controls remain independent; only the new, separately
                // gated default dielectric curve is shared with the pinned OmniPBR profile.
                int insertion = checked(32 + (int)BinaryPrimitives.ReadUInt32LittleEndian(command[16..]));
                byte[] adjusted = new byte[size + 12];
                command[..insertion].CopyTo(adjusted);
                command[insertion..].CopyTo(adjusted.AsSpan(insertion + 12));
                BinaryPrimitives.WriteUInt32LittleEndian(adjusted.AsSpan(4), (uint)adjusted.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(adjusted.AsSpan(24),
                    BinaryPrimitives.ReadUInt32LittleEndian(command[24..]) + 1);
                BinaryPrimitives.WriteUInt32LittleEndian(adjusted.AsSpan(insertion),
                    (uint)SilkMaterialParameter.OmniPbrSpecularLevel);
                BinaryPrimitives.WriteUInt32LittleEndian(adjusted.AsSpan(insertion + 4), 1);
                BinaryPrimitives.WriteSingleLittleEndian(adjusted.AsSpan(insertion + 8), 0.5f);
                output.Write(adjusted);
            }
            else
            {
                output.Write(command);
            }
            offset += size;
        }
        return new(SilkCommandParser.PageAbiVersion, page.Revision, output.ToArray(), page.CommandCount);
    }

    private static SilkMaterialData ReadMaterial(string plugins, string path)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
        using OpenUsdSilkPage page = session.Sync(32, 32);
        var scene = new SilkSceneState();
        _ = scene.Apply(page);
        return scene.Materials["/World/Material"];
    }

    private static string Preview(string color, string roughness, string metallic) => Material(
        $$"""
        uniform token info:id = "UsdPreviewSurface"
        color3f inputs:diffuseColor = ({{color}})
        float inputs:roughness = {{roughness}}
        float inputs:metallic = {{metallic}}
        token outputs:surface
        """, "surface", "surface");

    // Keep the independent mask on the GPU: byte sampling need not round like premixed float texels.
    private static string EmissionProductReference(string image, string mask) =>
        $$"""
        def Material "Material" {
            token outputs:surface.connect = </World/Material/Shader.outputs:out>
            def Shader "Shader" {
                uniform token info:id = "ND_standard_surface_surfaceshader"
                color3f inputs:base_color = (0.12, 0.34, 0.56)
                float inputs:specular_roughness = 0.21
                float inputs:metalness = 0.84
                float inputs:emission = 1
                color3f inputs:emission_color.connect = </World/Material/Product.outputs:out>
                token outputs:out
            }
            def Shader "Product" {
                uniform token info:id = "ND_multiply_color3"
                color3f inputs:in1.connect = </World/Material/Image.outputs:out>
                color3f inputs:in2.connect = </World/Material/Mask.outputs:out>
                color3f outputs:out
            }
            def Shader "Image" {
                uniform token info:id = "ND_image_color3"
                asset inputs:file = @{{image.Replace('\\', '/')}}@ (colorSpace = "raw")
                token inputs:sourceColorSpace = "raw"
                float2 inputs:texcoord.connect = </World/Material/St.outputs:out>
                color3f outputs:out
            }
            def Shader "Mask" {
                uniform token info:id = "ND_image_color3"
                asset inputs:file = @{{mask.Replace('\\', '/')}}@ (colorSpace = "raw")
                token inputs:sourceColorSpace = "raw"
                float2 inputs:texcoord.connect = </World/Material/St.outputs:out>
                color3f outputs:out
            }
            def Shader "St" {
                uniform token info:id = "ND_geompropvalue_vector2"
                string inputs:geomprop = "st"
                float2 outputs:out
            }
        }
        """;

    private static string TexturedPreview(string image, string scale) =>
        $$"""
        def Material "Material" {
            token outputs:surface.connect = </World/Material/Shader.outputs:surface>
            def Shader "Shader" {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor.connect = </World/Material/Texture.outputs:rgb>
                float inputs:roughness = 0.21
                float inputs:metallic = 0.84
                token outputs:surface
            }
            def Shader "Texture" {
                uniform token info:id = "UsdUVTexture"
                asset inputs:file = @{{image.Replace('\\', '/')}}@ (colorSpace = "raw")
                token inputs:sourceColorSpace = "raw"
                token inputs:wrapS = "repeat"
                token inputs:wrapT = "repeat"
                float4 inputs:scale = ({{scale}})
                float2 inputs:st.connect = </World/Material/St.outputs:result>
                float3 outputs:rgb
            }
            def Shader "St" {
                uniform token info:id = "UsdPrimvarReader_float2"
                token inputs:varname = "st"
                float2 outputs:result
            }
        }
        """;

    private static string ScalarMapPreview(string image, bool packed, bool full, bool includeOcclusion = true)
    {
        string roughChannel = packed ? "g" : "r";
        string metalChannel = packed ? "b" : "r";
        string roughScale = full ? "1, 1, 1, 1" : packed ? "1, 0.25, 1, 1" : "0.25, 1, 1, 1";
        string roughBias = full ? "0, 0, 0, 0" : packed ? "0, 0.15, 0, 0" : "0.15, 0, 0, 0";
        string metalScale = full ? "1, 1, 1, 1" : packed ? "1, 1, 0.5, 1" : "0.5, 1, 1, 1";
        string metalBias = full ? "0, 0, 0, 0" : packed ? "0, 0, 0.4, 0" : "0.4, 0, 0, 0";
        string occlusion = packed && includeOcclusion
            ? "float inputs:occlusion.connect = </World/Material/Occlusion.outputs:r>" : "";
        string occlusionNode = packed && includeOcclusion
            ? ScalarMapNode(image, "Occlusion", "1, 1, 1, 1", "0, 0, 0, 0") : "";
        return $$"""
            def Material "Material" {
                token outputs:surface.connect = </World/Material/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0.12, 0.34, 0.56)
                    float inputs:roughness.connect = </World/Material/Roughness.outputs:{{roughChannel}}>
                    float inputs:metallic.connect = </World/Material/Metallic.outputs:{{metalChannel}}>
                    {{occlusion}}
                    token outputs:surface
                }
                {{ScalarMapNode(image, "Roughness", roughScale, roughBias)}}
                {{ScalarMapNode(image, "Metallic", metalScale, metalBias)}}
                {{occlusionNode}}
                def Shader "St" {
                    uniform token info:id = "UsdPrimvarReader_float2"
                    token inputs:varname = "st"
                    float2 outputs:result
                }
            }
            """;
    }

    private static string ScalarMapNode(string image, string name, string scale, string bias) =>
        $$"""
        def Shader "{{name}}" {
            uniform token info:id = "UsdUVTexture"
            asset inputs:file = @{{image}}@ (colorSpace = "raw")
            token inputs:wrapS = "repeat"
            token inputs:wrapT = "repeat"
            float4 inputs:scale = ({{scale}})
            float4 inputs:bias = ({{bias}})
            float2 inputs:st.connect = </World/Material/St.outputs:result>
            float outputs:r
            float outputs:g
            float outputs:b
        }
        """;

    private static string Material(string shader, string terminal, string output) =>
        $$"""
            def Material "Material" {
                token outputs:{{terminal}}.connect = </World/Material/Shader.outputs:{{output}}>
                def Shader "Shader" {
                    {{shader}}
                }
            }
        """;

    private static string Scene(string material) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = 1
            upAxis = "Y"
        )
        def Xform "World" {
            def Mesh "Quad" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                point3f[] points = [(-1, -1, -4), (1, -1, -4), (1, 1, -4), (-1, 1, -4)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                uniform token subdivisionScheme = "none"
                normal3f[] normals = [(0, 0, 1)] (interpolation = "constant")
                texCoord2f[] primvars:st = [(0, 0), (1, 0), (1, 1), (0, 1)] (interpolation = "vertex")
                rel material:binding = </World/Material>
            }
        {{material}}
        }
        """;
}
