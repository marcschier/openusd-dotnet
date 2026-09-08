// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_aov_candidate.h"
#include "openusd_storm_aov_test_hooks.h"

#include "pxr/base/plug/registry.h"
#include "pxr/imaging/garch/glApi.h"

#include "storm_aov_wgl_context.h"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
using Owner = std::unique_ptr<
    openusd_storm_aov_owner, decltype(&openusd_storm_aov_candidate_release)>;

void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

void RequireOk(
    openusd_status status, const openusd_error_buffer& error, const char* message)
{
    if (status != OPENUSD_STATUS_OK)
    {
        throw std::runtime_error(
            std::string(message) + ": " +
            (error.data == nullptr ? "no error buffer" : error.data));
    }
}

openusd_storm_aov_request Request()
{
    openusd_storm_aov_request request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_STORM_AOV_CANDIDATE_VERSION;
    request.output_count = 2;
    request.output_kinds[0] = OPENUSD_STORM_AOV_COLOR;
    request.output_kinds[1] = OPENUSD_STORM_AOV_DEPTH;
    request.width = 64;
    request.height = 64;
    request.max_pixels = 4096;
    request.max_working_bytes = 1048576;
    request.state_revision = 17;
    request.scene_revision = 23;
    request.revision_flags = OPENUSD_STORM_RENDER_HAS_SCENE_REVISION;
    request.camera.struct_size = sizeof(request.camera);
    request.camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    const double view[] = {
        1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    const double projection[] = {
        0.25, 0, 0, 0, 0, 0.25, 0, 0,
        0, 0, -0.2, 0, 0, 0, -1.2, 1};
    std::memcpy(request.camera.view, view, sizeof(view));
    std::memcpy(request.camera.projection, projection, sizeof(projection));
    return request;
}

openusd_storm_aov_view View(const Owner& owner, openusd_error_buffer& error)
{
    openusd_storm_aov_view view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_CANDIDATE_VERSION;
    RequireOk(
        openusd_storm_aov_candidate_get_view(owner.get(), &view, &error),
        error, "Read snapshot view");
    return view;
}

Owner Capture(
    openusd_storm_renderer* renderer,
    const openusd_storm_aov_request& request,
    openusd_error_buffer& error)
{
    openusd_storm_aov_owner* raw_owner = nullptr;
    const openusd_status status =
        openusd_storm_aov_candidate_capture(renderer, &request, &raw_owner, &error);
    Owner owner(raw_owner, openusd_storm_aov_candidate_release);
    RequireOk(status, error, "Capture Storm AOV snapshot");
    Require(owner != nullptr, "Capture did not publish an owner.");
    return owner;
}

const openusd_storm_aov_output& Find(
    const openusd_storm_aov_view& view, uint32_t kind)
{
    for (uint32_t index = 0; index < view.output_count; ++index)
    {
        if (view.outputs[index].kind == kind)
        {
            return view.outputs[index];
        }
    }
    throw std::runtime_error("The requested output name binding is absent.");
}

openusd_storm_aov_test_statistics Statistics(openusd_error_buffer& error)
{
    openusd_storm_aov_test_statistics statistics{};
    statistics.struct_size = sizeof(statistics);
    statistics.version = OPENUSD_STORM_AOV_TEST_STATISTICS_VERSION;
    RequireOk(
        openusd_storm_aov_candidate_test_get_statistics(&statistics, &error),
        error, "Get private AOV allocation/map statistics");
    return statistics;
}

void VerifyAdmission(openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    openusd_storm_aov_request request = Request();
    request.width = 65;
    const auto before = Statistics(error);
    auto* owner = reinterpret_cast<openusd_storm_aov_owner*>(static_cast<uintptr_t>(1));
    const openusd_status status =
        openusd_storm_aov_candidate_capture(renderer, &request, &owner, &error);
    const auto after = Statistics(error);
    Require(
        status == OPENUSD_STATUS_BUFFER_TOO_SMALL && owner == nullptr &&
            after.owner_allocations == before.owner_allocations &&
            after.map_calls == before.map_calls &&
            after.decode_calls == before.decode_calls &&
            after.live_owners == before.live_owners,
        "Oversized pixels were not rejected before allocation, mapping, or decoding.");
    std::cout << "OVERSIZE_BEFORE_ALLOCATION_AND_MAP=passed\n";
}

void VerifyMapFailureCleanup(
    openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    const auto before = Statistics(error);
    RequireOk(
        openusd_storm_aov_candidate_test_set_failpoint(
            OPENUSD_STORM_AOV_TEST_FAIL_AFTER_MAP, &error),
        error, "Arm the private post-map failure");
    const openusd_storm_aov_request request = Request();
    auto* owner = reinterpret_cast<openusd_storm_aov_owner*>(static_cast<uintptr_t>(1));
    const openusd_status status =
        openusd_storm_aov_candidate_capture(renderer, &request, &owner, &error);
    const auto after = Statistics(error);
    Require(
        status == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr &&
            after.map_calls == before.map_calls + 1 &&
            after.unmap_calls == before.unmap_calls + 1 &&
            after.owner_allocations == before.owner_allocations + 1 &&
            after.live_owners == before.live_owners,
        "An exception after real SDK Map leaked a mapping or owned CPU allocation.");
    Owner retry = Capture(renderer, request, error);
    Require(
        Statistics(error).live_owners == before.live_owners + 1,
        "The renderer was not retryable after failed readback.");
    std::cout << "REAL_MAP_EXCEPTION_RAII_UNMAP_RELEASE_AND_RETRY=passed\n";
}

template <class TValue>
TValue Pixel(
    const openusd_storm_aov_view& view, uint32_t kind, uint32_t x, uint32_t y)
{
    const openusd_storm_aov_output& output = Find(view, kind);
    Require(output.status == OPENUSD_STORM_AOV_STATUS_READY, "Output is not ready.");
    Require(x < output.width && y < output.height, "Pixel coordinates exceed output.");
    const uint64_t offset = output.data_offset +
        static_cast<uint64_t>(y) * output.row_stride_bytes + x * sizeof(TValue);
    Require(
        offset <= view.pixel_bytes &&
            sizeof(TValue) <= view.pixel_bytes - offset,
        "Pixel data exceeds its owned extent.");
    TValue value{};
    std::memcpy(
        &value, static_cast<const uint8_t*>(view.pixel_data) + offset, sizeof(value));
    return value;
}

void VerifyPlanarDepth(openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    const openusd_storm_aov_request request = Request();
    openusd_storm_aov_owner* raw_owner = nullptr;
    RequireOk(
        openusd_storm_aov_candidate_capture(
            renderer, &request, &raw_owner, &error),
        error, "Capture actual native AOV outputs");
    Owner owner(raw_owner, openusd_storm_aov_candidate_release);
    Require(owner != nullptr, "Capture succeeded without an owned snapshot.");
    const openusd_storm_aov_view view = View(owner, error);
    const auto& color = Find(view, OPENUSD_STORM_AOV_COLOR);
    const auto& depth = Find(view, OPENUSD_STORM_AOV_DEPTH);
    Require(
        view.output_count == 2 && view.width == 64 && view.height == 64 &&
            view.state_revision == 17 && view.scene_revision == 23 &&
            view.revision_flags == OPENUSD_STORM_RENDER_HAS_SCENE_REVISION &&
            view.capture_id != 0 &&
            color.status == OPENUSD_STORM_AOV_STATUS_READY &&
            color.format == OPENUSD_STORM_AOV_FORMAT_FLOAT16_VEC4 &&
            color.row_stride_bytes == 512 &&
            depth.status == OPENUSD_STORM_AOV_STATUS_READY &&
            depth.format == OPENUSD_STORM_AOV_FORMAT_FLOAT32 &&
            depth.row_stride_bytes == 256 &&
            depth.origin == OPENUSD_STORM_AOV_ORIGIN_TOP_LEFT &&
            depth.depth_convention == OPENUSD_STORM_AOV_DEPTH_OPENGL_WINDOW &&
            view.admitted_working_bytes <= request.max_working_bytes,
        "Snapshot descriptors or exact render binding are invalid.");
    Require(
        std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 16, 31) - 0.2f) <
                0.00001f &&
            std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 48, 31) - 0.6f) <
                0.00001f &&
            Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 0, 0) == 1.0f,
        "Native bulk depth is not literal OpenGL window depth.");
    std::cout << "PRIVATE_C_ABI_PLANAR_DEPTH=0.2,0.6,1\n";
}

