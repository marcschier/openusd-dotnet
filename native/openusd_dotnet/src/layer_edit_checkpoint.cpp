// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/layer_edit.h"
#include "pxr/usd/sdf/changeBlock.h"

#include <filesystem>
#include <ostream>
#include <streambuf>

namespace OpenUsdEdit
{
namespace
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
thread_local int checkpointFailAfter = -1;

void CheckpointFailpoint(int phase)
{
    if (checkpointFailAfter == phase)
    {
        checkpointFailAfter = -1;
        throw std::runtime_error("Injected checkpoint installation failure.");
    }
}
#endif

std::string Anchor(const SdfLayerRefPtr& layer)
{
    const auto& path = layer->GetResolvedPath().GetPathString();
    Check(path.size() <= MaxString, "Layer anchor byte budget exceeded.");
    // Anonymous layers have no filesystem anchor. Do not pretend that the
    // root layer's location is their asset resolution anchor.
    return path;
}

void CheckAsset(const VtValue& value, size_t depth = 0)
{
    Check(depth < 16, "Checkpoint asset nesting budget exceeded.");
    if (value.IsHolding<SdfAssetPath>())
    {
        const auto& path = value.UncheckedGet<SdfAssetPath>();
        const auto& authored = path.GetAuthoredPath();
        Check(path.GetEvaluatedPath().empty() && path.GetResolvedPath().empty(),
            "Checkpoint export cannot preserve evaluated/resolved asset-path caches.");
        Check(authored.empty() || (authored.find('`') == std::string::npos
            && std::filesystem::u8path(authored).is_absolute()),
            "Relative or expression asset paths require a validated anchor rebind, which is not supported.");
    }
    else if (value.IsHolding<VtDictionary>())
    {
        for (const auto& item : value.UncheckedGet<VtDictionary>()) { CheckAsset(item.second, depth + 1); }
    }
    else if (value.IsHolding<SdfTimeSampleMap>())
    {
        for (const auto& item : value.UncheckedGet<SdfTimeSampleMap>()) { CheckAsset(item.second, depth + 1); }
    }
}

void ValidSpecPath(const SdfPath& path, SdfSpecType type)
{
    Check(path.IsAbsolutePath() && !path.ContainsPrimVariantSelection()
        && path.GetPathElementCount() <= MaxDepth, "Unsupported checkpoint spec path.");
    Check((path.IsAbsoluteRootPath() && type == SdfSpecTypePseudoRoot)
        || (path.IsPrimPath() && !path.IsAbsoluteRootPath() && type == SdfSpecTypePrim)
        || (path.IsPropertyPath() && (type == SdfSpecTypeAttribute || type == SdfSpecTypeRelationship)),
        "Checkpoint supports pseudo-root, prim, attribute and relationship specs only.");
}

void ValidField(const TfToken& field, const VtValue& value, SdfSpecType type)
{
    Check(!value.IsEmpty(), "Checkpoint field cannot hold an empty VtValue.");
    const auto& schema = SdfSchema::GetInstance();
    if (const auto* definition = schema.GetFieldDefinition(field))
    {
        Check(schema.IsValidFieldForSpec(field, type), "Checkpoint field is invalid for its spec kind.");
        const auto& fallback = definition->GetFallbackValue();
        Check(fallback.IsEmpty() || fallback.GetTypeid() == value.GetTypeid(),
            "Checkpoint field concrete type differs from the native schema.");
        if (!(field == SdfFieldKeys->Default && value.IsHolding<SdfValueBlock>())
            && !definition->IsValidValue(value))
        {
            throw std::runtime_error("Invalid checkpoint field value: " + field.GetString());
        }
    }
    if (field == SdfFieldKeys->SubLayers)
    {
        Check(value.IsHolding<std::vector<std::string>>(), "Unsupported sublayer paths.");
        for (const auto& path : value.UncheckedGet<std::vector<std::string>>())
        {
            Check(std::filesystem::u8path(path).is_absolute(),
                "Relative sublayers require an unsupported anchor rebind.");
        }
    }
    CheckAsset(value);
}

struct Document
{
    Identity identity;
    std::string identifier;
    std::string anchor;
    TfRefPtr<CountedData> data;

