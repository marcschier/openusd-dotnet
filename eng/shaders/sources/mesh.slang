// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef VERTEX_ENTRY_POINT
#define VERTEX_ENTRY_POINT vertexMain
#endif
#ifndef FRAGMENT_ENTRY_POINT
#define FRAGMENT_ENTRY_POINT fragmentMain
#endif
#ifndef HAS_UV
#define HAS_UV 0
#endif
#ifndef VERTEX_STAGE
#define VERTEX_STAGE 0
#endif
#ifndef FRAGMENT_STAGE
#define FRAGMENT_STAGE 0
#endif
#ifndef MAP_MATERIAL
#define MAP_MATERIAL 0
#endif
#ifndef MAP_NORMAL
#define MAP_NORMAL 0
#endif
#ifndef VOLUME_DENSITY_TEXTURES
#define VOLUME_DENSITY_TEXTURES 0
#endif

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
#if HAS_UV
    float2 texCoord : TEXCOORD0;
#endif
#if MAP_NORMAL
    float4 tangent : TANGENT;
#endif
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 eyePosition : TEXCOORD1;
    float3 objectPosition : TEXCOORD2;
    float3 normal : NORMAL;
    float4 tint : COLOR0;
    // The interpolated shading basis in *world* space, which is the space the
    // prefiltered environment maps are baked in. It is carried alongside the
    // object-space normal rather than replacing it because the direct-light loop
    // shades on the object-space one and changing that would move every gated
    // parity image; see the environment block in the fragment stage.
    //
    // worldNormal is transformed by the cofactor matrix of the object-to-world
    // upper 3x3, which is the inverse transpose up to a positive scale, so a
    // non-uniformly scaled prim reflects the sky its surface actually faces
    // rather than one skewed by the scale. It is not normalized here: an
    // interpolated normal has to be renormalized per fragment anyway.
    float3 worldNormal : TEXCOORD3;
#if HAS_UV
    float2 texCoord : TEXCOORD0;
#endif
#if MAP_NORMAL
    float4 tangent : TANGENT;
    // The world-space tangent, transformed by the object-to-world 3x3 itself
    // rather than by its inverse transpose: a tangent is a direction along the
    // surface, not a normal to it. w carries the authored bitangent sign.
    float4 worldTangent : TEXCOORD4;
#endif
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

#if VERTEX_STAGE || FRAGMENT_STAGE
struct FrameParameters
{
    float4x4 clipToEye;
    uint4 clipPlaneCount;
    float4 clipPlanes[8];
    float4 ambientLight;
    float4 lightPositionType[8];
    float4 lightDirectionRadius[8];
    float4 lightColorIntensity[8];
    float4 lightControls[8];
    float4 lightTangentShapeX[8];
    float4 lightBitangentShapeY[8];
    float4x4 eyeToWorld;
    // Raster shadow state. shadowWorldToLightClip[slot] projects a world-space
    // position into the slot's light clip space with [0,1] depth; shadowTile[slot]
    // is (offsetU, offsetV, scaleU, scaleV) of the slot's tile inside the single
    // shadow atlas; shadowControls[slot] is (depthBias, normalBias, pcfRadius in
    // texels, one shadow texel in atlas UV); shadowSlots[light].x is the slot the
    // direct light at that index samples, or -1 when the light casts no shadow.
    float4x4 shadowWorldToLightClip[4];
    float4 shadowTile[4];
    float4 shadowControls[4];
    float4 shadowSlots[8];
    // Prefiltered image-based environment lighting.
    //   x: non-zero when the environment resources are live for this frame.
    //   y: the number of prefiltered roughness slices in the specular atlas.
    //   z: the height in texels of one slice, which fixes the half-texel inset
    //      that keeps a slice's bilinear taps out of its neighbours.
    //   w: non-zero when the scene authors at least one dome light, whether or
    //      not this renderer resolved it into an environment. Separate from x so
    //      that a dome which is authored black, authored specular-only, or fell
    //      back to a zero ambient still counts as scene lighting and still
    //      suppresses the deterministic headlight.
    // There is no orientation matrix here on purpose: every map is baked in
    // world space, so each dome's authored light-to-world rotation is already
    // resolved in the texels.
    float4 environmentControls;
    // The bounded UsdLux dome table.
    //   x: the number of dome lights a per-prim dome mask addresses. Zero when
    //      no dome is individually addressable, which is what a scene with no
    //      dome light and a scene over the dome budget both produce.
    //   y: the number of environment groups the two atlases carry. One when the
    //      scene links no dome, in which case the composed bake is the whole
    //      atlas and is addressed exactly as it was before dome linking existed.
    //   z: the group index of the all-domes bake.
    //   w: the height in texels of one irradiance group, which fixes the
    //      half-texel inset that keeps a group's bilinear taps out of its
    //      neighbours.
    float4 domeControls;
    // Per dome: rgb is the ambient term that dome contributes on its own and w
    // is non-zero when the entry names a published dome. Summing the rgb of
    // every published dome reproduces ambientLight.rgb, because the producer
    // accumulated that value from these exact summands in this exact order.
    float4 domeAmbient[8];
    // Per dome: x is the environment group that dome's prefiltered response
    // lives in, or -1 for a dome that has no prefiltered response and
    // contributes through domeAmbient alone.
    float4 domeEnvironment[8];
};

[[vk::binding(8, 0)]]
StructuredBuffer<FrameParameters> frameParameters : register(t8);
#endif

#if VERTEX_STAGE
struct InstanceParameters
{
    float4x4 objectToClip;
    float4 tint;
};

[[vk::binding(6, 0)]]
StructuredBuffer<InstanceParameters> instanceParameters : register(t6);
#endif

#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(1, 0)]]
SamplerState baseColorSampler : register(s0);
#endif
#if FRAGMENT_STAGE && MAP_NORMAL
[[vk::binding(10, 0)]]
SamplerState normalSampler : register(s1);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(11, 0)]]
SamplerState roughnessMetallicSampler : register(s2);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(14, 0)]]
SamplerState metallicSampler : register(s5);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(16, 0)]]
SamplerState opacitySampler : register(s6);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(18, 0)]]
SamplerState occlusionSampler : register(s7);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(20, 0)]]
SamplerState specularColorSampler : register(s8);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(22, 0)]]
SamplerState clearcoatSampler : register(s9);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(24, 0)]]
SamplerState clearcoatRoughnessSampler : register(s10);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(26, 0)]]
SamplerState iorSampler : register(s11);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(12, 0)]]
SamplerState emissiveSampler : register(s3);
#endif
#if FRAGMENT_STAGE && VOLUME_DENSITY_TEXTURES
[[vk::binding(13, 0)]]
SamplerState volumeDensitySampler : register(s4);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(2, 0)]]
Texture2D<float4> baseColorTexture : register(t0);
#endif
#if FRAGMENT_STAGE && MAP_NORMAL
[[vk::binding(3, 0)]]
Texture2D<float4> normalTexture : register(t1);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(4, 0)]]
Texture2D<float4> roughnessMetallicTexture : register(t2);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(15, 0)]]
Texture2D<float4> metallicTexture : register(t4);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(17, 0)]]
Texture2D<float4> opacityTexture : register(t5);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(19, 0)]]
Texture2D<float4> occlusionTexture : register(t10);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(21, 0)]]
Texture2D<float4> specularColorTexture : register(t11);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(23, 0)]]
Texture2D<float4> clearcoatTexture : register(t12);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(25, 0)]]
Texture2D<float4> clearcoatRoughnessTexture : register(t13);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(27, 0)]]
Texture2D<float4> iorTexture : register(t14);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(5, 0)]]
Texture2D<float4> emissiveTexture : register(t3);
#endif

