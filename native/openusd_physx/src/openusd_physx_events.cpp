// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_events.h"

#include "openusd_physx_support.h"

#include <algorithm>
#include <cmath>

namespace
{
using openusd_physx_support::IsFinite;
using openusd_physx_support::IsUsableRotation;

template <typename TValue>
int CompareScalar(TValue left, TValue right) noexcept
{
    if (left < right)
    {
        return -1;
    }
    return left > right ? 1 : 0;
}

// Maps a distance onto a total order that tolerates a non finite value. A hit
// whose distance PhysX could not compute sorts last instead of poisoning the
// comparison and making the sort order depend on the initial arrangement.
float OrderableDistance(float value) noexcept
{
    return std::isnan(value) ? HUGE_VALF : value;
}

bool IsZero(openusd_physx_vec3f value) noexcept
{
    return value.x == 0.0F && value.y == 0.0F && value.z == 0.0F;
}

bool IsZero(openusd_physx_quatf value) noexcept
{
    return value.x == 0.0F && value.y == 0.0F && value.z == 0.0F && value.w == 0.0F;
}

bool IsZero(const openusd_physx_transform& value) noexcept
{
    return IsZero(value.position) && IsZero(value.rotation);
}

bool IsNonDegenerate(openusd_physx_vec3f value) noexcept
{
    const double x = static_cast<double>(value.x);
    const double y = static_cast<double>(value.y);
    const double z = static_cast<double>(value.z);
    return (x * x) + (y * y) + (z * z) > 0.0;
}

bool UsesPose(uint32_t command_type) noexcept
{
    return command_type == OPENUSD_PHYSX_COMMAND_KINEMATIC_TARGET ||
        command_type == OPENUSD_PHYSX_COMMAND_TELEPORT;
}

bool UsesVector(uint32_t command_type) noexcept
{
    switch (command_type)
    {
    case OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY:
    case OPENUSD_PHYSX_COMMAND_SET_ANGULAR_VELOCITY:
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE:
    case OPENUSD_PHYSX_COMMAND_ADD_TORQUE:
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE:
    case OPENUSD_PHYSX_COMMAND_ADD_ANGULAR_IMPULSE:
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT:
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE_AT_POINT:
    case OPENUSD_PHYSX_COMMAND_SET_SCENE_GRAVITY:
    case OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER:
    case OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT:
        return true;
    default:
        return false;
    }
}

bool UsesPoint(uint32_t command_type) noexcept
{
    return command_type == OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT ||
        command_type == OPENUSD_PHYSX_COMMAND_ADD_IMPULSE_AT_POINT ||
        command_type == OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT;
}

bool IsWithin(float value, float low, float high) noexcept
{
    return std::isfinite(value) && value >= low && value <= high;
}

// Validates a vehicle input against the ranges the ABI header documents. The
// gear is the dangerous one: it indexes the fixed size gearbox and autobox
// arrays of the simulation SDK, so a negative, fractional, or huge float would
// read outside of them once it is narrowed to an unsigned index.
bool ValidateVehicleInput(const openusd_physx_command& command, const std::string& prefix,
    std::string& reason)
{
    if (!IsWithin(command.vector.x, 0.0F, 1.0F) || !IsWithin(command.vector.y, 0.0F, 1.0F) ||
        !IsWithin(command.point.x, 0.0F, 1.0F) || !IsWithin(command.point.y, 0.0F, 1.0F))
    {
        reason = prefix + " declares a vehicle throttle, brake, handbrake, or clutch outside [0, 1].";
        return false;
    }
    if (!IsWithin(command.vector.z, -1.0F, 1.0F))
    {
        reason = prefix + " declares a vehicle steer outside [-1, 1].";
        return false;
    }

    const float gear = command.point.z;
    if (!std::isfinite(gear) || gear < 0.0F || std::floor(gear) != gear)
    {
        reason = prefix + " declares a vehicle gear that is not a non negative integral value.";
        return false;
    }
    if (gear > static_cast<float>(OPENUSD_PHYSX_MAX_VEHICLE_GEARS))
    {
        reason = prefix + " declares a vehicle gear beyond the gear budget.";
        return false;
    }
    return true;
}
}

