// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// One constant UsdPreviewSurface input, copied out of a material command.
/// </summary>
public sealed class SilkMaterialScalar
{
    internal SilkMaterialScalar(SilkMaterialParameter parameter, float[] values)
    {
        Parameter = parameter;
        Values = values;
    }

    /// <summary>Gets the parameter this value drives.</summary>
    public SilkMaterialParameter Parameter { get; }

    /// <summary>Gets the one to four component values.</summary>
    public IReadOnlyList<float> Values { get; }
}

/// <summary>
/// One UsdPreviewSurface input driven by a connected UsdUVTexture, copied out of
/// a material command.
/// </summary>
public sealed class SilkMaterialTexture
{
    internal SilkMaterialTexture(
        SilkMaterialParameter parameter,
        SilkTextureWrap wrapS,
        SilkTextureWrap wrapT,
        SilkColorSpace sourceColorSpace,
        int componentCount,
        float[] scale,
        float[] bias,
        float[] fallback,
        string asset,
        string uvPrimvar)
    {
        Parameter = parameter;
        WrapS = wrapS;
        WrapT = wrapT;
        SourceColorSpace = sourceColorSpace;
        ComponentCount = componentCount;
        Scale = scale;
        Bias = bias;
        Fallback = fallback;
        Asset = asset;
        UvPrimvar = uvPrimvar;
    }

    /// <summary>Gets the parameter this texture drives.</summary>
    public SilkMaterialParameter Parameter { get; }

    /// <summary>Gets the horizontal wrap mode.</summary>
    public SilkTextureWrap WrapS { get; }

    /// <summary>Gets the vertical wrap mode.</summary>
    public SilkTextureWrap WrapT { get; }

    /// <summary>Gets the authored source color space.</summary>
    public SilkColorSpace SourceColorSpace { get; }

    /// <summary>Gets how many channels the bound input consumes.</summary>
    public int ComponentCount { get; }

    /// <summary>Gets the multiply applied after sampling.</summary>
    public IReadOnlyList<float> Scale { get; }

    /// <summary>Gets the offset applied after scaling.</summary>
    public IReadOnlyList<float> Bias { get; }

    /// <summary>Gets the value used when the asset cannot be loaded.</summary>
    public IReadOnlyList<float> Fallback { get; }

    /// <summary>Gets the resolved texture asset path.</summary>
    public string Asset { get; }

    /// <summary>
    /// Gets the primvar supplying texture coordinates, empty when the texture has
    /// no resolvable reader connection.
    /// </summary>
    public string UvPrimvar { get; }
}

/// <summary>
/// A retained material, copied out of a material upsert command so it outlives
/// the native page.
/// </summary>
public sealed class SilkMaterialData
{
    private SilkMaterialData(
        string path,
        ulong stableHash,
        SilkSurfaceKind surfaceKind,
        SilkMaterialScalar[] scalars,
        SilkMaterialTexture[] textures,
        byte[] generatedFragmentSpirV,
        byte[] generatedFragmentMslSource)
    {
        Path = path;
        StableHash = stableHash;
        SurfaceKind = surfaceKind;
        Scalars = scalars;
        Textures = textures;
        GeneratedFragmentSpirV = generatedFragmentSpirV;
        GeneratedFragmentMslSource = generatedFragmentMslSource;
    }

    /// <summary>Gets the authoritative USD material path.</summary>
    public string Path { get; }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash { get; }

    /// <summary>Gets the surface model this material describes.</summary>
    public SilkSurfaceKind SurfaceKind { get; }

    /// <summary>Gets the constant inputs.</summary>
    public IReadOnlyList<SilkMaterialScalar> Scalars { get; }

    /// <summary>Gets the texture-driven inputs.</summary>
    public IReadOnlyList<SilkMaterialTexture> Textures { get; }

    /// <summary>Gets generated MaterialX fragment SPIR-V bytes for runtime shaders.</summary>
    public ReadOnlyMemory<byte> GeneratedFragmentSpirV { get; }

    /// <summary>Gets generated MaterialX fragment MSL source bytes for runtime shaders.</summary>
    public ReadOnlyMemory<byte> GeneratedFragmentMslSource { get; }

    /// <summary>
    /// Gets whether this material can be shaded. An unsupported network is
    /// retained rather than dropped so a consumer can report which material it
    /// could not shade instead of silently rendering a default.
    /// </summary>
    public bool IsSupported =>
        SurfaceKind is SilkSurfaceKind.PreviewSurface or
        SilkSurfaceKind.MaterialXProjected or
        SilkSurfaceKind.MaterialXGenerated or
        SilkSurfaceKind.VolumeDensity;

    /// <summary>Gets whether this material should travel through the runtime MaterialX shader service.</summary>
    internal bool UsesRuntimeMaterialShader =>
        SurfaceKind is SilkSurfaceKind.MaterialXProjected or SilkSurfaceKind.MaterialXGenerated;

