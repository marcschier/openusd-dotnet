// Copyright (c) marcschier. Licensed under the MIT License.

#include "nurbsSceneIndexPlugin.h"

#include "nurbsGeometry.h"

#include "pxr/base/gf/vec2d.h"
#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec4d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/tf/staticTokens.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/types.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/basisCurvesSchema.h"
#include "pxr/imaging/hd/basisCurvesTopologySchema.h"
#include "pxr/imaging/hd/filteringSceneIndex.h"
#include "pxr/imaging/hd/meshSchema.h"
#include "pxr/imaging/hd/meshTopologySchema.h"
#include "pxr/imaging/hd/nurbsCurvesSchema.h"
#include "pxr/imaging/hd/nurbsPatchSchema.h"
#include "pxr/imaging/hd/overlayContainerDataSource.h"
#include "pxr/imaging/hd/primvarSchema.h"
#include "pxr/imaging/hd/primvarsSchema.h"
#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/pxOsd/tokens.h"

#include <algorithm>
#include <cmath>
#include <functional>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

TF_DEFINE_PRIVATE_TOKENS(
    _hdSilkNurbsTokens,
    ((sceneIndexPluginName, "HdSilk_NurbsSceneIndexPlugin"))
);

namespace
{
const char* const _pluginDisplayName = "Silk.NET";
const TfToken _pointWeightsToken("pointWeights");
const TfToken _skelJointIndicesToken("skel:jointIndices");
const TfToken _skelJointWeightsToken("skel:jointWeights");
const TfToken _ptexFaceOffsetToken("ptexFaceOffset");
const TfToken _adapterProvenanceToken("silkNurbsAdapterProvenance");

enum class _NurbsKind
{
    None,
    Curves,
    Patch
};

_NurbsKind _GetNurbsKind(const HdSceneIndexPrim& prim)
{
    if (prim.primType == HdNurbsCurvesSchemaTokens->nurbsCurves ||
        HdNurbsCurvesSchema::GetFromParent(prim.dataSource).IsDefined())
    {
        return _NurbsKind::Curves;
    }
    if (prim.primType == HdNurbsPatchSchemaTokens->nurbsPatch ||
        HdNurbsPatchSchema::GetFromParent(prim.dataSource).IsDefined())
    {
        return _NurbsKind::Patch;
    }
    return _NurbsKind::None;
}

// Retained vertex primvars are evaluated with the control-point basis and
// weights. Values are packed three components per evaluator list.
size_t _AttributeComponents(const VtValue& value)
{
    if (value.IsHolding<VtFloatArray>() || value.IsHolding<VtDoubleArray>())
    {
        return 1;
    }
    if (value.IsHolding<VtVec2fArray>() || value.IsHolding<VtVec2dArray>())
    {
        return 2;
    }
    if (value.IsHolding<VtVec3fArray>() || value.IsHolding<VtVec3dArray>())
    {
        return 3;
    }
    if (value.IsHolding<VtVec4fArray>() || value.IsHolding<VtVec4dArray>())
    {
        return 4;
    }
    return 0;
}

size_t _AttributeLists(size_t components)
{
    return (components + 2) / 3;
}

double _GetComponent(float value, size_t) { return value; }
double _GetComponent(double value, size_t) { return value; }
template <typename T>
double _GetComponent(const T& value, size_t component)
{
    return static_cast<double>(value[component]);
}

void _SetComponent(float& value, size_t, float component) { value = component; }
template <typename T>
void _SetComponent(T& value, size_t component, float componentValue)
{
    value[component] = componentValue;
}

double& _PointComponent(HdSilkNurbs::Point& point, size_t component)
{
    return component == 0 ? point.x : component == 1 ? point.y : point.z;
}

template <typename Element>
bool _PackAttribute(
    const VtValue& value,
    size_t components,
    size_t firstList,
    std::vector<std::vector<HdSilkNurbs::Point>>* lists,
    size_t* count)
{
    if (!value.IsHolding<VtArray<Element>>())
    {
        return false;
    }
    const VtArray<Element>& array = value.UncheckedGet<VtArray<Element>>();
    *count = array.size();
    for (size_t list = 0; list < _AttributeLists(components); ++list)
    {
        (*lists)[firstList + list].assign(array.size(), HdSilkNurbs::Point{});
    }
    for (size_t index = 0; index < array.size(); ++index)
    {
        for (size_t component = 0; component < components; ++component)
        {
            _PointComponent(
                (*lists)[firstList + component / 3][index], component % 3) =
                _GetComponent(array[index], component);
        }
    }
    return true;
}

bool _PackAnyAttribute(
    const VtValue& value,
    size_t components,
    size_t firstList,
    std::vector<std::vector<HdSilkNurbs::Point>>* lists,
    size_t* count)
{
    switch (components)
    {
    case 1:
        return _PackAttribute<float>(value, 1, firstList, lists, count) ||
            _PackAttribute<double>(value, 1, firstList, lists, count);
    case 2:
        return _PackAttribute<GfVec2f>(value, 2, firstList, lists, count) ||
            _PackAttribute<GfVec2d>(value, 2, firstList, lists, count);
    case 3:
        return _PackAttribute<GfVec3f>(value, 3, firstList, lists, count) ||
            _PackAttribute<GfVec3d>(value, 3, firstList, lists, count);
    case 4:
        return _PackAttribute<GfVec4f>(value, 4, firstList, lists, count) ||
            _PackAttribute<GfVec4d>(value, 4, firstList, lists, count);
    default:
        return false;
    }
}

bool _ToFloat(double value, float* result)
{
    if (!std::isfinite(value) ||
        std::abs(value) > static_cast<double>(std::numeric_limits<float>::max()))
    {
        return false;
    }
    *result = static_cast<float>(value);
    return true;
}

template <typename Element>
bool _UnpackAttribute(
    const std::vector<std::vector<HdSilkNurbs::Point>>& lists,
    size_t components,
    size_t firstList,
    VtValue* result)
{
    const size_t count = lists[firstList].size();
    VtArray<Element> array(count);
    for (size_t index = 0; index < count; ++index)
    {
        for (size_t component = 0; component < components; ++component)
        {
            HdSilkNurbs::Point point = lists[firstList + component / 3][index];
            float value = 0.0f;
            if (!_ToFloat(_PointComponent(point, component % 3), &value))
            {
                return false;
            }
            _SetComponent(array[index], component, value);
        }
    }
    *result = VtValue(std::move(array));
    return true;
}

bool _UnpackAnyAttribute(
    const std::vector<std::vector<HdSilkNurbs::Point>>& lists,
    size_t components,
    size_t firstList,
    VtValue* result)
{
    switch (components)
    {
    case 1:
        return _UnpackAttribute<float>(lists, 1, firstList, result);
    case 2:
        return _UnpackAttribute<GfVec2f>(lists, 2, firstList, result);
    case 3:
        return _UnpackAttribute<GfVec3f>(lists, 3, firstList, result);
    default:
        return _UnpackAttribute<GfVec4f>(lists, 4, firstList, result);
    }
}

VtValue _EmptyAttribute(size_t components)
{
    switch (components)
    {
    case 1:
        return VtValue(VtFloatArray());
    case 2:
        return VtValue(VtVec2fArray());
    case 3:
        return VtValue(VtVec3fArray());
    default:
        return VtValue(VtVec4fArray());
    }
}

struct _RetainedAttribute
{
    TfToken name;
    TfToken role;
    HdSampledDataSourceHandle source;
    size_t components = 0;
    size_t firstList = 0;
};

template <typename T>
class _NurbsValueDataSource final : public HdTypedSampledDataSource<T>
{
public:
    using Getter = std::function<T(HdSampledDataSource::Time)>;

