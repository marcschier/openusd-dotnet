// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_property_inspection.h"

#include <filesystem>
#include <chrono>
#include <cstring>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>

namespace
{
void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

struct Snapshot
{
    openusd_property_snapshot* owner = nullptr;
    openusd_property_view view{};

    Snapshot()
    {
        view.struct_size = sizeof(view);
        view.version = OPENUSD_PROPERTY_INSPECTION_VERSION;
    }

    ~Snapshot() { openusd_property_snapshot_release(owner); }

    std::string String(size_t index) const
    {
        Require(index < view.string_count, "Property string index is outside snapshot.");
        return view.data + view.offsets[index];
    }

    const openusd_property_entry& Find(const char* name) const
    {
        for (size_t index = 0; index < view.entry_count; ++index)
        {
            if (String(view.entries[index].name) == name) return view.entries[index];
        }
        throw std::runtime_error(std::string("Property not found: ") + name);
    }
};

openusd_property_limits Limits()
{
    return {sizeof(openusd_property_limits), OPENUSD_PROPERTY_INSPECTION_VERSION,
        4096, 1024u * 1024u, 16, 16, 16, 262144, 4096, 0};
}

void Refuse(openusd_stage* stage, const openusd_property_limits& limits, const char* reason)
{
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    Snapshot snapshot;
    snapshot.owner = reinterpret_cast<openusd_property_snapshot*>(uintptr_t{1});
    snapshot.view.change_serial = 123;
    snapshot.view.entry_count = 99;
    const auto status = openusd_stage_get_prim_property_snapshot(
        stage, "/Subject", 0, 0, &limits, &snapshot.owner, &snapshot.view, &error);
    Require(status != OPENUSD_STATUS_OK && std::string(message).find(reason) != std::string::npos,
        "Property refusal did not include its actionable admission reason.");
    Require(snapshot.owner == nullptr && snapshot.view.change_serial == 0 &&
        snapshot.view.entries == nullptr && snapshot.view.entries_size == 0 && snapshot.view.entry_count == 0 &&
        snapshot.view.assets == nullptr && snapshot.view.assets_size == 0 && snapshot.view.asset_count == 0 &&
        snapshot.view.times == nullptr && snapshot.view.times_size == 0 && snapshot.view.time_count == 0 &&
        snapshot.view.data == nullptr && snapshot.view.data_size == 0 &&
        snapshot.view.offsets == nullptr && snapshot.view.offsets_size == 0 && snapshot.view.string_count == 0 &&
        snapshot.view.metadata_work == 0 && snapshot.view.is_complete == 0,
        "Property failure leaked an owner or partial success-shaped output.");
}

void Quotas(openusd_stage* stage)
{
    auto limits = Limits();
    limits.maximum_property_count = 0;
    Refuse(stage, limits, "property count");
    limits = Limits();
    limits.maximum_text_bytes = 1;
    Refuse(stage, limits, "text bytes");
    limits = Limits();
    limits.maximum_metadata_work = 0;
    Refuse(stage, limits, "metadata work");
    limits = Limits();
    limits.preview_elements = 17;
    Refuse(stage, limits, "bounded version 1");
    limits = Limits();
    limits.reserved = 1;
    Refuse(stage, limits, "bounded version 1");

    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    Snapshot invalidTime;
    Require(openusd_stage_get_prim_property_snapshot(stage, "/Subject", 1,
        std::numeric_limits<double>::infinity(), nullptr, &invalidTime.owner, &invalidTime.view, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT && invalidTime.owner == nullptr, "Infinite inspection time was accepted.");
    Snapshot missing;
    Require(openusd_stage_get_prim_property_snapshot(stage, "/Missing", 0, 0, nullptr,
        &missing.owner, &missing.view, &error) == OPENUSD_STATUS_NOT_FOUND && missing.owner == nullptr,
        "A missing prim returned a successful empty snapshot.");
    Snapshot noStage;
    Require(openusd_stage_get_prim_property_snapshot(nullptr, "/Subject", 0, 0, nullptr,
        &noStage.owner, &noStage.view, &error) == OPENUSD_STATUS_INVALID_ARGUMENT && noStage.owner == nullptr,
        "A missing stage was not refused safely.");
    alignas(openusd_property_view) unsigned char bytes[sizeof(openusd_property_view) + 8];
    std::memset(bytes, 0xa5, sizeof(bytes));
    const uint32_t shortSize = sizeof(uint32_t);
    std::memcpy(bytes, &shortSize, sizeof(shortSize));
    openusd_property_snapshot* owner = nullptr;
    Require(openusd_stage_get_prim_property_snapshot(stage, "/Subject", 0, 0, nullptr, &owner,
        reinterpret_cast<openusd_property_view*>(bytes), &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "An undersized property view was not rejected.");
    for (size_t index = shortSize; index < sizeof(bytes); ++index)
        Require(bytes[index] == 0xa5, "Property failure wrote beyond the caller's advertised view.");
}

void Scaling(const std::filesystem::path& directory)
{
    const auto wide = directory / "property-wide.usda";
    {
        std::ofstream file(wide);
        file << "#usda 1.0\ndef \"Subject\" {\n";
        for (size_t index = 0; index < 50000; ++index)
            file << " custom int p" << index << " = 7\n";
        file << "}\n";
    }
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    openusd_stage* stage = nullptr;
    Require(openusd_stage_open(wide.string().c_str(), &stage, &error) == OPENUSD_STATUS_OK,
        "Wide property fixture did not open.");
    auto limits = Limits();
    limits.maximum_property_count = 32;
    const auto start = std::chrono::steady_clock::now();
    Refuse(stage, limits, "property count");
    const auto countElapsed = std::chrono::steady_clock::now() - start;
    limits = Limits();
    limits.maximum_metadata_work = 32;
    Refuse(stage, limits, "metadata work");
    openusd_stage_release(stage);
    std::filesystem::remove(wide);
    std::cout << "property 50000 resident names: pre-copy count refusal_us="
        << std::chrono::duration_cast<std::chrono::microseconds>(countElapsed).count() << '\n';

    const auto large = directory / "property-large-values.usda";
    {
        std::ofstream file(large);
        file << "#usda 1.0\ndef \"Subject\" {\n custom string text = \"";
        for (size_t index = 0; index < 2 * 1024 * 1024; ++index) file << "\xc3\xa9";
        file << "\"\n custom int[] numbers = [";
        for (size_t index = 0; index < 1000000; ++index)
        {
            if (index) file << ',';
            file << index;
        }
        file << "]\n custom double sampled.timeSamples = {";
        for (size_t index = 0; index < 100000; ++index)
        {
            if (index) file << ',';
            file << index << ": " << index;
        }
        file << "}\n}\n";
    }
    Require(openusd_stage_open(large.string().c_str(), &stage, &error) == OPENUSD_STATUS_OK,
        "Large resident value fixture did not open.");
    limits = Limits();
    limits.maximum_text_bytes = 4096;
    limits.maximum_preview_text_bytes = 7;
    Snapshot snapshot;
    const auto valueStart = std::chrono::steady_clock::now();
    const auto status = openusd_stage_get_prim_property_snapshot(
        stage, "/Subject", 1, 2, &limits, &snapshot.owner, &snapshot.view, &error);
    const auto valueElapsed = std::chrono::steady_clock::now() - valueStart;
    openusd_stage_release(stage);
    if (status != OPENUSD_STATUS_OK) throw std::runtime_error(message);
    const auto& numbers = snapshot.Find("numbers");
    Require(numbers.value.total_count == 1000000 && numbers.value.count == 16 &&
        snapshot.String(numbers.value.offset + 15) == "15" && numbers.value.status == OPENUSD_PROPERTY_TRUNCATED,
        "A million-element resident array did not return its exact count and sixteen-element prefix.");
    const auto& text = snapshot.Find("text");
    Require(text.value.total_count == 1 && text.value.count == 1 &&
        snapshot.String(text.value.offset) == "\xc3\xa9\xc3\xa9\xc3\xa9" &&
        text.value.status == OPENUSD_PROPERTY_TRUNCATED && text.value.reason == OPENUSD_PROPERTY_REASON_TEXT_LIMIT,
        "A multi-megabyte resident UTF-8 string was copied whole or split mid-codepoint.");
    const auto& sampled = snapshot.Find("sampled");
    Require(sampled.time_samples.total_count == 100000 && sampled.time_samples.count == 16 &&
        snapshot.view.times[sampled.time_samples.offset + 15] == 15 &&
        snapshot.String(sampled.value.offset) == "2", "Large sample metadata was not counted and prefix-read natively.");
    Require(snapshot.view.data_size < 1024 && snapshot.view.metadata_work < 1000,
        "Large values grew inspection output or metadata work with their entire source size.");
    std::cout << "property resident array1000000/string4MiB/times100000: query_us="
        << std::chrono::duration_cast<std::chrono::microseconds>(valueElapsed).count()
        << " output_bytes=" << snapshot.view.data_size << " work=" << snapshot.view.metadata_work << '\n';
    std::filesystem::remove(large);
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 3, "Expected plugin and owned work directories.");
        char message[4096]{};
        openusd_error_buffer error{message, sizeof(message), 0};
        size_t plugins = 0;
        Require(openusd_register_plugins(argv[1], &plugins, &error) == OPENUSD_STATUS_OK,
            "Plugin registration failed.");
        const auto directory = std::filesystem::path(argv[2]);
        const auto referenceDirectory = directory / "reference-source";
        std::filesystem::create_directories(referenceDirectory);
        const auto reference = referenceDirectory / "property-reference.usda";
        const auto root = directory / "property-tracer.usda";
        std::ofstream(referenceDirectory / "valid.bin") << "native anchored asset";
        std::ofstream(reference) << R"(#usda 1.0
def Xform "Model"
{
    custom double referenced = 19
    custom asset validAsset = @valid.bin@
    custom asset missingAsset = @missing.bin@
}
)";
        std::ofstream(root) << R"(#usda 1.0
class "Inherited"
{
    custom int inherited = 29
}
def Xform "Subject" (
    references = @reference-source/property-reference.usda@</Model>
    inherits = </Inherited>
)
{
    custom int local = 7
    custom double referenced
    custom double animated.timeSamples = { 1: 2, 3: 6 }
    custom double3 precise = (1.25, 2.5, 3.75)
    custom int[] numbers = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19]
    custom float connected = 4
    custom float connected.connect = </Source.outputs:value>
    custom int blocked = None
    custom int unset
    custom rel link = </Destination>
}
def "Source"
{
    float outputs:value = 23
}
def Scope "Destination" {}
)";
        openusd_stage* stage = nullptr;
        if (openusd_stage_open(root.string().c_str(), &stage, &error) != OPENUSD_STATUS_OK)
            throw std::runtime_error(message);
        uint64_t before = 0;
        Require(openusd_stage_get_change_serial(stage, &before, &error) == OPENUSD_STATUS_OK,
            "Could not read initial stage serial.");
        Quotas(stage);
        Snapshot snapshot;
        const auto status = openusd_stage_get_prim_property_snapshot(
            stage, "/Subject", 1, 1, nullptr, &snapshot.owner, &snapshot.view, &error);
        uint64_t after = 0;
        Require(openusd_stage_get_change_serial(stage, &after, &error) == OPENUSD_STATUS_OK,
            "Could not read final stage serial.");
        openusd_stage_release(stage);
        if (status != OPENUSD_STATUS_OK) throw std::runtime_error(message);
        Require(snapshot.owner && snapshot.view.change_serial == before && before == after &&
            snapshot.String(0) == "/Subject", "Property snapshot is not a detached read-only result.");
        const auto& local = snapshot.Find("local");
        Require(local.resolve_source == OPENUSD_PROPERTY_SOURCE_DEFAULT &&
            local.value_state == OPENUSD_PROPERTY_VALUE_PRESENT &&
            local.value.total_count == 1 && snapshot.String(local.value.offset) == "7",
            "Local typed scalar value is incorrect.");
        const auto& referenced = snapshot.Find("referenced");
        Require(snapshot.String(referenced.value.offset) == "19" &&
            snapshot.String(referenced.source_layer).find("property-reference.usda") != std::string::npos &&
            snapshot.String(referenced.source_path) == "/Model.referenced",
            "A local declaration was substituted for native winning referenced value provenance.");
        const auto& fallback = snapshot.Find("visibility");
        Require(fallback.resolve_source == OPENUSD_PROPERTY_SOURCE_FALLBACK &&
            snapshot.String(fallback.value.offset) == "inherited" &&
            snapshot.String(fallback.source_layer).empty(), "Schema fallback was not distinguished.");
        const auto& animated = snapshot.Find("animated");
        Require(animated.resolve_source == OPENUSD_PROPERTY_SOURCE_TIME_SAMPLES &&
            snapshot.String(animated.value.offset) == "2" &&
            animated.time_samples.total_count == 2 && animated.time_samples.count == 2 &&
            snapshot.view.times[animated.time_samples.offset] == 1 &&
            snapshot.view.times[animated.time_samples.offset + 1] == 3,
            "Numeric value and exact time sample preview are incorrect.");
        const auto& link = snapshot.Find("link");
        Require(link.kind == 1 && link.targets.total_count == 1 &&
            snapshot.String(link.targets.offset) == "/Destination", "Relationship target is incorrect.");
        const auto& precise = snapshot.Find("precise");
        Require(snapshot.String(precise.type) == "double3" &&
            precise.value.status == OPENUSD_PROPERTY_COMPLETE &&
            snapshot.String(precise.value.offset) == "(1.25, 2.5, 3.75)",
            "Double vector preview lost its native type or components.");
        const auto& numbers = snapshot.Find("numbers");
        Require((numbers.flags & 4) != 0 && numbers.value.total_count == 20 &&
            numbers.value.count == 16 && numbers.value.status == OPENUSD_PROPERTY_TRUNCATED &&
            snapshot.String(numbers.value.offset) == "0" &&
            snapshot.String(numbers.value.offset + 15) == "15",
            "Typed array did not preserve its exact native count and bounded prefix.");
        const auto& inherited = snapshot.Find("inherited");
        Require(snapshot.String(inherited.value.offset) == "29" &&
            snapshot.String(inherited.source_path) == "/Inherited.inherited",
            "Inherited winning value provenance is incorrect.");
        const auto& connected = snapshot.Find("connected");
        Require(connected.resolve_source == OPENUSD_PROPERTY_SOURCE_DEFAULT &&
            snapshot.String(connected.value.offset) == "4" && connected.targets.total_count == 1 &&
            snapshot.String(connected.targets.offset) == "/Source.outputs:value",
            "A connected USD default was replaced by a flattened graph value.");
        const auto& blocked = snapshot.Find("blocked");
        Require(blocked.value_state == OPENUSD_PROPERTY_VALUE_BLOCKED &&
            (blocked.flags & 24) == 24 && blocked.value.count == 0, "Blocked opinion was reported as unset.");
        const auto& unset = snapshot.Find("unset");
        Require(unset.value_state == OPENUSD_PROPERTY_VALUE_UNSET &&
            unset.resolve_source == OPENUSD_PROPERTY_SOURCE_NONE &&
            (unset.flags & 24) == 8 && (unset.flags & 2) != 0,
            "An authored declaration was confused with an authored value.");
        const auto& validAsset = snapshot.Find("validAsset");
        Require(validAsset.asset_count == 1, "Valid native asset fields are absent.");
        const auto& validFields = snapshot.view.assets[validAsset.asset_offset];
        Require(snapshot.String(validFields.string_offset) == "valid.bin" &&
            snapshot.String(validFields.string_offset + 1).empty() &&
            std::filesystem::equivalent(snapshot.String(validFields.string_offset + 2), referenceDirectory / "valid.bin") &&
            snapshot.String(validFields.string_offset + 3).find("property-reference.usda") != std::string::npos &&
            validFields.missing == 0, "Asset authored/evaluated/resolved/native winning anchor fields are incorrect.");
        const auto& missingAsset = snapshot.Find("missingAsset");
        Require(missingAsset.asset_count == 1 &&
            snapshot.view.assets[missingAsset.asset_offset].missing == 1,
            "Missing asset was not distinguished from a valid resolver result.");
        for (size_t index = 1; index < snapshot.view.entry_count; ++index)
        {
            Require(snapshot.String(snapshot.view.entries[index - 1].name) <
                snapshot.String(snapshot.view.entries[index].name), "Property order is not canonical.");
        }
        std::filesystem::remove(root);
        std::filesystem::remove(reference);
        std::filesystem::remove(referenceDirectory / "valid.bin");
        std::filesystem::remove(referenceDirectory);
        std::cout << "property tracer: local/reference-winning/fallback/exact-samples/relationship/detached"
            "/double-vector/typed-array/inherit/connection/block/unset/assets PASS\n";
        Scaling(directory);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
