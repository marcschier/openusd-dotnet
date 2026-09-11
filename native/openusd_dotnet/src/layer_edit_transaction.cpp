// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/layer_edit.h"
#include "pxr/usd/sdf/attributeSpec.h"
#include "pxr/usd/sdf/changeBlock.h"
#include "pxr/usd/sdf/primSpec.h"
#include "pxr/usd/sdf/relationshipSpec.h"
#include <array>

namespace OpenUsdEdit
{
namespace
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
thread_local int32_t failAfter = -1;
#endif

const TfToken& Field(const Address& address)
{
    return address.field == 0 ? SdfFieldKeys->Default
        : address.field == 1 ? SdfFieldKeys->TimeSamples
        : address.field == 2 ? SdfFieldKeys->ConnectionPaths : SdfFieldKeys->TargetPaths;
}

bool SameDeclaration(const Opinion& a, const Opinion& b)
{
    return a.kind == b.kind && SameValue(a.typeName, b.typeName)
        && SameValue(a.variability, b.variability) && SameValue(a.custom, b.custom);
}

bool SameOpinions(const Snapshot& a, const Snapshot& b)
{
    if (a.opinions.size() != b.opinions.size()) { return false; }
    for (size_t i = 0; i < a.opinions.size(); ++i)
    {
        const auto& left = a.opinions[i];
        const auto& right = b.opinions[i];
        if (!(left.address == right.address) || !SameDeclaration(left, right)
            || !SameValue(left.value, right.value)) { return false; }
    }
    return true;
}

void ValidateOpinion(const Opinion& opinion)
{
    if (!opinion.kind) { return; }
    Check((opinion.kind == 2) == (opinion.address.field == 3),
        "Property kind and edited field differ.");
    Check(opinion.custom.IsEmpty() || opinion.custom.IsHolding<bool>(), "Invalid custom declaration.");
    Check(opinion.variability.IsEmpty() || opinion.variability.IsHolding<SdfVariability>(),
        "Invalid variability declaration.");
    if (opinion.address.field >= 2)
    {
        Check(opinion.value.IsEmpty() || opinion.value.IsHolding<SdfPathListOp>(),
            "Connections and targets require an exact SdfPathListOp (not a value block).");
        if (!opinion.value.IsEmpty())
        {
            const auto& list = opinion.value.UncheckedGet<SdfPathListOp>();
            for (const auto* items : {&list.GetExplicitItems(), &list.GetAddedItems(),
                &list.GetPrependedItems(), &list.GetAppendedItems(),
                &list.GetDeletedItems(), &list.GetOrderedItems()})
            {
                for (const auto& path : *items)
                {
                    Check(path.IsAbsolutePath() && !path.ContainsPrimVariantSelection()
                        && (path.IsPrimPath() || path.IsPropertyPath()),
                        "Path-list mutations require absolute prim/property paths.");
                    Check(opinion.address.field != 2 || path.IsPropertyPath(),
                        "Connection mutations require attribute property paths.");
                }
            }
        }
    }
    if (opinion.kind == 1)
    {
        Check(opinion.typeName.IsHolding<TfToken>(), "An attribute type name is required.");
        const auto type = SdfSchema::GetInstance().FindType(opinion.typeName.UncheckedGet<TfToken>());
        Check(static_cast<bool>(type), "Unknown attribute declaration type.");
        if (opinion.address.field < 2 && !opinion.value.IsEmpty()
            && !opinion.value.IsHolding<SdfValueBlock>())
        {
            Check(type.GetType().GetTypeid() == opinion.value.GetTypeid(),
                "Mutation value type does not exactly match the authored type (role aliases are preserved).");
        }
        if (opinion.address.field == 1 && !opinion.value.IsEmpty())
        {
            Check(opinion.variability.IsEmpty()
                || opinion.variability.UncheckedGet<SdfVariability>() == SdfVariabilityVarying,
                "Cannot author a sample on a uniform attribute.");
        }
    }
}

Snapshot Mutations(const Snapshot& before, const uint8_t* bytes, size_t size)
{
    Reader r(bytes, size);
    r.Header(3);
    const uint32_t count = r.Count(MaxAddresses);
    Check(count == before.opinions.size(), "Mutation addresses must exactly match the expected snapshot.");
    Snapshot desired = before;
    for (uint32_t i = 0; i < count; ++i)
    {
        const Address address = Address::Read(r);
        Check(address == before.opinions[i].address, "Mutation address order differs from snapshot.");
        const uint32_t operation = r.Count(2);
        const TfToken typeName(r.Text());
        const auto variability = static_cast<SdfVariability>(r.Count(1));
        const bool custom = r.Count(1) != 0;
        auto value = DecodeValue(r);
        Check(operation == 1 || value.IsEmpty(), "Clear/block mutation must use empty value tag.");
        Check(operation != 1 || (!value.IsEmpty() && !value.IsHolding<SdfValueBlock>()),
            "Set requires a concrete typed value; use Block for a value block.");
        auto& opinion = desired.opinions[i];
        if (operation == 0)
        {
            opinion.value = VtValue();
        }
        else
        {
            if (!opinion.kind)
            {
                opinion.kind = address.field == 3 ? 2u : 1u;
                opinion.typeName = opinion.kind == 1 ? VtValue(typeName) : VtValue();
                opinion.variability = VtValue(variability);
                opinion.custom = VtValue(custom);
            }
            opinion.value = operation == 2 ? VtValue(SdfValueBlock()) : std::move(value);
        }
    }
    r.End();
    // Multiple addresses can create the same property. Share its declaration,
    // including for another address whose requested action was a no-op clear.
    for (auto& opinion : desired.opinions)
    {
        for (const auto& other : desired.opinions)
        {
            if (opinion.address.path == other.address.path && other.kind)
            {
                if (!opinion.kind)
                {
                    opinion.kind = other.kind;
                    opinion.typeName = other.typeName;
                    opinion.variability = other.variability;
                    opinion.custom = other.custom;
                }
                Check(SameDeclaration(opinion, other), "Inconsistent declarations for one property.");
            }
        }
        ValidateOpinion(opinion);
    }
    return desired;
}

void AdmitChildren(const SdfAbstractDataConstPtr& data, const SdfPath& path, const TfToken& field)
{
    VtValue value;
    if (data->Has(path, field, &value))
    {
        Check(value.IsHolding<TfTokenVector>(), "Unsupported child-name storage.");
        const auto& items = value.UncheckedGet<TfTokenVector>();
        Check(items.size() < MaxItems, "Ancestor/property child count budget exceeded.");
        for (const auto& item : items)
        {
            Check(item.GetString().size() <= MaxString, "Child name byte budget exceeded.");
        }
    }
}

void AdmitAncestors(const SdfAbstractDataConstPtr& data, const Snapshot& desired)
{
    for (const auto& opinion : desired.opinions)
    {
        auto path = opinion.address.path.GetPrimPath();
        while (!path.IsEmpty())
        {
            const auto type = data->GetSpecType(path);
            Check(type == SdfSpecTypeUnknown || type == SdfSpecTypePrim
                || type == SdfSpecTypePseudoRoot, "Unsupported ancestor spec kind.");
            AdmitChildren(data, path, SdfChildrenKeys->PrimChildren);
            AdmitChildren(data, path, SdfChildrenKeys->PropertyChildren);
            path = path.GetParentPath();
        }
    }
}

bool CanDelete(const LayerRecord& record, const Snapshot& before, const SdfPath& path)
{
    const auto owned = record.owned.find(path);
    if (owned == record.owned.end()) { return false; }
    const auto data = Resident(record.layer);
    if (DataAccess::ConcreteType(data) == typeid(CountedData))
    {
        const auto& counted = static_cast<const CountedData&>(*data);
        if (counted.FieldCount(path) > MaxFields) { return false; }
        for (const auto& field : data->List(path))
        {
            if (field == SdfFieldKeys->TypeName || field == SdfFieldKeys->Variability
                || field == SdfFieldKeys->Custom) { continue; }
            bool covered = false;
            for (const auto& opinion : before.opinions)
            {
                if (opinion.address.path == path && Field(opinion.address) == field)
                {
                    covered = true;
                    break;
                }
            }
            if (!covered) { return false; }
        }
    }
    else if (owned->second)
    {
        // SDK resident stores have no bounded field inventory. Once externally
        // touched, their owned property cannot be proved safe to erase.
        return false;
    }
    VtValue samples;
    if (data->Has(path, SdfFieldKeys->TimeSamples, &samples))
    {
        if (!samples.IsHolding<SdfTimeSampleMap>()) { return false; }
        const auto& map = samples.UncheckedGet<SdfTimeSampleMap>();
        if (map.size() > MaxAddresses) { return false; }
        for (const auto& item : map)
        {
            bool covered = false;
            for (const auto& opinion : before.opinions)
            {
                if (opinion.address.path == path && opinion.address.field == 1
                    && opinion.address.time == item.first) { covered = true; break; }
            }
            if (!covered) { return false; }
        }
    }
    for (uint32_t field = 0; field < 4; ++field)
    {
        const Address address{path, field, 0};
        if (!data->Has(path, Field(address), static_cast<VtValue*>(nullptr))) { continue; }
        bool covered = false;
        for (const auto& opinion : before.opinions)
        {
            if (opinion.address.path == path && opinion.address.field == field)
            {
                covered = true; break;
            }
        }
        if (!covered) { return false; }
    }
    return true;
}

void SetRaw(const SdfLayerRefPtr& layer, const SdfPath& path,
    const TfToken& key, const VtValue& value)
{
    if (value.IsEmpty()) { layer->EraseField(path, key); }
    else
    {
        VtValue current;
        // Sdf equality elides changes such as +0 to -0. A transient block
        // forces the exact bits to be authored inside the transaction batch.
        if (key == SdfFieldKeys->Default && Resident(layer)->Has(path, key, &current)
            && current == value && !SameValue(current, value))
        {
            layer->SetField(path, key, VtValue(SdfValueBlock()));
        }
        // Sdf's high-level setter can elide an absent declaration equal to its
        // schema fallback. Prime it so replay preserves authored presence too.
        if (!Resident(layer)->Has(path, key, static_cast<VtValue*>(nullptr)))
        {
            if (key == SdfFieldKeys->Custom && value.IsHolding<bool>())
            {
                layer->SetField(path, key, VtValue(!value.UncheckedGet<bool>()));
            }
            else if (key == SdfFieldKeys->Variability && value.IsHolding<SdfVariability>())
            {
                const auto other = value.UncheckedGet<SdfVariability>() == SdfVariabilityVarying
                    ? SdfVariabilityUniform : SdfVariabilityVarying;
                layer->SetField(path, key, VtValue(other));
            }
        }
        layer->SetField(path, key, value);
    }
}

void EnsureProperty(LayerRecord& record, const Opinion& opinion)
{
    Check(opinion.address.path.IsPrimPropertyPath() &&
        opinion.address.path.GetPrimPath().AppendProperty(opinion.address.path.GetNameToken()) ==
            opinion.address.path,
        "Property creation must exactly match the admitted direct prim-property address.");
    const auto& layer = record.layer;
    if (layer->HasSpec(opinion.address.path)) { return; }
    std::array<SdfPath, MaxDepth> ancestors;
    size_t ancestorCount = 0;
    auto path = opinion.address.path.GetPrimPath();
    while (!path.IsAbsoluteRootPath() && !layer->HasSpec(path))
    {
        Check(ancestorCount < ancestors.size(), "Owned ancestor depth exceeded.");
        ancestors[ancestorCount++] = path;
        path = path.GetParentPath();
    }
    for (size_t i = 0; i < ancestorCount; ++i) { record.owned.try_emplace(ancestors[i], false); }
    record.owned.try_emplace(opinion.address.path, false);
    const auto prim = SdfCreatePrimInLayer(layer, opinion.address.path.GetPrimPath());
    Check(static_cast<bool>(prim), "Could not create owned ancestor prim.");
    const bool custom = !opinion.custom.IsEmpty() && opinion.custom.UncheckedGet<bool>();
    const auto variability = opinion.variability.IsEmpty() ? SdfVariabilityVarying
        : opinion.variability.UncheckedGet<SdfVariability>();
    if (opinion.kind == 1)
    {
        const auto type = SdfSchema::GetInstance().FindType(opinion.typeName.UncheckedGet<TfToken>());
        Check(static_cast<bool>(SdfAttributeSpec::New(prim, opinion.address.path.GetName(),
            type, variability, custom)), "Could not create owned attribute.");
    }
    else
    {
        Check(static_cast<bool>(SdfRelationshipSpec::New(prim, opinion.address.path.GetName(),
            custom, variability)), "Could not create owned relationship.");
    }
    SetRaw(layer, opinion.address.path, SdfFieldKeys->TypeName, opinion.typeName);
    SetRaw(layer, opinion.address.path, SdfFieldKeys->Variability, opinion.variability);
    SetRaw(layer, opinion.address.path, SdfFieldKeys->Custom, opinion.custom);
}

void WriteOpinion(LayerRecord& record, const Opinion& opinion)
{
    if (!opinion.kind)
    {
        const auto property = record.layer->GetPropertyAtPath(opinion.address.path);
        if (property)
        {
            record.layer->GetPrimAtPath(opinion.address.path.GetPrimPath())->RemoveProperty(property);
            record.owned.erase(opinion.address.path);
        }
        return;
    }
    EnsureProperty(record, opinion);
    if (opinion.address.field == 1)
    {
        if (opinion.value.IsEmpty())
        {
            record.layer->EraseTimeSample(opinion.address.path, opinion.address.time);
        }
        else
        {
            VtValue current;
            if (Resident(record.layer)->QueryTimeSample(opinion.address.path,
                opinion.address.time, &current) && current == opinion.value
                && !SameValue(current, opinion.value))
            {
                record.layer->SetTimeSample(opinion.address.path, opinion.address.time,
                    VtValue(SdfValueBlock()));
            }
            record.layer->SetTimeSample(opinion.address.path, opinion.address.time, opinion.value);
        }
    }
    else
    {
        SetRaw(record.layer, opinion.address.path, Field(opinion.address), opinion.value);
    }
}

void CleanupAncestors(LayerRecord& record, const std::set<SdfPath>* only = nullptr)
{
    const auto data = Resident(record.layer);
    std::vector<std::pair<SdfPath, bool>> candidates(record.owned.rbegin(), record.owned.rend());
    for (const auto& candidate : candidates)
    {
        const SdfPath& path = candidate.first;
        const bool foreign = candidate.second;
        if (only && only->count(path) == 0) { continue; }
        if (!path.IsPrimPath() || path.IsAbsoluteRootPath() || foreign) { continue; }
        VtValue children;
        bool empty = true;
        for (const auto& key : {SdfChildrenKeys->PrimChildren, SdfChildrenKeys->PropertyChildren})
        {
            if (data->Has(path, key, &children)
                && (!children.IsHolding<TfTokenVector>()
                    || !children.UncheckedGet<TfTokenVector>().empty()))
            {
                empty = false;
            }
        }
        if (!empty) { continue; }
        if (DataAccess::ConcreteType(data) == typeid(CountedData))
        {
            const auto& counted = static_cast<const CountedData&>(*data);
            if (counted.FieldCount(path) > 3) { continue; }
            for (const auto& key : data->List(path))
            {
                if (key != SdfFieldKeys->Specifier && key != SdfChildrenKeys->PrimChildren
                    && key != SdfChildrenKeys->PropertyChildren) { empty = false; }
            }
        }
        if (!empty) { continue; }
        const auto prim = record.layer->GetPrimAtPath(path);
        if (prim)
        {
            const auto parent = record.layer->GetPrimAtPath(path.GetParentPath());
            if (parent) { parent->RemoveNameChild(prim); }
            else { record.layer->GetPseudoRoot()->RemoveNameChild(prim); }
        }
        record.owned.erase(path);
    }
}

using ParentInventories = std::map<std::pair<SdfPath, TfToken>, VtValue>;

ParentInventories CaptureParentInventories(const SdfAbstractDataConstPtr& data, const Snapshot& affected)
{
    ParentInventories result;
    Writer admission;
    for (const auto& opinion : affected.opinions)
    {
        SdfPath child = opinion.address.path;
        while (!child.IsAbsoluteRootPath())
        {
            const SdfPath parent = child.GetParentPath();
            const TfToken field = child.IsPrimPropertyPath()
                ? SdfChildrenKeys->PropertyChildren : SdfChildrenKeys->PrimChildren;
            const auto key = std::make_pair(parent, field);
            if (data->HasSpec(parent) && result.count(key) == 0)
            {
                VtValue value;
                data->Has(parent, field, &value);
                Check(value.IsEmpty() || value.IsHolding<TfTokenVector>(),
                    "Affected parent inventory has unsupported storage.");
                admission.Text(parent.GetString());
                admission.Text(field.GetString());
                EncodeValue(admission, value);
                result.emplace(key, std::move(value));
            }
            child = parent;
        }
    }
    return result;
}

bool SameParentInventories(const SdfAbstractDataConstPtr& data, const ParentInventories& before)
{
    for (const auto& entry : before)
    {
        if (!data->HasSpec(entry.first.first)) { return false; }
        VtValue current;
        data->Has(entry.first.first, entry.first.second, &current);
        if (!SameValue(current, entry.second)) { return false; }
    }
    return true;
}

void RestoreParentInventories(LayerRecord& record, const ParentInventories& before)
{
    for (const auto& entry : before)
    {
        Check(record.layer->HasSpec(entry.first.first),
            "Rollback lost an existing affected parent spec.");
        SetRaw(record.layer, entry.first.first, entry.first.second, entry.second);
    }
    Check(SameParentInventories(Resident(record.layer), before),
        "Rollback could not restore exact affected parent inventory presence/order.");
}

int32_t Execute(const openusd_layer* handle, const Snapshot& expected, Snapshot desired,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    auto& record = Record(handle);
    if (!Matches(expected.identity, handle->stage, record))
    {
        WriteError(error, "Editing layer identity/generation is stale.");
        return OPENUSD_EDIT_STALE_TARGET;
    }
    if (!record.layer->PermissionToEdit())
    {
        WriteError(error, "Editing layer is not editable.");
        return OPENUSD_EDIT_NOT_EDITABLE;
    }
    const auto data = Resident(record.layer);
    if (DataAccess::ConcreteType(data) != typeid(CountedData))
    {
        WriteError(error, "Bounded mutation requires the owned review data inventory; this resident layer is capture-only.");
        return OPENUSD_EDIT_NOT_EDITABLE;
    }
    const auto& counted = static_cast<const CountedData&>(*data);
    Check(counted.Inventory().size() <= MaxSpecs, "Review layer spec count budget exceeded.");
    for (const auto& entry : counted.Inventory())
    {
        Check(entry.second <= MaxFields, "Review layer field count budget exceeded.");
    }
    const auto actual = Capture(handle, Addresses(expected));
    if (!SameOpinions(actual, expected))
    {
        WriteError(error, "Affected authored opinion/declaration changed.");
        return OPENUSD_EDIT_CONFLICT;
    }
    Check(desired.opinions.size() == actual.opinions.size(), "Restore address count differs.");
    for (size_t i = 0; i < desired.opinions.size(); ++i)
    {
        const auto& target = desired.opinions[i];
        const auto& current = actual.opinions[i];
        Check(target.address == current.address, "Restore address order differs.");
        ValidateOpinion(target);
        if (current.kind && target.kind)
        {
            Check(SameDeclaration(current, target),
                "Replay cannot replace an existing property's declaration.");
        }
        if (current.kind && !target.kind && !CanDelete(record, actual, target.address.path))
        {
            WriteError(error, "Deleting the owned property would erase foreign or unaddressed opinions.");
            return OPENUSD_EDIT_CONFLICT;
        }
        for (const auto& other : desired.opinions)
        {
            Check(target.address.path != other.address.path || SameDeclaration(target, other),
                "Inconsistent restore declarations for one property.");
        }
    }
    std::set<SdfPath> newSpecs;
    std::map<SdfPath, std::set<TfToken>> addedFields;
    std::map<SdfPath, size_t> projectedSamples;
    for (size_t index = 0; index < desired.opinions.size(); ++index)
    {
        const auto& opinion = desired.opinions[index];
        if (opinion.kind)
        {
            auto path = opinion.address.path;
            while (!path.IsEmpty())
            {
                if (!data->HasSpec(path)) { newSpecs.insert(path); }
                if (!path.IsAbsoluteRootPath())
                {
                    const auto key = path.IsPropertyPath()
                        ? SdfChildrenKeys->PropertyChildren : SdfChildrenKeys->PrimChildren;
                    if (!data->Has(path.GetParentPath(), key, static_cast<VtValue*>(nullptr)))
                    {
                        addedFields[path.GetParentPath()].insert(key);
                    }
                }
                path = path.GetParentPath();
            }
            if (!data->Has(opinion.address.path, Field(opinion.address), static_cast<VtValue*>(nullptr))
                && !opinion.value.IsEmpty())
            {
                addedFields[opinion.address.path].insert(Field(opinion.address));
            }
        }
        if (opinion.address.field == 1)
        {
            auto inserted = projectedSamples.try_emplace(opinion.address.path, 0);
            size_t& count = inserted.first->second;
            if (inserted.second)
            {
                VtValue samples;
                if (data->Has(opinion.address.path, SdfFieldKeys->TimeSamples, &samples))
                {
                    Check(samples.IsHolding<SdfTimeSampleMap>()
                        && samples.UncheckedGet<SdfTimeSampleMap>().size() <= MaxItems,
                        "Time-sample mutation inventory exceeds budget or has unsupported storage.");
                    count = samples.UncheckedGet<SdfTimeSampleMap>().size();
                }
            }
            const bool before = !actual.opinions[index].value.IsEmpty();
            const bool after = !opinion.value.IsEmpty();
            if (!before && after)
            {
                Check(count < MaxItems, "Time-sample batch would exceed the checkpoint inventory budget.");
                ++count;
            }
            else if (before && !after)
            {
                Check(count != 0, "Time-sample batch inventory is inconsistent.");
                --count;
            }
        }
    }
    Check(counted.Inventory().size() + newSpecs.size() <= MaxSpecs
        && record.owned.size() + newSpecs.size() <= MaxSpecs, "Owned editing spec budget exceeded.");
    for (const auto& added : addedFields)
    {
        Check(counted.FieldCount(added.first) + added.second.size() <= MaxFields,
            "Mutation would exceed the resident field inventory budget.");
    }
    AdmitAncestors(data, desired);
    const auto parentInventories = CaptureParentInventories(data, actual);
    desired.identity = actual.identity;
    const auto oldOwned = record.owned;
    auto plannedOwned = oldOwned;
    for (const auto& spec : newSpecs) { plannedOwned.try_emplace(spec, false); }
    const bool wasSaved = record.savedRevision == record.revision;
    auto afterBytes = desired.Bytes();
    openusd_edit_buffer* pending = nullptr;
    openusd_edit_buffer_view pendingView{};
    Publish(std::move(afterBytes), &pending, &pendingView);
    std::unique_ptr<openusd_edit_buffer, decltype(&openusd_edit_buffer_release)>
        pendingOwner(pending, openusd_edit_buffer_release);
    TfErrorMark mark;
    record.owned.swap(plannedOwned);
    bool rolledBack = false;
    record.writing = true;
    struct WritingReset
    {
        bool& flag;
        ~WritingReset() { flag = false; }
    } writingReset{record.writing};
    try
    {
        {
            SdfChangeBlock block;
            try
            {
                [[maybe_unused]] size_t written = 0;
                for (const auto& opinion : desired.opinions)
                {
                    WriteOpinion(record, opinion);
                    ++written;
                    Check(mark.IsClean(), "OpenUSD rejected an editing write.");
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
                    if (failAfter > 0 && written >= static_cast<size_t>(failAfter))
                    {
                        failAfter = -1;
                        throw std::runtime_error("Injected partial editing write failure.");
                    }
#endif
                }
                const auto verified = Capture(handle, Addresses(desired));
                if (!SameOpinions(verified, desired))
                {
                    const auto expectedBytes = desired.Bytes();
                    const auto actualBytes = verified.Bytes();
                    size_t index = 44;
                    while (index < expectedBytes.size() && index < actualBytes.size()
                        && expectedBytes[index] == actualBytes[index]) { ++index; }
                    throw std::runtime_error("OpenUSD authored state differs from mutation at byte "
                        + std::to_string(index) + " (actual "
                        + (index < actualBytes.size() ? std::to_string(actualBytes[index]) : "end")
                        + ", expected "
                        + (index < expectedBytes.size() ? std::to_string(expectedBytes[index]) : "end") + ").");
                }
                CleanupAncestors(record);
                Check(mark.IsClean(), "OpenUSD rejected owned spec cleanup.");
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
                if (failAfter == 0)
                {
                    failAfter = -1;
                    throw std::runtime_error("Injected owned ancestor cleanup failure.");
                }
#endif
            }
            catch (...)
            {
                ConsumeErrors(mark);
                for (const auto& opinion : actual.opinions) { WriteOpinion(record, opinion); }
                CleanupAncestors(record, &newSpecs);
                RestoreParentInventories(record, parentInventories);
                record.owned = oldOwned;
                Check(mark.IsClean() && SameOpinions(Capture(handle, Addresses(actual)), actual),
                    "Editing rollback failed; target requires explicit recovery.");
                rolledBack = true;
                throw;
            }
        }
        Check(mark.IsClean(), "OpenUSD rejected editing change notification.");
        record.writing = false;
    }
    catch (...)
    {
        if (!rolledBack)
        {
            ConsumeErrors(mark);
            {
                SdfChangeBlock rollback;
                for (const auto& opinion : actual.opinions) { WriteOpinion(record, opinion); }
                CleanupAncestors(record, &newSpecs);
                RestoreParentInventories(record, parentInventories);
                record.owned = oldOwned;
            }
            Check(mark.IsClean() && SameOpinions(Capture(handle, Addresses(actual)), actual),
                "Editing rollback failed after notification; explicit recovery is required.");
        }
        record.writing = false;
        Check(SameParentInventories(Resident(record.layer), parentInventories),
            "Editing rollback changed parent inventories after notification; explicit recovery is required.");
        if (wasSaved) { record.savedRevision = record.revision; }
        throw;
    }
    auto* mutableBytes = const_cast<uint8_t*>(pendingView.data);
    for (int i = 0; i < 8; ++i)
    {
        mutableBytes[36 + i] = static_cast<uint8_t>(record.revision >> (i * 8));
    }
    *owner = pendingOwner.release();
    *view = pendingView;
    return OPENUSD_EDIT_APPLIED;
}
}
}

