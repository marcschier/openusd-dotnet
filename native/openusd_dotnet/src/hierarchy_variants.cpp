// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/hierarchy.h"
#include "internal/layer_edit.h"

#include "pxr/usd/pcp/layerStack.h"
#include "pxr/usd/pcp/primIndex.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/usdaData.h"

#include <unordered_map>

namespace OpenUsdHierarchy
{
namespace
{
class LayerDataAccess : public SdfFileFormat
{
public:
    LayerDataAccess() = delete;

    static SdfAbstractDataConstPtr Get(const SdfLayer& layer)
    {
        return _GetLayerData(layer);
    }
};

enum class StoreKind { Resident, Crate, Deferred };

StoreKind Kind(const SdfAbstractDataConstPtr& data)
{
    if (!data)
    {
        return StoreKind::Deferred;
    }
    if (typeid(*data) == typeid(SdfData) || typeid(*data) == typeid(SdfUsdaData) ||
        typeid(*data) == typeid(OpenUsdEdit::CountedData))
    {
        return StoreKind::Resident;
    }
    static const SdfAbstractDataRefPtr crate = []
    {
        const auto format = SdfFileFormat::FindById(TfToken("usdc"));
        return format ? format->InitData({}) : SdfAbstractDataRefPtr();
    }();
    return crate && typeid(*data) == typeid(*crate) ? StoreKind::Crate : StoreKind::Deferred;
}

struct Store
{
    SdfAbstractDataConstPtr data;
    StoreKind kind;
};
}

struct VariantReader::Impl
{
    explicit Impl(Budget& value) : budget(value) {}

    Budget& budget;
    std::unordered_map<const SdfLayer*, Store> stores;

    const Store& Data(const SdfLayerRefPtr& layer)
    {
        const auto found = stores.find(layer.operator->());
        if (found != stores.end())
        {
            return found->second;
        }
        budget.Work();
        auto data = LayerDataAccess::Get(*layer);
        const auto kind = Kind(data);
        return stores.emplace(layer.operator->(), Store{std::move(data), kind}).first->second;
    }

    void SourceSetNames(const VtValue& value)
    {
        if (!value.IsHolding<SdfStringListOp>())
        {
            throw std::invalid_argument("Hierarchy variantSetNames metadata is not a string list operation.");
        }
        const auto& operation = value.UncheckedGet<SdfStringListOp>();
        for (const auto* names : {&operation.GetExplicitItems(), &operation.GetAddedItems(),
            &operation.GetPrependedItems(), &operation.GetAppendedItems(),
            &operation.GetDeletedItems(), &operation.GetOrderedItems()})
        {
            Budget::Check("variant sets (source)", budget.source_variant_sets + names->size(),
                budget.limits.maximum_variant_sets);
            budget.source_variant_sets += names->size();
            budget.Work(names->size());
            for (const std::string& name : *names)
            {
                budget.Text(name.size() + 1);
            }
        }
    }

    bool AdmitSetNames(const PcpPrimIndex& index)
    {
        for (const auto& node : index.GetNodeRange())
        {
            budget.Work();
            for (const auto& layer : node.GetLayerStack()->GetLayers())
            {
                budget.Work();
                const auto& store = Data(layer);
                if (store.kind == StoreKind::Deferred)
                {
                    return false;
                }
                const auto& path = node.GetPath();
                if (!store.data->Has(path, SdfFieldKeys->VariantSetNames, static_cast<VtValue*>(nullptr)))
                {
                    continue;
                }
                // Pinned crateData::_UnpackForField eagerly unpacks TokenVector, not
                // StringListOp. GetTypeid cannot distinguish a resident list op from
                // ValueRep, so do not deserialize it even when it was read previously.
                if (store.kind != StoreKind::Resident)
                {
                    return false;
                }
                VtValue value;
                if (store.data->Has(path, SdfFieldKeys->VariantSetNames, &value))
                {
                    // Known SdfData stores return a shared VtValue, not a vector copy.
                    SourceSetNames(value);
                }
            }
        }
        return true;
    }

