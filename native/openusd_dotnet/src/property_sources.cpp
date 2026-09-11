// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/property_inspection.h"
#include "internal/property_composition.h"
#include "internal/layer_edit.h"

#include "pxr/base/vt/valueComposeOver.h"
#include "pxr/usd/pcp/cache.h"
#include "pxr/usd/pcp/propertyIndex.h"
#include "pxr/usd/pcp/targetIndex.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/usdaData.h"
#include "pxr/usd/usd/primDefinition.h"
#include "pxr/usd/usd/resolver.h"

#include <set>
#include <unordered_map>

namespace OpenUsdProperties
{
namespace
{
class LayerDataAccess : public SdfFileFormat
{
public:
    LayerDataAccess() = delete;
    static SdfAbstractDataConstPtr Get(const SdfLayer& layer) { return _GetLayerData(layer); }
};

enum class StoreKind { Resident, Crate, Deferred };

StoreKind Kind(const SdfAbstractDataConstPtr& data)
{
    if (!data) return StoreKind::Deferred;
    const auto& type = OpenUsdEdit::DataAccess::ConcreteType(data);
    if (type == typeid(SdfData) || type == typeid(SdfUsdaData) ||
        type == typeid(OpenUsdEdit::CountedData)) return StoreKind::Resident;
    static const SdfAbstractDataRefPtr crate = []
    {
        const auto format = SdfFileFormat::FindById(TfToken("usdc"));
        return format ? format->InitData({}) : SdfAbstractDataRefPtr();
    }();
    return crate && type == OpenUsdEdit::DataAccess::ConcreteType(crate) ? StoreKind::Crate : StoreKind::Deferred;
}

struct Store
{
    SdfAbstractDataConstPtr data;
    StoreKind kind;
};

struct TokenLess
{
    bool operator()(const TfToken& left, const TfToken& right) const
    {
        return left.GetString() < right.GetString();
    }
};

SdfLayerOffset LayerToStage(const PcpNodeRef& node, const SdfLayerRefPtr& layer)
{
    auto offset = node.GetMapToRoot().GetTimeOffset();
    if (const auto* local = node.GetLayerStack()->GetLayerOffsetForLayer(layer)) offset = offset * *local;
    return offset;
}

class Reader
{
public:
    Reader(const UsdPrim& prim, UsdTimeCode time, openusd_property_snapshot& result, Budget& budget)
        : _prim(prim), _time(time), _result(result), _budget(budget) {}

    void Run()
    {
        AdmitComposition();
        std::set<TfToken, TokenLess> names;
        auto addNames = [&](const TfTokenVector& source)
        {
            _budget.Work(source.size());
            for (const auto& name : source)
            {
                _budget.Text(name.GetString().size() + 1);
                if (names.find(name) == names.end())
                {
                    Budget::Check("property count", names.size() + 1, _budget.limits.maximum_property_count);
                    names.insert(name);
                }
            }
        };
        addNames(_prim.GetPrimDefinition().GetPropertyNames());
        for (const auto& node : _prim.GetPrimIndex().GetNodeRange())
        {
            _budget.Work();
            if (node.IsCulled() || !node.CanContributeSpecs()) continue;
            for (const auto& layer : node.GetLayerStack()->GetLayers())
            {
                _budget.Work();
                ++_siteCount;
                const auto& store = Data(layer);
                if (store.kind == StoreKind::Deferred)
                {
                    throw std::invalid_argument("Property names are deferred by an unrecognized backing store.");
                }
                VtValue value;
                const auto& field = SdfChildrenKeys->PropertyChildren;
                if (store.data->GetTypeid(node.GetPath(), field) == typeid(TfTokenVector) &&
                    store.data->Has(node.GetPath(), field, &value))
                {
                    // Pinned crate token vectors and USDA SdfData VtValues are
                    // resident and shared here, before any token-vector copy.
                    addNames(value.UncheckedGet<TfTokenVector>());
                }
                _budget.Text(layer->GetIdentifier().size() + 1);
                std::string admitted;
                SourcePath(node.GetPath(), node, &admitted);
                for (SdfPath ancestor = node.GetPath(); !ancestor.IsEmpty(); ancestor = ancestor.GetParentPath())
                {
                    _budget.Work();
                    if (store.data->Has(ancestor, TfToken("clips"), static_cast<VtValue*>(nullptr))) _clips = true;
                }
            }
        }
        for (const auto& name : names) Read(name);
    }

private:
    void AdmitComposition()
    {
        for (UsdPrim ancestor = _prim; ancestor && !ancestor.IsPseudoRoot(); ancestor = ancestor.GetParent())
        {
            _budget.Work();
            const auto& index = ancestor.GetPrimIndex();
            if (!index.IsValid() || HasStoredCompositionErrors(index))
            {
                throw std::invalid_argument(
                    "Property inventory is unavailable because the prim has stored composition errors.");
            }
            for (const auto& node : index.GetNodeRange())
            {
                _budget.Work();
                if (!node)
                    throw std::invalid_argument("Property inventory has an invalid composition node.");
                if (node.IsCulled() || !node.CanContributeSpecs()) continue;
                const auto& stack = node.GetLayerStack();
                if (!stack || HasStoredCompositionErrors(*stack))
                {
                    throw std::invalid_argument(
                        "Property inventory is unavailable because a layer stack has stored composition errors.");
                }
            }
        }
    }