namespace openusd_physx_events
{
int CompareEvents(const openusd_physx_event& left, const openusd_physx_event& right) noexcept
{
    int order = CompareScalar(left.step_index, right.step_index);
    if (order != 0)
    {
        return order;
    }
    order = CompareScalar(left.type, right.type);
    if (order != 0)
    {
        return order;
    }
    order = CompareScalar(left.id0, right.id0);
    if (order != 0)
    {
        return order;
    }
    order = CompareScalar(left.id1, right.id1);
    if (order != 0)
    {
        return order;
    }
    order = CompareScalar(left.detail0, right.detail0);
    if (order != 0)
    {
        return order;
    }
    return CompareScalar(left.detail1, right.detail1);
}

bool EventLess(const openusd_physx_event& left, const openusd_physx_event& right) noexcept
{
    return CompareEvents(left, right) < 0;
}

bool HitLess(const openusd_physx_query_hit& left, const openusd_physx_query_hit& right) noexcept
{
    const float left_distance = OrderableDistance(left.distance);
    const float right_distance = OrderableDistance(right.distance);
    if (left_distance != right_distance)
    {
        return left_distance < right_distance;
    }
    if (left.actor_id != right.actor_id)
    {
        return left.actor_id < right.actor_id;
    }
    if (left.shape_id != right.shape_id)
    {
        return left.shape_id < right.shape_id;
    }
    return left.face_index < right.face_index;
}

void EventSink::Reserve(uint32_t capacity)
{
    entries_.clear();
    entries_.shrink_to_fit();
    capacity_ = capacity;
    dropped_ = 0;
    if (capacity != 0)
    {
        entries_.reserve(capacity);
    }
}

void EventSink::Reset() noexcept
{
    entries_.clear();
    dropped_ = 0;
}

void EventSink::Retain(const openusd_physx_event& event) noexcept
{
    if (capacity_ == 0)
    {
        if (dropped_ != UINT32_MAX)
        {
            ++dropped_;
        }
        return;
    }

    if (entries_.size() < capacity_)
    {
        // Reserve already guaranteed the storage, so this never allocates.
        entries_.push_back(event);
        std::push_heap(entries_.begin(), entries_.end(), EventLess);
        return;
    }

    if (dropped_ != UINT32_MAX)
    {
        ++dropped_;
    }
    if (!EventLess(event, entries_.front()))
    {
        return;
    }
    // The new event belongs to the deterministic prefix, so it replaces the
    // farthest event currently retained.
    std::pop_heap(entries_.begin(), entries_.end(), EventLess);
    entries_.back() = event;
    std::push_heap(entries_.begin(), entries_.end(), EventLess);
}

void EventSink::Sort() noexcept
{
    std::sort(entries_.begin(), entries_.end(), EventLess);
}

HitSink::HitSink(openusd_physx_query_hit* hits, size_t capacity) noexcept
    : hits_(hits)
    , capacity_(hits == nullptr ? 0 : capacity)
{
}

void HitSink::Retain(const openusd_physx_query_hit& hit) noexcept
{
    if (capacity_ == 0)
    {
        ++dropped_;
        return;
    }
    if (size_ < capacity_)
    {
        hits_[size_] = hit;
        ++size_;
        std::push_heap(hits_, hits_ + size_, HitLess);
        return;
    }

    ++dropped_;
    if (!HitLess(hit, hits_[0]))
    {
        return;
    }
    std::pop_heap(hits_, hits_ + size_, HitLess);
    hits_[size_ - 1] = hit;
    std::push_heap(hits_, hits_ + size_, HitLess);
}

void HitSink::Sort() noexcept
{
    if (hits_ != nullptr)
    {
        std::sort(hits_, hits_ + size_, HitLess);
    }
}

uint32_t AllowedCommandFlags(uint32_t command_type) noexcept
{
    switch (command_type)
    {
    case OPENUSD_PHYSX_COMMAND_TELEPORT:
        return OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE;
    case OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY:
    case OPENUSD_PHYSX_COMMAND_SET_ANGULAR_VELOCITY:
        return OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE | OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE;
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE:
    case OPENUSD_PHYSX_COMMAND_ADD_TORQUE:
        return OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE |
            OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION |
            OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE;
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE:
    case OPENUSD_PHYSX_COMMAND_ADD_ANGULAR_IMPULSE:
        return OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE |
            OPENUSD_PHYSX_COMMAND_FLAG_MODE_VELOCITY_CHANGE |
            OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE;
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT:
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE_AT_POINT:
        /* An application point is delivered through PxRigidBodyExt, which
         * documents that "only eFORCE and eIMPULSE are supported" because it
         * has to convert the force into a torque about the centre of mass and
         * needs a real force to do that. The command type already selects which
         * of the two applies, so no force mode modifier is accepted at all. A
         * caller that wants an acceleration or a velocity change at the centre
         * of mass asks for it with ADD_FORCE or ADD_IMPULSE, which is exactly
         * equivalent and carries no such restriction. */
        return OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE |
            OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL |
            OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS |
            OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE;
    case OPENUSD_PHYSX_COMMAND_SET_SCENE_GRAVITY:
        return OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE;
    case OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER:
        /* A controller move may be authored as a direction plus a distance. */
        return OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE;
    default:
        // Kinematic targets, wake, sleep, and the clear commands carry no
        // modifier at all.
        return OPENUSD_PHYSX_COMMAND_FLAG_NONE;
    }
}

bool ValidateCommand(const openusd_physx_command& command, size_t index, std::string& reason)
{
    const std::string prefix = "Command " + std::to_string(index);
    if (command.type >= static_cast<uint32_t>(OPENUSD_PHYSX_COMMAND_TYPE_COUNT))
    {
        reason = prefix + " declares an unknown type.";
        return false;
    }
    if (command.reserved0 != 0 || command.reserved1 != 0)
    {
        reason = prefix + " declares non zero reserved fields.";
        return false;
    }
    if (command.target_id == OPENUSD_PHYSX_INVALID_ID)
    {
        reason = prefix + " targets the reserved zero identity.";
        return false;
    }

    const uint32_t allowed = AllowedCommandFlags(command.type);
    if ((command.flags & ~allowed) != 0)
    {
        reason = prefix + " declares a modifier that its command type does not accept.";
        return false;
    }
    if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL) != 0 &&
        (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS) != 0)
    {
        reason = prefix + " declares two application point modes at once.";
        return false;
    }
    if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION) != 0 &&
        (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MODE_VELOCITY_CHANGE) != 0)
    {
        reason = prefix + " declares two force modes at once.";
        return false;
    }
    if (!std::isfinite(command.scalar))
    {
        reason = prefix + " declares a non finite scalar.";
        return false;
    }

    const bool uses_pose = UsesPose(command.type);
    const bool uses_vector = UsesVector(command.type);
    const bool uses_point = UsesPoint(command.type) &&
        (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS) == 0;

    if (uses_pose)
    {
        if (!IsFinite(command.pose) || !IsUsableRotation(command.pose.rotation))
        {
            reason = prefix + " declares a non finite pose or an unusable rotation.";
            return false;
        }
    }
    else if (!IsZero(command.pose))
    {
        reason = prefix + " declares a pose that its command type does not read.";
        return false;
    }

    if (uses_vector)
    {
        if (!IsFinite(command.vector))
        {
            reason = prefix + " declares a non finite vector.";
            return false;
        }
        if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE) != 0 &&
            !IsNonDegenerate(command.vector))
        {
            reason = prefix + " declares a magnitude with a zero length direction.";
            return false;
        }
    }
    else if (!IsZero(command.vector))
    {
        reason = prefix + " declares a vector that its command type does not read.";
        return false;
    }

    if (uses_point)
    {
        if (!IsFinite(command.point))
        {
            reason = prefix + " declares a non finite application point.";
            return false;
        }
    }
    else if (!IsZero(command.point))
    {
        reason = prefix + " declares an application point that its command type does not read.";
        return false;
    }

    if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE) == 0 && command.scalar != 0.0F)
    {
        reason = prefix + " declares a scalar without the magnitude modifier.";
        return false;
    }

    if (command.type == OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT &&
        !ValidateVehicleInput(command, prefix, reason))
    {
        return false;
    }
    return true;
}

