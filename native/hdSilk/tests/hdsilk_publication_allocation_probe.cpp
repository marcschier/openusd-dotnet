// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/sceneState.h"
#include "../src/pageWriter.h"
#include "../src/commandFile.h"

#include <cstdlib>
#include <cmath>
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
thread_local size_t largestAllocation = 0;
thread_local size_t failAt = std::numeric_limits<size_t>::max();

void* Allocate(size_t size)
{
    if (countAllocations && size > largestAllocation)
    {
        largestAllocation = size;
    }
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
    largestAllocation = 0;
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
    HdSilkMaterialTexture texture;
    texture.parameter = OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR;
    texture.componentCount = 3;
    texture.outputChannel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
    texture.asset = "fallback-allocation.png";
    texture.uvPrimvar = "uv";
    texture.hasUvFallback = true;
    texture.uvFallback[0] = roughness;
    texture.uvFallback[1] = 0.25f;
    record.textures = {texture};
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

void LargeTrianglePageRefusalPrecedesPayloadCopies()
{
    auto prepare = []()
    {
        auto state = std::make_unique<HdSilkSceneState>();
        HdSilkMeshRecord record = Mesh("/Large", 0);
        record.points.resize(300000, 0);
        state->ReplaceMeshInstances("/Large", {std::move(record)});
        return state;
    };
    auto control = prepare();
    uint64_t expectedRevision = 0;
    uint32_t expectedCount = 0;
    const auto expected = control->BuildPage(&expectedRevision, &expectedCount);
    for (size_t limit : {size_t{65536}, expected.size() - 1})
    {
        auto state = prepare();
        uint64_t revision = 123;
        uint32_t count = 456;
        bool refused = false;
        BeginAllocations();
        try
        {
            (void)state->BuildPage(&revision, &count, limit);
        }

        catch (const HdSilkPageLimitExceeded& error)
        {
            refused = true;
            Require(std::string(error.what()).find("Largest mesh command:") != std::string::npos,
                "Aggregate preflight omitted the indivisible mesh command requirement.");
        }
        countAllocations = false;
        Require(refused && revision == 123 && count == 456, "Page refusal published outputs.");
        Require(largestAllocation < 65536, "Refused page copied or allocated its large mesh payload.");
        const auto retry = state->BuildPage(&revision, &count, expected.size());
        Require(retry == expected && revision == expectedRevision && count == expectedCount,
            "Page preflight changed the exact-limit retry.");
    }
    std::cout << "LargeTrianglePageRefusalPrecedesPayloadCopies: bounded allocation and exact retries passed\n";
}

void NativeMeshArraysShareExactStorageAndDetachOnMutation()
{
    HdSilkSceneState state;
    auto attributes = []()
    {
        HdSilkMeshAttribute attribute;
        attribute.name = "normals";
        attribute.semantic = 1;
        attribute.componentCount = 3;
        attribute.interpolation = 1;
        attribute.data = {0.0f, 0.5f, 1.0f};
        return HdSilkMeshAttributes{attribute};
    };
    HdSilkMeshAttributes first = attributes();
    HdSilkMeshAttributes second = attributes();
    Require(first.cdata() != second.cdata(), "The independent attribute control already shared storage.");
    state.ShareMeshAttributes(&first);
    state.ShareMeshAttributes(&second);
    Require(first.cdata() == second.cdata(), "Identical native mesh attributes were duplicated.");
    second[0].data[1] = 0.25f;
    Require(first.cdata() != second.cdata() && first.cdata()[0].data[1] == 0.5f,
        "Editing shared native attributes changed another mesh.");
    HdSilkMeshAttributes signedZero = attributes();
    signedZero[0].data[0] = -0.0f;
    state.ShareMeshAttributes(&signedZero);
    Require(first.cdata() != signedZero.cdata() && std::signbit(signedZero.cdata()[0].data[0]),
        "Sharing collapsed signed-zero attribute bits.");
    for (int field = 0; field < 4; ++field)
    {
        HdSilkMeshAttributes changed = attributes();
        if (field == 0) { changed[0].name = "other"; }
        if (field == 1) { changed[0].semantic = 2; }
        if (field == 2) { changed[0].componentCount = 1; }
        if (field == 3) { changed[0].interpolation = 2; }
        state.ShareMeshAttributes(&changed);
        Require(changed.cdata() != first.cdata(), "Attribute sharing ignored its metadata.");
    }
    VtArray<uint32_t> indices{0, 1, 2};
    VtArray<uint32_t> duplicate{0, 1, 2};
    VtArray<uint32_t> different{0, 2, 1};
    state.ShareMeshIndices(&indices);
    state.ShareMeshIndices(&duplicate);
    state.ShareMeshIndices(&different);
    Require(indices.cdata() == duplicate.cdata() && indices.cdata() != different.cdata(),
        "Native index sharing did not preserve the exact ordered values.");
    duplicate[1] = 9;
    Require(indices.cdata()[1] == 1 && duplicate.cdata()[1] == 9,
        "Editing shared indices changed another mesh.");
    state.ClearMeshArraySharing();
    HdSilkMeshAttributes following = attributes();
    VtArray<uint32_t> followingIndices{0, 1, 2};
    state.ShareMeshAttributes(&following);
    state.ShareMeshIndices(&followingIndices);
    Require(following.cdata() != first.cdata() && followingIndices.cdata() != indices.cdata(),
        "The sharing index retained payloads across sync epochs.");
    Require(first.cdata()[0].data[1] == 0.5f && indices.cdata()[1] == 1,
        "Clearing the sharing index invalidated live mesh owners.");
    std::cout << "NativeMeshArraysShareExactStorageAndDetachOnMutation: passed\n";
}

void NativeSharingBoundsVerifyCollisionsAndKeepUncachedArrays()
{
    HdSilkArraySharing<VtArray<uint32_t>, 2, 1> sharing;
    auto equal = [](const VtArray<uint32_t>& a, const VtArray<uint32_t>& b) { return a == b; };
    VtArray<uint32_t> empty;
    Require(!sharing.Intern(empty, 0, equal) && sharing.EntryCount() == 0,
        "An empty native array consumed sharing capacity.");
    VtArray<uint32_t> first{1, 2, 3};
    VtArray<uint32_t> collision{3, 2, 1};
    VtArray<uint32_t> second{4, 5, 6};
    VtArray<uint32_t> over{7, 8, 9};
    const uint32_t* collisionStorage = collision.cdata();
    const uint32_t* overStorage = over.cdata();
    Require(!sharing.Intern(first, 10, equal) && sharing.EntryCount() == 1,
        "The first native array did not enter the bounded index.");
    Require(!sharing.Intern(collision, 10, equal) && collision.cdata() == collisionStorage &&
        sharing.EntryCount() == 1 && collision.cdata()[0] == 3,
        "A full collision bucket changed or retained an unequal array.");
    Require(!sharing.Intern(second, 11, equal) && sharing.EntryCount() == 2,
        "The exact sharing entry limit was refused.");
    Require(!sharing.Intern(over, 12, equal) && sharing.EntryCount() == 2 &&
        over.cdata() == overStorage && over.cdata()[0] == 7,
        "An over-capacity sharing index changed its uncached input.");
    VtArray<uint32_t> duplicate{1, 2, 3};
    Require(sharing.Intern(duplicate, 10, equal) && duplicate.cdata() == first.cdata(),
        "A full index refused an existing exact match.");
    sharing.Clear();
    Require(sharing.EntryCount() == 0 && first.cdata()[2] == 3 && duplicate.cdata()[0] == 1,
        "Clearing the index invalidated a live owner.");
    Require(!sharing.Intern(over, 12, equal) && sharing.EntryCount() == 1,
        "Cleared sharing capacity could not be reused.");
    std::cout << "NativeSharingBoundsVerifyCollisionsAndKeepUncachedArrays: passed\n";
}

void NativeSharingPreservesCompleteWireUpdates()
{
    auto makeMesh = [](const std::string& path, float height)
    {
        HdSilkMeshRecord mesh = Mesh(path, height);
        HdSilkMeshAttribute attribute;
        attribute.name = "weight";
        attribute.componentCount = 1;
        attribute.interpolation = OPENUSD_SILK_INTERPOLATION_CONSTANT;
        attribute.data = {0.5f};
        mesh.attributes = {attribute};
        return mesh;
    };
    HdSilkSceneState actual;
    HdSilkSceneState expected;
    HdSilkMeshRecord a = makeMesh("/A", 0);
    HdSilkMeshRecord b = makeMesh("/B", 1);
    expected.ReplaceMeshInstances("/A", {a});
    expected.ReplaceMeshInstances("/B", {b});
    actual.ShareMeshAttributes(&a.attributes);
    actual.ShareMeshAttributes(&b.attributes);
    actual.ShareMeshIndices(&a.indices);
    actual.ShareMeshIndices(&b.indices);
    Require(a.attributes.cdata() == b.attributes.cdata() && a.indices.cdata() == b.indices.cdata(),
        "The serialization control did not actually share its mesh arrays.");
    actual.ReplaceMeshInstances("/A", {a});
    actual.ReplaceMeshInstances("/B", {b});
    uint64_t actualRevision = 0, expectedRevision = 0;
    uint32_t actualCount = 0, expectedCount = 0;
    auto compare = [&]()
    {
        const auto actualPage = actual.BuildPage(&actualRevision, &actualCount);
        const auto expectedPage = expected.BuildPage(&expectedRevision, &expectedCount);
        Require(actualPage == expectedPage && actualRevision == expectedRevision &&
            actualCount == expectedCount && actualCount > 1,
            "Native sharing changed or dropped a complete serialized update.");
    };
    compare();
    a.attributes[0].data[0] = 0.75f;
    a.indices[1] = 2;
    a.indices[2] = 1;
    Require(b.attributes.cdata()[0].data[0] == 0.5f && b.indices.cdata()[1] == 1,
        "A native mesh edit changed its shared peer.");
    actual.ReplaceMeshInstances("/A", {a});
    expected.ReplaceMeshInstances("/A", {a});
    compare();
    std::cout << "NativeSharingPreservesCompleteWireUpdates: passed\n";
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
    rejected = false;
    try
    {
        HdSilkCommandPayload payload(overflow, 1);
        payload.AppendBytes(nullptr, 1);
    }
    catch (const std::invalid_argument&) { rejected = true; }
    Require(rejected && overflow.size() == 0, "A nonempty null byte payload was copied or retained.");
    {
        HdSilkCommandPayload empty(overflow, 1);
        empty.AppendBytes(nullptr, 0);
        empty.Complete();
    }
    Require(overflow.size() == 8, "A null zero-byte payload did not preserve its complete empty command.");
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

std::vector<uint8_t> ReadFileBytes(HdSilkCommandFile& file, size_t transferBytes)
{
    std::vector<uint8_t> bytes(file.size());
    for (size_t offset = 0; offset < bytes.size();)
    {
        const size_t count = std::min(transferBytes, bytes.size() - offset);
        file.Read(offset, bytes.data() + offset, count);
        offset += count;
    }
    return bytes;
}

class FaultSink final : public HdSilkCommandSink
{
public:
    explicit FaultSink(HdSilkCommandSink& target) : inner(target) {}
    void Append(const uint8_t* bytes, size_t count) override
    {
        if (appends++ == failAppend)
        {
            inner.Append(bytes, count / 2);
            throw std::runtime_error("Injected partial command write.");
        }
        inner.Append(bytes, count);
    }
    void Truncate(size_t size) override
    {
        inner.Truncate(size);
        if (failTruncate) { throw std::runtime_error("Injected truncate failure."); }
    }
    void Flush() override
    {
        if (failFlush == 1) { throw std::runtime_error("Injected pre-flush failure."); }
        inner.Flush();
        if (failFlush == 2) { throw std::runtime_error("Injected post-flush failure."); }
    }
    HdSilkCommandSink& inner;
    size_t failAppend = SIZE_MAX;
    size_t appends = 0;
    int failFlush = 0;
    bool failTruncate = false;
};

void EveryStorageFailurePreservesTheCompleteRetry()
{
    auto control = PreparedState();
    uint64_t revision = 0;
    uint32_t count = 0;
    const auto expected = control->BuildPage(&revision, &count);
    const uint32_t expectedCount = count;
    const auto quiet = control->BuildPage(nullptr, nullptr);
    for (size_t failure = 0; failure < static_cast<size_t>(expectedCount) + 2; ++failure)
    {
        auto state = PreparedState();
        HdSilkCommandFile file(expected.size(), 23);
        FaultSink sink(file);
        if (failure < expectedCount) { sink.failAppend = failure; }
        else { sink.failFlush = static_cast<int>(failure - expectedCount) + 1; }
        revision = 123;
        count = 456;
        const uint64_t rejectedMeshes = HdSilkSceneState::GetRejectedMeshCount();
        const uint64_t rejectedMaterials = HdSilkSceneState::GetRejectedMaterialCount();
        bool failed = false;
        try { state->BuildPageTo(sink, expected.size(), expected.size(), &revision, &count); }
        catch (const HdSilkCommandStorageError&) { failed = true; }
        Require(failed && revision == 123 && count == 456, "Storage failure published a partial update.");
        Require(rejectedMeshes == HdSilkSceneState::GetRejectedMeshCount() &&
            rejectedMaterials == HdSilkSceneState::GetRejectedMaterialCount(),
            "Storage failure was misreported as malformed scene content.");
        const auto retry = state->BuildPage(&revision, &count);
        Require(retry == expected && revision == 2 && count == expectedCount,
            "Storage failure lost pending commands or advanced the revision.");
        Require(state->BuildPage(nullptr, nullptr) == quiet, "Storage recovery changed the acknowledged scene.");
    }
    std::cout << "EveryStorageFailurePreservesTheCompleteRetry: " << expectedCount
        << " partial writes and both flush failure positions passed\n";
}

void FilePublicationAllocationFailuresPreserveTheCompleteRetry()
{
    auto control = PreparedState();
    const auto expected = control->BuildPage(nullptr, nullptr);
    auto measured = PreparedState();
    HdSilkCommandFile measuredFile(expected.size(), 31);
    BeginAllocations();
    measured->BuildPageTo(measuredFile, expected.size(), expected.size(), nullptr, nullptr, false);
    countAllocations = false;
    const size_t allocations = allocationIndex;
    Require(allocations > 10 && allocations < 4096, "The file allocation sweep did not cover a bounded real update.");
    for (size_t failure = 0; failure < allocations; ++failure)
    {
        auto state = PreparedState();
        HdSilkCommandFile file(expected.size(), 31);
        uint64_t revision = 123;
        uint32_t count = 456;
        bool failed = false;
        BeginAllocations(failure);
        try { state->BuildPageTo(file, expected.size(), expected.size(), &revision, &count); }
        catch (const std::bad_alloc&) { failed = true; }
        countAllocations = false;
        Require(failed && allocationFailed && revision == 123 && count == 456,
            "A file publication allocation failure was swallowed or published outputs.");
        Require(state->BuildPage(&revision, &count) == expected && revision == 2 && count == 10,
            "A file publication allocation failure changed the exact retry.");
    }
    std::cout << "FilePublicationAllocationFailuresPreserveTheCompleteRetry: "
        << allocations << " allocation points passed\n";
}

void FilePublicationMatchesContiguousAndRequiresAcknowledgement()
{
    auto control = PreparedState();
    uint64_t revision = 0;
    uint32_t count = 0;
    const auto expected = control->BuildPage(&revision, &count);
    const uint32_t expectedCount = count;
    const auto quiet = control->BuildPage(nullptr, nullptr);
    size_t largest = 0;
    for (size_t offset = 0; offset < expected.size();)
    {
        const size_t size = ReadU32(expected, offset + 4);
        largest = std::max(largest, size);
        offset += size;
    }
    for (bool commandLimit : {false, true})
    {
        auto state = PreparedState();
        HdSilkCommandFile refused(expected.size(), 23);
        revision = 123;
        count = 456;
        bool failed = false;
        try
        {
            state->BuildPageTo(refused, expected.size() - (commandLimit ? 0 : 1),
                largest - (commandLimit ? 1 : 0), &revision, &count);
        }
        catch (const HdSilkPageLimitExceeded&) { failed = true; }
        Require(failed && revision == 123 && count == 456, "A one-byte-short storage ceiling published outputs.");
        for (size_t attempt = 0; attempt < 2; ++attempt)
        {
            HdSilkCommandFile file(expected.size(), 23);
            Require(state->BuildPageTo(file, expected.size(), largest, &revision, &count) == expected.size() &&
                file.size() == expected.size(), "File publication reported the wrong extent.");
            Require(ReadFileBytes(file, 23) == expected && count == expectedCount && revision == attempt + 2,
                "Exact-limit file publication changed command bytes, count or revision.");
            bool blocked = false;
            try { state->RemoveMesh("/World/KeepMesh"); }
            catch (const std::logic_error&) { blocked = true; }
            Require(blocked && !state->CompletePage(revision + 1, true), "Deferred file publication allowed mutation.");
            Require(state->CompletePage(revision, attempt == 1), "Deferred file publication could not resolve.");
        }
        Require(state->BuildPage(nullptr, nullptr) == quiet, "File acknowledgement lost updates or retirements.");
    }
    std::cout << "FilePublicationMatchesContiguousAndRequiresAcknowledgement: exact bytes, limits and replay passed\n";
}

void FilePreflightRefusesBeforeLargePayloadCopies()
{
    auto prepare = []()
    {
        auto state = std::make_unique<HdSilkSceneState>();
        auto record = Mesh("/Large", 0);
        record.points.resize(300000, 0);
        state->ReplaceMeshInstances("/Large", {std::move(record)});
        return state;
    };
    auto control = prepare();
    const auto expected = control->BuildPage(nullptr, nullptr);
    size_t largest = 0;
    for (size_t offset = 0; offset < expected.size();)
    {
        const size_t bytes = ReadU32(expected, offset + 4);
        largest = std::max(largest, bytes);
        offset += bytes;
    }
    for (bool commandLimit : {false, true})
    {
        auto state = prepare();
        HdSilkCommandFile file(expected.size(), 4096);
        uint64_t revision = 123;
        uint32_t count = 456;
        bool failed = false;
        BeginAllocations();
        try
        {
            state->BuildPageTo(file, expected.size() - (commandLimit ? 0 : 1),
                largest - (commandLimit ? 1 : 0), &revision, &count);
        }
        catch (const HdSilkPageLimitExceeded&) { failed = true; }
        countAllocations = false;
        Require(failed && revision == 123 && count == 456 && largestAllocation < 65536,
            "File preflight copied a large refused mesh or published outputs.");
        Require(state->BuildPage(&revision, &count) == expected && revision == 1 && count == 2,
            "File preflight changed the retry.");
    }
    std::cout << "FilePreflightRefusesBeforeLargePayloadCopies: total and command limits passed\n";
}

std::unique_ptr<HdSilkSceneState> InvalidInstanceState()
{
    auto state = std::make_unique<HdSilkSceneState>();
    auto first = Mesh("/Bad", 0);
    auto second = first;
    first.instanceIndex = 0;
    second.instanceIndex = 1;
    second.indices = {0, 1, 4096};
    state->ReplaceMeshInstances("/Bad", {std::move(first), std::move(second)});
    state->ReplaceMeshInstances("/Healthy", {Mesh("/Healthy", 0)});
    return state;
}

void FileRollbackRemovesWholePathsAndTruncateFailureAborts()
{
    auto control = InvalidInstanceState();
    uint32_t expectedCount = 0;
    const auto expected = control->BuildPage(nullptr, &expectedCount);
    auto state = InvalidInstanceState();
    HdSilkCommandFile file(65536, 17);
    uint32_t count = 0;
    const uint64_t rejected = HdSilkSceneState::GetRejectedMeshCount();
    state->BuildPageTo(file, 65536, 32768, nullptr, &count, false);
    Require(ReadFileBytes(file, 17) == expected && count == expectedCount &&
        HdSilkSceneState::GetRejectedMeshCount() == rejected + 2,
        "Disk rollback retained a partial instance group or removed its healthy sibling.");

    state = InvalidInstanceState();
    HdSilkCommandFile broken(65536, 17);
    FaultSink sink(broken);
    sink.failTruncate = true;
    uint64_t revision = 123;
    count = 456;
    bool failed = false;
    try { state->BuildPageTo(sink, 65536, 32768, &revision, &count); }
    catch (const HdSilkCommandStorageError&) { failed = true; }
    Require(failed && revision == 123 && count == 456, "Truncate failure published an incomplete scene.");
    Require(state->BuildPage(&revision, &count) == expected && revision == 1 && count == expectedCount,
        "Truncate failure changed the complete retry.");
    std::cout << "FileRollbackRemovesWholePathsAndTruncateFailureAborts: rollback and retry passed\n";
}

void CommandFileBoundsBufferRollbackAndFinalization()
{
    for (const auto& limits : {std::pair<size_t, size_t>{0, 16}, {16, 0}, {SIZE_MAX, 16},
        {16, static_cast<size_t>(std::numeric_limits<int32_t>::max()) + 1}})
    {
        bool refused = false;
        try { HdSilkCommandFile invalid(limits.first, limits.second); }
        catch (const std::invalid_argument&) { refused = true; }
        Require(refused, "Invalid file admission was accepted.");
    }
    for (size_t buffer : {size_t{7}, size_t{16}, size_t{31}})
    {
        HdSilkCommandFile file(64, buffer);
        std::vector<uint8_t> data(64);
        for (size_t index = 0; index < data.size(); ++index) { data[index] = static_cast<uint8_t>(index); }
        file.Append(data.data(), 5);
        file.Truncate(3);
        file.Append(data.data() + 3, 61);
        bool refused = false;
        try { file.Append(data.data(), 1); }
        catch (const HdSilkPageLimitExceeded&) { refused = true; }
        Require(refused && file.size() == 64, "File quota was not enforced at its exact boundary.");
        file.Truncate(18);
        file.Append(data.data() + 18, 10);
        Require(file.size() == 28, "Rollback changed the logical file extent.");
        refused = false;
        try { file.Read(0, data.data(), 1); }
        catch (const HdSilkCommandStorageError&) { refused = true; }
        Require(refused, "An unfinalized command store was readable.");
        file.Flush();
        data.resize(28);
        Require(ReadFileBytes(file, buffer) == data, "Buffered or on-disk rollback corrupted bytes.");
        uint8_t last = 0;
        file.Read(27, &last, 1);
        Require(last == 27, "Random-access command reads returned the wrong byte.");
        file.Read(28, nullptr, 0);
        refused = false;
        try { file.Read(28, &last, 1); }
        catch (const std::invalid_argument&) { refused = true; }
        Require(refused, "An out-of-extent read was accepted.");
        refused = false;
        try { file.Read(0, data.data(), buffer + 1); }
        catch (const std::invalid_argument&) { refused = true; }
        Require(refused, "An oversized transfer was accepted.");
        refused = false;
        try { file.Append(data.data(), 1); }
        catch (const HdSilkCommandStorageError&) { refused = true; }
        Require(refused, "Finalized command data was mutable.");
    }
    std::cout << "CommandFileBoundsBufferRollbackAndFinalization: disk boundaries and immutable reads passed\n";
}

void StreamingWriterAbandonsIncompleteCommandsAndPoisonsFailedStorage()
{
    HdSilkCommandFile file(64, 16);
    HdSilkPageWriter writer(64, file, 16);
    {
        HdSilkCommandPayload command(writer, 1);
        command.AppendU64(123);
        command.Complete();
        bool refused = false;
        try { command.Complete(); }
        catch (const std::logic_error&) { refused = true; }
        Require(refused, "A completed streamed command was completed again.");
    }
    {
        HdSilkCommandPayload abandoned(writer, 2);
        abandoned.AppendU32(456);
    }
    bool refused = false;
    try
    {
        HdSilkCommandPayload oversized(writer, 2);
        oversized.AppendU64(456);
        oversized.AppendU32(789);
    }
    catch (const HdSilkPageLimitExceeded&) { refused = true; }
    Require(refused && writer.size() == 16 && file.size() == 16,
        "An abandoned or oversized command reached storage.");
    {
        HdSilkCommandPayload empty(writer, 3);
        empty.Complete();
    }
    writer.Finish();
    const auto bytes = ReadFileBytes(file, 16);
    Require(bytes.size() == 24 && ReadU32(bytes, 0) == 1 && ReadU32(bytes, 4) == 16 &&
        ReadU32(bytes, 8) == 123 && ReadU32(bytes, 12) == 0 &&
        ReadU32(bytes, 16) == 3 && ReadU32(bytes, 20) == 8,
        "Incomplete command recovery changed the preceding or following command.");

    HdSilkCommandFile broken(64, 16);
    FaultSink sink(broken);
    sink.failAppend = 0;
    HdSilkPageWriter poisoned(64, sink, 16);
    refused = false;
    try
    {
        HdSilkCommandPayload command(poisoned, 1);
        command.AppendU64(123);
        command.Complete();
    }
    catch (const HdSilkCommandStorageError&) { refused = true; }
    Require(refused && broken.size() == 8, "Partial storage failure was not observed.");
    for (int action = 0; action < 3; ++action)
    {
        refused = false;
        try
        {
            if (action == 0) { poisoned.Finish(); }
            else if (action == 1) { poisoned.resize(0); }
            else { HdSilkCommandPayload next(poisoned, 2); }
        }
        catch (const HdSilkCommandStorageError&) { refused = true; }
        Require(refused && sink.appends == 1 && broken.size() == 8,
            "A poisoned command store was reused or finalized.");
    }
    std::cout << "StreamingWriterAbandonsIncompleteCommandsAndPoisonsFailedStorage: recovery and sticky failure passed\n";
}

void StreamingWriterExceedsFourGiBWithOneBoundedCommandBuffer()
{
    class CountingSink final : public HdSilkCommandSink
    {
    public:
        void Append(const uint8_t* data, size_t size) override
        {
            uint32_t command = 0;
            uint32_t payload = 0;
            for (size_t index = 0; index < 4; ++index)
            {
                command |= uint32_t{data[4 + index]} << (index * 8);
                payload |= uint32_t{data[8 + index]} << (index * 8);
            }
            Require(size == 65536 && command == size && data[0] == 7 && payload == count &&
                data[size - 1] == 0xa5, "Streaming used a global offset inside the bounded command buffer.");
            bytes += size;
            ++count;
        }
        void Truncate(size_t) override { throw std::logic_error("Unexpected counting-store rollback."); }
        void Flush() override { flushed = true; }
        uint64_t bytes = 0;
        uint32_t count = 0;
        bool flushed = false;
    } sink;
    constexpr size_t commands = 65537;
    constexpr size_t commandSize = 65536;
    constexpr size_t extent = commands * commandSize;
    size_t oneBufferAllocation = 0;
    {
        BeginAllocations();
        const std::vector<uint8_t> allocatorControl(commandSize);
        countAllocations = false;
        oneBufferAllocation = largestAllocation;
        Require(allocatorControl.capacity() == commandSize && oneBufferAllocation >= commandSize,
            "The allocator control did not measure one command buffer including its platform overhead.");
    }
    HdSilkPageWriter writer(extent, sink, commandSize);
    const std::vector<uint8_t> tail(commandSize - 12, 0xa5);
    BeginAllocations();
    for (uint32_t index = 0; index < commands; ++index)
    {
        HdSilkCommandPayload payload(writer, 7);
        payload.reserve(SIZE_MAX);
        payload.AppendU32(index);
        payload.AppendBytes(tail.data(), tail.size());
        payload[0] = static_cast<uint8_t>(index);
        payload.Complete();
    }
    writer.Finish();
    countAllocations = false;
    Require(extent > std::numeric_limits<uint32_t>::max() && writer.size() == extent &&
        sink.bytes == extent && sink.count == commands && sink.flushed,
        "Streaming command offsets wrapped at a managed-array or 32-bit limit.");
    std::cout << "Streaming buffer accounting: largest allocation=" << largestAllocation
        << ", capacity=" << writer.capacity() << ", allocations=" << allocationIndex << '\n';
    Require(largestAllocation == oneBufferAllocation && writer.capacity() == commandSize && allocationIndex == 2,
        "Streaming retained the whole update or allocated one buffer per command.");
    std::cout << "StreamingWriterExceedsFourGiBWithOneBoundedCommandBuffer: " << extent
        << " serialized bytes, " << writer.capacity() << " buffer bytes, " << allocationIndex << " allocations\n";
}
}

int main()
{
    try
    {
        NativeMeshArraysShareExactStorageAndDetachOnMutation();
        NativeSharingBoundsVerifyCollisionsAndKeepUncachedArrays();
        NativeSharingPreservesCompleteWireUpdates();
        EveryAllocationFailurePreservesTheCompleteRetry();
        EveryAllocationFailurePreservesTheCompleteRetry(true);
        DeferredAcknowledgementPreservesAllUpdatesAndRetirements();
        PageByteLimitsPreserveCompleteUpdatesAndRetirements();
        LargeTrianglePageRefusalPrecedesPayloadCopies();
        PageWriterCapsGrowthAndRollsBackIncompleteCommands();
        ManyCommandsReuseOneAmortizedPageBuffer();
        EveryStorageFailurePreservesTheCompleteRetry();
        FilePublicationAllocationFailuresPreserveTheCompleteRetry();
        FilePublicationMatchesContiguousAndRequiresAcknowledgement();
        FilePreflightRefusesBeforeLargePayloadCopies();
        FileRollbackRemovesWholePathsAndTruncateFailureAborts();
        CommandFileBoundsBufferRollbackAndFinalization();
        StreamingWriterAbandonsIncompleteCommandsAndPoisonsFailedStorage();
        StreamingWriterExceedsFourGiBWithOneBoundedCommandBuffer();
        return 0;
    }
    catch (const std::exception& error)
    {
        countAllocations = false;
        std::cerr << "hdsilk_publication_allocation_probe: " << error.what() << '\n';
        return 1;
    }
}
