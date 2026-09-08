// Copyright (c) marcschier. Licensed under the MIT License.
#include "layer_edit_cases.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/quatf.h"
#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/vt/arrayEditBuilder.h"
#include "pxr/usd/sdf/attributeSpec.h"
#include "pxr/usd/sdf/data.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/primSpec.h"
#include "pxr/usd/sdf/relationshipSpec.h"
#include "pxr/usd/usd/stage.h"

#include <fstream>
#include <limits>

PXR_NAMESPACE_USING_DIRECTIVE
using namespace EditProbe;

namespace
{
struct RawLayerFixture : SdfFileFormat
{
    static SdfAbstractData* Data(const SdfLayerHandle& layer)
    {
        return const_cast<SdfAbstractData*>(get_pointer(_GetLayerData(*layer)));
    }
};

void F32(Bytes& bytes, float value)
{
    uint32_t bits;
    std::memcpy(&bits, &value, 4);
    U32(bytes, bits);
}

Bytes PathList(bool explicitMode, bool attribute = false)
{
    Bytes bytes;
    U32(bytes, 16);
    U32(bytes, explicitMode ? 1u : 0u);
    const char* paths[] = {"/Explicit", "/Added", "/Prepended", "/Appended", "/Deleted", "/Ordered"};
    for (uint32_t i = 0; i < 6; ++i)
    {
        const bool populated = explicitMode ? i == 0 : i != 0;
        U32(bytes, populated ? 1u : 0u);
        if (populated) { Text(bytes, std::string(paths[i]) + (attribute ? ".out" : "")); }
    }
    return bytes;
}

void FailsCapture(Api& api, openusd_layer* review, const Address& address)
{
    const auto packet = AddressPacket({address});
    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
    const auto status = openusd_layer_edit_capture(review, packet.data(), packet.size(),
        &owner, &view, &api.error);
    if (status != OPENUSD_STATUS_NATIVE_ERROR)
    {
        Api::Copy(owner, view);
        throw std::runtime_error("Unsafe capture unexpectedly accepted: " + api.Native(review)->GetIdentifier());
    }
    Require(owner == nullptr && view.data == nullptr && view.size == 0,
        "Failed capture must clear all outputs.");
}
}