openusd_storm_aov_request IdentityRequest()
{
    openusd_storm_aov_request request = Request();
    request.output_count = 7;
    request.output_kinds[0] = OPENUSD_STORM_AOV_DEPTH;
    request.output_kinds[1] = OPENUSD_STORM_AOV_COLOR;
    request.output_kinds[2] = OPENUSD_STORM_AOV_INSTANCE_ID;
    request.output_kinds[3] = OPENUSD_STORM_AOV_ELEMENT_ID;
    request.output_kinds[4] = OPENUSD_STORM_AOV_PRIM_ID;
    request.output_kinds[5] = OPENUSD_STORM_AOV_NEYE;
    request.output_kinds[6] = OPENUSD_STORM_AOV_NORMAL;
    request.flags = OPENUSD_STORM_AOV_REQUEST_IDENTITIES;
    request.max_unique_identities = 16;
    request.max_instance_contexts = 32;
    request.max_text_bytes = 2048;
    return request;
}

std::string Text(
    const openusd_storm_aov_view& view, uint32_t offset, uint32_t length)
{
    Require(
        offset <= view.text_byte_count && length <= view.text_byte_count - offset,
        "Identity text slice exceeds its owned extent.");
    return length == 0 ? std::string() : std::string(view.text + offset, length);
}

