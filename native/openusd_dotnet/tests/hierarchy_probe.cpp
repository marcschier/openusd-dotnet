// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hierarchy.h"

#include "pxr/usd/sdf/layer.h"

#include <chrono>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Require(bool value, const char* message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

struct Snapshot
{
    openusd_hierarchy_snapshot* owner = nullptr;
    openusd_hierarchy_view view{};

    Snapshot()
    {
        view.struct_size = sizeof(view);
        view.version = OPENUSD_HIERARCHY_VERSION;
    }

    ~Snapshot()
    {
        openusd_hierarchy_snapshot_release(owner);
    }

    std::string String(size_t index) const
    {
        Require(index < view.string_count, "String index outside snapshot.");
        return view.data + view.offsets[index];
    }

    std::string EntryString(size_t index, size_t field) const
    {
        Require(index < view.entry_count && field < 4, "Entry index outside snapshot.");
        return String(view.entries[index].string_offset + field);
    }
};

openusd_hierarchy_limits Limits()
{
    return {sizeof(openusd_hierarchy_limits), OPENUSD_HIERARCHY_VERSION,
        100000, 16u * 1024u * 1024u, 256, 4096, 65536, 2000000};
}

void Refusal(openusd_stage* stage, const openusd_hierarchy_limits& limits, const char* expected)
{
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    Snapshot snapshot;
    snapshot.owner = reinterpret_cast<openusd_hierarchy_snapshot*>(uintptr_t{1});
    const auto status = openusd_stage_get_hierarchy_snapshot(
        stage, &limits, &snapshot.owner, &snapshot.view, &error);
    Require(snapshot.owner == nullptr, "Refused hierarchy query leaked or retained an owner.");
    Require(status != OPENUSD_STATUS_OK && std::string(message).find(expected) != std::string::npos,
        "Expected an actionable hierarchy refusal.");
    Require(snapshot.view.entries == nullptr && snapshot.view.entry_count == 0 &&
        snapshot.view.entries_size == 0 && snapshot.view.variant_sets == nullptr &&
        snapshot.view.variant_set_count == 0 && snapshot.view.variant_sets_size == 0 &&
        snapshot.view.data == nullptr && snapshot.view.data_size == 0 &&
        snapshot.view.offsets == nullptr && snapshot.view.offsets_size == 0 &&
        snapshot.view.string_count == 0 && snapshot.view.is_complete == 0 &&
        snapshot.view.change_serial == 0 && snapshot.view.metadata_work == 0,
        "Refused hierarchy query exposed partial data.");
}

