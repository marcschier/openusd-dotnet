// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"
#include "internal/review_inspection_schema.h"

namespace OpenUsdReview
{
namespace
{
// SdfData destruction unconditionally queues WorkSwapDestroyAsync, including
// for empty data. Inspection must neither start those workers nor let deferred
// destruction escape its no-I/O call, so use synchronous, bounded local data.
class InspectionData
{
public:
    bool HasSpec(const SdfPath& path) const { return specs.count(path) != 0; }
    void CreateSpec(const SdfPath& path, SdfSpecType type)
    {
        specs.emplace(path, Spec{type, {}});
        inventory.emplace(path, 0);
    }
    SdfSpecType GetSpecType(const SdfPath& path) const
    {
        const auto found = specs.find(path);
        return found == specs.end() ? SdfSpecTypeUnknown : found->second.type;
    }
    bool Has(const SdfPath& path, const TfToken& field, VtValue* value) const
    {
        const auto spec = specs.find(path);
        if (spec == specs.end()) { return false; }
        const auto found = spec->second.fields.find(field);
        if (found == spec->second.fields.end()) { return false; }
        if (value) { *value = found->second; }
        return true;
    }
    VtValue Get(const SdfPath& path, const TfToken& field) const
    {
        VtValue value;
        Has(path, field, &value);
        return value;
    }
    void Set(const SdfPath& path, const TfToken& field, const VtValue& value)
    {
        auto& fields = specs.at(path).fields;
        fields[field] = value;
        inventory.at(path) = fields.size();
    }
    TfTokenVector List(const SdfPath& path) const
    {
        TfTokenVector result;
        const auto spec = specs.find(path);
        if (spec != specs.end())
        {
            result.reserve(spec->second.fields.size());
            for (const auto& field : spec->second.fields) { result.push_back(field.first); }
        }
        return result;
    }
    const std::map<SdfPath, size_t>& Inventory() const { return inventory; }

private:
    struct Spec
    {
        SdfSpecType type;
        std::map<TfToken, VtValue> fields;
    };
    std::map<SdfPath, Spec> specs;
    std::map<SdfPath, size_t> inventory;
};

const std::type_info* ValueType(const TfToken& name, bool inspectionOnly)
{
    if (inspectionOnly) { return InspectionValueType(name); }
    const auto type = SdfSchema::GetInstance().FindType(name);
    return type ? &type.GetType().GetTypeid() : nullptr;
}

void SpecPath(const SdfPath& path, SdfSpecType type)
{
    Check(path.IsAbsolutePath()
        && path.GetPathElementCount() <= OpenUsdEdit::MaxDepth, "Unsupported portable review spec path.");
    Check((path.IsAbsoluteRootPath() && type == SdfSpecTypePseudoRoot)
        || (path.IsPrimPath() && !path.IsAbsoluteRootPath() && type == SdfSpecTypePrim)
        || (path.IsPropertyPath() && (type == SdfSpecTypeAttribute || type == SdfSpecTypeRelationship))
        || (path.IsPrimVariantSelectionPath() && (type == SdfSpecTypeVariant || type == SdfSpecTypeVariantSet)),
        "Unsupported portable review spec kind.");
    if (path.IsPrimVariantSelectionPath())
    {
        const auto selection = path.GetVariantSelection();
        Check(SdfPath::IsValidIdentifier(selection.first)
            && ((type == SdfSpecTypeVariantSet && selection.second.empty())
                || (type == SdfSpecTypeVariant && !selection.second.empty())),
            "Variant spec path does not match its kind.");
    }
}
void Field(const TfToken& field, const VtValue& value, SdfSpecType type, bool inspectionOnly)
{
    Check(!field.IsEmpty() && !value.IsEmpty(), "Portable review field/name cannot be empty.");
    Check(field != TfToken("clips") && field != TfToken("clipSets"),
        "Value clips/templates are not in the portable filesystem domain.");
    if (inspectionOnly) { ValidateInspectionField(field, value, type); }
    else
    {
        const auto& schema = SdfSchema::GetInstance();
        if (const auto* definition = schema.GetFieldDefinition(field))
        {
            Check(schema.IsValidFieldForSpec(field, type), "Portable field is invalid for its spec kind.");
            const auto& fallback = definition->GetFallbackValue();
            Check(fallback.IsEmpty() || fallback.GetTypeid() == value.GetTypeid(), "Portable field concrete type differs from its native schema.");
            Check((field == SdfFieldKeys->Default && value.IsHolding<SdfValueBlock>()) || definition->IsValidValue(value),
                "Portable field value is invalid for the native schema.");
        }
    }
    VisitAssets(value, [](const std::string& raw, bool)
    {
        if (!raw.empty()) { CheckFilesystemPath(raw); }
    });
}
template <class Data>
void Topology(const Data& data, bool inspectionOnly)
{
    Check(data->GetSpecType(SdfPath::AbsoluteRootPath()) == SdfSpecTypePseudoRoot, "Portable review has no pseudo-root.");
    for (const auto& spec : data->Inventory())
    {
        const auto type = data->GetSpecType(spec.first);
        SpecPath(spec.first, type);
        if (!spec.first.IsAbsoluteRootPath())
        {
            auto parent = spec.first.GetParentPath();
            auto key = spec.first.IsPropertyPath() ? SdfChildrenKeys->PropertyChildren : SdfChildrenKeys->PrimChildren;
            auto name = spec.first.GetNameToken();
            if (type == SdfSpecTypeVariantSet)
            {
                key = SdfChildrenKeys->VariantSetChildren;
                name = TfToken(spec.first.GetVariantSelection().first);
            }
            else if (type == SdfSpecTypeVariant)
            {
                const auto selection = spec.first.GetVariantSelection();
                parent = parent.AppendVariantSelection(selection.first, "");
                key = SdfChildrenKeys->VariantChildren;
                name = TfToken(selection.second);
            }
            const auto parentType = data->GetSpecType(parent);
            Check(parentType == SdfSpecTypePrim || parentType == SdfSpecTypePseudoRoot
                || parentType == SdfSpecTypeVariant || (type == SdfSpecTypeVariant && parentType == SdfSpecTypeVariantSet),
                "Portable parent spec is missing.");
            const auto children = data->Get(parent, key);
            Check(children.template IsHolding<TfTokenVector>(), "Portable parent child-name inventory is missing.");
            const auto& names = children.template UncheckedGet<TfTokenVector>();
            Check(std::count(names.begin(), names.end(), name) == 1,
                "Portable parent inventory must uniquely contain its child.");
        }
        for (const auto& key : {SdfChildrenKeys->PrimChildren, SdfChildrenKeys->PropertyChildren,
            SdfChildrenKeys->VariantSetChildren, SdfChildrenKeys->VariantChildren})
        {
            const auto children = data->Get(spec.first, key);
            if (children.IsEmpty()) { continue; }
            Check(children.template IsHolding<TfTokenVector>(), "Invalid portable child inventory.");
            std::set<TfToken> unique;
            for (const auto& name : children.template UncheckedGet<TfTokenVector>())
            {
                const bool property = key == SdfChildrenKeys->PropertyChildren;
                if (key != SdfChildrenKeys->VariantChildren)
                {
                    Check(property ? SdfPath::IsValidNamespacedIdentifier(name.GetString()) : SdfPath::IsValidIdentifier(name.GetString()),
                        "Invalid portable child name.");
                }
                auto child = SdfPath::EmptyPath();
                if (key == SdfChildrenKeys->VariantSetChildren)
                {
                    Check(type == SdfSpecTypePrim || type == SdfSpecTypeVariant, "Variant-set inventory requires a prim owner.");
                    child = spec.first.AppendVariantSelection(name.GetString(), "");
                    Check(data->GetSpecType(child) == SdfSpecTypeVariantSet, "Variant-set child spec kind mismatch.");
                }
                else if (key == SdfChildrenKeys->VariantChildren)
                {
                    Check(type == SdfSpecTypeVariantSet, "Variant inventory requires a variant-set owner.");
                    Check(!name.IsEmpty() && SdfPath::IsValidPathString(spec.first.GetParentPath().GetString()
                        + "{" + spec.first.GetVariantSelection().first + "=" + name.GetString() + "}"),
                        "Invalid portable variant child name.");
                    child = spec.first.GetParentPath().AppendVariantSelection(spec.first.GetVariantSelection().first, name.GetString());
                    Check(data->GetSpecType(child) == SdfSpecTypeVariant, "Variant child spec kind mismatch.");
                }
                else { child = property ? spec.first.AppendProperty(name) : spec.first.AppendChild(name); }
                Check(unique.insert(name).second && data->HasSpec(child), "Duplicate or missing portable child.");
            }
        }
        if (type == SdfSpecTypeAttribute)
        {
            const auto name = data->Get(spec.first, SdfFieldKeys->TypeName);
            Check(name.template IsHolding<TfToken>(), "Portable attribute type declaration is missing.");
            const auto* valueType = ValueType(name.template UncheckedGet<TfToken>(), inspectionOnly);
            Check(valueType != nullptr, "Portable attribute declaration is unsupported.");
            const auto valid = [&](const VtValue& value)
            {
                Check(value.IsEmpty() || value.IsHolding<SdfValueBlock>()
                    || value.GetTypeid() == *valueType,
                    "Portable attribute value does not match its declaration.");
            };
            valid(data->Get(spec.first, SdfFieldKeys->Default));
            const auto samples = data->Get(spec.first, SdfFieldKeys->TimeSamples);
            if (!samples.IsEmpty())
            {
                Check(samples.template IsHolding<SdfTimeSampleMap>(), "Invalid portable sample storage.");
                for (const auto& sample : samples.template UncheckedGet<SdfTimeSampleMap>()) { valid(sample.second); }
            }
        }
    }
}
template <class T>
void ListAssets(const SdfListOp<T>& list, const std::function<void(const std::string&, bool)>& visit, size_t depth)
{
    for (const auto* items : {&list.GetExplicitItems(), &list.GetAddedItems(), &list.GetPrependedItems(),
        &list.GetAppendedItems(), &list.GetDeletedItems(), &list.GetOrderedItems()})
    {
        Check(items->size() <= OpenUsdEdit::MaxItems, "Composition list item budget exceeded.");
        for (const auto& item : *items)
        {
            if (!item.GetAssetPath().empty()) { visit(item.GetAssetPath(), true); }
            if constexpr (std::is_same_v<T, SdfReference>) { VisitAssets(VtValue(item.GetCustomData()), visit, depth + 1); }
        }
    }
}
}
void VisitAssets(const VtValue& value, const std::function<void(const std::string&, bool)>& visit, size_t depth)
{
    Check(depth < 16, "Portable asset/value nesting exceeds 16.");
    if (value.IsHolding<SdfAssetPath>())
    {
        const auto& path = value.UncheckedGet<SdfAssetPath>();
        Check(path.GetEvaluatedPath().empty() && path.GetResolvedPath().empty(),
            "Portable authored asset paths cannot contain resolver/evaluation cache payloads.");
        if (!path.GetAuthoredPath().empty()) { visit(path.GetAuthoredPath(), false); }
    }
    else if (value.IsHolding<VtArray<SdfAssetPath>>())
    {
        const auto& paths = value.UncheckedGet<VtArray<SdfAssetPath>>();
        Check(paths.size() <= OpenUsdEdit::MaxItems, "Asset-array budget exceeded.");
        for (const auto& path : paths) { VisitAssets(VtValue(path), visit, depth + 1); }
    }
    else if (value.IsHolding<VtDictionary>())
    {
        const auto& dictionary = value.UncheckedGet<VtDictionary>();
        Check(dictionary.size() <= OpenUsdEdit::MaxItems, "Asset dictionary budget exceeded.");
        for (const auto& item : dictionary) { VisitAssets(item.second, visit, depth + 1); }
    }
    else if (value.IsHolding<SdfTimeSampleMap>())
    {
        const auto& samples = value.UncheckedGet<SdfTimeSampleMap>();
        Check(samples.size() <= OpenUsdEdit::MaxItems, "Asset sample budget exceeded.");
        for (const auto& item : samples) { VisitAssets(item.second, visit, depth + 1); }
    }
    else if (value.IsHolding<SdfReferenceListOp>()) { ListAssets(value.UncheckedGet<SdfReferenceListOp>(), visit, depth); }
    else if (value.IsHolding<SdfPayloadListOp>()) { ListAssets(value.UncheckedGet<SdfPayloadListOp>(), visit, depth); }
    else
    {
        Check(!value.IsHolding<SdfUnregisteredValue>() && !value.IsArrayEditValued(),
            "Unregistered/array-edit authored values require explicit conversion before portable persistence.");
    }
}
namespace
{
template <class Data>
Bytes EncodeReviewData(const Data& data, bool inspectionOnly)
{
    OpenUsdEdit::Writer writer;
    writer.portable = true;
    writer.U32(0x31575652); writer.U32(1);
    const auto& inventory = data->Inventory();
    Check(inventory.size() <= OpenUsdEdit::MaxSpecs, "Portable review exceeds 4096 specs.");
    writer.U32(static_cast<uint32_t>(inventory.size()));
    for (const auto& spec : inventory)
    {
        Check(spec.second <= OpenUsdEdit::MaxFields, "Portable review exceeds 128 fields per spec.");
        const auto type = data->GetSpecType(spec.first); SpecPath(spec.first, type);
        writer.Text(spec.first.GetString()); writer.U32(static_cast<uint32_t>(type));
        auto fields = data->List(spec.first);
        Check(fields.size() == spec.second, "Portable review field inventory was invalidated.");
        std::sort(fields.begin(), fields.end(), [](const TfToken& a, const TfToken& b) { return a.GetString() < b.GetString(); });
        writer.U32(static_cast<uint32_t>(fields.size()));
        for (const auto& field : fields)
        {
            const auto value = data->Get(spec.first, field);
            writer.Text(field.GetString()); OpenUsdEdit::EncodeValue(writer, value); Field(field, value, type, inspectionOnly);
        }
    }
    Topology(data, inspectionOnly);
    return std::move(writer.bytes);
}
template <class CreateData>
auto DecodeReviewData(const uint8_t* bytes, size_t size, bool inspectionOnly, CreateData createData)
{
    OpenUsdEdit::Reader reader(bytes, size, true);
    Check(reader.U32() == 0x31575652 && reader.U32() == 1, "Unsupported portable typed review encoding.");
    const auto count = reader.Count(OpenUsdEdit::MaxSpecs);
    reader.Need(static_cast<size_t>(count) * 12);
    auto data = createData();
    for (uint32_t i = 0; i < count; ++i)
    {
        const auto text = reader.Text();
        OpenUsdEdit::AdmitPortablePathText(text);
        Check(!text.empty() && SdfPath::IsValidPathString(text), "Invalid portable spec path.");
        const SdfPath path(text);
        const auto type = static_cast<SdfSpecType>(reader.Count(SdfNumSpecTypes - 1));
        SpecPath(path, type);
        Check(!data->HasSpec(path), "Duplicate portable review spec.");
        data->CreateSpec(path, type);
        const auto fields = reader.Count(OpenUsdEdit::MaxFields);
        reader.Need(static_cast<size_t>(fields) * 8);
        for (uint32_t f = 0; f < fields; ++f)
        {
            const TfToken field(reader.Text());
            Check(!data->Has(path, field, static_cast<VtValue*>(nullptr)), "Duplicate portable field.");
            auto value = OpenUsdEdit::DecodeValue(reader); Field(field, value, type, inspectionOnly);
            data->Set(path, field, value);
        }
    }
    reader.End(); Topology(data, inspectionOnly);
    const auto canonical = EncodeReviewData(data, inspectionOnly);
    Check(canonical.size() == size && std::memcmp(canonical.data(), bytes, size) == 0,
        "Noncanonical portable typed content/order is not supported.");
    return data;
}
}
Bytes EncodeReview(const TfRefPtr<CountedData>& data, bool inspectionOnly)
{
    return EncodeReviewData(data, inspectionOnly);
}
TfRefPtr<CountedData> DecodeReview(const uint8_t* bytes, size_t size, bool inspectionOnly)
{
    if (inspectionOnly)
    {
        // The inspection caller only validates the review and exposes metadata;
        // it must not materialize/retain SdfData or its asynchronous destructor.
        DecodeReviewData(bytes, size, true, [] { return std::make_unique<InspectionData>(); });
        return {};
    }
    return DecodeReviewData(bytes, size, false, [] { return TfCreateRefPtr(new CountedData); });
}
Bytes StateBytes(const openusd_stage* stage, OpenUsdEdit::LayerRecord& record)
{
    OpenUsdEdit::Writer writer;
    writer.Header(5); OpenUsdEdit::GetIdentity(stage, record).Write(writer); writer.U32(record.role);
    writer.U32((record.layer->IsAnonymous() ? 1u : 0u)
        | (record.savedRevision != record.revision ? 2u : 0u) | (record.layer->IsDirty() ? 4u : 0u)
        | (record.layer->PermissionToEdit() ? 8u : 0u) | (record.layer->PermissionToSave() ? 16u : 0u)
        | (record.local ? 32u : 0u));
    writer.Text(record.layer->GetIdentifier()); writer.Text(record.layer->GetRealPath());
    writer.Text(record.layer->GetResolvedPath().GetPathString()); writer.Text(record.layer->GetResolvedPath().GetPathString());
    return std::move(writer.bytes);
}
}
