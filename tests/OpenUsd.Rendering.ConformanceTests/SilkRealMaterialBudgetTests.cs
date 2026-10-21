// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed partial class SilkRealMaterialBudgetTests
{
    private static readonly string[] ExpectedSourceNames =
    [
        "T_Rubber_Black_Glossy_A_Albedo.png",
        "t_metal_scratches_a_roughtnes.png",
        "T_Rubber_Black_Glossy_A_Normal.png",
        "t_metalpainted_gray_metallic_a_normal.png"
    ];

    [Test]
    public async Task Full4096MaterialUsesIndexedNormalsWithin512MiBOnHardwareD3D12AndReplays() =>
        await QualifyFullMaterial(false);

    [Test]
    public async Task Full4096ConcreteUsesIndexedRgbWithin512MiBOnHardwareD3D12AndReplays() =>
        await QualifyFullMaterial(true);

    [Test]
    public async Task Full4096CeilingLampPreservesAllMapsAndReplays() =>
        await QualifyFullMaterial(false, "ceilinglamp");

    [Test]
    public async Task Full4096StreetLampPreservesAllMapsAndReplays() =>
        await QualifyFullMaterial(false, "streetlamp");

    [Test]
    public async Task Full4096WallLampPreservesAllMapsAndReplays() =>
        await QualifyFullMaterial(false, "walllamp");

    private static async Task QualifyFullMaterial(bool concrete, string? lamp = null)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_FULL_NORMAL_MATERIAL_REQUIRED") != "1")
        {
            Skip.Test("Requires an explicitly admitted full-resolution material and hardware D3D12 qualification.");
            return;
        }
        string sourcePath = RequiredEnvironment("OPENUSD_NORMAL_MATERIAL_ASSET");
        if (concrete)
        {
            sourcePath = Path.Combine(Path.GetDirectoryName(sourcePath)!,
                "opaque__concrete__painted_white_a_warehouse_a.usd");
        }
        if (lamp is not null)
        {
            sourcePath = Path.Combine(Path.GetDirectoryName(sourcePath)!,
                $"opaque__emissive__{lamp}_warehouse_a.usd");
        }
        string plugins = RequiredEnvironment("OPENUSD_TEST_PLUGIN_PATH");
        string work = Path.Combine(RequiredEnvironment("OPENUSD_TEST_WORK_ROOT"), lamp ?? (
            concrete ? "concrete" : "rubber"));
        Directory.CreateDirectory(work);
        using var phases = new StreamWriter(new FileStream(
            Path.Combine(work, "phases.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
        const ulong poolBytes = 512ul * 1024 * 1024;
        ulong materialBytes = lamp is not null
            ? lamp == "walllamp" ? 447_457_958ul : 380_316_326ul
            : concrete ? 447_457_958ul : 425_088_337ul;
        const uint extent = 64;
        var cpu = new SilkCpuTextureBudget(poolBytes);
        var textures = new SilkGpuTextureBudget(poolBytes);
        var staging = new SilkGpuStagingBudget(poolBytes);
        var buffers = new SilkGpuBufferBudget(16 * 1024 * 1024);
        using Process process = Process.GetCurrentProcess();
        void phase(string name)
        {
            process.Refresh();
            phases.WriteLine(JsonSerializer.Serialize(new
            {
                phase = name,
                utc = DateTime.UtcNow,
                privateBytes = process.PrivateMemorySize64,
                workingSetBytes = process.WorkingSet64,
                managedBytes = GC.GetTotalMemory(false),
                cpu = cpu.Usage,
                textures = textures.Usage,
                staging = staging.Usage,
                buffers = buffers.Usage
            }));
        }
        phase("native-material-open");
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
        string materialPrim;
        using (UsdStage source = UsdStage.Open(sourcePath))
        {
            materialPrim = source.Traverse().Single(prim => prim.TypeName == "Material").Path;
            using UsdLayer layer = source.GetRootLayer();
            layer.Export(Path.Combine(work, "source-material.usda"));
        }
        string moduleLine = File.ReadLines(Path.Combine(work, "source-material.usda")).Single(line =>
            line.TrimStart().StartsWith("uniform asset info:mdl:sourceAsset = @", StringComparison.Ordinal));
        string authoredModule = moduleLine[(moduleLine.IndexOf('@') + 1)..moduleLine.LastIndexOf('@')];
        string modulePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, authoredModule));
        string? moduleOverride = File.Exists(modulePath) ? modulePath : null;
        if (moduleOverride is null)
        {
            modulePath = RequiredEnvironment("OPENUSD_MDL_MODULE_PATH").Split(Path.PathSeparator)
                .Select(root => Path.Combine(root, authoredModule)).First(File.Exists);
        }
        await Assert.That(File.Exists(modulePath)).IsTrue();
        string stagePath = Path.Combine(work, "single-material.usda");
        File.WriteAllText(stagePath, SingleMaterialStage(sourcePath, materialPrim, moduleOverride));
        phase("native-single-quad-sync");
        OpenUsdSilkPage page;
        var camera = new CameraState(Matrix4x4.Identity, new Matrix4x4(
            0.5f, 0, 0, 0, 0, 0.5f, 0, 0, 0, 0, -0.2f, 0, 0, 0, -1.2f, 1));
        using (OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, stagePath))
        {
            page = session.Sync((int)extent, (int)extent, camera: camera);
        }
        using (page)
        {
            var scene = new SilkSceneState();
            _ = scene.Apply(page);
            await Assert.That(scene.Meshes.Count).IsEqualTo(1);
            SilkMaterialData material = scene.Materials["/World/Material"];
            File.WriteAllText(Path.Combine(work, "material-projection.json"), JsonSerializer.Serialize(new
            {
                sourcePath,
                materialPrim,
                authoredModule,
                modulePath,
                material.SurfaceKind,
                material.IsSupported,
                material.UvTransform,
                detailRow0 = material.GetScalar(SilkMaterialParameter.DetailUvRow0).ToArray(),
                detailRow1 = material.GetScalar(SilkMaterialParameter.DetailUvRow1).ToArray(),
                textures = material.Textures.Select(texture =>
                {
                    SilkImageDescription description = SilkNativeImageDecoder.Describe(texture.Asset);
                    using var stream = File.OpenRead(texture.Asset);
                    return new
                    {
                        parameter = texture.Parameter.ToString(),
                        texture.Asset,
                        texture.Scale,
                        texture.Bias,
                        texture.SourceColorSpace,
                        texture.Channel,
                        texture.ComponentCount,
                        description.Width,
                        description.Height,
                        description.Format,
                        sha256 = Convert.ToHexString(SHA256.HashData(stream))
                    };
                })
            }));
            phase("native-material-projection-recorded");
            await Assert.That(material.IsSupported).IsTrue();
            await Assert.That(material.SurfaceKind).IsEqualTo(SilkSurfaceKind.MdlDistilled);
            await Assert.That(material.Textures.Count).IsEqualTo(concrete || lamp is not null ? 5 : 4);
            SilkMaterialParameter[] slots = lamp is not null
                ? [SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.Roughness, SilkMaterialParameter.Metallic,
                    SilkMaterialParameter.EmissiveColor, SilkMaterialParameter.Normal]
                : concrete
                ? [SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.Roughness, SilkMaterialParameter.Metallic,
                    SilkMaterialParameter.Normal, SilkMaterialParameter.DetailNormal]
                :
                [SilkMaterialParameter.DiffuseColor, SilkMaterialParameter.Roughness,
                    SilkMaterialParameter.Normal, SilkMaterialParameter.DetailNormal];
            string[] files = slots.Select(slot => material.GetTexture(slot)!.Asset).ToArray();
            string[] names = lamp is not null
                ? ["t_lamp_a01_albedo.png", "t_lamp_a01_orm.png", "t_lamp_a01_orm.png",
                    "t_lamp_a01_emissive.png", "t_lamp_a01_normal.png"]
                : concrete
                ? ["t_concrete_glossy_a_albedo.png", "t_concrete_glossy_a_orm.png", "t_concrete_glossy_a_orm.png",
                    "t_concrete_glossy_a_normal.png", "t_concrete_foundation_Normal.png"]
                : ExpectedSourceNames;
            await Assert.That(files.Select(Path.GetFileName).SequenceEqual(names)).IsTrue();
            var sourceEvidence = files.Select(file =>
            {
                SilkImageDescription description = SilkNativeImageDecoder.Describe(file);
                using var stream = File.OpenRead(file);
                return new
                {
                    path = file,
                    description.Width,
                    description.Height,
                    description.Format,
                    sha256 = Convert.ToHexString(SHA256.HashData(stream))
                };
            }).ToArray();
            await Assert.That(sourceEvidence.All(image => image.Width == 4096 && image.Height == 4096 &&
                image.Format == SilkTextureFormat.Rgba8Unorm)).IsTrue();
            File.WriteAllText(Path.Combine(work, "source.json"), JsonSerializer.Serialize(new
            {
                sourcePath,
                materialPrim,
                authoredModule,
                modulePath,
                material.SurfaceKind,
                material.UvTransform,
                detailRow0 = material.GetScalar(SilkMaterialParameter.DetailUvRow0).ToArray(),
                detailRow1 = material.GetScalar(SilkMaterialParameter.DetailUvRow1).ToArray(),
                textures = material.Textures.Select(texture => new
                {
                    texture.Parameter,
                    texture.Asset,
                    texture.Scale,
                    texture.Bias,
                    texture.SourceColorSpace,
                    texture.Channel,
                    texture.ComponentCount
                }),
                files = sourceEvidence
            }));
            phase("hardware-device-create");
            using var device = new SilkGpuPublicationDevice(D3D12SilkGraphicsDevice.Create(useWarp: false));
            await Assert.That(device.Capabilities.IsSoftware).IsFalse();
            device.ConfigureCpuTextureBudget(cpu);
            device.ConfigureTextureBudget(textures);
            device.ConfigureStagingBudget(staging);
            device.ConfigureBufferBudget(buffers);
            using var renderer = new SilkMeshRenderer(device, new(poolBytes, poolBytes, true), cpu);
            using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(
                extent, extent));
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.SampledDepthTarget(extent, extent));
            byte[]? firstColor = null;
            float[]? firstDepth = null;
            ulong? retainedCpu = null;
            ulong? retainedTextures = null;
            for (int pass = 0; pass < 2; pass++)
            {
                phase(pass == 0 ? "full-resolution-render" : "full-resolution-replay");
                device.CreatedTextures.Clear();
                ulong uploaded = renderer.GpuResources.Statistics.TextureUploadBytes;
                SilkMeshRenderResult result = pass == 0
                    ? renderer.ApplyAndRender(page, color, depth)
                    : renderer.Render(color, depth);
                phase(pass == 0 ? "render-completed" : "replay-completed");
                await Assert.That(result.DrawCount).IsEqualTo(1);
                await Assert.That(renderer.GpuResources.Statistics.PeakTextureResidentGpuBytes).IsEqualTo(
                    materialBytes);
                await Assert.That(renderer.GpuResources.Statistics.TextureUploadBytes - uploaded).IsEqualTo(
                    materialBytes);
                await Assert.That(renderer.GpuResources.Statistics.TextureResidentGpuBytes).IsEqualTo(0ul);
                await Assert.That(renderer.GpuResources.Statistics.TextureResidentDecodedBytes).IsEqualTo(0ul);
                await Assert.That(device.CreatedTextures.Count(texture =>
                    texture is { Width: 4096, Height: 4096, MipLevelCount: 13, Format: SilkTextureFormat.Rgba8Unorm }))
                    .IsEqualTo(3);
                await Assert.That(device.CreatedTextures.Count(texture =>
                    texture is { Width: 12288, Height: 1366, MipLevelCount: 1, Format: SilkTextureFormat.R32Float }))
                    .IsEqualTo(lamp is not null && lamp != "walllamp" ? 1 : 2);
                await Assert.That(device.CreatedTextures.Count(texture =>
                    texture is { Width: 4096, Height: 4096, MipLevelCount: 13, Format: SilkTextureFormat.R8Unorm }))
                    .IsEqualTo(concrete || lamp is not null ? 2 : 1);
                await Assert.That(device.CreatedTextures.Any(texture =>
                    texture is { Width: 4096, Height: 4096, Format: SilkTextureFormat.Rgba32Float })).IsFalse();
                await Assert.That(renderer.GpuResources.Diagnostics.Entries.Where(entry => entry.Code is
                    SilkRenderDiagnosticCodes.TextureAssetNotFound or SilkRenderDiagnosticCodes.TextureDecodeFailed or
                    SilkRenderDiagnosticCodes.TextureFallbackUsed)).IsEmpty();
                byte[] hdr = new byte[extent * extent * 8];
                float[] deviceDepth = new float[extent * extent];
                color.ReadbackForTesting(hdr);
                depth.ReadbackForTesting(deviceDepth);
                bool litCoveredPixel = false;
                for (int pixel = 0; pixel < deviceDepth.Length; pixel++)
                {
                    for (int component = 0; component < 4; component++)
                    {
                        float value = (float)BitConverter.UInt16BitsToHalf(
                            BinaryPrimitives.ReadUInt16LittleEndian(hdr.AsSpan(pixel * 8 + component * 2, 2)));
                        if (!float.IsFinite(value))
                        {
                            throw new InvalidDataException("Full-resolution material emitted non-finite HDR.");
                        }
                        litCoveredPixel |= component < 3 && deviceDepth[pixel] < 1 && value > 0;
                    }
                }
                await Assert.That(litCoveredPixel).IsTrue();
                await Assert.That(deviceDepth.Any(value => value == 1)).IsTrue();
                if (firstColor is null)
                {
                    firstColor = hdr;
                    firstDepth = deviceDepth;
                    retainedCpu = cpu.Usage.ReservedBytes;
                    retainedTextures = textures.Usage.ReservedBytes;
                }
                else
                {
                    await Assert.That(hdr.SequenceEqual(firstColor)).IsTrue();
                    await Assert.That(deviceDepth.SequenceEqual(firstDepth!)).IsTrue();
                    await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(retainedCpu!.Value);
                    await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(retainedTextures!.Value);
                }
                await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            }
            File.WriteAllText(Path.Combine(work, "qualification.json"), JsonSerializer.Serialize(new
            {
                adapter = device.Capabilities,
                cpu = cpu.Usage,
                textures = textures.Usage,
                staging = staging.Usage,
                materialBytes,
                materialStatistics = renderer.GpuResources.Statistics,
                createdTexturesOnReplay = device.CreatedTextures,
                hdrSha256 = Convert.ToHexString(SHA256.HashData(firstColor!)),
                successfulReplay = true
            }));
            renderer.Dispose();
            color.Dispose();
            depth.Dispose();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
            phase("all-owned-resources-released");
        }
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ??
            throw new InvalidOperationException($"The qualification requires {name}.");

    private static string SingleMaterialStage(string file, string prim, string? module) => $$"""
        #usda 1.0
        (defaultPrim = "World")
        def Xform "World" {
            def Material "Material" (prepend references = @{{file.Replace('\\', '/')}}@<{{prim}}>) {
                {{(module is null ? "" : "over \"Shader\" {\nuniform asset info:mdl:sourceAsset = @" +
                    module.Replace('\\', '/') + "@\n}")}}
            }
            def Mesh "Quad" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                point3f[] points = [(-1, -1, -4), (1, -1, -4), (1, 1, -4), (-1, 1, -4)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                uniform token subdivisionScheme = "none"
                normal3f[] normals = [(0, 0, 1)] (interpolation = "constant")
                texCoord2f[] primvars:st = [(0, 0), (1, 0), (1, 1), (0, 1)] (interpolation = "vertex")
                rel material:binding = </World/Material>
            }
        }
        """;
}
