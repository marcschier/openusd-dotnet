// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/layer_edit.h"
#include "internal/review_document.h"
#include <random>

namespace
{
uint64_t NewIdentity()
{
    static std::atomic<uint64_t> nextIdentity{[]()
    {
        std::random_device random;
        const uint64_t nonce = (static_cast<uint64_t>(random()) << 32) ^ random();
        return nonce ? nonce : uint64_t{1};
    }()};
    const uint64_t value = nextIdentity.fetch_add(1, std::memory_order_relaxed);
    OpenUsdEdit::Check(value != 0, "Editing identity space exhausted.");
    return value;
}

void ReturnLayer(const openusd_stage* stage, const SdfLayerHandle& value,
    openusd_layer** output)
{
    auto handle = std::make_unique<openusd_layer>();
    handle->value = value;
    handle->stage = const_cast<openusd_stage*>(stage);
    OpenUsdEdit::Check(RetainStageReference(handle->stage), "Could not retain editing stage.");
    *output = handle.release();
}
}

OpenUsdEditContext::OpenUsdEditContext(const openusd_stage* owner)
    : stage(owner), id(NewIdentity()),
      _key(TfNotice::Register(TfCreateWeakPtr(this),
          &OpenUsdEditContext::Changed, UsdStageConstPtr(owner->value)))
{
}

OpenUsdEditContext::~OpenUsdEditContext()
{
    TfNotice::RevokeAndWait(_key);
}

void OpenUsdEditContext::Changed(const UsdNotice::StageContentsChanged&)
{
    Refresh();
}

void OpenUsdEditContext::Refresh()
{
    for (const auto& record : records)
    {
        const auto backing = OpenUsdEdit::DataAccess::Get(*record->layer);
        if (record->backing != backing)
        {
            record->backing = TfCreateRefPtrFromProtectedWeakPtr(backing);
            ++record->generation;
            ++record->revision;
            record->owned.clear();
        }
        const bool local = stage->value->HasLocalLayer(record->layer);
        if (record->local != local)
        {
            record->local = local;
            ++record->generation;
            record->owned.clear();
        }
    }
}

OpenUsdEdit::LayerRecord& OpenUsdEditContext::Track(
    const SdfLayerHandle& layer, uint32_t role)
{
    Refresh();
    for (const auto& record : records)
    {
        if (record->layer == layer)
        {
            if (role != 4)
            {
                record->role = role;
            }
            return *record;
        }
    }
    OpenUsdEdit::Check(records.size() < 64, "Editing layer handle budget exceeded (64).");
    OpenUsdEdit::Check(layer && stage->value->HasLocalLayer(layer),
        "Editing target is not in the stage's local layer stack.");
    if (layer == stage->value->GetRootLayer())
    {
        role = 0;
    }
    else if (layer == stage->value->GetSessionLayer())
    {
        role = 1;
    }
    auto record = std::make_unique<OpenUsdEdit::LayerRecord>(
        TfCreateRefPtrFromProtectedWeakPtr(layer), role);
    records.push_back(std::move(record));
    return *records.back();
}

OpenUsdEditContext& EditContext(const openusd_stage* stage)
{
    OpenUsdEdit::Check(stage && stage->value, "A live stage is required.");
    if (!stage->edit_context)
    {
        stage->edit_context = std::make_shared<OpenUsdEditContext>(stage);
    }
    return *stage->edit_context;
}