// The one image a material may bind as the second operand of a two-image surface
// input. It is a single universal slot rather than one per material parameter:
// eleven more sampler/texture pairs would be needed for the latter, and every
// mesh pipeline would have to declare them. Which parameter it composites into,
// and with which operator, are runtime values in SurfaceParameters, so this
// changes no shader permutation.
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(28, 0)]]
SamplerState compositeSampler : register(s12);
#endif
#if FRAGMENT_STAGE && MAP_MATERIAL
[[vk::binding(29, 0)]]
Texture2D<float4> compositeTexture : register(t15);
#endif

#if FRAGMENT_STAGE && VOLUME_DENSITY_TEXTURES
[[vk::binding(9, 0)]]
Texture3D<float> volumeDensityTexture : register(t9);
#endif

// The single shadow atlas every mesh permutation samples, and its sampler. One
// atlas rather than one texture per shadow map: a per-map slot would add four
// sampled-texture bindings to every mesh pipeline, and the tile rectangle a map
// occupies is a runtime value in the frame constants instead. A frame with no
// shadow map still binds this slot, because the checked binary references it in
// every permutation; every light then names slot -1 and the atlas is never read.
#if FRAGMENT_STAGE
[[vk::binding(30, 0)]]
SamplerState shadowSampler : register(s13);
[[vk::binding(31, 0)]]
Texture2D<float> shadowAtlas : register(t16);
#endif

// The prefiltered environment resources every mesh fragment permutation samples.
// All three are equirectangular or tabulated in world orientation, so the
// fragment needs no per-dome loop and no rotation state:
//
//   environmentIrradiance  the cosine-convolved irradiance E(n) in each world
//                          normal direction, one equirectangular image.
//   environmentSpecular    the GGX-prefiltered radiance, as a vertical atlas of
//                          roughness slices that all share one angular
//                          resolution. It is deliberately *not* a mip chain: a
//                          chain's trailing levels collapse to a 2x1 and a 1x1
//                          image, so the roughest reflection would be a single
//                          texel rather than a real hemispherical integration.
//   environmentBrdf        the split-sum environment BRDF, numerically
//                          integrated from the same GGX distribution and Smith
//                          geometry term the direct-light lobe uses, indexed by
//                          (n.v, roughness) and read as (scale, bias) on F0/F90.
//
// They are declared in every permutation, exactly as the shadow atlas is, so a
// frame with no textured dome binds one-texel stand-ins and never reads them
// because environmentControls.x is zero.
//
// Two samplers, not one. The environment maps wrap in U because longitude is
// periodic and clamp in V because latitude is not -- a V that wrapped would
// reflect the north pole onto the south one -- while the BRDF table is a
// function on a bounded square and has to clamp on both axes, since a wrapped
// n.v of 1 would read the grazing end of the table.
#if FRAGMENT_STAGE
[[vk::binding(32, 0)]]
SamplerState environmentSampler : register(s14);
[[vk::binding(33, 0)]]
Texture2D<float4> environmentIrradiance : register(t17);
[[vk::binding(34, 0)]]
Texture2D<float4> environmentSpecular : register(t18);
[[vk::binding(35, 0)]]
SamplerState environmentBrdfSampler : register(s15);
[[vk::binding(36, 0)]]
Texture2D<float4> environmentBrdf : register(t19);
#endif

#if VERTEX_STAGE
[shader("vertex")]
VertexOutput VERTEX_ENTRY_POINT(VertexInput input, uint instanceId : SV_InstanceID)
{
    InstanceParameters instance = instanceParameters[instanceId];
    FrameParameters frame = frameParameters[0];
    VertexOutput output;
    float4 clipPosition = mul(instance.objectToClip, float4(input.position, 1.0));
    float4 eyePosition = mul(frame.clipToEye, clipPosition);
    output.position = clipPosition;
    output.eyePosition = eyePosition.xyz / eyePosition.w;
    output.objectPosition = input.position;
    output.normal = input.normal;
    output.tint = instance.tint;

    // The object-to-world transform, recovered rather than carried. The instance
    // block is a pinned 80 bytes and does not hold one, but the projection it
    // does hold composes with the frame's own inverses to exactly the same
    // matrix: clipToEye undoes the projection that objectToClip applied -- the
    // managed writer builds both from the same mirrored, depth-converted
    // projection, so the mirror cancels -- and eyeToWorld undoes the view. That
    // is the same identity the eye position above already relies on.
    float4x4 objectToWorld = mul(frame.eyeToWorld, mul(frame.clipToEye, instance.objectToClip));
    float3 basisX = float3(objectToWorld[0][0], objectToWorld[1][0], objectToWorld[2][0]);
    float3 basisY = float3(objectToWorld[0][1], objectToWorld[1][1], objectToWorld[2][1]);
    float3 basisZ = float3(objectToWorld[0][2], objectToWorld[1][2], objectToWorld[2][2]);

    // The cofactor matrix, which is the inverse transpose times the determinant.
    // The determinant is a uniform scale on the result and the fragment
    // normalizes, so it cancels for every orientation-preserving transform; a
    // mirroring transform flips the sign, which the fragment's front-face flip
    // resolves exactly as it does for the object-space normal.
    output.worldNormal =
        (cross(basisY, basisZ) * input.normal.x) +
        (cross(basisZ, basisX) * input.normal.y) +
        (cross(basisX, basisY) * input.normal.z);
#if HAS_UV
    output.texCoord = input.texCoord;
#endif
#if MAP_NORMAL
    output.tangent = input.tangent;
    output.worldTangent = float4(
        (basisX * input.tangent.x) +
        (basisY * input.tangent.y) +
        (basisZ * input.tangent.z),
        input.tangent.w);
#endif
    return output;
}
#endif

#if FRAGMENT_STAGE
/// Constant UsdPreviewSurface inputs plus the single deterministic light, packed
/// by SilkSurfaceUniformWriter. A storage buffer rather than a constant buffer
/// because that binding path is the one proven on all three backends.
struct SurfaceParameters
{
    float4 diffuseOpacity;
    float4 emissiveOcclusion;
    float4 specularIor;
    float4 metallicRoughnessThresholdWorkflow;
    // (clearcoat, clearcoatRoughness, shaded, lightLinkMask). lightLinkMask is
    // the UsdLux light-link mask of the prim this draw covers: bit i is set when
    // direct light i of the frame table illuminates it. It is packed here rather
    // than in a binding of its own because this block is already bound per draw,
    // and eight bits are exactly representable as a float.
    float4 clearcoatShaded;
    float4 lightDirectionIntensity;
    float4 lightColorAmbient;
    float4 reserved;
    // (textureMask, udimMask, volumeVoxelDepth, shadowLinkMask). shadowLinkMask
    // is the resolved UsdLux shadow-link mask. No raster shadow pass reads it
    // yet; it travels with the light mask so a shadow pass consumes exactly the
    // value the same resolution produced.
    float4 textureControls;
    // The one constant texture-coordinate transform this material samples every
    // texture through, as (m00, m01, tx, 0) and (m10, m11, ty, 0). Identity
    // unless a constant MaterialX place2d or UsdTransform2d chain was folded.
    float4 uvTransformRow0;
    float4 uvTransformRow1;
    // The two-image composite, as (targetTextureMaskBit, operator, factor, udim).
    // targetTextureMaskBit is zero when this material binds no composite image;
    // otherwise it is the same bit textureControls.x uses for the parameter the
    // composite drives, so exactly one of the slots below combines. operator is
    // one of the CompositeOp constants and factor is meaningful only for mix.
    float4 compositeControls;
    // (domeLinkMask, 0, 0, 0). domeLinkMask is the UsdLux dome-link mask of the
    // prim this draw covers: bit i is set when dome light i of the frame dome
    // table illuminates it. It is a slot of its own rather than more bits of
    // clearcoatShaded.w because the dome table and the direct-light table are two
    // orderings -- dome 0 and direct light 0 are different lights.
    float4 domeLinkControls;
};

[[vk::binding(7, 0)]]
StructuredBuffer<SurfaceParameters> surfaceParameters : register(t7);