    const Store& Data(const SdfLayerRefPtr& layer)
    {
        const auto found = _stores.find(layer.operator->());
        if (found != _stores.end()) return found->second;
        _budget.Work();
        auto data = LayerDataAccess::Get(*layer);
        const auto kind = Kind(data);
        return _stores.emplace(layer.operator->(), Store{std::move(data), kind}).first->second;
    }

    TfToken VariantName(const SdfPath& prefix, const PcpNodeRef& node)
    {
        const auto parent = prefix.GetParentPath();
        const auto& selection = prefix.GetNameToken().GetString();
        for (const auto& layer : node.GetLayerStack()->GetLayers())
        {
            _budget.Work();
            const auto& store = Data(layer);
            const auto& field = SdfChildrenKeys->VariantSetChildren;
            if (store.data->GetTypeid(parent, field) != typeid(TfTokenVector)) continue;
            VtValue value;
            if (!store.data->Has(parent, field, &value)) continue;
            const auto& names = value.UncheckedGet<TfTokenVector>();
            _budget.Work(names.size());
            TfToken found;
            for (const auto& name : names)
            {
                _budget.Text(name.GetString().size() + 1);
                if (found.IsEmpty() &&
                    (parent.AppendVariantSelection(name.GetString(), selection) == prefix ||
                     parent.AppendVariantSelection(name.GetString(), std::string()) == prefix))
                    found = name;
            }
            if (!found.IsEmpty()) return found;
        }
        throw std::invalid_argument("Property source variant names cannot be admitted from resident metadata.");
    }

    bool SourcePath(const SdfPath& path, const PcpNodeRef& node, std::string* text)
    {
        size_t length = 1;
        for (const auto& prefix : path.GetAncestorsRange())
        {
            _budget.Work();
            if (!prefix.IsPrimPath() && !prefix.IsPrimPropertyPath() && !prefix.IsPrimVariantSelectionPath())
                return false;
            length += prefix.GetNameToken().GetString().size() + 1;
            Budget::Check("text bytes", _budget.text + length, _budget.limits.maximum_text_bytes);
            if (prefix.IsPrimVariantSelectionPath())
            {
                const auto set = VariantName(prefix, node);
                length += set.GetString().size() + 2;
                Budget::Check("text bytes", _budget.text + length, _budget.limits.maximum_text_bytes);
            }
        }
        _budget.Text(length);
        *text = path.GetString();
        return true;
    }

