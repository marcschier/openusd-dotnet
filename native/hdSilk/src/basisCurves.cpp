// Copyright (c) marcschier. Licensed under the MIT License.

#include "basisCurves.h"

#include "openusd_hdsilk.h"

#include "instancer.h"
#include "nurbsSceneIndexPlugin.h"
#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/half.h"
#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
bool ExtractPoints(const VtValue& value, VtVec3fArray* out)
{
    if (value.IsHolding<VtVec3fArray>())
    {
        *out = value.UncheckedGet<VtVec3fArray>();
        return true;
    }
    if (value.IsHolding<VtVec3dArray>())
    {
        const VtVec3dArray& source = value.UncheckedGet<VtVec3dArray>();
        VtVec3fArray converted(source.size());
        for (size_t i = 0; i < source.size(); ++i)
        {
            converted[i] = GfVec3f(source[i]);
        }
        *out = std::move(converted);
        return true;
    }
    return false;
}

/// Reads the interpolation Hydra declares for the "widths" primvar. Curves that
/// reach hdSilk through UsdImaging always declare one; the size-based inference
/// in HdSilkResolveCurveWidths covers delegates that publish widths without a
/// usable descriptor.
bool FindAuthoredWidthInterpolation(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    HdInterpolation* interpolation)
{
    const HdInterpolation candidates[] = {
        HdInterpolationConstant,
        HdInterpolationUniform,
        HdInterpolationVarying,
        HdInterpolationVertex};
    for (HdInterpolation candidate : candidates)
    {
        for (const HdPrimvarDescriptor& descriptor :
            sceneDelegate->GetPrimvarDescriptors(id, candidate))
        {
            if (descriptor.name != HdTokens->widths)
            {
                continue;
            }
            *interpolation = candidate;
            return true;
        }
    }
    return false;
}

bool ExtractDisplayColor(const VtValue& value, GfVec3f* color)
{
    if (value.IsHolding<GfVec3f>())
    {
        *color = value.UncheckedGet<GfVec3f>();
        return true;
    }
    if (value.IsHolding<VtVec3fArray>())
    {
        const VtVec3fArray& colors = value.UncheckedGet<VtVec3fArray>();
        if (!colors.empty())
        {
            *color = colors.front();
            return true;
        }
    }
    return false;
}

uint64_t CountWidthValues(const VtValue& value)
{
    if (value.IsHolding<VtFloatArray>())
    {
        return value.UncheckedGet<VtFloatArray>().size();
    }
    if (value.IsHolding<VtDoubleArray>())
    {
        return value.UncheckedGet<VtDoubleArray>().size();
    }
    if (value.IsHolding<VtHalfArray>())
    {
        return value.UncheckedGet<VtHalfArray>().size();
    }
    return 1;
}

template <typename Element>
bool FlattenNurbsAttribute(const VtValue& value, uint32_t components, HdSilkMeshAttribute* out)
{
    if (!value.IsHolding<VtArray<Element>>())
    {
        return false;
    }
    const auto& source = value.UncheckedGet<VtArray<Element>>();
    out->componentCount = components;
    out->data.reserve(source.size() * components);
    for (const Element& element : source)
    {
        for (size_t component = 0; component < components; ++component)
        {
            out->data.push_back(element[component]);
        }
    }
    return true;
}

bool ReadNurbsVertexAttributes(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    std::vector<HdSilkMeshAttribute>* attributes)
{
    for (const auto& descriptor :
        sceneDelegate->GetPrimvarDescriptors(id, HdInterpolationVertex))
    {
        if (descriptor.name == HdTokens->points || descriptor.name == HdTokens->normals ||
            descriptor.name == HdTokens->widths)
        {
            continue;
        }
        const VtValue value = sceneDelegate->Get(id, descriptor.name);
        HdSilkMeshAttribute attribute;
        attribute.name = descriptor.name.GetString();
        attribute.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        if (value.IsHolding<VtFloatArray>())
        {
            const auto& source = value.UncheckedGet<VtFloatArray>();
            attribute.componentCount = 1;
            attribute.data.assign(source.begin(), source.end());
        }
        else if (!FlattenNurbsAttribute<GfVec2f>(value, 2, &attribute) &&
            !FlattenNurbsAttribute<GfVec3f>(value, 3, &attribute) &&
            !FlattenNurbsAttribute<GfVec4f>(value, 4, &attribute))
        {
            return false;
        }
        if (descriptor.name == HdTokens->displayColor)
        {
            attribute.semantic = OPENUSD_SILK_ATTRIBUTE_COLOR;
        }
        else if (descriptor.role == HdPrimvarRoleTokens->textureCoordinate ||
            (attribute.componentCount == 2 && attribute.name.rfind("st", 0) == 0))
        {
            attribute.semantic = OPENUSD_SILK_ATTRIBUTE_TEXCOORD;
        }
        else
        {
            attribute.semantic = OPENUSD_SILK_ATTRIBUTE_CUSTOM;
        }
        attributes->push_back(std::move(attribute));
    }
    return true;
}