const openusd_storm_aov_identity& IdentityAt(
    const openusd_storm_aov_view& view, uint32_t x, uint32_t y)
{
    const uint64_t pixel = static_cast<uint64_t>(y) * view.width + x;
    Require(
        view.identity_status == OPENUSD_STORM_AOV_STATUS_READY &&
            pixel < view.identity_index_count,
        "Identity index image is absent or truncated.");
    const uint32_t index = view.identity_indices[pixel];
    Require(index < view.identity_count, "Foreground identity is not indexed.");
    return view.identities[index];
}

void VerifyIdentityTable(openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    const openusd_storm_aov_request request = IdentityRequest();
    openusd_storm_aov_owner* raw_owner = nullptr;
    RequireOk(
        openusd_storm_aov_candidate_capture(renderer, &request, &raw_owner, &error),
        error, "Capture indexed native identities");
    Owner owner(raw_owner, openusd_storm_aov_candidate_release);
    const openusd_storm_aov_view view = View(owner, error);
    Require(
        view.identity_count == 4 && view.identity_index_count == 4096 &&
            view.context_count == 2 &&
            view.identity_indices[0] == OPENUSD_STORM_AOV_BACKGROUND_INDEX &&
            Pixel<int32_t>(view, OPENUSD_STORM_AOV_PRIM_ID, 0, 0) == -1 &&
            Pixel<int32_t>(view, OPENUSD_STORM_AOV_INSTANCE_ID, 0, 0) == -1,
        "Identity coverage or background status is incorrect.");
    for (uint32_t index = 0; index < view.output_count; ++index)
    {
        Require(
            view.outputs[index].kind == request.output_kinds[index],
            "Output name binding changed with request ordering.");
    }
    const auto& near_identity = IdentityAt(view, 16, 31);
    const auto& left = IdentityAt(view, 24, 15);
    const auto& right = IdentityAt(view, 40, 15);
    Require(
        near_identity.status == OPENUSD_STORM_AOV_IDENTITY_RESOLVED &&
            Text(view, near_identity.prim_path_offset, near_identity.prim_path_length) ==
                "/World/Near" &&
            near_identity.instance_index == -1 &&
            near_identity.instancer_path_length == 0 &&
            near_identity.context_count == 0 &&
            left.status == OPENUSD_STORM_AOV_IDENTITY_RESOLVED &&
            right.status == OPENUSD_STORM_AOV_IDENTITY_RESOLVED &&
            left.prim_id == right.prim_id &&
            left.instance_id != right.instance_id &&
            left.instance_index == 0 && right.instance_index == 1 &&
            Text(view, left.prim_path_offset, left.prim_path_length) ==
                "/World/Instances/Prototype" &&
            Text(view, right.prim_path_offset, right.prim_path_length) ==
                "/World/Instances/Prototype" &&
            left.context_count == 1 && right.context_count == 1,
        "Native IDs did not decode to distinct canonical point-instance identities.");
    for (const auto* identity : {&left, &right})
    {
        Require(
            Text(view, identity->instancer_path_offset, identity->instancer_path_length) ==
                "/World/Instances" &&
                identity->context_offset < view.context_count,
            "Decoded point-instancer path or context offset is invalid.");
        const auto& instance_context = view.contexts[identity->context_offset];
        Require(
            Text(view, instance_context.path_offset, instance_context.path_length) ==
                "/World/Instances" &&
                instance_context.instance_index == identity->instance_index,
            "Ordered instancer context was not retained.");
    }
    const auto& element = Find(view, OPENUSD_STORM_AOV_ELEMENT_ID);
    const auto& normal = Find(view, OPENUSD_STORM_AOV_NORMAL);
    const auto& neye = Find(view, OPENUSD_STORM_AOV_NEYE);
    Require(
        element.status == OPENUSD_STORM_AOV_STATUS_ABSENT && element.data_bytes == 0 &&
            element.format == OPENUSD_STORM_AOV_FORMAT_NONE &&
            normal.status == OPENUSD_STORM_AOV_STATUS_UNSUPPORTED &&
            normal.data_bytes == 0 &&
            neye.status == OPENUSD_STORM_AOV_STATUS_READY &&
            neye.format == OPENUSD_STORM_AOV_FORMAT_UNORM8_VEC4,
        "The actual pinned AOV support boundary was misreported.");
    const auto normal_pixel =
        Pixel<std::array<uint8_t, 4>>(view, OPENUSD_STORM_AOV_NEYE, 16, 31);
    Require(
        normal_pixel == std::array<uint8_t, 4>{0, 0, 255, 255},
        "The front-facing planar Neye literal is not the raw quantized eye normal.");
    std::cout << "QUANTIZED_NEYE="
              << static_cast<uint32_t>(normal_pixel[0]) << ","
              << static_cast<uint32_t>(normal_pixel[1]) << ","
              << static_cast<uint32_t>(normal_pixel[2]) << ","
              << static_cast<uint32_t>(normal_pixel[3]) << "\n"
              << "PRIVATE_C_ABI_IDENTITIES=4,point-instances=0/1,elementId=ABSENT\n";
}

