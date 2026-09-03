// Copyright (c) marcschier. Licensed under the MIT License.

#include "light.h"

#include "openusd_hdsilk.h"
#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/tf/token.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/light.h"
#include "pxr/usd/sdf/assetPath.h"
#include "pxr/base/vt/value.h"

#include <string>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
const TfToken WidthToken("width");
const TfToken HeightToken("height");
const TfToken LengthToken("length");
const TfToken PoleAxisToken("poleAxis");
const TfToken SceneAxisToken("scene");
const TfToken LatlongToken("latlong");
const TfToken MirroredBallToken("mirroredBall");
const TfToken AngularToken("angular");
const TfToken CubeMapVerticalCrossToken("cubeMapVerticalCross");

float _ReadFloat(const VtValue& value, float fallback)
{
    if (value.IsHolding<float>())
    {
        return value.UncheckedGet<float>();
    }
    if (value.IsHolding<double>())
    {
        return static_cast<float>(value.UncheckedGet<double>());
    }
    if (value.IsHolding<int>())
    {
        return static_cast<float>(value.UncheckedGet<int>());
    }
    return fallback;
}

bool _ReadBool(const VtValue& value, bool fallback)
{
    return value.IsHolding<bool>() ? value.UncheckedGet<bool>() : fallback;
}

GfVec3f _ReadVec3f(const VtValue& value, const GfVec3f& fallback)
{
    if (value.IsHolding<GfVec3f>())
    {
        return value.UncheckedGet<GfVec3f>();
    }
    return fallback;
}

/// Resolves a dome light's texture:file to the asset path the consumer opens.
/// The resolved path is preferred because that is what an asset resolver
/// already turned the authored, possibly relative or packaged, reference into;
/// the authored path is the fallback for a value a resolver left alone.
std::string _ReadAssetPath(const VtValue& value)
{
    if (value.IsHolding<SdfAssetPath>())
    {
        const SdfAssetPath& asset = value.UncheckedGet<SdfAssetPath>();
        return asset.GetResolvedPath().empty()
            ? asset.GetAssetPath()
            : asset.GetResolvedPath();
    }
    if (value.IsHolding<std::string>())
    {
        return value.UncheckedGet<std::string>();
    }
    return std::string();
}

TfToken _ReadToken(const VtValue& value)
{
    if (value.IsHolding<TfToken>())
    {
        return value.UncheckedGet<TfToken>();
    }
    if (value.IsHolding<std::string>())
    {
        return TfToken(value.UncheckedGet<std::string>());
    }
    return TfToken();
}

uint32_t _ReadDomeTextureFormat(const VtValue& value)
{
    const TfToken format = _ReadToken(value);
    if (format == LatlongToken)
    {
        return OPENUSD_SILK_DOME_TEXTURE_LATLONG;
    }
    if (format == MirroredBallToken)
    {
        return OPENUSD_SILK_DOME_TEXTURE_MIRRORED_BALL;
    }
    if (format == AngularToken)
    {
        return OPENUSD_SILK_DOME_TEXTURE_ANGULAR;
    }
    if (format == CubeMapVerticalCrossToken)
    {
        return OPENUSD_SILK_DOME_TEXTURE_CUBE_MAP_VERTICAL_CROSS;
    }
    return OPENUSD_SILK_DOME_TEXTURE_AUTOMATIC;
}
}

HdSilkLight::HdSilkLight(SdfPath const& id, TfToken typeId)
    : HdLight(id)
    , _typeId(std::move(typeId))
{
}

HdDirtyBits
HdSilkLight::GetInitialDirtyBitsMask() const
{
    return HdLight::AllDirty;
}