    std::vector<uint8_t> Bytes() const
    {
        Writer w;
        w.Header(4);
        identity.Write(w);
        w.Text(identifier);
        w.Text(anchor);
        const auto& inventory = data->Inventory();
        Check(inventory.size() <= MaxSpecs, "Checkpoint spec count budget exceeded (4096).");
        w.U32(static_cast<uint32_t>(inventory.size()));
        for (const auto& spec : inventory)
        {
            Check(spec.second <= MaxFields, "Checkpoint field count budget exceeded (128 per spec).");
            const auto type = data->GetSpecType(spec.first);
            ValidSpecPath(spec.first, type);
            w.Text(spec.first.GetString());
            w.U32(static_cast<uint32_t>(type));
            auto fields = data->List(spec.first);
            Check(fields.size() == spec.second, "Review field inventory was invalidated; checkpoint refused.");
            std::sort(fields.begin(), fields.end(), [](const TfToken& a, const TfToken& b)
            {
                return a.GetString() < b.GetString();
            });
            w.U32(static_cast<uint32_t>(fields.size()));
            for (const auto& field : fields)
            {
                w.Text(field.GetString());
                VtValue value;
                Check(data->Has(spec.first, field, &value), "Checkpoint field disappeared.");
                EncodeValue(w, value);
                ValidField(field, value, type);
            }
        }
        ValidateTopology();
        return std::move(w.bytes);
    }

    void ValidateTopology() const
    {
        Check(data->GetSpecType(SdfPath::AbsoluteRootPath()) == SdfSpecTypePseudoRoot,
            "Checkpoint pseudo-root is missing.");
        for (const auto& spec : data->Inventory())
        {
            const auto type = data->GetSpecType(spec.first);
            ValidSpecPath(spec.first, type);
            if (!spec.first.IsAbsoluteRootPath())
            {
                const SdfPath parent = spec.first.GetParentPath();
                const auto parentType = data->GetSpecType(parent);
                Check(parentType == SdfSpecTypePrim || parentType == SdfSpecTypePseudoRoot,
                    "Checkpoint parent spec is missing.");
                const auto key = spec.first.IsPropertyPath()
                    ? SdfChildrenKeys->PropertyChildren : SdfChildrenKeys->PrimChildren;
                VtValue children;
                Check(data->Has(parent, key, &children) && children.IsHolding<TfTokenVector>(),
                    "Checkpoint parent child-name field is missing.");
                const auto& names = children.UncheckedGet<TfTokenVector>();
                Check(std::count(names.begin(), names.end(), spec.first.GetNameToken()) == 1,
                    "Checkpoint parent inventory does not uniquely contain its child.");
            }
            for (const auto& key : {SdfChildrenKeys->PrimChildren, SdfChildrenKeys->PropertyChildren})
            {
                VtValue children;
                if (!data->Has(spec.first, key, &children)) { continue; }
                Check(children.IsHolding<TfTokenVector>(), "Invalid checkpoint child vector.");
                const auto& names = children.UncheckedGet<TfTokenVector>();
                std::set<TfToken> unique;
                for (const auto& name : names)
                {
                    const bool property = key == SdfChildrenKeys->PropertyChildren;
                    Check(property ? SdfPath::IsValidNamespacedIdentifier(name.GetString())
                        : SdfPath::IsValidIdentifier(name.GetString()), "Invalid checkpoint child name.");
                    const SdfPath child = property ? spec.first.AppendProperty(name) : spec.first.AppendChild(name);
                    Check(unique.insert(name).second && data->HasSpec(child), "Duplicate or missing checkpoint child.");
                }
            }
            if (type == SdfSpecTypeAttribute)
            {
                const auto name = data->Get(spec.first, SdfFieldKeys->TypeName);
                Check(name.IsHolding<TfToken>()
                    && SdfSchema::GetInstance().FindType(name.UncheckedGet<TfToken>()),
                    "Checkpoint attribute declaration is unsupported.");
                const auto valueType = SdfSchema::GetInstance().FindType(name.UncheckedGet<TfToken>());
                const auto validValue = [&](const VtValue& value)
                {
                    Check(value.IsEmpty() || value.IsHolding<SdfValueBlock>()
                        || value.GetTypeid() == valueType.GetType().GetTypeid(),
                        "Checkpoint attribute value does not match its declaration.");
                };
                validValue(data->Get(spec.first, SdfFieldKeys->Default));
                const auto samples = data->Get(spec.first, SdfFieldKeys->TimeSamples);
                if (!samples.IsEmpty())
                {
                    Check(samples.IsHolding<SdfTimeSampleMap>(), "Unsupported checkpoint samples.");
                    for (const auto& sample : samples.UncheckedGet<SdfTimeSampleMap>())
                    {
                        validValue(sample.second);
                    }
                }
            }
        }
    }