using PresentedPixels = std::array<uint8_t, 64 * 64 * 4>;

PresentedPixels ReadPresentation()
{
    PresentedPixels pixels{};
    glFinish();
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    glReadBuffer(GL_BACK);
    glReadPixels(0, 0, 64, 64, GL_RGBA, GL_UNSIGNED_BYTE, pixels.data());
    Require(glGetError() == GL_NO_ERROR, "Could not read the existing RGBA presentation.");
    return pixels;
}

void RenderPresentation(
    openusd_storm_renderer* renderer,
    const openusd_storm_aov_request& request,
    openusd_error_buffer& error)
{
    int32_t converged = 0;
    for (uint32_t iteration = 0; iteration < 32; ++iteration)
    {
        RequireOk(
            openusd_storm_render_v2(
                renderer, request.width, request.height, request.framebuffer,
                request.time_code, &request.camera,
                request.state_revision, request.scene_revision, request.revision_flags,
                &converged, &error),
            error, "Render existing RGBA presentation");
        if (converged != 0)
        {
            break;
        }
    }
    Require(converged != 0, "Existing presentation did not converge.");
}

void VerifyPresentationAndPicking(
    openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    const openusd_storm_aov_request request = IdentityRequest();
    RenderPresentation(renderer, request, error);
    const PresentedPixels before = ReadPresentation();
    Owner owner = Capture(renderer, request, error);
    Require(
        before == ReadPresentation(),
        "Bulk AOV selection changed the same-render RGBA presentation.");
    openusd_render_pick_request pick{};
    pick.struct_size = sizeof(pick);
    pick.version = OPENUSD_RENDER_PICK_REQUEST_VERSION;
    pick.x = 16;
    pick.y = 31;
    pick.width = 1;
    pick.height = 1;
    pick.viewport_width = request.width;
    pick.viewport_height = request.height;
    pick.flags = OPENUSD_RENDER_PICK_REQUEST_HAS_SCENE_REVISION;
    pick.state_revision = request.state_revision;
    pick.scene_revision = request.scene_revision;
    pick.time_code = request.time_code;
    pick.camera = request.camera;
    openusd_render_pick_result result{};
    result.struct_size = sizeof(result);
    result.version = OPENUSD_RENDER_PICK_RESULT_VERSION;
    char prim_path[128]{};
    char instancer_path[128]{};
    char context_paths[256]{};
    std::array<openusd_render_pick_instance_context, 4> contexts{};
    RequireOk(
        openusd_storm_pick(
            renderer, &pick, &result, prim_path, sizeof(prim_path),
            instancer_path, sizeof(instancer_path), contexts.data(),
            static_cast<uint32_t>(contexts.size()),
            context_paths, sizeof(context_paths), &error),
        error, "Pick after bulk AOV capture");
    Require(
        result.status == OPENUSD_RENDER_PICK_STATUS_HIT &&
            std::strcmp(prim_path, "/World/Near") == 0 &&
            std::abs(result.normalized_depth - 0.2) < 0.00001 &&
            result.scene_revision == request.scene_revision &&
            result.state_revision == request.state_revision,
        "Existing picking or its rendered-state binding changed.");
    RenderPresentation(renderer, request, error);
    Require(
        before == ReadPresentation(),
        "Ordinary RGBA presentation changed after restoring the color output.");
    const auto view = View(owner, error);
    Require(
        std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 16, 31) - 0.2f) <
            0.00001f,
        "A subsequent presentation render changed the owned AOV snapshot.");
    std::cout << "PRESENTATION_RGBA_BIT_EXACT_AND_EXISTING_PICK=passed\n";
}