void RunAuthoredKinds(Api& api, openusd_layer* review)
{
    const auto native = api.Native(review);
    const Address defaultAddress{"/Kinds.value"};
    const Address sample{"/Kinds.value", 1, 2.5};
    const auto absent = api.Capture(review, {defaultAddress, sample});
    Mutation block{defaultAddress, 2, "double", 0, 0, EmptyValue()};
    const auto both = api.Apply(review, absent, {block, SetDouble(sample, 7.5)});
    Require(native->GetField(SdfPath(defaultAddress.path), SdfFieldKeys->Default).IsHolding<SdfValueBlock>(),
        "Default block must remain distinct from absence and value.");
    VtValue sampled;
    Require(native->QueryTimeSample(SdfPath(sample.path), 2.5, &sampled) && sampled == VtValue(7.5),
        "Exact numeric time sample must be authored.");
    const auto defaultBefore = api.Capture(review, {defaultAddress});
    native->SetTimeSample(SdfPath(sample.path), 12.0, VtValue(120.0));
    api.Apply(review, defaultBefore, {SetDouble(defaultAddress, 8)});
    Require(native->QueryTimeSample(SdfPath(sample.path), 12.0, &sampled) && sampled == VtValue(120.0),
        "Disjoint time edits on the same property must not conflict or be erased.");
    const auto current = api.Capture(review, {defaultAddress, sample});
    api.Restore(review, current, absent, OPENUSD_EDIT_CONFLICT);
    api.Restore(review, current, both);
    Require(native->QueryTimeSample(SdfPath(sample.path), 12.0, &sampled),
        "Addressed restore must preserve unaddressed time samples.");
    const auto sampleBefore = api.Capture(review, {sample});
    api.Apply(review, sampleBefore, {{sample, 2, "double", 0, 0, EmptyValue()}});
    Require(native->QueryTimeSample(SdfPath(sample.path), 2.5, &sampled) && sampled.IsHolding<SdfValueBlock>(),
        "Sample block must remain distinct from default block.");

    const Address relation{"/Kinds.links", 3};
    const auto relationAbsent = api.Capture(review, {relation});
    const auto nonExplicit = api.Apply(review, relationAbsent,
        {{relation, 1, "", 0, 0, PathList(false)}});
    auto list = native->GetField(SdfPath(relation.path), SdfFieldKeys->TargetPaths).Get<SdfPathListOp>();
    Require(!list.IsExplicit() && list.GetExplicitItems().empty()
        && list.GetAddedItems() == SdfPathVector{SdfPath("/Added")}
        && list.GetPrependedItems() == SdfPathVector{SdfPath("/Prepended")}
        && list.GetAppendedItems() == SdfPathVector{SdfPath("/Appended")}
        && list.GetDeletedItems() == SdfPathVector{SdfPath("/Deleted")}
        && list.GetOrderedItems() == SdfPathVector{SdfPath("/Ordered")},
        "All six path-list buckets must be preserved without composition.");
    const auto explicitList = api.Apply(review, nonExplicit,
        {{relation, 1, "", 0, 0, PathList(true)}});
    api.Restore(review, explicitList, nonExplicit);
    const auto clear = api.Apply(review, nonExplicit, {{relation, 0, "", 0, 0, EmptyValue()}});
    Require(!native->HasField(SdfPath(relation.path), SdfFieldKeys->TargetPaths),
        "Clearing list field must not create an explicit empty list.");
    Bytes explicitEmpty;
    U32(explicitEmpty, 16); U32(explicitEmpty, 1);
    for (int i = 0; i < 6; ++i) { U32(explicitEmpty, 0); }
    api.Apply(review, clear, {{relation, 1, "", 0, 0, explicitEmpty}});
    list = native->GetField(SdfPath(relation.path), SdfFieldKeys->TargetPaths).Get<SdfPathListOp>();
    Require(list.IsExplicit() && list.GetExplicitItems().empty(), "Explicit empty must remain authored.");

    const Address connection{"/Kinds.input", 2};
    api.Apply(review, api.Capture(review, {connection}),
        {{connection, 1, "float", 0, 0, PathList(false, true)}});
    Require(native->GetField(SdfPath(connection.path), SdfFieldKeys->ConnectionPaths).IsHolding<SdfPathListOp>(),
        "Attribute connections must use their own raw field.");
    const Address zero{"/Kinds.signedZero"};
    const Address zeroSample{"/Kinds.signedZero", 1, 1};
    const auto plusZero = api.Apply(review, api.Capture(review, {zero, zeroSample}),
        {SetDouble(zero, 0.0), SetDouble(zeroSample, 0.0)});
    api.Apply(review, plusZero, {SetDouble(zero, -0.0), SetDouble(zeroSample, -0.0)});
    Require(std::signbit(native->GetField(SdfPath(zero.path), SdfFieldKeys->Default).Get<double>())
        && native->QueryTimeSample(SdfPath(zero.path), 1.0, &sampled)
        && std::signbit(sampled.Get<double>()), "Exact IEEE scalar state must retain negative zero.");
    api.Apply(review, api.Capture(review, {zeroSample}),
        {{zeroSample, 0, "double", 0, 0, EmptyValue()}});
    Require(!native->HasField(SdfPath(zero.path), SdfFieldKeys->TimeSamples),
        "Clearing the last sample must not leave a synthetic empty sample container.");

    struct ValueCase { std::string name; std::string type; Bytes bytes; VtValue expected; };
    std::vector<ValueCase> values;
    Bytes value;
    U32(value, 2); U32(value, 1);
    values.push_back({"boolean", "bool", value, VtValue(true)});
    value.clear(); U32(value, 3); U32(value, static_cast<uint32_t>(-17));
    values.push_back({"intValue", "int", value, VtValue(-17)});
    value.clear(); U32(value, 4); U32(value, 9); U32(value, 1);
    values.push_back({"longValue", "int64", value, VtValue(int64_t{4294967305})});
    value.clear(); U32(value, 5); F32(value, 1.25f);
    values.push_back({"floatValue", "float", value, VtValue(1.25f)});
    value.clear(); U32(value, 7); Text(value, "tokenValue");
    values.push_back({"tokenValue", "token", value, VtValue(TfToken("tokenValue"))});
    value.clear(); U32(value, 8); Text(value, "plain string");
    values.push_back({"text", "string", value, VtValue(std::string("plain string"))});
    value.clear(); U32(value, 10); F32(value, 1); F32(value, 2);
    values.push_back({"uv", "float2", value, VtValue(GfVec2f(1, 2))});
    value.clear(); U32(value, 11); F32(value, 1); F32(value, 2); F32(value, 3);
    values.push_back({"color", "color3f", value, VtValue(GfVec3f(1, 2, 3))});
    value.clear(); U32(value, 12); F64(value, 1); F64(value, 2); F64(value, 3);
    values.push_back({"point", "double3", value, VtValue(GfVec3d(1, 2, 3))});
    value.clear(); U32(value, 13); F32(value, 1); F32(value, 2); F32(value, 3); F32(value, 4);
    values.push_back({"vector", "float4", value, VtValue(GfVec4f(1, 2, 3, 4))});
    value.clear(); U32(value, 14); F32(value, 1); F32(value, 2); F32(value, 3); F32(value, 4);
    values.push_back({"rotation", "quatf", value, VtValue(GfQuatf(1, GfVec3f(2, 3, 4)))});
    value.clear(); U32(value, 15);
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column) { F64(value, row == column ? 2.0 : 0.0); }
    }
    values.push_back({"transform", "matrix4d", value, VtValue(GfMatrix4d(2.0))});
    value.clear(); U32(value, 262); U32(value, 2); F64(value, 1); F64(value, 2);
    values.push_back({"numbers", "double[]", value, VtValue(VtArray<double>{1, 2})});
    value.clear(); U32(value, 263); U32(value, 2); Text(value, "a"); Text(value, "b");
    values.push_back({"tokens", "token[]", value, VtValue(VtArray<TfToken>{TfToken("a"), TfToken("b")})});
    value.clear(); U32(value, 264); U32(value, 2); Text(value, "a"); Text(value, "b");
    values.push_back({"strings", "string[]", value, VtValue(VtArray<std::string>{"a", "b"})});
    std::vector<Address> addresses;
    std::vector<Mutation> edits;
    for (const auto& item : values)
    {
        Address address{"/Values." + item.name};
        addresses.push_back(address);
        edits.push_back({address, 1, item.type, 0, 1, item.bytes});
    }
    const auto valuesAbsent = api.Capture(review, addresses);
    const auto valuesAfter = api.Apply(review, valuesAbsent, edits);
    for (const auto& item : values)
    {
        Require(native->GetField(SdfPath("/Values." + item.name), SdfFieldKeys->Default) == item.expected,
            "Closed typed value domain must preserve exact native values.");
    }
    Require(native->GetField(SdfPath("/Values.color"), SdfFieldKeys->TypeName) == VtValue(TfToken("color3f")),
        "Native role-bearing declaration must not collapse to float3.");
    const auto valuesUndo = api.Restore(review, valuesAfter, valuesAbsent);
    api.Restore(review, valuesUndo, valuesAfter);
}

void RunSampleBatchBounds(Api& api, openusd_layer* review)
{
    const auto layer = api.Native(review);
    const SdfPath path("/SampleBudget.value");
    SdfAttributeSpec::New(SdfCreatePrimInLayer(layer, path.GetPrimPath()),
        path.GetName(), SdfValueTypeNames->Double);
    for (int index = 0; index < 4095; ++index)
    {
        layer->SetTimeSample(path, static_cast<double>(index), VtValue(static_cast<double>(index)));
    }
    api.Checkpoint(review);
    const Address first{path.GetString(), 1, 10000};
    const Address second{path.GetString(), 1, 10001};
    const auto before = api.Capture(review, {first, second});
    const auto changes = MutationPacket({SetDouble(first, 1), SetDouble(second, 2)});
    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
    int32_t outcome = -1;
    const auto status = openusd_layer_edit_apply(review, before.data(), before.size(),
        changes.data(), changes.size(), &outcome, &owner, &view, &api.error);
    Api::Copy(owner, view);
    Require(status == OPENUSD_STATUS_NATIVE_ERROR,
        "A batch crossed the 4096-sample checkpoint bound through two individually admitted additions.");
    VtValue value;
    Require(!layer->QueryTimeSample(path, first.time, &value) &&
        !layer->QueryTimeSample(path, second.time, &value),
        "A rejected sample-count batch changed an affected opinion.");
    api.Checkpoint(review);
    api.Apply(review, api.Capture(review, {first}), {SetDouble(first, 1)});
    api.Checkpoint(review);
    api.Apply(review, api.Capture(review, {first}), {{first, 0, "double", 0, 0, EmptyValue()}});
    Require(!layer->QueryTimeSample(path, first.time, &value),
        "An exactly-full sample map could not safely clear one affected sample.");
    api.Checkpoint(review);
}