    uint32_t Domain(const TfToken& name)
    {
        for (Usd_Resolver resolver(&_prim.GetPrimIndex()); resolver.IsValid(); resolver.NextLayer())
        {
            _budget.Work();
            const auto& store = Data(resolver.GetLayer());
            const auto path = resolver.GetLocalPath(name);
            if (store.kind == StoreKind::Deferred) return OPENUSD_PROPERTY_REASON_DEFERRED_STORE;
            if (store.data->Has(path, SdfFieldKeys->Spline, static_cast<VtValue*>(nullptr)))
                return OPENUSD_PROPERTY_REASON_SPLINE;
            if (VtValueTypeCanComposeOver(store.data->GetTypeid(path, SdfFieldKeys->Default)))
                return OPENUSD_PROPERTY_REASON_ARRAY_EDIT;
            if (_time.IsNumeric() && store.data->GetNumTimeSamplesForPath(path) != 0)
            {
                double lower = 0, upper = 0;
                const auto local = LayerToStage(resolver.GetNode(), resolver.GetLayer()).GetInverse() * _time.GetValue();
                if (store.data->GetBracketingTimeSamplesForPath(path, local, &lower, &upper) &&
                    (VtValueTypeCanComposeOver(store.data->QueryTimeSampleTypeid(path, lower)) ||
                     VtValueTypeCanComposeOver(store.data->QueryTimeSampleTypeid(path, upper))))
                    return OPENUSD_PROPERTY_REASON_ARRAY_EDIT;
            }
        }
        return OPENUSD_PROPERTY_REASON_NONE;
    }

    void Source(const UsdResolveInfo& info, const TfToken& name,
        SdfLayerRefPtr* layer, PcpNodeRef* node, SdfPath* path,
        std::string* layerText, std::string* pathText)
    {
        if (info.GetSource() != UsdResolveInfoSourceDefault &&
            info.GetSource() != UsdResolveInfoSourceTimeSamples) return;
        Usd_Resolver resolver(&_prim.GetPrimIndex(), true, &info);
        if (!resolver.IsValid() || resolver.GetNode() != info.GetNode())
            throw std::runtime_error("Native property resolve source could not be located.");
        *layer = resolver.GetLayer();
        *node = resolver.GetNode();
        *path = resolver.GetLocalPath(name);
        if (SourcePath(*path, *node, pathText))
        {
            _budget.Text((*layer)->GetIdentifier().size() + 1);
            *layerText = (*layer)->GetIdentifier();
        }
    }

    void Samples(const UsdResolveInfo& info, const TfToken& name, openusd_property_entry& entry)
    {
        entry.time_samples.offset = static_cast<uint32_t>(_result.times.size());
        if (info.GetSource() != UsdResolveInfoSourceTimeSamples) return;
        Usd_Resolver resolver(&_prim.GetPrimIndex(), true, &info);
        const auto& store = Data(resolver.GetLayer());
        const auto path = resolver.GetLocalPath(name);
        const auto offset = LayerToStage(resolver.GetNode(), resolver.GetLayer());
        const size_t count = store.data->GetNumTimeSamplesForPath(path);
        if ((entry.flags & 4) != 0)
        {
            if (store.kind != StoreKind::Resident)
            {
                Unavailable(entry.time_samples, OPENUSD_PROPERTY_REASON_DEFERRED_STORE);
                return;
            }
            if (count > _budget.limits.maximum_metadata_work - _budget.work)
            {
                Unavailable(entry.time_samples, OPENUSD_PROPERTY_REASON_COMPOSITION);
                return;
            }
            VtValue value;
            if (!store.data->Has(path, SdfFieldKeys->TimeSamples, &value) ||
                !value.IsHolding<SdfTimeSampleMap>())
            {
                Unavailable(entry.time_samples, OPENUSD_PROPERTY_REASON_COMPOSITION);
                return;
            }
            const auto& samples = value.UncheckedGet<SdfTimeSampleMap>();
            _budget.Work(samples.size());
            for (const auto& sample : samples)
            {
                if (sample.second.CanComposeOver())
                {
                    Unavailable(entry.time_samples, OPENUSD_PROPERTY_REASON_ARRAY_EDIT);
                    return;
                }
            }
        }
        entry.time_samples.total_count = count;
        const size_t previewCount = std::min(count, static_cast<size_t>(_budget.limits.time_sample_preview));
        const bool forward = offset.GetScale() >= 0;
        double query = forward ? -std::numeric_limits<double>::max() : std::numeric_limits<double>::max();
        for (size_t index = 0; index < previewCount; ++index)
        {
            _budget.Work();
            double lower = 0, upper = 0;
            if (!store.data->GetBracketingTimeSamplesForPath(path, query, &lower, &upper))
                throw std::runtime_error("Native property sample count and brackets disagree.");
            const double sample = index == 0 ? (forward ? lower : upper) : (forward ? upper : lower);
            const double stageTime = offset * sample;
            if (!std::isfinite(stageTime)) throw std::invalid_argument("Property sample time is not finite.");
            _result.times.push_back(stageTime);
            query = std::nextafter(sample, forward ?
                std::numeric_limits<double>::infinity() : -std::numeric_limits<double>::infinity());
        }
        entry.time_samples.count = static_cast<uint32_t>(previewCount);
        if (count > previewCount)
        {
            entry.time_samples.status = OPENUSD_PROPERTY_TRUNCATED;
            entry.time_samples.reason = OPENUSD_PROPERTY_REASON_PREVIEW_LIMIT;
        }
    }

