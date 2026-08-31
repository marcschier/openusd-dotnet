// Copyright (c) marcschier. Licensed under the MIT License.

#include "instancer.h"

#include "pxr/base/gf/quatd.h"
#include "pxr/base/gf/quatf.h"
#include "pxr/base/gf/quath.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"

#include <algorithm>
#include <cstddef>
#include <limits>
#include <stdexcept>

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

std::vector<HdSilkInstanceSample>
HdSilkInstancer::ComputeInstanceSamples(SdfPath const& prototypeId)
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

    std::vector<HdSilkInstanceSample> samples;
    samples.reserve(instanceIndices.size());
    for (size_t i = 0; i < instanceIndices.size(); ++i)
    {
        // A negative index cannot address any instance primvar element, so it
        // cannot be resolved into an instance at all.
        if (instanceIndices[i] < 0)
        {
            continue;
        }
        HdSilkInstanceSample sample;
        sample.transform = instancerTransform;
        sample.index = static_cast<int64_t>(instanceIndices[i]);
        samples.push_back(sample);
    }

    if (const VtValue* translations = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceTranslations,
            "translate"))
    {
        for (HdSilkInstanceSample& sample : samples)
        {
            GfVec3d translation;
            if (SampleVector(
                    *translations,
                    static_cast<int>(sample.index),
                    &translation))
            {
                GfMatrix4d matrix(1.0);
                matrix.SetTranslate(translation);
                sample.transform = matrix * sample.transform;
            }
        }
    }

    if (const VtValue* rotations = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceRotations,
            "rotate"))
    {
        for (HdSilkInstanceSample& sample : samples)
        {
            GfQuatd rotation;
            if (SampleRotation(
                    *rotations,
                    static_cast<int>(sample.index),
                    &rotation))
            {
                GfMatrix4d matrix(1.0);
                matrix.SetRotate(rotation);
                sample.transform = matrix * sample.transform;
            }
        }
    }

    if (const VtValue* scales = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceScales,
            "scale"))
    {
        for (HdSilkInstanceSample& sample : samples)
        {
            GfVec3d scale;
            if (SampleVector(
                    *scales,
                    static_cast<int>(sample.index),
                    &scale))
            {
                GfMatrix4d matrix(1.0);
                matrix.SetScale(scale);
                sample.transform = matrix * sample.transform;
            }
        }
    }

    if (const VtValue* instanceTransforms = FindPrimvar(
            primvars,
            HdInstancerTokens->instanceTransforms,
            "instanceTransform"))
    {
        for (HdSilkInstanceSample& sample : samples)
        {
            GfMatrix4d instanceTransform;
            if (SampleElement(
                    *instanceTransforms,
                    static_cast<int>(sample.index),
                    &instanceTransform))
            {
                sample.transform = instanceTransform * sample.transform;
            }
        }
    }

    // Hydra does not promise an order for GetInstanceIndices, and the wire
    // contract depends on ascending instance indices: the lowest index of a
    // prototype carries the geometry payload every other record reuses.
    std::sort(
        samples.begin(),
        samples.end(),
        [](const HdSilkInstanceSample& left, const HdSilkInstanceSample& right)
        {
            return left.index < right.index;
        });

    if (GetParentId().IsEmpty())
    {
        return samples;
    }

    HdInstancer* parent =
        GetDelegate()->GetRenderIndex().GetInstancer(GetParentId());
    if (parent == nullptr)
    {
        return samples;
    }

    const std::vector<HdSilkInstanceSample> parentSamples =
        static_cast<HdSilkInstancer*>(parent)->ComputeInstanceSamples(GetId());

    // The radix is this instancer's own instance count and nothing else. It
    // must not be widened to fit the samples of the prototype being resolved:
    // a per-prototype radix would give two prototypes of the same instancer two
    // different index spaces, so the same nested instance would be numbered
    // differently depending on which prototype asked, and adding or hiding an
    // instance of one prototype would renumber the other. That is exactly the
    // instability the authoritative index exists to avoid.
    //
    // If the authoritative count cannot explain an index this level published,
    // the composition has no unique encoding to offer, so the sample is dropped
    // with a diagnostic rather than silently folded into another instance's
    // slot. A zero count -- an instancer that published no instance primvar
    // array at all -- rejects every sample for the same reason.
    const int64_t stride = _InstanceCount(primvars);

    std::vector<HdSilkInstanceSample> composable;
    composable.reserve(samples.size());
    for (const HdSilkInstanceSample& sample : samples)
    {
        if (sample.index >= stride)
        {
            TF_WARN(
                "hdSilk dropped nested instance %lld of '%s': the instancer reports %lld instances, so the index has no unique nested encoding",
                static_cast<long long>(sample.index),
                GetId().GetText(),
                static_cast<long long>(stride));
            continue;
        }
        composable.push_back(sample);
    }

    std::vector<HdSilkInstanceSample> nested;
    nested.reserve(parentSamples.size() * composable.size());
    for (const HdSilkInstanceSample& parentSample : parentSamples)
    {
        for (const HdSilkInstanceSample& sample : composable)
        {
            constexpr int64_t maximum = std::numeric_limits<int64_t>::max();
            if (parentSample.index > (maximum - sample.index) / stride)
            {
                throw std::overflow_error(
                    "The hdSilk nested instance index overflows.");
            }
            HdSilkInstanceSample composed;
            composed.transform = sample.transform * parentSample.transform;
            composed.index = (parentSample.index * stride) + sample.index;
            nested.push_back(composed);
        }
    }
    return nested;
}

int64_t
HdSilkInstancer::_InstanceCount(
    const std::unordered_map<TfToken, VtValue, TfToken::HashFunctor>& primvars)
{
    // Hydra publishes one element per instance in every instance primvar, so
    // the longest array is this instancer's instance count.
    int64_t count = 0;
    for (const auto& entry : primvars)
    {
        if (!entry.second.IsArrayValued())
        {
            continue;
        }
        count = std::max(count, static_cast<int64_t>(entry.second.GetArraySize()));
    }
    return count;
}

PXR_NAMESPACE_CLOSE_SCOPE
