// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Shade;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkRealMaterialBudgetTests
{
    [Test]
    public async Task ReferencedBookMeshesPreserveReachableUdimTilesWithinOriginalPools()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_FULL_NORMAL_MATERIAL_REQUIRED") != "1")
        {
            Skip.Test("Requires an admitted native UDIM reachability qualification.");
            return;
        }
        string source = RequiredEnvironment("OPENUSD_MATERIAL_INVENTORY_STAGE");
        string plugins = RequiredEnvironment("OPENUSD_TEST_PLUGIN_PATH");
        string work = RequiredEnvironment("OPENUSD_TEST_WORK_ROOT");
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
        string[] owners;
        using (UsdStage stage = UsdStage.Open(source))
        {
            owners = stage.Traverse().Where(prim => prim.TypeName == "Material" &&
                prim.Path.EndsWith("/Materials/opaque__paper__book_open_a", StringComparison.Ordinal))
                .Select(prim => prim.Path[..prim.Path.LastIndexOf("/Materials/", StringComparison.Ordinal)])
                .Order(StringComparer.Ordinal).ToArray();
        }
        await Assert.That(owners.Length).IsEqualTo(4);
        int ownerIndex = int.Parse(RequiredEnvironment("OPENUSD_UDIM_OWNER_INDEX"),
            System.Globalization.CultureInfo.InvariantCulture);
        string owner = owners[ownerIndex];
        string stagePath = Path.Combine(work, "referenced-book.usda");
        File.WriteAllText(stagePath, $$"""
            #usda 1.0
            (defaultPrim = "World")
            def Xform "World" {
                def Xform "Source" (prepend references = @{{source.Replace('\\', '/')}}@<{{owner}}>) {}
            }
            """);
        using OpenUsdSilkPage page = ReadScopedPage(plugins, stagePath);
        var scene = new SilkSceneState();
        SilkSceneDelta delta = scene.Apply(page);
        await Assert.That(scene.Meshes.Count).IsGreaterThan(0);
        using (UsdStage original = UsdStage.Open(source))
        using (UsdStage scoped = UsdStage.Open(stagePath))
        {
            foreach (SilkMeshData mesh in scene.Meshes.Values)
            {
                string originalPath = owner + mesh.Path["/World/Source".Length..];
                UsdPrim originalPrim = original.GetPrim(originalPath);
                UsdPrim scopedPrim = scoped.GetPrim(mesh.Path);
                await Assert.That(original.GetBoundMaterial(originalPrim).Path)
                    .IsEqualTo(owner + mesh.MaterialPath["/World/Source".Length..]);
                await Assert.That(originalPrim.GetVec2fArray("primvars:st")
                    .SequenceEqual(scopedPrim.GetVec2fArray("primvars:st"))).IsTrue();
                await Assert.That(originalPrim.GetInt32Array("faceVertexIndices")
                    .SequenceEqual(scopedPrim.GetInt32Array("faceVertexIndices"))).IsTrue();
                if (originalPrim.GetAttributeNames().Contains("primvars:st:indices", StringComparer.Ordinal))
                {
                    await Assert.That(originalPrim.GetInt32Array("primvars:st:indices")
                        .SequenceEqual(scopedPrim.GetInt32Array("primvars:st:indices"))).IsTrue();
                }
            }
        }
        const ulong limit = 512ul * 1024 * 1024;
        var cpu = new SilkCpuTextureBudget(limit);
        var textures = new SilkGpuTextureBudget(limit);
        var staging = new SilkGpuStagingBudget(limit);
        var buffers = new SilkGpuBufferBudget(16 * 1024 * 1024);
        using var device = new SilkGpuPublicationDevice(D3D12SilkGraphicsDevice.Create(useWarp: false));
        await Assert.That(device.Capabilities.IsSoftware).IsFalse();
        device.ConfigureCpuTextureBudget(cpu);
        device.ConfigureTextureBudget(textures);
        device.ConfigureStagingBudget(staging);
        device.ConfigureBufferBudget(buffers);
        var decoded = new List<UdimDecodedTile>();
        SilkMaterialTexture? active = null;
        SilkDecodedImage decode(string asset, bool linear, bool floating, SilkCpuTextureBudget? budget)
        {
            SilkDecodedImage image = SilkNativeImageDecoder.Decode(asset, linear, floating, budget);
            if (active is not null)
            {
                int channel = (int)active.Channel;
                bool constant = channel < 4 && active.LuminanceMix.W == 0;
                if (constant && image.Format == SilkTextureFormat.Rgba8Unorm)
                {
                    byte first = image.Pixels[channel];
                    for (int offset = channel; offset < image.Pixels.Length; offset += 4)
                    {
                        if (image.Pixels[offset] != first)
                        { constant = false; break; }
                    }
                }
                else if (constant)
                {
                    ReadOnlySpan<uint> pixels = MemoryMarshal.Cast<byte, uint>(image.Pixels);
                    uint first = pixels[channel];
                    for (int offset = channel; offset < pixels.Length; offset += 4)
                    {
                        if (pixels[offset] != first)
                        { constant = false; break; }
                    }
                }
                decoded.Add(new(asset, image.Width, image.Height, image.Format, constant));
            }
            return image;
        }
        using var resources = new SilkSceneGpuResources(device,
            (asset, linear) => decode(asset, linear, false, cpu),
            residencyOptions: new(limit, limit, true), cpuTextureBudget: cpu,
            imageDescriber: SilkNativeImageDecoder.Describe,
            ownedImageDecoder: (asset, linear, budget) => decode(asset, linear, false, budget),
            precisionImageDecoder: decode);
        resources.Apply(scene, delta);
        await Assert.That(resources.Meshes.Count).IsEqualTo(scene.Meshes.Count);
        using var log = new StreamWriter(new FileStream(Path.Combine(work, "udim-reachability.jsonl"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
        int materialCount = 0;
        ulong maximum = 0;
        using (resources.BeginMaterialTextureFrame())
        {
            foreach (SilkMaterialData material in scene.Materials.Values.Where(material =>
                material.Textures.Any(texture => texture.Asset.Contains("<UDIM>", StringComparison.Ordinal))))
            {
                if (!scene.Meshes.Values.Any(mesh => mesh.MaterialPath == material.Path))
                { continue; }
                materialCount++;
                resources.ReleaseStreamedTextureWorkingSet();
                var evidence = new List<object>();
                foreach (SilkMaterialTexture texture in material.Textures)
                {
                    active = texture;
                    decoded.Clear();
                    device.CreatedTextures.Clear();
                    IReadOnlyList<SilkUdimTile> tiles = SilkNativeImageDecoder.ResolveUdimTiles(texture.Asset);
                    HashSet<uint> required = RequiredUdimTiles(resources, material, texture, tiles);
                    using ISilkGraphicsCommandList commands = device.CreateCommandList();
                    resources.UploadMaterialTexture(commands, material, texture.Parameter);
                    uint[] selected = tiles.Where(tile => decoded.Any(item =>
                        string.Equals(item.Asset, tile.Asset, StringComparison.Ordinal))).Select(tile => tile.Number).ToArray();
                    await Assert.That(required.Count).IsGreaterThan(0);
                    await Assert.That(required.IsSubsetOf(selected)).IsTrue();
                    await Assert.That(selected.Order().SequenceEqual(required.Order())).IsTrue();
                    await Assert.That(selected.Length).IsGreaterThan(0);
                    await Assert.That(device.CreatedTextures.Count).IsEqualTo(1);
                    SilkTextureDescriptor atlas = device.CreatedTextures.Single();
                    bool compacted = atlas.Width < decoded[0].Width && atlas.Height < decoded[0].Height;
                    if (compacted)
                    {
                        await Assert.That(decoded.All(tile => tile.ConstantRelevantChannel)).IsTrue();
                    }
                    evidence.Add(new
                    {
                        parameter = texture.Parameter.ToString(),
                        texture.Asset,
                        allTiles = tiles.Select(tile => tile.Number),
                        requiredTiles = required.Order(),
                        selectedTiles = selected,
                        decoded = decoded.ToArray(),
                        atlas,
                        constantTileCompaction = compacted
                    });
                }
                ulong bytes = resources.Statistics.TextureResidentGpuBytes;
                maximum = Math.Max(maximum, bytes);
                log.WriteLine(JsonSerializer.Serialize(new
                {
                    owner,
                    material = material.Path,
                    meshes = scene.Meshes.Values.Where(mesh => mesh.MaterialPath == material.Path)
                        .Select(mesh => new { mesh.Path, mesh.InstanceIndex, indices = mesh.Indices.Length }),
                    workingSetBytes = bytes,
                    textures = evidence
                }));
                await Assert.That(bytes).IsLessThanOrEqualTo(limit);
                await Assert.That(bytes + 24_993_796ul).IsLessThanOrEqualTo(limit);
                await Assert.That(resources.Diagnostics.Entries.Where(entry => entry.Code is
                    SilkRenderDiagnosticCodes.TextureFallbackUsed or SilkRenderDiagnosticCodes.TextureDecodeFailed or
                    SilkRenderDiagnosticCodes.TextureAssetNotFound)).IsEmpty();
            }
            active = null;
            resources.ReleaseStreamedTextureWorkingSet();
        }
        await Assert.That(materialCount).IsEqualTo(3);
        resources.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        using FileStream sourceStream = File.OpenRead(source);
        File.WriteAllText(Path.Combine(work, "udim-summary.json"), JsonSerializer.Serialize(new
        {
            source,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceStream)),
            owner,
            ownerIndex,
            materialCount,
            maximumActualMaterialBytes = maximum,
            cpu = cpu.Usage,
            textures = textures.Usage,
            staging = staging.Usage,
            buffers = buffers.Usage,
            qualification = "Actual referenced subtree geometry and native UVs; production reachability and compaction. Independent double-precision triangle/tile clipping proves selected tiles cover triangle interiors and vertices. No warehouse geometry sync."
        }));
    }

    private static OpenUsdSilkPage ReadScopedPage(string plugins, string path)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
        return session.Sync(64, 64);
    }

    private sealed record UdimDecodedTile(
        string Asset, uint Width, uint Height, SilkTextureFormat Format, bool ConstantRelevantChannel);

    private static HashSet<uint> RequiredUdimTiles(
        SilkSceneGpuResources resources, SilkMaterialData material,
        SilkMaterialTexture texture, IReadOnlyList<SilkUdimTile> tiles)
    {
        var result = new HashSet<uint>();
        IReadOnlyList<float> transform = material.UvTransform;
        if (texture.Parameter == SilkMaterialParameter.DetailNormal)
        {
            float[] a = material.GetScalar(SilkMaterialParameter.DetailUvRow0).ToArray();
            float[] b = material.GetScalar(SilkMaterialParameter.DetailUvRow1).ToArray();
            transform = [a[0], a[1], b[0], b[1], a[2], b[2]];
        }
        foreach (SilkMeshGpuResource resource in resources.Meshes.Values.Where(item => item.Mesh.MaterialPath == material.Path))
        {
            SilkMeshData mesh = resource.Mesh;
            if (mesh.TopologyKind != SilkTopologyKind.TriangleList)
            { throw new InvalidDataException("Expected real triangle meshes."); }
            SilkVertexAttributeData? coordinates = mesh.FindTexCoord(resource.Geometry.Key.UvPrimvar);
            if (coordinates is null)
            { throw new InvalidDataException("The referenced mesh has no actual texture coordinates."); }
            for (int first = 0; first < mesh.Indices.Length; first += 3)
            {
                var triangle = new List<(double X, double Y)>(3);
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = checked((int)mesh.Indices.Span[first + corner]);
                    double u = coordinates?.GetComponent(vertex, 0) ?? 0;
                    double v = coordinates?.GetComponent(vertex, 1) ?? 0;
                    triangle.Add((transform[0] * u + transform[1] * v + transform[4],
                        transform[2] * u + transform[3] * v + transform[5]));
                }
                foreach (SilkUdimTile tile in tiles)
                {
                    double x = (tile.Number - 1001) % 10;
                    double y = (tile.Number - 1001) / 10;
                    if (triangle.Any(point => Math.Floor(point.X) == x && Math.Floor(point.Y) == y))
                    {
                        result.Add(tile.Number);
                    }
                    List<(double X, double Y)> polygon = triangle;
                    polygon = Clip(polygon, 0, x, true);
                    polygon = Clip(polygon, 0, x + 1, false);
                    polygon = Clip(polygon, 1, y, true);
                    polygon = Clip(polygon, 1, y + 1, false);
                    double area = 0;
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        var a = polygon[i];
                        var b = polygon[(i + 1) % polygon.Count];
                        area += a.X * b.Y - b.X * a.Y;
                    }
                    if (Math.Abs(area) > 1e-12)
                    { result.Add(tile.Number); }
                }
            }
        }
        return result;
    }

    private static List<(double X, double Y)> Clip(
        List<(double X, double Y)> polygon, int axis, double bound, bool lower)
    {
        var output = new List<(double X, double Y)>();
        if (polygon.Count == 0)
        { return output; }
        var previous = polygon[^1];
        foreach (var current in polygon)
        {
            double a = axis == 0 ? previous.X : previous.Y;
            double b = axis == 0 ? current.X : current.Y;
            bool insideA = lower ? a >= bound : a <= bound;
            bool insideB = lower ? b >= bound : b <= bound;
            if (insideA != insideB)
            {
                double ratio = (bound - a) / (b - a);
                output.Add((previous.X + ratio * (current.X - previous.X), previous.Y + ratio * (current.Y - previous.Y)));
            }
            if (insideB)
            { output.Add(current); }
            previous = current;
        }
        return output;
    }
}