    _NurbsValueDataSource(
        const std::shared_ptr<class _NurbsGeometryCache>& cache,
        Getter getter)
        : _cache(cache)
        , _getter(std::move(getter))
    {
    }

    VtValue GetValue(HdSampledDataSource::Time time) override
    {
        return VtValue(_getter(time));
    }

    bool GetContributingSampleTimesForInterval(
        HdSampledDataSource::Time start,
        HdSampledDataSource::Time end,
        std::vector<HdSampledDataSource::Time>* times) override;

    T GetTypedValue(HdSampledDataSource::Time time) override
    {
        return _getter(time);
    }

private:
    std::shared_ptr<class _NurbsGeometryCache> _cache;
    Getter _getter;
};

class _NurbsGeometryCache
{
public:
    // Each retained vertex primvar owns two evaluator lists, which hold up
    // to four components. Plans and evaluation therefore charge identical work.
    static constexpr size_t ListsPerAttribute = 2;
    static constexpr size_t MaxRetainedAttributes =
        HdSilkNurbs::MaxAttributeLists / ListsPerAttribute;

    _NurbsGeometryCache(
        SdfPath path,
        HdSceneIndexPrim source,
        bool isPatch)
        : _path(std::move(path))
        , _source(std::move(source))
        , _isPatch(isPatch)
    {
        const HdPrimvarsSchema primvars =
            HdPrimvarsSchema::GetFromParent(_source.dataSource);
        const TfTokenVector primvarNames = primvars.GetPrimvarNames();
        _hasSkelJointPrimvars =
            std::find(primvarNames.begin(), primvarNames.end(),
                _skelJointIndicesToken) != primvarNames.end() ||
            std::find(primvarNames.begin(), primvarNames.end(),
                _skelJointWeightsToken) != primvarNames.end();
        const HdPrimvarSchema points =
            primvars.GetPrimvar(HdPrimvarsSchemaTokens->points);
        _points = points.GetFlattenedPrimvarValue();
        _AddSampledSource(_points);

        const HdPrimvarSchema weights = primvars.GetPrimvar(_pointWeightsToken);
        _weights = weights.GetFlattenedPrimvarValue();
        _AddSampledSource(_weights);

        for (const TfToken& name : primvarNames)
        {
            if (!_IsRetainableVertexPrimvar(primvars, name))
            {
                continue;
            }
            if (_attributes.size() == MaxRetainedAttributes)
            {
                continue;
            }
            const HdPrimvarSchema primvar = primvars.GetPrimvar(name);
            _RetainedAttribute attribute;
            attribute.name = name;
            const HdTokenDataSourceHandle role = primvar.GetRole();
            attribute.role = role ? role->GetTypedValue(0.0f) : TfToken();
            attribute.source = primvar.GetFlattenedPrimvarValue();
            attribute.firstList = _attributes.size() * ListsPerAttribute;
            if (!attribute.source)
            {
                continue;
            }
            _AddSampledSource(attribute.source);
            _attributes.push_back(std::move(attribute));
        }
        _attributeOutputs.resize(_attributes.size());

        if (_isPatch)
        {
            const HdNurbsPatchSchema schema =
                HdNurbsPatchSchema::GetFromParent(_source.dataSource);
            _AddSampledSource(schema.GetUVertexCount());
            _AddSampledSource(schema.GetVVertexCount());
            _AddSampledSource(schema.GetUOrder());
            _AddSampledSource(schema.GetVOrder());
            _AddSampledSource(schema.GetUKnots());
            _AddSampledSource(schema.GetVKnots());
            _AddSampledSource(schema.GetUForm());
            _AddSampledSource(schema.GetVForm());
            _AddSampledSource(schema.GetURange());
            _AddSampledSource(schema.GetVRange());
            _AddSampledSource(schema.GetOrientation());

            const HdNurbsPatchTrimCurveSchema trim = schema.GetTrimCurve();
            _AddSampledSource(trim.GetCounts());
            _AddSampledSource(trim.GetOrders());
            _AddSampledSource(trim.GetVertexCounts());
            _AddSampledSource(trim.GetKnots());
            _AddSampledSource(trim.GetRanges());
            _AddSampledSource(trim.GetPoints());
        }
        else
        {
            const HdNurbsCurvesSchema schema =
                HdNurbsCurvesSchema::GetFromParent(_source.dataSource);
            _AddSampledSource(schema.GetCurveVertexCounts());
            _AddSampledSource(schema.GetOrder());
            _AddSampledSource(schema.GetKnots());
            _AddSampledSource(schema.GetRanges());
        }
    }

