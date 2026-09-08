// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "openusd_review_document.h"
#include "layer_edit_probe_support.h"
#include "pxr/usd/sdf/attributeSpec.h"
#include "pxr/usd/sdf/primSpec.h"
#include "pxr/usd/sdf/reference.h"
#include "pxr/usd/sdf/payload.h"
#include "pxr/usd/sdf/variantSetSpec.h"
#include "pxr/usd/sdf/variantSpec.h"
#include "pxr/usd/sdf/layerUtils.h"
#include "pxr/usd/ar/resolver.h"

#include <filesystem>
#include <fstream>
#include <map>
#include <memory>

namespace ReviewProbe
{
using namespace EditProbe;
namespace fs = std::filesystem;
using Stage = std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)>;
using Layer = std::unique_ptr<openusd_layer, decltype(&openusd_layer_release)>;

inline void Write(const fs::path& path, std::string_view bytes)
{
    fs::create_directories(path.parent_path());
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    file.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
    Require(file.good(), "Write owned filesystem fixture.");
}
inline std::string Read(const fs::path& path)
{
    std::ifstream file(path, std::ios::binary);
    Require(file.good(), "Read owned filesystem fixture.");
    return std::string(std::istreambuf_iterator<char>(file), {});
}
struct Packet
{
    const Bytes& bytes;
    size_t offset = 0;
    uint32_t U32() { const auto value = ReadU32(bytes, offset); offset += 4; return value; }
    uint64_t U64() { const auto value = ReadU64(bytes, offset); offset += 8; return value; }
    std::string Text()
    {
        const auto count = U32();
        Require(count <= bytes.size() - offset, "Bounded output text.");
        std::string result(bytes.begin() + offset, bytes.begin() + offset + count); offset += count; return result;
    }
    Bytes Blob()
    {
        const auto count = U32();
        Require(count <= bytes.size() - offset, "Bounded output blob.");
        Bytes result(bytes.begin() + offset, bytes.begin() + offset + count); offset += count; return result;
    }
};
struct Saved
{
    size_t envelopeSize = 0;
    Bytes document;
    Bytes receipt;
    std::string id;
    std::string source;
    std::string fingerprint;
    std::string originalTarget;
    std::string target;
    std::string anchor;
};
struct ReviewApi : Api
{
    Stage Open(const fs::path& path)
    {
        openusd_stage* stage = nullptr;
        Ok(openusd_stage_open_for_review(path.u8string().c_str(), &stage, &error));
        return Stage(stage, openusd_stage_release);
    }
    Layer User(openusd_stage* stage)
    {
        openusd_layer* layer = nullptr;
        Ok(openusd_stage_edit_get_user_layer(stage, &layer, &error));
        return Layer(layer, openusd_layer_release);
    }
    Layer Session(openusd_stage* stage)
    {
        openusd_layer* layer = nullptr;
        Ok(openusd_stage_get_session_layer(stage, &layer, &error));
        return Layer(layer, openusd_layer_release);
    }
    Layer Root(openusd_stage* stage)
    {
        openusd_layer* layer = nullptr;
        Ok(openusd_stage_get_root_layer(stage, &layer, &error));
        return Layer(layer, openusd_layer_release);
    }
    Bytes Binding(openusd_stage* stage)
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        Ok(openusd_stage_review_source_binding(stage, &owner, &view, &error));
        return Copy(owner, view);
    }
    Saved Save(openusd_layer* review, const Bytes& binding, const fs::path& target)
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        const auto path = target.u8string();
        Ok(openusd_layer_review_capture(review, binding.data(), binding.size(), path.data(), path.size(),
            &owner, &view, &error));
        const auto bytes = Copy(owner, view);
        Packet reader{bytes};
        Require(reader.U32() == 0x31454452 && reader.U32() == 1, "RDE1 v1 envelope.");
        Saved saved;
        saved.envelopeSize = bytes.size();
        saved.document = reader.Blob(); saved.receipt = reader.Blob();
        saved.id = reader.Text(); saved.source = reader.Text(); saved.fingerprint = reader.Text();
        saved.originalTarget = reader.Text(); saved.target = reader.Text(); saved.anchor = reader.Text();
        return saved;
    }
    Bytes Import(openusd_stage* stage, const Bytes& document, const Bytes& binding, int32_t expected = OPENUSD_EDIT_APPLIED)
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        Ok(openusd_stage_review_import(stage, document.data(), document.size(), binding.data(), binding.size(),
            &outcome, &owner, &view, &error));
        const auto bytes = Copy(owner, view);
        Require(outcome == expected, "Expected portable import outcome.");
        Require((expected == OPENUSD_EDIT_APPLIED) == !bytes.empty(), "Non-applied imports return empty outputs.");
        return bytes;
    }
    double Number(openusd_stage* stage, const char* prim, const char* name)
    {
        openusd_scalar_value value{};
        value.struct_size = sizeof(value);
        char text[4097]{};
        size_t required = 0;
        Ok(openusd_stage_get_attribute_scalar_value(stage, prim, name, 0, 0,
            &value, text, sizeof(text), &required, &error));
        Require(value.kind == OPENUSD_SCALAR_KIND_DOUBLE, "Composed value is a double.");
        return value.double_value;
    }
};