    bool Raw(const UsdResolveInfo& info, const UsdAttribute& attribute,
        const SdfLayerRefPtr& layer, const PcpNodeRef& node, const SdfPath& path,
        VtValue* value, uint32_t* reason)
    {
        if (info.GetSource() == UsdResolveInfoSourceFallback)
            return _prim.GetPrimDefinition().GetAttributeFallbackValue(attribute.GetName(), value);
        if (!layer) return false;
        const auto& store = Data(layer);
        if (info.GetSource() == UsdResolveInfoSourceDefault)
        {
            const auto& type = store.data->GetTypeid(path, SdfFieldKeys->Default);
            if (store.kind == StoreKind::Crate && !FixedValueType(type) &&
                type != typeid(std::string) && type != typeid(SdfAssetPath))
            {
                *reason = OPENUSD_PROPERTY_REASON_DEFERRED_STORE;
                return false;
            }
            const bool read = store.data->Has(path, SdfFieldKeys->Default, value);
            return read && type == typeid(SdfTimeCode) ? attribute.Get(value, _time) : read;
        }
        double lower = 0, upper = 0;
        const auto local = LayerToStage(node, layer).GetInverse() * _time.GetValue();
        if (!store.data->GetBracketingTimeSamplesForPath(path, local, &lower, &upper)) return false;
        const auto& type = store.data->QueryTimeSampleTypeid(path, lower);
        if (store.kind == StoreKind::Crate && !FixedValueType(type))
        {
            *reason = OPENUSD_PROPERTY_REASON_DEFERRED_STORE;
            return false;
        }
        if (!store.data->QueryTimeSample(path, lower, value)) return false;
        if (value->IsArrayValued() && lower != upper && local > lower && local < upper)
        {
            *reason = OPENUSD_PROPERTY_REASON_COMPOSITION;
            return false;
        }
        if (FixedValueType(type) && type != typeid(TfToken) && type != typeid(SdfValueBlock))
        {
            // The admitted native scalar interpolation has constant-size output.
            return attribute.Get(value, _time);
        }
        return true;
    }

