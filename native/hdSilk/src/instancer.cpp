// Copyright (c) marcschier. Licensed under the MIT License.

#include "instancer.h"

#include "pxr/base/gf/quatd.h"
#include "pxr/base/gf/quatf.h"
#include "pxr/base/gf/quath.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"

#include <cstddef>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
/// Reads element "index" from a VtArray<T> held by "value". Returns false for
/// an absent primvar, a mismatched element type, or an out-of-range index so
/// that the caller can fall back to the identity contribution.
template <typename T>
bool SampleElement(const VtValue& value, int index, T* out)
{
    if (index < 0 || !value.IsHolding<VtArray<T>>())
    {
        return false;
    }
    const VtArray<T>& array = value.UncheckedGet<VtArray<T>>();
    if (static_cast<size_t>(index) >= array.size())
    {
        return false;
    }
    *out = array[static_cast<size_t>(index)];
    return true;
}

/// Instance rotations reach Hydra as half, float, or double quaternions
/// depending on the authored type, and as <real, i, j, k> vectors from older
/// scene delegates. All four spellings resolve to the same rotation.
bool SampleRotation(const VtValue& value, int index, GfQuatd* out)
{
    GfQuath half;
    if (SampleElement(value, index, &half))
    {
        *out = GfQuatd(
            half.GetReal(),
            GfVec3d(half.GetImaginary()));
        return true;
    }
    GfQuatf single;
    if (SampleElement(value, index, &single))
    {
        *out = GfQuatd(
            single.GetReal(),
            GfVec3d(single.GetImaginary()));
        return true;
    }
    if (SampleElement(value, index, out))
    {
        return true;
    }
    GfVec4f packed;
    if (SampleElement(value, index, &packed))
    {
        *out = GfQuatd(packed[0], packed[1], packed[2], packed[3]);
        return true;
    }
    return false;
}

/// Instance translations and scales may be authored at float or double
/// precision.
bool SampleVector(const VtValue& value, int index, GfVec3d* out)
{
    GfVec3f single;
    if (SampleElement(value, index, &single))
    {
        *out = GfVec3d(single);
        return true;
    }
    return SampleElement(value, index, out);
}

const VtValue* FindPrimvar(
    const std::unordered_map<TfToken, VtValue, TfToken::HashFunctor>& map,
    const TfToken& primary,
    const char* legacy)
{
    const auto primaryIterator = map.find(primary);
    if (primaryIterator != map.end())
    {
        return &primaryIterator->second;
    }
    const auto legacyIterator = map.find(TfToken(legacy));
    if (legacyIterator != map.end())
    {
        return &legacyIterator->second;
    }
    return nullptr;
}
}

HdSilkInstancer::HdSilkInstancer(HdSceneDelegate* delegate, SdfPath const& id)
    : HdInstancer(delegate, id)
{
}

HdSilkInstancer::~HdSilkInstancer() = default;

void
HdSilkInstancer::Sync(
    HdSceneDelegate* delegate,
    HdRenderParam* /*renderParam*/,
    HdDirtyBits* dirtyBits)
{
    if ((*dirtyBits & HdChangeTracker::DirtyVisibility) != 0)
    {
        const bool visible = delegate->GetVisible(GetId());
        std::lock_guard<std::mutex> lock(_mutex);
        _visible = visible;
    }

    _UpdateInstancer(delegate, dirtyBits);

    if (HdChangeTracker::IsAnyPrimvarDirty(*dirtyBits, GetId()))
    {
        _SyncPrimvars(delegate, *dirtyBits);
    }
}

void
HdSilkInstancer::_SyncPrimvars(HdSceneDelegate* delegate, HdDirtyBits dirtyBits)
{
    SdfPath const& id = GetId();
    const HdPrimvarDescriptorVector primvars =
        delegate->GetPrimvarDescriptors(id, HdInterpolationInstance);

    for (HdPrimvarDescriptor const& primvar : primvars)
    {
        if (!HdChangeTracker::IsPrimvarDirty(dirtyBits, id, primvar.name))
        {
            continue;
        }
        VtValue value = delegate->Get(id, primvar.name);
        if (value.IsEmpty())
        {
            continue;
        }
        std::lock_guard<std::mutex> lock(_mutex);
        _primvarMap[primvar.name] = std::move(value);
    }
}