    static bool _IsRetainableVertexPrimvar(
        const HdPrimvarsSchema& primvars,
        const TfToken& name)
    {
        if (name == HdPrimvarsSchemaTokens->points ||
            name == HdPrimvarsSchemaTokens->normals ||
            name == _pointWeightsToken ||
            name == _ptexFaceOffsetToken ||
            name == _skelJointIndicesToken ||
            name == _skelJointWeightsToken ||
            name == _adapterProvenanceToken ||
            name == HdSilkNurbsGeneratedGeometryPrimvar() ||
            name == HdSilkNurbsGeneratedGeometryPlanPrimvar())
        {
            return false;
        }
        const HdTokenDataSourceHandle interpolation =
            primvars.GetPrimvar(name).GetInterpolation();
        return interpolation &&
            interpolation->GetTypedValue(0.0f) == HdPrimvarSchemaTokens->vertex;
    }

    const std::vector<_RetainedAttribute>& GetRetainedAttributes() const
    {
        return _attributes;
    }

    bool IsForPath(const SdfPath& path) const { return _path == path; }

    bool GetContributingSampleTimesForInterval(
        HdSampledDataSource::Time start,
        HdSampledDataSource::Time end,
        std::vector<HdSampledDataSource::Time>* times)
    {
        std::vector<HdSampledDataSource::Time> combined;
        for (const HdSampledDataSourceHandle& source : _sampledSources)
        {
            std::vector<HdSampledDataSource::Time> sourceTimes;
            if (source->GetContributingSampleTimesForInterval(
                    start, end, &sourceTimes))
            {
                combined.insert(
                    combined.end(), sourceTimes.begin(), sourceTimes.end());
            }
        }
        std::sort(combined.begin(), combined.end());
        combined.erase(std::unique(combined.begin(), combined.end()), combined.end());
        *times = std::move(combined);
        return !times->empty();
    }

    VtVec3fArray GetPoints(HdSampledDataSource::Time time)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _EvaluateLocked(time);
        return _valid ? _pointsOutput : VtVec3fArray{};
    }

    VtVec3fArray GetNormals(HdSampledDataSource::Time time)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _EvaluateLocked(time);
        return _valid ? _normalsOutput : VtVec3fArray{};
    }

    VtValue GetAttribute(HdSampledDataSource::Time time, size_t index)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _EvaluateLocked(time);
        if (index >= _attributeOutputs.size())
        {
            return VtValue();
        }
        if (_valid)
        {
            return _attributeOutputs[index];
        }
        const size_t components = _attributes[index].components;
        return components == 0 ? VtValue() : _EmptyAttribute(components);
    }

    VtIntArray GetCurveVertexCounts(HdSampledDataSource::Time time)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _EvaluateLocked(time);
        return _valid ? _curveVertexCounts : VtIntArray{};
    }

    VtIntArray GetFaceVertexCounts(HdSampledDataSource::Time time)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _EvaluateLocked(time);
        return _valid ? _faceVertexCounts : VtIntArray{};
    }

    VtIntArray GetFaceVertexIndices(HdSampledDataSource::Time time)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _EvaluateLocked(time);
        return _valid ? _faceVertexIndices : VtIntArray{};
    }

    // Output sizes from counts, orders, knots, ranges, and trim presence only.
    // Control points, weights, and primvar values are not read. An empty
    // result means evaluation at the same time refuses the geometry too.
    VtUInt64Array GetPlan(HdSampledDataSource::Time time, std::string* refusal = nullptr)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        if (_hasSkelJointPrimvars)
        {
            if (refusal)
            {
                *refusal = "UsdSkel joint primvars on NURBS control points are not supported.";
            }
            return {};
        }
        std::string error;
        HdSilkNurbs::GeometryPlan plan;
        const size_t lists = _attributes.size() * ListsPerAttribute;
        bool planned = false;
        if (_isPatch)
        {
            HdSilkNurbs::PatchInput input;
            planned = _ReadPatchTopology(time, &input, &error) &&
                HdSilkNurbs::PlanPatch(input, lists, &plan, &error);
        }
        else
        {
            HdSilkNurbs::CurvesInput input;
            planned = _ReadCurvesTopology(time, &input, &error) &&
                HdSilkNurbs::PlanCurves(input, lists, &plan, &error);
        }
        if (!planned)
        {
            if (refusal)
            {
                *refusal = error;
            }
            return {};
        }
        VtUInt64Array result(4);
        result[0] = plan.points;
        result[1] = plan.elements;
        result[2] = plan.runs;
        result[3] = _attributes.size();
        return result;
    }