void Quotas(openusd_stage* stage)
{
    auto limits = Limits();
    limits.maximum_prim_count = 1;
    Refusal(stage, limits, "prim count");
    limits = Limits();
    limits.maximum_text_bytes = 4;
    Refusal(stage, limits, "text bytes");
    limits = Limits();
    limits.maximum_depth = 1;
    Refusal(stage, limits, "depth");
    limits = Limits();
    limits.maximum_variant_sets = 0;
    Refusal(stage, limits, "variant sets");
    limits = Limits();
    limits.maximum_variant_names = 1;
    Refusal(stage, limits, "variant names");
    limits = Limits();
    limits.maximum_metadata_work = 0;
    Refusal(stage, limits, "metadata work");
    limits = Limits();
    limits.version = 9;
    Refusal(stage, limits, "version 1");
    limits = Limits();
    limits.maximum_prim_count = OPENUSD_HIERARCHY_MAX_PRIMS + 1;
    Refusal(stage, limits, "bounded");

    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    Snapshot invalid;
    invalid.view.version = 9;
    Require(openusd_stage_get_hierarchy_snapshot(stage, nullptr, &invalid.owner, &invalid.view, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT && invalid.owner == nullptr && invalid.view.entry_count == 0,
        "Invalid hierarchy view version was not cleared and refused.");
    Snapshot noStage;
    Require(openusd_stage_get_hierarchy_snapshot(nullptr, nullptr, &noStage.owner, &noStage.view, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT && noStage.owner == nullptr,
        "A missing stage was not refused.");

    alignas(openusd_hierarchy_view) unsigned char storage[sizeof(openusd_hierarchy_view) + 8];
    std::memset(storage, 0xA5, sizeof(storage));
    const uint32_t shortSize = sizeof(uint32_t);
    std::memcpy(storage, &shortSize, sizeof(shortSize));
    openusd_hierarchy_snapshot* owner = nullptr;
    Require(openusd_stage_get_hierarchy_snapshot(stage, nullptr, &owner,
        reinterpret_cast<openusd_hierarchy_view*>(storage), &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "An undersized view was not refused.");
    for (size_t index = shortSize; index < sizeof(storage); ++index)
    {
        Require(storage[index] == 0xA5, "Refusal wrote beyond the advertised view.");
    }
}

void Scaling(const std::filesystem::path& directory)
{
    const auto path = directory / "hierarchy-wide.usda";
    {
        std::ofstream file(path);
        file << "#usda 1.0\n";
        for (int index = 0; index < 50000; ++index)
        {
            file << "def Scope \"P" << index << "\" {}\n";
        }
    }
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    openusd_stage* stage = nullptr;
    Require(openusd_stage_open(path.string().c_str(), &stage, &error) == OPENUSD_STATUS_OK,
        "Wide hierarchy fixture failed to open.");
    auto limits = Limits();
    limits.maximum_prim_count = 32;
    auto start = std::chrono::steady_clock::now();
    Refusal(stage, limits, "prim count");
    const auto countTime = std::chrono::steady_clock::now() - start;
    limits = Limits();
    limits.maximum_text_bytes = 16;
    start = std::chrono::steady_clock::now();
    Refusal(stage, limits, "text bytes");
    const auto textTime = std::chrono::steady_clock::now() - start;
    limits = Limits();
    Snapshot snapshot;
    start = std::chrono::steady_clock::now();
    const auto status = openusd_stage_get_hierarchy_snapshot(stage, &limits, &snapshot.owner, &snapshot.view, &error);
    const auto fullTime = std::chrono::steady_clock::now() - start;
    openusd_stage_release(stage);
    Require(status == OPENUSD_STATUS_OK && snapshot.view.entry_count == 50000 &&
        snapshot.EntryString(49999, 0) == "/P49999", "Wide bulk hierarchy was not complete.");
    std::cout << "hierarchy 50000 rows: full_us="
        << std::chrono::duration_cast<std::chrono::microseconds>(fullTime).count()
        << " count_refusal_us=" << std::chrono::duration_cast<std::chrono::microseconds>(countTime).count()
        << " text_refusal_us=" << std::chrono::duration_cast<std::chrono::microseconds>(textTime).count()
        << " output_bytes=" << snapshot.view.data_size << " metadata_work=" << snapshot.view.metadata_work << '\n';
    std::filesystem::remove(path);

    const auto longPath = directory / "hierarchy-long-name.usda";
    {
        std::ofstream file(longPath);
        file << "#usda 1.0\ndef Scope \"" << std::string(524288, 'N') << "\" {}\n";
    }
    stage = nullptr;
    Require(openusd_stage_open(longPath.string().c_str(), &stage, &error) == OPENUSD_STATUS_OK,
        "Long resident-name fixture failed to open.");
    limits.maximum_text_bytes = 32;
    start = std::chrono::steady_clock::now();
    Refusal(stage, limits, "text bytes");
    std::cout << "hierarchy resident 512KiB name: pre-copy text refusal_us="
        << std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - start).count()
        << '\n';
    openusd_stage_release(stage);
    std::filesystem::remove(longPath);
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 3, "Expected plugin and work directories.");
        Require(openusd_get_abi_version() == OPENUSD_DATA_ABI_VERSION &&
            (openusd_get_capabilities() & OPENUSD_CAPABILITY_HIERARCHY_SNAPSHOT) != 0,
            "The hierarchy probe requires the matched ABI and capability.");
        char message[4096]{};
        openusd_error_buffer error{message, sizeof(message), 0};
        size_t plugins = 0;
        Require(openusd_register_plugins(argv[1], &plugins, &error) == OPENUSD_STATUS_OK,
            "Plugin registration failed.");
        const auto path = std::filesystem::path(argv[2]) / "hierarchy-tracer.usda";
        std::ofstream(path) << R"(#usda 1.0
def Xform "World" (
    prepend variantSets = ["look", "lod"]
    variants = { string look = "zebra" }
)
{
    variantSet "look" = {
        "zebra" {}
        "amber" {}
    }
    variantSet "lod" = {
        "low" {}
        "high" {}
    }
    def Scope "Zulu" {}
    def Mesh "Alpha" {}
}
def Scope "Other" {}
)";
        openusd_stage* stage = nullptr;
        Require(openusd_stage_open(path.string().c_str(), &stage, &error) == OPENUSD_STATUS_OK,
            "Stage opening failed.");
        const auto root = SdfLayer::FindOrOpen(path.string());
        std::string original;
        Require(root && root->ExportToString(&original), "Could not capture the source layer.");
        const bool originallyDirty = root->IsDirty();
        uint64_t serialBefore = 0;
        Require(openusd_stage_get_change_serial(stage, &serialBefore, &error) == OPENUSD_STATUS_OK,
            "Could not capture the stage serial.");
        Quotas(stage);
        const auto limits = Limits();
        Snapshot snapshot;
        const openusd_status status = openusd_stage_get_hierarchy_snapshot(
            stage, &limits, &snapshot.owner, &snapshot.view, &error);
        uint64_t serialAfter = 0;
        Require(openusd_stage_get_change_serial(stage, &serialAfter, &error) == OPENUSD_STATUS_OK &&
            serialBefore == serialAfter, "Hierarchy reads changed the stage serial.");
        std::string after;
        Require(root->ExportToString(&after) && after == original && root->IsDirty() == originallyDirty,
            "Hierarchy reads changed source metadata/content or dirty state.");
        openusd_stage_release(stage);
        if (status != OPENUSD_STATUS_OK)
        {
            throw std::runtime_error(message);
        }
        Require(snapshot.owner != nullptr && snapshot.view.is_complete == 1,
            "Expected an owned complete snapshot.");
        Require(snapshot.view.entry_count == 4, "Expected four hierarchy entries.");
        Require(snapshot.EntryString(0, 0) == "/World", "Root order changed.");
        Require(snapshot.EntryString(1, 0) == "/World/Zulu", "Native child order changed.");
        Require(snapshot.EntryString(2, 0) == "/World/Alpha", "Native child order changed.");
        Require(snapshot.EntryString(3, 0) == "/Other", "Native root order changed.");
        Require(snapshot.view.entries[0].parent_index == -1 &&
            snapshot.view.entries[0].depth == 1 && snapshot.view.entries[0].child_count == 2,
            "Incorrect root hierarchy shape.");
        Require(snapshot.view.entries[2].parent_index == 0 &&
            snapshot.view.entries[2].depth == 2 && snapshot.view.entries[2].child_count == 0,
            "Incorrect child hierarchy shape.");
        Require(snapshot.EntryString(2, 2) == "Mesh", "Type token missing.");
        Require(snapshot.view.variant_set_count == 2 && snapshot.view.entries[0].variant_count == 2,
            "Expected composed inline variant selectors.");
        const auto firstVariant = snapshot.view.variant_sets[0];
        Require(firstVariant.variant_count == 2 && snapshot.String(firstVariant.string_offset) == "look" &&
            snapshot.String(firstVariant.string_offset + 1) == "zebra" &&
            snapshot.String(firstVariant.string_offset + 2) == "amber" &&
            snapshot.String(firstVariant.string_offset + 3) == "zebra",
            "Variant set order, native choice order or applied selection changed.");
        Scaling(argv[2]);
        std::filesystem::remove(path);
        std::cout << "hierarchy public C ABI passed: ownership, quotas, exact metadata/content and scaling\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
