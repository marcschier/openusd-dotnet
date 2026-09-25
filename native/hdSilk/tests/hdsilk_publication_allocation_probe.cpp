// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/sceneState.h"
#include "../src/pageWriter.h"

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

void EveryAllocationFailurePreservesTheCompleteRetry(bool deferred = false)
{
    auto control = PreparedState();
    uint64_t expectedRevision = 0;
    uint32_t expectedCount = 0;
    BeginAllocations();
    const auto expected = control->BuildPage(&expectedRevision, &expectedCount, SIZE_MAX, deferred);
    countAllocations = false;
    const size_t total = allocationIndex;
    Require(total > 10 && total < 4096, "The allocation sweep did not cover a bounded real page.");
    Require(expectedRevision == 2, "The control revision did not advance once.");
    RequireControlCommands(expected, expectedCount);
    if (deferred)
    {
        BeginAllocations(0);
        const bool acknowledged = control->CompletePage(expectedRevision, true);
        countAllocations = false;
        Require(acknowledged && allocationIndex == 0, "Acknowledgement allocated memory or refused its pending page.");
    }
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
            bytes = state->BuildPage(&revision, &commands, SIZE_MAX, deferred);
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

        bytes = state->BuildPage(&revision, &commands, SIZE_MAX, deferred);
        if (revision != expectedRevision || commands != expectedCount || bytes != expected)
        {
            throw std::runtime_error("Allocation " + std::to_string(failure) +
                " acknowledged state that the complete retry must still publish.");
        }
        if (deferred)
        {
            Require(state->CompletePage(revision, true), "A successful deferred retry could not be acknowledged.");
        }
        const auto quiet = state->BuildPage(&revision, &commands);
        Require(quiet == expectedQuiet && revision == expectedQuietRevision && commands == expectedQuietCount,
            "A successful retry did not retire its pending state exactly once.");
    }
    std::cout << (deferred ? "DeferredPublicationAllocationFailures: " :
        "EveryAllocationFailurePreservesTheCompleteRetry: ") << total << " allocation points passed\n";
}

void DeferredAcknowledgementPreservesAllUpdatesAndRetirements()
{
    auto control = PreparedState();
    uint64_t revision = 0;
    uint32_t commands = 0;
    const auto expected = control->BuildPage(&revision, &commands);
    RequireControlCommands(expected, commands);
    auto state = PreparedState();
    for (int attempt = 0; attempt < 3; ++attempt)
    {
        const auto page = state->BuildPage(&revision, &commands, expected.size(), true);
        Require(page == expected, "Releasing an unacknowledged page changed the complete retry bytes.");
        Require(!state->CompletePage(revision + 1, true), "A stale acknowledgement was accepted.");
        bool blocked = false;
        try { (void)state->BuildPage(nullptr, nullptr); }
        catch (const std::logic_error&) { blocked = true; }
        Require(blocked, "Another page was built before resolving the pending publication.");
        blocked = false;
        try { state->RemoveMesh("/World/KeepMesh"); }
        catch (const std::logic_error&) { blocked = true; }
        Require(blocked, "A pending publication did not protect borrowed dirty entries from mutation.");
        Require(state->CompletePage(revision, attempt == 2), "The pending page could not be resolved.");
    }
    const auto quiet = state->BuildPage(&revision, &commands);
    Require(commands == 1 && ReadU32(quiet, 0) == OPENUSD_SILK_COMMAND_FRAME,
        "Acknowledgement did not consume each update and retirement exactly once.");
    std::cout << "DeferredAcknowledgementPreservesAllUpdatesAndRetirements: exact bytes and quiet follow-up passed\n";
}