static const float SurfacePi = 3.14159265358979323846;
static const float SurfaceEpsilon = 0.001;
/// The floor a BRDF denominator is clamped to.
///
/// It guards against a division by exactly zero and nothing else, so it has to be
/// far below any denominator the lobe actually produces. At the minimum roughness
/// the GGX denominator is pi * alpha^4, which is about 3e-24 -- a floor of 1e-9
/// would clamp the peak of every lobe smoother than roughness 0.1 and cost a
/// roughness-0.05 lobe 42% of its energy. Every ratio it guards has a numerator
/// that vanishes at least as fast as its denominator, so it never engages.
static const float SurfaceDenominatorEpsilon = 1.0e-30;
/// The narrowest specular lobe this renderer evaluates. It is the same clamp the
/// CPU BRDF table applies before it integrates, so the direct lobe and the
/// environment lobe are the same function of authored roughness at the mirror end
/// of the axis rather than two that diverge there.
static const float SurfaceMinimumRoughness = 0.001;

[ForceInline]
float4 SampleMaterialTexture(
    Texture2D<float4> textureMap,
    SamplerState textureSampler,
    float2 textureCoordinate,
    bool isUdim)
{
    if (!isUdim)
    {
        return textureMap.Sample(textureSampler, textureCoordinate);
    }

    uint atlasWidth;
    uint atlasHeight;
    textureMap.GetDimensions(atlasWidth, atlasHeight);
    float4 metadata = round(textureMap.Load(int3(0, 0, 0)) * 255.0);
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
float2 TransformTextureCoordinate(SurfaceParameters surface, float2 textureCoordinate)
{
    return float2(
        dot(surface.uvTransformRow0.xy, textureCoordinate) + surface.uvTransformRow0.z,
        dot(surface.uvTransformRow1.xy, textureCoordinate) + surface.uvTransformRow1.z);
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
        surface.compositeControls.w >= 0.5);
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
#endif

/// Storm's Preview.Lighting NormalDistribution, with its additive epsilon removed
/// and its denominator written in the form that survives single precision.
///
/// Storm writes the numerator as `alphaSquared + 0.001`, which is not the GGX
/// distribution: 0.001 is comparable to alphaSquared itself for anything smooth
/// -- at roughness 0.2, alphaSquared is 0.0016, so the epsilon inflates the lobe
/// by more than half -- and it leaves a floor of `0.001 / (pi * d * d)` radiating
/// in every direction at roughness 0, which is a mirror that glows. The epsilon
/// was there to keep a zero-width lobe from dividing by zero; clamping the
/// roughness and guarding the denominator does that without adding energy, and it
/// is the same lobe the CPU BRDF table integrates.
///
/// Storm also writes the denominator as `n2 * (alphaSquared - 1) + 1`, which is
/// algebraically `n2 * alphaSquared + (1 - n2)` and numerically nothing like it.
/// At roughness 0.01, alphaSquared is 1e-8, so `alphaSquared - 1` rounds to
/// exactly -1 in single precision; at n.h = 1 the whole expression then cancels to
/// exactly zero and the lobe divides by its own guard. Grouping the two small
/// quantities together instead never subtracts one from the other, so the value at
/// n.h = 1 is alphaSquared exactly, which is the peak of the lobe rather than an
/// overflow of it. It is the same expression in exact arithmetic, so the CPU table
/// evaluates it in the same form for the same reason.
///
/// Because the two forms are the same expression in exact arithmetic, an
/// optimizer is entitled to turn one into the other: reassociating
/// `n2 * a2 + (1 - n2)` into `1 + n2 * (a2 - 1)` is a legal transformation under
/// fast floating-point rules and would reintroduce the cancellation the grouping
/// exists to avoid. Every value on the chain is therefore `precise`, which is the
/// HLSL and Slang contract that forbids reassociation and contraction along it.
/// Slang emits it as `NoContraction` in SPIR-V and as precise metadata in DXIL,
/// and both are gated: see `SilkCheckedShaderPrecisionTests`.
[ForceInline]
float NormalDistribution(float specularRoughness, float normalDotHalf)
{
    float clampedRoughness = max(specularRoughness, SurfaceMinimumRoughness);
    float alpha = clampedRoughness * clampedRoughness;
    float alphaSquared = alpha * alpha;

    // Each step is named and precise rather than written as one expression, so
    // that the two small quantities are materialized as separate values an
    // optimizer cannot fold back through `a2 - 1`.
    precise float lobeCosineSquared = saturate(normalDotHalf * normalDotHalf);
    precise float scaledLobe = lobeCosineSquared * alphaSquared;
    precise float lobeComplement = 1.0 - lobeCosineSquared;
    precise float denominator = scaledLobe + lobeComplement;
    precise float scaled = SurfacePi * denominator * denominator;
    return alphaSquared / max(scaled, SurfaceDenominatorEpsilon);
}

/// Schlick-GGX with k = alpha/2, matching Storm's Geometric.
[ForceInline]
float Geometric(float specularRoughness, float normalDotLight, float normalDotEye)
{
    float clampedRoughness = max(specularRoughness, SurfaceMinimumRoughness);
    float alpha = clampedRoughness * clampedRoughness;
    float k = alpha * 0.5;
    float geometric = normalDotEye / ((normalDotEye * (1.0 - k)) + k);
    geometric *= normalDotLight / ((normalDotLight * (1.0 - k)) + k);
    return geometric;
}

[ForceInline]
float SchlickFresnel(float eyeDotHalf)
{
    return pow(max(0.0, 1.0 - eyeDotHalf), 5.0);
}

[ForceInline]
float3 EvaluateDirectSpecular(
    float3 specularColorF0,
    float3 specularColorF90,
    float specularRoughness,
    float fresnel,
    float normalDotLight,
    float normalDotEye,
    float normalDotHalf)
{
    float3 f = lerp(specularColorF0, specularColorF90, fresnel);
    float d = NormalDistribution(specularRoughness, normalDotHalf);
    float g = Geometric(specularRoughness, normalDotLight, normalDotEye);
    // Guarded rather than biased. Adding an epsilon to this denominator darkened
    // every grazing and every dim fragment by a term that has nothing to do with
    // the BRDF: at normalDotLight * normalDotEye of 0.01 the divisor was 4% high.
    // g carries normalDotLight and normalDotEye in its own numerators, so the
    // ratio is finite as either goes to zero and only needs a floor, not a bias.
    return (f * g * d) / max(4.0 * normalDotLight * normalDotEye, SurfaceDenominatorEpsilon);
}

[ForceInline]
float3 GetEyeVector(float3 eyePosition)
{
    float lengthSquared = dot(eyePosition, eyePosition);
    return lengthSquared > SurfaceEpsilon
        ? -eyePosition * rsqrt(lengthSquared)
        : float3(0.0, 0.0, 1.0);
}

[ForceInline]
void DiscardClippedFragment(float3 eyePosition, FrameParameters frame)
{
    uint count = min(frame.clipPlaneCount.x, 8u);
    for (uint index = 0; index < count; index++)
    {
        float4 plane = frame.clipPlanes[index];
        if (dot(plane.xyz, eyePosition) + plane.w < 0.0)
        {
            discard;
        }
    }
}

/// Resolves the fraction of one direct light that reaches a shading point,
/// filtered over a fixed nine-tap kernel of the light's shadow map.
///
/// The receiver is offset along its world-space geometric normal before
/// projection and the comparison depth is then biased by a slope-scaled
/// constant. Both are needed and neither is sufficient: a constant depth bias
/// alone has to be large enough for the steepest lit slope, which detaches
/// contact shadows everywhere else, and a normal offset alone cannot cover the
/// depth quantization of the map.
///
/// `worldNormal` must be in world space, because everything else this function
/// touches is: the offset is added to a world position and the result is
/// projected by a world-to-light matrix. Feeding the interpolated object-space
/// normal in makes the offset point somewhere else entirely under any rotation
/// or non-uniform scale, which produces acne or detachment that only appears on
/// transformed prims. `lightDirection` points from the shading point toward the
/// light, exactly as the frame light table publishes it.
///
/// Everything outside the map's own frustum is fully lit rather than shadowed.
/// A light-space projection is derived from the caster bounds, so a receiver
/// outside it has no caster in front of it by construction; treating the outside
/// as occluded would darken exactly the geometry the map deliberately excludes.
[ForceInline]
float ResolveShadowVisibility(
    FrameParameters frame,
    int slot,
    float3 worldPosition,
    float3 worldNormal,
    float3 lightDirection)
{
    float4 controls = frame.shadowControls[slot];
    float4 tile = frame.shadowTile[slot];

    // A normal derived from screen-space derivatives carries the sign of the
    // triangle's screen winding, which is meaningless here. The bias always
    // offsets the receiver toward the light, so the light itself resolves the
    // ambiguity: orienting the normal into the lit hemisphere is both the
    // correct offset direction and the true grazing measure, and it makes this
    // function independent of the camera.
    float3 orientedNormal = dot(worldNormal, lightDirection) < 0.0
        ? -worldNormal
        : worldNormal;
    float normalDotLight = saturate(dot(orientedNormal, lightDirection));
    float slope = clamp(1.0 - normalDotLight, 0.0, 1.0);
    float3 offsetPosition = worldPosition + (orientedNormal * (controls.y * slope));
    float4 lightClip = mul(
        frame.shadowWorldToLightClip[slot],
        float4(offsetPosition, 1.0));
    if (lightClip.w <= 0.0)
    {
        return 1.0;
    }

    float3 ndc = lightClip.xyz / lightClip.w;
    if (abs(ndc.x) > 1.0 || abs(ndc.y) > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 uv = (ndc.xy * float2(0.5, -0.5)) + float2(0.5, 0.5);
    float2 atlasUv = tile.xy + (uv * tile.zw);
    float texel = controls.w;
    float radius = max(controls.z, 0.0);
    float receiverDepth = ndc.z - (controls.x * (1.0 + (2.0 * slope)));
    float2 lowerBound = tile.xy + (texel * 0.5);
    float2 upperBound = tile.xy + tile.zw - (texel * 0.5);
    float lit = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2((float)x, (float)y) * (radius * texel);
            float2 coordinate = clamp(atlasUv + offset, lowerBound, upperBound);
            float occluder = shadowAtlas.SampleLevel(shadowSampler, coordinate, 0.0);
            lit += receiverDepth <= occluder ? 1.0 : 0.0;
        }
    }
    return lit * (1.0 / 9.0);
}

