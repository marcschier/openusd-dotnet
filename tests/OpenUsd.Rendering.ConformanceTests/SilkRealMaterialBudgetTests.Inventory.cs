// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkRealMaterialBudgetTests
{
    [Test]
    public async Task ProjectMaterialInventoryWithoutWarehouseGeometry()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_FULL_NORMAL_MATERIAL_REQUIRED") != "1")
        {
            Skip.Test("Requires explicitly admitted native material inventory.");
            return;
        }
        string stagePath = RequiredEnvironment("OPENUSD_MATERIAL_INVENTORY_STAGE");
        string plugins = RequiredEnvironment("OPENUSD_TEST_PLUGIN_PATH");
        string work = RequiredEnvironment("OPENUSD_TEST_WORK_ROOT");
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins);
        using UsdStage root = UsdStage.Open(stagePath);
        string[] paths = root.Traverse().Where(prim => prim.TypeName == "Material")
            .Select(prim => prim.Path).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(paths.Length).IsGreaterThan(0);
        using var stream = new StreamWriter(new FileStream(
            Path.Combine(work, "inventory.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
        var descriptions = new Dictionary<string, (SilkImageDescription Image, bool Png8)>(StringComparer.Ordinal);
        var summary = new List<InventoryMaterial>();
        string quad = Path.Combine(work, "inventory-quad.usda");
        foreach (string path in paths)
        {
            // Compose the real material (including root opinions), not the source
            // file's defaults. Never synchronize the warehouse's geometry.
            File.WriteAllText(quad, SingleMaterialStage(stagePath, path, null));
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, quad);
            using OpenUsdSilkPage page = session.Sync(16, 16);
            var scene = new SilkSceneState();
            _ = scene.Apply(page);
            await Assert.That(scene.Meshes.Count).IsEqualTo(1);
            SilkMaterialData material = scene.Materials["/World/Material"];
            var textures = new List<InventoryTexture>();
            foreach (SilkMaterialTexture texture in material.Textures)
            {
                try
                {
                    if (texture.Asset.Contains("<UDIM>", StringComparison.Ordinal))
                    {
                        textures.Add(ClassifyInventoryUdim(material, texture));
                        continue;
                    }
                    if (!descriptions.TryGetValue(texture.Asset, out var source))
                    {
                        source = (SilkNativeImageDecoder.Describe(texture.Asset), IsEightBitPng(texture.Asset));
                        descriptions.Add(texture.Asset, source);
                    }
                    textures.Add(ClassifyInventoryTexture(material, texture, source.Image, source.Png8));
                }
                catch (Exception error) when (error is IOException or InvalidDataException)
                {
                    textures.Add(new(texture.Parameter.ToString(), texture.Asset, 0, 0,
                        "unresolved", null, false, error.Message));
                }
            }
            bool unknown = textures.Any(texture => texture.Bytes is null);
            ulong bytes = textures.Aggregate(0ul, (sum, texture) => checked(sum + (texture.Bytes ?? 0)));
            bool physicsOnly = !material.IsSupported &&
                root.GetPrim(path).GetAppliedSchemas().Contains("PhysicsMaterialAPI", StringComparer.Ordinal);
            var entry = new InventoryMaterial(path, material.IsSupported, physicsOnly, material.SurfaceKind.ToString(),
                unknown ? null : bytes, textures);
            summary.Add(entry);
            stream.WriteLine(JsonSerializer.Serialize(entry));
        }
        using var sourceStream = File.OpenRead(stagePath);
        const ulong frameBytes = 24_993_796;
        File.WriteAllText(Path.Combine(work, "inventory-summary.json"), JsonSerializer.Serialize(new
        {
            stagePath,
            stageSha256 = Convert.ToHexString(SHA256.HashData(sourceStream)),
            scope = "Composed root Material prims; only a referenced material plus one synthetic quad is synchronized. No warehouse geometry synchronization or GPU rendering.",
            materialCount = paths.Length,
            uniqueSourceAssets = descriptions.Count,
            maximumMaterialBytes = summary.Max(material => material.Bytes ?? 0),
            measuredFrameOverheadBytes = frameBytes,
            poolBytes = 536870912ul,
            physicsOnlyCount = summary.Count(material => material.PhysicsOnly),
            unsupported = summary.Where(material => !material.Supported && !material.PhysicsOnly).Select(material => material.Path),
            unresolved = summary.Where(material => material.Bytes is null).Select(material => material.Path),
            overBudget = summary.Where(material => material.Bytes + frameBytes > 536870912ul),
            classes = summary.GroupBy(material => string.Join(";", material.Textures.Select(texture => texture.Storage)))
                .Select(group => new { storage = group.Key, count = group.Count(), maximumBytes = group.Max(item => item.Bytes) }),
            materials = summary.OrderByDescending(material => material.Bytes)
        }));
    }

    private static InventoryTexture ClassifyInventoryUdim(SilkMaterialData material, SilkMaterialTexture texture)
    {
        IReadOnlyList<SilkUdimTile> tiles = SilkNativeImageDecoder.ResolveUdimTiles(texture.Asset);
        if (tiles.Count == 0)
        {
            throw new InvalidDataException("UDIM source has no resolved tiles.");
        }
        SilkImageDescription first = SilkNativeImageDecoder.Describe(tiles[0].Asset);
        foreach (SilkUdimTile tile in tiles)
        {
            SilkImageDescription description = SilkNativeImageDecoder.Describe(tile.Asset);
            if (description.Width != first.Width || description.Height != first.Height || description.Format != first.Format)
            {
                throw new InvalidDataException("UDIM tile extents or precision differ.");
            }
        }
        InventoryTexture ordinary = ClassifyInventoryTexture(material, texture, first, false);
        uint bpp = ordinary.Storage switch { "R8" => 1, "R32F" or "RGBA8" => 4, _ => 16 };
        uint columns = tiles.Max(tile => (tile.Number - 1001) % 10) - tiles.Min(tile => (tile.Number - 1001) % 10) + 1;
        uint rows = tiles.Max(tile => (tile.Number - 1001) / 10) - tiles.Min(tile => (tile.Number - 1001) / 10) + 1;
        ulong width = columns * (first.Width + 2ul);
        ulong height = 1 + rows * (first.Height + 2ul);
        bool packed = ordinary.Storage is "R8" or "R32F" || width > 16384 || height > 16384;
        if (packed)
        {
            ulong best = ulong.MaxValue;
            for (uint across = 1; across <= tiles.Count; across++)
            {
                ulong w = across * (first.Width + 2ul);
                ulong h = (10 + 2ul * columns * rows + w - 1) / w +
                    (((ulong)tiles.Count + across - 1) / across) * (first.Height + 2ul);
                if (w <= 16384 && h <= 16384 && w * h < best)
                {
                    width = w;
                    height = h;
                    best = w * h;
                }
            }
            if (best == ulong.MaxValue)
            {
                throw new InvalidDataException("All UDIM tiles exceed the bounded atlas extent.");
            }
        }
        return new(texture.Parameter.ToString(), texture.Asset, (uint)width, (uint)height,
            "UDIM-" + ordinary.Storage, width * height * bpp, false,
            $"Conservative all-{tiles.Count}-tile atlas bound, including gutters/metadata; no geometry reachability or constant-tile reduction assumed.");
    }

    private static InventoryTexture ClassifyInventoryTexture(
        SilkMaterialData material, SilkMaterialTexture texture, SilkImageDescription source, bool png8)
    {
        uint levels = SilkMipChainLayout.GetMaxMipLevelCount(source.Width, source.Height);
        ulong pixels = SilkMipChainLayout.GetLogicalByteSize(
            source.Width, source.Height, SilkTextureFormat.Rgba8Unorm, levels) / 4;
        bool promoted = texture.LuminanceMix.W != 0 || texture.Channel > SilkTextureChannel.Rgb ||
            Enumerable.Range(0, 4).Any(component =>
                Math.Min(texture.Bias[component], (double)texture.Scale[component] + texture.Bias[component]) < 0 ||
                Math.Max(texture.Bias[component], (double)texture.Scale[component] + texture.Bias[component]) > 1);
        bool floating = promoted || source.Format == SilkTextureFormat.Rgba32Float;
        bool normal = texture.Parameter is SilkMaterialParameter.Normal or SilkMaterialParameter.DetailNormal;
        bool scalar = texture.Parameter is SilkMaterialParameter.Roughness or SilkMaterialParameter.Metallic or
            SilkMaterialParameter.Opacity or SilkMaterialParameter.Occlusion or SilkMaterialParameter.Clearcoat or
            SilkMaterialParameter.ClearcoatRoughness or SilkMaterialParameter.Ior;
        bool compactColor = texture.Parameter is SilkMaterialParameter.DiffuseColor or SilkMaterialParameter.EmissiveColor &&
            material.GetCompositeTexture() is null;
        bool eligible = promoted && !material.UsesRuntimeMaterialShader &&
            material.SurfaceKind is SilkSurfaceKind.PreviewSurface or SilkSurfaceKind.MdlDistilled &&
            (normal || compactColor) && texture.ComponentCount == 3 && texture.Channel == SilkTextureChannel.Rgb &&
            !texture.Asset.Contains("<UDIM>", StringComparison.Ordinal);
        string storage = floating ? "RGBA32F" : "RGBA8";
        ulong bytes = pixels * (floating ? 16ul : 4ul);
        if (scalar)
        {
            storage = floating ? "R32F" : "R8";
            bytes = pixels * (floating ? 4ul : 1ul);
        }
        else if (eligible && compactColor)
        {
            storage = "RG32F+R32F";
            bytes = pixels * 12;
        }
        bool guaranteed = png8 && texture.LuminanceMix.W == 0;
        if (eligible && guaranteed)
        {
            ulong tail = pixels - (ulong)source.Width * source.Height;
            uint tupleWidth = Math.Clamp(source.Width, 256u, 4096u);
            uint width = tupleWidth * 3;
            ulong height = (256 + tail + tupleWidth - 1) / tupleWidth;
            ulong indexed = pixels * 4 + width * height * 4;
            if ((normal || width != source.Width) && width <= 16384 && height <= 16384 &&
                256 + tail <= 0x1000000 && indexed < bytes)
            {
                storage = normal ? "indexed-normal/R32F-triples" :
                    texture.Parameter == SilkMaterialParameter.EmissiveColor
                        ? "indexed-emissive/R32F-triples" : "indexed-diffuse/R32F-triples";
                bytes = indexed;
            }
        }
        return new(texture.Parameter.ToString(), texture.Asset, source.Width, source.Height,
            storage, bytes, guaranteed, eligible && !guaranteed
                ? "Conservative fallback bound; source palette needs decoding to establish a smaller exact representation." : null);
    }

    private static bool IsEightBitPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[26];
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        return stream.Read(header) == header.Length &&
            header[..8].SequenceEqual(signature) &&
            header[24] == 8;
    }

    private sealed record InventoryTexture(
        string Parameter, string Asset, uint Width, uint Height, string Storage,
        ulong? Bytes, bool EightBitIndependentPalette, string? Limitation);

    private sealed record InventoryMaterial(
        string Path, bool Supported, bool PhysicsOnly, string SurfaceKind, ulong? Bytes, IReadOnlyList<InventoryTexture> Textures);
}
