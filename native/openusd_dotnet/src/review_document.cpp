// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"
#include "pxr/usd/sdf/changeBlock.h"
#include "pxr/usd/ar/resolverContextBinder.h"

using OpenUsdReview::Check;

namespace OpenUsdReview
{
namespace
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
thread_local int failAfter = -1;
#endif
constexpr uint32_t ReceiptMagic = 0x31525255;

template <class Action>
openusd_status BufferGuard(const openusd_stage* stage, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error, Action&& action, bool acquireFiles = true)
{
    ResetAbiOutput(owner); ResetAbiOutput(view);
    const auto status = GuardStage(stage, error, [&]()
    {
        OpenUsdEdit::Outputs(owner, view);
        Check(IsAligned(owner) && IsAligned(view), "Aligned portable buffer outputs are required.");
        if (acquireFiles)
        {
            FileReadScope files;
            return action();
        }
        return action();
    });
    if (status != OPENUSD_STATUS_OK)
    {
        if (owner && IsAligned(owner)) { openusd_edit_buffer_release(*owner); }
        ResetAbiOutput(owner); ResetAbiOutput(view);
    }
    return status;
}
Bytes MakeReceipt(const std::string& token)
{
    PacketWriter writer(128);
    writer.U32(ReceiptMagic); writer.U32(1); writer.Text(token);
    return std::move(writer.bytes);
}
std::string ReadReceipt(const uint8_t* bytes, size_t size)
{
    PacketReader reader(bytes, size, 128);
    Check(reader.U32() == ReceiptMagic && reader.U32() == 1, "Unsupported saved-receipt magic or version.");
    auto token = reader.Text(); reader.End();
    Check(IsGuid(token), "Malformed process-local saved receipt.");
    return token;
}
struct RecordState
{
    OpenUsdEdit::LayerRecord* record;
    uint64_t generation;
    uint64_t revision;
    uint64_t saved;
    uint32_t role;
    bool local;
    std::map<SdfPath, bool> owned;
    void Restore() const
    {
        record->generation = generation; record->revision = revision; record->savedRevision = saved;
        record->role = role; record->local = local; record->owned = owned;
    }
};
void RestoreSession(const SdfLayerRefPtr& session, const TfRefPtr<CountedData>& before)
{
    SdfChangeBlock block;
    OpenUsdEdit::DataAccess::Install(get_pointer(session), before);
    const auto resident = OpenUsdEdit::DataAccess::Get(*session);
    Check(OpenUsdEdit::DataAccess::ConcreteType(resident) == typeid(CountedData),
        "Review import rollback lost its owned session data.");
    auto* installed = const_cast<CountedData*>(static_cast<const CountedData*>(get_pointer(resident)));
    std::vector<SdfPath> paths;
    Check(installed->Inventory().size() <= OpenUsdEdit::MaxSpecs, "Rollback session inventory exceeded its budget.");
    for (const auto& spec : installed->Inventory()) { paths.push_back(spec.first); }
    for (const auto& path : paths) { installed->EraseSpec(path); }
    for (const auto& spec : before->Inventory())
    {
        installed->CreateSpec(spec.first, before->GetSpecType(spec.first));
        for (const auto& field : before->List(spec.first))
        {
            installed->Set(spec.first, field, before->Get(spec.first, field));
        }
    }
}
}
void Failpoint(int phase)
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    if (failAfter == phase)
    {
        failAfter = -1; throw std::runtime_error("Injected portable review import failure.");
    }
#else
    (void)phase;
#endif
}
}