#if VOLUME_DENSITY_TEXTURES
/// Integrates the density column with one sample per voxel layer.
///
/// The step count is the grid's own Z resolution rather than a constant. A fixed
/// count only reconstructs the column exactly when the sample lattice happens to
/// align with the voxel layers, which is why a hardcoded 32 looked correct for as
/// long as the only gated asset was 32 deep. For a 96-deep grid the same 32 steps
/// land off-layer and can integrate a two-layer feature to exactly zero -- the
/// volume still renders, it has simply lost the structure, which is the class of
/// plausible-but-wrong image this program exists to prevent. Sampling at layer
/// centres instead makes the linear filter return each texel exactly, so the sum
/// is the true mean of the column. The command parser rejects depths above the
/// renderer's 512-layer exact-integration limit.
[ForceInline]
float SampleVolumeDensity(float3 objectPosition, float voxelDepth)
{
    float2 uv = saturate(objectPosition.xy + float2(0.5, 0.5));
    float density = 0.0;
    // Clamped defensively even though the command parser guarantees a depth in
    // [1, 512] whenever the sampled-volume flag is set.
    const uint stepCount = (uint)clamp(voxelDepth, 1.0, 512.0);
    for (uint step = 0u; step < stepCount; step++)
    {
        float z = ((float)step + 0.5) / (float)stepCount;
        density += volumeDensityTexture.Sample(volumeDensitySampler, float3(uv, z));
    }
    return density / (float)stepCount;
}
#endif

/// Projects a unit world direction onto the equirectangular coordinates every
/// environment map is stored in.
///
/// This is Hydra's own `ProjectToLatLong` from `hdSt/shaders/domeLight.glslfx`,
/// restated term for term: `u = (atan2(z, x) + pi/2) / 2pi` and
/// `v = acos(y) / pi`, so `u = 0` is `-Z`, `u = 0.25` is `+X`, `u = 0.5` is
/// `+Z`, and `v = 0` is the `+Y` pole. The CPU prefilter that writes the maps
/// restates the same mapping, and both are gated against these external
/// reference points rather than against each other: a bake and a sample that
/// agreed with one another but not with USD would light a scene plausibly with
/// its sun in the wrong place.
[ForceInline]
float2 ProjectEnvironmentDirection(float3 direction)
{
    float3 unit = normalize(direction);
    float u = (atan2(unit.z, unit.x) + (0.5 * SurfacePi)) / (2.0 * SurfacePi);
    // atan2 answers in (-pi, pi], so a direction at or past -X comes back with a
    // negative longitude. The sampler's repeat mode would wrap that correctly for
    // the horizontal axis on its own, but the slice atlas derives its vertical
    // coordinate from this same value and the CPU bake bins by it directly, so it
    // is wrapped here and the two agree exactly.
    u = frac(u);
    float v = acos(clamp(unit.y, -1.0, 1.0)) / SurfacePi;
    return float2(u, v);
}

/// Reads the cosine-convolved irradiance arriving at one world-space normal.
///
/// The stored value is E(n) = integral of L(l) max(0, n.l) dw, so a constant
/// environment of radiance L reads back as exactly pi * L. That is the identity
/// the Lambert normalization below depends on: the diffuse lobe already carries
/// its 1/pi, so multiplying it by this value reproduces the uniform-sky result
/// exactly rather than approximately.
[ForceInline]
float3 SampleEnvironmentIrradiance(float3 worldNormal)
{
    return environmentIrradiance.SampleLevel(
        environmentSampler,
        ProjectEnvironmentDirection(worldNormal),
        0.0).rgb;
}

/// Reads the irradiance one environment group carries.
///
/// A single-group atlas is read through the plain projection, not through the
/// group arithmetic with a group index of zero and a group count of one. The two
/// are equal in exact arithmetic and not in float: the grouped path clamps the
/// latitude into a half-texel inset and divides, and a scene with no dome linking
/// must produce the bytes it produced before dome linking existed rather than
/// bytes that merely round to the same place.
[ForceInline]
float3 SampleEnvironmentIrradianceGroup(
    float3 worldNormal,
    float group,
    float groupCount,
    float groupHeight)
{
    if (groupCount <= 1.0)
    {
        return SampleEnvironmentIrradiance(worldNormal);
    }

    float2 uv = ProjectEnvironmentDirection(worldNormal);
    float inset = 0.5 / max(groupHeight, 1.0);
    float v = clamp(uv.y, inset, 1.0 - inset);
    return environmentIrradiance.SampleLevel(
        environmentSampler,
        float2(uv.x, (group + v) / groupCount),
        0.0).rgb;
}

/// Reads the prefiltered radiance reflected in one world direction at one
/// roughness.
///
/// The atlas stacks `sliceCount` roughness slices vertically, all at the same
/// angular resolution, so the roughest slice is a genuine hemisphere-wide
/// integration rather than the single texel a collapsing mip chain would leave.
/// Slice `i` was prefiltered for `roughness = i / (sliceCount - 1)`; the two
/// bracketing slices are read and blended here rather than by a mip filter,
/// because the roughness axis is not a resolution axis and nothing in the
/// hardware knows the difference.
///
/// The latitude coordinate is inset by half a texel before it is mapped into the
/// atlas, which is what stops a slice's bilinear taps from reaching into its
/// neighbour and blending two different roughnesses together at the poles.
[ForceInline]
float3 SampleEnvironmentSpecular(
    float3 reflectionDirection,
    float roughness,
    float sliceCount,
    float sliceHeight)
{
    float2 uv = ProjectEnvironmentDirection(reflectionDirection);
    float slices = max(sliceCount, 1.0);
    float slice = saturate(roughness) * max(slices - 1.0, 0.0);
    float lower = floor(slice);
    float upper = min(lower + 1.0, slices - 1.0);
    float blend = slice - lower;
    float inset = 0.5 / max(sliceHeight, 1.0);
    float v = clamp(uv.y, inset, 1.0 - inset);
    float3 nearer = environmentSpecular.SampleLevel(
        environmentSampler,
        float2(uv.x, (lower + v) / slices),
        0.0).rgb;
    float3 farther = environmentSpecular.SampleLevel(
        environmentSampler,
        float2(uv.x, (upper + v) / slices),
        0.0).rgb;
    return lerp(nearer, farther, blend);
}