void RunUnsupportedAddresses(Api& api, openusd_layer* review)
{
    const Address invalid{"/Relational.link[/Target].unexpected"};
    const auto valid = api.Capture(review, {{"/Relational.unexpected"}});
    auto expected = Header(2);
    expected.insert(expected.end(), valid.begin() + 12, valid.begin() + 44);
    U32(expected, 1);
    invalid.Write(expected);
    for (int index = 0; index < 5; ++index) { U32(expected, 0); }
    const auto changes = MutationPacket({SetDouble(invalid, 47)});
    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
    int32_t outcome = -1;
    const auto status = openusd_layer_edit_apply(review, expected.data(), expected.size(),
        changes.data(), changes.size(), &outcome, &owner, &view, &api.error);
    Api::Copy(owner, view);
    Require(status == OPENUSD_STATUS_NATIVE_ERROR &&
        !api.Native(review)->HasSpec(SdfPath("/Relational")),
        "Unsupported relational address leaked a direct prim property outside affected rollback coverage.");
    FailsCapture(api, review, invalid);
}

void RunPersistence(Api& api, openusd_layer* review, const std::string& directory)
{
    const auto native = api.Native(review);
    native->SetField(SdfPath("/World"), TfToken("futureOpinion"), VtValue(std::string("retain unknown")));
    const Address texture{"/World.texture"};
    Bytes asset;
    U32(asset, 9); Text(asset, directory + "/texture.png"); Text(asset, ""); Text(asset, "");
    api.Apply(review, api.Capture(review, {texture}), {{texture, 1, "asset", 0, 0, asset}});
    const auto checkpoint = api.Checkpoint(review);
    const Address weight{"/World.weight"};
    const auto before = api.Capture(review, {weight});
    api.Apply(review, before, {SetDouble(weight, 123)});
    int32_t acknowledged = -1;
    api.Ok(openusd_layer_edit_acknowledge_saved(review, checkpoint.data(), checkpoint.size(),
        &acknowledged, &api.error));
    Require(acknowledged == 0, "Later edits must not be cleared by old saved-baseline acknowledgement.");
    Require((ReadU32(api.State(review), 48) & 2u) != 0, "Post-publication edits must remain dirty.");
    const auto current = api.Checkpoint(review);
    api.Ok(openusd_layer_edit_acknowledge_saved(review, current.data(), current.size(),
        &acknowledged, &api.error));
    Require(acknowledged == 1, "Matching identity/generation/revision/content must be acknowledged.");
    const uint32_t flags = ReadU32(api.State(review), 48);
    Require((flags & 2u) == 0 && (flags & 4u) != 0,
        "Logical saved baseline must not silently clear native anonymous dirty state.");
    const std::string destination = directory + "/review-export.usda";
    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
    api.Ok(openusd_edit_checkpoint_export(current.data(), current.size(),
        destination.data(), destination.size(), &owner, &view, &api.error));
    const auto exported = Api::Copy(owner, view);
    {
        std::ofstream output(destination, std::ios::binary | std::ios::trunc);
        Require(static_cast<bool>(output), "Open owned checkpoint export fixture.");
        output.write(reinterpret_cast<const char*>(exported.data()), static_cast<std::streamsize>(exported.size()));
        Require(static_cast<bool>(output), "Publish owned checkpoint fixture.");
    }
    const auto reopened = SdfLayer::FindOrOpen(destination);
    Require(static_cast<bool>(reopened), "Native checkpoint export must reopen as a layer.");
    Require(reopened->GetField(SdfPath("/World.weight"), SdfFieldKeys->Default) == VtValue(123.0),
        "Reopened review opinions must match the saved capture.");
    Require(reopened->HasField(SdfPath("/World"), TfToken("futureOpinion")),
        "Composition-preserving export must not drop unknown opinion names.");
    Require(reopened->GetField(SdfPath("/Values.color"), SdfFieldKeys->TypeName) == VtValue(TfToken("color3f")),
        "Save/reopen must preserve declaration role.");
    Require(reopened->GetField(SdfPath(texture.path), SdfFieldKeys->Default).Get<SdfAssetPath>().GetAuthoredPath()
        == directory + "/texture.png", "Absolute asset anchor must not be rewritten on export.");
    owner = nullptr;
    int32_t outcome = -1;
    api.Ok(openusd_layer_edit_checkpoint_restore(review, current.data(), current.size(),
        checkpoint.data(), checkpoint.size(), &outcome, &owner, &view, &api.error));
    Api::Copy(owner, view);
    Require(outcome == OPENUSD_EDIT_APPLIED, "Expected explicit conditional checkpoint restore.");
    Require(native->GetField(SdfPath("/World.weight"), SdfFieldKeys->Default) == VtValue(99.0),
        "Explicit checkpoint restore must restore the captured review opinion.");
    api.Apply(review, before, {SetDouble(weight, 17)}, OPENUSD_EDIT_STALE_TARGET);
    api.Ok(openusd_layer_edit_acknowledge_saved(review, current.data(), current.size(),
        &acknowledged, &api.error));
    Require(acknowledged == 0, "Generation-changing restore must invalidate saved acknowledgements.");

    native->SetField(SdfPath(texture.path), SdfFieldKeys->Default, VtValue(SdfAssetPath("relative.png")));
    owner = nullptr;
    Require(openusd_layer_edit_checkpoint(review, &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR
        && owner == nullptr, "Unvalidated relative asset relocation must fail closed.");
    native->SetField(SdfPath(texture.path), SdfFieldKeys->Default, VtValue(SdfAssetPath(directory + "/texture.png")));
    native->SetField(SdfPath::AbsoluteRootPath(), TfToken("futureDocumentField"), VtValue(std::string("keep")));
    const auto metadataCheckpoint = api.Checkpoint(review);
    Require(openusd_edit_checkpoint_export(metadataCheckpoint.data(), metadataCheckpoint.size(),
        destination.data(), destination.size(), &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR
        && owner == nullptr, "Unsupported bounded pseudo-root text writing must not drop metadata.");
    native->EraseField(SdfPath::AbsoluteRootPath(), TfToken("futureDocumentField"));
}

void RunBitExactCheckpoint(Api& api, openusd_stage* stage, openusd_layer* review)
{
    const auto requireComposedSign = [&](bool negative)
    {
        const auto requireBits = [&](double value)
        {
            uint64_t bits;
            std::memcpy(&bits, &value, sizeof(bits));
            Require(bits == (negative ? uint64_t{1} << 63 : 0),
                "An already-open stage retained stale composed checkpoint payload bits.");
        };
        for (int sampled = 0; sampled < 2; ++sampled)
        {
            double value = 1;
            api.Ok(openusd_stage_get_double(stage, "/CheckpointBits",
                sampled ? "sample" : "doubleValue", sampled, 2.5, &value, &api.error));
            requireBits(value);
        }
        double values[3]{};
        size_t required = 0;
        api.Ok(openusd_stage_get_double_array(stage, "/CheckpointBits", "doubles",
            0, 0, values, 3, &required, &api.error));
        Require(required == 3, "Composed checkpoint array length changed.");
        for (double value : values) { requireBits(value); }
    };
    struct ZeroCase { const char* name; const char* type; uint32_t tag; size_t components; bool wide; };
    const ZeroCase cases[] = {
        {"floatValue", "float", 5, 1, false},
        {"doubleValue", "double", 6, 1, true},
        {"vector2", "float2", 10, 2, false},
        {"vector3", "float3", 11, 3, false},
        {"vector3d", "double3", 12, 3, true},
        {"vector4", "float4", 13, 4, false},
        {"quaternion", "quatf", 14, 4, false},
        {"matrix", "matrix4d", 15, 16, true},
        {"floats", "float[]", 261, 3, false},
        {"doubles", "double[]", 262, 3, true}
    };
    std::vector<Address> addresses;
    std::vector<Mutation> positive;
    std::vector<Mutation> negative;
    for (const auto& item : cases)
    {
        const Address address{std::string("/CheckpointBits.") + item.name};
        addresses.push_back(address);
        Bytes plus;
        Bytes minus;
        U32(plus, item.tag);
        U32(minus, item.tag);
        if (item.tag >= 256)
        {
            U32(plus, static_cast<uint32_t>(item.components));
            U32(minus, static_cast<uint32_t>(item.components));
        }
        for (size_t component = 0; component < item.components; ++component)
        {
            if (item.wide)
            {
                F64(plus, 0.0);
                F64(minus, -0.0);
            }
            else
            {
                F32(plus, 0.0f);
                F32(minus, -0.0f);
            }
        }
        positive.push_back({address, 1, item.type, 0, 0, plus});
        negative.push_back({address, 1, item.type, 0, 0, minus});
    }
    const Address sample{"/CheckpointBits.sample", 1, 2.5};
    addresses.push_back(sample);
    positive.push_back(SetDouble(sample, 0.0));
    negative.push_back(SetDouble(sample, -0.0));
    const auto positiveState = api.Apply(review, api.Capture(review, addresses), positive);
    requireComposedSign(false);
    const auto a = api.Checkpoint(review);
    api.Apply(review, positiveState, negative);
    requireComposedSign(true);
    const auto b = api.Checkpoint(review);
    auto expectedCurrent = b;

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    for (int phase = 1; phase <= 2; ++phase)
    {
        int32_t acknowledged = 0;
        api.Ok(openusd_layer_edit_acknowledge_saved(review, expectedCurrent.data(), expectedCurrent.size(),
            &acknowledged, &api.error));
        Require(acknowledged == 1, "Could not acknowledge checkpoint rollback baseline.");
        openusd_layer_edit_test_checkpoint_fail_after(phase);
        openusd_edit_buffer* failedOwner = nullptr;
        openusd_edit_buffer_view failedView{};
        int32_t failedOutcome = -1;
        const auto status = openusd_layer_edit_checkpoint_restore(review,
            expectedCurrent.data(), expectedCurrent.size(), a.data(), a.size(),
            &failedOutcome, &failedOwner, &failedView, &api.error);
        Require(status == OPENUSD_STATUS_NATIVE_ERROR && failedOwner == nullptr && failedView.data == nullptr,
            "Injected checkpoint installation failure must not publish an applied result.");
        expectedCurrent = api.Checkpoint(review);
        Require(expectedCurrent.size() == b.size() &&
            std::equal(b.begin() + 44, b.end(), expectedCurrent.begin() + 44),
            "Failed checkpoint installation did not restore exact prior content.");
        Require((ReadU32(api.State(review), 48) & 2u) == 0,
            "Verified checkpoint rollback did not preserve a saved logical baseline.");
        requireComposedSign(true);
    }
#endif

    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
    int32_t outcome = -1;
    api.Ok(openusd_layer_edit_checkpoint_restore(review, expectedCurrent.data(), expectedCurrent.size(), a.data(), a.size(),
        &outcome, &owner, &view, &api.error));
    const auto restored = Api::Copy(owner, view);
    Require(outcome == OPENUSD_EDIT_APPLIED, "Bit-exact checkpoint restore was not applied.");
    const auto fresh = api.Checkpoint(review);
    Require(restored == fresh, "AfterCheckpoint did not describe actual installed content.");
    Require(fresh.size() == a.size() && std::equal(a.begin() + 44, a.end(), fresh.begin() + 44),
        "Applied checkpoint restore retained numerically equal but bit-distinct scalar/vector/array/sample values.");
    requireComposedSign(false);
    api.Ok(openusd_layer_edit_checkpoint_restore(review, fresh.data(), fresh.size(), b.data(), b.size(),
        &outcome, &owner, &view, &api.error));
    const auto restoredNegative = Api::Copy(owner, view);
    Require(outcome == OPENUSD_EDIT_APPLIED && restoredNegative.size() == b.size() &&
        std::equal(b.begin() + 44, b.end(), restoredNegative.begin() + 44),
        "Reverse checkpoint restore did not retain exact negative-zero payloads.");
    requireComposedSign(true);
}

void RunRollbackInventories(Api& api, openusd_layer* review)
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    for (int scenario = 0; scenario < 2; ++scenario)
    {
        const Address first{scenario == 0 ? "/RollbackOrder.a" : "/RollbackPrims/A.value"};
        const Address second{scenario == 0 ? "/RollbackOrder.b" : "/RollbackPrims/B.value"};
        const auto absent = api.Capture(review, {first});
        api.Apply(review, absent, {SetDouble(first, 1)});
        api.Apply(review, api.Capture(review, {second}), {SetDouble(second, 2)});
        const auto current = api.Capture(review, {first});
        const auto saved = api.Checkpoint(review);
        int32_t acknowledged = 0;
        api.Ok(openusd_layer_edit_acknowledge_saved(
            review, saved.data(), saved.size(), &acknowledged, &api.error));
        Require(acknowledged == 1, "Could not establish the rollback inventory saved baseline.");
        openusd_layer_edit_test_fail_after(scenario == 0 ? 1 : 0);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        const auto status = openusd_layer_edit_restore(review, current.data(), current.size(),
            absent.data(), absent.size(), &outcome, &owner, &view, &api.error);
        Api::Copy(owner, view);
        Require(status == OPENUSD_STATUS_NATIVE_ERROR, "Expected controlled undo/cleanup failure.");
        const auto fresh = api.Checkpoint(review);
        Require(fresh.size() == saved.size() &&
            std::equal(saved.begin() + 44, saved.end(), fresh.begin() + 44),
            "Failed undo reordered affected parent inventories or changed unrelated checkpoint content.");
        const SdfPath parent(scenario == 0 ? "/RollbackOrder" : "/RollbackPrims");
        const auto key = scenario == 0 ? SdfChildrenKeys->PropertyChildren : SdfChildrenKeys->PrimChildren;
        const auto expected = scenario == 0 ?
            TfTokenVector{TfToken("a"), TfToken("b")} : TfTokenVector{TfToken("A"), TfToken("B")};
        Require(api.Native(review)->GetField(parent, key).Get<TfTokenVector>() == expected,
            "Rollback did not restore exact parent child ordering.");
        Require((ReadU32(api.State(review), 48) & 2u) == 0,
            "Exact rollback of a saved layer did not restore its logical baseline.");
    }
#else
    static_cast<void>(api);
    static_cast<void>(review);
#endif
}

void RunIdentityAndBounds(Api& api, openusd_stage* stage, openusd_layer* review,
    const std::string& rootFile, const std::string& directory)
{
    const auto native = api.Native(review);
    const Address weight{"/World.weight"};
    auto before = api.Capture(review, {weight});
    native->SetPermissionToEdit(false);
    api.Apply(review, before, {SetDouble(weight, 321)}, OPENUSD_EDIT_NOT_EDITABLE);
    Require((ReadU32(api.State(review), 48) & 8u) == 0, "State must report actual edit permission.");
    native->SetPermissionToEdit(true);
    openusd_layer* resolved = nullptr;
    const auto identifier = native->GetIdentifier();
    api.Ok(openusd_stage_edit_get_local_layer(stage, identifier.data(), identifier.size(), &resolved, &api.error));
    Require(ReadU64(api.State(resolved), 20) == ReadU64(api.State(review), 20), "Local resolution must retain logical identity.");
    openusd_layer_release(resolved);
    const auto foreign = SdfLayer::CreateAnonymous("review.usda");
    const auto foreignId = foreign->GetIdentifier();
    Require(openusd_stage_edit_get_local_layer(stage, foreignId.data(), foreignId.size(), &resolved, &api.error)
        == OPENUSD_STATUS_NOT_FOUND && resolved == nullptr, "Globally registered non-local layer must be refused.");

    openusd_layer* root = nullptr;
    api.Ok(openusd_stage_get_root_layer(stage, &root, &api.error));
    const auto rootBefore = api.Capture(root, {weight});
    int32_t reloaded = 0;
    api.Ok(openusd_layer_reload(root, 1, &reloaded, &api.error));
    api.Apply(root, rootBefore, {SetDouble(weight, 81)}, OPENUSD_EDIT_STALE_TARGET);
    openusd_layer_release(root);

    openusd_layer* session = nullptr;
    api.Ok(openusd_stage_get_session_layer(stage, &session, &api.error));
    const auto nativeSession = api.Native(session);
    nativeSession->RemoveSubLayerPath(0);
    api.Apply(review, before, {SetDouble(weight, 322)}, OPENUSD_EDIT_STALE_TARGET);
    nativeSession->InsertSubLayerPath(identifier, 0);
    api.Apply(review, before, {SetDouble(weight, 323)}, OPENUSD_EDIT_STALE_TARGET);
    openusd_layer_release(session);

    before = api.Capture(review, {weight});
    auto packet = AddressPacket({weight});
    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
    Require(openusd_layer_edit_capture(review, packet.data(), 4 * 1024 * 1024 + 1,
        &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR,
        "Oversized packet must be rejected before dereferencing its advertised extent.");
    const auto valueBefore = native->GetField(SdfPath(weight.path), SdfFieldKeys->Default);
    Bytes invalid;
    U32(invalid, 262); U32(invalid, std::numeric_limits<uint32_t>::max());
    const auto mutation = MutationPacket({{weight, 1, "double[]", 0, 0, invalid}});
    int32_t outcome = -1;
    Require(openusd_layer_edit_apply(review, before.data(), before.size(), mutation.data(),
        mutation.size(), &outcome, &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR,
        "Array count must be admitted before allocation.");
    Require(native->GetField(SdfPath(weight.path), SdfFieldKeys->Default) == valueBefore,
        "Bad packet admission must not mutate the target.");
    native->SetField(SdfPath(weight.path), SdfFieldKeys->Default, VtValue(std::string(4097, 'x')));
    FailsCapture(api, review, weight);
    native->SetField(SdfPath(weight.path), SdfFieldKeys->Default, valueBefore);
    const Address badAsset{"/World.badAsset"};
    const auto beforeInvalidAsset = api.Capture(review, {weight, badAsset});
    Bytes asset;
    U32(asset, 9); Text(asset, std::string(1, '\x01')); Text(asset, ""); Text(asset, "");
    const auto invalidAsset = MutationPacket({SetDouble(weight, 500),
        {badAsset, 1, "asset", 0, 0, asset}});
    Require(openusd_layer_edit_apply(review, beforeInvalidAsset.data(), beforeInvalidAsset.size(),
        invalidAsset.data(), invalidAsset.size(), &outcome, &owner, &view, &api.error)
        == OPENUSD_STATUS_NATIVE_ERROR, "Malformed native asset must be rejected.");
    Api::Copy(owner, view);
    Require(!native->HasSpec(SdfPath(badAsset.path))
        && native->GetField(SdfPath(weight.path), SdfFieldKeys->Default) == valueBefore,
        "Native validation diagnostics must fail BEFORE the first write.");
    VtArrayEditBuilder<float> editBuilder;
    native->SetField(SdfPath(weight.path), SdfFieldKeys->Default,
        VtValue(editBuilder.SetSize(1024 * 1024, 1.0f).FinalizeAndReset()));
    FailsCapture(api, review, weight);
    native->SetField(SdfPath(weight.path), SdfFieldKeys->Default, valueBefore);
    const Address invalidTime{"/World.weight", 1, std::numeric_limits<double>::infinity()};
    FailsCapture(api, review, invalidTime);

    openusd_stage* overlayStage = nullptr;
    api.Ok(openusd_stage_open(rootFile.c_str(), &overlayStage, &api.error));
    openusd_layer* physics = nullptr;
    openusd_layer* user = nullptr;
    api.Ok(openusd_stage_session_overlay_normalize(overlayStage, &physics, &user, &api.error));
    openusd_layer* actualReview = nullptr;
    api.Ok(openusd_stage_edit_get_user_layer(overlayStage, &actualReview, &api.error));
    Require(api.Native(actualReview) == api.Native(user) && api.Native(actualReview) != api.Native(physics),
        "Normalized overlay must register the actual user layer separately from physics/session.");
    Require(ReadU32(api.State(actualReview), 44) == 2 && ReadU32(api.State(physics), 44) == 3,
        "User and physics roles must be tied to layer identity.");
    const auto importedCapture = api.Capture(actualReview, {weight});
    api.Apply(actualReview, importedCapture, {SetDouble(weight, 5)});
    Require(!api.Native(physics)->HasSpec(SdfPath(weight.path))
        && !api.Checkpoint(actualReview).empty(), "Normalized user edits must remain separate from physics.");
    openusd_layer_release(actualReview);
    openusd_layer_release(user);
    openusd_layer_release(physics);
    openusd_stage_release(overlayStage);

    const std::string crateFile = directory + "/unsupported-edit.usdc";
    const auto crate = SdfLayer::CreateNew(crateFile);
    Require(crate && crate->ImportFromString("#usda 1.0\n def \"World\"\n{\n double weight = 4\n}\n")
        && crate->Save(), "Create native crate admission fixture.");
    Require(crate->Reload(true), "Reload the written crate into its deferred backing store.");
    openusd_stage* crateStage = nullptr;
    api.Ok(openusd_stage_open(crateFile.c_str(), &crateStage, &api.error));
    api.Ok(openusd_stage_get_root_layer(crateStage, &root, &api.error));
    FailsCapture(api, root, weight);
    openusd_layer_release(root);
    openusd_stage_release(crateStage);

    const auto budgetPrim = SdfCreatePrimInLayer(native, SdfPath("/FieldBudget"));
    for (int i = 0; i < 129; ++i)
    {
        native->SetField(SdfPath("/FieldBudget"), TfToken("field" + std::to_string(i)), VtValue(i));
    }
    Require(openusd_layer_edit_checkpoint(review, &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR
        && owner == nullptr, "Field-name inventory must be counted before allocating an SDK List result.");
    native->GetPseudoRoot()->RemoveNameChild(budgetPrim);

    before = api.Capture(review, {weight});
    std::string replacement;
    Require(native->ExportToString(&replacement) && native->ImportFromString(replacement),
        "Replace review backing data from its native text representation.");
    api.Apply(review, before, {SetDouble(weight, 7)}, OPENUSD_EDIT_STALE_TARGET);
}

void RunOverlayContinuity(Api& api, const std::string& directory)
{
    const std::string file = directory + "/review-continuity-root.usda";
    const auto root = SdfLayer::CreateNew(file);
    Require(root && root->ImportFromString(
        "#usda 1.0\n def \"World\"\n{\n double weight = 7\n}\n")
        && root->Save(), "Create review continuity source.");
    openusd_stage* stage = nullptr;
    openusd_layer* review = nullptr;
    api.Ok(openusd_stage_open(file.c_str(), &stage, &api.error));
    api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
    const Address weight{"/World.weight"};
    const auto before = api.Capture(review, {weight});
    const auto edited = api.Apply(review, before, {SetDouble(weight, 4)});
    const auto checkpoint = api.Checkpoint(review);
    openusd_layer* physics = nullptr;
    openusd_layer* user = nullptr;
    api.Ok(openusd_stage_session_overlay_normalize(stage, &physics, &user, &api.error));
    Require(api.Native(user) == api.Native(review),
        "Starting simulation must retain the same owned review layer.");
    openusd_layer* active = nullptr;
    api.Ok(openusd_stage_edit_get_user_layer(stage, &active, &api.error));
    Require(api.Native(active) == api.Native(review) && api.Checkpoint(active) == checkpoint,
        "Starting simulation changed review identity, generation, revision or authored content.");
    Require(ReadU32(api.State(active), 44) == 2 && ReadU32(api.State(physics), 44) == 3,
        "Review and simulation roles must remain distinct.");
    api.Restore(active, edited, before);
    double value = 0;
    api.Ok(openusd_stage_get_double(stage, "/World", "weight", 0, 0, &value, &api.error));
    Require(value == 7 && !api.Native(active)->HasSpec(SdfPath(weight.path)),
        "Pre-simulation history must undo the review opinion and reveal the unchanged root.");
    Require(root->GetField(SdfPath(weight.path), SdfFieldKeys->Default) == VtValue(7.0),
        "Simulation normalization must leave source opinions unchanged.");
    openusd_layer_release(active);
    openusd_layer_release(user);
    openusd_layer_release(physics);
    openusd_layer_release(review);
    openusd_stage_release(stage);
}

void RunOverlayLifecycles(Api& api, const std::string& directory)
{
    const std::string file = directory + "/review-continuity-root.usda";
    openusd_stage* stage = nullptr;
    openusd_layer* review = nullptr;
    openusd_layer* session = nullptr;
    api.Ok(openusd_stage_open(file.c_str(), &stage, &api.error));
    api.Ok(openusd_stage_get_session_layer(stage, &session, &api.error));
    const auto container = api.Native(session);
    const auto foreign = SdfLayer::CreateAnonymous("weaker-session");
    container->InsertSubLayerPath(foreign->GetIdentifier(), 0);
    container->SetSubLayerOffset(SdfLayerOffset(3.25, 2), 0);
    const TfToken unregistered("unregisteredPreservedRootField");
    container->SetField(SdfPath::AbsoluteRootPath(), unregistered, VtValue(std::string("retain exactly")));
    api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
    const auto nativeReview = api.Native(review);
    const Address weight{"/World.weight"};
    const auto absent = api.Capture(review, {weight});
    const auto originalPaths = container->GetSubLayerPaths();
    const std::vector<std::string> expectedPaths(originalPaths.begin(), originalPaths.end());
    for (int cycle = 0; cycle < 2; ++cycle)
    {
        nativeReview->SetPermissionToEdit(cycle != 0);
        const auto before = api.Checkpoint(review);
        openusd_layer* physics = nullptr;
        openusd_layer* user = nullptr;
        api.Ok(openusd_stage_session_overlay_normalize(stage, &physics, &user, &api.error));
        Require(api.Checkpoint(user) == before && nativeReview->PermissionToEdit() == (cycle != 0),
            "Normalization must preserve empty/edited review state and edit permission.");
        const auto paths = container->GetSubLayerPaths();
        Require(paths.size() == 3 && paths[0] == api.Native(physics)->GetIdentifier()
            && paths[1] == nativeReview->GetIdentifier() && paths[2] == foreign->GetIdentifier(),
            "Physics/review/foreign sublayer strength order changed.");
        const auto offset = container->GetSubLayerOffset(2);
        Require(offset.GetOffset() == 3.25 && offset.GetScale() == 2,
            "Weaker foreign sublayer offset was not preserved.");
        Require(container->GetField(SdfPath::AbsoluteRootPath(), unregistered) ==
            VtValue(std::string("retain exactly")), "Unrecognized root metadata was altered or dropped.");
        if (cycle == 0)
        {
            api.Apply(review, absent, {SetDouble(weight, 4)}, OPENUSD_EDIT_NOT_EDITABLE);
            nativeReview->SetPermissionToEdit(true);
        }
        const auto edited = api.Apply(review, api.Capture(review, {weight}), {SetDouble(weight, 4)});
        const auto simulated = SdfAttributeSpec::New(
            SdfCreatePrimInLayer(api.Native(physics), SdfPath("/World")), "weight", SdfValueTypeNames->Double);
        simulated->SetDefaultValue(VtValue(9.0));
        double value = 0;
        api.Ok(openusd_stage_get_double(stage, "/World", "weight", 0, 0, &value, &api.error));
        Require(value == 9, "Simulation must compose above the preserved review.");
        const auto physicsId = api.Native(physics)->GetIdentifier();
        api.Ok(openusd_stage_session_overlay_remove(stage, physicsId.c_str(), &api.error));
        api.Ok(openusd_stage_get_double(stage, "/World", "weight", 0, 0, &value, &api.error));
        Require(value == 4 && api.Native(user) == nativeReview,
            "Stopping simulation must reveal the same review opinion and identity.");
        const auto undone = api.Restore(user, edited, absent);
        api.Ok(openusd_stage_get_double(stage, "/World", "weight", 0, 0, &value, &api.error));
        Require(value == 7, "Pre-simulation absent history must still reveal the root after stopping.");
        api.Restore(user, undone, edited);
        const auto restoredPaths = container->GetSubLayerPaths();
        Require(std::vector<std::string>(restoredPaths.begin(), restoredPaths.end()) == expectedPaths,
            "Stopping simulation changed pre-existing sublayer order.");
        openusd_layer_release(user);
        openusd_layer_release(physics);
    }
    openusd_layer_release(session);
    openusd_layer_release(review);
    openusd_stage_release(stage);
}

void RunOverlayRefusals(Api& api, const std::string& directory)
{
    const std::string file = directory + "/review-continuity-root.usda";
    for (int scenario = 0; scenario < 7; ++scenario)
    {
        openusd_stage* stage = nullptr;
        openusd_layer* review = nullptr;
        openusd_layer* session = nullptr;
        api.Ok(openusd_stage_open(file.c_str(), &stage, &api.error));
        api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
        api.Ok(openusd_stage_get_session_layer(stage, &session, &api.error));
        const auto container = api.Native(session);
        const Address weight{"/World.weight"};
        const auto absent = api.Capture(review, {weight});
        const auto edited = api.Apply(review, absent, {SetDouble(weight, 4)});
        SdfLayerRefPtr foreign;
        openusd_layer* activePhysics = nullptr;
        openusd_layer* activeUser = nullptr;
        if (scenario == 0) { SdfCreatePrimInLayer(container, SdfPath("/Direct")); }
        if (scenario == 1) { container->SetDocumentation("preserve direct container metadata"); }
        if (scenario == 2)
        {
            foreign = SdfLayer::CreateAnonymous("stronger-foreign");
            container->InsertSubLayerPath(foreign->GetIdentifier(), 0);
        }
        if (scenario == 3) { container->SetSubLayerOffset(SdfLayerOffset(1, 2), 0); }
        if (scenario == 4) { container->SetPermissionToEdit(false); }
        if (scenario == 5) { container->RemoveSubLayerPath(0); }
        if (scenario == 6)
        {
            api.Ok(openusd_stage_session_overlay_normalize(stage, &activePhysics, &activeUser, &api.error));
        }
        const auto beforeState = api.State(review);
        const auto beforePaths = container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayers);
        const auto beforeOffsets = container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayerOffsets);
        openusd_layer* physics = nullptr;
        openusd_layer* user = nullptr;
        const auto status = openusd_stage_session_overlay_normalize(stage, &physics, &user, &api.error);
        Require(status == OPENUSD_STATUS_NATIVE_ERROR && physics == nullptr && user == nullptr,
            "Unsafe or ambiguous review adoption must fail with empty outputs.");
        Require(api.State(review) == beforeState &&
            container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayers) == beforePaths &&
            container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayerOffsets) == beforeOffsets,
            "Refused review adoption changed target state or existing topology.");
        Require(api.Native(review)->GetField(SdfPath(weight.path), SdfFieldKeys->Default) == VtValue(4.0),
            "Refused normalization modified the prior review opinion.");
        if (scenario == 5) { api.Restore(review, edited, absent, OPENUSD_EDIT_STALE_TARGET); }
        if (scenario == 4) { container->SetPermissionToEdit(true); }
        openusd_layer_release(activePhysics);
        openusd_layer_release(activeUser);
        openusd_layer_release(session);
        openusd_layer_release(review);
        openusd_stage_release(stage);
    }
}