private:
    void _AddSampledSource(const HdSampledDataSourceHandle& source)
    {
        if (source)
        {
            _sampledSources.push_back(source);
        }
    }

    bool _ReadPoints(
        HdSampledDataSource::Time time,
        std::vector<HdSilkNurbs::Point>* points) const
    {
        if (!_points)
        {
            return false;
        }
        const VtValue value = _points->GetValue(time);
        if (value.IsHolding<VtVec3fArray>())
        {
            const VtVec3fArray& authored = value.UncheckedGet<VtVec3fArray>();
            points->reserve(authored.size());
            for (const GfVec3f& point : authored)
            {
                points->push_back({point[0], point[1], point[2]});
            }
            return true;
        }
        if (value.IsHolding<VtVec3dArray>())
        {
            const VtVec3dArray& authored = value.UncheckedGet<VtVec3dArray>();
            points->reserve(authored.size());
            for (const GfVec3d& point : authored)
            {
                points->push_back({point[0], point[1], point[2]});
            }
            return true;
        }
        return false;
    }

    bool _ReadWeights(
        HdSampledDataSource::Time time,
        std::vector<double>* weights) const
    {
        if (!_weights)
        {
            return true;
        }
        const VtValue value = _weights->GetValue(time);
        if (value.IsHolding<VtFloatArray>())
        {
            const VtFloatArray& authored = value.UncheckedGet<VtFloatArray>();
            weights->assign(authored.begin(), authored.end());
            return !weights->empty();
        }
        if (value.IsHolding<VtDoubleArray>())
        {
            const VtDoubleArray& authored = value.UncheckedGet<VtDoubleArray>();
            weights->assign(authored.begin(), authored.end());
            return !weights->empty();
        }
        return false;
    }

    // Reads every retained vertex primvar into two evaluator lists. Fills
    // components for each attribute so that a refusal can still report an
    // empty array of the authored element type.
    bool _ReadAttributes(
        HdSampledDataSource::Time time,
        size_t controlCount,
        std::vector<std::vector<HdSilkNurbs::Point>>* lists,
        std::string* error)
    {
        lists->assign(_attributes.size() * ListsPerAttribute, {});
        for (_RetainedAttribute& attribute : _attributes)
        {
            const VtValue value = attribute.source->GetValue(time);
            const size_t components = _AttributeComponents(value);
            size_t count = 0;
            if (components == 0 ||
                !_PackAnyAttribute(
                    value, components, attribute.firstList, lists, &count))
            {
                *error = "vertex primvar '" + attribute.name.GetString() +
                    "' has an unsupported value type; expected float, double, or a 2-, 3-, or 4-component vector array.";
                return false;
            }
            attribute.components = components;
            if (count != controlCount)
            {
                *error = "vertex primvar '" + attribute.name.GetString() +
                    "' has " + std::to_string(count) + " values; expected " +
                    std::to_string(controlCount) + " control points.";
                return false;
            }
            for (size_t list = _AttributeLists(components);
                 list < ListsPerAttribute;
                 ++list)
            {
                (*lists)[attribute.firstList + list].assign(
                    count, HdSilkNurbs::Point{});
            }
        }
        return true;
    }

    bool _ConvertAttributes(
        const std::vector<std::vector<HdSilkNurbs::Point>>& lists,
        std::vector<VtValue>* outputs) const
    {
        outputs->assign(_attributes.size(), VtValue());
        for (size_t index = 0; index < _attributes.size(); ++index)
        {
            const _RetainedAttribute& attribute = _attributes[index];
            if (!_UnpackAnyAttribute(
                    lists,
                    attribute.components,
                    attribute.firstList,
                    &(*outputs)[index]))
            {
                return false;
            }
        }
        return true;
    }

    template <typename T>
    static bool _HasValues(
        const typename HdTypedSampledDataSource<T>::Handle& source,
        HdSampledDataSource::Time time)
    {
        return source && !source->GetTypedValue(time).empty();
    }

    bool _HasTrimData(HdSampledDataSource::Time time) const
    {
        const HdNurbsPatchTrimCurveSchema trim =
            HdNurbsPatchSchema::GetFromParent(_source.dataSource).GetTrimCurve();
        return _HasValues<VtIntArray>(trim.GetCounts(), time) ||
            _HasValues<VtIntArray>(trim.GetOrders(), time) ||
            _HasValues<VtIntArray>(trim.GetVertexCounts(), time) ||
            _HasValues<VtDoubleArray>(trim.GetKnots(), time) ||
            _HasValues<VtVec2dArray>(trim.GetRanges(), time) ||
            _HasValues<VtVec3dArray>(trim.GetPoints(), time);
    }

    bool _ReadCurvesTopology(
        HdSampledDataSource::Time time,
        HdSilkNurbs::CurvesInput* input,
        std::string* error) const
    {
        const HdNurbsCurvesSchema schema =
            HdNurbsCurvesSchema::GetFromParent(_source.dataSource);
        const auto countsSource = schema.GetCurveVertexCounts();
        const auto orderSource = schema.GetOrder();
        const auto knotsSource = schema.GetKnots();
        if (!countsSource || !orderSource || !knotsSource)
        {
            *error = "required curve counts, orders, or knots are absent.";
            return false;
        }

        const VtIntArray counts = countsSource->GetTypedValue(time);
        const VtIntArray orders = orderSource->GetTypedValue(time);
        const VtDoubleArray knots = knotsSource->GetTypedValue(time);
        input->vertexCounts.assign(counts.begin(), counts.end());
        input->orders.assign(orders.begin(), orders.end());
        input->knots.assign(knots.begin(), knots.end());

        const auto rangesSource = schema.GetRanges();
        if (rangesSource)
        {
            const VtVec2dArray ranges = rangesSource->GetTypedValue(time);
            input->ranges.reserve(ranges.size());
            for (const GfVec2d& range : ranges)
            {
                input->ranges.push_back({range[0], range[1]});
            }
        }
        return true;
    }

    // Closed and periodic forms only describe how the authored control grid
    // and knots overlap. The authored points, orders, knots, weights, and
    // ranges already define the surface, so every form evaluates the same way.
    bool _ReadPatchTopology(
        HdSampledDataSource::Time time,
        HdSilkNurbs::PatchInput* input,
        std::string* error) const
    {
        const HdNurbsPatchSchema schema =
            HdNurbsPatchSchema::GetFromParent(_source.dataSource);
        const auto uCount = schema.GetUVertexCount();
        const auto vCount = schema.GetVVertexCount();
        const auto uOrder = schema.GetUOrder();
        const auto vOrder = schema.GetVOrder();
        const auto uKnots = schema.GetUKnots();
        const auto vKnots = schema.GetVKnots();
        if (!uCount || !vCount || !uOrder || !vOrder || !uKnots || !vKnots)
        {
            *error = "required patch counts, orders, or knots are absent.";
            return false;
        }

        input->uVertexCount = uCount->GetTypedValue(time);
        input->vVertexCount = vCount->GetTypedValue(time);
        input->uOrder = uOrder->GetTypedValue(time);
        input->vOrder = vOrder->GetTypedValue(time);
        const VtDoubleArray authoredUKnots = uKnots->GetTypedValue(time);
        const VtDoubleArray authoredVKnots = vKnots->GetTypedValue(time);
        input->uKnots.assign(authoredUKnots.begin(), authoredUKnots.end());
        input->vKnots.assign(authoredVKnots.begin(), authoredVKnots.end());

        const auto uRange = schema.GetURange();
        const auto vRange = schema.GetVRange();
        if (uRange)
        {
            const GfVec2d range = uRange->GetTypedValue(time);
            input->uRange = {range[0], range[1]};
            input->hasURange = true;
        }
        if (vRange)
        {
            const GfVec2d range = vRange->GetTypedValue(time);
            input->vRange = {range[0], range[1]};
            input->hasVRange = true;
        }
        const HdTokenDataSourceHandle orientation = schema.GetOrientation();
        input->leftHanded = orientation &&
            orientation->GetTypedValue(time) ==
                HdNurbsPatchSchemaTokens->leftHanded;
        input->hasTrimData = _HasTrimData(time);
        if (input->hasTrimData)
        {
            *error = "trimmed NURBS patches are not supported.";
            return false;
        }
        return true;
    }

    bool _EvaluateCurves(HdSampledDataSource::Time time, std::string* error)
    {
        HdSilkNurbs::CurvesInput input;
        if (!_ReadCurvesTopology(time, &input, error))
        {
            return false;
        }
        if (!_ReadPoints(time, &input.points) || !_ReadWeights(time, &input.weights))
        {
            *error = "curve points or rational weights have an unsupported type.";
            return false;
        }
        if (!_ReadAttributes(time, input.points.size(), &input.attributes, error))
        {
            return false;
        }

        HdSilkNurbs::CurvesOutput output;
        VtVec3fArray pointsOutput;
        std::vector<VtValue> attributeOutputs;
        if (!HdSilkNurbs::EvaluateCurves(input, &output, error) ||
            !_ConvertPoints(output.points, &pointsOutput) ||
            !_ConvertAttributes(output.attributes, &attributeOutputs))
        {
            if (error->empty())
            {
                *error = "curve output cannot be represented as float values.";
            }
            return false;
        }
        VtIntArray curveVertexCounts(
            output.vertexCounts.begin(), output.vertexCounts.end());
        _pointsOutput = std::move(pointsOutput);
        _curveVertexCounts = std::move(curveVertexCounts);
        _attributeOutputs = std::move(attributeOutputs);
        return true;
    }

    bool _EvaluatePatch(HdSampledDataSource::Time time, std::string* error)
    {
        HdSilkNurbs::PatchInput input;
        if (!_ReadPatchTopology(time, &input, error))
        {
            return false;
        }
        if (!_ReadPoints(time, &input.points) || !_ReadWeights(time, &input.weights))
        {
            *error = "patch points or rational weights have an unsupported type.";
            return false;
        }
        if (!_ReadAttributes(time, input.points.size(), &input.attributes, error))
        {
            return false;
        }

        HdSilkNurbs::PatchOutput output;
        VtVec3fArray pointsOutput;
        VtVec3fArray normalsOutput;
        std::vector<VtValue> attributeOutputs;
        if (!HdSilkNurbs::EvaluatePatch(input, &output, error) ||
            !_ConvertPoints(output.points, &pointsOutput) ||
            !_ConvertPoints(output.normals, &normalsOutput) ||
            !_ConvertAttributes(output.attributes, &attributeOutputs))
        {
            if (error->empty())
            {
                *error = "patch output cannot be represented as float values.";
            }
            return false;
        }
        VtIntArray faceVertexCounts(
            output.faceVertexCounts.begin(), output.faceVertexCounts.end());
        VtIntArray faceVertexIndices(
            output.faceVertexIndices.begin(), output.faceVertexIndices.end());
        _pointsOutput = std::move(pointsOutput);
        _normalsOutput = std::move(normalsOutput);
        _faceVertexCounts = std::move(faceVertexCounts);
        _faceVertexIndices = std::move(faceVertexIndices);
        _attributeOutputs = std::move(attributeOutputs);
        return true;
    }

    static bool _ConvertPoints(
        const std::vector<HdSilkNurbs::Point>& source,
        VtVec3fArray* destination)
    {
        const double maxFloat =
            static_cast<double>(std::numeric_limits<float>::max());
        VtVec3fArray converted;
        converted.reserve(source.size());
        for (const HdSilkNurbs::Point& point : source)
        {
            if (std::abs(point.x) > maxFloat || std::abs(point.y) > maxFloat ||
                std::abs(point.z) > maxFloat)
            {
                return false;
            }
            const GfVec3f value(
                static_cast<float>(point.x),
                static_cast<float>(point.y),
                static_cast<float>(point.z));
            if (!std::isfinite(value[0]) || !std::isfinite(value[1]) ||
                !std::isfinite(value[2]))
            {
                return false;
            }
            converted.push_back(value);
        }
        *destination = std::move(converted);
        return true;
    }

    void _EvaluateLocked(HdSampledDataSource::Time time)
    {
        if (_hasResult && _cachedTime == time)
        {
            return;
        }
        _hasResult = true;
        _cachedTime = time;
        _valid = false;
        _pointsOutput.clear();
        _normalsOutput.clear();
        _curveVertexCounts.clear();
        _faceVertexCounts.clear();
        _faceVertexIndices.clear();
        _attributeOutputs.assign(_attributes.size(), VtValue());

        std::string error;
        if (_hasSkelJointPrimvars)
        {
            error = "UsdSkel joint primvars on NURBS control points are not supported.";
        }
        else
        {
            _valid = _isPatch
                ? _EvaluatePatch(time, &error)
                : _EvaluateCurves(time, &error);
        }
        if (_valid)
        {
            _lastDiagnostic.clear();
        }
        else if (error != _lastDiagnostic)
        {
            TF_WARN("hdSilk skipped NURBS geometry '%s': %s",
                _path.GetText(), error.c_str());
            _lastDiagnostic = error;
        }
    }

    SdfPath _path;
    HdSceneIndexPrim _source;
    bool _isPatch;
    bool _hasSkelJointPrimvars = false;
    HdSampledDataSourceHandle _points;
    HdSampledDataSourceHandle _weights;
    std::vector<_RetainedAttribute> _attributes;
    std::vector<HdSampledDataSourceHandle> _sampledSources;
    std::mutex _mutex;
    bool _hasResult = false;
    bool _valid = false;
    std::string _lastDiagnostic;
    HdSampledDataSource::Time _cachedTime = 0.0f;
    VtVec3fArray _pointsOutput;
    VtVec3fArray _normalsOutput;
    VtIntArray _curveVertexCounts;
    VtIntArray _faceVertexCounts;
    VtIntArray _faceVertexIndices;
    std::vector<VtValue> _attributeOutputs;
};

