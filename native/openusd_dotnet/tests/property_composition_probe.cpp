// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_property_inspection.h"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
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
};

void Run(const char* plugins, const std::filesystem::path& directory, const std::string& mode)
{
    std::filesystem::create_directories(directory);
    const auto path = directory / ("composition-" + mode + ".usda");
    const auto missing = directory / ("missing-" + mode + ".usda");
    Require(!std::filesystem::exists(path) && !std::filesystem::exists(missing),
        "Composition probe refuses to overwrite a source.");
    std::string text("#usda 1.0\n");
    const bool layerStack = mode == "layer-stack";
    const bool unrelated = mode == "unrelated";
    const bool ancestor = mode == "ancestor";
    const bool local = mode == "external-local" || mode == "internal-local" || layerStack || unrelated;
    if (layerStack) text += "(subLayers = [@" + missing.filename().string() + "@])\n";
    if (ancestor) text += "def \"Container\" (references = </Missing>) {\n";
    text += "def \"Subject\"";
    if (mode == "external" || mode == "external-local" || mode == "repair")
        text += " (references = @" + missing.filename().string() + "@</Model>)";
    else if (mode == "internal" || mode == "internal-local")
        text += " (references = </Missing>)";
    text += "\n{\n";
    if (local) text += " custom double answer = 7\n";
    text += "}\n";
    if (ancestor) text += "}\n";
    if (unrelated) text += "def \"Unrelated\" (references = </Missing>) {}\n";
    {
        std::ofstream file(path);
        file << text;
    }
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    size_t registered = 0;
    Require(openusd_register_plugins(plugins, &registered, &error) == OPENUSD_STATUS_OK, message);
    openusd_stage* raw = nullptr;
    Require(openusd_stage_open(path.string().c_str(), &raw, &error) == OPENUSD_STATUS_OK, message);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(raw, openusd_stage_release);
    uint64_t serial = 0;
    Require(openusd_stage_get_change_serial(stage.get(), &serial, &error) == OPENUSD_STATUS_OK, message);
    Snapshot snapshot;
    snapshot.view.change_serial = 123;
    snapshot.view.entry_count = 99;
    snapshot.view.is_complete = 1;
    const auto status = openusd_stage_get_prim_property_snapshot(
        stage.get(), ancestor ? "/Container/Subject" : "/Subject", 0, 0, nullptr,
        &snapshot.owner, &snapshot.view, &error);
    if (unrelated)
    {
        Require(status == OPENUSD_STATUS_OK && snapshot.owner != nullptr && snapshot.view.is_complete == 1 &&
            snapshot.view.entry_count == 1, "An unrelated prim error incorrectly poisoned the selected inventory.");
    }
    else
    {
        Require(status == OPENUSD_STATUS_NATIVE_ERROR, "Unproven property inventory was reported as complete.");
        Require(snapshot.owner == nullptr && snapshot.view.entry_count == 0 && snapshot.view.entries == nullptr &&
            snapshot.view.entries_size == 0 && snapshot.view.assets == nullptr && snapshot.view.asset_count == 0 &&
            snapshot.view.assets_size == 0 && snapshot.view.times == nullptr && snapshot.view.time_count == 0 &&
            snapshot.view.times_size == 0 && snapshot.view.data == nullptr && snapshot.view.data_size == 0 &&
            snapshot.view.offsets == nullptr && snapshot.view.offsets_size == 0 && snapshot.view.string_count == 0 &&
            snapshot.view.is_complete == 0 && snapshot.view.change_serial == 0 && snapshot.view.metadata_work == 0,
            "Composition error exposed a native owner or partial/success-shaped snapshot.");
        Require(std::string(message).find("composition") != std::string::npos &&
            std::string(message).find(missing.filename().string()) == std::string::npos,
            "Composition refusal must use a fixed bounded diagnostic, not stringify stored SDK errors.");
    }
    uint64_t after = 0;
    Require(openusd_stage_get_change_serial(stage.get(), &after, &error) == OPENUSD_STATUS_OK && after == serial,
        "Property inspection changed the stage serial.");
    if (mode == "repair")
    {
        {
            std::ofstream file(missing);
            file << "#usda 1.0\ndef \"Model\" {\n custom double answer = 7\n}\n";
        }
        Require(openusd_stage_reload(stage.get(), &error) == OPENUSD_STATUS_OK, message);
        Snapshot repaired;
        Require(openusd_stage_get_prim_property_snapshot(stage.get(), "/Subject", 0, 0, nullptr,
            &repaired.owner, &repaired.view, &error) == OPENUSD_STATUS_OK &&
            repaired.view.is_complete == 1 && repaired.view.entry_count == 1,
            "Repaired composition did not restore complete property inspection.");
    }
    stage.reset();
    std::filesystem::remove(path);
    if (std::filesystem::exists(missing)) std::filesystem::remove(missing);
    std::cout << "PROPERTY_COMPOSITION_OK: " << mode << '\n';
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 4, "Expected plugin path, owned work directory and case.");
        Run(argv[1], argv[2], argv[3]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