void SetPlaneDepth(
    openusd_stage* stage,
    const char* path,
    float left,
    float right,
    float depth,
    int32_t has_time,
    double time,
    openusd_error_buffer& error)
{
    const std::array<openusd_vec3f, 4> points{
        openusd_vec3f{left, -1, -depth}, openusd_vec3f{right, -1, -depth},
        openusd_vec3f{right, 1, -depth}, openusd_vec3f{left, 1, -depth}};
    RequireOk(
        openusd_geom_mesh_set_points(
            stage, path, points.data(), points.size(), has_time, time, &error),
        error, "Edit the shared planar stage");
}

void VerifyTemporalFramingAndPerspective(
    openusd_storm_renderer* renderer, openusd_stage* stage, openusd_error_buffer& error)
{
    openusd_storm_aov_request request = IdentityRequest();
    Owner owner = Capture(renderer, request, error);
    openusd_storm_aov_view view = View(owner, error);
    const uint64_t initial_capture = view.capture_id;
    SetPlaneDepth(stage, "/World/Near", -3, -1, 5, 0, 0, error);
    Require(
        std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 16, 31) - 0.2f) <
            0.00001f,
        "Editing the stage mutated a detached snapshot.");
    owner.reset();
    ++request.state_revision;
    ++request.scene_revision;
    owner = Capture(renderer, request, error);
    view = View(owner, error);
    const auto& edited = IdentityAt(view, 16, 31);
    Require(
        std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 16, 31) - 0.4f) <
                0.00001f &&
            view.capture_id > initial_capture &&
            view.state_revision == request.state_revision &&
            view.scene_revision == request.scene_revision &&
            Text(view, edited.prim_path_offset, edited.prim_path_length) ==
                "/World/Near",
        "A shared-stage edit left depth or canonical identity stale.");
    owner.reset();
    SetPlaneDepth(stage, "/World/Near", -3, -1, 3, 1, 0, error);
    SetPlaneDepth(stage, "/World/Near", -3, -1, 5, 1, 1, error);
    request.width = 128;
    request.height = 96;
    request.max_pixels = 128 * 96;
    request.max_working_bytes = 2097152;
    request.time_code = 0.5;
    ++request.scene_revision;
    owner = Capture(renderer, request, error);
    view = View(owner, error);
    const auto& framed = IdentityAt(view, 32, 47);
    const auto& instance = IdentityAt(view, 48, 23);
    Require(
        view.width == 128 && view.height == 96 && view.time_code == 0.5 &&
            Find(view, OPENUSD_STORM_AOV_DEPTH).row_stride_bytes == 512 &&
            std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 32, 47) - 0.3f) <
                0.00001f &&
            Text(view, framed.prim_path_offset, framed.prim_path_length) ==
                "/World/Near" &&
            Text(view, instance.prim_path_offset, instance.prim_path_length) ==
                "/World/Instances/Prototype" &&
            instance.instance_index == 0,
        "Time interpolation, framing, or name binding is stale.");
    owner.reset();
    SetPlaneDepth(stage, "/World/Near", -3, -1, 2, 1, 2, error);
    SetPlaneDepth(stage, "/World/Far", 1, 3, 5, 1, 2, error);
    request.width = 64;
    request.height = 64;
    request.time_code = 2;
    ++request.state_revision;
    ++request.scene_revision;
    const double projection[] = {
        1, 0, 0, 0, 0, 1, 0, 0,
        0, 0, -1.2, -1, 0, 0, -2.2, 0};
    std::memcpy(request.camera.projection, projection, sizeof(projection));
    owner = Capture(renderer, request, error);
    view = View(owner, error);
    Require(
        std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 8, 31) - 0.55f) <
                0.00001f &&
            std::abs(Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, 45, 31) - 0.88f) <
                0.00001f &&
            Find(view, OPENUSD_STORM_AOV_DEPTH).depth_convention ==
                OPENUSD_STORM_AOV_DEPTH_OPENGL_WINDOW,
        "Perspective depth was linearized or assigned the wrong convention.");
    std::cout << "EDIT_TIME_FRAMING=0.2/0.4/0.3,128x96\n"
              << "PERSPECTIVE_LITERAL_DEPTH=0.55,0.88\n";
}

