// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_gpu.h"

#include "openusd_physx_cuda.h"
#include "openusd_physx_runtime.h"
#include "openusd_physx_support.h"
#include "openusd_physx_translate.h"

#if PX_SUPPORT_GPU_PHYSX
#include <cooking/PxCooking.h>
#include <extensions/PxCudaHelpersExt.h>
#include <extensions/PxDeformableSurfaceExt.h>
#include <extensions/PxDeformableVolumeExt.h>
#endif

#include <cmath>
#include <cstring>
#include <map>
#include <utility>

namespace
{
using namespace physx;

#if PX_SUPPORT_GPU_PHYSX
constexpr float kDefaultParticleDensity = 1000.0F;
constexpr float kDefaultDeformableDensity = 1000.0F;
constexpr float kDefaultRestOffset = 0.05F;
constexpr float kDefaultShellThickness = 0.001F;

// Places one authored local point in the world frame the page declared.
PxVec3 PlacePoint(const openusd_physx_transform& pose, const openusd_physx_vec3f& point)
{
    const PxQuat rotation = openusd_physx_translate::ToPx(
        openusd_physx_support::ResolveRotationOrIdentity(pose.rotation));
    return openusd_physx_translate::ToPx(pose.position) + rotation.rotate(openusd_physx_translate::ToPx(point));
}

PxTransform PlaceFrame(const openusd_physx_transform& pose)
{
    return PxTransform(
        openusd_physx_translate::ToPx(pose.position),
        openusd_physx_translate::ToPx(openusd_physx_support::ResolveRotationOrIdentity(pose.rotation)));
}
#endif
}

namespace openusd_physx_gpu
{
struct Content::Impl
{
    Counts counts;
#if PX_SUPPORT_GPU_PHYSX
    // One published vertex window plus everything needed to read it back.
    struct Body
    {
        uint64_t id = OPENUSD_PHYSX_INVALID_ID;
        uint32_t kind = OPENUSD_PHYSX_DEFORMATION_PARTICLES;
        uint32_t vertex_count = 0;
        PxParticleBuffer* particles = nullptr;
        PxDeformableSurface* surface = nullptr;
        PxDeformableVolume* volume = nullptr;
        // Host mirrors the build filled and a reset restores from. They are
        // pinned host memory owned by the context manager, so they are released
        // through it and never with the default allocator.
        PxVec4* mirror_positions = nullptr;
        PxVec4* mirror_velocities = nullptr;
        PxVec4* mirror_rest = nullptr;
        PxVec4* mirror_collision = nullptr;
        std::vector<PxVec4> initial_positions;
        std::vector<PxVec4> initial_velocities;
        std::vector<PxU32> initial_phases;
    };