    static Document Read(const uint8_t* bytes, size_t size)
    {
        Reader r(bytes, size);
        r.Header(4);
        Document document;
        document.identity = Identity::Read(r);
        document.identifier = r.Text();
        document.anchor = r.Text();
        document.data = TfCreateRefPtr(new CountedData);
        const uint32_t count = r.Count(MaxSpecs);
        r.Need(count * 12ull);
        for (uint32_t i = 0; i < count; ++i)
        {
            const auto pathText = r.Text();
            Check(!pathText.empty() && SdfPath::IsValidPathString(pathText), "Invalid checkpoint spec path.");
            const SdfPath path(pathText);
            const auto type = static_cast<SdfSpecType>(r.Count(SdfNumSpecTypes - 1));
            ValidSpecPath(path, type);
            Check(!document.data->HasSpec(path), "Duplicate checkpoint spec.");
            document.data->CreateSpec(path, type);
            const uint32_t fields = r.Count(MaxFields);
            r.Need(fields * 8ull);
            for (uint32_t f = 0; f < fields; ++f)
            {
                const TfToken field(r.Text());
                Check(!field.IsEmpty() && !document.data->Has(path, field, static_cast<VtValue*>(nullptr)),
                    "Empty or duplicate checkpoint field.");
                auto value = DecodeValue(r);
                ValidField(field, value, type);
                document.data->Set(path, field, value);
            }
        }
        r.End();
        document.ValidateTopology();
        return document;
    }
};

Document CaptureDocument(const openusd_layer* layer)
{
    auto& record = Record(layer);
    Check(record.local && record.role == 2, "Checkpoint capture requires the local owned user-review layer.");
    const auto data = Resident(record.layer);
    Check(DataAccess::ConcreteType(data) == typeid(CountedData),
        "Bounded checkpoint inventory is unavailable for this imported/normalized layer; no unbounded preparation is performed.");
    Document document;
    document.identity = GetIdentity(layer->stage, record);
    Check(record.layer->GetIdentifier().size() <= MaxString, "Checkpoint identifier byte budget exceeded.");
    document.identifier = record.layer->GetIdentifier();
    document.anchor = Anchor(record.layer);
    // A retained typed reference is safe: this is our exact concrete class,
    // never a private-layout cast to an OpenUSD backend.
    document.data = TfCreateRefPtrFromProtectedWeakPtr(
        TfWeakPtr<CountedData>(const_cast<CountedData*>(static_cast<const CountedData*>(get_pointer(data)))));
    return document;
}

bool SameDocument(Document a, const Document& b)
{
    a.identity = b.identity;
    return a.Bytes() == b.Bytes();
}

void InstallDocumentExact(const openusd_layer* handle, const Document& desired)
{
    auto& record = Record(handle);
    auto empty = TfCreateRefPtr(new CountedData);
    empty->CreateSpec(SdfPath::AbsoluteRootPath(), SdfSpecTypePseudoRoot);
    // This is an explicit document replacement, never per-edit undo. Removing the old
    // opinions first prevents Sdf's numerical equality from eliding bit-distinct data.
    DataAccess::Install(get_pointer(record.layer), empty);
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    CheckpointFailpoint(1);
#endif
    DataAccess::Install(get_pointer(record.layer), desired.data);
    auto installed = CaptureDocument(handle);
    Check(installed.data != desired.data, "Checkpoint installation requires an independent parsed document.");
    // The layer operation above records the complete topology resync. Before its enclosing
    // change block is released, install raw counted fields so declaration fallback equality
    // and numerical equality cannot suppress authored presence or IEEE payload bits.
    std::vector<SdfPath> oldPaths;
    Check(installed.data->Inventory().size() <= MaxSpecs,
        "Installed checkpoint topology exceeds the admitted spec budget.");
    oldPaths.reserve(installed.data->Inventory().size());
    for (const auto& spec : installed.data->Inventory()) { oldPaths.push_back(spec.first); }
    for (const auto& path : oldPaths) { installed.data->EraseSpec(path); }
    for (const auto& spec : desired.data->Inventory())
    {
        installed.data->CreateSpec(spec.first, desired.data->GetSpecType(spec.first));
        for (const auto& field : desired.data->List(spec.first))
        {
            installed.data->Set(spec.first, field, desired.data->Get(spec.first, field));
        }
    }
    Check(SameDocument(installed, desired),
        "Checkpoint installation did not reproduce the desired canonical authored content.");
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    CheckpointFailpoint(2);
#endif
}

template <class T, int N>
bool FiniteVector(const VtValue& value)
{
    if (!value.IsHolding<T>()) { return false; }
    const auto& vector = value.UncheckedGet<T>();
    for (int i = 0; i < N; ++i)
    {
        Check(std::isfinite(vector[i]), "Non-finite numeric payloads require the lossless binary checkpoint.");
    }
    return true;
}

void FiniteExport(const VtValue& value)
{
    const auto finite = [](auto number)
    {
        Check(std::isfinite(number), "Non-finite numeric payloads require the lossless binary checkpoint.");
    };
    if (value.IsHolding<float>()) { finite(value.UncheckedGet<float>()); }
    else if (value.IsHolding<double>()) { finite(value.UncheckedGet<double>()); }
    else if (value.IsHolding<VtArray<float>>())
    {
        for (float number : value.UncheckedGet<VtArray<float>>()) { finite(number); }
    }
    else if (value.IsHolding<VtArray<double>>())
    {
        for (double number : value.UncheckedGet<VtArray<double>>()) { finite(number); }
    }
    else if (FiniteVector<GfVec2f, 2>(value) || FiniteVector<GfVec3f, 3>(value)
        || FiniteVector<GfVec3d, 3>(value) || FiniteVector<GfVec4f, 4>(value)) {}
    else if (value.IsHolding<GfQuatf>())
    {
        const auto& q = value.UncheckedGet<GfQuatf>();
        finite(q.GetReal());
        for (int i = 0; i < 3; ++i) { finite(q.GetImaginary()[i]); }
    }
    else if (value.IsHolding<GfMatrix4d>())
    {
        const auto& matrix = value.UncheckedGet<GfMatrix4d>();
        for (int row = 0; row < 4; ++row)
        {
            for (int column = 0; column < 4; ++column) { finite(matrix[row][column]); }
        }
    }
    else if (value.IsHolding<VtDictionary>())
    {
        for (const auto& item : value.UncheckedGet<VtDictionary>()) { FiniteExport(item.second); }
    }
    else if (value.IsHolding<SdfTimeSampleMap>())
    {
        for (const auto& item : value.UncheckedGet<SdfTimeSampleMap>()) { FiniteExport(item.second); }
    }
}

class BoundedStream final : public std::streambuf
{
public:
    std::vector<uint8_t> bytes;
protected:
    std::streamsize xsputn(const char* text, std::streamsize count) override
    {
        Check(count >= 0 && static_cast<uint64_t>(count) <= MaxBytes - bytes.size(),
            "USDA export byte budget exceeded (4 MiB).");
        bytes.insert(bytes.end(), text, text + count);
        return count;
    }
    int_type overflow(int_type value) override
    {
        if (traits_type::eq_int_type(value, traits_type::eof())) { return traits_type::not_eof(value); }
        Check(bytes.size() < MaxBytes, "USDA export byte budget exceeded (4 MiB).");
        bytes.push_back(static_cast<uint8_t>(traits_type::to_char_type(value)));
        return value;
    }
};

}
}

