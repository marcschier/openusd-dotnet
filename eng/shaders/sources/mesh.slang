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
#ifndef MAP_BASE_COLOR
#define MAP_BASE_COLOR 0
#endif
#ifndef MAP_NORMAL
#define MAP_NORMAL 0
#endif
#ifndef MAP_ROUGHNESS_METALLIC
#define MAP_ROUGHNESS_METALLIC 0
#endif
#ifndef MAP_EMISSIVE
#define MAP_EMISSIVE 0
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

#if VERTEX_STAGE
struct InstanceParameters
{
    float4x4 objectToClip;
    float4 tint;
};

[[vk::binding(6, 0)]]
StructuredBuffer<InstanceParameters> instanceParameters : register(t6);
#endif

#if FRAGMENT_STAGE && (MAP_BASE_COLOR || MAP_NORMAL || MAP_ROUGHNESS_METALLIC || MAP_EMISSIVE)
[[vk::binding(1, 0)]]
SamplerState materialSampler : register(s0);
#endif
#if FRAGMENT_STAGE && MAP_BASE_COLOR
[[vk::binding(2, 0)]]
Texture2D<float4> baseColorTexture : register(t0);
#endif
#if FRAGMENT_STAGE && MAP_NORMAL
[[vk::binding(3, 0)]]
Texture2D<float4> normalTexture : register(t1);
#endif
#if FRAGMENT_STAGE && MAP_ROUGHNESS_METALLIC
[[vk::binding(4, 0)]]
Texture2D<float4> roughnessMetallicTexture : register(t2);
#endif
#if FRAGMENT_STAGE && MAP_EMISSIVE
[[vk::binding(5, 0)]]
Texture2D<float4> emissiveTexture : register(t3);
#endif

#if VERTEX_STAGE
[shader("vertex")]
VertexOutput VERTEX_ENTRY_POINT(VertexInput input, uint instanceId : SV_InstanceID)
{
    InstanceParameters instance = instanceParameters[instanceId];
    VertexOutput output;
    output.position = mul(instance.objectToClip, float4(input.position, 1.0));
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
};

[[vk::binding(7, 0)]]
StructuredBuffer<SurfaceParameters> surfaceParameters : register(t7);

static const float SurfacePi = 3.14159265358979323846;
static const float SurfaceEpsilon = 0.001;

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
#endif

