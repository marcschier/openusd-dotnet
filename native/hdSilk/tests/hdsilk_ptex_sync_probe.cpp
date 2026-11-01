// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_test_hooks.h"
#include "subdivision.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr int32_t Mapped = static_cast<int32_t>(HdSilkPtexMappingStatus::Mapped);
constexpr int32_t Invalid =
    static_cast<int32_t>(HdSilkPtexMappingStatus::InvalidFaceOffsets);

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
              << " strict=" << observation.strict_status
              << " default=" << observation.default_one_status
              << " mapped=" << observation.mapped_faces[0] << ","
              << observation.mapped_faces[1] << "\n";
}

bool CheckFrame(
    const std::vector<openusd_hdsilk_test_ptex_observation>& observations, bool updated)
{
    bool ordinary = false;
    bool indexedDefault = false;
    bool nativeInstance = false;
    bool pointInstance = false;
    bool grouped = updated;
    bool groupedIndexed = updated;
    bool missing = updated;
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
                observation.strict_status == Invalid &&
                observation.default_one_status == Invalid;
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
                observation.strict_status == Invalid && observation.default_one_status == Invalid;
            continue;
        }
        if (std::strcmp(observation.path, "/World/NoOffset") == 0)
        {
            missing = !observation.has_primvar &&
                observation.strict_status == Invalid && observation.default_one_status == Invalid;
            continue;
        }
        if (std::strcmp(observation.path, "/World/IndexedDefault") == 0)
        {
            indexedDefault = !observation.instanced && observation.has_primvar &&
                !observation.has_element_size && observation.delegate_matches &&
                observation.value_count == 2 && observation.index_count == 2 &&
                observation.values[0] == (updated ? 9 : 7) &&
                observation.values[1] == (updated ? 4 : 2) &&
                observation.indices[0] == (updated ? 0 : 1) &&
                observation.indices[1] == (updated ? 1 : 0) &&
                observation.strict_status == Invalid &&
                observation.default_one_status == Invalid &&
                observation.mapped_faces[0] == 0 && observation.mapped_faces[1] == 0;
            continue;
        }
        const bool common = observation.has_primvar && !observation.has_element_size &&
            observation.delegate_matches && observation.value_count == 2 &&
            observation.index_count == 0 && observation.strict_status == Invalid &&
            observation.default_one_status == Mapped;
        if (std::strcmp(observation.path, "/World/Ordinary") == 0)
        {
            const int32_t first = updated ? 4 : 2;
            const int32_t second = updated ? 1 : 3;
            ordinary = common && !observation.instanced &&
                observation.values[0] == first && observation.values[1] == second &&
                observation.mapped_faces[0] == static_cast<uint32_t>(first) &&
                observation.mapped_faces[1] == static_cast<uint32_t>(second + 1);
        }
        else if (observation.values[0] == (updated ? 6 : 5) &&
                 observation.values[1] == (updated ? 2 : 3))
        {
            nativeInstance = common && observation.instanced &&
                std::strstr(observation.path, "/UsdNiPropagatedPrototypes/") == observation.path &&
                observation.mapped_faces[0] == static_cast<uint32_t>(updated ? 6 : 5) &&
                observation.mapped_faces[1] == static_cast<uint32_t>(updated ? 3 : 4);
        }
        else if (observation.values[0] == (updated ? 9 : 8) &&
                 observation.values[1] == (updated ? 3 : 4))
        {
            pointInstance = common && observation.instanced &&
                std::strstr(observation.path, "/World/PointInstances/Prototype/") == observation.path &&
                observation.mapped_faces[0] == static_cast<uint32_t>(updated ? 9 : 8) &&
                observation.mapped_faces[1] == static_cast<uint32_t>(updated ? 4 : 5);
        }
        else
        {
            valid = false;
        }
    }
    if (!ordinary || !indexedDefault || !nativeInstance || !pointInstance ||
        !grouped || !groupedIndexed || !missing)
    {
        std::cerr << "Missing or invalid Ptex observations: ordinary=" << ordinary
                  << " indexedDefault=" << indexedDefault
                  << " nativeInstance=" << nativeInstance << " pointInstance=" << pointInstance
                  << " grouped=" << grouped << " groupedIndexed=" << groupedIndexed
                  << " missing=" << missing << "\n";
        valid = false;
    }
    return valid;
}

bool Sync(
    openusd_silk_session* session,
    double time,
    std::vector<openusd_hdsilk_test_ptex_observation>* observations,
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
    if (page != nullptr)
    {
        openusd_silk_page_release(page);
    }
    if (status != OPENUSD_STATUS_OK || count > buffer.size())
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
        valid = Sync(session, 0.0, &observations, &error) && CheckFrame(observations, false);
        valid = Sync(session, 1.0, &observations, &error) && CheckFrame(observations, true) && valid;
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
