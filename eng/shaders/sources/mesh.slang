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
#if HAS_UV
    float2 texCoord : TEXCOORD0;
#endif
#if MAP_NORMAL
    float4 tangent : TANGENT;
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

#if FRAGMENT_STAGE && VOLUME_DENSITY_TEXTURES
[[vk::binding(9, 0)]]
Texture3D<float> volumeDensityTexture : register(t9);
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
#if HAS_UV
    output.texCoord = input.texCoord;
#endif
#if MAP_NORMAL
    output.tangent = input.tangent;
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
    float4 clearcoatShaded;
    float4 lightDirectionIntensity;
    float4 lightColorAmbient;
    float4 reserved;
    float4 textureControls;
};

[[vk::binding(7, 0)]]
StructuredBuffer<SurfaceParameters> surfaceParameters : register(t7);

static const float SurfacePi = 3.14159265358979323846;
static const float SurfaceEpsilon = 0.001;

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

/// Mirrors Storm's Preview.Lighting NormalDistribution, including its epsilon, so
/// the two renderers agree by construction rather than by approximation.
[ForceInline]
float NormalDistribution(float specularRoughness, float normalDotHalf)
{
    float alpha = specularRoughness * specularRoughness;
    float alphaSquared = alpha * alpha;
    float denominator = (normalDotHalf * normalDotHalf * (alphaSquared - 1.0)) + 1.0;
    denominator *= denominator;
    denominator *= SurfacePi;
    return (alphaSquared + SurfaceEpsilon) / denominator;
}

/// Schlick-GGX with k = alpha/2, matching Storm's Geometric.
[ForceInline]
float Geometric(float specularRoughness, float normalDotLight, float normalDotEye)
{
    float alpha = specularRoughness * specularRoughness;
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
    return (f * g * d) / ((4.0 * normalDotLight * normalDotEye) + SurfaceEpsilon);
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

#if VOLUME_DENSITY_TEXTURES
[ForceInline]
float SampleVolumeDensity(float3 objectPosition)
{
    float2 uv = saturate(objectPosition.xy + float2(0.5, 0.5));
    float density = 0.0;
    const uint stepCount = 32u;
    for (uint step = 0u; step < stepCount; step++)
    {
        float z = ((float)step + 0.5) / (float)stepCount;
        density += volumeDensityTexture.Sample(volumeDensitySampler, float3(uv, z));
    }
    return density / (float)stepCount;
}
#endif
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

    // shaded is zero for the shared default block, which a mesh with no supported
    // material bound uses. Storm shows the display colour for such a prim, so the
    // scene tint drives diffuse rather than the UsdPreviewSurface default.
    bool shaded = surface.clearcoatShaded.z >= 0.5;
    float3 diffuseColor = shaded ? surface.diffuseOpacity.rgb : input.tint.rgb;
    float opacity = shaded ? surface.diffuseOpacity.a : input.tint.a;
    float3 emissiveColor = surface.emissiveOcclusion.rgb;
    float occlusion = surface.emissiveOcclusion.a;
    if (surface.reserved.x >= 0.5)
    {
#if VOLUME_DENSITY_TEXTURES
        float density = surface.reserved.w >= 0.5
            ? SampleVolumeDensity(input.objectPosition) * max(0.0, surface.reserved.y)
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
    uint textureMask = (uint)round(surface.textureControls.x);
    uint udimMask = (uint)round(surface.textureControls.y);

#if MAP_MATERIAL
    if ((textureMask & 2u) != 0)
    {
        diffuseColor = SampleMaterialTexture(
            baseColorTexture,
            baseColorSampler,
            input.texCoord,
            (udimMask & 2u) != 0).rgb;
    }
#endif
#if MAP_NORMAL
    float3 sampledNormal =
        SampleMaterialTexture(
            normalTexture,
            normalSampler,
            input.texCoord,
            (udimMask & 4u) != 0).xyz * 2.0 - 1.0;
    float3 tangent = normalize(input.tangent.xyz);
    float3 bitangent = cross(normalize(shadingNormal), tangent) * input.tangent.w;
    shadingNormal = normalize(
        (tangent * sampledNormal.x) +
        (bitangent * sampledNormal.y) +
        (shadingNormal * sampledNormal.z));
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
            SampleMaterialTexture(
                roughnessMetallicTexture,
                roughnessMetallicSampler,
                input.texCoord,
                (udimMask & 8u) != 0).r,
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
            SampleMaterialTexture(
                metallicTexture,
                metallicSampler,
                input.texCoord,
                (udimMask & 32u) != 0).r);
    }
    if ((textureMask & 16u) != 0)
    {
        emissiveColor = SampleMaterialTexture(
            emissiveTexture,
            emissiveSampler,
            input.texCoord,
            (udimMask & 16u) != 0).rgb;
    }
    if ((textureMask & 64u) != 0)
    {
        opacity = saturate(
            SampleMaterialTexture(
                opacityTexture,
                opacitySampler,
                input.texCoord,
                (udimMask & 64u) != 0).r);
    }
    if ((textureMask & 128u) != 0)
    {
        occlusion = saturate(
            SampleMaterialTexture(
                occlusionTexture,
                occlusionSampler,
                input.texCoord,
                (udimMask & 128u) != 0).r);
    }
    if ((textureMask & 256u) != 0)
    {
        specularColor = saturate(
            SampleMaterialTexture(
                specularColorTexture,
                specularColorSampler,
                input.texCoord,
                (udimMask & 256u) != 0).rgb);
    }
    if ((textureMask & 512u) != 0)
    {
        clearcoatAmount = saturate(
            SampleMaterialTexture(
                clearcoatTexture,
                clearcoatSampler,
                input.texCoord,
                (udimMask & 512u) != 0).r);
    }
    if ((textureMask & 1024u) != 0)
    {
        clearcoatRoughness = saturate(
            SampleMaterialTexture(
                clearcoatRoughnessTexture,
                clearcoatRoughnessSampler,
                input.texCoord,
                (udimMask & 1024u) != 0).r);
    }
    if ((textureMask & 2048u) != 0)
    {
        ior = SampleMaterialTexture(
                iorTexture,
                iorSampler,
                input.texCoord,
                (udimMask & 2048u) != 0).r;
    }
