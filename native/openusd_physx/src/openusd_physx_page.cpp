// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_page.h"

#include "openusd_physx_support.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <string>
#include <unordered_set>

namespace
{
using openusd_physx_support::ComputeIdentity;
using openusd_physx_support::IsFinite;
using openusd_physx_support::IsNonNegativeFinite;
using openusd_physx_support::IsPositiveFinite;
using openusd_physx_support::IsUsableRotation;
using openusd_physx_support::IsUnsetOrUsableRotation;
using openusd_physx_support::IsValidUtf8;
using openusd_physx_support::WriteError;

constexpr uint32_t kMaxRecords = 1u << 22;
constexpr uint32_t kMaxMeshPoints = 1u << 26;
constexpr uint32_t kMaxMeshIndices = 1u << 27;
constexpr size_t kSectionCount = 25;

struct Context
{
    openusd_physx_page_validation* validation = nullptr;
    openusd_physx_error_buffer* error = nullptr;
};

openusd_physx_status Fail(
    const Context& context,
    openusd_physx_page_error code,
    openusd_physx_page_section section,
    uint32_t element_index,
    uint64_t byte_offset,
    const std::string& message)
{
    if (context.validation != nullptr)
    {
        context.validation->error_code = static_cast<uint32_t>(code);
        context.validation->section = static_cast<uint32_t>(section);
        context.validation->element_index = element_index;
        context.validation->byte_offset = byte_offset;
    }
    WriteError(context.error, message);
    return OPENUSD_PHYSX_STATUS_INVALID_PAGE;
}

std::string Describe(openusd_physx_page_section section, uint32_t index)
{
    static const char* const names[] = {
        "header",
        "strings",
        "identities",
        "scenes",
        "materials",
        "shapes",
        "actors",
        "actor shapes",
        "joints",
        "filter pairs",
        "mesh points",
        "mesh indices",
        "capacities",
        "height field samples",
        "articulations",
        "articulation links",
        "controllers",
        "articulation tendons",
        "articulation tendon nodes",
        "articulation mimic joints",
        "vehicles",
        "vehicle wheels",
        "particle materials",
        "particle systems",
        "particle bodies",
        "deformable materials",
        "deformables"};
    const size_t position = static_cast<size_t>(section);
    const char* name = position < (sizeof(names) / sizeof(names[0])) ? names[position] : "unknown";
    return std::string(name) + " record " + std::to_string(index);
}

struct SectionRange
{
    uint64_t offset = 0;
    uint64_t bytes = 0;
    openusd_physx_page_section section = OPENUSD_PHYSX_SECTION_HEADER;
};

openusd_physx_status CheckSpan(
    const Context& context,
    const openusd_physx_page_span& span,
    size_t stride,
    uint32_t max_count,
    openusd_physx_page_section section,
    uint64_t page_bytes,
    uint32_t header_size,
    SectionRange* range)
{
    if (span.count == 0)
    {
        if (span.offset != 0)
        {
            return Fail(
                context,
                OPENUSD_PHYSX_PAGE_ERROR_RANGE,
                section,
                0,
                span.offset,
                "An empty page section must declare a zero byte offset.");
        }
        *range = SectionRange{0, 0, section};
        return OPENUSD_PHYSX_STATUS_OK;
    }
    if (span.count > max_count)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_COUNT_LIMIT,
            section,
            span.count,
            span.offset,
            "A page section exceeds the supported element count.");
    }
    if ((span.offset % OPENUSD_PHYSX_PAGE_ALIGNMENT) != 0)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_ALIGNMENT,
            section,
            0,
            span.offset,
            "A page section offset is not eight byte aligned.");
    }
    if (span.offset < header_size)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_RANGE,
            section,
            0,
            span.offset,
            "A page section overlaps the page header.");
    }
    const uint64_t bytes = static_cast<uint64_t>(span.count) * static_cast<uint64_t>(stride);
    if (bytes > page_bytes || static_cast<uint64_t>(span.offset) + bytes > page_bytes)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_RANGE,
            section,
            span.count,
            span.offset,
            "A page section extends past the end of the page.");
    }
    *range = SectionRange{span.offset, bytes, section};
    return OPENUSD_PHYSX_STATUS_OK;
}

bool IsFiniteNonNegative(float value) noexcept
{
    return IsNonNegativeFinite(value);
}
}