/// Reads the prefiltered radiance one environment group carries.
///
/// The atlas stacks `groupCount` groups of `sliceCount` roughness slices, so
/// group `g` owns slices `[g * sliceCount, (g + 1) * sliceCount)`. A single-group
/// atlas takes the ungrouped path above for the same reason the irradiance one
/// does: the two are equal in exact arithmetic and not in float, and an unlinked
/// scene must render the bytes it always rendered.
[ForceInline]
float3 SampleEnvironmentSpecularGroup(
    float3 reflectionDirection,
    float roughness,
    float sliceCount,
    float sliceHeight,
    float group,
    float groupCount)
{
    if (groupCount <= 1.0)
    {
        return SampleEnvironmentSpecular(
            reflectionDirection,
            roughness,
            sliceCount,
            sliceHeight);
    }

    float2 uv = ProjectEnvironmentDirection(reflectionDirection);
    float slices = max(sliceCount, 1.0);
    float total = slices * groupCount;
    float slice = saturate(roughness) * max(slices - 1.0, 0.0);
    float lower = floor(slice);
    float upper = min(lower + 1.0, slices - 1.0);
    float blend = slice - lower;
    float inset = 0.5 / max(sliceHeight, 1.0);
    float v = clamp(uv.y, inset, 1.0 - inset);
    float base = group * slices;
    float3 nearer = environmentSpecular.SampleLevel(
        environmentSampler,
        float2(uv.x, (base + lower + v) / total),
        0.0).rgb;
    float3 farther = environmentSpecular.SampleLevel(
        environmentSampler,
        float2(uv.x, (base + upper + v) / total),
        0.0).rgb;
    return lerp(nearer, farther, blend);
}

/// Reads the split-sum environment BRDF as a scale and bias on F0 and F90.
///
/// The table is numerically integrated on the CPU from the same GGX normal
/// distribution and Smith geometry term `EvaluateDirectSpecular` uses, so the
/// environment lobe and the direct lobe are the same BRDF rather than two that
/// happen to look similar. It replaced an analytic curve fit, which was accurate
/// to a few percent in the middle of the domain and visibly wrong at grazing
/// incidence and at the roughness extremes.
[ForceInline]
float2 SampleEnvironmentBrdf(float roughness, float normalDotEye)
{
    return environmentBrdf.SampleLevel(
        environmentBrdfSampler,
        float2(saturate(normalDotEye), saturate(roughness)),
        0.0).rg;
}
#endif

