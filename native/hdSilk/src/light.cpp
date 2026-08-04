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
#include "pxr/base/vt/value.h"

#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
const TfToken WidthToken("width");
const TfToken HeightToken("height");
const TfToken LengthToken("length");

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

    HdSilkFlattenMatrix(sceneDelegate->GetTransform(GetId()), record.transform);
    silkParam->GetSceneState().ReplaceLight(std::move(record));
    *dirtyBits = Clean;
}

PXR_NAMESPACE_CLOSE_SCOPE