namespace openusd_physx_page
{
openusd_physx_status Validate(
    const void* page,
    size_t page_size,
    openusd_physx_page_validation* validation,
    View* view,
    openusd_physx_error_buffer* error)
{
    Context context{validation, error};
    if (view != nullptr)
    {
        *view = View();
    }
    if (validation != nullptr)
    {
        if (validation->struct_size != sizeof(openusd_physx_page_validation))
        {
            WriteError(error, "The page validation structure size does not match this ABI.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        const uint32_t struct_size = validation->struct_size;
        *validation = openusd_physx_page_validation{};
        validation->struct_size = struct_size;
    }
    if (page == nullptr)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_NULL, OPENUSD_PHYSX_SECTION_HEADER, 0, 0, "The build page is null.");
    }
    if ((reinterpret_cast<uintptr_t>(page) % OPENUSD_PHYSX_PAGE_ALIGNMENT) != 0)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_ALIGNMENT,
            OPENUSD_PHYSX_SECTION_HEADER,
            0,
            0,
            "The build page must start on an eight byte boundary.");
    }
    if (page_size < sizeof(openusd_physx_build_page_header) || page_size > OPENUSD_PHYSX_PAGE_MAX_BYTES)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_SIZE,
            OPENUSD_PHYSX_SECTION_HEADER,
            0,
            static_cast<uint64_t>(page_size),
            "The build page size is smaller than the header or larger than the supported maximum.");
    }

    const unsigned char* base = static_cast<const unsigned char*>(page);
    openusd_physx_build_page_header header{};
    std::memcpy(&header, base, sizeof(header));

    if (header.magic != OPENUSD_PHYSX_PAGE_MAGIC)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_MAGIC, OPENUSD_PHYSX_SECTION_HEADER, 0, 0, "The build page magic is wrong.");
    }
    if (header.abi_version != OPENUSD_PHYSX_WORLD_ABI_VERSION)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_ABI,
            OPENUSD_PHYSX_SECTION_HEADER,
            header.abi_version,
            8,
            "The build page requires an exact ABI version match.");
    }
    if (header.header_size != sizeof(openusd_physx_build_page_header))
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_HEADER_SIZE,
            OPENUSD_PHYSX_SECTION_HEADER,
            header.header_size,
            12,
            "The build page header size does not match this ABI.");
    }
    if (header.byte_size != static_cast<uint64_t>(page_size))
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_SIZE,
            OPENUSD_PHYSX_SECTION_HEADER,
            0,
            header.byte_size,
            "The build page declares a byte size that differs from the supplied buffer size.");
    }
    if (!(std::isfinite(header.meters_per_unit) && header.meters_per_unit > 0.0) ||
        !(std::isfinite(header.kilograms_per_unit) && header.kilograms_per_unit > 0.0))
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_VALUE,
            OPENUSD_PHYSX_SECTION_HEADER,
            0,
            40,
            "The build page requires positive finite unit scales.");
    }
    if (!(std::isfinite(header.time_codes_per_second) && header.time_codes_per_second > 0.0) ||
        !std::isfinite(header.start_time_code) || !std::isfinite(header.end_time_code) ||
        header.end_time_code < header.start_time_code)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_VALUE,
            OPENUSD_PHYSX_SECTION_HEADER,
            0,
            56,
            "The build page requires a positive time code rate and an ordered finite time range.");
    }
    if (header.up_axis >= 3)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER, header.up_axis, 80, "The build page up axis is out of range.");
    }
    if (header.simulation_rate_hz < OPENUSD_PHYSX_MIN_SIMULATION_RATE_HZ ||
        header.simulation_rate_hz > OPENUSD_PHYSX_MAX_SIMULATION_RATE_HZ)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_VALUE,
            OPENUSD_PHYSX_SECTION_HEADER,
            header.simulation_rate_hz,
            88,
            "The build page simulation rate must be between 24 and 240 hertz.");
    }
    if (header.max_substeps == 0 || header.max_substeps > OPENUSD_PHYSX_MAX_SUBSTEPS)
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_VALUE,
            OPENUSD_PHYSX_SECTION_HEADER,
            header.max_substeps,
            92,
            "The build page substep limit must be between one and sixty four.");
    }
    for (size_t index = 0; index < 3; ++index)
    {
        if (header.reserved[index] != 0)
        {
            return Fail(
                context,
                OPENUSD_PHYSX_PAGE_ERROR_VALUE,
                OPENUSD_PHYSX_SECTION_HEADER,
                static_cast<uint32_t>(index),
                offsetof(openusd_physx_build_page_header, reserved),
                "Reserved build page header fields must be zero.");
        }
    }

    const uint64_t page_bytes = header.byte_size;
    const uint32_t header_size = header.header_size;
    std::array<SectionRange, kSectionCount> ranges{};
    size_t range_count = 0;

    const struct
    {
        const openusd_physx_page_span* span;
        size_t stride;
        uint32_t max_count;
        openusd_physx_page_section section;
    } span_rules[kSectionCount] = {
        {&header.string_bytes, 1, static_cast<uint32_t>(OPENUSD_PHYSX_PAGE_MAX_BYTES - 1), OPENUSD_PHYSX_SECTION_STRINGS},
        {&header.identities, sizeof(openusd_physx_identity), kMaxRecords, OPENUSD_PHYSX_SECTION_IDENTITIES},
        {&header.scenes, sizeof(openusd_physx_scene_desc), OPENUSD_PHYSX_MAX_SCENES, OPENUSD_PHYSX_SECTION_SCENES},
        {&header.materials, sizeof(openusd_physx_material_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_MATERIALS},
        {&header.shapes, sizeof(openusd_physx_shape_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_SHAPES},
        {&header.actors, sizeof(openusd_physx_actor_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_ACTORS},
        {&header.actor_shapes, sizeof(openusd_physx_actor_shape_ref), kMaxRecords, OPENUSD_PHYSX_SECTION_ACTOR_SHAPES},
        {&header.joints, sizeof(openusd_physx_joint_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_JOINTS},
        {&header.filter_pairs, sizeof(openusd_physx_filter_pair), kMaxRecords, OPENUSD_PHYSX_SECTION_FILTER_PAIRS},
        {&header.mesh_points, sizeof(openusd_physx_vec3f), kMaxMeshPoints, OPENUSD_PHYSX_SECTION_MESH_POINTS},
        {&header.mesh_indices, sizeof(uint32_t), kMaxMeshIndices, OPENUSD_PHYSX_SECTION_MESH_INDICES},
        {&header.heightfield_samples, sizeof(openusd_physx_heightfield_sample), kMaxMeshPoints, OPENUSD_PHYSX_SECTION_HEIGHTFIELD_SAMPLES},
        {&header.articulations, sizeof(openusd_physx_articulation_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_ARTICULATIONS},
        {&header.articulation_links, sizeof(openusd_physx_articulation_link_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS},
        {&header.controllers, sizeof(openusd_physx_controller_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_CONTROLLERS},
        {&header.articulation_tendons, sizeof(openusd_physx_tendon_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS},
        {&header.articulation_tendon_nodes, sizeof(openusd_physx_tendon_node_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES},
        {&header.articulation_mimic_joints, sizeof(openusd_physx_mimic_joint_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS},
        {&header.vehicles, sizeof(openusd_physx_vehicle_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_VEHICLES},
        {&header.vehicle_wheels, sizeof(openusd_physx_vehicle_wheel_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS},
        {&header.particle_materials, sizeof(openusd_physx_particle_material_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS},
        {&header.particle_systems, sizeof(openusd_physx_particle_system_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS},
        {&header.particle_bodies, sizeof(openusd_physx_particle_body_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES},
        {&header.deformable_materials, sizeof(openusd_physx_deformable_material_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS},
        {&header.deformables, sizeof(openusd_physx_deformable_desc), kMaxRecords, OPENUSD_PHYSX_SECTION_DEFORMABLES}};

    for (const auto& rule : span_rules)
    {
        SectionRange range{};
        const openusd_physx_status status = CheckSpan(
            context,
            *rule.span,
            rule.stride,
            rule.max_count,
            rule.section,
            page_bytes,
            header_size,
            &range);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (range.bytes != 0)
        {
            ranges[range_count] = range;
            ++range_count;
        }
    }

    std::sort(
        ranges.begin(),
        ranges.begin() + static_cast<std::ptrdiff_t>(range_count),
        [](const SectionRange& first, const SectionRange& second)
        {
            return first.offset < second.offset;
        });
    for (size_t index = 1; index < range_count; ++index)
    {
        const SectionRange& previous = ranges[index - 1];
        const SectionRange& current = ranges[index];
        if (previous.offset + previous.bytes > current.offset)
        {
            return Fail(
                context,
                OPENUSD_PHYSX_PAGE_ERROR_OVERLAP,
                current.section,
                0,
                current.offset,
                "Two build page sections overlap.");
        }
    }

    const View page_view(base, page_size, header);

    if (header.string_bytes.count != 0 &&
        !IsValidUtf8(base + header.string_bytes.offset, header.string_bytes.count))
    {
        return Fail(
            context,
            OPENUSD_PHYSX_PAGE_ERROR_ENCODING,
            OPENUSD_PHYSX_SECTION_STRINGS,
            0,
            header.string_bytes.offset,
            "The build page string section is not valid UTF-8 without embedded null bytes.");
    }

    std::unordered_set<uint64_t> identifiers;
    identifiers.reserve(header.identities.count);
    for (uint32_t index = 0; index < header.identities.count; ++index)
    {
        const openusd_physx_identity identity = page_view.Get<openusd_physx_identity>(header.identities, index);
        if (identity.id == OPENUSD_PHYSX_INVALID_ID)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_IDENTITIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_IDENTITIES, index) + " uses the reserved zero identity.");
        }
        if (identity.instance_domain >= static_cast<uint32_t>(OPENUSD_PHYSX_INSTANCE_DOMAIN_COUNT))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_IDENTITIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_IDENTITIES, index) + " declares an unknown instance domain.");
        }
        if (identity.path_length == 0 ||
            static_cast<uint64_t>(identity.path_offset) + identity.path_length > header.string_bytes.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_IDENTITIES, index, identity.path_offset, Describe(OPENUSD_PHYSX_SECTION_IDENTITIES, index) + " references a path outside the string section.");
        }
        const std::string_view path = page_view.String(identity.path_offset, identity.path_length);
        if (path.front() != '/')
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_IDENTITIES, index, identity.path_offset, Describe(OPENUSD_PHYSX_SECTION_IDENTITIES, index) + " does not reference an absolute prim path.");
        }
        if (identity.id !=
            ComputeIdentity(path.data(), path.size(), identity.instance_domain, identity.instance_index))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_IDENTITIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_IDENTITIES, index) + " is not derived from its path, instance domain, and instance index.");
        }
        if (!identifiers.insert(identity.id).second)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_IDENTITIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_IDENTITIES, index) + " collides with an earlier identity.");
        }
    }

    const auto require_identity = [&](const Context& fail_context, uint64_t id, openusd_physx_page_section section, uint32_t index)
    {
        return identifiers.find(id) != identifiers.end()
            ? OPENUSD_PHYSX_STATUS_OK
            : Fail(fail_context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, section, index, 0, Describe(section, index) + " uses an identity that is missing from the identity table.");
    };

    for (uint32_t index = 0; index < header.scenes.count; ++index)
    {
        const openusd_physx_scene_desc scene = page_view.Get<openusd_physx_scene_desc>(header.scenes, index);
        const openusd_physx_status status = require_identity(context, scene.id, OPENUSD_PHYSX_SECTION_SCENES, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (!IsFinite(scene.gravity_direction) || !IsFiniteNonNegative(scene.gravity_magnitude) ||
            !IsFiniteNonNegative(scene.bounce_threshold) || !IsPositiveFinite(scene.contact_offset))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SCENES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SCENES, index) + " declares a non finite or negative simulation value.");
        }
        if (scene.gravity_magnitude > 0.0F &&
            scene.gravity_direction.x == 0.0F && scene.gravity_direction.y == 0.0F && scene.gravity_direction.z == 0.0F)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SCENES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SCENES, index) + " declares gravity without a direction.");
        }
        if (scene.position_iterations == 0 || scene.position_iterations > 255 ||
            scene.velocity_iterations == 0 || scene.velocity_iterations > 255)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SCENES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SCENES, index) + " declares solver iteration counts outside one to two hundred fifty five.");
        }
        if (scene.reserved0 != 0 || (scene.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_SCENE_FLAG_ALL)) != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SCENES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SCENES, index) + " declares unknown flags or must leave reserved fields zero.");
        }
    }

    for (uint32_t index = 0; index < header.materials.count; ++index)
    {
        const openusd_physx_material_desc material = page_view.Get<openusd_physx_material_desc>(header.materials, index);
        const openusd_physx_status status = require_identity(context, material.id, OPENUSD_PHYSX_SECTION_MATERIALS, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (!openusd_physx_support::IsValidMaterial(material.static_friction, material.dynamic_friction, material.restitution) ||
            !IsPositiveFinite(material.density))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_MATERIALS, index) + " declares friction, restitution, or density outside the supported range.");
        }
        if (material.flags != 0 && (material.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_MATERIAL_FLAG_ALL)) != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_MATERIALS, index) + " declares unknown flags.");
        }
        if (material.friction_combine_mode >= static_cast<uint32_t>(OPENUSD_PHYSX_COMBINE_MODE_COUNT) ||
            material.restitution_combine_mode >= static_cast<uint32_t>(OPENUSD_PHYSX_COMBINE_MODE_COUNT))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_MATERIALS, index) + " declares an unknown friction or restitution combine mode.");
        }
        if (!IsFiniteNonNegative(material.damping))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_MATERIALS, index) + " declares a non finite or negative contact damping.");
        }
    }

    for (uint32_t index = 0; index < header.shapes.count; ++index)
    {
        const openusd_physx_shape_desc shape = page_view.Get<openusd_physx_shape_desc>(header.shapes, index);
        const openusd_physx_status status = require_identity(context, shape.id, OPENUSD_PHYSX_SECTION_SHAPES, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (shape.type >= static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_TYPE_COUNT) ||
            (shape.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_FLAG_ALL)) != 0 ||
            shape.reserved0 != 0 || shape.reserved1 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " declares an unknown type, unknown flags, or non zero reserved fields.");
        }
        if (!openusd_physx_support::IsFinite(shape.local_pose) || !IsUsableRotation(shape.local_pose.rotation) ||
            !IsPositiveFinite(shape.scale.x) || !IsPositiveFinite(shape.scale.y) || !IsPositiveFinite(shape.scale.z))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " declares a non finite local pose or a non positive scale.");
        }
        if (shape.material_index < -1 || shape.material_index >= static_cast<int32_t>(header.materials.count))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " references a material that does not exist.");
        }

        const bool is_mesh = shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_CONVEX_MESH) ||
            shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH);
        if (!is_mesh && (shape.point_count != 0 || shape.index_count != 0 || shape.point_offset != 0 || shape.index_offset != 0))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " is an analytic shape and must not reference mesh data.");
        }
        if (!IsFiniteNonNegative(shape.contact_offset) || !IsFiniteNonNegative(shape.rest_offset) ||
            !IsFiniteNonNegative(shape.torsional_patch_radius) || !IsFiniteNonNegative(shape.min_torsional_patch_radius))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " declares a non finite or negative contact, rest, or torsional patch offset.");
        }
        /* A rest offset at or above the contact offset means the solver would
         * separate the pair before it ever generates a contact. */
        if (shape.contact_offset > 0.0F && shape.rest_offset >= shape.contact_offset)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " declares a rest offset that is not below its contact offset.");
        }
        const bool is_heightfield = shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_HEIGHTFIELD);
        if (!is_heightfield &&
            (shape.sample_offset != 0 || shape.row_count != 0 || shape.column_count != 0 ||
             shape.height_scale != 0.0F || shape.row_scale != 0.0F || shape.column_scale != 0.0F))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " is not a height field and must leave the height field block zero.");
        }
        switch (shape.type)
        {
        case OPENUSD_PHYSX_SHAPE_SPHERE:
            if (!IsPositiveFinite(shape.radius))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " requires a positive radius.");
            }
            break;
        case OPENUSD_PHYSX_SHAPE_BOX:
            if (!IsPositiveFinite(shape.half_extents.x) || !IsPositiveFinite(shape.half_extents.y) ||
                !IsPositiveFinite(shape.half_extents.z))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " requires positive half extents.");
            }
            break;
        case OPENUSD_PHYSX_SHAPE_CAPSULE:
        case OPENUSD_PHYSX_SHAPE_CYLINDER:
        case OPENUSD_PHYSX_SHAPE_CONE:
            if (!IsPositiveFinite(shape.radius) || !IsPositiveFinite(shape.half_height))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " requires a positive radius and half height.");
            }
            break;
        case OPENUSD_PHYSX_SHAPE_HEIGHTFIELD:
        {
            /* A height field needs at least a two by two sample window,
             * because a single row or column describes no triangle. */
            if (shape.row_count < 2 || shape.column_count < 2)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " requires at least two height field rows and columns.");
            }
            const uint64_t sample_count = static_cast<uint64_t>(shape.row_count) * shape.column_count;
            if (static_cast<uint64_t>(shape.sample_offset) + sample_count > header.heightfield_samples.count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_SHAPES, index, shape.sample_offset, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " references height field samples outside the height field sample section.");
            }
            if (!IsPositiveFinite(shape.height_scale) || !IsPositiveFinite(shape.row_scale) ||
                !IsPositiveFinite(shape.column_scale))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " requires positive height, row, and column scales.");
            }
            break;
        }
        case OPENUSD_PHYSX_SHAPE_PLANE:
            break;
        case OPENUSD_PHYSX_SHAPE_CONVEX_MESH:
        case OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH:
        {
            const uint32_t minimum_points = shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_CONVEX_MESH) ? 4u : 3u;
            if (shape.point_count < minimum_points ||
                static_cast<uint64_t>(shape.point_offset) + shape.point_count > header.mesh_points.count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_SHAPES, index, shape.point_offset, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " references mesh points outside the mesh point section.");
            }
            if (static_cast<uint64_t>(shape.index_offset) + shape.index_count > header.mesh_indices.count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_SHAPES, index, shape.index_offset, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " references mesh indices outside the mesh index section.");
            }
            if (shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH) &&
                (shape.index_count < 3 || (shape.index_count % 3) != 0))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " requires a positive triangle index count that is a multiple of three.");
            }
            for (uint32_t offset = 0; offset < shape.index_count; ++offset)
            {
                const uint32_t value = page_view.Get<uint32_t>(header.mesh_indices, shape.index_offset + offset);
                if (value >= shape.point_count)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_MESH_INDICES, shape.index_offset + offset, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " references a vertex index outside its own point range.");
                }
            }
            break;
        }
        default:
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_SHAPES, index) + " declares an unknown shape type.");
        }
    }

    for (uint32_t index = 0; index < header.mesh_points.count; ++index)
    {
        const openusd_physx_vec3f point = page_view.Get<openusd_physx_vec3f>(header.mesh_points, index);
        if (!IsFinite(point))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_MESH_POINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_MESH_POINTS, index) + " is not finite.");
        }
    }

    for (uint32_t index = 0; index < header.actor_shapes.count; ++index)
    {
        const openusd_physx_actor_shape_ref reference = page_view.Get<openusd_physx_actor_shape_ref>(header.actor_shapes, index);
        if (reference.shape_index >= header.shapes.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ACTOR_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTOR_SHAPES, index) + " references a shape that does not exist.");
        }
        if (reference.material_index < -1 || reference.material_index >= static_cast<int32_t>(header.materials.count))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ACTOR_SHAPES, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTOR_SHAPES, index) + " references a material that does not exist.");
        }
    }

    uint32_t dynamic_actor_count = 0;
    for (uint32_t index = 0; index < header.actors.count; ++index)
    {
        const openusd_physx_actor_desc actor = page_view.Get<openusd_physx_actor_desc>(header.actors, index);
        const openusd_physx_status status = require_identity(context, actor.id, OPENUSD_PHYSX_SECTION_ACTORS, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (actor.type >= static_cast<uint32_t>(OPENUSD_PHYSX_ACTOR_TYPE_COUNT) ||
            (actor.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_ACTOR_FLAG_ALL)) != 0 ||
            actor.reserved0 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " declares an unknown type, unknown flags, or non zero reserved fields.");
        }
        if (actor.scene_index < 0 || actor.scene_index >= static_cast<int32_t>(header.scenes.count))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " is not owned by a scene in this page.");
        }
        if (!openusd_physx_support::IsFinite(actor.world_pose) || !IsUsableRotation(actor.world_pose.rotation) ||
            !IsFinite(actor.linear_velocity) || !IsFinite(actor.angular_velocity) ||
            !IsFinite(actor.center_of_mass) || !IsFinite(actor.inertia) ||
            !IsUnsetOrUsableRotation(actor.principal_axes) ||
            !IsFiniteNonNegative(actor.mass) || !IsFiniteNonNegative(actor.linear_damping) ||
            !IsFiniteNonNegative(actor.angular_damping) || !IsFiniteNonNegative(actor.inertia.x) ||
            !IsFiniteNonNegative(actor.inertia.y) || !IsFiniteNonNegative(actor.inertia.z))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " declares a non finite or negative rigid body value.");
        }
        if (actor.shape_count == 0 ||
            static_cast<uint64_t>(actor.shape_offset) + actor.shape_count > header.actor_shapes.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ACTORS, index, actor.shape_offset, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " must reference at least one shape inside the actor shape section.");
        }
        if (actor.collision_group >= OPENUSD_PHYSX_MAX_COLLISION_GROUPS)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " uses a collision group outside zero to thirty one.");
        }
        if (actor.position_iterations > 255 || actor.velocity_iterations > 255)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " declares more solver iterations than the world supports.");
        }
        if (!IsFiniteNonNegative(actor.max_linear_velocity) || !IsFiniteNonNegative(actor.max_angular_velocity) ||
            !IsFiniteNonNegative(actor.max_depenetration_velocity) || !IsFiniteNonNegative(actor.max_contact_impulse) ||
            !IsFiniteNonNegative(actor.stabilization_threshold) || !IsFiniteNonNegative(actor.wake_counter) ||
            !IsFiniteNonNegative(actor.sleep_threshold))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " declares a non finite or negative velocity, impulse, sleep, or wake budget.");
        }
        /* Both coefficients are fractions of a step, so a value above one has
         * no meaning the simulation could honour. */
        if (!IsFiniteNonNegative(actor.min_ccd_advance_coefficient) || actor.min_ccd_advance_coefficient > 1.0F ||
            !IsFiniteNonNegative(actor.contact_slop_coefficient))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " declares a continuous collision advance or contact slop coefficient outside the supported range.");
        }
        if (actor.type == static_cast<uint32_t>(OPENUSD_PHYSX_ACTOR_STATIC))
        {
            const bool has_motion = actor.linear_velocity.x != 0.0F || actor.linear_velocity.y != 0.0F ||
                actor.linear_velocity.z != 0.0F || actor.angular_velocity.x != 0.0F ||
                actor.angular_velocity.y != 0.0F || actor.angular_velocity.z != 0.0F;
            if (has_motion)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " is static and must not declare velocities.");
            }
        }
        else
        {
            ++dynamic_actor_count;
            for (uint32_t offset = 0; offset < actor.shape_count; ++offset)
            {
                const openusd_physx_actor_shape_ref reference =
                    page_view.Get<openusd_physx_actor_shape_ref>(header.actor_shapes, actor.shape_offset + offset);
                const openusd_physx_shape_desc shape = page_view.Get<openusd_physx_shape_desc>(header.shapes, reference.shape_index);
                if (shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_PLANE) ||
                    shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH) ||
                    shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_HEIGHTFIELD))
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " is movable and cannot use a plane, triangle mesh, or height field shape.");
                }
            }
        }
    }

    for (uint32_t index = 0; index < header.joints.count; ++index)
    {
        const openusd_physx_joint_desc joint = page_view.Get<openusd_physx_joint_desc>(header.joints, index);
        const openusd_physx_status status = require_identity(context, joint.id, OPENUSD_PHYSX_SECTION_JOINTS, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (joint.type >= static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_TYPE_COUNT) ||
            (joint.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_FLAG_ALL)) != 0 ||
            joint.axis >= 3 || joint.reserved0 != 0 || joint.reserved1 != 0 ||
            joint.reserved2 != 0 || joint.reserved3 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares an unknown type, axis, flags, or non zero reserved fields.");
        }
        if (joint.actor0_index < -1 || joint.actor0_index >= static_cast<int32_t>(header.actors.count) ||
            joint.actor1_index < -1 || joint.actor1_index >= static_cast<int32_t>(header.actors.count) ||
            (joint.actor0_index < 0 && joint.actor1_index < 0) ||
            (joint.actor0_index >= 0 && joint.actor0_index == joint.actor1_index))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " must reference one or two distinct actors from this page.");
        }
        if (!openusd_physx_support::IsFinite(joint.local_frame0) || !IsUsableRotation(joint.local_frame0.rotation) ||
            !openusd_physx_support::IsFinite(joint.local_frame1) || !IsUsableRotation(joint.local_frame1.rotation))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares a non finite joint frame.");
        }
        if (!std::isfinite(joint.lower_limit) || !std::isfinite(joint.upper_limit) ||
            joint.lower_limit > joint.upper_limit)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares an unordered or non finite limit range.");
        }
        if (!IsFiniteNonNegative(joint.min_distance) || !IsFiniteNonNegative(joint.max_distance) ||
            joint.min_distance > joint.max_distance)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares an unordered or negative distance range.");
        }
        if (!IsFiniteNonNegative(joint.cone_angle0) || !IsFiniteNonNegative(joint.cone_angle1) ||
            !IsFiniteNonNegative(joint.drive_stiffness) || !IsFiniteNonNegative(joint.drive_damping) ||
            !IsFiniteNonNegative(joint.drive_max_force) || !std::isfinite(joint.drive_target_position) ||
            !std::isfinite(joint.drive_target_velocity) || !IsFiniteNonNegative(joint.break_force) ||
            !IsFiniteNonNegative(joint.break_torque))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares a non finite or negative limit, drive, or break value.");
        }
        if (!IsFiniteNonNegative(joint.limit_stiffness) || !IsFiniteNonNegative(joint.limit_damping) ||
            !IsFiniteNonNegative(joint.limit_restitution) || joint.limit_restitution > 1.0F ||
            !IsFiniteNonNegative(joint.limit_bounce_threshold) || !IsFiniteNonNegative(joint.limit_contact_distance))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares a limit spring or restitution outside the supported range.");
        }
        /* A soft limit is only meaningful with a spring behind it. */
        if ((joint.flags & static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_FLAG_LIMIT_SOFT)) != 0 &&
            joint.limit_stiffness <= 0.0F)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares a soft limit without a positive limit stiffness.");
        }
        if (!IsFiniteNonNegative(joint.inv_mass_scale0) || !IsFiniteNonNegative(joint.inv_inertia_scale0) ||
            !IsFiniteNonNegative(joint.inv_mass_scale1) || !IsFiniteNonNegative(joint.inv_inertia_scale1))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares a non finite or negative mass scale.");
        }
        for (uint32_t axis = 0; axis < static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_AXIS_COUNT); ++axis)
        {
            if (joint.motion[axis] >= static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_MOTION_COUNT) ||
                (joint.axis_drive_flags[axis] & ~static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ALL)) != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, axis, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares an unknown per axis motion or drive flag.");
            }
            if (!std::isfinite(joint.axis_lower_limit[axis]) || !std::isfinite(joint.axis_upper_limit[axis]) ||
                joint.axis_lower_limit[axis] > joint.axis_upper_limit[axis])
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, axis, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares an unordered or non finite per axis limit range.");
            }
            if (!IsFiniteNonNegative(joint.axis_drive_stiffness[axis]) ||
                !IsFiniteNonNegative(joint.axis_drive_damping[axis]) ||
                !IsFiniteNonNegative(joint.axis_drive_max_force[axis]) ||
                !std::isfinite(joint.axis_drive_target_position[axis]) ||
                !std::isfinite(joint.axis_drive_target_velocity[axis]))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, axis, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " declares a non finite or negative per axis drive value.");
            }
            /* A locked axis cannot be driven, because the lock already removes
             * the degree of freedom the drive would act on. */
            if (joint.motion[axis] == static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_MOTION_LOCKED) &&
                (joint.axis_drive_flags[axis] & static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED)) != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS, index, axis, Describe(OPENUSD_PHYSX_SECTION_JOINTS, index) + " drives an axis that it also locks.");
            }
        }
    }

    for (uint32_t index = 0; index < header.filter_pairs.count; ++index)
    {
        const openusd_physx_filter_pair pair = page_view.Get<openusd_physx_filter_pair>(header.filter_pairs, index);
        if (pair.actor0_index >= header.actors.count || pair.actor1_index >= header.actors.count ||
            pair.actor0_index == pair.actor1_index)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_FILTER_PAIRS, index, 0, Describe(OPENUSD_PHYSX_SECTION_FILTER_PAIRS, index) + " must reference two distinct actors from this page.");
        }
    }

    /* Articulations own a contiguous window of the link section, and the
     * windows must tile it without overlapping. Checking that here is what lets
     * the world walk each window without re-deriving ownership. */
    uint32_t claimed_links = 0;
    for (uint32_t index = 0; index < header.articulations.count; ++index)
    {
        const openusd_physx_articulation_desc articulation =
            page_view.Get<openusd_physx_articulation_desc>(header.articulations, index);
        const openusd_physx_status status =
            require_identity(context, articulation.id, OPENUSD_PHYSX_SECTION_ARTICULATIONS, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if ((articulation.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_ARTICULATION_FLAG_ALL)) != 0 ||
            articulation.reserved0 != 0 || articulation.reserved1 != 0 ||
            articulation.reserved2 != 0 || articulation.reserved3 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATIONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATIONS, index) + " declares unknown flags or non zero reserved fields.");
        }
        if (articulation.scene_index < 0 ||
            articulation.scene_index >= static_cast<int32_t>(header.scenes.count))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATIONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATIONS, index) + " must reference a scene from this page.");
        }
        if (articulation.link_count == 0 ||
            articulation.link_offset != claimed_links ||
            articulation.link_count > header.articulation_links.count - claimed_links)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ARTICULATIONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATIONS, index) + " must own a non empty link window that continues where the previous articulation ended.");
        }
        claimed_links += articulation.link_count;
        if (articulation.position_iterations > 255 || articulation.velocity_iterations > 255)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATIONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATIONS, index) + " declares more solver iterations than the simulation SDK accepts.");
        }
        if (!IsFiniteNonNegative(articulation.sleep_threshold) ||
            !IsFiniteNonNegative(articulation.stabilization_threshold) ||
            !IsFiniteNonNegative(articulation.max_joint_velocity) ||
            !IsFiniteNonNegative(articulation.wake_counter))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATIONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATIONS, index) + " declares a non finite or negative solver budget.");
        }
    }
    if (claimed_links != header.articulation_links.count)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, claimed_links, 0, "Every articulation link must belong to exactly one articulation.");
    }

    uint32_t articulation_link_count = 0;
    for (uint32_t index = 0; index < header.articulations.count; ++index)
    {
        const openusd_physx_articulation_desc articulation =
            page_view.Get<openusd_physx_articulation_desc>(header.articulations, index);
        for (uint32_t local = 0; local < articulation.link_count; ++local)
        {
            const uint32_t link_index = articulation.link_offset + local;
            const openusd_physx_articulation_link_desc link =
                page_view.Get<openusd_physx_articulation_link_desc>(header.articulation_links, link_index);
            const openusd_physx_status status =
                require_identity(context, link.id, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index);
            if (status != OPENUSD_PHYSX_STATUS_OK)
            {
                return status;
            }
            if (link.joint_type >= static_cast<uint32_t>(OPENUSD_PHYSX_ARTICULATION_JOINT_TYPE_COUNT) ||
                (link.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_ALL)) != 0 ||
                link.reserved0 != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares an unknown joint type, unknown flags, or a non zero reserved field.");
            }

            /* The first link of a window is the root; every other link must
             * name a parent that is already part of the same window, so a page
             * describes a tree and can never describe a cycle. */
            if (local == 0)
            {
                if (link.parent_id != 0 ||
                    link.joint_type != static_cast<uint32_t>(OPENUSD_PHYSX_ARTICULATION_JOINT_NONE))
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " is a root link, so it must name no parent and no inbound joint.");
                }
            }
            else
            {
                bool parent_found = false;
                for (uint32_t earlier = 0; earlier < local && !parent_found; ++earlier)
                {
                    const openusd_physx_articulation_link_desc candidate =
                        page_view.Get<openusd_physx_articulation_link_desc>(
                            header.articulation_links, articulation.link_offset + earlier);
                    parent_found = candidate.id == link.parent_id;
                }
                if (!parent_found ||
                    link.joint_type == static_cast<uint32_t>(OPENUSD_PHYSX_ARTICULATION_JOINT_NONE))
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " must name an inbound joint and a parent link that appears earlier in the same articulation.");
                }
            }

            if (!openusd_physx_support::IsFinite(link.world_pose) || !IsUsableRotation(link.world_pose.rotation) ||
                !openusd_physx_support::IsFinite(link.parent_frame) || !IsUsableRotation(link.parent_frame.rotation) ||
                !openusd_physx_support::IsFinite(link.child_frame) || !IsUsableRotation(link.child_frame.rotation))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares a non finite pose or joint frame.");
            }
            if (!IsFiniteNonNegative(link.mass) || !openusd_physx_support::IsFinite(link.center_of_mass) ||
                !openusd_physx_support::IsFinite(link.inertia) || link.inertia.x < 0.0F ||
                link.inertia.y < 0.0F || link.inertia.z < 0.0F ||
                !IsUnsetOrUsableRotation(link.principal_axes))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares a non finite or negative mass frame.");
            }
            if (!IsFiniteNonNegative(link.linear_damping) || !IsFiniteNonNegative(link.angular_damping) ||
                !IsFiniteNonNegative(link.max_linear_velocity) || !IsFiniteNonNegative(link.max_angular_velocity) ||
                !IsFiniteNonNegative(link.joint_friction) || !IsFiniteNonNegative(link.max_joint_velocity))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares a non finite or negative damping, velocity, or friction budget.");
            }
            if (link.shape_count != 0 &&
                (link.shape_offset >= header.actor_shapes.count ||
                 link.shape_count > header.actor_shapes.count - link.shape_offset))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " references an actor shape window outside this page.");
            }
            if (link.collision_group >= OPENUSD_PHYSX_MAX_COLLISION_GROUPS)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares a collision group outside the supported range.");
            }
            /* Every articulation link is a movable body, so it carries the same
             * geometry restriction a dynamic actor carries. */
            for (uint32_t offset = 0; offset < link.shape_count; ++offset)
            {
                const openusd_physx_actor_shape_ref reference =
                    page_view.Get<openusd_physx_actor_shape_ref>(header.actor_shapes, link.shape_offset + offset);
                const openusd_physx_shape_desc shape =
                    page_view.Get<openusd_physx_shape_desc>(header.shapes, reference.shape_index);
                if (shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_PLANE) ||
                    shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH) ||
                    shape.type == static_cast<uint32_t>(OPENUSD_PHYSX_SHAPE_HEIGHTFIELD))
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " is movable and cannot use a plane, triangle mesh, or height field shape.");
                }
            }
            for (uint32_t axis = 0; axis < static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_AXIS_COUNT); ++axis)
            {
                if (link.motion[axis] >= static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_MOTION_COUNT) ||
                    (link.drive_flags[axis] & ~static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ALL)) != 0)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, axis, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares an unknown per axis motion or drive flag.");
                }
                if (!std::isfinite(link.lower_limit[axis]) || !std::isfinite(link.upper_limit[axis]) ||
                    link.lower_limit[axis] > link.upper_limit[axis])
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, axis, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares an unordered or non finite per axis limit range.");
                }
                if (!IsFiniteNonNegative(link.drive_stiffness[axis]) ||
                    !IsFiniteNonNegative(link.drive_damping[axis]) ||
                    !IsFiniteNonNegative(link.drive_max_force[axis]) ||
                    !std::isfinite(link.drive_target_position[axis]) ||
                    !std::isfinite(link.drive_target_velocity[axis]) ||
                    !IsFiniteNonNegative(link.armature[axis]))
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, axis, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " declares a non finite or negative per axis drive value.");
                }
                if (link.motion[axis] == static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_MOTION_LOCKED) &&
                    (link.drive_flags[axis] & static_cast<uint32_t>(OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED)) != 0)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index, axis, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, link_index) + " drives an axis that it also locks.");
                }
            }
            ++articulation_link_count;
        }
    }

    for (uint32_t index = 0; index < header.controllers.count; ++index)
    {
        const openusd_physx_controller_desc controller =
            page_view.Get<openusd_physx_controller_desc>(header.controllers, index);
        const openusd_physx_status status =
            require_identity(context, controller.id, OPENUSD_PHYSX_SECTION_CONTROLLERS, index);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        if (controller.shape >= static_cast<uint32_t>(OPENUSD_PHYSX_CONTROLLER_SHAPE_COUNT) ||
            (controller.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_CONTROLLER_FLAG_ALL)) != 0 ||
            controller.non_walkable_mode >= static_cast<uint32_t>(OPENUSD_PHYSX_CONTROLLER_NON_WALKABLE_MODE_COUNT) ||
            controller.climbing_mode >= static_cast<uint32_t>(OPENUSD_PHYSX_CONTROLLER_CLIMBING_MODE_COUNT) ||
            controller.reserved0 != 0 || controller.reserved1 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " declares an unknown shape, mode, flags, or a non zero reserved field.");
        }
        if (controller.scene_index < 0 || controller.scene_index >= static_cast<int32_t>(header.scenes.count))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " must reference a scene from this page.");
        }
        if (controller.material_index < -1 ||
            controller.material_index >= static_cast<int32_t>(header.materials.count))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " references a material outside this page.");
        }
        if (!openusd_physx_support::IsFinite(controller.position) ||
            !openusd_physx_support::IsFinite(controller.up_direction))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " declares a non finite position or up direction.");
        }
        if (controller.shape == static_cast<uint32_t>(OPENUSD_PHYSX_CONTROLLER_CAPSULE))
        {
            if (!(controller.radius > 0.0F) || !std::isfinite(controller.radius) ||
                !IsFiniteNonNegative(controller.height))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " declares a capsule without a positive radius.");
            }
        }
        else if (!(controller.half_extents.x > 0.0F) || !(controller.half_extents.y > 0.0F) ||
                 !(controller.half_extents.z > 0.0F) || !openusd_physx_support::IsFinite(controller.half_extents))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " declares a box without positive half extents.");
        }
        /* A slope limit is an angle, so anything at or beyond a right angle
         * would mean every surface is walkable and the limit means nothing. */
        if (!IsFiniteNonNegative(controller.slope_limit) ||
            controller.slope_limit >= 1.5707963F ||
            !IsFiniteNonNegative(controller.step_offset) ||
            !IsFiniteNonNegative(controller.contact_offset) ||
            !IsFiniteNonNegative(controller.density) ||
            !IsFiniteNonNegative(controller.scale_coefficient) || controller.scale_coefficient > 1.0F ||
            !IsFiniteNonNegative(controller.volume_growth))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " declares a slope, step, contact, or scale value outside the supported range.");
        }
        if (controller.collision_group >= OPENUSD_PHYSX_MAX_COLLISION_GROUPS)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CONTROLLERS, index, 0, Describe(OPENUSD_PHYSX_SECTION_CONTROLLERS, index) + " declares a collision group outside the supported range.");
        }
    }

    /* Tendons own a contiguous window of the tendon node section in the same
     * way an articulation owns a window of the link section, so a node can only
     * ever belong to one tendon and the windows tile the section exactly. */
    uint32_t claimed_tendon_nodes = 0;
    for (uint32_t index = 0; index < header.articulation_tendons.count; ++index)
    {
        const openusd_physx_tendon_desc& tendon =
            page_view.Get<openusd_physx_tendon_desc>(header.articulation_tendons, index);
        const openusd_physx_status identity_status =
            require_identity(context, tendon.id, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if (tendon.type >= OPENUSD_PHYSX_TENDON_TYPE_COUNT ||
            (tendon.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_TENDON_FLAG_ALL)) != 0 ||
            tendon.reserved0 != 0 || tendon.reserved1 != 0.0F)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index) + " declares an unknown tendon type, unknown flags, or a non zero reserved field.");
        }
        if (tendon.articulation_index >= header.articulations.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index) + " must reference an articulation from this page.");
        }
        if (tendon.node_offset != claimed_tendon_nodes || tendon.node_count == 0 ||
            tendon.node_count > header.articulation_tendon_nodes.count - claimed_tendon_nodes)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index) + " must own a non empty node window that continues where the previous tendon ended.");
        }
        claimed_tendon_nodes += tendon.node_count;
        if (!IsFiniteNonNegative(tendon.stiffness) || !IsFiniteNonNegative(tendon.damping) ||
            !IsFiniteNonNegative(tendon.limit_stiffness) || !std::isfinite(tendon.offset) ||
            !IsFiniteNonNegative(tendon.rest_length))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index) + " declares a non finite or negative tendon gain.");
        }
        if (!std::isfinite(tendon.low_limit) || !std::isfinite(tendon.high_limit) ||
            tendon.low_limit > tendon.high_limit)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS, index) + " declares an unordered or non finite tendon limit range.");
        }
    }
    if (claimed_tendon_nodes != header.articulation_tendon_nodes.count)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, claimed_tendon_nodes, 0, "Every articulation tendon node must belong to exactly one tendon.");
    }

    for (uint32_t index = 0; index < header.articulation_tendons.count; ++index)
    {
        const openusd_physx_tendon_desc& tendon =
            page_view.Get<openusd_physx_tendon_desc>(header.articulation_tendons, index);
        const openusd_physx_articulation_desc& articulation =
            page_view.Get<openusd_physx_articulation_desc>(header.articulations, tendon.articulation_index);
        for (uint32_t local = 0; local < tendon.node_count; ++local)
        {
            const uint32_t node_index = tendon.node_offset + local;
            const openusd_physx_tendon_node_desc& node =
                page_view.Get<openusd_physx_tendon_node_desc>(header.articulation_tendon_nodes, node_index);
            const openusd_physx_status node_identity =
                require_identity(context, node.id, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index);
            if (node_identity != OPENUSD_PHYSX_STATUS_OK)
            {
                return node_identity;
            }
            if ((node.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_TENDON_FLAG_ALL)) != 0 ||
                node.reserved0 != 0 || node.reserved1 != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " declares unknown flags or a non zero reserved field.");
            }
            if (local == 0)
            {
                if (node.parent_index != 0)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " roots its tendon, so it must name no parent node.");
                }
            }
            else if (node.parent_index == 0 || node.parent_index > local)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " must name a parent node that appears earlier in the same tendon.");
            }
            if (node.link_index >= articulation.link_count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " must reference a link of the articulation that owns its tendon.");
            }
            if (tendon.type == OPENUSD_PHYSX_TENDON_FIXED)
            {
                if (node.axis >= OPENUSD_PHYSX_JOINT_AXIS_COUNT)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " declares an axis outside the supported range.");
                }
                /* The root tendon joint names the link the tendon hangs from,
                 * which may be the articulation root; every later tendon joint
                 * drives the inbound joint of its link, so it may not. */
                if (local != 0 && node.link_index == 0)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " drives an inbound joint, so it must not reference the articulation root, which has none.");
                }
            }
            else if (node.axis != 0 || !IsFinite(node.relative_offset))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " is a spatial attachment, so it must leave the axis unset and declare a finite relative offset.");
            }
            if (!std::isfinite(node.coefficient) || !std::isfinite(node.recip_coefficient) ||
                !IsFiniteNonNegative(node.rest_length))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " declares a non finite coefficient or a negative rest length.");
            }
            if (!std::isfinite(node.low_limit) || !std::isfinite(node.high_limit) ||
                node.low_limit > node.high_limit)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES, node_index) + " declares an unordered or non finite limit range.");
            }
        }
    }

    for (uint32_t index = 0; index < header.articulation_mimic_joints.count; ++index)
    {
        const openusd_physx_mimic_joint_desc& mimic =
            page_view.Get<openusd_physx_mimic_joint_desc>(header.articulation_mimic_joints, index);
        const openusd_physx_status identity_status =
            require_identity(context, mimic.id, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if (mimic.reserved0 != 0 || mimic.reserved1 != 0 || mimic.reserved2 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index) + " declares a non zero reserved field.");
        }
        if (mimic.articulation_index >= header.articulations.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index) + " must reference an articulation from this page.");
        }
        const openusd_physx_articulation_desc& articulation =
            page_view.Get<openusd_physx_articulation_desc>(header.articulations, mimic.articulation_index);
        if (mimic.link_a == 0 || mimic.link_b == 0 ||
            mimic.link_a >= articulation.link_count || mimic.link_b >= articulation.link_count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index) + " must couple two non root links of the articulation that owns it.");
        }
        if (mimic.link_a == mimic.link_b && mimic.axis_a == mimic.axis_b)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index) + " must couple two different joint axes.");
        }
        if (mimic.axis_a >= OPENUSD_PHYSX_JOINT_AXIS_COUNT || mimic.axis_b >= OPENUSD_PHYSX_JOINT_AXIS_COUNT)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index) + " declares an axis outside the supported range.");
        }
        if (!std::isfinite(mimic.gear_ratio) || mimic.gear_ratio == 0.0F || !std::isfinite(mimic.offset) ||
            !IsFiniteNonNegative(mimic.natural_frequency) || !IsFiniteNonNegative(mimic.damping_ratio))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS, index) + " declares a zero, non finite, or negative coupling value.");
        }
    }

    /* Vehicles own a contiguous window of the wheel section. */
    uint32_t claimed_wheels = 0;
    uint32_t published_wheel_count = 0;
    for (uint32_t index = 0; index < header.vehicles.count; ++index)
    {
        const openusd_physx_vehicle_desc& vehicle =
            page_view.Get<openusd_physx_vehicle_desc>(header.vehicles, index);
        const openusd_physx_status identity_status =
            require_identity(context, vehicle.id, OPENUSD_PHYSX_SECTION_VEHICLES, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if ((vehicle.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_VEHICLE_FLAG_ALL)) != 0 ||
            vehicle.drive >= OPENUSD_PHYSX_VEHICLE_DRIVE_COUNT ||
            vehicle.query >= OPENUSD_PHYSX_VEHICLE_QUERY_COUNT ||
            vehicle.reserved0 != 0 || vehicle.reserved1 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " declares unknown flags, an unknown drive or query mode, or a non zero reserved field.");
        }
        if (vehicle.scene_index >= header.scenes.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " must reference a scene from this page.");
        }
        if (vehicle.actor_index >= header.actors.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " must reference a chassis actor from this page.");
        }
        const openusd_physx_actor_desc& chassis =
            page_view.Get<openusd_physx_actor_desc>(header.actors, vehicle.actor_index);
        if (chassis.type != OPENUSD_PHYSX_ACTOR_DYNAMIC)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " must reference a dynamic, non kinematic chassis actor.");
        }
        if (static_cast<uint32_t>(chassis.scene_index) != vehicle.scene_index)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " must share the scene of its chassis actor.");
        }
        if (vehicle.longitudinal_axis > OPENUSD_PHYSX_AXIS_Z || vehicle.lateral_axis > OPENUSD_PHYSX_AXIS_Z ||
            vehicle.vertical_axis > OPENUSD_PHYSX_AXIS_Z ||
            vehicle.longitudinal_axis == vehicle.lateral_axis ||
            vehicle.longitudinal_axis == vehicle.vertical_axis ||
            vehicle.lateral_axis == vehicle.vertical_axis)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " must declare three different coordinate axes.");
        }
        if (vehicle.wheel_offset != claimed_wheels || vehicle.wheel_count == 0 ||
            vehicle.wheel_count > header.vehicle_wheels.count - claimed_wheels ||
            vehicle.wheel_count > OPENUSD_PHYSX_MAX_VEHICLE_WHEELS)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " must own a non empty wheel window that continues where the previous vehicle ended and fits the supported wheel budget.");
        }
        claimed_wheels += vehicle.wheel_count;
        if ((vehicle.flags & static_cast<uint32_t>(OPENUSD_PHYSX_VEHICLE_FLAG_PUBLISH_WHEELS)) != 0)
        {
            published_wheel_count += vehicle.wheel_count;
        }
        if (!IsFiniteNonNegative(vehicle.chassis_mass) || !IsFinite(vehicle.chassis_moi) ||
            vehicle.chassis_moi.x < 0.0F || vehicle.chassis_moi.y < 0.0F || vehicle.chassis_moi.z < 0.0F ||
            !IsFiniteNonNegative(vehicle.sprung_mass_total))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " declares a non finite or negative chassis mass frame.");
        }
        if (!IsFiniteNonNegative(vehicle.max_brake_torque) ||
            !IsFiniteNonNegative(vehicle.max_hand_brake_torque) ||
            !IsFiniteNonNegative(vehicle.max_steer_angle) || vehicle.max_steer_angle > 3.1415927F ||
            !IsFiniteNonNegative(vehicle.default_friction))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " declares a brake, steer, or friction value outside the supported range.");
        }
        if (vehicle.drive == OPENUSD_PHYSX_VEHICLE_DRIVE_ENGINE)
        {
            if (!IsPositiveFinite(vehicle.engine_peak_torque) || !IsPositiveFinite(vehicle.engine_moi) ||
                !IsFiniteNonNegative(vehicle.engine_idle_omega) ||
                !IsPositiveFinite(vehicle.engine_max_omega) ||
                vehicle.engine_idle_omega >= vehicle.engine_max_omega ||
                !IsFiniteNonNegative(vehicle.engine_damping_full_throttle) ||
                !IsFiniteNonNegative(vehicle.engine_damping_zero_throttle_clutch_engaged) ||
                !IsFiniteNonNegative(vehicle.engine_damping_zero_throttle_clutch_disengaged))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " drives an engine, so it must declare a usable engine.");
            }
            if (!IsPositiveFinite(vehicle.clutch_strength) ||
                !IsFiniteNonNegative(vehicle.gear_switch_time) ||
                !IsPositiveFinite(vehicle.final_gear_ratio) ||
                !IsPositiveFinite(vehicle.reverse_gear_ratio) ||
                !IsPositiveFinite(vehicle.first_gear_ratio) || !IsPositiveFinite(vehicle.top_gear_ratio) ||
                vehicle.top_gear_ratio > vehicle.first_gear_ratio)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " drives an engine, so it must declare a usable clutch and gearbox.");
            }
            /* One neutral, one reverse and the forward gears must all fit the
             * gear budget the simulation SDK accepts. The bound is written as
             * a subtraction so that a forward gear count near the top of the
             * unsigned range cannot wrap past the comparison. */
            if (vehicle.forward_gear_count == 0 ||
                vehicle.forward_gear_count > OPENUSD_PHYSX_MAX_VEHICLE_GEARS - 2u)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " declares a forward gear count outside the supported range.");
            }
            if (!IsFiniteNonNegative(vehicle.autobox_up_ratio) || vehicle.autobox_up_ratio > 1.0F ||
                !IsFiniteNonNegative(vehicle.autobox_down_ratio) || vehicle.autobox_down_ratio > 1.0F ||
                !IsFiniteNonNegative(vehicle.autobox_latency))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " declares an autobox value outside the supported range.");
            }
        }
    }
    if (claimed_wheels != header.vehicle_wheels.count)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, claimed_wheels, 0, "Every vehicle wheel must belong to exactly one vehicle.");
    }

    for (uint32_t index = 0; index < header.vehicles.count; ++index)
    {
        const openusd_physx_vehicle_desc& vehicle =
            page_view.Get<openusd_physx_vehicle_desc>(header.vehicles, index);
        bool has_driven_wheel = false;
        for (uint32_t local = 0; local < vehicle.wheel_count; ++local)
        {
            const uint32_t wheel_index = vehicle.wheel_offset + local;
            const openusd_physx_vehicle_wheel_desc& wheel =
                page_view.Get<openusd_physx_vehicle_wheel_desc>(header.vehicle_wheels, wheel_index);
            const openusd_physx_status wheel_identity =
                require_identity(context, wheel.id, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index);
            if (wheel_identity != OPENUSD_PHYSX_STATUS_OK)
            {
                return wheel_identity;
            }
            if ((wheel.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_ALL)) != 0 ||
                wheel.reserved0 != 0 || wheel.reserved1 != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares unknown flags or a non zero reserved field.");
            }
            if ((wheel.flags & static_cast<uint32_t>(OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_DRIVEN)) != 0)
            {
                has_driven_wheel = true;
            }
            if (wheel.axle_index >= vehicle.wheel_count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares an axle index outside the axle budget of its vehicle.");
            }
            if (!IsFinite(wheel.suspension_attachment) || !IsFinite(wheel.wheel_attachment) ||
                !IsUnsetOrUsableRotation(wheel.suspension_attachment.rotation) ||
                !IsUnsetOrUsableRotation(wheel.wheel_attachment.rotation))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares a non finite or unusable attachment frame.");
            }
            if (!IsFinite(wheel.suspension_travel_dir) ||
                !IsPositiveFinite(wheel.suspension_travel_dist) ||
                !IsPositiveFinite(wheel.suspension_stiffness) ||
                !IsFiniteNonNegative(wheel.suspension_damping) ||
                !IsFiniteNonNegative(wheel.sprung_mass))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares a non finite or non positive suspension.");
            }
            const float travel_length =
                (wheel.suspension_travel_dir.x * wheel.suspension_travel_dir.x) +
                (wheel.suspension_travel_dir.y * wheel.suspension_travel_dir.y) +
                (wheel.suspension_travel_dir.z * wheel.suspension_travel_dir.z);
            if (travel_length <= 1.0e-8F)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares a degenerate suspension travel direction.");
            }
            if (!IsPositiveFinite(wheel.radius) || !IsPositiveFinite(wheel.half_width) ||
                !IsPositiveFinite(wheel.mass) || !IsFiniteNonNegative(wheel.moi) ||
                !IsFiniteNonNegative(wheel.damping_rate))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares a non finite or non positive wheel body.");
            }
            if (!IsFiniteNonNegative(wheel.tire_lat_stiff_x) ||
                !IsFiniteNonNegative(wheel.tire_lat_stiff_y) ||
                !IsFiniteNonNegative(wheel.tire_long_stiff) ||
                !IsFiniteNonNegative(wheel.tire_camber_stiff) ||
                !IsFiniteNonNegative(wheel.tire_rest_load) ||
                !IsFiniteNonNegative(wheel.tire_friction))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares a non finite or negative tire value.");
            }
            if (!IsFiniteNonNegative(wheel.steer_response) || wheel.steer_response > 1.0F ||
                !IsFiniteNonNegative(wheel.brake_response) || wheel.brake_response > 1.0F ||
                !IsFiniteNonNegative(wheel.hand_brake_response) || wheel.hand_brake_response > 1.0F ||
                !IsFiniteNonNegative(wheel.drive_torque_ratio) || wheel.drive_torque_ratio > 1.0F)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS, wheel_index) + " declares a command response outside the zero to one range.");
            }
        }
        if (vehicle.drive == OPENUSD_PHYSX_VEHICLE_DRIVE_ENGINE && !has_driven_wheel)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_VEHICLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_VEHICLES, index) + " drives an engine, so at least one of its wheels must receive drive torque.");
        }
    }

    /* ---------------------------------------------------------------------
     * CUDA accelerated domains. Everything below is validated exactly like a
     * CPU domain: a page that declares a particle system or a deformable is
     * either structurally sound or is rejected whole, long before a runtime
     * decides whether it can reach a device. Whether the objects can then
     * actually be simulated is a runtime capability question, never a page
     * question.
     * ------------------------------------------------------------------ */

    for (uint32_t index = 0; index < header.particle_materials.count; ++index)
    {
        const openusd_physx_particle_material_desc material =
            page_view.Get<openusd_physx_particle_material_desc>(header.particle_materials, index);
        const openusd_physx_status identity_status =
            require_identity(context, material.id, OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if (material.reserved0 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS, index) + " declares a non zero reserved field.");
        }
        if (!IsFiniteNonNegative(material.friction) || !IsFiniteNonNegative(material.damping) ||
            !IsFiniteNonNegative(material.adhesion) || !IsFiniteNonNegative(material.adhesion_offset_scale) ||
            !IsFiniteNonNegative(material.particle_friction_scale) ||
            !IsFiniteNonNegative(material.particle_adhesion_scale) ||
            !IsFiniteNonNegative(material.viscosity) || !IsFiniteNonNegative(material.surface_tension) ||
            !IsFiniteNonNegative(material.cohesion) || !IsFiniteNonNegative(material.vorticity_confinement) ||
            !IsFiniteNonNegative(material.drag) || !IsFiniteNonNegative(material.lift) ||
            !IsFiniteNonNegative(material.gravity_scale) || !IsFiniteNonNegative(material.density) ||
            !IsFiniteNonNegative(material.cfl_coefficient))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS, index) + " declares a non finite or negative particle material value.");
        }
    }

    /* A particle system owns a contiguous window of the particle body section,
     * so every body belongs to exactly one system and the windows tile the
     * section from the first record to the last. */
    uint32_t claimed_particle_bodies = 0;
    uint64_t deformation_body_count = 0;
    uint64_t deformation_point_count = 0;
    for (uint32_t index = 0; index < header.particle_systems.count; ++index)
    {
        const openusd_physx_particle_system_desc system =
            page_view.Get<openusd_physx_particle_system_desc>(header.particle_systems, index);
        const openusd_physx_status identity_status =
            require_identity(context, system.id, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if ((system.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_ALL)) != 0 ||
            system.reserved0 != 0 || system.reserved1 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " declares unknown flags or a non zero reserved field.");
        }
        if (system.scene_index < 0 || static_cast<uint32_t>(system.scene_index) >= header.scenes.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " must reference a scene from this page.");
        }
        if (!IsFiniteNonNegative(system.contact_offset) || !IsFiniteNonNegative(system.rest_offset) ||
            !IsFiniteNonNegative(system.particle_contact_offset) ||
            !IsFiniteNonNegative(system.solid_rest_offset) || !IsFiniteNonNegative(system.fluid_rest_offset) ||
            !IsFiniteNonNegative(system.max_depenetration_velocity) ||
            !IsFiniteNonNegative(system.neighborhood_scale) || !IsFinite(system.wind))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " declares a non finite or negative particle system value.");
        }
        /* A rest offset above the contact offset would ask the solver to
         * separate particles further than it ever generates contacts for,
         * which no amount of iteration can satisfy. */
        if (system.particle_contact_offset > 0.0F)
        {
            if (system.solid_rest_offset > system.particle_contact_offset ||
                system.fluid_rest_offset > system.particle_contact_offset)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " declares a rest offset larger than its particle contact offset.");
            }
        }
        if (system.contact_offset > 0.0F && system.rest_offset > system.contact_offset)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " declares a rest offset larger than its contact offset.");
        }
        if (system.max_neighborhood != 0 &&
            (system.max_neighborhood < OPENUSD_PHYSX_MIN_PARTICLE_NEIGHBORHOOD ||
             system.max_neighborhood > OPENUSD_PHYSX_MAX_PARTICLE_NEIGHBORHOOD))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " declares a neighbourhood budget outside the supported range.");
        }
        if (system.solver_position_iterations > 255)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " declares a solver iteration count outside the supported range.");
        }
        if (system.body_offset != claimed_particle_bodies ||
            system.body_count > header.particle_bodies.count - claimed_particle_bodies)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, index) + " must own a particle body window that continues where the previous system ended.");
        }
        claimed_particle_bodies += system.body_count;
    }
    if (claimed_particle_bodies != header.particle_bodies.count)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, claimed_particle_bodies, 0, "Every particle body must belong to exactly one particle system.");
    }

    for (uint32_t index = 0; index < header.particle_bodies.count; ++index)
    {
        const openusd_physx_particle_body_desc body =
            page_view.Get<openusd_physx_particle_body_desc>(header.particle_bodies, index);
        const openusd_physx_status identity_status =
            require_identity(context, body.id, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if (body.kind >= OPENUSD_PHYSX_PARTICLE_BODY_KIND_COUNT ||
            (body.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_PARTICLE_BODY_FLAG_ALL)) != 0 ||
            body.reserved0 != 0 || body.reserved1 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index) + " declares an unknown kind, unknown flags, or a non zero reserved field.");
        }
        if (body.material_index >= 0 &&
            static_cast<uint32_t>(body.material_index) >= header.particle_materials.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index) + " must reference a particle material from this page.");
        }
        /* The collision group is bounded to the twenty bits a phase reserves
         * for it, which is also what makes the runtime phase key that packs the
         * group, the behaviour flags, and the material index provably free of
         * collisions between two different bodies. */
        if (body.particle_group > OPENUSD_PHYSX_MAX_PARTICLE_GROUP)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index) + " declares a collision group outside the twenty bits a particle phase reserves for it.");
        }
        if (!IsFiniteNonNegative(body.mass) || !IsFinite(body.world_pose) ||
            !IsUnsetOrUsableRotation(body.world_pose.rotation))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index) + " declares a negative mass or an unusable world pose.");
        }
        /* The point window is checked before it is ever used to index the
         * shared point section, and the count is bounded before it can turn
         * into a device allocation. */
        if (body.point_count == 0 || body.point_count > OPENUSD_PHYSX_MAX_PARTICLES_PER_BODY ||
            body.point_offset > header.mesh_points.count ||
            body.point_count > header.mesh_points.count - body.point_offset)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index) + " must own a non empty point window inside the mesh point section and inside the supported particle budget.");
        }
        for (uint32_t point = 0; point < body.point_count; ++point)
        {
            const openusd_physx_vec3f value =
                page_view.Get<openusd_physx_vec3f>(header.mesh_points, body.point_offset + point);
            if (!IsFinite(value))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index, 0, Describe(OPENUSD_PHYSX_SECTION_PARTICLE_BODIES, index) + " declares a non finite particle position.");
            }
        }
        deformation_body_count += 1;
        deformation_point_count += body.point_count;
    }

    for (uint32_t index = 0; index < header.deformable_materials.count; ++index)
    {
        const openusd_physx_deformable_material_desc material =
            page_view.Get<openusd_physx_deformable_material_desc>(header.deformable_materials, index);
        const openusd_physx_status identity_status =
            require_identity(context, material.id, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if (material.kind >= OPENUSD_PHYSX_DEFORMABLE_KIND_COUNT || material.reserved0 != 0 ||
            material.reserved1 != 0 || material.reserved2 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index) + " declares an unknown kind or a non zero reserved field.");
        }
        if (!IsPositiveFinite(material.youngs_modulus) || !IsPositiveFinite(material.density) ||
            !IsFiniteNonNegative(material.dynamic_friction) ||
            !IsFiniteNonNegative(material.elasticity_damping))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index) + " declares a non positive stiffness or density, or a negative friction or damping.");
        }
        /* A Poisson ratio of one half is incompressible and divides by zero in
         * the Lame parameters, so the open interval is the only usable one. */
        if (!std::isfinite(material.poissons_ratio) || material.poissons_ratio < 0.0F ||
            material.poissons_ratio >= 0.5F)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index) + " declares a Poisson ratio outside the usable zero to one half interval.");
        }
        if (material.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE)
        {
            if (!IsFiniteNonNegative(material.bending_stiffness) ||
                !IsFiniteNonNegative(material.bending_damping) || !IsPositiveFinite(material.thickness))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index) + " is a surface material, so it must declare a positive thickness and a non negative bending response.");
            }
        }
        else if (material.bending_stiffness != 0.0F || material.bending_damping != 0.0F ||
                 material.thickness != 0.0F)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS, index) + " is a volume material, so it must leave the surface shell fields unset.");
        }
    }

    for (uint32_t index = 0; index < header.deformables.count; ++index)
    {
        const openusd_physx_deformable_desc deformable =
            page_view.Get<openusd_physx_deformable_desc>(header.deformables, index);
        const openusd_physx_status identity_status =
            require_identity(context, deformable.id, OPENUSD_PHYSX_SECTION_DEFORMABLES, index);
        if (identity_status != OPENUSD_PHYSX_STATUS_OK)
        {
            return identity_status;
        }
        if (deformable.kind >= OPENUSD_PHYSX_DEFORMABLE_KIND_COUNT ||
            (deformable.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_DEFORMABLE_FLAG_ALL)) != 0 ||
            deformable.reserved0 != 0 || deformable.reserved1 != 0)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares an unknown kind, unknown flags, or a non zero reserved field.");
        }
        if (deformable.scene_index < 0 ||
            static_cast<uint32_t>(deformable.scene_index) >= header.scenes.count)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must reference a scene from this page.");
        }
        if (deformable.material_index >= 0)
        {
            if (static_cast<uint32_t>(deformable.material_index) >= header.deformable_materials.count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must reference a deformable material from this page.");
            }
            const openusd_physx_deformable_material_desc material =
                page_view.Get<openusd_physx_deformable_material_desc>(
                    header.deformable_materials,
                    static_cast<uint32_t>(deformable.material_index));
            if (material.kind != deformable.kind)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must reference a material of its own deformable kind.");
            }
        }
        if (!IsFiniteNonNegative(deformable.vertex_velocity_damping) ||
            !IsFiniteNonNegative(deformable.max_displacement) ||
            !IsFiniteNonNegative(deformable.self_collision_filter_distance) ||
            !IsFiniteNonNegative(deformable.max_depenetration_velocity) ||
            !IsFiniteNonNegative(deformable.settling_threshold) ||
            !IsFiniteNonNegative(deformable.sleep_threshold) || !IsFinite(deformable.world_pose) ||
            !IsUnsetOrUsableRotation(deformable.world_pose.rotation))
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares a non finite or negative solver value, or an unusable world pose.");
        }
        if (deformable.solver_position_iterations > 255)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares a solver iteration count outside the supported range.");
        }
        if (deformable.point_count < 3 || deformable.point_count > OPENUSD_PHYSX_MAX_DEFORMABLE_VERTICES ||
            deformable.point_offset > header.mesh_points.count ||
            deformable.point_count > header.mesh_points.count - deformable.point_offset)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must own a simulation point window inside the mesh point section and inside the supported vertex budget.");
        }
        for (uint32_t point = 0; point < deformable.point_count; ++point)
        {
            const openusd_physx_vec3f value =
                page_view.Get<openusd_physx_vec3f>(header.mesh_points, deformable.point_offset + point);
            if (!IsFinite(value))
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares a non finite simulation vertex.");
            }
        }
        const uint32_t vertices_per_element =
            deformable.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE ? 3u : 4u;
        if (deformable.index_count == 0 || (deformable.index_count % vertices_per_element) != 0 ||
            deformable.index_offset > header.mesh_indices.count ||
            deformable.index_count > header.mesh_indices.count - deformable.index_offset)
        {
            return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must own a whole element index window inside the mesh index section.");
        }
        for (uint32_t element = 0; element < deformable.index_count; ++element)
        {
            const uint32_t value =
                page_view.Get<uint32_t>(header.mesh_indices, deformable.index_offset + element);
            if (value >= deformable.point_count)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares a simulation index outside its own point window.");
            }
        }
        if (deformable.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE)
        {
            if (deformable.collision_point_count != 0 || deformable.collision_index_count != 0 ||
                deformable.collision_point_offset != 0 || deformable.collision_index_offset != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " is a surface, so it must leave the collision mesh window unset.");
            }
            if ((deformable.flags & static_cast<uint32_t>(OPENUSD_PHYSX_DEFORMABLE_FLAG_KINEMATIC)) != 0)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " is a surface, so it cannot declare a kinematic simulation mesh.");
            }
        }
        else if (deformable.collision_point_count != 0 || deformable.collision_index_count != 0)
        {
            if (deformable.collision_point_count < 4 ||
                deformable.collision_point_count > OPENUSD_PHYSX_MAX_DEFORMABLE_VERTICES ||
                deformable.collision_point_offset > header.mesh_points.count ||
                deformable.collision_point_count >
                    header.mesh_points.count - deformable.collision_point_offset)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must own a collision point window inside the mesh point section.");
            }
            if (deformable.collision_index_count == 0 || (deformable.collision_index_count % 4u) != 0 ||
                deformable.collision_index_offset > header.mesh_indices.count ||
                deformable.collision_index_count >
                    header.mesh_indices.count - deformable.collision_index_offset)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " must own a whole tetrahedron collision index window inside the mesh index section.");
            }
            for (uint32_t element = 0; element < deformable.collision_index_count; ++element)
            {
                const uint32_t value = page_view.Get<uint32_t>(
                    header.mesh_indices, deformable.collision_index_offset + element);
                if (value >= deformable.collision_point_count)
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares a collision index outside its own collision point window.");
                }
            }
            for (uint32_t point = 0; point < deformable.collision_point_count; ++point)
            {
                const openusd_physx_vec3f value = page_view.Get<openusd_physx_vec3f>(
                    header.mesh_points, deformable.collision_point_offset + point);
                if (!IsFinite(value))
                {
                    return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES, index, 0, Describe(OPENUSD_PHYSX_SECTION_DEFORMABLES, index) + " declares a non finite collision vertex.");
                }
            }
        }
        deformation_body_count += 1;
        deformation_point_count += deformable.point_count;
    }

    if ((header.particle_systems.count != 0 || header.deformables.count != 0) && header.scenes.count == 0)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_SCENES, 0, 0, "A page with particle systems or deformables must declare at least one scene.");
    }
    if (header.particle_bodies.count != 0 && header.particle_systems.count == 0)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS, 0, 0, "A page with particle bodies must declare at least one particle system.");
    }

    if ((header.articulations.count != 0 || header.controllers.count != 0 || header.vehicles.count != 0) &&
        header.scenes.count == 0)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_SCENES, 0, 0, "A page with articulations, controllers, or vehicles must declare at least one scene.");
    }

    if ((header.articulation_tendons.count != 0 || header.articulation_mimic_joints.count != 0) &&
        header.articulations.count == 0)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ARTICULATIONS, 0, 0, "A page with tendons or mimic joints must declare at least one articulation.");
    }

    if (header.actors.count != 0 && header.scenes.count == 0)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_SCENES, 0, 0, "A page with actors must declare at least one scene.");
    }

    /* An actor and an articulation link are both published as a body state keyed
     * by identity, and both are resolved from that identity when a command
     * arrives, so two of them sharing an identity gives the world two bodies at
     * one address: whichever the command map happened to keep receives every
     * command while both publish a pose for the same prim. Overlapping
     * articulation roots are how that happens in practice and the composer
     * refuses them, but the page is the contract this world builds from and a
     * caller that hands it a page directly must be refused too. The order here -
     * every actor, then every link - matches the managed validator exactly so
     * that the same page fails the same way on both sides. */
    {
        std::unordered_set<uint64_t> simulated_body_ids;
        simulated_body_ids.reserve(
            static_cast<size_t>(header.actors.count) + static_cast<size_t>(header.articulation_links.count));
        for (uint32_t index = 0; index < header.actors.count; ++index)
        {
            const openusd_physx_actor_desc actor = page_view.Get<openusd_physx_actor_desc>(header.actors, index);
            if (!simulated_body_ids.insert(actor.id).second)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_ACTORS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ACTORS, index) + " declares an identity another simulated body already declares.");
            }
        }
        for (uint32_t index = 0; index < header.articulation_links.count; ++index)
        {
            const openusd_physx_articulation_link_desc link =
                page_view.Get<openusd_physx_articulation_link_desc>(header.articulation_links, index);
            if (!simulated_body_ids.insert(link.id).second)
            {
                return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, index, 0, Describe(OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS, index) + " declares an identity another simulated body already declares.");
            }
        }
    }

    const openusd_physx_result_capacities& capacities = header.capacities;
    if (capacities.max_body_states > OPENUSD_PHYSX_MAX_RESULT_CAPACITY ||
        capacities.max_events > OPENUSD_PHYSX_MAX_RESULT_CAPACITY ||
        capacities.max_diagnostics > OPENUSD_PHYSX_MAX_RESULT_CAPACITY ||
        capacities.max_debug_lines > OPENUSD_PHYSX_MAX_RESULT_CAPACITY ||
        capacities.max_query_hits > OPENUSD_PHYSX_MAX_RESULT_CAPACITY ||
        capacities.max_deformation_bodies > OPENUSD_PHYSX_MAX_RESULT_CAPACITY)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES, 0, offsetof(openusd_physx_build_page_header, capacities), "A declared result capacity exceeds the supported maximum.");
    }
    if (capacities.reserved0 != 0)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CAPACITIES, 0, offsetof(openusd_physx_build_page_header, capacities), "Reserved capacity fields must be zero.");
    }
    if (capacities.max_body_states <
        dynamic_actor_count + articulation_link_count + header.controllers.count + published_wheel_count)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES, dynamic_actor_count, offsetof(openusd_physx_build_page_header, capacities), "The declared body state capacity is smaller than the number of movable actors, articulation links, controllers, and published vehicle wheels.");
    }
    /* Deformation output is bounded exactly like every other result section:
     * the page states the capacity, and it must be large enough for every GPU
     * object the same page declares. A page that declares no GPU object needs
     * no deformation capacity at all, which is what a CPU only build carries.
     * The point budget is compared in 64 bits because a page may legally
     * declare more vertices than a 32 bit sum could hold, and that must be a
     * diagnosed capacity error rather than a wrapped comparison. */
    if (deformation_body_count > capacities.max_deformation_bodies)
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES, static_cast<uint32_t>(deformation_body_count & 0xFFFFFFFFu), offsetof(openusd_physx_build_page_header, capacities), "The declared deformation body capacity is smaller than the number of particle bodies and deformables the page declares.");
    }
    if (deformation_point_count > static_cast<uint64_t>(capacities.max_deformation_points))
    {
        return Fail(context, OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES, static_cast<uint32_t>(deformation_point_count & 0xFFFFFFFFu), offsetof(openusd_physx_build_page_header, capacities), "The declared deformation point capacity is smaller than the number of simulated vertices the page declares.");
    }

    if (validation != nullptr)
    {
        validation->error_code = static_cast<uint32_t>(OPENUSD_PHYSX_PAGE_ERROR_NONE);
        validation->section = static_cast<uint32_t>(OPENUSD_PHYSX_SECTION_HEADER);
        validation->element_index = 0;
        validation->byte_offset = 0;
        validation->revision = header.revision;
        validation->source_hash = header.source_hash;
        validation->identity_count = header.identities.count;
        validation->scene_count = header.scenes.count;
        validation->material_count = header.materials.count;
        validation->shape_count = header.shapes.count;
        validation->actor_count = header.actors.count;
        validation->dynamic_actor_count = dynamic_actor_count;
        validation->joint_count = header.joints.count;
        validation->filter_pair_count = header.filter_pairs.count;
        validation->capacities = capacities;
    }
    if (view != nullptr)
    {
        *view = page_view;
    }
    return OPENUSD_PHYSX_STATUS_OK;
}
}

