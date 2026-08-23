// Copyright (c) marcschier. Licensed under the MIT License.

// Contract probe for the event ordering, the bounded overflow policy, the
// runtime command rules, and the batched query rules of the retained world. It
// links only the policy translation unit, so it builds and runs on machines
// without the PhysX SDK and proves the parts of the contract that do not need a
// simulation at all.

#include "openusd_physx_events.h"

#include <algorithm>
#include <iostream>
#include <random>
#include <string>
#include <vector>

namespace
{
using openusd_physx_events::CompareEvents;
using openusd_physx_events::EventLess;
using openusd_physx_events::EventSink;
using openusd_physx_events::HitLess;
using openusd_physx_events::HitSink;

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

openusd_physx_event MakeEvent(
    uint32_t type,
    uint64_t id0,
    uint64_t id1,
    uint64_t detail0 = 0,
    uint64_t detail1 = 0,
    uint64_t step = 1)
{
    openusd_physx_event event{};
    event.id0 = id0;
    event.id1 = id1;
    event.detail0 = detail0;
    event.detail1 = detail1;
    event.step_index = step;
    event.type = type;
    return event;
}

openusd_physx_command MakeCommand(uint32_t type, uint64_t target = 7)
{
    openusd_physx_command command{};
    command.target_id = target;
    command.type = type;
    return command;
}

openusd_physx_query_request MakeRaycast(uint64_t user_id = 1)
{
    openusd_physx_query_request request{};
    request.user_id = user_id;
    request.type = OPENUSD_PHYSX_QUERY_RAYCAST;
    request.direction = openusd_physx_vec3f{0.0F, 0.0F, -1.0F};
    request.max_distance = 10.0F;
    request.max_hits = 4;
    return request;
}

openusd_physx_query_request MakeSweep(uint64_t user_id = 1)
{
    openusd_physx_query_request request{};
    request.user_id = user_id;
    request.type = OPENUSD_PHYSX_QUERY_SWEEP;
    request.direction = openusd_physx_vec3f{0.0F, 0.0F, -1.0F};
    request.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 1.0F};
    request.shape_type = OPENUSD_PHYSX_SHAPE_SPHERE;
    request.radius = 0.5F;
    request.max_distance = 10.0F;
    request.max_hits = 4;
    return request;
}

openusd_physx_query_hit MakeHit(float distance, uint64_t actor, uint64_t shape = 0, uint32_t face = 0)
{
    openusd_physx_query_hit hit{};
    hit.actor_id = actor;
    hit.shape_id = shape;
    hit.distance = distance;
    hit.face_index = face;
    return hit;
}

// The event order must be a strict total order: every pair of distinct events
// compares in exactly one direction, and only fully identical events compare
// equal. A weaker order would let the retained prefix depend on arrival order.
void ProbeOrderIsTotal()
{
    std::vector<openusd_physx_event> events;
    for (uint32_t type = 0; type < OPENUSD_PHYSX_EVENT_TYPE_COUNT; ++type)
    {
        for (uint64_t id0 = 1; id0 <= 3; ++id0)
        {
            for (uint64_t id1 = 1; id1 <= 3; ++id1)
            {
                events.push_back(MakeEvent(type, id0, id1, id0 * 10, id1 * 10));
                events.push_back(MakeEvent(type, id0, id1, id0 * 10, id1 * 10, 2));
            }
        }
    }

    bool antisymmetric = true;
    bool identifies = true;
    for (const openusd_physx_event& left : events)
    {
        for (const openusd_physx_event& right : events)
        {
            const int forward = CompareEvents(left, right);
            const int backward = CompareEvents(right, left);
            if (forward != -backward)
            {
                antisymmetric = false;
            }
            const bool same = &left == &right;
            if ((forward == 0) != same)
            {
                identifies = false;
            }
        }
    }
    Expect(antisymmetric, "the event order is antisymmetric");
    Expect(identifies, "only identical events compare equal");

    bool transitive = true;
    for (const openusd_physx_event& a : events)
    {
        for (const openusd_physx_event& b : events)
        {
            for (const openusd_physx_event& c : events)
            {
                if (EventLess(a, b) && EventLess(b, c) && !EventLess(a, c))
                {
                    transitive = false;
                }
            }
        }
    }
    Expect(transitive, "the event order is transitive");

    // A step index always dominates the type, and the type always dominates the
    // identities, so a late step can never sort before an early one.
    Expect(
        EventLess(
            MakeEvent(OPENUSD_PHYSX_EVENT_CONTROLLER_HIT, 9, 9, 9, 9, 1),
            MakeEvent(OPENUSD_PHYSX_EVENT_CONTACT_FOUND, 1, 1, 1, 1, 2)),
        "the step index dominates every other key");
    Expect(
        EventLess(
            MakeEvent(OPENUSD_PHYSX_EVENT_CONTACT_FOUND, 9, 9),
            MakeEvent(OPENUSD_PHYSX_EVENT_TRIGGER_ENTER, 1, 1)),
        "the event type dominates the identities");
}

