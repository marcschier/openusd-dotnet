// Copyright (c) marcschier. Licensed under the MIT License.

// Contract probe for the retained physics world ABI. It exercises the exact
// version negotiation, the identity model, and every structural and semantic
// rule of the pointer free build page. It links only the page validator, so it
// builds and runs on machines without the PhysX SDK.

#include "page_builder.h"

#include "openusd_physx_support.h"

#include <cmath>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

namespace
{
using openusd_physx_test::MakeGpuDomainScene;
using openusd_physx_test::MakeReferenceScene;
using openusd_physx_test::PageBuilder;

int g_failures = 0;

bool Expect(bool condition, const std::string& description)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << description << '\n';
        ++g_failures;
    }
    return condition;
}

openusd_physx_page_validation MakeValidation() noexcept
{
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    return validation;
}

openusd_physx_build_page_header* HeaderOf(std::vector<uint64_t>& page) noexcept
{
    return reinterpret_cast<openusd_physx_build_page_header*>(page.data());
}

unsigned char* BytesOf(std::vector<uint64_t>& page) noexcept
{
    return reinterpret_cast<unsigned char*>(page.data());
}

template <typename TRecord>
TRecord ReadRecord(std::vector<uint64_t>& page, const openusd_physx_page_span& span, size_t index) noexcept
{
    TRecord record{};
    std::memcpy(&record, BytesOf(page) + span.offset + index * sizeof(TRecord), sizeof(TRecord));
    return record;
}

template <typename TRecord>
void WriteRecord(std::vector<uint64_t>& page, const openusd_physx_page_span& span, size_t index, const TRecord& record) noexcept
{
    std::memcpy(BytesOf(page) + span.offset + index * sizeof(TRecord), &record, sizeof(TRecord));
}

void ExpectRejected(
    PageBuilder& builder,
    const std::string& description,
    openusd_physx_page_error expected_error,
    openusd_physx_page_section expected_section,
    void (*mutate)(std::vector<uint64_t>&))
{
    std::vector<uint64_t> page = builder.Build();
    const size_t size = builder.Size();
    mutate(page);
    openusd_physx_page_validation validation = MakeValidation();
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};
    const openusd_physx_status status = openusd_physx_page_validate(page.data(), size, &validation, &error);
    const bool rejected = Expect(
        status == OPENUSD_PHYSX_STATUS_INVALID_PAGE,
        description + " must be rejected as an invalid page");
    const bool coded = Expect(
        validation.error_code == static_cast<uint32_t>(expected_error),
        description + " must report page error " + std::to_string(static_cast<int>(expected_error)) +
            " but reported " + std::to_string(validation.error_code));
    const bool sectioned = Expect(
        validation.section == static_cast<uint32_t>(expected_section),
        description + " must report section " + std::to_string(static_cast<int>(expected_section)) +
            " but reported " + std::to_string(validation.section));
    if (!(rejected && coded && sectioned))
    {
        std::cerr << "  reported message: " << error_data << '\n';
    }
    else
    {
        Expect(std::strlen(error_data) != 0, description + " must report a diagnostic message");
    }
}

void ExpectAccepted(
    PageBuilder& builder,
    const std::string& description,
    void (*mutate)(std::vector<uint64_t>&))
{
    std::vector<uint64_t> page = builder.Build();
    const size_t size = builder.Size();
    mutate(page);
    openusd_physx_page_validation validation = MakeValidation();
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};
    const openusd_physx_status status = openusd_physx_page_validate(page.data(), size, &validation, &error);
    if (!Expect(status == OPENUSD_PHYSX_STATUS_OK, description + " must be accepted"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
    }
}