    void Targets(const UsdProperty& property, bool relationship, openusd_property_entry& entry)
    {
        entry.targets.offset = static_cast<uint32_t>(_result.offsets.size());
        const auto& field = relationship ? SdfFieldKeys->TargetPaths : SdfFieldKeys->ConnectionPaths;
        for (Usd_Resolver resolver(&_prim.GetPrimIndex()); resolver.IsValid(); resolver.NextLayer())
        {
            _budget.Work();
            const auto& store = Data(resolver.GetLayer());
            const auto path = resolver.GetLocalPath(property.GetName());
            if (!store.data->Has(path, field, static_cast<VtValue*>(nullptr))) continue;
            if (store.kind != StoreKind::Resident)
            {
                Unavailable(entry.targets, OPENUSD_PROPERTY_REASON_DEFERRED_STORE);
                return;
            }
            VtValue value;
            store.data->Has(path, field, &value);
            if (!value.IsHolding<SdfPathListOp>())
                throw std::invalid_argument("Property targets are not a native path list operation.");
            const auto& operation = value.UncheckedGet<SdfPathListOp>();
            for (const auto* paths : {&operation.GetExplicitItems(), &operation.GetAddedItems(),
                &operation.GetPrependedItems(), &operation.GetAppendedItems(),
                &operation.GetDeletedItems(), &operation.GetOrderedItems()})
            {
                _budget.Work(paths->size());
                for (const auto& target : *paths)
                {
                    std::string admitted;
                    if (!PathText(target, _budget, &admitted))
                    {
                        Unavailable(entry.targets, OPENUSD_PROPERTY_REASON_COMPOSITION);
                        return;
                    }
                }
            }
        }
        if (!_targetContext)
        {
            _targetContext = std::make_unique<PcpCache>(
                _prim.GetPrimIndex().GetRootNode().GetLayerStack()->GetIdentifier(), std::string(), true);
        }
        PcpPropertyIndex propertyIndex;
        PcpTargetIndex targetIndex;
        PcpErrorVector errors;
        const auto propertyPath = property.GetPath();
        PcpBuildPrimPropertyIndex(propertyPath, *_targetContext, _prim.GetPrimIndex(), &propertyIndex, &errors);
        if (errors.empty())
        {
            PcpBuildTargetIndex(PcpSite(_targetContext->GetLayerStackIdentifier(), propertyPath), propertyIndex,
                relationship ? SdfSpecTypeRelationship : SdfSpecTypeAttribute, &targetIndex, &errors);
        }
        if (!errors.empty())
        {
            // Usd's convenience getters only issue warnings and may return a
            // partial/empty list on mapping errors. Preserve explicit deferral.
            Unavailable(entry.targets, OPENUSD_PROPERTY_REASON_COMPOSITION);
            return;
        }
        SdfPathVector paths;
        const bool read = relationship ? property.As<UsdRelationship>().GetTargets(&paths) :
            property.As<UsdAttribute>().GetConnections(&paths);
        if (read != targetIndex.hasTargetOpinions)
            throw std::runtime_error("Native property target composition changed after admission.");
        entry.targets.total_count = paths.size();
        const size_t count = std::min(paths.size(), static_cast<size_t>(_budget.limits.target_preview));
        for (size_t index = 0; index < count; ++index)
        {
            std::string text;
            if (!PathText(paths[index], _budget, &text))
                throw std::runtime_error("Native composed target path is outside the admitted domain.");
            Append(_result, _budget, text);
        }
        entry.targets.count = static_cast<uint32_t>(count);
        if (count < paths.size())
        {
            entry.targets.status = OPENUSD_PROPERTY_TRUNCATED;
            entry.targets.reason = OPENUSD_PROPERTY_REASON_PREVIEW_LIMIT;
        }
    }

    void TargetSource(const UsdProperty& property, bool relationship,
        std::string* layerText, std::string* pathText)
    {
        const auto& field = relationship ? SdfFieldKeys->TargetPaths : SdfFieldKeys->ConnectionPaths;
        for (Usd_Resolver resolver(&_prim.GetPrimIndex()); resolver.IsValid(); resolver.NextLayer())
        {
            _budget.Work();
            const auto& store = Data(resolver.GetLayer());
            const auto path = resolver.GetLocalPath(property.GetName());
            if (!store.data->Has(path, field, static_cast<VtValue*>(nullptr))) continue;
            if (store.kind != StoreKind::Resident) return;
            VtValue value;
            store.data->Has(path, field, &value);
            if (value.IsHolding<SdfPathListOp>() && value.UncheckedGet<SdfPathListOp>().IsExplicit() &&
                SourcePath(path, resolver.GetNode(), pathText))
            {
                _budget.Text(resolver.GetLayer()->GetIdentifier().size() + 1);
                *layerText = resolver.GetLayer()->GetIdentifier();
            }
            // Only an unopposed strongest explicit list has one winning source.
            // Do not label a declaration or one contributing edit as the source.
            return;
        }
    }