// Whatever order events arrive in, the sink must retain exactly the smallest N
// of the total order, and must count everything else.
void ProbeOverflowKeepsDeterministicPrefix()
{
    std::vector<openusd_physx_event> events;
    for (uint64_t id = 1; id <= 64; ++id)
    {
        events.push_back(MakeEvent(OPENUSD_PHYSX_EVENT_CONTACT_FOUND, id, id + 1, id * 2, id * 3));
    }

    std::vector<openusd_physx_event> expected = events;
    std::sort(expected.begin(), expected.end(), EventLess);
    expected.resize(8);

    bool stable = true;
    bool counted = true;
    std::mt19937 generator(20240607U);
    for (int attempt = 0; attempt < 32; ++attempt)
    {
        std::vector<openusd_physx_event> shuffled = events;
        std::shuffle(shuffled.begin(), shuffled.end(), generator);

        EventSink sink;
        sink.Reserve(8);
        for (const openusd_physx_event& event : shuffled)
        {
            sink.Retain(event);
        }
        sink.Sort();

        if (sink.Size() != expected.size())
        {
            stable = false;
            continue;
        }
        for (size_t index = 0; index < expected.size(); ++index)
        {
            if (CompareEvents(sink.Data()[index], expected[index]) != 0)
            {
                stable = false;
            }
        }
        if (sink.Dropped() != events.size() - expected.size() || !sink.Overflowed())
        {
            counted = false;
        }
    }
    Expect(stable, "an overflowing sink keeps the deterministic prefix in every arrival order");
    Expect(counted, "an overflowing sink counts every event it did not retain");

    EventSink empty;
    empty.Reserve(0);
    empty.Retain(MakeEvent(OPENUSD_PHYSX_EVENT_SLEEP, 1, 0));
    Expect(empty.Size() == 0 && empty.Dropped() == 1, "a zero capacity sink retains nothing and counts everything");

    EventSink reused;
    reused.Reserve(4);
    for (uint64_t id = 1; id <= 16; ++id)
    {
        reused.Retain(MakeEvent(OPENUSD_PHYSX_EVENT_WAKE, id, 0));
    }
    Expect(reused.Dropped() == 12, "the sink counts drops until it is reset");
    reused.Reset();
    Expect(
        reused.Size() == 0 && reused.Dropped() == 0 && !reused.Overflowed(),
        "a reset sink reports neither retained nor dropped events");
    Expect(reused.Capacity() == 4, "a reset sink keeps its capacity");
}