void CheckAbiInfo()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};
    openusd_physx_abi_info info{};
    info.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_abi_info));
    Expect(
        openusd_physx_world_get_abi(&info, &error) == OPENUSD_PHYSX_STATUS_OK,
        "openusd_physx_world_get_abi must succeed for a matching structure size");
    Expect(info.abi_version == OPENUSD_PHYSX_WORLD_ABI_VERSION, "The reported ABI version must be exact");
    Expect(info.page_magic == OPENUSD_PHYSX_PAGE_MAGIC, "The reported page magic must match the header");
    Expect(info.page_alignment == OPENUSD_PHYSX_PAGE_ALIGNMENT, "The reported page alignment must match the header");
    Expect(info.build_page_header_size == 352, "The build page header must stay 352 bytes");
    Expect(info.page_span_size == 8, "The page span must stay 8 bytes");
    Expect(info.capacities_size == 32, "The result capacities must stay 32 bytes");
    Expect(info.identity_size == 24, "The identity record must stay 24 bytes");
    Expect(info.scene_desc_size == 48, "The scene record must stay 48 bytes");
    Expect(info.material_desc_size == 40, "The material record must stay 40 bytes");
    Expect(info.shape_desc_size == 144, "The shape record must stay 144 bytes");
    Expect(info.actor_desc_size == 184, "The actor record must stay 184 bytes");
    Expect(info.actor_shape_ref_size == 8, "The actor shape reference must stay 8 bytes");
    Expect(info.joint_desc_size == 408, "The joint record must stay 408 bytes");
    Expect(info.filter_pair_size == 8, "The filter pair must stay 8 bytes");
    Expect(info.command_size == 80, "The command record must stay 80 bytes");
    Expect(info.body_state_size == 72, "The body state record must stay 72 bytes");
    Expect(info.event_size == 80, "The event record must stay 80 bytes");
    Expect(info.diagnostic_size == 208, "The diagnostic record must stay 208 bytes");
    Expect(info.debug_line_size == 32, "The debug line record must stay 32 bytes");
    Expect(info.result_header_size == 88, "The result header must stay 88 bytes");
    Expect(info.query_request_size == 96, "The query request must stay 96 bytes");
    Expect(info.query_hit_size == 64, "The query hit must stay 64 bytes");
    Expect(info.heightfield_sample_size == 4, "The height field sample must stay 4 bytes");
    Expect(info.articulation_desc_size == 64, "The articulation record must stay 64 bytes");
    Expect(info.articulation_link_desc_size == 432, "The articulation link record must stay 432 bytes");
    Expect(info.controller_desc_size == 112, "The controller record must stay 112 bytes");
    Expect(info.tendon_desc_size == 64, "The tendon record must stay 64 bytes");
    Expect(info.tendon_node_desc_size == 64, "The tendon node record must stay 64 bytes");
    Expect(info.mimic_joint_desc_size == 64, "The mimic joint record must stay 64 bytes");
    Expect(info.vehicle_desc_size == 160, "The vehicle record must stay 160 bytes");
    Expect(info.vehicle_wheel_desc_size == 168, "The vehicle wheel record must stay 168 bytes");
    Expect(info.particle_material_desc_size == 72, "The particle material record must stay 72 bytes");
    Expect(info.particle_system_desc_size == 80, "The particle system record must stay 80 bytes");
    Expect(info.particle_body_desc_size == 72, "The particle body record must stay 72 bytes");
    Expect(info.deformable_material_desc_size == 56, "The deformable material record must stay 56 bytes");
    Expect(info.deformable_desc_size == 128, "The deformable record must stay 128 bytes");
    Expect(info.deformation_state_size == 32, "The deformation state record must stay 32 bytes");

    openusd_physx_abi_info stale{};
    stale.struct_size = 8;
    Expect(
        openusd_physx_world_get_abi(&stale, &error) == OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
        "A mismatched ABI information structure size must be refused");
    Expect(
        openusd_physx_world_get_abi(nullptr, &error) == OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "A null ABI information output must be refused");
}

void CheckIdentities()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};
    const std::string path = "/World/Box";
    uint64_t first = 0;
    uint64_t second = 0;
    uint64_t instanced = 0;
    uint64_t other_domain = 0;
    Expect(
        openusd_physx_identity_compute(path.c_str(), path.size(), OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0, &first, &error) ==
            OPENUSD_PHYSX_STATUS_OK,
        "A prim path identity must be computable");
    Expect(
        openusd_physx_identity_compute(path.c_str(), path.size(), OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0, &second, &error) ==
            OPENUSD_PHYSX_STATUS_OK,
        "A prim path identity must be recomputable");
    Expect(first == second && first != OPENUSD_PHYSX_INVALID_ID, "Identities must be stable and never zero");
    Expect(
        openusd_physx_identity_compute(path.c_str(), path.size(), OPENUSD_PHYSX_INSTANCE_DOMAIN_POINT_INSTANCER, 3, &instanced, &error) ==
            OPENUSD_PHYSX_STATUS_OK &&
            instanced != first,
        "The instance index must change the identity");
    Expect(
        openusd_physx_identity_compute(path.c_str(), path.size(), OPENUSD_PHYSX_INSTANCE_DOMAIN_NATIVE_INSTANCE, 0, &other_domain, &error) ==
            OPENUSD_PHYSX_STATUS_OK &&
            other_domain != first,
        "The instance domain must change the identity");

    uint64_t rejected = 1;
    Expect(
        openusd_physx_identity_compute("World/Box", 9, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0, &rejected, &error) ==
            OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT &&
            rejected == OPENUSD_PHYSX_INVALID_ID,
        "A relative path must be refused and must clear the output");
    Expect(
        openusd_physx_identity_compute(path.c_str(), path.size(), OPENUSD_PHYSX_INSTANCE_DOMAIN_COUNT, 0, &rejected, &error) ==
            OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "An unknown instance domain must be refused");
    Expect(
        openusd_physx_identity_compute(nullptr, 0, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0, &rejected, &error) ==
            OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "A null path must be refused");
    Expect(
        openusd_physx_identity_compute(path.c_str(), path.size(), OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0, nullptr, &error) ==
            OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "A null identity output must be refused");
}