constexpr size_t BezierLineSegmentsPerSpan = 8;

bool SupportsBezierTopology(const HdBasisCurvesTopology& topology)
{
    const TfToken wrap = topology.GetCurveWrap();
    return topology.GetCurveType() == HdTokens->cubic &&
        topology.GetCurveBasis() == HdTokens->bezier &&
        (wrap == HdTokens->nonperiodic ||
            wrap == HdTokens->periodic ||
            wrap == HdTokens->pinned) &&
        topology.GetInvisiblePoints().empty();
}

bool BezierSpanCount(int count, const TfToken& wrap, size_t* spans)
{
    if (wrap == HdTokens->periodic)
    {
        if (count < 3 || (count % 3) != 0)
        {
            return false;
        }
        *spans = static_cast<size_t>(count / 3);
    }
    else if (wrap == HdTokens->pinned && (count == 2 || count == 3))
    {
        // Storm repeats the last control here; this is not USD's cubic validity rule.
        *spans = 1;
    }
    else
    {
        if (count < 4 || ((count - 4) % 3) != 0)
        {
            return false;
        }
        *spans = static_cast<size_t>((count - 4) / 3 + 1);
    }
    return true;
}

struct BezierTopologySize
{
    size_t controls = 0;
    size_t spans = 0;
    size_t varying = 0;
};

bool GetBezierTopologySize(
    const HdBasisCurvesTopology& topology,
    BezierTopologySize* size)
{
    *size = {};
    const VtIntArray& counts = topology.GetCurveVertexCounts();
    if (!SupportsBezierTopology(topology) ||
        counts.size() > static_cast<size_t>(std::numeric_limits<int>::max()))
    {
        return false;
    }

    const TfToken wrap = topology.GetCurveWrap();
    const bool periodic = wrap == HdTokens->periodic;
    const size_t maxControls = static_cast<size_t>(std::numeric_limits<int>::max());
    const size_t maxSpans = static_cast<size_t>(std::numeric_limits<uint32_t>::max()) /
        (2 * BezierLineSegmentsPerSpan);
    for (int count : counts)
    {
        size_t spans = 0;
        if (!BezierSpanCount(count, wrap, &spans) ||
            static_cast<size_t>(count) > maxControls - size->controls ||
            spans > maxSpans - size->spans)
        {
            return false;
        }
        const size_t varying = spans + (periodic ? 0u : 1u);
        if (varying > std::numeric_limits<size_t>::max() - size->varying)
        {
            return false;
        }
        size->controls += static_cast<size_t>(count);
        size->spans += spans;
        size->varying += varying;
    }
    return true;
}

bool HasValue(const VtValue& value)
{
    return !value.IsEmpty() && (!value.IsArrayValued() || value.GetArraySize() != 0);
}

bool IsOpaqueConstant(const VtValue& value)
{
    if (!HasValue(value))
    {
        return true;
    }
    if (value.IsHolding<float>())
    {
        return value.UncheckedGet<float>() == 1.0f;
    }
    if (value.IsHolding<double>())
    {
        return value.UncheckedGet<double>() == 1.0;
    }
    if (value.IsHolding<GfHalf>())
    {
        return static_cast<float>(value.UncheckedGet<GfHalf>()) == 1.0f;
    }
    if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& values = value.UncheckedGet<VtFloatArray>();
        return values.size() == 1 && values.front() == 1.0f;
    }
    if (value.IsHolding<VtDoubleArray>())
    {
        const VtDoubleArray& values = value.UncheckedGet<VtDoubleArray>();
        return values.size() == 1 && values.front() == 1.0;
    }
    if (value.IsHolding<VtHalfArray>())
    {
        const VtHalfArray& values = value.UncheckedGet<VtHalfArray>();
        return values.size() == 1 && static_cast<float>(values.front()) == 1.0f;
    }
    return false;
}