void
HdSilkLight::Sync(
    HdSceneDelegate* sceneDelegate,
    HdRenderParam* renderParam,
    HdDirtyBits* dirtyBits)
{
    if (sceneDelegate == nullptr || renderParam == nullptr || dirtyBits == nullptr)
    {
        return;
    }

    HdSilkRenderParam* silkParam = dynamic_cast<HdSilkRenderParam*>(renderParam);
    if (silkParam == nullptr)
    {
        *dirtyBits = Clean;
        return;
    }

    HdSilkLightRecord record;
    record.path = GetId().GetString();
    if (_typeId == HdPrimTypeTokens->distantLight)
    {
        record.type = OPENUSD_SILK_LIGHT_DISTANT;
    }
    else if (_typeId == HdPrimTypeTokens->sphereLight)
    {
        record.type = OPENUSD_SILK_LIGHT_SPHERE;
    }
    else if (_typeId == HdPrimTypeTokens->rectLight)
    {
        record.type = OPENUSD_SILK_LIGHT_RECT;
        record.shapeX = _ReadFloat(sceneDelegate->GetLightParamValue(GetId(), WidthToken), 1.0f);
        record.shapeY = _ReadFloat(sceneDelegate->GetLightParamValue(GetId(), HeightToken), 1.0f);
    }
    else if (_typeId == HdPrimTypeTokens->diskLight)
    {
        record.type = OPENUSD_SILK_LIGHT_DISK;
    }
    else if (_typeId == HdPrimTypeTokens->cylinderLight)
    {
        record.type = OPENUSD_SILK_LIGHT_CYLINDER;
        record.shapeX = _ReadFloat(sceneDelegate->GetLightParamValue(GetId(), LengthToken), 1.0f);
    }
    else if (_typeId == HdPrimTypeTokens->domeLight)
    {
        record.ambientOnly = true;
        record.textureAsset = _ReadAssetPath(
            sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->textureFile));
        if (!record.textureAsset.empty())
        {
            record.textureFormat = _ReadDomeTextureFormat(
                sceneDelegate->GetLightParamValue(
                    GetId(),
                    HdLightTokens->textureFormat));

            // sourceColorSpace stays AUTO: UsdLux carries the dome texture's
            // colour space as asset-path metadata, which HdLight parameters do
            // not expose, so hdSilk states that it does not know rather than
            // asserting a space it never read. The consumer resolves AUTO from
            // the decoded image, which is the only place the information exists.
            record.sourceColorSpace = OPENUSD_SILK_COLOR_SPACE_AUTO;

            // Colour temperature and a non-scene pole axis both change what the
            // image means and neither is on this wire, so they are named rather
            // than silently dropped.
            if (_ReadBool(
                    sceneDelegate->GetLightParamValue(
                        GetId(),
                        HdLightTokens->enableColorTemperature),
                    false))
            {
                record.unsupportedFeatures |=
                    OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_COLOR_TEMPERATURE;
            }
            const TfToken poleAxis = _ReadToken(
                sceneDelegate->GetLightParamValue(GetId(), PoleAxisToken));
            if (!poleAxis.IsEmpty() && poleAxis != SceneAxisToken)
            {
                record.unsupportedFeatures |=
                    OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_POLE_AXIS;
            }
        }
    }
    else
    {
        silkParam->GetSceneState().RemoveLight(record.path);
        *dirtyBits = Clean;
        return;
    }

    const GfVec3f color = _ReadVec3f(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->color),
        GfVec3f(1.0f, 1.0f, 1.0f));
    record.color[0] = color[0];
    record.color[1] = color[1];
    record.color[2] = color[2];
    record.intensity = _ReadFloat(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->intensity),
        1.0f);
    record.exposure = _ReadFloat(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->exposure),
        0.0f);
    record.diffuse = _ReadFloat(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->diffuse),
        1.0f);
    record.specular = _ReadFloat(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->specular),
        1.0f);
    record.radius = _ReadFloat(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->radius),
        0.5f);
    record.shadowEnabled = _ReadBool(
        sceneDelegate->GetLightParamValue(GetId(), HdLightTokens->shadowEnable),
        false) ? 1u : 0u;

    // UsdImaging resolves each linking collection -- includes, excludes,
    // expansion rules, nested collections and membership expressions alike --
    // into a single category identity before Hydra reports it, and reserves the
    // empty identity for the query that includes everything. Reading the
    // identity is therefore the whole of what a render delegate can and should
    // do here: an empty token means the light links to every prim, and a
    // non-empty one is matched against the categories Hydra reports per prim.
    record.lightLinkCategory =
        _ReadToken(sceneDelegate->GetLightParamValue(GetId(), HdTokens->lightLink))
            .GetString();
    record.shadowLinkCategory =
        _ReadToken(sceneDelegate->GetLightParamValue(GetId(), HdTokens->shadowLink))
            .GetString();

    // A dome renders no shadow map -- the ABI v19 shadow slice covers direct
    // lights only -- so collection:shadowLink on one is a caster restriction
    // with nothing to restrict. It is named rather than folded into the dome's
    // receiver mask, which would silently darken exactly the prims the author
    // asked to keep lit. collection:lightLink is a receiver restriction and *is*
    // applied, through the ABI v21 dome mask.
    if (record.ambientOnly && !record.shadowLinkCategory.empty())
    {
        record.unsupportedFeatures |=
            OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION;
    }

    HdSilkFlattenMatrix(sceneDelegate->GetTransform(GetId()), record.transform);
    silkParam->GetSceneState().ReplaceLight(std::move(record));
    *dirtyBits = Clean;
}

PXR_NAMESPACE_CLOSE_SCOPE
