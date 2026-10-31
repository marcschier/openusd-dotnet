// Copyright (c) marcschier. Licensed under the MIT License.

struct SurfaceParameters
{
    float4 diffuseOpacity;
    float4 emissiveOcclusion;
    float4 specularIor;
    float4 metallicRoughnessThresholdWorkflow;
    // (clearcoat, clearcoatRoughness, shaded, OmniPBR dielectric specular weight).
    float4 clearcoatShaded;
    float4 lightDirectionIntensity;
    float4 lightColorAmbient;
    float4 reserved;
    // (textureMask, udimMask, volumeVoxelDepth, OmniPBR custom-curve enabled).
    float4 textureControls;
    // The primary constant texture-coordinate transform, as (m00, m01, tx, 0)
    // and (m10, m11, ty, secondaryMask). Identity unless a constant MaterialX
    // place2d or UsdTransform2d chain was folded. secondaryMask holds the
    // textureControls.x bits of the textures that sample the second coordinate
    // set (texCoord1) instead of the first. Slots in alternateUvMask
    // (domeLinkControls.w) read through alternateUvTransformRow0/1 instead.
    float4 uvTransformRow0;
    float4 uvTransformRow1;
    // The two-image composite, as (targetTextureMaskBit, operator, factor, udim).
    // targetTextureMaskBit is zero when this material binds no composite image;
    // otherwise it is the same bit textureControls.x uses for the parameter the
    // composite drives, so exactly one of the slots below combines. operator is
    // one of the CompositeOp constants and factor is meaningful only for mix.
    float4 compositeControls;
    // (domeLinkMask, constantBorderMaskU, constantBorderMaskV, alternateUvMask). domeLinkMask
    // is the UsdLux dome-link mask of the prim this draw covers: bit i is set
    // when dome light i of the frame dome table illuminates it. It is a slot of
    // its own rather than more bits of clearcoatShaded.w because the dome table
    // and the direct-light table are two orderings -- dome 0 and direct light 0
    // are different lights. The two border masks carry the textureControls.x
    // bit of every slot whose image has a one-texel ring of its MaterialX
    // constant default on that axis; bit 1 stands for the composite operand.
    float4 domeLinkControls;
    // Bit i is in word i/32, bit i%32. Never convert direct membership to float.
    uint4 lightLinkMask;
    uint4 shadowLinkMask;
    float4 detailUvTransformRow0;
    float4 detailUvTransformRow1;
    // The material's second folded transform, (m00, m01, tx, 0) and (m10, m11, ty, 0).
    float4 alternateUvTransformRow0;
    float4 alternateUvTransformRow1;
};

[ForceInline]
uint LoadUdimByte(Texture2D<float4> textureMap, uint width, uint offset)
{
    return uint(round(textureMap.Load(int3(offset % width, offset / width, 0)).r * 255.0));
}

/// Selects the constant-border axes (bit 0 = u, bit 1 = v) of one slot.
[ForceInline]
uint ConstantBorderAxes(SurfaceParameters surface, uint slotBit)
{
    uint maskU = (uint)round(surface.domeLinkControls.y);
    uint maskV = (uint)round(surface.domeLinkControls.z);
    return ((maskU & slotBit) != 0 ? 1u : 0u) | ((maskV & slotBit) != 0 ? 2u : 0u);
}

/// Maps a unit-range coordinate into the interior of an image that carries a
/// one-texel ring of its MaterialX constant default, on each masked axis.
///
/// Inside [0, 1] the coordinate addresses the authored texels exactly; outside
/// it clamps to the centre of the ring texel, so a level-zero linear sample
/// returns the default and an edge sample blends into it over half a texel.
/// Lower mip levels average the ring with the outermost texels, so a minified
/// edge carries a little of the default inward.
[ForceInline]
float2 ConstantBorderCoordinate(Texture2D<float4> textureMap, float2 textureCoordinate, uint borderAxes)
{
    if (borderAxes == 0u)
    {
        return textureCoordinate;
    }
    uint width;
    uint height;
    textureMap.GetDimensions(width, height);
    float2 size = float2(width, height);
    float2 remapped = clamp(
        (textureCoordinate * (size - 2.0) + 1.0) / size,
        0.5 / size,
        (size - 0.5) / size);
    return float2(
        (borderAxes & 1u) != 0 ? remapped.x : textureCoordinate.x,
        (borderAxes & 2u) != 0 ? remapped.y : textureCoordinate.y);
}