std::string UnsupportedBezierPrimvar(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    const HdBasisCurvesTopology& topology)
{
    if (!sceneDelegate->GetMaterialId(id).IsEmpty())
    {
        return "cubic Bezier material bindings are unsupported";
    }
    bool hasWidthDescriptor = false;
    for (size_t interpolation = 0; interpolation < HdInterpolationCount; ++interpolation)
    {
        const HdInterpolation mode = static_cast<HdInterpolation>(interpolation);
        if (!sceneDelegate->GetExtComputationPrimvarDescriptors(id, mode).empty())
        {
            return "computed primvars are unsupported for cubic Bezier curves";
        }
        for (const HdPrimvarDescriptor& descriptor :
            sceneDelegate->GetPrimvarDescriptors(id, mode))
        {
            if (descriptor.name == HdTokens->points)
            {
                continue;
            }
            const VtValue value = sceneDelegate->Get(id, descriptor.name);
            if (!HasValue(value))
            {
                continue;
            }
            if (descriptor.name == HdTokens->widths)
            {
                hasWidthDescriptor = true;
                if (topology.HasIndices() && mode == HdInterpolationVarying)
                {
                    return "indexed cubic Bezier varying widths are unsupported";
                }
                if (mode == HdInterpolationConstant || mode == HdInterpolationUniform ||
                    mode == HdInterpolationVarying || mode == HdInterpolationVertex)
                {
                    continue;
                }
                return "unsupported cubic Bezier widths interpolation";
            }
            if (descriptor.name == HdTokens->displayColor)
            {
                if (mode == HdInterpolationConstant &&
                    (value.IsHolding<GfVec3f>() ||
                        (value.IsHolding<VtVec3fArray>() && value.GetArraySize() == 1)))
                {
                    continue;
                }
                return "cubic Bezier displayColor must be constant";
            }
            if (descriptor.name == HdTokens->displayOpacity &&
                mode == HdInterpolationConstant && IsOpaqueConstant(value))
            {
                continue;
            }
            return "unsupported cubic Bezier primvar '" + descriptor.name.GetString() + "'";
        }
    }
    if (topology.HasIndices() && !hasWidthDescriptor)
    {
        const VtValue widths = sceneDelegate->Get(id, HdTokens->widths);
        if (HasValue(widths) && CountWidthValues(widths) > 1)
        {
            return "indexed cubic Bezier widths require a declared interpolation";
        }
    }
    return {};
}

bool ExtractBezierWidths(const VtValue& value, std::vector<float>* widths)
{
    widths->clear();
    if (value.IsEmpty())
    {
        return true;
    }
    if (value.IsHolding<float>())
    {
        widths->push_back(value.UncheckedGet<float>());
    }
    else if (value.IsHolding<double>())
    {
        widths->push_back(static_cast<float>(value.UncheckedGet<double>()));
    }
    else if (value.IsHolding<GfHalf>())
    {
        widths->push_back(static_cast<float>(value.UncheckedGet<GfHalf>()));
    }
    else if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& source = value.UncheckedGet<VtFloatArray>();
        widths->assign(source.begin(), source.end());
    }
    else if (value.IsHolding<VtDoubleArray>())
    {
        const VtDoubleArray& source = value.UncheckedGet<VtDoubleArray>();
        widths->reserve(source.size());
        for (double width : source)
        {
            widths->push_back(static_cast<float>(width));
        }
    }
    else if (value.IsHolding<VtHalfArray>())
    {
        const VtHalfArray& source = value.UncheckedGet<VtHalfArray>();
        widths->reserve(source.size());
        for (GfHalf width : source)
        {
            widths->push_back(static_cast<float>(width));
        }
    }
    else
    {
        return false;
    }
    for (float& width : *widths)
    {
        if (!std::isfinite(width))
        {
            return false;
        }
        width = std::max(width, 0.0f);
    }
    return true;
}

bool ResolveBezierWidths(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    const VtValue& value,
    bool hasDeclaredInterpolation,
    HdInterpolation declaredInterpolation,
    HdSilkCurveWidths* widths,
    bool* varying)
{
    BezierTopologySize size;
    std::vector<float> authored;
    if (!GetBezierTopologySize(topology, &size) ||
        !ExtractBezierWidths(value, &authored))
    {
        return false;
    }
    if (authored.empty())
    {
        widths->interpolation = HdSilkCurveWidthInterpolation::Constant;
        std::vector<float>{HdSilkDefaultCurveWidth}.swap(widths->values);
        *varying = false;
        return true;
    }

    const auto matches = [&](HdInterpolation interpolation)
    {
        switch (interpolation)
        {
        case HdInterpolationConstant:
            return authored.size() == 1;
        case HdInterpolationUniform:
            return authored.size() == topology.GetCurveVertexCounts().size();
        case HdInterpolationVarying:
            return authored.size() == size.varying;
        case HdInterpolationVertex:
            return topology.HasIndices()
                ? authored.size() == pointCount
                : authored.size() == size.controls ||
                    (authored.size() == pointCount && pointCount >= size.controls);
        default:
            return false;
        }
    };

    HdInterpolation resolved = declaredInterpolation;
    if (hasDeclaredInterpolation)
    {
        if (!matches(resolved))
        {
            return false;
        }
    }
    else
    {
        const HdInterpolation candidates[] = {
            HdInterpolationConstant, HdInterpolationUniform,
            HdInterpolationVarying, HdInterpolationVertex};
        bool found = false;
        for (HdInterpolation candidate : candidates)
        {
            if (matches(candidate))
            {
                resolved = candidate;
                found = true;
                break;
            }
        }
        if (!found)
        {
            return false;
        }
    }

    *varying = resolved == HdInterpolationVarying;
    widths->interpolation = resolved == HdInterpolationConstant
        ? HdSilkCurveWidthInterpolation::Constant
        : resolved == HdInterpolationUniform
            ? HdSilkCurveWidthInterpolation::Uniform
            : HdSilkCurveWidthInterpolation::Vertex;
    widths->values = std::move(authored);
    return true;
}