namespace OpenUsdEdit
{
LayerRecord::LayerRecord(SdfLayerRefPtr value, uint32_t roleValue)
    : layer(std::move(value)),
      backing(TfCreateRefPtrFromProtectedWeakPtr(DataAccess::Get(*layer))),
      id(NewIdentity()), role(roleValue),
      _changed(TfNotice::Register(TfCreateWeakPtr(this), &LayerRecord::Changed,
          SdfLayerHandle(layer))),
      _replaced(TfNotice::Register(TfCreateWeakPtr(this), &LayerRecord::Replaced,
          SdfLayerHandle(layer))),
      _renamed(TfNotice::Register(TfCreateWeakPtr(this), &LayerRecord::Renamed,
          SdfLayerHandle(layer))),
      _saved(TfNotice::Register(TfCreateWeakPtr(this), &LayerRecord::Saved,
          SdfLayerHandle(layer)))
{
    savedRevision = layer->IsDirty() ? 0 : revision;
}

LayerRecord::~LayerRecord()
{
    TfNotice::RevokeAndWait(_changed);
    TfNotice::RevokeAndWait(_replaced);
    TfNotice::RevokeAndWait(_renamed);
    TfNotice::RevokeAndWait(_saved);
}

void LayerRecord::Changed(const SdfNotice::LayersDidChangeSentPerLayer& notice)
{
    ++revision;
    const auto it = notice.find(layer);
    if (it != notice.end())
    {
        for (const auto& entry : it->second.GetEntryList())
        {
            if (entry.second.flags.didReplaceContent || entry.second.flags.didReloadContent
                || entry.second.flags.didChangeIdentifier || entry.second.flags.didChangeResolvedPath)
            {
                ++generation;
                owned.clear();
            }
            if (!writing)
            {
                auto found = owned.find(entry.first);
                if (found != owned.end())
                {
                    found->second = true;
                }
            }
        }
    }
}

void LayerRecord::Replaced(const SdfNotice::LayerDidReplaceContent&)
{
    ++generation;
    ++revision;
    owned.clear();
}

void LayerRecord::Renamed(const SdfNotice::LayerIdentifierDidChange&)
{
    ++generation;
    ++revision;
    owned.clear();
}

void LayerRecord::Saved(const SdfNotice::LayerDidSaveLayerToFile&)
{
    if (role != 2) { savedRevision = revision; }
}

void RegisterOverlay(const openusd_stage* stage, const SdfLayerRefPtr& physics,
    const SdfLayerRefPtr& user)
{
    auto& context = EditContext(stage);
    Check(context.records.size() <= 62, "Editing overlay registration budget exceeded.");
    context.Track(physics, 3);
    context.Track(user, 2);
    if (context.user && context.user != user)
    {
        auto& old = context.Track(context.user);
        old.role = 4;
        ++old.generation;
    }
    context.physics = physics;
    context.user = user;
}

void InitializeReviewData(const SdfLayerRefPtr& newlyCreatedLayer)
{
    // Only called immediately after CreateAnonymous, before authoring/exposure.
    auto data = TfCreateRefPtr(new CountedData);
    data->CreateSpec(SdfPath::AbsoluteRootPath(), SdfSpecTypePseudoRoot);
    DataAccess::Install(get_pointer(newlyCreatedLayer), data);
}

LayerRecord& Record(const openusd_layer* layer)
{
    Check(layer && layer->value && layer->stage, "A live stage-bound layer is required.");
    return EditContext(layer->stage).Track(layer->value);
}

Identity GetIdentity(const openusd_stage* stage, const LayerRecord& record)
{
    return {EditContext(stage).id, record.id, record.generation, record.revision};
}

bool Matches(const Identity& identity, const openusd_stage* stage, const LayerRecord& record)
{
    return record.local && identity.stage == EditContext(stage).id
        && identity.layer == record.id && identity.generation == record.generation;
}

SdfAbstractDataConstPtr Resident(const SdfLayerHandle& layer)
{
    const auto data = DataAccess::Get(*layer);
    Check(data && (typeid(*data) == typeid(SdfData)
        || typeid(*data) == typeid(SdfUsdaData)
        || typeid(*data) == typeid(CountedData)),
        "Editing requires resident SdfData/SdfUsdaData; deferred or custom backing is unsupported.");
    return data;
}

void CountedData::CreateSpec(const SdfPath& path, SdfSpecType type)
{
    ++_version;
    _fields.try_emplace(path, 0);
    SdfData::CreateSpec(path, type);
}

void CountedData::EraseSpec(const SdfPath& path)
{
    ++_version;
    SdfData::EraseSpec(path);
    _fields.erase(path);
}

void CountedData::MoveSpec(const SdfPath& from, const SdfPath& to)
{
    ++_version;
    auto node = _fields.extract(from);
    if (!node.empty())
    {
        node.key() = to;
        _fields.insert(std::move(node));
    }
    SdfData::MoveSpec(from, to);
}

void CountedData::Changed(const SdfPath& path, const TfToken& field, bool before, size_t previousCount)
{
    ++_version;
    const bool after = SdfData::Has(path, field, static_cast<VtValue*>(nullptr));
    _fields[path] = previousCount + (after ? 1u : 0u) - (before ? 1u : 0u);
}

void CountedData::Set(const SdfPath& path, const TfToken& field, const VtValue& value)
{
    const bool before = SdfData::Has(path, field, static_cast<VtValue*>(nullptr));
    const size_t previousCount = FieldCount(path);
    SdfData::Set(path, field, value);
    Changed(path, field, before, previousCount);
}

void CountedData::Set(const SdfPath& path, const TfToken& field,
    const SdfAbstractDataConstValue& value)
{
    VtValue copy;
    value.GetValue(&copy);
    Set(path, field, copy);
}

void CountedData::Erase(const SdfPath& path, const TfToken& field)
{
    const bool before = SdfData::Has(path, field, static_cast<VtValue*>(nullptr));
    const size_t previousCount = FieldCount(path);
    SdfData::Erase(path, field);
    Changed(path, field, before, previousCount);
}

void CountedData::SetTimeSample(const SdfPath& path, double time, const VtValue& value)
{
    const bool before = SdfData::Has(path, SdfFieldKeys->TimeSamples,
        static_cast<VtValue*>(nullptr));
    const size_t previousCount = FieldCount(path);
    SdfData::SetTimeSample(path, time, value);
    Changed(path, SdfFieldKeys->TimeSamples, before, previousCount);
}

void CountedData::EraseTimeSample(const SdfPath& path, double time)
{
    const bool before = SdfData::Has(path, SdfFieldKeys->TimeSamples,
        static_cast<VtValue*>(nullptr));
    const size_t previousCount = FieldCount(path);
    SdfData::EraseTimeSample(path, time);
    Changed(path, SdfFieldKeys->TimeSamples, before, previousCount);
}

size_t CountedData::FieldCount(const SdfPath& path) const
{
    const auto it = _fields.find(path);
    return it == _fields.end() ? 0 : it->second;
}
}

