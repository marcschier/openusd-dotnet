// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_test_hooks.h"
#include "subdivision.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <string>
#include <system_error>
#include <utility>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr int32_t Mapped = static_cast<int32_t>(HdSilkPtexMappingStatus::Mapped);
constexpr int32_t Invalid =
    static_cast<int32_t>(HdSilkPtexMappingStatus::InvalidFaceOffsets);

struct MaterialWire
{
    std::string path;
    std::string asset;
    std::string uv;
    uint32_t parameter = 0;
    uint32_t channel = 0;
    uint32_t sourceKind = 0;
};

struct PageWire
{
    std::vector<std::string> materialPaths;
    std::vector<MaterialWire> materials;
    std::vector<std::string> meshes;
};

bool ReadPage(const openusd_silk_page_view& view, PageWire* result)
{
    if (view.abi_version != OPENUSD_SILK_PAGE_ABI_VERSION ||
        (view.data_size != 0 && view.data == nullptr))
    {
        return false;
    }
    const auto readU32 = [&](size_t offset, size_t end, uint32_t* value)
    {
        if (offset > end || end - offset < sizeof(uint32_t))
        {
            return false;
        }
        std::memcpy(value, view.data + offset, sizeof(uint32_t));
        return true;
    };
    size_t offset = 0;
    while (offset < view.data_size)
    {
        uint32_t type = 0;
        uint32_t size = 0;
        if (!readU32(offset, view.data_size, &type) ||
            !readU32(offset + 4, view.data_size, &size) ||
            size < 8 || size > view.data_size - offset)
        {
            return false;
        }
        const size_t end = offset + size;
        if (type == OPENUSD_SILK_COMMAND_MESH_UPSERT)
        {
            uint32_t pathSize = 0;
            constexpr size_t fixedSize = 276;
            if (!readU32(offset + 48, end, &pathSize) ||
                end - offset < fixedSize || pathSize > end - offset - fixedSize)
            {
                return false;
            }
            result->meshes.emplace_back(
                reinterpret_cast<const char*>(view.data + offset + fixedSize), pathSize);
        }
        if (type == OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            uint32_t pathSize = 0;
            uint32_t scalarCount = 0;
            uint32_t textureCount = 0;
            constexpr size_t fixedSize = 32;
            if (!readU32(offset + 16, end, &pathSize) ||
                !readU32(offset + 24, end, &scalarCount) ||
                !readU32(offset + 28, end, &textureCount) ||
                end - offset < fixedSize || pathSize > end - offset - fixedSize)
            {
                return false;
            }
            const std::string path(
                reinterpret_cast<const char*>(view.data + offset + fixedSize), pathSize);
            result->materialPaths.push_back(path);
            size_t cursor = offset + fixedSize + pathSize;
            for (uint32_t scalar = 0; scalar < scalarCount; ++scalar)
            {
                uint32_t components = 0;
                if (!readU32(cursor + 4, end, &components) ||
                    components == 0 || components > 4 ||
                    cursor > end || end - cursor < 8 + components * sizeof(float))
                {
                    return false;
                }
                cursor += 8 + components * sizeof(float);
            }
            for (uint32_t texture = 0; texture < textureCount; ++texture)
            {
                MaterialWire entry;
                entry.path = path;
                uint32_t assetSize = 0;
                uint32_t uvSize = 0;
                constexpr size_t textureSize = 92;
                if (!readU32(cursor, end, &entry.parameter) ||
                    !readU32(cursor + 16, end, &assetSize) ||
                    !readU32(cursor + 20, end, &uvSize) ||
                    !readU32(cursor + 76, end, &entry.channel) ||
                    !readU32(cursor + 88, end, &entry.sourceKind) ||
                    cursor > end || end - cursor < textureSize ||
                    assetSize > end - cursor - textureSize ||
                    uvSize > end - cursor - textureSize - assetSize)
                {
                    return false;
                }
                entry.asset.assign(
                    reinterpret_cast<const char*>(view.data + cursor + textureSize), assetSize);
                entry.uv.assign(
                    reinterpret_cast<const char*>(view.data + cursor + textureSize + assetSize), uvSize);
                result->materials.push_back(std::move(entry));
                cursor += textureSize + assetSize + uvSize;
            }
        }
        offset = end;
    }
    return offset == view.data_size;
}