double BezierValue(double p0, double p1, double p2, double p3, double t)
{
    const double s = 1.0 - t;
    const double a = s * p0 + t * p1;
    const double b = s * p1 + t * p2;
    const double c = s * p2 + t * p3;
    return s * (s * a + t * b) + t * (s * b + t * c);
}

bool BuildBezierCurveLines(
    const HdBasisCurvesTopology& topology,
    const VtVec3fArray& points,
    const HdSilkCurveWidths& widths,
    bool varyingWidths,
    HdSilkMeshRecord* record)
{
    BezierTopologySize size;
    if (!GetBezierTopologySize(topology, &size) || widths.values.empty())
    {
        return false;
    }

    const TfToken wrap = topology.GetCurveWrap();
    const bool periodic = wrap == HdTokens->periodic;
    const VtIntArray& counts = topology.GetCurveVertexCounts();
    const VtIntArray& indices = topology.GetCurveIndices();
    const VtIntArray& invisibleCurves = topology.GetInvisibleCurves();
    std::vector<float> emittedWidths;
    size_t controlOffset = 0;
    size_t varyingOffset = 0;
    size_t spanIndex = 0;
    for (size_t curve = 0; curve < counts.size(); ++curve)
    {
        const size_t count = static_cast<size_t>(counts[curve]);
        size_t spans = 0;
        if (!BezierSpanCount(counts[curve], wrap, &spans))
        {
            return false;
        }
        const bool invisible = std::find(
            invisibleCurves.begin(), invisibleCurves.end(),
            static_cast<int>(curve)) != invisibleCurves.end();
        if (invisible)
        {
            controlOffset += count;
            varyingOffset += spans + (periodic ? 0u : 1u);
            spanIndex += spans;
            continue;
        }

        for (size_t span = 0; span < spans; ++span, ++spanIndex)
        {
            size_t controlIndices[4] = {};
            for (size_t control = 0; control < 4; ++control)
            {
                size_t local = span * 3 + control;
                if (periodic)
                {
                    local %= count;
                }
                else if (wrap == HdTokens->pinned)
                {
                    local = std::min(local, count - 1);
                }
                const size_t slot = controlOffset + local;
                if (topology.HasIndices() && slot >= indices.size())
                {
                    return false;
                }
                const int index = topology.HasIndices()
                    ? indices[slot] : static_cast<int>(slot);
                if (index < 0 || static_cast<size_t>(index) >= points.size() ||
                    (!varyingWidths &&
                        widths.interpolation == HdSilkCurveWidthInterpolation::Vertex &&
                        static_cast<size_t>(index) >= widths.values.size()))
                {
                    return false;
                }
                controlIndices[control] = static_cast<size_t>(index);
                const GfVec3f& point = points[controlIndices[control]];
                if (!std::isfinite(point[0]) || !std::isfinite(point[1]) ||
                    !std::isfinite(point[2]))
                {
                    return false;
                }
            }
            if ((widths.interpolation == HdSilkCurveWidthInterpolation::Uniform &&
                    curve >= widths.values.size()) ||
                (varyingWidths &&
                    (varyingOffset + span >= widths.values.size() ||
                        varyingOffset + (periodic ? (span + 1) % spans : span + 1) >=
                            widths.values.size())))
            {
                return false;
            }

            const auto appendEndpoint = [&](size_t sample)
            {
                const double t = static_cast<double>(sample) /
                    static_cast<double>(BezierLineSegmentsPerSpan);
                for (size_t component = 0; component < 3; ++component)
                {
                    record->points.push_back(static_cast<float>(BezierValue(
                        points[controlIndices[0]][component],
                        points[controlIndices[1]][component],
                        points[controlIndices[2]][component],
                        points[controlIndices[3]][component], t)));
                }
                float width = widths.values.front();
                if (widths.interpolation == HdSilkCurveWidthInterpolation::Uniform)
                {
                    width = widths.values[curve];
                }
                else if (varyingWidths)
                {
                    const float first = widths.values[varyingOffset + span];
                    const float second = widths.values[
                        varyingOffset + (periodic ? (span + 1) % spans : span + 1)];
                    width = static_cast<float>((1.0 - t) * first + t * second);
                }
                else if (widths.interpolation == HdSilkCurveWidthInterpolation::Vertex)
                {
                    width = static_cast<float>(BezierValue(
                        widths.values[controlIndices[0]],
                        widths.values[controlIndices[1]],
                        widths.values[controlIndices[2]],
                        widths.values[controlIndices[3]], t));
                }
                emittedWidths.push_back(width);
            };

            for (size_t sample = 0; sample < BezierLineSegmentsPerSpan; ++sample)
            {
                const uint32_t base = static_cast<uint32_t>(record->points.size() / 3);
                appendEndpoint(sample);
                appendEndpoint(sample + 1);
                record->indices.insert(record->indices.end(), {base, base + 1});
                record->triangleSubprims.push_back(static_cast<uint32_t>(spanIndex));
            }
        }
        controlOffset += count;
        varyingOffset += spans + (periodic ? 0u : 1u);
    }
    if (record->indices.empty())
    {
        return false;
    }

    HdSilkMeshAttribute normals;
    normals.name = HdTokens->normals.GetString();
    normals.semantic = OPENUSD_SILK_ATTRIBUTE_NORMAL;
    normals.componentCount = 3;
    normals.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
    normals.data.reserve(record->points.size());
    for (size_t point = 0; point < record->points.size() / 3; ++point)
    {
        normals.data.insert(normals.data.end(), {0.0f, 0.0f, 1.0f});
    }
    record->attributes.push_back(std::move(normals));

    HdSilkMeshAttribute widthAttribute;
    widthAttribute.name = HdTokens->widths.GetString();
    widthAttribute.semantic = OPENUSD_SILK_ATTRIBUTE_WIDTH;
    widthAttribute.componentCount = 1;
    if (widths.interpolation == HdSilkCurveWidthInterpolation::Constant)
    {
        widthAttribute.interpolation = OPENUSD_SILK_INTERPOLATION_CONSTANT;
        widthAttribute.data.assign(1, widths.values.front());
    }
    else
    {
        widthAttribute.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        widthAttribute.data = std::move(emittedWidths);
    }
    record->attributes.push_back(std::move(widthAttribute));
    return true;
}
}