void RefuseBeforeReadback(
    openusd_storm_renderer* renderer,
    const openusd_storm_aov_request& request,
    openusd_status expected,
    openusd_error_buffer& error)
{
    const auto before = Statistics(error);
    auto* owner = reinterpret_cast<openusd_storm_aov_owner*>(static_cast<uintptr_t>(1));
    const openusd_status status =
        openusd_storm_aov_candidate_capture(renderer, &request, &owner, &error);
    Require(
        status == expected && owner == nullptr &&
            error.required > 1 && error.data[0] != '\0',
        "A refused request published an owner or lost its explicit error.");
    const auto after = Statistics(error);
    Require(
        before.owner_allocations == after.owner_allocations &&
            before.map_calls == after.map_calls &&
            before.decode_calls == after.decode_calls &&
            before.live_owners == after.live_owners,
        "A refused request performed readback allocation, mapping, or decoding.");
}

void VerifyInvalidRequestsAndIdentityBudgets(
    openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    for (uint32_t test = 0; test < 16; ++test)
    {
        openusd_storm_aov_request request = Request();
        openusd_status expected = OPENUSD_STATUS_INVALID_ARGUMENT;
        switch (test)
        {
        case 0: request.version = 999; break;
        case 1: --request.struct_size; break;
        case 2: request.output_count = OPENUSD_STORM_AOV_MAX_OUTPUTS + 1; break;
        case 3: request.output_kinds[0] = UINT32_MAX; break;
        case 4: request.output_kinds[1] = request.output_kinds[0]; break;
        case 5: request.output_kinds[7] = OPENUSD_STORM_AOV_COLOR; break;
        case 6: request.width = (std::numeric_limits<int32_t>::max)(); break;
        case 7: request.height = 0; break;
        case 8: request.time_code = std::numeric_limits<double>::quiet_NaN(); break;
        case 9:
            request.camera.mode = static_cast<openusd_render_camera_mode>(UINT32_MAX);
            break;
        case 10: request.flags = UINT32_MAX; break;
        case 11: request.revision_flags = UINT32_MAX; break;
        case 12: request.max_pixels = OPENUSD_STORM_AOV_MAX_PIXELS + 1; break;
        case 13: request.max_working_bytes = OPENUSD_STORM_AOV_MAX_WORKING_BYTES + 1; break;
        case 14: request.max_text_bytes = 1; break;
        case 15:
            request.max_working_bytes = 1;
            expected = OPENUSD_STATUS_BUFFER_TOO_SMALL;
            break;
        }
        RefuseBeforeReadback(renderer, request, expected, error);
    }
    for (uint32_t test = 0; test < 3; ++test)
    {
        openusd_storm_aov_request request = IdentityRequest();
        if (test == 0)
        {
            request.max_unique_identities = 1;
        }
        else if (test == 1)
        {
            request.max_instance_contexts = 0;
        }
        else
        {
            request.max_text_bytes = 1;
        }
        const auto before = Statistics(error);
        openusd_storm_aov_owner* owner = nullptr;
        const openusd_status status =
            openusd_storm_aov_candidate_capture(renderer, &request, &owner, &error);
        const auto after = Statistics(error);
        Require(
            status == OPENUSD_STATUS_BUFFER_TOO_SMALL && owner == nullptr &&
                after.live_owners == before.live_owners &&
                after.map_calls - before.map_calls ==
                    after.unmap_calls - before.unmap_calls &&
                after.decode_calls - before.decode_calls <= request.max_unique_identities &&
                (test != 0 || after.decode_calls == before.decode_calls),
            "An identity budget silently truncated, leaked, or over-decoded its output.");
    }
    std::cout << "INVALID_ARGUMENT_AND_IDENTITY_TEXT_CONTEXT_BUDGETS=passed\n";
}

void RequireClearedView(const openusd_storm_aov_view& view)
{
    Require(
        view.struct_size == 0 && view.version == 0 && view.output_count == 0 &&
            view.width == 0 && view.height == 0 && view.identity_count == 0 &&
            view.context_count == 0 && view.text_byte_count == 0 &&
            view.capture_id == 0 && view.state_revision == 0 &&
            view.scene_revision == 0 && view.pixel_bytes == 0 &&
            view.owned_bytes == 0 && view.admitted_working_bytes == 0 &&
            view.outputs == nullptr && view.pixel_data == nullptr &&
            view.identities == nullptr && view.contexts == nullptr &&
            view.text == nullptr && view.identity_indices == nullptr &&
            view.identity_index_count == 0,
        "Failure did not initialize the complete versioned view.");
}