openusd_status openusd_layer_edit_get_state(
    const openusd_layer* layer, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner); ResetAbiOutput(view);
        return OpenUsdEdit::GuardBuffer(layer, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            auto& record = OpenUsdEdit::Record(layer);
            OpenUsdEdit::Writer w;
            w.Header(5);
            OpenUsdEdit::GetIdentity(layer->stage, record).Write(w);
            w.U32(record.role);
            const uint32_t flags = (record.layer->IsAnonymous() ? 1u : 0u)
                | (record.savedRevision != record.revision ? 2u : 0u)
                | (record.layer->IsDirty() ? 4u : 0u)
                | (record.layer->PermissionToEdit() ? 8u : 0u)
                | (record.layer->PermissionToSave() ? 16u : 0u)
                | (record.local ? 32u : 0u);
            w.U32(flags);
            w.Text(record.layer->GetIdentifier());
            w.Text(record.layer->GetRealPath());
            w.Text(record.layer->GetResolvedPath().GetPathString());
            w.Text(OpenUsdEdit::Anchor(record.layer));
            OpenUsdEdit::Publish(std::move(w.bytes), owner, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_layer_edit_checkpoint(
    const openusd_layer* layer, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner); ResetAbiOutput(view);
        return OpenUsdEdit::GuardBuffer(layer, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            OpenUsdEdit::Publish(OpenUsdEdit::CaptureDocument(layer).Bytes(), owner, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_layer_edit_acknowledge_saved(
    const openusd_layer* layer, const uint8_t* checkpoint, size_t checkpoint_size,
    int32_t* acknowledged, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(acknowledged);
        OpenUsdEdit::Check(acknowledged != nullptr && IsAligned(acknowledged),
            "An aligned saved acknowledgement output is required.");
        const auto saved = OpenUsdEdit::Document::Read(checkpoint, checkpoint_size);
        auto& record = OpenUsdEdit::Record(layer);
        if (OpenUsdEdit::Matches(saved.identity, layer->stage, record)
            && saved.identity.revision == record.revision
            && OpenUsdEdit::SameDocument(saved, OpenUsdEdit::CaptureDocument(layer)))
        {
            record.savedRevision = record.revision;
            *acknowledged = 1;
        }
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_layer_edit_checkpoint_restore(
    const openusd_layer* layer, const uint8_t* expected, size_t expected_size,
    const uint8_t* restore, size_t restore_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner); ResetAbiOutput(view); ResetAbiOutput(outcome);
        return OpenUsdEdit::GuardBuffer(layer, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            OpenUsdEdit::Check(outcome != nullptr && IsAligned(outcome),
                "An aligned checkpoint outcome output is required.");
            *outcome = OPENUSD_EDIT_CONFLICT;
            TfErrorMark validation;
            const auto before = OpenUsdEdit::Document::Read(expected, expected_size);
            const auto desired = OpenUsdEdit::Document::Read(restore, restore_size);
            OpenUsdEdit::Check(validation.IsClean(), "Checkpoint validation failed before mutation.");
            auto& record = OpenUsdEdit::Record(layer);
            if (!OpenUsdEdit::Matches(before.identity, layer->stage, record)
                || desired.identity.stage != before.identity.stage || desired.identity.layer != before.identity.layer)
            {
                *outcome = OPENUSD_EDIT_STALE_TARGET;
                return OPENUSD_STATUS_OK;
            }
            if (!record.layer->PermissionToEdit())
            {
                *outcome = OPENUSD_EDIT_NOT_EDITABLE;
                return OPENUSD_STATUS_OK;
            }
            const auto current = OpenUsdEdit::CaptureDocument(layer);
            if (!OpenUsdEdit::SameDocument(before, current)
                || desired.identifier != current.identifier || desired.anchor != current.anchor)
            {
                *outcome = OPENUSD_EDIT_CONFLICT;
                return OPENUSD_STATUS_OK;
            }
            // Whole-document replacement is confined to this explicit operation;
            // ordinary edit undo never enters this path.
            TfErrorMark mark;
            const auto oldOwned = record.owned;
            const bool wasSaved = record.savedRevision == record.revision;
            const bool oldWriting = record.writing;
            record.writing = true;
            struct WritingScope
            {
                bool& flag;
                bool prior;
                ~WritingScope() { flag = prior; }
            } writing{record.writing, oldWriting};
            try
            {
                {
                    SdfChangeBlock block;
                    OpenUsdEdit::InstallDocumentExact(layer, desired);
                }
                OpenUsdEdit::Check(mark.IsClean(), "Checkpoint replacement failed.");
                auto after = OpenUsdEdit::CaptureDocument(layer);
                OpenUsdEdit::Check(OpenUsdEdit::SameDocument(after, desired),
                    "Checkpoint content changed before the applied result could be published.");
                ++record.generation;
                ++record.revision;
                record.owned.clear();
                after.identity = OpenUsdEdit::GetIdentity(layer->stage, record);
                OpenUsdEdit::Publish(after.Bytes(), owner, view);
            }
            catch (...)
            {
                ConsumeErrors(mark);
                {
                    SdfChangeBlock rollback;
                    OpenUsdEdit::InstallDocumentExact(layer, before);
                }
                OpenUsdEdit::Check(mark.IsClean() &&
                    OpenUsdEdit::SameDocument(OpenUsdEdit::CaptureDocument(layer), before),
                    "Explicit checkpoint rollback failed; document recovery is required.");
                record.owned = oldOwned;
                if (wasSaved) { record.savedRevision = record.revision; }
                throw;
            }
            *outcome = OPENUSD_EDIT_APPLIED;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_edit_checkpoint_export(
    const uint8_t* checkpoint, size_t checkpoint_size,
    const char* destination, size_t destination_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner); ResetAbiOutput(view);
        return OpenUsdEdit::GuardBuffer(nullptr, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            const auto target = std::filesystem::u8path(OpenUsdEdit::InputText(destination, destination_size));
            OpenUsdEdit::Check(target.is_absolute() && target.extension() == ".usda",
                "Checkpoint export requires an absolute .usda destination (publication is caller-owned).");
            const auto document = OpenUsdEdit::Document::Read(checkpoint, checkpoint_size);
            for (const auto& spec : document.data->Inventory())
            {
                for (const auto& field : document.data->List(spec.first))
                {
                    OpenUsdEdit::FiniteExport(document.data->Get(spec.first, field));
                }
            }
            for (const auto& field : document.data->List(SdfPath::AbsoluteRootPath()))
            {
                OpenUsdEdit::Check(field == SdfChildrenKeys->PrimChildren,
                    "Bounded USDA writer cannot emit pseudo-root metadata; use the lossless binary checkpoint.");
            }
            const auto layer = SdfLayer::CreateAnonymous("review-checkpoint.usda");
            OpenUsdEdit::Check(static_cast<bool>(layer), "Could not allocate detached checkpoint export layer.");
            OpenUsdEdit::DataAccess::Install(get_pointer(layer), document.data);
            OpenUsdEdit::BoundedStream buffer;
            std::ostream stream(&buffer);
            stream.exceptions(std::ios::badbit | std::ios::failbit);
            stream << "#usda 1.0\n";
            VtValue names;
            if (document.data->Has(SdfPath::AbsoluteRootPath(), SdfChildrenKeys->PrimChildren, &names))
            {
                for (const auto& name : names.UncheckedGet<TfTokenVector>())
                {
                    const auto prim = layer->GetPrimAtPath(SdfPath::AbsoluteRootPath().AppendChild(name));
                    OpenUsdEdit::Check(layer->GetFileFormat()->WriteToStream(prim, stream, 0),
                        "Native composition-preserving USDA checkpoint serialization failed.");
                }
            }
            OpenUsdEdit::Publish(std::move(buffer.bytes), owner, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_layer_edit_test_checkpoint_fail_after(int32_t phase)
{
    OpenUsdEdit::checkpointFailAfter = phase;
}
#endif