void CheckValidPages()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    PageBuilder builder = MakeReferenceScene();
    std::vector<uint64_t> page = builder.Build();
    openusd_physx_page_validation validation = MakeValidation();
    const openusd_physx_status status =
        openusd_physx_page_validate(page.data(), builder.Size(), &validation, &error);
    if (!Expect(status == OPENUSD_PHYSX_STATUS_OK, "The reference scene page must validate"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
    }
    Expect(validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_NONE, "A valid page must report no page error");
    Expect(validation.scene_count == 1, "The reference scene must declare one scene");
    Expect(validation.material_count == 2, "The reference scene must declare two materials");
    Expect(validation.shape_count == 4, "The reference scene must declare four shapes");
    Expect(validation.actor_count == 4, "The reference scene must declare four actors");
    Expect(validation.dynamic_actor_count == 2, "The reference scene must declare two movable actors");
    Expect(validation.joint_count == 1, "The reference scene must declare one joint");
    Expect(validation.filter_pair_count == 1, "The reference scene must declare one filtered pair");
    Expect(validation.identity_count == 12, "The reference scene must declare twelve identities");
    Expect(validation.revision == 1, "The validator must echo the page revision");
    Expect(validation.capacities.max_body_states == 8, "The validator must echo the declared capacities");

    PageBuilder empty;
    std::vector<uint64_t> empty_page = empty.Build();
    openusd_physx_page_validation empty_validation = MakeValidation();
    Expect(
        openusd_physx_page_validate(empty_page.data(), empty.Size(), &empty_validation, &error) ==
            OPENUSD_PHYSX_STATUS_OK,
        "An empty page must validate");
    Expect(empty_validation.actor_count == 0, "An empty page must report no actors");
}

void CheckArgumentRules()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};
    PageBuilder builder = MakeReferenceScene();
    std::vector<uint64_t> page = builder.Build();

    openusd_physx_page_validation validation = MakeValidation();
    Expect(
        openusd_physx_page_validate(nullptr, builder.Size(), &validation, &error) == OPENUSD_PHYSX_STATUS_INVALID_PAGE &&
            validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_NULL,
        "A null page must be reported as a null page error");

    validation = MakeValidation();
    Expect(
        openusd_physx_page_validate(BytesOf(page) + 4, builder.Size() - 4, &validation, &error) ==
                OPENUSD_PHYSX_STATUS_INVALID_PAGE &&
            validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_ALIGNMENT,
        "A misaligned page must be reported as an alignment error");

    validation = MakeValidation();
    Expect(
        openusd_physx_page_validate(page.data(), 16, &validation, &error) == OPENUSD_PHYSX_STATUS_INVALID_PAGE &&
            validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_SIZE,
        "A truncated page must be reported as a size error");

    openusd_physx_page_validation stale{};
    stale.struct_size = 4;
    Expect(
        openusd_physx_page_validate(page.data(), builder.Size(), &stale, &error) == OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
        "A mismatched validation structure size must be refused");
    Expect(
        openusd_physx_page_validate(page.data(), builder.Size(), nullptr, &error) == OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "A null validation output must be refused");
}

