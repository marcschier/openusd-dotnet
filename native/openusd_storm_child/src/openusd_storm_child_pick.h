// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_PICK_H
#define OPENUSD_STORM_CHILD_PICK_H

#include "openusd_hydra.h"
#include "openusd_render_camera_internal.h"
#include "openusd_render_pick.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

constexpr uint32_t OpenUsdStormChildMaximumPickStringBytes = 1024u * 1024u;
constexpr uint32_t OpenUsdStormChildMaximumPickContextEntries = 4096u;

struct OpenUsdStormChildPickPayload
{
    openusd_render_pick_request request{};
    openusd_render_pick_result result{};
    std::vector<char> prim_path;
    std::vector<char> instancer_path;
    std::vector<openusd_render_pick_instance_context> instance_context;
    std::vector<char> instance_context_paths;

    openusd_status Execute(
        openusd_storm_renderer* renderer,
        uint64_t context_generation,
        std::string& error)
    {
        result.context_generation = context_generation;
        if (request.context_generation != context_generation)
        {
            result.status = OPENUSD_RENDER_PICK_STATUS_STALE;
            result.flags |=
                OPENUSD_RENDER_PICK_RESULT_STALE_CONTEXT_GENERATION;
            result.state_revision = request.state_revision;
            result.scene_revision = request.scene_revision;
            result.time_code = request.time_code;
            result.camera_signature =
                openusd_render_camera_detail::Signature(request.camera);
            if ((request.flags &
                 OPENUSD_RENDER_PICK_REQUEST_HAS_SCENE_REVISION) != 0)
            {
                result.flags |=
                    OPENUSD_RENDER_PICK_RESULT_HAS_SCENE_REVISION;
            }
            return OPENUSD_STATUS_OK;
        }

        openusd_render_pick_request direct_request = request;
        direct_request.context_generation = 0;
        char error_bytes[4096]{};
        openusd_error_buffer native_error{
            error_bytes,
            sizeof(error_bytes),
            0};
        const openusd_status status = openusd_storm_pick(
            renderer,
            &direct_request,
            &result,
            prim_path.empty() ? nullptr : prim_path.data(),
            static_cast<uint32_t>(prim_path.size()),
            instancer_path.empty() ? nullptr : instancer_path.data(),
            static_cast<uint32_t>(instancer_path.size()),
            instance_context.empty() ? nullptr : instance_context.data(),
            static_cast<uint32_t>(instance_context.size()),
            instance_context_paths.empty()
                ? nullptr
                : instance_context_paths.data(),
            static_cast<uint32_t>(instance_context_paths.size()),
            &native_error);
        result.context_generation = context_generation;
        if (status != OPENUSD_STATUS_OK &&
            status != OPENUSD_STATUS_BUFFER_TOO_SMALL)
        {
            error = error_bytes;
        }
        return status;
    }

    void Cancel(uint64_t context_generation) noexcept
    {
        result.context_generation = context_generation;
        result.status = OPENUSD_RENDER_PICK_STATUS_CANCELLED;
    }
};

struct OpenUsdStormChildSelectionPayload
{
    openusd_storm_selection_update update{};
    std::vector<openusd_storm_selection_item> items;
    std::vector<char> path_bytes;

    openusd_status Execute(
        openusd_storm_renderer* renderer,
        std::string& error)
    {
        update.items = items.empty() ? nullptr : items.data();
        update.path_bytes = path_bytes.empty() ? nullptr : path_bytes.data();
        char error_bytes[4096]{};
        openusd_error_buffer native_error{
            error_bytes,
            sizeof(error_bytes),
            0};
        const openusd_status status =
            openusd_storm_set_selection(renderer, &update, &native_error);
        if (status != OPENUSD_STATUS_OK)
        {
            error = error_bytes;
        }
        return status;
    }
};

inline bool OpenUsdStormChildValidPickCapacities(
    uint32_t prim_path_capacity,
    uint32_t instancer_path_capacity,
    uint32_t instance_context_capacity,
    uint32_t instance_context_paths_capacity) noexcept
{
    return
        prim_path_capacity <= OpenUsdStormChildMaximumPickStringBytes &&
        instancer_path_capacity <= OpenUsdStormChildMaximumPickStringBytes &&
        instance_context_capacity <=
            OpenUsdStormChildMaximumPickContextEntries &&
        instance_context_paths_capacity <=
            OpenUsdStormChildMaximumPickStringBytes;
}

inline void OpenUsdStormChildCopyPickOutputs(
    const OpenUsdStormChildPickPayload& payload,
    openusd_render_pick_result* result,
    char* prim_path_buffer,
    uint32_t prim_path_capacity,
    char* instancer_path_buffer,
    uint32_t instancer_path_capacity,
    openusd_render_pick_instance_context* instance_context,
    uint32_t instance_context_capacity,
    char* instance_context_paths_buffer,
    uint32_t instance_context_paths_capacity) noexcept
{
    *result = payload.result;
    if (prim_path_buffer != nullptr && prim_path_capacity != 0)
    {
        std::memcpy(
            prim_path_buffer,
            payload.prim_path.data(),
            std::min<size_t>(prim_path_capacity, payload.prim_path.size()));
    }
    if (instancer_path_buffer != nullptr && instancer_path_capacity != 0)
    {
        std::memcpy(
            instancer_path_buffer,
            payload.instancer_path.data(),
            std::min<size_t>(
                instancer_path_capacity,
                payload.instancer_path.size()));
    }
    if (instance_context != nullptr && instance_context_capacity != 0)
    {
        std::memcpy(
            instance_context,
            payload.instance_context.data(),
            std::min<size_t>(
                instance_context_capacity,
                payload.instance_context.size()) *
                sizeof(*instance_context));
    }
    if (instance_context_paths_buffer != nullptr &&
        instance_context_paths_capacity != 0)
    {
        std::memcpy(
            instance_context_paths_buffer,
            payload.instance_context_paths.data(),
            std::min<size_t>(
                instance_context_paths_capacity,
                payload.instance_context_paths.size()));
    }
}

#endif