VtMatrix4dArray
HdSilkInstancer::ComputeInstanceTransforms(SdfPath const& prototypeId)
{
    std::unordered_map<TfToken, VtValue, TfToken::HashFunctor> primvars;
    {
        std::lock_guard<std::mutex> lock(_mutex);
        if (!_visible)
        {
            return {};
        }
        primvars = _primvarMap;
    }

    // Each instance transform is
    //   instancerTransform
    //   * translation(index) * rotation(index) * scale(index)
    //   * instanceTransform(index)
    // with any absent primvar contributing the identity, matching the
    // documented Hydra convention implemented by hdEmbree.
    const GfMatrix4d instancerTransform =
        GetDelegate()->GetInstancerTransform(GetId());
    const VtIntArray instanceIndices =
        GetDelegate()->GetInstanceIndices(GetId(), prototypeId);

    VtMatrix4dArray transforms(instanceIndices.size());
    for (size_t i = 0; i < instanceIndices.size(); ++i)
    {
        transforms[i] = instancerTransform;
    }

    if (const VtValue* translations = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceTranslations,
            "translate"))
    {
        for (size_t i = 0; i < instanceIndices.size(); ++i)
        {
            GfVec3d translation;
            if (SampleVector(*translations, instanceIndices[i], &translation))
            {
                GfMatrix4d matrix(1.0);
                matrix.SetTranslate(translation);
                transforms[i] = matrix * transforms[i];
            }
        }
    }

    if (const VtValue* rotations = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceRotations,
            "rotate"))
    {
        for (size_t i = 0; i < instanceIndices.size(); ++i)
        {
            GfQuatd rotation;
            if (SampleRotation(*rotations, instanceIndices[i], &rotation))
            {
                GfMatrix4d matrix(1.0);
                matrix.SetRotate(rotation);
                transforms[i] = matrix * transforms[i];
            }
        }
    }

    if (const VtValue* scales = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceScales,
            "scale"))
    {
        for (size_t i = 0; i < instanceIndices.size(); ++i)
        {
            GfVec3d scale;
            if (SampleVector(*scales, instanceIndices[i], &scale))
            {
                GfMatrix4d matrix(1.0);
                matrix.SetScale(scale);
                transforms[i] = matrix * transforms[i];
            }
        }
    }

    if (const VtValue* instanceTransforms = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceTransforms,
            "instanceTransform"))
    {
        for (size_t i = 0; i < instanceIndices.size(); ++i)
        {
            GfMatrix4d instanceTransform;
            if (SampleElement(
                    *instanceTransforms,
                    instanceIndices[i],
                    &instanceTransform))
            {
                transforms[i] = instanceTransform * transforms[i];
            }
        }
    }

    if (GetParentId().IsEmpty())
    {
        return transforms;
    }

    HdInstancer* parent =
        GetDelegate()->GetRenderIndex().GetInstancer(GetParentId());
    if (parent == nullptr)
    {
        return transforms;
    }

    const VtMatrix4dArray parentTransforms =
        static_cast<HdSilkInstancer*>(parent)->ComputeInstanceTransforms(
            GetId());
    VtMatrix4dArray nested(parentTransforms.size() * transforms.size());
    for (size_t parentIndex = 0;
         parentIndex < parentTransforms.size();
         ++parentIndex)
    {
        for (size_t index = 0; index < transforms.size(); ++index)
        {
            nested[(parentIndex * transforms.size()) + index] =
                transforms[index] * parentTransforms[parentIndex];
        }
    }
    return nested;
}

PXR_NAMESPACE_CLOSE_SCOPE
