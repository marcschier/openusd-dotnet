// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/sceneState.h"

#include <cstdlib>
#include <iostream>
#include <limits>
#include <memory>
#include <new>
#include <string>

namespace
{
thread_local bool countAllocations = false;
thread_local bool allocationFailed = false;
thread_local size_t allocationIndex = 0;
thread_local size_t failAt = std::numeric_limits<size_t>::max();

void* Allocate(size_t size)
{
    if (countAllocations && allocationIndex++ == failAt)
    {
        countAllocations = false;
        allocationFailed = true;
        throw std::bad_alloc();
    }
    if (void* value = std::malloc(size == 0 ? 1 : size))
    {
        return value;
    }
    throw std::bad_alloc();
}

void BeginAllocations(size_t failure = std::numeric_limits<size_t>::max())
{
    allocationIndex = 0;
    failAt = failure;
    allocationFailed = false;
    countAllocations = true;
}

void Require(bool value, const char* message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}
}

// This executable owns the retained-state implementation, so ordinary C++
// allocations in BuildPage can fail without hooks in the shipped library.
void* operator new(size_t size) { return Allocate(size); }
void* operator new[](size_t size) { return Allocate(size); }
void operator delete(void* value) noexcept { std::free(value); }
void operator delete[](void* value) noexcept { std::free(value); }
void operator delete(void* value, size_t) noexcept { std::free(value); }
void operator delete[](void* value, size_t) noexcept { std::free(value); }

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
HdSilkMeshRecord Mesh(const std::string& path, float height)
{
    HdSilkMeshRecord record;
    record.path = path;
    record.primId = 7;
    record.topologyRevision = height == 0 ? 1 : 2;
    record.points = {0, 0, height, 1, 0, height, 0, 1, height};
    record.indices = {0, 1, 2};
    record.triangleSubprims = {3};
    record.materialPath = "/Materials/KeepMaterial";
    return record;
}

HdSilkMaterialRecord Material(const std::string& path, float roughness)
{
    HdSilkMaterialRecord record;
    record.path = path;
    record.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;
    HdSilkMaterialScalar scalar;
    scalar.parameter = OPENUSD_SILK_MATERIAL_ROUGHNESS;
    scalar.componentCount = 1;
    scalar.value[0] = roughness;
    record.scalars = {scalar};
    return record;
}

HdSilkLightRecord Dome(const std::string& path, const std::string& texture)
{
    HdSilkLightRecord record;
    record.path = path;
    record.ambientOnly = true;
    record.textureAsset = texture;
    record.textureFormat = OPENUSD_SILK_DOME_TEXTURE_LATLONG;
    return record;
}

std::unique_ptr<HdSilkSceneState> PreparedState()
{
    auto state = std::make_unique<HdSilkSceneState>();
    HdSilkLightRecord direct;
    direct.path = "/Lights/Directional";
    direct.type = 1;
    direct.shadowEnabled = 1;
    direct.lightLinkCategory = "receivers";
    direct.shadowLinkCategory = "casters";
    state->ReplaceLight(direct);
    state->SetCategoryMemberships({{"/World/KeepMesh", -1, {"receivers", "casters"}}}, false);
    state->ReplaceLight(Dome("/Environment/KeepDome", "environment-before.exr"));
    state->ReplaceLight(Dome("/Environment/RemoveDome", "environment-removed.exr"));
    state->ReplaceMaterial(Material("/Materials/KeepMaterial", 0.25f));
    state->ReplaceMaterial(Material("/Materials/RemoveMaterial", 0.25f));
    state->ReplaceMeshInstances("/World/KeepMesh", {Mesh("/World/KeepMesh", 0)});
    state->ReplaceMeshInstances("/World/RemoveMesh", {Mesh("/World/RemoveMesh", 0)});
    uint64_t revision = 0;
    uint32_t commands = 0;
    const auto baseline = state->BuildPage(&revision, &commands);
    Require(revision == 1 && !baseline.empty(), "The fixture did not publish its initial scene.");

    state->ReplaceLight(Dome("/Environment/KeepDome", "environment-after.exr"));
    state->RemoveLight("/Environment/RemoveDome");
    state->ReplaceLight(Dome("/Environment/NewDome", "environment-added.exr"));
    state->ReplaceMaterial(Material("/Materials/KeepMaterial", 0.75f));
    state->RemoveMaterial("/Materials/RemoveMaterial");
    state->ReplaceMeshInstances("/World/KeepMesh", {Mesh("/World/KeepMesh", 2)});
    state->RemoveMesh("/World/RemoveMesh");
    state->SetCategoryMemberships({{"/World/KeepMesh", -1, {}}}, false);
    return state;
}