    void AdmitOptions(const PcpPrimIndex& index, const std::string& setName)
    {
        for (const auto& node : index.GetNodeRange())
        {
            budget.Work();
            if (!node.GetPath().IsPrimOrPrimVariantSelectionPath())
            {
                continue;
            }
            const auto path = node.GetPath().AppendVariantSelection(setName, std::string());
            for (const auto& layer : node.GetLayerStack()->GetLayers())
            {
                budget.Work();
                const auto& store = Data(layer);
                const auto& field = SdfChildrenKeys->VariantChildren;
                if (!store.data->Has(path, field, static_cast<VtValue*>(nullptr)))
                {
                    continue;
                }
                // Crate token vectors are resident after opening. Checking the
                // concrete type first excludes malformed deferred field values.
                if (store.data->GetTypeid(path, field) != typeid(TfTokenVector))
                {
                    throw std::invalid_argument("Hierarchy variantChildren metadata is not a token vector.");
                }
                VtValue value;
                if (store.data->Has(path, field, &value))
                {
                    const auto& names = value.UncheckedGet<TfTokenVector>();
                    Budget::Check("variant names (source)", budget.source_variant_names + names.size(),
                        budget.limits.maximum_variant_names);
                    budget.source_variant_names += names.size();
                    budget.Work(names.size());
                    for (const auto& name : names)
                    {
                        budget.Text(name.GetString().size() + 1);
                    }
                }
            }
        }
    }

    std::string Selection(const PcpPrimIndex& index, const std::string& setName)
    {
        for (const auto& node : index.GetNodeRange())
        {
            budget.Work();
            const auto& path = node.GetPath();
            if (node.GetArcType() != PcpArcTypeVariant || !path.IsPrimVariantSelectionPath())
            {
                continue;
            }
            const auto& selection = path.GetNameToken().GetString();
            budget.Text(selection.size() + 1);
            // The token borrows the selection. Avoid GetVariantSelection(), which
            // copies both strings before either can be admitted.
            if (path.GetParentPath().AppendVariantSelection(setName, selection) == path)
            {
                return selection;
            }
        }
        return {};
    }

    void Read(const UsdPrim& prim, openusd_hierarchy_snapshot& result, openusd_hierarchy_entry& entry)
    {
        const auto& index = prim.GetPrimIndex();
        if (!AdmitSetNames(index))
        {
            entry.variant_status = OPENUSD_HIERARCHY_VARIANTS_DEFERRED;
            return;
        }
        // These SDK materializations only run after admission of every source
        // list-op element/byte and every source choice list read by the pinned API.
        const auto names = prim.GetVariantSets().GetNames();
        Budget::Check("variant sets", budget.variant_sets + names.size(), budget.limits.maximum_variant_sets);
        budget.variant_sets += names.size();
        entry.variant_count = static_cast<uint32_t>(names.size());
        for (const auto& name : names)
        {
            AdmitOptions(index, name);
            const auto variants = prim.GetVariantSets().GetVariantSet(name).GetVariantNames();
            Budget::Check("variant names", budget.variant_names + variants.size(), budget.limits.maximum_variant_names);
            budget.variant_names += variants.size();
            const std::string selection = Selection(index, name);
            result.variant_sets.push_back({static_cast<uint32_t>(result.offsets.size()),
                static_cast<uint32_t>(variants.size())});
            Append(result, budget, name);
            Append(result, budget, selection);
            for (const auto& variant : variants)
            {
                Append(result, budget, variant);
            }
        }
    }
};

VariantReader::VariantReader(Budget& budget) : _impl(std::make_unique<Impl>(budget)) {}
VariantReader::~VariantReader() = default;

void VariantReader::Read(
    const UsdPrim& prim, openusd_hierarchy_snapshot& result, openusd_hierarchy_entry& entry)
{
    _impl->Read(prim, result, entry);
}
}
