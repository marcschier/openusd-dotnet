// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/hierarchy.h"
#include "pxr/base/tf/unicodeUtils.h"

namespace OpenUsdHierarchy
{
void Append(openusd_hierarchy_snapshot& result, Budget& budget, std::string_view value)
{
    budget.Text(value.size() + 1);
    if (value.find('\0') != std::string_view::npos)
    {
        throw std::invalid_argument("Hierarchy text contains an embedded NUL.");
    }
    TfUtf8CodePointIterator iterator(value.begin(), value.end());
    while (iterator != TfUtf8CodePointIterator::PastTheEndSentinel{})
    {
        const auto start = iterator.GetBase();
        const auto codePoint = *iterator;
        ++iterator;
        if (codePoint == TfUtf8InvalidCodePoint &&
            value.substr(static_cast<size_t>(start - value.begin()),
                static_cast<size_t>(iterator.GetBase() - start)) != "\xef\xbf\xbd")
        {
            throw std::invalid_argument("Hierarchy text is not valid UTF-8.");
        }
    }
    result.offsets.push_back(static_cast<uint32_t>(result.data.size()));
    result.data.insert(result.data.end(), value.begin(), value.end());
    result.data.push_back('\0');
}
}

namespace
{
using namespace OpenUsdHierarchy;

openusd_hierarchy_limits DefaultLimits()
{
    return {sizeof(openusd_hierarchy_limits), OPENUSD_HIERARCHY_VERSION,
        100000, 16u * 1024u * 1024u, 256, 4096, 65536, 2000000};
}

bool ValidLimits(const openusd_hierarchy_limits& limits)
{
    return limits.struct_size == sizeof(limits) && limits.version == OPENUSD_HIERARCHY_VERSION &&
        limits.maximum_prim_count <= OPENUSD_HIERARCHY_MAX_PRIMS &&
        limits.maximum_text_bytes <= OPENUSD_HIERARCHY_MAX_TEXT_BYTES &&
        limits.maximum_depth <= OPENUSD_HIERARCHY_MAX_DEPTH &&
        limits.maximum_variant_sets <= OPENUSD_HIERARCHY_MAX_VARIANT_SETS &&
        limits.maximum_variant_names <= OPENUSD_HIERARCHY_MAX_VARIANT_NAMES &&
        limits.maximum_metadata_work <= OPENUSD_HIERARCHY_MAX_METADATA_WORK;
}

uint32_t Flags(const UsdPrim& prim)
{
    uint32_t flags = 0;
    if (prim.IsActive()) flags |= OPENUSD_HIERARCHY_ACTIVE;
    if (prim.IsLoaded()) flags |= OPENUSD_HIERARCHY_LOADED;
    if (prim.IsDefined()) flags |= OPENUSD_HIERARCHY_DEFINED;
    if (prim.IsAbstract()) flags |= OPENUSD_HIERARCHY_ABSTRACT;
    if (prim.IsPrototype()) flags |= OPENUSD_HIERARCHY_PROTOTYPE;
    if (prim.IsInPrototype()) flags |= OPENUSD_HIERARCHY_IN_PROTOTYPE;
    if (prim.IsInstance()) flags |= OPENUSD_HIERARCHY_INSTANCE;
    if (prim.IsInstanceProxy()) flags |= OPENUSD_HIERARCHY_INSTANCE_PROXY;
    if (prim.HasPayload()) flags |= OPENUSD_HIERARCHY_HAS_PAYLOAD;
    if (prim.IsInstanceable()) flags |= OPENUSD_HIERARCHY_INSTANCEABLE;
    return flags;
}

class Builder
{
public:
    Builder(openusd_hierarchy_snapshot& result, Budget& budget)
        : _result(result), _budget(budget), _variants(budget) {}

    void Stage(const UsdStageRefPtr& stage)
    {
        Walk(stage->GetPseudoRoot(), true);
        for (size_t index = 0; index < _prototypes.size(); ++index)
        {
            // A nested instance may grow the queue during this walk.
            const UsdPrim prototype = _prototypes[index];
            Walk(prototype, false);
        }
    }

private:
    void Walk(const UsdPrim& root, bool pseudoRoot)
    {
        std::vector<int32_t> parents;
        const auto range = UsdPrimRange::AllPrimsPreAndPostVisit(root);
        for (auto iterator = range.begin(); iterator != range.end(); ++iterator)
        {
            if (pseudoRoot && *iterator == root)
            {
                continue;
            }
            if (iterator.IsPostVisit())
            {
                parents.pop_back();
                continue;
            }
            Budget::Check("prim count", _result.entries.size() + 1, _budget.limits.maximum_prim_count);
            Budget::Check("depth", parents.size() + 1, _budget.limits.maximum_depth);
            const int32_t parent = parents.empty() ? -1 : parents.back();
            const auto index = static_cast<int32_t>(_result.entries.size());
            Add(*iterator, parent, static_cast<uint32_t>(parents.size() + 1));
            parents.push_back(index);
        }
    }