void PageByteLimitsPreserveCompleteUpdatesAndRetirements()
{
    auto control = PreparedState();
    uint64_t expectedRevision = 0;
    uint32_t expectedCount = 0;
    const auto expected = control->BuildPage(&expectedRevision, &expectedCount);
    RequireControlCommands(expected, expectedCount);
    std::vector<size_t> limits{1, 7, expected.size() - 1};
    for (size_t offset = 0; offset < expected.size();)
    {
        offset += ReadU32(expected, offset + 4);
        limits.push_back(offset - 1);
        if (offset < expected.size()) { limits.push_back(offset); }
    }
    for (size_t limit : limits)
    {
        auto state = PreparedState();
        uint64_t revision = 12345;
        uint32_t commands = 54321;
        const auto rejectedMeshes = HdSilkSceneState::GetRejectedMeshCount();
        const auto rejectedMaterials = HdSilkSceneState::GetRejectedMaterialCount();
        bool refused = false;
        try { (void)state->BuildPage(&revision, &commands, limit); }
        catch (const std::bad_alloc&) { refused = true; }
        if (!refused)
        {
            throw std::runtime_error("PageByteLimitsPreserveCompleteUpdatesAndRetirements: " +
                std::to_string(limit) + " bytes admitted a larger page.");
        }
        Require(revision == 12345 && commands == 54321 &&
            rejectedMeshes == HdSilkSceneState::GetRejectedMeshCount() &&
            rejectedMaterials == HdSilkSceneState::GetRejectedMaterialCount(),
            "A serialized-byte refusal consumed outputs or became a malformed-record rejection.");
        const auto retry = state->BuildPage(&revision, &commands, expected.size());
        Require(retry == expected && revision == expectedRevision && commands == expectedCount,
            "Retry at the exact page byte limit lost an update or retirement.");
        const auto quiet = state->BuildPage(&revision, &commands, expected.size());
        Require(commands == 1 && ReadU32(quiet, 0) == OPENUSD_SILK_COMMAND_FRAME,
            "The subsequent quiet page replayed an already committed change.");
    }
    auto above = PreparedState();
    uint64_t revision = 0;
    uint32_t commands = 0;
    Require(above->BuildPage(&revision, &commands, expected.size() + 1) == expected,
        "One byte above the exact limit changed the successful page.");
    std::cout << "PageByteLimitsPreserveCompleteUpdatesAndRetirements: "
        << limits.size() << " refusal limits and exact/above-limit retries passed\n";
}

void PageWriterCapsGrowthAndRollsBackIncompleteCommands()
{
    HdSilkPageWriter page(24);
    {
        HdSilkCommandPayload payload(page, 0x12345678u);
        payload.AppendU64(0x0123456789abcdefull);
        payload.Complete();
    }
    Require(page.size() == 16 && page.capacity() <= 24, "The complete command exceeded its buffer ceiling.");
    try
    {
        HdSilkCommandPayload payload(page, 2);
        payload.reserve(SIZE_MAX);
        payload.AppendU32(99);
        throw std::logic_error("Expected the second payload to exceed the page limit.");
    }
    catch (const HdSilkPageLimitExceeded&) {}
    Require(page.size() == 16 && page.capacity() <= 24, "Refusal retained an incomplete command or oversized capacity.");
    {
        HdSilkCommandPayload abandoned(page, 3);
    }
    Require(page.size() == 16, "An abandoned command retained its header.");
    const auto bytes = page.Take();
    Require(ReadU32(bytes, 0) == 0x12345678u && ReadU32(bytes, 4) == 16u &&
        ReadU32(bytes, 8) == 0x89abcdefu && ReadU32(bytes, 12) == 0x01234567u,
        "Direct serialization changed command size, endian order or an earlier command.");

    bool invalid = false;
    try { HdSilkPageWriter zero(0); }
    catch (const std::invalid_argument&) { invalid = true; }
    Require(invalid, "A zero-sized page limit was accepted.");
    HdSilkPageWriter overflow(64);
    bool rejected = false;
    try
    {
        HdSilkCommandPayload payload(overflow, 1);
        payload.AppendBytes(nullptr, SIZE_MAX);
    }
    catch (const std::length_error&) { rejected = true; }
    Require(rejected && overflow.size() == 0, "A wire-size overflow was copied or retained.");
}

void ManyCommandsReuseOneAmortizedPageBuffer()
{
    HdSilkPageWriter page(12000);
    BeginAllocations();
    for (uint32_t command = 0; command < 1000; ++command)
    {
        HdSilkCommandPayload payload(page, command + 1);
        payload.reserve(4);
        payload.AppendU32(command);
        payload.Complete();
    }
    countAllocations = false;
    Require(page.size() == 12000 && page.capacity() <= 12000 && allocationIndex < 32,
        "Per-command payload allocation or exact-size growth defeated the shared page buffer.");
    const auto bytes = page.Take();
    for (uint32_t command = 0; command < 1000; ++command)
    {
        Require(ReadU32(bytes, command * 12) == command + 1 &&
            ReadU32(bytes, command * 12 + 4) == 12 &&
            ReadU32(bytes, command * 12 + 8) == command,
            "Amortized construction changed the command stream.");
    }
    std::cout << "ManyCommandsReuseOneAmortizedPageBuffer: " << allocationIndex
        << " buffer allocations for 1000 commands\n";
}
}

int main()
{
    try
    {
        EveryAllocationFailurePreservesTheCompleteRetry();
        EveryAllocationFailurePreservesTheCompleteRetry(true);
        DeferredAcknowledgementPreservesAllUpdatesAndRetirements();
        PageByteLimitsPreserveCompleteUpdatesAndRetirements();
        PageWriterCapsGrowthAndRollsBackIncompleteCommands();
        ManyCommandsReuseOneAmortizedPageBuffer();
        return 0;
    }
    catch (const std::exception& error)
    {
        countAllocations = false;
        std::cerr << "hdsilk_publication_allocation_probe: " << error.what() << '\n';
        return 1;
    }
}