bool CheckMaterialWire(
    const PageWire& page, const std::filesystem::path& fixtureDirectory,
    const char* materialPath, const char* file, uint32_t sourceKind)
{
    for (const MaterialWire& entry : page.materials)
    {
        if (entry.path != materialPath ||
            entry.parameter != OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR)
        {
            continue;
        }
        std::error_code error;
        const std::filesystem::path asset(entry.asset);
        const bool sameFile = std::filesystem::equivalent(
            asset, fixtureDirectory / "native-profile" / "ptex" / file, error);
        return entry.sourceKind == sourceKind &&
            entry.channel == OPENUSD_SILK_TEXTURE_CHANNEL_RGB &&
            (sourceKind != OPENUSD_SILK_TEXTURE_SOURCE_PTEX || entry.uv.empty()) &&
            asset.is_absolute() && !error && sameFile;
    }
    std::cerr << "Material source entry missing from page: " << materialPath << "\n";
    return false;
}

bool CheckOmittedMaterialTexture(
    const PageWire& page, const char* materialPath, uint32_t parameter)
{
    if (std::find(page.materialPaths.begin(), page.materialPaths.end(), materialPath) ==
        page.materialPaths.end())
    {
        std::cerr << "Material missing from page: " << materialPath << "\n";
        return false;
    }
    if (std::any_of(page.materials.begin(), page.materials.end(),
        [&](const MaterialWire& entry)
        {
            return entry.path == materialPath && entry.parameter == parameter;
        }))
    {
        std::cerr << "Unsupported texture published for material: " << materialPath << "\n";
        return false;
    }
    return true;
}

void Print(const openusd_hdsilk_test_ptex_observation& observation)
{
    std::cerr << observation.path
              << ": registered=" << observation.registered
              << " sameDelegate=" << observation.same_scene_delegate
              << " terminalMesh=" << observation.terminal_mesh
              << " instanced=" << observation.instanced
              << " primvar=" << observation.has_primvar
              << " truncated=" << observation.path_truncated
              << " element=" << observation.has_element_size << ":" << observation.element_size
              << " matches=" << observation.delegate_matches
              << " count=" << observation.value_count
              << " values=" << observation.values[0] << "," << observation.values[1]
              << "," << observation.values[2] << "," << observation.values[3]
              << " indices=" << observation.index_count << ":"
              << observation.indices[0] << "," << observation.indices[1]
              << " usdDefaultOne=" << observation.usd_default_one_proven
              << " strict=" << observation.strict_status
              << " default=" << observation.default_one_status
              << " mapped=" << observation.mapped_faces[0] << ","
              << observation.mapped_faces[1]
              << " material=" << observation.terminal_material
              << " ptexConnection=" << observation.ptex_material_connection
              << " materialMatches=" << observation.ptex_material_delegate_matches
              << " asset=" << observation.ptex_material_asset
              << " resolved=" << observation.ptex_material_resolved_asset << "\n";
}

bool CheckPtexMaterial(
    const openusd_hdsilk_test_ptex_observation& observation,
    const std::filesystem::path& fixtureDirectory,
    bool updated)
{
    const std::string file = updated ? "quad-colors.ptx" : "tetrahedron.ptx";
    const std::filesystem::path resolved(observation.ptex_material_resolved_asset);
    std::error_code error;
    const bool sameFile = std::filesystem::equivalent(
        resolved, fixtureDirectory / "native-profile" / "ptex" / file, error);
    return observation.terminal_material && observation.ptex_material_connection &&
        observation.ptex_material_delegate_matches && !observation.ptex_material_path_truncated &&
        observation.ptex_material_asset == std::string("native-profile/ptex/") + file &&
        resolved.is_absolute() && !error && sameFile;
}

