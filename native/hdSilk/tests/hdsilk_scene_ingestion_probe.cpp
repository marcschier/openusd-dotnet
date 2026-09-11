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

        if (!VerifyRetainedInvalidation(argv[1]) ||
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