HdSilkBasisCurves::HdSilkBasisCurves(SdfPath const& id)
    : HdBasisCurves(id)
    , _transform(1.0)
{
}

HdDirtyBits
HdSilkBasisCurves::GetInitialDirtyBitsMask() const
{
    return HdChangeTracker::Clean
        | HdChangeTracker::InitRepr
        | HdChangeTracker::DirtyPoints
        | HdChangeTracker::DirtyTopology
        | HdChangeTracker::DirtyWidths
        | HdChangeTracker::DirtyTransform
        | HdChangeTracker::DirtyVisibility
        | HdChangeTracker::DirtyMaterialId
        | HdChangeTracker::DirtyPrimvar
        | HdChangeTracker::DirtyInstancer
        | HdChangeTracker::DirtyInstanceIndex;
}

HdDirtyBits
HdSilkBasisCurves::_PropagateDirtyBits(HdDirtyBits bits) const
{
    return bits;
}

void
HdSilkBasisCurves::_InitRepr(TfToken const& reprToken, HdDirtyBits* /*dirtyBits*/)
{
    _ReprVector::iterator it = std::find_if(
        _reprs.begin(), _reprs.end(), _ReprComparator(reprToken));
    if (it == _reprs.end())
    {
        _reprs.emplace_back(reprToken, HdReprSharedPtr());
    }
}

uint64_t
HdSilkBasisCurves::_PlanPreparation(HdSceneDelegate* sceneDelegate, SdfPath const& id) const
{
    for (size_t interpolation = 0; interpolation < HdInterpolationCount; ++interpolation)
    {
        if (!sceneDelegate->GetExtComputationPrimvarDescriptors(
                id, static_cast<HdInterpolation>(interpolation)).empty())
        {
            throw std::invalid_argument(
                "The bounded hdSilk curve preparation profile does not admit computed primvars.");
        }
    }
    HdSilkNurbsGeneratedGeometryPlan nurbs;
    if (HdSilkGetNurbsGeneratedGeometryPlan(sceneDelegate, id, &nurbs))
    {
        // Generated NURBS curves are linear polylines sized by a topology-only
        // plan. Points, topology, and vertex widths are not requested here,
        // because each of those requests evaluates the curves.
        const HdPrimvarDescriptorVector vertexPrimvars =
            sceneDelegate->GetPrimvarDescriptors(id, HdInterpolationVertex);
        const bool vertexWidths = std::any_of(
            vertexPrimvars.begin(), vertexPrimvars.end(),
            [](const HdPrimvarDescriptor& primvar) {
                return primvar.name == HdTokens->widths;
            });
        const uint64_t widths = vertexWidths
            ? nurbs.points
            : CountWidthValues(sceneDelegate->Get(id, HdTokens->widths));
        HdSilkCurvePreparationPlan plan(nurbs.points, widths, nurbs.elements);
        for (const auto& primvar : vertexPrimvars)
        {
            if (primvar.name == HdTokens->points || primvar.name == HdTokens->normals ||
                primvar.name == HdTokens->widths)
            {
                continue;
            }
            using Plan = HdSilkMeshPreparationPlan;
            const uint64_t endpoints = Plan::Multiply(nurbs.elements, 2);
            plan.bytes = Plan::Add(plan.bytes, Plan::Multiply(
                Plan::Add(nurbs.points, Plan::Multiply(endpoints, 3)), 4 * sizeof(float)));
        }
        return plan.bytes;
    }

    const HdBasisCurvesTopology topology = GetBasisCurvesTopology(sceneDelegate);
    const bool bezier = SupportsBezierTopology(topology);
    if (!HdSilkSupportsLinearCurveTopology(topology) && !bezier)
    {
        throw std::invalid_argument(
            "The bounded hdSilk curve preparation profile admits supported linear wraps and "
            "cubic Bezier nonperiodic, periodic or pinned wraps without invisible control points.");
    }
    if (bezier)
    {
        const std::string unsupported = UnsupportedBezierPrimvar(sceneDelegate, id, topology);
        if (!unsupported.empty())
        {
            throw std::invalid_argument(unsupported);
        }
    }
    const VtValue pointValue = sceneDelegate->Get(id, HdTokens->points);
    uint64_t points = 0;
    if (pointValue.IsHolding<VtVec3fArray>())
    {
        points = pointValue.UncheckedGet<VtVec3fArray>().size();
    }
    else if (pointValue.IsHolding<VtVec3dArray>())
    {
        points = pointValue.UncheckedGet<VtVec3dArray>().size();
    }
    else
    {
        throw std::invalid_argument(
            "The bounded hdSilk curve preparation profile requires resolved float3 or double3 points.");
    }

    uint64_t segments = 0;
    if (bezier)
    {
        BezierTopologySize size;
        if (GetBezierTopologySize(topology, &size))
        {
            segments = HdSilkMeshPreparationPlan::Multiply(
                static_cast<uint64_t>(size.spans),
                static_cast<uint64_t>(BezierLineSegmentsPerSpan));
        }
    }
    else
    {
        const TfToken wrap = topology.GetCurveWrap();
        const bool segmented = wrap == HdTokens->segmented;
        const bool periodic = wrap == HdTokens->periodic;
        for (int count : topology.GetCurveVertexCounts())
        {
            if (count <= 0)
            {
                continue;
            }
            const uint64_t vertices = static_cast<uint64_t>(count);
            const uint64_t curveSegments =
                segmented ? vertices / 2 : periodic ? vertices : vertices - 1;
            segments = HdSilkMeshPreparationPlan::Add(segments, curveSegments);
        }
    }
    const uint64_t widths = CountWidthValues(sceneDelegate->Get(id, HdTokens->widths));
    return HdSilkCurvePreparationPlan(points, widths, segments).bytes;
}