bool CheckFrame(
    const std::vector<openusd_hdsilk_test_ptex_observation>& observations,
    const std::filesystem::path& fixtureDirectory,
    bool updated)
{
    bool ordinary = false;
    bool indexedDefault = false;
    bool nativeInstance = false;
    bool pointInstance = false;
    bool grouped = updated;
    bool groupedIndexed = updated;
    bool missing = updated;
    bool materialOnly = updated;
    bool valid = true;
    for (const auto& observation : observations)
    {
        Print(observation);
        if (observation.path_truncated || !observation.registered || !observation.terminal_mesh ||
            !observation.same_scene_delegate)
        {
            valid = false;
        }
        if (std::strcmp(observation.path, "/World/Grouped") == 0)
        {
            grouped = observation.has_primvar && observation.has_element_size &&
                observation.element_size == 2 && observation.delegate_matches &&
                observation.value_count == 4 && observation.index_count == 0 &&
                observation.values[0] == 2 && observation.values[1] == 70 &&
                observation.values[2] == 3 && observation.values[3] == 80 &&
                !observation.usd_default_one_proven &&
                observation.strict_status == Invalid &&
                observation.default_one_status == Invalid &&
                CheckPtexMaterial(observation, fixtureDirectory, false);
            continue;
        }
        if (std::strcmp(observation.path, "/World/GroupedIndexed") == 0)
        {
            groupedIndexed = observation.has_primvar && observation.has_element_size &&
                observation.element_size == 2 && observation.delegate_matches &&
                observation.value_count == 4 && observation.index_count == 2 &&
                observation.values[0] == 2 && observation.values[1] == 7 &&
                observation.values[2] == 3 && observation.values[3] == 8 &&
                observation.indices[0] == 0 && observation.indices[1] == 1 &&
                !observation.usd_default_one_proven &&
                observation.strict_status == Invalid && observation.default_one_status == Invalid;
            continue;
        }
        if (std::strcmp(observation.path, "/World/NoOffset") == 0)
        {
            missing = !observation.has_primvar &&
                !observation.usd_default_one_proven &&
                !observation.terminal_material && !observation.ptex_material_connection &&
                observation.strict_status == Invalid && observation.default_one_status == Invalid;
            continue;
        }
        if (std::strcmp(observation.path, "/World/IndexedDefault") == 0)
        {
            indexedDefault = !observation.instanced && observation.has_primvar &&
                observation.terminal_material && !observation.ptex_material_connection &&
                !observation.has_element_size && observation.delegate_matches &&
                observation.value_count == 2 && observation.index_count == 2 &&
                observation.values[0] == (updated ? 9 : 7) &&
                observation.values[1] == (updated ? 4 : 2) &&
                observation.indices[0] == (updated ? 0 : 1) &&
                observation.indices[1] == (updated ? 1 : 0) &&
                observation.usd_default_one_proven &&
                observation.strict_status == Invalid &&
                observation.default_one_status == Invalid &&
                observation.mapped_faces[0] == 0 && observation.mapped_faces[1] == 0;
            continue;
        }
        if (std::strcmp(observation.path, "/World/MaterialOnly") == 0)
        {
            materialOnly = !observation.instanced && observation.has_primvar &&
                observation.delegate_matches && observation.value_count == 2 &&
                observation.values[0] == 2 && observation.values[1] == 3 &&
                CheckPtexMaterial(observation, fixtureDirectory, updated);
            continue;
        }
        const bool common = observation.has_primvar && !observation.has_element_size &&
            observation.delegate_matches &&
            observation.value_count == 2 &&
            observation.index_count == 0 && observation.strict_status == Invalid;
        if (std::strcmp(observation.path, "/World/Ordinary") == 0)
        {
            const int32_t first = updated ? 4 : 2;
            const int32_t second = updated ? 1 : 3;
            ordinary = common && observation.usd_default_one_proven &&
                observation.default_one_status == Mapped && !observation.instanced &&
                observation.values[0] == first && observation.values[1] == second &&
                observation.mapped_faces[0] == static_cast<uint32_t>(first) &&
                observation.mapped_faces[1] == static_cast<uint32_t>(second + 1) &&
                CheckPtexMaterial(observation, fixtureDirectory, updated);
        }
        else if (observation.values[0] == (updated ? 6 : 5) &&
                 observation.values[1] == (updated ? 2 : 3))
        {
            nativeInstance = common && !observation.usd_default_one_proven &&
                observation.default_one_status == Invalid && observation.instanced &&
                std::strstr(observation.path, "/UsdNiPropagatedPrototypes/") == observation.path &&
                observation.mapped_faces[0] == 0 && observation.mapped_faces[1] == 0;
        }
        else if (observation.values[0] == (updated ? 9 : 8) &&
                 observation.values[1] == (updated ? 3 : 4))
        {
            pointInstance = common && !observation.usd_default_one_proven &&
                observation.default_one_status == Invalid && observation.instanced &&
                std::strstr(observation.path, "/World/PointInstances/Prototype/") == observation.path &&
                observation.mapped_faces[0] == 0 && observation.mapped_faces[1] == 0;
        }
        else
        {
            valid = false;
        }
    }
    if (!ordinary || !indexedDefault || !nativeInstance || !pointInstance ||
        !grouped || !groupedIndexed || !missing || !materialOnly)
    {
        std::cerr << "Missing or invalid Ptex observations: ordinary=" << ordinary
                  << " indexedDefault=" << indexedDefault
                  << " nativeInstance=" << nativeInstance << " pointInstance=" << pointInstance
                  << " grouped=" << grouped << " groupedIndexed=" << groupedIndexed
                  << " missing=" << missing << " materialOnly=" << materialOnly << "\n";
        valid = false;
    }
    return valid;
}