openusd_status openusd_stage_edit_get_user_layer(
    const openusd_stage* stage, openusd_layer** layer, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(layer);
        OpenUsdEdit::Check(layer != nullptr && IsAligned(layer), "An aligned output layer is required.");
        auto& context = EditContext(stage);
        context.Refresh();
        if (context.user)
        {
            OpenUsdEdit::Check(stage->value->HasLocalLayer(context.user),
                "The registered user-review layer was detached; explicit document preparation is required.");
            OpenUsdEdit::Resident(context.user);
            ReturnLayer(stage, context.user, layer);
            return OPENUSD_STATUS_OK;
        }
        const auto session = stage->value->GetSessionLayer();
        OpenUsdEdit::Check(session && session->PermissionToEdit(),
            "The session container is not editable.");
        VtValue sublayers;
        if (OpenUsdEdit::Resident(session)->Has(SdfPath::AbsoluteRootPath(),
            SdfFieldKeys->SubLayers, &sublayers))
        {
            OpenUsdEdit::Check(sublayers.IsHolding<std::vector<std::string>>(),
                "Unsupported session sublayer storage.");
            const auto& paths = sublayers.UncheckedGet<std::vector<std::string>>();
            OpenUsdEdit::Check(paths.size() < OpenUsdEdit::MaxItems, "Session sublayer budget exceeded.");
            size_t bytes = 0;
            for (const auto& path : paths)
            {
                OpenUsdEdit::Check(path.size() <= OpenUsdEdit::MaxString,
                    "Session sublayer identifier byte budget exceeded.");
                bytes += path.size();
                OpenUsdEdit::Check(bytes <= OpenUsdEdit::MaxBytes, "Session topology byte budget exceeded.");
            }
        }
        const auto user = context.review ? OpenUsdReview::NewReviewLayer(stage)
            : SdfLayer::CreateAnonymous("review.usda");
        OpenUsdEdit::Check(static_cast<bool>(user), "Could not create review layer.");
        if (!context.review) { OpenUsdEdit::InitializeReviewData(user); }
        // Never move/copy session opinions or normalize the entire session here.
        TfErrorMark mark;
        session->InsertSubLayerPath(user->GetIdentifier(), 0);
        try
        {
            OpenUsdEdit::Check(mark.IsClean(), "Could not attach the review layer.");
            auto& record = context.Track(user, 2);
            record.savedRevision = record.revision;
            context.user = user;
            if (context.review) { OpenUsdReview::UserLayerAttached(stage); }
            ReturnLayer(stage, user, layer);
        }
        catch (...)
        {
            session->RemoveSubLayerPath(0);
            context.user.Reset();
            throw;
        }
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_stage_edit_get_local_layer(
    const openusd_stage* stage, const char* identifier, size_t identifier_size,
    openusd_layer** layer, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(layer);
        OpenUsdEdit::Check(layer != nullptr && IsAligned(layer), "An aligned output layer is required.");
        auto& context = EditContext(stage);
        const auto value = SdfLayer::Find(OpenUsdEdit::InputText(identifier, identifier_size));
        if (!value || !stage->value->HasLocalLayer(value))
        {
            WriteError(error, "The identifier does not name a stage-local layer.");
            return OPENUSD_STATUS_NOT_FOUND;
        }

        context.Track(value);
        ReturnLayer(stage, value, layer);
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_layer_edit_capture(
    const openusd_layer* layer, const uint8_t* addresses, size_t addresses_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]()
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(owner);
        ResetAbiOutput(view);
        return OpenUsdEdit::GuardBuffer(layer, owner, view, error, [&]()
        {
            OpenUsdEdit::Outputs(owner, view);
            OpenUsdEdit::Reader reader(addresses, addresses_size);
            reader.Header(1);
            const uint32_t count = reader.Count(OpenUsdEdit::MaxAddresses);
            OpenUsdEdit::Check(count != 0, "At least one editing address is required.");
            reader.Need(count * 16ull);
            std::vector<OpenUsdEdit::Address> requested;
            for (uint32_t i = 0; i < count; ++i)
            {
                auto address = OpenUsdEdit::Address::Read(reader);
                OpenUsdEdit::Check(std::find(requested.begin(), requested.end(), address)
                    == requested.end(), "Duplicate editing address.");
                requested.push_back(std::move(address));
            }
            reader.End();
            OpenUsdEdit::Publish(OpenUsdEdit::Capture(layer, requested).Bytes(), owner, view);
            return OPENUSD_STATUS_OK;
        });
    });
}
