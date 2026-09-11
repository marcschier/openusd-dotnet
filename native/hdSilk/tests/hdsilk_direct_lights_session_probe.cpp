// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_direct_lights_test_decode.h"
#include "openusd_dotnet.h"
#include "openusd_hdsilk.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace
{
using namespace hdsilk_direct_lights_test;

static_assert(OPENUSD_STATUS_NATIVE_ERROR == 4);
static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 24u);
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 6u);
static_assert(OPENUSD_SILK_SCENE_INGESTION_VERSION == 1u);
static_assert(OPENUSD_SILK_MAX_FRAME_LIGHTS == 128u);
static_assert(OPENUSD_SILK_MAX_SHADOW_MAPS == 4u);
static_assert(OPENUSD_SILK_MAX_DOME_LIGHTS == 8u);

constexpr size_t FrameBytes = 23368;
constexpr size_t DirectOffset = 552;
constexpr size_t DirectBytes = 176;
constexpr std::array<double, 16> Identity{
    1.0, 0.0, 0.0, 0.0,
    0.0, 1.0, 0.0, 0.0,
    0.0, 0.0, 1.0, 0.0,
    0.0, 0.0, 0.0, 1.0};

void Require(bool condition, const std::string& message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

struct ApiStatus
{
    char text[4096]{};
    openusd_error_buffer error{text, sizeof(text), 0};

    openusd_error_buffer* Reset()
    {
        std::memset(text, 0, sizeof(text));
        error.required = 0;
        return &error;
    }

    void RequireOk(openusd_status status, const std::string& operation) const
    {
        Require(
            status == OPENUSD_STATUS_OK,
            operation + " returned status " + std::to_string(status) + ": " + text);
    }
};

// Ownership only: both cases create their actual stage/session through the C
// APIs below. Pages are scoped inside this owner and released before teardown.
struct StageSessionOwner
{
    openusd_stage* stage = nullptr;
    openusd_silk_session* session = nullptr;

    StageSessionOwner() = default;
    StageSessionOwner(const StageSessionOwner&) = delete;
    StageSessionOwner& operator=(const StageSessionOwner&) = delete;

    ~StageSessionOwner() noexcept
    {
        if (session != nullptr)
        {
            // Normal exits call Close(). On an assertion/status exception,
            // still check teardown, without throwing over the original error.
            ApiStatus api;
            const openusd_status status =
                openusd_silk_session_destroy(session, api.Reset());
            if (status != OPENUSD_STATUS_OK)
            {
                std::fprintf(
                    stderr, "Session cleanup also failed (status %d): %s\n",
                    static_cast<int>(status), api.text);
            }
        }
        if (stage != nullptr)
        {
            openusd_stage_release(stage);
        }
    }

    void Close()
    {
        if (session != nullptr)
        {
            ApiStatus api;
            api.RequireOk(
                openusd_silk_session_destroy(session, api.Reset()),
                "Destroy session");
            session = nullptr;
        }
        if (stage != nullptr)
        {
            openusd_stage_release(stage);
            stage = nullptr;
        }
    }
};

struct OwnedPage
{
    openusd_silk_page* handle = nullptr;
    openusd_silk_page_view view{};

    OwnedPage()
    {
        view.struct_size = static_cast<uint32_t>(sizeof(view));
    }

    OwnedPage(const OwnedPage&) = delete;
    OwnedPage& operator=(const OwnedPage&) = delete;

    ~OwnedPage() noexcept
    {
        if (handle != nullptr)
        {
            openusd_silk_page_release(handle);
        }
    }
};

Frame ReadNativeFrame(const OwnedPage& page)
{
    const openusd_silk_page_view& view = page.view;
    Require(page.handle != nullptr, "Successful sync returned no owned page");
    Require(
        openusd_silk_get_session_abi_version() == 6u &&
            openusd_silk_get_page_abi_version() == 24u,
        "Loaded native session/page ABI is not 6/24");
    Require(
        view.struct_size == sizeof(view) && view.abi_version == 24u &&
            view.revision != 0 && view.data != nullptr &&
            view.data_size >= FrameBytes,
        "Successful sync returned an invalid page24 view");
    Frame frame = ReadFrame(view.data, view.data_size, view.command_count);
    Require(
        frame.width == 96 && frame.height == 96 && frame.clipPlaneCount == 0u,
        "Native FRAME lost the requested viewport/camera state");
    Require(
        frame.domeCount == 0u && frame.ambientColor == std::array<float, 3>{} &&
            frame.ambientIntensity == 0.0f,
        "The no-dome fixture acquired environment lighting");
    for (const DomeSlot& dome : frame.domes)
    {
        Require(
            dome.flags == 0u && dome.ambientColor == std::array<float, 3>{},
            "An unused dome slot is occupied");
    }

    // Check every bit in every unused direct entry, including the identity
    // doubles, not an all-zero blob or a sample of the first unused slot.
    const CommandView wire{view.data, FrameBytes};
    for (size_t slot = frame.directLightCount; slot < 128; ++slot)
    {
        for (size_t offset = 0; offset < DirectBytes; offset += 4)
        {
            const uint32_t expected =
                offset == 36 || offset == 76 || offset == 116 || offset == 156
                ? 0x3ff00000u : 0u;
            Require(
                ReadU32Le(wire, DirectOffset + DirectBytes * slot + offset) ==
                    expected,
                "Unused direct slot " + std::to_string(slot) +
                    " differs at byte " + std::to_string(offset));
        }
    }
    return frame;
}

void RequireDirectSlot(
    const DirectSlot& actual,
    const DirectSlot& expected,
    const std::string& label)
{
    Require(
        actual.type == expected.type &&
            actual.shadowEnabled == expected.shadowEnabled,
        label + ": type/shadow enable differs");
    Require(
        actual.shapeX == expected.shapeX && actual.shapeY == expected.shapeY,
        label + ": supported shape dimensions differ");
    Require(
        actual.color == expected.color && actual.intensity == expected.intensity,
        label + ": color/intensity marker differs");
    Require(actual.transform == expected.transform, label + ": world transform differs");
    Require(
        actual.exposure == expected.exposure && actual.diffuse == expected.diffuse &&
            actual.specular == expected.specular && actual.radius == expected.radius,
        label + ": exposure/diffuse/specular/radius differs");
}

void RequireImmutablePage(
    const OwnedPage& page,
    const std::vector<uint8_t>& bytes,
    const char* when)
{
    Require(
        page.handle != nullptr && page.view.data != nullptr &&
            page.view.data_size == bytes.size() &&
            std::memcmp(page.view.data, bytes.data(), bytes.size()) == 0,
        std::string("Owned baseline page bytes changed ") + when);
}

// Decode only the uninstanced mesh identity/geometry needed by these cases.
// Attribute/deformation payloads remain opaque; this is not a renderer or a
// manufactured retained record. VisitCommands checks the complete page framing.
struct MeshGeometry
{
    std::array<double, 16> transform{};
    std::vector<std::array<float, 3>> points;
    std::vector<uint32_t> indices;
    std::vector<uint32_t> triangleSubprims;
};

std::string ReadPath(CommandView command, size_t offset, uint32_t count)
{
    Require(
        offset <= command.size && count <= command.size - offset,
        "Truncated native mesh path");
    std::string path(reinterpret_cast<const char*>(command.data + offset), count);
    Require(
        !path.empty() && path.front() == '/' && path.find('\0') == std::string::npos,
        "Native mesh command has no valid absolute path");
    return path;
}

void RequireArray(
    CommandView command,
    size_t offset,
    size_t count,
    size_t elementBytes)
{
    Require(
        offset <= command.size && count <= (command.size - offset) / elementBytes,
        "Truncated native mesh geometry array");
}

void ApplyMeshDeltas(
    const openusd_silk_page_view& view,
    std::map<std::string, MeshGeometry>& meshes)
{
    VisitCommands(view.data, view.data_size, view.command_count,
        [&](uint32_t type, CommandView command)
    {
        if (type == OPENUSD_SILK_COMMAND_MESH_REMOVE)
        {
            const uint32_t pathCount = ReadU32Le(command, 20);
            const std::string path = ReadPath(command, 24, pathCount);
            Require(
                ReadI32Le(command, 16) == 0 && size_t{24} + pathCount == command.size,
                "Unexpected instanced or trailing MESH_REMOVE data");
            meshes.erase(path);
        }
        else if (type == OPENUSD_SILK_COMMAND_MESH_UPSERT)
        {
            Require(command.size >= 268, "Truncated MESH_UPSERT header");
            Require(
                ReadI32Le(command, 16) >= 0 &&
                    ReadI32Le(command, 20) == 0 && ReadI32Le(command, 24) == 0 &&
                    ReadU32Le(command, 260) == 0u && ReadU32Le(command, 264) == 0u,
                "The uninstanced fixture acquired a different mesh identity");
            Require(
                ReadU32Le(command, 28) == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST,
                "The fixture mesh is not a triangle list");
            const uint32_t pathCount = ReadU32Le(command, 48);
            const uint32_t pointCount = ReadU32Le(command, 52);
            const uint32_t indexCount = ReadU32Le(command, 56);
            const uint32_t triangleCount = ReadU32Le(command, 60);
            const std::string path = ReadPath(command, 268, pathCount);
            Require(
                pointCount >= 3u && indexCount >= 3u && indexCount % 3u == 0u &&
                    triangleCount == indexCount / 3u,
                path + ": missing or inconsistent mesh geometry");

            MeshGeometry mesh;
            for (size_t element = 0; element < 16; ++element)
            {
                mesh.transform[element] = ReadF64Le(command, 80 + 8 * element);
            }
            size_t cursor = size_t{268} + pathCount;
            RequireArray(command, cursor, pointCount, 12);
            mesh.points.reserve(pointCount);
            for (size_t point = 0; point < pointCount; ++point)
            {
                mesh.points.push_back({
                    ReadF32Le(command, cursor),
                    ReadF32Le(command, cursor + 4),
                    ReadF32Le(command, cursor + 8)});
                cursor += 12;
            }
            RequireArray(command, cursor, indexCount, 4);
            mesh.indices.reserve(indexCount);
            for (size_t index = 0; index < indexCount; ++index)
            {
                const uint32_t vertex = ReadU32Le(command, cursor);
                Require(vertex < pointCount, path + ": index names a missing point");
                mesh.indices.push_back(vertex);
                cursor += 4;
            }
            RequireArray(command, cursor, triangleCount, 4);
            mesh.triangleSubprims.reserve(triangleCount);
            for (size_t triangle = 0; triangle < triangleCount; ++triangle)
            {
                mesh.triangleSubprims.push_back(ReadU32Le(command, cursor));
                cursor += 4;
            }
            meshes.insert_or_assign(path, std::move(mesh));
        }
    });
}

void RequireTriangle(
    const MeshGeometry& mesh,
    const std::array<openusd_vec3f, 3>& points,
    const openusd_matrix4d& transform,
    const char* label)
{
    Require(mesh.points.size() == 3, std::string(label) + ": expected three points");
    for (size_t point = 0; point < points.size(); ++point)
    {
        Require(
            mesh.points[point] == std::array<float, 3>{
                points[point].x, points[point].y, points[point].z},
            std::string(label) + ": point " + std::to_string(point) + " differs");
    }
    Require(
        mesh.indices == std::vector<uint32_t>{0u, 1u, 2u} &&
            mesh.triangleSubprims == std::vector<uint32_t>{0u},
        std::string(label) + ": topology/authored face identity differs");
    Require(
        std::equal(mesh.transform.begin(), mesh.transform.end(), transform.values),
        std::string(label) + ": transform differs");
}

void VerifyNativeInheritedVisibility(const char* pluginPath, const char* fixturePath)
{
    StageSessionOwner owned;
    ApiStatus api;
    api.RequireOk(
        openusd_stage_open(fixturePath, &owned.stage, api.Reset()), "Open minimal stage");
    api.RequireOk(
        openusd_stage_set_edit_target_session_layer(owned.stage, api.Reset()),
        "Select unsaved session layer");
    api.RequireOk(
        openusd_silk_session_create_from_stage(
            pluginPath, owned.stage, &owned.session, api.Reset()),
        "Create shared-stage session");
    {
        openusd_render_camera camera{};
        camera.struct_size = static_cast<uint32_t>(sizeof(camera));
        camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
        {
            OwnedPage empty;
            api.RequireOk(
                openusd_silk_session_sync(
                    owned.session, 96, 96, 0.0, &camera,
                    &empty.handle, &empty.view, api.Reset()),
                "Sync no-light fixture");
            const Frame frame = ReadNativeFrame(empty);
            Require(
                frame.directLightCount == 0u && frame.lightingFlags == 0u,
                "The fresh minimal fixture must have count0/flag0");
        }

        constexpr const char* parent = "/World/Hidden";
        constexpr const char* child = "/World/Hidden/Key";
        api.RequireOk(
            openusd_stage_define_prim(owned.stage, parent, "Xform", api.Reset()),
            "Define visibility ancestor");
        // The typed shape facade rejects radius on DistantLight. A SphereLight
        // lets this complete-entry check author a supported non-default radius;
        // the capacity case separately exercises the DistantLight adapter.
        api.RequireOk(
            openusd_lux_define(
                owned.stage, child, OPENUSD_LUX_SCHEMA_SPHERE_LIGHT, api.Reset()),
            "Define inherited child light");
        const openusd_matrix4d parentTransform{{
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            10.0, -20.0, 30.0, 1.0}};
        const openusd_matrix4d childTransform{{
            0.0, 2.0, 0.0, 0.0,
            -3.0, 0.0, 0.0, 0.0,
            0.0, 0.0, 4.0, 0.0,
            5.0, 6.0, 7.0, 1.0}};
        api.RequireOk(
            openusd_geom_xformable_set_local_transform(
                owned.stage, parent, &parentTransform, 0, 0.0, api.Reset()),
            "Author ancestor transform");
        api.RequireOk(
            openusd_geom_xformable_set_local_transform(
                owned.stage, child, &childTransform, 0, 0.0, api.Reset()),
            "Author child transform");
        api.RequireOk(
            openusd_lux_set_color(
                owned.stage, child, openusd_vec3f{0.25f, 0.5f, 0.75f}, api.Reset()),
            "Author child color");
        api.RequireOk(
            openusd_lux_set_float(
                owned.stage, child, OPENUSD_LUX_FLOAT_INTENSITY, 6.5f, api.Reset()),
            "Author child intensity");
        api.RequireOk(
            openusd_lux_set_float(
                owned.stage, child, OPENUSD_LUX_FLOAT_EXPOSURE, 1.25f, api.Reset()),
            "Author child exposure");
        api.RequireOk(
            openusd_lux_set_float(
                owned.stage, child, OPENUSD_LUX_FLOAT_DIFFUSE, 0.375f, api.Reset()),
            "Author child diffuse");
        api.RequireOk(
            openusd_lux_set_float(
                owned.stage, child, OPENUSD_LUX_FLOAT_SPECULAR, 0.625f, api.Reset()),
            "Author child specular");
        api.RequireOk(
            openusd_lux_set_shape(
                owned.stage, child, OPENUSD_LUX_SHAPE_RADIUS, 1.75f, api.Reset()),
            "Author child radius");
        api.RequireOk(
            openusd_stage_set_bool(
                owned.stage, child, "inputs:shadow:enable", 0, 0, 0.0, api.Reset()),
            "Author child shadow disable");

        const DirectSlot expected{
            OPENUSD_SILK_LIGHT_SPHERE, 0u, 0.0f, 0.0f,
            {0.25f, 0.5f, 0.75f}, 6.5f,
            {0.0, 2.0, 0.0, 0.0,
             -3.0, 0.0, 0.0, 0.0,
             0.0, 0.0, 4.0, 0.0,
             15.0, -14.0, 37.0, 1.0},
            1.25f, 0.375f, 0.625f, 1.75f};
        std::array<uint8_t, DirectBytes> visibleEntry{};
        const char* labels[]{"visible child", "hidden ancestor", "restored ancestor"};
        for (size_t step = 0; step < 3; ++step)
        {
            const bool hidden = step == 1;
            if (step != 0)
            {
                api.RequireOk(
                    openusd_geom_imageable_set_visibility(
                        owned.stage, parent,
                        hidden ? OPENUSD_GEOM_VISIBILITY_INVISIBLE
                               : OPENUSD_GEOM_VISIBILITY_INHERITED,
                        0, 0.0, api.Reset()),
                    std::string(labels[step]) + ": change only parent visibility");
            }
            // get_visibility computes ancestry; get_token reads the child's
            // own property. Never author a visibility opinion on the child.
            char childToken[32]{};
            size_t tokenBytes = 0;
            api.RequireOk(
                openusd_stage_get_token(
                    owned.stage, child, "visibility", 0, 0.0,
                    childToken, sizeof(childToken), &tokenBytes, api.Reset()),
                "Read child local visibility token");
            Require(
                std::string(childToken) == "inherited",
                "Ancestor changes must leave the child visibility inherited");
            int32_t computedVisibility = -1;
            api.RequireOk(
                openusd_geom_imageable_get_visibility(
                    owned.stage, child, 0, 0.0, &computedVisibility, api.Reset()),
                "Read child computed visibility");
            Require(
                computedVisibility == (hidden ? OPENUSD_GEOM_VISIBILITY_INVISIBLE
                                              : OPENUSD_GEOM_VISIBILITY_INHERITED),
                std::string(labels[step]) + ": USD ancestor visibility did not propagate");

            OwnedPage page;
            api.RequireOk(
                openusd_silk_session_sync(
                    owned.session, 96, 96, 0.0, &camera,
                    &page.handle, &page.view, api.Reset()),
                std::string("Sync ") + labels[step]);
            const Frame frame = ReadNativeFrame(page);
            Require(
                frame.directLightCount == (hidden ? 0u : 1u) && frame.lightingFlags == 1u,
                std::string(labels[step]) + ": native count/authored flag differs");
            if (!hidden)
            {
                RequireDirectSlot(frame.directLights[0], expected, labels[step]);
                if (step == 0)
                {
                    std::copy_n(page.view.data + DirectOffset, DirectBytes, visibleEntry.begin());
                }
                else
                {
                    Require(
                        std::memcmp(
                            page.view.data + DirectOffset,
                            visibleEntry.data(), visibleEntry.size()) == 0,
                        "Restoring only the parent changed the child's 176-byte entry");
                }
            }
        }
    }
    owned.Close();
}

void VerifyNativeCapacityRefusalAndRecovery(const char* pluginPath, const char* fixturePath)
{
    StageSessionOwner owned;
    ApiStatus api;
    api.RequireOk(
        openusd_stage_open(fixturePath, &owned.stage, api.Reset()), "Open fresh capacity stage");
    api.RequireOk(
        openusd_stage_set_edit_target_session_layer(owned.stage, api.Reset()),
        "Select capacity session layer");
    api.RequireOk(
        openusd_silk_session_create_from_stage(
            pluginPath, owned.stage, &owned.session, api.Reset()),
        "Create capacity shared-stage session");
    {
        constexpr const char* hiddenParent = "/World/Hidden";
        constexpr const char* extraPath = "/World/Hidden/Extra";
        api.RequireOk(
            openusd_stage_define_prim(owned.stage, "/World/Lights", "Xform", api.Reset()),
            "Define visible light group");
        api.RequireOk(
            openusd_stage_define_prim(owned.stage, hiddenParent, "Xform", api.Reset()),
            "Define overflow ancestor");
        api.RequireOk(
            openusd_geom_imageable_set_visibility(
                owned.stage, hiddenParent, OPENUSD_GEOM_VISIBILITY_INVISIBLE,
                0, 0.0, api.Reset()),
            "Hide only overflow ancestor");

        const openusd_lux_schema_kind schemas[]{
            OPENUSD_LUX_SCHEMA_DISTANT_LIGHT, OPENUSD_LUX_SCHEMA_SPHERE_LIGHT,
            OPENUSD_LUX_SCHEMA_RECT_LIGHT, OPENUSD_LUX_SCHEMA_DISK_LIGHT,
            OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT};
        const uint32_t wireTypes[]{
            OPENUSD_SILK_LIGHT_DISTANT, OPENUSD_SILK_LIGHT_SPHERE,
            OPENUSD_SILK_LIGHT_RECT, OPENUSD_SILK_LIGHT_DISK,
            OPENUSD_SILK_LIGHT_CYLINDER};
        std::array<DirectSlot, 128> expectedLights{};
        // The extra is authored first, then visible L127..L000 in reverse.
        // Independently identifiable controls must appear in ascending path
        // order on the wire, not insertion order or a type-grouped ordering.
        for (int index = 128; index >= 0; --index)
        {
            const std::string path = index == 128
                ? extraPath
                : "/World/Lights/L" + std::to_string(1000 + index).substr(1);
            const size_t kind = static_cast<size_t>(index % 5);
            const float marker = static_cast<float>(index);
            const double position = static_cast<double>(index);
            DirectSlot expected;
            expected.type = wireTypes[kind];
            expected.shadowEnabled = index == 125 ? 1u : 0u;
            expected.color = {
                0.125f + marker / 1024.0f,
                0.375f + marker / 2048.0f,
                0.625f + marker / 4096.0f};
            expected.intensity = 4.0f + marker;
            expected.exposure = 0.25f + marker / 1024.0f;
            expected.diffuse = 0.375f + marker / 2048.0f;
            expected.specular = 0.625f + marker / 4096.0f;
            // Distant/Rect have no radius property in the typed USD facade.
            // Their wire radius is light.cpp's explicit _ReadFloat fallback,
            // not a guessed USD schema default or an invented shape opinion.
            expected.radius = 0.5f;
            const openusd_matrix4d transform{{
                0.0, 1.5, 0.0, 0.0,
                -2.0, 0.0, 0.0, 0.0,
                0.0, 0.0, 0.75, 0.0,
                2.0 + position / 2.0, -3.0 + position / 4.0,
                4.0 + position / 8.0, 1.0}};
            std::copy_n(transform.values, 16, expected.transform.begin());

            api.RequireOk(
                openusd_lux_define(owned.stage, path.c_str(), schemas[kind], api.Reset()),
                path + ": define light");
            api.RequireOk(
                openusd_lux_set_color(
                    owned.stage, path.c_str(),
                    openusd_vec3f{expected.color[0], expected.color[1], expected.color[2]},
                    api.Reset()),
                path + ": color");
            api.RequireOk(
                openusd_lux_set_float(
                    owned.stage, path.c_str(), OPENUSD_LUX_FLOAT_INTENSITY,
                    expected.intensity, api.Reset()),
                path + ": intensity");
            api.RequireOk(
                openusd_lux_set_float(
                    owned.stage, path.c_str(), OPENUSD_LUX_FLOAT_EXPOSURE,
                    expected.exposure, api.Reset()),
                path + ": exposure");
            api.RequireOk(
                openusd_lux_set_float(
                    owned.stage, path.c_str(), OPENUSD_LUX_FLOAT_DIFFUSE,
                    expected.diffuse, api.Reset()),
                path + ": diffuse");
            api.RequireOk(
                openusd_lux_set_float(
                    owned.stage, path.c_str(), OPENUSD_LUX_FLOAT_SPECULAR,
                    expected.specular, api.Reset()),
                path + ": specular");
            api.RequireOk(
                openusd_stage_set_bool(
                    owned.stage, path.c_str(), "inputs:shadow:enable",
                    static_cast<int32_t>(expected.shadowEnabled), 0, 0.0, api.Reset()),
                path + ": shadow enable");
            api.RequireOk(
                openusd_geom_xformable_set_local_transform(
                    owned.stage, path.c_str(), &transform, 0, 0.0, api.Reset()),
                path + ": transform");
            if (expected.type == OPENUSD_SILK_LIGHT_SPHERE ||
                expected.type == OPENUSD_SILK_LIGHT_DISK ||
                expected.type == OPENUSD_SILK_LIGHT_CYLINDER)
            {
                expected.radius = 0.75f + marker / 128.0f;
                api.RequireOk(
                    openusd_lux_set_shape(
                        owned.stage, path.c_str(), OPENUSD_LUX_SHAPE_RADIUS,
                        expected.radius, api.Reset()),
                    path + ": supported radius");
            }
            if (expected.type == OPENUSD_SILK_LIGHT_RECT)
            {
                expected.shapeX = 2.5f + marker / 16.0f;
                expected.shapeY = 1.25f + marker / 32.0f;
                api.RequireOk(
                    openusd_lux_set_shape(
                        owned.stage, path.c_str(), OPENUSD_LUX_SHAPE_WIDTH,
                        expected.shapeX, api.Reset()),
                    path + ": rect width");
                api.RequireOk(
                    openusd_lux_set_shape(
                        owned.stage, path.c_str(), OPENUSD_LUX_SHAPE_HEIGHT,
                        expected.shapeY, api.Reset()),
                    path + ": rect height");
            }
            if (expected.type == OPENUSD_SILK_LIGHT_CYLINDER)
            {
                expected.shapeX = 3.5f + marker / 16.0f;
                api.RequireOk(
                    openusd_lux_set_shape(
                        owned.stage, path.c_str(), OPENUSD_LUX_SHAPE_LENGTH,
                        expected.shapeX, api.Reset()),
                    path + ": cylinder length");
            }
            if (index < 128)
            {
                expectedLights[static_cast<size_t>(index)] = expected;
            }
        }

        constexpr const char* keepPath = "/World/QueuedKeep";
        constexpr const char* gonePath = "/World/QueuedGone";
        constexpr const char* newPath = "/World/QueuedNew";
        const std::array<openusd_vec3f, 3> keepPoints{{
            {2.0f, 0.0f, 1.0f}, {4.0f, 0.0f, 1.0f}, {2.0f, 3.0f, 1.0f}}};
        const std::array<openusd_vec3f, 3> gonePoints{{
            {-4.0f, -2.0f, 0.0f}, {-2.0f, -2.0f, 0.0f}, {-4.0f, 1.0f, 0.0f}}};
        const openusd_matrix4d keepTransform{{
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            3.0, -2.0, 1.0, 1.0}};
        const openusd_matrix4d goneTransform{{
            1.5, 0.0, 0.0, 0.0,
            0.0, 2.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            -1.0, 2.0, -3.0, 1.0}};
        const int32_t faceCounts[]{3};
        const int32_t indices[]{0, 1, 2};
        const char* meshPaths[]{keepPath, gonePath};
        const std::array<openusd_vec3f, 3>* meshPoints[]{&keepPoints, &gonePoints};
        const openusd_matrix4d* meshTransforms[]{&keepTransform, &goneTransform};
        for (size_t mesh = 0; mesh < 2; ++mesh)
        {
            const std::string label = meshPaths[mesh];
            api.RequireOk(
                openusd_geom_define_mesh(owned.stage, meshPaths[mesh], api.Reset()),
                label + ": define session-owned mesh");
            api.RequireOk(
                openusd_geom_mesh_set_points(
                    owned.stage, meshPaths[mesh], meshPoints[mesh]->data(),
                    meshPoints[mesh]->size(), 0, 0.0, api.Reset()),
                label + ": points");
            api.RequireOk(
                openusd_geom_mesh_set_topology(
                    owned.stage, meshPaths[mesh], faceCounts, 1, indices, 3, api.Reset()),
                label + ": topology");
            api.RequireOk(
                openusd_geom_xformable_set_local_transform(
                    owned.stage, meshPaths[mesh], meshTransforms[mesh], 0, 0.0, api.Reset()),
                label + ": transform");
        }

        openusd_render_camera camera{};
        camera.struct_size = static_cast<uint32_t>(sizeof(camera));
        camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
        OwnedPage baseline;
        api.RequireOk(
            openusd_silk_session_sync(
                owned.session, 96, 96, 0.0, &camera,
                &baseline.handle, &baseline.view, api.Reset()),
            "Sync exactly128 with inherited-hidden extra");
        const Frame baselineFrame = ReadNativeFrame(baseline);
        Require(
            baselineFrame.directLightCount == 128u && baselineFrame.lightingFlags == 1u,
            "Baseline must admit all128 visible lights and omit the hidden extra");
        for (size_t slot = 0; slot < expectedLights.size(); ++slot)
        {
            RequireDirectSlot(
                baselineFrame.directLights[slot], expectedLights[slot],
                "Baseline sorted direct slot " + std::to_string(slot));
        }
        const uint64_t baselineRevision = baseline.view.revision;
        const std::vector<uint8_t> baselineBytes(
            baseline.view.data, baseline.view.data + baseline.view.data_size);
        std::map<std::string, MeshGeometry> currentMeshes;
        ApplyMeshDeltas(baseline.view, currentMeshes);
        Require(
            currentMeshes.size() == 3 && currentMeshes.count("/World/Cube") == 1 &&
                currentMeshes.count(keepPath) == 1 && currentMeshes.count(gonePath) == 1,
            "Baseline must contain the root cube and both session-owned meshes");
        RequireTriangle(currentMeshes.at(keepPath), keepPoints, keepTransform, "Baseline Keep");
        RequireTriangle(currentMeshes.at(gonePath), gonePoints, goneTransform, "Baseline Gone");
        const MeshGeometry rootBaseline = currentMeshes.at("/World/Cube");
        const std::set<std::array<float, 3>> rootCorners(
            rootBaseline.points.begin(), rootBaseline.points.end());
        const std::set<std::array<float, 3>> expectedCorners{
            {-1.0f, -1.0f, -1.0f}, {-1.0f, -1.0f, 1.0f},
            {-1.0f, 1.0f, -1.0f}, {-1.0f, 1.0f, 1.0f},
            {1.0f, -1.0f, -1.0f}, {1.0f, -1.0f, 1.0f},
            {1.0f, 1.0f, -1.0f}, {1.0f, 1.0f, 1.0f}};
        Require(
            rootCorners == expectedCorners && rootBaseline.indices.size() == 36 &&
                rootBaseline.triangleSubprims.size() == 12 && rootBaseline.transform == Identity,
            "The immutable root fixture must remain the size2 cube");

        const std::array<openusd_vec3f, 3> editedKeepPoints{{
            {1.0f, -1.0f, 2.0f}, {5.0f, -1.0f, 2.0f}, {1.0f, 4.0f, 3.0f}}};
        const openusd_matrix4d editedKeepTransform{{
            0.0, 2.0, 0.0, 0.0,
            -1.0, 0.0, 0.0, 0.0,
            0.0, 0.0, 1.5, 0.0,
            -4.0, 5.0, -6.0, 1.0}};
        const std::array<openusd_vec3f, 3> newPoints{{
            {-2.0f, 1.0f, -1.0f}, {1.0f, 1.0f, -1.0f}, {-2.0f, 5.0f, -1.0f}}};
        const openusd_matrix4d newTransform{{
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.25, 0.0, 0.0,
            0.0, 0.0, 1.5, 0.0,
            7.0, -3.0, 2.0, 1.0}};
        api.RequireOk(
            openusd_geom_mesh_set_points(
                owned.stage, keepPath, editedKeepPoints.data(),
                editedKeepPoints.size(), 0, 0.0, api.Reset()),
            "Queue changed Keep points");
        api.RequireOk(
            openusd_geom_xformable_set_local_transform(
                owned.stage, keepPath, &editedKeepTransform, 0, 0.0, api.Reset()),
            "Queue changed Keep transform");
        api.RequireOk(
            openusd_stage_remove_prim(owned.stage, gonePath, api.Reset()),
            "Remove session-owned Gone (not root-layer Cube)");
        api.RequireOk(
            openusd_geom_define_mesh(owned.stage, newPath, api.Reset()), "Queue New mesh");
        api.RequireOk(
            openusd_geom_mesh_set_points(
                owned.stage, newPath, newPoints.data(), newPoints.size(), 0, 0.0, api.Reset()),
            "Queue New points");
        api.RequireOk(
            openusd_geom_mesh_set_topology(
                owned.stage, newPath, faceCounts, 1, indices, 3, api.Reset()),
            "Queue New topology");
        api.RequireOk(
            openusd_geom_xformable_set_local_transform(
                owned.stage, newPath, &newTransform, 0, 0.0, api.Reset()),
            "Queue New transform");
        api.RequireOk(
            openusd_geom_imageable_set_visibility(
                owned.stage, hiddenParent, OPENUSD_GEOM_VISIBILITY_INHERITED,
                0, 0.0, api.Reset()),
            "Unhide extra's ancestor to produce actual129");
        int32_t extraVisibility = -1;
        api.RequireOk(
            openusd_geom_imageable_get_visibility(
                owned.stage, extraPath, 0, 0.0, &extraVisibility, api.Reset()),
            "Read extra's computed visibility before refusal");
        Require(
            extraVisibility == OPENUSD_GEOM_VISIBILITY_INHERITED,
            "The otherwise-effective extra light must really be visible at refusal");

        const auto sentinel = reinterpret_cast<openusd_silk_page*>(uintptr_t{1});
        openusd_silk_page* failurePage = sentinel;
        openusd_silk_page_view failureView{};
        failureView.struct_size = static_cast<uint32_t>(sizeof(failureView));
        ApiStatus failureError;
        const openusd_status failureStatus = openusd_silk_session_sync(
            owned.session, 96, 96, 0.0, &camera,
            &failurePage, &failureView, failureError.Reset());
        OwnedPage unexpectedPage;
        // If a regression publishes a real page on failure, own it before any
        // assertion can throw. Never adopt/release the sentinel or baseline.
        if (failurePage != sentinel && failurePage != baseline.handle)
        {
            unexpectedPage.handle = failurePage;
        }
        Require(
            failureStatus == OPENUSD_STATUS_NATIVE_ERROR,
            "Actual129 must return NATIVE_ERROR(4), got " +
                std::to_string(failureStatus) + ": " + failureError.text);
        Require(failurePage == nullptr, "Actual129 must null the separate sentinel output");
        const std::string diagnostic = failureError.text;
        Require(
            diagnostic.find("129 effective direct lights") != std::string::npos &&
                diagnostic.find("bounded limit of 128") != std::string::npos &&
                diagnostic.find("no command page was published") != std::string::npos,
            "Actual129 refusal lost count/bound/publication diagnostic: " + diagnostic);
        // failureView has no successful publication contract. Do not decode it
        // or assert that the API zeroed it.
        RequireImmutablePage(baseline, baselineBytes, "after actual129 refusal");

        // This is the only authoring after refusal. Do not reapply Keep's edit,
        // Gone's removal, New's creation, or any light controls before retry.
        api.RequireOk(
            openusd_geom_imageable_set_visibility(
                owned.stage, hiddenParent, OPENUSD_GEOM_VISIBILITY_INVISIBLE,
                0, 0.0, api.Reset()),
            "Rehide only extra's ancestor for recovery");
        OwnedPage retry;
        const openusd_status retryStatus = openusd_silk_session_sync(
            owned.session, 96, 96, 0.0, &camera,
            &retry.handle, &retry.view, api.Reset());
        const bool retryAliasesBaseline = retry.handle == baseline.handle;
        if (retryAliasesBaseline)
        {
            retry.handle = nullptr;
        }
        api.RequireOk(retryStatus, "Retry after actual capacity refusal");
        Require(!retryAliasesBaseline, "Retry reused the still-owned baseline page handle");
        const Frame retryFrame = ReadNativeFrame(retry);
        Require(
            retry.view.revision == baselineRevision + 1,
            "Failed sync must not consume a page revision");
        Require(
            retryFrame.directLightCount == 128u && retryFrame.lightingFlags == 1u,
            "Retry must restore all128 current visible lights");
        for (size_t slot = 0; slot < expectedLights.size(); ++slot)
        {
            RequireDirectSlot(
                retryFrame.directLights[slot], expectedLights[slot],
                "Recovered sorted direct slot " + std::to_string(slot));
        }

        // Imaging recovery can republish the whole current scene. Apply its
        // actual deltas to the consumer's baseline, rather than clearing this
        // map or demanding the retained-state probe's seven-command sequence.
        ApplyMeshDeltas(retry.view, currentMeshes);
        Require(
            currentMeshes.size() == 3 && currentMeshes.count(gonePath) == 0 &&
                currentMeshes.count(keepPath) == 1 && currentMeshes.count(newPath) == 1 &&
                currentMeshes.count("/World/Cube") == 1,
            "Recovery must remove Gone, preserve Keep/Cube, and add New");
        RequireTriangle(
            currentMeshes.at(keepPath), editedKeepPoints, editedKeepTransform, "Recovered Keep");
        RequireTriangle(currentMeshes.at(newPath), newPoints, newTransform, "Recovered New");
        const MeshGeometry& recoveredRoot = currentMeshes.at("/World/Cube");
        Require(
            recoveredRoot.points == rootBaseline.points &&
                recoveredRoot.indices == rootBaseline.indices &&
                recoveredRoot.triangleSubprims == rootBaseline.triangleSubprims &&
                recoveredRoot.transform == rootBaseline.transform,
            "Recovery changed the immutable root-layer fixture geometry");
        RequireImmutablePage(baseline, baselineBytes, "after successful recovery");

        OwnedPage steady;
        const openusd_status steadyStatus = openusd_silk_session_sync(
            owned.session, 96, 96, 0.0, &camera,
            &steady.handle, &steady.view, api.Reset());
        const bool steadyAliasesOwnedPage =
            steady.handle == baseline.handle || steady.handle == retry.handle;
        if (steadyAliasesOwnedPage)
        {
            steady.handle = nullptr;
        }
        api.RequireOk(steadyStatus, "Unchanged sync after recovery");
        Require(!steadyAliasesOwnedPage, "Steady sync reused a still-owned page handle");
        const Frame steadyFrame = ReadNativeFrame(steady);
        Require(
            steady.view.revision == baselineRevision + 2 &&
                steadyFrame.directLightCount == 128u && steadyFrame.lightingFlags == 1u,
            "Steady sync lost revision continuity or the current direct-light set");
        Require(
            steady.view.command_count == 1u && steady.view.data_size == FrameBytes,
            "Steady sync repeated scene or light-link/shadow table deltas");
        Require(
            std::memcmp(steady.view.data, retry.view.data, FrameBytes) == 0,
            "Steady sync changed the recovered full FRAME");
    }
    owned.Close();
}
}

int main(int argc, char** argv)
{
    if (argc != 3 || argv[1][0] == '\0' || argv[2][0] == '\0')
    {
        std::fprintf(
            stderr,
            "Usage: hdsilk_direct_lights_session_probe <staged-plugin-dir> <minimal.usda>\n");
        return 64;
    }
    struct Case
    {
        const char* name;
        void (*run)(const char*, const char*);
        int failureCode;
    };
    const Case cases[]{
        {"VerifyNativeInheritedVisibility", VerifyNativeInheritedVisibility, 1},
        {"VerifyNativeCapacityRefusalAndRecovery", VerifyNativeCapacityRefusalAndRecovery, 2}};
    int result = 0;
    for (const Case& test : cases)
    {
        try
        {
            test.run(argv[1], argv[2]);
            std::printf("%s: passed\n", test.name);
        }
        catch (const std::exception& error)
        {
            std::fprintf(stderr, "%s: %s\n", test.name, error.what());
            if (result == 0)
            {
                result = test.failureCode;
            }
        }
        catch (...)
        {
            std::fprintf(stderr, "%s: unknown native exception\n", test.name);
            if (result == 0)
            {
                result = test.failureCode;
            }
        }
    }
    return result;
}