bool CheckRestoredFrame(
    const std::vector<openusd_hdsilk_test_ptex_observation>& observations,
    const std::filesystem::path& fixtureDirectory)
{
    bool ordinary = false;
    bool indexedDefault = false;
    for (const auto& observation : observations)
    {
        if (std::strcmp(observation.path, "/World/Ordinary") == 0)
        {
            Print(observation);
            ordinary = observation.usd_default_one_proven &&
                observation.delegate_matches &&
                observation.value_count == 2 &&
                observation.values[0] == 2 && observation.values[1] == 3 &&
                observation.default_one_status == Mapped &&
                observation.mapped_faces[0] == 2 && observation.mapped_faces[1] == 4 &&
                CheckPtexMaterial(observation, fixtureDirectory, false);
        }
        else if (std::strcmp(observation.path, "/World/IndexedDefault") == 0)
        {
            Print(observation);
            indexedDefault = observation.usd_default_one_proven &&
                observation.delegate_matches &&
                observation.value_count == 2 &&
                observation.values[0] == 7 && observation.values[1] == 2 &&
                observation.index_count == 2 &&
                observation.indices[0] == 1 && observation.indices[1] == 0 &&
                observation.default_one_status == Invalid;
        }
    }
    if (!ordinary || !indexedDefault)
    {
        std::cerr << "Missing or invalid restored Ptex observations: ordinary="
                  << ordinary << " indexedDefault=" << indexedDefault << "\n";
    }
    return ordinary && indexedDefault;
}

bool Sync(
    openusd_silk_session* session,
    double time,
    std::vector<openusd_hdsilk_test_ptex_observation>* observations,
    PageWire* wire,
    openusd_error_buffer* error)
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(view);
    openusd_silk_page* page = nullptr;
    openusd_hdsilk_test_begin_ptex_observation();
    const openusd_status status = openusd_silk_session_sync(
        session, 64, 64, time, &camera, &page, &view, error);
    std::array<openusd_hdsilk_test_ptex_observation, 64> buffer{};
    const size_t count = openusd_hdsilk_test_end_ptex_observation(buffer.data(), buffer.size());
    const bool pageValid = status == OPENUSD_STATUS_OK && ReadPage(view, wire);
    if (page != nullptr)
    {
        openusd_silk_page_release(page);
    }
    if (!pageValid || count > buffer.size())
    {
        std::cerr << "Ptex sync failed: status=" << status << ", observations=" << count
                  << ", error=" << error->data << "\n";
        return false;
    }
    observations->assign(buffer.begin(), buffer.begin() + count);
    return true;
}
}