    void Add(const UsdPrim& prim, int32_t parent, uint32_t depth)
    {
        const std::string& name = prim.GetName().GetString();
        const std::string& type = prim.GetTypeName().GetString();
        std::string_view prefix;
        if (parent >= 0)
        {
            const auto& entry = _result.entries[static_cast<size_t>(parent)];
            prefix = _result.data.data() + _result.offsets[entry.string_offset];
        }
        // No SdfPath::GetString: its cache can allocate an unbounded full path.
        Budget::Check("text bytes", prefix.size() + name.size() + 2,
            _budget.limits.maximum_text_bytes - _budget.text);
        std::string path(prefix);
        path += '/';
        path += name;
        openusd_hierarchy_entry entry{};
        entry.parent_index = parent;
        entry.depth = depth;
        entry.flags = Flags(prim);
        entry.string_offset = static_cast<uint32_t>(_result.offsets.size());
        entry.variant_offset = static_cast<uint32_t>(_result.variant_sets.size());
        Append(_result, _budget, path);
        Append(_result, _budget, name);
        Append(_result, _budget, type);
        const UsdPrim prototype = prim.IsInstance() ? prim.GetPrototype() : UsdPrim();
        if (prototype)
        {
            const std::string& prototypeName = prototype.GetName().GetString();
            Budget::Check("text bytes", prototypeName.size() + 2,
                _budget.limits.maximum_text_bytes - _budget.text);
            Append(_result, _budget, "/" + prototypeName);
            if (_seenPrototypes.find(prototype.GetPath()) == _seenPrototypes.end())
            {
                Budget::Check("prim count", _prototypes.size() + 1, _budget.limits.maximum_prim_count);
                _seenPrototypes.insert(prototype.GetPath());
                _prototypes.push_back(prototype);
            }
        }
        else
        {
            Append(_result, _budget, {});
        }
        _variants.Read(prim, _result, entry);
        _result.entries.push_back(entry);
        if (parent >= 0)
        {
            ++_result.entries[static_cast<size_t>(parent)].child_count;
        }
    }

    openusd_hierarchy_snapshot& _result;
    Budget& _budget;
    VariantReader _variants;
    std::vector<UsdPrim> _prototypes;
    std::unordered_set<SdfPath, SdfPath::Hash> _seenPrototypes;
};
}

openusd_status openusd_stage_get_hierarchy_snapshot(
    const openusd_stage* stage,
    const openusd_hierarchy_limits* limits,
    openusd_hierarchy_snapshot** snapshot,
    openusd_hierarchy_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        std::unique_ptr<openusd_hierarchy_snapshot> result;
        openusd_hierarchy_view output{};
        const bool validView = view != nullptr && IsAligned(view) &&
            view->struct_size == sizeof(*view) && view->version == OPENUSD_HIERARCHY_VERSION;
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(snapshot);
        ResetVersionedAbiOutput(view);
        const auto effective = limits == nullptr ? DefaultLimits() :
            (IsAligned(limits) && limits->struct_size == sizeof(*limits) &&
             limits->version == OPENUSD_HIERARCHY_VERSION ? *limits : openusd_hierarchy_limits{});
        if (snapshot == nullptr || !IsAligned(snapshot) || !validView || !ValidLimits(effective))
        {
            WriteError(error, "A valid stage, aligned hierarchy outputs and bounded version 1 limits are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const openusd_status status = GuardStage(stage, error, [&]() -> openusd_status
        {
            if (stage == nullptr || !stage->value)
            {
                WriteError(error, "A valid stage is required for a hierarchy snapshot.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            result = std::make_unique<openusd_hierarchy_snapshot>();
            Budget budget(effective);
            Builder builder(*result, budget);
            builder.Stage(stage->value);
            output.struct_size = sizeof(output);
            output.version = OPENUSD_HIERARCHY_VERSION;
            output.change_serial = stage->change_serial.load(std::memory_order_relaxed);
            output.is_complete = std::all_of(result->entries.begin(), result->entries.end(),
                [](const auto& entry) { return entry.variant_status == OPENUSD_HIERARCHY_VARIANTS_COMPLETE; }) ? 1u : 0u;
            output.metadata_work = static_cast<uint32_t>(budget.work);
            output.entries = result->entries.data();
            output.entries_size = result->entries.size() * sizeof(openusd_hierarchy_entry);
            output.entry_count = result->entries.size();
            output.variant_sets = result->variant_sets.data();
            output.variant_sets_size = result->variant_sets.size() * sizeof(openusd_hierarchy_variant_set);
            output.variant_set_count = result->variant_sets.size();
            output.data = result->data.data();
            output.data_size = result->data.size();
            output.offsets = result->offsets.data();
            output.offsets_size = result->offsets.size() * sizeof(uint32_t);
            output.string_count = result->offsets.size();
            return OPENUSD_STATUS_OK;
        });
        // No SDK objects or callbacks remain after the stage diagnostic guard.
        if (status == OPENUSD_STATUS_OK)
        {
            *view = output;
            *snapshot = result.release();
        }
        return status;
    });
}

void openusd_hierarchy_snapshot_release(openusd_hierarchy_snapshot* snapshot)
{
    delete snapshot;
}