inline std::map<fs::path, std::string> Composition(const fs::path& directory)
{
    std::map<fs::path, std::string> files{
        {directory / "root.usda",
            "#usda 1.0\n(\n defaultPrim = \"World\"\n metersPerUnit = 0.25\n upAxis = \"Z\"\n"
            " startTimeCode = 7\n endTimeCode = 77\n timeCodesPerSecond = 60\n"
            " subLayers = [@layers/sub.usda@]\n)\n"
            "def Xform \"World\" (\n prepend references = @layers/ref.usda@</Model>\n"
            " prepend payload = @layers/payload.usda@</Payload>\n"
            " variants = { string model = \"one\" }\n prepend variantSets = \"model\"\n)\n"
            "{\n double weight = 42\n asset image = @textures/picture.bin@\n"
            " variantSet \"model\" = {\n \"one\" {\n def \"Choice\" (references = @layers/variant.usd@</Variant>) {}\n }\n"
            " \"two\" {\n def \"Unused\" (references = @layers/inactive.usda@</Inactive>) {}\n }\n }\n}\n"},
        {directory / "layers" / "sub.usda", "#usda 1.0\ndef \"Sub\"\n{\n double sub = 5\n}\n"},
        {directory / "layers" / "ref.usda", "#usda 1.0\ndef \"Model\"\n{\n double ref = 11\n}\n"},
        {directory / "layers" / "payload.usda", "#usda 1.0\ndef \"Payload\"\n{\n double payload = 13\n}\n"},
        {directory / "layers" / "variant.usd", "#usda 1.0\ndef \"Variant\"\n{\n double selected = 17\n}\n"},
        {directory / "layers" / "inactive.usda", "#usda 1.0\ndef \"Inactive\"\n{\n double unused = 19\n}\n"},
        {directory / "textures" / "picture.bin", "independent actual asset bytes"}
    };
    for (const auto& file : files) { Write(file.first, file.second); }
    return files;
}
void FilesystemDomain(const fs::path& output);
void AdditionalDependencies(const fs::path& output);
void ResolverAnchors(const fs::path& output);
void SourceFreshness(const fs::path& output);
void ImportIsolation(const fs::path& output);
void ImportRollback(const fs::path& output);
void TypedExactness(const fs::path& output);
void MalformedAndBounds(const fs::path& output);
void InspectionOnly(const fs::path& output);
void ProcessMode(const fs::path& output, const std::string& mode);

inline fs::path Basic(const fs::path& directory)
{
    const auto path = directory / "source.usda";
    Write(path, "#usda 1.0\ndef \"World\"\n{\n double weight = 42\n}\n");
    return path;
}
inline Saved Edited(ReviewApi& api, openusd_stage* stage, openusd_layer* review, const fs::path& target)
{
    api.Apply(review, api.Capture(review, {{"/World.weight"}}), {SetDouble({"/World.weight"}, 47)});
    return api.Save(review, api.Binding(stage), target);
}
inline void RejectCapture(ReviewApi& api, openusd_layer* review, const Bytes& binding, const fs::path& target)
{
    openusd_edit_buffer* owner = reinterpret_cast<openusd_edit_buffer*>(uintptr_t{1});
    openusd_edit_buffer_view view{reinterpret_cast<const uint8_t*>(uintptr_t{1}), 1};
    const auto path = target.u8string();
    Require(openusd_layer_review_capture(review, binding.data(), binding.size(), path.data(), path.size(),
        &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR, "Changed/unverified source capture must fail.");
    Require(owner == nullptr && view.data == nullptr && view.size == 0 && api.message[0],
        "Failed capture returns actionable error and empty ownership.");
}
}