// A reference scene plus one two link articulation, so the duplicate identity
// rules can be exercised across actors and links in the same page. The chain is
// the smallest one the page contract accepts: a root with no inbound joint and
// one revolute child.
PageBuilder MakeArticulatedScene()
{
    PageBuilder builder = MakeReferenceScene();

    const uint32_t shape_offset = static_cast<uint32_t>(builder.ActorShapes().size());
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});

    openusd_physx_articulation_desc articulation{};
    articulation.id = builder.AddIdentity("/World/Arm", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    articulation.scene_index = 0;
    articulation.flags = OPENUSD_PHYSX_ARTICULATION_FLAG_FIXED_BASE;
    articulation.link_offset = 0;
    articulation.link_count = 2;
    articulation.position_iterations = 16;
    articulation.velocity_iterations = 4;
    builder.Articulations().push_back(articulation);

    uint64_t parent_id = 0;
    for (uint32_t index = 0; index < 2; ++index)
    {
        openusd_physx_articulation_link_desc link{};
        link.id = builder
                      .AddIdentity("/World/Arm/Link" + std::to_string(index), OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0)
                      .id;
        link.parent_id = parent_id;
        link.parent_frame = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        link.child_frame = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        link.world_pose = openusd_physx_test::Pose(static_cast<float>(index), 5.0F, 0.0F);
        link.mass = 1.0F;
        link.shape_offset = shape_offset + index;
        link.shape_count = 1;
        if (index == 0)
        {
            link.joint_type = OPENUSD_PHYSX_ARTICULATION_JOINT_NONE;
        }
        else
        {
            link.joint_type = OPENUSD_PHYSX_ARTICULATION_JOINT_REVOLUTE;
            link.parent_frame.position = openusd_physx_vec3f{0.5F, 0.0F, 0.0F};
            link.child_frame.position = openusd_physx_vec3f{-0.5F, 0.0F, 0.0F};
            link.motion[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = OPENUSD_PHYSX_JOINT_MOTION_FREE;
        }
        parent_id = link.id;
        builder.ArticulationLinks().push_back(link);
    }

    return builder;
}

void CheckRejectedPages()
{
    PageBuilder builder = MakeReferenceScene();

    ExpectRejected(builder, "A wrong magic", OPENUSD_PHYSX_PAGE_ERROR_MAGIC, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->magic ^= 1ULL; });
    ExpectRejected(builder, "A future ABI version", OPENUSD_PHYSX_PAGE_ERROR_ABI, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION + 1u; });
    ExpectRejected(builder, "A wrong header size", OPENUSD_PHYSX_PAGE_ERROR_HEADER_SIZE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->header_size = 200; });
    ExpectRejected(builder, "A byte size that disagrees with the buffer", OPENUSD_PHYSX_PAGE_ERROR_SIZE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->byte_size += 8; });
    ExpectRejected(builder, "A zero meters per unit", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->meters_per_unit = 0.0; });
    ExpectRejected(builder, "An inverted time range", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->end_time_code = -1.0; });
    ExpectRejected(builder, "An unknown up axis", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->up_axis = 7; });
    ExpectRejected(builder, "A simulation rate below the clamp", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->simulation_rate_hz = 10; });
    ExpectRejected(builder, "A substep limit above the maximum", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->max_substeps = OPENUSD_PHYSX_MAX_SUBSTEPS + 1u; });
    ExpectRejected(builder, "A non zero reserved header field", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_HEADER,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->reserved[2] = 1; });
    ExpectRejected(builder, "A misaligned section offset", OPENUSD_PHYSX_PAGE_ERROR_ALIGNMENT, OPENUSD_PHYSX_SECTION_IDENTITIES,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->identities.offset += 4; });
    ExpectRejected(builder, "A section that leaves the page", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ACTORS,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->actors.offset = static_cast<uint32_t>(HeaderOf(page)->byte_size - 8); });
    ExpectRejected(builder, "An empty section with a non zero offset", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_FILTER_PAIRS,
        [](std::vector<uint64_t>& page)
        {
            HeaderOf(page)->filter_pairs.count = 0;
            HeaderOf(page)->filter_pairs.offset = 256;
        });
    ExpectRejected(builder, "Two overlapping sections", OPENUSD_PHYSX_PAGE_ERROR_OVERLAP, OPENUSD_PHYSX_SECTION_MATERIALS,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->materials.offset = HeaderOf(page)->scenes.offset + 8u; });
    ExpectRejected(builder, "A string section that is not UTF-8", OPENUSD_PHYSX_PAGE_ERROR_ENCODING, OPENUSD_PHYSX_SECTION_STRINGS,
        [](std::vector<uint64_t>& page) { BytesOf(page)[HeaderOf(page)->string_bytes.offset] = 0xFFU; });
    ExpectRejected(builder, "A duplicated identity", OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_IDENTITIES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->identities;
            openusd_physx_identity first = ReadRecord<openusd_physx_identity>(page, span, 0);
            openusd_physx_identity second = ReadRecord<openusd_physx_identity>(page, span, 1);
            second.id = first.id;
            second.path_offset = first.path_offset;
            second.path_length = first.path_length;
            second.instance_domain = first.instance_domain;
            second.instance_index = first.instance_index;
            WriteRecord(page, span, 1, second);
        });
    ExpectRejected(builder, "An identity that is not derived from its path", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_IDENTITIES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->identities;
            openusd_physx_identity identity = ReadRecord<openusd_physx_identity>(page, span, 2);
            identity.id ^= 0x5555ULL;
            WriteRecord(page, span, 2, identity);
        });
    ExpectRejected(builder, "A restitution above one", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_MATERIALS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->materials;
            openusd_physx_material_desc material = ReadRecord<openusd_physx_material_desc>(page, span, 0);
            material.restitution = 2.0F;
            WriteRecord(page, span, 0, material);
        });
    ExpectRejected(builder, "A shape material that does not exist", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_SHAPES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->shapes;
            openusd_physx_shape_desc shape = ReadRecord<openusd_physx_shape_desc>(page, span, 0);
            shape.material_index = 99;
            WriteRecord(page, span, 0, shape);
        });
    ExpectRejected(builder, "A non positive shape scale", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->shapes;
            openusd_physx_shape_desc shape = ReadRecord<openusd_physx_shape_desc>(page, span, 1);
            shape.scale.y = 0.0F;
            WriteRecord(page, span, 1, shape);
        });
    ExpectRejected(builder, "An analytic shape that references mesh data", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->shapes;
            openusd_physx_shape_desc shape = ReadRecord<openusd_physx_shape_desc>(page, span, 2);
            shape.point_count = 4;
            WriteRecord(page, span, 2, shape);
        });
    ExpectRejected(builder, "A triangle count that is not a multiple of three", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_SHAPES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->shapes;
            openusd_physx_shape_desc shape = ReadRecord<openusd_physx_shape_desc>(page, span, 3);
            shape.index_count = 5;
            WriteRecord(page, span, 3, shape);
        });
    ExpectRejected(builder, "A vertex index outside the shape point range", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_MESH_INDICES,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->mesh_indices;
            const uint32_t index = 9;
            WriteRecord(page, span, 0, index);
        });
    ExpectRejected(builder, "A static actor with a velocity", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->actors;
            openusd_physx_actor_desc actor = ReadRecord<openusd_physx_actor_desc>(page, span, 0);
            actor.linear_velocity.y = 1.0F;
            WriteRecord(page, span, 0, actor);
        });
    ExpectRejected(builder, "A movable actor with a triangle mesh shape", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_ACTORS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->actors;
            openusd_physx_actor_desc actor = ReadRecord<openusd_physx_actor_desc>(page, span, 3);
            actor.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
            WriteRecord(page, span, 3, actor);
        });
    ExpectRejected(builder, "An actor without a scene", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_ACTORS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->actors;
            openusd_physx_actor_desc actor = ReadRecord<openusd_physx_actor_desc>(page, span, 1);
            actor.scene_index = -1;
            WriteRecord(page, span, 1, actor);
        });
    ExpectRejected(builder, "An actor without shapes", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_ACTORS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->actors;
            openusd_physx_actor_desc actor = ReadRecord<openusd_physx_actor_desc>(page, span, 1);
            actor.shape_count = 0;
            WriteRecord(page, span, 1, actor);
        });
    ExpectRejected(builder, "A joint that references a missing actor", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_JOINTS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->joints;
            openusd_physx_joint_desc joint = ReadRecord<openusd_physx_joint_desc>(page, span, 0);
            joint.actor1_index = 99;
            WriteRecord(page, span, 0, joint);
        });
    ExpectRejected(builder, "A joint with an inverted limit range", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_JOINTS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->joints;
            openusd_physx_joint_desc joint = ReadRecord<openusd_physx_joint_desc>(page, span, 0);
            joint.lower_limit = 2.0F;
            joint.upper_limit = -2.0F;
            WriteRecord(page, span, 0, joint);
        });
    ExpectRejected(builder, "A filtered pair of one actor", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_FILTER_PAIRS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->filter_pairs;
            const openusd_physx_filter_pair pair{1, 1};
            WriteRecord(page, span, 0, pair);
        });
    ExpectRejected(builder, "A body state capacity below the movable actor count", OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->capacities.max_body_states = 1; });
    ExpectRejected(builder, "A capacity above the supported maximum", OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->capacities.max_events = OPENUSD_PHYSX_MAX_RESULT_CAPACITY + 1u; });
    ExpectRejected(builder, "A non zero reserved capacity field", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_CAPACITIES,
        [](std::vector<uint64_t>& page) { HeaderOf(page)->capacities.reserved0 = 3; });

    /* An actor and an articulation link are both published as a body state keyed
     * by identity and both are resolved from that identity when a command
     * arrives, so a page that gives two of them one address gives the world two
     * bodies the caller cannot tell apart. The composer refuses to produce such
     * a page, but a caller may hand one to the world directly, so the page
     * contract has to refuse it too - and in the same order the managed
     * validator uses: every actor first, then every link. */
    ExpectRejected(builder, "Two actors that share an identity", OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_ACTORS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->actors;
            openusd_physx_actor_desc first = ReadRecord<openusd_physx_actor_desc>(page, span, 0);
            openusd_physx_actor_desc second = ReadRecord<openusd_physx_actor_desc>(page, span, 1);
            second.id = first.id;
            WriteRecord(page, span, 1, second);
        });

    PageBuilder articulated = MakeArticulatedScene();
    ExpectAccepted(articulated, "A scene with one articulation", [](std::vector<uint64_t>&) { });
    ExpectRejected(articulated, "An articulation link that shares an actor identity", OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_actor_desc actor =
                ReadRecord<openusd_physx_actor_desc>(page, HeaderOf(page)->actors, 0);
            const openusd_physx_page_span span = HeaderOf(page)->articulation_links;
            /* The LEAF is renamed rather than the root: the root's identity is
             * the parent_id of the link below it, so renaming the root would
             * orphan that reference and trip the inbound joint rule first. The
             * leaf is named by nothing, so the duplicate is the only fault the
             * page carries and the check under test is the one that reports. */
            openusd_physx_articulation_link_desc leaf =
                ReadRecord<openusd_physx_articulation_link_desc>(page, span, 1);
            leaf.id = actor.id;
            WriteRecord(page, span, 1, leaf);
        });
    ExpectRejected(articulated, "Two articulation links that share an identity", OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID, OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS,
        [](std::vector<uint64_t>& page)
        {
            const openusd_physx_page_span span = HeaderOf(page)->articulation_links;
            openusd_physx_articulation_link_desc root =
                ReadRecord<openusd_physx_articulation_link_desc>(page, span, 0);
            openusd_physx_articulation_link_desc child =
                ReadRecord<openusd_physx_articulation_link_desc>(page, span, 1);
            child.id = root.id;
            WriteRecord(page, span, 1, child);
        });
}