void
HdSilkBasisCurves::Sync(
    HdSceneDelegate* sceneDelegate,
    HdRenderParam* renderParam,
    HdDirtyBits* dirtyBits,
    TfToken const& /*reprToken*/)
{
    const auto& budget = static_cast<HdSilkRenderParam*>(renderParam)->GetSceneState().MeshPreparationBudget();
    if (!budget->Enabled())
    {
        _Sync(sceneDelegate, renderParam, dirtyBits);
        return;
    }
    if (!budget->Refused())
    {
        try
        {
            _Sync(sceneDelegate, renderParam, dirtyBits);
            return;
        }
        catch (const std::exception& error)
        {
            budget->Refuse(error.what());
        }
    }
    *dirtyBits = HdChangeTracker::Clean;
}

void
HdSilkBasisCurves::_Sync(
    HdSceneDelegate* sceneDelegate,
    HdRenderParam* renderParam,
    HdDirtyBits* dirtyBits)
{
    SdfPath const& id = GetId();
    const auto& budget = static_cast<HdSilkRenderParam*>(renderParam)->GetSceneState().MeshPreparationBudget();
    const bool visibilityDirty =
        HdChangeTracker::IsVisibilityDirty(*dirtyBits, id);
    const bool topologyDirty = HdChangeTracker::IsTopologyDirty(*dirtyBits, id);
    const bool pointsDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->points);
    const bool widthsDirty = ((*dirtyBits & HdChangeTracker::DirtyWidths) != 0) ||
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->widths);
    const bool displayColorDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->displayColor);
    const bool primvarsDirty = (*dirtyBits & HdChangeTracker::DirtyPrimvar) != 0;
    const bool transformDirty = HdChangeTracker::IsTransformDirty(*dirtyBits, id);
    const bool instancerDirty =
        HdChangeTracker::IsInstancerDirty(*dirtyBits, id) ||
        HdChangeTracker::IsInstanceIndexDirty(*dirtyBits, id);
    const uint64_t materialBindingGeneration =
        static_cast<HdSilkRenderParam*>(renderParam)->GetSceneState().GetMaterialBindingGeneration();
    const bool cubicMaterialDirty = SupportsBezierTopology(_topology) &&
        (((*dirtyBits & HdChangeTracker::DirtyMaterialId) != 0) ||
            materialBindingGeneration != _materialBindingGeneration);
    const bool topologyRefreshed = topologyDirty || _topologyRevision == 0;
    const bool recordDirty =
        topologyRefreshed || pointsDirty || widthsDirty || transformDirty ||
        visibilityDirty || displayColorDirty || primvarsDirty || instancerDirty ||
        cubicMaterialDirty;

    // Keep the published record's lease when this sync does not replace it.
    std::shared_ptr<HdSilkMeshPreparationLease> preparation;
    if (budget->Enabled() && (recordDirty || !_preparationLease))
    {
        preparation = budget->Acquire(_PlanPreparation(sceneDelegate, id));
        preparation->RetainPrevious(_preparationLease);
        _preparationLease = preparation;
    }
    else if (!budget->Enabled())
    {
        _preparationLease.reset();
    }

    if (visibilityDirty)
    {
        _UpdateVisibility(sceneDelegate, dirtyBits);
    }

    if (topologyRefreshed)
    {
        _topology = GetBasisCurvesTopology(sceneDelegate);
        _topologySupported = HdSilkSupportsLinearCurveTopology(_topology) ||
            SupportsBezierTopology(_topology);
        if (_topologySupported)
        {
            if (_topologyRevision == std::numeric_limits<uint64_t>::max())
            {
                throw std::overflow_error(
                    "The hdSilk basisCurves topology revision is exhausted.");
            }
            ++_topologyRevision;
        }
        else
        {
            TF_WARN(
                "hdSilk skipped basisCurves '%s': unsupported topology type='%s' basis='%s' wrap='%s'; "
                "cubic support is limited to Bezier without invisible control points",
                id.GetText(),
                _topology.GetCurveType().GetText(),
                _topology.GetCurveBasis().GetText(),
                _topology.GetCurveWrap().GetText());
        }
    }
    if (transformDirty)
    {
        _transform = sceneDelegate->GetTransform(id);
    }
    bool invalidPoints = false;
    if (pointsDirty || topologyRefreshed)
    {
        invalidPoints = !ExtractPoints(sceneDelegate->Get(id, HdTokens->points), &_points);
        if (invalidPoints)
        {
            _points.clear();
            TF_WARN(
                "hdSilk skipped basisCurves '%s': points must resolve to a float3 or double3 array",
                id.GetText());
        }
    }
    // Vertex widths are validated and indexed against the resolved point array,
    // so a points change alone still invalidates the resolution.
    if (!invalidPoints && _topologySupported &&
        (widthsDirty || pointsDirty || topologyRefreshed || primvarsDirty))
    {
        HdInterpolation declared = HdInterpolationConstant;
        const bool hasDeclared =
            FindAuthoredWidthInterpolation(sceneDelegate, id, &declared);
        const bool bezier = SupportsBezierTopology(_topology);
        const HdSilkCurveWidthInterpolation linearDeclared =
            declared == HdInterpolationConstant
                ? HdSilkCurveWidthInterpolation::Constant
                : declared == HdInterpolationUniform
                    ? HdSilkCurveWidthInterpolation::Uniform
                    : HdSilkCurveWidthInterpolation::Vertex;
        const VtValue value = sceneDelegate->Get(id, HdTokens->widths);
        const bool resolved = bezier
            ? ResolveBezierWidths(
                _topology, _points.size(), value, hasDeclared, declared,
                &_widths, &_cubicWidthsVarying)
            : HdSilkResolveCurveWidths(
                _topology, _points.size(), value, hasDeclared, linearDeclared, &_widths);
        if (!resolved)
        {
            // Storm still rasterizes such a curve, so falling back to the
            // UsdGeomCurves default width keeps the geometry rather than
            // deleting the prim over unusable width data.
            _widths.interpolation = HdSilkCurveWidthInterpolation::Constant;
            if (bezier)
            {
                std::vector<float>{HdSilkDefaultCurveWidth}.swap(_widths.values);
            }
            else
            {
                _widths.values.assign(1, HdSilkDefaultCurveWidth);
            }
            _cubicWidthsVarying = false;
            TF_WARN(
                "hdSilk used the default width for basisCurves '%s': authored widths are unusable for this topology",
                id.GetText());
        }
    }
    if (displayColorDirty || primvarsDirty)
    {
        if (!ExtractDisplayColor(sceneDelegate->Get(id, HdTokens->displayColor), &_displayColor) &&
            SupportsBezierTopology(_topology))
        {
            _displayColor = GfVec3f(0.7f);
        }
    }

    _UpdateInstancer(sceneDelegate, dirtyBits);
    HdInstancer::_SyncInstancerAndParents(
        sceneDelegate->GetRenderIndex(),
        GetInstancerId());

    HdSilkRenderParam* silkRenderParam =
        static_cast<HdSilkRenderParam*>(renderParam);
    const std::string unsupportedBezier =
        recordDirty && SupportsBezierTopology(_topology)
            ? UnsupportedBezierPrimvar(sceneDelegate, id, _topology) : std::string();
    if (SupportsBezierTopology(_topology))
    {
        _materialBindingGeneration = materialBindingGeneration;
    }
    if (!unsupportedBezier.empty())
    {
        TF_WARN(
            "hdSilk skipped basisCurves '%s': %s",
            id.GetText(), unsupportedBezier.c_str());
    }
    if (!IsVisible() || !_topologySupported || invalidPoints || !unsupportedBezier.empty())
    {
        silkRenderParam->GetSceneState().RemoveMesh(id.GetString());
        if (preparation) { preparation->Complete(); }
        *dirtyBits = HdChangeTracker::Clean;
        return;
    }

    if (recordDirty)
    {
        HdSilkMeshRecord record;
        record.preparationLease = preparation;
        record.path = id.GetString();
        record.primId = GetPrimId();
        record.topologyKind = OPENUSD_SILK_TOPOLOGY_LINE_LIST;
        // A curve's emitted vertices and segments are resampled from authored
        // control points and are not authored mesh points or edges, so both
        // exact targets are refused by topology rather than answered with an
        // emitted index.
        record.subprimUnsupported = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE;
        record.topologyRevision = _topologyRevision;
        HdSilkFlattenMatrix(_transform, record.transform);
        record.displayColor[0] = _displayColor[0];
        record.displayColor[1] = _displayColor[1];
        record.displayColor[2] = _displayColor[2];

        // Cubic spans use fixed line chords; widths remain attributes, not ribbons.
        const bool bezier = SupportsBezierTopology(_topology);
        std::vector<HdSilkMeshAttribute> vertexAttributes;
        const bool attributesRead = !HdSilkHasNurbsGeneratedGeometry(sceneDelegate, id) ||
            ReadNurbsVertexAttributes(sceneDelegate, id, &vertexAttributes);
        const bool built = attributesRead && (bezier
            ? BuildBezierCurveLines(_topology, _points, _widths, _cubicWidthsVarying, &record)
            : HdSilkBuildLinearCurveLines(_topology, _points, _widths, &record, vertexAttributes));
        if (!built)
        {
            TF_WARN(
                "hdSilk skipped basisCurves '%s': invalid or empty %s topology, control points or vertex attributes",
                id.GetText(), bezier ? "cubic Bezier" : "linear");
            silkRenderParam->GetSceneState().RemoveMesh(id.GetString());
            if (preparation) { preparation->Complete(); }
            *dirtyBits = HdChangeTracker::Clean;
            return;
        }

        const std::string path = record.path;
        std::vector<HdSilkMeshRecord> records =
            _BuildInstanceRecords(sceneDelegate, std::move(record));
        silkRenderParam->GetSceneState().ReplaceMeshInstances(
            path,
            std::move(records));
    }

    if (preparation) { preparation->Complete(); }
    *dirtyBits = HdChangeTracker::Clean;
}