// Exposes one retained vertex primvar. The value type follows the authored
// element type at each sample time, so the source is untyped.
class _NurbsAttributeDataSource final : public HdSampledDataSource
{
public:
    HD_DECLARE_DATASOURCE(_NurbsAttributeDataSource);

    VtValue GetValue(HdSampledDataSource::Time time) override
    {
        return _cache->GetAttribute(time, _index);
    }

    bool GetContributingSampleTimesForInterval(
        HdSampledDataSource::Time start,
        HdSampledDataSource::Time end,
        std::vector<HdSampledDataSource::Time>* times) override
    {
        return _cache->GetContributingSampleTimesForInterval(start, end, times);
    }

private:
    _NurbsAttributeDataSource(
        std::shared_ptr<_NurbsGeometryCache> cache,
        size_t index)
        : _cache(std::move(cache))
        , _index(index)
    {
    }

    std::shared_ptr<_NurbsGeometryCache> _cache;
    size_t _index;
};

template <typename T>
bool _NurbsValueDataSource<T>::GetContributingSampleTimesForInterval(
    HdSampledDataSource::Time start,
    HdSampledDataSource::Time end,
    std::vector<HdSampledDataSource::Time>* times)
{
    return _cache->GetContributingSampleTimesForInterval(start, end, times);
}