    PxCudaContextManager* cuda = nullptr;
    std::vector<PxPBDMaterial*> particle_materials;
    PxPBDMaterial* default_particle_material = nullptr;
    std::vector<PxDeformableSurfaceMaterial*> surface_materials;
    std::vector<PxDeformableVolumeMaterial*> volume_materials;
    PxDeformableSurfaceMaterial* default_surface_material = nullptr;
    PxDeformableVolumeMaterial* default_volume_material = nullptr;
    std::vector<PxPBDParticleSystem*> systems;
    std::vector<PxTriangleMesh*> surface_meshes;
    std::vector<PxDeformableVolumeMesh*> volume_meshes;
    std::vector<Body> bodies;
    // Host scratch the readback writes into. It is sized once at build time so
    // publishing a result never allocates.
    std::vector<PxVec4> readback;
#endif
};

Content::Content()
    : impl_(std::make_unique<Impl>())
{
}

Content::~Content()
{
    Teardown();
}

bool Content::IsEmpty() const noexcept
{
    const Impl& impl = *impl_;
    return impl.counts.particle_systems == 0 && impl.counts.surfaces == 0 && impl.counts.volumes == 0;
}

const Counts& Content::GetCounts() const noexcept
{
    return impl_->counts;
}

Content::Impl& Content::GetImpl() noexcept
{
    return *impl_;
}

bool SceneDeclaresGpuContent(const openusd_physx_page::View& view, size_t scene_index)
{
    if (view.IsEmpty())
    {
        return false;
    }
    const openusd_physx_build_page_header& header = view.Header();
    const int32_t index = static_cast<int32_t>(scene_index);
    for (uint32_t position = 0; position < header.particle_systems.count; ++position)
    {
        const openusd_physx_particle_system_desc system =
            view.Get<openusd_physx_particle_system_desc>(header.particle_systems, position);
        if (system.scene_index == index)
        {
            return true;
        }
    }
    for (uint32_t position = 0; position < header.deformables.count; ++position)
    {
        const openusd_physx_deformable_desc deformable =
            view.Get<openusd_physx_deformable_desc>(header.deformables, position);
        if (deformable.scene_index == index)
        {
            return true;
        }
    }
    return false;
}

bool PageDeclaresGpuContent(const openusd_physx_page::View& view)
{
    if (view.IsEmpty())
    {
        return false;
    }
    const openusd_physx_build_page_header& header = view.Header();
    return header.particle_systems.count != 0 || header.deformables.count != 0;
}

#if PX_SUPPORT_GPU_PHYSX
namespace
{
PxCookingParams GpuCookingParams()
{
    PxCookingParams params = openusd_physx_runtime::CookingParams();
    // Every mesh a deformable is built from is solved on the device, so it has
    // to carry the GPU acceleration structures. Without this the simulation SDK
    // refuses the mesh at attach time rather than at cook time.
    params.buildGPUData = true;
    return params;
}

// Reports the per particle inverse mass a body asks for. A body that states a
// total mass splits it evenly; a body that does not derives one from the
// material density and the rest volume of a single particle, which is the same
// rule an authored point set without a mass means.
float ParticleInverseMass(
    const openusd_physx_particle_body_desc& body,
    float density,
    float rest_offset,
    uint32_t count)
{
    float mass = 0.0F;
    if (body.mass > 0.0F && count != 0)
    {
        mass = body.mass / static_cast<float>(count);
    }
    else
    {
        const float radius = rest_offset > 0.0F ? rest_offset : kDefaultRestOffset;
        const float resolved_density = density > 0.0F ? density : kDefaultParticleDensity;
        const float volume = (4.0F / 3.0F) * 3.14159265F * radius * radius * radius;
        mass = resolved_density * volume;
    }
    if (!(mass > 0.0F) || !std::isfinite(mass))
    {
        return 0.0F;
    }
    return 1.0F / mass;
}

// Uploads one host buffer to the device buffer of a particle buffer. Every
// upload is scoped by the CUDA lock because PhysX requires the context to be
// current on the calling thread.
bool UploadParticles(
    PxCudaContextManager& cuda,
    PxParticleBuffer& buffer,
    const std::vector<PxVec4>& positions,
    const std::vector<PxVec4>& velocities,
    const std::vector<PxU32>& phases)
{
    PxCudaContext* context = cuda.getCudaContext();
    if (context == nullptr)
    {
        return false;
    }
    const PxScopedCudaLock lock(cuda);
    const size_t vector_bytes = positions.size() * sizeof(PxVec4);
    const size_t phase_bytes = phases.size() * sizeof(PxU32);
    if (context->memcpyHtoD(
            reinterpret_cast<CUdeviceptr>(buffer.getPositionInvMasses()), positions.data(), vector_bytes) != 0)
    {
        return false;
    }
    if (context->memcpyHtoD(
            reinterpret_cast<CUdeviceptr>(buffer.getVelocities()), velocities.data(), vector_bytes) != 0)
    {
        return false;
    }
    if (context->memcpyHtoD(reinterpret_cast<CUdeviceptr>(buffer.getPhases()), phases.data(), phase_bytes) != 0)
    {
        return false;
    }
    return true;
}

// Reads one device vertex window back into host scratch.
bool DownloadVertices(PxCudaContextManager& cuda, const PxVec4* device, PxVec4* host, uint32_t count)
{
    if (device == nullptr || host == nullptr || count == 0)
    {
        return false;
    }
    PxCudaContext* context = cuda.getCudaContext();
    if (context == nullptr)
    {
        return false;
    }
    const PxScopedCudaLock lock(cuda);
    return context->memcpyDtoH(
               host, reinterpret_cast<CUdeviceptr>(device), static_cast<size_t>(count) * sizeof(PxVec4)) == 0;
}

void ReleaseMirror(PxCudaContextManager* cuda, PxVec4*& pointer) noexcept
{
    if (cuda == nullptr || pointer == nullptr)
    {
        return;
    }
    physx::Ext::PxCudaHelpersExt::freePinnedHostBuffer(*cuda, pointer);    pointer = nullptr;
}
}
#endif

physx::PxCudaContextManager* ConfigureScene(physx::PxSceneDesc& desc, std::string& reason)
{
#if PX_SUPPORT_GPU_PHYSX
    PxCudaContextManager* cuda = openusd_physx_cuda::Acquire(reason);
    if (cuda == nullptr)
    {
        return nullptr;
    }
    desc.cudaContextManager = cuda;
    desc.flags |= PxSceneFlag::eENABLE_GPU_DYNAMICS;
    desc.broadPhaseType = PxBroadPhaseType::eGPU;
    // Every deformable solver in the simulation SDK is a temporal Gauss-Seidel
    // solver, so a scene that carries one has to run that solver for every
    // body in it rather than only for the deformables.
    desc.solverType = PxSolverType::eTGS;
    // Enhanced determinism is a CPU pipeline guarantee the GPU pipeline does
    // not implement, and a description that asks for both is refused outright.
    // Dropping it here keeps the GPU scene buildable; the world reports the
    // approximation through its capability set rather than silently promising
    // a determinism it cannot deliver.
    desc.flags &= ~PxSceneFlags(PxSceneFlag::eENABLE_ENHANCED_DETERMINISM);
    return cuda;
#else
    static_cast<void>(desc);
    reason = openusd_physx_cuda::IsCompiledIn()
        ? std::string("No CUDA device is reachable.")
        : std::string("This build of the simulation SDK has no CUDA support, so the GPU domains cannot be simulated.");
    return nullptr;
#endif
}

#if PX_SUPPORT_GPU_PHYSX
void Build(
    const openusd_physx_page::View& view,
    const std::vector<physx::PxScene*>& scenes,
    const std::vector<char>& scene_is_gpu,
    Content& content,
    std::vector<SkipNote>& skipped)
{
    Content::Impl& impl = content.GetImpl();
    const openusd_physx_build_page_header& header = view.Header();
    if (!PageDeclaresGpuContent(view))
    {
        return;
    }

    std::string reason;
    PxCudaContextManager* cuda = openusd_physx_cuda::Acquire(reason);
    if (cuda == nullptr)
    {
        for (uint32_t index = 0; index < header.particle_systems.count; ++index)
        {
            const openusd_physx_particle_system_desc system =
                view.Get<openusd_physx_particle_system_desc>(header.particle_systems, index);
            skipped.push_back(SkipNote{system.id, "particle system skipped: " + reason});
        }
        for (uint32_t index = 0; index < header.deformables.count; ++index)
        {
            const openusd_physx_deformable_desc deformable =
                view.Get<openusd_physx_deformable_desc>(header.deformables, index);
            skipped.push_back(SkipNote{
                deformable.id,
                (deformable.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE ? std::string("surface deformable skipped: ")
                                                                     : std::string("volume deformable skipped: ")) +
                    reason});
        }
        return;
    }
    impl.cuda = cuda;

    PxPhysics& physics = openusd_physx_runtime::Physics();
    const openusd_physx_runtime::FactoryLock factory_lock;

    // Particle materials first: a body may bind one, and a body that binds none
    // uses one default material that behaves like plain granular material.
    impl.particle_materials.reserve(header.particle_materials.count);
    for (uint32_t index = 0; index < header.particle_materials.count; ++index)
    {
        const openusd_physx_particle_material_desc desc =
            view.Get<openusd_physx_particle_material_desc>(header.particle_materials, index);
        PxPBDMaterial* material = physics.createPBDMaterial(
            desc.friction,
            desc.damping,
            desc.adhesion,
            desc.viscosity,
            desc.vorticity_confinement,
            desc.surface_tension,
            desc.cohesion,
            desc.lift,
            desc.drag,
            desc.cfl_coefficient > 0.0F ? desc.cfl_coefficient : 1.0F,
            desc.gravity_scale > 0.0F ? desc.gravity_scale : 1.0F);
        if (material == nullptr)
        {
            skipped.push_back(SkipNote{desc.id, "particle material skipped: the simulation SDK refused it."});
        }
        else
        {
            material->setAdhesionRadiusScale(desc.adhesion_offset_scale);
            material->setParticleFrictionScale(
                desc.particle_friction_scale > 0.0F ? desc.particle_friction_scale : 1.0F);
            material->setParticleAdhesionScale(
                desc.particle_adhesion_scale > 0.0F ? desc.particle_adhesion_scale : 1.0F);
        }
        impl.particle_materials.push_back(material);
    }
    if (header.particle_bodies.count != 0)
    {
        impl.default_particle_material = physics.createPBDMaterial(
            0.2F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 1.0F, 1.0F);
    }

    impl.surface_materials.resize(header.deformable_materials.count, nullptr);
    impl.volume_materials.resize(header.deformable_materials.count, nullptr);
    for (uint32_t index = 0; index < header.deformable_materials.count; ++index)
    {
        const openusd_physx_deformable_material_desc desc =
            view.Get<openusd_physx_deformable_material_desc>(header.deformable_materials, index);
        if (desc.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE)
        {
            impl.surface_materials[index] = physics.createDeformableSurfaceMaterial(
                desc.youngs_modulus,
                desc.poissons_ratio,
                desc.dynamic_friction,
                desc.thickness > 0.0F ? desc.thickness : 0.001F,
                desc.bending_stiffness,
                desc.elasticity_damping,
                desc.bending_damping);
            if (impl.surface_materials[index] == nullptr)
            {
                skipped.push_back(
                    SkipNote{desc.id, "surface deformable material skipped: the simulation SDK refused it."});
            }
        }
        else
        {
            impl.volume_materials[index] = physics.createDeformableVolumeMaterial(
                desc.youngs_modulus, desc.poissons_ratio, desc.dynamic_friction);
            if (impl.volume_materials[index] == nullptr)
            {
                skipped.push_back(
                    SkipNote{desc.id, "volume deformable material skipped: the simulation SDK refused it."});
            }
            else
            {
                impl.volume_materials[index]->setElasticityDamping(desc.elasticity_damping);
                impl.volume_materials[index]->setMaterialModel(PxDeformableVolumeMaterialModel::eCO_ROTATIONAL);
            }
        }
    }

    // Particle systems and the particle buffers they own.
    impl.systems.resize(header.particle_systems.count, nullptr);
    for (uint32_t index = 0; index < header.particle_systems.count; ++index)
    {
        const openusd_physx_particle_system_desc desc =
            view.Get<openusd_physx_particle_system_desc>(header.particle_systems, index);
        const size_t scene_index = static_cast<size_t>(desc.scene_index);
        if (scene_index >= scenes.size() || scene_index >= scene_is_gpu.size() ||
            scene_is_gpu[scene_index] == 0 || scenes[scene_index] == nullptr)
        {
            skipped.push_back(
                SkipNote{desc.id, "particle system skipped: its scene could not be created on the device."});
            continue;
        }
        const PxU32 neighborhood = desc.max_neighborhood != 0 ? desc.max_neighborhood : 96u;
        const float neighborhood_scale = desc.neighborhood_scale > 0.0F ? desc.neighborhood_scale : 1.01F;
        PxPBDParticleSystem* system = physics.createPBDParticleSystem(*cuda, neighborhood, neighborhood_scale);
        if (system == nullptr)
        {
            skipped.push_back(SkipNote{desc.id, "particle system skipped: the simulation SDK refused it."});
            continue;
        }
        const float particle_contact_offset =
            desc.particle_contact_offset > 0.0F ? desc.particle_contact_offset : 0.2F;
        system->setParticleContactOffset(particle_contact_offset);
        system->setContactOffset(desc.contact_offset > 0.0F ? desc.contact_offset : particle_contact_offset);
        system->setRestOffset(desc.rest_offset > 0.0F ? desc.rest_offset : particle_contact_offset * 0.8F);
        system->setSolidRestOffset(
            desc.solid_rest_offset > 0.0F ? desc.solid_rest_offset : particle_contact_offset * 0.8F);
        system->setFluidRestOffset(
            desc.fluid_rest_offset > 0.0F ? desc.fluid_rest_offset : particle_contact_offset * 0.6F);
        if (desc.max_depenetration_velocity > 0.0F)
        {
            system->setMaxDepenetrationVelocity(desc.max_depenetration_velocity);
        }
        if (desc.solver_position_iterations != 0)
        {
            system->setSolverIterationCounts(desc.solver_position_iterations);
        }
        system->setWind(openusd_physx_translate::ToPx(desc.wind));
        PxParticleFlags particle_flags = PxParticleFlags(0);
        if ((desc.flags & OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_GLOBAL_SELF_COLLISION) == 0)
        {
            particle_flags |= PxParticleFlag::eDISABLE_SELF_COLLISION;
        }
        if ((desc.flags & OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_NON_PARTICLE_COLLISION) == 0)
        {
            particle_flags |= PxParticleFlag::eDISABLE_RIGID_COLLISION;
        }
        if ((desc.flags & OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_ENABLE_CCD) != 0)
        {
            particle_flags |= PxParticleFlag::eENABLE_SPECULATIVE_CCD;
        }
        system->setParticleFlags(particle_flags);
        scenes[scene_index]->addActor(*system);
        impl.systems[index] = system;
        ++impl.counts.particle_systems;

        // One phase per distinct material, group, and behaviour triple, so two
        // bodies that share all three genuinely share one solver group.
        std::map<uint64_t, PxU32> phases;
        for (uint32_t local = 0; local < desc.body_count; ++local)
        {
            const uint32_t body_index = desc.body_offset + local;
            const openusd_physx_particle_body_desc body =
                view.Get<openusd_physx_particle_body_desc>(header.particle_bodies, body_index);
            PxPBDMaterial* material = impl.default_particle_material;
            float density = 0.0F;
            if (body.material_index >= 0 &&
                static_cast<size_t>(body.material_index) < impl.particle_materials.size())
            {
                PxPBDMaterial* bound = impl.particle_materials[static_cast<size_t>(body.material_index)];
                if (bound != nullptr)
                {
                    material = bound;
                }
                density = view
                              .Get<openusd_physx_particle_material_desc>(
                                  header.particle_materials, static_cast<uint32_t>(body.material_index))
                              .density;
            }
            if (material == nullptr)
            {
                skipped.push_back(SkipNote{body.id, "particle body skipped: no usable particle material."});
                continue;
            }

            const bool fluid = (body.flags & OPENUSD_PHYSX_PARTICLE_BODY_FLAG_FLUID) != 0;
            const bool self_collision = (body.flags & OPENUSD_PHYSX_PARTICLE_BODY_FLAG_SELF_COLLISION) != 0;
            // The key packs three disjoint fields into one word: the two
            // behaviour bits, the twenty bit collision group the page validator
            // bounds, and the bound material index widened by one so that "no
            // material" is a value of its own. The fields never overlap, so two
            // bodies share a phase only when all three agree, which is the only
            // reason a phase is shared at all.
            const uint64_t phase_key = (self_collision ? 0x1ULL : 0x0ULL) | (fluid ? 0x2ULL : 0x0ULL) |
                (static_cast<uint64_t>(body.particle_group & OPENUSD_PHYSX_MAX_PARTICLE_GROUP) << 2) |
                (static_cast<uint64_t>(static_cast<uint32_t>(body.material_index + 1)) << 32);
            PxU32 phase = 0;
            const auto existing = phases.find(phase_key);
            if (existing != phases.end())
            {
                phase = existing->second;
            }
            else
            {
                PxParticlePhaseFlags phase_flags = PxParticlePhaseFlags(0);
                if (self_collision)
                {
                    phase_flags |= PxParticlePhaseFlag::eParticlePhaseSelfCollide;
                }
                if (fluid)
                {
                    phase_flags |= PxParticlePhaseFlag::eParticlePhaseFluid;
                }
                phase = system->createPhase(material, phase_flags);
                phases.emplace(phase_key, phase);
            }

            PxParticleBuffer* buffer = physics.createParticleBuffer(body.point_count, 0, cuda);
            if (buffer == nullptr)
            {
                skipped.push_back(
                    SkipNote{body.id, "particle body skipped: the simulation SDK refused its particle buffer."});
                continue;
            }

            Content::Impl::Body record;
            record.id = body.id;
            record.kind = fluid ? OPENUSD_PHYSX_DEFORMATION_FLUID : OPENUSD_PHYSX_DEFORMATION_PARTICLES;
            record.vertex_count = body.point_count;
            record.particles = buffer;
            const float rest_offset = fluid ? system->getFluidRestOffset() : system->getSolidRestOffset();
            const float inverse_mass = ParticleInverseMass(body, density, rest_offset, body.point_count);
            record.initial_positions.resize(body.point_count);
            record.initial_velocities.assign(body.point_count, PxVec4(0.0F));
            record.initial_phases.assign(body.point_count, phase);
            for (uint32_t point = 0; point < body.point_count; ++point)
            {
                const openusd_physx_vec3f local_point =
                    view.Get<openusd_physx_vec3f>(header.mesh_points, body.point_offset + point);
                const PxVec3 placed = PlacePoint(body.world_pose, local_point);
                record.initial_positions[point] = PxVec4(placed.x, placed.y, placed.z, inverse_mass);
            }
            buffer->setNbActiveParticles(body.point_count);
            if (!UploadParticles(
                    *cuda, *buffer, record.initial_positions, record.initial_velocities, record.initial_phases))
            {
                buffer->release();
                skipped.push_back(
                    SkipNote{body.id, "particle body skipped: its particle data could not be uploaded to the device."});
                continue;
            }
            buffer->raiseFlags(PxParticleBufferFlag::eUPDATE_POSITION);
            buffer->raiseFlags(PxParticleBufferFlag::eUPDATE_VELOCITY);
            buffer->raiseFlags(PxParticleBufferFlag::eUPDATE_PHASE);
            system->addParticleBuffer(buffer);

            impl.bodies.push_back(std::move(record));
            ++impl.counts.particle_bodies;
        }
    }

    // Surface and volume deformables.
    for (uint32_t index = 0; index < header.deformables.count; ++index)
    {
        const openusd_physx_deformable_desc desc =
            view.Get<openusd_physx_deformable_desc>(header.deformables, index);
        const size_t scene_index = static_cast<size_t>(desc.scene_index);
        const bool surface = desc.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE;
        const std::string prefix =
            surface ? std::string("surface deformable skipped: ") : std::string("volume deformable skipped: ");
        if (scene_index >= scenes.size() || scene_index >= scene_is_gpu.size() ||
            scene_is_gpu[scene_index] == 0 || scenes[scene_index] == nullptr)
        {
            skipped.push_back(SkipNote{desc.id, prefix + "its scene could not be created on the device."});
            continue;
        }

        std::vector<PxVec3> points(desc.point_count);
        for (uint32_t point = 0; point < desc.point_count; ++point)
        {
            const openusd_physx_vec3f local_point =
                view.Get<openusd_physx_vec3f>(header.mesh_points, desc.point_offset + point);
            points[point] = surface ? PlacePoint(desc.world_pose, local_point)
                                    : openusd_physx_translate::ToPx(local_point);
        }
        std::vector<PxU32> indices(desc.index_count);
        for (uint32_t element = 0; element < desc.index_count; ++element)
        {
            indices[element] = view.Get<uint32_t>(header.mesh_indices, desc.index_offset + element);
        }

        const PxCookingParams cooking = GpuCookingParams();
        if (surface)
        {
            PxDeformableSurfaceMaterial* material = impl.default_surface_material;
            float density = kDefaultDeformableDensity;
            float thickness = kDefaultShellThickness;
            if (desc.material_index >= 0 && static_cast<size_t>(desc.material_index) < impl.surface_materials.size())
            {
                material = impl.surface_materials[static_cast<size_t>(desc.material_index)];
                const openusd_physx_deformable_material_desc bound =
                    view.Get<openusd_physx_deformable_material_desc>(
                        header.deformable_materials, static_cast<uint32_t>(desc.material_index));
                density = bound.density;
                thickness = bound.thickness;
            }
            if (material == nullptr)
            {
                material = physics.createDeformableSurfaceMaterial(500000.0F, 0.45F, 0.25F);
                impl.default_surface_material = material;
            }
            if (material == nullptr)
            {
                skipped.push_back(SkipNote{desc.id, prefix + "no usable surface material."});
                continue;
            }

            PxTriangleMeshDesc mesh_desc;
            mesh_desc.points.count = desc.point_count;
            mesh_desc.points.stride = sizeof(PxVec3);
            mesh_desc.points.data = points.data();
            mesh_desc.triangles.count = desc.index_count / 3u;
            mesh_desc.triangles.stride = 3u * sizeof(PxU32);
            mesh_desc.triangles.data = indices.data();
            PxTriangleMesh* mesh =
                PxCreateTriangleMesh(cooking, mesh_desc, physics.getPhysicsInsertionCallback());
            if (mesh == nullptr)
            {
                skipped.push_back(SkipNote{desc.id, prefix + "its triangulation could not be cooked for the device."});
                continue;
            }
            if (mesh->getNbVertices() != desc.point_count)
            {
                mesh->release();
                skipped.push_back(SkipNote{
                    desc.id,
                    prefix + "cooking changed its vertex count, so the simulated vertices could not be bound "
                             "back to the authored points."});
                continue;
            }
            impl.surface_meshes.push_back(mesh);

            PxDeformableSurface* body = physics.createDeformableSurface(*cuda);
            if (body == nullptr)
            {
                skipped.push_back(SkipNote{desc.id, prefix + "the simulation SDK refused it."});
                continue;
            }
            PxShape* shape = physics.createShape(
                PxTriangleMeshGeometry(mesh),
                *material,
                true,
                PxShapeFlag::eVISUALIZATION | PxShapeFlag::eSIMULATION_SHAPE);
            if (shape == nullptr || !body->attachShape(*shape))
            {
                if (shape != nullptr)
                {
                    shape->release();
                }
                body->release();
                skipped.push_back(SkipNote{desc.id, prefix + "its collision shape could not be attached."});
                continue;
            }
            shape->release();

            PxDeformableBody& deformable = *body;
            deformable.setLinearDamping(desc.vertex_velocity_damping);
            deformable.setSelfCollisionFilterDistance(
                desc.self_collision_filter_distance > 0.0F ? desc.self_collision_filter_distance : 0.1F);
            if (desc.solver_position_iterations != 0)
            {
                deformable.setSolverIterationCounts(desc.solver_position_iterations);
            }
            if (desc.max_displacement > 0.0F && header.simulation_rate_hz != 0)
            {
                // The page states a bound on the distance one vertex may move in
                // one fixed step, and the simulation SDK models the same bound as
                // a maximum velocity. The fixed rate the page declares is what
                // turns one into the other exactly, so the authored intent is
                // converted rather than dropped or reinterpreted.
                deformable.setMaxVelocity(
                    desc.max_displacement * static_cast<float>(header.simulation_rate_hz));
            }
            PxDeformableBodyFlags body_flags = PxDeformableBodyFlags(0);
            if ((desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_SELF_COLLISION) == 0)
            {
                body_flags |= PxDeformableBodyFlag::eDISABLE_SELF_COLLISION;
            }
            if ((desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_ENABLE_CCD) != 0)
            {
                body_flags |= PxDeformableBodyFlag::eENABLE_SPECULATIVE_CCD;
            }
            deformable.setDeformableBodyFlags(body_flags);
            // A deformable is a PxActor, so the authored gravity opt out is the
            // same actor flag a rigid body and an articulation link already use.
            deformable.setActorFlag(
                PxActorFlag::eDISABLE_GRAVITY,
                (desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_DISABLE_GRAVITY) != 0);
            if (desc.collision_pair_update_frequency != 0)
            {
                body->setNbCollisionPairUpdatesPerTimestep(desc.collision_pair_update_frequency);
            }
            if (desc.collision_iteration_multiplier != 0)
            {
                body->setNbCollisionSubsteps(desc.collision_iteration_multiplier);
            }

            Content::Impl::Body record;
            record.id = desc.id;
            record.kind = OPENUSD_PHYSX_DEFORMATION_SURFACE;
            record.vertex_count = desc.point_count;
            record.surface = body;
            // The mirror is allocated with a placeholder mass and the authored
            // density is then distributed over the triangulation, which is what
            // turns a density and a shell thickness into per vertex masses that
            // agree with the geometry instead of a mass this library invented.
            const PxU32 mirror_vertices = PxDeformableSurfaceExt::allocateAndInitializeHostMirror(
                points.data(),
                nullptr,
                points.data(),
                desc.point_count,
                1.0F,
                PxTransform(PxIdentity),
                cuda,
                record.mirror_positions,
                record.mirror_velocities,
                record.mirror_rest);
            if (mirror_vertices != desc.point_count || record.mirror_positions == nullptr)
            {
                ReleaseMirror(cuda, record.mirror_positions);
                ReleaseMirror(cuda, record.mirror_velocities);
                ReleaseMirror(cuda, record.mirror_rest);
                body->release();
                skipped.push_back(SkipNote{desc.id, prefix + "its host mirror could not be allocated."});
                continue;
            }
            scenes[scene_index]->addActor(*body);
            PxDeformableSurfaceExt::distributeDensityToVertices(
                *body,
                density > 0.0F ? density : kDefaultDeformableDensity,
                thickness > 0.0F ? thickness : kDefaultShellThickness,
                record.mirror_positions);
            record.initial_positions.assign(
                record.mirror_positions, record.mirror_positions + desc.point_count);
            PxDeformableSurfaceExt::copyToDevice(
                *body,
                PxDeformableSurfaceDataFlag::eALL,
                desc.point_count,
                record.mirror_positions,
                record.mirror_velocities,
                record.mirror_rest);
            impl.bodies.push_back(std::move(record));
            ++impl.counts.surfaces;
            continue;
        }

        PxDeformableVolumeMaterial* material = impl.default_volume_material;
        float density = kDefaultDeformableDensity;
        if (desc.material_index >= 0 && static_cast<size_t>(desc.material_index) < impl.volume_materials.size())
        {
            material = impl.volume_materials[static_cast<size_t>(desc.material_index)];
            density = view
                          .Get<openusd_physx_deformable_material_desc>(
                              header.deformable_materials, static_cast<uint32_t>(desc.material_index))
                          .density;
        }
        if (material == nullptr)
        {
            material = physics.createDeformableVolumeMaterial(50000.0F, 0.45F, 0.25F);
            impl.default_volume_material = material;
        }
        if (material == nullptr)
        {
            skipped.push_back(SkipNote{desc.id, prefix + "no usable volume material."});
            continue;
        }

        std::vector<PxVec3> collision_points;
        std::vector<PxU32> collision_indices;
        if (desc.collision_point_count != 0 && desc.collision_index_count != 0)
        {
            collision_points.resize(desc.collision_point_count);
            for (uint32_t point = 0; point < desc.collision_point_count; ++point)
            {
                collision_points[point] = openusd_physx_translate::ToPx(
                    view.Get<openusd_physx_vec3f>(header.mesh_points, desc.collision_point_offset + point));
            }
            collision_indices.resize(desc.collision_index_count);
            for (uint32_t element = 0; element < desc.collision_index_count; ++element)
            {
                collision_indices[element] =
                    view.Get<uint32_t>(header.mesh_indices, desc.collision_index_offset + element);
            }
        }

        PxTetrahedronMeshDesc simulation_desc;
        simulation_desc.points.count = desc.point_count;
        simulation_desc.points.stride = sizeof(PxVec3);
        simulation_desc.points.data = points.data();
        simulation_desc.tetrahedrons.count = desc.index_count / 4u;
        simulation_desc.tetrahedrons.stride = 4u * sizeof(PxU32);
        simulation_desc.tetrahedrons.data = indices.data();
        simulation_desc.tetsPerElement = 1;

        PxTetrahedronMeshDesc collision_desc = simulation_desc;
        if (!collision_points.empty())
        {
            collision_desc.points.count = desc.collision_point_count;
            collision_desc.points.data = collision_points.data();
            collision_desc.tetrahedrons.count = desc.collision_index_count / 4u;
            collision_desc.tetrahedrons.data = collision_indices.data();
        }

        const PxDeformableVolumeSimulationDataDesc simulation_data;
        PxDeformableVolumeMesh* mesh = PxCreateDeformableVolumeMesh(
            cooking, simulation_desc, collision_desc, simulation_data, physics.getPhysicsInsertionCallback());
        if (mesh == nullptr)
        {
            skipped.push_back(
                SkipNote{desc.id, prefix + "its tetrahedral mesh could not be cooked for the device."});
            continue;
        }
        impl.volume_meshes.push_back(mesh);

        PxDeformableVolume* body = PxDeformableVolumeExt::createDeformableVolumeFromMesh(
            mesh, PlaceFrame(desc.world_pose), *material, *cuda, density > 0.0F ? density : kDefaultDeformableDensity);
        if (body == nullptr)
        {
            skipped.push_back(SkipNote{desc.id, prefix + "the simulation SDK refused it."});
            continue;
        }
        const PxTetrahedronMesh* simulation_mesh = body->getSimulationMesh();
        if (simulation_mesh == nullptr || simulation_mesh->getNbVertices() != desc.point_count)
        {
            body->release();
            skipped.push_back(SkipNote{
                desc.id,
                prefix + "cooking changed its simulation vertex count, so the simulated vertices could not be "
                         "bound back to the authored points."});
            continue;
        }

        PxDeformableBody& deformable = *body;
        deformable.setLinearDamping(desc.vertex_velocity_damping);
        deformable.setSelfCollisionFilterDistance(
            desc.self_collision_filter_distance > 0.0F ? desc.self_collision_filter_distance : 0.1F);
        if (desc.solver_position_iterations != 0)
        {
            deformable.setSolverIterationCounts(desc.solver_position_iterations);
        }
        if (desc.max_depenetration_velocity > 0.0F)
        {
            deformable.setMaxDepenetrationVelocity(desc.max_depenetration_velocity);
        }
        if (desc.sleep_threshold > 0.0F)
        {
            deformable.setSleepThreshold(desc.sleep_threshold);
        }
        if (desc.settling_threshold > 0.0F)
        {
            deformable.setSettlingThreshold(desc.settling_threshold);
        }
        PxDeformableBodyFlags body_flags = PxDeformableBodyFlags(0);
        if ((desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_SELF_COLLISION) == 0)
        {
            body_flags |= PxDeformableBodyFlag::eDISABLE_SELF_COLLISION;
        }
        if ((desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_ENABLE_CCD) != 0)
        {
            body_flags |= PxDeformableBodyFlag::eENABLE_SPECULATIVE_CCD;
        }
        if ((desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_KINEMATIC) != 0)
        {
            body_flags |= PxDeformableBodyFlag::eKINEMATIC;
        }
        deformable.setDeformableBodyFlags(body_flags);
        // A deformable is a PxActor, so the authored gravity opt out is the same
        // actor flag a rigid body and an articulation link already use.
        deformable.setActorFlag(
            PxActorFlag::eDISABLE_GRAVITY,
            (desc.flags & OPENUSD_PHYSX_DEFORMABLE_FLAG_DISABLE_GRAVITY) != 0);

        Content::Impl::Body record;
        record.id = desc.id;
        record.kind = OPENUSD_PHYSX_DEFORMATION_VOLUME;
        record.vertex_count = desc.point_count;
        record.volume = body;
        // The host mirror is allocated from the device buffers the scene owns, so
        // the volume joins its scene first and is mirrored afterwards.
        scenes[scene_index]->addActor(*body);
        PxDeformableVolumeExt::allocateAndInitializeHostMirror(
            *body,
            cuda,
            record.mirror_positions,
            record.mirror_velocities,
            record.mirror_collision,
            record.mirror_rest);
        if (record.mirror_positions != nullptr)
        {
            record.initial_positions.assign(
                record.mirror_positions, record.mirror_positions + desc.point_count);
        }
        impl.bodies.push_back(std::move(record));
        ++impl.counts.volumes;
    }

    uint32_t deformation_points = 0;
    uint32_t largest = 0;
    for (const Content::Impl::Body& body : impl.bodies)
    {
        deformation_points += body.vertex_count;
        largest = body.vertex_count > largest ? body.vertex_count : largest;
    }
    impl.counts.deformation_bodies = static_cast<uint32_t>(impl.bodies.size());
    impl.counts.deformation_points = deformation_points;
    impl.readback.assign(largest, PxVec4(0.0F));
}

void Reset(Content& content) noexcept
{
    Content::Impl& impl = content.GetImpl();
    if (impl.cuda == nullptr)
    {
        return;
    }
    for (Content::Impl::Body& body : impl.bodies)
    {
        if (body.particles != nullptr && !body.initial_positions.empty())
        {
            if (UploadParticles(
                    *impl.cuda, *body.particles, body.initial_positions, body.initial_velocities, body.initial_phases))
            {
                body.particles->raiseFlags(PxParticleBufferFlag::eUPDATE_POSITION);
                body.particles->raiseFlags(PxParticleBufferFlag::eUPDATE_VELOCITY);
            }
            continue;
        }
        if (body.mirror_positions == nullptr || body.initial_positions.empty())
        {
            continue;
        }
        std::memcpy(
            body.mirror_positions,
            body.initial_positions.data(),
            body.initial_positions.size() * sizeof(PxVec4));
        if (body.mirror_velocities != nullptr)
        {
            std::memset(body.mirror_velocities, 0, body.initial_positions.size() * sizeof(PxVec4));
        }
        if (body.surface != nullptr)
        {
            PxDeformableSurfaceExt::copyToDevice(
                *body.surface,
                PxDeformableSurfaceDataFlag::eALL,
                body.vertex_count,
                body.mirror_positions,
                body.mirror_velocities,
                body.mirror_rest);
        }
        else if (body.volume != nullptr)
        {
            PxDeformableVolumeExt::copyToDevice(
                *body.volume,
                PxDeformableVolumeDataFlag::eALL,
                body.mirror_positions,
                body.mirror_velocities,
                body.mirror_collision,
                body.mirror_rest);
        }
    }
}

void Publish(
    Content& content,
    openusd_physx_deformation_state* states,
    size_t state_capacity,
    openusd_physx_vec3f* points,
    size_t point_capacity,
    uint32_t& state_count,
    uint32_t& point_count,
    uint32_t& dropped_count) noexcept
{
    Content::Impl& impl = content.GetImpl();
    state_count = 0;
    point_count = 0;
    dropped_count = 0;
    if (impl.cuda == nullptr || states == nullptr || points == nullptr)
    {
        dropped_count = static_cast<uint32_t>(impl.bodies.size());
        return;
    }

    for (Content::Impl::Body& body : impl.bodies)
    {
        if (body.vertex_count == 0)
        {
            continue;
        }
        if (static_cast<size_t>(state_count) >= state_capacity ||
            static_cast<size_t>(point_count) + body.vertex_count > point_capacity)
        {
            ++dropped_count;
            continue;
        }
        const PxVec4* device = nullptr;
        if (body.particles != nullptr)
        {
            device = body.particles->getPositionInvMasses();
        }
        else if (body.surface != nullptr)
        {
            device = body.surface->getPositionInvMassBufferD();
        }
        else if (body.volume != nullptr)
        {
            device = body.volume->getSimPositionInvMassBufferD();
        }
        if (body.vertex_count > impl.readback.size())
        {
            ++dropped_count;
            continue;
        }
        if (!DownloadVertices(*impl.cuda, device, impl.readback.data(), body.vertex_count))
        {
            ++dropped_count;
            continue;
        }

        openusd_physx_deformation_state state{};
        state.id = body.id;
        state.kind = body.kind;
        state.flags = OPENUSD_PHYSX_DEFORMATION_FLAG_NONE;
        if (body.volume != nullptr && body.volume->isSleeping())
        {
            state.flags |= OPENUSD_PHYSX_DEFORMATION_FLAG_SLEEPING;
        }
        state.point_offset = point_count;
        state.point_count = body.vertex_count;
        states[state_count] = state;
        for (uint32_t index = 0; index < body.vertex_count; ++index)
        {
            const PxVec4& source = impl.readback[index];
            points[point_count + index] = openusd_physx_vec3f{source.x, source.y, source.z};
        }
        point_count += body.vertex_count;
        ++state_count;
    }
}
#else
void Build(
    const openusd_physx_page::View& view,
    const std::vector<physx::PxScene*>& scenes,
    const std::vector<char>& scene_is_gpu,
    Content& content,
    std::vector<SkipNote>& skipped)
{
    static_cast<void>(scenes);
    static_cast<void>(scene_is_gpu);
    static_cast<void>(content);
    if (!PageDeclaresGpuContent(view))
    {
        return;
    }
    const openusd_physx_build_page_header& header = view.Header();
    std::string reason;
    static_cast<void>(openusd_physx_cuda::Acquire(reason));
    for (uint32_t index = 0; index < header.particle_systems.count; ++index)
    {
        const openusd_physx_particle_system_desc system =
            view.Get<openusd_physx_particle_system_desc>(header.particle_systems, index);
        skipped.push_back(SkipNote{system.id, "particle system skipped: " + reason});
    }
    for (uint32_t index = 0; index < header.deformables.count; ++index)
    {
        const openusd_physx_deformable_desc deformable =
            view.Get<openusd_physx_deformable_desc>(header.deformables, index);
        skipped.push_back(SkipNote{
            deformable.id,
            (deformable.kind == OPENUSD_PHYSX_DEFORMABLE_SURFACE ? std::string("surface deformable skipped: ")
                                                                 : std::string("volume deformable skipped: ")) +
                reason});
    }
}

void Reset(Content& content) noexcept
{
    static_cast<void>(content);
}

void Publish(
    Content& content,
    openusd_physx_deformation_state* states,
    size_t state_capacity,
    openusd_physx_vec3f* points,
    size_t point_capacity,
    uint32_t& state_count,
    uint32_t& point_count,
    uint32_t& dropped_count) noexcept
{
    static_cast<void>(content);
    static_cast<void>(states);
    static_cast<void>(state_capacity);
    static_cast<void>(points);
    static_cast<void>(point_capacity);
    state_count = 0;
    point_count = 0;
    dropped_count = 0;
}
#endif

void Content::Teardown() noexcept
{
#if PX_SUPPORT_GPU_PHYSX
    Impl& impl = *impl_;
    if (impl.cuda != nullptr)
    {
        const openusd_physx_runtime::FactoryLock factory_lock;
        for (Impl::Body& body : impl.bodies)
        {
            if (body.particles != nullptr)
            {
                body.particles->release();
                body.particles = nullptr;
            }
            if (body.surface != nullptr)
            {
                body.surface->release();
                body.surface = nullptr;
            }
            if (body.volume != nullptr)
            {
                body.volume->release();
                body.volume = nullptr;
            }
            ReleaseMirror(impl.cuda, body.mirror_positions);
            ReleaseMirror(impl.cuda, body.mirror_velocities);
            ReleaseMirror(impl.cuda, body.mirror_rest);
            ReleaseMirror(impl.cuda, body.mirror_collision);
        }
        impl.bodies.clear();
        for (PxPBDParticleSystem* system : impl.systems)
        {
            if (system != nullptr)
            {
                system->release();
            }
        }
        impl.systems.clear();
        for (PxDeformableVolumeMesh* mesh : impl.volume_meshes)
        {
            if (mesh != nullptr)
            {
                mesh->release();
            }
        }
        impl.volume_meshes.clear();
        for (PxTriangleMesh* mesh : impl.surface_meshes)
        {
            if (mesh != nullptr)
            {
                mesh->release();
            }
        }
        impl.surface_meshes.clear();
        for (PxPBDMaterial* material : impl.particle_materials)
        {
            if (material != nullptr)
            {
                material->release();
            }
        }
        impl.particle_materials.clear();
        if (impl.default_particle_material != nullptr)
        {
            impl.default_particle_material->release();
            impl.default_particle_material = nullptr;
        }
        for (PxDeformableSurfaceMaterial* material : impl.surface_materials)
        {
            if (material != nullptr)
            {
                material->release();
            }
        }
        impl.surface_materials.clear();
        for (PxDeformableVolumeMaterial* material : impl.volume_materials)
        {
            if (material != nullptr)
            {
                material->release();
            }
        }
        impl.volume_materials.clear();
        if (impl.default_surface_material != nullptr)
        {
            impl.default_surface_material->release();
            impl.default_surface_material = nullptr;
        }
        if (impl.default_volume_material != nullptr)
        {
            impl.default_volume_material->release();
            impl.default_volume_material = nullptr;
        }
        impl.readback.clear();
        impl.cuda = nullptr;
    }
#endif
    impl_->counts = Counts{};
}
}