openusd_physx_vec3f ResolveCommandVector(const openusd_physx_command& command) noexcept
{
    if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE) == 0)
    {
        return command.vector;
    }

    const double x = static_cast<double>(command.vector.x);
    const double y = static_cast<double>(command.vector.y);
    const double z = static_cast<double>(command.vector.z);
    const double length = std::sqrt((x * x) + (y * y) + (z * z));
    if (!(length > 0.0))
    {
        return openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    }
    const double scale = static_cast<double>(command.scalar) / length;
    return openusd_physx_vec3f{
        static_cast<float>(x * scale),
        static_cast<float>(y * scale),
        static_cast<float>(z * scale)};
}

bool ValidateQueryRequest(
    const openusd_physx_query_request& request,
    size_t scene_count,
    std::string& reason)
{
    if (request.type >= static_cast<uint32_t>(OPENUSD_PHYSX_QUERY_TYPE_COUNT) ||
        (request.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_QUERY_FLAG_ALL)) != 0)
    {
        reason = "The query declares an unknown type or unknown flags.";
        return false;
    }
    if (request.scene_index >= scene_count)
    {
        reason = "The query references a scene index that this world does not contain.";
        return false;
    }
    if (request.max_hits == 0)
    {
        reason = "The query must accept at least one hit.";
        return false;
    }

    const bool exclude_static = (request.flags & OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_STATIC) != 0;
    const bool exclude_dynamic = (request.flags & OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_DYNAMIC) != 0;
    if (exclude_static && exclude_dynamic)
    {
        reason = "The query excludes both static and dynamic actors.";
        return false;
    }

    const bool any_hit = (request.flags & OPENUSD_PHYSX_QUERY_FLAG_ANY_HIT) != 0;
    if (any_hit && (request.filter_mask != 0 ||
        (request.flags & OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_TRIGGERS) != 0))
    {
        reason = "An any hit query cannot also declare a collision group or trigger filter, "
            "because the single hit it reports is not guaranteed to survive the filter.";
        return false;
    }
    if ((request.flags & OPENUSD_PHYSX_QUERY_FLAG_SWEEP_INITIAL_OVERLAP) != 0 &&
        request.type != static_cast<uint32_t>(OPENUSD_PHYSX_QUERY_SWEEP))
    {
        reason = "Only a sweep can request initial overlap hits.";
        return false;
    }
    if (!IsFinite(request.origin))
    {
        reason = "The query origin is not finite.";
        return false;
    }

    if (request.type == static_cast<uint32_t>(OPENUSD_PHYSX_QUERY_OVERLAP))
    {
        if (!IsZero(request.direction) || request.max_distance != 0.0F)
        {
            reason = "An overlap query must not declare a direction or a maximum distance.";
            return false;
        }
    }
    else
    {
        if (!IsFinite(request.direction) || !IsNonDegenerate(request.direction))
        {
            reason = "The query direction must be finite and must not be a zero vector.";
            return false;
        }
        if (!std::isfinite(request.max_distance) || !(request.max_distance > 0.0F))
        {
            reason = "The query maximum distance must be positive and finite.";
            return false;
        }
    }

    if (request.type == static_cast<uint32_t>(OPENUSD_PHYSX_QUERY_RAYCAST))
    {
        if (request.shape_type != 0 || !IsZero(request.half_extents) || !IsZero(request.rotation) ||
            request.radius != 0.0F || request.half_height != 0.0F)
        {
            reason = "A raycast must not declare swept geometry.";
            return false;
        }
        return true;
    }

    if (!IsUsableRotation(request.rotation))
    {
        reason = "The swept or overlapped shape declares an unusable rotation.";
        return false;
    }
    switch (request.shape_type)
    {
    case OPENUSD_PHYSX_SHAPE_SPHERE:
        if (!std::isfinite(request.radius) || !(request.radius > 0.0F))
        {
            reason = "The query sphere radius must be positive and finite.";
            return false;
        }
        if (!IsZero(request.half_extents) || request.half_height != 0.0F)
        {
            reason = "A query sphere must not declare box or capsule dimensions.";
            return false;
        }
        return true;
    case OPENUSD_PHYSX_SHAPE_BOX:
        if (!IsFinite(request.half_extents) || !(request.half_extents.x > 0.0F) ||
            !(request.half_extents.y > 0.0F) || !(request.half_extents.z > 0.0F))
        {
            reason = "The query box half extents must be positive and finite.";
            return false;
        }
        if (request.radius != 0.0F || request.half_height != 0.0F)
        {
            reason = "A query box must not declare sphere or capsule dimensions.";
            return false;
        }
        return true;
    case OPENUSD_PHYSX_SHAPE_CAPSULE:
        if (!std::isfinite(request.radius) || !(request.radius > 0.0F) ||
            !std::isfinite(request.half_height) || !(request.half_height > 0.0F))
        {
            reason = "The query capsule radius and half height must be positive and finite.";
            return false;
        }
        if (!IsZero(request.half_extents))
        {
            reason = "A query capsule must not declare box dimensions.";
            return false;
        }
        return true;
    default:
        reason = "Only sphere, box, and capsule geometry is supported by sweep and overlap queries.";
        return false;
    }
}
}