std::vector<HdSilkMeshRecord>
HdSilkBasisCurves::_BuildInstanceRecords(
    HdSceneDelegate* sceneDelegate,
    HdSilkMeshRecord record)
{
    const SdfPath& instancerId = GetInstancerId();
    if (instancerId.IsEmpty())
    {
        record.instanceId = 0;
        record.instanceIndex = 0;
        std::vector<HdSilkMeshRecord> records;
        records.push_back(std::move(record));
        return records;
    }

    HdInstancer* instancer =
        sceneDelegate->GetRenderIndex().GetInstancer(instancerId);
    if (instancer == nullptr)
    {
        return {};
    }

    // Curve prototypes carry their payload once, exactly as meshes do since ABI
    // v8: the lowest published instance index keeps the full record and every
    // later instance reuses it. The published index is the instance's own index
    // inside the instancer rather than its position in the resolved array, so
    // the payload rides the lowest index the prototype owns and not necessarily
    // index zero.
    const std::vector<HdSilkInstanceSample> samples =
        static_cast<HdSilkInstancer*>(instancer)->ComputeInstanceSamples(
            GetId());

    const int32_t instanceId = HdSilkStableInstanceId(instancerId.GetString());
    std::vector<HdSilkMeshRecord> records;
    records.reserve(samples.size());
    // Built once and copied per instance: it holds no geometry and no identity
    // table, so an instance reference never costs a copy of the prototype's
    // curve points.
    const HdSilkMeshRecord reference = HdSilkMakeInstanceReference(record);
    for (size_t position = 0; position < samples.size(); ++position)
    {
        const HdSilkInstanceSample& sample = samples[position];
        if (sample.index > static_cast<int64_t>(
                std::numeric_limits<int32_t>::max()) ||
            sample.sourcePickIndex > static_cast<int64_t>(std::numeric_limits<int32_t>::max()))
        {
            throw std::overflow_error(
                "The hdSilk basisCurves instance index exceeds the 32-bit instance index.");
        }
        HdSilkMeshRecord instanceRecord =
            position == 0 ? std::move(record) : reference;
        instanceRecord.instanceId = instanceId;
        instanceRecord.instancerPath = instancerId.GetString();
        instanceRecord.instanceIndex = static_cast<int32_t>(sample.index);
        instanceRecord.sourcePickIndex = static_cast<int32_t>(sample.sourcePickIndex);
        instanceRecord.instancerContext = sample.context;
        HdSilkFlattenMatrix(
            _transform * sample.transform,
            instanceRecord.transform);
        records.push_back(std::move(instanceRecord));
    }
    return records;
}

PXR_NAMESPACE_CLOSE_SCOPE