void RunOverlayRollback(Api& api, const std::string& directory)
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    const std::string file = directory + "/review-continuity-root.usda";
    for (int phase = 1; phase <= 2; ++phase)
    {
        for (bool clean : {false, true})
        {
            openusd_stage* stage = nullptr;
            openusd_layer* review = nullptr;
            openusd_layer* session = nullptr;
            api.Ok(openusd_stage_open(file.c_str(), &stage, &api.error));
            api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
            api.Ok(openusd_stage_get_session_layer(stage, &session, &api.error));
            const Address weight{"/World.weight"};
            const auto absent = api.Capture(review, {weight});
            const auto edited = api.Apply(review, absent, {SetDouble(weight, 4)});
            const auto before = api.Checkpoint(review);
            if (clean)
            {
                int32_t acknowledged = 0;
                api.Ok(openusd_layer_edit_acknowledge_saved(
                    review, before.data(), before.size(), &acknowledged, &api.error));
                Require(acknowledged == 1, "Could not acknowledge the overlay rollback baseline.");
            }
            const auto state = api.State(review);
            const auto container = api.Native(session);
            const auto paths = container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayers);
            const auto offsets = container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayerOffsets);
            openusd_layer_edit_test_overlay_fail_after(phase);
            openusd_layer* physics = nullptr;
            openusd_layer* user = nullptr;
            Require(openusd_stage_session_overlay_normalize(stage, &physics, &user, &api.error)
                == OPENUSD_STATUS_NATIVE_ERROR && physics == nullptr && user == nullptr,
                "Injected overlay adoption failure must clear outputs.");
            Require(api.Checkpoint(review) == before && api.State(review) == state &&
                container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayers) == paths &&
                container->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayerOffsets) == offsets,
                "Failed overlay adoption changed prior topology, review identity/content or dirtiness.");
            api.Ok(openusd_stage_session_overlay_normalize(stage, &physics, &user, &api.error));
            Require(api.Checkpoint(user) == before, "Retry after adoption failure lost review continuity.");
            api.Restore(user, edited, absent);
            double value = 0;
            api.Ok(openusd_stage_get_double(stage, "/World", "weight", 0, 0, &value, &api.error));
            Require(value == 7, "Failed adoption invalidated pre-existing owned undo.");
            openusd_layer_release(user);
            openusd_layer_release(physics);
            openusd_layer_release(session);
            openusd_layer_release(review);
            openusd_stage_release(stage);
        }
    }