[shader("fragment")]
float4 FRAGMENT_ENTRY_POINT(
    VertexOutput input,
    bool isFrontFace : SV_IsFrontFace) : SV_Target0
{
#if FRAGMENT_STAGE
    SurfaceParameters surface = surfaceParameters[0];
    FrameParameters frame = frameParameters[0];
    DiscardClippedFragment(input.eyePosition, frame);

    // clearcoatShaded.z carries three states, not a flag. 0 is the shared default
    // block a mesh with no supported material binds, where Storm shows the
    // display colour, so the scene tint drives diffuse. 1 is an ordinary shaded
    // material. 2 is an *unlit* surface: the authored terminal is MaterialX's
    // ND_surface_unlit, which hdSilk publishes as a runtime-generated fragment.
    // While that fragment is compiling or has failed, this permutation stands in
    // for it, and standing in with a lit shading model would light a surface the
    // author explicitly declared unlit -- including with the environment, which
    // an unlit surface must never receive.
    float shadedMode = surface.clearcoatShaded.z;
    bool shaded = shadedMode >= 0.5;
    bool unlit = shadedMode >= 1.5;
    float3 diffuseColor = shaded ? surface.diffuseOpacity.rgb : input.tint.rgb;
    float opacity = shaded ? surface.diffuseOpacity.a : input.tint.a;
    float3 emissiveColor = surface.emissiveOcclusion.rgb;
    float occlusion = surface.emissiveOcclusion.a;
    if (unlit)
    {
        // No lighting of any kind, and no ambient: the placeholder states "this
        // surface is unlit and its generated colour has not arrived" rather than
        // inventing a shaded appearance the finished fragment will not have. The
        // display transform still applies, because it is a property of the
        // target rather than of the shading model.
        float3 unlitColor = diffuseColor + emissiveColor;
        unlitColor *= exp2(asfloat(frame.clipPlaneCount.z));
        if (frame.clipPlaneCount.y == 1u)
        {
            unlitColor /= 1.0 + max(unlitColor, float3(0.0, 0.0, 0.0));
        }
        return float4(unlitColor, opacity);
    }
    if (surface.reserved.x >= 0.5)
    {
#if VOLUME_DENSITY_TEXTURES
        float density = surface.reserved.w >= 0.5
            ? SampleVolumeDensity(input.objectPosition, surface.textureControls.z) *
                max(0.0, surface.reserved.y)
            : max(0.0, surface.reserved.y);
#else
        float density = max(0.0, surface.reserved.y);
#endif
        float distance = max(0.0, surface.reserved.z);
        float opacityIntegral = 1.0 - exp(-density * distance);
        return float4(diffuseColor * opacityIntegral, 1.0);
    }
    float metallic = saturate(surface.metallicRoughnessThresholdWorkflow.x);
    float roughness = clamp(surface.metallicRoughnessThresholdWorkflow.y, 0.01, 1.0);
    float3 specularColor = surface.specularIor.rgb;
    float ior = surface.specularIor.a;
    float clearcoatAmount = surface.clearcoatShaded.x;
    float clearcoatRoughness = surface.clearcoatShaded.y;
    float3 shadingNormal = input.normal;
    // The world-space shading basis, carried from the vertex stage through the
    // cofactor of the object-to-world 3x3. It is what the environment maps are
    // sampled with, and it is maintained in parallel with the object-space
    // `shadingNormal` above rather than replacing it: the direct-light loop
    // shades on the object-space one, and unifying the two would move every
    // gated Storm parity image and cannot be re-measured here.
    float3 worldShadingNormal = input.worldNormal;
    uint textureMask = (uint)round(surface.textureControls.x);
    uint udimMask = (uint)round(surface.textureControls.y);
#if HAS_UV
    // Applied once, before any sample: the material publishes exactly one folded
    // MaterialX place2d transform, and every texture it binds reads through it.
    float2 materialTexCoord = TransformTextureCoordinate(surface, input.texCoord);
#endif

#if MAP_MATERIAL
    if ((textureMask & 2u) != 0)
    {
        diffuseColor = ApplyMaterialComposite(
            surface,
            2.0,
            SampleMaterialTexture(
                baseColorTexture,
                baseColorSampler,
                materialTexCoord,
                (udimMask & 2u) != 0),
            materialTexCoord).rgb;
    }
#endif
#if MAP_NORMAL
    float3 sampledNormal =
        SampleMaterialTexture(
            normalTexture,
            normalSampler,
            materialTexCoord,
            (udimMask & 4u) != 0).xyz * 2.0 - 1.0;
    float3 tangent = normalize(input.tangent.xyz);
    float3 bitangent = cross(normalize(shadingNormal), tangent) * input.tangent.w;
    shadingNormal = normalize(
        (tangent * sampledNormal.x) +
        (bitangent * sampledNormal.y) +
        (shadingNormal * sampledNormal.z));

    // The same tangent-space normal, resolved through the *world* basis. A
    // normal map has to be applied in the space it will be consumed in: applying
    // it in object space and then treating the result as world space rotates the
    // perturbation by the prim's own transform, which is invisible on an
    // axis-aligned prim and wrong on every other. The tangent is
    // re-orthogonalized against the world normal because interpolation and a
    // non-uniform transform both break the authored orthogonality.
    float3 worldNormalBasis = normalize(worldShadingNormal);
    float3 worldTangentBasis = normalize(input.worldTangent.xyz);
    worldTangentBasis = normalize(
        worldTangentBasis - (worldNormalBasis * dot(worldNormalBasis, worldTangentBasis)));
    float3 worldBitangentBasis =
        cross(worldNormalBasis, worldTangentBasis) * input.worldTangent.w;
    worldShadingNormal = normalize(
        (worldTangentBasis * sampledNormal.x) +
        (worldBitangentBasis * sampledNormal.y) +
        (worldNormalBasis * sampledNormal.z));
#endif
#if MAP_MATERIAL
    // The managed texture cache replicates the connected UsdUVTexture output
    // channel into every component before upload, so a scalar map is always read
    // from .r whichever of r/g/b/a the material authored. The sampled value
    // replaces the authored constant rather than modulating it: a connected input
    // has no constant to modulate, and multiplying by the UsdPreviewSurface
    // default silently halved roughness and zeroed metallic.
    if ((textureMask & 8u) != 0)
    {
        roughness = clamp(
            ApplyMaterialComposite(
                surface,
                8.0,
                SampleMaterialTexture(
                    roughnessMetallicTexture,
                    roughnessMetallicSampler,
                    materialTexCoord,
            (udimMask & 8u) != 0),
                materialTexCoord).r,
            0.01,
            1.0);
    }
    // Metallic has its own texture, sampler and UDIM bit, so a roughness-only or
    // metallic-only material never disturbs the other input. A packed
    // occlusion/roughness/metallic file reaches here as two connections naming one
    // asset and two channels, which resolves to two swizzled uploads.
    if ((textureMask & 32u) != 0)
    {
        metallic = saturate(
            ApplyMaterialComposite(
                surface,
                32.0,
                SampleMaterialTexture(
                    metallicTexture,
                    metallicSampler,
                    materialTexCoord,
                    (udimMask & 32u) != 0),
                materialTexCoord).r);
    }
    if ((textureMask & 16u) != 0)
    {
        emissiveColor = ApplyMaterialComposite(
            surface,
            16.0,
            SampleMaterialTexture(
                emissiveTexture,
                emissiveSampler,
                materialTexCoord,
                (udimMask & 16u) != 0),
            materialTexCoord).rgb;
    }
    if ((textureMask & 64u) != 0)
    {
        opacity = saturate(
            ApplyMaterialComposite(
                surface,
                64.0,
                SampleMaterialTexture(
                    opacityTexture,
                    opacitySampler,
                    materialTexCoord,
                    (udimMask & 64u) != 0),
                materialTexCoord).r);
    }
    if ((textureMask & 128u) != 0)
    {
        occlusion = saturate(
            ApplyMaterialComposite(
                surface,
                128.0,
                SampleMaterialTexture(
                    occlusionTexture,
                    occlusionSampler,
                    materialTexCoord,
                    (udimMask & 128u) != 0),
                materialTexCoord).r);
    }
    if ((textureMask & 256u) != 0)
    {
        specularColor = saturate(
            ApplyMaterialComposite(
                surface,
                256.0,
                SampleMaterialTexture(
                    specularColorTexture,
                    specularColorSampler,
                    materialTexCoord,
                    (udimMask & 256u) != 0),
                materialTexCoord).rgb);
    }
    if ((textureMask & 512u) != 0)
    {
        clearcoatAmount = saturate(
            ApplyMaterialComposite(
                surface,
                512.0,
                SampleMaterialTexture(
                    clearcoatTexture,
                    clearcoatSampler,
                    materialTexCoord,
                    (udimMask & 512u) != 0),
                materialTexCoord).r);
    }
    if ((textureMask & 1024u) != 0)
    {
        clearcoatRoughness = saturate(
            ApplyMaterialComposite(
                surface,
                1024.0,
                SampleMaterialTexture(
                    clearcoatRoughnessTexture,
                    clearcoatRoughnessSampler,
                    materialTexCoord,
                    (udimMask & 1024u) != 0),
                materialTexCoord).r);
    }
    if ((textureMask & 2048u) != 0)
    {
        ior = ApplyMaterialComposite(
                surface,
                2048.0,
                SampleMaterialTexture(
                    iorTexture,
                    iorSampler,
                    materialTexCoord,
                    (udimMask & 2048u) != 0),
                materialTexCoord).r;
    }
#endif

    float opacityThreshold = surface.metallicRoughnessThresholdWorkflow.z;
    if (opacityThreshold > 0.0 && opacity < opacityThreshold)
    {
        discard;
    }

    float3 normal = normalize(shadingNormal);
    float3 worldNormal = normalize(worldShadingNormal);
    float3 eye = GetEyeVector(input.eyePosition);
    // Storm flips the shading normal for rasterized back faces. Basing this on
    // coverage-facing state rather than the eye vector keeps orthographic parity
    // scenes with identity projection from turning black when Peye.z is positive.
    normal = isFrontFace ? normal : -normal;
    // The same flip on the world basis, for the same reason and by the same
    // rule. It also resolves the sign a mirroring object-to-world transform puts
    // into the cofactor normal, because the rasterizer's facing state is
    // computed from the transformed winding.
    worldNormal = isFrontFace ? worldNormal : -worldNormal;
    float normalDotEye = saturate(abs(dot(normal, eye)) + 1.0e-5);

    // Storm clamps roughness rather than clamping into a minimum band.
    float specularRoughness = max(0.001, roughness);
    clearcoatRoughness = max(0.001, clearcoatRoughness);
    float reflectanceRatio =
        (1.0 - ior) / (1.0 + ior);

    // Diffuse carries the 1/pi Lambert normalization, exactly as Storm's
    // evaluateDirectDiffuse does; the light supplies the matching pi of irradiance.
    float3 diffuse = diffuseColor / SurfacePi;
    float3 normalIncidence;
    float3 grazingIncidence;
    if (surface.metallicRoughnessThresholdWorkflow.w >= 0.5)
    {
        normalIncidence = specularColor;
        grazingIncidence = float3(1.0, 1.0, 1.0);
    }
    else
    {
        float3 specularTint = lerp(float3(1.0, 1.0, 1.0), diffuseColor, metallic);
        normalIncidence = lerp(
            reflectanceRatio * reflectanceRatio * specularTint,
            specularTint,
            metallic);
        grazingIncidence = specularTint;
        // Pure metals have no diffuse response.
        diffuse *= 1.0 - metallic;
    }

    // A dome light is scene lighting, whether or not this renderer resolved it
    // into a prefiltered environment. Without it here, a stage whose only light
    // is a dome would take the deterministic-headlight fallback and be lit from
    // the camera by a light no author placed, on top of whatever the dome
    // contributes -- so the environment could never be measured on its own and a
    // dome-only scene would never look like one.
    //
    // environmentControls.w is the authored fact, and .x is this renderer's
    // resolution of it. They are deliberately separate: a dome authored black,
    // or authored specular-only, or refused by the prefilter and fallen back to a
    // zero ambient, contributes nothing measurable and would leave both the
    // ambient term and .x at zero -- and a headlight keyed on either would then
    // switch itself on for a stage that is lit exactly as its author asked.
    bool hasSceneLighting =
        frame.ambientLight.w > 0.5 ||
        dot(frame.ambientLight.rgb, frame.ambientLight.rgb) > 0.0 ||
        frame.environmentControls.x >= 0.5 ||
        frame.environmentControls.w >= 0.5;
    uint lightCount = min((uint)frame.ambientLight.w, 8u);
    float3 sceneEye = normalize(mul(frame.eyeToWorld, float4(eye, 0.0)).xyz);
    float3 worldPosition = mul(frame.eyeToWorld, float4(input.eyePosition, 1.0)).xyz;

    // The world-space geometric normal of the shaded surface, used only by the
    // shadow bias. It is derived from the screen-space derivatives of the world
    // position rather than from the interpolated vertex normal on purpose: the
    // interpolated normal reaches this stage in object space, and a shadow offset
    // is added to a world position, so under any rotation or non-uniform scale an
    // object-space normal offsets the receiver in the wrong direction entirely.
    // Differentiating the world position applies the object-to-world transform's
    // inverse transpose implicitly and exactly, without widening the pinned
    // 80-byte instance block to carry a normal matrix. It is computed here, in
    // uniform control flow, because derivatives inside the per-light branch below
    // would be undefined. Its sign follows screen winding and is resolved against
    // each light inside ResolveShadowVisibility, not here.
    float3 worldGeometricNormal = cross(ddx(worldPosition), ddy(worldPosition));
    float worldNormalLengthSquared = dot(worldGeometricNormal, worldGeometricNormal);
    worldGeometricNormal = worldNormalLengthSquared > 1.0e-20
        ? worldGeometricNormal * rsqrt(worldNormalLengthSquared)
        : float3(0.0, 0.0, 1.0);
    float3 color = diffuseColor * surface.lightColorAmbient.a;

    // UsdLux dome linking. The dome table is its own bounded ordering, so a dome
    // whose bit is clear contributes neither its ambient term nor its prefiltered
    // response to this surface while still lighting the rest of the scene.
    //
    // A prim linked to every published dome reads the aggregate the producer
    // accumulated rather than re-summing the per-dome terms. The two are equal by
    // construction -- the aggregate is the sum of exactly these summands in
    // exactly this order -- and taking the aggregate is what makes a scene that
    // authors no dome collection produce the bytes it produced before dome
    // linking existed rather than bytes that are only arithmetically equal.
    uint domeLinkMask = (uint)max(surface.domeLinkControls.x, 0.0);
    uint domeCount = min((uint)max(frame.domeControls.x, 0.0), 8u);
    uint allDomes = (1u << domeCount) - 1u;
    bool allDomesLinked = (domeLinkMask & allDomes) == allDomes;
    float3 domeAmbient = frame.ambientLight.rgb;
    if (!allDomesLinked)
    {
        domeAmbient = float3(0.0, 0.0, 0.0);
        for (uint domeIndex = 0u; domeIndex < domeCount; domeIndex++)
        {
            if ((domeLinkMask & (1u << domeIndex)) != 0u)
            {
                domeAmbient += frame.domeAmbient[domeIndex].rgb;
            }
        }
    }
    color += diffuseColor * domeAmbient;
    if (!hasSceneLighting)
    {
        lightCount = 1u;
    }
    // UsdLux light linking. The mask is the prim's own, so a light whose bit is
    // clear contributes nothing to this surface while still lighting the rest of
    // the scene. A prim with no authored linking carries every bit, so the
    // branch below is taken for every light exactly as it was before linking.
    uint lightLinkMask = (uint)max(surface.clearcoatShaded.w, 0.0);
    for (uint lightIndex = 0u; lightIndex < lightCount; lightIndex++)
    {
        if (hasSceneLighting && (lightLinkMask & (1u << lightIndex)) == 0u)
        {
            continue;
        }
        float lightType = lightIndex == 0u && !hasSceneLighting
            ? 1.0
            : frame.lightPositionType[lightIndex].w;
        float3 lightDirection = lightIndex == 0u && !hasSceneLighting
            ? normalize(surface.lightDirectionIntensity.xyz)
            : normalize(frame.lightDirectionRadius[lightIndex].xyz);
        float lightIntensity = lightIndex == 0u && !hasSceneLighting
            ? surface.lightDirectionIntensity.w
            : frame.lightColorIntensity[lightIndex].w;
        float3 lightColor = lightIndex == 0u && !hasSceneLighting
            ? surface.lightColorAmbient.rgb
            : frame.lightColorIntensity[lightIndex].rgb;
        float diffuseScale = lightIndex == 0u && !hasSceneLighting
            ? 1.0
            : frame.lightControls[lightIndex].x;
        float specularScale = lightIndex == 0u && !hasSceneLighting
            ? 1.0
            : frame.lightControls[lightIndex].y;
        float3 lightTangent = lightIndex == 0u && !hasSceneLighting
            ? float3(1.0, 0.0, 0.0)
            : normalize(frame.lightTangentShapeX[lightIndex].xyz);
        float3 lightBitangent = lightIndex == 0u && !hasSceneLighting
            ? float3(0.0, 1.0, 0.0)
            : normalize(frame.lightBitangentShapeY[lightIndex].xyz);
        float shapeX = lightIndex == 0u && !hasSceneLighting
            ? 0.0
            : frame.lightTangentShapeX[lightIndex].w;
        float shapeY = lightIndex == 0u && !hasSceneLighting
            ? 0.0
            : frame.lightBitangentShapeY[lightIndex].w;
        float lightRadius = lightIndex == 0u && !hasSceneLighting
            ? 0.0
            : frame.lightDirectionRadius[lightIndex].w;
        float3 lightEyeBase = lightIndex == 0u && !hasSceneLighting
            ? eye
            : sceneEye;
        // Raster shadows. A light with no shadow map names slot -1 and is fully
        // visible, which is exactly the pre-shadow result. Resolved once per
        // light rather than per area-light sample because only a light whose
        // light-space projection is exact gets a map at all.
        float shadowVisibility = 1.0;
        if (hasSceneLighting)
        {
            int shadowSlot = (int)frame.shadowSlots[lightIndex].x;
            if (shadowSlot >= 0)
            {
                shadowVisibility = ResolveShadowVisibility(
                    frame,
                    shadowSlot,
                    worldPosition,
                    worldGeometricNormal,
                    lightDirection);
            }
        }
        float3 sampleOffsets[5];
        sampleOffsets[0] = float3(0.0, 0.0, 0.0);
        sampleOffsets[1] = float3(0.0, 0.0, 0.0);
        sampleOffsets[2] = float3(0.0, 0.0, 0.0);
        sampleOffsets[3] = float3(0.0, 0.0, 0.0);
        sampleOffsets[4] = float3(0.0, 0.0, 0.0);
        float sampleCount = 1.0;
        if (lightType == 3.0)
        {
            float3 halfWidth = lightTangent * (shapeX * 0.5);
            float3 halfHeight = lightBitangent * (shapeY * 0.5);
            sampleOffsets[1] = halfWidth + halfHeight;
            sampleOffsets[2] = halfWidth - halfHeight;
            sampleOffsets[3] = -halfWidth + halfHeight;
            sampleOffsets[4] = -halfWidth - halfHeight;
            sampleCount = 5.0;
        }
        else if (lightType == 4.0)
        {
            sampleOffsets[1] = lightTangent * lightRadius;
            sampleOffsets[2] = -lightTangent * lightRadius;
            sampleOffsets[3] = lightBitangent * lightRadius;
            sampleOffsets[4] = -lightBitangent * lightRadius;
            sampleCount = 5.0;
        }
        else if (lightType == 5.0)
        {
            float3 halfLength = lightDirection * (shapeX * 0.5);
            sampleOffsets[1] = halfLength;
            sampleOffsets[2] = -halfLength;
            sampleCount = 3.0;
        }
        for (uint sampleIndex = 0u; sampleIndex < 5u; sampleIndex++)
        {
            if ((float)sampleIndex >= sampleCount)
            {
                continue;
            }
            float sampleIntensity = lightIntensity / sampleCount;
            float emissionScale = 1.0;
            float3 sampleDirection = lightDirection;
            if (lightType >= 2.0)
            {
                float3 samplePosition = frame.lightPositionType[lightIndex].xyz + sampleOffsets[sampleIndex];
                float3 toLight = samplePosition - worldPosition;
                float distanceSquared = max(dot(toLight, toLight), SurfaceEpsilon);
                sampleDirection = toLight * rsqrt(distanceSquared);
                sampleIntensity /= distanceSquared;
                if (lightType == 4.0)
                {
                    emissionScale = saturate(dot(lightDirection, -sampleDirection));
                }
            }
            float3 half = normalize(sampleDirection + lightEyeBase);
            float normalDotLight = saturate(dot(normal, sampleDirection));
            float normalDotHalf = saturate(dot(normal, half));
            float eyeDotHalf = saturate(dot(lightEyeBase, half));
            float fresnel = SchlickFresnel(eyeDotHalf);
            float3 directDiffuse =
                diffuse * (1.0 - lerp(normalIncidence, grazingIncidence, fresnel));
            float3 directSpecular = EvaluateDirectSpecular(
                normalIncidence,
                grazingIncidence,
                specularRoughness,
                fresnel,
                normalDotLight,
                normalDotEye,
                normalDotHalf);
            if (clearcoatAmount > 0.0)
            {
                directSpecular += clearcoatAmount * EvaluateDirectSpecular(
                    reflectanceRatio * reflectanceRatio * float3(1.0, 1.0, 1.0),
                    float3(1.0, 1.0, 1.0),
                    clearcoatRoughness,
                    fresnel,
                    normalDotLight,
                    normalDotEye,
                    normalDotHalf);
            }
            float3 diffuseIrradiance = lightColor * sampleIntensity * SurfacePi;
            float3 specularIrradiance = lightColor * sampleIntensity;
            color += shadowVisibility * occlusion * emissionScale * normalDotLight *
                ((directDiffuse * diffuseScale * diffuseIrradiance) +
                (directSpecular * specularScale * specularIrradiance));
        }
    }
    // Prefiltered image-based environment lighting. A frame with no live
    // environment writes zero here and pays for nothing: neither map is sampled,
    // and the mean-radiance ambient fallback a diagnosed dome falls back to has
    // already been folded into the dome ambient term above. The two are mutually
    // exclusive per dome by construction, so a dome is never counted both as an
    // image and as an untextured approximation of itself.
    //
    // Deliberately outside the direct-light loop, and deliberately not multiplied
    // by any direct light mask or shadow visibility. The dome mask is the mask
    // that applies here, and it is applied: a dome's collection:lightLink selects
    // which environment groups this surface reads. No shadow map is ever rendered
    // for a dome -- the raster shadow slice covers distant lights only -- so a
    // dome's collection:shadowLink is diagnosed by the producer against the dome
    // rather than applied here, where it would turn a caster restriction into a
    // receiver restriction. The material's own occlusion input is the one
    // attenuation that is semantically valid, and it is applied.
    if (frame.environmentControls.x >= 0.5)
    {
        // Sampled with the *world* basis carried from the vertex stage, which is
        // what makes a rotated or non-uniformly scaled prim reflect the sky its
        // surface actually faces. The direct-light loop above still shades on the
        // object-space normal; that inconsistency is pre-existing and is stated
        // in docs/rendering.md rather than silently inherited here.
        float3 environmentNormal = worldNormal;
        float environmentNormalDotEye =
            saturate(abs(dot(environmentNormal, sceneEye)) + 1.0e-5);

        // One table read serves both halves. The specular half is the split-sum
        // scale and bias on F0/F90; the diffuse half is its energy complement, so
        // the two lobes divide the environment between them instead of each
        // taking all of it.
        float2 environmentBrdfTerm = SampleEnvironmentBrdf(
            specularRoughness,
            environmentNormalDotEye);
        float3 specularWeight =
            (normalIncidence * environmentBrdfTerm.x) +
            (grazingIncidence * environmentBrdfTerm.y);
        float3 diffuseWeight = saturate(float3(1.0, 1.0, 1.0) - specularWeight);
        float3 reflectionDirection = reflect(-sceneEye, environmentNormal);

        float environmentGroupCount = max(frame.domeControls.y, 1.0);
        float3 irradiance = float3(0.0, 0.0, 0.0);
        float3 prefiltered = float3(0.0, 0.0, 0.0);
        float3 clearcoatPrefiltered = float3(0.0, 0.0, 0.0);
        if (allDomesLinked || environmentGroupCount <= 1.0)
        {
            // The composed bake of every dome this renderer prefiltered. A
            // single-group atlas takes this branch whatever the mask says,
            // because there is then no per-dome group to select and reading the
            // composed sky is the same verdict the producer already reported as
            // "no dome is individually addressable".
            float composedGroup = frame.domeControls.z;
            irradiance = SampleEnvironmentIrradianceGroup(
                environmentNormal,
                composedGroup,
                environmentGroupCount,
                frame.domeControls.w);
            prefiltered = SampleEnvironmentSpecularGroup(
                reflectionDirection,
                specularRoughness,
                frame.environmentControls.y,
                frame.environmentControls.z,
                composedGroup,
                environmentGroupCount);
            if (clearcoatAmount > 0.0)
            {
                clearcoatPrefiltered = SampleEnvironmentSpecularGroup(
                    reflectionDirection,
                    clearcoatRoughness,
                    frame.environmentControls.y,
                    frame.environmentControls.z,
                    composedGroup,
                    environmentGroupCount);
            }
        }
        else
        {
            for (uint domeIndex = 0u; domeIndex < domeCount; domeIndex++)
            {
                if ((domeLinkMask & (1u << domeIndex)) == 0u)
                {
                    continue;
                }
                float domeGroup = frame.domeEnvironment[domeIndex].x;
                if (domeGroup < 0.0)
                {
                    // The dome has no prefiltered response and contributes
                    // through its ambient term alone, which the dome ambient sum
                    // above already applied.
                    continue;
                }
                irradiance += SampleEnvironmentIrradianceGroup(
                    environmentNormal,
                    domeGroup,
                    environmentGroupCount,
                    frame.domeControls.w);
                prefiltered += SampleEnvironmentSpecularGroup(
                    reflectionDirection,
                    specularRoughness,
                    frame.environmentControls.y,
                    frame.environmentControls.z,
                    domeGroup,
                    environmentGroupCount);
                if (clearcoatAmount > 0.0)
                {
                    clearcoatPrefiltered += SampleEnvironmentSpecularGroup(
                        reflectionDirection,
                        clearcoatRoughness,
                        frame.environmentControls.y,
                        frame.environmentControls.z,
                        domeGroup,
                        environmentGroupCount);
                }
            }
        }

        // diffuse already carries the 1/pi Lambert normalization and the metallic
        // suppression, so a constant sky of radiance L contributes exactly
        // diffuseColor * (1 - metallic) * L * (1 - F) here, which is the same
        // Fresnel complement the direct diffuse lobe applies.
        color += occlusion * diffuse * irradiance * diffuseWeight;
        color += occlusion * prefiltered * specularWeight;
        if (clearcoatAmount > 0.0)
        {
            float3 clearcoatF0 =
                reflectanceRatio * reflectanceRatio * float3(1.0, 1.0, 1.0);
            float2 clearcoatBrdf = SampleEnvironmentBrdf(
                clearcoatRoughness,
                environmentNormalDotEye);
            color += occlusion * clearcoatAmount * clearcoatPrefiltered *
                ((clearcoatF0 * clearcoatBrdf.x) + clearcoatBrdf.y);
        }
    }
    color += emissiveColor;
    color *= exp2(asfloat(frame.clipPlaneCount.z));
    if (frame.clipPlaneCount.y == 1u)
    {
        color /= 1.0 + max(color, float3(0.0, 0.0, 0.0));
    }
    return float4(color, opacity);
#else
    return float4(abs(input.normal), 1.0) * input.tint;
#endif
}