// The hit sink writes straight into a caller owned region and keeps the nearest
// hits, so a request that overflows still reports the hits closest to it.
void ProbeHitSinkKeepsNearest()
{
    std::vector<openusd_physx_query_hit> storage(3);
    HitSink sink(storage.data(), storage.size());
    for (uint64_t actor = 1; actor <= 10; ++actor)
    {
        sink.Retain(MakeHit(static_cast<float>(11 - actor), actor));
    }
    sink.Sort();

    Expect(sink.Size() == 3, "the hit sink retains exactly its capacity");
    Expect(sink.Dropped() == 7, "the hit sink counts every hit past its capacity");
    Expect(
        storage[0].distance == 1.0F && storage[1].distance == 2.0F && storage[2].distance == 3.0F,
        "the hit sink keeps the nearest hits in ascending distance order");

    std::vector<openusd_physx_query_hit> ties(4);
    HitSink tie_sink(ties.data(), ties.size());
    tie_sink.Retain(MakeHit(1.0F, 5, 2, 1));
    tie_sink.Retain(MakeHit(1.0F, 5, 1, 0));
    tie_sink.Retain(MakeHit(1.0F, 2, 9, 3));
    tie_sink.Retain(MakeHit(1.0F, 5, 1, 4));
    tie_sink.Sort();
    Expect(
        ties[0].actor_id == 2 && ties[1].shape_id == 1 && ties[1].face_index == 0 &&
            ties[2].face_index == 4 && ties[3].shape_id == 2,
        "hits at the same distance are ordered by actor, shape, and face identity");

    HitSink empty(nullptr, 4);
    empty.Retain(MakeHit(1.0F, 1));
    Expect(empty.Size() == 0 && empty.Dropped() == 1, "a hit sink without storage retains nothing");

    Expect(
        HitLess(MakeHit(1.0F, 1), MakeHit(std::nanf(""), 1)),
        "a hit without a usable distance sorts last");
}