// The mass frame rotation of an actor is optional, so the page contract, the
// page validator and every consumer of the page must agree on one rule: an all
// zero quaternion stands for the identity, a quaternion whose squared length
// stays inside a quarter and four is legal and is only normalized, and anything
// else falls back to the identity. A legal quaternion that is not unit length
// must never lose its orientation.
void CheckMassFrameRotation()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    const openusd_physx_quatf identity{0.0F, 0.0F, 0.0F, 1.0F};
    const openusd_physx_quatf unset{0.0F, 0.0F, 0.0F, 0.0F};
    const openusd_physx_quatf legal_non_unit{0.0F, 0.0F, 0.9F, 0.9F};
    const openusd_physx_quatf oversized{0.0F, 0.0F, 3.0F, 3.0F};

    Expect(
        openusd_physx_support::IsUnsetOrUsableRotation(unset),
        "An unset mass frame rotation must be accepted");
    Expect(
        openusd_physx_support::IsUnsetOrUsableRotation(legal_non_unit),
        "A legal mass frame rotation that is not unit length must be accepted");
    Expect(
        !openusd_physx_support::IsUnsetOrUsableRotation(oversized),
        "A mass frame rotation that is far from unit length must be refused");

    const openusd_physx_quatf resolved_unset =
        openusd_physx_support::ResolveRotationOrIdentity(unset);
    Expect(
        resolved_unset.x == identity.x && resolved_unset.y == identity.y &&
            resolved_unset.z == identity.z && resolved_unset.w == identity.w,
        "An unset mass frame rotation must resolve to the identity");

    const openusd_physx_quatf resolved_oversized =
        openusd_physx_support::ResolveRotationOrIdentity(oversized);
    Expect(
        resolved_oversized.x == identity.x && resolved_oversized.y == identity.y &&
            resolved_oversized.z == identity.z && resolved_oversized.w == identity.w,
        "A refused mass frame rotation must resolve to the identity");

    // A quarter turn about Z authored with a length of about 1.27.
    const openusd_physx_quatf resolved =
        openusd_physx_support::ResolveRotationOrIdentity(legal_non_unit);
    const double length = std::sqrt(
        static_cast<double>(resolved.x) * static_cast<double>(resolved.x) +
        static_cast<double>(resolved.y) * static_cast<double>(resolved.y) +
        static_cast<double>(resolved.z) * static_cast<double>(resolved.z) +
        static_cast<double>(resolved.w) * static_cast<double>(resolved.w));
    Expect(std::abs(length - 1.0) < 1e-6, "A resolved mass frame rotation must be unit length");
    Expect(
        std::abs(static_cast<double>(resolved.z) - 0.70710678) < 1e-6 &&
            std::abs(static_cast<double>(resolved.w) - 0.70710678) < 1e-6,
        "A legal mass frame rotation must keep its orientation and only be normalized");
    Expect(
        std::abs(static_cast<double>(resolved.z)) > 0.5,
        "A legal mass frame rotation must not collapse into the identity");

    PageBuilder accepted = MakeReferenceScene();
    accepted.Actors()[1].principal_axes = legal_non_unit;
    accepted.Actors()[1].inertia = openusd_physx_vec3f{1.0F, 2.0F, 3.0F};
    std::vector<uint64_t> accepted_page = accepted.Build();
    openusd_physx_page_validation accepted_validation = MakeValidation();
    if (!Expect(
            openusd_physx_page_validate(
                accepted_page.data(), accepted.Size(), &accepted_validation, &error) ==
                OPENUSD_PHYSX_STATUS_OK,
            "A page whose actor states a legal non unit mass frame rotation must validate"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
    }

    PageBuilder refused = MakeReferenceScene();
    refused.Actors()[1].principal_axes = oversized;
    std::vector<uint64_t> refused_page = refused.Build();
    openusd_physx_page_validation refused_validation = MakeValidation();
    Expect(
        openusd_physx_page_validate(
            refused_page.data(), refused.Size(), &refused_validation, &error) ==
                OPENUSD_PHYSX_STATUS_INVALID_PAGE &&
            refused_validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_VALUE &&
            refused_validation.section == OPENUSD_PHYSX_SECTION_ACTORS,
        "A page whose actor states an unusable mass frame rotation must be refused");
}

