// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "openusd_dotnet.h"
#include "openusd_render_camera.h"

#include <cstdint>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <cmath>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace
{
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 6u);
static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 24u);
static_assert(sizeof(openusd_silk_scene_ingestion_request) == 32u);
static_assert(offsetof(openusd_silk_scene_ingestion_request, material_binding_purpose) == 24u);

constexpr uint32_t RequestVersion = OPENUSD_SILK_SCENE_INGESTION_VERSION;
constexpr uint32_t LegacyMask =
    OPENUSD_GEOM_PURPOSE_MASK_DEFAULT |
    OPENUSD_GEOM_PURPOSE_MASK_PROXY |
    OPENUSD_GEOM_PURPOSE_MASK_RENDER;
constexpr uint32_t AllMask = LegacyMask | OPENUSD_GEOM_PURPOSE_MASK_GUIDE;

struct PageStats
{
    uint32_t meshUpserts = 0;
    uint32_t meshRemovals = 0;
    uint32_t materialUpserts = 0;
    uint32_t materialRemovals = 0;
    std::string boundMaterialPath;
    std::unordered_map<std::string, uint32_t> meshUpsertsByPath;
    std::unordered_map<std::string, std::vector<uint32_t>> instanceIndicesByInstancer;
    std::unordered_map<std::string, float> firstPointXByPath;
};

void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

void SetEnvironment(const char* name, const char* value)
{
#if defined(_WIN32)
    _putenv_s(name, value);
#else
    if (value[0] == '\0')
    {
        unsetenv(name);
    }
    else
    {
        setenv(name, value, 1);
    }
#endif
}

std::string ReadError(const openusd_error_buffer& error)
{
    return error.data == nullptr ? std::string() : std::string(error.data);
}

openusd_render_camera MakeCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(openusd_render_camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    const double view[16] = {
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, -5, 1};
    const double projection[16] = {
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, -0.2, 0,
        0, 0, -1.2, 1};
    std::memcpy(camera.view, view, sizeof(view));
    std::memcpy(camera.projection, projection, sizeof(projection));
    return camera;
}

std::filesystem::path WriteStage()
{
#if defined(_WIN32)
    char* rootText = nullptr;
    size_t rootTextBytes = 0;
    if (_dupenv_s(&rootText, &rootTextBytes, "OPENUSD_TEST_WORK_ROOT") != 0)
    {
        rootText = nullptr;
        rootTextBytes = 0;
    }
    std::filesystem::path root =
        rootText == nullptr || rootText[0] == '\0'
            ? std::filesystem::temp_directory_path() / "hdsilk-scene-ingestion-probe"
            : std::filesystem::path(rootText) / "hdsilk-scene-ingestion-probe";
    std::free(rootText);
#else
    const char* rootText = std::getenv("OPENUSD_TEST_WORK_ROOT");
    const std::filesystem::path root =
        rootText == nullptr || rootText[0] == '\0'
            ? std::filesystem::temp_directory_path() / "hdsilk-scene-ingestion-probe"
            : std::filesystem::path(rootText) / "hdsilk-scene-ingestion-probe";
#endif
    std::filesystem::create_directories(root);
    const std::filesystem::path path = root / "scene.usda";
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output << R"usda(#usda 1.0
(
    defaultPrim = "World"
    renderSettingsPrimPath = "/Settings"
    metersPerUnit = 1
    upAxis = "Y"
)
def Xform "World"
{
    def Camera "Camera"
    {
        token projection = "orthographic"
        float horizontalAperture = 20
        float verticalAperture = 20
        float focalLength = 10
        float2 clippingRange = (1, 11)
        double3 xformOp:translate = (0, 0, 5)
        uniform token[] xformOpOrder = ["xformOp:translate"]
    }

    def Mesh "DefaultQuad" (
        prepend apiSchemas = ["MaterialBindingAPI"]
    )
    {
        uniform token subdivisionScheme = "none"
        uniform bool doubleSided = 1
        point3f[] points = [(-0.8, 0.2, 0), (-0.2, 0.2, 0), (-0.2, 0.8, 0), (-0.8, 0.8, 0)]
        int[] faceVertexCounts = [4]
        int[] faceVertexIndices = [0, 1, 2, 3]
        rel material:binding = </World/AllPurposeMaterial>
        rel material:binding:full = </World/FullMaterial>
        rel material:binding:preview = </World/PreviewMaterial>
        rel material:binding:customLook = </World/CustomMaterial>
    }

    def Xform "ProxyParent"
    {
        uniform token purpose = "proxy"
        def Mesh "InheritedProxyQuad" (
            prepend apiSchemas = ["MaterialBindingAPI"]
        )
        {
            uniform token subdivisionScheme = "none"
            uniform bool doubleSided = 1
            point3f[] points = [(-0.8, -0.8, 0), (-0.2, -0.8, 0), (-0.2, -0.2, 0), (-0.8, -0.2, 0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0, 1, 2, 3]
            rel material:binding = </World/ProxyMaterial>
        }
    }

    def Mesh "RenderQuad" (
        prepend apiSchemas = ["MaterialBindingAPI"]
    )
    {
        uniform token purpose = "render"
        uniform token subdivisionScheme = "none"
        uniform bool doubleSided = 1
        point3f[] points.timeSamples = {
            0: [(0.2, 0.2, 0), (0.8, 0.2, 0), (0.8, 0.8, 0), (0.2, 0.8, 0)],
            1: [(0.35, 0.2, 0), (0.95, 0.2, 0), (0.95, 0.8, 0), (0.35, 0.8, 0)]
        }
        int[] faceVertexCounts = [4]
        int[] faceVertexIndices = [0, 1, 2, 3]
        rel material:binding = </World/RenderMaterial>
    }

    def Scope "InstancerPrototypes"
    {
        def Mesh "InstancedRenderQuad" (
            prepend apiSchemas = ["MaterialBindingAPI"]
        )
        {
            uniform token purpose = "guide"
            uniform token subdivisionScheme = "none"
            uniform bool doubleSided = 1
            point3f[] points = [(-0.1, -0.1, 0), (0.1, -0.1, 0), (0.1, 0.1, 0), (-0.1, 0.1, 0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0, 1, 2, 3]
            rel material:binding = </World/RenderMaterial>
        }
    }

    def PointInstancer "RenderInstancer"
    {
        uniform token purpose = "guide"
        rel prototypes = [</World/InstancerPrototypes/InstancedRenderQuad>]
        int[] protoIndices = [0]
        point3f[] positions = [(0.55, -0.5, 0)]
        quatf[] orientations = [(1, 0, 0, 0)]
        float3[] scales = [(1, 1, 1)]
    }

    def Mesh "GuideQuad" (
        prepend apiSchemas = ["MaterialBindingAPI"]
    )
    {
        uniform token purpose = "guide"
        uniform token subdivisionScheme = "none"
        uniform bool doubleSided = 1
        point3f[] points = [(0.2, -0.8, 0), (0.8, -0.8, 0), (0.8, -0.2, 0), (0.2, -0.2, 0)]
        int[] faceVertexCounts = [4]
        int[] faceVertexIndices = [0, 1, 2, 3]
        rel material:binding = </World/GuideMaterial>
    }

    def Material "AllPurposeMaterial"
    {
        token outputs:surface.connect = </World/AllPurposeMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (1, 0, 0)
            float inputs:opacity = 1
            token outputs:surface
        }
    }

    def Material "FullMaterial"
    {
        token outputs:surface.connect = </World/FullMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (0, 1, 0)
            float inputs:opacity = 1
            token outputs:surface
        }
    }

    def Material "PreviewMaterial"
    {
        token outputs:surface.connect = </World/PreviewMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (0, 0, 1)
            float inputs:opacity = 1
            token outputs:surface
        }
    }

    def Material "CustomMaterial"
    {
        token outputs:surface.connect = </World/CustomMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (1, 1, 0)
            float inputs:opacity = 1
            token outputs:surface
        }
    }

    def Material "ProxyMaterial"
    {
        token outputs:surface.connect = </World/ProxyMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (0, 1, 1)
            float inputs:opacity = 1
            token outputs:surface
        }
    }

    def Material "RenderMaterial"
    {
        token outputs:surface.connect = </World/RenderMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (1, 0, 1)
            float inputs:opacity = 1
            token outputs:surface
        }
    }

    def Material "GuideMaterial"
    {
        token outputs:surface.connect = </World/GuideMaterial/Surface.outputs:surface>
        def Shader "Surface"
        {
            uniform token info:id = "UsdPreviewSurface"
            color3f inputs:diffuseColor = (0, 0, 0)
            color3f inputs:emissiveColor = (1, 1, 1)
            float inputs:opacity = 1
            token outputs:surface
        }
    }
}
def RenderSettings "Settings"
{
    rel camera = </World/Camera>
    rel products = </Product>
    int2 resolution = (96, 96)
}
def RenderProduct "Product"
{
    token productName = "never-created.exr"
    rel orderedVars = </Color>
}
def RenderVar "Color"
{
    token dataType = "color3f"
    string sourceName = "color"
    token sourceType = "raw"
}
)usda";
    Require(static_cast<bool>(output), "Could not write the scene-ingestion probe stage.");
    return path;
}

