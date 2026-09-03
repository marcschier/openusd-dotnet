// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Packs the constant UsdPreviewSurface inputs and the single deterministic light
/// into the surface constant block the mesh fragment shader reads.
/// </summary>
/// <remarks>
/// Deliberately separate from the scene constants. Those are pinned at exactly 80
/// bytes by contract and their layout is mirrored by the instance table, so
/// widening them to carry material values would change the instancing wire format
/// for no benefit. The light rides here rather than in its own binding because it
/// is four floats and a second slot would cost a second upload path everywhere.
/// </remarks>
internal static class SilkSurfaceUniformWriter
{
    /// <summary>Byte size of the surface constant block.</summary>
    /// <remarks>
    /// float4 diffuseColor+opacity, emissiveColor+occlusion, specularColor+ior,
    /// (metallic, roughness, opacityThreshold, useSpecularWorkflow),
    /// (clearcoat, clearcoatRoughness, shaded, lightLinkMask),
    /// lightDirection+intensity,
    /// lightColor+ambient, volume values,
    /// (textureMask, udimMask, volumeVoxelDepth, shadowLinkMask),
    /// the
    /// two folded MaterialX UV transform rows (m00, m01, tx, 0) and
    /// (m10, m11, ty, 0), the two-image composite controls
    /// (targetTextureMaskBit, operator, factor, 0), and the dome link controls
    /// (domeLinkMask, 0, 0, 0).
    /// </remarks>
    internal const int ByteSize = 208;

    // UsdPreviewSurface authored defaults, used when a material omits an input.
    private const float DefaultDiffuse = 0.18f;
    private const float DefaultRoughness = 0.5f;
    private const float DefaultOpacity = 1;
    private const float DefaultOcclusion = 1;
    private const float DefaultIor = 1.5f;
    private const float DefaultClearcoatRoughness = 0.01f;