// The CUDA accelerated domains are page content like any other: they are
// validated whole, before any runtime decides whether it can reach a device, so
// a malformed particle system or deformable is refused with an exact section
// and error code rather than being handed to the simulation SDK.
void CheckGpuDomainPages()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    PageBuilder builder = MakeGpuDomainScene();
    std::vector<uint64_t> page = builder.Build();
    openusd_physx_page_validation validation = MakeValidation();
    if (!Expect(
            openusd_physx_page_validate(page.data(), builder.Size(), &validation, &error) ==
                OPENUSD_PHYSX_STATUS_OK,
            "The GPU domain page must validate"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
    }
    Expect(
        validation.capacities.max_deformation_bodies == 8 &&
            validation.capacities.max_deformation_points == 256,
        "The validator must echo the declared deformation capacities");

    ExpectRejected(builder, "A particle system with unknown flags", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_systems;
            openusd_physx_particle_system_desc system =
                ReadRecord<openusd_physx_particle_system_desc>(page_bytes, span, 0);
            system.flags = 0x80u;
            WriteRecord(page_bytes, span, 0, system);
        });
    ExpectRejected(builder, "A particle system naming a missing scene", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_systems;
            openusd_physx_particle_system_desc system =
                ReadRecord<openusd_physx_particle_system_desc>(page_bytes, span, 0);
            system.scene_index = 9;
            WriteRecord(page_bytes, span, 0, system);
        });
    ExpectRejected(builder, "A particle system whose rest offset exceeds its contact offset", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_systems;
            openusd_physx_particle_system_desc system =
                ReadRecord<openusd_physx_particle_system_desc>(page_bytes, span, 0);
            system.solid_rest_offset = system.particle_contact_offset + 1.0F;
            WriteRecord(page_bytes, span, 0, system);
        });
    ExpectRejected(builder, "A particle system whose body window leaves an orphan body", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_systems;
            openusd_physx_particle_system_desc system =
                ReadRecord<openusd_physx_particle_system_desc>(page_bytes, span, 0);
            system.body_count = 1;
            WriteRecord(page_bytes, span, 0, system);
        });
    ExpectRejected(builder, "A particle body whose point window leaves the mesh point section", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_bodies;
            openusd_physx_particle_body_desc body =
                ReadRecord<openusd_physx_particle_body_desc>(page_bytes, span, 0);
            body.point_count = HeaderOf(page_bytes)->mesh_points.count + 1u;
            WriteRecord(page_bytes, span, 0, body);
        });
    ExpectRejected(builder, "A particle body naming a missing particle material", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_bodies;
            openusd_physx_particle_body_desc body =
                ReadRecord<openusd_physx_particle_body_desc>(page_bytes, span, 0);
            body.material_index = 7;
            WriteRecord(page_bytes, span, 0, body);
        });
    /* The runtime packs the collision group, the behaviour flags, and the bound
     * material index into one phase lookup key. A group wider than the twenty
     * bits a phase reserves used to reach the bits the material index occupies,
     * so two bodies that share nothing could collide on the same key and be
     * given the same phase. Both of these were accepted before the bound
     * existed; the first is exactly the group that aliased material index zero. */
    ExpectRejected(builder, "A particle body whose collision group aliases the material field", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_bodies;
            openusd_physx_particle_body_desc body =
                ReadRecord<openusd_physx_particle_body_desc>(page_bytes, span, 0);
            body.material_index = -1;
            body.particle_group = 1u << 24;
            WriteRecord(page_bytes, span, 0, body);
        });
    ExpectRejected(builder, "A particle body one group past the twenty bit phase group", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_PARTICLE_BODIES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_bodies;
            openusd_physx_particle_body_desc body =
                ReadRecord<openusd_physx_particle_body_desc>(page_bytes, span, 0);
            body.particle_group = OPENUSD_PHYSX_MAX_PARTICLE_GROUP + 1u;
            WriteRecord(page_bytes, span, 0, body);
        });
    ExpectAccepted(builder, "A particle body on the last usable collision group",
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->particle_bodies;
            openusd_physx_particle_body_desc body =
                ReadRecord<openusd_physx_particle_body_desc>(page_bytes, span, 0);
            body.particle_group = OPENUSD_PHYSX_MAX_PARTICLE_GROUP;
            WriteRecord(page_bytes, span, 0, body);
        });
    ExpectRejected(builder, "A deformable material with an incompressible Poisson ratio", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformable_materials;
            openusd_physx_deformable_material_desc material =
                ReadRecord<openusd_physx_deformable_material_desc>(page_bytes, span, 0);
            material.poissons_ratio = 0.5F;
            WriteRecord(page_bytes, span, 0, material);
        });
    ExpectRejected(builder, "A volume material carrying a surface shell", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformable_materials;
            openusd_physx_deformable_material_desc material =
                ReadRecord<openusd_physx_deformable_material_desc>(page_bytes, span, 1);
            material.thickness = 0.01F;
            WriteRecord(page_bytes, span, 1, material);
        });
    ExpectRejected(builder, "A deformable bound to a material of the other kind", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformables;
            openusd_physx_deformable_desc deformable =
                ReadRecord<openusd_physx_deformable_desc>(page_bytes, span, 0);
            deformable.material_index = 1;
            WriteRecord(page_bytes, span, 0, deformable);
        });
    ExpectRejected(builder, "A surface whose index window is not whole triangles", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_DEFORMABLES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformables;
            openusd_physx_deformable_desc deformable =
                ReadRecord<openusd_physx_deformable_desc>(page_bytes, span, 0);
            deformable.index_count -= 1u;
            WriteRecord(page_bytes, span, 0, deformable);
        });
    ExpectRejected(builder, "A surface index outside its own point window", OPENUSD_PHYSX_PAGE_ERROR_REFERENCE, OPENUSD_PHYSX_SECTION_DEFORMABLES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformables;
            const openusd_physx_deformable_desc deformable =
                ReadRecord<openusd_physx_deformable_desc>(page_bytes, span, 0);
            const openusd_physx_page_span indices = HeaderOf(page_bytes)->mesh_indices;
            WriteRecord(page_bytes, indices, deformable.index_offset, deformable.point_count);
        });
    ExpectRejected(builder, "A surface declaring a collision mesh", OPENUSD_PHYSX_PAGE_ERROR_VALUE, OPENUSD_PHYSX_SECTION_DEFORMABLES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformables;
            openusd_physx_deformable_desc deformable =
                ReadRecord<openusd_physx_deformable_desc>(page_bytes, span, 0);
            deformable.collision_point_count = 4;
            WriteRecord(page_bytes, span, 0, deformable);
        });
    ExpectRejected(builder, "A volume whose index window is not whole tetrahedra", OPENUSD_PHYSX_PAGE_ERROR_RANGE, OPENUSD_PHYSX_SECTION_DEFORMABLES,
        [](std::vector<uint64_t>& page_bytes)
        {
            const openusd_physx_page_span span = HeaderOf(page_bytes)->deformables;
            openusd_physx_deformable_desc deformable =
                ReadRecord<openusd_physx_deformable_desc>(page_bytes, span, 1);
            deformable.index_count -= 1u;
            WriteRecord(page_bytes, span, 1, deformable);
        });
    ExpectRejected(builder, "A deformation body capacity below the declared GPU objects", OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES,
        [](std::vector<uint64_t>& page_bytes) { HeaderOf(page_bytes)->capacities.max_deformation_bodies = 1; });
    ExpectRejected(builder, "A deformation point capacity below the declared vertices", OPENUSD_PHYSX_PAGE_ERROR_CAPACITY, OPENUSD_PHYSX_SECTION_CAPACITIES,
        [](std::vector<uint64_t>& page_bytes) { HeaderOf(page_bytes)->capacities.max_deformation_points = 4; });
}
}

int main()
{
    CheckAbiInfo();
    CheckIdentities();
    CheckValidPages();
    CheckArgumentRules();
    CheckRejectedPages();
    CheckGpuDomainPages();
    CheckMassFrameRotation();

    if (g_failures != 0)
    {
        std::cerr << g_failures << " physics ABI contract check(s) failed.\n";
        return 1;
    }
    std::cout << "openusd_physx retained world ABI contract checks passed.\n";
    return 0;
}