openusd_status openusd_stage_open_for_review(
    const char* path, openusd_stage** stage, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION
    ResetAbiOutput(stage);
    return Guard(error, [&]()
    {
        Check(stage && IsAligned(stage) && path, "A source path and aligned stage output are required.");
        OpenUsdReview::FileReadScope files;
        size_t count = 0;
        while (count <= OpenUsdEdit::MaxString && path[count]) { ++count; }
        const auto text = OpenUsdReview::TextInput(path, count);
        auto admitted = OpenUsdReview::Admit(text);
        OpenUsdReview::Failpoint(4);
        auto sessionData = TfCreateRefPtr(new OpenUsdEdit::CountedData);
        sessionData->CreateSpec(SdfPath::AbsoluteRootPath(), SdfSpecTypePseudoRoot);
        auto session = OpenUsdReview::SeedLayer("review-session.usda", sessionData, true);
        TfErrorMark mark;
        ArResolverContextBinder binder(admitted.resolver);
        const auto root = SdfLayer::Find(admitted.source.root);
        auto value = UsdStage::Open(root, session, admitted.resolver);
        Check(value && mark.IsClean(), "Could not compose the verified filesystem source.");
        for (const auto& layer : value->GetUsedLayers())
        {
            Check(layer == session || std::find(admitted.layers.begin(), admitted.layers.end(), layer) != admitted.layers.end(),
                "Composition opened a dependency outside the admitted actual-byte closure.");
        }
        auto result = std::make_unique<openusd_stage>(value);
        auto& context = EditContext(result.get());
        auto state = std::make_shared<OpenUsdReviewState>();
        state->source = std::move(admitted.source); state->origins = std::move(admitted.origins);
        state->sourceLayers = std::move(admitted.layers); state->resolver = std::move(admitted.resolver);
        state->session = session; state->sessionData = sessionData; state->sessionVersion = sessionData->Version();
        state->documentId = OpenUsdReview::Guid();
        context.review = std::move(state);
        OpenUsdReview::VerifySource(result.get());
        openusd_layer* prepared = nullptr;
        const auto preparation = openusd_stage_edit_get_user_layer(result.get(), &prepared, error);
        std::unique_ptr<openusd_layer, decltype(&openusd_layer_release)> review(prepared, openusd_layer_release);
        Check(preparation == OPENUSD_STATUS_OK,
            "Source changed or anchored review preparation failed; reconcile and retry OpenForReview.");
        Check(mark.IsClean(), "Verified source open reported SDK errors.");
        review.reset();
        *stage = result.release();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_stage_review_source_binding(
    const openusd_stage* stage, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION: BufferGuard resets owner and view before guarded work.
    return OpenUsdReview::BufferGuard(stage, owner, view, error, [&]()
    {
        OpenUsdReview::VerifySource(stage);
        auto& context = EditContext(stage);
        OpenUsdEdit::PublishPortable(OpenUsdReview::BindingBytes(context.id, context.review->source), owner, view);
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_layer_review_capture(
    const openusd_layer* layer, const uint8_t* binding, size_t binding_size,
    const char* target_document_path, size_t target_document_path_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION: BufferGuard resets owner and view before guarded work.
    return OpenUsdReview::BufferGuard(layer ? layer->stage : nullptr, owner, view, error, [&]()
    {
        Check(layer && layer->stage, "A live stage-bound review layer is required.");
        Check(OpenUsdReview::MatchBinding(layer->stage, binding, binding_size),
            "Source binding belongs to a different stage/byte image; reconcile and acquire its verified binding.");
        OpenUsdReview::VerifySource(layer->stage);
        auto& state = *EditContext(layer->stage).review;
        const auto data = OpenUsdReview::ReviewData(layer);
        auto& record = OpenUsdEdit::Record(layer);
        const auto identity = OpenUsdEdit::GetIdentity(layer->stage, record);
        OpenUsdReview::Document document;
        document.id = state.documentId; document.source = state.source;
        document.originalTarget = layer->value->GetIdentifier();
        document.target = OpenUsdReview::Path(OpenUsdReview::TextInput(
            target_document_path, target_document_path_size), false);
        document.review = OpenUsdReview::EncodeReview(data);
        document.source.files = OpenUsdReview::ReviewFiles(layer->stage, record.layer, data);
        const auto portable = OpenUsdReview::EncodeDocument(document);
        const auto token = OpenUsdReview::Guid();
        const auto receipt = OpenUsdReview::MakeReceipt(token);
        const auto envelope = OpenUsdReview::Envelope(document, portable, receipt);
        OpenUsdReview::VerifySource(layer->stage);
        Check(identity.revision == record.revision && identity.generation == record.generation,
            "Review changed during capture; retry after reconciling concurrent edits.");
        for (auto it = state.receipts.begin(); it != state.receipts.end();)
        {
            const auto& prior = it->second.identity;
            if (prior.layer != identity.layer || prior.generation != identity.generation || prior.revision != identity.revision)
            {
                it = state.receipts.erase(it);
            }
            else { ++it; }
        }
        Check(state.receipts.size() < 64, "Outstanding review receipt budget exceeded (64); acknowledge or retire the review session.");
        OpenUsdReviewState::Receipt saved{identity, OpenUsdReview::Hash(document.review),
            OpenUsdReview::ManifestHash(document.source.files), document.originalTarget, state.source.anchor};
        OpenUsdEdit::PublishPortable(envelope, owner, view);
        state.receipts.emplace(token, std::move(saved));
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_review_document_read(
    const uint8_t* document, size_t document_size, const char* expected_source_path,
    size_t expected_source_path_size, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION: BufferGuard resets owner and view before guarded work.
    return OpenUsdReview::BufferGuard(nullptr, owner, view, error, [&]()
    {
        const auto parsed = OpenUsdReview::DecodeDocument(document, document_size);
        const auto expected = OpenUsdReview::Path(OpenUsdReview::TextInput(expected_source_path, expected_source_path_size), false);
        Check(parsed.source.root == expected, "Foreign review document: select its explicit recorded source root instead.");
        const OpenUsdReview::Bytes portable(document, document + document_size);
        OpenUsdEdit::PublishPortable(OpenUsdReview::Envelope(parsed, portable, {}), owner, view);
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_review_document_inspect(
    const uint8_t* document, size_t document_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION: BufferGuard resets owner and view before guarded work.
    return OpenUsdReview::BufferGuard(nullptr, owner, view, error, [&]()
    {
        const auto parsed = OpenUsdReview::DecodeDocument(document, document_size, true);
        OpenUsdEdit::PublishPortable(OpenUsdReview::Inspection(parsed, document_size), owner, view);
        return OPENUSD_STATUS_OK;
    }, false);
}

openusd_status openusd_stage_review_import(
    const openusd_stage* stage, const uint8_t* document, size_t document_size,
    const uint8_t* binding, size_t binding_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION: BufferGuard also resets the owned buffer outputs.
    ResetAbiOutput(outcome);
    return OpenUsdReview::BufferGuard(stage, owner, view, error, [&]()
    {
        Check(outcome && IsAligned(outcome), "An aligned import outcome is required.");
        *outcome = OPENUSD_EDIT_CONFLICT;
        const auto parsed = OpenUsdReview::DecodeDocument(document, document_size);
        if (!OpenUsdReview::MatchBinding(stage, binding, binding_size))
        {
            *outcome = OPENUSD_EDIT_STALE_TARGET; return OPENUSD_STATUS_OK;
        }
        auto& context = EditContext(stage);
        auto& state = *context.review;
        if (parsed.source.root != state.source.root || parsed.source.fingerprint != state.source.fingerprint
            || parsed.source.anchor != state.source.anchor || !OpenUsdReview::Pristine(stage))
        {
            WriteError(error, "Review/source mismatch or non-pristine ambient session; reconcile without clobbering current work.");
            return OPENUSD_STATUS_OK;
        }
        if (!state.session->PermissionToEdit() || (context.user && !context.user->PermissionToEdit()))
        {
            *outcome = OPENUSD_EDIT_NOT_EDITABLE; return OPENUSD_STATUS_OK;
        }
        try
        {
            OpenUsdReview::VerifySource(stage); OpenUsdReview::VerifyFiles(parsed.source.files);
        }
        catch (const std::exception& exception)
        {
            WriteError(error, exception.what()); return OPENUSD_STATUS_OK;
        }
        const auto data = OpenUsdReview::DecodeReview(parsed.review.data(), parsed.review.size());
        const auto replacement = OpenUsdReview::NewReviewLayer(stage, data);
        const auto beforeOrigins = state.origins.size();
        const auto beforeLayers = state.sourceLayers.size();
        const auto dependencies = OpenUsdReview::ReviewFiles(stage, replacement, data);
        if (dependencies != parsed.source.files)
        {
            state.origins.resize(beforeOrigins); state.sourceLayers.resize(beforeLayers);
            WriteError(error, "Review dependency closure changed; reconcile against the saved manifest before importing.");
            return OPENUSD_STATUS_OK;
        }
        auto before = TfCreateRefPtr(new OpenUsdEdit::CountedData);
        before->CopyFrom(state.sessionData);
        const auto oldUser = context.user;
        const auto oldTarget = stage->value->GetEditTarget();
        const auto oldId = state.documentId;
        const auto oldUserVersion = state.userVersion;
        std::vector<OpenUsdReview::RecordState> records;
        for (const auto& record : context.records)
        {
            records.push_back({record.get(), record->generation, record->revision,
                record->savedRevision, record->role, record->local, record->owned});
        }
        TfErrorMark mark;
        try
        {
            {
                SdfChangeBlock change;
                state.session->SetSubLayerPaths({replacement->GetIdentifier()});
            }
            OpenUsdReview::Failpoint(1);
            Check(mark.IsClean(), "Portable import attachment failed.");
            context.user = replacement;
            auto& record = context.Track(replacement, 2);
            record.savedRevision = record.revision;
            if (oldUser && oldTarget.GetLayer() == oldUser) { stage->value->SetEditTarget(UsdEditTarget(replacement)); }
            OpenUsdReview::Failpoint(2);
            OpenUsdReview::VerifySource(stage); OpenUsdReview::VerifyFiles(parsed.source.files);
            Check(mark.IsClean(), "Portable import composition failed.");
            state.documentId = parsed.id; state.imported = true;
            OpenUsdReview::UserLayerAttached(stage);
            OpenUsdReview::Failpoint(3);
            OpenUsdEdit::PublishPortable(OpenUsdReview::StateBytes(stage, record), owner, view);
            *outcome = OPENUSD_EDIT_APPLIED;
            return OPENUSD_STATUS_OK;
        }
        catch (...)
        {
            ConsumeErrors(mark);
            OpenUsdReview::RestoreSession(state.session, before);
            context.user = oldUser;
            stage->value->SetEditTarget(oldTarget);
            context.records.resize(records.size());
            for (const auto& record : records) { record.Restore(); }
            state.documentId = oldId; state.imported = false; state.userVersion = oldUserVersion;
            state.sessionVersion = state.sessionData->Version();
            state.origins.resize(beforeOrigins); state.sourceLayers.resize(beforeLayers);
            Check(mark.IsClean() && OpenUsdReview::Pristine(stage),
                "Portable import rollback failed; explicit session reconciliation is required.");
            throw;
        }
    });
}

openusd_status openusd_layer_review_acknowledge_saved(
    const openusd_layer* layer, const uint8_t* receipt, size_t receipt_size,
    int32_t* acknowledged, openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    // ABI_OUTPUT_INITIALIZATION
    ResetAbiOutput(acknowledged);
    return GuardLayer(layer, error, [&]()
    {
        Check(acknowledged && IsAligned(acknowledged), "An aligned saved acknowledgement output is required.");
        OpenUsdReview::FileReadScope files;
        const auto token = OpenUsdReview::ReadReceipt(receipt, receipt_size);
        const auto data = OpenUsdReview::ReviewData(layer);
        auto& context = EditContext(layer->stage);
        Check(context.review != nullptr, "A saved receipt requires its original verified review session.");
        auto& state = *context.review;
        const auto found = state.receipts.find(token);
        if (found == state.receipts.end()) { return OPENUSD_STATUS_OK; }
        const auto& saved = found->second;
        auto& record = OpenUsdEdit::Record(layer);
        if (!OpenUsdEdit::Matches(saved.identity, layer->stage, record) || saved.identity.revision != record.revision
            || record.layer->GetIdentifier() != saved.targetIdentifier || state.source.anchor != saved.anchor)
        {
            return OPENUSD_STATUS_OK;
        }
        OpenUsdReview::VerifySource(layer->stage);
        if (OpenUsdReview::Hash(OpenUsdReview::EncodeReview(data)) != saved.reviewHash
            || OpenUsdReview::ManifestHash(OpenUsdReview::ReviewFiles(layer->stage, record.layer, data)) != saved.manifestHash)
        {
            return OPENUSD_STATUS_OK;
        }
        record.savedRevision = record.revision;
        *acknowledged = 1;
        state.receipts.erase(found);
        return OPENUSD_STATUS_OK;
    });
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_review_test_fail_after(int32_t phase)
{
    OpenUsdReview::failAfter = phase;
}
#endif