    void Read(const TfToken& name)
    {
        // Account for the bounded native metadata/value resolver passes before
        // invoking them; source node/layer storage itself is stage-resident.
        _budget.Work(1 + _siteCount * 8);
        const UsdProperty property = _prim.GetProperty(name);
        if (!property) throw std::invalid_argument("A composed property declaration is invalid.");
        const UsdAttribute attribute = property.As<UsdAttribute>();
        openusd_property_entry entry{};
        entry.time_samples.offset = static_cast<uint32_t>(_result.times.size());
        entry.kind = attribute ? 0u : 1u;
        entry.flags = (property.IsCustom() ? 1u : 0u) | (property.IsAuthored() ? 2u : 0u);
        std::string type, sourceLayer, sourcePath;
        SdfLayerRefPtr layer;
        PcpNodeRef node;
        SdfPath path;
        VtValue value;
        uint32_t reason = OPENUSD_PROPERTY_REASON_NONE;
        if (attribute)
        {
            const auto valueType = attribute.GetTypeName();
            _budget.Text(valueType.GetAsToken().GetString().size() + 1);
            type = valueType.GetAsToken().GetString();
            if (valueType.IsArray()) entry.flags |= 4u;
            entry.variability = attribute.GetVariability() == SdfVariabilityUniform ? 1u : 0u;
            reason = Domain(name);
            if (_clips && _time.IsNumeric()) reason = OPENUSD_PROPERTY_REASON_VALUE_CLIPS;
            if (reason != OPENUSD_PROPERTY_REASON_NONE)
            {
                entry.resolve_source = OPENUSD_PROPERTY_SOURCE_DEFERRED;
                entry.value_state = OPENUSD_PROPERTY_VALUE_DEFERRED;
                Unavailable(entry.value, reason);
                Unavailable(entry.time_samples, reason);
            }
            else
            {
                const auto info = attribute.GetResolveInfo(_time);
                entry.resolve_source = static_cast<uint32_t>(info.GetSource());
                entry.value_state = info.ValueIsBlocked() ? OPENUSD_PROPERTY_VALUE_BLOCKED :
                    (info.GetSource() == UsdResolveInfoSourceNone ? OPENUSD_PROPERTY_VALUE_UNSET : OPENUSD_PROPERTY_VALUE_PRESENT);
                if (_clips)
                {
                    Unavailable(entry.time_samples, OPENUSD_PROPERTY_REASON_VALUE_CLIPS);
                }
                else
                {
                    const auto anyInfo = attribute.GetResolveInfo();
                    entry.flags |= 8u | (anyInfo.HasAuthoredValueOpinion() ? 16u : 0u);
                    Samples(anyInfo, name, entry);
                }
                Source(info, name, &layer, &node, &path, &sourceLayer, &sourcePath);
                Raw(info, attribute, layer, node, path, &value, &reason);
                if (reason == OPENUSD_PROPERTY_REASON_NONE && !value.IsEmpty() &&
                    !value.IsHolding<SdfValueBlock>() && value.GetTypeid() != valueType.GetType().GetTypeid())
                    reason = OPENUSD_PROPERTY_REASON_TYPE;
                if (reason != OPENUSD_PROPERTY_REASON_NONE)
                {
                    entry.value_state = OPENUSD_PROPERTY_VALUE_DEFERRED;
                    Unavailable(entry.value, reason, reason == OPENUSD_PROPERTY_REASON_TYPE ?
                        OPENUSD_PROPERTY_UNSUPPORTED : OPENUSD_PROPERTY_DEFERRED);
                    value = VtValue();
                }
                if (value.IsHolding<SdfValueBlock>())
                {
                    entry.value_state = OPENUSD_PROPERTY_VALUE_BLOCKED;
                    value = VtValue();
                }
            }
        }
        std::string targetLayer, targetPath;
        TargetSource(property, !attribute, &targetLayer, &targetPath);
        entry.name = Append(_result, _budget, name.GetString());
        entry.type = Append(_result, _budget, type);
        entry.source_layer = Append(_result, _budget, sourceLayer);
        entry.source_path = Append(_result, _budget, sourcePath);
        entry.target_source_layer = Append(_result, _budget, targetLayer);
        entry.target_source_path = Append(_result, _budget, targetPath);
        entry.asset_offset = static_cast<uint32_t>(_result.assets.size());
        entry.value.offset = static_cast<uint32_t>(_result.offsets.size());
        if (attribute && reason == OPENUSD_PROPERTY_REASON_NONE)
            PreviewValue(value, _prim.GetStage(), layer, node, _result, _budget, entry);
        Targets(property, !attribute, entry);
        _result.entries.push_back(entry);
    }

    UsdPrim _prim;
    UsdTimeCode _time;
    openusd_property_snapshot& _result;
    Budget& _budget;
    std::unordered_map<const SdfLayer*, Store> _stores;
    std::unique_ptr<PcpCache> _targetContext;
    size_t _siteCount = 0;
    bool _clips = false;
};
}

void Build(const UsdPrim& prim, UsdTimeCode time, openusd_property_snapshot& result, Budget& budget)
{
    Reader(prim, time, result, budget).Run();
}
}