[ForceInline]
float4 SampleMaterialTexture(
    Texture2D<float4> textureMap,
    SamplerState textureSampler,
    float2 textureCoordinate,
    bool isUdim,
    uint borderAxes)
{
    if (!isUdim)
    {
        return textureMap.Sample(
            textureSampler,
            ConstantBorderCoordinate(textureMap, textureCoordinate, borderAxes));
    }

    uint atlasWidth;
    uint atlasHeight;
    textureMap.GetDimensions(atlasWidth, atlasHeight);
    float4 metadata = round(textureMap.Load(int3(0, 0, 0)) * 255.0);
    if (metadata.x == 255.0)
    {
        int2 minimumTile = int2(LoadUdimByte(textureMap, atlasWidth, 2), LoadUdimByte(textureMap, atlasWidth, 3));
        uint2 grid = uint2(LoadUdimByte(textureMap, atlasWidth, 4), LoadUdimByte(textureMap, atlasWidth, 5));
        int2 tile = int2(floor(textureCoordinate)) - minimumTile;
        if (any(tile < 0) || any(tile >= int2(grid)))
        {
            return textureMap.Load(int3(1, 0, 0));
        }
        uint offset = 10 + 2 * (uint(tile.y) * grid.x + uint(tile.x));
        uint slot = LoadUdimByte(textureMap, atlasWidth, offset) |
            (LoadUdimByte(textureMap, atlasWidth, offset + 1) << 8);
        if (slot == 0)
        {
            return textureMap.Load(int3(1, 0, 0));
        }
        --slot;
        uint columns = LoadUdimByte(textureMap, atlasWidth, 6) |
            (LoadUdimByte(textureMap, atlasWidth, 7) << 8);
        uint count = LoadUdimByte(textureMap, atlasWidth, 8) |
            (LoadUdimByte(textureMap, atlasWidth, 9) << 8);
        uint rows = (count + columns - 1) / columns;
        uint metadataRows = (10 + 2 * grid.x * grid.y + atlasWidth - 1) / atlasWidth;
        float2 cellSize = float2(atlasWidth / columns, (atlasHeight - metadataRows) / rows);
        float2 pixel = float2(slot % columns, slot / columns) * cellSize +
            float2(1.5, metadataRows + 1.5) + frac(textureCoordinate) * max(cellSize - 3.0, 0.0);
        return textureMap.Sample(textureSampler, pixel / float2(atlasWidth, atlasHeight));
    }
    int2 minimumTile = int2(metadata.xy);
    int2 gridSize = int2(metadata.zw);
    int2 tile = int2(floor(textureCoordinate)) - minimumTile;
    if (any(tile < 0) || any(tile >= gridSize))
    {
        return textureMap.Load(int3(min(1u, atlasWidth - 1), 0, 0));
    }

    float2 cellSize = float2(
        atlasWidth / gridSize.x,
        (atlasHeight - 1) / gridSize.y);
    float2 tileSize = cellSize - 2.0;
    float2 pixel = float2(tile) * cellSize +
        float2(1.5, 2.5) +
        frac(textureCoordinate) * max(tileSize - 1.0, 0.0);
    return textureMap.Sample(
        textureSampler,
        pixel / float2(atlasWidth, atlasHeight));
}

[ForceInline]
float2 TransformTextureCoordinate(float4 row0, float4 row1, float2 textureCoordinate)
{
    return float2(
        dot(row0.xy, textureCoordinate) + row0.z,
        dot(row1.xy, textureCoordinate) + row1.z);
}

// One slot's coordinate: the set comes from secondaryMask and the transform
// from alternateMask, independently.
[ForceInline]
float2 SelectTexCoord(
    uint bit,
    uint secondaryMask,
    uint alternateMask,
    float2 primary,
    float2 secondary,
    float2 alternatePrimary,
    float2 alternateSecondary)
{
    bool useSecondary = (secondaryMask & bit) != 0;
    return (alternateMask & bit) != 0
        ? (useSecondary ? alternateSecondary : alternatePrimary)
        : (useSecondary ? secondary : primary);
}

// Wire values of OPENUSD_SILK_COMPOSITE_*; see openusd_hdsilk.h.
static const uint CompositeOpNone = 0u;
static const uint CompositeOpMultiply = 1u;
static const uint CompositeOpAdd = 2u;
static const uint CompositeOpSubtract = 3u;
static const uint CompositeOpMix = 4u;

#if MAP_MATERIAL
/// Combines the primary sample of one material slot with the material's single
/// composite image, when that slot is the one the composite drives.
///
/// The whole expression is evaluated here rather than folded into the primary
/// entry's scale and bias because the product, sum, difference or blend of two
/// images is not affine in either one. It runs in floating point after both
/// decodes, so the eight-bit unit-range restriction that governs the per-entry
/// affines does not apply to it.
///
/// `slotBit` is the same textureControls.x bit the slot already uses, so the
/// comparison is exact and no second identifier space is introduced.
[ForceInline]
float4 ApplyMaterialComposite(
    SurfaceParameters surface,
    float slotBit,
    float4 primary,
    float2 textureCoordinate)
{
    if (surface.compositeControls.x != slotBit)
    {
        return primary;
    }
    // The composite's own UDIM bit, not the driven slot's. The two operands of
    // one surface input are independent assets and either may be a UDIM set on
    // its own, so reusing the slot's bit sampled a plain image through the atlas
    // path -- which reads its first texel as tile metadata -- or sampled an atlas
    // with raw coordinates.
    float4 composite = SampleMaterialTexture(
        compositeTexture,
        compositeSampler,
        textureCoordinate,
        surface.compositeControls.w >= 0.5,
        ConstantBorderAxes(surface, 1u));
    uint operation = (uint)round(surface.compositeControls.y);
    if (operation == CompositeOpMultiply)
    {
        return primary * composite;
    }
    if (operation == CompositeOpAdd)
    {
        return primary + composite;
    }
    if (operation == CompositeOpSubtract)
    {
        return primary - composite;
    }
    if (operation == CompositeOpMix)
    {
        float factor = surface.compositeControls.z;
        return primary * (1.0 - factor) + composite * factor;
    }
    return primary;
}

#if FRAGMENT_STAGE
[ForceInline]
float SampleMaterialOpacity(SurfaceParameters surface, float2 textureCoordinate)
{
    uint udimMask = (uint)round(surface.textureControls.y);
    return saturate(
        ApplyMaterialComposite(
            surface,
            64.0,
            SampleMaterialTexture(
                opacityTexture,
                opacitySampler,
                textureCoordinate,
                (udimMask & 64u) != 0,
                ConstantBorderAxes(surface, 64u)),
            textureCoordinate).r);
}
#endif
#endif