openusd_physx_status openusd_physx_world_get_abi(
    openusd_physx_abi_info* info,
    openusd_physx_error_buffer* error)
{
    return openusd_physx_support::Guard(error, [&]() -> openusd_physx_status
    {
        if (info == nullptr)
        {
            WriteError(error, "An ABI information output is required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (info->struct_size != sizeof(openusd_physx_abi_info))
        {
            WriteError(error, "The ABI information structure size does not match this ABI.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        info->abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        info->page_magic = OPENUSD_PHYSX_PAGE_MAGIC;
        info->build_page_header_size = static_cast<uint32_t>(sizeof(openusd_physx_build_page_header));
        info->page_span_size = static_cast<uint32_t>(sizeof(openusd_physx_page_span));
        info->capacities_size = static_cast<uint32_t>(sizeof(openusd_physx_result_capacities));
        info->identity_size = static_cast<uint32_t>(sizeof(openusd_physx_identity));
        info->scene_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_scene_desc));
        info->material_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_material_desc));
        info->shape_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_shape_desc));
        info->actor_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_actor_desc));
        info->actor_shape_ref_size = static_cast<uint32_t>(sizeof(openusd_physx_actor_shape_ref));
        info->joint_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_joint_desc));
        info->filter_pair_size = static_cast<uint32_t>(sizeof(openusd_physx_filter_pair));
        info->command_size = static_cast<uint32_t>(sizeof(openusd_physx_command));
        info->body_state_size = static_cast<uint32_t>(sizeof(openusd_physx_body_state));
        info->event_size = static_cast<uint32_t>(sizeof(openusd_physx_event));
        info->diagnostic_size = static_cast<uint32_t>(sizeof(openusd_physx_diagnostic));
        info->debug_line_size = static_cast<uint32_t>(sizeof(openusd_physx_debug_line));
        info->result_header_size = static_cast<uint32_t>(sizeof(openusd_physx_result_header));
        info->query_request_size = static_cast<uint32_t>(sizeof(openusd_physx_query_request));
        info->query_hit_size = static_cast<uint32_t>(sizeof(openusd_physx_query_hit));
        info->heightfield_sample_size = static_cast<uint32_t>(sizeof(openusd_physx_heightfield_sample));
        info->articulation_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_articulation_desc));
        info->articulation_link_desc_size =
            static_cast<uint32_t>(sizeof(openusd_physx_articulation_link_desc));
        info->controller_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_controller_desc));
    info->tendon_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_tendon_desc));
    info->tendon_node_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_tendon_node_desc));
    info->mimic_joint_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_mimic_joint_desc));
    info->vehicle_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_vehicle_desc));
    info->vehicle_wheel_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_vehicle_wheel_desc));
        info->particle_material_desc_size =
            static_cast<uint32_t>(sizeof(openusd_physx_particle_material_desc));
        info->particle_system_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_particle_system_desc));
        info->particle_body_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_particle_body_desc));
        info->deformable_material_desc_size =
            static_cast<uint32_t>(sizeof(openusd_physx_deformable_material_desc));
        info->deformable_desc_size = static_cast<uint32_t>(sizeof(openusd_physx_deformable_desc));
        info->deformation_state_size = static_cast<uint32_t>(sizeof(openusd_physx_deformation_state));
        info->page_alignment = OPENUSD_PHYSX_PAGE_ALIGNMENT;
        info->reserved = 0;
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_identity_compute(
    const char* path,
    size_t path_length,
    uint32_t instance_domain,
    uint32_t instance_index,
    uint64_t* id,
    openusd_physx_error_buffer* error)
{
    return openusd_physx_support::Guard(error, [&]() -> openusd_physx_status
    {
        if (id == nullptr)
        {
            WriteError(error, "An identity output is required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        *id = OPENUSD_PHYSX_INVALID_ID;
        if (path == nullptr || path_length == 0 || path[0] != '/' ||
            instance_domain >= static_cast<uint32_t>(OPENUSD_PHYSX_INSTANCE_DOMAIN_COUNT))
        {
            WriteError(error, "An absolute prim path and a known instance domain are required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (!IsValidUtf8(reinterpret_cast<const unsigned char*>(path), path_length))
        {
            WriteError(error, "The prim path must be UTF-8 without embedded null bytes.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        *id = ComputeIdentity(path, path_length, instance_domain, instance_index);
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_page_validate(
    const void* page,
    size_t page_size,
    openusd_physx_page_validation* validation,
    openusd_physx_error_buffer* error)
{
    return openusd_physx_support::Guard(error, [&]() -> openusd_physx_status
    {
        if (validation == nullptr)
        {
            WriteError(error, "A page validation output is required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        return openusd_physx_page::Validate(page, page_size, validation, nullptr, error);
    });
}