void VerifyOwnerAndContext(
    openusd_storm_renderer* renderer,
    const StormAovWglContext& context,
    openusd_error_buffer& error)
{
    const openusd_storm_aov_request request = Request();
    const auto initial = Statistics(error);
    Owner owner = Capture(renderer, request, error);
    RefuseBeforeReadback(renderer, request, OPENUSD_STATUS_INVALID_ARGUMENT, error);
    auto view = View(owner, error);
    view.version = 999;
    Require(
        openusd_storm_aov_candidate_get_view(owner.get(), &view, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT,
        "An invalid view version was accepted.");
    RequireClearedView(view);
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_CANDIDATE_VERSION;
    Require(
        openusd_storm_aov_candidate_get_view(nullptr, &view, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT,
        "An absent owner was accepted.");
    RequireClearedView(view);
    openusd_status wrong_thread = OPENUSD_STATUS_OK;
    openusd_storm_aov_owner* wrong_owner = nullptr;
    std::thread wrong_thread_probe([&]()
    {
        char text[256]{};
        openusd_error_buffer thread_error{text, sizeof(text), 0};
        wrong_thread = openusd_storm_aov_candidate_capture(
            renderer, &request, &wrong_owner, &thread_error);
    });
    wrong_thread_probe.join();
    Require(
        wrong_thread == OPENUSD_STATUS_WRONG_THREAD && wrong_owner == nullptr,
        "The private capture bypassed the existing Storm owner-thread guard.");
    owner.reset();
    StormAovWglContext::ClearCurrent();
    RefuseBeforeReadback(renderer, request, OPENUSD_STATUS_INVALID_ARGUMENT, error);
    Require(context.MakeCurrent(), "Could not restore the original context.");
    {
        StormAovWglContext different;
        Require(different.Create(), "Could not create the wrong-context negative control.");
        RefuseBeforeReadback(renderer, request, OPENUSD_STATUS_INVALID_ARGUMENT, error);
    }
    Require(context.MakeCurrent(), "Could not restore context after the negative control.");
    owner = Capture(renderer, request, error);
    openusd_storm_aov_owner* transferred = owner.release();
    openusd_status detached_view = OPENUSD_STATUS_NATIVE_ERROR;
    bool detached_pixels = false;
    std::thread release_thread([&]()
    {
        char text[256]{};
        openusd_error_buffer thread_error{text, sizeof(text), 0};
        openusd_storm_aov_view thread_view{};
        thread_view.struct_size = sizeof(thread_view);
        thread_view.version = OPENUSD_STORM_AOV_CANDIDATE_VERSION;
        detached_view = openusd_storm_aov_candidate_get_view(
            transferred, &thread_view, &thread_error);
        detached_pixels = thread_view.pixel_data != nullptr && thread_view.pixel_bytes > 0;
        openusd_storm_aov_candidate_release(transferred);
    });
    release_thread.join();
    Require(
        detached_view == OPENUSD_STATUS_OK && detached_pixels &&
            Statistics(error).live_owners == initial.live_owners,
        "CPU-only view/release required a render thread or GL context.");
    openusd_storm_aov_candidate_release(nullptr);
    std::cout << "OWNER_THREAD_CONTEXT_SINGLE_OWNER_AND_CPU_RELEASE=passed\n";
}

void VerifyBackground(
    openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    openusd_storm_aov_request request = IdentityRequest();
    request.camera.view[12] = 100;
    request.max_unique_identities = 0;
    request.max_instance_contexts = 0;
    request.max_text_bytes = 0;
    const auto before = Statistics(error);
    Owner owner = Capture(renderer, request, error);
    const auto view = View(owner, error);
    Require(
        view.identity_status == OPENUSD_STORM_AOV_STATUS_READY &&
            view.identity_count == 0 && view.identities == nullptr &&
            view.context_count == 0 && view.contexts == nullptr &&
            view.text_byte_count == 0 && view.text == nullptr &&
            view.identity_index_count == 4096 &&
            Statistics(error).decode_calls == before.decode_calls,
        "True background was confused with unsupported or unresolved identity.");
    for (uint32_t y = 0; y < view.height; ++y)
    {
        for (uint32_t x = 0; x < view.width; ++x)
        {
            Require(
                view.identity_indices[y * view.width + x] ==
                    OPENUSD_STORM_AOV_BACKGROUND_INDEX &&
                    Pixel<float>(view, OPENUSD_STORM_AOV_DEPTH, x, y) == 1.0f &&
                    Pixel<int32_t>(view, OPENUSD_STORM_AOV_PRIM_ID, x, y) == -1,
                "Background was silently remapped to zero or a foreground value.");
        }
    }
    std::cout << "ALL_BACKGROUND_WITH_ZERO_IDENTITY_BUDGET=passed\n";
}

Owner VerifyPixelThreshold(
    openusd_storm_renderer* renderer, openusd_error_buffer& error)
{
    openusd_storm_aov_request request = IdentityRequest();
    request.width = 1024;
    request.height = 1025;
    request.max_pixels = OPENUSD_STORM_AOV_MAX_PIXELS;
    request.max_working_bytes = OPENUSD_STORM_AOV_MAX_WORKING_BYTES;
    RefuseBeforeReadback(renderer, request, OPENUSD_STATUS_BUFFER_TOO_SMALL, error);
    request.height = 1024;
    const auto before = Statistics(error);
    Owner owner = Capture(renderer, request, error);
    const auto view = View(owner, error);
    const auto after = Statistics(error);
    Require(
        view.width == 1024 && view.height == 1024 &&
            view.identity_index_count == OPENUSD_STORM_AOV_MAX_PIXELS &&
            view.identity_count == 4 &&
            after.decode_calls - before.decode_calls == 4 &&
            after.map_calls - before.map_calls == 5 &&
            after.unmap_calls - before.unmap_calls == 5 &&
            view.owned_bytes <= view.admitted_working_bytes &&
            view.admitted_working_bytes <= request.max_working_bytes,
        "The exact pixel threshold or bounded unique-only decode contract failed.");
    std::cout << "EXACT_PIXEL_LIMIT=1048576,unique-decoder-calls=4"
              << ",owned-bytes=" << view.owned_bytes
              << ",working-upper-bound=" << view.admitted_working_bytes << "\n";
    return owner;
}

void Run(const char* plugin_path, const char* stage_path)
{
    StormAovWglContext context;
    Require(context.Create(), "Could not create a hidden WGL context.");
    Require(GarchGLApiLoad(), "Could not load GL.");
    StormAovWglContext::PrintDriver();
    PlugRegistry::GetInstance().RegisterPlugins(plugin_path);
    Require(openusd_storm_get_abi_version() == 8, "Public Storm ABI changed.");
    char error_text[2048]{};
    openusd_error_buffer error{error_text, sizeof(error_text), 0};
    openusd_stage* stage = nullptr;
    RequireOk(openusd_stage_open(stage_path, &stage, &error), error, "Open stage");
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)>
        stage_owner(stage, openusd_stage_release);
    openusd_storm_renderer* renderer = nullptr;
    RequireOk(
        openusd_storm_create_from_stage(plugin_path, stage, &renderer, &error),
        error, "Create shared-stage Storm renderer");
    std::unique_ptr<openusd_storm_renderer, decltype(&openusd_storm_release)>
        renderer_owner(renderer, openusd_storm_release);
    VerifyAdmission(renderer, error);
    VerifyMapFailureCleanup(renderer, error);
    VerifyPlanarDepth(renderer, error);
    VerifyIdentityTable(renderer, error);
    VerifyPresentationAndPicking(renderer, error);
    VerifyInvalidRequestsAndIdentityBudgets(renderer, error);
    VerifyOwnerAndContext(renderer, context, error);
    VerifyBackground(renderer, error);
    VerifyTemporalFramingAndPerspective(renderer, stage, error);
    Owner detached = VerifyPixelThreshold(renderer, error);
    RequireOk(openusd_storm_destroy(renderer, &error), error, "Destroy Storm");
    static_cast<void>(renderer_owner.release());
    stage_owner.reset();
    StormAovWglContext::ClearCurrent();
    const auto detached_view = View(detached, error);
    const auto& detached_identity = IdentityAt(detached_view, 256, 511);
    Require(
        Text(
            detached_view,
            detached_identity.prim_path_offset,
            detached_identity.prim_path_length) == "/World/Near" &&
            std::abs(
                Pixel<float>(detached_view, OPENUSD_STORM_AOV_DEPTH, 256, 511) -
                0.2f) < 0.00001f,
        "Renderer/stage teardown invalidated detached pixels or decoded paths.");
    detached.reset();
    const auto final_statistics = Statistics(error);
    Require(
        final_statistics.live_owners == 0 &&
            final_statistics.map_calls == final_statistics.unmap_calls,
        "The probe left a native owner or mapped SDK render buffer live.");
    std::cout << "DETACHED_AFTER_RENDERER_STAGE_AND_GL_TEARDOWN=passed\n";
    std::cout << "PRIVATE_C_ABI_PROOF=passed\n";
}
}

int main(int argc, char** argv)
{
    std::cout << std::unitbuf;
    if (argc != 3)
    {
        std::cerr << "Usage: probe <plugin-path> <planar-stage>\n";
        return 2;
    }
    try
    {
        Run(argv[1], argv[2]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "PRIVATE_AOV_PROOF_FAILED=" << error.what() << "\n";
        return 1;
    }
}