openusd_status openusd_layer_edit_apply(
    const openusd_layer* layer, const uint8_t* expected, size_t expected_size,
    const uint8_t* mutations, size_t mutations_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner);
        ResetAbiOutput(view);
        ResetAbiOutput(outcome);
        return OpenUsdEdit::GuardBuffer(layer, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            OpenUsdEdit::Check(outcome != nullptr && IsAligned(outcome), "An aligned editing outcome is required.");
            *outcome = OPENUSD_EDIT_CONFLICT;
            TfErrorMark validation;
            const auto before = OpenUsdEdit::Snapshot::Read(expected, expected_size);
            auto desired = OpenUsdEdit::Mutations(before, mutations, mutations_size);
            if (!validation.IsClean())
            {
                ConsumeErrors(validation);
                throw std::runtime_error("Invalid native mutation value; no writes performed.");
            }
            *outcome = OpenUsdEdit::Execute(layer, before, std::move(desired), owner, view, error);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_layer_edit_restore(
    const openusd_layer* layer, const uint8_t* expected, size_t expected_size,
    const uint8_t* restore, size_t restore_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner);
        ResetAbiOutput(view);
        ResetAbiOutput(outcome);
        return OpenUsdEdit::GuardBuffer(layer, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            OpenUsdEdit::Check(outcome != nullptr && IsAligned(outcome), "An aligned editing outcome is required.");
            *outcome = OPENUSD_EDIT_CONFLICT;
            TfErrorMark validation;
            const auto before = OpenUsdEdit::Snapshot::Read(expected, expected_size);
            auto desired = OpenUsdEdit::Snapshot::Read(restore, restore_size);
            if (!validation.IsClean())
            {
                ConsumeErrors(validation);
                throw std::runtime_error("Invalid native snapshot value; no writes performed.");
            }
            auto& record = OpenUsdEdit::Record(layer);
            if (!OpenUsdEdit::Matches(desired.identity, layer->stage, record))
            {
                *outcome = OPENUSD_EDIT_STALE_TARGET;
            }
            else
            {
                *outcome = OpenUsdEdit::Execute(layer, before, std::move(desired), owner, view, error);
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_layer_edit_test_fail_after(int32_t writes)
{
    OpenUsdEdit::failAfter = writes;
}
#endif
