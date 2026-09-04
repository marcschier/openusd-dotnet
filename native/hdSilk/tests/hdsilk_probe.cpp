// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "hdsilk_test_hooks.h"
#include "curveWidths.h"
#include "instanceLinking.h"
#include "material.h"
#include "materialXBridge.h"
#include "mdlAdapter.h"
#include "openusd_dotnet_test_hooks.h"
#include "sceneState.h"

#include "pxr/base/arch/env.h"
#include "pxr/base/arch/fileSystem.h"
#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/half.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/imaging/cameraUtil/conformWindow.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/sdf/assetPath.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdlib>
#include <cstdint>
#include <cstdio>
#include <cstring>
#if defined(_WIN32)
#include <direct.h>
#else
#include <unistd.h>
#endif
#include <fstream>
#include <iostream>
#include <limits>
#include <map>
#include <mutex>
#include <string>
#include <thread>
#include <tuple>
#include <unordered_set>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr char SharedMeshPath[] = "/World/SharedStageProbeMesh";
constexpr char TopologyMeshPath[] = "/World/TopologyProbeMesh";
constexpr char MaterialPath[] = "/World/ProbeMaterial";
constexpr char SurfaceShaderPath[] = "/World/ProbeMaterial/Surface";
constexpr char TextureShaderPath[] = "/World/ProbeMaterial/Texture";
constexpr char MaterialTextureAsset[] = "textures/probe-albedo.png";
constexpr char PrimvarMeshPath[] = "/World/PrimvarMesh";
constexpr char BasisCurvesPath[] = "/World/ProbeBasisCurves";
constexpr char VaryingWidthCurvesPath[] = "/World/ProbeVaryingWidthCurves";
constexpr char UniformWidthCurvesPath[] = "/World/ProbeUniformWidthCurves";
constexpr char RightHandedMeshPath[] = "/World/ProbeRightHandedMesh";
constexpr char LeftHandedMeshPath[] = "/World/ProbeLeftHandedMesh";
constexpr char LeftHandedFaceVaryingMeshPath[] =
    "/World/ProbeLeftHandedFaceVaryingMesh";
constexpr char ExpandedTopologyToggleMeshPath[] =
    "/World/ProbeExpandedTopologyToggleMesh";
constexpr char StrayPointMeshPath[] = "/World/ProbeStrayPointMesh";
constexpr char LinkedLitMeshPath[] = "/World/LinkedLitMesh";
constexpr char LinkedUnlitMeshPath[] = "/World/LinkedUnlitMesh";
constexpr char LinkedLightPath[] = "/World/LinkedKeyLight";
constexpr char LinkedDomePath[] = "/World/LinkedDome";

constexpr char BlendShapeMeshPath[] = "/World/Rig/BlendShapeTriangle";
constexpr char InbetweenMeshPath[] = "/World/Rig/InbetweenTriangle";
constexpr char SkinnedNormalMeshPath[] = "/World/Rig/SkinnedNormalQuad";
constexpr char FaceVaryingNormalMeshPath[] =
    "/World/Rig/FaceVaryingNormalTriangle";
/// One parsed entry of the ABI 4 vertex attribute table.
struct ParsedAttribute
{
    std::string name;
    uint32_t semantic = 0;
    uint32_t componentCount = 0;
    uint32_t interpolation = 0;
    uint32_t elementCount = 0;
    float firstValue = 0.0F;
    std::vector<float> values;
};
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 5);
static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 23);

/// One decoded ABI v20 deformation block. The probe decodes the whole rig
/// rather than its header, because the only assertion worth making about a
/// bounded rig is that evaluating it reproduces the deformed points published
/// beside it: a header check would pass for a rig that describes a different
/// surface.
struct ParsedDeformation
{
    bool present = false;
    uint32_t flags = 0;
    uint32_t unsupported = 0;
    uint32_t joint_count = 0;
    uint32_t influences_per_point = 0;
    uint32_t bind_point_count = 0;
    uint64_t identity = 0;
    std::array<float, 16> geom_bind_transform{};
    std::vector<float> bind_points;
    std::vector<float> bind_normals;
    std::vector<uint32_t> joint_indices;
    std::vector<float> joint_weights;
    std::vector<float> joint_matrices;
    std::vector<std::array<float, 3>> blend_range;  // first, count, weight.
    std::vector<uint32_t> blend_delta_points;
    std::vector<float> blend_delta_offsets;   // 3 per delta.
    std::vector<float> blend_delta_normals;   // 3 per delta.
};

openusd_render_camera AutomaticCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    return camera;
}

openusd_render_camera ExplicitCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    GfMatrix4d view(1.0);
    view.SetTranslate(GfVec3d(1.25, -2.5, 3.75));
    GfFrustum frustum;
    frustum.SetPerspective(37.0, 1.5, 0.25, 750.0);
    std::memcpy(camera.view, view.GetArray(), sizeof(camera.view));
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    std::memcpy(
        camera.projection,
        projection.GetArray(),
        sizeof(camera.projection));
    return camera;
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

struct ParsedMeshIdentity
{
    std::string path;
    int32_t prim_id = -1;
    uint64_t topology_revision = 0;
    int32_t instance_id = 0;
    int32_t instance_index = 0;
    // Point-instancer evidence needs the resolved world transform and whether
    // the record carried the ABI v8 prototype payload, neither of which any
    // other parsed field can stand in for.
    uint32_t point_count = 0;
    std::array<double, 16> transform{};
    // ABI v23. The complete ordered instancing chain, outermost level first.
    // A composed ordinal cannot be decoded back to a scene instance once more
    // than one level is involved, so nested-instancing evidence has to assert
    // on the chain itself.
    std::vector<std::pair<std::string, int32_t>> instancer_context;
};

/// One published basis-curves line list, including the resolved width
/// attribute. Widths are captured in full rather than by first element: the
/// point of the varying-width probe is that every emitted vertex carries its
/// own authored value.
struct ParsedCurves
{
    bool found = false;
    uint32_t topology_kind = 0;
    uint64_t topology_revision = 0;
    uint32_t triangle_count = 0;
    std::vector<uint32_t> point_origins;
    std::vector<float> points;
    std::vector<uint32_t> indices;
    std::vector<uint32_t> subprims;
    std::vector<ParsedAttribute> attributes;
};

/// One published material, kept so a stage that publishes several on purpose can
/// be checked material by material rather than only by whichever came last.
///
/// Each texture entry is (parameter, output channel, asset, uv primvar). The
/// primvar is per entry on the wire, which is what lets a displacement sample its
/// own coordinate set while the surface samples another.
struct ParsedMaterialRecord
{
    std::string path;
    uint32_t surface_kind = 0;
    std::vector<std::tuple<uint32_t, float>> scalars;
    std::vector<std::tuple<uint32_t, uint32_t, std::string, std::string>> textures;
};

struct ParsedPage
{
    uint32_t frame_count = 0;
    uint32_t mesh_upsert_count = 0;
    uint32_t mesh_remove_count = 0;
    uint32_t parsed_count = 0;
    bool found_shared_upsert = false;
    bool found_shared_remove = false;
    bool found_topology_upsert = false;
    bool mesh_identity_valid = true;
    bool instance_fields_zero = true;
    float shared_first_x = 0.0F;
    uint64_t shared_stable_hash = 0;
    int32_t shared_prim_id = -1;
    uint32_t shared_topology_kind = 0;
    uint64_t shared_topology_revision = 0;
    uint32_t shared_triangle_count = 0;
    std::vector<uint32_t> shared_subprims;
    std::vector<uint32_t> topology_subprims;
    std::vector<std::string> upsert_paths;
    std::vector<std::string> remove_paths;
    std::vector<int32_t> remove_instance_indices;
    std::vector<ParsedMeshIdentity> mesh_identities;
    std::vector<uint32_t> mesh_point_counts;
    std::vector<uint32_t> mesh_index_counts;
    std::vector<uint32_t> mesh_attribute_counts;
    std::vector<uint32_t> mesh_cull_styles;
    std::vector<uint32_t> mesh_topology_kinds;
    // The topology revision each record published, in wire order. It is the
    // presentation revision, not the authored one: draw mode and complexity
    // rebuild the emitted arrays a consumer retains, so a record drawn as
    // points and the same record drawn shaded must not arrive under one
    // revision.
    std::vector<uint64_t> mesh_topology_revisions;
    std::vector<uint32_t> mesh_triangle_counts;
    // ABI v20 deformation evidence. The published count and the reason mask are
    // separate because a page that publishes no rig and a page that publishes
    // one it could not describe are different outcomes, and only the second one
    // names a reason.
    uint32_t deformation_published_count = 0;
    uint32_t deformation_unsupported_mask = 0;
    bool deformation_valid = true;
    // Whether every MESH_UPSERT this page carried declared an ABI v22
    // subprim-identity claim that matched the tables it published, and every
    // published entry named an authored component inside the count the record
    // declared or the explicit "no authored counterpart" sentinel.
    bool subprim_identity_valid = true;
    // ABI v22 per-record subprim evidence, in wire order. The claim and the
    // reasons are separate from the tables because a record that answers a
    // target and one that refuses it with a reason are different outcomes, and
    // only the emitted arrays can show which components a claim describes.
    std::vector<uint32_t> mesh_subprim_identities;
    std::vector<uint32_t> mesh_subprim_unsupported;
    std::vector<std::vector<uint32_t>> mesh_point_origins;
    // The authored point space each record declares, in wire order. Separate
    // from the table because a transform that keeps the table while zeroing
    // the count -- or the reverse -- publishes an authored space no consumer
    // can resolve into.
    std::vector<uint32_t> mesh_authored_point_counts;
    std::vector<std::vector<uint32_t>> mesh_primitive_subprims;
    bool deformation_reproduces_points = true;
    float deformation_worst_point_error = 0.0F;
    uint32_t material_upsert_count = 0;
    uint32_t material_remove_count = 0;
    bool material_valid = true;
    bool found_material_upsert = false;
    std::string material_path;
    std::string shared_material_binding;
    uint32_t material_surface_kind = 0;
    uint32_t material_scalar_count = 0;
    uint32_t material_texture_count = 0;
    std::array<float, 6> material_uv_transform{1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F};
    float material_roughness = -1.0F;
    // Every published scalar as (parameter, first component). The MDL
    // module-default check needs more than one input, because the claim is
    // that an authored value and a module default arrived together.
    std::vector<std::tuple<uint32_t, float>> material_scalars;
    std::string material_texture_asset;
    std::string material_texture_uv;
    uint32_t material_texture_parameter = 0;
    // Every published texture entry as (parameter, output channel, asset), in
    // wire order. The channel is what proves the authored UsdUVTexture output
    // token survived resolution, which no other field can stand in for.
    std::vector<std::tuple<uint32_t, uint32_t, std::string, std::string>> material_textures;
    // Every published material, in wire order. The single-material fields above
    // describe whichever one came last, which is enough for a stage with one
    // material and useless for a stage that publishes several on purpose.
    std::vector<ParsedMaterialRecord> materials;
    std::vector<std::string> material_remove_paths;
    std::vector<ParsedAttribute> primvar_mesh_attributes;
    bool found_primvar_mesh = false;
    ParsedCurves basis_curves;
    ParsedCurves varying_width_curves;
    ParsedCurves uniform_width_curves;
    ParsedCurves right_handed_mesh;
    ParsedCurves left_handed_mesh;
    ParsedCurves left_handed_face_varying_mesh;
    ParsedCurves expanded_topology_toggle_mesh;
    ParsedCurves stray_point_mesh;

    bool found_blend_shape_mesh = false;
    float blend_shape_first_x = 0.0F;
    ParsedCurves inbetween_mesh;
    ParsedCurves skinned_normal_mesh;
    ParsedCurves face_varying_normal_mesh;
    int32_t frame_width = 0;
    int32_t frame_height = 0;
    std::array<double, 16> frame_view{};
    std::array<double, 16> frame_projection{};
    uint32_t frame_light_count = 0;
    // ABI v21 dome table. dome_count is the ordering a LIGHT_LINK dome_mask
    // indexes; the per-dome ambient and flags are read so a masked dome sum can
    // be checked against the scene-wide ambient it must reproduce.
    uint32_t frame_dome_count = 0;
    std::array<float, 3> frame_ambient{};
    std::vector<std::tuple<float, float, float, uint32_t>> frame_domes;

    // ABI v18 light linking. The table is sparse and default-free, so its
    // presence and exact contents are the evidence that a collection resolved
    // into per-prim masks rather than into a table that names every prim.
    uint32_t light_link_count = 0;
    bool light_link_valid = true;
    uint32_t light_link_light_count = 0;
    uint32_t light_link_unsupported = 0;
    uint32_t light_link_dome_count = 0;
    // (path, instance_index, light_mask, shadow_mask, dome_mask)
    std::vector<std::tuple<std::string, int32_t, uint32_t, uint32_t, uint32_t>>
        light_links;
    // ABI v21 environment dome indices, keyed by dome prim path, so a probe can
    // prove the bit an ENVIRONMENT record claims is the bit the dome table
    // published. The third element is unsupported_features, which is where a
    // dome's collection:shadowLink is named.
    std::vector<std::tuple<std::string, uint32_t, uint32_t>> environment_dome_indices;
    uint32_t shadow_count = 0;
    bool shadow_valid = true;
    uint32_t shadow_descriptor_count = 0;
    uint32_t shadow_light_count = 0;
    uint32_t shadow_unsupported = 0;
    // (light_index, map_index, resolution, flags) per published descriptor.
    std::vector<std::tuple<uint32_t, uint32_t, uint32_t, uint32_t>> shadows;
};

class StartBarrier
{
public:
    explicit StartBarrier(size_t participantCount) : _remaining(participantCount)
    {
    }

    void ArriveAndWait()
    {
        std::unique_lock<std::mutex> lock(_mutex);
        if (--_remaining == 0)
        {
            _condition.notify_all();
            return;
        }
        _condition.wait(lock, [this] { return _remaining == 0; });
    }

private:
    std::mutex _mutex;
    std::condition_variable _condition;
    size_t _remaining;
};

template <typename T>
bool ReadValue(const uint8_t* data, size_t size, size_t offset, T* value)
{
    if (offset > size || sizeof(T) > size - offset)
    {
        return false;
    }
    std::memcpy(value, data + offset, sizeof(T));
    return true;
}

uint64_t ComputeStableHash(const std::string& path)
{
    uint64_t hash = 14695981039346656037ull;
    for (unsigned char byte : path)
    {
        hash ^= static_cast<uint64_t>(byte);
        hash *= 1099511628211ull;
    }
    return hash;
}

bool AddSize(size_t* value, size_t add)
{
    if (add > std::numeric_limits<size_t>::max() - *value)
    {
        return false;
    }
    *value += add;
    return true;
}

bool MultiplySize(size_t left, size_t right, size_t* result)
{
    if (left != 0 && right > std::numeric_limits<size_t>::max() / left)
    {
        return false;
    }
    *result = left * right;
    return true;
}

/// Decodes one ABI v20 deformation block and checks every bound and every
/// internal reference it declares. A block that does not decode exactly is a
/// parse failure rather than a tolerated one: the whole point of publishing a
/// self-describing rig is that a consumer can reject it before evaluating it.
bool DecodeDeformation(
    const uint8_t* data,
    size_t size,
    size_t offset,
    uint32_t byteCount,
    uint32_t flags,
    uint32_t pointCount,
    ParsedDeformation* deformation)
{
    const uint32_t knownFlags =
        OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS |
        OPENUSD_SILK_DEFORMATION_FLAG_BLEND_NORMAL_OFFSETS;
    if ((flags & ~knownFlags) != 0)
    {
        return false;
    }
    uint32_t blendRangeCount = 0;
    uint32_t blendDeltaCount = 0;
    uint32_t reserved = 1;
    if (!ReadValue(data, size, offset, &deformation->joint_count) ||
        !ReadValue(data, size, offset + 4, &deformation->influences_per_point) ||
        !ReadValue(data, size, offset + 8, &deformation->bind_point_count) ||
        !ReadValue(data, size, offset + 12, &blendRangeCount) ||
        !ReadValue(data, size, offset + 16, &blendDeltaCount) ||
        !ReadValue(data, size, offset + 20, &reserved) ||
        !ReadValue(data, size, offset + 24, &deformation->identity))
    {
        return false;
    }
    if (reserved != 0 ||
        deformation->joint_count == 0 ||
        deformation->joint_count > OPENUSD_SILK_MAX_DEFORMATION_JOINTS ||
        deformation->influences_per_point == 0 ||
        deformation->influences_per_point >
            OPENUSD_SILK_MAX_DEFORMATION_INFLUENCES ||
        deformation->bind_point_count != pointCount ||
        blendRangeCount > OPENUSD_SILK_MAX_DEFORMATION_BLEND_RANGES ||
        blendDeltaCount > OPENUSD_SILK_MAX_DEFORMATION_BLEND_DELTAS)
    {
        return false;
    }

    size_t cursor = offset + 32;
    for (size_t element = 0; element < 16; ++element)
    {
        if (!ReadValue(
                data,
                size,
                cursor + (element * sizeof(float)),
                &deformation->geom_bind_transform[element]))
        {
            return false;
        }
    }
    cursor += 16 * sizeof(float);

    const size_t pointComponents = static_cast<size_t>(pointCount) * 3;
    const bool hasBindNormals =
        (flags & OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS) != 0;
    const size_t influences =
        static_cast<size_t>(pointCount) * deformation->influences_per_point;
    const auto readFloats =
        [&](std::vector<float>* target, size_t count) -> bool
    {
        target->resize(count);
        for (size_t element = 0; element < count; ++element)
        {
            if (!ReadValue(
                    data,
                    size,
                    cursor + (element * sizeof(float)),
                    &(*target)[element]))
            {
                return false;
            }
        }
        cursor += count * sizeof(float);
        return true;
    };

    if (!readFloats(&deformation->bind_points, pointComponents))
    {
        return false;
    }
    if (hasBindNormals && !readFloats(&deformation->bind_normals, pointComponents))
    {
        return false;
    }
    deformation->joint_indices.resize(influences);
    for (size_t element = 0; element < influences; ++element)
    {
        if (!ReadValue(
                data,
                size,
                cursor + (element * sizeof(uint32_t)),
                &deformation->joint_indices[element]) ||
            deformation->joint_indices[element] >= deformation->joint_count)
        {
            return false;
        }
    }
    cursor += influences * sizeof(uint32_t);
    if (!readFloats(&deformation->joint_weights, influences) ||
        !readFloats(
            &deformation->joint_matrices,
            static_cast<size_t>(deformation->joint_count) * 16))
    {
        return false;
    }

    deformation->blend_range.resize(blendRangeCount);
    for (uint32_t range = 0; range < blendRangeCount; ++range)
    {
        uint32_t firstDelta = 0;
        uint32_t deltaCount = 0;
        float weight = 0.0F;
        uint32_t rangeReserved = 1;
        const size_t entry = cursor + (static_cast<size_t>(range) * 16);
        if (!ReadValue(data, size, entry, &firstDelta) ||
            !ReadValue(data, size, entry + 4, &deltaCount) ||
            !ReadValue(data, size, entry + 8, &weight) ||
            !ReadValue(data, size, entry + 12, &rangeReserved) ||
            rangeReserved != 0 ||
            static_cast<uint64_t>(firstDelta) + deltaCount > blendDeltaCount)
        {
            return false;
        }
        deformation->blend_range[range] = {
            static_cast<float>(firstDelta),
            static_cast<float>(deltaCount),
            weight};
    }
    cursor += static_cast<size_t>(blendRangeCount) * 16;

    deformation->blend_delta_points.resize(blendDeltaCount);
    deformation->blend_delta_offsets.resize(
        static_cast<size_t>(blendDeltaCount) * 3);
    deformation->blend_delta_normals.resize(
        static_cast<size_t>(blendDeltaCount) * 3);
    for (uint32_t delta = 0; delta < blendDeltaCount; ++delta)
    {
        const size_t entry = cursor + (static_cast<size_t>(delta) * 28);
        if (!ReadValue(
                data,
                size,
                entry,
                &deformation->blend_delta_points[delta]) ||
            deformation->blend_delta_points[delta] >= pointCount)
        {
            return false;
        }
        for (size_t component = 0; component < 3; ++component)
        {
            if (!ReadValue(
                    data,
                    size,
                    entry + 4 + (component * sizeof(float)),
                    &deformation->blend_delta_offsets[
                        (static_cast<size_t>(delta) * 3) + component]) ||
                !ReadValue(
                    data,
                    size,
                    entry + 16 + (component * sizeof(float)),
                    &deformation->blend_delta_normals[
                        (static_cast<size_t>(delta) * 3) + component]))
            {
                return false;
            }
        }
    }
    cursor += static_cast<size_t>(blendDeltaCount) * 28;

    if (cursor != offset + byteCount)
    {
        return false;
    }
    // The identity is an index over the block's own bytes, so recomputing it
    // here proves the producer indexed the bytes it actually published.
    uint64_t identity = 14695981039346656037ull;
    for (size_t byte = offset + 32; byte < cursor; ++byte)
    {
        identity ^= static_cast<uint64_t>(data[byte]);
        identity *= 1099511628211ull;
    }
    if (identity != deformation->identity)
    {
        return false;
    }
    deformation->flags = flags;
    deformation->present = true;
    return true;
}

/// Evaluates a decoded rig in the order the ABI documents and compares it with
/// the deformed points published beside it. This is the analytic gate the whole
/// deformation block exists for: a rig that does not reproduce the CPU answer
/// is a rig a consumer must not evaluate.
bool DeformationReproducesPoints(
    const ParsedDeformation& deformation,
    const std::vector<float>& published,
    float* worstError)
{
    const size_t pointCount = deformation.bind_point_count;
    if (published.size() != pointCount * 3)
    {
        return false;
    }
    std::vector<float> offsets(pointCount * 3, 0.0F);
    for (const std::array<float, 3>& range : deformation.blend_range)
    {
        const size_t first = static_cast<size_t>(range[0]);
        const size_t count = static_cast<size_t>(range[1]);
        for (size_t entry = 0; entry < count; ++entry)
        {
            const size_t delta = first + entry;
            const size_t point = deformation.blend_delta_points[delta];
            for (size_t component = 0; component < 3; ++component)
            {
                offsets[(point * 3) + component] +=
                    range[2] *
                    deformation.blend_delta_offsets[(delta * 3) + component];
            }
        }
    }

    const auto transform =
        [](const float* matrix, const float* point, float* out)
    {
        out[0] = (point[0] * matrix[0]) + (point[1] * matrix[4]) +
            (point[2] * matrix[8]) + matrix[12];
        out[1] = (point[0] * matrix[1]) + (point[1] * matrix[5]) +
            (point[2] * matrix[9]) + matrix[13];
        out[2] = (point[0] * matrix[2]) + (point[1] * matrix[6]) +
            (point[2] * matrix[10]) + matrix[14];
    };

    bool matches = true;
    for (size_t point = 0; point < pointCount; ++point)
    {
        float bind[3];
        for (size_t component = 0; component < 3; ++component)
        {
            bind[component] = deformation.bind_points[(point * 3) + component] +
                offsets[(point * 3) + component];
        }
        float bound[3];
        transform(deformation.geom_bind_transform.data(), bind, bound);
        float skinned[3] = {0.0F, 0.0F, 0.0F};
        for (uint32_t influence = 0;
             influence < deformation.influences_per_point;
             ++influence)
        {
            const size_t slot =
                (point * deformation.influences_per_point) + influence;
            const float weight = deformation.joint_weights[slot];
            if (weight == 0.0F)
            {
                continue;
            }
            float moved[3];
            transform(
                deformation.joint_matrices.data() +
                    (static_cast<size_t>(deformation.joint_indices[slot]) * 16),
                bound,
                moved);
            for (size_t component = 0; component < 3; ++component)
            {
                skinned[component] += moved[component] * weight;
            }
        }
        for (size_t component = 0; component < 3; ++component)
        {
            const float expected = published[(point * 3) + component];
            const float error = std::fabs(skinned[component] - expected);
            const float scale = std::fmax(1.0F, std::fabs(expected));
            const float relative = error / scale;
            if (relative > *worstError)
            {
                *worstError = relative;
            }
            if (!(relative <= OPENUSD_SILK_DEFORMATION_VERIFY_TOLERANCE))
            {
                matches = false;
            }
        }
    }
    return matches;
}

bool Utf8PathLess(const std::string& left, const std::string& right)
{
    return std::lexicographical_compare(
        left.begin(),
        left.end(),
        right.begin(),
        right.end(),
        [](char leftByte, char rightByte)
        {
            return static_cast<unsigned char>(leftByte) <
                static_cast<unsigned char>(rightByte);
        });
}

ParsedPage ParseCommands(const uint8_t* data, size_t size)
{
    ParsedPage result;
    size_t offset = 0;
    while (offset + 8 <= size)
    {
        uint32_t type = 0;
        uint32_t byteSize = 0;
        if (!ReadValue(data, size, offset, &type) ||
            !ReadValue(data, size, offset + 4, &byteSize) ||
            byteSize < 8 ||
            static_cast<size_t>(byteSize) > size - offset)
        {
            std::cerr << "Malformed command at offset " << offset << "\n";
            break;
        }

        if (type == OPENUSD_SILK_COMMAND_FRAME)
        {
            ++result.frame_count;
            constexpr size_t payloadOffset = 8;
            constexpr size_t viewOffset = payloadOffset + 8;
            constexpr size_t projectionOffset = viewOffset + (16 * sizeof(double));
            if (!ReadValue(
                    data,
                    size,
                    offset + payloadOffset,
                    &result.frame_width) ||
                !ReadValue(
                    data,
                    size,
                    offset + payloadOffset + sizeof(int32_t),
                    &result.frame_height) ||
                projectionOffset + (16 * sizeof(double)) >
                    static_cast<size_t>(byteSize))
            {
                std::cerr << "Malformed FRAME command.\n";
                break;
            }
            std::memcpy(
                result.frame_view.data(),
                data + offset + viewOffset,
                sizeof(result.frame_view));
            std::memcpy(
                result.frame_projection.data(),
                data + offset + projectionOffset,
                sizeof(result.frame_projection));

            // The direct-light count the LIGHT_LINK masks index. Reading it here
            // keeps a link-table assertion from passing for the wrong reason,
            // because a table that indexes no lights masks nothing.
            constexpr size_t lightCountOffset = 536;
            if (static_cast<size_t>(byteSize) >= lightCountOffset + sizeof(uint32_t))
            {
                static_cast<void>(ReadValue(
                    data,
                    size,
                    offset + lightCountOffset,
                    &result.frame_light_count));
            }

            // The ABI v21 dome table. It follows the eight fixed light entries
            // and the scene-wide ambient term: 552 header, 8 * 176 lights, 16
            // ambient.
            constexpr size_t ambientOffset = 552 + (8 * 176);
            constexpr size_t domeCountOffset = ambientOffset + 16;
            constexpr size_t domeTableOffset = domeCountOffset + 16;
            if (static_cast<size_t>(byteSize) >=
                domeTableOffset + (OPENUSD_SILK_MAX_DOME_LIGHTS * 32))
            {
                for (size_t component = 0; component < 3; ++component)
                {
                    static_cast<void>(ReadValue(
                        data,
                        size,
                        offset + ambientOffset + (component * sizeof(float)),
                        &result.frame_ambient[component]));
                }
                static_cast<void>(ReadValue(
                    data,
                    size,
                    offset + domeCountOffset,
                    &result.frame_dome_count));
                result.frame_domes.clear();
                for (uint32_t dome = 0; dome < result.frame_dome_count &&
                    dome < OPENUSD_SILK_MAX_DOME_LIGHTS; ++dome)
                {
                    const size_t entry = domeTableOffset + (static_cast<size_t>(dome) * 32);
                    float red = 0.0f;
                    float green = 0.0f;
                    float blue = 0.0f;
                    uint32_t flags = 0;
                    if (ReadValue(data, size, offset + entry, &red) &&
                        ReadValue(data, size, offset + entry + 4, &green) &&
                        ReadValue(data, size, offset + entry + 8, &blue) &&
                        ReadValue(data, size, offset + entry + 16, &flags))
                    {
                        result.frame_domes.emplace_back(red, green, blue, flags);
                    }
                }
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MESH_UPSERT)
        {
            ++result.mesh_upsert_count;
            uint64_t stableHash = 0;
            int32_t primId = -1;
            int32_t instanceId = -1;
            int32_t instanceIndex = -1;
            uint32_t topologyKind = 0;
            uint64_t topologyRevision = 0;
            uint32_t doubleSided = 0;
            uint32_t cullStyle = 0;
            uint32_t pathSize = 0;
            uint32_t pointCount = 0;
            uint32_t indexCount = 0;
            uint32_t triangleCount = 0;
            uint32_t materialPathSize = 0;
            uint32_t attributeCount = 0;
            uint32_t deformationFlags = 0;
            uint32_t deformationUnsupported = 0;
            uint32_t deformationByteCount = 0;
            uint32_t subprimIdentity = 0;
            uint32_t subprimUnsupported = 0;
            uint32_t pointOriginCount = 0;
            uint32_t cornerEdgeCount = 0;
            uint32_t authoredEdgeCount = 0;
            uint32_t authoredPointCount = 0;
            uint32_t instancerPathByteCount = 0;
            uint32_t instancerContextCount = 0;
            constexpr size_t pathOffset = 268;
            if (ReadValue(data, size, offset + 8, &stableHash) &&
                ReadValue(data, size, offset + 16, &primId) &&
                ReadValue(data, size, offset + 20, &instanceId) &&
                ReadValue(data, size, offset + 24, &instanceIndex) &&
                ReadValue(data, size, offset + 28, &topologyKind) &&
                ReadValue(data, size, offset + 32, &topologyRevision) &&
                ReadValue(data, size, offset + 40, &doubleSided) &&
                ReadValue(data, size, offset + 44, &cullStyle) &&
                ReadValue(data, size, offset + 48, &pathSize) &&
                ReadValue(data, size, offset + 52, &pointCount) &&
                ReadValue(data, size, offset + 56, &indexCount) &&
                ReadValue(data, size, offset + 60, &triangleCount) &&
                ReadValue(data, size, offset + 216, &materialPathSize) &&
                ReadValue(data, size, offset + 220, &attributeCount) &&
                ReadValue(data, size, offset + 224, &deformationFlags) &&
                ReadValue(data, size, offset + 228, &deformationUnsupported) &&
                ReadValue(data, size, offset + 232, &deformationByteCount) &&
                ReadValue(data, size, offset + 236, &subprimIdentity) &&
                ReadValue(data, size, offset + 240, &subprimUnsupported) &&
                ReadValue(data, size, offset + 244, &pointOriginCount) &&
                ReadValue(data, size, offset + 248, &cornerEdgeCount) &&
                ReadValue(data, size, offset + 252, &authoredEdgeCount) &&
                ReadValue(data, size, offset + 256, &authoredPointCount) &&
                ReadValue(data, size, offset + 260, &instancerPathByteCount) &&
                ReadValue(data, size, offset + 264, &instancerContextCount))
            {
                size_t pointBytes = 0;
                size_t indexBytes = 0;
                size_t subprimBytes = 0;
                size_t expectedSize = pathOffset;
                bool sizesValid =
                    MultiplySize(pointCount, 3 * sizeof(float), &pointBytes) &&
                    MultiplySize(indexCount, sizeof(uint32_t), &indexBytes) &&
                    MultiplySize(
                        triangleCount,
                        sizeof(uint32_t),
                        &subprimBytes) &&
                    AddSize(&expectedSize, pathSize) &&
                    AddSize(&expectedSize, pointBytes) &&
                    AddSize(&expectedSize, indexBytes) &&
                    AddSize(&expectedSize, subprimBytes) &&
                    AddSize(&expectedSize, materialPathSize);
                // Walk the ABI v4 attribute table so the exact-size check stays
                // exact rather than being relaxed to accommodate it.
                std::vector<ParsedAttribute> attributes;
                for (uint32_t attribute = 0;
                     sizesValid && attribute < attributeCount;
                     ++attribute)
                {
                    uint32_t componentCount = 0;
                    uint32_t nameSize = 0;
                    uint32_t elementCount = 0;
                    size_t dataBytes = 0;
                    const size_t entry = offset + expectedSize;
                    uint32_t semantic = 0;
                    uint32_t interpolation = 0;
                    sizesValid =
                        ReadValue(data, size, entry, &semantic) &&
                        ReadValue(data, size, entry + 4, &componentCount) &&
                        ReadValue(data, size, entry + 8, &interpolation) &&
                        ReadValue(data, size, entry + 12, &nameSize) &&
                        ReadValue(data, size, entry + 16, &elementCount) &&
                        MultiplySize(elementCount, componentCount, &dataBytes) &&
                        MultiplySize(dataBytes, sizeof(float), &dataBytes) &&
                        AddSize(&expectedSize, 20) &&
                        AddSize(&expectedSize, nameSize) &&
                        AddSize(&expectedSize, dataBytes);
                    if (!sizesValid)
                    {
                        break;
                    }
                    ParsedAttribute parsed;
                    parsed.name.assign(
                        reinterpret_cast<const char*>(data + entry + 20),
                        nameSize);
                    parsed.semantic = semantic;
                    parsed.componentCount = componentCount;
                    parsed.interpolation = interpolation;
                    parsed.elementCount = elementCount;
                    if (elementCount != 0 && componentCount != 0)
                    {
                        ReadValue(
                            data,
                            size,
                            entry + 20 + nameSize,
                            &parsed.firstValue);
                        const size_t valueCount =
                            static_cast<size_t>(elementCount) * componentCount;
                        parsed.values.resize(valueCount);
                        for (size_t value = 0; value < valueCount; ++value)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    entry + 20 + nameSize +
                                        (value * sizeof(float)),
                                    &parsed.values[value]))
                            {
                                sizesValid = false;
                                break;
                            }
                        }
                    }
                    attributes.push_back(std::move(parsed));
                }
                // The deformation block closes the record, so decoding it is
                // what keeps the exact-size check exact after ABI v20.
                ParsedDeformation deformation;
                const size_t deformationOffset = offset + expectedSize;
                if (sizesValid && deformationByteCount != 0)
                {
                    sizesValid =
                        DecodeDeformation(
                            data,
                            size,
                            deformationOffset,
                            deformationByteCount,
                            deformationFlags,
                            pointCount,
                            &deformation) &&
                        AddSize(&expectedSize, deformationByteCount);
                    if (!sizesValid)
                    {
                        result.deformation_valid = false;
                    }
                }
                const uint32_t indicesPerPrimitive =
                    topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST
                        ? 3u
                        : (topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ? 2u : 1u);
                // The ABI v22 subprim-identity tables close the record after the
                // deformation block, so decoding them is what keeps the
                // exact-size check exact. Each table is either absent or
                // complete, and every entry either names an authored component
                // inside the count the record declares or is the explicit
                // "no authored counterpart" sentinel.
                size_t pointOriginBytes = 0;
                size_t cornerEdgeBytes = 0;
                const size_t pointOriginOffset = offset + expectedSize;
                if (sizesValid)
                {
                    sizesValid =
                        MultiplySize(
                            pointOriginCount, sizeof(uint32_t), &pointOriginBytes) &&
                        MultiplySize(
                            cornerEdgeCount, sizeof(uint32_t), &cornerEdgeBytes) &&
                        AddSize(&expectedSize, pointOriginBytes) &&
                        AddSize(&expectedSize, cornerEdgeBytes) &&
                        AddSize(&expectedSize, instancerPathByteCount);
                }
                const size_t cornerEdgeOffset = pointOriginOffset + pointOriginBytes;
                const uint32_t cornersPerPrimitive =
                    topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST
                        ? 3u
                        : (topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ? 1u : 0u);
                bool subprimValid = sizesValid &&
                    (subprimIdentity & ~(OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
                        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
                        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT)) == 0 &&
                    (subprimUnsupported &
                        ~(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_REFINED_SUBDIVISION |
                            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE |
                            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY |
                            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET)) == 0 &&
                    ((subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_POINT) != 0) ==
                        (pointOriginCount != 0) &&
                    ((subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE) != 0) ==
                        (cornerEdgeCount != 0) &&
                    (pointOriginCount == 0 || pointOriginCount == pointCount) &&
                    (cornerEdgeCount == 0 ||
                        (cornersPerPrimitive != 0 &&
                            cornerEdgeCount ==
                                triangleCount * cornersPerPrimitive)) &&
                    (pointOriginCount != 0 || authoredPointCount == 0) &&
                    (cornerEdgeCount != 0 || authoredEdgeCount == 0);
                std::vector<uint32_t> parsedPointOrigins;
                parsedPointOrigins.reserve(pointOriginCount);
                for (uint32_t entry = 0; subprimValid && entry < pointOriginCount;
                     ++entry)
                {
                    uint32_t origin = 0;
                    subprimValid =
                        ReadValue(
                            data,
                            size,
                            pointOriginOffset + (entry * sizeof(uint32_t)),
                            &origin) &&
                        (origin == OPENUSD_SILK_SUBPRIM_NONE ||
                            origin < authoredPointCount);
                    parsedPointOrigins.push_back(origin);
                }
                for (uint32_t entry = 0; subprimValid && entry < cornerEdgeCount;
                     ++entry)
                {
                    uint32_t edge = 0;
                    subprimValid =
                        ReadValue(
                            data,
                            size,
                            cornerEdgeOffset + (entry * sizeof(uint32_t)),
                            &edge) &&
                        (edge == OPENUSD_SILK_SUBPRIM_NONE ||
                            edge < authoredEdgeCount);
                }
                if (!subprimValid)
                {
                    result.subprim_identity_valid = false;
                }

                // The ABI v23 instancer-context block closes the record, so
                // walking it is what keeps the exact-size check exact. Every
                // entry names an absolute path and a non-negative index, the
                // chain is published exactly when the record belongs to an
                // instancer, and it ends at the instancer the record separately
                // names.
                const size_t instancerPathOffset =
                    cornerEdgeOffset + cornerEdgeBytes;
                std::vector<std::pair<std::string, int32_t>> parsedContext;
                bool contextValid = sizesValid &&
                    ((instancerContextCount != 0) ==
                        (instancerPathByteCount != 0)) &&
                    instancerContextCount <=
                        OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES;
                size_t contextOffset =
                    instancerPathOffset + instancerPathByteCount;
                for (uint32_t entry = 0;
                     contextValid && entry < instancerContextCount;
                     ++entry)
                {
                    uint32_t entryPathSize = 0;
                    int32_t entryIndex = 0;
                    contextValid =
                        ReadValue(data, size, contextOffset, &entryPathSize) &&
                        ReadValue(data, size, contextOffset + 4, &entryIndex) &&
                        entryIndex >= 0 &&
                        entryPathSize != 0 &&
                        contextOffset + 8 + entryPathSize <= offset + byteSize &&
                        data[contextOffset + 8] == '/' &&
                        AddSize(&expectedSize, 8) &&
                        AddSize(&expectedSize, entryPathSize);
                    if (!contextValid)
                    {
                        break;
                    }
                    parsedContext.emplace_back(
                        std::string(
                            reinterpret_cast<const char*>(data + contextOffset + 8),
                            entryPathSize),
                        entryIndex);
                    contextOffset += 8 + entryPathSize;
                }
                if (contextValid && instancerContextCount != 0)
                {
                    const std::string instancerPath(
                        reinterpret_cast<const char*>(data + instancerPathOffset),
                        instancerPathByteCount);
                    contextValid = parsedContext.back().first == instancerPath;
                }
                sizesValid = sizesValid && contextValid;
                sizesValid = sizesValid && subprimValid &&
                    expectedSize == byteSize &&
                    ((instancerPathByteCount != 0) == (instanceId != 0)) &&
                    (topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
                     topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
                     topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST) &&
                    static_cast<uint64_t>(triangleCount) *
                        indicesPerPrimitive == indexCount &&
                    doubleSided <= 1 &&
                    cullStyle <= OPENUSD_SILK_CULL_STYLE_FRONT_UNLESS_DOUBLE_SIDED;
                if (!sizesValid)
                {
                    result.mesh_identity_valid = false;
                }
                else
                {
                    const std::string path(
                        reinterpret_cast<const char*>(data + offset + pathOffset),
                        pathSize);
                    result.upsert_paths.push_back(path);
                    result.mesh_point_counts.push_back(pointCount);
                    result.mesh_index_counts.push_back(indexCount);
                    result.mesh_attribute_counts.push_back(attributeCount);
                    result.mesh_cull_styles.push_back(cullStyle);
                    result.mesh_topology_kinds.push_back(topologyKind);
                    result.mesh_topology_revisions.push_back(topologyRevision);
                    result.mesh_triangle_counts.push_back(triangleCount);
                    result.mesh_subprim_identities.push_back(subprimIdentity);
                    result.mesh_subprim_unsupported.push_back(subprimUnsupported);
                    result.mesh_point_origins.push_back(parsedPointOrigins);
                    result.mesh_authored_point_counts.push_back(authoredPointCount);
                    result.deformation_unsupported_mask |= deformationUnsupported;
                    if (deformation.present)
                    {
                        ++result.deformation_published_count;
                        const size_t publishedPointsOffset =
                            offset + pathOffset + pathSize;
                        std::vector<float> published(
                            static_cast<size_t>(pointCount) * 3);
                        for (size_t component = 0;
                             component < published.size();
                             ++component)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    publishedPointsOffset +
                                        (component * sizeof(float)),
                                    &published[component]))
                            {
                                result.deformation_valid = false;
                                published.clear();
                                break;
                            }
                        }
                        if (!published.empty() &&
                            !DeformationReproducesPoints(
                                deformation,
                                published,
                                &result.deformation_worst_point_error))
                        {
                            result.deformation_reproduces_points = false;
                        }
                    }
                    ParsedMeshIdentity identity{
                        path,
                        primId,
                        topologyRevision,
                        instanceId,
                        instanceIndex,
                        pointCount,
                        {},
                        parsedContext};
                    std::memcpy(
                        identity.transform.data(),
                        data + offset + 80,
                        sizeof(identity.transform));
                    result.mesh_identities.push_back(std::move(identity));
                    result.mesh_identity_valid &=
                        !path.empty() &&
                        path.front() == '/' &&
                        stableHash == ComputeStableHash(path) &&
                        primId >= 0 &&
                        (topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
                         topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
                         topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST) &&
                        topologyRevision != 0;
                    result.instance_fields_zero &=
                        instanceId == 0 && instanceIndex == 0;

                    const size_t pointsOffset =
                        offset + pathOffset + pathSize;
                    const size_t subprimOffset =
                        pointsOffset + pointBytes + indexBytes;
                    std::vector<uint32_t> subprims(triangleCount);
                    for (uint32_t triangle = 0;
                         triangle < triangleCount;
                         ++triangle)
                    {
                        if (!ReadValue(
                                data,
                                size,
                                subprimOffset +
                                    (triangle * sizeof(uint32_t)),
                                &subprims[triangle]))
                        {
                            result.mesh_identity_valid = false;
                            break;
                        }
                    }
                    result.mesh_primitive_subprims.push_back(subprims);

                    float firstX = 0.0F;
                    if (path == SharedMeshPath && pointCount > 0 &&
                        ReadValue(data, size, pointsOffset, &firstX))
                    {
                        result.found_shared_upsert = true;
                        result.shared_first_x = firstX;
                        result.shared_stable_hash = stableHash;
                        result.shared_prim_id = primId;
                        result.shared_topology_kind = topologyKind;
                        result.shared_topology_revision = topologyRevision;
                        result.shared_triangle_count = triangleCount;
                        result.shared_subprims = std::move(subprims);
                        result.shared_material_binding.assign(
                            reinterpret_cast<const char*>(
                                data + subprimOffset + subprimBytes),
                            materialPathSize);
                    }
                    else if (path == TopologyMeshPath)
                    {
                        result.found_topology_upsert = true;
                        result.topology_subprims = std::move(subprims);
                    }
                    else if (path == PrimvarMeshPath)
                    {
                        result.found_primvar_mesh = true;
                        result.primvar_mesh_attributes = std::move(attributes);
                    }
                    else if (path == BasisCurvesPath ||
                             path == VaryingWidthCurvesPath ||
                             path == UniformWidthCurvesPath ||
                             path == RightHandedMeshPath ||
                             path == LeftHandedMeshPath ||
                             path == LeftHandedFaceVaryingMeshPath ||
                             path == ExpandedTopologyToggleMeshPath ||
                             path == StrayPointMeshPath ||
                             path == InbetweenMeshPath ||
                             path == SkinnedNormalMeshPath ||
                             path == FaceVaryingNormalMeshPath)
                    {
                        ParsedCurves* target = &result.basis_curves;
                        if (path == VaryingWidthCurvesPath)
                        {
                            target = &result.varying_width_curves;
                        }
                        else if (path == UniformWidthCurvesPath)
                        {
                            target = &result.uniform_width_curves;
                        }
                        else if (path == RightHandedMeshPath)
                        {
                            target = &result.right_handed_mesh;
                        }
                        else if (path == LeftHandedMeshPath)
                        {
                            target = &result.left_handed_mesh;
                        }
                        else if (path == LeftHandedFaceVaryingMeshPath)
                        {
                            target = &result.left_handed_face_varying_mesh;
                        }
                        else if (path == ExpandedTopologyToggleMeshPath)
                        {
                            target = &result.expanded_topology_toggle_mesh;
                        }
                        else if (path == StrayPointMeshPath)
                        {
                            target = &result.stray_point_mesh;
                        }
                        else if (path == InbetweenMeshPath)
                        {
                            target = &result.inbetween_mesh;
                        }
                        else if (path == SkinnedNormalMeshPath)
                        {
                            target = &result.skinned_normal_mesh;
                        }
                        else if (path == FaceVaryingNormalMeshPath)
                        {
                            target = &result.face_varying_normal_mesh;
                        }
                        ParsedCurves& curves = *target;
                        curves.found = true;
                        curves.topology_kind = topologyKind;
                        curves.topology_revision = topologyRevision;
                        curves.triangle_count = triangleCount;
                        curves.point_origins = parsedPointOrigins;
                        curves.subprims = std::move(subprims);
                        curves.attributes = std::move(attributes);
                        curves.points.resize(
                            static_cast<size_t>(pointCount) * 3);
                        for (size_t component = 0;
                             component < curves.points.size();
                             ++component)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    pointsOffset +
                                        (component * sizeof(float)),
                                    &curves.points[component]))
                            {
                                result.mesh_identity_valid = false;
                                break;
                            }
                        }
                        curves.indices.resize(indexCount);
                        const size_t indicesOffset = pointsOffset + pointBytes;
                        for (uint32_t index = 0; index < indexCount; ++index)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    indicesOffset +
                                        (static_cast<size_t>(index) *
                                         sizeof(uint32_t)),
                                    &curves.indices[index]))
                            {
                                result.mesh_identity_valid = false;
                                break;
                            }
                        }
                    }
                    else if (path == BlendShapeMeshPath && pointCount > 0)
                    {
                        result.found_blend_shape_mesh = true;
                        ReadValue(
                            data,
                            size,
                            pointsOffset,
                            &result.blend_shape_first_x);
                    }
                }
            }
            else
            {
                result.mesh_identity_valid = false;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MESH_REMOVE)
        {
            ++result.mesh_remove_count;
            uint32_t pathSize = 0;
            constexpr size_t pathSizeOffset = 8 + 8 + 4;
            constexpr size_t pathOffset = 8 + 8 + 4 + 4;
            if (ReadValue(data, size, offset + pathSizeOffset, &pathSize) &&
                pathOffset <= byteSize &&
                static_cast<size_t>(pathSize) <=
                    static_cast<size_t>(byteSize) - pathOffset)
            {
                const std::string path(
                    reinterpret_cast<const char*>(data + offset + pathOffset),
                    pathSize);
                int32_t removedInstanceIndex = 0;
                ReadValue(data, size, offset + 16, &removedInstanceIndex);
                result.remove_paths.push_back(path);
                result.remove_instance_indices.push_back(removedInstanceIndex);
                result.found_shared_remove = path == SharedMeshPath;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            ++result.material_upsert_count;
            uint64_t stableHash = 0;
            uint32_t pathSize = 0;
            uint32_t surfaceKind = 0;
            uint32_t scalarCount = 0;
            uint32_t textureCount = 0;
            constexpr size_t pathOffset = 32;
            if (!ReadValue(data, size, offset + 8, &stableHash) ||
                !ReadValue(data, size, offset + 16, &pathSize) ||
                !ReadValue(data, size, offset + 20, &surfaceKind) ||
                !ReadValue(data, size, offset + 24, &scalarCount) ||
                !ReadValue(data, size, offset + 28, &textureCount))
            {
                result.material_valid = false;
                offset += byteSize;
                ++result.parsed_count;
                continue;
            }
            size_t cursor = pathOffset;
            bool valid = AddSize(&cursor, pathSize);
            const std::string path(
                reinterpret_cast<const char*>(data + offset + pathOffset),
                pathSize);
            float roughness = -1.0F;
            std::vector<std::tuple<uint32_t, float>> scalarEntries;
            for (uint32_t scalar = 0; valid && scalar < scalarCount; ++scalar)
            {
                uint32_t parameter = 0;
                uint32_t componentCount = 0;
                const size_t entry = offset + cursor;
                valid = ReadValue(data, size, entry, &parameter) &&
                    ReadValue(data, size, entry + 4, &componentCount) &&
                    componentCount >= 1 && componentCount <= 4;
                if (valid)
                {
                    float first = 0.0F;
                    if (ReadValue(data, size, entry + 8, &first))
                    {
                        scalarEntries.emplace_back(parameter, first);
                    }
                }
                if (valid && parameter == OPENUSD_SILK_MATERIAL_ROUGHNESS)
                {
                    valid = ReadValue(data, size, entry + 8, &roughness);
                }
                size_t valueBytes = 0;
                valid = valid &&
                    MultiplySize(componentCount, sizeof(float), &valueBytes) &&
                    AddSize(&cursor, 8) &&
                    AddSize(&cursor, valueBytes);
            }
            std::string textureAsset;
            std::string textureUv;
            uint32_t textureParameter = 0;
            std::vector<std::tuple<uint32_t, uint32_t, std::string, std::string>> textures;
            for (uint32_t texture = 0; valid && texture < textureCount; ++texture)
            {
                uint32_t parameter = 0;
                uint32_t assetSize = 0;
                uint32_t uvSize = 0;
                uint32_t componentCount = 0;
                uint32_t outputChannel = 0;
                uint32_t compositeOp = 0;
                const size_t entry = offset + cursor;
                valid = ReadValue(data, size, entry, &parameter) &&
                    ReadValue(data, size, entry + 16, &assetSize) &&
                    ReadValue(data, size, entry + 20, &uvSize) &&
                    ReadValue(data, size, entry + 24, &componentCount) &&
                    ReadValue(data, size, entry + 76, &outputChannel) &&
                    ReadValue(data, size, entry + 80, &compositeOp) &&
                    compositeOp <= OPENUSD_SILK_COMPOSITE_MIX &&
                    assetSize != 0 &&
                    outputChannel <= OPENUSD_SILK_TEXTURE_CHANNEL_RGB &&
                    (outputChannel == OPENUSD_SILK_TEXTURE_CHANNEL_RGB
                            ? componentCount == 3
                            : componentCount == 1);
                if (valid)
                {
                    std::string asset(
                        reinterpret_cast<const char*>(data + entry + 88),
                        assetSize);
                    if (texture == 0)
                    {
                        textureParameter = parameter;
                        textureAsset = asset;
                        textureUv.assign(
                            reinterpret_cast<const char*>(
                                data + entry + 88 + assetSize),
                            uvSize);
                    }
                    std::string entryUv(
                        reinterpret_cast<const char*>(data + entry + 88 + assetSize),
                        uvSize);
                    textures.emplace_back(
                        parameter, outputChannel, std::move(asset), std::move(entryUv));
                }
                valid = valid &&
                    AddSize(&cursor, 88) &&
                    AddSize(&cursor, assetSize) &&
                    AddSize(&cursor, uvSize);
            }
            uint32_t generatedFragmentSize = 0;
            if (valid)
            {
                valid = ReadValue(data, size, offset + cursor, &generatedFragmentSize) &&
                    (generatedFragmentSize % sizeof(uint32_t)) == 0 &&
                    AddSize(&cursor, sizeof(uint32_t)) &&
                    AddSize(&cursor, generatedFragmentSize);
            }
            uint32_t generatedMslSourceSize = 0;
            if (valid)
            {
                valid = ReadValue(data, size, offset + cursor, &generatedMslSourceSize) &&
                    AddSize(&cursor, sizeof(uint32_t)) &&
                    AddSize(&cursor, generatedMslSourceSize);
            }
            std::array<float, 6> uvTransform{};
            for (size_t element = 0; valid && element < uvTransform.size(); ++element)
            {
                valid = ReadValue(
                    data,
                    size,
                    offset + cursor + (element * sizeof(float)),
                    &uvTransform[element]);
            }
            valid = valid && AddSize(&cursor, 6 * sizeof(float));
            // Requiring the exact size means an unaccounted byte fails here rather
            // than surfacing as a silently mis-read parameter later.
            valid = valid && cursor == byteSize &&
                !path.empty() && path.front() == '/' &&
                stableHash == ComputeStableHash(path);
            result.material_valid &= valid;
            if (valid)
            {
                result.found_material_upsert = true;
                result.material_path = path;
                result.material_surface_kind = surfaceKind;
                result.material_scalar_count = scalarCount;
                result.material_texture_count = textureCount;
                result.material_roughness = roughness;
                result.materials.push_back(
                    ParsedMaterialRecord{path, surfaceKind, scalarEntries, textures});
                result.material_scalars = std::move(scalarEntries);
                result.material_texture_asset = std::move(textureAsset);
                result.material_texture_uv = std::move(textureUv);
                result.material_texture_parameter = textureParameter;
                result.material_textures = std::move(textures);
                result.material_uv_transform = uvTransform;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MATERIAL_REMOVE)
        {
            ++result.material_remove_count;
            uint32_t pathSize = 0;
            constexpr size_t pathOffset = 20;
            if (ReadValue(data, size, offset + 16, &pathSize) &&
                pathOffset <= byteSize &&
                static_cast<size_t>(pathSize) ==
                    static_cast<size_t>(byteSize) - pathOffset)
            {
                result.material_remove_paths.emplace_back(
                    reinterpret_cast<const char*>(data + offset + pathOffset),
                    pathSize);
            }
            else
            {
                result.material_valid = false;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_ENVIRONMENT_UPSERT ||
            type == OPENUSD_SILK_COMMAND_ENVIRONMENT_REMOVE)
        {
            // The ABI v21 dome index is decoded, and nothing else is: it is the
            // bit a LIGHT_LINK dome_mask sets, so a probe that asserts on dome
            // linking has to be able to see it. The rest of the record's layout
            // is owned by the managed round trip in
            // tests/OpenUsd.Rendering.Tests/SilkDomeEnvironmentTests.cs.
            if (type == OPENUSD_SILK_COMMAND_ENVIRONMENT_UPSERT)
            {
                uint32_t pathSize = 0;
                uint32_t unsupported = 0;
                uint32_t domeIndex = OPENUSD_SILK_DOME_INDEX_NONE;
                if (ReadValue(data, size, offset + 16, &pathSize) &&
                    ReadValue(data, size, offset + 32, &unsupported) &&
                    ReadValue(data, size, offset + 36, &domeIndex) &&
                    pathSize > 0 &&
                    static_cast<size_t>(byteSize) >= 200 + static_cast<size_t>(pathSize))
                {
                    result.environment_dome_indices.emplace_back(
                        std::string(
                            reinterpret_cast<const char*>(data + offset + 200),
                            pathSize),
                        domeIndex,
                        unsupported);
                }
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_LIGHT_LINK)
        {
            ++result.light_link_count;
            uint32_t entryCount = 0;
            uint32_t lightCount = 0;
            uint32_t unsupported = 0;
            uint32_t domeCount = 0;
            bool valid = ReadValue(data, size, offset + 8, &entryCount) &&
                ReadValue(data, size, offset + 12, &lightCount) &&
                ReadValue(data, size, offset + 16, &unsupported) &&
                ReadValue(data, size, offset + 20, &domeCount) &&
                lightCount <= OPENUSD_SILK_MAX_FRAME_LIGHTS &&
                domeCount <= OPENUSD_SILK_MAX_DOME_LIGHTS &&
                entryCount <= OPENUSD_SILK_MAX_LINK_ENTRIES;
            size_t cursor = 24;
            for (uint32_t entry = 0; valid && entry < entryCount; ++entry)
            {
                uint32_t lightMask = 0;
                uint32_t shadowMask = 0;
                uint32_t domeMask = 0;
                int32_t instanceIndex = 0;
                uint32_t pathSize = 0;
                valid = ReadValue(data, size, offset + cursor, &lightMask) &&
                    ReadValue(data, size, offset + cursor + 4, &shadowMask) &&
                    ReadValue(data, size, offset + cursor + 8, &domeMask) &&
                    ReadValue(data, size, offset + cursor + 12, &instanceIndex) &&
                    ReadValue(data, size, offset + cursor + 16, &pathSize) &&
                    pathSize > 0 &&
                    instanceIndex >= OPENUSD_SILK_LINK_ALL_INSTANCES &&
                    // Bits at or above the published counts must be zero, which
                    // is what keeps a mask from naming a light or a dome the
                    // frame never published.
                    (lightCount >= 32 || lightMask < (1u << lightCount)) &&
                    (lightCount >= 32 || shadowMask < (1u << lightCount)) &&
                    (domeCount >= 32 || domeMask < (1u << domeCount)) &&
                    AddSize(&cursor, 20) &&
                    cursor <= byteSize &&
                    static_cast<size_t>(pathSize) <= byteSize - cursor;
                if (!valid)
                {
                    break;
                }
                std::string path(
                    reinterpret_cast<const char*>(data + offset + cursor),
                    pathSize);
                // The masks are deliberately not intersected: UsdLux resolves
                // collection:lightLink and collection:shadowLink separately, so a
                // prim that casts a light's shadow without being lit by it is a
                // valid combination rather than a malformed one.
                valid = !path.empty() && path.front() == '/' &&
                    AddSize(&cursor, pathSize);
                result.light_links.emplace_back(
                    std::move(path),
                    instanceIndex,
                    lightMask,
                    shadowMask,
                    domeMask);
            }
            valid = valid && cursor == byteSize;
            result.light_link_valid = result.light_link_valid && valid;
            result.light_link_light_count = lightCount;
            result.light_link_unsupported = unsupported;
            result.light_link_dome_count = domeCount;
        }
        else if (type == OPENUSD_SILK_COMMAND_SHADOW)
        {
            ++result.shadow_count;
            uint32_t descriptorCount = 0;
            uint32_t lightCount = 0;
            uint32_t unsupported = 0;
            uint32_t headerReserved = 1;
            bool valid = ReadValue(data, size, offset + 8, &descriptorCount) &&
                ReadValue(data, size, offset + 12, &lightCount) &&
                ReadValue(data, size, offset + 16, &unsupported) &&
                ReadValue(data, size, offset + 20, &headerReserved) &&
                headerReserved == 0u &&
                lightCount <= OPENUSD_SILK_MAX_FRAME_LIGHTS &&
                descriptorCount <= OPENUSD_SILK_MAX_SHADOW_MAPS &&
                byteSize == 24 + (static_cast<size_t>(descriptorCount) * 288);
            for (uint32_t entry = 0; valid && entry < descriptorCount; ++entry)
            {
                const size_t cursor = 24 + (static_cast<size_t>(entry) * 288);
                uint32_t lightIndex = 0;
                uint32_t mapIndex = 0;
                uint32_t resolution = 0;
                uint32_t flags = 0;
                uint32_t reserved = 1;
                float depthBias = -1.0f;
                float normalBias = -1.0f;
                float pcfRadius = -1.0f;
                valid = ReadValue(data, size, offset + cursor, &lightIndex) &&
                    ReadValue(data, size, offset + cursor + 4, &mapIndex) &&
                    ReadValue(data, size, offset + cursor + 8, &resolution) &&
                    ReadValue(data, size, offset + cursor + 12, &flags) &&
                    ReadValue(data, size, offset + cursor + 272, &depthBias) &&
                    ReadValue(data, size, offset + cursor + 276, &normalBias) &&
                    ReadValue(data, size, offset + cursor + 280, &pcfRadius) &&
                    ReadValue(data, size, offset + cursor + 284, &reserved) &&
                    lightIndex < lightCount &&
                    mapIndex == entry &&
                    resolution >= OPENUSD_SILK_MIN_SHADOW_MAP_RESOLUTION &&
                    resolution <= OPENUSD_SILK_MAX_SHADOW_MAP_RESOLUTION &&
                    (resolution & (resolution - 1u)) == 0u &&
                    (flags & ~(OPENUSD_SILK_SHADOW_FLAG_ORTHOGRAPHIC |
                        OPENUSD_SILK_SHADOW_FLAG_CASTER_LINKED)) == 0u &&
                    depthBias >= 0.0f && normalBias >= 0.0f && pcfRadius >= 0.0f &&
                    reserved == 0u;

                // Every matrix element must be finite: a light-space projection
                // with a non-finite element produces a shadow map of nothing and
                // is exactly the value a consumer cannot diagnose from pixels.
                for (int element = 0; valid && element < 32; ++element)
                {
                    double value = 0.0;
                    valid = ReadValue(
                        data,
                        size,
                        offset + cursor + 16 + (static_cast<size_t>(element) * 8),
                        &value) &&
                        std::isfinite(value);
                }
                if (!valid)
                {
                    break;
                }
                result.shadows.emplace_back(lightIndex, mapIndex, resolution, flags);
            }
            result.shadow_valid = result.shadow_valid && valid;
            result.shadow_descriptor_count = descriptorCount;
            result.shadow_light_count = lightCount;
            result.shadow_unsupported = unsupported;
        }
        else
        {
            std::cerr << "Unknown command type " << type << " at offset " << offset << "\n";
        }

        offset += byteSize;
        ++result.parsed_count;
    }
    return result;
}

HdSilkMeshRecord MakeSceneStateRecord(
    const std::string& path,
    int32_t primId,
    uint64_t topologyRevision = 1)
{
    HdSilkMeshRecord record;
    record.path = path;
    record.primId = primId;
    record.topologyRevision = topologyRevision;
    record.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    record.indices = {0, 1, 2};
    record.triangleSubprims = {0};
    return record;
}

const ParsedMeshIdentity* FindMeshIdentity(
    const ParsedPage& page,
    const std::string& path)
{
    const auto iterator = std::find_if(
        page.mesh_identities.begin(),
        page.mesh_identities.end(),
        [&path](const ParsedMeshIdentity& identity)
        {
            return identity.path == path;
        });
    return iterator == page.mesh_identities.end() ? nullptr : &*iterator;
}

/// Publishes exactly one non-instanced record, mirroring what HdSilkMesh does
/// for a prim without an instancer.
void ReplaceSingleMesh(
    HdSilkSceneState& state,
    const std::string& path,
    HdSilkMeshRecord record)
{
    std::vector<HdSilkMeshRecord> records;
    records.push_back(std::move(record));
    state.ReplaceMeshInstances(path, std::move(records));
}

/// Point-instanced prototypes publish one record per instance under a shared
/// authoritative path. ABI v8 carries geometry only in instance zero, and a
/// shrinking instancer must retire exactly the instances it dropped.
bool VerifyInstancedSceneStateSerialization()
{
    constexpr char InstancedPath[] = "/Instanced";
    HdSilkSceneState state;

    std::vector<HdSilkMeshRecord> instances;
    for (int32_t index = 0; index < 3; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(InstancedPath, 7);
        record.instanceId = 42;
        record.instancerPath = "/Instancer";
        record.instanceIndex = index;
        record.instancerContext = {{"/Instancer", index}};
        record.transform[3] = static_cast<double>(index);
        if (index != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        instances.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(InstancedPath, std::move(instances));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> firstBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage first = ParseCommands(firstBytes.data(), firstBytes.size());
    if (commandCount != 4 ||
        first.mesh_upsert_count != 3 ||
        first.mesh_remove_count != 0 ||
        first.mesh_identities.size() != 3)
    {
        return false;
    }
    for (int32_t index = 0; index < 3; ++index)
    {
        const ParsedMeshIdentity& identity =
            first.mesh_identities[static_cast<size_t>(index)];
        if (identity.path != InstancedPath ||
            identity.instance_id != 42 ||
            identity.instance_index != index ||
            identity.prim_id != 7 ||
            first.mesh_point_counts[static_cast<size_t>(index)] !=
                (index == 0 ? 3u : 0u) ||
            first.mesh_index_counts[static_cast<size_t>(index)] !=
                (index == 0 ? 3u : 0u) ||
            first.mesh_attribute_counts[static_cast<size_t>(index)] != 0u)
        {
            return false;
        }
    }

    // Shrinking to a single instance must retire instances 1 and 2 without
    // disturbing instance 0.
    std::vector<HdSilkMeshRecord> shrunk;
    HdSilkMeshRecord survivor = MakeSceneStateRecord(InstancedPath, 7);
    survivor.instanceId = 42;
    survivor.instancerPath = "/Instancer";
    survivor.instanceIndex = 0;
    survivor.instancerContext = {{"/Instancer", 0}};
    shrunk.push_back(std::move(survivor));
    state.ReplaceMeshInstances(InstancedPath, std::move(shrunk));

    const std::vector<uint8_t> shrunkBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage shrunkPage =
        ParseCommands(shrunkBytes.data(), shrunkBytes.size());
    if (commandCount != 4 ||
        shrunkPage.mesh_upsert_count != 1 ||
        shrunkPage.mesh_remove_count != 2 ||
        shrunkPage.remove_paths !=
            std::vector<std::string>({InstancedPath, InstancedPath}))
    {
        return false;
    }

    // Removing the prim retires the one remaining instance.
    state.RemoveMesh(InstancedPath);
    const std::vector<uint8_t> removedBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage removedPage =
        ParseCommands(removedBytes.data(), removedBytes.size());
    return commandCount == 2 &&
        removedPage.mesh_upsert_count == 0 &&
        removedPage.mesh_remove_count == 1;
}

/// The ABI v23 ordered instancer context survives a page round trip, and a
/// record that contradicts its own chain is refused.
///
/// A composed mixed-radix ordinal cannot be decoded back to a scene instance
/// once more than one level is involved: the record's instancer path names the
/// innermost level and the ordinal counts in a composed space, so the pair
/// describes an instance that does not exist. Only the chain does, which is
/// exactly why the chain has to survive the wire intact and in order.
bool VerifyNestedInstancerContextSerialization()
{
    constexpr char LeafPath[] = "/Nested/Leaf";

    // Two levels. The record keeps the composite ordinal it always carried, and
    // the chain carries each level's own index beside it.
    {
        HdSilkSceneState state;
        HdSilkMeshRecord record = MakeSceneStateRecord(LeafPath, 31);
        record.instanceId = 91;
        record.instancerPath = "/World/Outer/Inner";
        record.instanceIndex = 17;
        record.instancerContext = {
            {"/World/Outer", 2},
            {"/World/Outer/Inner", 5}};
        ReplaceSingleMesh(state, LeafPath, std::move(record));

        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (!page.mesh_identity_valid ||
            page.mesh_identities.size() != 1 ||
            page.mesh_identities[0].instance_index != 17 ||
            page.mesh_identities[0].instancer_context.size() != 2 ||
            page.mesh_identities[0].instancer_context[0] !=
                std::make_pair(std::string("/World/Outer"), 2) ||
            page.mesh_identities[0].instancer_context[1] !=
                std::make_pair(std::string("/World/Outer/Inner"), 5))
        {
            std::cerr << "hdSilk two-level instancer context did not round "
                         "trip.\n";
            return false;
        }
    }

    // Three levels, outermost first. Depth is not special-cased anywhere, so a
    // third level must arrive in the same order with nothing collapsed.
    {
        HdSilkSceneState state;
        HdSilkMeshRecord record = MakeSceneStateRecord(LeafPath, 32);
        record.instanceId = 92;
        record.instancerPath = "/A/B/C";
        record.instanceIndex = 41;
        record.instancerContext = {{"/A", 1}, {"/A/B", 0}, {"/A/B/C", 3}};
        ReplaceSingleMesh(state, LeafPath, std::move(record));

        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (!page.mesh_identity_valid ||
            page.mesh_identities.size() != 1 ||
            page.mesh_identities[0].instancer_context.size() != 3 ||
            page.mesh_identities[0].instancer_context[0].first != "/A" ||
            page.mesh_identities[0].instancer_context[1].first != "/A/B" ||
            page.mesh_identities[0].instancer_context[2].first != "/A/B/C" ||
            page.mesh_identities[0].instancer_context[2].second != 3)
        {
            std::cerr << "hdSilk three-level instancer context did not round "
                         "trip in order.\n";
            return false;
        }
    }

    // A non-instanced record publishes no chain at all, so the pre-v23 shape is
    // byte-for-byte what it was for the overwhelming majority of scenes.
    {
        HdSilkSceneState state;
        ReplaceSingleMesh(state, LeafPath, MakeSceneStateRecord(LeafPath, 33));
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (!page.mesh_identity_valid ||
            page.mesh_identities.size() != 1 ||
            !page.mesh_identities[0].instancer_context.empty())
        {
            std::cerr << "hdSilk non-instanced record published a chain.\n";
            return false;
        }
    }

    // Malformed and over-budget chains are refused before anything is
    // serialized, and the whole prim is skipped rather than published with an
    // identity no consumer can decode.
    const auto refuses = [&LeafPath](HdSilkMeshRecord record) {
        HdSilkSceneState state;
        ReplaceSingleMesh(state, LeafPath, std::move(record));
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        return page.mesh_upsert_count == 0;
    };

    HdSilkMeshRecord chainWithoutInstancer = MakeSceneStateRecord(LeafPath, 34);
    chainWithoutInstancer.instancerContext = {{"/Instancer", 0}};

    HdSilkMeshRecord instancerWithoutChain = MakeSceneStateRecord(LeafPath, 35);
    instancerWithoutChain.instanceId = 93;
    instancerWithoutChain.instancerPath = "/Instancer";

    HdSilkMeshRecord disagreeingChain = MakeSceneStateRecord(LeafPath, 36);
    disagreeingChain.instanceId = 94;
    disagreeingChain.instancerPath = "/Instancer";
    disagreeingChain.instancerContext = {{"/Somewhere/Else", 0}};

    HdSilkMeshRecord negativeIndex = MakeSceneStateRecord(LeafPath, 37);
    negativeIndex.instanceId = 95;
    negativeIndex.instancerPath = "/Instancer";
    negativeIndex.instancerContext = {{"/Instancer", -1}};

    HdSilkMeshRecord relativePath = MakeSceneStateRecord(LeafPath, 38);
    relativePath.instanceId = 96;
    relativePath.instancerPath = "/Instancer";
    relativePath.instancerContext = {
        {"Relative", 0},
        {"/Instancer", 0}};

    HdSilkMeshRecord overBudget = MakeSceneStateRecord(LeafPath, 39);
    overBudget.instanceId = 97;
    overBudget.instancerPath = "/Instancer";
    overBudget.instancerContext.assign(
        OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES + 1,
        HdSilkInstancerContextEntry{"/Instancer", 0});

    if (!refuses(std::move(chainWithoutInstancer)) ||
        !refuses(std::move(instancerWithoutChain)) ||
        !refuses(std::move(disagreeingChain)) ||
        !refuses(std::move(negativeIndex)) ||
        !refuses(std::move(relativePath)) ||
        !refuses(std::move(overBudget)))
    {
        std::cerr << "hdSilk published a malformed or over-budget instancer "
                     "context.\n";
        return false;
    }

    // Exactly at the level budget is admissible, so the ceiling refuses one
    // level past it rather than the last legal one.
    HdSilkMeshRecord atBudget = MakeSceneStateRecord(LeafPath, 40);
    atBudget.instanceId = 98;
    atBudget.instancerPath = "/Instancer";
    atBudget.instancerContext.assign(
        OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES,
        HdSilkInstancerContextEntry{"/Instancer", 0});
    {
        HdSilkSceneState state;
        ReplaceSingleMesh(state, LeafPath, std::move(atBudget));
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (page.mesh_upsert_count != 1 ||
            !page.mesh_identity_valid ||
            page.mesh_identities[0].instancer_context.size() !=
                OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES)
        {
            std::cerr << "hdSilk refused an instancer context at the level "
                         "budget.\n";
            return false;
        }
    }
    return true;
}

/// A prototype that owns only part of an instancer publishes a sparse index
/// set, so its ABI v8 payload record is the lowest index it owns and not
/// necessarily index zero. Nothing about the wire may depend on a zero-indexed
/// record existing.
bool VerifySparsePrototypeInstanceSerialization()
{
    constexpr char SparsePath[] = "/Sparse";
    HdSilkSceneState state;

    // Proto indices [0, 1, 0, 1] give this prototype instancer instances 1
    // and 3; the payload rides on index 1.
    const int32_t owned[] = {1, 3};
    std::vector<HdSilkMeshRecord> instances;
    for (size_t position = 0; position < 2; ++position)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(SparsePath, 9);
        record.instanceId = 77;
        record.instancerPath = "/Instancer";
        record.instanceIndex = owned[position];
        record.instancerContext = {{"/Instancer", owned[position]}};
        record.transform[3] = static_cast<double>(owned[position]);
        if (position != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        instances.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(SparsePath, std::move(instances));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return commandCount == 3 &&
        page.mesh_upsert_count == 2 &&
        page.mesh_remove_count == 0 &&
        page.mesh_identities.size() == 2 &&
        page.mesh_identities[0].instance_index == 1 &&
        page.mesh_identities[1].instance_index == 3 &&
        page.mesh_point_counts == std::vector<uint32_t>({3u, 0u}) &&
        page.mesh_index_counts == std::vector<uint32_t>({3u, 0u});
}

/// A path whose payload record cannot be serialized must be dropped whole.
/// ABI v8 makes the records of one path interdependent, so appending the later
/// instance references after the payload record was rejected would publish
/// references that no consumer can ever resolve. Sibling paths are independent
/// and must still serialize.
bool VerifyRejectedPayloadDropsWholePath()
{
    constexpr char BrokenPath[] = "/Broken";
    constexpr char HealthyPath[] = "/Healthy";
    HdSilkSceneState state;

    std::vector<HdSilkMeshRecord> broken;
    for (int32_t index = 0; index < 3; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(BrokenPath, 11);
        record.instanceId = 21;
        record.instancerPath = "/Instancer";
        record.instanceIndex = index;
        record.instancerContext = {{"/Instancer", index}};
        if (index == 0)
        {
            // The payload record alone is malformed: one subprim index short.
            record.triangleSubprims.clear();
        }
        else
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        broken.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(BrokenPath, std::move(broken));
    ReplaceSingleMesh(state, HealthyPath, MakeSceneStateRecord(HealthyPath, 12));

    const uint64_t rejectedBefore = HdSilkSceneState::GetRejectedMeshCount();
    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (commandCount != 2 ||
        page.mesh_upsert_count != 1 ||
        page.mesh_identities.size() != 1 ||
        page.mesh_identities[0].path != HealthyPath ||
        HdSilkSceneState::GetRejectedMeshCount() <= rejectedBefore)
    {
        return false;
    }

    // A later instance reference failing drops the path just as completely:
    // the payload record has no meaning without the instances that reuse it,
    // and a torn path is exactly what this rule exists to prevent. Rolling the
    // whole path back leaves every consumer on the last version it retained.
    HdSilkSceneState later;
    std::vector<HdSilkMeshRecord> records;
    for (int32_t index = 0; index < 3; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(BrokenPath, 11);
        record.instanceId = 21;
        record.instancerPath = "/Instancer";
        record.instanceIndex = index;
        record.instancerContext = {{"/Instancer", index}};
        if (index != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        if (index == 2)
        {
            record.cullStyle = 99;
        }
        records.push_back(std::move(record));
    }
    later.ReplaceMeshInstances(BrokenPath, std::move(records));
    const std::vector<uint8_t> laterBytes =
        later.BuildPage(nullptr, &commandCount);
    const ParsedPage laterPage =
        ParseCommands(laterBytes.data(), laterBytes.size());
    return commandCount == 1 &&
        laterPage.mesh_upsert_count == 0;
}

/// A malformed record must be rejected before anything indexes into it.
///
/// ApplyDrawMode and ApplyComplexity both dereference record.indices into
/// record.points and into vertex attribute data. Wireframe draw mode turns a
/// triangle list into a line list carrying the authored index values through
/// unchanged, and medium complexity then subdivides every line by reading both
/// endpoints' positions and interpolating every VERTEX attribute at the
/// subdivision parameter. An out-of-range index therefore reads past the end of
/// both arrays before the transformed record is ever validated. Validating the
/// record as published closes that, and this case is the proof: a three-point
/// triangle whose third index is 4096, under wireframe and medium complexity,
/// must produce a page with no MESH_UPSERT and no crash. It runs under the
/// address sanitiser in the fuzz job, where an out-of-bounds read is fatal
/// rather than merely wrong.
/// The subprim-identity budget is decided from the counts a record WOULD
/// publish, before either table is reserved, and a refusal releases the
/// capacity rather than only the size.
///
/// Capacity is the allocation. A record refused for exceeding the 64 MiB budget
/// that kept its vectors' capacity would still be holding exactly the memory the
/// budget exists to refuse, for as long as the record lived. Asserting on
/// capacity is therefore an allocation assertion, not a size one, and the
/// hostile counts below are checked without ever allocating a single entry.
/// An authored point no face references is emitted but never rasterized, so it
/// is not a pick target and must publish the "no authored counterpart"
/// sentinel.
///
/// Naming it as its own authored index would advertise a pick target the user
/// can never hit: no primitive covers the pixel, so the point pass draws
/// nothing there and the readback answers token zero. Worse, it would inflate
/// authored_point_count past the last point any primitive actually references,
/// handing a consumer an authored space larger than the geometry has.
bool VerifyStrayPointsAreNotPickTargets(const ParsedPage& page)
{
    const ParsedCurves& mesh = page.stray_point_mesh;
    const std::vector<uint32_t> expected{
        0, 1, 2, 3, OPENUSD_SILK_SUBPRIM_NONE};
    if (!mesh.found ||
        mesh.points.size() != 15 ||
        mesh.point_origins != expected)
    {
        std::cerr << "hdSilk stray-point mesh did not sentinel its unreferenced "
                     "point. found=" << mesh.found
                  << " pointFloats=" << mesh.points.size()
                  << " origins=";
        for (uint32_t origin : mesh.point_origins)
        {
            std::cerr << origin << ' ';
        }
        std::cerr << "\n";
        return false;
    }
    return true;
}
bool VerifySubprimIdentityBudgetIsPreflighted()
{
    constexpr size_t maximumEntries =
        OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES / sizeof(uint32_t);

    // Exactly at the budget is admissible; one entry past it is not.
    if (HdSilkSubprimIdentityExceedsBudget(maximumEntries, 0) ||
        HdSilkSubprimIdentityExceedsBudget(maximumEntries / 2, maximumEntries / 2) ||
        !HdSilkSubprimIdentityExceedsBudget(maximumEntries + 1, 0) ||
        !HdSilkSubprimIdentityExceedsBudget(maximumEntries, 1))
    {
        std::cerr << "hdSilk subprim budget preflight is not exact.\n";
        return false;
    }

    // A hostile count must be refused without overflowing the byte product that
    // a naive multiply would wrap.
    if (!HdSilkSubprimIdentityExceedsBudget(
            std::numeric_limits<size_t>::max() / 2,
            std::numeric_limits<size_t>::max() / 2))
    {
        std::cerr << "hdSilk subprim budget preflight overflowed.\n";
        return false;
    }

    HdSilkMeshRecord record = MakeSceneStateRecord("/Budget", 97);
    record.pointOrigins.assign(4096, 0);
    record.cornerEdges.assign(4096, 0);
    record.authoredPointCount = 4096;
    record.authoredEdgeCount = 4096;
    record.subprimIdentity =
        OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
    if (record.pointOrigins.capacity() == 0 ||
        record.cornerEdges.capacity() == 0)
    {
        std::cerr << "hdSilk budget probe did not allocate its fixture.\n";
        return false;
    }

    record.RejectSubprimIdentity(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET);
    if (record.pointOrigins.capacity() != 0 ||
        record.cornerEdges.capacity() != 0 ||
        record.authoredPointCount != 0 ||
        record.authoredEdgeCount != 0 ||
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE) != 0 ||
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_POINT) != 0 ||
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_FACE) == 0 ||
        (record.subprimUnsupported &
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET) == 0)
    {
        std::cerr << "hdSilk subprim refusal kept its allocation. pointCapacity="
                  << record.pointOrigins.capacity()
                  << " edgeCapacity=" << record.cornerEdges.capacity() << "\n";
        return false;
    }
    return true;
}

/// A lightweight ABI v8 instance-reference record is BUILT without the
/// prototype's geometry and identity, rather than copying and then emptying it.
///
/// The reference used to be a full copy of the prototype record whose vectors
/// were cleared afterwards, which cost one deep copy of the prototype's points,
/// indices, subprims, identity tables and attributes per instance -- O(points x
/// instances) -- for data no instance publishes a byte of, and `clear()` kept
/// the capacity so every one of those allocations stayed resident. Capacity is
/// the allocation, so every assertion here is on capacity and not on size, and
/// the reference must hold none at all.
bool VerifyInstanceReferenceReleasesIdentityCapacity()
{
    HdSilkMeshRecord prototype = MakeSceneStateRecord("/Prototype", 98);
    prototype.points.assign(3 * 65536, 0.0f);
    prototype.indices.assign(65536, 0);
    prototype.triangleSubprims.assign(65536, 0);
    prototype.pointOrigins.assign(65536, 0);
    prototype.cornerEdges.assign(65536, 0);
    prototype.attributes.emplace_back();
    prototype.materialPath = "/Materials/Surface";
    prototype.authoredPointCount = 65536;
    prototype.authoredEdgeCount = 65536;
    prototype.subprimIdentity =
        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
    prototype.deformation.published = true;
    prototype.displayColor[0] = 0.25f;
    prototype.transform[3] = 4.0;

    const HdSilkMeshRecord reference = HdSilkMakeInstanceReference(prototype);
    if (reference.points.capacity() != 0 ||
        reference.indices.capacity() != 0 ||
        reference.triangleSubprims.capacity() != 0 ||
        reference.pointOrigins.capacity() != 0 ||
        reference.cornerEdges.capacity() != 0 ||
        reference.attributes.capacity() != 0 ||
        !reference.materialPath.empty() ||
        reference.deformation.published ||
        reference.authoredPointCount != 0 ||
        reference.authoredEdgeCount != 0 ||
        reference.subprimIdentity != OPENUSD_SILK_SUBPRIM_IDENTITY_NONE ||
        reference.subprimUnsupported != OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE)
    {
        std::cerr << "hdSilk instance reference allocated prototype payload. "
                     "pointCapacity="
                  << reference.points.capacity()
                  << " indexCapacity=" << reference.indices.capacity()
                  << " originCapacity=" << reference.pointOrigins.capacity()
                  << " edgeCapacity=" << reference.cornerEdges.capacity()
                  << "\n";
        return false;
    }

    // Identity and per-instance scalars still travel: the reference is the same
    // prim, drawn with the same topology and the same resolved colour.
    if (reference.path != prototype.path ||
        reference.primId != prototype.primId ||
        reference.topologyKind != prototype.topologyKind ||
        reference.topologyRevision != prototype.topologyRevision ||
        reference.doubleSided != prototype.doubleSided ||
        reference.cullStyle != prototype.cullStyle ||
        reference.displayColor[0] != prototype.displayColor[0] ||
        reference.transform[3] != prototype.transform[3])
    {
        std::cerr << "hdSilk instance reference lost prototype identity.\n";
        return false;
    }

    // The prototype it was derived from is untouched: building a reference must
    // not disturb the record that actually publishes the table.
    if (prototype.pointOrigins.size() != 65536 ||
        prototype.cornerEdges.size() != 65536 ||
        prototype.points.size() != 3 * 65536 ||
        prototype.authoredPointCount != 65536)
    {
        std::cerr << "hdSilk instance reference disturbed its prototype.\n";
        return false;
    }

    // The explicit release path stays exercised, because a transform that
    // rebuilds the emitted arrays of an existing record still uses it.
    HdSilkMeshRecord cleared = prototype;
    cleared.ClearSubprimIdentity();
    if (cleared.pointOrigins.capacity() != 0 ||
        cleared.cornerEdges.capacity() != 0 ||
        cleared.subprimIdentity != OPENUSD_SILK_SUBPRIM_IDENTITY_NONE)
    {
        std::cerr << "hdSilk ClearSubprimIdentity kept its allocation.\n";
        return false;
    }
    return true;
}

/// The UsdGeomPoints producer decides the identity budget from the count the
/// table WOULD have, before reserving or filling it.
///
/// A point cloud publishes exactly one point origin per authored point, so the
/// planned table size is the authored point count and nothing else. The budget
/// therefore has a single exact boundary, and the hostile side of it must be
/// refused without the producer ever reserving the entries it refuses.
bool VerifyPointsIdentityBudgetBoundaryIsExact()
{
    constexpr size_t maximumEntries =
        OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES / sizeof(uint32_t);

    if (HdSilkSubprimIdentityExceedsBudget(maximumEntries, 0) ||
        !HdSilkSubprimIdentityExceedsBudget(maximumEntries + 1, 0))
    {
        std::cerr << "hdSilk points identity budget boundary is not exact.\n";
        return false;
    }

    // A hostile authored point count -- one no machine could allocate -- is
    // answered by the predicate rather than by a bad_alloc, and without
    // overflowing the byte product a naive multiply would wrap.
    if (!HdSilkSubprimIdentityExceedsBudget(
            std::numeric_limits<size_t>::max(), 0) ||
        !HdSilkSubprimIdentityExceedsBudget(
            (std::numeric_limits<size_t>::max() / sizeof(uint32_t)) + 1, 0))
    {
        std::cerr << "hdSilk points identity budget overflowed on a hostile "
                     "count.\n";
        return false;
    }

    // A refused point cloud still publishes geometry: only the exact point
    // identity is omitted, and the budget is named beside the topology reason
    // the emitted points always carry.
    HdSilkMeshRecord refused = MakeSceneStateRecord("/OverBudgetPoints", 99);
    refused.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
    refused.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_NONE;
    refused.subprimUnsupported =
        OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE |
        OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET;
    refused.authoredPointCount = 0;
    if (!refused.pointOrigins.empty() ||
        refused.points.empty() ||
        (refused.subprimUnsupported &
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET) == 0)
    {
        std::cerr << "hdSilk over-budget point record did not keep its "
                     "geometry.\n";
        return false;
    }
    return true;
}

bool VerifyMalformedRecordIsRejectedBeforeTransforms()
{
    const uint32_t drawModes[] = {
        OPENUSD_SILK_DRAW_MODE_WIREFRAME,
        OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME,
        OPENUSD_SILK_DRAW_MODE_POINTS};
    for (uint32_t drawMode : drawModes)
    {
        HdSilkSceneState state;
        HdSilkMeshRecord record = MakeSceneStateRecord("/HighIndex", 31);
        record.indices = {0, 1, 4096};
        HdSilkMeshAttribute normals;
        normals.name = "normals";
        normals.semantic = OPENUSD_SILK_ATTRIBUTE_NORMAL;
        normals.componentCount = 3;
        normals.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        normals.data = {
            0.0F, 0.0F, 1.0F,
            0.0F, 0.0F, 1.0F,
            0.0F, 0.0F, 1.0F};
        record.attributes.push_back(std::move(normals));
        ReplaceSingleMesh(state, "/HighIndex", std::move(record));

        // A healthy sibling proves the rejection is scoped to the bad path
        // rather than aborting the page.
        ReplaceSingleMesh(state, "/Healthy", MakeSceneStateRecord("/Healthy", 32));

        state.SetDrawMode(drawMode);
        state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);

        const uint64_t rejectedBefore = HdSilkSceneState::GetRejectedMeshCount();
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (commandCount != 2 ||
            page.mesh_upsert_count != 1 ||
            page.mesh_identities.size() != 1 ||
            page.mesh_identities[0].path != "/Healthy" ||
            HdSilkSceneState::GetRejectedMeshCount() <= rejectedBefore)
        {
            return false;
        }
    }
    return true;
}

/// Complexity subdivides line and point topology, and a triangle list becomes
/// one of those on the way to the wire whenever the draw mode converts it. A
/// complexity change while wireframe or points draw mode is active must
/// therefore republish those records too; dirtying only the records already
/// stored as lines or points leaves every converted record at the previous
/// density until something else happens to dirty it.
bool VerifyComplexityDirtiesConvertedTriangles()
{
    struct ConvertingMode
    {
        uint32_t drawMode;
        uint32_t topologyKind;
        uint32_t lowTriangleCount;
    };
    const ConvertingMode modes[] = {
        {OPENUSD_SILK_DRAW_MODE_WIREFRAME, OPENUSD_SILK_TOPOLOGY_LINE_LIST, 3},
        {OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME,
            OPENUSD_SILK_TOPOLOGY_LINE_LIST,
            3},
        {OPENUSD_SILK_DRAW_MODE_POINTS, OPENUSD_SILK_TOPOLOGY_POINT_LIST, 3}};
    for (const ConvertingMode& mode : modes)
    {
        HdSilkSceneState state;
        ReplaceSingleMesh(state, "/Converted", MakeSceneStateRecord("/Converted", 41));
        state.SetDrawMode(mode.drawMode);

        uint32_t commandCount = 0;
        const std::vector<uint8_t> lowBytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage low = ParseCommands(lowBytes.data(), lowBytes.size());
        if (low.mesh_upsert_count != 1 ||
            low.mesh_topology_kinds.size() != 1 ||
            low.mesh_topology_kinds[0] != mode.topologyKind ||
            low.mesh_triangle_counts[0] != mode.lowTriangleCount)
        {
            return false;
        }

        // The record is stored as a triangle list, so it is only republished if
        // the complexity change accounts for the draw-mode conversion.
        state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);
        const std::vector<uint8_t> mediumBytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage medium =
            ParseCommands(mediumBytes.data(), mediumBytes.size());
        if (medium.mesh_upsert_count != 1 ||
            medium.mesh_topology_kinds[0] != mode.topologyKind ||
            medium.mesh_triangle_counts[0] != mode.lowTriangleCount * 2)
        {
            return false;
        }

        // And back down again, so the dirtying is not a one-way ratchet.
        state.SetComplexity(OPENUSD_SILK_COMPLEXITY_LOW);
        const std::vector<uint8_t> restoredBytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage restored =
            ParseCommands(restoredBytes.data(), restoredBytes.size());
        if (restored.mesh_upsert_count != 1 ||
            restored.mesh_triangle_counts[0] != mode.lowTriangleCount)
        {
            return false;
        }
    }
    return true;
}

/// A malformed record must be skipped with a diagnostic rather than aborting
/// the page, so one bad prim cannot blank an otherwise renderable scene. The
/// page still carries its FRAME command and no MESH_UPSERT.
bool RejectsInvalidSceneStateRecord(HdSilkMeshRecord record)
{
    const std::string path = record.path;
    HdSilkSceneState state;
    std::vector<HdSilkMeshRecord> records;
    records.push_back(std::move(record));
    const uint64_t rejectedBefore = HdSilkSceneState::GetRejectedMeshCount();
    try
    {
        state.ReplaceMeshInstances(path, std::move(records));
    }
    catch (const std::invalid_argument&)
    {
        // Path/key mismatches are still contract violations at publish time.
        return true;
    }

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return commandCount == 1 &&
        page.mesh_upsert_count == 0 &&
        HdSilkSceneState::GetRejectedMeshCount() > rejectedBefore;
}

/// A quad triangulated from one authored face, carrying every ABI v22 subprim
/// table a coarse mesh publishes. The authored face is deliberately not zero
/// and not the triangle index, so a record that answers a face pick with the
/// entry it actually published is distinguishable from one that answers with
/// the zero a rebuilt table happens to hold.
HdSilkMeshRecord MakeSubprimIdentityQuad(const std::string& path, int32_t primId)
{
    HdSilkMeshRecord record;
    record.path = path;
    record.primId = primId;
    record.topologyRevision = 1;
    record.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        1.0F, 1.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    record.indices = {0, 1, 2, 0, 2, 3};
    record.triangleSubprims = {2, 2};
    record.pointOrigins = {0, 1, 2, 3};
    record.cornerEdges = {
        0, 1, OPENUSD_SILK_SUBPRIM_NONE,
        OPENUSD_SILK_SUBPRIM_NONE, 2, 3};
    record.authoredPointCount = 4;
    record.authoredEdgeCount = 4;
    record.subprimIdentity =
        OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
    return record;
}

/// A mesh drawn as points refuses the face target instead of answering it with
/// a fabricated index.
///
/// The points draw mode replaces the emitted primitives with one point per
/// vertex and rebuilds `triangle_subprims` as one zero per point, because a
/// point belongs to no triangulated face. The record used to keep its FACE
/// claim across that rebuild, so every face pick over a mesh drawn as points
/// was answered with authored face zero -- not even the face the mesh actually
/// published, and an index no round trip could resolve back to what was
/// picked. Points are the one target this mode can still answer exactly: the
/// emitted vertex array is untouched, so the point-origin table still names the
/// authored point behind every drawn point.
bool VerifyPointDrawModeRefusesFaceIdentity()
{
    HdSilkSceneState state;
    ReplaceSingleMesh(state, "/Quad", MakeSubprimIdentityQuad("/Quad", 51));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> shadedBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage shaded =
        ParseCommands(shadedBytes.data(), shadedBytes.size());
    if (!shaded.subprim_identity_valid ||
        shaded.mesh_upsert_count != 1 ||
        shaded.mesh_subprim_identities.size() != 1 ||
        shaded.mesh_subprim_identities[0] !=
            (OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
                OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
                OPENUSD_SILK_SUBPRIM_IDENTITY_POINT) ||
        shaded.mesh_subprim_unsupported[0] !=
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE ||
        shaded.mesh_primitive_subprims[0] != std::vector<uint32_t>({2, 2}) ||
        shaded.mesh_point_origins[0] != std::vector<uint32_t>({0, 1, 2, 3}))
    {
        std::cerr << "hdSilk shaded quad did not publish its authored subprim "
                     "identity.\n";
        return false;
    }

    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_POINTS);
    const std::vector<uint8_t> pointBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage points =
        ParseCommands(pointBytes.data(), pointBytes.size());
    if (!points.subprim_identity_valid ||
        points.mesh_upsert_count != 1 ||
        points.mesh_topology_kinds[0] != OPENUSD_SILK_TOPOLOGY_POINT_LIST ||
        points.mesh_triangle_counts[0] != 4)
    {
        std::cerr << "hdSilk points draw mode did not publish a point list.\n";
        return false;
    }
    if (points.mesh_subprim_identities[0] != OPENUSD_SILK_SUBPRIM_IDENTITY_POINT)
    {
        std::cerr << "hdSilk points draw mode claimed a subprim target it "
                     "cannot answer. identity="
                  << points.mesh_subprim_identities[0] << "\n";
        return false;
    }
    if ((points.mesh_subprim_unsupported[0] &
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE) == 0)
    {
        std::cerr << "hdSilk points draw mode refused a target without naming "
                     "the topology reason.\n";
        return false;
    }

    // The evidence that the refusal matters: the rebuilt per-primitive table is
    // all zeros, so a retained FACE claim would have answered every face pick
    // over this mesh with authored face zero rather than the face 2 the mesh
    // published while shaded.
    if (points.mesh_primitive_subprims[0] !=
            std::vector<uint32_t>({0, 0, 0, 0}) ||
        points.mesh_point_origins[0] != std::vector<uint32_t>({0, 1, 2, 3}))
    {
        std::cerr << "hdSilk points draw mode lost exact point identity.\n";
        return false;
    }

    // Wireframe is the mode that keeps face identity: a line list emits one
    // line per triangle corner and every line still names the authored face its
    // triangle was triangulated from, so the refusal is scoped to points.
    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_WIREFRAME);
    const std::vector<uint8_t> wireBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage wire = ParseCommands(wireBytes.data(), wireBytes.size());
    if (!wire.subprim_identity_valid ||
        wire.mesh_upsert_count != 1 ||
        wire.mesh_topology_kinds[0] != OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
        (wire.mesh_subprim_identities[0] &
            OPENUSD_SILK_SUBPRIM_IDENTITY_FACE) == 0 ||
        wire.mesh_primitive_subprims[0] !=
            std::vector<uint32_t>({2, 2, 2, 2, 2, 2}))
    {
        std::cerr << "hdSilk wireframe draw mode lost authored face identity.\n";
        return false;
    }

    // And the claim cannot be reintroduced by a producer either: a point list
    // that declares authored face identity is refused by validation, because
    // an emitted point is not a face of the authored mesh.
    HdSilkMeshRecord faceClaimingPoints;
    faceClaimingPoints.path = "/FaceClaimingPoints";
    faceClaimingPoints.primId = 52;
    faceClaimingPoints.topologyRevision = 1;
    faceClaimingPoints.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
    faceClaimingPoints.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    faceClaimingPoints.indices = {0, 1, 2};
    faceClaimingPoints.triangleSubprims = {0, 1, 2};

    // The same record without the claim is publishable, so the refusal is the
    // face claim and nothing else about the record.
    HdSilkMeshRecord admissiblePoints = faceClaimingPoints;
    admissiblePoints.path = "/AdmissiblePoints";
    HdSilkSceneState admissibleState;
    ReplaceSingleMesh(
        admissibleState,
        "/AdmissiblePoints",
        std::move(admissiblePoints));
    const std::vector<uint8_t> admissibleBytes =
        admissibleState.BuildPage(nullptr, &commandCount);
    const ParsedPage admissible =
        ParseCommands(admissibleBytes.data(), admissibleBytes.size());
    if (admissible.mesh_upsert_count != 1 ||
        admissible.mesh_subprim_identities[0] !=
            OPENUSD_SILK_SUBPRIM_IDENTITY_NONE)
    {
        std::cerr << "hdSilk point list without a face claim was not "
                     "published.\n";
        return false;
    }

    faceClaimingPoints.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_FACE;
    if (!RejectsInvalidSceneStateRecord(std::move(faceClaimingPoints)))
    {
        std::cerr << "hdSilk published a point list claiming authored face "
                     "identity.\n";
        return false;
    }
    return true;
}

/// The record a UsdGeomPoints prim publishes: one emitted vertex per authored
/// point, in authored order, with the exact point-origin table that identity
/// implies and the edge target refused by topology.
HdSilkMeshRecord MakePointCloudRecord(
    const std::string& path,
    int32_t primId,
    size_t authoredPointCount,
    size_t drawnPointCount)
{
    HdSilkMeshRecord record;
    record.path = path;
    record.primId = primId;
    record.topologyRevision = 1;
    record.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
    for (size_t point = 0; point < authoredPointCount; ++point)
    {
        record.points.push_back(static_cast<float>(point));
        record.points.push_back(0.0F);
        record.points.push_back(0.0F);
        record.pointOrigins.push_back(static_cast<uint32_t>(point));
    }
    for (size_t point = 0; point < drawnPointCount; ++point)
    {
        record.indices.push_back(static_cast<uint32_t>(point));
        record.triangleSubprims.push_back(static_cast<uint32_t>(point));
    }
    record.authoredPointCount = static_cast<uint32_t>(authoredPointCount);
    record.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
    record.subprimUnsupported = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE;
    return record;
}

/// A point cloud keeps exact point identity at every complexity.
///
/// Complexity duplicates a point list rather than resubdividing it: every copy
/// of authored point p is still authored point p, at the same position, so the
/// mapping from emitted vertex to authored point stays exact. The transform
/// used to refuse all subprim identity here on the grounds that it rebuilds the
/// emitted arrays, which is true of a resubdivided line and false of a
/// duplicated point: a point cloud viewed at anything above Low answered no
/// point pick at all, and published authoredPointCount zero, so a consumer
/// could not even tell how large the authored point space had been. The
/// evidence is per density, because the duplication factor is what the emitted
/// table has to follow.
bool VerifyComplexityKeepsPointIdentity()
{
    struct DensityCase
    {
        uint32_t complexity;
        uint32_t density;
    };
    const DensityCase densities[] = {
        {OPENUSD_SILK_COMPLEXITY_LOW, 1},
        {OPENUSD_SILK_COMPLEXITY_MEDIUM, 2},
        {OPENUSD_SILK_COMPLEXITY_HIGH, 4},
        {OPENUSD_SILK_COMPLEXITY_VERY_HIGH, 8}};
    for (const DensityCase& density : densities)
    {
        HdSilkSceneState state;
        ReplaceSingleMesh(
            state,
            "/Points",
            MakePointCloudRecord("/Points", 61, 3, 3));
        state.SetComplexity(density.complexity);

        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        std::vector<uint32_t> expectedOrigins;
        for (uint32_t point = 0; point < 3; ++point)
        {
            for (uint32_t copy = 0; copy < density.density; ++copy)
            {
                expectedOrigins.push_back(point);
            }
        }
        if (!page.subprim_identity_valid ||
            page.mesh_upsert_count != 1 ||
            page.mesh_topology_kinds[0] != OPENUSD_SILK_TOPOLOGY_POINT_LIST ||
            page.mesh_point_counts[0] != 3 * density.density ||
            page.mesh_triangle_counts[0] != 3 * density.density)
        {
            std::cerr << "hdSilk complexity did not duplicate the point list. "
                         "density="
                      << density.density << "\n";
            return false;
        }
        if (page.mesh_subprim_identities[0] !=
                OPENUSD_SILK_SUBPRIM_IDENTITY_POINT ||
            page.mesh_subprim_unsupported[0] !=
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE)
        {
            std::cerr << "hdSilk complexity dropped exact point identity. "
                         "density="
                      << density.density
                      << " identity=" << page.mesh_subprim_identities[0]
                      << " unsupported=" << page.mesh_subprim_unsupported[0]
                      << "\n";
            return false;
        }
        // One origin per emitted vertex, each duplicate naming the authored
        // point its copy came from, and the authored space retained exactly.
        if (page.mesh_point_origins[0] != expectedOrigins ||
            page.mesh_authored_point_counts[0] != 3)
        {
            std::cerr << "hdSilk complexity published a point-origin table that "
                         "does not follow the duplicated vertices. density="
                      << density.density << "\n";
            return false;
        }
    }

    // A resubdivided line is the case the refusal exists for, and it stays
    // refused: the interior vertices it emits are not authored points and its
    // segments are fractions of an authored edge, so neither table survives.
    HdSilkSceneState lineState;
    HdSilkMeshRecord line;
    line.path = "/Curve";
    line.primId = 62;
    line.topologyRevision = 1;
    line.topologyKind = OPENUSD_SILK_TOPOLOGY_LINE_LIST;
    line.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        2.0F, 0.0F, 0.0F};
    line.indices = {0, 1, 1, 2};
    line.triangleSubprims = {0, 0};
    line.pointOrigins = {0, 1, 2};
    line.cornerEdges = {0, 1};
    line.authoredPointCount = 3;
    line.authoredEdgeCount = 2;
    line.subprimIdentity =
        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE | OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
    ReplaceSingleMesh(lineState, "/Curve", std::move(line));
    lineState.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);

    uint32_t lineCommandCount = 0;
    const std::vector<uint8_t> lineBytes =
        lineState.BuildPage(nullptr, &lineCommandCount);
    const ParsedPage subdivided =
        ParseCommands(lineBytes.data(), lineBytes.size());
    if (!subdivided.subprim_identity_valid ||
        subdivided.mesh_upsert_count != 1 ||
        subdivided.mesh_subprim_identities[0] !=
            OPENUSD_SILK_SUBPRIM_IDENTITY_NONE ||
        (subdivided.mesh_subprim_unsupported[0] &
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE) == 0 ||
        !subdivided.mesh_point_origins[0].empty() ||
        subdivided.mesh_authored_point_counts[0] != 0)
    {
        std::cerr << "hdSilk complexity kept subprim identity across a "
                     "resubdivided line.\n";
        return false;
    }

    // And a point list whose emitted vertices do not cover the authored space
    // the record declares is refused by name rather than renumbered. Authored
    // point 3 is drawn by no primitive here, so the duplicated table would name
    // an authored space of three against a declared count of four -- a count
    // the ABI defines as one past the largest index the table names. The
    // geometry is still published, exactly as it is for an over-budget cloud.
    HdSilkSceneState strayState;
    ReplaceSingleMesh(
        strayState,
        "/StrayPoint",
        MakePointCloudRecord("/StrayPoint", 63, 4, 3));
    strayState.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);

    uint32_t strayCommandCount = 0;
    const std::vector<uint8_t> strayBytes =
        strayState.BuildPage(nullptr, &strayCommandCount);
    const ParsedPage stray = ParseCommands(strayBytes.data(), strayBytes.size());
    if (!stray.subprim_identity_valid ||
        stray.mesh_upsert_count != 1 ||
        stray.mesh_point_counts[0] != 6 ||
        stray.mesh_subprim_identities[0] != OPENUSD_SILK_SUBPRIM_IDENTITY_NONE ||
        (stray.mesh_subprim_unsupported[0] &
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY) == 0 ||
        !stray.mesh_point_origins[0].empty() ||
        stray.mesh_authored_point_counts[0] != 0)
    {
        std::cerr << "hdSilk complexity did not name why it refused an "
                     "incomplete point-origin table.\n";
        return false;
    }
    return true;
}

/// The emitted point-origin table follows the record's INDEX count, which is
/// what the preflight, the budget and the reservation are all sized from.
///
/// A point list may index a source vertex more than once, or not at all, so its
/// index count and its source vertex count are independent. The duplication
/// emits `density` copies per emitted primitive, so the table it builds has
/// indices.size() * density entries -- never pointOrigins.size() * density,
/// which is what every one of those three decisions used to be taken from. On a
/// record whose two counts differ, that number described a table this transform
/// never emits: the budget was decided for the wrong size, and the reservation
/// was made for the wrong size beside it.
bool VerifyComplexityOriginsFollowEmittedIndices()
{
    // The exact boundary the mis-sized count crosses. A source table of four
    // origins is trivially inside the budget at any density, while the emitted
    // table of a record that draws maximumEntries/2 + 1 primitives at density
    // two is one entry outside it. Deciding the budget from the source table
    // therefore admitted a table the bound exists to refuse.
    constexpr size_t maximumEntries =
        OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES / sizeof(uint32_t);
    constexpr size_t density = 2;
    constexpr size_t sourceOriginCount = 4;
    constexpr size_t emittedPrimitiveCount = (maximumEntries / density) + 1;
    if (HdSilkSubprimIdentityExceedsBudget(sourceOriginCount * density, 0) ||
        !HdSilkSubprimIdentityExceedsBudget(
            emittedPrimitiveCount * density, 0))
    {
        std::cerr << "hdSilk complexity origin budget does not separate the "
                     "source table from the emitted one.\n";
        return false;
    }

    // And the published table on a record whose counts differ: three source
    // vertices, four drawn primitives because the last vertex is drawn twice.
    HdSilkMeshRecord record;
    record.path = "/RedrawnPoints";
    record.primId = 64;
    record.topologyRevision = 1;
    record.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
    record.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        2.0F, 0.0F, 0.0F};
    record.indices = {0, 1, 2, 2};
    record.triangleSubprims = {0, 1, 2, 3};
    record.pointOrigins = {0, 1, 2};
    record.authoredPointCount = 3;
    record.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
    record.subprimUnsupported = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE;

    HdSilkSceneState state;
    ReplaceSingleMesh(state, "/RedrawnPoints", std::move(record));
    state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    const std::vector<uint32_t> expectedOrigins = {0, 0, 1, 1, 2, 2, 2, 2};
    // Eight entries, which is indices.size() * density. The source table's own
    // length would have sized this at six, and the branch refuses a table that
    // does not close on the count its preflight sized the budget and the
    // reservation from -- so a record that keeps exact point identity here is
    // direct evidence that all three used the emitted count.
    if (!page.subprim_identity_valid ||
        page.mesh_upsert_count != 1 ||
        page.mesh_point_counts[0] != 8 ||
        page.mesh_triangle_counts[0] != 8 ||
        page.mesh_subprim_identities[0] != OPENUSD_SILK_SUBPRIM_IDENTITY_POINT ||
        page.mesh_point_origins[0] != expectedOrigins ||
        page.mesh_authored_point_counts[0] != 3)
    {
        std::cerr << "hdSilk complexity sized its point-origin table from the "
                     "source table rather than the emitted primitives. entries="
                  << page.mesh_point_origins[0].size() << "\n";
        return false;
    }
    return true;
}

/// A mesh drawn as points names every authored point it has just made visible.
///
/// USD lets a mesh carry an authored point no face references. While the mesh
/// is shaded nothing rasterizes that point, so the producer publishes the "no
/// authored counterpart" sentinel for it and keeps it out of the authored point
/// space -- which the ABI defines as one past the largest index the table
/// names. The points draw mode draws every emitted vertex, so those points are
/// suddenly on screen and pickable, and the sentinel then answered a pick on a
/// plainly visible point with "this is not an authored component" while the
/// declared authored space stopped short of it.
bool VerifyPointDrawModeNamesStrayAuthoredPoints()
{
    HdSilkMeshRecord record;
    record.path = "/StrayMesh";
    record.primId = 71;
    record.topologyRevision = 1;
    record.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        0.0F, 1.0F, 0.0F,
        // Authored point 3 is carried by the mesh and referenced by no face.
        5.0F, 5.0F, 0.0F};
    record.indices = {0, 1, 2};
    record.triangleSubprims = {7};
    record.pointOrigins = {0, 1, 2, OPENUSD_SILK_SUBPRIM_NONE};
    record.cornerEdges = {0, 1, 2};
    record.authoredPointCount = 3;
    record.authoredEdgeCount = 3;
    record.subprimIdentity =
        OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;

    HdSilkSceneState state;
    ReplaceSingleMesh(state, "/StrayMesh", record);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> shadedBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage shaded =
        ParseCommands(shadedBytes.data(), shadedBytes.size());
    const std::vector<uint32_t> shadedOrigins = {
        0, 1, 2, OPENUSD_SILK_SUBPRIM_NONE};
    if (!shaded.subprim_identity_valid ||
        shaded.mesh_upsert_count != 1 ||
        shaded.mesh_point_origins[0] != shadedOrigins ||
        shaded.mesh_authored_point_counts[0] != 3)
    {
        std::cerr << "hdSilk shaded mesh did not keep its stray point out of "
                     "the authored point space.\n";
        return false;
    }

    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_POINTS);
    const std::vector<uint8_t> pointBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage points =
        ParseCommands(pointBytes.data(), pointBytes.size());
    const std::vector<uint32_t> pointOrigins = {0, 1, 2, 3};
    if (!points.subprim_identity_valid ||
        points.mesh_upsert_count != 1 ||
        points.mesh_topology_kinds[0] != OPENUSD_SILK_TOPOLOGY_POINT_LIST ||
        points.mesh_triangle_counts[0] != 4 ||
        points.mesh_subprim_identities[0] !=
            OPENUSD_SILK_SUBPRIM_IDENTITY_POINT ||
        (points.mesh_subprim_unsupported[0] &
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE) == 0 ||
        points.mesh_point_origins[0] != pointOrigins ||
        points.mesh_authored_point_counts[0] != 4)
    {
        std::cerr << "hdSilk points draw mode did not name the authored point "
                     "it made visible. authoredPoints="
                  << points.mesh_authored_point_counts[0] << "\n";
        return false;
    }

    // Complexity duplicates that table without losing the point it just named,
    // so the stray point stays pickable at every density.
    state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);
    const std::vector<uint8_t> denseBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage dense = ParseCommands(denseBytes.data(), denseBytes.size());
    const std::vector<uint32_t> denseOrigins = {0, 0, 1, 1, 2, 2, 3, 3};
    if (!dense.subprim_identity_valid ||
        dense.mesh_upsert_count != 1 ||
        dense.mesh_point_origins[0] != denseOrigins ||
        dense.mesh_authored_point_counts[0] != 4)
    {
        std::cerr << "hdSilk complexity lost the authored point the points "
                     "draw mode named.\n";
        return false;
    }

    // The sentinel survives exactly where it means what it says. An expanded
    // face-varying record emits one vertex per corner, so its table is not the
    // identity and a sentinel in it marks a vertex the mesh generated rather
    // than an authored point that went undrawn. Naming such a vertex after its
    // own emitted index would invent an authored point the stage does not have.
    HdSilkMeshRecord expanded;
    expanded.path = "/ExpandedMesh";
    expanded.primId = 72;
    expanded.topologyRevision = 1;
    expanded.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        1.0F, 1.0F, 0.0F,
        0.0F, 0.0F, 0.0F,
        1.0F, 1.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    expanded.indices = {0, 1, 2, 3, 4, 5};
    expanded.triangleSubprims = {0, 0};
    expanded.pointOrigins = {0, 1, 2, 0, 2, OPENUSD_SILK_SUBPRIM_NONE};
    expanded.authoredPointCount = 3;
    expanded.subprimIdentity =
        OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;

    HdSilkSceneState expandedState;
    ReplaceSingleMesh(expandedState, "/ExpandedMesh", std::move(expanded));
    expandedState.SetDrawMode(OPENUSD_SILK_DRAW_MODE_POINTS);
    const std::vector<uint8_t> expandedBytes =
        expandedState.BuildPage(nullptr, &commandCount);
    const ParsedPage expandedPage =
        ParseCommands(expandedBytes.data(), expandedBytes.size());
    const std::vector<uint32_t> expandedOrigins = {
        0, 1, 2, 0, 2, OPENUSD_SILK_SUBPRIM_NONE};
    if (!expandedPage.subprim_identity_valid ||
        expandedPage.mesh_upsert_count != 1 ||
        expandedPage.mesh_point_origins[0] != expandedOrigins ||
        expandedPage.mesh_authored_point_counts[0] != 3)
    {
        std::cerr << "hdSilk points draw mode invented an authored point for a "
                     "generated vertex.\n";
        return false;
    }
    return true;
}

/// Sequential presentation changes publish distinct, stable topology revisions.
///
/// topology_revision is what a consumer keys its retained topology by: a record
/// carrying the revision it already holds is taken to have the topology it
/// already holds, and one that contradicts that is refused outright. Draw mode
/// and complexity rebuild the emitted arrays -- the topology kind, the indices
/// and the point-origin table all change -- while the authored topology behind
/// them does not, so publishing the authored revision for every presentation
/// handed the consumer two topologies under one revision. Applying a shaded
/// page and then a points page, or a Low page and then a Medium page, to one
/// retained scene was rejected for exactly that.
bool VerifyPresentationTopologyRevisionFollowsPresentation()
{
    HdSilkSceneState state;
    ReplaceSingleMesh(state, "/Quad", MakeSubprimIdentityQuad("/Quad", 73));

    struct Published
    {
        uint64_t revision;
        uint32_t topologyKind;
        uint32_t pointCount;
        std::vector<uint32_t> origins;
    };
    const auto publish = [&state](Published* out) -> bool
    {
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (!page.mesh_identity_valid ||
            page.mesh_upsert_count != 1 ||
            page.mesh_topology_revisions.size() != 1)
        {
            return false;
        }
        out->revision = page.mesh_topology_revisions[0];
        out->topologyKind = page.mesh_topology_kinds[0];
        out->pointCount = page.mesh_point_counts[0];
        out->origins = page.mesh_point_origins[0];
        return true;
    };

    // A presentation that rebuilds nothing publishes the authored revision
    // unchanged, so a session that never leaves smooth-shaded Low sees exactly
    // the revisions it always did.
    Published shaded{};
    if (!publish(&shaded) ||
        shaded.revision != 1 ||
        shaded.topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST)
    {
        std::cerr << "hdSilk shaded page did not publish the authored topology "
                     "revision. revision=" << shaded.revision << "\n";
        return false;
    }

    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_POINTS);
    Published drawnAsPoints{};
    if (!publish(&drawnAsPoints) ||
        drawnAsPoints.topologyKind != OPENUSD_SILK_TOPOLOGY_POINT_LIST ||
        drawnAsPoints.revision == shaded.revision)
    {
        std::cerr << "hdSilk points page reused the shaded topology revision. "
                     "revision=" << drawnAsPoints.revision << "\n";
        return false;
    }

    // Republishing the same presentation republishes the same revision, so a
    // consumer keeps its retained topology instead of rotating identity for a
    // page that changed nothing.
    ReplaceSingleMesh(state, "/Quad", MakeSubprimIdentityQuad("/Quad", 73));
    Published republished{};
    if (!publish(&republished) ||
        republished.revision != drawnAsPoints.revision ||
        republished.origins != drawnAsPoints.origins)
    {
        std::cerr << "hdSilk republished one presentation under two revisions.\n";
        return false;
    }

    state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);
    Published dense{};
    if (!publish(&dense) ||
        dense.pointCount != drawnAsPoints.pointCount * 2 ||
        dense.revision == drawnAsPoints.revision ||
        dense.revision == shaded.revision)
    {
        std::cerr << "hdSilk complexity page reused a revision of a different "
                     "topology. revision=" << dense.revision << "\n";
        return false;
    }

    // The revision is a pure function of the authored revision and the
    // presentation, so returning to a presentation returns to its revision.
    state.SetComplexity(OPENUSD_SILK_COMPLEXITY_LOW);
    Published restored{};
    if (!publish(&restored) || restored.revision != drawnAsPoints.revision)
    {
        std::cerr << "hdSilk did not restore the revision of a restored "
                     "presentation.\n";
        return false;
    }

    // Two draw modes that emit the same topology are one presentation: the
    // wireframe and hidden-surface wireframe modes both publish the same line
    // list, so they share a revision rather than churning identity between
    // them.
    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_WIREFRAME);
    Published wireframe{};
    if (!publish(&wireframe) ||
        wireframe.topologyKind != OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
        wireframe.revision == shaded.revision ||
        wireframe.revision == drawnAsPoints.revision)
    {
        std::cerr << "hdSilk wireframe page reused another presentation's "
                     "revision.\n";
        return false;
    }
    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME);
    Published hiddenWireframe{};
    if (!publish(&hiddenWireframe) ||
        hiddenWireframe.revision != wireframe.revision)
    {
        std::cerr << "hdSilk published one line list under two revisions.\n";
        return false;
    }

    // And back to shaded, which is the authored topology itself.
    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED);
    Published reshaded{};
    if (!publish(&reshaded) ||
        reshaded.revision != shaded.revision ||
        reshaded.topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST)
    {
        std::cerr << "hdSilk did not restore the authored revision when the "
                     "presentation stopped rebuilding the topology.\n";
        return false;
    }

    // A point cloud is presented by complexity alone, with no draw mode
    // involved: the same rule has to hold for it, or a Low page and a Medium
    // page of one cloud arrive under one revision carrying different vertices.
    HdSilkSceneState cloudState;
    ReplaceSingleMesh(
        cloudState,
        "/Points",
        MakePointCloudRecord("/Points", 74, 3, 3));
    uint32_t cloudCommandCount = 0;
    const std::vector<uint8_t> lowBytes =
        cloudState.BuildPage(nullptr, &cloudCommandCount);
    const ParsedPage low = ParseCommands(lowBytes.data(), lowBytes.size());
    cloudState.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);
    const std::vector<uint8_t> mediumBytes =
        cloudState.BuildPage(nullptr, &cloudCommandCount);
    const ParsedPage medium =
        ParseCommands(mediumBytes.data(), mediumBytes.size());
    if (low.mesh_upsert_count != 1 ||
        medium.mesh_upsert_count != 1 ||
        low.mesh_topology_revisions[0] != 1 ||
        medium.mesh_topology_revisions[0] == low.mesh_topology_revisions[0] ||
        medium.mesh_point_counts[0] != low.mesh_point_counts[0] * 2)
    {
        std::cerr << "hdSilk point cloud published two densities under one "
                     "topology revision.\n";
        return false;
    }

    // An ABI v8 instance reference carries no arrays at all: it reuses the
    // prototype payload's geometry, and a consumer refuses a reference whose
    // topology does not match the prototype it points at. The revision is
    // therefore derived from the presentation and not from the emitted arrays,
    // so a presented prototype and its references still agree.
    HdSilkSceneState instancedState;
    std::vector<HdSilkMeshRecord> instances;
    for (int32_t index = 0; index < 2; ++index)
    {
        HdSilkMeshRecord instance = MakeSubprimIdentityQuad("/Instanced", 75);
        instance.instanceId = 42;
        instance.instancerPath = "/Instancer";
        instance.instanceIndex = index;
        instance.instancerContext = {{"/Instancer", index}};
        if (index != 0)
        {
            instance = HdSilkMakeInstanceReference(instance);
            instance.instanceId = 42;
            instance.instancerPath = "/Instancer";
            instance.instanceIndex = index;
            instance.instancerContext = {{"/Instancer", index}};
        }
        instances.push_back(std::move(instance));
    }
    instancedState.ReplaceMeshInstances("/Instanced", std::move(instances));
    instancedState.SetDrawMode(OPENUSD_SILK_DRAW_MODE_POINTS);
    instancedState.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);

    uint32_t instancedCommandCount = 0;
    const std::vector<uint8_t> instancedBytes =
        instancedState.BuildPage(nullptr, &instancedCommandCount);
    const ParsedPage instanced =
        ParseCommands(instancedBytes.data(), instancedBytes.size());
    if (instanced.mesh_upsert_count != 2 ||
        instanced.mesh_topology_revisions.size() != 2 ||
        instanced.mesh_topology_revisions[0] !=
            instanced.mesh_topology_revisions[1] ||
        instanced.mesh_topology_kinds[0] != instanced.mesh_topology_kinds[1] ||
        instanced.mesh_topology_revisions[0] == 1)
    {
        std::cerr << "hdSilk presented an instance reference under a different "
                     "revision from its prototype.\n";
        return false;
    }
    return true;
}

/// A direct light with a non-default light-link collection resolves into a
/// sparse per-prim mask table.
///
/// The table is default-free by contract, so the evidence that linking worked is
/// exactly which prims appear: a prim inside every light's collection resolves to
/// the default and must be absent, and a prim outside one collection must be
/// present with that light's bit clear. It also proves the mask indexes the
/// frame light ordering: with two direct lights sorted by path, /Lights/A owns
/// bit 0 and /Lights/B owns bit 1.
bool VerifyLightLinkTableResolvesCategories()
{
    HdSilkSceneState state;

    HdSilkLightRecord keyLight;
    keyLight.path = "/Lights/A";
    keyLight.type = OPENUSD_SILK_LIGHT_DISTANT;
    keyLight.lightLinkCategory = "lit";
    state.ReplaceLight(keyLight);

    HdSilkLightRecord fillLight;
    fillLight.path = "/Lights/B";
    fillLight.type = OPENUSD_SILK_LIGHT_SPHERE;
    // No collection: this light links to everything, exactly as UsdImaging's
    // empty category identity means.
    state.ReplaceLight(fillLight);

    if (!state.HasLightLinks())
    {
        return false;
    }

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership lit;
    lit.path = "/Geom/Lit";
    lit.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    lit.categories = {"lit"};
    memberships.push_back(std::move(lit));
    HdSilkCategoryMembership unlit;
    unlit.path = "/Geom/Unlit";
    unlit.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(unlit));
    HdSilkCategoryMembership unlitInstance;
    unlitInstance.path = "/Geom/Unlit";
    unlitInstance.instanceIndex = 3;
    unlitInstance.categories = {"lit"};
    memberships.push_back(std::move(unlitInstance));
    state.SetCategoryMemberships(std::move(memberships), false);

    ReplaceSingleMesh(state, "/Geom/Lit", MakeSceneStateRecord("/Geom/Lit", 41));
    ReplaceSingleMesh(state, "/Geom/Unlit", MakeSceneStateRecord("/Geom/Unlit", 42));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.light_link_light_count != 2 ||
        page.light_link_light_count != page.frame_light_count ||
        page.light_link_unsupported != OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE ||
        page.light_links.size() != 2)
    {
        return false;
    }

    // Sorted by path, then instance index. /Geom/Lit is in the key light's
    // collection and in the unlinked fill light's, so it resolves to the default
    // and is omitted; /Geom/Unlit is absent from the key light's collection, so
    // its light bit 0 is clear. Neither light declares a shadow collection, so
    // both shadow bits stay set: the two collections are resolved independently,
    // and an unlit prim still casts.
    if (std::get<0>(page.light_links[0]) != "/Geom/Unlit" ||
        std::get<1>(page.light_links[0]) != OPENUSD_SILK_LINK_ALL_INSTANCES ||
        std::get<2>(page.light_links[0]) != 0x2u ||
        std::get<3>(page.light_links[0]) != 0x3u)
    {
        return false;
    }
    if (std::get<0>(page.light_links[1]) != "/Geom/Unlit" ||
        std::get<1>(page.light_links[1]) != 3 ||
        std::get<2>(page.light_links[1]) != 0x3u ||
        std::get<3>(page.light_links[1]) != 0x3u)
    {
        return false;
    }

    // An unchanged table publishes nothing at all, and retiring the collection
    // publishes an empty table exactly once so a consumer stops masking.
    const std::vector<uint8_t> unchanged = state.BuildPage(nullptr, &commandCount);
    const ParsedPage unchangedPage =
        ParseCommands(unchanged.data(), unchanged.size());
    if (unchangedPage.light_link_count != 0)
    {
        return false;
    }

    keyLight.lightLinkCategory.clear();
    state.ReplaceLight(keyLight);
    if (state.HasLightLinks())
    {
        return false;
    }
    const std::vector<uint8_t> retired = state.BuildPage(nullptr, &commandCount);
    const ParsedPage retiredPage = ParseCommands(retired.data(), retired.size());
    if (!retiredPage.light_link_valid ||
        retiredPage.light_link_count != 1 ||
        !retiredPage.light_links.empty())
    {
        return false;
    }

    const std::vector<uint8_t> stillRetired =
        state.BuildPage(nullptr, &commandCount);
    return ParseCommands(stillRetired.data(), stillRetired.size())
        .light_link_count == 0;
}

/// A shadow link narrows the shadow mask without narrowing the light mask, and a
/// shadow link is resolved independently of the light link: a prim the light does
/// not illuminate still casts its shadow when the caster collection includes it.
bool VerifyShadowLinkNarrowsOnlyTheShadowMask()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.shadowLinkCategory = "casters";
    state.ReplaceLight(light);

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership receiver;
    receiver.path = "/Geom/Receiver";
    receiver.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(receiver));
    state.SetCategoryMemberships(std::move(memberships), false);
    ReplaceSingleMesh(
        state,
        "/Geom/Receiver",
        MakeSceneStateRecord("/Geom/Receiver", 43));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.light_link_valid &&
        page.light_link_count == 1 &&
        page.light_links.size() == 1 &&
        std::get<0>(page.light_links[0]) == "/Geom/Receiver" &&
        std::get<2>(page.light_links[0]) == 0x1u &&
        std::get<3>(page.light_links[0]) == 0x0u;
}

/// The two link collections are resolved independently. A prim excluded from a
/// light's lightLink collection but included in its shadowLink collection is an
/// unlit blocker, and it must still publish the shadow bit: intersecting the two
/// masks would silently delete its shadow.
bool VerifyUnlitBlockerStillPublishesItsShadowBit()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.lightLinkCategory = "lit";
    state.ReplaceLight(light);

    // The blocker belongs to no category at all, so it is outside the "lit"
    // collection while the shadow collection -- which the light leaves empty --
    // includes everything.
    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership blocker;
    blocker.path = "/Geom/Blocker";
    blocker.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(blocker));
    state.SetCategoryMemberships(std::move(memberships), false);
    ReplaceSingleMesh(
        state,
        "/Geom/Blocker",
        MakeSceneStateRecord("/Geom/Blocker", 64));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.light_link_valid &&
        page.light_link_count == 1 &&
        page.light_links.size() == 1 &&
        std::get<0>(page.light_links[0]) == "/Geom/Blocker" &&
        std::get<2>(page.light_links[0]) == 0x0u &&
        std::get<3>(page.light_links[0]) == 0x1u;
}

/// A table larger than the page budget reports the omission rather than
/// publishing a partial table that would silently darken the prims it dropped.
bool VerifyOversizedLightLinkTableIsDiagnosed()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.lightLinkCategory = "lit";
    state.ReplaceLight(light);

    std::vector<HdSilkCategoryMembership> memberships;
    memberships.reserve(OPENUSD_SILK_MAX_LINK_ENTRIES + 8u);
    for (uint32_t index = 0; index < OPENUSD_SILK_MAX_LINK_ENTRIES + 8u; ++index)
    {
        HdSilkCategoryMembership membership;
        membership.path = "/Geom/Prim" + std::to_string(index);
        membership.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
        memberships.push_back(std::move(membership));
    }
    state.SetCategoryMemberships(std::move(memberships), false);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.light_link_valid &&
        page.light_link_count == 1 &&
        page.light_links.size() == OPENUSD_SILK_MAX_LINK_ENTRIES &&
        page.light_link_unsupported ==
            OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED;
}

/// A prim that links to every light costs the bounded table nothing, however
/// many of them a scene has.
///
/// The bound belongs to the resolved, sparsified table and not to the
/// memberships the collector gathers. Charging a membership before its masks
/// are known is the same as charging a prim for linking to everything: a scene
/// of 4096 such prims filled the budget with rows that resolve to nothing, and
/// the one prim a collection really excluded was dropped and reported as
/// truncated. The measurement is the whole table: exactly one entry, naming the
/// excluded prim, and no truncation at all.
bool VerifyDefaultPrimsDoNotConsumeTheLinkBudget()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.lightLinkCategory = "lit";
    state.ReplaceLight(light);

    // Built through the collector's own entry point, so the rule under test is
    // the one the render pass runs rather than a restatement of it.
    std::vector<HdSilkCategoryMembership> memberships;
    for (uint32_t index = 0; index < OPENUSD_SILK_MAX_LINK_ENTRIES; ++index)
    {
        if (!HdSilkAppendPathMemberships(
                "/Geom/Linked" + std::to_string(index),
                {"lit"},
                {},
                {},
                HdSilkMaxCollectedInstanceRows,
                &memberships,
                nullptr))
        {
            return false;
        }
    }
    if (!HdSilkAppendPathMemberships(
            "/Geom/Excluded",
            {},
            {},
            {},
            HdSilkMaxCollectedInstanceRows,
            &memberships,
            nullptr) ||
        memberships.size() != OPENUSD_SILK_MAX_LINK_ENTRIES + 1u)
    {
        return false;
    }
    state.SetCategoryMemberships(std::move(memberships), false);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.light_link_valid &&
        page.light_link_count == 1 &&
        page.light_links.size() == 1 &&
        std::get<0>(page.light_links[0]) == "/Geom/Excluded" &&
        std::get<1>(page.light_links[0]) == OPENUSD_SILK_LINK_ALL_INSTANCES &&
        std::get<2>(page.light_links[0]) == 0x0u &&
        std::get<3>(page.light_links[0]) == 0x1u &&
        page.light_link_unsupported == OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE;
}

/// Instances whose categories differ but whose masks do not cost the bounded
/// table nothing either, however many of them a prototype has.
///
/// A category set that differs from the prototype's is not a mask that differs
/// from it. The collector cannot tell them apart -- the page's light and dome
/// orderings do not exist yet -- so it emits a row for every instance whose
/// categories differ, and the resolution discards the ones that resolve to
/// their path. Charging those rows against the ABI budget while they are still
/// unresolved refused a prototype whose instances all carry a collection no
/// light names, and the path's one genuine override went with it.
///
/// Here 5000 instances carry a category the scene's only light ignores, and one
/// instance carries the light's caster collection. The published table must be
/// exactly the prototype's row and that single override, with no truncation at
/// all.
bool VerifyCategoryDifferingInstancesResolvingToTheirPathCostNothing()
{
    constexpr size_t instances = 5000;
    constexpr int overrideIndex = 4321;

    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.lightLinkCategory = "lit";
    light.shadowLinkCategory = "casters";
    state.ReplaceLight(light);

    HdSilkInstancerLevel level;
    level.path = "/World/Instancer";
    level.instanceCount = static_cast<int64_t>(instances);
    level.publishedIndices.reserve(instances);
    level.instanceCategories.reserve(instances);
    for (size_t index = 0; index < instances; ++index)
    {
        level.publishedIndices.push_back(static_cast<int>(index));

        // Every instance differs from the prototype's empty category set, so
        // the collector emits a row for every one of them. Only the categories
        // a light actually names can move a mask, and "ignoredN" names none:
        // the light links "lit" and casts for "casters" and nothing else.
        level.instanceCategories.push_back(
            static_cast<int>(index) == overrideIndex
                ? std::vector<std::string>{"casters"}
                : std::vector<std::string>{"ignored" + std::to_string(index)});
    }

    std::vector<HdSilkCategoryMembership> memberships;
    if (!HdSilkAppendPathMemberships(
            "/Geom/Scattered",
            {},
            {},
            {level},
            HdSilkMaxCollectedInstanceRows,
            &memberships,
            nullptr))
    {
        std::cerr << "hdSilk refused to collect " << instances
                  << " category-differing instance rows.\n";
        return false;
    }

    // The rows really were collected: the path row plus one per instance. A
    // collector that refused them at the ABI budget would have rolled the whole
    // group back and left nothing at all.
    if (memberships.size() != instances + 1)
    {
        std::cerr << "hdSilk collected " << memberships.size()
                  << " membership rows, expected " << (instances + 1) << "\n";
        return false;
    }
    state.SetCategoryMemberships(std::move(memberships), false);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.light_links.size() != 2 ||
        page.light_link_unsupported != OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE)
    {
        std::cerr << "hdSilk published " << page.light_links.size()
                  << " entries for 5000 category-differing instances, "
                  << "unsupported " << page.light_link_unsupported << "\n";
        return false;
    }

    // The prototype's own row: outside the light's collection and outside its
    // caster collection. Then exactly one instance row, for the one instance
    // whose categories move a mask, and it moves only the shadow mask.
    return std::get<0>(page.light_links[0]) == "/Geom/Scattered" &&
        std::get<1>(page.light_links[0]) == OPENUSD_SILK_LINK_ALL_INSTANCES &&
        std::get<2>(page.light_links[0]) == 0x0u &&
        std::get<3>(page.light_links[0]) == 0x0u &&
        std::get<0>(page.light_links[1]) == "/Geom/Scattered" &&
        std::get<1>(page.light_links[1]) == overrideIndex &&
        std::get<2>(page.light_links[1]) == 0x0u &&
        std::get<3>(page.light_links[1]) == 0x1u;
}

/// A path whose resolved entries do not fit is omitted whole, so it fails open
/// to every light rather than keeping a path row its own instances contradict.
///
/// A path-wide row is what a consumer falls back to for every instance it has
/// no row for. Publishing a restrictive path row and dropping the overrides
/// that widen it therefore applies the author's narrow mask to exactly the
/// instances the author opted back in -- a darker image than either the
/// authored scene or the unlinked default. Truncation has to fail open, which
/// means the whole group goes or none of it does.
bool VerifyOversizedPathOverridesFailOpenAsAWholeGroup()
{
    // A prototype whose every instance opts back into the light its path is
    // excluded from, more times than the table can ever carry. Unlike the
    // category-differing case, every one of these rows really does resolve to a
    // different mask, so they are genuine entries and the table cannot hold
    // them.
    const size_t oversized = OPENUSD_SILK_MAX_LINK_ENTRIES + 32u;
    HdSilkInstancerLevel level;
    level.path = "/World/Instancer";
    level.instanceCount = static_cast<int64_t>(oversized);
    level.publishedIndices.reserve(oversized);
    level.instanceCategories.assign(oversized, std::vector<std::string>{"lit"});
    for (size_t index = 0; index < oversized; ++index)
    {
        level.publishedIndices.push_back(static_cast<int>(index));
    }

    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.lightLinkCategory = "lit";
    state.ReplaceLight(light);

    std::vector<HdSilkCategoryMembership> memberships;
    if (!HdSilkAppendPathMemberships(
            "/Geom/Oversized",
            {},
            {},
            {level},
            HdSilkMaxCollectedInstanceRows,
            &memberships,
            nullptr))
    {
        return false;
    }
    state.SetCategoryMemberships(std::move(memberships), false);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> oversizedBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage oversizedPage =
        ParseCommands(oversizedBytes.data(), oversizedBytes.size());
    if (!oversizedPage.light_link_valid ||
        oversizedPage.light_link_count != 1 ||
        !oversizedPage.light_links.empty() ||
        oversizedPage.light_link_unsupported !=
            OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED)
    {
        // Not one row of the group may survive: the path row alone would apply
        // the author's narrow mask to the instances that were dropped.
        std::cerr << "hdSilk published " << oversizedPage.light_links.size()
                  << " entries for an over-budget path, unsupported "
                  << oversizedPage.light_link_unsupported << "\n";
        return false;
    }

    // The collector's own limit is a memory policy, and it is atomic too: a
    // prototype that would materialize more unresolved rows than it admits
    // leaves nothing behind, not even its path row.
    std::vector<HdSilkCategoryMembership> refused;
    if (HdSilkAppendPathMemberships(
            "/Geom/Refused",
            {},
            {},
            {level},
            oversized - 1,
            &refused,
            nullptr) ||
        !refused.empty())
    {
        return false;
    }

    // The same rule between paths. Two paths each need 3001 entries, so the
    // second cannot fit beside the first. It must vanish entirely -- no path
    // row, no overrides -- and the omission must be reported.
    constexpr size_t perPath = 3000;
    HdSilkSceneState pairState;
    pairState.ReplaceLight(light);

    HdSilkInstancerLevel fittingLevel;
    fittingLevel.path = "/World/Instancer";
    fittingLevel.instanceCount = static_cast<int64_t>(perPath);
    fittingLevel.publishedIndices.reserve(perPath);
    fittingLevel.instanceCategories.assign(
        perPath,
        std::vector<std::string>{"lit"});
    for (size_t index = 0; index < perPath; ++index)
    {
        fittingLevel.publishedIndices.push_back(static_cast<int>(index));
    }

    std::vector<HdSilkCategoryMembership> pair;
    if (!HdSilkAppendPathMemberships(
            "/Geom/First",
            {},
            {},
            {fittingLevel},
            HdSilkMaxCollectedInstanceRows,
            &pair,
            nullptr) ||
        !HdSilkAppendPathMemberships(
            "/Geom/Second",
            {},
            {},
            {fittingLevel},
            HdSilkMaxCollectedInstanceRows,
            &pair,
            nullptr))
    {
        return false;
    }
    pairState.SetCategoryMemberships(std::move(pair), false);

    const std::vector<uint8_t> bytes =
        pairState.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.light_links.size() != perPath + 1 ||
        page.light_link_unsupported !=
            OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED)
    {
        std::cerr << "hdSilk atomic link group published "
                  << page.light_links.size() << " entries, unsupported "
                  << page.light_link_unsupported << "\n";
        return false;
    }
    for (const auto& entry : page.light_links)
    {
        if (std::get<0>(entry) != "/Geom/First")
        {
            std::cerr << "hdSilk published an entry for the omitted group: "
                      << std::get<0>(entry) << " instance "
                      << std::get<1>(entry) << "\n";
            return false;
        }
    }
    return true;
}

/// An instancer that scatters several prototypes emits membership rows only for
/// the instances each prototype actually draws.
///
/// Hydra reports one category array per instance of the *instancer*, so every
/// prototype sees every instance. Walking that array directly emitted a row for
/// (this prototype, that index) even where the index draws a different
/// prototype: identities no record is ever published under, which no mask can
/// ever match and which consume the same bounded table the real rows need. With
/// a large instancer and a handful of prototypes the phantoms alone fill the
/// budget and truncate the rows that address something.
bool VerifyInstanceMembershipsFollowPublishedPrototypeIndices()
{
    // 8192 instances, alternating between two prototypes, so the phantom rows
    // would be double the budget on their own.
    constexpr size_t instanceCount = 8192;
    const std::vector<std::string> primCategories{"proto"};
    std::vector<std::vector<std::string>> instanceCategories;
    instanceCategories.reserve(instanceCount);
    std::vector<int> even;
    std::vector<int> odd;
    for (size_t index = 0; index < instanceCount; ++index)
    {
        // Every instance's categories differ from the prototype's, so nothing is
        // skipped for being redundant and the intersection is the only thing
        // keeping the table small.
        instanceCategories.push_back({"instance" + std::to_string(index)});
        if (index % 2 == 0)
        {
            even.push_back(static_cast<int>(index));
        }
        else
        {
            odd.push_back(static_cast<int>(index));
        }
    }

    std::vector<HdSilkCategoryMembership> memberships;
    const bool fitted = HdSilkAppendInstanceMemberships(
        "/Geom/Even",
        primCategories,
        instanceCategories,
        even,
        OPENUSD_SILK_MAX_LINK_ENTRIES,
        &memberships);

    // Exactly the prototype's own instances, and every one of them even.
    if (!fitted ||
        memberships.size() != even.size() ||
        even.size() > OPENUSD_SILK_MAX_LINK_ENTRIES)
    {
        return false;
    }
    for (size_t row = 0; row < memberships.size(); ++row)
    {
        if (memberships[row].path != "/Geom/Even" ||
            memberships[row].instanceIndex != even[row] ||
            memberships[row].instanceIndex % 2 != 0)
        {
            return false;
        }
    }

    // The second prototype's rows are the complement, and neither prototype's
    // table contains an identity the other publishes.
    std::vector<HdSilkCategoryMembership> odds;
    if (!HdSilkAppendInstanceMemberships(
            "/Geom/Odd",
            primCategories,
            instanceCategories,
            odd,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &odds) ||
        odds.size() != odd.size())
    {
        return false;
    }
    for (const HdSilkCategoryMembership& membership : odds)
    {
        if (membership.instanceIndex % 2 != 1)
        {
            return false;
        }
    }

    // A hidden or proto instance addresses no instance primvar element, so it
    // draws nothing and must contribute no row at all -- and neither must an
    // index past the reported categories.
    std::vector<HdSilkCategoryMembership> hidden;
    if (!HdSilkAppendInstanceMemberships(
            "/Geom/Hidden",
            primCategories,
            instanceCategories,
            {-1, -7, static_cast<int>(instanceCount), static_cast<int>(instanceCount) + 5},
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &hidden) ||
        !hidden.empty())
    {
        return false;
    }

    // An instance that resolves to the prototype's own categories is already
    // described by the prototype's row, so it is not repeated.
    std::vector<std::vector<std::string>> matching(4, primCategories);
    std::vector<HdSilkCategoryMembership> redundant;
    if (!HdSilkAppendInstanceMemberships(
            "/Geom/Redundant",
            primCategories,
            matching,
            {0, 1, 2, 3},
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &redundant) ||
        !redundant.empty())
    {
        return false;
    }

    // The budget still bounds a prototype that really does draw more instances
    // than the table admits, and reports that it did.
    std::vector<int> all;
    all.reserve(instanceCount);
    for (size_t index = 0; index < instanceCount; ++index)
    {
        all.push_back(static_cast<int>(index));
    }
    std::vector<HdSilkCategoryMembership> bounded;
    return !HdSilkAppendInstanceMemberships(
            "/Geom/All",
            primCategories,
            instanceCategories,
            all,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &bounded) &&
        bounded.size() == OPENUSD_SILK_MAX_LINK_ENTRIES;
}

/// The rows one nested prototype is expected to publish: a composed instance
/// index and the categories it resolves to.
struct ExpectedMembership
{
    int32_t instanceIndex = 0;
    std::vector<std::string> categories;
};

bool MatchesMemberships(
    const std::vector<HdSilkCategoryMembership>& actual,
    const std::string& path,
    const std::vector<ExpectedMembership>& expected)
{
    if (actual.size() != expected.size())
    {
        std::cerr << "hdSilk nested linking published " << actual.size()
                  << " rows for '" << path << "', expected "
                  << expected.size() << "\n";
        for (const HdSilkCategoryMembership& row : actual)
        {
            std::cerr << "  row " << row.path << " instance "
                      << row.instanceIndex << "\n";
        }
        return false;
    }
    for (size_t row = 0; row < expected.size(); ++row)
    {
        std::vector<std::string> wanted = expected[row].categories;
        std::sort(wanted.begin(), wanted.end());
        if (actual[row].path != path ||
            actual[row].instanceIndex != expected[row].instanceIndex ||
            actual[row].categories != wanted)
        {
            std::cerr << "hdSilk nested linking row " << row << " of '" << path
                      << "' published instance " << actual[row].instanceIndex
                      << " with " << actual[row].categories.size()
                      << " categories, expected instance "
                      << expected[row].instanceIndex << " with "
                      << wanted.size() << "\n";
            return false;
        }
    }
    return true;
}

/// Builds one level of a chain: its authoritative instance count, the indices
/// it publishes for the child it scatters, and Hydra's per-instance categories.
HdSilkInstancerLevel MakeLevel(
    const char* path,
    int64_t instanceCount,
    std::vector<int> publishedIndices,
    std::vector<std::vector<std::string>> instanceCategories)
{
    HdSilkInstancerLevel level;
    level.path = path;
    level.instanceCount = instanceCount;
    level.publishedIndices = std::move(publishedIndices);
    level.instanceCategories = std::move(instanceCategories);
    return level;
}

/// Under nested instancing a membership row must name the composed identity
/// hdSilk publishes -- outerIndex * innerInstanceCount + innerIndex -- and must
/// combine what every level of the chain contributes to it.
///
/// The two failures this closes are opposite and both silent. Resolving the
/// inner instancer's own category array against the published index would apply
/// instance 2's collection to composed index 2, which under an outer instancer
/// of four is a different scene instance every time. Emitting a row per
/// (outer, inner) pair without intersecting each level against the indices it
/// actually draws would emit rows for identities the other prototype publishes,
/// which no record ever matches and which consume the bounded table.
bool VerifyNestedInstanceMembershipsResolveComposedIdentities()
{
    // Outer: three instances, all three drawing the inner instancer, and only
    // outer instance 1 in a collection. Inner: four instances shared by two
    // prototypes -- TwigA owns 0 and 2, TwigB owns 1 and 3 -- with only inner
    // instance 2 in a collection of its own.
    const std::vector<std::vector<std::string>> outerCategories{
        {}, {"rimlit"}, {}};
    const std::vector<std::vector<std::string>> innerCategories{
        {}, {}, {"keyoff"}, {}};

    std::vector<HdSilkInstancerLevel> twigA{
        MakeLevel("/World/Outer", 3, {0, 1, 2}, outerCategories),
        MakeLevel("/World/Outer/Inner", 4, {0, 2}, innerCategories)};
    std::vector<HdSilkCategoryMembership> rows;
    HdSilkNestedLinkDiagnostics diagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/TwigA",
            {},
            {},
            twigA,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &rows,
            &diagnostics) ||
        diagnostics.Any())
    {
        return false;
    }

    // TwigA draws composed 0, 2, 4, 6, 8 and 10. Only the identities a level
    // actually moves are published: inner 2 under each outer, and every inner
    // under outer 1. Composed 0 and 8 resolve to the prototype's own row and
    // are absent, which is what keeps the table sparse.
    if (!MatchesMemberships(
            rows,
            "/World/Outer/Inner/TwigA",
            {{2, {"keyoff"}},
             {4, {"rimlit"}},
             {6, {"keyoff", "rimlit"}},
             {10, {"keyoff"}}}))
    {
        return false;
    }

    // The second prototype of the same inner instancer shares the index space
    // and publishes only its own identities: 5 and 7 are the odd inner indices
    // under outer 1, and nothing TwigA published is repeated.
    std::vector<HdSilkInstancerLevel> twigB{
        MakeLevel("/World/Outer", 3, {0, 1, 2}, outerCategories),
        MakeLevel("/World/Outer/Inner", 4, {1, 3}, innerCategories)};
    std::vector<HdSilkCategoryMembership> other;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/TwigB",
            {},
            {},
            twigB,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &other,
            &diagnostics) ||
        diagnostics.Any())
    {
        return false;
    }
    return MatchesMemberships(
        other,
        "/World/Outer/Inner/TwigB",
        {{5, {"rimlit"}}, {7, {"rimlit"}}});
}

/// Hidden instances and sparse proto indices consume no rows at any level, and
/// a three-level chain composes against each level's own radix.
bool VerifyNestedInstanceMembershipsSkipHiddenAndDeepIdentities()
{
    // Outer instance 1 is hidden, so Hydra publishes -1 for it. Nothing under
    // it is drawn, so nothing under it may be linked either.
    std::vector<HdSilkInstancerLevel> sparse{
        MakeLevel("/World/Outer", 3, {0, -1, 2}, {{}, {"rimlit"}, {}}),
        MakeLevel("/World/Outer/Inner", 4, {2, 3}, {{}, {}, {}, {"tag"}})};
    std::vector<HdSilkCategoryMembership> rows;
    HdSilkNestedLinkDiagnostics diagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {},
            {},
            sparse,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &rows,
            &diagnostics) ||
        diagnostics.Any())
    {
        return false;
    }
    // Composed 3 and 11 are inner 3 under outer 0 and outer 2. The hidden
    // outer instance would have contributed composed 5, 6 and 7, and the
    // "rimlit" collection it belongs to must not reach anything.
    if (!MatchesMemberships(
            rows,
            "/World/Outer/Inner/Leaf",
            {{3, {"tag"}}, {11, {"tag"}}}))
    {
        return false;
    }

    // Three levels: 2 outer, 3 middle, 2 inner. The composed index is
    // ((outer * 3) + middle) * 2 + inner, and a middle-level collection reaches
    // exactly the inner instances beneath that middle instance.
    std::vector<HdSilkInstancerLevel> deep{
        MakeLevel("/World/A", 2, {0, 1}, {}),
        MakeLevel("/World/A/B", 3, {0, 1, 2}, {{}, {"midlit"}, {}}),
        MakeLevel("/World/A/B/C", 2, {0, 1}, {{}, {"innerlit"}})};
    std::vector<HdSilkCategoryMembership> deepRows;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/A/B/C/Leaf",
            {},
            {},
            deep,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &deepRows,
            &diagnostics) ||
        diagnostics.Any())
    {
        return false;
    }
    // Middle 1 covers composed ((0*3)+1)*2 + {0,1} = 2, 3 and
    // ((1*3)+1)*2 + {0,1} = 8, 9. Inner 1 covers every odd composed index.
    return MatchesMemberships(
        deepRows,
        "/World/A/B/C/Leaf",
        {{1, {"innerlit"}},
         {2, {"midlit"}},
         {3, {"innerlit", "midlit"}},
         {5, {"innerlit"}},
         {7, {"innerlit"}},
         {8, {"midlit"}},
         {9, {"innerlit", "midlit"}},
         {11, {"innerlit"}}});
}

/// The path-wide row still describes every instance that shares it, and the
/// categories an ancestor instancer applies to all of its instances belong on
/// that row rather than on a row per composed identity.
bool VerifyNestedPrototypeWideMembershipsStaySparse()
{
    // Every inner instance carries exactly the prototype's own categories, and
    // no ancestor instance carries anything, so every composed identity
    // resolves to the prototype's row and the table stays empty.
    std::vector<HdSilkInstancerLevel> uniform{
        MakeLevel("/World/Outer", 64, {}, {}),
        MakeLevel("/World/Outer/Inner", 64, {}, {})};
    uniform[0].publishedIndices.reserve(64);
    uniform[0].instanceCategories.assign(64, {"proto"});
    uniform[1].publishedIndices.reserve(64);
    uniform[1].instanceCategories.assign(64, {"proto"});
    for (int index = 0; index < 64; ++index)
    {
        uniform[0].publishedIndices.push_back(index);
        uniform[1].publishedIndices.push_back(index);
    }
    std::vector<HdSilkCategoryMembership> rows;
    HdSilkNestedLinkDiagnostics diagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {"proto"},
            {},
            uniform,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &rows,
            &diagnostics) ||
        !rows.empty() ||
        diagnostics.Any())
    {
        return false;
    }

    // The same 4096 identities with an ancestor collection that covers all of
    // them: it is path-wide by construction, so it belongs to the prototype's
    // own row and still publishes nothing per instance. A resolution that
    // folded it in per identity instead would fill the whole budget with rows
    // that all say the same thing.
    std::vector<HdSilkInstancerLevel> shared{
        MakeLevel("/World/Outer", 64, uniform[0].publishedIndices, {}),
        MakeLevel(
            "/World/Outer/Inner",
            64,
            uniform[1].publishedIndices,
            {})};
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {},
            {"ancestorlit"},
            shared,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &rows,
            &diagnostics) ||
        !rows.empty() ||
        diagnostics.Any())
    {
        return false;
    }

    // An instance that opts back into nothing under a prototype that is in a
    // collection is still a difference and is still published, because the
    // consumer would otherwise fall back to the prototype's narrower mask.
    std::vector<HdSilkInstancerLevel> optOut{
        MakeLevel("/World/Outer", 2, {0, 1}, {}),
        MakeLevel("/World/Outer/Inner", 2, {0, 1}, {{"proto"}, {}})};
    std::vector<HdSilkCategoryMembership> optOutRows;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {"proto"},
            {},
            optOut,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &optOutRows,
            &diagnostics) ||
        diagnostics.Any())
    {
        return false;
    }
    return MatchesMemberships(
        optOutRows,
        "/World/Outer/Inner/Leaf",
        {{1, {}}, {3, {}}});
}

/// Malformed levels are reported rather than resolved against a wrong index,
/// and the bounded table is enforced before a row is appended.
bool VerifyNestedInstanceMembershipsAreBoundedAndDiagnosed()
{
    // An inner index the inner instancer's own count cannot explain has no
    // unique nested encoding, which is exactly the sample HdSilkInstancer
    // drops, so it must publish no row either.
    std::vector<HdSilkInstancerLevel> uncomposable{
        MakeLevel("/World/Outer", 2, {0, 1}, {}),
        MakeLevel("/World/Outer/Inner", 2, {0, 5}, {{}, {"a"}, {}, {}, {}, {"b"}})};
    std::vector<HdSilkCategoryMembership> rows;
    HdSilkNestedLinkDiagnostics diagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {},
            {},
            uncomposable,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &rows,
            &diagnostics) ||
        !rows.empty() ||
        diagnostics.uncomposableIndices != 1 ||
        diagnostics.unresolvedIndices != 0)
    {
        return false;
    }

    // A level that reports per-instance categories but not for an instance it
    // publishes leaves that instance's membership unknown. It keeps the
    // prototype's row and is counted; nothing beneath it is published under a
    // mask resolved from another instance.
    std::vector<HdSilkInstancerLevel> unresolved{
        MakeLevel("/World/Outer", 3, {0, 1, 2}, {{}}),
        MakeLevel("/World/Outer/Inner", 2, {0, 1}, {{}, {"innerlit"}})};
    std::vector<HdSilkCategoryMembership> unresolvedRows;
    HdSilkNestedLinkDiagnostics unresolvedDiagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {},
            {},
            unresolved,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &unresolvedRows,
            &unresolvedDiagnostics) ||
        unresolvedDiagnostics.unresolvedIndices != 2 ||
        unresolvedDiagnostics.uncomposableIndices != 0)
    {
        return false;
    }
    // Only outer 0 survives, so only composed 0 and 1 exist and only inner 1
    // moves off the prototype's row.
    if (!MatchesMemberships(
            unresolvedRows,
            "/World/Outer/Inner/Leaf",
            {{1, {"innerlit"}}}))
    {
        return false;
    }

    // A composed identity past the signed 32-bit instance index the ABI carries
    // is pruned with a diagnostic rather than truncated into another instance's
    // slot.
    std::vector<HdSilkInstancerLevel> huge{
        MakeLevel("/World/Outer", 4, {0, 3}, {{}, {}, {}, {}}),
        MakeLevel(
            "/World/Outer/Inner",
            1073741824,
            {0, 1},
            {{"deep"}, {"deep"}})};
    std::vector<HdSilkCategoryMembership> hugeRows;
    HdSilkNestedLinkDiagnostics hugeDiagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {},
            {},
            huge,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &hugeRows,
            &hugeDiagnostics) ||
        hugeDiagnostics.unrepresentableIndices != 2)
    {
        return false;
    }
    if (!MatchesMemberships(
            hugeRows,
            "/World/Outer/Inner/Leaf",
            {{0, {"deep"}}, {1, {"deep"}}}))
    {
        return false;
    }

    // The budget is checked before the row is appended, so a chain that would
    // resolve more identities than the table admits fills it exactly and says
    // it did. The rows that fit are the lowest composed identities, which is
    // what makes a truncated table reproducible.
    std::vector<int> everyIndex;
    everyIndex.reserve(128);
    std::vector<std::vector<std::string>> everyCategory(128);
    for (int index = 0; index < 128; ++index)
    {
        everyIndex.push_back(index);
        everyCategory[static_cast<size_t>(index)] = {"i" + std::to_string(index)};
    }
    std::vector<HdSilkInstancerLevel> oversized{
        MakeLevel("/World/Outer", 128, everyIndex, {}),
        MakeLevel("/World/Outer/Inner", 128, everyIndex, everyCategory)};
    std::vector<HdSilkCategoryMembership> boundedRows;
    HdSilkNestedLinkDiagnostics boundedDiagnostics;
    if (HdSilkAppendNestedInstanceMemberships(
            "/World/Outer/Inner/Leaf",
            {},
            {},
            oversized,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &boundedRows,
            &boundedDiagnostics) ||
        boundedRows.size() != OPENUSD_SILK_MAX_LINK_ENTRIES)
    {
        return false;
    }
    for (size_t row = 0; row < boundedRows.size(); ++row)
    {
        if (boundedRows[row].instanceIndex != static_cast<int32_t>(row))
        {
            return false;
        }
    }
    return true;
}

/// The composed rows reach the wire as one sparse ABI v21 entry per published
/// identity, with the direct, shadow and dome masks each resolved from the
/// collection the identity belongs to, and they retire the way any other table
/// does.
bool VerifyNestedInstanceLinksReachTheWire()
{
    constexpr char LeafPath[] = "/World/Outer/Inner/Leaf";
    HdSilkSceneState state;

    HdSilkLightRecord keyLight;
    keyLight.path = "/Lights/Key";
    keyLight.type = OPENUSD_SILK_LIGHT_DISTANT;
    keyLight.lightLinkCategory = "keyoff";
    keyLight.shadowLinkCategory = "casters";
    state.ReplaceLight(keyLight);

    HdSilkLightRecord dome;
    dome.path = "/Lights/Dome";
    dome.ambientOnly = true;
    dome.lightLinkCategory = "rimlit";
    state.ReplaceLight(dome);

    // The chain from the render pass: three outer instances of a four-instance
    // inner instancer, with outer 1 in the dome's collection, inner 2 in the
    // key light's collection and inner 3 in the key light's caster collection.
    std::vector<HdSilkInstancerLevel> levels{
        MakeLevel("/World/Outer", 3, {0, 1, 2}, {{}, {"rimlit"}, {}}),
        MakeLevel(
            "/World/Outer/Inner",
            4,
            {0, 1, 2, 3},
            {{}, {}, {"keyoff"}, {"casters"}})};

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership prototype;
    prototype.path = LeafPath;
    prototype.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(prototype));
    HdSilkNestedLinkDiagnostics diagnostics;
    if (!HdSilkAppendNestedInstanceMemberships(
            LeafPath,
            {},
            {},
            levels,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &memberships,
            &diagnostics) ||
        diagnostics.Any())
    {
        return false;
    }
    state.SetCategoryMemberships(memberships, false);

    std::vector<HdSilkMeshRecord> instances;
    for (int32_t index = 0; index < 12; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(LeafPath, 71);
        record.instanceId = 5;
        record.instancerPath = "/Instancer";
        record.instanceIndex = index;
        record.instancerContext = {{"/Instancer", index}};
        if (index != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        instances.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(LeafPath, std::move(instances));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.light_link_light_count != 1 ||
        page.light_link_dome_count != 1 ||
        page.light_link_unsupported != OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE)
    {
        return false;
    }

    // The prototype row itself: outside every collection, so no direct light,
    // no shadow and no dome. Then exactly one row per composed identity whose
    // masks differ from it, sorted by instance index. Composed 2, 6 and 10 are
    // inner 2 under each outer; 3, 7 and 11 are inner 3; 4, 5, 6 and 7 are
    // every inner index under outer 1.
    const std::vector<std::tuple<int32_t, uint32_t, uint32_t, uint32_t>> expected{
        {OPENUSD_SILK_LINK_ALL_INSTANCES, 0x0u, 0x0u, 0x0u},
        {2, 0x1u, 0x0u, 0x0u},
        {3, 0x0u, 0x1u, 0x0u},
        {4, 0x0u, 0x0u, 0x1u},
        {5, 0x0u, 0x0u, 0x1u},
        {6, 0x1u, 0x0u, 0x1u},
        {7, 0x0u, 0x1u, 0x1u},
        {10, 0x1u, 0x0u, 0x0u},
        {11, 0x0u, 0x1u, 0x0u}};
    if (page.light_links.size() != expected.size())
    {
        std::cerr << "hdSilk nested link table published "
                  << page.light_links.size() << " entries, expected "
                  << expected.size() << "\n";
        return false;
    }
    for (size_t entry = 0; entry < expected.size(); ++entry)
    {
        if (std::get<0>(page.light_links[entry]) != LeafPath ||
            std::get<1>(page.light_links[entry]) !=
                std::get<0>(expected[entry]) ||
            std::get<2>(page.light_links[entry]) !=
                std::get<1>(expected[entry]) ||
            std::get<3>(page.light_links[entry]) !=
                std::get<2>(expected[entry]) ||
            std::get<4>(page.light_links[entry]) !=
                std::get<3>(expected[entry]))
        {
            std::cerr << "hdSilk nested link entry " << entry
                      << " published instance "
                      << std::get<1>(page.light_links[entry]) << " masks "
                      << std::get<2>(page.light_links[entry]) << "/"
                      << std::get<3>(page.light_links[entry]) << "/"
                      << std::get<4>(page.light_links[entry]) << "\n";
            return false;
        }
    }

    // An unchanged chain republishes nothing at all.
    const std::vector<uint8_t> unchanged = state.BuildPage(nullptr, &commandCount);
    if (ParseCommands(unchanged.data(), unchanged.size()).light_link_count != 0)
    {
        return false;
    }

    // Moving the ancestor collection to a different outer instance moves the
    // dome bit to a different composed range rather than leaving the previous
    // one behind.
    levels[0].instanceCategories = {{"rimlit"}, {}, {}};
    std::vector<HdSilkCategoryMembership> updated;
    HdSilkCategoryMembership updatedPrototype;
    updatedPrototype.path = LeafPath;
    updatedPrototype.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    updated.push_back(std::move(updatedPrototype));
    if (!HdSilkAppendNestedInstanceMemberships(
            LeafPath,
            {},
            {},
            levels,
            OPENUSD_SILK_MAX_LINK_ENTRIES,
            &updated,
            &diagnostics))
    {
        return false;
    }
    state.SetCategoryMemberships(std::move(updated), false);
    const std::vector<uint8_t> movedBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage moved = ParseCommands(movedBytes.data(), movedBytes.size());
    if (moved.light_link_count != 1)
    {
        return false;
    }
    for (const auto& entry : moved.light_links)
    {
        const int32_t instanceIndex = std::get<1>(entry);
        const bool domeLit = (std::get<4>(entry) & 0x1u) != 0;
        if (domeLit != (instanceIndex >= 0 && instanceIndex < 4))
        {
            return false;
        }
    }

    // Retiring both collections publishes the canonical empty table exactly
    // once, so the composed rows stop being applied without a consumer having
    // to guess which of them still hold.
    keyLight.lightLinkCategory.clear();
    keyLight.shadowLinkCategory.clear();
    state.ReplaceLight(keyLight);
    dome.lightLinkCategory.clear();
    state.ReplaceLight(dome);
    state.SetCategoryMemberships({}, false);
    const std::vector<uint8_t> retired = state.BuildPage(nullptr, &commandCount);
    const ParsedPage retiredPage = ParseCommands(retired.data(), retired.size());
    if (!retiredPage.light_link_valid ||
        retiredPage.light_link_count != 1 ||
        !retiredPage.light_links.empty())
    {
        return false;
    }
    const std::vector<uint8_t> stillRetired =
        state.BuildPage(nullptr, &commandCount);
    return ParseCommands(stillRetired.data(), stillRetired.size())
        .light_link_count == 0;
}

/// A scene that authors no linking publishes no table at all, so the feature
/// costs an unlinked scene neither a command nor a page byte.
bool VerifyUnlinkedSceneryPublishesNoLinkTable()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    state.ReplaceLight(light);
    ReplaceSingleMesh(state, "/Geom/Prim", MakeSceneStateRecord("/Geom/Prim", 44));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return !state.HasLightLinks() &&
        page.light_link_count == 0 &&
        page.mesh_upsert_count == 1;
}

/// A DomeLight that authors collection:lightLink resolves into the bounded dome
/// mask, for a textured dome and an untextured one alike.
///
/// The two domes carry complementary collections, so each prim keeps exactly one
/// dome bit. The evidence that the ordering is the one the wire promises is
/// positional: sorted by path, /Lights/DomeA owns dome bit 0 and /Lights/DomeB
/// owns dome bit 1, the ENVIRONMENT record of the textured dome claims that same
/// index, and the untextured dome's frame ambient summand reproduces the
/// scene-wide ambient term it is the only contributor to.
bool VerifyDomeLightLinkResolvesIntoDomeMask()
{
    HdSilkSceneState state;

    HdSilkLightRecord textured;
    textured.path = "/Lights/DomeA";
    textured.ambientOnly = true;
    textured.textureAsset = "/assets/sky.hdr";
    textured.lightLinkCategory = "skyA";
    state.ReplaceLight(textured);

    HdSilkLightRecord untextured;
    untextured.path = "/Lights/DomeB";
    untextured.ambientOnly = true;
    untextured.lightLinkCategory = "skyB";
    state.ReplaceLight(untextured);

    // A dome collection is a reason to collect prim categories, exactly as a
    // direct light's is. Before ABI v21 it was not, and the collection resolved
    // to nothing at all.
    if (!state.HasLightLinks())
    {
        return false;
    }

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership underA;
    underA.path = "/Geom/UnderA";
    underA.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    underA.categories = {"skyA"};
    memberships.push_back(std::move(underA));
    HdSilkCategoryMembership underB;
    underB.path = "/Geom/UnderB";
    underB.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    underB.categories = {"skyB"};
    memberships.push_back(std::move(underB));
    state.SetCategoryMemberships(std::move(memberships), false);

    ReplaceSingleMesh(state, "/Geom/UnderA", MakeSceneStateRecord("/Geom/UnderA", 71));
    ReplaceSingleMesh(state, "/Geom/UnderB", MakeSceneStateRecord("/Geom/UnderB", 72));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.light_link_dome_count != 2 ||
        page.light_link_unsupported != OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE ||
        page.light_links.size() != 2 ||
        page.frame_dome_count != 2 ||
        page.frame_domes.size() != 2)
    {
        return false;
    }

    // A published, non-canonical table indexes the frame's own light and dome
    // orderings. A consumer resolves the masks against those counts, so a table
    // that disagrees with the frame it travels with names a different set of
    // lights or domes than the producer resolved -- and the consumer refuses the
    // whole page rather than masking against the wrong ordering.
    if (page.light_link_light_count != page.frame_light_count ||
        page.light_link_dome_count != page.frame_dome_count)
    {
        return false;
    }

    // Dome bit 0 is the textured dome and bit 1 the untextured one, by path
    // order. Neither prim is in the other's collection, so each keeps one bit.
    if (std::get<0>(page.light_links[0]) != "/Geom/UnderA" ||
        std::get<4>(page.light_links[0]) != 0x1u ||
        std::get<0>(page.light_links[1]) != "/Geom/UnderB" ||
        std::get<4>(page.light_links[1]) != 0x2u)
    {
        return false;
    }

    // The textured dome contributes an image and no ambient colour; the
    // untextured one contributes the whole of the frame ambient term, which is
    // what makes a masked sum reproduce an unmasked one exactly.
    if ((std::get<3>(page.frame_domes[0]) &
            (OPENUSD_SILK_DOME_FLAG_PRESENT | OPENUSD_SILK_DOME_FLAG_TEXTURED)) !=
            (OPENUSD_SILK_DOME_FLAG_PRESENT | OPENUSD_SILK_DOME_FLAG_TEXTURED) ||
        std::get<0>(page.frame_domes[0]) != 0.0f ||
        (std::get<3>(page.frame_domes[1]) & OPENUSD_SILK_DOME_FLAG_TEXTURED) != 0u ||
        (std::get<3>(page.frame_domes[1]) & OPENUSD_SILK_DOME_FLAG_PRESENT) == 0u)
    {
        return false;
    }
    if (std::get<0>(page.frame_domes[1]) != page.frame_ambient[0] ||
        std::get<1>(page.frame_domes[1]) != page.frame_ambient[1] ||
        std::get<2>(page.frame_domes[1]) != page.frame_ambient[2])
    {
        return false;
    }

    // The ENVIRONMENT record names the same bit the dome table published, which
    // is what lets a consumer keep one prefiltered response per dome.
    if (page.environment_dome_indices.size() != 1 ||
        std::get<0>(page.environment_dome_indices[0]) != "/Lights/DomeA" ||
        std::get<1>(page.environment_dome_indices[0]) != 0u ||
        (std::get<2>(page.environment_dome_indices[0]) &
            OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_LINK_COLLECTION) != 0u)
    {
        return false;
    }

    // Retiring the dome collection retires the table exactly once, so a consumer
    // stops masking domes rather than keeping the last mask it saw.
    textured.lightLinkCategory.clear();
    state.ReplaceLight(textured);
    untextured.lightLinkCategory.clear();
    state.ReplaceLight(untextured);
    if (state.HasLightLinks())
    {
        return false;
    }
    const std::vector<uint8_t> retired = state.BuildPage(nullptr, &commandCount);
    const ParsedPage retiredPage = ParseCommands(retired.data(), retired.size());
    return retiredPage.light_link_valid &&
        retiredPage.light_link_count == 1 &&
        retiredPage.light_links.empty() &&
        retiredPage.light_link_dome_count == 0;
}

/// collection:shadowLink on a DomeLight is named, and never applied.
///
/// hdSilk renders no dome shadow map, so a dome caster collection has nothing to
/// restrict. Folding it into the dome's receiver mask would turn "these prims
/// cast my shadow" into "only these prims are lit by me" and darken exactly the
/// prims the author asked to keep lit, so the collection has to leave the dome
/// mask alone and reach the consumer as a diagnostic instead.
bool VerifyDomeShadowLinkIsDiagnosedNotApplied()
{
    HdSilkSceneState state;
    HdSilkLightRecord dome;
    dome.path = "/Lights/Dome";
    dome.ambientOnly = true;
    dome.textureAsset = "/assets/sky.hdr";
    dome.shadowLinkCategory = "casters";
    dome.unsupportedFeatures =
        OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION;
    state.ReplaceLight(dome);

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership receiver;
    receiver.path = "/Geom/Receiver";
    receiver.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(receiver));
    state.SetCategoryMemberships(std::move(memberships), false);
    ReplaceSingleMesh(
        state,
        "/Geom/Receiver",
        MakeSceneStateRecord("/Geom/Receiver", 73));

    // A dome shadow collection alone is not a reason to collect categories:
    // there is no dome shadow mask for it to resolve into.
    if (state.HasLightLinks())
    {
        return false;
    }

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.light_link_count == 0 &&
        page.frame_dome_count == 1 &&
        page.environment_dome_indices.size() == 1 &&
        std::get<1>(page.environment_dome_indices[0]) == 0u &&
        (std::get<2>(page.environment_dome_indices[0]) &
            OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION) != 0u;
}

/// More domes than the bounded table admits publishes no dome table at all and
/// names the loss, rather than making some domes maskable and the rest not.
bool VerifyOverBudgetDomeTableIsDiagnosed()
{
    HdSilkSceneState state;
    for (uint32_t index = 0; index < OPENUSD_SILK_MAX_DOME_LIGHTS + 1u; ++index)
    {
        HdSilkLightRecord dome;
        dome.path = "/Lights/Dome" + std::to_string(index);
        dome.ambientOnly = true;
        dome.textureAsset = "/assets/sky" + std::to_string(index) + ".hdr";
        dome.lightLinkCategory = "sky";
        state.ReplaceLight(dome);
    }

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership outside;
    outside.path = "/Geom/Outside";
    outside.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(outside));
    state.SetCategoryMemberships(std::move(memberships), false);
    ReplaceSingleMesh(
        state,
        "/Geom/Outside",
        MakeSceneStateRecord("/Geom/Outside", 74));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.frame_dome_count != 0 ||
        page.light_link_dome_count != 0 ||
        (page.light_link_unsupported &
            OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET) == 0u)
    {
        return false;
    }

    // Every dome keeps lighting every prim, and every ENVIRONMENT record says so
    // rather than claiming a bit that does not exist.
    if (page.environment_dome_indices.size() != OPENUSD_SILK_MAX_DOME_LIGHTS + 1u)
    {
        return false;
    }
    for (const auto& record : page.environment_dome_indices)
    {
        if (std::get<1>(record) != OPENUSD_SILK_DOME_INDEX_NONE ||
            (std::get<2>(record) &
                OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_LINK_COLLECTION) == 0u)
        {
            return false;
        }
    }
    return true;
}

/// A distant light that authors shadow-enable publishes exactly one bounded
/// descriptor, derived from the caster bounds, and republishes nothing while the
/// scene stands still.
bool VerifyDistantShadowDescriptorIsPublishedAndStable()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.shadowEnabled = 1u;
    state.ReplaceLight(light);
    ReplaceSingleMesh(state, "/Geom/Caster", MakeSceneStateRecord("/Geom/Caster", 60));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    const bool published = page.shadow_valid &&
        page.shadow_count == 1 &&
        page.shadow_descriptor_count == 1 &&
        page.shadow_light_count == 1 &&
        page.shadow_unsupported == OPENUSD_SILK_SHADOW_UNSUPPORTED_NONE &&
        page.shadows.size() == 1 &&
        std::get<0>(page.shadows[0]) == 0u &&
        std::get<1>(page.shadows[0]) == 0u &&
        std::get<2>(page.shadows[0]) ==
            OPENUSD_SILK_DEFAULT_SHADOW_MAP_RESOLUTION &&
        std::get<3>(page.shadows[0]) == OPENUSD_SILK_SHADOW_FLAG_ORTHOGRAPHIC;

    // An unchanged scene must publish nothing: that silence is exactly how a
    // consumer knows its retained shadow map is still the one these lights and
    // these caster bounds produced.
    const std::vector<uint8_t> unchanged = state.BuildPage(nullptr, &commandCount);
    return published &&
        ParseCommands(unchanged.data(), unchanged.size()).shadow_count == 0;
}

/// A light type with no exact light-space projection is named rather than given
/// an approximate map that would shadow the wrong geometry.
bool VerifyUnsupportedShadowLightTypeIsDiagnosed()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Bulb";
    light.type = OPENUSD_SILK_LIGHT_SPHERE;
    light.shadowEnabled = 1u;
    state.ReplaceLight(light);
    ReplaceSingleMesh(state, "/Geom/Caster", MakeSceneStateRecord("/Geom/Caster", 61));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.shadow_valid &&
        page.shadow_count == 1 &&
        page.shadow_descriptor_count == 0 &&
        page.shadow_unsupported == OPENUSD_SILK_SHADOW_UNSUPPORTED_LIGHT_TYPE;
}

/// A shadow-enabled light with no published geometry has no world extent to
/// derive a projection from, and says so instead of publishing an arbitrary one.
bool VerifyShadowWithoutCastersIsDiagnosed()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.shadowEnabled = 1u;
    state.ReplaceLight(light);

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.shadow_valid &&
        page.shadow_count == 1 &&
        page.shadow_descriptor_count == 0 &&
        page.shadow_unsupported == OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS;
}

/// A shadow-linked caster set marks the descriptor, so a consumer that ignores
/// the ABI 18 shadow mask knows it is rendering a different image.
bool VerifyCasterLinkedShadowDescriptorIsFlagged()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    light.shadowEnabled = 1u;
    light.shadowLinkCategory = "casters";
    state.ReplaceLight(light);

    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkCategoryMembership receiver;
    receiver.path = "/Geom/Receiver";
    receiver.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    memberships.push_back(std::move(receiver));
    state.SetCategoryMemberships(std::move(memberships), false);
    ReplaceSingleMesh(
        state,
        "/Geom/Receiver",
        MakeSceneStateRecord("/Geom/Receiver", 62));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.shadow_valid &&
        page.shadow_descriptor_count == 1 &&
        std::get<3>(page.shadows[0]) ==
            (OPENUSD_SILK_SHADOW_FLAG_ORTHOGRAPHIC |
                OPENUSD_SILK_SHADOW_FLAG_CASTER_LINKED);
}

/// A scene that authors no shadow publishes no shadow command at all, so the
/// feature costs an unshadowed scene neither a command nor a page byte.
bool VerifyUnshadowedScenePublishesNoShadowTable()
{
    HdSilkSceneState state;
    HdSilkLightRecord light;
    light.path = "/Lights/Key";
    light.type = OPENUSD_SILK_LIGHT_DISTANT;
    state.ReplaceLight(light);
    ReplaceSingleMesh(state, "/Geom/Prim", MakeSceneStateRecord("/Geom/Prim", 63));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return page.shadow_count == 0 && page.mesh_upsert_count == 1;
}

bool VerifySceneStateSerialization()
{
    HdSilkSceneState state;
    ReplaceSingleMesh(state, "/Zed", MakeSceneStateRecord("/Zed", 2));
    ReplaceSingleMesh(state, "/Alpha", MakeSceneStateRecord("/Alpha", 1, 5));
    uint64_t revision = 0;
    uint32_t commandCount = 0;
    std::vector<uint8_t> firstBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage first = ParseCommands(firstBytes.data(), firstBytes.size());
    if (revision != 1 ||
        commandCount != 3 ||
        first.upsert_paths !=
            std::vector<std::string>({"/Alpha", "/Zed"}))
    {
        return false;
    }

    std::vector<uint8_t> steadyBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage steady = ParseCommands(steadyBytes.data(), steadyBytes.size());
    if (revision != 2 ||
        commandCount != 1 ||
        steady.mesh_upsert_count != 0 ||
        steady.mesh_remove_count != 0)
    {
        return false;
    }

    state.RemoveMesh("/Alpha");
    ReplaceSingleMesh(state, "/Alpha", MakeSceneStateRecord("/Alpha", 9, 1));
    std::vector<uint8_t> readdBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage readd = ParseCommands(readdBytes.data(), readdBytes.size());
    const ParsedMeshIdentity* readdIdentity =
        FindMeshIdentity(readd, "/Alpha");
    if (readd.upsert_paths != std::vector<std::string>({"/Alpha"}) ||
        readd.mesh_remove_count != 0 ||
        readdIdentity == nullptr ||
        readdIdentity->prim_id != 9 ||
        readdIdentity->topology_revision != 1)
    {
        return false;
    }

    state.RemoveMesh("/Zed");
    state.RemoveMesh("/Alpha");
    std::vector<uint8_t> removalBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage removals =
        ParseCommands(removalBytes.data(), removalBytes.size());
    if (removals.remove_paths !=
        std::vector<std::string>({"/Alpha", "/Zed"}))
    {
        return false;
    }

    HdSilkMeshRecord invalidPoints = MakeSceneStateRecord("/InvalidPoints", 3);
    invalidPoints.points.pop_back();
    HdSilkMeshRecord invalidMapping = MakeSceneStateRecord("/InvalidMapping", 4);
    invalidMapping.triangleSubprims.clear();
    HdSilkMeshRecord invalidInstance = MakeSceneStateRecord("/InvalidInstance", 5);
    invalidInstance.instanceIndex = -1;
    return RejectsInvalidSceneStateRecord(std::move(invalidPoints)) &&
        RejectsInvalidSceneStateRecord(std::move(invalidMapping)) &&
        RejectsInvalidSceneStateRecord(std::move(invalidInstance)) &&
        VerifyInstancedSceneStateSerialization() &&
        VerifyNestedInstancerContextSerialization() &&
        VerifySparsePrototypeInstanceSerialization() &&
        VerifyRejectedPayloadDropsWholePath() &&
        VerifyMalformedRecordIsRejectedBeforeTransforms() &&
        VerifySubprimIdentityBudgetIsPreflighted() &&
        VerifyInstanceReferenceReleasesIdentityCapacity() &&
        VerifyPointsIdentityBudgetBoundaryIsExact() &&
        VerifyPointDrawModeRefusesFaceIdentity() &&
        VerifyComplexityKeepsPointIdentity() &&
        VerifyComplexityOriginsFollowEmittedIndices() &&
        VerifyPointDrawModeNamesStrayAuthoredPoints() &&
        VerifyPresentationTopologyRevisionFollowsPresentation() &&
        VerifyComplexityDirtiesConvertedTriangles() &&
        VerifyLightLinkTableResolvesCategories() &&
        VerifyShadowLinkNarrowsOnlyTheShadowMask() &&
        VerifyUnlitBlockerStillPublishesItsShadowBit() &&
        VerifyOversizedLightLinkTableIsDiagnosed() &&
        VerifyDefaultPrimsDoNotConsumeTheLinkBudget() &&
        VerifyCategoryDifferingInstancesResolvingToTheirPathCostNothing() &&
        VerifyOversizedPathOverridesFailOpenAsAWholeGroup() &&
        VerifyUnlinkedSceneryPublishesNoLinkTable() &&
        VerifyInstanceMembershipsFollowPublishedPrototypeIndices() &&
        VerifyNestedInstanceMembershipsResolveComposedIdentities() &&
        VerifyNestedInstanceMembershipsSkipHiddenAndDeepIdentities() &&
        VerifyNestedPrototypeWideMembershipsStaySparse() &&
        VerifyNestedInstanceMembershipsAreBoundedAndDiagnosed() &&
        VerifyNestedInstanceLinksReachTheWire() &&
        VerifyDomeLightLinkResolvesIntoDomeMask() &&
        VerifyDomeShadowLinkIsDiagnosedNotApplied() &&
        VerifyOverBudgetDomeTableIsDiagnosed() &&
        VerifyDistantShadowDescriptorIsPublishedAndStable() &&
        VerifyUnsupportedShadowLightTypeIsDiagnosed() &&
        VerifyShadowWithoutCastersIsDiagnosed() &&
        VerifyCasterLinkedShadowDescriptorIsFlagged() &&
        VerifyUnshadowedScenePublishesNoShadowTable();
}

openusd_status Sync(
    openusd_silk_session* session,
    ParsedPage* parsed,
    openusd_error_buffer* error,
    const openusd_render_camera* requestedCamera = nullptr,
    double timeCode = 0.0,
    const uint32_t* requestedComplexity = nullptr)
{
    const openusd_render_camera automatic = AutomaticCamera();
    const openusd_render_camera* camera =
        requestedCamera == nullptr ? &automatic : requestedCamera;
    openusd_silk_page* page = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    const openusd_status status = requestedComplexity == nullptr
        ? openusd_silk_session_sync(
            session,
            64,
            64,
            timeCode,
            camera,
            &page,
            &view,
            error)
        : openusd_silk_session_sync_with_complexity(
            session,
            64,
            64,
            timeCode,
            camera,
            *requestedComplexity,
            &page,
            &view,
            error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    if (view.struct_size != sizeof(openusd_silk_page_view) ||
        view.abi_version != OPENUSD_SILK_PAGE_ABI_VERSION)
    {
        openusd_silk_page_release(page);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    *parsed = ParseCommands(view.data, view.data_size);
    const bool countMatches = parsed->parsed_count == view.command_count;
    openusd_silk_page_release(page);
    return countMatches ? OPENUSD_STATUS_OK : OPENUSD_STATUS_NATIVE_ERROR;
}

bool IsLegacyAutomaticFrame(const ParsedPage& parsed)
{
    GfMatrix4d view(1.0);
    view.SetLookAt(
        GfVec3d(4.0, 3.0, 4.0),
        GfVec3d(0.0, 0.0, 0.0),
        GfVec3d(0.0, 1.0, 0.0));
    GfFrustum frustum;
    frustum.SetPerspective(45.0, 1.0, 0.1, 1000.0);
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    return parsed.frame_width == 64 &&
        parsed.frame_height == 64 &&
        std::memcmp(
            parsed.frame_view.data(),
            view.GetArray(),
            sizeof(parsed.frame_view)) == 0 &&
        std::memcmp(
            parsed.frame_projection.data(),
            projection.GetArray(),
            sizeof(parsed.frame_projection)) == 0;
}

bool IsExplicitFrame(
    const ParsedPage& parsed,
    const openusd_render_camera& camera)
{
    // The published projection is conformed to the viewport aspect, matching
    // what UsdImagingGLEngine's free camera does for Storm. This camera declares
    // aspect 1.5 while the probe syncs a square 64x64 viewport, so the conformed
    // matrix must differ from the authored one -- asserting that difference is
    // what stops this check passing if the conform were dropped again.
    GfMatrix4d authored(1.0);
    std::memcpy(
        const_cast<double*>(authored.GetArray()),
        camera.projection,
        sizeof(camera.projection));
    const GfMatrix4d conformed =
        CameraUtilConformedWindow(authored, CameraUtilFit, 1.0);
    if (conformed == authored)
    {
        return false;
    }
    return parsed.frame_width == 64 &&
        parsed.frame_height == 64 &&
        std::memcmp(
            parsed.frame_view.data(),
            camera.view,
            sizeof(camera.view)) == 0 &&
        std::memcmp(
            parsed.frame_projection.data(),
            conformed.GetArray(),
            sizeof(camera.projection)) == 0;
}

bool RejectsCamera(
    openusd_silk_session* session,
    const openusd_render_camera* camera,
    openusd_error_buffer* error)
{
    openusd_silk_page* page = reinterpret_cast<openusd_silk_page*>(1);
    openusd_silk_page_view view{};
    view.struct_size = sizeof(view);
    return openusd_silk_session_sync(
               session,
               64,
               64,
               0.0,
               camera,
               &page,
               &view,
               error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
        page == nullptr;
}

bool VerifyCameraValidation(
    openusd_silk_session* session,
    openusd_error_buffer* error)
{
    openusd_render_camera invalid = ExplicitCamera();
    invalid.struct_size = sizeof(invalid) - 1;
    if (!RejectsCamera(session, &invalid, error))
    {
        return false;
    }
    invalid = ExplicitCamera();
    invalid.mode = static_cast<openusd_render_camera_mode>(99);
    if (!RejectsCamera(session, &invalid, error))
    {
        return false;
    }
    invalid = ExplicitCamera();
    invalid.view[3] = std::nan("");
    if (!RejectsCamera(session, &invalid, error))
    {
        return false;
    }
    invalid = ExplicitCamera();
    invalid.projection[7] = std::numeric_limits<double>::infinity();
    return RejectsCamera(session, &invalid, error) &&
        RejectsCamera(session, nullptr, error);
}

/// Requires the exact attribute table the probe stage authors. Element counts
/// and values are asserted, not just presence, so a table that arrives with the
/// right shape but the wrong data still fails.
bool VerifyPrimvarAttributes(const std::vector<ParsedAttribute>& attributes)
{
    const ParsedAttribute* st = nullptr;
    const ParsedAttribute* weight = nullptr;
    const ParsedAttribute* tint = nullptr;
    const ParsedAttribute* normals = nullptr;
    const ParsedAttribute* faceWeight = nullptr;
    const ParsedAttribute* uniformWeight = nullptr;
    for (const ParsedAttribute& attribute : attributes)
    {
        if (attribute.name == "st")
        {
            st = &attribute;
        }
        else if (attribute.name == "probeWeight")
        {
            weight = &attribute;
        }
        else if (attribute.name == "probeTint")
        {
            tint = &attribute;
        }
        else if (attribute.name == "normals")
        {
            normals = &attribute;
        }
        else if (attribute.name == "probeFaceWeight")
        {
            faceWeight = &attribute;
        }
        else if (attribute.name == "probeUniformWeight")
        {
            uniformWeight = &attribute;
        }
    }
    if (st == nullptr || weight == nullptr || tint == nullptr ||
        normals == nullptr || faceWeight == nullptr || uniformWeight == nullptr)
    {
        return false;
    }

    // Entries are sorted by name, so an unchanged scene produces byte-identical
    // pages. Checking the order here keeps that contract honest.
    for (size_t index = 1; index < attributes.size(); ++index)
    {
        if (attributes[index - 1].name > attributes[index].name)
        {
            return false;
        }
    }

    return st->semantic == OPENUSD_SILK_ATTRIBUTE_TEXCOORD &&
        st->componentCount == 2 &&
        st->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        st->elementCount == 6 &&
        st->firstValue == 0.0F &&
        weight->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        weight->componentCount == 1 &&
        weight->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        weight->elementCount == 6 &&
        weight->firstValue == 0.25F &&
        tint->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        tint->componentCount == 3 &&
        tint->interpolation == OPENUSD_SILK_INTERPOLATION_CONSTANT &&
        tint->elementCount == 1 &&
        tint->firstValue == 0.2F &&
        normals->semantic == OPENUSD_SILK_ATTRIBUTE_NORMAL &&
        normals->componentCount == 3 &&
        normals->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        normals->elementCount == 6 &&
        normals->firstValue == 0.0F &&
        faceWeight->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        faceWeight->componentCount == 1 &&
        faceWeight->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        faceWeight->elementCount == 6 &&
        faceWeight->firstValue == 0.125F &&
        uniformWeight->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        uniformWeight->componentCount == 1 &&
        uniformWeight->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        uniformWeight->elementCount == 6 &&
        uniformWeight->firstValue == 0.875F;
}

bool AuthorSharedMesh(
    openusd_stage* stage,

    float firstX,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 3> points{
        openusd_vec3f{firstX, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 1> counts{3};
    const std::array<int32_t, 3> indices{0, 1, 2};
    return openusd_geom_define_mesh(stage, SharedMeshPath, error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_points(
            stage, SharedMeshPath, points.data(), points.size(), 0, 0.0, error) ==
            OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            SharedMeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK;
}

/// Authors a UsdPreviewSurface with one constant input and one connected
/// UsdUVTexture, then binds it to the shared mesh. This exercises the real
/// Hydra path end to end rather than the serializer alone: the resolution
/// depends on Hydra building the network map from these authored opinions.
/// Authors two meshes and one distant light whose UsdLux light-link collection
/// keeps its schema-default includeRoot and excludes one of them.
///
/// This is the only place the collection itself is authored on a real stage and
/// read back through UsdImaging, Hydra categories and hdSilk together. Every
/// other light-linking case builds the resolved category memberships directly,
/// which proves the wire and the masking but not that a UsdLux collection ever
/// becomes those memberships. The excludes-with-includeRoot shape is the one an
/// Omniverse-authored stage uses most: the light lights the world, minus a set.
bool AuthorLinkedLighting(openusd_stage* stage, openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 3> points{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 1> counts{3};
    const std::array<int32_t, 3> indices{0, 1, 2};
    for (const char* path : {LinkedLitMeshPath, LinkedUnlitMeshPath})
    {
        if (openusd_geom_define_mesh(stage, path, error) != OPENUSD_STATUS_OK ||
            openusd_geom_mesh_set_points(
                stage, path, points.data(), points.size(), 0, 0.0, error) !=
                OPENUSD_STATUS_OK ||
            openusd_geom_mesh_set_topology(
                stage,
                path,
                counts.data(),
                counts.size(),
                indices.data(),
                indices.size(),
                error) != OPENUSD_STATUS_OK)
        {
            return false;
        }
    }

    if (openusd_lux_define(
            stage,
            LinkedLightPath,
            OPENUSD_LUX_SCHEMA_DISTANT_LIGHT,
            error) != OPENUSD_STATUS_OK)
    {
        return false;
    }

    // UsdLuxLightAPI declares collection:lightLink as a built-in collection whose
    // includeRoot fallback is true, so authoring only the exclusion is what
    // "everything but this prim" looks like on a real stage.
    // The list view is a NUL-terminated packed buffer with a byte-sized offset
    // table, so data_size counts the terminator and offsets_size is measured in
    // bytes rather than entries.
    const std::string excluded(LinkedUnlitMeshPath);
    const std::array<size_t, 1> offsets{0};
    openusd_string_list_view targets{};
    targets.struct_size = sizeof(openusd_string_list_view);
    targets.data = excluded.c_str();
    targets.data_size = excluded.size() + 1;
    targets.offsets = offsets.data();
    targets.offsets_size = offsets.size() * sizeof(size_t);
    targets.count = 1;
    if (openusd_stage_create_relationship(
            stage,
            LinkedLightPath,
            "collection:lightLink:excludes",
            error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_relationship_targets(
            stage,
            LinkedLightPath,
            "collection:lightLink:excludes",
            &targets,
            error) != OPENUSD_STATUS_OK)
    {
        return false;
    }

    // The same collection shape on a DomeLight. UsdLuxLightAPI declares
    // collection:lightLink on every light, dome included, and since ABI v21
    // hdSilk resolves a dome's into the bounded dome mask -- so this is the end
    // to end evidence that a real dome collection travels through UsdImaging's
    // collection cache and Hydra's prim categories into a per-prim dome bit,
    // includeRoot fallback and all.
    if (openusd_lux_define(
            stage,
            LinkedDomePath,
            OPENUSD_LUX_SCHEMA_DOME_LIGHT,
            error) != OPENUSD_STATUS_OK)
    {
        return false;
    }
    return openusd_stage_create_relationship(
            stage,
            LinkedDomePath,
            "collection:lightLink:excludes",
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_relationship_targets(
            stage,
            LinkedDomePath,
            "collection:lightLink:excludes",
            &targets,
            error) == OPENUSD_STATUS_OK;
}

bool AuthorSharedMaterial(openusd_stage* stage, openusd_error_buffer* error)
{
    return openusd_shade_define_material(stage, MaterialPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_define_shader(stage, SurfaceShaderPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_shader_set_source_id(
            stage, SurfaceShaderPath, "UsdPreviewSurface", error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            SurfaceShaderPath,
            "roughness",
            OPENUSD_SHADE_VALUE_FLOAT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_set_input_float(
            stage, SurfaceShaderPath, "roughness", 0.375F, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            SurfaceShaderPath,
            "diffuseColor",
            OPENUSD_SHADE_VALUE_COLOR3F,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_output(
            stage,
            SurfaceShaderPath,
            "surface",
            OPENUSD_SHADE_VALUE_TOKEN,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_define_shader(stage, TextureShaderPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_shader_set_source_id(
            stage, TextureShaderPath, "UsdUVTexture", error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            TextureShaderPath,
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_set_input_string(
            stage,
            TextureShaderPath,
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            MaterialTextureAsset,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_output(
            stage,
            TextureShaderPath,
            "rgb",
            OPENUSD_SHADE_VALUE_FLOAT3,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_output(
            stage,
            TextureShaderPath,
            "b",
            OPENUSD_SHADE_VALUE_FLOAT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            SurfaceShaderPath,
            "metallic",
            OPENUSD_SHADE_VALUE_FLOAT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_connect(
            stage,
            SurfaceShaderPath,
            "diffuseColor",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            TextureShaderPath,
            "rgb",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            error) == OPENUSD_STATUS_OK &&
        // The same texture prim also drives metallic, from a different output.
        // Only the output token distinguishes the two connections, so this is
        // what proves the token survives resolution rather than being inferred.
        openusd_shade_connect(
            stage,
            SurfaceShaderPath,
            "metallic",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            TextureShaderPath,
            "b",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_material_create_surface_output(stage, MaterialPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_connect(
            stage,
            MaterialPath,
            "surface",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            SurfaceShaderPath,
            "surface",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_material_bind(stage, SharedMeshPath, MaterialPath, error) ==
            OPENUSD_STATUS_OK;
}

bool EditSharedMeshPoints(
    openusd_stage* stage,

    float firstX,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 3> points{
        openusd_vec3f{firstX, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    return openusd_geom_mesh_set_points(
               stage,
               SharedMeshPath,
               points.data(),
               points.size(),
               0,
               0.0,
               error) == OPENUSD_STATUS_OK;
}

bool AuthorTopologyMesh(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 7> points{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 1.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F},
        openusd_vec3f{2.0F, 0.0F, 0.0F},
        openusd_vec3f{3.0F, 0.0F, 0.0F},
        openusd_vec3f{2.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 3> counts{4, 3, 2};
    const std::array<int32_t, 9> indices{0, 1, 2, 3, 4, 5, 6, 0, 1};
    return openusd_geom_define_mesh(stage, TopologyMeshPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_points(
            stage,
            TopologyMeshPath,
            points.data(),
            points.size(),
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            TopologyMeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK;
}

/// Authors one linear segmented BasisCurves prim. The generic vec3f setter
/// creates Float3Array, while a typed BasisCurves prim predeclares points as
/// Point3fArray, so the arrays are authored before the schema is applied and
/// the schema tokens are re-authored afterwards. That keeps the probe on the
/// public C ABI without a test-only BasisCurves authoring entry point.
bool AuthorLinearSegmentedCurves(
    openusd_stage* stage,
    const char* path,
    const openusd_vec3f* points,
    size_t pointCount,
    const int32_t* counts,
    size_t countCount,
    const float* widths,
    size_t widthCount,
    openusd_error_buffer* error)
{
    return openusd_stage_define_prim(stage, path, "Scope", error) ==
            OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "type", "linear", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "basis", "bezier", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "wrap", "segmented", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_vec3f_array(
            stage,
            path,
            "points",
            points,
            pointCount,
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_int32_array(
            stage,
            path,
            "curveVertexCounts",
            counts,
            countCount,
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_float_array(
            stage,
            path,
            "widths",
            widths,
            widthCount,
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_define_prim(stage, path, "BasisCurves", error) ==
            OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "type", "linear", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "basis", "bezier", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "wrap", "segmented", 0, 0.0, error) == OPENUSD_STATUS_OK;
}

bool AuthorBasisCurves(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 2> points{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F}};
    const std::array<int32_t, 1> counts{2};
    const std::array<float, 1> widths{0.2F};

    // Two curves whose four control points each author their own width.
    // UsdGeomCurves defaults the widths interpolation to "vertex", so this is
    // exactly the case that used to delete the prim from the page instead of
    // publishing it.
    const std::array<openusd_vec3f, 4> varyingPoints{
        openusd_vec3f{0.0F, 2.0F, 0.0F},
        openusd_vec3f{1.0F, 2.0F, 0.0F},
        openusd_vec3f{0.0F, 3.0F, 0.0F},
        openusd_vec3f{1.0F, 3.0F, 0.0F}};
    const std::array<int32_t, 2> varyingCounts{2, 2};
    const std::array<float, 4> varyingWidths{0.1F, 0.2F, 0.3F, 0.4F};

    // Two curves with one authored width each. UsdGeomCurves still defaults the
    // declared interpolation to "vertex", so the element count is what selects
    // uniform here, and both endpoints of a curve must receive that curve's
    // single value.
    const std::array<openusd_vec3f, 4> uniformPoints{
        openusd_vec3f{0.0F, 4.0F, 0.0F},
        openusd_vec3f{1.0F, 4.0F, 0.0F},
        openusd_vec3f{0.0F, 5.0F, 0.0F},
        openusd_vec3f{1.0F, 5.0F, 0.0F}};
    const std::array<int32_t, 2> uniformCounts{2, 2};
    const std::array<float, 2> uniformWidths{0.25F, 0.75F};

    return AuthorLinearSegmentedCurves(
            stage,
            BasisCurvesPath,
            points.data(),
            points.size(),
            counts.data(),
            counts.size(),
            widths.data(),
            widths.size(),
            error) &&
        AuthorLinearSegmentedCurves(
            stage,
            VaryingWidthCurvesPath,
            varyingPoints.data(),
            varyingPoints.size(),
            varyingCounts.data(),
            varyingCounts.size(),
            varyingWidths.data(),
            varyingWidths.size(),
            error) &&
        AuthorLinearSegmentedCurves(
            stage,
            UniformWidthCurvesPath,
            uniformPoints.data(),
            uniformPoints.size(),
            uniformCounts.data(),
            uniformCounts.size(),
            uniformWidths.data(),
            uniformWidths.size(),
            error);
}

const ParsedAttribute* FindAttribute(
    const std::vector<ParsedAttribute>& attributes,
    const char* name)
{
    for (const ParsedAttribute& attribute : attributes)
    {
        if (attribute.name == name)
        {
            return &attribute;
        }
    }
    return nullptr;
}

bool VerifyBasisCurvesTessellation(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1};
    const std::vector<uint32_t> expectedSubprims{0};
    const ParsedCurves& curves = page.basis_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    // A single authored width stays a one-element CONSTANT entry, so the
    // constant-width payload is exactly what it was before non-constant widths
    // were resolved.
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 1 &&
        curves.points == expectedPoints &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH &&
        widths->componentCount == 1 &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_CONSTANT &&
        widths->values == std::vector<float>{0.2F};
}

/// Proves authored per-control-point widths reach the wire resolved onto the
/// emitted line vertices, and that the curve is published at all: before width
/// interpolation was resolved, a four-element widths array made hdSilk drop the
/// whole prim, so this scene rendered nothing.
bool VerifyVaryingWidthCurves(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 2.0F, 0.0F,
        1.0F, 2.0F, 0.0F,
        0.0F, 3.0F, 0.0F,
        1.0F, 3.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1, 2, 3};
    const std::vector<uint32_t> expectedSubprims{0, 1};
    const std::vector<float> expectedWidths{0.1F, 0.2F, 0.3F, 0.4F};
    const ParsedCurves& curves = page.varying_width_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 2 &&
        curves.points == expectedPoints &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH &&
        widths->componentCount == 1 &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        widths->elementCount == 4 &&
        widths->values == expectedWidths;
}

/// Proves per-curve widths are expanded onto both endpoints of every segment the
/// curve emits. Two curves, one authored width each: the first segment must
/// carry 0.25 twice and the second 0.75 twice, which no endpoint-selecting or
/// first-value-wins shortcut produces.
bool VerifyUniformWidthCurves(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 4.0F, 0.0F,
        1.0F, 4.0F, 0.0F,
        0.0F, 5.0F, 0.0F,
        1.0F, 5.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1, 2, 3};
    const std::vector<uint32_t> expectedSubprims{0, 1};
    const std::vector<float> expectedWidths{0.25F, 0.25F, 0.75F, 0.75F};
    const ParsedCurves& curves = page.uniform_width_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 2 &&
        curves.points == expectedPoints &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH &&
        widths->componentCount == 1 &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        widths->elementCount == 4 &&
        widths->values == expectedWidths;
}

/// Pins the winding hdSilk emits for each authored USD "orientation".
///
/// USD authors face winding with `orientation`: rightHanded faces are wound
/// counter-clockwise seen from the front, leftHanded clockwise. Every backend
/// hdSilk targets rasterizes with counter-clockwise front faces, so the emitted
/// triangle list has to carry that distinction faithfully. It already does:
/// HdMeshUtil::ComputeTriangleIndices reverses the corners of a leftHanded face
/// itself, so this delegate must pass its output through untouched. That was
/// measured rather than assumed -- adding a reversal here, on the theory that
/// HdMeshUtil ignored orientation and left the convention to the renderer, made
/// the leftHanded quad publish the double-reversed order 2,0,1,2,3,0, which is
/// the inverted facing this case now locks out in both directions.
///
/// The two quads are byte-identical apart from that one token, so the emitted
/// indices are the only place the difference can appear. The third mesh carries
/// a face-varying primvar, which forces the expanded-topology path: that data
/// is one element per triangulated corner in the same HdMeshUtil order, so it
/// stays aligned with the triangles only while neither array is permuted alone.
/// All three live in hdsilk-probe-stage.usda rather than being authored here,
/// because primvar interpolation is attribute metadata and the C ABI exposes
/// prim metadata only.
bool VerifyOrientationWinding(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        1.0F, 1.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    const std::vector<uint32_t> expectedSubprims{0, 0};

    const ParsedCurves& right = page.right_handed_mesh;
    const ParsedCurves& left = page.left_handed_mesh;
    if (!right.found || !left.found ||
        right.topology_kind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
        left.topology_kind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
        right.triangle_count != 2 || left.triangle_count != 2 ||
        right.points != expectedPoints || left.points != expectedPoints ||
        right.subprims != expectedSubprims ||
        left.subprims != expectedSubprims)
    {
        std::cerr << "hdSilk orientation probe: the two quads did not publish "
                  << "matching points or subprims.\n";
        return false;
    }

    // A rightHanded quad triangulates to (0, 1, 2) and (0, 2, 3). The
    // leftHanded quad emits the opposite winding, up to the cyclic rotation
    // HdMeshUtil happens to produce. That opposition is the invariant:
    // publishing the rightHanded order for both, or reversing an already
    // reversed leftHanded face, both break it.
    const std::vector<uint32_t> expectedRight{0, 1, 2, 0, 2, 3};
    const std::vector<uint32_t> expectedLeft{2, 1, 0, 2, 0, 3};
    if (right.indices != expectedRight || left.indices != expectedLeft)
    {
        std::cerr << "hdSilk orientation probe: expected rightHanded indices "
                  << "0,1,2,0,2,3 and leftHanded 2,1,0,2,0,3; got right=";
        for (uint32_t index : right.indices)
        {
            std::cerr << index << ',';
        }
        std::cerr << " left=";
        for (uint32_t index : left.indices)
        {
            std::cerr << index << ',';
        }
        std::cerr << "\n";
        return false;
    }

    // The face-varying mesh expands its topology: face-varying data is one
    // element per triangulated corner in HdMeshUtil's order, so the emitted
    // vertices are one per corner and the winding shows up in the point order
    // rather than in the indices. Every emitted vertex must carry the corner
    // value authored for the point it was expanded from. cornerId is authored
    // 10, 11, 12, 13 for points 0, 1, 2, 3, so this is a direct
    // point-to-corner cross-check that permuting one array without the other
    // cannot satisfy.
    const ParsedCurves& faceVarying = page.left_handed_face_varying_mesh;
    const ParsedAttribute* corner =
        FindAttribute(faceVarying.attributes, "cornerId");
    const std::vector<uint32_t> expandedIndices{0, 1, 2, 3, 4, 5};
    if (!faceVarying.found ||
        faceVarying.triangle_count != 2 ||
        faceVarying.indices != expandedIndices ||
        faceVarying.points.size() != 18 ||
        corner == nullptr ||
        corner->interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX ||
        corner->componentCount != 1 ||
        corner->values.size() != 6)
    {
        std::cerr << "hdSilk orientation probe: the leftHanded face-varying "
                  << "quad did not publish six expanded corners. found="
                  << faceVarying.found
                  << " triangles=" << faceVarying.triangle_count
                  << " indices=" << faceVarying.indices.size()
                  << " pointFloats=" << faceVarying.points.size()
                  << " attributes=";
        for (const ParsedAttribute& attribute : faceVarying.attributes)
        {
            std::cerr << attribute.name << '(' << attribute.elementCount << ')';
        }
        std::cerr << "\n";
        return false;
    }
    for (size_t vertex = 0; vertex < 6; ++vertex)
    {
        const float x = faceVarying.points[vertex * 3];
        const float y = faceVarying.points[(vertex * 3) + 1];
        float expected = -1.0F;
        if (x == 0.0F && y == 0.0F)
        {
            expected = 10.0F;
        }
        else if (x == 1.0F && y == 0.0F)
        {
            expected = 11.0F;
        }
        else if (x == 1.0F && y == 1.0F)
        {
            expected = 12.0F;
        }
        else if (x == 0.0F && y == 1.0F)
        {
            expected = 13.0F;
        }
        if (corner->values[vertex] != expected)
        {
            std::cerr << "hdSilk orientation probe: expanded vertex " << vertex
                      << " at (" << x << ", " << y << ") carries corner "
                      << corner->values[vertex] << ", expected " << expected
                      << "\n";
            return false;
        }
    }

    return true;
}

bool NearlyEqual(const std::vector<float>& left, const std::vector<float>& right)
{
    if (left.size() != right.size())
    {
        return false;
    }
    for (size_t index = 0; index < left.size(); ++index)
    {
        if (std::fabs(left[index] - right[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Medium complexity halves every emitted segment, and a vertex attribute has to
/// be interpolated at the same parameter as the position rather than copied from
/// the nearer endpoint. The varying-width curve authors 0.1 -> 0.2 and 0.3 ->
/// 0.4, so a correct 2x subdivision publishes the midpoints 0.15 and 0.35;
/// endpoint selection would publish 0.1, 0.1, 0.2, 0.2 and a step function where
/// the authored data is a ramp.
bool VerifyMediumComplexityInterpolatesWidths(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 2.0F, 0.0F,
        0.5F, 2.0F, 0.0F,
        0.5F, 2.0F, 0.0F,
        1.0F, 2.0F, 0.0F,
        0.0F, 3.0F, 0.0F,
        0.5F, 3.0F, 0.0F,
        0.5F, 3.0F, 0.0F,
        1.0F, 3.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1, 2, 3, 4, 5, 6, 7};
    const std::vector<uint32_t> expectedSubprims{0, 0, 1, 1};
    const std::vector<float> expectedWidths{
        0.10F, 0.15F,
        0.15F, 0.20F,
        0.30F, 0.35F,
        0.35F, 0.40F};
    const std::vector<float> expectedNormals{
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F};
    const ParsedCurves& curves = page.varying_width_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    const ParsedAttribute* normals = FindAttribute(curves.attributes, "normals");
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 4 &&
        NearlyEqual(curves.points, expectedPoints) &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        widths->elementCount == 8 &&
        NearlyEqual(widths->values, expectedWidths) &&
        normals != nullptr &&
        normals->elementCount == 8 &&
        NearlyEqual(normals->values, expectedNormals);
}

/// Direct coverage of the width resolver and the line builder, without a render
/// index. UsdImaging never authors HdBasisCurvesTopology curve indices, so an
/// indexed topology cannot be reached from a stage; it is still a topology Hydra
/// may hand a delegate, and the vertex-width lookup has to follow the resolved
/// point index exactly as the position does.
bool VerifyCurveWidthResolution()
{
    const VtIntArray counts({2, 2});
    const VtIntArray indices({3, 2, 1, 0});
    const HdBasisCurvesTopology indexed(
        HdTokens->linear,
        HdTokens->bezier,
        HdTokens->segmented,
        counts,
        indices);
    const HdBasisCurvesTopology plain(
        HdTokens->linear,
        HdTokens->bezier,
        HdTokens->segmented,
        counts,
        VtIntArray());
    VtVec3fArray points(4);
    for (size_t point = 0; point < 4; ++point)
    {
        points[point] = GfVec3f(static_cast<float>(point), 0.0F, 0.0F);
    }

    // Indexed vertex widths are sized to the points array and are selected
    // through the same curve indices as the positions, so the emitted order is
    // the reversed authored order here.
    HdSilkCurveWidths indexedWidths;
    if (!HdSilkResolveCurveWidths(
            indexed,
            points.size(),
            VtValue(VtFloatArray({0.1F, 0.2F, 0.3F, 0.4F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &indexedWidths) ||
        indexedWidths.interpolation != HdSilkCurveWidthInterpolation::Vertex)
    {
        return false;
    }
    HdSilkMeshRecord indexedRecord;
    if (!HdSilkBuildLinearSegmentedCurveLines(
            indexed, points, indexedWidths, &indexedRecord))
    {
        return false;
    }
    const ParsedAttribute* indexedWidthAttribute = nullptr;
    std::vector<ParsedAttribute> indexedAttributes;
    for (const HdSilkMeshAttribute& attribute : indexedRecord.attributes)
    {
        ParsedAttribute parsed;
        parsed.name = attribute.name;
        parsed.semantic = attribute.semantic;
        parsed.componentCount = attribute.componentCount;
        parsed.interpolation = attribute.interpolation;
        parsed.elementCount = static_cast<uint32_t>(
            attribute.data.size() / attribute.componentCount);
        parsed.values = attribute.data;
        indexedAttributes.push_back(std::move(parsed));
    }
    indexedWidthAttribute = FindAttribute(indexedAttributes, "widths");
    const std::vector<float> expectedIndexedPoints{
        3.0F, 0.0F, 0.0F,
        2.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        0.0F, 0.0F, 0.0F};
    if (indexedWidthAttribute == nullptr ||
        indexedRecord.points != expectedIndexedPoints ||
        !NearlyEqual(
            indexedWidthAttribute->values,
            std::vector<float>({0.4F, 0.3F, 0.2F, 0.1F})))
    {
        return false;
    }

    // An indexed topology sizes vertex widths to the points array, so the
    // flattened control-point count is not a substitute for it.
    if (HdSilkExpectedCurveWidthCount(
            indexed, 7, HdSilkCurveWidthInterpolation::Vertex) != 7 ||
        HdSilkExpectedCurveWidthCount(
            plain, 7, HdSilkCurveWidthInterpolation::Vertex) != 4 ||
        HdSilkExpectedCurveWidthCount(
            indexed, 7, HdSilkCurveWidthInterpolation::Uniform) != 2 ||
        HdSilkExpectedCurveWidthCount(
            indexed, 7, HdSilkCurveWidthInterpolation::Constant) != 1)
    {
        return false;
    }

    // An unindexed curve resolves a point by its flattened ordinal, so a widths
    // array parallel to a points array longer than the curves consume -- which
    // USD permits -- indexes identically and is accepted alongside the
    // control-point count. A shorter one is not: the builder would read past it.
    if (!HdSilkCurveWidthCountMatches(
            plain, 7, HdSilkCurveWidthInterpolation::Vertex, 7) ||
        !HdSilkCurveWidthCountMatches(
            plain, 7, HdSilkCurveWidthInterpolation::Vertex, 4) ||
        HdSilkCurveWidthCountMatches(
            plain, 3, HdSilkCurveWidthInterpolation::Vertex, 3) ||
        HdSilkCurveWidthCountMatches(
            indexed, 7, HdSilkCurveWidthInterpolation::Vertex, 4))
    {
        return false;
    }

    // The same case end to end: six points, four of which two curves consume,
    // and six authored widths. Before the points-sized count was accepted this
    // fell through to the default width and published a flat 1.0.
    VtVec3fArray longPoints(6);
    for (size_t point = 0; point < 6; ++point)
    {
        longPoints[point] = GfVec3f(static_cast<float>(point), 0.0F, 0.0F);
    }
    HdSilkCurveWidths trailing;
    if (!HdSilkResolveCurveWidths(
            plain,
            longPoints.size(),
            VtValue(VtFloatArray({0.1F, 0.2F, 0.3F, 0.4F, 0.5F, 0.6F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &trailing) ||
        trailing.interpolation != HdSilkCurveWidthInterpolation::Vertex)
    {
        return false;
    }
    HdSilkMeshRecord trailingRecord;
    if (!HdSilkBuildLinearSegmentedCurveLines(
            plain, longPoints, trailing, &trailingRecord))
    {
        return false;
    }
    {
        std::vector<ParsedAttribute> parsedAttributes;
        for (const HdSilkMeshAttribute& attribute : trailingRecord.attributes)
        {
            ParsedAttribute parsed;
            parsed.name = attribute.name;
            parsed.semantic = attribute.semantic;
            parsed.componentCount = attribute.componentCount;
            parsed.interpolation = attribute.interpolation;
            parsed.elementCount = static_cast<uint32_t>(
                attribute.data.size() / attribute.componentCount);
            parsed.values = attribute.data;
            parsedAttributes.push_back(std::move(parsed));
        }
        const ParsedAttribute* trailingWidths =
            FindAttribute(parsedAttributes, "widths");
        if (trailingWidths == nullptr ||
            !NearlyEqual(
                trailingWidths->values,
                std::vector<float>({0.1F, 0.2F, 0.3F, 0.4F})))
        {
            return false;
        }
    }

    // A per-curve array is inferred as uniform even when the delegate declares
    // vertex, because only the element count can be right.
    HdSilkCurveWidths uniform;
    if (!HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtFloatArray({0.25F, 0.75F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &uniform) ||
        uniform.interpolation != HdSilkCurveWidthInterpolation::Uniform ||
        !NearlyEqual(uniform.values, std::vector<float>({0.25F, 0.75F})))
    {
        return false;
    }

    // Doubles are accepted, negatives clamp to zero, an empty value falls back
    // to the UsdGeomCurves default, and a non-finite or unexplainable array is
    // rejected so the caller can publish the default instead of guessing.
    HdSilkCurveWidths doubles;
    HdSilkCurveWidths empty;
    HdSilkCurveWidths rejected;

    // Half-precision widths reach the delegate whenever a scene index or a
    // delegate hands Hydra the half primvar it authored, and GfHalf converts to
    // float exactly for these values. Both the array and the scalar form are
    // covered; the scalar resolves as constant because one element can only be
    // that for a two-curve topology.
    HdSilkCurveWidths halfArray;
    HdSilkCurveWidths halfScalar;
    if (!HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtHalfArray({
                GfHalf(-1.0F),
                GfHalf(0.5F),
                GfHalf(0.25F),
                GfHalf(0.125F)})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &halfArray) ||
        halfArray.interpolation != HdSilkCurveWidthInterpolation::Vertex ||
        !NearlyEqual(
            halfArray.values,
            std::vector<float>({0.0F, 0.5F, 0.25F, 0.125F})) ||
        !HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(GfHalf(0.75F)),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &halfScalar) ||
        halfScalar.interpolation != HdSilkCurveWidthInterpolation::Constant ||
        !NearlyEqual(halfScalar.values, std::vector<float>({0.75F})))
    {
        return false;
    }

    return HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtDoubleArray({-1.0, 2.0, 3.0, 4.0})),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &doubles) &&
        doubles.interpolation == HdSilkCurveWidthInterpolation::Vertex &&
        NearlyEqual(doubles.values, std::vector<float>({0.0F, 2.0F, 3.0F, 4.0F})) &&
        HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &empty) &&
        empty.interpolation == HdSilkCurveWidthInterpolation::Constant &&
        NearlyEqual(empty.values, std::vector<float>({1.0F})) &&
        !HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtFloatArray({0.1F, 0.2F, 0.3F})),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &rejected) &&
        !HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtFloatArray({
                0.1F,
                std::numeric_limits<float>::quiet_NaN(),
                0.3F,
                0.4F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &rejected);
}

bool EditSharedMeshTopologyToQuad(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 4> points{
        openusd_vec3f{2.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 1.0F, 0.0F},
        openusd_vec3f{2.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 1> counts{4};
    const std::array<int32_t, 4> indices{0, 1, 2, 3};
    return openusd_geom_mesh_set_points(
               stage,
               SharedMeshPath,
               points.data(),
               points.size(),
               0,
               0.0,
               error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            SharedMeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK;
}

bool VerifyConcurrentSessions(
    const char* pluginPath,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    constexpr size_t threadCount = 4;
    StartBarrier barrier(threadCount);
    std::array<bool, threadCount> passed{};
    std::array<std::thread, threadCount> threads;
    for (size_t index = 0; index < threadCount; ++index)
    {
        threads[index] = std::thread(
            [&, index]
            {
                std::array<char, 4096> threadErrorText{};
                openusd_error_buffer threadError{
                    threadErrorText.data(), threadErrorText.size(), 0};
                barrier.ArriveAndWait();
                openusd_silk_session* session = nullptr;
                if (openusd_silk_session_create_from_stage(
                        pluginPath, stage, &session, &threadError) != OPENUSD_STATUS_OK)
                {
                    return;
                }
                ParsedPage parsed;
                const openusd_status status = Sync(session, &parsed, &threadError);
                openusd_silk_session_release(session);
                passed[index] =
                    status == OPENUSD_STATUS_OK &&
                    parsed.frame_count == 1 &&
                    parsed.found_shared_upsert;
            });
    }
    for (std::thread& thread : threads)
    {
        thread.join();
    }
    for (bool value : passed)
    {
        if (!value)
        {
            if (error != nullptr && error->data != nullptr && error->capacity > 0)
            {
                constexpr char message[] = "Concurrent hdSilk session creation failed.";
                const size_t copySize = sizeof(message) <= error->capacity
                    ? sizeof(message)
                    : error->capacity;
                std::memcpy(error->data, message, copySize);
                error->data[copySize - 1] = '\0';
                error->required = sizeof(message);
            }
            return false;
        }
    }
    return true;
}

openusd_status GetRendererName(
    openusd_silk_session* session,
    openusd_error_buffer* error)
{
    std::array<char, 256> buffer{};
    size_t required = 0;
    return openusd_silk_session_get_renderer_name(
        session, buffer.data(), buffer.size(), &required, error);
}

bool WaitForInFlight(
    openusd_silk_session* session,
    size_t expected)
{
    const auto deadline =
        std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (std::chrono::steady_clock::now() < deadline)
    {
        if (openusd_hdsilk_test_get_session_in_flight(session) == expected)
        {
            return true;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    return false;
}

bool VerifyDestroyRace(
    const char* pluginPath,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    constexpr size_t queuedCount = 12;
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create_from_stage(
            pluginPath, stage, &session, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }

    openusd_stage_access* access = nullptr;
    if (openusd_stage_access_begin(stage, &access, error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }

    std::array<openusd_status, queuedCount + 1> statuses{};
    std::thread blocker(
        [&]
        {
            std::array<char, 4096> text{};
            openusd_error_buffer threadError{text.data(), text.size(), 0};
            ParsedPage parsed;
            statuses[0] = Sync(session, &parsed, &threadError);
        });
    if (!WaitForInFlight(session, 1))
    {
        openusd_stage_access_end(access, error);
        blocker.join();
        openusd_silk_session_release(session);
        return false;
    }

    std::array<std::thread, queuedCount> queued;
    for (size_t index = 0; index < queuedCount; ++index)
    {
        queued[index] = std::thread(
            [&, index]
            {
                std::array<char, 4096> text{};
                openusd_error_buffer threadError{text.data(), text.size(), 0};
                if ((index % 2) == 0)
                {
                    ParsedPage parsed;
                    statuses[index + 1] = Sync(session, &parsed, &threadError);
                }
                else
                {
                    statuses[index + 1] = GetRendererName(session, &threadError);
                }
            });
    }
    if (!WaitForInFlight(session, queuedCount + 1))
    {
        openusd_stage_access_end(access, error);
        blocker.join();
        for (std::thread& thread : queued)
        {
            thread.join();
        }
        openusd_silk_session_release(session);
        return false;
    }

    std::atomic<bool> destroyDone{false};
    openusd_status destroyStatus = OPENUSD_STATUS_NATIVE_ERROR;
    std::thread destroyer(
        [&]
        {
            std::array<char, 4096> text{};
            openusd_error_buffer threadError{text.data(), text.size(), 0};
            destroyStatus = openusd_silk_session_destroy(session, &threadError);
            destroyDone.store(true, std::memory_order_release);
        });
    std::this_thread::sleep_for(std::chrono::milliseconds(25));
    const bool waited = !destroyDone.load(std::memory_order_acquire);
    const openusd_status endStatus = openusd_stage_access_end(access, error);
    blocker.join();
    for (std::thread& thread : queued)
    {
        thread.join();
    }
    destroyer.join();

    for (openusd_status status : statuses)
    {
        if (status != OPENUSD_STATUS_OK)
        {
            return false;
        }
    }
    if (!waited || endStatus != OPENUSD_STATUS_OK ||
        destroyStatus != OPENUSD_STATUS_OK)
    {
        return false;
    }

    openusd_silk_page* stalePage =
        reinterpret_cast<openusd_silk_page*>(1);
    openusd_silk_page_view staleView{};
    staleView.struct_size = sizeof(openusd_silk_page_view);
    const openusd_render_camera automatic = AutomaticCamera();
    if (openusd_silk_session_sync(
            session,
            64,
            64,
            0.0,
            &automatic,
            &stalePage,
            &staleView,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        stalePage != nullptr)
    {
        return false;
    }
    std::array<char, 8> staleName{};
    staleName.fill('x');
    size_t staleRequired = 99;
    if (openusd_silk_session_get_renderer_name(
            session,
            staleName.data(),
            staleName.size(),
            &staleRequired,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        staleRequired != 0 || staleName[0] != '\0' ||
        openusd_silk_session_destroy(session, error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        return false;
    }
    return true;
}

bool VerifyNeverReusedHandles(
    const char* pluginPath,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    constexpr size_t iterationCount = 32;
    std::unordered_set<uintptr_t> tokens;
    for (size_t index = 0; index < iterationCount; ++index)
    {
        openusd_silk_session* session = nullptr;
        if (openusd_silk_session_create_from_stage(
                pluginPath, stage, &session, error) != OPENUSD_STATUS_OK ||
            !tokens.insert(reinterpret_cast<uintptr_t>(session)).second ||
            openusd_silk_session_destroy(session, error) != OPENUSD_STATUS_OK ||
            GetRendererName(session, error) != OPENUSD_STATUS_INVALID_ARGUMENT)
        {
            openusd_silk_session_release(session);
            return false;
        }
    }
    return tokens.size() == iterationCount;
}

/// Builds a MaterialX standard-surface network whose base-colour image reads its
/// texture coordinates through a place2d node carrying the supplied constant
/// inputs. When "connectOffset" is set, the place2d offset is driven by another
/// node instead, which is exactly the per-pixel case this projection rejects.
HdMaterialNetworkMap
MakePlace2dNetwork(
    const std::map<TfToken, VtValue>& place2dInputs,
    bool connectOffset)
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Placed/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode place2d;
    place2d.path = SdfPath("/World/Placed/Place2d");
    place2d.identifier = TfToken("ND_place2d_vector2");
    place2d.parameters = place2dInputs;

    HdMaterialNode image;
    image.path = SdfPath("/World/Placed/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Placed/Driver");
    driver.identifier = TfToken("ND_constant_vector2");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, place2d, image, driver, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), place2d.path, TfToken("texcoord")});
    network.relationships.push_back(
        {place2d.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    if (connectOffset)
    {
        network.relationships.push_back(
            {driver.path, TfToken("out"), place2d.path, TfToken("offset")});
    }
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour image reads its
/// texture coordinates through two chained place2d nodes. The composed affine is
/// the only correct answer: applying either node alone, or the outer node over
/// the default primvar, produces a different matrix.
HdMaterialNetworkMap
MakeChainedPlace2dNetwork()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Placed/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode inner;
    inner.path = SdfPath("/World/Placed/Inner");
    inner.identifier = TfToken("ND_place2d_vector2");
    inner.parameters[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));

    HdMaterialNode outer;
    outer.path = SdfPath("/World/Placed/Outer");
    outer.identifier = TfToken("ND_place2d_vector2");
    outer.parameters[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.0F));

    HdMaterialNode image;
    image.path = SdfPath("/World/Placed/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, inner, outer, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), inner.path, TfToken("texcoord")});
    network.relationships.push_back(
        {inner.path, TfToken("out"), outer.path, TfToken("texcoord")});
    network.relationships.push_back(
        {outer.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour image reads its
/// coordinates through a place2d that sits behind a per-pixel vector2 multiply.
/// Folding only the place2d would render the outer transform over coordinates
/// the multiply never produced.
HdMaterialNetworkMap
MakePlace2dBehindUnsupportedNodeNetwork()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Placed/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode multiply;
    multiply.path = SdfPath("/World/Placed/Multiply");
    multiply.identifier = TfToken("ND_multiply_vector2");
    multiply.parameters[TfToken("in2")] = VtValue(GfVec2f(3.0F, 5.0F));

    HdMaterialNode place2d;
    place2d.path = SdfPath("/World/Placed/Place2d");
    place2d.identifier = TfToken("ND_place2d_vector2");
    place2d.parameters[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.5F));

    HdMaterialNode image;
    image.path = SdfPath("/World/Placed/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, multiply, place2d, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), multiply.path, TfToken("in1")});
    network.relationships.push_back(
        {multiply.path, TfToken("out"), place2d.path, TfToken("texcoord")});
    network.relationships.push_back(
        {place2d.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a UsdPreviewSurface network whose diffuse texture reads its
/// coordinates through a UsdTransform2d. When "connectReader" is false the
/// transform has no upstream coordinate node at all, which is the case that must
/// be rejected rather than resolved to the default primvar.
HdMaterialNetworkMap
MakeTransform2dNetwork(bool connectReader)
{
    HdMaterialNode reader;
    reader.path = SdfPath("/World/Preview/Reader");
    reader.identifier = TfToken("UsdPrimvarReader_float2");
    reader.parameters[TfToken("varname")] = VtValue(TfToken("uvSet1"));

    HdMaterialNode transform;
    transform.path = SdfPath("/World/Preview/Transform");
    transform.identifier = TfToken("UsdTransform2d");
    transform.parameters[TfToken("rotation")] = VtValue(90.0F);
    transform.parameters[TfToken("scale")] = VtValue(GfVec2f(2.0F, 1.0F));
    transform.parameters[TfToken("translation")] = VtValue(GfVec2f(0.5F, 0.25F));

    HdMaterialNode texture;
    texture.path = SdfPath("/World/Preview/Texture");
    texture.identifier = TfToken("UsdUVTexture");
    texture.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/preview-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Preview/Surface");
    surface.identifier = TfToken("UsdPreviewSurface");

    HdMaterialNetwork network;
    network.nodes = {reader, transform, texture, surface};
    if (connectReader)
    {
        network.relationships.push_back(
            {reader.path, TfToken("result"), transform.path, TfToken("in")});
    }
    network.relationships.push_back(
        {transform.path, TfToken("result"), texture.path, TfToken("st")});
    network.relationships.push_back(
        {texture.path, TfToken("rgb"), surface.path, TfToken("diffuseColor")});
    network.primvars = {TfToken("uvSet1")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    return map;
}

/// Builds a MaterialX standard-surface network with a transformed base-colour
/// image and an untransformed normal map, which is the ordinary authoring shape
/// this renderer's single coordinate stream per material cannot serve.
HdMaterialNetworkMap
MakeDivergentUvChainNetwork()
{
    HdMaterialNode baseCoordinate;
    baseCoordinate.path = SdfPath("/World/Placed/BasePrimvar");
    baseCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    baseCoordinate.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode place2d;
    place2d.path = SdfPath("/World/Placed/Place2d");
    place2d.identifier = TfToken("ND_place2d_vector2");
    place2d.parameters[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));
    place2d.parameters[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.5F));

    HdMaterialNode baseImage;
    baseImage.path = SdfPath("/World/Placed/BaseImage");
    baseImage.identifier = TfToken("ND_image_color3");
    baseImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode normalCoordinate;
    normalCoordinate.path = SdfPath("/World/Placed/NormalPrimvar");
    normalCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    normalCoordinate.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode normalImage;
    normalImage.path = SdfPath("/World/Placed/NormalImage");
    normalImage.identifier = TfToken("ND_image_vector3");
    normalImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-normal.png"));

    HdMaterialNode normalMap;
    normalMap.path = SdfPath("/World/Placed/NormalMap");
    normalMap.identifier = TfToken("ND_normalmap");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {
        baseCoordinate,
        place2d,
        baseImage,
        normalCoordinate,
        normalImage,
        normalMap,
        surface};
    network.relationships.push_back(
        {baseCoordinate.path, TfToken("out"), place2d.path, TfToken("texcoord")});
    network.relationships.push_back(
        {place2d.path, TfToken("out"), baseImage.path, TfToken("texcoord")});
    network.relationships.push_back(
        {baseImage.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.relationships.push_back(
        {normalCoordinate.path,
         TfToken("out"),
         normalImage.path,
         TfToken("texcoord")});
    network.relationships.push_back(
        {normalImage.path, TfToken("out"), normalMap.path, TfToken("in")});
    network.relationships.push_back(
        {normalMap.path, TfToken("out"), surface.path, TfToken("normal")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour and normal-map
/// images read two different primvars, with no UV transform anywhere.
///
/// This is the primvar counterpart of MakeDivergentUvChainNetwork, and the
/// no-transform part is deliberate: both entries carry the identity affine, so
/// only the primvar can distinguish them. Reversing the order proves the
/// material stream is the one the first texture in the fixed input order states
/// rather than whichever primvar happens to sort first.
HdMaterialNetworkMap
MakeDivergentUvPrimvarNetwork(bool baseReadsFirstSet)
{
    HdMaterialNode baseCoordinate;
    baseCoordinate.path = SdfPath("/World/Streams/BasePrimvar");
    baseCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    baseCoordinate.parameters[TfToken("geomprop")] =
        VtValue(std::string(baseReadsFirstSet ? "uvSet0" : "uvSet1"));

    HdMaterialNode baseImage;
    baseImage.path = SdfPath("/World/Streams/BaseImage");
    baseImage.identifier = TfToken("ND_image_color3");
    baseImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode normalCoordinate;
    normalCoordinate.path = SdfPath("/World/Streams/NormalPrimvar");
    normalCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    normalCoordinate.parameters[TfToken("geomprop")] =
        VtValue(std::string(baseReadsFirstSet ? "uvSet1" : "uvSet0"));

    HdMaterialNode normalImage;
    normalImage.path = SdfPath("/World/Streams/NormalImage");
    normalImage.identifier = TfToken("ND_image_vector3");
    normalImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-normal.png"));

    HdMaterialNode normalMap;
    normalMap.path = SdfPath("/World/Streams/NormalMap");
    normalMap.identifier = TfToken("ND_normalmap");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Streams/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {
        baseCoordinate,
        baseImage,
        normalCoordinate,
        normalImage,
        normalMap,
        surface};
    network.relationships.push_back(
        {baseCoordinate.path, TfToken("out"), baseImage.path, TfToken("texcoord")});
    network.relationships.push_back(
        {baseImage.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.relationships.push_back(
        {normalCoordinate.path,
         TfToken("out"),
         normalImage.path,
         TfToken("texcoord")});
    network.relationships.push_back(
        {normalImage.path, TfToken("out"), normalMap.path, TfToken("in")});
    network.relationships.push_back(
        {normalMap.path, TfToken("out"), surface.path, TfToken("normal")});
    network.primvars = {TfToken("uvSet0"), TfToken("uvSet1")};

    HdMaterialNetworkMap streamMap;
    streamMap.map[HdMaterialTerminalTokens->surface] = network;
    streamMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return streamMap;
}

/// Builds the same two-image shape with both images on one primvar, which must
/// keep both entries. Without it the divergence case above would pass just as
/// well against a projection that dropped every normal map.
HdMaterialNetworkMap
MakeSharedUvPrimvarNetwork()
{
    HdMaterialNetworkMap shared = MakeDivergentUvPrimvarNetwork(true);
    HdMaterialNetwork& network = shared.map[HdMaterialTerminalTokens->surface];
    for (HdMaterialNode& node : network.nodes)
    {
        if (node.path == SdfPath("/World/Streams/NormalPrimvar"))
        {
            node.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));
        }
    }
    return shared;
}

/// Builds a MaterialX standard-surface network whose base colour is an image
/// driven through a chain of constant arithmetic. The chain is authored as
/// mix(bg = constant, fg = multiply(image, constant), mix = constant) so both the
/// multiply and the mix folds are exercised together.
HdMaterialNetworkMap
MakeAffineOverImageNetwork(
    const TfToken& surfaceInput,
    const TfToken& imageIdentifier,
    const VtValue& multiplyFactor,
    const VtValue& mixBackground,
    float mixFactor)
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Arith/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode image;
    image.path = SdfPath("/World/Arith/Image");
    image.identifier = imageIdentifier;
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode multiply;
    multiply.path = SdfPath("/World/Arith/Multiply");
    multiply.identifier = TfToken("ND_multiply_color3");
    multiply.parameters[TfToken("in2")] = multiplyFactor;

    HdMaterialNode mix;
    mix.path = SdfPath("/World/Arith/Mix");
    mix.identifier = TfToken("ND_mix_color3");
    mix.parameters[TfToken("bg")] = mixBackground;
    mix.parameters[TfToken("mix")] = VtValue(mixFactor);

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Arith/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, image, multiply, mix, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), multiply.path, TfToken("in1")});
    network.relationships.push_back(
        {multiply.path, TfToken("out"), mix.path, TfToken("fg")});
    network.relationships.push_back(
        {mix.path, TfToken("out"), surface.path, surfaceInput});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base colour multiplies two
/// images together, which the renderer's one-texture-per-input binding cannot
/// serve and which must therefore be reported rather than half-folded.
HdMaterialNetworkMap
MakeTwoImageProductNetwork()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Arith/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode first;
    first.path = SdfPath("/World/Arith/First");
    first.identifier = TfToken("ND_image_color3");
    first.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/first.png"));

    HdMaterialNode second;
    second.path = SdfPath("/World/Arith/Second");
    second.identifier = TfToken("ND_image_color3");
    second.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/second.png"));

    HdMaterialNode multiply;
    multiply.path = SdfPath("/World/Arith/Multiply");
    multiply.identifier = TfToken("ND_multiply_color3");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Arith/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, first, second, multiply, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), first.path, TfToken("texcoord")});
    network.relationships.push_back(
        {primvar.path, TfToken("out"), second.path, TfToken("texcoord")});
    network.relationships.push_back(
        {first.path, TfToken("out"), multiply.path, TfToken("in1")});
    network.relationships.push_back(
        {second.path, TfToken("out"), multiply.path, TfToken("in2")});
    network.relationships.push_back(
        {multiply.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network that drives one or two surface
/// inputs from two images joined by the supplied operator.
///
/// The two operands carry different constant multipliers so their folded affines
/// differ, which is what proves each entry keeps its own branch rather than
/// sharing one. `secondInput` adds a second composited parameter, which is the
/// case the renderer's single composite slot cannot serve.
HdMaterialNetworkMap
MakeTwoImageCompositeNetwork(
    const TfToken& nodeIdentifier,
    const TfToken& firstInput,
    const TfToken& secondInput,
    const VtValue& mixFactor)
{
    const bool mix = nodeIdentifier.GetString().rfind("ND_mix_", 0) == 0;
    const TfToken primaryInput = mix ? TfToken("bg") : TfToken("in1");
    const TfToken compositeInput = mix ? TfToken("fg") : TfToken("in2");

    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Composite/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode first;
    first.path = SdfPath("/World/Composite/First");
    first.identifier = TfToken("ND_image_color3");
    first.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/first.png"));

    HdMaterialNode second;
    second.path = SdfPath("/World/Composite/Second");
    second.identifier = TfToken("ND_image_color3");
    second.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/second.png"));

    // A constant scale on the second branch only, so the two published entries
    // must differ. A fold that shared one affine would report the same scale on
    // both, and a fold that dropped the inner multiply would report neither.
    HdMaterialNode scaleSecond;
    scaleSecond.path = SdfPath("/World/Composite/ScaleSecond");
    scaleSecond.identifier = TfToken("ND_multiply_color3");
    scaleSecond.parameters[TfToken("in2")] = VtValue(GfVec3f(0.5F, 0.5F, 0.5F));

    HdMaterialNode combine;
    combine.path = SdfPath("/World/Composite/Combine");
    combine.identifier = nodeIdentifier;
    if (mix && !mixFactor.IsEmpty())
    {
        combine.parameters[TfToken("mix")] = mixFactor;
    }

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Composite/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");
    // standard_surface defaults its emission weight to zero, so a network that
    // composites into emission_color must state the weight or the projection
    // correctly reports an unlit emission rather than the composite under test.
    surface.parameters[TfToken("emission")] = VtValue(1.0F);

    HdMaterialNetwork network;
    network.nodes = {primvar, first, second, scaleSecond, combine, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), first.path, TfToken("texcoord")});
    network.relationships.push_back(
        {primvar.path, TfToken("out"), second.path, TfToken("texcoord")});
    network.relationships.push_back(
        {second.path, TfToken("out"), scaleSecond.path, TfToken("in1")});
    network.relationships.push_back(
        {first.path, TfToken("out"), combine.path, primaryInput});
    network.relationships.push_back(
        {scaleSecond.path, TfToken("out"), combine.path, compositeInput});
    network.relationships.push_back(
        {combine.path, TfToken("out"), surface.path, firstInput});
    if (!secondInput.IsEmpty())
    {
        network.relationships.push_back(
            {combine.path, TfToken("out"), surface.path, secondInput});
    }
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap compositeMap;
    compositeMap.map[HdMaterialTerminalTokens->surface] = network;
    compositeMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return compositeMap;
}

/// Builds a MaterialX standard-surface network whose base colour applies a
/// non-affine operator to an image, which no scale and bias can represent.
HdMaterialNetworkMap
MakeNonAffineOverImageNetwork()
{
    HdMaterialNode image;
    image.path = SdfPath("/World/Arith/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode clamp;
    clamp.path = SdfPath("/World/Arith/Clamp");
    clamp.identifier = TfToken("ND_clamp_color3");
    clamp.parameters[TfToken("low")] = VtValue(GfVec3f(0.2F, 0.2F, 0.2F));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Arith/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {image, clamp, surface};
    network.relationships.push_back(
        {image.path, TfToken("out"), clamp.path, TfToken("in")});
    network.relationships.push_back(
        {clamp.path, TfToken("out"), surface.path, TfToken("base_color")});

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour image carries
/// the supplied node parameters and reads its coordinates through the supplied
/// coordinate node. Used to pin address-mode and default-value reading, which
/// have MaterialX names and MaterialX defaults rather than UsdUVTexture's.
HdMaterialNetworkMap
MakeMaterialXImageNetwork(
    const std::map<TfToken, VtValue>& imageInputs,
    const TfToken& coordinateIdentifier,
    const std::map<TfToken, VtValue>& coordinateInputs)
{
    HdMaterialNode coordinate;
    coordinate.path = SdfPath("/World/Sampled/Coordinate");
    coordinate.identifier = coordinateIdentifier;
    coordinate.parameters = coordinateInputs;

    HdMaterialNode image;
    image.path = SdfPath("/World/Sampled/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters = imageInputs;
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Sampled/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {coordinate, image, surface};
    network.relationships.push_back(
        {coordinate.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap sampledMap;
    sampledMap.map[HdMaterialTerminalTokens->surface] = network;
    sampledMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return sampledMap;
}

/// Builds a MaterialX standard-surface network whose base colour is a single
/// node of the supplied identifier carrying the supplied inputs, optionally with
/// one of those inputs also driven by a connection.
///
/// Both halves matter. Without a connection this pins that the node folds to the
/// authored constant; with one it pins that the authored value behind the
/// connection is *not* folded, because Hydra leaves the nodedef value in
/// `parameters` even when the author replaced it with a connection.
HdMaterialNetworkMap
MakeConstantFoldNetwork(
    const TfToken& nodeIdentifier,
    const std::map<TfToken, VtValue>& nodeInputs,
    const TfToken& connectedInput,
    bool connectToMissingNode)
{
    HdMaterialNode node;
    node.path = SdfPath("/World/Folded/Node");
    node.identifier = nodeIdentifier;
    node.parameters = nodeInputs;

    HdMaterialNode image;
    image.path = SdfPath("/World/Folded/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Folded/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {node, surface};
    if (!connectedInput.IsEmpty() && !connectToMissingNode)
    {
        network.nodes.push_back(image);
    }
    network.relationships.push_back(
        {node.path, TfToken("out"), surface.path, TfToken("base_color")});
    if (!connectedInput.IsEmpty())
    {
        // A dangling relationship is authored data too: a network can name a node
        // it does not carry, and reading the authored fallback in that case folds
        // a value the author replaced with a connection.
        network.relationships.push_back(
            {connectToMissingNode ? SdfPath("/World/Folded/Absent") : image.path,
             TfToken("out"),
             node.path,
             connectedInput});
    }

    HdMaterialNetworkMap foldedMap;
    foldedMap.map[HdMaterialTerminalTokens->surface] = network;
    foldedMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return foldedMap;
}

/// Builds a MaterialX standard-surface network whose base-colour image has the
/// named constant input driven by a connection while still carrying an authored
/// value for it, which is the shape Hydra produces for any connected input.
HdMaterialNetworkMap
MakeConnectedImageConstantNetwork(const TfToken& connectedInput)
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Sampled/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Sampled/Driver");
    driver.identifier = TfToken("ND_constant_color3");
    driver.parameters[TfToken("value")] = VtValue(GfVec3f(0.5F, 0.5F, 0.5F));

    HdMaterialNode image;
    image.path = SdfPath("/World/Sampled/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));
    image.parameters[connectedInput] = VtValue(GfVec3f(0.125F, 0.25F, 0.5F));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Sampled/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, driver, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {driver.path, TfToken("out"), image.path, connectedInput});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap connectedMap;
    connectedMap.map[HdMaterialTerminalTokens->surface] = network;
    connectedMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return connectedMap;
}

/// Builds a surface network whose base colour is wired to a node the network
/// does not carry, while the surface node still holds an authored value for that
/// same input. Hydra produces this shape whenever a referenced node is filtered
/// out, and the authored value is the one the author replaced.
HdMaterialNetworkMap
MakeDanglingSurfaceInputNetwork(bool previewSurface)
{
    HdMaterialNode surface;
    surface.path = SdfPath("/World/Folded/Surface");
    surface.identifier = previewSurface
        ? TfToken("UsdPreviewSurface")
        : TfToken("ND_standard_surface_surfaceshader");
    const TfToken inputName =
        previewSurface ? TfToken("diffuseColor") : TfToken("base_color");
    surface.parameters[inputName] = VtValue(GfVec3f(0.7F, 0.7F, 0.7F));

    HdMaterialNetwork network;
    network.nodes = {surface};
    network.relationships.push_back(
        {SdfPath("/World/Folded/Absent"), TfToken("out"), surface.path, inputName});

    HdMaterialNetworkMap danglingMap;
    danglingMap.map[HdMaterialTerminalTokens->surface] = network;
    if (!previewSurface)
    {
        danglingMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    }
    return danglingMap;
}

/// Builds a UsdPreviewSurface network whose UsdUVTexture has a connected `scale`
/// alongside an authored one. The four constant wire floats cannot carry a
/// per-pixel scale, and the authored value is not what the graph asks for.
HdMaterialNetworkMap
MakeConnectedPreviewTextureConstantNetwork()
{
    HdMaterialNode reader;
    reader.path = SdfPath("/World/Folded/Reader");
    reader.identifier = TfToken("UsdPrimvarReader_float2");
    reader.parameters[TfToken("varname")] = VtValue(TfToken("st"));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Folded/Driver");
    driver.identifier = TfToken("UsdPrimvarReader_float3");

    HdMaterialNode texture;
    texture.path = SdfPath("/World/Folded/Texture");
    texture.identifier = TfToken("UsdUVTexture");
    texture.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/preview-basecolor.png"));
    texture.parameters[TfToken("scale")] = VtValue(GfVec4f(0.5F, 0.5F, 0.5F, 1.0F));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Folded/Surface");
    surface.identifier = TfToken("UsdPreviewSurface");

    HdMaterialNetwork network;
    network.nodes = {reader, driver, texture, surface};
    network.relationships.push_back(
        {reader.path, TfToken("result"), texture.path, TfToken("st")});
    network.relationships.push_back(
        {driver.path, TfToken("result"), texture.path, TfToken("scale")});
    network.relationships.push_back(
        {texture.path, TfToken("rgb"), surface.path, TfToken("diffuseColor")});
    network.primvars = {TfToken("st")};

    HdMaterialNetworkMap previewMap;
    previewMap.map[HdMaterialTerminalTokens->surface] = network;
    return previewMap;
}

bool UvTransformMatches(
    const float (&actual)[6],
    const std::array<float, 6>& expected)
{
    for (size_t index = 0; index < expected.size(); ++index)
    {
        if (std::fabs(actual[index] - expected[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Extracts the ABI v14 trailing uv_transform of the one MATERIAL_UPSERT command
/// in a built page, so the fold is proven on the wire and not only in memory.
/// Extracts the ABI v15 composite_op of the first two texture entries of the one
/// MATERIAL_UPSERT command in a built page.
///
/// The renderer only ever sees the page, so a composite that folds correctly in
/// memory but never reaches the wire would render as an ordinary single texture.
bool ReadPageCompositeOperators(
    const std::vector<uint8_t>& page,
    std::array<uint32_t, 2>* out)
{
    size_t offset = 0;
    while (offset + 8 <= page.size())
    {
        uint32_t type = 0;
        uint32_t byteSize = 0;
        std::memcpy(&type, page.data() + offset, sizeof(type));
        std::memcpy(&byteSize, page.data() + offset + 4, sizeof(byteSize));
        if (byteSize < 8 || offset + byteSize > page.size())
        {
            return false;
        }
        if (type != OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            offset += byteSize;
            continue;
        }
        uint32_t pathByteCount = 0;
        uint32_t scalarCount = 0;
        uint32_t textureCount = 0;
        std::memcpy(&pathByteCount, page.data() + offset + 16, sizeof(pathByteCount));
        std::memcpy(&scalarCount, page.data() + offset + 24, sizeof(scalarCount));
        std::memcpy(&textureCount, page.data() + offset + 28, sizeof(textureCount));
        if (textureCount < 2)
        {
            return false;
        }
        size_t cursor = offset + 32 + pathByteCount;
        for (uint32_t scalar = 0; scalar < scalarCount; ++scalar)
        {
            uint32_t componentCount = 0;
            std::memcpy(&componentCount, page.data() + cursor + 4, sizeof(componentCount));
            cursor += 8 + (static_cast<size_t>(componentCount) * sizeof(float));
        }
        for (uint32_t texture = 0; texture < 2; ++texture)
        {
            uint32_t assetSize = 0;
            uint32_t uvSize = 0;
            std::memcpy(&assetSize, page.data() + cursor + 16, sizeof(assetSize));
            std::memcpy(&uvSize, page.data() + cursor + 20, sizeof(uvSize));
            std::memcpy(&(*out)[texture], page.data() + cursor + 80, sizeof(uint32_t));
            cursor += 88 + assetSize + uvSize;
        }
        return true;
    }
    return false;
}

bool ReadPageUvTransform(
    const std::vector<uint8_t>& page,
    std::array<float, 6>* out)
{
    size_t offset = 0;
    bool found = false;
    while (offset + 8 <= page.size())
    {
        uint32_t type = 0;
        uint32_t byteSize = 0;
        std::memcpy(&type, page.data() + offset, sizeof(type));
        std::memcpy(&byteSize, page.data() + offset + 4, sizeof(byteSize));
        if (byteSize < 8 || offset + byteSize > page.size())
        {
            return false;
        }
        if (type == OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            if (byteSize < 8 + (6 * sizeof(float)))
            {
                return false;
            }
            std::memcpy(
                out->data(),
                page.data() + offset + byteSize - (6 * sizeof(float)),
                6 * sizeof(float));
            found = true;
        }
        offset += byteSize;
    }
    return found && offset == page.size();
}

/// Proves the bounded MaterialX and UsdPreviewSurface UV chain projection:
/// constant place2d SRT/TRS folds, chained-transform composition, the
/// UsdTransform2d fold with its own upstream primvar, the primvar behind the
/// transform, the wire round trip, and every case the projection rejects with a
/// diagnostic instead of approximating.
bool TextureAffineMatches(
    const HdSilkMaterialTexture& texture,
    const std::array<float, 4>& scale,
    const std::array<float, 4>& bias)
{
    for (size_t index = 0; index < 4; ++index)
    {
        if (std::fabs(texture.scale[index] - scale[index]) > 1e-5F ||
            std::fabs(texture.bias[index] - bias[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Proves the bounded fold of constant arithmetic over exactly one image into
/// the texture entry's own scale and bias, and every graph shape it refuses.
bool VerifyMaterialXImageArithmeticProjection()
{
    const SdfPath materialPath("/World/Arith");

    // mix(bg = 0.2, fg = image * 0.5, mix = 0.75) is affine in the image:
    // value = image * 0.375 + 0.05. Neither node alone produces that pair, so a
    // fold that dropped either the multiply or the mix fails here.
    HdSilkMaterialRecord folded = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("base_color"),
            TfToken("ND_image_color3"),
            VtValue(GfVec3f(0.5F, 0.5F, 0.5F)),
            VtValue(GfVec3f(0.2F, 0.2F, 0.2F)),
            0.75F));
    if (folded.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED ||
        folded.textures.size() != 1 ||
        folded.textures[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        folded.textures[0].uvPrimvar != "uvSet0" ||
        folded.textures[0].outputChannel != OPENUSD_SILK_TEXTURE_CHANNEL_RGB ||
        !TextureAffineMatches(
            folded.textures[0],
            {0.375F, 0.375F, 0.375F, 1.0F},
            {0.05F, 0.05F, 0.05F, 0.0F}))
    {
        std::cerr << "MaterialX image arithmetic fold was wrong: textures="
                  << folded.textures.size();
        if (!folded.textures.empty())
        {
            std::cerr << " scale=(" << folded.textures[0].scale[0] << ", "
                      << folded.textures[0].scale[3] << ") bias=("
                      << folded.textures[0].bias[0] << ", "
                      << folded.textures[0].bias[3] << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // The same shape on a scalar input folds only the red channel, because the
    // consumer replicates the connected output channel after scale and bias.
    HdSilkMaterialRecord scalarFold = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("specular_roughness"),
            TfToken("ND_image_float"),
            VtValue(0.5F),
            VtValue(0.2F),
            0.75F));
    if (scalarFold.textures.size() != 1 ||
        scalarFold.textures[0].parameter != OPENUSD_SILK_MATERIAL_ROUGHNESS ||
        scalarFold.textures[0].outputChannel != OPENUSD_SILK_TEXTURE_CHANNEL_R ||
        !TextureAffineMatches(
            scalarFold.textures[0],
            {0.375F, 1.0F, 1.0F, 1.0F},
            {0.05F, 0.0F, 0.0F, 0.0F}))
    {
        std::cerr << "MaterialX scalar image arithmetic fold was wrong: textures="
                  << scalarFold.textures.size() << "\n";
        return false;
    }

    // An affine that leaves the unit range would be clamped by the eight-bit
    // upload path, which changes the lit result rather than rounding it.
    HdSilkMaterialRecord brightened = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("base_color"),
            TfToken("ND_image_color3"),
            VtValue(GfVec3f(3.0F, 3.0F, 3.0F)),
            VtValue(GfVec3f(0.0F, 0.0F, 0.0F)),
            1.0F));
    if (!brightened.textures.empty())
    {
        std::cerr << "An out-of-range image arithmetic fold was not rejected: textures="
                  << brightened.textures.size() << "\n";
        return false;
    }

    // Two images multiplied into one input are no longer folded away: since ABI
    // v15 they publish a primary entry and a composite operand that the shader
    // combines per pixel. The affine fold must decline them, which is what routes
    // them to the composite path, and it must not half-fold either operand.
    HdSilkMaterialRecord twoImages =
        HdSilkMaterial::Resolve(materialPath, MakeTwoImageProductNetwork());
    if (twoImages.textures.size() != 2)
    {
        std::cerr << "A two-image product did not publish both operands: textures="
                  << twoImages.textures.size() << "\n";
        return false;
    }
    if (twoImages.textures[0].compositeOp != OPENUSD_SILK_COMPOSITE_NONE ||
        twoImages.textures[1].compositeOp != OPENUSD_SILK_COMPOSITE_MULTIPLY ||
        !TextureAffineMatches(
            twoImages.textures[0], {1.0F, 1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}) ||
        !TextureAffineMatches(
            twoImages.textures[1], {1.0F, 1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A two-image product published the wrong operator or affine: ops=("
                  << twoImages.textures[0].compositeOp << ", "
                  << twoImages.textures[1].compositeOp << ")\n";
        return false;
    }

    // A non-affine operator over an image has no scale and bias representation.
    HdSilkMaterialRecord nonAffine =
        HdSilkMaterial::Resolve(materialPath, MakeNonAffineOverImageNetwork());
    if (!nonAffine.textures.empty() || !nonAffine.scalars.empty())
    {
        std::cerr << "A non-affine operator over an image was not rejected: textures="
                  << nonAffine.textures.size() << " scalars="
                  << nonAffine.scalars.size() << "\n";
        return false;
    }

    // The fold must not touch the normal input: scaling a tangent-space normal is
    // not the colour operation this projection models.
    HdSilkMaterialRecord normalArithmetic = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("normal"),
            TfToken("ND_image_vector3"),
            VtValue(GfVec3f(0.5F, 0.5F, 0.5F)),
            VtValue(GfVec3f(0.2F, 0.2F, 0.2F)),
            0.75F));
    if (!normalArithmetic.textures.empty())
    {
        std::cerr << "Arithmetic over a normal image was folded rather than rejected: textures="
                  << normalArithmetic.textures.size() << "\n";
        return false;
    }

    // Two images joined by one constant operator publish two entries: a primary
    // and a composite operand the shader combines per pixel. Each keeps its own
    // branch's folded affine, which is what the differing scales prove.
    const SdfPath compositePath("/World/Composite");
    struct CompositeCase
    {
        const char* node;
        uint32_t op;
    };
    const std::array<CompositeCase, 3> compositeCases{
        CompositeCase{"ND_multiply_color3", OPENUSD_SILK_COMPOSITE_MULTIPLY},
        CompositeCase{"ND_add_color3", OPENUSD_SILK_COMPOSITE_ADD},
        CompositeCase{"ND_subtract_color3", OPENUSD_SILK_COMPOSITE_SUBTRACT}};
    for (const CompositeCase& compositeCase : compositeCases)
    {
        HdSilkMaterialRecord composed = HdSilkMaterial::Resolve(
            compositePath,
            MakeTwoImageCompositeNetwork(
                TfToken(compositeCase.node),
                TfToken("base_color"),
                TfToken(),
                VtValue()));
        if (composed.textures.size() != 2 ||
            composed.textures[0].compositeOp != OPENUSD_SILK_COMPOSITE_NONE ||
            composed.textures[1].compositeOp != compositeCase.op ||
            composed.textures[0].asset != "textures/first.png" ||
            composed.textures[1].asset != "textures/second.png" ||
            composed.textures[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
            composed.textures[1].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
            !TextureAffineMatches(
                composed.textures[0], {1.0F, 1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}) ||
            !TextureAffineMatches(
                composed.textures[1], {0.5F, 0.5F, 0.5F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}))
        {
            std::cerr << "The two-image " << compositeCase.node
                      << " composite was wrong: textures=" << composed.textures.size();
            for (const HdSilkMaterialTexture& texture : composed.textures)
            {
                std::cerr << " (" << texture.asset << " op=" << texture.compositeOp
                          << " scale=" << texture.scale[0] << ")";
            }
            std::cerr << "\n";
            return false;
        }
    }

    // Operand order is observable for subtract, so the primary must be `in1`.
    // mix carries its constant factor, which nothing else on the wire encodes.
    HdSilkMaterialRecord blended = HdSilkMaterial::Resolve(
        compositePath,
        MakeTwoImageCompositeNetwork(
            TfToken("ND_mix_color3"),
            TfToken("base_color"),
            TfToken(),
            VtValue(0.25F)));
    if (blended.textures.size() != 2 ||
        blended.textures[1].compositeOp != OPENUSD_SILK_COMPOSITE_MIX ||
        std::fabs(blended.textures[1].compositeFactor - 0.25F) > 1e-6F ||
        blended.textures[0].asset != "textures/first.png")
    {
        std::cerr << "The two-image mix composite was wrong: textures="
                  << blended.textures.size() << " factor="
                  << (blended.textures.size() > 1
                          ? blended.textures[1].compositeFactor
                          : -1.0F)
                  << "\n";
        return false;
    }

    // The renderer binds one composite image per material, so a second
    // composited parameter has no slot. Both of its entries go, not just one:
    // publishing the primary alone would render one of two authored images.
    HdSilkMaterialRecord twoComposites = HdSilkMaterial::Resolve(
        compositePath,
        MakeTwoImageCompositeNetwork(
            TfToken("ND_multiply_color3"),
            TfToken("base_color"),
            TfToken("emission_color"),
            VtValue()));
    if (twoComposites.textures.size() != 2)
    {
        std::cerr << "A second composited parameter was not dropped whole: textures="
                  << twoComposites.textures.size();
        for (const HdSilkMaterialTexture& texture : twoComposites.textures)
        {
            std::cerr << " (parameter=" << texture.parameter
                      << " op=" << texture.compositeOp << ")";
        }
        std::cerr << "\n";
        return false;
    }
    for (const HdSilkMaterialTexture& texture : twoComposites.textures)
    {
        if (texture.parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR)
        {
            std::cerr << "The surviving composite was not the first input: parameter="
                      << texture.parameter << "\n";
            return false;
        }
    }

    // The wire must round-trip both entries, because the renderer only ever sees
    // the page. BuildPage also rejects a lone composite and a second one.
    HdSilkSceneState compositeState;
    compositeState.ReplaceMaterial(HdSilkMaterial::Resolve(
        compositePath,
        MakeTwoImageCompositeNetwork(
            TfToken("ND_multiply_color3"),
            TfToken("base_color"),
            TfToken(),
            VtValue())));
    uint64_t compositeRevision = 0;
    uint32_t compositeCommands = 0;
    const std::vector<uint8_t> compositePage =
        compositeState.BuildPage(&compositeRevision, &compositeCommands);
    std::array<uint32_t, 2> wireOps{};
    if (!ReadPageCompositeOperators(compositePage, &wireOps) ||
        wireOps[0] != OPENUSD_SILK_COMPOSITE_NONE ||
        wireOps[1] != OPENUSD_SILK_COMPOSITE_MULTIPLY)
    {
        std::cerr << "The composite operators did not reach the page: ("
                  << wireOps[0] << ", " << wireOps[1] << ")\n";
        return false;
    }

    // A pure constant chain must still resolve to a scalar, not to a texture.
    std::map<TfToken, VtValue> unusedInputs;
    HdSilkMaterialRecord constantOnly =
        HdSilkMaterial::Resolve(SdfPath("/World/Placed"), MakePlace2dNetwork(unusedInputs, false));
    if (constantOnly.textures.size() != 1)
    {
        std::cerr << "The direct-image path regressed while folding arithmetic.\n";
        return false;
    }
    return true;
}

bool ScalarMatches(
    const HdSilkMaterialScalar& scalar,
    const std::array<float, 3>& expected)
{
    for (size_t index = 0; index < expected.size(); ++index)
    {
        if (std::fabs(scalar.value[index] - expected[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Finds the one scalar a parameter carries, or nullptr when the projection
/// left the parameter at its renderer default.
const HdSilkMaterialScalar*
FindMaterialScalar(const HdSilkMaterialRecord& record, uint32_t parameter)
{
    for (const HdSilkMaterialScalar& scalar : record.scalars)
    {
        if (scalar.parameter == parameter)
        {
            return &scalar;
        }
    }
    return nullptr;
}

const HdSilkMaterialTexture*
FindMaterialTexture(const HdSilkMaterialRecord& record, uint32_t parameter)
{
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (texture.parameter == parameter)
        {
            return &texture;
        }
    }
    return nullptr;
}

bool ScalarIs(
    const HdSilkMaterialRecord& record,
    uint32_t parameter,
    float expected,
    const char* what)
{
    const HdSilkMaterialScalar* scalar = FindMaterialScalar(record, parameter);
    if (scalar == nullptr)
    {
        std::cerr << what << " was not projected onto parameter " << parameter
                  << ".\n";
        return false;
    }
    if (std::fabs(scalar->value[0] - expected) > 1e-5F)
    {
        std::cerr << what << " projected " << scalar->value[0] << " rather than "
                  << expected << ".\n";
        return false;
    }
    return true;
}

bool ParameterIsAbsent(
    const HdSilkMaterialRecord& record,
    uint32_t parameter,
    const char* what)
{
    if (FindMaterialScalar(record, parameter) != nullptr ||
        FindMaterialTexture(record, parameter) != nullptr)
    {
        std::cerr << what << " reached parameter " << parameter
                  << " even though the projection cannot carry it.\n";
        return false;
    }
    return true;
}

/// Builds a UsdPreviewSurface material, optionally publishing a `displacement`
/// terminal network and optionally driving `inputs:displacement` from a node.
///
/// The surface and displacement terminals are separate networks in USD, and both
/// are built here from the same node set so a probe can vary exactly one thing:
/// whether the displacement output is connected at all, and what drives the
/// shader's displacement input.
///
/// `driver` selects what feeds `inputs:displacement` in the displacement
/// network: nothing, a `UsdUVTexture`, a primvar reader (a node that is a legal
/// float source in USD but is not a height field this renderer can sample per
/// vertex), or a dangling connection to a node the network omits.
enum class DisplacementDriver
{
    None,
    Texture,
    PrimvarReader,
    Dangling,
    // A UsdUVTexture with no authored `file`, which resolves no asset at all.
    EmptyFileTexture,
    // A UsdUVTexture whose `scale` the author replaced with a connection, which
    // is a graph this delegate cannot evaluate rather than a missing file.
    UnsupportedChainTexture
};

HdMaterialNetworkMap
MakeDisplacementNetwork(
    bool publishDisplacementTerminal,
    DisplacementDriver driver,
    float authoredDisplacement = 0.5F,
    const TfToken& terminalIdentifier = TfToken("UsdPreviewSurface"),
    const TfToken& outputName = TfToken("r"))
{
    HdMaterialNode surface;
    surface.path = SdfPath("/World/Displaced/Surface");
    surface.identifier = terminalIdentifier;
    surface.parameters[TfToken("diffuseColor")] =
        VtValue(GfVec3f(0.4F, 0.4F, 0.4F));
    surface.parameters[TfToken("displacement")] = VtValue(authoredDisplacement);

    HdMaterialNode reader;
    reader.path = SdfPath("/World/Displaced/Reader");
    reader.identifier = TfToken("UsdPrimvarReader_float2");
    reader.parameters[TfToken("varname")] = VtValue(TfToken("st"));

    HdMaterialNode texture;
    texture.path = SdfPath("/World/Displaced/Height");
    texture.identifier = TfToken("UsdUVTexture");
    texture.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/height.png"));
    texture.parameters[TfToken("wrapS")] = VtValue(TfToken("clamp"));
    texture.parameters[TfToken("wrapT")] = VtValue(TfToken("clamp"));
    texture.parameters[TfToken("sourceColorSpace")] = VtValue(TfToken("raw"));

    HdMaterialNode floatReader;
    floatReader.path = SdfPath("/World/Displaced/FloatReader");
    floatReader.identifier = TfToken("UsdPrimvarReader_float");
    floatReader.parameters[TfToken("varname")] = VtValue(TfToken("height"));

    // Same node, no `file` at all: UsdUVTexture states what the reader produces
    // then, and the authored fallback is (0.5 * 3) + (-0.25) = 1.25 on r, while
    // the unauthored alpha keeps the schema default of one, giving (1 * 3) +
    // (-0.25) = 2.75 on a.
    HdMaterialNode emptyFile;
    emptyFile.path = SdfPath("/World/Displaced/EmptyHeight");
    emptyFile.identifier = TfToken("UsdUVTexture");
    emptyFile.parameters[TfToken("fallback")] = VtValue(GfVec4f(0.5F, 0.5F, 0.5F, 1.0F));
    emptyFile.parameters[TfToken("scale")] = VtValue(GfVec4f(3.0F, 3.0F, 3.0F, 3.0F));
    emptyFile.parameters[TfToken("bias")] =
        VtValue(GfVec4f(-0.25F, -0.25F, -0.25F, -0.25F));

    // A file *and* a connected `scale`: the file is readable, the graph is not.
    HdMaterialNode unsupportedChain;
    unsupportedChain.path = SdfPath("/World/Displaced/ChainHeight");
    unsupportedChain.identifier = TfToken("UsdUVTexture");
    unsupportedChain.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/height.png"));
    unsupportedChain.parameters[TfToken("fallback")] =
        VtValue(GfVec4f(0.5F, 0.5F, 0.5F, 1.0F));

    HdMaterialNode scaleDriver;
    scaleDriver.path = SdfPath("/World/Displaced/ScaleDriver");
    scaleDriver.identifier = TfToken("UsdPrimvarReader_float4");
    scaleDriver.parameters[TfToken("varname")] = VtValue(TfToken("scale"));

    HdMaterialNetwork surfaceNetwork;
    surfaceNetwork.nodes = {surface};
    surfaceNetwork.primvars = {TfToken("st")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = surfaceNetwork;
    if (!publishDisplacementTerminal)
    {
        return map;
    }

    HdMaterialNetwork displacementNetwork;
    displacementNetwork.primvars = {TfToken("st")};
    switch (driver)
    {
        case DisplacementDriver::None:
            displacementNetwork.nodes = {surface};
            break;
        case DisplacementDriver::Texture:
            displacementNetwork.nodes = {reader, texture, surface};
            displacementNetwork.relationships.push_back(
                {reader.path, TfToken("result"), texture.path, TfToken("st")});
            displacementNetwork.relationships.push_back(
                {texture.path, TfToken("r"), surface.path,
                    TfToken("displacement")});
            break;
        case DisplacementDriver::PrimvarReader:
            displacementNetwork.nodes = {floatReader, surface};
            displacementNetwork.relationships.push_back(
                {floatReader.path, TfToken("result"), surface.path,
                    TfToken("displacement")});
            break;
        case DisplacementDriver::Dangling:
            displacementNetwork.nodes = {surface};
            displacementNetwork.relationships.push_back(
                {SdfPath("/World/Displaced/Absent"), TfToken("result"),
                    surface.path, TfToken("displacement")});
            break;
        case DisplacementDriver::EmptyFileTexture:
            displacementNetwork.nodes = {reader, emptyFile, surface};
            displacementNetwork.relationships.push_back(
                {reader.path, TfToken("result"), emptyFile.path, TfToken("st")});
            displacementNetwork.relationships.push_back(
                {emptyFile.path, outputName, surface.path, TfToken("displacement")});
            break;
        case DisplacementDriver::UnsupportedChainTexture:
            displacementNetwork.nodes = {reader, scaleDriver, unsupportedChain, surface};
            displacementNetwork.relationships.push_back(
                {reader.path, TfToken("result"), unsupportedChain.path, TfToken("st")});
            displacementNetwork.relationships.push_back(
                {scaleDriver.path, TfToken("result"), unsupportedChain.path,
                    TfToken("scale")});
            displacementNetwork.relationships.push_back(
                {unsupportedChain.path, TfToken("r"), surface.path,
                    TfToken("displacement")});
            break;
    }
    map.map[HdMaterialTerminalTokens->displacement] = displacementNetwork;
    return map;
}

/// Proves hdSilk resolves displacement from the authored `displacement` material
/// terminal and from nothing else.
///
/// The producer, not the consumer, is what these cases exercise: every managed
/// displacement gate builds a wire page directly, so without this the claim that
/// hdSilk *publishes* displacement only for a material that authored a
/// displacement terminal would be untested. Each case varies exactly one thing
/// against the same surface shader, whose `inputs:displacement` is authored
/// non-zero throughout -- so a delegate that read the surface input, or the value
/// Hydra leaves behind a connection, publishes displacement in cases that must
/// publish none.
bool VerifyDisplacementTerminal()
{
    const SdfPath materialPath("/World/Displaced");

    // A material that authors no displacement terminal publishes no
    // displacement, even though its surface shader carries a non-zero
    // inputs:displacement. This is the non-vacuity partner of every case below.
    HdSilkMaterialRecord noTerminal = HdSilkMaterial::Resolve(
        materialPath, MakeDisplacementNetwork(false, DisplacementDriver::None));
    if (noTerminal.surfaceKind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        !ParameterIsAbsent(
            noTerminal,
            OPENUSD_SILK_MATERIAL_DISPLACEMENT,
            "A material with no displacement terminal"))
    {
        std::cerr << "Displacement was published without a displacement terminal.\n";
        return false;
    }

    // The same shader, with the terminal connected, publishes the authored
    // constant.
    HdSilkMaterialRecord constant = HdSilkMaterial::Resolve(
        materialPath, MakeDisplacementNetwork(true, DisplacementDriver::None));
    const HdSilkMaterialScalar* amount =
        FindMaterialScalar(constant, OPENUSD_SILK_MATERIAL_DISPLACEMENT);
    if (amount == nullptr || amount->componentCount != 1 ||
        std::fabs(amount->value[0] - 0.5F) > 1e-6F)
    {
        std::cerr << "An authored displacement terminal did not publish its "
                  << "constant.\n";
        return false;
    }

    // A connected UsdUVTexture publishes the height field, and the authored
    // constant behind that connection must not also appear as a scalar.
    HdSilkMaterialRecord textured = HdSilkMaterial::Resolve(
        materialPath, MakeDisplacementNetwork(true, DisplacementDriver::Texture));
    const HdSilkMaterialTexture* height =
        FindMaterialTexture(textured, OPENUSD_SILK_MATERIAL_DISPLACEMENT);
    if (height == nullptr ||
        height->asset.find("height.png") == std::string::npos ||
        height->outputChannel != OPENUSD_SILK_TEXTURE_CHANNEL_R ||
        height->uvPrimvar != "st" ||
        FindMaterialScalar(textured, OPENUSD_SILK_MATERIAL_DISPLACEMENT) !=
            nullptr)
    {
        std::cerr << "A connected displacement texture was not published as the "
                  << "only displacement source.\n";
        return false;
    }

    // A float source this renderer cannot sample per vertex is reported and
    // refused rather than collapsed to the constant the author replaced.
    HdSilkMaterialRecord reader = HdSilkMaterial::Resolve(
        materialPath,
        MakeDisplacementNetwork(true, DisplacementDriver::PrimvarReader));
    if (!ParameterIsAbsent(
            reader,
            OPENUSD_SILK_MATERIAL_DISPLACEMENT,
            "A displacement driven by a primvar reader"))
    {
        return false;
    }

    // A connection to a node the network omits is still a connection.
    HdSilkMaterialRecord dangling = HdSilkMaterial::Resolve(
        materialPath, MakeDisplacementNetwork(true, DisplacementDriver::Dangling));
    if (!ParameterIsAbsent(
            dangling,
            OPENUSD_SILK_MATERIAL_DISPLACEMENT,
            "A dangling displacement connection"))
    {
        return false;
    }

    // A terminal that is not a UsdPreviewSurface is reported by name rather than
    // read as if it were one.
    HdSilkMaterialRecord foreign = HdSilkMaterial::Resolve(
        materialPath,
        MakeDisplacementNetwork(
            true,
            DisplacementDriver::None,
            0.5F,
            TfToken("ND_displacement_float")));
    if (!ParameterIsAbsent(
            foreign,
            OPENUSD_SILK_MATERIAL_DISPLACEMENT,
            "A non-UsdPreviewSurface displacement terminal"))
    {
        return false;
    }

    // A UsdUVTexture with no resolvable file is not a refusal: UsdUVTexture
    // states what the reader produces then, so the authored fallback is
    // published through the node's own scale and bias.
    HdSilkMaterialRecord emptyFile = HdSilkMaterial::Resolve(
        materialPath,
        MakeDisplacementNetwork(true, DisplacementDriver::EmptyFileTexture));
    const HdSilkMaterialScalar* emptyAmount =
        FindMaterialScalar(emptyFile, OPENUSD_SILK_MATERIAL_DISPLACEMENT);
    if (emptyAmount == nullptr || std::fabs(emptyAmount->value[0] - 1.25F) > 1e-5F)
    {
        std::cerr << "An empty-file displacement did not publish its authored "
                  << "fallback.\n";
        return false;
    }

    // The same node read through `outputs:a`, whose fallback component nobody
    // authored: the UsdUVTexture schema default is one, not zero, so the amount
    // is (1 * 3) - 0.25 rather than -0.25.
    HdSilkMaterialRecord emptyAlpha = HdSilkMaterial::Resolve(
        materialPath,
        MakeDisplacementNetwork(
            true,
            DisplacementDriver::EmptyFileTexture,
            0.5F,
            TfToken("UsdPreviewSurface"),
            TfToken("a")));
    const HdSilkMaterialScalar* alphaAmount =
        FindMaterialScalar(emptyAlpha, OPENUSD_SILK_MATERIAL_DISPLACEMENT);
    if (alphaAmount == nullptr || std::fabs(alphaAmount->value[0] - 2.75F) > 1e-5F)
    {
        std::cerr << "An unauthored fallback alpha was not the schema default one; "
                  << "published "
                  << (alphaAmount == nullptr ? 0.0F : alphaAmount->value[0]) << ".\n";
        return false;
    }

    // A readable file behind a graph this delegate cannot evaluate is a refusal,
    // not a fallback: UsdUVTexture says nothing about what a reader produces for
    // a connection it cannot resolve, and publishing the fallback there would
    // displace the surface by a value nobody authored for that condition.
    HdSilkMaterialRecord unsupportedChain = HdSilkMaterial::Resolve(
        materialPath,
        MakeDisplacementNetwork(true, DisplacementDriver::UnsupportedChainTexture));
    if (!ParameterIsAbsent(
            unsupportedChain,
            OPENUSD_SILK_MATERIAL_DISPLACEMENT,
            "A displacement behind an unsupported graph"))
    {
        return false;
    }

    // An authored zero still publishes, because zero and unauthored are
    // different statements and the consumer distinguishes them.
    HdSilkMaterialRecord zero = HdSilkMaterial::Resolve(
        materialPath, MakeDisplacementNetwork(true, DisplacementDriver::None, 0.0F));
    const HdSilkMaterialScalar* zeroAmount =
        FindMaterialScalar(zero, OPENUSD_SILK_MATERIAL_DISPLACEMENT);
    if (zeroAmount == nullptr || std::fabs(zeroAmount->value[0]) > 1e-6F)
    {
        std::cerr << "An authored zero displacement was not published.\n";
        return false;
    }
    return true;
}

/// Builds a MaterialX surface-shader network with the supplied nodedef and
/// constant inputs, and optionally one image driving one input.
///
/// The nodedef identifier is a parameter so the same shape can be resolved as
/// standard_surface and as OpenPBR, which is what proves the two projections
/// read their own input names rather than sharing one hard-coded table.
HdMaterialNetworkMap
MakeSurfaceModelNetwork(
    const TfToken& surfaceIdentifier,
    const std::map<TfToken, VtValue>& inputs,
    const TfToken& imageInput = TfToken(),
    const TfToken& connectedWeight = TfToken())
{
    HdMaterialNode surface;
    surface.path = SdfPath("/World/Model/Surface");
    surface.identifier = surfaceIdentifier;
    for (const auto& input : inputs)
    {
        surface.parameters[input.first] = input.second;
    }

    HdMaterialNetwork network;
    network.nodes = {surface};

    if (!imageInput.IsEmpty())
    {
        HdMaterialNode primvar;
        primvar.path = SdfPath("/World/Model/Primvar");
        primvar.identifier = TfToken("ND_geompropvalue_vector2");
        primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

        HdMaterialNode image;
        image.path = SdfPath("/World/Model/Image");
        image.identifier = TfToken("ND_image_color3");
        image.parameters[TfToken("file")] =
            VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

        network.nodes.insert(network.nodes.begin(), image);
        network.nodes.insert(network.nodes.begin(), primvar);
        network.relationships.push_back(
            {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
        network.relationships.push_back(
            {image.path, TfToken("out"), surface.path, imageInput});
        network.primvars = {TfToken("uvSet0")};
    }

    if (!connectedWeight.IsEmpty())
    {
        HdMaterialNode constant;
        constant.path = SdfPath("/World/Model/Weight");
        constant.identifier = TfToken("ND_constant_float");
        constant.parameters[TfToken("value")] = VtValue(0.5F);
        network.nodes.insert(network.nodes.begin(), constant);
        network.relationships.push_back(
            {constant.path, TfToken("out"), surface.path, connectedWeight});
    }

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Proves the projection of the two MaterialX surface models this delegate
/// carries: OpenPBR 1.1 (`ND_open_pbr_surface_surfaceshader`, the identifier the
/// pinned MaterialX 1.39.4 standard libraries declare) and the broadened
/// standard_surface table.
///
/// Only inputs that are the same quantity as a wire parameter are projected. The
/// weights the nodedefs state as multiplies inside their own implementation
/// graphs are applied, because leaving them out would render a material whose
/// emission is switched off as fully emissive. Everything else is reported and
/// left at the renderer default rather than folded into an unrelated parameter.
bool VerifyGeneratedUnlitSurvivesGenerationFailure()
{
    // ND_surface_unlit is unlit because its author said so, not because a
    // fragment was produced for it. A generation failure used to publish
    // OPENUSD_SILK_SURFACE_UNSUPPORTED, which handed the prim to the shaded
    // fallback and lit a surface whose whole definition is that it is not lit --
    // and it did so silently, because the page carries no record of a generation
    // that failed.
    //
    // Driven through HdSilkMaterial::Resolve rather than through a hand-built
    // record, because the rule under test is what the delegate publishes. The
    // failure is forced by a test hook: in a build whose MaterialX generation
    // works this path is otherwise unreachable, and a probe that only saw success
    // would be gating the branch it cannot enter.
    const SdfPath materialPath("/World/UnlitFailure");
    HdMaterialNode surface;
    surface.path = SdfPath("/World/UnlitFailure/Surface");
    surface.identifier = TfToken("ND_surface_unlit");
    surface.parameters[TfToken("emission_color")] =
        VtValue(GfVec3f(0.25F, 0.5F, 0.75F));

    HdMaterialNetwork network;
    network.nodes = {surface};
    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.terminals = {surface.path};
    map.config[TfToken("mtlxVersion")] = VtValue(std::string("1.39"));

    const uint64_t before =
        HdSilkMaterial::GetGeneratedSurfaceFailureCountForTesting();
    HdSilkMaterial::SetGeneratedSurfaceFailureForTesting(true);
    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(materialPath, map);
    HdSilkMaterial::SetGeneratedSurfaceFailureForTesting(false);
    const uint64_t after =
        HdSilkMaterial::GetGeneratedSurfaceFailureCountForTesting();

    if (record.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_GENERATED)
    {
        std::cerr << "A failed ND_surface_unlit generation did not keep the "
                     "generated surface kind: kind="
                  << record.surfaceKind << "\n";
        return false;
    }
    if (!record.generatedFragmentSpirv.empty() ||
        !record.generatedFragmentMslSource.empty())
    {
        std::cerr << "A failed ND_surface_unlit generation published a "
                     "non-empty fragment payload.\n";
        return false;
    }
    if (after != before + 1)
    {
        std::cerr << "A failed ND_surface_unlit generation was not reported "
                     "separately: before="
                  << before << " after=" << after << "\n";
        return false;
    }

    // And the surface kind does not depend on whether generation succeeded, so a
    // consumer cannot tell "unlit" from "unlit and pending" by the kind alone --
    // which is the point: both draw the unlit placeholder. This build may have no
    // MaterialX shader generators at all, in which case the unforced resolve
    // fails too; what must hold either way is the kind and the payload rule.
    const uint64_t beforeUnforced =
        HdSilkMaterial::GetGeneratedSurfaceFailureCountForTesting();
    const HdSilkMaterialRecord unforced =
        HdSilkMaterial::Resolve(materialPath, map);
    const uint64_t afterUnforced =
        HdSilkMaterial::GetGeneratedSurfaceFailureCountForTesting();
    if (unforced.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_GENERATED)
    {
        std::cerr << "An unforced ND_surface_unlit resolution did not publish "
                     "the generated surface kind: kind="
                  << unforced.surfaceKind << "\n";
        return false;
    }
    const bool generatorsAvailable = afterUnforced == beforeUnforced;
    if (generatorsAvailable && unforced.generatedFragmentSpirv.empty() &&
        unforced.generatedFragmentMslSource.empty())
    {
        std::cerr << "An ND_surface_unlit resolution reported no generation "
                     "failure but published no fragment payload.\n";
        return false;
    }
    if (!generatorsAvailable && (!unforced.generatedFragmentSpirv.empty() ||
                                    !unforced.generatedFragmentMslSource.empty()))
    {
        std::cerr << "A failed ND_surface_unlit generation published a "
                     "fragment payload.\n";
        return false;
    }
    return true;
}

bool VerifySurfaceModelProjection()
{
    const SdfPath materialPath("/World/Model");
    const TfToken openPbr("ND_open_pbr_surface_surfaceshader");
    const TfToken standardSurface("ND_standard_surface_surfaceshader");

    // OpenPBR constants that are the same quantity as a wire parameter.
    HdSilkMaterialRecord openPbrRecord = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("base_color"), VtValue(GfVec3f(0.2F, 0.4F, 0.6F))},
                {TfToken("base_metalness"), VtValue(0.75F)},
                {TfToken("specular_roughness"), VtValue(0.25F)},
                {TfToken("specular_ior"), VtValue(1.45F)},
                {TfToken("coat_weight"), VtValue(0.5F)},
                {TfToken("coat_roughness"), VtValue(0.125F)},
                {TfToken("geometry_opacity"), VtValue(0.375F)}}));
    if (openPbrRecord.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED)
    {
        std::cerr << "The OpenPBR nodedef was not recognised as a projected "
                     "MaterialX surface: kind="
                  << openPbrRecord.surfaceKind << "\n";
        return false;
    }
    const HdSilkMaterialScalar* openPbrBase =
        FindMaterialScalar(openPbrRecord, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR);
    if (openPbrBase == nullptr ||
        !ScalarMatches(*openPbrBase, {0.2F, 0.4F, 0.6F}))
    {
        std::cerr << "OpenPBR base_color did not project onto the diffuse "
                     "colour.\n";
        return false;
    }
    if (!ScalarIs(
            openPbrRecord, OPENUSD_SILK_MATERIAL_METALLIC, 0.75F,
            "OpenPBR base_metalness") ||
        !ScalarIs(
            openPbrRecord, OPENUSD_SILK_MATERIAL_ROUGHNESS, 0.25F,
            "OpenPBR specular_roughness") ||
        !ScalarIs(
            openPbrRecord, OPENUSD_SILK_MATERIAL_IOR, 1.45F,
            "OpenPBR specular_ior") ||
        !ScalarIs(
            openPbrRecord, OPENUSD_SILK_MATERIAL_CLEARCOAT, 0.5F,
            "OpenPBR coat_weight") ||
        !ScalarIs(
            openPbrRecord, OPENUSD_SILK_MATERIAL_CLEARCOAT_ROUGHNESS, 0.125F,
            "OpenPBR coat_roughness") ||
        !ScalarIs(
            openPbrRecord, OPENUSD_SILK_MATERIAL_OPACITY, 0.375F,
            "OpenPBR geometry_opacity"))
    {
        return false;
    }

    // emission_luminance defaults to zero, and the nodedef multiplies the
    // emission colour by it. Publishing the colour on its own would light up a
    // material the author left unlit.
    HdSilkMaterialRecord unlit = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("emission_color"), VtValue(GfVec3f(1.0F, 0.5F, 0.25F))}}));
    const HdSilkMaterialScalar* unlitEmission =
        FindMaterialScalar(unlit, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR);
    if (unlitEmission == nullptr ||
        !ScalarMatches(*unlitEmission, {0.0F, 0.0F, 0.0F}))
    {
        std::cerr << "An OpenPBR emission colour with no luminance did not "
                     "project as unlit.\n";
        return false;
    }

    HdSilkMaterialRecord emissive = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("emission_color"), VtValue(GfVec3f(0.5F, 0.25F, 0.125F))},
                {TfToken("emission_luminance"), VtValue(2.0F)}}));
    const HdSilkMaterialScalar* emission =
        FindMaterialScalar(emissive, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR);
    if (emission == nullptr || !ScalarMatches(*emission, {1.0F, 0.5F, 0.25F}))
    {
        std::cerr << "The OpenPBR emission luminance did not scale the emission "
                     "colour.\n";
        return false;
    }

    // base_weight scales the base reflection, and the same multiply folds into
    // a direct image's scale and bias rather than needing a shader path.
    HdSilkMaterialRecord weightedImage = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("base_weight"), VtValue(0.5F)}},
            TfToken("base_color")));
    const HdSilkMaterialTexture* weighted =
        FindMaterialTexture(weightedImage, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR);
    if (weighted == nullptr || std::fabs(weighted->scale[0] - 0.5F) > 1e-5F ||
        std::fabs(weighted->bias[0]) > 1e-5F)
    {
        std::cerr << "The OpenPBR base weight did not fold into the image "
                     "scale.\n";
        return false;
    }

    // A connected weight is a per-pixel multiply this projection cannot carry,
    // so the input it scales is left at the renderer default rather than being
    // published at an intensity nobody authored.
    HdSilkMaterialRecord connectedWeight = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("base_color"), VtValue(GfVec3f(0.4F, 0.4F, 0.4F))}},
            TfToken(),
            TfToken("base_weight")));
    if (!ParameterIsAbsent(
            connectedWeight,
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR,
            "A base colour behind a connected weight"))
    {
        return false;
    }

    // Lobes OpenPBR carries and this projection does not must not land in an
    // unrelated parameter: transmission is not opacity and subsurface is not
    // diffuse, however similar the ranges look.
    HdSilkMaterialRecord unsupportedLobes = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("transmission_weight"), VtValue(1.0F)},
                {TfToken("subsurface_weight"), VtValue(1.0F)},
                {TfToken("fuzz_weight"), VtValue(1.0F)},
                {TfToken("thin_film_weight"), VtValue(1.0F)},
                {TfToken("specular_color"), VtValue(GfVec3f(0.9F, 0.1F, 0.1F))},
                {TfToken("coat_color"), VtValue(GfVec3f(0.1F, 0.9F, 0.1F))}}));
    if (!unsupportedLobes.scalars.empty() || !unsupportedLobes.textures.empty())
    {
        std::cerr << "An unsupported OpenPBR lobe was projected onto a wire "
                     "parameter: scalars="
                  << unsupportedLobes.scalars.size()
                  << " textures=" << unsupportedLobes.textures.size() << "\n";
        return false;
    }
    if (unsupportedLobes.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED)
    {
        std::cerr << "A material whose unsupported lobes were reported stopped "
                     "being a projected MaterialX surface.\n";
        return false;
    }

    // The broadened standard_surface table reads the names that nodedef states,
    // which are not the OpenPBR names.
    HdSilkMaterialRecord standard = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            standardSurface,
            {{TfToken("specular_IOR"), VtValue(1.6F)},
                {TfToken("coat"), VtValue(0.25F)},
                {TfToken("coat_roughness"), VtValue(0.2F)},
                {TfToken("opacity"), VtValue(GfVec3f(0.4F, 0.4F, 0.4F))},
                {TfToken("base"), VtValue(0.5F)},
                {TfToken("base_color"), VtValue(GfVec3f(0.8F, 0.6F, 0.4F))}}));
    if (!ScalarIs(
            standard, OPENUSD_SILK_MATERIAL_IOR, 1.6F,
            "standard_surface specular_IOR") ||
        !ScalarIs(
            standard, OPENUSD_SILK_MATERIAL_CLEARCOAT, 0.25F,
            "standard_surface coat") ||
        !ScalarIs(
            standard, OPENUSD_SILK_MATERIAL_CLEARCOAT_ROUGHNESS, 0.2F,
            "standard_surface coat_roughness") ||
        !ScalarIs(
            standard, OPENUSD_SILK_MATERIAL_OPACITY, 0.4F,
            "standard_surface opacity"))
    {
        return false;
    }
    const HdSilkMaterialScalar* weightedBase =
        FindMaterialScalar(standard, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR);
    if (weightedBase == nullptr ||
        !ScalarMatches(*weightedBase, {0.4F, 0.3F, 0.2F}))
    {
        std::cerr << "The standard_surface base weight did not scale the base "
                     "colour.\n";
        return false;
    }

    // standard_surface types opacity as a colour while the wire binds one
    // channel, so a per-channel opacity has no single value to publish.
    HdSilkMaterialRecord perChannel = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            standardSurface,
            {{TfToken("opacity"), VtValue(GfVec3f(0.4F, 0.5F, 0.4F))}}));
    if (!ParameterIsAbsent(
            perChannel,
            OPENUSD_SILK_MATERIAL_OPACITY,
            "A per-channel standard_surface opacity"))
    {
        return false;
    }

    // A connected colour-typed opacity has no single channel either, and the
    // image behind it must not be bound as if it did.
    HdSilkMaterialRecord connectedOpacity = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            standardSurface, {}, TfToken("opacity")));
    if (!ParameterIsAbsent(
            connectedOpacity,
            OPENUSD_SILK_MATERIAL_OPACITY,
            "A connected standard_surface opacity"))
    {
        return false;
    }

    // OpenPBR types its opacity as a float, so the same connection is a single
    // channel there and is bound rather than refused. That difference is the
    // nodedef's, not this projection's.
    HdSilkMaterialRecord openPbrOpacity = HdSilkMaterial::Resolve(
        materialPath,
        MakeSurfaceModelNetwork(
            openPbr,
            {{TfToken("geometry_opacity"), VtValue(0.9F)}}));
    if (!ScalarIs(
            openPbrOpacity, OPENUSD_SILK_MATERIAL_OPACITY, 0.9F,
            "OpenPBR geometry_opacity"))
    {
        return false;
    }

    // The single-coordinate-stream invariant is unchanged by the broader table:
    // an OpenPBR image still publishes exactly one texture with the material's
    // one primvar, and no second stream appears.
    if (weightedImage.textures.size() != 1 ||
        weightedImage.textures[0].uvPrimvar != "uvSet0")
    {
        std::cerr << "The OpenPBR image projection did not keep one texture on "
                     "the material's single coordinate stream: textures="
                  << weightedImage.textures.size() << "\n";
        return false;
    }
    return true;
}

/// Proves that a value hdSilk folds to a constant is the value the MaterialX
/// nodedef states, and that an input the author replaced with a connection is
/// never read from the authored fallback Hydra leaves behind it.
bool VerifyMaterialXConstantFoldProjection()
{
    const SdfPath materialPath("/World/Folded");

    // ND_constant_* states its value on `value`. Before this was read the node
    // matched no operator branch and the whole input was reported unsupported.
    std::map<TfToken, VtValue> constantInputs;
    constantInputs[TfToken("value")] = VtValue(GfVec3f(0.25F, 0.5F, 0.75F));
    HdSilkMaterialRecord constantNode = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_constant_color3"), constantInputs, TfToken(), false));
    if (constantNode.scalars.size() != 1 ||
        constantNode.scalars[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        !ScalarMatches(constantNode.scalars[0], {0.25F, 0.5F, 0.75F}))
    {
        std::cerr << "ND_constant_color3 did not fold its authored value: scalars="
                  << constantNode.scalars.size() << "\n";
        return false;
    }

    // A constant node whose value is connected is not constant. The authored
    // value is still present in `parameters`, so folding it would publish a
    // colour the author replaced with a connection.
    HdSilkMaterialRecord connectedConstant = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_constant_color3"),
            constantInputs,
            TfToken("value"),
            false));
    if (!connectedConstant.scalars.empty())
    {
        std::cerr << "A connected ND_constant_color3 value was folded anyway: scalars="
                  << connectedConstant.scalars.size() << "\n";
        return false;
    }

    // The MaterialX mix default is 0, which selects bg. A 0.5 default folded an
    // unauthored mix to the midpoint of two operands the graph never blended.
    std::map<TfToken, VtValue> mixInputs;
    mixInputs[TfToken("bg")] = VtValue(GfVec3f(0.25F, 0.25F, 0.25F));
    mixInputs[TfToken("fg")] = VtValue(GfVec3f(0.75F, 0.75F, 0.75F));
    HdSilkMaterialRecord unauthoredMix = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_mix_color3"), mixInputs, TfToken(), false));
    if (unauthoredMix.scalars.size() != 1 ||
        !ScalarMatches(unauthoredMix.scalars[0], {0.25F, 0.25F, 0.25F}))
    {
        std::cerr << "An unauthored ND_mix_color3 factor did not default to 0: value="
                  << (unauthoredMix.scalars.empty()
                          ? -1.0F
                          : unauthoredMix.scalars[0].value[0])
                  << "\n";
        return false;
    }

    // A connected mix factor is not the authored fallback either.
    std::map<TfToken, VtValue> authoredMix = mixInputs;
    authoredMix[TfToken("mix")] = VtValue(1.0F);
    HdSilkMaterialRecord connectedMix = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_mix_color3"), authoredMix, TfToken("mix"), false));
    if (!connectedMix.scalars.empty())
    {
        std::cerr << "A connected mix factor folded its authored fallback: scalars="
                  << connectedMix.scalars.size() << "\n";
        return false;
    }

    // A modelled operator must win over the pass-through `in` shortcut. clamp
    // declares `in`, so taking the shortcut first returned the *unclamped* value
    // and made the operator's meaning depend on whether its input happened to be
    // a connection: clamp(1.5, 0.2, default high 1.0) folded to 1.5.
    std::map<TfToken, VtValue> clampInputs;
    clampInputs[TfToken("in")] = VtValue(GfVec3f(1.5F, 1.5F, 1.5F));
    clampInputs[TfToken("low")] = VtValue(GfVec3f(0.2F, 0.2F, 0.2F));
    HdSilkMaterialRecord unconnectedClamp = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_clamp_color3"), clampInputs, TfToken(), false));
    if (unconnectedClamp.scalars.size() != 1 ||
        !ScalarMatches(unconnectedClamp.scalars[0], {1.0F, 1.0F, 1.0F}))
    {
        std::cerr << "An unconnected clamp did not clamp: scalars="
                  << unconnectedClamp.scalars.size() << " value="
                  << (unconnectedClamp.scalars.empty()
                          ? -1.0F
                          : unconnectedClamp.scalars[0].value[0])
                  << "\n";
        return false;
    }

    // The shortcut itself must still work for a node that only passes `in`
    // through and that this projection does not otherwise model.
    std::map<TfToken, VtValue> dotInputs;
    dotInputs[TfToken("in")] = VtValue(GfVec3f(0.9F, 0.8F, 0.7F));
    HdSilkMaterialRecord passThrough = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_dot_color3"), dotInputs, TfToken(), false));
    if (passThrough.scalars.size() != 1 ||
        !ScalarMatches(passThrough.scalars[0], {0.9F, 0.8F, 0.7F}))
    {
        std::cerr << "The pass-through `in` shortcut stopped folding: scalars="
                  << passThrough.scalars.size() << "\n";
        return false;
    }

    // The pass-through `in` shortcut must not fire for a connected `in`. A clamp
    // whose `in` is an image used to publish its authored `in` value as a
    // constant colour, dropping the image with no diagnostic at all.
    HdSilkMaterialRecord connectedClamp = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_clamp_color3"), clampInputs, TfToken("in"), false));
    if (!connectedClamp.scalars.empty() || !connectedClamp.textures.empty())
    {
        std::cerr << "A clamp over an image folded its authored `in`: scalars="
                  << connectedClamp.scalars.size() << " textures="
                  << connectedClamp.textures.size() << "\n";
        return false;
    }

    // A relationship naming a node the network does not carry is still a
    // connection, so the authored fallback behind it must not be folded.
    HdSilkMaterialRecord danglingConnection = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_clamp_color3"), clampInputs, TfToken("in"), true));
    if (!danglingConnection.scalars.empty())
    {
        std::cerr << "A dangling connection folded the authored fallback: scalars="
                  << danglingConnection.scalars.size() << "\n";
        return false;
    }

    // The same rule applies to the surface terminal itself: a base colour wired
    // to a node the network does not carry must not fall back to the value
    // authored on the surface node.
    HdSilkMaterialRecord danglingTerminal = HdSilkMaterial::Resolve(
        materialPath, MakeDanglingSurfaceInputNetwork(false));
    if (!danglingTerminal.scalars.empty())
    {
        std::cerr << "A dangling MaterialX terminal connection folded the surface "
                     "value: scalars="
                  << danglingTerminal.scalars.size() << "\n";
        return false;
    }
    HdSilkMaterialRecord danglingPreviewTerminal = HdSilkMaterial::Resolve(
        materialPath, MakeDanglingSurfaceInputNetwork(true));
    if (!danglingPreviewTerminal.scalars.empty())
    {
        std::cerr << "A dangling UsdPreviewSurface terminal connection folded the "
                     "surface value: scalars="
                  << danglingPreviewTerminal.scalars.size() << "\n";
        return false;
    }

    // The UsdPreviewSurface texture path used to build its entry inline and read
    // scale, bias and fallback without checking for a connection.
    HdSilkMaterialRecord connectedScale = HdSilkMaterial::Resolve(
        materialPath, MakeConnectedPreviewTextureConstantNetwork());
    if (!connectedScale.textures.empty())
    {
        std::cerr << "A connected UsdUVTexture `scale` was folded anyway: textures="
                  << connectedScale.textures.size() << "\n";
        return false;
    }
    return true;
}

/// Proves that a MaterialX image is sampled the way MaterialX states it: its own
/// address-mode inputs with their own periodic default, and its own `default`
/// value as the entry fallback.
bool VerifyMaterialXImageSamplingProjection()
{
    const SdfPath materialPath("/World/Sampled");
    std::map<TfToken, VtValue> primvarInputs;
    primvarInputs[TfToken("geomprop")] = VtValue(std::string("uvSet0"));
    const TfToken geomPropValue("ND_geompropvalue_vector2");

    // MaterialX defaults both address modes to periodic. Reading UsdUVTexture's
    // wrapS/wrapT here found nothing and published black, which the renderer
    // resolves to clamp-to-edge, so a tiled MaterialX texture stopped tiling.
    std::map<TfToken, VtValue> unauthored;
    HdSilkMaterialRecord tiled = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(unauthored, geomPropValue, primvarInputs));
    if (tiled.textures.size() != 1 ||
        tiled.textures[0].wrapS != OPENUSD_SILK_WRAP_REPEAT ||
        tiled.textures[0].wrapT != OPENUSD_SILK_WRAP_REPEAT)
    {
        std::cerr << "An unauthored MaterialX address mode was not periodic: textures="
                  << tiled.textures.size();
        if (!tiled.textures.empty())
        {
            std::cerr << " wrap=(" << tiled.textures[0].wrapS << ", "
                      << tiled.textures[0].wrapT << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // Every enumerated mode maps to its own wire value, and the two axes are
    // independent: a single shared read would report the same mode twice.
    struct AddressCase
    {
        const char* mode;
        uint32_t wrap;
    };
    const std::array<AddressCase, 4> addressCases{
        AddressCase{"constant", OPENUSD_SILK_WRAP_BLACK},
        AddressCase{"clamp", OPENUSD_SILK_WRAP_CLAMP},
        AddressCase{"periodic", OPENUSD_SILK_WRAP_REPEAT},
        AddressCase{"mirror", OPENUSD_SILK_WRAP_MIRROR}};
    for (const AddressCase& addressCase : addressCases)
    {
        std::map<TfToken, VtValue> authored;
        authored[TfToken("uaddressmode")] = VtValue(std::string(addressCase.mode));
        // The V axis is authored as a TfToken rather than a std::string because
        // usdMtlx carries MaterialX strings as either, depending on the layer.
        authored[TfToken("vaddressmode")] = VtValue(TfToken("clamp"));
        HdSilkMaterialRecord addressed = HdSilkMaterial::Resolve(
            materialPath,
            MakeMaterialXImageNetwork(authored, geomPropValue, primvarInputs));
        if (addressed.textures.size() != 1 ||
            addressed.textures[0].wrapS != addressCase.wrap ||
            addressed.textures[0].wrapT != OPENUSD_SILK_WRAP_CLAMP)
        {
            std::cerr << "MaterialX address mode '" << addressCase.mode
                      << "' did not map correctly: textures="
                      << addressed.textures.size();
            if (!addressed.textures.empty())
            {
                std::cerr << " wrap=(" << addressed.textures[0].wrapS << ", "
                          << addressed.textures[0].wrapT << ")";
            }
            std::cerr << "\n";
            return false;
        }
    }

    // An address mode outside the enumeration is reported, not guessed at.
    std::map<TfToken, VtValue> unknownMode;
    unknownMode[TfToken("uaddressmode")] = VtValue(std::string("wraparound"));
    HdSilkMaterialRecord unknown = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(unknownMode, geomPropValue, primvarInputs));
    if (!unknown.textures.empty())
    {
        std::cerr << "An unmodelled MaterialX address mode was not rejected: textures="
                  << unknown.textures.size() << "\n";
        return false;
    }

    // MaterialX names the unreadable-file value `default`; UsdUVTexture names it
    // `fallback`. Reading only the UsdUVTexture name left an authored MaterialX
    // default at the struct's opaque black instead of the authored colour.
    std::map<TfToken, VtValue> defaulted;
    defaulted[TfToken("default")] = VtValue(GfVec3f(0.125F, 0.25F, 0.5F));
    HdSilkMaterialRecord withDefault = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(defaulted, geomPropValue, primvarInputs));
    if (withDefault.textures.size() != 1 ||
        std::fabs(withDefault.textures[0].fallback[0] - 0.125F) > 1e-5F ||
        std::fabs(withDefault.textures[0].fallback[1] - 0.25F) > 1e-5F ||
        std::fabs(withDefault.textures[0].fallback[2] - 0.5F) > 1e-5F ||
        std::fabs(withDefault.textures[0].fallback[3] - 1.0F) > 1e-5F)
    {
        std::cerr << "A MaterialX image `default` was not read as the fallback: textures="
                  << withDefault.textures.size();
        if (!withDefault.textures.empty())
        {
            std::cerr << " fallback=(" << withDefault.textures[0].fallback[0] << ", "
                      << withDefault.textures[0].fallback[1] << ", "
                      << withDefault.textures[0].fallback[2] << ", "
                      << withDefault.textures[0].fallback[3] << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // A texcoord node reading UV set zero is the documented default primvar.
    std::map<TfToken, VtValue> firstSet;
    firstSet[TfToken("index")] = VtValue(0);
    HdSilkMaterialRecord defaultSet = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(
            unauthored, TfToken("ND_texcoord_vector2"), firstSet));
    if (defaultSet.textures.size() != 1 || defaultSet.textures[0].uvPrimvar != "st")
    {
        std::cerr << "A zero texcoord index did not resolve to the default primvar.\n";
        return false;
    }

    // A non-zero index names a second UV set. hdSilk carries one coordinate
    // stream per material, so resolving it to `st` would sample the first set
    // while the graph asked for another.
    std::map<TfToken, VtValue> secondSet;
    secondSet[TfToken("index")] = VtValue(1);
    HdSilkMaterialRecord otherSet = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(
            unauthored, TfToken("ND_texcoord_vector2"), secondSet));
    if (!otherSet.textures.empty())
    {
        std::cerr << "A non-zero texcoord index was silently resolved to st: textures="
                  << otherSet.textures.size() << " primvar='"
                  << (otherSet.textures.empty() ? std::string()
                                                : otherSet.textures[0].uvPrimvar)
                  << "'\n";
        return false;
    }

    // A connected `default` is a per-pixel value the four constant wire floats
    // cannot carry, and the authored value Hydra leaves behind the connection is
    // not what the graph asks to be rendered.
    HdSilkMaterialRecord connectedDefault = HdSilkMaterial::Resolve(
        materialPath, MakeConnectedImageConstantNetwork(TfToken("default")));
    if (!connectedDefault.textures.empty())
    {
        std::cerr << "A connected MaterialX image `default` was folded anyway: textures="
                  << connectedDefault.textures.size() << "\n";
        return false;
    }
    return true;
}

bool VerifyMaterialXUvChainProjection()
{
    const SdfPath materialPath("/World/Placed");

    // SRT with scale and offset only. place2d divides by scale, so scale 2
    // halves the coordinate, and the offset is subtracted after rotation.
    std::map<TfToken, VtValue> scaleOffset;
    scaleOffset[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));
    scaleOffset[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.5F));
    HdSilkMaterialRecord scaled =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(scaleOffset, false));
    if (scaled.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED ||
        scaled.textures.size() != 1 ||
        scaled.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            scaled.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.25F, -0.5F}))
    {
        std::cerr << "MaterialX place2d SRT scale/offset fold was wrong: textures="
                  << scaled.textures.size() << " transform=("
                  << scaled.uvTransform[0] << ", " << scaled.uvTransform[1] << ", "
                  << scaled.uvTransform[2] << ", " << scaled.uvTransform[3] << ", "
                  << scaled.uvTransform[4] << ", " << scaled.uvTransform[5] << ")\n";
        return false;
    }

    // A quarter turn about the tile centre. Nothing but an exact rotate2d fold
    // produces this matrix, so an approximated rotation fails here.
    std::map<TfToken, VtValue> rotated;
    rotated[TfToken("rotate")] = VtValue(90.0F);
    rotated[TfToken("pivot")] = VtValue(GfVec2f(0.5F, 0.5F));
    HdSilkMaterialRecord turned =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(rotated, false));
    if (!UvTransformMatches(
            turned.uvTransform, {0.0F, 1.0F, -1.0F, 0.0F, 0.0F, 1.0F}))
    {
        std::cerr << "MaterialX place2d rotation fold was wrong: ("
                  << turned.uvTransform[0] << ", " << turned.uvTransform[1] << ", "
                  << turned.uvTransform[2] << ", " << turned.uvTransform[3] << ", "
                  << turned.uvTransform[4] << ", " << turned.uvTransform[5] << ")\n";
        return false;
    }

    // TRS removes the offset before dividing by scale, so the same authored
    // values must not produce the SRT translation.
    std::map<TfToken, VtValue> trs;
    trs[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));
    trs[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.0F));
    trs[TfToken("operationorder")] = VtValue(1);
    HdSilkMaterialRecord ordered =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(trs, false));
    if (!UvTransformMatches(
            ordered.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.125F, 0.0F}))
    {
        std::cerr << "MaterialX place2d TRS fold was wrong: ("
                  << ordered.uvTransform[0] << ", " << ordered.uvTransform[1] << ", "
                  << ordered.uvTransform[2] << ", " << ordered.uvTransform[3] << ", "
                  << ordered.uvTransform[4] << ", " << ordered.uvTransform[5] << ")\n";
        return false;
    }

    // A connected place2d input is per-pixel, which this projection does not
    // model. The input must be left at its default rather than sampled with
    // coordinates the graph never asked for.
    HdSilkMaterialRecord connected =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(scaleOffset, true));
    if (!connected.textures.empty() ||
        !UvTransformMatches(
            connected.uvTransform, {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A connected place2d input was not rejected: textures="
                  << connected.textures.size() << "\n";
        return false;
    }

    // A second image asking for a different transform is outside the one
    // transform per material this renderer can sample through. This is the
    // ordinary authoring shape -- a transformed base colour and a normal map
    // reading the same primvar untransformed -- so the limitation is stated
    // against a real graph rather than a contrived one.
    HdSilkMaterialRecord reconciled =
        HdSilkMaterial::Resolve(materialPath, MakeDivergentUvChainNetwork());
    if (reconciled.textures.size() != 1 ||
        reconciled.textures[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        reconciled.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            reconciled.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.25F, -0.5F}))
    {
        std::cerr << "Divergent MaterialX UV chains were not reconciled: textures="
                  << reconciled.textures.size();
        for (const HdSilkMaterialTexture& texture : reconciled.textures)
        {
            std::cerr << " (parameter=" << texture.parameter << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // The primvar half of the same limitation. Both images carry the identity
    // affine here, so the transform reconciliation cannot fire and only the
    // primvar can separate them. Before the primvar was reconciled both entries
    // were published and the consumer, which derives one stream from the first
    // entry that names a primvar, sampled the normal map through the base
    // colour's `uvSet0` with nothing reported.
    const SdfPath streamPath("/World/Streams");
    HdSilkMaterialRecord divergentPrimvar =
        HdSilkMaterial::Resolve(streamPath, MakeDivergentUvPrimvarNetwork(true));
    if (divergentPrimvar.textures.size() != 1 ||
        divergentPrimvar.textures[0].parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        divergentPrimvar.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            divergentPrimvar.uvTransform, {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "Divergent MaterialX UV primvars were not reconciled: textures="
                  << divergentPrimvar.textures.size();
        for (const HdSilkMaterialTexture& texture : divergentPrimvar.textures)
        {
            std::cerr << " (parameter=" << texture.parameter << " primvar='"
                      << texture.uvPrimvar << "')";
        }
        std::cerr << "\n";
        return false;
    }

    // Reversed, the surviving stream must follow the first texture in the fixed
    // input order rather than whichever primvar name sorts first: base colour
    // now reads uvSet1 and it is the normal map that is dropped.
    HdSilkMaterialRecord reversedPrimvar =
        HdSilkMaterial::Resolve(streamPath, MakeDivergentUvPrimvarNetwork(false));
    if (reversedPrimvar.textures.size() != 1 ||
        reversedPrimvar.textures[0].parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        reversedPrimvar.textures[0].uvPrimvar != "uvSet1")
    {
        std::cerr << "The reconciled UV primvar did not follow the first texture: textures="
                  << reversedPrimvar.textures.size();
        for (const HdSilkMaterialTexture& texture : reversedPrimvar.textures)
        {
            std::cerr << " (parameter=" << texture.parameter << " primvar='"
                      << texture.uvPrimvar << "')";
        }
        std::cerr << "\n";
        return false;
    }

    // Non-vacuity: the same two-image shape on one primvar must keep both
    // entries, so the two cases above cannot be satisfied by a projection that
    // simply drops every normal map.
    HdSilkMaterialRecord sharedPrimvar =
        HdSilkMaterial::Resolve(streamPath, MakeSharedUvPrimvarNetwork());
    if (sharedPrimvar.textures.size() != 2)
    {
        std::cerr << "Two images on one primvar did not both survive: textures="
                  << sharedPrimvar.textures.size() << "\n";
        return false;
    }
    for (const HdSilkMaterialTexture& texture : sharedPrimvar.textures)
    {
        if (texture.uvPrimvar != "uvSet0")
        {
            std::cerr << "A shared-primvar texture reported '" << texture.uvPrimvar
                      << "'\n";
            return false;
        }
    }

    HdSilkMaterialRecord chainedBehindUnsupported = HdSilkMaterial::Resolve(
        materialPath, MakePlace2dBehindUnsupportedNodeNetwork());
    if (!chainedBehindUnsupported.textures.empty() ||
        !UvTransformMatches(
            chainedBehindUnsupported.uvTransform,
            {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A place2d behind an unsupported node was not rejected: textures="
                  << chainedBehindUnsupported.textures.size() << " transform=("
                  << chainedBehindUnsupported.uvTransform[0] << ", "
                  << chainedBehindUnsupported.uvTransform[4] << ", "
                  << chainedBehindUnsupported.uvTransform[5] << ")\n";
        return false;
    }

    // Two chained place2d nodes compose. The inner halves the coordinate and the
    // outer shifts it, so neither node's own matrix nor the reverse composition
    // produces this result.
    HdSilkMaterialRecord chained =
        HdSilkMaterial::Resolve(materialPath, MakeChainedPlace2dNetwork());
    if (chained.textures.size() != 1 ||
        chained.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            chained.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.25F, 0.0F}))
    {
        std::cerr << "Chained place2d composition was wrong: textures="
                  << chained.textures.size() << " transform=("
                  << chained.uvTransform[0] << ", " << chained.uvTransform[1] << ", "
                  << chained.uvTransform[2] << ", " << chained.uvTransform[3] << ", "
                  << chained.uvTransform[4] << ", " << chained.uvTransform[5] << ")\n";
        return false;
    }

    // UsdTransform2d scales, then rotates counter-clockwise, then translates.
    // That is the opposite rotation sense to MaterialX rotate2d, so a shared
    // matrix builder would transpose the off-diagonal terms here. The primvar
    // behind the transform must survive: falling back to the default "st" would
    // sample a primvar the graph never named.
    HdSilkMaterialRecord transformed =
        HdSilkMaterial::Resolve(SdfPath("/World/Preview"), MakeTransform2dNetwork(true));
    if (transformed.surfaceKind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        transformed.textures.size() != 1 ||
        transformed.textures[0].uvPrimvar != "uvSet1" ||
        !UvTransformMatches(
            transformed.uvTransform, {0.0F, -1.0F, 2.0F, 0.0F, 0.5F, 0.25F}))
    {
        std::cerr << "UsdTransform2d fold was wrong: textures="
                  << transformed.textures.size() << " primvar='"
                  << (transformed.textures.empty()
                          ? std::string()
                          : transformed.textures[0].uvPrimvar)
                  << "' transform=(" << transformed.uvTransform[0] << ", "
                  << transformed.uvTransform[1] << ", " << transformed.uvTransform[2]
                  << ", " << transformed.uvTransform[3] << ", "
                  << transformed.uvTransform[4] << ", " << transformed.uvTransform[5]
                  << ")\n";
        return false;
    }

    // A UsdTransform2d with no upstream reader transforms its own constant
    // input, so there is no primvar to sample. Silently falling back to an
    // untransformed "st" would render a texture nothing authored.
    HdSilkMaterialRecord unrooted =
        HdSilkMaterial::Resolve(SdfPath("/World/Preview"), MakeTransform2dNetwork(false));
    if (!unrooted.textures.empty() ||
        !UvTransformMatches(
            unrooted.uvTransform, {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A rootless UsdTransform2d chain was not rejected: textures="
                  << unrooted.textures.size() << "\n";
        return false;
    }

    // The fold has to survive serialization, because the renderer only ever
    // sees the page bytes.
    HdSilkSceneState state;
    state.ReplaceMaterial(scaled);
    uint64_t revision = 0;
    uint32_t commandCount = 0;
    const std::vector<uint8_t> page = state.BuildPage(&revision, &commandCount);
    std::array<float, 6> wire{};
    if (!ReadPageUvTransform(page, &wire) ||
        std::fabs(wire[0] - 0.5F) > 1e-5F ||
        std::fabs(wire[3] - 0.5F) > 1e-5F ||
        std::fabs(wire[4] + 0.25F) > 1e-5F ||
        std::fabs(wire[5] + 0.5F) > 1e-5F)
    {
        std::cerr << "The folded MaterialX UV transform did not reach the wire.\n";
        return false;
    }
    return true;
}

bool VerifyMaterialXBridge()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Material/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("st"));

    HdMaterialNode image;
    image.path = SdfPath("/World/Material/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Material/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");
    surface.parameters[TfToken("specular_roughness")] = VtValue(0.42F);
    surface.parameters[TfToken("metalness")] = VtValue(0.0F);

    HdMaterialNetwork network;
    network.nodes = {primvar, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("st")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));

    const HdSilkMaterialXDocument document =
        HdSilkCreateMaterialXDocumentFromNetwork(SdfPath("/World/Material"), map);
    if (!document.success)
    {
        std::cerr << "MaterialX bridge failed: " << document.error
                  << " validation='" << document.validation << "'\n";
        return false;
    }

    bool vulkanGenerated = true;
    bool mslGenerated = true;
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
    const HdSilkMaterialXVulkanShader vulkan =
        HdSilkGenerateMaterialXVulkanFragment(SdfPath("/World/Material"), map);
    vulkanGenerated = vulkan.fragmentSpirv.size() > 5 &&
        vulkan.fragmentSpirv[0] == 0x07230203u &&
        vulkan.fragmentSource.find("main") != std::string::npos;
    if (!vulkan.success)
    {
        std::cerr << "MaterialX Vulkan generation failed: " << vulkan.error << "\n";
        return false;
    }
#endif
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
    const HdSilkMaterialXVulkanShader msl =
        HdSilkGenerateMaterialXVulkanFragment(SdfPath("/World/Material"), map);
    mslGenerated = msl.success &&
        msl.fragmentMslSource.find("fragment") != std::string::npos;
    if (!mslGenerated)
    {
        std::cerr << "MaterialX Metal generation failed: " << msl.error << "\n";
        return false;
    }
#endif

    return vulkanGenerated &&
        mslGenerated &&
        document.xml.find("standard_surface") != std::string::npos &&
        document.xml.find("image") != std::string::npos &&
        document.xml.find("materialx-basecolor.png") != std::string::npos &&
        document.xml.find("specular_roughness") != std::string::npos &&
        document.textureNodeCount == 1 &&
        document.primvarNodeCount == 1;
}

/// Builds the surface network UsdImaging produces for a material that authors
/// only `outputs:mdl:surface`, after HdSilk_MdlMaterialSceneIndexPlugin has
/// folded the MDL source asset and subIdentifier into the node identifier. The
/// network deliberately carries no UsdPreviewSurface and no MaterialX node:
/// that absence is the condition the MDL branch exists for.
HdMaterialNetworkMap
MakeMdlNetwork(
    const std::string& moduleUri,
    const std::string& materialName,
    const std::map<TfToken, VtValue>& inputs)
{
    HdMaterialNode surface;
    surface.path = SdfPath("/World/Looks/MdlMat/Shader");
    surface.identifier = TfToken("mdl:" + moduleUri + ":" + materialName);
    surface.parameters = inputs;

    HdMaterialNetwork network;
    network.nodes = {surface};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.terminals.push_back(surface.path);
    return map;
}

std::map<TfToken, VtValue>
MakeOmniPbrInputs()
{
    std::map<TfToken, VtValue> inputs;
    inputs[TfToken("diffuse_color_constant")] =
        VtValue(GfVec3f(0.72f, 0.28f, 0.12f));
    inputs[TfToken("reflection_roughness_constant")] = VtValue(0.35f);
    inputs[TfToken("metallic_constant")] = VtValue(0.25f);
    inputs[TfToken("enable_opacity")] = VtValue(true);
    inputs[TfToken("opacity_constant")] = VtValue(0.5f);
    inputs[TfToken("enable_emission")] = VtValue(true);
    inputs[TfToken("emissive_color")] = VtValue(GfVec3f(0.1f, 0.2f, 0.4f));
    inputs[TfToken("normalmap_texture")] =
        VtValue(SdfAssetPath("textures/mdl-normal.png"));
    // Outside the accepted subset: must be reported, never folded into another
    // parameter.
    inputs[TfToken("subsurface_weight")] = VtValue(0.4f);
    return inputs;
}

const HdSilkMaterialScalar*
FindScalar(const HdSilkMaterialRecord& record, uint32_t parameter)
{
    for (const HdSilkMaterialScalar& scalar : record.scalars)
    {
        if (scalar.parameter == parameter)
        {
            return &scalar;
        }
    }
    return nullptr;
}

const HdSilkMaterialTexture*
FindTexture(const HdSilkMaterialRecord& record, uint32_t parameter)
{
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (texture.parameter == parameter)
        {
            return &texture;
        }
    }
    return nullptr;
}

void SetMdlAdapterPath(const char* path)
{
    ArchSetEnv("OPENUSD_MDL_ADAPTER_PATH", path, true);
    HdSilkMdlAdapter::ResetForTesting();
}

void ClearMdlAdapterPath()
{
    ArchRemoveEnv("OPENUSD_MDL_ADAPTER_PATH");
    HdSilkMdlAdapter::ResetForTesting();
}

/// The absolute path the loader treats as the adapter's default location: the
/// sibling of the module hosting the loader. Asking the loader for it, rather
/// than deriving it from argv[0], is deliberate -- a second derivation in the
/// probe would test the probe's own path arithmetic instead of the loader's.
std::string ResolveMdlAdapterDefaultPath()
{
    ClearMdlAdapterPath();
    return HdSilkMdlAdapter::GetResolvedPath();
}

std::string JoinProbePath(const std::string& directory, const std::string& name)
{
#if defined(_WIN32)
    const char separator = '\\';
#else
    const char separator = '/';
#endif
    std::string path(directory);
    if (!path.empty() && path.back() != '/' && path.back() != '\\')
    {
        path.push_back(separator);
    }
    path.append(name);
    return path;
}

bool ProbeFileExists(const std::string& path)
{
    std::ifstream file(path, std::ios::binary);
    return file.good();
}

bool CopyProbeFile(const std::string& source, const std::string& destination)
{
    std::ifstream input(source, std::ios::binary);
    if (!input)
    {
        return false;
    }
    std::ofstream output(destination, std::ios::binary | std::ios::trunc);
    if (!output)
    {
        return false;
    }
    output << input.rdbuf();
    return output.good();
}

/// An adapter path that is not absolute must be refused outright. Resolving one
/// against the process working directory is exactly the search this loader
/// exists to avoid, so the refusal is the behaviour under test rather than an
/// incidental validation.
bool VerifyMdlAdapterRejectsRelativePath()
{
    SetMdlAdapterPath(HDSILK_PROBE_MDL_LIBRARY_NAME);
    if (HdSilkMdlAdapter::GetState() != HdSilkMdlAdapterState::PathNotAbsolute)
    {
        return false;
    }
    const HdMaterialNetworkMap map =
        MakeMdlNetwork("OmniPBR.mdl", "OmniPBR", MakeOmniPbrInputs());
    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(SdfPath("/World/Looks/MdlMat"), map);
    return record.surfaceKind == OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE &&
        record.scalars.empty() && record.textures.empty();
}

/// An absolute path naming a library that is not there is LoadFailed, not
/// NotInstalled: the operator asked for a specific library and did not get it,
/// and that is a different thing to report than a default install.
bool VerifyMdlAdapterMissingExplicitPath(const std::string& defaultPath)
{
    const std::string missing = defaultPath + ".absent-on-purpose";
    SetMdlAdapterPath(missing.c_str());
    return HdSilkMdlAdapter::GetState() == HdSilkMdlAdapterState::LoadFailed;
}

/// The default location is the absolute sibling of the module hosting the
/// loader, and nothing else. A library sitting in the process working directory
/// under the adapter's own name must not be found: the loader must report
/// NotInstalled without attempting any load at all. A loader that passed a bare
/// library name to the platform would load that file instead, which is the
/// hazard this check pins.
///
/// The working directory is changed for the duration of the check because CTest
/// runs the probe from its own binary directory, which is also the directory
/// the probe's copy of the loader treats as the default location. With the two
/// the same, planting a decoy would land in the default slot and prove nothing.
bool VerifyMdlAdapterIgnoresWorkingDirectory(const std::string& defaultPath)
{
    if (ProbeFileExists(defaultPath))
    {
        // The default slot must be empty for this check to mean anything.
        return false;
    }

    std::array<char, 4096> previous{};
#if defined(_WIN32)
    if (_getcwd(previous.data(), static_cast<int>(previous.size())) == nullptr ||
        _chdir(HDSILK_PROBE_MDL_DECOY_DIR) != 0)
#else
    if (getcwd(previous.data(), previous.size()) == nullptr ||
        chdir(HDSILK_PROBE_MDL_DECOY_DIR) != 0)
#endif
    {
        return false;
    }

    const std::string decoy = HDSILK_PROBE_MDL_LIBRARY_NAME;
    bool planted = false;
    {
        std::ofstream file(decoy, std::ios::binary | std::ios::trunc);
        planted = static_cast<bool>(file);
        if (planted)
        {
            file << "not a library";
        }
    }

    HdSilkMdlAdapterState state = HdSilkMdlAdapterState::Loaded;
    std::string resolved;
    if (planted)
    {
        ClearMdlAdapterPath();
        state = HdSilkMdlAdapter::GetState();
        resolved = HdSilkMdlAdapter::GetResolvedPath();
        std::remove(decoy.c_str());
    }

#if defined(_WIN32)
    const bool restored = _chdir(previous.data()) == 0;
#else
    const bool restored = chdir(previous.data()) == 0;
#endif

    return planted && restored &&
        state == HdSilkMdlAdapterState::NotInstalled &&
        resolved == defaultPath;
}

/// The loader refuses a library that does not implement this build's ABI, and
/// reaching that refusal at all proves the dependency rule. The stub lives in a
/// private directory that is on no search path and links a support library
/// staged beside it; if the loader did not resolve the stub's dependency from
/// the directory the stub was loaded from, the stub would not load and the
/// state would be LoadFailed instead.
bool VerifyMdlAdapterAbiMismatchAndSiblingDependency()
{
    SetMdlAdapterPath(HDSILK_PROBE_MDL_ABI_STUB);
    if (HdSilkMdlAdapter::GetState() != HdSilkMdlAdapterState::AbiMismatch)
    {
        return false;
    }
    const std::string description = HdSilkMdlAdapter::GetDescription();
    return description.find("ABI version") != std::string::npos;
}

#if defined(HDSILK_PROBE_MDL_ADAPTER)
/// With no configured path, an adapter placed beside the module hosting the
/// loader is found and used. This is the deployment the documentation
/// describes, and it is the only default location the loader will accept.
bool VerifyMdlAdapterLoadsFromModuleSibling(const std::string& defaultPath)
{
    if (defaultPath.empty() || !CopyProbeFile(HDSILK_PROBE_MDL_ADAPTER, defaultPath))
    {
        return false;
    }
    ClearMdlAdapterPath();
    const bool loaded =
        HdSilkMdlAdapter::GetState() == HdSilkMdlAdapterState::Loaded &&
        HdSilkMdlAdapter::GetResolvedPath() == defaultPath;
    bool distilled = false;
    if (loaded)
    {
        const HdMaterialNetworkMap map =
            MakeMdlNetwork("OmniPBR.mdl", "OmniPBR", MakeOmniPbrInputs());
        const HdSilkMaterialRecord record =
            HdSilkMaterial::Resolve(SdfPath("/World/Looks/MdlMat"), map);
        distilled = record.surfaceKind == OPENUSD_SILK_SURFACE_MDL_DISTILLED;
    }

    // The sibling is removed again so the working-directory check that follows
    // sees an empty default slot. The loader is reset while pointing nowhere so
    // it releases its handle on the copy before the copy is deleted.
    SetMdlAdapterPath((defaultPath + ".absent-on-purpose").c_str());
    std::remove(defaultPath.c_str());
    return loaded && distilled;
}
#endif

/// With no adapter installed -- the state of every base package -- an MDL-only
/// material must still be published, marked MDL-unavailable and carrying no
/// shading data. Publishing a supported-but-empty record instead is exactly the
/// silent default grey this branch removes.
bool VerifyMdlAdapterUnavailable(const std::string& defaultPath)
{
    const std::string missing = defaultPath + ".absent-on-purpose";
    SetMdlAdapterPath(missing.c_str());
    if (HdSilkMdlAdapter::GetState() == HdSilkMdlAdapterState::Loaded)
    {
        return false;
    }
    const HdMaterialNetworkMap map =
        MakeMdlNetwork("OmniPBR.mdl", "OmniPBR", MakeOmniPbrInputs());
    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(SdfPath("/World/Looks/MdlMat"), map);
    return record.surfaceKind == OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE &&
        record.scalars.empty() &&
        record.textures.empty() &&
        record.path == "/World/Looks/MdlMat";
}

#if defined(HDSILK_PROBE_MDL_ADAPTER)
/// With the optional adapter installed, the accepted OmniPBR subset distils
/// into the same scalar and texture tables a UsdPreviewSurface fills, and the
/// record is marked with the MDL provenance kind rather than pretending to be
/// an authored preview surface.
bool VerifyMdlDistillation()
{
    SetMdlAdapterPath(HDSILK_PROBE_MDL_ADAPTER);
    if (HdSilkMdlAdapter::GetState() != HdSilkMdlAdapterState::Loaded)
    {
        return false;
    }
    const HdMaterialNetworkMap map =
        MakeMdlNetwork("OmniPBR.mdl", "OmniPBR", MakeOmniPbrInputs());
    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(SdfPath("/World/Looks/MdlMat"), map);
    if (record.surfaceKind != OPENUSD_SILK_SURFACE_MDL_DISTILLED)
    {
        return false;
    }

    const HdSilkMaterialScalar* diffuse =
        FindScalar(record, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR);
    const HdSilkMaterialScalar* roughness =
        FindScalar(record, OPENUSD_SILK_MATERIAL_ROUGHNESS);
    const HdSilkMaterialScalar* metallic =
        FindScalar(record, OPENUSD_SILK_MATERIAL_METALLIC);
    const HdSilkMaterialScalar* opacity =
        FindScalar(record, OPENUSD_SILK_MATERIAL_OPACITY);
    const HdSilkMaterialScalar* emissive =
        FindScalar(record, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR);
    const HdSilkMaterialTexture* normal =
        FindTexture(record, OPENUSD_SILK_MATERIAL_NORMAL);
    if (diffuse == nullptr || roughness == nullptr || metallic == nullptr ||
        opacity == nullptr || emissive == nullptr || normal == nullptr)
    {
        return false;
    }
    return diffuse->componentCount == 3 &&
        std::fabs(diffuse->value[0] - 0.72f) < 1e-5f &&
        std::fabs(diffuse->value[1] - 0.28f) < 1e-5f &&
        std::fabs(diffuse->value[2] - 0.12f) < 1e-5f &&
        std::fabs(roughness->value[0] - 0.35f) < 1e-5f &&
        std::fabs(metallic->value[0] - 0.25f) < 1e-5f &&
        std::fabs(opacity->value[0] - 0.5f) < 1e-5f &&
        std::fabs(emissive->value[2] - 0.4f) < 1e-5f &&
        normal->outputChannel == OPENUSD_SILK_TEXTURE_CHANNEL_RGB &&
        normal->sourceColorSpace == OPENUSD_SILK_COLOR_SPACE_RAW &&
        normal->uvPrimvar == "st" &&
        normal->asset.find("mdl-normal.png") != std::string::npos;
}

/// An MDL module outside the accepted set is refused by name. It must not fall
/// back to the accepted mapping of a different module, and it must not publish
/// a shadeable record.
bool VerifyMdlUnsupportedModule()
{
    SetMdlAdapterPath(HDSILK_PROBE_MDL_ADAPTER);
    const HdMaterialNetworkMap map = MakeMdlNetwork(
        "VendorSpecific.mdl", "VendorSurface", MakeOmniPbrInputs());
    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(SdfPath("/World/Looks/MdlMat"), map);
    return record.surfaceKind == OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE &&
        record.scalars.empty() && record.textures.empty();
}

/// An accepted module whose every accepted input is driven by a connection
/// rather than authored as a constant distils to nothing, and nothing is not a
/// success: the record must stay MDL-unavailable.
bool VerifyMdlConnectedInputsRefused()
{
    SetMdlAdapterPath(HDSILK_PROBE_MDL_ADAPTER);
    std::map<TfToken, VtValue> inputs;
    inputs[TfToken("diffuse_color_constant")] = VtValue(GfVec3f(1.0f, 1.0f, 1.0f));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Looks/MdlMat/Driver");
    driver.identifier = TfToken("ND_constant_color3");

    HdMaterialNetworkMap map = MakeMdlNetwork("OmniPBR.mdl", "OmniPBR", inputs);
    HdMaterialNetwork& network = map.map[HdMaterialTerminalTokens->surface];
    network.nodes.insert(network.nodes.begin(), driver);
    network.relationships.push_back(
        {driver.path,
         TfToken("out"),
         SdfPath("/World/Looks/MdlMat/Shader"),
         TfToken("diffuse_color_constant")});

    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(SdfPath("/World/Looks/MdlMat"), map);
    return record.surfaceKind == OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE &&
        record.scalars.empty();
}
#endif

/// An authored universal UsdPreviewSurface context still wins over an MDL one.
/// The dual-context material is the shape Omniverse's own asset guidance asks
/// for, and adding MDL to the delegate's render contexts must not change how it
/// resolves.
bool VerifyMdlDoesNotDisplacePreviewSurface()
{
    HdMaterialNode preview;
    preview.path = SdfPath("/World/Looks/Dual/Preview");
    preview.identifier = TfToken("UsdPreviewSurface");
    preview.parameters[TfToken("diffuseColor")] =
        VtValue(GfVec3f(0.1f, 0.9f, 0.3f));

    HdMaterialNode mdl;
    mdl.path = SdfPath("/World/Looks/Dual/Mdl");
    mdl.identifier = TfToken("mdl:OmniPBR.mdl:OmniPBR");
    mdl.parameters[TfToken("diffuse_color_constant")] =
        VtValue(GfVec3f(0.9f, 0.1f, 0.1f));

    HdMaterialNetwork network;
    network.nodes = {mdl, preview};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.terminals.push_back(preview.path);

    const HdSilkMaterialRecord record =
        HdSilkMaterial::Resolve(SdfPath("/World/Looks/Dual"), map);
    const HdSilkMaterialScalar* diffuse =
        FindScalar(record, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR);
    return record.surfaceKind == OPENUSD_SILK_SURFACE_PREVIEW_SURFACE &&
        diffuse != nullptr && std::fabs(diffuse->value[1] - 0.9f) < 1e-5f;
}

std::string BlendShapeProbeStagePath(const char* probeStagePath)
{
    std::string path(probeStagePath);
    const std::string fileName = "hdsilk-probe-stage.usda";
    const size_t position = path.rfind(fileName);
    if (position == std::string::npos)
    {
        return "hdsilk-blendshape-probe.usda";
    }
    path.replace(position, fileName.size(), "hdsilk-blendshape-probe.usda");
    return path;
}

std::string SiblingProbeStagePath(
    const char* probeStagePath,
    const std::string& siblingFileName)
{
    std::string path(probeStagePath);
    const std::string fileName = "hdsilk-probe-stage.usda";
    const size_t position = path.rfind(fileName);
    if (position == std::string::npos)
    {
        return siblingFileName;
    }
    path.replace(position, fileName.size(), siblingFileName);
    return path;
}

/// One expected published instance of a point-instancer prototype.
struct ExpectedInstance
{
    int32_t index = 0;
    bool carries_geometry = false;
    double translate_x = 0.0;
};

/// Collects every published record whose path contains "marker", in wire
/// order. The prototype rprim path UsdImaging synthesizes for a point
/// instancer carries a generated suffix, so the authored prototype name is
/// what identifies it.
std::vector<ParsedMeshIdentity> InstancesMatching(
    const ParsedPage& page,
    const char* marker)
{
    std::vector<ParsedMeshIdentity> matches;
    for (const ParsedMeshIdentity& identity : page.mesh_identities)
    {
        if (identity.path.find(marker) != std::string::npos)
        {
            matches.push_back(identity);
        }
    }
    return matches;
}

bool MatchesInstances(
    const ParsedPage& page,
    const char* marker,
    const std::vector<ExpectedInstance>& expected)
{
    const std::vector<ParsedMeshIdentity> actual = InstancesMatching(page, marker);
    if (actual.size() != expected.size())
    {
        std::cerr << "hdSilk instancer prototype '" << marker << "' published "
                  << actual.size() << " instances, expected " << expected.size()
                  << "\n";
        return false;
    }
    for (size_t index = 0; index < expected.size(); ++index)
    {
        const ParsedMeshIdentity& record = actual[index];
        const ExpectedInstance& want = expected[index];
        const bool carriesGeometry = record.point_count != 0;
        if (record.instance_index != want.index ||
            carriesGeometry != want.carries_geometry ||
            std::fabs(record.transform[12] - want.translate_x) > 1e-6)
        {
            std::cerr << "hdSilk instancer prototype '" << marker
                      << "' record " << index << " published index "
                      << record.instance_index << " geometry "
                      << record.point_count << " x " << record.transform[12]
                      << ", expected index " << want.index << " geometry "
                      << (want.carries_geometry ? "yes" : "no") << " x "
                      << want.translate_x << "\n";
            return false;
        }
        if (record.instance_id == 0)
        {
            std::cerr << "hdSilk instanced record carries instance id zero.\n";
            return false;
        }
    }
    return true;
}

bool MatchesRemovals(
    const ParsedPage& page,
    const char* marker,
    const std::vector<int32_t>& expected)
{
    std::vector<int32_t> actual;
    for (size_t index = 0; index < page.remove_paths.size(); ++index)
    {
        if (page.remove_paths[index].find(marker) != std::string::npos)
        {
            actual.push_back(page.remove_instance_indices[index]);
        }
    }
    std::sort(actual.begin(), actual.end());
    if (actual != expected)
    {
        std::cerr << "hdSilk instancer prototype '" << marker
                  << "' retired " << actual.size()
                  << " instances, expected " << expected.size() << "\n";
        return false;
    }
    return true;
}

/// Gates the point-instancer subset hdSilk claims: several prototypes under
/// one instancer, proto indices that vary over time, instances hidden through
/// invisibleIds, and one level of nesting.
///
/// The published instance index is the instance's own index inside the
/// instancer, which is what UsdImaging decodes back to a scene instance. The
/// position in the resolved array is not: with two prototypes it counts from
/// zero for each of them, and it renumbers every surviving instance when
/// proto indices change or an instance is hidden, which silently re-points a
/// retained pick identity at a different scene instance.
bool VerifyPointInstancerProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "hdsilk-pointinstancer-probe.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "hdSilk point-instancer session create failed.\n";
        return false;
    }

    bool passed = true;

    // Frame 1 assigns proto indices [0, 1, 0, 1], so Alpha owns instancer
    // instances 0 and 2 while Beta owns 1 and 3. Beta's payload therefore
    // rides on instance 1: the prototype record is the lowest published
    // index, not index zero.
    ParsedPage first;
    if (Sync(session, &first, error, nullptr, 1.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    passed = passed &&
        MatchesInstances(
            first,
            "Alpha",
            {{0, true, 10.0}, {2, false, 12.0}}) &&
        MatchesInstances(
            first,
            "Beta",
            {{1, true, 11.0}, {3, false, 13.0}}) &&
        // Two outer instances of a three-instance inner instancer resolve to
        // six leaves whose composed index is outer * 3 + inner.
        MatchesInstances(
            first,
            "Leaf",
            {{0, true, 0.0},
             {1, false, 1.0},
             {2, false, 2.0},
             {3, false, 100.0},
             {4, false, 101.0},
             {5, false, 102.0}}) &&
        // NestedMulti's inner instancer has four instances and two prototypes.
        // The nested radix is that instance count and nothing else, so both
        // prototypes share one interleaved index space: TwigA owns inner 0 and
        // 2, TwigB owns inner 1 and 3, and the composed index is
        // outer * 4 + inner for both. A radix widened to fit whichever
        // prototype was being resolved would have numbered them against
        // different bases.
        MatchesInstances(
            first,
            "TwigA",
            {{0, true, 0.0},
             {2, false, 2.0},
             {4, false, 200.0},
             {6, false, 202.0}}) &&
        MatchesInstances(
            first,
            "TwigB",
            {{1, true, 1.0},
             {3, false, 3.0},
             {5, false, 201.0},
             {7, false, 203.0}}) &&
        // An empty mesh publishes nothing, instanced or not. A record with no
        // points and no indices is indistinguishable on the wire from an ABI
        // v8 instance reference, so an empty prototype would publish a payload
        // record that looks like a record reusing a payload.
        MatchesInstances(first, "Hollow", {}) &&
        // ABI v8 payload elision is topology neutral. Line-list and point-list
        // prototypes carry their geometry once on the lowest published index
        // exactly as a triangle-list prototype does. Proto indices [0, 1, 0]
        // put the second prototype of each pair on instance 1 alone, so its
        // payload record is a non-zero index.
        MatchesInstances(
            first,
            "StrandA",
            {{0, true, 0.0}, {2, false, 2.0}}) &&
        MatchesInstances(first, "StrandB", {{1, true, 1.0}}) &&
        MatchesInstances(
            first,
            "SpeckA",
            {{0, true, 0.0}, {2, false, 2.0}}) &&
        MatchesInstances(first, "SpeckB", {{1, true, 1.0}});

    // Frame 2 swaps the proto indices to [1, 0, 1, 0]. Each prototype now owns
    // the other two instancer instances, so the previous identities retire and
    // the new ones appear rather than the old ones silently changing meaning.
    ParsedPage second;
    if (Sync(session, &second, error, nullptr, 2.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    passed = passed &&
        MatchesInstances(
            second,
            "Alpha",
            {{1, true, 21.0}, {3, false, 23.0}}) &&
        MatchesInstances(
            second,
            "Beta",
            {{0, true, 20.0}, {2, false, 22.0}}) &&
        MatchesRemovals(second, "Alpha", {0, 2}) &&
        MatchesRemovals(second, "Beta", {1, 3}) &&
        // The nested prototypes swap the same way and against the same radix:
        // every composed index still equals outer * 4 + inner, so the two
        // prototypes trade index sets instead of renumbering each other.
        MatchesInstances(
            second,
            "TwigA",
            {{1, true, 1.0},
             {3, false, 3.0},
             {5, false, 201.0},
             {7, false, 203.0}}) &&
        MatchesInstances(
            second,
            "TwigB",
            {{0, true, 0.0},
             {2, false, 2.0},
             {4, false, 200.0},
             {6, false, 202.0}}) &&
        MatchesRemovals(second, "TwigA", {0, 2, 4, 6}) &&
        MatchesRemovals(second, "TwigB", {1, 3, 5, 7});

    // Frame 3 keeps frame 2's assignment and hides instancer instance 0 with
    // invisibleIds. Only Beta's instance 0 retires; its surviving instance
    // keeps index 2 instead of being renumbered down to zero, and inherits the
    // prototype payload because it is now the lowest published index.
    ParsedPage third;
    if (Sync(session, &third, error, nullptr, 3.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    passed = passed &&
        MatchesInstances(
            third,
            "Alpha",
            {{1, true, 31.0}, {3, false, 33.0}}) &&
        MatchesInstances(third, "Beta", {{2, true, 32.0}}) &&
        MatchesRemovals(third, "Alpha", {}) &&
        MatchesRemovals(third, "Beta", {0}) &&
        // Hiding inner instance 0 removes exactly the two composed indices it
        // owned under each outer instance -- 0 and 4 -- and leaves TwigA's
        // indices and TwigB's surviving indices untouched. Renumbering the
        // survivors down would have been invisible to a per-prototype radix
        // and is exactly what the authoritative index prevents.
        MatchesInstances(
            third,
            "TwigA",
            {{1, true, 1.0},
             {3, false, 3.0},
             {5, false, 201.0},
             {7, false, 203.0}}) &&
        MatchesInstances(
            third,
            "TwigB",
            {{2, true, 2.0}, {6, false, 202.0}}) &&
        MatchesRemovals(third, "TwigA", {}) &&
        MatchesRemovals(third, "TwigB", {0, 4});

    openusd_silk_session_release(session);
    return passed;
}

/// Gates per-instance UsdLux linking under nested instancing and per-instancer
/// linking under point instancing, end to end, from authored collections on a
/// stage to sparse ABI v21 LIGHT_LINK entries on the identities hdSilk
/// publishes.
///
/// The nested half nests two instanceable prims inside a prototype that two more
/// instanceable prims reference, so UsdImaging builds an inner instancer whose
/// parent is an outer one and hdSilk publishes four composed identities:
/// outerIndex * innerInstanceCount + innerIndex. The collections partition those
/// identities three different ways -- one light excludes one outer instance, its
/// caster collection excludes the other, and the two domes split them again --
/// so a resolution that dropped the ancestor level, applied the inner index
/// directly, or intersected the collections produces a different table in each
/// case.
///
/// The point-instancer half scatters two prototypes that live outside the
/// instancer's namespace, with a hidden instance. Every collection reaching them
/// names the instancer and excludes the prototype scope, so all three of their
/// masks can only arrive through the instancer prim's own path-wide categories,
/// which is exactly how HdsiLightLinkingSceneIndex reports a linked point
/// instancer.
///
/// The evidence is the identity each entry names. Every entry a prototype owns
/// must be one of the identities the page's own MESH_UPSERT records publish, so
/// a phantom row is a failure rather than a harmless extra.
bool VerifyNestedInstanceLinkingProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    const std::string stagePath = SiblingProbeStagePath(
        probeStagePath,
        "hdsilk-nested-linking-probe.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "hdSilk nested-linking session create failed.\n";
        return false;
    }

    ParsedPage page;
    if (Sync(session, &page, error, nullptr, 0.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    openusd_silk_session_release(session);

    // Exactly four composed identities are published, and their indices are the
    // mixed-radix composition rather than a per-level index.
    std::vector<int32_t> published;
    std::string leafPath;
    std::map<int32_t, double> translations;
    for (const ParsedMeshIdentity& identity : page.mesh_identities)
    {
        if (identity.path.find("Leaf") == std::string::npos)
        {
            continue;
        }
        published.push_back(identity.instance_index);
        translations[identity.instance_index] = identity.transform[12];
        leafPath = identity.path;
    }
    std::sort(published.begin(), published.end());
    if (published != std::vector<int32_t>({0, 1, 2, 3}))
    {
        std::cerr << "hdSilk nested-linking stage published "
                  << published.size() << " leaf instances:";
        for (int32_t index : published)
        {
            std::cerr << ' ' << index;
        }
        std::cerr << "\n";
        return false;
    }

    // Splitting the identities across two collections must not move any of
    // them: the outer instances translate by 0 and 100 and the inner ones by 0
    // and 2, so the composed identity that loses a light must still be drawn at
    // its own composed transform.
    const std::vector<std::pair<int32_t, double>> expectedTranslations{
        {0, 0.0}, {1, 2.0}, {2, 100.0}, {3, 102.0}};
    for (const auto& expectedTranslation : expectedTranslations)
    {
        const double actual = translations[expectedTranslation.first];
        if (std::fabs(actual - expectedTranslation.second) > 1e-6)
        {
            std::cerr << "hdSilk nested-linking identity "
                      << expectedTranslation.first << " was drawn at x="
                      << actual << ", expected "
                      << expectedTranslation.second << "\n";
            return false;
        }
    }

    if (!page.light_link_valid ||
        page.light_link_count != 1 ||
        page.light_link_light_count != 2 ||
        page.light_link_dome_count != 2 ||
        page.light_link_unsupported != OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE)
    {
        std::cerr << "hdSilk nested-linking page published "
                  << page.light_link_count << " link commands with "
                  << page.light_link_light_count << " lights and "
                  << page.light_link_dome_count << " domes, unsupported "
                  << page.light_link_unsupported << "\n";
        return false;
    }

    // Every entry the prototype owns has to name a published identity: the
    // prototype path itself, or one of the four composed indices. A row for
    // anything else addresses no record and consumes the bounded table for
    // nothing. The prototype's own rows are exactly the path row plus one per
    // composed identity, so a level that emitted a row per (outer, inner) pair
    // without intersecting against the indices each level draws would be caught
    // by the count alone.
    size_t leafEntries = 0;
    for (const auto& entry : page.light_links)
    {
        if (std::get<0>(entry) != leafPath)
        {
            continue;
        }
        ++leafEntries;
        const int32_t instanceIndex = std::get<1>(entry);
        if (instanceIndex != OPENUSD_SILK_LINK_ALL_INSTANCES &&
            !std::binary_search(
                published.begin(),
                published.end(),
                instanceIndex))
        {
            std::cerr << "hdSilk nested-linking table named an unpublished "
                      << "identity " << std::get<0>(entry) << " instance "
                      << instanceIndex << "\n";
            return false;
        }
    }
    if (leafEntries != published.size() + 1)
    {
        std::cerr << "hdSilk nested-linking prototype published "
                  << leafEntries << " entries, expected "
                  << (published.size() + 1) << "\n";
        return false;
    }

    // The masks each composed identity resolves to. Outer instance 0 owns
    // composed 0 and 1, outer instance 1 owns composed 2 and 3.
    auto resolve = [&page](const std::string& path, int32_t instanceIndex)
    {
        std::tuple<uint32_t, uint32_t, uint32_t> masks{3u, 3u, 3u};
        for (const auto& entry : page.light_links)
        {
            if (std::get<0>(entry) == path &&
                std::get<1>(entry) == OPENUSD_SILK_LINK_ALL_INSTANCES)
            {
                masks = {
                    std::get<2>(entry),
                    std::get<3>(entry),
                    std::get<4>(entry)};
            }
        }
        for (const auto& entry : page.light_links)
        {
            if (std::get<0>(entry) == path &&
                std::get<1>(entry) == instanceIndex)
            {
                masks = {
                    std::get<2>(entry),
                    std::get<3>(entry),
                    std::get<4>(entry)};
            }
        }
        return masks;
    };

    bool matched = true;
    auto require = [&matched, &resolve](
        const std::string& path,
        int32_t instanceIndex,
        uint32_t light,
        uint32_t shadow,
        uint32_t dome,
        const char* what)
    {
        const auto masks = resolve(path, instanceIndex);
        if (std::get<0>(masks) != light ||
            std::get<1>(masks) != shadow ||
            std::get<2>(masks) != dome)
        {
            std::cerr << "hdSilk nested-linking " << what << " instance "
                      << instanceIndex << " resolved to light="
                      << std::get<0>(masks) << " shadow=" << std::get<1>(masks)
                      << " dome=" << std::get<2>(masks) << ", expected light="
                      << light << " shadow=" << shadow << " dome=" << dome
                      << "\n";
            matched = false;
        }
    };

    // Direct light 0 is /World/Key and 1 is /World/Spot; dome 0 is /World/Sky
    // and 1 is /World/SkyScatter, both orderings sorted by path.
    //
    // The prototype path itself is in no collection, because a native-instance
    // prototype lives outside /World and every collection is authored there.
    // Composed 0 and 1 descend from /World/GroupA, which Key lights, Key's
    // caster collection excludes and SkyScatter alone lights. Composed 2 and 3
    // descend from /World/GroupB, which is the complement in all three. Nothing
    // but the outer instancer's per-instance categories can tell them apart.
    require(leafPath, OPENUSD_SILK_LINK_ALL_INSTANCES, 0u, 0u, 0u, "nested prototype");
    require(leafPath, 0, 1u, 0u, 2u, "nested");
    require(leafPath, 1, 1u, 0u, 2u, "nested");
    require(leafPath, 2, 0u, 1u, 1u, "nested");
    require(leafPath, 3, 0u, 1u, 1u, "nested");

    // The point-instancer half. Every collection that reaches these prototypes
    // names /World/Scatter and excludes /World/ScatterProtos, so the prototype
    // path carries no category of its own: light bit 1, shadow bit 1 and dome
    // bit 0 can only have arrived through the instancer prim's own path-wide
    // categories. Dropping the leaf level's contribution resolves all three
    // masks to zero instead.
    std::map<std::string, std::vector<int32_t>> scattered;
    std::map<std::string, double> scatterOrigin;
    for (const ParsedMeshIdentity& identity : page.mesh_identities)
    {
        if (identity.path.find("ScatterProtos") == std::string::npos)
        {
            continue;
        }
        scattered[identity.path].push_back(identity.instance_index);
        if (identity.instance_index == 0 ||
            scatterOrigin.find(identity.path) == scatterOrigin.end())
        {
            scatterOrigin[identity.path] = identity.transform[12];
        }
    }
    if (scattered.size() != 2)
    {
        std::cerr << "hdSilk nested-linking stage published "
                  << scattered.size() << " scattered prototypes, expected 2\n";
        for (const auto& entry : scattered)
        {
            std::cerr << "  " << entry.first << "\n";
        }
        return false;
    }
    for (auto& prototype : scattered)
    {
        std::sort(prototype.second.begin(), prototype.second.end());
        const bool isRed = prototype.first.find("Red") != std::string::npos;

        // Proto indices [0, 1, 0, 1] give Red instances 0 and 2 and Blue 1 and
        // 3, and invisibleIds hides instance 2. A hidden instance and the
        // instances a prototype does not own must both consume no row at all.
        const std::vector<int32_t> expectedInstances = isRed
            ? std::vector<int32_t>{0}
            : std::vector<int32_t>{1, 3};
        if (prototype.second != expectedInstances)
        {
            std::cerr << "hdSilk scattered prototype " << prototype.first
                      << " published " << prototype.second.size()
                      << " instances:";
            for (int32_t index : prototype.second)
            {
                std::cerr << ' ' << index;
            }
            std::cerr << "\n";
            matched = false;
            continue;
        }
        require(prototype.first, OPENUSD_SILK_LINK_ALL_INSTANCES, 2u, 2u, 1u,
            "scattered prototype");
        size_t rows = 0;
        for (const auto& entry : page.light_links)
        {
            if (std::get<0>(entry) == prototype.first)
            {
                ++rows;
            }
        }
        if (rows != 1)
        {
            std::cerr << "hdSilk scattered prototype " << prototype.first
                      << " published " << rows
                      << " link entries, expected exactly the path row\n";
            matched = false;
        }
    }

    if (!matched)
    {
        for (const auto& entry : page.light_links)
        {
            std::cerr << "  " << std::get<0>(entry)
                      << " instance=" << std::get<1>(entry)
                      << " light=" << std::get<2>(entry)
                      << " shadow=" << std::get<3>(entry)
                      << " dome=" << std::get<4>(entry) << "\n";
        }
    }
    return matched;
}

bool VerifyBlendShapeProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    const std::string stagePath = BlendShapeProbeStagePath(probeStagePath);
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }
    ParsedPage page;
    const openusd_status status = Sync(session, &page, error, nullptr, 2.0);
    openusd_silk_session_release(session);
    return status == OPENUSD_STATUS_OK &&
        page.found_blend_shape_mesh &&
        page.blend_shape_first_x > 0.49F &&
        page.blend_shape_first_x < 0.51F;
}

/// Drives the MDL-only fixture through the whole product path: UsdImaging, the
/// hdSilk scene index plugin that gives the MDL node an identifier, the render
/// delegate's material render contexts, and the page wire format.
///
/// The assertion that matters at every build configuration is that the material
/// is published at all with an MDL surface kind. Before this slice the material
/// arrived with no surface terminal, was published as plain unsupported, and a
/// consumer could not tell MDL from an unrecognised graph.
#if defined(HDSILK_PROBE_MDL_SDK_ADAPTER)
/// Drives a material bound to a repository-authored MDL module through the whole
/// product path with the SDK-backed adapter installed.
///
/// The fixture authors exactly one input and leaves the rest of the accepted
/// subset unauthored, so a published value for any of the others can only have
/// come out of the compiled module. That is what makes this end-to-end evidence
/// of module evaluation rather than of authored values being echoed back.
bool VerifyMdlModuleDefaultsStageProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    ArchSetEnv("OPENUSD_MDL_ADAPTER_PATH", HDSILK_PROBE_MDL_SDK_ADAPTER, true);
    ArchSetEnv("OPENUSD_MDL_MODULE_PATH", HDSILK_PROBE_MDL_MODULE_DIR, true);
#if defined(HDSILK_PROBE_MDL_SDK_RUNTIME_DIR)
    ArchSetEnv("OPENUSD_MDL_SDK_RUNTIME", HDSILK_PROBE_MDL_SDK_RUNTIME_DIR, true);
#endif
    // The delegate caches its adapter once per process, deliberately: an adapter
    // that could be swapped mid-frame would let two materials in one page
    // disagree about what they were shaded from. This probe proves several
    // configurations, so it drops that cache explicitly.
    openusd_hdsilk_test_reset_mdl_adapter();
    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "mdl/mdl-module-defaults.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }
    ParsedPage page;
    const openusd_status status = Sync(session, &page, error, nullptr, 2.0);
    openusd_silk_session_release(session);
    if (status != OPENUSD_STATUS_OK || !page.found_material_upsert ||
        !page.material_valid || page.material_path != "/World/Looks/ProbeMat" ||
        page.material_surface_kind != OPENUSD_SILK_SURFACE_MDL_DISTILLED)
    {
        return false;
    }

    // The authored roughness must win, and the unauthored metallic must have come
    // from the module. Both together are the claim; either alone is not.
    bool authoredRoughness = false;
    bool moduleMetallic = false;
    bool moduleDiffuse = false;
    for (const auto& scalar : page.material_scalars)
    {
        const uint32_t parameter = std::get<0>(scalar);
        const float value = std::get<1>(scalar);
        if (parameter == OPENUSD_SILK_MATERIAL_ROUGHNESS)
        {
            authoredRoughness = std::fabs(value - 0.9F) < 1e-5F;
        }
        else if (parameter == OPENUSD_SILK_MATERIAL_METALLIC)
        {
            moduleMetallic = std::fabs(value - 0.125F) < 1e-5F;
        }
        else if (parameter == OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR)
        {
            moduleDiffuse = std::fabs(value - 0.25F) < 1e-5F;
        }
    }
    return authoredRoughness && moduleMetallic && moduleDiffuse;
}
#endif

/// Finds one published material by path, or null.
const ParsedMaterialRecord* FindParsedMaterial(const ParsedPage& page, const char* path)
{
    for (const ParsedMaterialRecord& material : page.materials)
    {
        if (material.path == path)
        {
            return &material;
        }
    }
    return nullptr;
}

/// Reads one published scalar of a material, or false when it published none.
bool TryReadParsedScalar(
    const ParsedMaterialRecord& material,
    uint32_t parameter,
    float* value)
{
    for (const auto& scalar : material.scalars)
    {
        if (std::get<0>(scalar) == parameter)
        {
            *value = std::get<1>(scalar);
            return true;
        }
    }
    return false;
}

/// Proves the displacement terminal rules against a real stage, resolved through
/// the delegate's own GetMaterialResource path.
///
/// The hand-built network maps elsewhere in this probe exercise the resolution
/// logic; they cannot exercise the composition that produces those maps. This
/// case opens a stage, lets UsdImaging compose it, and checks four claims a
/// hand-built map cannot make: that a connected displacement output publishes,
/// that an *unconnected* one publishes nothing even though the same shader
/// authors the same non-zero input, that a displacement survives a surface this
/// renderer cannot shade, and that an image with no authored file publishes the
/// node's authored fallback rather than dropping the height.
bool VerifyDisplacementStageProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "displacement-terminal-stage.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "Could not open the displacement terminal stage.\n";
        return false;
    }
    ParsedPage page;
    const openusd_status status = Sync(session, &page, error, nullptr, 0.0);
    openusd_silk_session_release(session);
    if (status != OPENUSD_STATUS_OK || !page.material_valid)
    {
        std::cerr << "The displacement terminal stage did not publish valid materials.\n";
        return false;
    }

    const ParsedMaterialRecord* constant =
        FindParsedMaterial(page, "/World/Looks/ConstantDisplaced");
    float amount = 0.0F;
    if (constant == nullptr ||
        !TryReadParsedScalar(*constant, OPENUSD_SILK_MATERIAL_DISPLACEMENT, &amount) ||
        std::fabs(amount - 0.5F) > 1e-6F)
    {
        std::cerr << "A connected displacement terminal did not publish its constant "
                  << "from a composed stage.\n";
        return false;
    }

    // The same shader, the same authored inputs:displacement, no connected
    // outputs:displacement. This is the claim a hand-built map cannot make on
    // its own, because the map is where the connection would have been.
    const ParsedMaterialRecord* surfaceOnly =
        FindParsedMaterial(page, "/World/Looks/SurfaceOnly");
    float unexpected = 0.0F;
    if (surfaceOnly == nullptr ||
        TryReadParsedScalar(*surfaceOnly, OPENUSD_SILK_MATERIAL_DISPLACEMENT, &unexpected))
    {
        std::cerr << "A material with no displacement terminal published displacement "
                  << "from a composed stage.\n";
        return false;
    }

    // A surface this renderer cannot shade does not silence a valid displacement
    // terminal: the prim is drawn with the default surface and still moves.
    const ParsedMaterialRecord* unshaded =
        FindParsedMaterial(page, "/World/Looks/UnshadedDisplaced");
    float unshadedAmount = 0.0F;
    if (unshaded == nullptr ||
        unshaded->surface_kind == OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        !TryReadParsedScalar(
            *unshaded, OPENUSD_SILK_MATERIAL_DISPLACEMENT, &unshadedAmount) ||
        std::fabs(unshadedAmount - 0.75F) > 1e-6F)
    {
        std::cerr << "An unshadeable surface suppressed a valid displacement terminal.\n";
        return false;
    }

    // fallback 0.5 through scale 3 and bias -0.25 is 1.25.
    const ParsedMaterialRecord* emptyFile =
        FindParsedMaterial(page, "/World/Looks/EmptyFileDisplaced");
    float fallbackAmount = 0.0F;
    if (emptyFile == nullptr ||
        !TryReadParsedScalar(
            *emptyFile, OPENUSD_SILK_MATERIAL_DISPLACEMENT, &fallbackAmount) ||
        std::fabs(fallbackAmount - 1.25F) > 1e-5F)
    {
        std::cerr << "A displacement image with no authored file did not publish the "
                  << "authored fallback; published " << fallbackAmount << ".\n";
        return false;
    }

    // A height field reading `st2` while the surface colour reads `st` is exactly
    // representable: the wire carries a primvar per texture entry, and a
    // displacement is sampled per vertex through its own. Reconciling the two
    // into one stream would drop one of them for no reason.
    const ParsedMaterialRecord* independent =
        FindParsedMaterial(page, "/World/Looks/IndependentUvDisplaced");
    if (independent == nullptr)
    {
        std::cerr << "The independent-UV displacement material was not published.\n";
        return false;
    }
    std::string surfacePrimvar;
    std::string displacementPrimvar;
    for (const auto& texture : independent->textures)
    {
        if (std::get<0>(texture) == OPENUSD_SILK_MATERIAL_DISPLACEMENT)
        {
            displacementPrimvar = std::get<3>(texture);
        }
        else if (std::get<0>(texture) == OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR)
        {
            surfacePrimvar = std::get<3>(texture);
        }
    }
    if (surfacePrimvar != "st" || displacementPrimvar != "st2")
    {
        std::cerr << "A displacement sampling its own coordinate set was reconciled "
                  << "into the surface stream: surface='" << surfacePrimvar
                  << "' displacement='" << displacementPrimvar << "'.\n";
        return false;
    }
    return true;
}

bool VerifyMdlOnlyStageProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    // The delegate inside the plugin has its own adapter cache, so the search
    // path is stated here rather than inherited from whatever the in-process
    // resolution checks above left behind. Both branches use an absolute path,
    // because this loader refuses a relative one outright.
#if defined(HDSILK_PROBE_MDL_ADAPTER)
    ArchSetEnv("OPENUSD_MDL_ADAPTER_PATH", HDSILK_PROBE_MDL_ADAPTER, true);
#else
    ArchRemoveEnv("OPENUSD_MDL_ADAPTER_PATH");
#endif
    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "omniverse/mdl-only-omnipbr.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }
    ParsedPage page;
    const openusd_status status = Sync(session, &page, error, nullptr, 2.0);
    openusd_silk_session_release(session);
    if (status != OPENUSD_STATUS_OK || !page.found_material_upsert ||
        !page.material_valid ||
        page.material_path != "/World/Looks/OmniPbrMat")
    {
        return false;
    }
#if defined(HDSILK_PROBE_MDL_ADAPTER)
    return page.material_surface_kind == OPENUSD_SILK_SURFACE_MDL_DISTILLED &&
        page.material_scalar_count > 0;
#else
    return page.material_surface_kind == OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE &&
        page.material_scalar_count == 0 &&
        page.material_texture_count == 0;
#endif
}

const ParsedAttribute* FindParsedAttribute(
    const ParsedCurves& mesh,
    const char* name)
{
    for (const ParsedAttribute& attribute : mesh.attributes)
    {
        if (attribute.name == name)
        {
            return &attribute;
        }
    }
    return nullptr;
}

bool NearlyEqual(float value, float expected, float tolerance = 1e-4F)
{
    return std::fabs(value - expected) <= tolerance;
}

/// Compares one three-component element of a parsed attribute or point array.
bool MatchesVec3(
    const std::vector<float>& values,
    size_t element,
    float x,
    float y,
    float z,
    float tolerance = 1e-4F)
{
    const size_t offset = element * 3;
    return values.size() >= offset + 3 &&
        NearlyEqual(values[offset], x, tolerance) &&
        NearlyEqual(values[offset + 1], y, tolerance) &&
        NearlyEqual(values[offset + 2], z, tolerance);
}

/// Verifies the analytic expectations of hdsilk-deformation-probe.usda at one
/// time code. Every value is computed from the authored stage rather than
/// captured, so a regression in the CPU deformation subset fails arithmetic
/// rather than a pixel comparison.
bool VerifyDeformationProbeAtTime(
    openusd_silk_session* session,
    double timeCode,
    openusd_error_buffer* error,
    std::string* failure)
{
    ParsedPage page;
    const openusd_status status = Sync(session, &page, error, nullptr, timeCode);
    if (status != OPENUSD_STATUS_OK)
    {
        *failure = "sync failed";
        return false;
    }
    if (!page.inbetween_mesh.found || !page.skinned_normal_mesh.found ||
        !page.face_varying_normal_mesh.found)
    {
        *failure = "a deformation probe mesh was not published";
        return false;
    }

    // The ABI v20 rig is gated analytically rather than structurally: every
    // record that published one was re-evaluated from its own bytes while the
    // page was parsed, and the result has to be the deformed points the same
    // record carries. A rig that decodes but describes another surface is
    // exactly the failure a consumer could not detect for itself.
    if (!page.deformation_valid)
    {
        *failure = "a published deformation block did not decode";
        return false;
    }
    // The ABI v22 subprim-identity tables are gated the same way: a record that
    // claims an exact edge or point target must publish the table behind it,
    // every entry must name an authored component the record declares, and a
    // triangulation diagonal must be the explicit sentinel rather than an
    // invented index.
    if (!page.subprim_identity_valid)
    {
        *failure =
            "a published subprim identity table did not match the record's claim";
        return false;
    }
    if (!page.deformation_reproduces_points)
    {
        *failure =
            "a published deformation block did not reproduce the CPU points "
            "(worst relative error " +
            std::to_string(page.deformation_worst_point_error) + ")";
        return false;
    }
    if (page.deformation_published_count < 2)
    {
        *failure =
            "the deformation probe published " +
            std::to_string(page.deformation_published_count) +
            " bounded rigs, expected the blend-shape and skinned meshes";
        return false;
    }

    const ParsedAttribute* inbetweenNormals =
        FindParsedAttribute(page.inbetween_mesh, "normals");
    const ParsedAttribute* skinnedNormals =
        FindParsedAttribute(page.skinned_normal_mesh, "normals");
    if (inbetweenNormals == nullptr || inbetweenNormals->componentCount != 3)
    {
        // The published attribute table is named in the failure, because "no
        // normals" and "normals of the wrong shape" fail here for different
        // reasons and the table is the only thing that tells them apart.
        *failure = "the in-between mesh published no per-point normals (";
        for (const ParsedAttribute& attribute : page.inbetween_mesh.attributes)
        {
            *failure += attribute.name + ":" +
                std::to_string(attribute.componentCount) + ":" +
                std::to_string(attribute.elementCount) + " ";
        }
        *failure += ")";
        return false;
    }
    if (skinnedNormals == nullptr || skinnedNormals->componentCount != 3)
    {
        *failure = "the skinned mesh published no per-point normals";
        return false;
    }

    // Face-varying normals cannot be addressed by the point-indexed offsets and
    // influences UsdSkel resolves, so a deformed mesh publishes none rather
    // than publishing the bind pose of a surface that moved.
    if (FindParsedAttribute(page.face_varying_normal_mesh, "normals") != nullptr)
    {
        *failure =
            "face-varying normals were published for a deformed mesh";
        return false;
    }

    if (timeCode < 1.5)
    {
        // Weight 0 and identity joints: the bind pose travels unchanged.
        const bool ok =
            MatchesVec3(page.inbetween_mesh.points, 0, 0.0F, 0.0F, 0.0F) &&
            MatchesVec3(inbetweenNormals->values, 0, 0.0F, 0.0F, 1.0F) &&
            MatchesVec3(page.skinned_normal_mesh.points, 2, 0.0F, 1.0F, 0.0F) &&
            MatchesVec3(skinnedNormals->values, 0, 0.0F, 0.0F, 1.0F);
        if (!ok)
        {
            *failure = "the undeformed bind pose did not travel unchanged";
        }
        return ok;
    }
    if (timeCode < 2.5)
    {
        // Weight 0.5 lands exactly on the authored in-between shape, whose
        // offsets are deliberately not the linear midpoint of the primary
        // shape: linear interpolation would publish x=1, not x=0.25.
        const bool ok =
            MatchesVec3(page.inbetween_mesh.points, 0, 0.25F, 0.0F, 0.0F) &&
            MatchesVec3(
                inbetweenNormals->values,
                0,
                0.0F,
                0.242536F,
                0.970143F) &&
            MatchesVec3(inbetweenNormals->values, 1, 0.0F, 0.0F, 1.0F);
        if (!ok)
        {
            *failure = "the in-between shape was not resolved at weight 0.5";
        }
        return ok;
    }

    // Weight 1 selects the primary shape, and the second joint rotates 90
    // degrees about X, so the skinned points and normals both change axis.
    const bool ok =
        MatchesVec3(page.inbetween_mesh.points, 0, 2.0F, 0.0F, 0.0F) &&
        MatchesVec3(
            inbetweenNormals->values,
            0,
            0.0F,
            0.894427F,
            0.447214F) &&
        MatchesVec3(page.skinned_normal_mesh.points, 1, 1.0F, 0.0F, 0.0F) &&
        MatchesVec3(page.skinned_normal_mesh.points, 2, 0.0F, 0.0F, 1.0F) &&
        MatchesVec3(skinnedNormals->values, 0, 0.0F, -1.0F, 0.0F) &&
        MatchesVec3(skinnedNormals->values, 2, 0.0F, -1.0F, 0.0F);
    if (!ok)
    {
        *failure =
            "the primary shape or the skinned normal rotation did not resolve";
    }
    return ok;
}

/// The published-rig self-verification treats a degenerate normal as ignorable
/// only when both sides are degenerate. A one-sided degeneracy is a
/// disagreement about the surface: the rig either collapsed a direction the CPU
/// deformation kept, or kept one the CPU deformation collapsed, and publishing
/// it as verified would hand a consumer a normal hdSilk never resolved.
///
/// This case cannot be reached from a stage fixture, so the four combinations
/// are constructed through a test hook. The two one-sided cases are the
/// load-bearing ones: an implementation that skipped whenever either side was
/// degenerate would pass all four and prove nothing.
bool VerifyDeformationDegenerateNormalRule(std::string* failure)
{
    struct Case
    {
        int32_t resolvedDegenerate;
        int32_t evaluatedDegenerate;
        int32_t expected;
        const char* name;
    };
    static const Case cases[] = {
        {0, 0, 1, "neither side degenerate must verify"},
        {1, 1, 1, "both sides degenerate must verify"},
        {1, 0, 0, "a degenerate CPU normal against a resolved rig normal "
                  "must be refused"},
        {0, 1, 0, "a resolved CPU normal against a degenerate rig normal "
                  "must be refused"}};
    for (const Case& entry : cases)
    {
        const int32_t verified =
            openusd_hdsilk_test_verify_degenerate_normal_rule(
                entry.resolvedDegenerate,
                entry.evaluatedDegenerate);
        if (verified != entry.expected)
        {
            *failure = entry.name;
            return false;
        }
    }
    return true;
}

bool VerifyDeformationProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error,
    std::string* failure)
{    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "hdsilk-deformation-probe.usda");
    for (double timeCode : {1.0, 2.0, 3.0})
    {
        openusd_silk_session* session = nullptr;
        if (openusd_silk_session_create(
                pluginPath,
                stagePath.c_str(),
                &session,
                error) != OPENUSD_STATUS_OK)
        {
            *failure = "session create failed";
            return false;
        }
        const bool ok = VerifyDeformationProbeAtTime(
            session,
            timeCode,
            error,
            failure);
        openusd_silk_session_release(session);
        if (!ok)
        {
            *failure += " at timeCode " + std::to_string(timeCode);
            return false;
        }
    }
    return true;
}

/// Time-varying invalidation, measured inside one session rather than across
/// fresh ones: the deformed points and normals must both follow the evaluation
/// time forward and back, so a cached bind pose cannot survive a scrub and a
/// cached deformed pose cannot survive a scrub back to the bind pose.
bool VerifyDeformationInvalidation(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error,
    std::string* failure)
{
    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "hdsilk-deformation-probe.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(
            pluginPath,
            stagePath.c_str(),
            &session,
            error) != OPENUSD_STATUS_OK)
    {
        *failure = "session create failed";
        return false;
    }

    bool passed = true;
    for (double timeCode : {1.0, 3.0, 1.0})
    {
        ParsedPage page;
        if (Sync(session, &page, error, nullptr, timeCode) !=
            OPENUSD_STATUS_OK)
        {
            *failure = "sync failed";
            passed = false;
            break;
        }
        if (!page.inbetween_mesh.found || !page.skinned_normal_mesh.found)
        {
            *failure = "a deformed mesh was not republished after a time change";
            passed = false;
            break;
        }
        const ParsedAttribute* skinnedNormals =
            FindParsedAttribute(page.skinned_normal_mesh, "normals");
        if (skinnedNormals == nullptr)
        {
            *failure = "the skinned mesh published no normals after a scrub";
            passed = false;
            break;
        }
        const bool deformed = timeCode > 2.0;
        const float expectedX = deformed ? 2.0F : 0.0F;
        const float expectedNormalY = deformed ? -1.0F : 0.0F;
        const float expectedNormalZ = deformed ? 0.0F : 1.0F;
        if (!MatchesVec3(page.inbetween_mesh.points, 0, expectedX, 0.0F, 0.0F) ||
            !MatchesVec3(
                skinnedNormals->values,
                0,
                0.0F,
                expectedNormalY,
                expectedNormalZ))
        {
            *failure = "a deformed value did not follow the evaluation time";
            passed = false;
            break;
        }
    }

    openusd_silk_session_release(session);
    return passed;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: hdsilk_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    if (!VerifySceneStateSerialization())
    {
        std::cerr << "hdSilk scene-state ABI serialization checks failed.\n";
        return 3;
    }
    if (openusd_hdsilk_test_external_delegate_does_not_publish() != 1)
    {
        std::cerr << "External hdSilk delegate stole a creation token.\n";
        return 4;
    }
    if (!VerifyMaterialXBridge())
    {
        std::cerr << "hdSilk MaterialX bridge check failed.\n";
        return 4;
    }
    if (!VerifyDisplacementTerminal())
    {
        std::cerr << "hdSilk displacement terminal resolution check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXUvChainProjection())
    {
        std::cerr << "hdSilk MaterialX UV chain projection check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXImageArithmeticProjection())
    {
        std::cerr << "hdSilk MaterialX image arithmetic projection check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXConstantFoldProjection())
    {
        std::cerr << "hdSilk MaterialX constant fold projection check failed.\n";
        return 4;
    }
    if (!VerifySurfaceModelProjection())
    {
        std::cerr << "hdSilk MaterialX surface model projection check failed.\n";
        return 4;
    }
    if (!VerifyGeneratedUnlitSurvivesGenerationFailure())
    {
        std::cerr << "hdSilk ND_surface_unlit generation failure check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXImageSamplingProjection())
    {
        std::cerr << "hdSilk MaterialX image sampling projection check failed.\n";
        return 4;
    }
    if (!VerifyMdlDoesNotDisplacePreviewSurface())
    {
        std::cerr
            << "hdSilk resolved an MDL node over an authored UsdPreviewSurface "
            << "terminal.\n";
        return 4;
    }
    const std::string mdlAdapterDefaultPath = ResolveMdlAdapterDefaultPath();
    if (mdlAdapterDefaultPath.empty())
    {
        std::cerr
            << "hdSilk MDL adapter loader could not form its default adapter "
            << "path from the module hosting it.\n";
        return 4;
    }
    if (!VerifyMdlAdapterRejectsRelativePath())
    {
        std::cerr
            << "hdSilk MDL adapter loader accepted a relative "
            << "OPENUSD_MDL_ADAPTER_PATH.\n";
        return 4;
    }
    if (!VerifyMdlAdapterMissingExplicitPath(mdlAdapterDefaultPath))
    {
        std::cerr
            << "hdSilk MDL adapter loader did not report a missing explicit "
            << "adapter path as a load failure.\n";
        return 4;
    }
    if (!VerifyMdlAdapterAbiMismatchAndSiblingDependency())
    {
        std::cerr
            << "hdSilk MDL adapter loader did not refuse a wrong-ABI adapter, "
            << "or did not resolve that adapter's sibling dependency.\n";
        return 4;
    }
#if defined(HDSILK_PROBE_MDL_ADAPTER)
    if (!VerifyMdlAdapterLoadsFromModuleSibling(mdlAdapterDefaultPath))
    {
        std::cerr
            << "hdSilk MDL adapter loader did not load an adapter placed beside "
            << "the module hosting it.\n";
        return 4;
    }
#endif
    if (!VerifyMdlAdapterIgnoresWorkingDirectory(mdlAdapterDefaultPath))
    {
        std::cerr
            << "hdSilk MDL adapter loader consulted the process working "
            << "directory instead of the module sibling.\n";
        return 4;
    }
    if (!VerifyMdlAdapterUnavailable(mdlAdapterDefaultPath))
    {
        std::cerr
            << "hdSilk did not publish an MDL-only material as MDL-unavailable "
            << "when no adapter is installed.\n";
        return 4;
    }
#if defined(HDSILK_PROBE_MDL_ADAPTER)
    if (!VerifyMdlDistillation())
    {
        std::cerr << "hdSilk MDL distillation check failed.\n";
        return 4;
    }
    if (!VerifyMdlUnsupportedModule())
    {
        std::cerr
            << "hdSilk distilled an MDL module outside the accepted set.\n";
        return 4;
    }
    if (!VerifyMdlConnectedInputsRefused())
    {
        std::cerr
            << "hdSilk distilled an MDL material whose inputs are all "
            << "connected rather than authored.\n";
        return 4;
    }
#endif
    if (!VerifyMdlOnlyStageProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk MDL-only stage probe did not publish the expected "
            << "material: " << errorText.data() << "\n";
        return 4;
    }
    if (!VerifyDisplacementStageProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk displacement terminal stage probe failed: "
            << errorText.data() << "\n";
        return 4;
    }
#if defined(HDSILK_PROBE_MDL_SDK_ADAPTER)
    if (!VerifyMdlModuleDefaultsStageProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk MDL module-default stage probe did not distil the "
            << "synthetic module: " << errorText.data() << "\n";
        return 4;
    }
#endif
    if (!VerifyBlendShapeProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk blend-shape probe did not publish the expected "
            << "deformed point: " << errorText.data() << "\n";
        return 4;
    }
    std::string deformationFailure;
    if (!VerifyDeformationDegenerateNormalRule(&deformationFailure))
    {
        std::cerr
            << "hdSilk deformation self-verification rule failed: "
            << deformationFailure << "\n";
        return 4;
    }
    if (!VerifyDeformationProbe(argv[1], argv[2], &error, &deformationFailure))
    {
        std::cerr
            << "hdSilk deformation probe failed: " << deformationFailure
            << ": " << errorText.data() << "\n";
        return 4;
    }
    if (!VerifyDeformationInvalidation(
            argv[1],
            argv[2],
            &error,
            &deformationFailure))
    {
        std::cerr
            << "hdSilk deformation invalidation probe failed: "
            << deformationFailure << ": " << errorText.data() << "\n";
        return 4;
    }
    if (!VerifyPointInstancerProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk point-instancer probe did not publish the expected "
            << "instance identities: " << errorText.data() << "\n";
        return 4;
    }
    if (!VerifyNestedInstanceLinkingProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk nested-instance light linking probe did not publish the "
            << "expected composed link table: " << errorText.data() << "\n";
        return 4;
    }

    const size_t initialStageCoreCount = openusd_test_get_live_stage_core_count();
    openusd_stage* stage = nullptr;
    if (openusd_stage_open(argv[2], &stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_retain(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_session_layer(stage, &error) != OPENUSD_STATUS_OK ||
        !AuthorSharedMesh(stage, 0.0F, &error) ||
        !AuthorTopologyMesh(stage, &error) ||
        !AuthorBasisCurves(stage, &error) ||
        !AuthorLinkedLighting(stage, &error) ||
        !AuthorSharedMaterial(stage, &error))
    {
        openusd_stage_release(stage);
        std::cerr << "Stage setup failed: " << errorText.data() << "\n";
        return 4;
    }

    const size_t retainedStageCoreCount = openusd_test_get_live_stage_core_count();
    SetEnvironment("OPENUSD_RENDERER_CREATE_FAILPOINT", "after-retain");
    openusd_silk_session* failedSession =
        reinterpret_cast<openusd_silk_session*>(1);
    const openusd_status failedCreateStatus =
        openusd_silk_session_create_from_stage(argv[1], stage, &failedSession, &error);
    SetEnvironment("OPENUSD_RENDERER_CREATE_FAILPOINT", "");
    if (failedCreateStatus == OPENUSD_STATUS_OK ||
        failedSession != nullptr ||
        openusd_test_get_live_stage_core_count() != retainedStageCoreCount)
    {
        openusd_stage_release(stage);
        openusd_stage_release(stage);
        std::cerr << "hdSilk after-retain failpoint did not roll back cleanly.\n";
        return 5;
    }

    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create_from_stage(
            argv[1], stage, &session, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        openusd_stage_release(stage);
        std::cerr << "session_create_from_stage failed: " << errorText.data() << "\n";
        return 6;
    }
    openusd_stage_release(stage);

    SetEnvironment("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "after-retain");
    const openusd_status failedDestroyStatus =
        openusd_silk_session_destroy(session, &error);
    SetEnvironment("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "");
    if (failedDestroyStatus == OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk destroy ignored the stage-access failpoint.\n";
        return 7;
    }

    ParsedPage initial;
    if (Sync(session, &initial, &error) != OPENUSD_STATUS_OK ||
        !initial.found_shared_upsert ||
        !initial.found_topology_upsert ||
        initial.shared_first_x != 0.0F ||
        !initial.mesh_identity_valid ||
        !initial.instance_fields_zero ||
        initial.shared_stable_hash != ComputeStableHash(SharedMeshPath) ||
        initial.shared_prim_id < 0 ||
        initial.shared_topology_kind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
        initial.shared_topology_revision != 1 ||
        initial.shared_triangle_count != 1 ||
        initial.shared_subprims != std::vector<uint32_t>{0} ||
        initial.topology_subprims != std::vector<uint32_t>({0, 0, 1}) ||
        !VerifyBasisCurvesTessellation(initial) ||
        !VerifyVaryingWidthCurves(initial) ||
        !VerifyUniformWidthCurves(initial) ||
        !VerifyOrientationWinding(initial) ||
        !VerifyStrayPointsAreNotPickTargets(initial) ||
        !VerifyCurveWidthResolution() ||
        !std::is_sorted(
            initial.upsert_paths.begin(),
            initial.upsert_paths.end(),
            Utf8PathLess))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Unsaved shared-stage mesh was not observed: " << errorText.data() << "\n";
        return 5;
    }
    if (!IsLegacyAutomaticFrame(initial))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr
            << "Automatic hdSilk camera changed from the legacy frame: "
            << initial.frame_width << 'x' << initial.frame_height
            << " view[0]=" << initial.frame_view[0]
            << " projection[0]=" << initial.frame_projection[0] << "\n";
        return 5;
    }

    // The authored collection:lightLink:excludes must have travelled all the way
    // from the stage, through UsdImaging's collection cache and Hydra's prim
    // categories, into a sparse LIGHT_LINK table naming exactly the excluded
    // prim. The table is default-free, so the included mesh must be absent. The
    // stage authors no shadow collection, so the excluded prim keeps its shadow
    // bit: the two collections are resolved independently and an unlit prim still
    // casts. The stage authors the same exclusion on a DomeLight, so the excluded
    // prim must also lose its dome bit -- which is the end to end evidence that a
    // dome collection resolves through the same path a direct light's does.
    {
        const bool hasExcludedEntry = std::any_of(
            initial.light_links.begin(),
            initial.light_links.end(),
            [](const std::tuple<std::string, int32_t, uint32_t, uint32_t, uint32_t>& entry)
            {
                return std::get<0>(entry) == LinkedUnlitMeshPath &&
                    std::get<1>(entry) == OPENUSD_SILK_LINK_ALL_INSTANCES &&
                    std::get<2>(entry) == 0u &&
                    std::get<3>(entry) == 1u &&
                    std::get<4>(entry) == 0u;
            });
        const bool hasIncludedEntry = std::any_of(
            initial.light_links.begin(),
            initial.light_links.end(),
            [](const std::tuple<std::string, int32_t, uint32_t, uint32_t, uint32_t>& entry)
            {
                return std::get<0>(entry) == LinkedLitMeshPath;
            });
        if (!initial.light_link_valid ||
            initial.light_link_count != 1 ||
            initial.light_link_light_count != 1 ||
            initial.light_link_dome_count != 1 ||
            initial.frame_dome_count != 1 ||
            initial.light_link_unsupported !=
                OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE ||
            !hasExcludedEntry ||
            hasIncludedEntry)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr
                << "Authored UsdLux light linking did not reach the page: commands="
                << initial.light_link_count
                << " frameLights=" << initial.frame_light_count
                << " lights=" << initial.light_link_light_count
                << " entries=" << initial.light_links.size() << "\n";
            for (const auto& entry : initial.light_links)
            {
                std::cerr << "  " << std::get<0>(entry)
                    << " instance=" << std::get<1>(entry)
                    << " light=" << std::get<2>(entry)
                    << " shadow=" << std::get<3>(entry)
                    << " dome=" << std::get<4>(entry) << "\n";
            }
            return 5;
        }
    }
    // Medium complexity halves every emitted line segment. The subdivided
    // vertices must carry attributes interpolated at the same parameter as the
    // position, so the authored width ramp stays a ramp instead of collapsing
    // into a step function at the original endpoints.
    //
    // Complexity also selects a mesh refinement level, which changes the
    // emitted triangle topology of every subdivision surface and therefore its
    // published topology revision. The revision the restored Low sync leaves
    // behind is captured here so the live-edit check below can compare across
    // the points edit it is actually asserting about rather than across this
    // round trip.
    uint64_t restoredComplexityRevision = 0;
    {
        const uint32_t mediumComplexity = OPENUSD_SILK_COMPLEXITY_MEDIUM;
        const uint32_t lowComplexity = OPENUSD_SILK_COMPLEXITY_LOW;
        ParsedPage medium;
        ParsedPage low;
        const bool mediumObserved =
            Sync(session, &medium, &error, nullptr, 0.0, &mediumComplexity) ==
                OPENUSD_STATUS_OK &&
            VerifyMediumComplexityInterpolatesWidths(medium);
        const bool lowRestored =
            Sync(session, &low, &error, nullptr, 0.0, &lowComplexity) ==
                OPENUSD_STATUS_OK &&
            VerifyVaryingWidthCurves(low) &&
            VerifyUniformWidthCurves(low);
        restoredComplexityRevision = low.shared_topology_revision;
        if (!mediumObserved || !lowRestored)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr
                << "hdSilk medium complexity did not interpolate vertex widths: "
                << "mediumTriangles=" << medium.varying_width_curves.triangle_count
                << " lowTriangles=" << low.varying_width_curves.triangle_count
                << "\n";
            return 5;
        }
    }
    // ABI v5: the bound UsdPreviewSurface must arrive resolved, and the mesh must
    // reference it by path. Asserting both together is what proves the material
    // Sprim, the binding, and the wire agree, rather than each in isolation.
    // ABI v13 adds the connected output channel: diffuseColor and metallic are
    // wired to two different outputs of the same UsdUVTexture prim, so nothing
    // but the published channel can tell those two entries apart.
    bool packedChannelsPublished = initial.material_textures.size() == 2;
    if (packedChannelsPublished)
    {
        const auto& diffuseEntry = initial.material_textures[0];
        const auto& metallicEntry = initial.material_textures[1];
        packedChannelsPublished =
            std::get<0>(diffuseEntry) == OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR &&
            std::get<1>(diffuseEntry) == OPENUSD_SILK_TEXTURE_CHANNEL_RGB &&
            std::get<0>(metallicEntry) == OPENUSD_SILK_MATERIAL_METALLIC &&
            std::get<1>(metallicEntry) == OPENUSD_SILK_TEXTURE_CHANNEL_B &&
            std::get<2>(diffuseEntry) == std::get<2>(metallicEntry) &&
            std::get<2>(diffuseEntry).find(MaterialTextureAsset) !=
                std::string::npos;
    }
    if (!initial.material_valid ||
        !initial.found_material_upsert ||
        initial.material_upsert_count != 1 ||
        initial.material_path != MaterialPath ||
        initial.material_surface_kind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        initial.material_scalar_count != 1 ||
        initial.material_roughness != 0.375F ||
        initial.material_texture_count != 2 ||
        initial.material_texture_parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        initial.material_texture_asset.find(MaterialTextureAsset) ==
            std::string::npos ||
        !packedChannelsPublished ||
        initial.shared_material_binding != MaterialPath)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr
            << "hdSilk material was not published as expected: path='"
            << initial.material_path << "' kind=" << initial.material_surface_kind
            << " scalars=" << initial.material_scalar_count
            << " roughness=" << initial.material_roughness
            << " textures=" << initial.material_texture_count
            << " asset='" << initial.material_texture_asset
            << "' channels=";
        for (const auto& entry : initial.material_textures)
        {
            std::cerr << '(' << std::get<0>(entry) << ':' << std::get<1>(entry) << ')';
        }
        std::cerr << " binding='" << initial.shared_material_binding << "'\n";
        return 5;
    }

    // Cull style must arrive as BACK_UNLESS_DOUBLE_SIDED, the usdview default
    // hdSilk declares on its render params. UsdImagingGLRenderParams otherwise
    // defaults to CULL_STYLE_NOTHING, and UsdImaging reports that as the
    // per-prim value because USD gprims author doubleSided rather than a Hydra
    // cull style. That made the renderer resolve "do not cull" for every mesh
    // and silently stop honoring authored single-sidedness. A range check
    // cannot catch this, because NOTHING is a legal value.
    for (uint32_t cullStyle : initial.mesh_cull_styles)
    {
        if (cullStyle != OPENUSD_SILK_CULL_STYLE_BACK_UNLESS_DOUBLE_SIDED)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr
                << "hdSilk published cull style " << cullStyle
                << " but the render params policy requires "
                << OPENUSD_SILK_CULL_STYLE_BACK_UNLESS_DOUBLE_SIDED
                << " so authored doubleSided is honored.\n";
            return 5;
        }
    }
    if (initial.mesh_cull_styles.empty())
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk published no meshes, so the cull style policy "
                     "check proved nothing.\n";
        return 5;
    }

    // ABI v4 attribute table: the stage authors texture coordinates, arbitrary
    // named primvars, a constant primvar, and face-varying normals. All must
    // arrive resolved onto the emitted vertices. Without this, nothing proves
    // texture coordinates or authored normals reach a consumer at all.
    if (!initial.found_primvar_mesh ||
        !VerifyPrimvarAttributes(initial.primvar_mesh_attributes))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk primvar attributes were not published as expected; got "
                  << initial.primvar_mesh_attributes.size() << " attribute(s):";
        for (const ParsedAttribute& attribute : initial.primvar_mesh_attributes)
        {
            std::cerr << " {" << attribute.name
                      << " semantic=" << attribute.semantic
                      << " components=" << attribute.componentCount
                      << " interpolation=" << attribute.interpolation
                      << " elements=" << attribute.elementCount
                      << " first=" << attribute.firstValue << "}";
        }
        std::cerr << "\n";
        return 5;
    }

    if (openusd_geom_imageable_set_visibility(
            stage,
            PrimvarMeshPath,
            OPENUSD_GEOM_VISIBILITY_INVISIBLE,
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Primvar mesh visibility edit failed: "
                  << errorText.data() << "\n";
        return 5;
    }
    ParsedPage hidden;
    if (Sync(session, &hidden, &error) != OPENUSD_STATUS_OK ||
        std::find(
            hidden.remove_paths.begin(),
            hidden.remove_paths.end(),
            PrimvarMeshPath) == hidden.remove_paths.end())
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Invisible hdSilk mesh did not emit removal.\n";
        return 5;
    }
    if (openusd_geom_imageable_set_visibility(
            stage,
            PrimvarMeshPath,
            OPENUSD_GEOM_VISIBILITY_INHERITED,
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Primvar mesh visibility restore failed: "
                  << errorText.data() << "\n";
        return 5;
    }
    ParsedPage visible;
    if (Sync(session, &visible, &error) != OPENUSD_STATUS_OK ||
        !visible.found_primvar_mesh ||
        !VerifyPrimvarAttributes(visible.primvar_mesh_attributes))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Restored visible hdSilk mesh did not republish attributes.\n";
        return 5;
    }

    const openusd_render_camera explicitCamera = ExplicitCamera();
    ParsedPage explicitPage;
    if (Sync(session, &explicitPage, &error, &explicitCamera) !=
            OPENUSD_STATUS_OK ||
        !IsExplicitFrame(explicitPage, explicitCamera) ||
        explicitPage.mesh_upsert_count != 0 ||
        !VerifyCameraValidation(session, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk camera ABI validation failed: "
                  << errorText.data() << "\n";
        return 6;
    }

    if (!VerifyConcurrentSessions(argv[1], stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << errorText.data() << "\n";
        return 6;
    }
    if (!VerifyDestroyRace(argv[1], stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Queued Sync/name destruction race failed: "
                  << errorText.data() << "\n";
        return 7;
    }
    if (!VerifyNeverReusedHandles(argv[1], stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk token reuse/stale-handle soak failed: "
                  << errorText.data() << "\n";
        return 8;
    }

    if (!EditSharedMeshPoints(stage, 2.0F, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh edit failed: " << errorText.data() << "\n";
        return 7;
    }
    ParsedPage edited;
    if (Sync(session, &edited, &error) != OPENUSD_STATUS_OK ||
        !edited.found_shared_upsert ||
        edited.shared_first_x != 2.0F ||
        edited.shared_topology_revision != restoredComplexityRevision ||
        edited.shared_subprims != initial.shared_subprims ||
        !edited.instance_fields_zero)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh edit was not observed: " << errorText.data() << "\n";
        return 8;
    }

    if (!EditSharedMeshTopologyToQuad(stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh topology edit failed: " << errorText.data() << "\n";
        return 9;
    }
    ParsedPage topologyEdited;
    if (Sync(session, &topologyEdited, &error) != OPENUSD_STATUS_OK ||
        !topologyEdited.found_shared_upsert ||
        topologyEdited.shared_topology_revision <=
            edited.shared_topology_revision ||
        topologyEdited.shared_triangle_count != 2 ||
        topologyEdited.shared_subprims != std::vector<uint32_t>({0, 0}) ||
        !topologyEdited.instance_fields_zero)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh topology identity was not updated: "
                  << errorText.data() << "\n";
        return 9;
    }

    // Expanding the topology so a uniform primvar can be resolved onto corners
    // -- and collapsing it again when that primvar stops resolving -- rewrites
    // the emitted points, the indices and every subprim-identity table with
    // them, even though Hydra reports only a primvar change. The published
    // topology revision has to advance in BOTH directions, or a consumer that
    // keys retained geometry on it keeps a vertex buffer the toggle
    // invalidated. The toggle mesh time samples its only uniform primvar so
    // that it resolves at time 0 and cannot resolve at time 1, which moves the
    // expansion without touching the topology.
    {
        ParsedPage collapsedPage;
        if (Sync(session, &collapsedPage, &error, nullptr, 1.0) != OPENUSD_STATUS_OK ||
            !collapsedPage.expanded_topology_toggle_mesh.found ||
            collapsedPage.expanded_topology_toggle_mesh.points.size() != 12)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr << "Collapsing the expanded topology did not publish the "
                         "unexpanded points. pointFloats="
                      << collapsedPage.expanded_topology_toggle_mesh.points.size()
                      << " found="
                      << collapsedPage.expanded_topology_toggle_mesh.found
                      << "\n";
            return 9;
        }

        ParsedPage reexpandedPage;
        if (Sync(session, &reexpandedPage, &error, nullptr, 0.0) != OPENUSD_STATUS_OK ||
            !reexpandedPage.expanded_topology_toggle_mesh.found ||
            reexpandedPage.expanded_topology_toggle_mesh.points.size() != 18 ||
            reexpandedPage.expanded_topology_toggle_mesh.topology_revision <=
                collapsedPage.expanded_topology_toggle_mesh.topology_revision)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr << "Re-expanding the topology did not advance the "
                         "published topology revision. pointFloats="
                      << reexpandedPage.expanded_topology_toggle_mesh.points.size()
                      << " revision="
                      << reexpandedPage.expanded_topology_toggle_mesh.topology_revision
                      << " previous="
                      << collapsedPage.expanded_topology_toggle_mesh.topology_revision
                      << "\n";
            return 9;
        }

        ParsedPage recollapsedPage;
        if (Sync(session, &recollapsedPage, &error, nullptr, 1.0) != OPENUSD_STATUS_OK ||
            !recollapsedPage.expanded_topology_toggle_mesh.found ||
            recollapsedPage.expanded_topology_toggle_mesh.points.size() != 12 ||
            recollapsedPage.expanded_topology_toggle_mesh.topology_revision <=
                reexpandedPage.expanded_topology_toggle_mesh.topology_revision)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr << "Collapsing the topology again did not advance the "
                         "published topology revision. pointFloats="
                      << recollapsedPage.expanded_topology_toggle_mesh.points.size()
                      << " revision="
                      << recollapsedPage.expanded_topology_toggle_mesh.topology_revision
                      << " previous="
                      << reexpandedPage.expanded_topology_toggle_mesh.topology_revision
                      << "\n";
            return 9;
        }
    }

    if (openusd_stage_remove_prim(stage, SharedMeshPath, &error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh removal failed: " << errorText.data() << "\n";
        return 9;
    }
    openusd_stage_release(stage);

    ParsedPage removed;
    if (Sync(session, &removed, &error) != OPENUSD_STATUS_OK ||
        !removed.found_shared_remove)
    {
        openusd_silk_session_release(session);
        std::cerr << "Live mesh removal was not observed: " << errorText.data() << "\n";
        return 10;
    }

    for (int index = 0; index < 16; ++index)
    {
        openusd_silk_session* rapidSession = nullptr;
        if (openusd_silk_session_create(argv[1], argv[2], &rapidSession, &error) !=
            OPENUSD_STATUS_OK)
        {
            openusd_silk_session_release(session);
            std::cerr << "Rapid session creation failed: " << errorText.data() << "\n";
            return 11;
        }
        if (index == 0)
        {
            ParsedPage pathPage;
            if (Sync(rapidSession, &pathPage, &error) != OPENUSD_STATUS_OK ||
                pathPage.frame_count != 1)
            {
                openusd_silk_session_release(rapidSession);
                openusd_silk_session_release(session);
                std::cerr << "Path session failed after temporary stage release: "
                          << errorText.data() << "\n";
                return 12;
            }
        }
        openusd_silk_session_release(rapidSession);
    }

    if (openusd_silk_session_destroy(session, &error) != OPENUSD_STATUS_OK)
    {
        std::cerr << "Checked hdSilk destruction failed: " << errorText.data() << "\n";
        return 12;
    }
    if (openusd_test_get_live_stage_core_count() != initialStageCoreCount)
    {
        std::cerr << "hdSilk probe leaked a stage core.\n";
        return 13;
    }
    std::cout << "OK: exact-stage retention, unsaved/live edits, concurrent and rapid sessions\n";
    return 0;
}
