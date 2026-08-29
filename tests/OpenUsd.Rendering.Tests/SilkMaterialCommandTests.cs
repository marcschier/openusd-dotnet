// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
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
        string UvPrimvar);

    private static byte[] CreateMaterialUpsert(
        string path,
        SilkSurfaceKind kind,
        (SilkMaterialParameter Parameter, float[] Values)[] scalars,
        TextureSpec[] textures,
        byte[]? generatedFragmentSpirV = null,
        byte[]? generatedFragmentMslSource = null)
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
            payload.AddRange(assetBytes);
            payload.AddRange(uvBytes);
        }

        generatedFragmentSpirV ??= [];
        payload.AddRange(BitConverter.GetBytes((uint)generatedFragmentSpirV.Length));
        payload.AddRange(generatedFragmentSpirV);
        generatedFragmentMslSource ??= [];
        payload.AddRange(BitConverter.GetBytes((uint)generatedFragmentMslSource.Length));
        payload.AddRange(generatedFragmentMslSource);

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

    private static byte[] CreateMeshUpsert(string pathValue, string materialPath)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        byte[] material = Encoding.UTF8.GetBytes(materialPath);
        float[] points = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] indices = [0, 1, 2];
        int size = 224 +
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
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 7);
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
        path.CopyTo(bytes, 224);
        int pointsOffset = 224 + path.Length;
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

    private sealed class TextureGraphicsDevice : ISilkGraphicsDevice
    {
        public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

        public SilkGraphicsCapabilities Capabilities => new(
            "Texture diagnostics test device",
            "test",
            SupportsCompute: false,
            IsSoftware: true);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new Texture(width, height, format);

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            new Texture(descriptor.Width, descriptor.Height, descriptor.Format);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new TextureGraphicsBuffer(size, usage);

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

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
            throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Texture(
        uint width,
        uint height,
        SilkTextureFormat format) : ISilkGraphicsTexture
    {
        public uint Width { get; } = width;

        public uint Height { get; } = height;

        public SilkTextureFormat Format { get; } = format;

        public SilkTextureUsage Usage => SilkTextureUsage.Sampled;

        public void ReadbackForTesting(Span<byte> destination) =>
            throw new NotSupportedException();

        public void ReadbackForTesting(Span<float> destination) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class TextureGraphicsBuffer(nuint size, SilkBufferUsage usage)
        : ISilkGraphicsBuffer
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];

        public nuint Size => size;

        public SilkBufferUsage Usage => usage;

        public void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));

        public void ReadbackForTesting(Span<byte> destination) =>
            _bytes.AsSpan(0, destination.Length).CopyTo(destination);

        public void Dispose()
        {
        }
    }

    private sealed class TextureCommandList : ISilkGraphicsCommandList
    {
        internal List<byte[]> Uploads { get; } = [];

        internal int UploadCount => Uploads.Count;

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            Uploads.Add(source.ToArray());

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color) =>
            throw new NotSupportedException();

        public void ClearDepth(ISilkGraphicsTexture texture, float depth) =>
            throw new NotSupportedException();

        public void BeginRendering(SilkRenderingDescriptor descriptor) =>
            throw new NotSupportedException();

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline) =>
            throw new NotSupportedException();

        public void SetViewport(SilkViewport viewport) =>
            throw new NotSupportedException();

        public void SetScissor(SilkScissor scissor) =>
            throw new NotSupportedException();

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture) =>
            throw new NotSupportedException();

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler) =>
            throw new NotSupportedException();

        public void DrawIndexed(uint indexCount) =>
            throw new NotSupportedException();

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
            throw new NotSupportedException();

        public void EndRendering() =>
            throw new NotSupportedException();

        public void SetComputePipeline(ISilkComputePipeline pipeline) =>
            throw new NotSupportedException();

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
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

        public void Dispose()
        {
        }
    }
}