template <typename T, typename Getter>
typename HdTypedSampledDataSource<T>::Handle _NewNurbsValueSource(
    const std::shared_ptr<_NurbsGeometryCache>& cache,
    Getter getter)
{
    return std::make_shared<_NurbsValueDataSource<T>>(cache, std::move(getter));
}

HdContainerDataSourceHandle _BuildPrimvar(
    const HdSampledDataSourceHandle& value,
    const TfToken& interpolation,
    const TfToken& role = TfToken())
{
    HdPrimvarSchema::Builder builder;
    builder.SetPrimvarValue(value)
        .SetInterpolation(HdPrimvarSchema::BuildInterpolationDataSource(interpolation));
    if (!role.IsEmpty())
    {
        builder.SetRole(HdPrimvarSchema::BuildRoleDataSource(role));
    }
    return builder.Build();
}

HdContainerDataSourceHandle _BuildNurbsPrimvars(
    const SdfPath& path,
    const HdContainerDataSourceHandle& source,
    const std::shared_ptr<_NurbsGeometryCache>& cache,
    bool isPatch)
{
    const HdSampledDataSourceHandle points =
        _NewNurbsValueSource<VtVec3fArray>(
            cache,
            [cache](HdSampledDataSource::Time time) {
                return cache->GetPoints(time);
            });
    const HdSampledDataSourceHandle marker =
        HdRetainedTypedSampledDataSource<bool>::New(true);
    std::vector<TfToken> names;
    std::vector<HdDataSourceBaseHandle> values;
    names.push_back(HdPrimvarsSchemaTokens->points);
    values.push_back(_BuildPrimvar(
        points,
        HdPrimvarSchemaTokens->vertex,
        HdPrimvarSchemaTokens->point));
    if (!isPatch)
    {
        names.push_back(HdSilkNurbsGeneratedGeometryPrimvar());
        values.push_back(_BuildPrimvar(marker, HdPrimvarSchemaTokens->constant));
    }
    else
    {
        const HdSampledDataSourceHandle normals =
            _NewNurbsValueSource<VtVec3fArray>(
                cache,
                [cache](HdSampledDataSource::Time time) {
                    return cache->GetNormals(time);
                });
        names.push_back(HdPrimvarsSchemaTokens->normals);
        values.push_back(_BuildPrimvar(
            normals,
            HdPrimvarSchemaTokens->vertex,
            HdPrimvarSchemaTokens->normal));
        names.push_back(HdSilkNurbsGeneratedGeometryPrimvar());
        values.push_back(_BuildPrimvar(marker, HdPrimvarSchemaTokens->constant));
    }
    const HdSampledDataSourceHandle plan =
        _NewNurbsValueSource<VtUInt64Array>(
            cache,
            [cache](HdSampledDataSource::Time time) {
                return cache->GetPlan(time);
            });
    names.push_back(HdSilkNurbsGeneratedGeometryPlanPrimvar());
    values.push_back(_BuildPrimvar(plan, HdPrimvarSchemaTokens->constant));
    // USD values cannot author this translation-unit-private cache type.
    // Keep ownership as well as identity across the scene-delegate boundary.
    names.push_back(_adapterProvenanceToken);
    values.push_back(_BuildPrimvar(
        HdRetainedTypedSampledDataSource<std::shared_ptr<_NurbsGeometryCache>>::New(cache),
        HdPrimvarSchemaTokens->constant));

    const std::vector<_RetainedAttribute>& attributes =
        cache->GetRetainedAttributes();
    for (size_t index = 0; index < attributes.size(); ++index)
    {
        names.push_back(attributes[index].name);
        values.push_back(_BuildPrimvar(
            _NurbsAttributeDataSource::New(cache, index),
            HdPrimvarSchemaTokens->vertex,
            attributes[index].role));
    }

    const HdPrimvarsSchema sourcePrimvars =
        HdPrimvarsSchema::GetFromParent(source);
    for (const TfToken& name : sourcePrimvars.GetPrimvarNames())
    {
        if (name == HdPrimvarsSchemaTokens->points ||
            name == _pointWeightsToken ||
            name == HdSilkNurbsGeneratedGeometryPrimvar() ||
            name == HdSilkNurbsGeneratedGeometryPlanPrimvar() ||
            name == _adapterProvenanceToken ||
            name == _skelJointIndicesToken ||
            name == _skelJointWeightsToken ||
            std::any_of(attributes.begin(), attributes.end(),
                [&name](const _RetainedAttribute& attribute) {
                    return attribute.name == name;
                }))
        {
            continue;
        }
        const HdPrimvarSchema primvar = sourcePrimvars.GetPrimvar(name);
        const HdTokenDataSourceHandle interpolation = primvar.GetInterpolation();
        const HdDataSourceBaseHandle primvarDataSource =
            sourcePrimvars.GetContainer()->Get(name);
        if (!interpolation || !primvarDataSource)
        {
            continue;
        }
        const TfToken interpolationValue = interpolation->GetTypedValue(0.0f);
        if (interpolationValue == HdPrimvarSchemaTokens->constant)
        {
            if (name != HdPrimvarsSchemaTokens->normals &&
                name != _ptexFaceOffsetToken)
            {
                names.push_back(name);
                values.push_back(primvarDataSource);
            }
            continue;
        }
        if (name == HdPrimvarsSchemaTokens->normals)
        {
            TF_WARN(
                "hdSilk discarded authored NURBS normals on '%s' with '%s' interpolation; generated patches use evaluated surface normals and generated curves carry no normals.",
                path.GetText(),
                interpolationValue.GetText());
            continue;
        }
        if (interpolationValue == HdPrimvarSchemaTokens->vertex &&
            name != _ptexFaceOffsetToken)
        {
            TF_WARN(
                "hdSilk discarded authored NURBS vertex primvar '%s' on '%s'; at most %zu vertex primvars are interpolated.",
                name.GetText(),
                path.GetText(),
                _NurbsGeometryCache::MaxRetainedAttributes);
            continue;
        }
        TF_WARN(
            "hdSilk discarded authored NURBS primvar '%s' on '%s' with '%s' interpolation; only constant and vertex primvars are preserved.",
            name.GetText(),
            path.GetText(),
            interpolationValue.GetText());
    }
    return HdRetainedContainerDataSource::New(
        names.size(), names.data(), values.data());
}