void ProbeCommandValidation()
{
    std::string reason;

    Expect(
        openusd_physx_events::ValidateCommand(MakeCommand(OPENUSD_PHYSX_COMMAND_WAKE), 0, reason),
        "a zero initialised wake command is accepted");

    openusd_physx_command unknown = MakeCommand(OPENUSD_PHYSX_COMMAND_TYPE_COUNT);
    Expect(!openusd_physx_events::ValidateCommand(unknown, 0, reason), "an unknown command type is rejected");

    openusd_physx_command zero_target = MakeCommand(OPENUSD_PHYSX_COMMAND_WAKE, OPENUSD_PHYSX_INVALID_ID);
    Expect(
        !openusd_physx_events::ValidateCommand(zero_target, 0, reason),
        "a command that targets the reserved zero identity is rejected");

    openusd_physx_command reserved = MakeCommand(OPENUSD_PHYSX_COMMAND_WAKE);
    reserved.reserved0 = 1;
    Expect(!openusd_physx_events::ValidateCommand(reserved, 0, reason), "a non zero reserved field is rejected");

    // Every command type only accepts the modifiers it can act on. A modifier
    // that the type ignores is a caller mistake, never a silent no operation.
    openusd_physx_command wake_with_mode = MakeCommand(OPENUSD_PHYSX_COMMAND_WAKE);
    wake_with_mode.flags = OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION;
    Expect(
        !openusd_physx_events::ValidateCommand(wake_with_mode, 0, reason),
        "a modifier the command type does not accept is rejected");

    openusd_physx_command both_points = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT);
    both_points.vector = openusd_physx_vec3f{0.0F, 1.0F, 0.0F};
    both_points.flags = OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL |
        OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS;
    Expect(
        !openusd_physx_events::ValidateCommand(both_points, 0, reason),
        "two application point modes at once are rejected");

    openusd_physx_command both_modes = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT);
    both_modes.vector = openusd_physx_vec3f{0.0F, 1.0F, 0.0F};
    both_modes.flags = OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION |
        OPENUSD_PHYSX_COMMAND_FLAG_MODE_VELOCITY_CHANGE;
    Expect(
        !openusd_physx_events::ValidateCommand(both_modes, 0, reason),
        "two force modes at once are rejected");

    openusd_physx_command stray_pose = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_FORCE);
    stray_pose.vector = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    stray_pose.pose.position = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    Expect(
        !openusd_physx_events::ValidateCommand(stray_pose, 0, reason),
        "a pose on a command that does not read a pose is rejected");

    openusd_physx_command stray_point = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_FORCE);
    stray_point.vector = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    stray_point.point = openusd_physx_vec3f{0.0F, 1.0F, 0.0F};
    Expect(
        !openusd_physx_events::ValidateCommand(stray_point, 0, reason),
        "an application point on a command that does not read one is rejected");

    openusd_physx_command stray_vector = MakeCommand(OPENUSD_PHYSX_COMMAND_SLEEP);
    stray_vector.vector = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    Expect(
        !openusd_physx_events::ValidateCommand(stray_vector, 0, reason),
        "a vector on a command that does not read one is rejected");

    openusd_physx_command stray_scalar = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_FORCE);
    stray_scalar.vector = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    stray_scalar.scalar = 5.0F;
    Expect(
        !openusd_physx_events::ValidateCommand(stray_scalar, 0, reason),
        "a scalar without the magnitude modifier is rejected");

    openusd_physx_command degenerate = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_IMPULSE);
    degenerate.flags = OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE;
    degenerate.scalar = 5.0F;
    Expect(
        !openusd_physx_events::ValidateCommand(degenerate, 0, reason),
        "a magnitude with a zero length direction is rejected");

    openusd_physx_command teleport = MakeCommand(OPENUSD_PHYSX_COMMAND_TELEPORT);
    teleport.pose.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 0.0F};
    Expect(
        !openusd_physx_events::ValidateCommand(teleport, 0, reason),
        "a teleport with an unusable rotation is rejected");
    teleport.pose.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 1.0F};
    Expect(
        openusd_physx_events::ValidateCommand(teleport, 0, reason),
        "a teleport with a unit rotation is accepted");

    openusd_physx_command clear = MakeCommand(OPENUSD_PHYSX_COMMAND_CLEAR_FORCE);
    Expect(openusd_physx_events::ValidateCommand(clear, 0, reason), "a clear force command is accepted");
    Expect(
        openusd_physx_events::AllowedCommandFlags(OPENUSD_PHYSX_COMMAND_CLEAR_TORQUE) ==
            OPENUSD_PHYSX_COMMAND_FLAG_NONE,
        "a clear command accepts no modifier at all");

    openusd_physx_command magnitude = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_IMPULSE);
    magnitude.flags = OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE;
    magnitude.vector = openusd_physx_vec3f{0.0F, 3.0F, 0.0F};
    magnitude.scalar = 12.0F;
    Expect(
        openusd_physx_events::ValidateCommand(magnitude, 0, reason),
        "a magnitude with a usable direction is accepted");
    const openusd_physx_vec3f resolved = openusd_physx_events::ResolveCommandVector(magnitude);
    Expect(
        resolved.x == 0.0F && resolved.y == 12.0F && resolved.z == 0.0F,
        "the magnitude modifier scales the unit direction by the scalar");

    openusd_physx_command plain = MakeCommand(OPENUSD_PHYSX_COMMAND_ADD_IMPULSE);
    plain.vector = openusd_physx_vec3f{0.0F, 3.0F, 0.0F};
    const openusd_physx_vec3f unchanged = openusd_physx_events::ResolveCommandVector(plain);
    Expect(unchanged.y == 3.0F, "a command without the magnitude modifier keeps its vector");
}

