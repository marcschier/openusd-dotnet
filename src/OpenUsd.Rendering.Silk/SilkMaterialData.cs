// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

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
        SilkTextureChannel channel,
        float[] scale,
        float[] bias,
        float[] fallback,
        string asset,
        string uvPrimvar,
        SilkCompositeOperator compositeOperator,
        float compositeFactor,
        Vector4 luminanceMix = default)
    {
        Parameter = parameter;
        WrapS = wrapS;
        WrapT = wrapT;
        SourceColorSpace = sourceColorSpace;
        ComponentCount = componentCount;
        Channel = channel;
        Scale = scale;
        Bias = bias;
        Fallback = fallback;
        Asset = asset;
        UvPrimvar = uvPrimvar;
        CompositeOperator = compositeOperator;
        CompositeFactor = compositeFactor;
        LuminanceMix = luminanceMix;
    }

    internal Vector4 LuminanceMix { get; }

    /// <summary>Gets the parameter this texture drives.</summary>
    public SilkMaterialParameter Parameter { get; }

    /// <summary>
    /// Gets how this entry combines with the primary entry of the same parameter.
    /// </summary>
    /// <remarks>
    /// <see cref="SilkCompositeOperator.None"/> marks the primary entry. Any
    /// other value marks the second operand of a two-image surface input, and the
    /// material then also carries exactly one primary entry for the same
    /// parameter. A material carries at most one such operand in total, because
    /// the renderer binds one composite image per material.
    /// </remarks>
    public SilkCompositeOperator CompositeOperator { get; }

    /// <summary>
    /// Gets the blend factor, meaningful only for
    /// <see cref="SilkCompositeOperator.Mix"/>.
    /// </summary>
    public float CompositeFactor { get; }

    /// <summary>Gets the horizontal wrap mode.</summary>
    public SilkTextureWrap WrapS { get; }

    /// <summary>Gets the vertical wrap mode.</summary>
    public SilkTextureWrap WrapT { get; }

    /// <summary>Gets the authored source color space.</summary>
    public SilkColorSpace SourceColorSpace { get; }

    /// <summary>Gets how many channels the bound input consumes.</summary>
    public int ComponentCount { get; }

    /// <summary>
    /// Gets the connected output port or explicit scalar opacity reduction. Two inputs driven by the same
    /// file are told apart by this alone -- a packed occlusion/roughness/metallic
    /// image is exactly one asset with two or three different output connections.
    /// </summary>
    public SilkTextureChannel Channel { get; }

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
    /// <remarks>
    /// Every texture of one material reports the same primvar in practice: the
    /// renderer builds one coordinate stream per material and takes its name from
    /// the first entry that names one, so hdSilk reconciles this across a
    /// material's textures and drops an entry whose primvar diverges rather than
    /// publishing one that would be sampled through another entry's coordinates.
    /// </remarks>
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
        float[] uvTransform,
        uint alternateUvParameterMask,
        float[] alternateUvTransform,
        byte[] generatedFragmentSpirV,
        byte[] generatedFragmentMslSource)
    {
        Path = path;
        StableHash = stableHash;
        SurfaceKind = surfaceKind;
        Scalars = scalars;
        Textures = textures;
        UvTransform = uvTransform;
        AlternateUvParameterMask = alternateUvParameterMask;
        AlternateUvTransform = alternateUvTransform;
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

    /// <summary>
    /// Gets the primary constant texture-coordinate transform of this material,
    /// as the row-major affine (m00, m01, m10, m11, tx, ty).
    /// </summary>
    /// <remarks>
    /// The identity unless hdSilk folded a chain of constant MaterialX place2d
    /// or UsdPreviewSurface UsdTransform2d nodes. It is the first surface
    /// texture's transform. Every surface texture whose parameter is absent from
    /// <see cref="AlternateUvParameterMask"/> samples through it, and so does
    /// displacement. A transform applies to whichever of the material's two
    /// coordinate sets a texture reads. Surface textures may name at most two
    /// distinct <see cref="SilkMaterialTexture.UvPrimvar"/> values and at most
    /// two transforms; hdSilk drops a texture asking for a third of either with
    /// a diagnostic rather than sampling coordinates it did not author. Detail
    /// normals carry independent affine rows through
    /// <see cref="SilkMaterialParameter.DetailUvRow0"/> and
    /// <see cref="SilkMaterialParameter.DetailUvRow1"/>.
    /// </remarks>
    public IReadOnlyList<float> UvTransform { get; }

    /// <summary>
    /// Gets the surface parameters whose textures sample through
    /// <see cref="AlternateUvTransform"/>, as bit <c>1 &lt;&lt; parameter</c>.
    /// </summary>
    /// <remarks>
    /// Each bit names a surface texture this material carries. Displacement,
    /// detail normals and volume density are never masked, and both entries of
    /// a composited parameter read one transform.
    /// </remarks>
    public uint AlternateUvParameterMask { get; }

    /// <summary>
    /// Gets the material's second constant texture-coordinate transform, in the
    /// same row-major order as <see cref="UvTransform"/>. It is the identity
    /// when <see cref="AlternateUvParameterMask"/> is zero.
    /// </summary>
    public IReadOnlyList<float> AlternateUvTransform { get; }

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
        SilkSurfaceKind.VolumeDensity or
        SilkSurfaceKind.MdlDistilled;

    /// <summary>
    /// Gets whether this material is an MDL-only surface this runtime could not
    /// distil, so the reason it is unshaded can be reported as MDL rather than as
    /// an unrecognised graph.
    /// </summary>
    public bool IsMdlUnavailable => SurfaceKind is SilkSurfaceKind.MdlUnavailable;

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
        Vector4 luminanceMix = default;
        bool hasLuminanceMix = false;
        bool hasSpecularLevel = false;
        int detailRows = 0;
        foreach (SilkMaterialScalar scalar in scalars)
        {
            if (scalar.Parameter is SilkMaterialParameter.DetailUvRow0 or SilkMaterialParameter.DetailUvRow1)
            {
                int row = scalar.Parameter == SilkMaterialParameter.DetailUvRow0 ? 1 : 2;
                if ((detailRows & row) != 0)
                {
                    throw new InvalidDataException("A material cannot carry duplicate detail UV rows.");
                }
                detailRows |= row;
            }
            if (scalar.Parameter == SilkMaterialParameter.OmniPbrSpecularLevel)
            {
                if (hasSpecularLevel)
                {
                    throw new InvalidDataException("A material cannot carry duplicate OmniPBR specular levels.");
                }
                hasSpecularLevel = true;
            }
            if (scalar.Parameter == SilkMaterialParameter.DiffuseTextureLuminanceMix)
            {
                if (hasLuminanceMix)
                {
                    throw new InvalidDataException("A material cannot carry duplicate diffuse luminance mixes.");
                }
                hasLuminanceMix = true;
                luminanceMix = new Vector4(
                    scalar.Values[0], scalar.Values[1], scalar.Values[2], scalar.Values[3]);
            }
        }
        if (detailRows is 1 or 2)
        {
            throw new InvalidDataException("Detail UV transforms require both rows.");
        }
        bool hasDetailNormal = false;
        for (int index = 0; index < textures.Length; index++)
        {
            SilkMaterialTextureEntry entry = command.GetTexture(index);
            if (entry.Parameter == SilkMaterialParameter.DetailNormal)
            {
                if (hasDetailNormal || entry.ComponentCount != 3 ||
                    entry.Channel != SilkTextureChannel.Rgb || entry.CompositeOperator != SilkCompositeOperator.None ||
                    command.SurfaceKind == SilkSurfaceKind.VolumeDensity)
                {
                    throw new InvalidDataException("Invalid or duplicate detail normal texture.");
                }
                hasDetailNormal = true;
            }
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
                entry.Channel,
                scale,
                bias,
                fallback,
                entry.Asset,
                entry.UvPrimvar,
                entry.CompositeOperator,
                entry.CompositeFactor,
                entry.Parameter == SilkMaterialParameter.DiffuseColor ? luminanceMix : default);
        }

        byte[] generatedFragmentSpirV = command.GeneratedFragmentSpirV.ToArray();
        byte[] generatedFragmentMslSource = command.GeneratedFragmentMslSource.ToArray();
        float[] uvTransform = new float[6];
        for (int index = 0; index < uvTransform.Length; index++)
        {
            uvTransform[index] = command.GetUvTransform(index);
        }
        float[] alternateUvTransform = new float[6];
        for (int index = 0; index < alternateUvTransform.Length; index++)
        {
            alternateUvTransform[index] = command.GetAlternateUvTransform(index);
        }
        uint alternateUvParameterMask = command.AlternateUvParameterMask;
        uint maskableParameters = 0;
        foreach (SilkMaterialTexture texture in textures)
        {
            if (IsAlternateUvCandidate(texture.Parameter))
            {
                maskableParameters |= 1u << (int)texture.Parameter;
            }
        }
        if ((alternateUvParameterMask & ~maskableParameters) != 0)
        {
            throw new InvalidDataException(
                "The material alternate UV transform names a parameter with no surface texture.");
        }
        return new SilkMaterialData(
            command.Path,
            command.StableHash,
            command.SurfaceKind,
            scalars,
            textures,
            uvTransform,
            alternateUvParameterMask,
            alternateUvTransform,
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
            if (texture.Parameter == parameter &&
                texture.CompositeOperator == SilkCompositeOperator.None)
            {
                return texture;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the material's single composite operand, or null when no surface
    /// input of this material is driven by two images.
    /// </summary>
    /// <remarks>
    /// The entry names the parameter it combines into and the operator to use.
    /// Its primary half is the ordinary <see cref="GetTexture"/> entry for that
    /// same parameter, which is why <see cref="GetTexture"/> skips composite
    /// operands rather than returning whichever entry it reaches first.
    /// </remarks>
    public SilkMaterialTexture? GetCompositeTexture()
    {
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (texture.CompositeOperator != SilkCompositeOperator.None)
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
        if (GetTexture(SilkMaterialParameter.DetailNormal) is not null)
        {
            features |= SilkShaderFeatures.DetailNormalMap;
        }

        // Roughness and metallic are separate UsdPreviewSurface inputs with
        // separate connections, so each selects its own bit. A packed
        // occlusion/roughness/metallic file reaches here as two entries naming one
        // asset and two different output channels, and still resolves correctly
        // because each entry carries the channel it was authored with.
        if (GetTexture(SilkMaterialParameter.Roughness) is not null)
        {
            features |= SilkShaderFeatures.RoughnessMetallicMap;
        }

        if (GetTexture(SilkMaterialParameter.Metallic) is not null)
        {
            features |= SilkShaderFeatures.MetallicMap;
        }

        if (GetTexture(SilkMaterialParameter.EmissiveColor) is not null)
        {
            features |= SilkShaderFeatures.EmissiveMap;
        }
        if (GetTexture(SilkMaterialParameter.Opacity) is not null)
        {
            features |= SilkShaderFeatures.OpacityMap;
        }
        if (GetTexture(SilkMaterialParameter.Occlusion) is not null)
        {
            features |= SilkShaderFeatures.OcclusionMap;
        }
        if (GetTexture(SilkMaterialParameter.SpecularColor) is not null)
        {
            features |= SilkShaderFeatures.SpecularColorMap;
        }
        if (GetTexture(SilkMaterialParameter.Clearcoat) is not null)
        {
            features |= SilkShaderFeatures.ClearcoatMap;
        }
        if (GetTexture(SilkMaterialParameter.ClearcoatRoughness) is not null)
        {
            features |= SilkShaderFeatures.ClearcoatRoughnessMap;
        }
        if (GetTexture(SilkMaterialParameter.Ior) is not null)
        {
            features |= SilkShaderFeatures.IorMap;
        }
        return features == SilkShaderFeatures.None
            ? features
            : features | SilkShaderFeatures.Uv;
    }

    internal byte[] CreateRuntimeShaderKeyBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("OpenUsd.Rendering.Silk.MaterialXRuntime.v7");
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
            writer.Write((uint)texture.Channel);
            writer.Write((uint)texture.CompositeOperator);
            writer.Write(texture.CompositeFactor);
        }
        foreach (float element in UvTransform)
        {
            writer.Write(element);
        }
        writer.Write(AlternateUvParameterMask);
        foreach (float element in AlternateUvTransform)
        {
            writer.Write(element);
        }
        writer.Write(GeneratedFragmentSpirV.Length);
        writer.Write(GeneratedFragmentSpirV.Span);
        writer.Write(GeneratedFragmentMslSource.Length);
        writer.Write(GeneratedFragmentMslSource.Span);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Resolves the primary texture-coordinate primvar this material samples its
    /// surface textures through.
    /// </summary>
    /// <remarks>
    /// The first surface entry that names one wins. Surface textures naming a
    /// different primvar sample the second coordinate set
    /// (<see cref="GetSecondaryUvPrimvar"/>); an entry may legitimately name no
    /// primvar at all, and then reads the primary set.
    ///
    /// Displacement is sampled per vertex through its own authored coordinate
    /// set, which may be a different primvar entirely, so it never names the
    /// stream while a surface texture does -- a material shading through
    /// <c>st</c> and displacing through <c>st2</c> must emit <c>st</c>. A
    /// material whose only texture is the height field has no surface sampler to
    /// mis-feed, and keeps emitting the coordinates it names.
    /// </remarks>
    internal string GetPrimaryUvPrimvar()
    {
        string displacementPrimvar = string.Empty;
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (string.IsNullOrEmpty(texture.UvPrimvar))
            {
                continue;
            }
            if (texture.Parameter != SilkMaterialParameter.Displacement)
            {
                return texture.UvPrimvar;
            }
            if (displacementPrimvar.Length == 0)
            {
                displacementPrimvar = texture.UvPrimvar;
            }
        }
        return displacementPrimvar;
    }

    /// <summary>
    /// Resolves the second surface texture-coordinate primvar, or an empty string
    /// when every surface texture reads the primary one.
    /// </summary>
    /// <remarks>
    /// hdSilk admits at most two distinct surface primvars per material and drops
    /// any texture naming a third, so the first surface primvar that differs from
    /// <see cref="GetPrimaryUvPrimvar"/> is the only candidate on any page it
    /// produced. Displacement and volume density are excluded: the former samples
    /// per vertex through its own set and the latter carries extents, not a
    /// primvar.
    /// </remarks>
    internal string GetSecondaryUvPrimvar()
    {
        string primary = GetPrimaryUvPrimvar();
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (IsSurfaceUvTexture(texture) &&
                !string.IsNullOrEmpty(texture.UvPrimvar) &&
                !string.Equals(texture.UvPrimvar, primary, StringComparison.Ordinal))
            {
                return texture.UvPrimvar;
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Gets the texture-mask bits of the surface inputs that sample the second
    /// coordinate set.
    /// </summary>
    internal SilkShaderFeatures GetSecondaryUvTextureMask()
    {
        string secondary = GetSecondaryUvPrimvar();
        if (secondary.Length == 0)
        {
            return SilkShaderFeatures.None;
        }
        SilkShaderFeatures mask = SilkShaderFeatures.None;
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (IsSurfaceUvTexture(texture) &&
                string.Equals(texture.UvPrimvar, secondary, StringComparison.Ordinal))
            {
                mask |= GetSurfaceTextureBit(texture.Parameter);
            }
        }
        return mask;
    }

    /// <summary>
    /// Gets whether the mesh tangent frame must be derived from the second
    /// coordinate set, because the normal map that consumes it samples that set.
    /// </summary>
    internal bool TangentsFromSecondaryUv()
    {
        SilkShaderFeatures mask = GetSecondaryUvTextureMask();
        if (GetTexture(SilkMaterialParameter.Normal) is not null)
        {
            return (mask & SilkShaderFeatures.NormalMap) != 0;
        }
        return (mask & SilkShaderFeatures.DetailNormalMap) != 0;
    }

    /// <summary>
    /// Gets the texture-mask bits of the surface inputs that sample through
    /// <see cref="AlternateUvTransform"/>.
    /// </summary>
    internal SilkShaderFeatures GetAlternateUvTextureMask()
    {
        SilkShaderFeatures mask = SilkShaderFeatures.None;
        foreach (SilkMaterialTexture texture in Textures)
        {
            if (UsesAlternateUvTransform(texture.Parameter))
            {
                mask |= GetSurfaceTextureBit(texture.Parameter);
            }
        }
        return mask;
    }

    /// <summary>
    /// Gets the row-major affine one surface parameter samples its coordinates
    /// through.
    /// </summary>
    internal IReadOnlyList<float> GetUvTransformFor(SilkMaterialParameter parameter) =>
        UsesAlternateUvTransform(parameter) ? AlternateUvTransform : UvTransform;

    private bool UsesAlternateUvTransform(SilkMaterialParameter parameter) =>
        IsAlternateUvCandidate(parameter) &&
        (AlternateUvParameterMask & (1u << (int)parameter)) != 0;

    private static bool IsAlternateUvCandidate(SilkMaterialParameter parameter) =>
        (uint)parameter < 32 &&
        parameter is not SilkMaterialParameter.Displacement and
            not SilkMaterialParameter.VolumeDensity and
            not SilkMaterialParameter.DetailNormal;

    private static bool IsSurfaceUvTexture(SilkMaterialTexture texture) =>
        texture.Parameter is not SilkMaterialParameter.Displacement and
            not SilkMaterialParameter.VolumeDensity;

    private static SilkShaderFeatures GetSurfaceTextureBit(SilkMaterialParameter parameter) =>
        parameter switch
        {
            SilkMaterialParameter.DiffuseColor => SilkShaderFeatures.BaseColorMap,
            SilkMaterialParameter.Normal => SilkShaderFeatures.NormalMap,
            SilkMaterialParameter.DetailNormal => SilkShaderFeatures.DetailNormalMap,
            SilkMaterialParameter.Roughness => SilkShaderFeatures.RoughnessMetallicMap,
            SilkMaterialParameter.Metallic => SilkShaderFeatures.MetallicMap,
            SilkMaterialParameter.EmissiveColor => SilkShaderFeatures.EmissiveMap,
            SilkMaterialParameter.Opacity => SilkShaderFeatures.OpacityMap,
            SilkMaterialParameter.Occlusion => SilkShaderFeatures.OcclusionMap,
            SilkMaterialParameter.SpecularColor => SilkShaderFeatures.SpecularColorMap,
            SilkMaterialParameter.Clearcoat => SilkShaderFeatures.ClearcoatMap,
            SilkMaterialParameter.ClearcoatRoughness => SilkShaderFeatures.ClearcoatRoughnessMap,
            SilkMaterialParameter.Ior => SilkShaderFeatures.IorMap,
            _ => SilkShaderFeatures.None
        };
}