HdContainerDataSourceHandle _ReplacePrimvars(
    const HdContainerDataSourceHandle& source,
    const HdContainerDataSourceHandle& primvars)
{
    std::vector<TfToken> names;
    std::vector<HdDataSourceBaseHandle> values;
    bool replaced = false;
    if (source)
    {
        for (const TfToken& name : source->GetNames())
        {
            names.push_back(name);
            if (name == HdPrimvarsSchemaTokens->primvars)
            {
                values.push_back(primvars);
                replaced = true;
            }
            else
            {
                values.push_back(source->Get(name));
            }
        }
    }
    if (!replaced)
    {
        names.push_back(HdPrimvarsSchemaTokens->primvars);
        values.push_back(primvars);
    }
    return HdRetainedContainerDataSource::New(
        names.size(), names.data(), values.data());
}

HdSceneIndexPrim _ConvertCurves(
    const SdfPath& path,
    const HdSceneIndexPrim& source)
{
    auto cache = std::make_shared<_NurbsGeometryCache>(path, source, false);
    const HdSampledDataSourceHandle counts =
        _NewNurbsValueSource<VtIntArray>(
            cache,
            [cache](HdSampledDataSource::Time time) {
                return cache->GetCurveVertexCounts(time);
            });
    const HdContainerDataSourceHandle topology =
        HdBasisCurvesTopologySchema::Builder()
            .SetCurveVertexCounts(HdIntArrayDataSource::Cast(counts))
            .SetBasis(HdRetainedTypedSampledDataSource<TfToken>::New(
                HdTokens->bezier))
            .SetType(HdRetainedTypedSampledDataSource<TfToken>::New(
                HdTokens->linear))
            .SetWrap(HdRetainedTypedSampledDataSource<TfToken>::New(
                HdTokens->nonperiodic))
            .Build();
    const HdContainerDataSourceHandle sanitized =
        _ReplacePrimvars(source.dataSource, _BuildNurbsPrimvars(
            path, source.dataSource, cache, false));
    const HdContainerDataSourceHandle additions =
        HdRetainedContainerDataSource::New(
            HdBasisCurvesSchemaTokens->basisCurves,
            HdBasisCurvesSchema::Builder().SetTopology(topology).Build());
    return {
        HdPrimTypeTokens->basisCurves,
        HdOverlayContainerDataSource::OverlayedContainerDataSources(
            additions, sanitized)};
}

HdSceneIndexPrim _ConvertPatch(
    const SdfPath& path,
    const HdSceneIndexPrim& source)
{
    auto cache = std::make_shared<_NurbsGeometryCache>(path, source, true);
    const HdSampledDataSourceHandle faceCounts =
        _NewNurbsValueSource<VtIntArray>(
            cache,
            [cache](HdSampledDataSource::Time time) {
                return cache->GetFaceVertexCounts(time);
            });
    const HdSampledDataSourceHandle faceIndices =
        _NewNurbsValueSource<VtIntArray>(
            cache,
            [cache](HdSampledDataSource::Time time) {
                return cache->GetFaceVertexIndices(time);
            });
    const HdNurbsPatchSchema nurbs =
        HdNurbsPatchSchema::GetFromParent(source.dataSource);
    HdTokenDataSourceHandle orientation = nurbs.GetOrientation();
    if (!orientation)
    {
        orientation = HdRetainedTypedSampledDataSource<TfToken>::New(
            HdMeshTopologySchemaTokens->rightHanded);
    }
    HdContainerDataSourceHandle topology =
        HdMeshTopologySchema::Builder()
            .SetFaceVertexCounts(HdIntArrayDataSource::Cast(faceCounts))
            .SetFaceVertexIndices(HdIntArrayDataSource::Cast(faceIndices))
            .SetOrientation(orientation)
            .Build();
    HdContainerDataSourceHandle mesh =
        HdMeshSchema::Builder()
            .SetTopology(topology)
            .SetSubdivisionScheme(HdRetainedTypedSampledDataSource<TfToken>::New(
                PxOsdOpenSubdivTokens->none))
            .SetDoubleSided(nurbs.GetDoubleSided())
            .Build();
    const HdContainerDataSourceHandle sanitized =
        _ReplacePrimvars(source.dataSource, _BuildNurbsPrimvars(
            path, source.dataSource, cache, true));
    const HdContainerDataSourceHandle additions =
        HdRetainedContainerDataSource::New(
            HdMeshSchemaTokens->mesh,
            mesh);
    return {
        HdPrimTypeTokens->mesh,
        HdOverlayContainerDataSource::OverlayedContainerDataSources(
            additions, sanitized)};
}

class _HdSilkNurbsSceneIndex final : public HdSingleInputFilteringSceneIndexBase
{
public:
    using Handle = TfRefPtr<_HdSilkNurbsSceneIndex>;

    static Handle New(const HdSceneIndexBaseRefPtr& input)
    {
        return TfCreateRefPtr(new _HdSilkNurbsSceneIndex(input));
    }