#else
    static_cast<void>(api);
    static_cast<void>(directory);
#endif
}

void RunOverlayAdmission(Api& api, const std::string& directory)
{
    const std::string file = directory + "/review-continuity-root.usda";
    for (int scenario = 0; scenario < 8; ++scenario)
    {
        openusd_stage* stage = nullptr;
        openusd_layer* review = nullptr;
        openusd_layer* session = nullptr;
        api.Ok(openusd_stage_open(file.c_str(), &stage, &api.error));
        api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
        api.Ok(openusd_stage_get_session_layer(stage, &session, &api.error));
        const auto before = api.Checkpoint(review);
        const auto root = SdfPath::AbsoluteRootPath();
        auto* data = RawLayerFixture::Data(api.Native(session));
        const auto paths = data->Get(root, SdfFieldKeys->SubLayers);
        const auto offsets = data->Get(root, SdfFieldKeys->SubLayerOffsets);
        const auto children = data->Get(root, SdfChildrenKeys->PrimChildren);
        const auto& id = api.Native(review)->GetIdentifier();
        if (scenario == 0)
        {
            data->Set(root, SdfFieldKeys->SubLayers, VtValue(std::vector<std::string>(4096, id)));
        }
        if (scenario == 1)
        {
            data->Set(root, SdfFieldKeys->SubLayers,
                VtValue(std::vector<std::string>{id, std::string(4097, 'x')}));
        }
        if (scenario == 2)
        {
            std::vector<std::string> excessive(1024, std::string(4096, 'x'));
            excessive.insert(excessive.begin(), id);
            data->Set(root, SdfFieldKeys->SubLayers, VtValue(excessive));
        }
        if (scenario == 3)
        {
            data->Set(root, SdfFieldKeys->SubLayers, VtValue(std::vector<std::string>{id, id}));
        }
        if (scenario == 4) { data->Set(root, SdfFieldKeys->SubLayers, VtValue(1)); }
        if (scenario == 5)
        {
            data->Set(root, SdfFieldKeys->SubLayerOffsets,
                VtValue(std::vector<SdfLayerOffset>{SdfLayerOffset(), SdfLayerOffset()}));
        }
        if (scenario == 6)
        {
            data->Set(root, SdfFieldKeys->SubLayerOffsets,
                VtValue(std::vector<SdfLayerOffset>{SdfLayerOffset(std::numeric_limits<double>::infinity(), 1)}));
        }
        if (scenario == 7)
        {
            data->Set(root, SdfChildrenKeys->PrimChildren,
                VtValue(TfTokenVector{TfToken(std::string(64 * 1024 * 1024, 'x'))}));
        }
        const auto refusedPaths = data->Get(root, SdfFieldKeys->SubLayers);
        const auto refusedOffsets = data->Get(root, SdfFieldKeys->SubLayerOffsets);
        openusd_layer* physics = nullptr;
        openusd_layer* user = nullptr;
        Require(openusd_stage_session_overlay_normalize(stage, &physics, &user, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && physics == nullptr && user == nullptr,
            "Malformed or over-budget session topology must fail before mutation.");
        Require(data->Get(root, SdfFieldKeys->SubLayers) == refusedPaths &&
            data->Get(root, SdfFieldKeys->SubLayerOffsets) == refusedOffsets &&
            api.Checkpoint(review) == before, "Rejected topology admission changed prior state.");
        data->Set(root, SdfFieldKeys->SubLayers, paths);
        data->Set(root, SdfFieldKeys->SubLayerOffsets, offsets);
        data->Set(root, SdfChildrenKeys->PrimChildren, children);
        openusd_layer* aliased = nullptr;
        Require(openusd_stage_session_overlay_normalize(stage, &aliased, &aliased, &api.error)
            == OPENUSD_STATUS_INVALID_ARGUMENT && aliased == nullptr,
            "Aliased overlay output owners must be rejected before mutation.");
        openusd_layer_release(session);
        openusd_layer_release(review);
        openusd_stage_release(stage);
    }
}
