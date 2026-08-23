// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_translate.h"

#include "openusd_physx_runtime.h"

#include <cstring>

namespace
{
using namespace physx;

bool IsPairSuppressed(const void* constant_block, PxU32 constant_block_size, PxU32 first, PxU32 second) noexcept
{
    if (constant_block == nullptr || constant_block_size < sizeof(openusd_physx_translate::PairFilterHeader))
    {
        return false;
    }
    openusd_physx_translate::PairFilterHeader header{};
    const unsigned char* bytes = static_cast<const unsigned char*>(constant_block);
    std::memcpy(&header, bytes, sizeof(header));
    if (first >= header.actor_count || second >= header.actor_count)
    {
        return false;
    }
    const uint64_t bit_index = static_cast<uint64_t>(first) * header.actor_count + second;
    const uint64_t byte_index = sizeof(header) + (bit_index / 8);
    if (byte_index >= constant_block_size)
    {
        return false;
    }
    return (bytes[byte_index] & (1U << (bit_index % 8))) != 0;
}

uint32_t NotifyFlagsOf(const void* constant_block, PxU32 constant_block_size) noexcept
{
    if (constant_block == nullptr || constant_block_size < sizeof(openusd_physx_translate::PairFilterHeader))
    {
        return openusd_physx_translate::kPairNotifyNone;
    }
    openusd_physx_translate::PairFilterHeader header{};
    std::memcpy(&header, constant_block, sizeof(header));
    return header.notify_flags;
}
}

namespace openusd_physx_translate
{
PxFilterFlags WorldFilterShader(
    PxFilterObjectAttributes attributes0,
    PxFilterData filter_data0,
    PxFilterObjectAttributes attributes1,
    PxFilterData filter_data1,
    PxPairFlags& pair_flags,
    const void* constant_block,
    PxU32 constant_block_size)
{
    if (((filter_data0.word0 & filter_data1.word1) == 0) ||
        ((filter_data1.word0 & filter_data0.word1) == 0))
    {
        return PxFilterFlag::eSUPPRESS;
    }
    if (IsPairSuppressed(constant_block, constant_block_size, filter_data0.word2, filter_data1.word2) ||
        IsPairSuppressed(constant_block, constant_block_size, filter_data1.word2, filter_data0.word2))
    {
        return PxFilterFlag::eSUPPRESS;
    }
    const uint32_t notify = NotifyFlagsOf(constant_block, constant_block_size);
    if (PxFilterObjectIsTrigger(attributes0) || PxFilterObjectIsTrigger(attributes1))
    {
        pair_flags = PxPairFlag::eTRIGGER_DEFAULT;
        if ((notify & kPairNotifyTriggers) != 0)
        {
            pair_flags |= PxPairFlag::eNOTIFY_TOUCH_FOUND | PxPairFlag::eNOTIFY_TOUCH_LOST;
        }
        return PxFilterFlags();
    }
    pair_flags = PxPairFlag::eCONTACT_DEFAULT;
    if (((filter_data0.word3 | filter_data1.word3) & kFilterWord3Ccd) != 0)
    {
        // Enabling CCD on the scene and on the body only makes PhysX sweep the
        // body; the pair still has to ask for swept contact generation or the
        // fast mover tunnels through this pair anyway.
        pair_flags |= PxPairFlag::eDETECT_CCD_CONTACT;
    }
    if ((notify & kPairNotifyContacts) != 0)
    {
        pair_flags |= PxPairFlag::eNOTIFY_TOUCH_FOUND |
            PxPairFlag::eNOTIFY_TOUCH_LOST |
            PxPairFlag::eNOTIFY_CONTACT_POINTS;
    }
    return PxFilterFlags();
}

PxConvexMesh* CookConvexMesh(
    PxPhysics& physics,
    const PxCookingParams& cooking_params,
    const PxVec3* points,
    size_t point_count)
{
    if (points == nullptr || point_count < 4)
    {
        return nullptr;
    }
    PxConvexMeshDesc desc;
    desc.points.count = static_cast<PxU32>(point_count);
    desc.points.stride = sizeof(PxVec3);
    desc.points.data = points;
    desc.flags = PxConvexFlag::eCOMPUTE_CONVEX;
    // Cooking inserts the result through the shared physics insertion callback.
    const openusd_physx_runtime::FactoryLock factory_lock;
    return PxCreateConvexMesh(cooking_params, desc, physics.getPhysicsInsertionCallback());
}

PxTriangleMesh* CookTriangleMesh(
    PxPhysics& physics,
    const PxCookingParams& cooking_params,
    const PxVec3* points,
    size_t point_count,
    const uint32_t* indices,
    size_t index_count)
{
    if (points == nullptr || point_count < 3 || indices == nullptr || index_count < 3 || (index_count % 3) != 0)
    {
        return nullptr;
    }
    PxTriangleMeshDesc desc;
    desc.points.count = static_cast<PxU32>(point_count);
    desc.points.stride = sizeof(PxVec3);
    desc.points.data = points;
    desc.triangles.count = static_cast<PxU32>(index_count / 3);
    desc.triangles.stride = 3 * sizeof(uint32_t);
    desc.triangles.data = indices;
    // Cooking inserts the result through the shared physics insertion callback.
    const openusd_physx_runtime::FactoryLock factory_lock;
    return PxCreateTriangleMesh(cooking_params, desc, physics.getPhysicsInsertionCallback());
}

PxHeightField* CookHeightField(
    PxPhysics& physics,
    const PxHeightFieldSample* samples,
    uint32_t row_count,
    uint32_t column_count)
{
    if (samples == nullptr || row_count < 2 || column_count < 2)
    {
        return nullptr;
    }
    PxHeightFieldDesc desc;
    desc.format = PxHeightFieldFormat::eS16_TM;
    desc.nbRows = row_count;
    desc.nbColumns = column_count;
    desc.samples.data = samples;
    desc.samples.stride = sizeof(PxHeightFieldSample);
    const openusd_physx_runtime::FactoryLock factory_lock;
    return PxCreateHeightField(desc, physics.getPhysicsInsertionCallback());
}

PxTransform AxisFrame(const PxTransform& frame, uint32_t axis)
{
    PxQuat axis_rotation(PxIdentity);
    if (axis == static_cast<uint32_t>(OPENUSD_PHYSX_AXIS_Y))
    {
        axis_rotation = PxQuat(PxHalfPi, PxVec3(0.0F, 0.0F, 1.0F));
    }
    else if (axis == static_cast<uint32_t>(OPENUSD_PHYSX_AXIS_Z))
    {
        axis_rotation = PxQuat(-PxHalfPi, PxVec3(0.0F, 1.0F, 0.0F));
    }
    return PxTransform(frame.p, (frame.q * axis_rotation).getNormalized());
}

PxVec3 ToPx(openusd_physx_vec3f value) noexcept
{
    return PxVec3(value.x, value.y, value.z);
}

PxQuat ToPx(openusd_physx_quatf value) noexcept
{
    return PxQuat(value.x, value.y, value.z, value.w);
}

PxTransform ToPx(const openusd_physx_transform& value) noexcept
{
    return PxTransform(ToPx(value.position), ToPx(value.rotation).getNormalized());
}

openusd_physx_vec3f FromPx(const PxVec3& value) noexcept
{
    return openusd_physx_vec3f{value.x, value.y, value.z};
}

openusd_physx_transform FromPx(const PxTransform& value) noexcept
{
    openusd_physx_transform result{};
    result.position = FromPx(value.p);
    result.rotation = openusd_physx_quatf{value.q.x, value.q.y, value.q.z, value.q.w};
    return result;
}
}