int main(int argc, char** argv)
{
    if (argc != 4 ||
        (std::strcmp(argv[1], "usd") != 0 && std::strcmp(argv[1], "filtered") != 0 &&
         std::strcmp(argv[1], "filtered-unindexed") != 0 &&
         std::strcmp(argv[1], "legacy") != 0))
    {
        std::cerr << "Expected a mode, the plugin directory and Ptex sync stage.\n";
        return 1;
    }
    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    openusd_stage* stage = nullptr;
    openusd_silk_session* session = nullptr;
    if (openusd_stage_open(argv[3], &stage, &error) != OPENUSD_STATUS_OK ||
        openusd_silk_session_create_from_stage(argv[2], stage, &session, &error) != OPENUSD_STATUS_OK)
    {
        std::cerr << errorText.data() << "\n";
        openusd_stage_release(stage);
        return 1;
    }
    std::vector<openusd_hdsilk_test_ptex_observation> observations;
    bool valid;
    if (std::strcmp(argv[1], "usd") == 0)
    {
        const std::filesystem::path fixtureDirectory = std::filesystem::path(argv[3]).parent_path();
        PageWire initial;
        valid = Sync(session, 0.0, &observations, &initial, &error) &&
            CheckFrame(observations, fixtureDirectory, false) &&
            CheckMaterialWire(initial, fixtureDirectory, "/World/PtexMaterial",
                "tetrahedron.ptx", OPENUSD_SILK_TEXTURE_SOURCE_PTEX) &&
            CheckMaterialWire(initial, fixtureDirectory, "/World/UvMaterial",
                "tetrahedron.ptx", OPENUSD_SILK_TEXTURE_SOURCE_STANDARD) &&
            CheckOmittedMaterialTexture(initial, "/World/UnsupportedPtexMaterial",
                OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR);
        PageWire updated;
        valid = Sync(session, 1.0, &observations, &updated, &error) &&
            CheckFrame(observations, fixtureDirectory, true) &&
            CheckMaterialWire(updated, fixtureDirectory, "/World/PtexMaterial",
                "quad-colors.ptx", OPENUSD_SILK_TEXTURE_SOURCE_PTEX) &&
            std::find(updated.meshes.begin(), updated.meshes.end(), "/World/MaterialOnly") ==
                updated.meshes.end() && valid;
        PageWire restored;
        valid = Sync(session, 0.0, &observations, &restored, &error) &&
            CheckRestoredFrame(observations, fixtureDirectory) &&
            CheckMaterialWire(restored, fixtureDirectory, "/World/PtexMaterial",
                "tetrahedron.ptx", OPENUSD_SILK_TEXTURE_SOURCE_PTEX) && valid;
    }
    else
    {
        openusd_hdsilk_test_begin_ptex_observation();
        const bool legacy = std::strcmp(argv[1], "legacy") == 0;
        const bool filteredUnindexed = std::strcmp(argv[1], "filtered-unindexed") == 0;
        const int32_t mode = legacy ? 1 : filteredUnindexed ? 2 : 0;
        const int32_t status = openusd_hdsilk_test_ptex_synthetic_sync(argv[3], mode);
        std::array<openusd_hdsilk_test_ptex_observation, 8> buffer{};
        const size_t count = openusd_hdsilk_test_end_ptex_observation(buffer.data(), buffer.size());
        valid = status == 0 && count == 1;
        if (count == 1)
        {
            const auto& result = buffer[0];
            Print(result);
            valid = valid && !result.path_truncated && result.registered && result.terminal_mesh &&
                result.same_scene_delegate == (legacy ? 0 : 1) &&
                std::strcmp(result.path, legacy
                    ? "/LegacyMesh"
                    : (filteredUnindexed ? "/World/Grouped" : "/World/GroupedIndexed")) == 0 &&
                result.has_primvar && !result.has_element_size && result.delegate_matches &&
                result.value_count == 4 &&
                result.values[0] == 2 &&
                result.values[1] == (filteredUnindexed ? 70 : 7) &&
                result.values[2] == 3 &&
                result.values[3] == (filteredUnindexed ? 80 : 8) &&
                result.index_count == (filteredUnindexed ? 0 : 2) &&
                !result.usd_default_one_proven &&
                (filteredUnindexed ||
                 (result.indices[0] == 0 && result.indices[1] == 1)) &&
                result.strict_status == Invalid && result.default_one_status == Invalid &&
                result.mapped_faces[0] == 0 && result.mapped_faces[1] == 0;
        }
        if (!valid)
        {
            std::cerr << "Synthetic Ptex sync failed: status=" << status
                      << ", observations=" << count << "\n";
        }
    }
    openusd_silk_session_release(session);
    openusd_stage_release(stage);
    return valid ? 0 : 1;
}