    /// <summary>
    /// Writes the block for one material. A mesh with no supported material bound
    /// gets the shared default block, whose shaded flag is zero so the shader falls
    /// back to the scene display colour -- which is what Storm shows for a prim
    /// whose material is absent or unsupported.
    /// </summary>
    internal static void Write(
        SilkMaterialData? material,
        RenderHeadlight light,
        Span<byte> destination,
        bool supportsVolumeTextures = false,
        SilkLightLinkMasks? linkMasks = null)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"Surface constants must be exactly {ByteSize} bytes.",
                nameof(destination));
        }

        // Absent means "no linking was resolved", which is not the same as
        // "linked to nothing": every caller that has no link table must still
        // light the surface with every light. It is an explicit absence rather
        // than a default-valued struct because an all-zero mask set is a
        // perfectly ordinary resolution -- a prim excluded from every collection
        // -- and treating it as "unresolved" lit exactly the prims the author
        // excluded.
        SilkLightLinkMasks masks = linkMasks ?? SilkLightLinkMasks.All;

        SilkMaterialData? shaded = material is { IsSupported: true } ? material : null;

        // MaterialXGenerated is exactly, and only, the ND_surface_unlit terminal:
        // hdSilk routes standard-surface and OpenPBR graphs through the projected
        // path instead, so a generated material is an unlit surface by
        // construction. That matters here because this permutation stands in for
        // the generated fragment while it compiles and if it fails, and standing
        // in with a lit shading model would light a surface the author explicitly
        // declared unlit -- including with the prefiltered environment, which an
        // unlit surface must never receive. Mode 2 makes the checked fragment
        // return the surface colour with no lighting at all.
        bool unlit = shaded?.SurfaceKind == SilkSurfaceKind.MaterialXGenerated;
        Vector3 diffuse = Vector(
            shaded,
            SilkMaterialParameter.DiffuseColor,
            new Vector3(DefaultDiffuse, DefaultDiffuse, DefaultDiffuse));
        float opacity = Scalar(shaded, SilkMaterialParameter.Opacity, DefaultOpacity);
        Vector3 emissive = Vector(shaded, SilkMaterialParameter.EmissiveColor, Vector3.Zero);
        float occlusion = Scalar(shaded, SilkMaterialParameter.Occlusion, DefaultOcclusion);
        Vector3 specular = Vector(shaded, SilkMaterialParameter.SpecularColor, Vector3.Zero);
        float ior = Scalar(shaded, SilkMaterialParameter.Ior, DefaultIor);
        float metallic = Scalar(shaded, SilkMaterialParameter.Metallic, 0);
        float roughness = Scalar(shaded, SilkMaterialParameter.Roughness, DefaultRoughness);
        float opacityThreshold = Scalar(shaded, SilkMaterialParameter.OpacityThreshold, 0);
        float specularWorkflow = Scalar(shaded, SilkMaterialParameter.UseSpecularWorkflow, 0);
        float clearcoat = Scalar(shaded, SilkMaterialParameter.Clearcoat, 0);
        float clearcoatRoughness = Scalar(
            shaded,
            SilkMaterialParameter.ClearcoatRoughness,
            DefaultClearcoatRoughness);
        bool volumeDensity = shaded?.SurfaceKind == SilkSurfaceKind.VolumeDensity;
        bool sampledVolume = supportsVolumeTextures &&
            shaded?.GetTexture(SilkMaterialParameter.VolumeDensity) is not null;
        float density = Scalar(shaded, SilkMaterialParameter.VolumeDensity, 0);
        WriteVector4(destination, 0, diffuse.X, diffuse.Y, diffuse.Z, opacity);
        WriteVector4(destination, 16, emissive.X, emissive.Y, emissive.Z, occlusion);
        WriteVector4(destination, 32, specular.X, specular.Y, specular.Z, ior);
        WriteVector4(destination, 48, metallic, roughness, opacityThreshold, specularWorkflow);
        WriteVector4(
            destination,
            64,
            clearcoat,
            clearcoatRoughness,
            unlit ? 2 : shaded is null ? 0 : 1,
            // The UsdLux light-link mask for the prim this block is drawn for.
            // It rides in the surface block rather than in a binding of its own
            // because the block is already bound per draw and has an unused
            // component here; a mask of eight bits is exactly representable as a
            // float, so the shader reads it back without rounding.
            masks.LightMask & SilkLightLinkMasks.AllBits);

        Vector3 direction = Normalize(light.Direction);
        WriteVector4(
            destination,
            80,
            direction.X,
            direction.Y,
            direction.Z,
            Finite(light.Intensity, "light intensity"));
        WriteVector4(
            destination,
            96,
            Finite(light.Color.X, "light red"),
            Finite(light.Color.Y, "light green"),
            Finite(light.Color.Z, "light blue"),
            Finite(light.Ambient, "light ambient"));
        float udimMask = volumeDensity ? 0 : GetUdimMask(shaded);
        WriteVector4(
            destination,
            112,
            volumeDensity ? 1 : 0,
            density,
            2,
            sampledVolume ? 1 : 0);
        // The density grid's own Z resolution, and it is not decoration: the shader
        // integrates one sample per voxel layer, so a wrong or absent depth silently
        // reconstructs the wrong column. Zero for everything that is not a sampled
        // volume, because only that path reads it.
        float volumeDepth = sampledVolume
            ? SilkVolumeTextureExtent.Parse(
                shaded!.GetTexture(SilkMaterialParameter.VolumeDensity)!.UvPrimvar).Depth
            : 0;
        WriteVector4(
            destination,
            128,
            volumeDensity ? 0 : (float)(uint)(shaded?.GetTextureFeatures() ?? SilkShaderFeatures.None),
            udimMask,
            volumeDepth,
            // The UsdLux shadow-link mask for the prim. It is resolved and packed
            // even though no raster shadow pass consumes it yet, so the value a
            // shadow pass will read is produced and regression-gated by exactly
            // the path that produces the light mask, rather than being invented
            // when that pass lands.
            masks.ShadowMask & SilkLightLinkMasks.AllBits);

        // hdSilk publishes one folded MaterialX place2d affine per material, so the
        // shader applies it once to the interpolated coordinate rather than
        // re-deriving a per-texture transform it was never given.
        IReadOnlyList<float>? uv = shaded?.UvTransform;
        WriteVector4(
            destination,
            144,
            uv is null ? 1 : Finite(uv[0], "UV transform m00"),
            uv is null ? 0 : Finite(uv[1], "UV transform m01"),
            uv is null ? 0 : Finite(uv[4], "UV transform tx"),
            0);
        WriteVector4(
            destination,
            160,
            uv is null ? 0 : Finite(uv[2], "UV transform m10"),
            uv is null ? 1 : Finite(uv[3], "UV transform m11"),
            uv is null ? 0 : Finite(uv[5], "UV transform ty"),
            0);

        // The material's single two-image composite. The target is written as the
        // same texture-mask bit the slot already uses, so the shader compares one
        // value rather than mapping a second identifier space, and a material with
        // no composite writes zero, which matches no slot bit.
        SilkMaterialTexture? composite = volumeDensity ? null : shaded?.GetCompositeTexture();
        WriteVector4(
            destination,
            176,
            composite is null ? 0 : (float)(uint)GetTextureFeatureBit(composite.Parameter),
            composite is null ? 0 : (float)(uint)composite.CompositeOperator,
            composite is null ? 0 : Finite(composite.CompositeFactor, "composite factor"),
            // The composite's own UDIM-ness, not the slot's. The two operands of a
            // pair are independent assets and only one of them may be a UDIM set;
            // reusing the primary slot's bit sampled a plain image through the
            // atlas path, which reads its first texel as tile metadata and returns
            // one flat colour for the whole surface.
            composite is not null && IsUdim(composite) ? 1 : 0);

        // The UsdLux dome link mask for the prim this block is drawn for. It is a
        // slot of its own rather than more bits of the light mask because the
        // frame's dome table and its direct-light table are two orderings: dome 0
        // and direct light 0 are different lights, and folding them together would
        // have made every existing mask depend on how many domes a scene authors.
        // Eight bits are exactly representable as a float, so the shader reads it
        // back without rounding.
        WriteVector4(
            destination,
            192,
            masks.DomeMask & SilkLightLinkMasks.AllDomeBits,
            0,
            0,
            0);
    }

    private static bool IsUdim(SilkMaterialTexture texture) =>
        texture.Asset.Contains("<UDIM>", StringComparison.Ordinal);

    /// <summary>
    /// Maps a material parameter onto the shader's texture-mask bit for its slot.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="SilkShaderFeatures"/> because the mask the shader reads
    /// from <c>textureControls.x</c> is that same enum, so a composite target and
    /// the slot it drives can never disagree about which bit means which input.
    /// </remarks>
    private static SilkShaderFeatures GetTextureFeatureBit(SilkMaterialParameter parameter) =>
        parameter switch
        {
            SilkMaterialParameter.DiffuseColor => SilkShaderFeatures.BaseColorMap,
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

    private static float GetUdimMask(SilkMaterialData? material)
    {
        int mask = 0;
        addUdimBit(SilkMaterialParameter.DiffuseColor, SilkShaderFeatures.BaseColorMap);
        addUdimBit(SilkMaterialParameter.Normal, SilkShaderFeatures.NormalMap);
        addUdimBit(SilkMaterialParameter.Roughness, SilkShaderFeatures.RoughnessMetallicMap);
        addUdimBit(SilkMaterialParameter.EmissiveColor, SilkShaderFeatures.EmissiveMap);
        addUdimBit(SilkMaterialParameter.Metallic, SilkShaderFeatures.MetallicMap);
        addUdimBit(SilkMaterialParameter.Opacity, SilkShaderFeatures.OpacityMap);
        addUdimBit(SilkMaterialParameter.Occlusion, SilkShaderFeatures.OcclusionMap);
        addUdimBit(SilkMaterialParameter.SpecularColor, SilkShaderFeatures.SpecularColorMap);
        addUdimBit(SilkMaterialParameter.Clearcoat, SilkShaderFeatures.ClearcoatMap);
        addUdimBit(
            SilkMaterialParameter.ClearcoatRoughness,
            SilkShaderFeatures.ClearcoatRoughnessMap);
        addUdimBit(SilkMaterialParameter.Ior, SilkShaderFeatures.IorMap);
        return mask;

        void addUdimBit(SilkMaterialParameter parameter, SilkShaderFeatures feature)
        {
            // The primary entry only. A composite operand carries its own UDIM bit
            // in compositeControls.w, because the two operands of one input are
            // independent assets and either may be a UDIM set on its own.
            if (material?.GetTexture(parameter) is { } texture && IsUdim(texture))
            {
                mask |= (int)feature;
            }
        }
    }

    private static Vector3 Normalize(Vector3 direction)
    {
        // A zero or non-finite direction would produce NaN lighting across the whole
        // surface, which is far harder to diagnose than an obviously wrong constant.
        float lengthSquared = direction.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return new Vector3(0, 0, 1);
        }

        return direction / MathF.Sqrt(lengthSquared);
    }

    private static bool TryReadVector(
        SilkMaterialData material,
        SilkMaterialParameter parameter,
        out Vector3 value)
    {
        ReadOnlySpan<float> values = material.GetScalar(parameter);
        if (values.Length < 3)
        {
            value = default;
            return false;
        }

        value = new Vector3(
            Finite(values[0], parameter, 0),
            Finite(values[1], parameter, 1),
            Finite(values[2], parameter, 2));
        return true;
    }

    private static Vector3 Vector(
        SilkMaterialData? material,
        SilkMaterialParameter parameter,
        Vector3 fallback) =>
        material is not null && TryReadVector(material, parameter, out Vector3 value)
            ? value
            : fallback;

    private static float Scalar(
        SilkMaterialData? material,
        SilkMaterialParameter parameter,
        float fallback)
    {
        if (material is null)
        {
            return fallback;
        }

        ReadOnlySpan<float> values = material.GetScalar(parameter);
        return values.Length == 0 ? fallback : Finite(values[0], parameter, 0);
    }

    private static float Finite(float value, SilkMaterialParameter parameter, int component)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Material parameter {parameter} component {component} is not finite.");
        }

        return value;
    }

    private static float Finite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"The {name} is not finite.");
        }

        return value;
    }

    private static void WriteVector4(
        Span<byte> destination,
        int offset,
        float x,
        float y,
        float z,
        float w)
    {
        WriteSingle(destination, offset, x);
        WriteSingle(destination, offset + 4, y);
        WriteSingle(destination, offset + 8, z);
        WriteSingle(destination, offset + 12, w);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));
}