    HdSceneIndexPrim GetPrim(const SdfPath& path) const override
    {
        const HdSceneIndexPrim source = _GetInputSceneIndex()->GetPrim(path);
        const _NurbsKind kind = _GetNurbsKind(source);
        if (kind == _NurbsKind::Curves)
        {
            return _ConvertCurves(path, source);
        }
        if (kind == _NurbsKind::Patch)
        {
            return _ConvertPatch(path, source);
        }
        return source;
    }

    SdfPathVector GetChildPrimPaths(const SdfPath& path) const override
    {
        return _GetInputSceneIndex()->GetChildPrimPaths(path);
    }

public:
    explicit _HdSilkNurbsSceneIndex(const HdSceneIndexBaseRefPtr& input)
        : HdSingleInputFilteringSceneIndexBase(input)
    {
    }

    void _PrimsAdded(
        const HdSceneIndexBase& sender,
        const HdSceneIndexObserver::AddedPrimEntries& entries) override
    {
        HdSceneIndexObserver::AddedPrimEntries converted;
        converted.reserve(entries.size());
        for (const auto& entry : entries)
        {
            TfToken type = entry.primType;
            const HdSceneIndexPrim source = sender.GetPrim(entry.primPath);
            const _NurbsKind kind = _GetNurbsKind(source);
            if (kind == _NurbsKind::Curves)
            {
                type = HdPrimTypeTokens->basisCurves;
            }
            else if (kind == _NurbsKind::Patch)
            {
                type = HdPrimTypeTokens->mesh;
            }
            converted.push_back({entry.primPath, type});
        }
        _SendPrimsAdded(converted);
    }

    void _PrimsRemoved(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::RemovedPrimEntries& entries) override
    {
        _SendPrimsRemoved(entries);
    }

    void _PrimsDirtied(
        const HdSceneIndexBase& sender,
        const HdSceneIndexObserver::DirtiedPrimEntries& entries) override
    {
        HdSceneIndexObserver::DirtiedPrimEntries converted;
        converted.reserve(entries.size());
        for (const auto& entry : entries)
        {
            const _NurbsKind kind = _GetNurbsKind(sender.GetPrim(entry.primPath));
            const bool isNurbs = kind != _NurbsKind::None;
            converted.push_back({
                entry.primPath,
                isNurbs
                    ? HdDataSourceLocatorSet::UniversalSet()
                    : entry.dirtyLocators});
        }
        _SendPrimsDirtied(converted);
    }
};
} // namespace

const TfToken& HdSilkNurbsGeneratedGeometryPrimvar()
{
    static const TfToken token("silkNurbsGeneratedGeometry");
    return token;
}

namespace
{
std::shared_ptr<_NurbsGeometryCache> _GetNurbsProvenance(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& primPath)
{
    for (const HdPrimvarDescriptor& primvar :
        sceneDelegate->GetPrimvarDescriptors(primPath, HdInterpolationConstant))
    {
        if (primvar.name == _adapterProvenanceToken)
        {
            const VtValue value = sceneDelegate->Get(primPath, _adapterProvenanceToken);
            if (value.IsHolding<std::shared_ptr<_NurbsGeometryCache>>())
            {
                const auto cache = value.UncheckedGet<std::shared_ptr<_NurbsGeometryCache>>();
                if (cache && cache->IsForPath(primPath))
                {
                    return cache;
                }
            }
        }
    }
    return {};
}
}

bool HdSilkHasNurbsGeneratedGeometry(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& primPath)
{
    return static_cast<bool>(_GetNurbsProvenance(sceneDelegate, primPath));
}

const TfToken& HdSilkNurbsGeneratedGeometryPlanPrimvar()
{
    static const TfToken token("silkNurbsGeneratedGeometryPlan");
    return token;
}

bool HdSilkGetNurbsGeneratedGeometryPlan(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& primPath,
    HdSilkNurbsGeneratedGeometryPlan* plan)
{
    *plan = {};
    const auto cache = _GetNurbsProvenance(sceneDelegate, primPath);
    if (!cache)
    {
        return false;
    }
    std::string refusal;
    const VtUInt64Array expected = cache->GetPlan(0.0f, &refusal);
    const VtValue value =
        sceneDelegate->Get(primPath, HdSilkNurbsGeneratedGeometryPlanPrimvar());
    if (!value.IsHolding<VtUInt64Array>() ||
        value.UncheckedGet<VtUInt64Array>() != expected)
    {
        throw std::invalid_argument("Generated NURBS preparation metadata is missing or malformed.");
    }
    if (expected.empty())
    {
        throw std::invalid_argument(
            "Generated NURBS topology was refused before preparation: " + refusal);
    }
    if (expected.size() != 4 || expected[0] > HdSilkNurbs::MaxTessellatedPoints ||
        expected[3] > _NurbsGeometryCache::MaxRetainedAttributes)
    {
        throw std::invalid_argument("Generated NURBS preparation counts are invalid.");
    }
    plan->points = expected[0];
    plan->elements = expected[1];
    plan->runs = expected[2];
    plan->vertexPrimvars = expected[3];
    return true;
}

TF_REGISTRY_FUNCTION(TfType)
{
    HdSceneIndexPluginRegistry::Define<HdSilk_NurbsSceneIndexPlugin>();
}

TF_REGISTRY_FUNCTION(HdSceneIndexPlugin)
{
    HdSceneIndexPluginRegistry::GetInstance().RegisterSceneIndexForRenderer(
        _pluginDisplayName,
        _hdSilkNurbsTokens->sceneIndexPluginName,
        nullptr,
        0,
        HdSceneIndexPluginRegistry::InsertionOrderAtStart);
}

HdSilk_NurbsSceneIndexPlugin::HdSilk_NurbsSceneIndexPlugin() = default;

HdSceneIndexBaseRefPtr HdSilk_NurbsSceneIndexPlugin::_AppendSceneIndex(
    const HdSceneIndexBaseRefPtr& inputScene,
    const HdContainerDataSourceHandle&)
{
    return _HdSilkNurbsSceneIndex::New(inputScene);
}

PXR_NAMESPACE_CLOSE_SCOPE