uint32_t ReadU32(const uint8_t* bytes)
{
    return static_cast<uint32_t>(bytes[0]) |
        (static_cast<uint32_t>(bytes[1]) << 8) |
        (static_cast<uint32_t>(bytes[2]) << 16) |
        (static_cast<uint32_t>(bytes[3]) << 24);
}

float ReadF32(const uint8_t* bytes)
{
    float value = 0.0f;
    std::memcpy(&value, bytes, sizeof(value));
    return value;
}

PageStats ReadPage(const openusd_silk_page_view& view)
{
    PageStats result;
    const uint8_t* data = view.data;
    size_t remaining = view.data_size;
    while (remaining >= 8)
    {
        const uint32_t type = ReadU32(data);
        const uint32_t size = ReadU32(data + 4);
        Require(size >= 8 && size <= remaining, "The page command size is invalid.");
        if (type == OPENUSD_SILK_COMMAND_MESH_UPSERT)
        {
            Require(size >= 268, "A mesh record is shorter than its fixed header.");
            ++result.meshUpserts;
            const uint32_t pathBytes = ReadU32(data + 48);
            Require(pathBytes <= size - 268, "A mesh path exceeds its record.");
            const std::string path(reinterpret_cast<const char*>(data + 268), pathBytes);
            ++result.meshUpsertsByPath[path];
            const uint32_t pointCount = ReadU32(data + 52);
            if (pointCount != 0)
            {
                result.firstPointXByPath[path] = ReadF32(data + 268u + pathBytes);
            }
            const uint32_t indexCount = ReadU32(data + 56);
            const uint32_t triangleCount = ReadU32(data + 60);
            const uint32_t materialBytes = ReadU32(data + 216);
            size_t offset = 268u + pathBytes;
            offset += static_cast<size_t>(pointCount) * 3u * sizeof(float);
            offset += static_cast<size_t>(indexCount) * sizeof(uint32_t);
            offset += static_cast<size_t>(triangleCount) * sizeof(uint32_t);
            Require(offset <= size && materialBytes <= size - offset,
                "A mesh payload exceeds its record.");
            if (path == "/World/DefaultQuad")
            {
                result.boundMaterialPath.assign(
                    reinterpret_cast<const char*>(data + offset),
                    materialBytes);
            }
            offset += materialBytes;
            for (uint32_t attribute = 0; attribute < ReadU32(data + 220); ++attribute)
            {
                Require(offset <= size && size - offset >= 20,
                    "A vertex attribute header exceeds its record.");
                const size_t elements = static_cast<size_t>(ReadU32(data + offset + 4)) *
                    ReadU32(data + offset + 16);
                offset += 20u + ReadU32(data + offset + 12) + elements * sizeof(float);
            }
            offset += ReadU32(data + 232);
            offset += static_cast<size_t>(ReadU32(data + 244)) * sizeof(uint32_t);
            offset += static_cast<size_t>(ReadU32(data + 248)) * sizeof(uint32_t);
            const uint32_t instancerBytes = ReadU32(data + 260);
            Require(offset <= size && instancerBytes <= size - offset,
                "An instancer path exceeds its record.");
            if (instancerBytes != 0)
            {
                const std::string instancer(
                    reinterpret_cast<const char*>(data + offset), instancerBytes);
                result.instanceIndicesByInstancer[instancer].push_back(ReadU32(data + 24));
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MESH_REMOVE)
        {
            ++result.meshRemovals;
        }
        else if (type == OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            ++result.materialUpserts;
        }
        else if (type == OPENUSD_SILK_COMMAND_MATERIAL_REMOVE)
        {
            ++result.materialRemovals;
        }
        data += size;
        remaining -= size;
    }
    return result;
}

PageStats Sync(
    openusd_silk_session* session,
    const openusd_render_camera& camera,
    uint32_t mask,
    const char* materialPurpose,
    double timeCode = 0.0)
{
    openusd_silk_page* page = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    const openusd_silk_scene_ingestion_request request{
        sizeof(openusd_silk_scene_ingestion_request),
        RequestVersion,
        mask,
        OPENUSD_SILK_COMPLEXITY_LOW,
        OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED,
        materialPurpose};
    const openusd_status status = openusd_silk_session_sync_with_scene_ingestion(
        session,
        96,
        96,
        timeCode,
        &camera,
        &request,
        &page,
        &view,
        &error);
    if (status != OPENUSD_STATUS_OK)
    {
        throw std::runtime_error(ReadError(error));
    }

    PageStats result = ReadPage(view);
    openusd_silk_page_release(page);
    return result;
}

PageStats SyncLegacy(
    openusd_silk_session* session,
    const openusd_render_camera& camera,
    double timeCode = 0.0)
{
    openusd_silk_page* page = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    const openusd_status status = openusd_silk_session_sync(
        session,
        96,
        96,
        timeCode,
        &camera,
        &page,
        &view,
        &error);
    if (status != OPENUSD_STATUS_OK)
    {
        throw std::runtime_error(ReadError(error));
    }

    PageStats result = ReadPage(view);
    openusd_silk_page_release(page);
    return result;
}

void ExpectExplicitSyncFailure(
    openusd_silk_session* session,
    const openusd_render_camera& camera,
    uint32_t mask,
    const char* materialPurpose,
    const char* expectedText)
{
    openusd_silk_page* page = reinterpret_cast<openusd_silk_page*>(1);
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    const openusd_silk_scene_ingestion_request request{
        sizeof(openusd_silk_scene_ingestion_request),
        RequestVersion,
        mask,
        OPENUSD_SILK_COMPLEXITY_LOW,
        OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED,
        materialPurpose};
    const openusd_status status = openusd_silk_session_sync_with_scene_ingestion(
        session,
        96,
        96,
        0.0,
        &camera,
        &request,
        &page,
        &view,
        &error);
    Require(status == OPENUSD_STATUS_INVALID_ARGUMENT, "The explicit scene-ingestion request should have been rejected.");
    Require(page == nullptr, "The explicit scene-ingestion failure returned a page.");
    Require(
        std::string(errorBytes).find(expectedText) != std::string::npos,
        "The explicit scene-ingestion failure did not report the expected diagnostic.");
}

struct EditableSession
{
    openusd_stage* stage = nullptr;
    openusd_silk_session* session = nullptr;
};

void RequireStatus(
    openusd_status status,
    const openusd_error_buffer& error,
    const char* message)
{
    if (status != OPENUSD_STATUS_OK)
    {
        throw std::runtime_error(
            std::string(message) + ": " + ReadError(error));
    }
}

EditableSession CreateEditableSession(const char* pluginPath)
{
    const std::filesystem::path scene = WriteStage();
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    EditableSession result;
    RequireStatus(
        openusd_stage_open(scene.string().c_str(), &result.stage, &error),
        error,
        "Could not open the editable scene-ingestion probe stage");
    RequireStatus(
        openusd_stage_set_edit_target_session_layer(result.stage, &error),
        error,
        "Could not switch the probe stage to its session layer");
    RequireStatus(
        openusd_silk_session_create_from_stage(
            pluginPath,
            result.stage,
            &result.session,
            &error),
        error,
        "Could not create the editable hdSilk session");
    return result;
}

void DestroyEditableSession(EditableSession* session)
{
    if (session == nullptr)
    {
        return;
    }

    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    if (session->session != nullptr)
    {
        RequireStatus(
            openusd_silk_session_destroy(session->session, &error),
            error,
            "Could not destroy the editable hdSilk session");
        session->session = nullptr;
    }
    if (session->stage != nullptr)
    {
        openusd_stage_release(session->stage);
        session->stage = nullptr;
    }
}

void SetRenderQuadPoints(
    const openusd_stage* stage,
    float left,
    float right)
{
    const openusd_vec3f points[] = {
        {left, 0.2f, 0.0f},
        {right, 0.2f, 0.0f},
        {right, 0.8f, 0.0f},
        {left, 0.8f, 0.0f}};
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    RequireStatus(
        openusd_geom_mesh_set_points(
            stage,
            "/World/RenderQuad",
            points,
            4,
            0,
            0.0,
            &error),
        error,
        "Could not update the hidden render quad points");
}

void RedefineRenderQuad(const openusd_stage* stage, float left, float right)
{
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    RequireStatus(
        openusd_stage_set_edit_target_root_layer(stage, &error),
        error,
        "Could not switch the probe stage to its root layer");
    RequireStatus(
        openusd_stage_remove_prim(stage, "/World/RenderQuad", &error),
        error,
        "Could not remove the hidden render quad");
    RequireStatus(
        openusd_geom_define_mesh(stage, "/World/RenderQuad", &error),
        error,
        "Could not redefine the hidden render quad");
    RequireStatus(
        openusd_geom_imageable_set_purpose(
            stage,
            "/World/RenderQuad",
            OPENUSD_GEOM_PURPOSE_RENDER,
            &error),
        error,
        "Could not restore the render purpose on the hidden render quad");
    RequireStatus(
        openusd_geom_mesh_set_double_sided(
            stage,
            "/World/RenderQuad",
            1,
            &error),
        error,
        "Could not restore the double-sided flag on the hidden render quad");
    const int32_t faceCounts[] = {4};
    const int32_t faceIndices[] = {0, 1, 2, 3};
    RequireStatus(
        openusd_geom_mesh_set_topology(
            stage,
            "/World/RenderQuad",
            faceCounts,
            1,
            faceIndices,
            4,
            &error),
        error,
        "Could not restore the hidden render quad topology");
    SetRenderQuadPoints(stage, left, right);
    RequireStatus(
        openusd_stage_set_edit_target_session_layer(stage, &error),
        error,
        "Could not switch the probe stage back to its session layer");
}

void SetInstancerInstanceCount(const openusd_stage* stage, size_t count)
{
    std::vector<int32_t> protoIndices(count, 0);
    std::vector<openusd_vec3f> positions(count);
    std::vector<openusd_quatf> orientations(count);
    std::vector<openusd_vec3f> scales(count);
    for (size_t index = 0; index < count; ++index)
    {
        positions[index] = openusd_vec3f{
            0.35f + static_cast<float>(index) * 0.3f,
            -0.5f,
            0.0f};
        orientations[index] = openusd_quatf{1.0f, 0.0f, 0.0f, 0.0f};
        scales[index] = openusd_vec3f{1.0f, 1.0f, 1.0f};
    }

    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    RequireStatus(
        openusd_stage_set_int32_array(
            stage,
            "/World/RenderInstancer",
            "protoIndices",
            protoIndices.data(),
            protoIndices.size(),
            0,
            0.0,
            &error),
        error,
        "Could not update the hidden render instancer protoIndices");
    RequireStatus(
        openusd_stage_set_vec3f_array(
            stage,
            "/World/RenderInstancer",
            "positions",
            positions.data(),
            positions.size(),
            0,
            0.0,
            &error),
        error,
        "Could not update the hidden render instancer positions");
    RequireStatus(
        openusd_geom_point_instancer_set_orientations(
            stage,
            "/World/RenderInstancer",
            orientations.data(),
            orientations.size(),
            0,
            0.0,
            &error),
        error,
        "Could not update the hidden render instancer orientations");
    RequireStatus(
        openusd_stage_set_vec3f_array(
            stage,
            "/World/RenderInstancer",
            "scales",
            scales.data(),
            scales.size(),
            0,
            0.0,
            &error),
        error,
        "Could not update the hidden render instancer scales");
}

float RequirePointX(const PageStats& stats, const char* path, const char* message)
{
    const auto iterator = stats.firstPointXByPath.find(path);
    if (iterator == stats.firstPointXByPath.end())
    {
        throw std::runtime_error(message);
    }
    return iterator->second;
}

bool Near(float left, float right)
{
    return std::fabs(left - right) < 0.0001f;
}

bool VerifySessionLimitsCannotBeBypassed(const char* pluginPath)
{
    for (bool limitPage : {false, true})
    {
        EditableSession editable = CreateEditableSession(pluginPath);
        try
        {
            char errorBytes[4096]{};
            openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
            const openusd_silk_mesh_preparation_page_limits limits{
                {sizeof(limits), OPENUSD_SILK_MESH_PREPARATION_PAGE_VERSION, limitPage ? 1'000'000u : 1u},
                limitPage ? 1u : 1'000'000u};
            RequireStatus(openusd_silk_session_set_preparation_limits(editable.session, &limits, &error),
                error, "Could not configure session preparation ceilings");
            const openusd_render_camera camera = MakeCamera();
            const openusd_silk_scene_ingestion_request request{
                sizeof(request), RequestVersion, LegacyMask,
                OPENUSD_SILK_COMPLEXITY_LOW, OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED, "full"};
            const openusd_silk_mesh_preparation_page_limits relaxed{
                {sizeof(relaxed), OPENUSD_SILK_MESH_PREPARATION_PAGE_VERSION, 1'000'000}, 1'000'000};
            for (int call = 0; call < 3; ++call)
            {
                openusd_silk_page* page = nullptr;
                openusd_silk_page_view view{};
                view.struct_size = sizeof(view);
                view.revision = 123;
                view.command_count = 321;
                openusd_silk_mesh_preparation_usage usage{sizeof(usage), 1, 1000, 31, 43};
                error = {errorBytes, sizeof(errorBytes), 0};
                const openusd_status status = call == 0
                    ? openusd_silk_session_sync(editable.session, 96, 96, 0, &camera, &page, &view, &error)
                    : call == 1
                        ? openusd_silk_session_sync_with_scene_ingestion(
                            editable.session, 96, 96, 0, &camera, &request, &page, &view, &error)
                        : openusd_silk_session_sync_with_mesh_preparation(
                            editable.session, 96, 96, 0, &camera, &request, &relaxed.base,
                            &usage, &page, &view, &error);
                if (page != nullptr) { openusd_silk_page_release(page); }
                Require(status != OPENUSD_STATUS_OK && page == nullptr,
                    "A legacy or explicit sync bypassed its immutable session preparation ceiling.");
                Require(view.revision == 123 && view.command_count == 321 &&
                    usage.maximum_reserved_bytes == 1000 && usage.reserved_bytes == 31 && usage.peak_reserved_bytes == 43,
                    "Session admission refusal changed caller output.");
                Require(std::string(errorBytes).find(limitPage
                    ? "hdSilk command page refused" : "hdSilk mesh preparation refused") != std::string::npos,
                    "The session refusal did not identify the resource ceiling.");
            }
        }
        catch (...)
        {
            DestroyEditableSession(&editable);
            throw;
        }
        DestroyEditableSession(&editable);
    }
    return true;
}

bool VerifySessionLimitsPreserveLegacyChoicesAndPageSnapshots(const char* pluginPath)
{
    EditableSession editable = CreateEditableSession(pluginPath);
    openusd_silk_page* owned = nullptr;
    try
    {
        char errorBytes[4096]{};
        openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
        openusd_silk_mesh_preparation_page_limits limits{
            {sizeof(limits), OPENUSD_SILK_MESH_PREPARATION_PAGE_VERSION, 1'000'000}, 1'000'000};
        limits.base.struct_size--;
        Require(openusd_silk_session_set_preparation_limits(editable.session, &limits, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT, "A truncated session ceiling was accepted.");
        limits.base.struct_size = sizeof(limits);
        limits.maximum_page_bytes = 0;
        Require(openusd_silk_session_set_preparation_limits(editable.session, &limits, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT, "A zero session page ceiling was accepted.");
        limits.maximum_page_bytes = 1'000'000;
        RequireStatus(openusd_silk_session_set_preparation_limits(editable.session, &limits, &error),
            error, "Could not configure valid session ceilings after malformed requests");
        Require(openusd_silk_session_set_preparation_limits(editable.session, &limits, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT, "An immutable session ceiling was reconfigured.");
        const openusd_render_camera camera = MakeCamera();
        openusd_silk_page_view view{};
        view.struct_size = sizeof(view);
        RequireStatus(openusd_silk_session_sync(editable.session, 96, 96, 0, &camera, &owned, &view, &error),
            error, "Could not synchronize a bounded legacy viewport");
        Require(ReadPage(view).boundMaterialPath == "/World/AllPurposeMaterial",
            "Session admission changed the legacy allPurpose material binding.");
        const auto* bytes = static_cast<const uint8_t*>(view.data);
        const std::vector<uint8_t> original(bytes, bytes + view.data_size);
        openusd_silk_mesh_preparation_usage usage{sizeof(usage), 0, 0, 0, 0};
        RequireStatus(openusd_silk_page_get_preparation_usage(owned, &usage, &error),
            error, "Could not read a bounded legacy page's reservation snapshot");
        Require(usage.version == 1 && usage.maximum_reserved_bytes == 1'000'000 &&
            usage.reserved_bytes > 0 && usage.reserved_bytes <= usage.peak_reserved_bytes &&
            usage.peak_reserved_bytes <= usage.maximum_reserved_bytes,
            "A bounded legacy page did not report its actual reservation snapshot.");
        const openusd_silk_mesh_preparation_usage expected = usage;
        const PageStats full = Sync(editable.session, camera, LegacyMask, "full");
        Require(full.boundMaterialPath == "/World/FullMaterial",
            "Session admission changed explicit full-purpose binding.");
        const PageStats legacy = SyncLegacy(editable.session, camera);
        Require(legacy.boundMaterialPath == "/World/AllPurposeMaterial",
            "A bounded legacy retry did not restore allPurpose binding after an explicit request.");
        Require(openusd_silk_session_set_preparation_limits(editable.session, &limits, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT, "Session ceilings changed after synchronization.");
        const PageStats quiet = SyncLegacy(editable.session, camera);
        Require(quiet.meshUpserts == 0 && quiet.materialUpserts == 0,
            "Refused reconfiguration invalidated unchanged geometry.");
        RequireStatus(openusd_silk_page_get_preparation_usage(owned, &usage, &error),
            error, "Could not read an earlier page after further synchronization");
        Require(usage.maximum_reserved_bytes == expected.maximum_reserved_bytes &&
            usage.reserved_bytes == expected.reserved_bytes && usage.peak_reserved_bytes == expected.peak_reserved_bytes &&
            original == std::vector<uint8_t>(bytes, bytes + view.data_size),
            "Later synchronization changed an earlier owned page or its accounting.");
        usage.struct_size--;
        Require(openusd_silk_page_get_preparation_usage(owned, &usage, &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
            usage.struct_size == sizeof(usage) - 1 && usage.reserved_bytes == expected.reserved_bytes,
            "A truncated usage output was accepted or overwritten.");
        DestroyEditableSession(&editable);
        usage.struct_size = sizeof(usage);
        RequireStatus(openusd_silk_page_get_preparation_usage(owned, &usage, &error),
            error, "Destroying the session invalidated its owned page snapshot");
        Require(usage.reserved_bytes == expected.reserved_bytes,
            "Session teardown changed an owned page's accounting.");
    }
    catch (...)
    {
        if (owned != nullptr) { openusd_silk_page_release(owned); }
        DestroyEditableSession(&editable);
        throw;
    }
    openusd_silk_page_release(owned);
    return true;
}

bool VerifyAcknowledgedPagesRetainOwnershipAndBlockSync(const char* pluginPath)
{
    EditableSession editable = CreateEditableSession(pluginPath);
    openusd_silk_page* first = nullptr;
    openusd_silk_page* retry = nullptr;
    openusd_silk_page* later = nullptr;
    try
    {
        char errorBytes[4096]{};
        openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
        RequireStatus(openusd_silk_session_enable_page_acknowledgement(editable.session, &error),
            error, "Could not enable acknowledged publication");
        Require(openusd_silk_session_enable_page_acknowledgement(editable.session, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT, "Repeated acknowledgement configuration was accepted.");
        const openusd_render_camera camera = MakeCamera();
        openusd_silk_page_view view{};
        view.struct_size = sizeof(view);
        RequireStatus(openusd_silk_session_sync(editable.session, 96, 96, 0, &camera, &first, &view, &error),
            error, "Could not publish the first acknowledged-mode page");
        const auto* data = static_cast<const uint8_t*>(view.data);
        const std::vector<uint8_t> expected(data, data + view.data_size);
        const uint64_t firstRevision = view.revision;
        const auto requireBlocked = [&]()
        {
            openusd_silk_page* rejected = reinterpret_cast<openusd_silk_page*>(static_cast<uintptr_t>(1));
            openusd_silk_page_view untouched{};
            untouched.struct_size = sizeof(untouched);
            untouched.revision = 1234;
            untouched.command_count = 4321;
            Require(openusd_silk_session_sync(
                editable.session, 96, 96, 0, &camera, &rejected, &untouched, &error) ==
                    OPENUSD_STATUS_INVALID_ARGUMENT,
                "A sync bypassed its unresolved page.");
            Require(rejected == nullptr && untouched.revision == 1234 && untouched.command_count == 4321,
                "Blocked publication changed its output view.");
        };
        requireBlocked();
        Require(openusd_silk_session_request_repopulation(editable.session, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT, "Repopulation bypassed an unresolved page.");
        Require(openusd_silk_page_acknowledge(nullptr, &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "A null page was acknowledged.");
        openusd_silk_page_release(first);
        first = nullptr;
        RequireStatus(openusd_silk_session_sync(editable.session, 96, 96, 0, &camera, &retry, &view, &error),
            error, "An unacknowledged release did not permit a retry");
        data = static_cast<const uint8_t*>(view.data);
        Require(view.revision == firstRevision + 1 &&
            expected == std::vector<uint8_t>(data, data + view.data_size),
            "Unacknowledged release changed the retry payload or issued-page sequence.");
        openusd_status acknowledged = OPENUSD_STATUS_NATIVE_ERROR;
        std::thread acknowledge([&]()
        {
            acknowledged = openusd_silk_page_acknowledge(retry, &error);
        });
        acknowledge.join();
        RequireStatus(acknowledged, error, "Cross-thread acknowledgement failed");
        RequireStatus(openusd_silk_page_acknowledge(retry, &error), error, "Acknowledgement was not idempotent");
        RequireStatus(openusd_silk_session_sync(editable.session, 96, 96, 0, &camera, &later, &view, &error),
            error, "The acknowledged page still blocked synchronization");
        Require(view.command_count == 1, "Acknowledgement replayed an already consumed update.");
        RequireStatus(openusd_silk_page_acknowledge(retry, &error), error,
            "An old acknowledged page did not remain idempotent");
        openusd_silk_page_release(retry);
        retry = nullptr;
        requireBlocked();
        const auto* quietBytes = static_cast<const uint8_t*>(view.data);
        const std::vector<uint8_t> quiet(quietBytes, quietBytes + view.data_size);
        DestroyEditableSession(&editable);
        Require(quiet == std::vector<uint8_t>(quietBytes, quietBytes + view.data_size),
            "Destroying the session invalidated unacknowledged owned page bytes.");
        Require(openusd_silk_page_acknowledge(later, &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "A page was acknowledged after its session was destroyed.");
    }
    catch (...)
    {
        openusd_silk_page_release(first);
        openusd_silk_page_release(retry);
        openusd_silk_page_release(later);
        DestroyEditableSession(&editable);
        throw;
    }
    openusd_silk_page_release(later);
    return true;
}

bool VerifyRetainedInvalidation(const char* pluginPath)
{
    Require(
        openusd_silk_get_session_abi_version() == 6u,
        "The built hdSilk binary does not export session ABI 6.");
    Require(
        openusd_silk_get_page_abi_version() == 24u,
        "The built hdSilk binary does not export page ABI 24.");

    const std::filesystem::path scene = WriteStage();
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    openusd_silk_session* session = nullptr;
    const openusd_status status = openusd_silk_session_create(
        pluginPath,
        scene.string().c_str(),
        &session,
        &error);
    Require(status == OPENUSD_STATUS_OK, ReadError(error).c_str());

    const openusd_render_camera camera = MakeCamera();
    const PageStats full = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    const PageStats fullRepeat = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    const PageStats preview = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "preview");
    const PageStats previewRestore = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    const PageStats legacy = SyncLegacy(session, camera);
    const PageStats fullAfterLegacy = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    const PageStats proxy = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_PROXY,
        "full");

    openusd_error_buffer destroyError{errorBytes, sizeof(errorBytes), 0};
    const openusd_status destroyStatus =
        openusd_silk_session_destroy(session, &destroyError);
    Require(destroyStatus == OPENUSD_STATUS_OK, ReadError(destroyError).c_str());

    Require(full.meshUpserts >= 2, "The first explicit sync did not publish the whole visible product scene.");
    Require(full.materialUpserts >= 1, "The first explicit sync did not publish any materials.");
    Require(fullRepeat.meshUpserts == 0 && fullRepeat.materialUpserts == 0,
        "Repeated identical ingestion unexpectedly rebuilt retained meshes or materials.");
    Require(preview.meshUpserts >= 1,
        "Changing the material-binding purpose did not republish affected meshes.");
    Require(preview.boundMaterialPath == "/World/PreviewMaterial",
        "Preview purpose did not bind the preview material.");
    Require(previewRestore.boundMaterialPath == "/World/FullMaterial",
        "Restoring full purpose after preview did not restore the full material.");
    Require(legacy.boundMaterialPath == "/World/AllPurposeMaterial",
        "The legacy viewport sync did not restore the original all-purpose material binding token.");
    Require(legacy.meshUpserts >= 1,
        "The legacy viewport sync did not republish retained scene content after explicit filters.");
    Require(fullAfterLegacy.boundMaterialPath == "/World/FullMaterial",
        "Restoring full purpose after a legacy sync did not restore the full material.");
    Require(fullAfterLegacy.meshUpserts >= 1,
        "Returning from legacy viewport sync to explicit product sync did not republish retained scene content.");
    Require(proxy.meshRemovals >= 1,
        "Narrowing the purpose mask did not retire previously retained prims.");
    return true;
}

bool VerifyInvalidArguments(const char* pluginPath)
{
    const std::filesystem::path scene = WriteStage();
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    openusd_silk_session* session = nullptr;
    const openusd_status status = openusd_silk_session_create(
        pluginPath,
        scene.string().c_str(),
        &session,
        &error);
    Require(status == OPENUSD_STATUS_OK, ReadError(error).c_str());

    const openusd_render_camera camera = MakeCamera();
    openusd_silk_page* page = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);

    openusd_silk_scene_ingestion_request unsupportedMask{
        sizeof(openusd_silk_scene_ingestion_request),
        RequestVersion,
        OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        OPENUSD_SILK_COMPLEXITY_LOW,
        OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED,
        ""};
    error = {errorBytes, sizeof(errorBytes), 0};
    Require(
        openusd_silk_session_sync_with_scene_ingestion(
            session,
            96,
            96,
            0,
            &camera,
            &unsupportedMask,
            &page,
            &view,
            &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "A mask excluding default purpose was accepted.");
    Require(std::string(errorBytes).find("default purpose") != std::string::npos,
        "The unsupported default-purpose exclusion diagnostic was not explicit.");

    const PageStats full = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    Require(full.meshUpserts >= 2, "The baseline explicit sync did not publish the visible scene.");

    ExpectExplicitSyncFailure(
        session,
        camera,
        LegacyMask,
        "",
        "full' and 'preview");
    const PageStats emptyRetry = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    Require(
        emptyRetry.meshUpserts == 0 && emptyRetry.materialUpserts == 0,
        "Rejecting the empty explicit material-binding token still changed retained scene state.");

    ExpectExplicitSyncFailure(
        session,
        camera,
        LegacyMask,
        "allPurpose",
        "full' and 'preview");
    const PageStats allPurposeRetry = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    Require(
        allPurposeRetry.meshUpserts == 0 && allPurposeRetry.materialUpserts == 0,
        "Rejecting the allPurpose explicit material-binding token still changed retained scene state.");

    ExpectExplicitSyncFailure(
        session,
        camera,
        LegacyMask,
        "customLook",
        "full' and 'preview");
    const PageStats customRetry = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    Require(
        customRetry.meshUpserts == 0 && customRetry.materialUpserts == 0,
        "Rejecting the custom explicit material-binding token still changed retained scene state.");

    openusd_silk_scene_ingestion_request malformedToken{
        sizeof(openusd_silk_scene_ingestion_request),
        RequestVersion,
        LegacyMask,
        OPENUSD_SILK_COMPLEXITY_LOW,
        OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED,
        "bad token"};
    error = {errorBytes, sizeof(errorBytes), 0};
    Require(
        openusd_silk_session_sync_with_scene_ingestion(
            session,
            96,
            96,
            0,
            &camera,
            &malformedToken,
            &page,
            &view,
            &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "A malformed material-binding purpose token was accepted.");

    openusd_silk_scene_ingestion_request unsupportedVersion{
        sizeof(openusd_silk_scene_ingestion_request),
        RequestVersion + 1,
        LegacyMask,
        OPENUSD_SILK_COMPLEXITY_LOW,
        OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED,
        "full"};
    error = {errorBytes, sizeof(errorBytes), 0};
    Require(
        openusd_silk_session_sync_with_scene_ingestion(
            session,
            96,
            96,
            0,
            &camera,
            &unsupportedVersion,
            &page,
            &view,
            &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "A scene-ingestion packet with an unsupported version was accepted.");

    const PageStats malformedRetry = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    Require(
        malformedRetry.meshUpserts == 0 && malformedRetry.materialUpserts == 0,
        "Rejecting a malformed material-binding token still changed retained scene state.");

    static_assert(sizeof(openusd_silk_mesh_preparation_limits) == 16);
    static_assert(sizeof(openusd_silk_mesh_preparation_page_limits) == 24);
    static_assert(offsetof(openusd_silk_mesh_preparation_page_limits, maximum_page_bytes) == 16);
    static_assert(sizeof(openusd_silk_mesh_preparation_usage) == 32);
    const openusd_silk_scene_ingestion_request boundedRequest{
        sizeof(openusd_silk_scene_ingestion_request), RequestVersion,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        OPENUSD_SILK_COMPLEXITY_LOW, OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED, "full"};
    const auto rejectLimits = [&](const openusd_silk_mesh_preparation_limits* limits, const char* diagnostic)
    {
        openusd_silk_mesh_preparation_usage usage{sizeof(usage), 1, 1000, 31, 43};
        page = reinterpret_cast<openusd_silk_page*>(static_cast<uintptr_t>(1));
        view.abi_version = 77;
        view.revision = 123;
        view.data_size = 7;
        view.command_count = 11;
        error = {errorBytes, sizeof(errorBytes), 0};
        Require(openusd_silk_session_sync_with_mesh_preparation(
            session, 96, 96, 0, &camera, &boundedRequest, limits, &usage, &page, &view, &error) ==
                OPENUSD_STATUS_INVALID_ARGUMENT, "Invalid preparation limits were accepted.");
        Require(page == nullptr, "Invalid preparation limits returned an owned page.");
        Require(view.abi_version == 77 && view.revision == 123 && view.data_size == 7 &&
            view.command_count == 11, "Invalid preparation limits changed the page view.");
        Require(usage.struct_size == sizeof(usage) && usage.version == 1 && usage.maximum_reserved_bytes == 1000 &&
            usage.reserved_bytes == 31 && usage.peak_reserved_bytes == 43,
            "Invalid preparation limits changed usage output.");
        Require(std::string(errorBytes).find(diagnostic) != std::string::npos,
            "Invalid preparation limits did not give the expected diagnostic.");
        const PageStats unchanged = Sync(session, camera, boundedRequest.included_purpose_mask, "full");
        Require(unchanged.meshUpserts == 0 && unchanged.materialUpserts == 0,
            "Invalid preparation limits changed retained publication state.");
    };
    const openusd_silk_mesh_preparation_limits truncated{
        sizeof(openusd_silk_mesh_preparation_limits), OPENUSD_SILK_MESH_PREPARATION_PAGE_VERSION, 1'000'000};
    rejectLimits(&truncated, "version-2 preparation limit packet is incomplete");
    openusd_silk_mesh_preparation_page_limits pageLimits{
        {sizeof(pageLimits), OPENUSD_SILK_MESH_PREPARATION_PAGE_VERSION, 1'000'000}, 0};
    rejectLimits(&pageLimits.base, "command page byte limit must be positive");
    pageLimits.maximum_page_bytes = 1'000'000;
    pageLimits.base.version = OPENUSD_SILK_MESH_PREPARATION_PAGE_VERSION + 1;
    rejectLimits(&pageLimits.base, "Valid mesh preparation limits");

    openusd_error_buffer destroyError{errorBytes, sizeof(errorBytes), 0};
    Require(
        openusd_silk_session_destroy(session, &destroyError) == OPENUSD_STATUS_OK,
        ReadError(destroyError).c_str());

    error = {errorBytes, sizeof(errorBytes), 0};
    Require(
        openusd_silk_session_sync_with_scene_ingestion(
            session,
            96,
            96,
            0,
            &camera,
            &malformedToken,
            &page,
            &view,
            &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "A stale session handle was accepted.");
    return true;
}

bool VerifyFailureRecovery(const char* pluginPath)
{
    const std::filesystem::path scene = WriteStage();
    char errorBytes[4096] = {};
    openusd_error_buffer error{errorBytes, sizeof(errorBytes), 0};
    openusd_silk_session* session = nullptr;
    const openusd_status status = openusd_silk_session_create(
        pluginPath,
        scene.string().c_str(),
        &session,
        &error);
    Require(status == OPENUSD_STATUS_OK, ReadError(error).c_str());

    const openusd_render_camera camera = MakeCamera();
    const PageStats full = Sync(
        session,
        camera,
        OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
        "full");
    Require(full.meshUpserts >= 2, "The failure-recovery baseline did not publish the visible scene.");

    const char* failpoints[] = {
        "after-engine-reset",
        "after-engine-create",
        "after-configure",
        "after-render",
        "before-build-page"};
    for (const char* failpoint : failpoints)
    {
        SetEnvironment("OPENUSD_HDSILK_SCENE_SYNC_FAILPOINT", failpoint);
        bool previewFailed = false;
        try
        {
            static_cast<void>(Sync(
                session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "preview"));
        }
        catch (const std::exception& exception)
        {
            previewFailed =
                std::string(exception.what()).find("scene-ingestion sync failpoint") !=
                std::string::npos;
        }
        SetEnvironment("OPENUSD_HDSILK_SCENE_SYNC_FAILPOINT", "");
        Require(previewFailed, "The explicit preview failpoint did not fail the sync.");

        const PageStats preview = Sync(
            session,
            camera,
            OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
            "preview");
        Require(preview.meshUpserts >= 1, "Retrying the same explicit preview request did not rebuild the affected mesh binding.");
        Require(preview.boundMaterialPath == "/World/PreviewMaterial",
            "Retrying the same explicit preview request did not restore the preview binding.");

        const PageStats restored = Sync(
            session,
            camera,
            OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
            "full");
        Require(restored.meshUpserts >= 1, "Restoring full after an injected preview failure did not rebuild the affected mesh binding.");
        Require(restored.boundMaterialPath == "/World/FullMaterial",
            "Restoring full after an injected preview failure did not restore the full binding.");

        SetEnvironment("OPENUSD_HDSILK_SCENE_SYNC_FAILPOINT", failpoint);
        bool legacyFailed = false;
        try
        {
            static_cast<void>(SyncLegacy(session, camera));
        }
        catch (const std::exception& exception)
        {
            legacyFailed =
                std::string(exception.what()).find("scene-ingestion sync failpoint") !=
                std::string::npos;
        }
        SetEnvironment("OPENUSD_HDSILK_SCENE_SYNC_FAILPOINT", "");
        Require(legacyFailed, "The legacy failpoint did not fail the sync.");

        const PageStats legacy = SyncLegacy(session, camera);
        Require(legacy.meshUpserts >= 1, "Retrying the same legacy request did not rebuild retained scene content.");
        Require(legacy.boundMaterialPath == "/World/AllPurposeMaterial",
            "Retrying the same legacy request did not restore the all-purpose binding.");

        const PageStats fullAfterLegacy = Sync(
            session,
            camera,
            OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
            "full");
        Require(fullAfterLegacy.meshUpserts >= 1,
            "Restoring full after an injected legacy failure did not rebuild retained scene content.");
        Require(fullAfterLegacy.boundMaterialPath == "/World/FullMaterial",
            "Restoring full after an injected legacy failure did not restore the full binding.");
    }

    openusd_error_buffer destroyError{errorBytes, sizeof(errorBytes), 0};
    const openusd_status destroyStatus =
        openusd_silk_session_destroy(session, &destroyError);
    Require(destroyStatus == OPENUSD_STATUS_OK, ReadError(destroyError).c_str());
    return true;
}

bool VerifyHiddenSceneEditsDoNotRestoreStaleRetiredMeshes(const char* pluginPath)
{
    {
        EditableSession editable = CreateEditableSession(pluginPath);
        try
        {
            const openusd_render_camera camera = MakeCamera();
            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "full",
                0.0));

            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_PROXY,
                "full",
                0.0));
            SetRenderQuadPoints(editable.stage, 0.45f, 1.05f);
            const PageStats edited = Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "full",
                0.0);
            Require(
                Near(
                    RequirePointX(
                        edited,
                        "/World/RenderQuad",
                        "The edited render quad was not republished after widening."),
                    0.45f),
                "A hidden edited render mesh was restored from stale cached geometry.");
        }
        catch (...)
        {
            DestroyEditableSession(&editable);
            throw;
        }
        DestroyEditableSession(&editable);
    }

    {
        EditableSession editable = CreateEditableSession(pluginPath);
        try
        {
            const openusd_render_camera camera = MakeCamera();
            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "full",
                0.0));

            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_PROXY,
                "full",
                0.0));
            RedefineRenderQuad(editable.stage, 0.6f, 1.2f);
            const PageStats redefined = Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "full",
                0.0);
            Require(
                Near(
                    RequirePointX(
                        redefined,
                        "/World/RenderQuad",
                        "The redefined render quad was not republished after widening."),
                    0.6f),
                "A hidden deleted-and-redefined render mesh was restored from stale cached geometry.");
        }
        catch (...)
        {
            DestroyEditableSession(&editable);
            throw;
        }
        DestroyEditableSession(&editable);
    }

    {
        EditableSession editable = CreateEditableSession(pluginPath);
        try
        {
            const openusd_render_camera camera = MakeCamera();
            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "full",
                0.0));

            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_PROXY,
                "full",
                0.0));
            const PageStats animated = Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_RENDER,
                "full",
                1.0);
            Require(
                Near(
                    RequirePointX(
                        animated,
                        "/World/RenderQuad",
                        "The time-varying render quad was not republished after widening."),
                    0.35f),
                "Changing the widening time reused stale hidden geometry instead of the current sample.");
        }
        catch (...)
        {
            DestroyEditableSession(&editable);
            throw;
        }
        DestroyEditableSession(&editable);
    }

    {
        EditableSession editable = CreateEditableSession(pluginPath);
        try
        {
            const openusd_render_camera camera = MakeCamera();
            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_GUIDE,
                "full",
                0.0));

            static_cast<void>(Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_PROXY,
                "full",
                0.0));
            SetInstancerInstanceCount(editable.stage, 2);
            const PageStats current = Sync(
                editable.session,
                camera,
                OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_GUIDE,
                "full",
                0.0);
            const auto instances = current.instanceIndicesByInstancer.find("/World/RenderInstancer");
            Require(
                instances != current.instanceIndicesByInstancer.end() &&
                    instances->second == std::vector<uint32_t>{0u, 1u},
                "A hidden instancer change did not republish both current instance identities.");
        }
        catch (...)
        {
            DestroyEditableSession(&editable);
            throw;
        }
        DestroyEditableSession(&editable);
    }
    return true;
}
}

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2)
        {
            std::cerr << "usage: hdsilk_scene_ingestion_probe <plugin-dir>\n";
            return 2;
        }

        if (!VerifyAcknowledgedPagesRetainOwnershipAndBlockSync(argv[1]) ||
            !VerifySessionLimitsCannotBeBypassed(argv[1]) ||
            !VerifySessionLimitsPreserveLegacyChoicesAndPageSnapshots(argv[1]) ||
            !VerifyRetainedInvalidation(argv[1]) ||
            !VerifyInvalidArguments(argv[1]) ||
            !VerifyFailureRecovery(argv[1]) ||
            !VerifyHiddenSceneEditsDoNotRestoreStaleRetiredMeshes(argv[1]))
        {
            return 1;
        }

        std::cout << "hdsilk_scene_ingestion_probe: ok\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "hdsilk_scene_ingestion_probe failed: " << error.what() << '\n';
        return 1;
    }
}