#endif

    float opacityThreshold = surface.metallicRoughnessThresholdWorkflow.z;
    if (opacityThreshold > 0.0 && opacity < opacityThreshold)
    {
        discard;
    }

    float3 normal = normalize(shadingNormal);
    float3 eye = GetEyeVector(input.eyePosition);
    // Storm flips the shading normal for rasterized back faces. Basing this on
    // coverage-facing state rather than the eye vector keeps orthographic parity
    // scenes with identity projection from turning black when Peye.z is positive.
    normal = isFrontFace ? normal : -normal;
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

    bool hasSceneLighting =
        frame.ambientLight.w > 0.5 ||
        dot(frame.ambientLight.rgb, frame.ambientLight.rgb) > 0.0;
    uint lightCount = min((uint)frame.ambientLight.w, 8u);
    float3 sceneEye = normalize(mul(frame.eyeToWorld, float4(eye, 0.0)).xyz);
    float3 worldPosition = mul(frame.eyeToWorld, float4(input.eyePosition, 1.0)).xyz;
    float3 color = diffuseColor * surface.lightColorAmbient.a;
    color += diffuseColor * frame.ambientLight.rgb;
    if (!hasSceneLighting)
    {
        lightCount = 1u;
    }
    for (uint lightIndex = 0u; lightIndex < lightCount; lightIndex++)
    {
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
            color += occlusion * emissionScale * normalDotLight *
                ((directDiffuse * diffuseScale * diffuseIrradiance) +
                (directSpecular * specularScale * specularIrradiance));
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