uint32_t ReadU32(const std::vector<uint8_t>& bytes, size_t offset)
{
    Require(offset + 4 <= bytes.size(), "The control page is truncated.");
    return uint32_t{bytes[offset]} | (uint32_t{bytes[offset + 1]} << 8) |
        (uint32_t{bytes[offset + 2]} << 16) | (uint32_t{bytes[offset + 3]} << 24);
}

void RequireControlCommands(const std::vector<uint8_t>& bytes, uint32_t count)
{
    uint32_t observed[10] = {};
    size_t offset = 0;
    for (uint32_t command = 0; command < count; ++command)
    {
        const uint32_t type = ReadU32(bytes, offset);
        const uint32_t size = ReadU32(bytes, offset + 4);
        Require(type < 10 && size >= 8 && size <= bytes.size() - offset, "The control command is invalid.");
        ++observed[type];
        offset += size;
    }
    Require(offset == bytes.size() &&
        observed[OPENUSD_SILK_COMMAND_FRAME] == 1 &&
        observed[OPENUSD_SILK_COMMAND_ENVIRONMENT_UPSERT] == 2 &&
        observed[OPENUSD_SILK_COMMAND_ENVIRONMENT_REMOVE] == 1 &&
        observed[OPENUSD_SILK_COMMAND_MATERIAL_UPSERT] == 1 &&
        observed[OPENUSD_SILK_COMMAND_MATERIAL_REMOVE] == 1 &&
        observed[OPENUSD_SILK_COMMAND_MESH_UPSERT] == 1 &&
        observed[OPENUSD_SILK_COMMAND_MESH_REMOVE] == 1 &&
        observed[OPENUSD_SILK_COMMAND_LIGHT_LINK] == 1 &&
        observed[OPENUSD_SILK_COMMAND_SHADOW] == 1,
        "The control must cover environments, materials, meshes, linking and shadows.");
}

void EveryAllocationFailurePreservesTheCompleteRetry()
{
    auto control = PreparedState();
    uint64_t expectedRevision = 0;
    uint32_t expectedCount = 0;
    BeginAllocations();
    const auto expected = control->BuildPage(&expectedRevision, &expectedCount);
    countAllocations = false;
    const size_t total = allocationIndex;
    Require(total > 10 && total < 4096, "The allocation sweep did not cover a bounded real page.");
    Require(expectedRevision == 2, "The control revision did not advance once.");
    RequireControlCommands(expected, expectedCount);
    uint64_t expectedQuietRevision = 0;
    uint32_t expectedQuietCount = 0;
    const auto expectedQuiet = control->BuildPage(&expectedQuietRevision, &expectedQuietCount);

    for (size_t failure = 0; failure < total; ++failure)
    {
        auto state = PreparedState();
        uint64_t revision = 0x123456789abcdef0ull;
        uint32_t commands = 0xa5a5a5a5u;
        std::vector<uint8_t> bytes{0x12, 0x34};
        const auto rejectedMeshes = HdSilkSceneState::GetRejectedMeshCount();
        const auto rejectedMaterials = HdSilkSceneState::GetRejectedMaterialCount();
        bool threw = false;
        BeginAllocations(failure);
        try
        {
            bytes = state->BuildPage(&revision, &commands);
        }
        catch (const std::bad_alloc&)
        {
            threw = true;
        }
        countAllocations = false;
        if (!allocationFailed || !threw)
        {
            throw std::runtime_error("Allocation " + std::to_string(failure) +
                " was swallowed as a successful partial page.");
        }
        Require(revision == 0x123456789abcdef0ull && commands == 0xa5a5a5a5u &&
            bytes == std::vector<uint8_t>({0x12, 0x34}),
            "An allocation failure changed caller outputs.");
        Require(HdSilkSceneState::GetRejectedMeshCount() == rejectedMeshes &&
            HdSilkSceneState::GetRejectedMaterialCount() == rejectedMaterials,
            "An allocation failure was counted as malformed scene data.");

        bytes = state->BuildPage(&revision, &commands);
        if (revision != expectedRevision || commands != expectedCount || bytes != expected)
        {
            throw std::runtime_error("Allocation " + std::to_string(failure) +
                " acknowledged state that the complete retry must still publish.");
        }
        const auto quiet = state->BuildPage(&revision, &commands);
        Require(quiet == expectedQuiet && revision == expectedQuietRevision && commands == expectedQuietCount,
            "A successful retry did not retire its pending state exactly once.");
    }
    std::cout << "EveryAllocationFailurePreservesTheCompleteRetry: " << total << " allocation points passed\n";
}
}

int main()
{
    try
    {
        EveryAllocationFailurePreservesTheCompleteRetry();
        return 0;
    }
    catch (const std::exception& error)
    {
        countAllocations = false;
        std::cerr << "hdsilk_publication_allocation_probe: " << error.what() << '\n';
        return 1;
    }
}