void ProbeQueryValidation()
{
    std::string reason;

    Expect(
        openusd_physx_events::ValidateQueryRequest(MakeRaycast(), 1, reason),
        "a well formed raycast is accepted");
    Expect(
        openusd_physx_events::ValidateQueryRequest(MakeSweep(), 1, reason),
        "a well formed sweep is accepted");

    openusd_physx_query_request unknown = MakeRaycast();
    unknown.type = OPENUSD_PHYSX_QUERY_TYPE_COUNT;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(unknown, 1, reason),
        "an unknown query type is rejected");

    openusd_physx_query_request bad_flags = MakeRaycast();
    bad_flags.flags = 1U << 30;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(bad_flags, 1, reason),
        "an unknown query flag is rejected");

    openusd_physx_query_request missing_scene = MakeRaycast();
    missing_scene.scene_index = 4;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(missing_scene, 1, reason),
        "a query that names a scene the world does not hold is rejected");

    openusd_physx_query_request no_hits = MakeRaycast();
    no_hits.max_hits = 0;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(no_hits, 1, reason),
        "a query that accepts no hit is rejected");

    openusd_physx_query_request nothing = MakeRaycast();
    nothing.flags = OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_STATIC | OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_DYNAMIC;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(nothing, 1, reason),
        "a query that excludes every actor kind is rejected");

    openusd_physx_query_request filtered_any = MakeRaycast();
    filtered_any.flags = OPENUSD_PHYSX_QUERY_FLAG_ANY_HIT;
    filtered_any.filter_mask = 2;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(filtered_any, 1, reason),
        "an any hit query with a collision group filter is rejected");

    openusd_physx_query_request trigger_any = MakeRaycast();
    trigger_any.flags = OPENUSD_PHYSX_QUERY_FLAG_ANY_HIT | OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_TRIGGERS;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(trigger_any, 1, reason),
        "an any hit query with a trigger filter is rejected");

    openusd_physx_query_request initial_overlap = MakeRaycast();
    initial_overlap.flags = OPENUSD_PHYSX_QUERY_FLAG_SWEEP_INITIAL_OVERLAP;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(initial_overlap, 1, reason),
        "only a sweep can request initial overlap hits");

    openusd_physx_query_request swept_ray = MakeRaycast();
    swept_ray.radius = 1.0F;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(swept_ray, 1, reason),
        "a raycast that declares swept geometry is rejected");

    openusd_physx_query_request zero_direction = MakeRaycast();
    zero_direction.direction = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    Expect(
        !openusd_physx_events::ValidateQueryRequest(zero_direction, 1, reason),
        "a raycast with a zero direction is rejected");

    openusd_physx_query_request negative_distance = MakeRaycast();
    negative_distance.max_distance = -1.0F;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(negative_distance, 1, reason),
        "a raycast with a non positive maximum distance is rejected");

    openusd_physx_query_request overlap{};
    overlap.type = OPENUSD_PHYSX_QUERY_OVERLAP;
    overlap.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 1.0F};
    overlap.shape_type = OPENUSD_PHYSX_SHAPE_BOX;
    overlap.half_extents = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    overlap.max_hits = 2;
    Expect(
        openusd_physx_events::ValidateQueryRequest(overlap, 1, reason),
        "a well formed box overlap is accepted");
    overlap.max_distance = 1.0F;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(overlap, 1, reason),
        "an overlap that declares a maximum distance is rejected");

    openusd_physx_query_request bad_shape = MakeSweep();
    bad_shape.shape_type = OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(bad_shape, 1, reason),
        "a sweep against unsupported geometry is rejected");

    openusd_physx_query_request bad_rotation = MakeSweep();
    bad_rotation.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 0.0F};
    Expect(
        !openusd_physx_events::ValidateQueryRequest(bad_rotation, 1, reason),
        "a sweep with an unusable rotation is rejected");

    openusd_physx_query_request bad_radius = MakeSweep();
    bad_radius.radius = 0.0F;
    Expect(
        !openusd_physx_events::ValidateQueryRequest(bad_radius, 1, reason),
        "a sweep sphere with a non positive radius is rejected");
}
}

int main()
{
    ProbeOrderIsTotal();
    ProbeOverflowKeepsDeterministicPrefix();
    ProbeHitSinkKeepsNearest();
    ProbeCommandValidation();
    ProbeQueryValidation();

    if (g_failures != 0)
    {
        std::cerr << g_failures << " event contract probe checks failed.\n";
        return 1;
    }
    std::cout << "openusd_physx event contract probe succeeded.\n";
    return 0;
}
