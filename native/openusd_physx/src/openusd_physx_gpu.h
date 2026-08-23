// Copyright (c) marcschier. Licensed under the MIT License.

// CUDA accelerated simulation domains of the retained physics world: position
// based dynamics particle systems, surface deformables, and finite element
// volume deformables.
//
// Contract
// --------
// * Every entry point is safe to call on a runtime without a CUDA device. The
//   build reports one skip note per object it could not create and leaves the
//   rest of the world untouched, so a stage that mixes rigid bodies with a
//   cloth still simulates its rigid bodies.
// * Nothing here is ever emulated on the CPU. An object that cannot run on a
//   device is skipped, never approximated.
// * Simulation state lives on the device. The only thing that crosses back is
//   the per vertex position window each object publishes into the caller owned
//   deformation buffers of one result page.

#ifndef OPENUSD_PHYSX_GPU_H
#define OPENUSD_PHYSX_GPU_H

#include "openusd_physx_page.h"
#include "openusd_physx_world.h"

#include <PxPhysicsAPI.h>

#include <memory>
#include <string>
#include <vector>

namespace openusd_physx_gpu
{
// One object the build could not create, named by its stable identity so the
// world can report it as a per object diagnostic.
struct SkipNote
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    std::string message;
};

// What the build actually created, which is what the world status reports and
// what the deformation result capacities have to cover.
struct Counts
{
    uint32_t particle_systems = 0;
    uint32_t particle_bodies = 0;
    uint32_t surfaces = 0;
    uint32_t volumes = 0;
    uint32_t deformation_bodies = 0;
    uint32_t deformation_points = 0;
};

// Owns every CUDA backed object one world created. The implementation is
// hidden because the simulation SDK only declares the GPU types on the
// platforms it supports, so a header that named them directly could not be
// included on the platforms it does not.
class Content final
{
public:
    struct Impl;

    Content();
    ~Content();

    Content(const Content&) = delete;
    Content& operator=(const Content&) = delete;
    Content(Content&&) = delete;
    Content& operator=(Content&&) = delete;

    // Releases every object in reverse creation order. Safe to call twice.
    void Teardown() noexcept;

    // True while this world owns at least one CUDA backed object.
    bool IsEmpty() const noexcept;

    const Counts& GetCounts() const noexcept;

    // Access to the owned simulation objects. It is public because the build,
    // reset, and publish entry points live outside this class so the platforms
    // without CUDA support can replace them wholesale.
    Impl& GetImpl() noexcept;

private:
    std::unique_ptr<Impl> impl_;
};

// True when the page declares at least one CUDA backed object for the scene.
bool SceneDeclaresGpuContent(const openusd_physx_page::View& view, size_t scene_index);

// True when the page declares any CUDA backed object at all.
bool PageDeclaresGpuContent(const openusd_physx_page::View& view);

// Applies the scene description changes a GPU scene needs and returns the
// context manager it was bound to. Returns nullptr and fills reason when no
// device is reachable, in which case the description is left untouched and the
// scene is created as a plain CPU scene.
physx::PxCudaContextManager* ConfigureScene(physx::PxSceneDesc& desc, std::string& reason);

// Creates every CUDA backed object the page declares. Objects that cannot be
// created are skipped individually and appended to skipped; the function only
// fails, by leaving content empty and appending one note per declared object,
// when no device can be reached at all.
void Build(
    const openusd_physx_page::View& view,
    const std::vector<physx::PxScene*>& scenes,
    const std::vector<char>& scene_is_gpu,
    Content& content,
    std::vector<SkipNote>& skipped);

// Restores every object to the state the build captured.
void Reset(Content& content) noexcept;

// Copies the current vertex positions of every built object into one caller
// owned deformation window. A body whose vertices do not fit the remaining
// capacity is dropped whole and counted, so a consumer never reads a partially
// written region.
void Publish(
    Content& content,
    openusd_physx_deformation_state* states,
    size_t state_capacity,
    openusd_physx_vec3f* points,
    size_t point_capacity,
    uint32_t& state_count,
    uint32_t& point_count,
    uint32_t& dropped_count) noexcept;
}

#endif