    /// <summary>Copies one material upsert command into retained storage.</summary>
    public static SilkMaterialData CopyFrom(SilkMaterialUpsertCommand command)
    {
        SilkMaterialScalar[] scalars = command.ScalarCount == 0
            ? []
            : new SilkMaterialScalar[command.ScalarCount];
        for (int index = 0; index < scalars.Length; index++)
        {
            SilkMaterialScalarEntry entry = command.GetScalar(index);
            float[] values = new float[entry.ComponentCount];
            for (int component = 0; component < values.Length; component++)
            {
                values[component] = entry.GetComponent(component);
            }
            scalars[index] = new SilkMaterialScalar(entry.Parameter, values);
        }

        SilkMaterialTexture[] textures = command.TextureCount == 0
            ? []
            : new SilkMaterialTexture[command.TextureCount];
        for (int index = 0; index < textures.Length; index++)
        {
            SilkMaterialTextureEntry entry = command.GetTexture(index);
            float[] scale = new float[4];
            float[] bias = new float[4];
            float[] fallback = new float[4];
            for (int component = 0; component < 4; component++)
            {
                scale[component] = entry.GetScale(component);
                bias[component] = entry.GetBias(component);
                fallback[component] = entry.GetFallback(component);
            }
            textures[index] = new SilkMaterialTexture(
                entry.Parameter,
                entry.WrapS,
                entry.WrapT,
                entry.SourceColorSpace,
                entry.ComponentCount,
                scale,
                bias,
                fallback,
                entry.Asset,
                entry.UvPrimvar);
        }

        byte[] generatedFragmentSpirV = command.GeneratedFragmentSpirV.ToArray();
        byte[] generatedFragmentMslSource = command.GeneratedFragmentMslSource.ToArray();
        return new SilkMaterialData(
            command.Path,
            command.StableHash,
            command.SurfaceKind,
            scalars,
            textures,
            generatedFragmentSpirV,
            generatedFragmentMslSource);
    }

    /// <summary>
    /// Gets the constant value of one parameter, or an empty span when the
    /// parameter is texture-driven or left at its default.
    /// </summary>
    public ReadOnlySpan<float> GetScalar(SilkMaterialParameter parameter)
    {
        foreach (SilkMaterialScalar scalar in Scalars)
        {
            if (scalar.Parameter == parameter)
            {
                return ((float[])scalar.Values).AsSpan();
            }
        }
        return [];
    }

    /// <summary>
    /// Gets the texture driving one parameter, or null when the parameter is
    /// constant or left at its default.
    /// </summary>
    public SilkMaterialTexture? GetTexture(SilkMaterialParameter parameter)
    {
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (texture.Parameter == parameter)
            {
                return texture;
            }
        }
        return null;
    }

    internal SilkShaderFeatures GetTextureFeatures()
    {
        SilkShaderFeatures features = SilkShaderFeatures.None;
        if (GetTexture(SilkMaterialParameter.DiffuseColor) is not null)
        {
            features |= SilkShaderFeatures.BaseColorMap;
        }

        if (GetTexture(SilkMaterialParameter.Normal) is not null)
        {
            features |= SilkShaderFeatures.NormalMap;
        }
        if (GetTexture(SilkMaterialParameter.Roughness) is not null ||
            GetTexture(SilkMaterialParameter.Metallic) is not null)
        {
            features |= SilkShaderFeatures.RoughnessMetallicMap;
        }
        if (GetTexture(SilkMaterialParameter.EmissiveColor) is not null)
        {
            features |= SilkShaderFeatures.EmissiveMap;
        }
        return features == SilkShaderFeatures.None
            ? features
            : features | SilkShaderFeatures.Uv;
    }

    internal byte[] CreateRuntimeShaderKeyBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("OpenUsd.Rendering.Silk.MaterialXRuntime.v2");
        writer.Write(Path);
        writer.Write((uint)SurfaceKind);
        writer.Write(Scalars.Count);
        foreach (SilkMaterialScalar scalar in Scalars.OrderBy(s => s.Parameter))
        {
            writer.Write((uint)scalar.Parameter);
            writer.Write(scalar.Values.Count);
            foreach (float value in scalar.Values)
            {
                writer.Write(value);
            }
        }
        writer.Write(Textures.Count);
        foreach (SilkMaterialTexture texture in Textures.OrderBy(t => t.Parameter))
        {
            writer.Write((uint)texture.Parameter);
            writer.Write(texture.Asset);
            writer.Write(texture.UvPrimvar);
            writer.Write((uint)texture.WrapS);
            writer.Write((uint)texture.WrapT);
            writer.Write((uint)texture.SourceColorSpace);
            writer.Write(texture.ComponentCount);
        }
        writer.Write(GeneratedFragmentSpirV.Length);
        writer.Write(GeneratedFragmentSpirV.Span);
        writer.Write(GeneratedFragmentMslSource.Length);
        writer.Write(GeneratedFragmentMslSource.Span);
        writer.Flush();
        return stream.ToArray();
    }

    internal string GetPrimaryUvPrimvar()
    {
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (!string.IsNullOrEmpty(texture.UvPrimvar))
            {
                return texture.UvPrimvar;
            }
        }
        return string.Empty;
    }
}
