// Copyright (c) marcschier. Licensed under the MIT License.

// Translation helpers shared by the legacy stage simulation entry point and the
// retained world. They depend on PhysX only, never on OpenUSD, so both paths
// use one implementation of filtering, cooking, and joint axis frames.

#ifndef OPENUSD_PHYSX_TRANSLATE_H
#define OPENUSD_PHYSX_TRANSLATE_H

#include "openusd_physx_world.h"

#include <PxPhysicsAPI.h>

#include <cstddef>
#include <cstdint>

namespace openusd_physx_translate
{
// Layout of the optional filter shader constant block: one 32 bit actor count,
// one 32 bit notification selector, and then a symmetric bit matrix of
// suppressed actor pairs.
//
// The notification selector is what tells the shader whether the scene it
// filters for has a simulation event callback installed. A scene that reports no
// events keeps the cheap default pair flags, so a world built without
// OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS never pays for contact reports.
struct PairFilterHeader
{
    uint32_t actor_count;
    uint32_t notify_flags;
};

// Notification selectors carried by PairFilterHeader::notify_flags.
enum PairNotifyFlags : uint32_t
{
    kPairNotifyNone = 0,
    // Report touch found and touch lost for colliding pairs, with contact
    // points so the world can report a contact position, normal, and impulse.
    kPairNotifyContacts = 1U << 0,
    // Report touch found and touch lost for trigger pairs.
    kPairNotifyTriggers = 1U << 1
};

constexpr uint32_t kMaxPairFilterActors = 4096;

// Selectors carried by shape filter data word3. word0, word1, and word2 are
// already spoken for by the collision group, the accepted mask, and the actor
// index, so per actor simulation switches live here.
enum PairFilterWord3 : uint32_t
{
    kFilterWord3None = 0,
    // The actor this shape belongs to runs continuous collision detection, so
    // every pair it takes part in must ask for swept contact generation. The
    // selector is per actor rather than per world because a world may build one
    // scene with CCD and one without.
    kFilterWord3Ccd = 1U << 0
};

// Simulation filter shader used by every scene this library creates. Filter
// data word0 carries the collision group bit, word1 the accepted group mask,
// word2 the actor index used by the optional suppressed pair matrix, and word3
// the PairFilterWord3 selectors.
physx::PxFilterFlags WorldFilterShader(
    physx::PxFilterObjectAttributes attributes0,
    physx::PxFilterData filter_data0,
    physx::PxFilterObjectAttributes attributes1,
    physx::PxFilterData filter_data1,
    physx::PxPairFlags& pair_flags,
    const void* constant_block,
    physx::PxU32 constant_block_size);

physx::PxConvexMesh* CookConvexMesh(
    physx::PxPhysics& physics,
    const physx::PxCookingParams& cooking_params,
    const physx::PxVec3* points,
    size_t point_count);

physx::PxTriangleMesh* CookTriangleMesh(
    physx::PxPhysics& physics,
    const physx::PxCookingParams& cooking_params,
    const physx::PxVec3* points,
    size_t point_count,
    const uint32_t* indices,
    size_t index_count);

physx::PxHeightField* CookHeightField(
    physx::PxPhysics& physics,
    const physx::PxHeightFieldSample* samples,
    uint32_t row_count,
    uint32_t column_count);

// Rotates a joint frame so that the requested axis becomes the PhysX primary
// axis. The axis argument uses openusd_physx_axis values.
physx::PxTransform AxisFrame(const physx::PxTransform& frame, uint32_t axis);

physx::PxVec3 ToPx(openusd_physx_vec3f value) noexcept;

physx::PxQuat ToPx(openusd_physx_quatf value) noexcept;

physx::PxTransform ToPx(const openusd_physx_transform& value) noexcept;

openusd_physx_vec3f FromPx(const physx::PxVec3& value) noexcept;

openusd_physx_transform FromPx(const physx::PxTransform& value) noexcept;
}

#endif