[shader("fragment")]
float4 FRAGMENT_ENTRY_POINT(VertexOutput input) : SV_Target0
{
#if FRAGMENT_STAGE
    SurfaceParameters surface = surfaceParameters[0];

    // shaded is zero for the shared default block, which a mesh with no supported
    // material bound uses. Storm shows the display colour for such a prim, so the
    // scene tint drives diffuse rather than the UsdPreviewSurface default.
    bool shaded = surface.clearcoatShaded.z >= 0.5;
    float3 diffuseColor = shaded ? surface.diffuseOpacity.rgb : input.tint.rgb;
    float opacity = shaded ? surface.diffuseOpacity.a : input.tint.a;
    float3 emissiveColor = surface.emissiveOcclusion.rgb;
    float occlusion = surface.emissiveOcclusion.a;
    float metallic = saturate(surface.metallicRoughnessThresholdWorkflow.x);
    float roughness = clamp(surface.metallicRoughnessThresholdWorkflow.y, 0.01, 1.0);
    float3 shadingNormal = input.normal;

#if MAP_BASE_COLOR
    float4 sampledBaseColor = baseColorTexture.Sample(materialSampler, input.texCoord);
    diffuseColor *= sampledBaseColor.rgb;
    opacity *= sampledBaseColor.a;
#endif
#if MAP_NORMAL
    float3 sampledNormal =
        normalTexture.Sample(materialSampler, input.texCoord).xyz * 2.0 - 1.0;
    float3 tangent = normalize(input.tangent.xyz);
    float3 bitangent = cross(normalize(shadingNormal), tangent) * input.tangent.w;
    shadingNormal = normalize(
        (tangent * sampledNormal.x) +
        (bitangent * sampledNormal.y) +
        (shadingNormal * sampledNormal.z));
#endif
#if MAP_ROUGHNESS_METALLIC
    float3 sampledRoughnessMetallic =
        roughnessMetallicTexture.Sample(materialSampler, input.texCoord).rgb;
    roughness = clamp(roughness * sampledRoughnessMetallic.g, 0.01, 1.0);
    metallic = saturate(metallic * sampledRoughnessMetallic.b);
#endif
#if MAP_EMISSIVE
    emissiveColor *= emissiveTexture.Sample(materialSampler, input.texCoord).rgb;
#endif

    float opacityThreshold = surface.metallicRoughnessThresholdWorkflow.z;
    if (opacityThreshold > 0.0 && opacity < opacityThreshold)
    {
        discard;
    }

    // The headlight is expressed in camera space and the curated parity scenes are
    // camera-aligned, so the eye vector is the camera forward axis.
    float3 normal = normalize(shadingNormal);
    float3 eye = float3(0.0, 0.0, 1.0);
    // Storm's mesh shader flips the normal for back faces before shading, so a
    // double-sided surface is lit from whichever side is visible. Without this a
    // prim whose computed face normal points away from the camera renders black.
    normal = dot(normal, eye) < 0.0 ? -normal : normal;
    float3 lightDirection = normalize(surface.lightDirectionIntensity.xyz);
    float3 half = normalize(lightDirection + eye);
    float normalDotLight = saturate(dot(normal, lightDirection));
    float normalDotEye = saturate(abs(dot(normal, eye)) + 1.0e-5);
    float normalDotHalf = saturate(dot(normal, half));
    float eyeDotHalf = saturate(dot(eye, half));

    // Storm clamps roughness rather than clamping into a minimum band.
    float specularRoughness = max(0.001, roughness);
    float clearcoatAmount = surface.clearcoatShaded.x;
    float clearcoatRoughness = max(0.001, surface.clearcoatShaded.y);
    float fresnel = SchlickFresnel(eyeDotHalf);
    float reflectanceRatio =
        (1.0 - surface.specularIor.a) / (1.0 + surface.specularIor.a);

    // Diffuse carries the 1/pi Lambert normalization, exactly as Storm's
    // evaluateDirectDiffuse does; the light supplies the matching pi of irradiance.
    float3 diffuse = diffuseColor / SurfacePi;
    float3 specular = float3(0.0, 0.0, 0.0);
    float3 normalIncidence;
    float3 grazingIncidence;
    if (surface.metallicRoughnessThresholdWorkflow.w >= 0.5)
    {
        normalIncidence = surface.specularIor.rgb;
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

    specular = EvaluateDirectSpecular(
        normalIncidence,
        grazingIncidence,
        specularRoughness,
        fresnel,
        normalDotLight,
        normalDotEye,
        normalDotHalf);
    // Glazing angles trade diffuse for specular.
    diffuse *= 1.0 - lerp(normalIncidence, grazingIncidence, fresnel);

    if (clearcoatAmount > 0.0)
    {
        specular += clearcoatAmount * EvaluateDirectSpecular(
            reflectanceRatio * reflectanceRatio * float3(1.0, 1.0, 1.0),
            float3(1.0, 1.0, 1.0),
            clearcoatRoughness,
            fresnel,
            normalDotLight,
            normalDotEye,
            normalDotHalf);
    }

    // An intensity of one means an irradiance of pi, which is what makes a fully
    // lit Lambertian surface return its albedo and is the convention Storm's
    // headlight uses.
    float3 irradiance =
        surface.lightColorAmbient.rgb * surface.lightDirectionIntensity.w * SurfacePi;
    float3 color = occlusion * normalDotLight * (diffuse + specular) * irradiance;
    color += diffuseColor * surface.lightColorAmbient.a;
    color += emissiveColor;
    return float4(color, opacity);
#else
    return float4(abs(input.normal), 1.0) * input.tint;
#endif
}
