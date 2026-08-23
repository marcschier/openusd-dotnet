// Copyright (c) marcschier. Licensed under the MIT License.

// Retained physics world C ABI.
//
// Contract summary
// ----------------
// * Exact-version negotiation. A caller must request
//   OPENUSD_PHYSX_WORLD_ABI_VERSION exactly; there is no forward or backward
//   compatibility window and no silent downgrade.
// * The build page is pointer free. It is a single contiguous, self describing
//   byte blob addressed by byte offsets and element counts, fully validated
//   before any PhysX object is created.
// * Command input is batched. One call carries every command for one step.
// * Result output is caller owned and fixed capacity. Capacities are declared
//   in the build page; exceeding them is a diagnosed bounded overflow that
//   requires a rebuild with larger capacities, never a step time allocation.
// * Identities are stable unsigned 64 bit values derived from a canonical prim
//   path plus an instance domain and index. Zero is never a valid identity.
// * There are no callbacks across this ABI and no per element entry points.

#ifndef OPENUSD_PHYSX_WORLD_H
#define OPENUSD_PHYSX_WORLD_H

#include "openusd_physx.h"

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Exact ABI version required by every caller of this header.
 *
 * Version 2 widened openusd_physx_event by two identity fields (detail0 and
 * detail1) so a contact, trigger, or controller hit event can name the exact
 * colliders involved instead of only the two owning actors. Because the ABI is
 * negotiated exactly and nothing shipped against version 1, the version number
 * moves with the record layout rather than trying to keep both alive.
 *
 * Version 3 widened openusd_physx_actor_desc from 128 to 144 bytes by adding
 * principal_axes, the rotation that turns the actor frame into the frame the
 * diagonal inertia tensor is stated in. Without it a diagonal inertia authored
 * about rotated principal axes would be applied in the wrong frame, so the
 * quaternion travels with the centre of mass rather than being dropped.
 *
 * Version 4 completes the CPU rigid body, collision, material, and joint
 * domains. It adds cylinder, cone, and height field collision geometry, per
 * shape contact, rest, and torsional patch offsets, material combine modes and
 * damping, the full per body solver, velocity, sleep, and continuous collision
 * budget, per axis rotation and translation locks, and the per axis motion,
 * limit, and drive description a six degree of freedom joint needs. Every new
 * field is additive and a zero value keeps the version 3 behaviour, but the
 * record sizes moved, so the version moves with them.
 *
 * Version 7 adds the optional CUDA accelerated domains: position based
 * dynamics particle systems with solid and fluid particle bodies, surface
 * deformables, and finite element volume deformables. It adds six build page
 * sections, the deformation result buffers a per vertex domain has to publish
 * because a rigid body state cannot express one, and the capability bits that
 * are only reported once a CUDA context has actually been created and a domain
 * has actually been simulated. Every added field is additive, but the build
 * page header, the result capacities, the result header, the result page, the
 * world status, and the ABI record table all moved, so the version moves with
 * them. */
#define OPENUSD_PHYSX_WORLD_ABI_VERSION 7u

/* "USDPHYSX" in little endian byte order. */
#define OPENUSD_PHYSX_PAGE_MAGIC UINT64_C(0x5853594850445355)

/* Hard bounds enforced by the page validator. */
#define OPENUSD_PHYSX_PAGE_MAX_BYTES UINT64_C(0x40000000)
#define OPENUSD_PHYSX_PAGE_ALIGNMENT 8u
#define OPENUSD_PHYSX_MAX_SCENES 64u
#define OPENUSD_PHYSX_MAX_COLLISION_GROUPS 32u
#define OPENUSD_PHYSX_MIN_SIMULATION_RATE_HZ 24u
#define OPENUSD_PHYSX_MAX_SIMULATION_RATE_HZ 240u
#define OPENUSD_PHYSX_MAX_SUBSTEPS 64u
#define OPENUSD_PHYSX_MAX_RESULT_CAPACITY 1048576u
/* The wheel budget the simulation SDK accepts for one vehicle. This mirrors
 * `PxVehicleLimits::eMAX_NB_WHEELS` exactly; the runtime asserts the two agree
 * where the simulation SDK header is available, because every brake, steer,
 * differential and axle response table the runtime fills is a fixed array of
 * that length. */
#define OPENUSD_PHYSX_MAX_VEHICLE_WHEELS 20u
/* The gear budget the simulation SDK accepts for one vehicle, counting the
 * reverse gear and the neutral gear. */
#define OPENUSD_PHYSX_MAX_VEHICLE_GEARS 32u

/* Bounds the CUDA accelerated domains are validated against before any device
 * memory is reserved. A particle system, a surface, or a volume that exceeds
 * one of these is rejected as a page error rather than being handed to the
 * simulation SDK, because every one of them turns straight into a device
 * allocation whose size the page alone decides. */
#define OPENUSD_PHYSX_MAX_PARTICLES_PER_BODY 4194304u
#define OPENUSD_PHYSX_MAX_DEFORMABLE_VERTICES 1048576u
/* The neighbourhood budget PhysX accepts for one position based dynamics
 * particle system. */
#define OPENUSD_PHYSX_MIN_PARTICLE_NEIGHBORHOOD 8u
#define OPENUSD_PHYSX_MAX_PARTICLE_NEIGHBORHOOD 1024u
/* The largest particle collision group a page may declare.
 *
 * A position based dynamics phase packs the collision group into the low
 * twenty bits of a thirty two bit phase word and reserves the bits above it
 * for behaviour flags, so twenty bits is the group space the solver itself
 * has. Bounding the authored group here is what lets the runtime pack a
 * group, the per body behaviour flags, and the bound material index into one
 * lookup key without two different bodies ever colliding on the same key and
 * silently sharing a phase they never asked to share. */
#define OPENUSD_PHYSX_MAX_PARTICLE_GROUP 1048575u

#define OPENUSD_PHYSX_DIAGNOSTIC_MESSAGE_BYTES 192u
#define OPENUSD_PHYSX_INVALID_ID UINT64_C(0)

typedef struct openusd_physx_world openusd_physx_world;

typedef enum openusd_physx_up_axis
{
    OPENUSD_PHYSX_UP_AXIS_X = 0,
    OPENUSD_PHYSX_UP_AXIS_Y = 1,
    OPENUSD_PHYSX_UP_AXIS_Z = 2
} openusd_physx_up_axis;

typedef enum openusd_physx_axis
{
    OPENUSD_PHYSX_AXIS_X = 0,
    OPENUSD_PHYSX_AXIS_Y = 1,
    OPENUSD_PHYSX_AXIS_Z = 2
} openusd_physx_axis;

typedef enum openusd_physx_instance_domain
{
    OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM = 0,
    OPENUSD_PHYSX_INSTANCE_DOMAIN_NATIVE_INSTANCE = 1,
    OPENUSD_PHYSX_INSTANCE_DOMAIN_POINT_INSTANCER = 2,
    OPENUSD_PHYSX_INSTANCE_DOMAIN_COUNT = 3
} openusd_physx_instance_domain;

typedef enum openusd_physx_actor_type
{
    OPENUSD_PHYSX_ACTOR_STATIC = 0,
    OPENUSD_PHYSX_ACTOR_DYNAMIC = 1,
    OPENUSD_PHYSX_ACTOR_KINEMATIC = 2,
    OPENUSD_PHYSX_ACTOR_TYPE_COUNT = 3
} openusd_physx_actor_type;

typedef enum openusd_physx_scene_flags
{
    OPENUSD_PHYSX_SCENE_FLAG_NONE = 0u,
    OPENUSD_PHYSX_SCENE_FLAG_ENABLE_CCD = 1u << 0,
    OPENUSD_PHYSX_SCENE_FLAG_ENABLE_ENHANCED_DETERMINISM = 1u << 1,
    OPENUSD_PHYSX_SCENE_FLAG_DISABLE_SLEEPING = 1u << 2,
    OPENUSD_PHYSX_SCENE_FLAG_ALL = 0x7u
} openusd_physx_scene_flags;

typedef enum openusd_physx_material_flags
{
    OPENUSD_PHYSX_MATERIAL_FLAG_NONE = 0u,
    OPENUSD_PHYSX_MATERIAL_FLAG_DISABLE_FRICTION = 1u << 0,
    OPENUSD_PHYSX_MATERIAL_FLAG_DISABLE_STRONG_FRICTION = 1u << 1,
    /* Compensates the additional restitution a discrete solver introduces on a
     * shallow bounce. */
    OPENUSD_PHYSX_MATERIAL_FLAG_COMPLIANT_CONTACT = 1u << 2,
    OPENUSD_PHYSX_MATERIAL_FLAG_ALL = 0x7u
} openusd_physx_material_flags;

/* How the two materials of a contact pair are folded into one value. The value
 * matches PxCombineMode so a page carries the authored intent rather than a
 * value this library has to guess. */
typedef enum openusd_physx_combine_mode
{
    OPENUSD_PHYSX_COMBINE_AVERAGE = 0,
    OPENUSD_PHYSX_COMBINE_MIN = 1,
    OPENUSD_PHYSX_COMBINE_MULTIPLY = 2,
    OPENUSD_PHYSX_COMBINE_MAX = 3,
    OPENUSD_PHYSX_COMBINE_MODE_COUNT = 4
} openusd_physx_combine_mode;

typedef enum openusd_physx_actor_flags
{
    OPENUSD_PHYSX_ACTOR_FLAG_NONE = 0u,
    OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_GRAVITY = 1u << 0,
    OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_CCD = 1u << 1,
    OPENUSD_PHYSX_ACTOR_FLAG_START_ASLEEP = 1u << 2,
    /* Shorthand for the three angular locks below. */
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ROTATION = 1u << 3,
    /* Swept contact generation is expensive; speculative contacts are the
     * cheaper approximation and are independent of it. */
    OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_SPECULATIVE_CCD = 1u << 4,
    /* Keeps the accelerations of the previous step instead of clearing them,
     * which is what a body driven by an external controller needs. */
    OPENUSD_PHYSX_ACTOR_FLAG_RETAIN_ACCELERATIONS = 1u << 5,
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_X = 1u << 6,
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_Y = 1u << 7,
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_Z = 1u << 8,
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ANGULAR_X = 1u << 9,
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ANGULAR_Y = 1u << 10,
    OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ANGULAR_Z = 1u << 11,
    /* Integrates the gyroscopic term, without which a free body with a
     * non uniform inertia never precesses. */
    OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_GYROSCOPIC_FORCES = 1u << 12,
    /* Keeps the body out of the sleep scan entirely. */
    OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING = 1u << 13,
    OPENUSD_PHYSX_ACTOR_FLAG_ALL = 0x3FFFu
} openusd_physx_actor_flags;

typedef enum openusd_physx_shape_type
{
    OPENUSD_PHYSX_SHAPE_SPHERE = 0,
    OPENUSD_PHYSX_SHAPE_BOX = 1,
    OPENUSD_PHYSX_SHAPE_CAPSULE = 2,
    OPENUSD_PHYSX_SHAPE_PLANE = 3,
    OPENUSD_PHYSX_SHAPE_CONVEX_MESH = 4,
    OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH = 5,
    /* Analytic convex core geometry. The axis is the shape local X axis, which
     * is what PxConvexCore states, so the local pose carries whatever rotation
     * the authored axis needs. */
    OPENUSD_PHYSX_SHAPE_CYLINDER = 6,
    OPENUSD_PHYSX_SHAPE_CONE = 7,
    /* Row major height samples addressed through the height field section. */
    OPENUSD_PHYSX_SHAPE_HEIGHTFIELD = 8,
    OPENUSD_PHYSX_SHAPE_TYPE_COUNT = 9
} openusd_physx_shape_type;

typedef enum openusd_physx_shape_flags
{
    OPENUSD_PHYSX_SHAPE_FLAG_NONE = 0u,
    OPENUSD_PHYSX_SHAPE_FLAG_TRIGGER = 1u << 0,
    OPENUSD_PHYSX_SHAPE_FLAG_DISABLE_COLLISION = 1u << 1,
    OPENUSD_PHYSX_SHAPE_FLAG_DOUBLE_SIDED = 1u << 2,
    OPENUSD_PHYSX_SHAPE_FLAG_ALL = 0x7u
} openusd_physx_shape_flags;

typedef enum openusd_physx_joint_type
{
    OPENUSD_PHYSX_JOINT_FIXED = 0,
    OPENUSD_PHYSX_JOINT_REVOLUTE = 1,
    OPENUSD_PHYSX_JOINT_PRISMATIC = 2,
    OPENUSD_PHYSX_JOINT_SPHERICAL = 3,
    OPENUSD_PHYSX_JOINT_DISTANCE = 4,
    OPENUSD_PHYSX_JOINT_D6 = 5,
    OPENUSD_PHYSX_JOINT_TYPE_COUNT = 6
} openusd_physx_joint_type;

typedef enum openusd_physx_joint_flags
{
    OPENUSD_PHYSX_JOINT_FLAG_NONE = 0u,
    OPENUSD_PHYSX_JOINT_FLAG_DISABLED = 1u << 0,
    OPENUSD_PHYSX_JOINT_FLAG_COLLISION_ENABLED = 1u << 1,
    OPENUSD_PHYSX_JOINT_FLAG_LIMIT_ENABLED = 1u << 2,
    OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED = 1u << 3,
    /* The single axis drive is an acceleration drive rather than a force
     * drive, so its stiffness and damping are mass independent. */
    OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ACCELERATION = 1u << 4,
    /* Drive limits are expressed as forces rather than impulses. */
    OPENUSD_PHYSX_JOINT_FLAG_DRIVE_LIMITS_ARE_FORCES = 1u << 5,
    /* The limit is a soft spring described by limit_stiffness and
     * limit_damping rather than a hard stop with a restitution. */
    OPENUSD_PHYSX_JOINT_FLAG_LIMIT_SOFT = 1u << 6,
    /* The joint states an authored articulation but must be simulated as a
     * maximal coordinate joint anyway. */
    OPENUSD_PHYSX_JOINT_FLAG_EXCLUDE_FROM_ARTICULATION = 1u << 7,
    /* Reports the constraint force and torque of the joint through the result
     * page instead of only its break state. */
    OPENUSD_PHYSX_JOINT_FLAG_REPORT_FORCES = 1u << 8,
    OPENUSD_PHYSX_JOINT_FLAG_ALL = 0x1FFu
} openusd_physx_joint_flags;

/* One degree of freedom of a six degree of freedom joint. The order matches
 * PxD6Axis so a page states the axis it means rather than a remapped index. */
typedef enum openusd_physx_joint_axis
{
    OPENUSD_PHYSX_JOINT_AXIS_X = 0,
    OPENUSD_PHYSX_JOINT_AXIS_Y = 1,
    OPENUSD_PHYSX_JOINT_AXIS_Z = 2,
    OPENUSD_PHYSX_JOINT_AXIS_TWIST = 3,
    OPENUSD_PHYSX_JOINT_AXIS_SWING1 = 4,
    OPENUSD_PHYSX_JOINT_AXIS_SWING2 = 5,
    OPENUSD_PHYSX_JOINT_AXIS_COUNT = 6
} openusd_physx_joint_axis;

typedef enum openusd_physx_joint_motion
{
    OPENUSD_PHYSX_JOINT_MOTION_LOCKED = 0,
    OPENUSD_PHYSX_JOINT_MOTION_LIMITED = 1,
    OPENUSD_PHYSX_JOINT_MOTION_FREE = 2,
    OPENUSD_PHYSX_JOINT_MOTION_COUNT = 3
} openusd_physx_joint_motion;

typedef enum openusd_physx_joint_drive_flags
{
    OPENUSD_PHYSX_JOINT_DRIVE_FLAG_NONE = 0u,
    OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED = 1u << 0,
    OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ACCELERATION = 1u << 1,
    OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ALL = 0x3u
} openusd_physx_joint_drive_flags;

typedef enum openusd_physx_command_type
{
    OPENUSD_PHYSX_COMMAND_KINEMATIC_TARGET = 0,
    OPENUSD_PHYSX_COMMAND_TELEPORT = 1,
    OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY = 2,
    OPENUSD_PHYSX_COMMAND_SET_ANGULAR_VELOCITY = 3,
    OPENUSD_PHYSX_COMMAND_ADD_FORCE = 4,
    OPENUSD_PHYSX_COMMAND_ADD_TORQUE = 5,
    OPENUSD_PHYSX_COMMAND_ADD_IMPULSE = 6,
    OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT = 7,
    OPENUSD_PHYSX_COMMAND_WAKE = 8,
    OPENUSD_PHYSX_COMMAND_SLEEP = 9,
    OPENUSD_PHYSX_COMMAND_SET_SCENE_GRAVITY = 10,
    OPENUSD_PHYSX_COMMAND_ADD_IMPULSE_AT_POINT = 11,
    OPENUSD_PHYSX_COMMAND_ADD_ANGULAR_IMPULSE = 12,
    OPENUSD_PHYSX_COMMAND_CLEAR_FORCE = 13,
    OPENUSD_PHYSX_COMMAND_CLEAR_TORQUE = 14,
    /* Moves a character controller by the command vector over the step. The
     * vector is a displacement, not a velocity, which is what a controller
     * consumes. */
    OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER = 15,
    /* Drives a vehicle for the step. `vector` carries (throttle, brake, steer)
     * and `point` carries (handbrake, clutch, gear). Inputs accumulate over the
     * batch, so the last command wins per field only when it is submitted with
     * a different value.
     *
     * Every input is validated before a single command of the batch is
     * applied, and the whole batch is rejected when any of them is out of
     * range:
     *   - throttle, brake, handbrake and clutch are in `[0, 1]`;
     *   - steer is in `[-1, 1]`;
     *   - gear is a non negative integral value. `0` leaves the gear to the
     *     drivetrain: it asks the autobox to choose while the vehicle declares
     *     `OPENUSD_PHYSX_VEHICLE_FLAG_AUTOBOX_ENABLED`, and holds the gear the
     *     gearbox already targets while it does not, because a vehicle without
     *     an autobox has nothing that could choose a gear for the driver.
     *     `1` selects gear index `0`, which is the reverse
     *     gear, `2` selects neutral, and `n` selects gear index `n - 1` in
     *     general. The largest accepted value is
     *     `OPENUSD_PHYSX_MAX_VEHICLE_GEARS`, so the selected index is always
     *     inside the fixed gearbox and autobox arrays. A gear beyond the
     *     gearbox a vehicle actually declares is additionally clamped to that
     *     vehicle's highest configured gear when the command is applied. */
    OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT = 16,
    OPENUSD_PHYSX_COMMAND_TYPE_COUNT = 17
} openusd_physx_command_type;

/* Modifiers a command may declare. Every flag is only meaningful for a subset
 * of the command types; declaring a flag on any other type is rejected before a
 * single command of the batch is applied.
 *
 * Application point modes are mutually exclusive. When neither
 * OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL nor
 * OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS is set, the point of an
 * "at point" command is interpreted in world space. Force modes are mutually
 * exclusive as well; the default mode is force for the force and torque
 * commands and impulse for the impulse commands. */
typedef enum openusd_physx_command_flags
{
    OPENUSD_PHYSX_COMMAND_FLAG_NONE = 0u,
    /* The vector is a direction and the scalar carries the magnitude. A zero
     * length direction is rejected. */
    OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE = 1u << 0,
    /* The application point is expressed in the actor's local frame. */
    OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL = 1u << 1,
    /* The application point is ignored and the center of mass is used. */
    OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS = 1u << 2,
    /* Applies the vector as an acceleration instead of a force. */
    OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION = 1u << 3,
    /* Applies the vector as a direct velocity change instead of an impulse. */
    OPENUSD_PHYSX_COMMAND_FLAG_MODE_VELOCITY_CHANGE = 1u << 4,
    /* Leaves a sleeping body asleep instead of waking it. */
    OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE = 1u << 5,
    OPENUSD_PHYSX_COMMAND_FLAG_ALL = 0x3Fu
} openusd_physx_command_flags;

typedef enum openusd_physx_event_type
{
    OPENUSD_PHYSX_EVENT_SLEEP = 0,
    OPENUSD_PHYSX_EVENT_WAKE = 1,
    OPENUSD_PHYSX_EVENT_JOINT_BREAK = 2,
    OPENUSD_PHYSX_EVENT_CONTACT_FOUND = 3,
    OPENUSD_PHYSX_EVENT_CONTACT_LOST = 4,
    OPENUSD_PHYSX_EVENT_TRIGGER_ENTER = 5,
    OPENUSD_PHYSX_EVENT_TRIGGER_LEAVE = 6,
    OPENUSD_PHYSX_EVENT_CONTROLLER_HIT = 7,
    OPENUSD_PHYSX_EVENT_VEHICLE_GEAR_CHANGE = 8,
    OPENUSD_PHYSX_EVENT_TYPE_COUNT = 9
} openusd_physx_event_type;

/* Describes which optional fields of an event carry data. An event never
 * carries an uninitialized field: a field whose flag is clear is zero. */
typedef enum openusd_physx_event_flags
{
    OPENUSD_PHYSX_EVENT_FLAG_NONE = 0u,
    OPENUSD_PHYSX_EVENT_FLAG_HAS_POSITION = 1u << 0,
    OPENUSD_PHYSX_EVENT_FLAG_HAS_NORMAL = 1u << 1,
    OPENUSD_PHYSX_EVENT_FLAG_HAS_IMPULSE = 1u << 2,
    /* detail0 and detail1 name shapes rather than actors or joints. */
    OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE = 1u << 3,
    OPENUSD_PHYSX_EVENT_FLAG_ALL = 0xFu
} openusd_physx_event_flags;

typedef enum openusd_physx_diagnostic_severity
{
    OPENUSD_PHYSX_DIAGNOSTIC_INFO = 0,
    OPENUSD_PHYSX_DIAGNOSTIC_WARNING = 1,
    OPENUSD_PHYSX_DIAGNOSTIC_ERROR = 2,
    OPENUSD_PHYSX_DIAGNOSTIC_SEVERITY_COUNT = 3
} openusd_physx_diagnostic_severity;

typedef enum openusd_physx_diagnostic_code
{
    OPENUSD_PHYSX_DIAGNOSTIC_NONE = 0,
    OPENUSD_PHYSX_DIAGNOSTIC_UNSUPPORTED_SHAPE = 1,
    OPENUSD_PHYSX_DIAGNOSTIC_COOKING_FAILED = 2,
    OPENUSD_PHYSX_DIAGNOSTIC_ACTOR_CREATE_FAILED = 3,
    OPENUSD_PHYSX_DIAGNOSTIC_JOINT_CREATE_FAILED = 4,
    OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_TARGET_MISSING = 5,
    OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_REJECTED = 6,
    OPENUSD_PHYSX_DIAGNOSTIC_RESULT_OVERFLOW = 7,
    OPENUSD_PHYSX_DIAGNOSTIC_QUERY_REJECTED = 8,
    /* No CUDA device or context could be reached, so every GPU domain the page
     * declares was skipped while every CPU domain still built and stepped. */
    OPENUSD_PHYSX_DIAGNOSTIC_GPU_UNAVAILABLE = 9,
    /* One named GPU object could not be created or attached and was skipped on
     * its own; every other object of the same page still built. */
    OPENUSD_PHYSX_DIAGNOSTIC_GPU_OBJECT_SKIPPED = 10,
    OPENUSD_PHYSX_DIAGNOSTIC_CODE_COUNT = 11
} openusd_physx_diagnostic_code;

typedef enum openusd_physx_overflow_flags
{
    OPENUSD_PHYSX_OVERFLOW_NONE = 0u,
    OPENUSD_PHYSX_OVERFLOW_BODY_STATES = 1u << 0,
    OPENUSD_PHYSX_OVERFLOW_EVENTS = 1u << 1,
    OPENUSD_PHYSX_OVERFLOW_DIAGNOSTICS = 1u << 2,
    OPENUSD_PHYSX_OVERFLOW_DEBUG_LINES = 1u << 3,
    OPENUSD_PHYSX_OVERFLOW_QUERY_HITS = 1u << 4,
    /* The simulation SDK filled its own touch buffer for at least one request
     * and may have discarded an unknown number of touches before this library
     * ever saw them. dropped_hit_count is therefore a lower bound, not an exact
     * count, and the hits of the affected request are not guaranteed to be the
     * globally nearest ones. This flag is never set together with an exact
     * claim: a caller that needs exact counts must lower max_hits or raise the
     * hit capacity until it stops appearing. */
    OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED = 1u << 5,
    /* A deformation body or one of its vertices did not fit the declared
     * deformation capacity. The retained prefix is complete for every body it
     * did report, so a consumer never reads a partially written region. */
    OPENUSD_PHYSX_OVERFLOW_DEFORMATION = 1u << 6
} openusd_physx_overflow_flags;

typedef enum openusd_physx_world_state
{
    OPENUSD_PHYSX_WORLD_STATE_EMPTY = 0,
    OPENUSD_PHYSX_WORLD_STATE_READY = 1,
    OPENUSD_PHYSX_WORLD_STATE_FAULTED = 2,
    OPENUSD_PHYSX_WORLD_STATE_COUNT = 3
} openusd_physx_world_state;

typedef enum openusd_physx_world_flags
{
    OPENUSD_PHYSX_WORLD_FLAG_NONE = 0u,
    OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS = 1u << 0,
    OPENUSD_PHYSX_WORLD_FLAG_ENABLE_DEBUG = 1u << 1,
    OPENUSD_PHYSX_WORLD_FLAG_ENABLE_CCD = 1u << 2,
    OPENUSD_PHYSX_WORLD_FLAG_DETERMINISTIC = 1u << 3,
    OPENUSD_PHYSX_WORLD_FLAG_ALL = 0xFu
} openusd_physx_world_flags;

typedef enum openusd_physx_body_state_flags
{
    OPENUSD_PHYSX_BODY_STATE_FLAG_NONE = 0u,
    OPENUSD_PHYSX_BODY_STATE_FLAG_SLEEPING = 1u << 0,
    OPENUSD_PHYSX_BODY_STATE_FLAG_KINEMATIC = 1u << 1,
    /* The state belongs to a reduced coordinate articulation link rather than
     * to a stand alone rigid body. */
    OPENUSD_PHYSX_BODY_STATE_FLAG_ARTICULATION_LINK = 1u << 2,
    /* The state belongs to a character controller. A controller reports its
     * foot to centre pose and its last move velocity, and never sleeps. */
    OPENUSD_PHYSX_BODY_STATE_FLAG_CONTROLLER = 1u << 3,
    OPENUSD_PHYSX_BODY_STATE_FLAG_VEHICLE_WHEEL = 1u << 4,
    OPENUSD_PHYSX_BODY_STATE_FLAG_ALL = 0x1Fu
} openusd_physx_body_state_flags;

typedef enum openusd_physx_capability_flags
{
    OPENUSD_PHYSX_CAPABILITY_NONE = 0u,
    OPENUSD_PHYSX_CAPABILITY_CPU_RIGID_BODIES = 1u << 0,
    OPENUSD_PHYSX_CAPABILITY_MESH_COOKING = 1u << 1,
    OPENUSD_PHYSX_CAPABILITY_JOINTS = 1u << 2,
    OPENUSD_PHYSX_CAPABILITY_SCENE_QUERIES = 1u << 3,
    OPENUSD_PHYSX_CAPABILITY_SLEEP_EVENTS = 1u << 4,
    OPENUSD_PHYSX_CAPABILITY_JOINT_BREAK_EVENTS = 1u << 5,
    OPENUSD_PHYSX_CAPABILITY_CONTACT_EVENTS = 1u << 6,
    OPENUSD_PHYSX_CAPABILITY_DEBUG_LINES = 1u << 7,
    OPENUSD_PHYSX_CAPABILITY_GPU_DOMAINS = 1u << 8,
    OPENUSD_PHYSX_CAPABILITY_TRIGGER_EVENTS = 1u << 9,
    OPENUSD_PHYSX_CAPABILITY_CONTROLLER_HIT_EVENTS = 1u << 10,
    OPENUSD_PHYSX_CAPABILITY_BATCHED_QUERIES = 1u << 11,
    /* Analytic cylinder and cone collision geometry, which needs the convex
     * core geometry PhysX 5.5 introduced. */
    OPENUSD_PHYSX_CAPABILITY_CONVEX_CORE_SHAPES = 1u << 12,
    OPENUSD_PHYSX_CAPABILITY_HEIGHTFIELD_SHAPES = 1u << 13,
    /* Per axis motion, limit, and drive on a six degree of freedom joint. */
    OPENUSD_PHYSX_CAPABILITY_D6_JOINT_DRIVES = 1u << 14,
    /* Per shape contact, rest, and torsional patch offsets and per material
     * combine modes. */
    OPENUSD_PHYSX_CAPABILITY_SHAPE_OFFSETS = 1u << 15,
    /* Per body solver iterations, velocity and impulse budgets, per axis
     * locks, speculative continuous collision, and retained accelerations. */
    OPENUSD_PHYSX_CAPABILITY_RIGID_BODY_TUNING = 1u << 16,
    /* Reduced coordinate articulations with per link joints, limits, drives,
     * and armature. */
    OPENUSD_PHYSX_CAPABILITY_ARTICULATIONS = 1u << 17,
    /* Capsule and box character controllers with per scene managers, move
     * commands, and controller hit reporting. */
    OPENUSD_PHYSX_CAPABILITY_CHARACTER_CONTROLLERS = 1u << 18,
    /* Fixed and spatial articulation tendons are built and simulated. */
    OPENUSD_PHYSX_CAPABILITY_ARTICULATION_TENDONS = 1u << 19,
    /* Articulation mimic joints are built and simulated. */
    OPENUSD_PHYSX_CAPABILITY_ARTICULATION_MIMIC_JOINTS = 1u << 20,
    /* The PhysX vehicle2 CPU path is built and stepped. Only reported when a
     * vehicle can actually be driven; it is never reported for a build that
     * merely carries the vehicle records. */
    OPENUSD_PHYSX_CAPABILITY_VEHICLES = 1u << 21,
    /* A CUDA context manager was created, reported a valid context, and the
     * device it names accepts the GPU broad phase and solver PhysX needs. It is
     * never reported because the library was merely compiled with CUDA support:
     * the context is created and validated during capability negotiation, and
     * a build that cannot reach a device reports none of the bits below. */
    OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT = 1u << 22,
    /* Position based dynamics particle systems with solid and fluid particle
     * bodies. Implies OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT. */
    OPENUSD_PHYSX_CAPABILITY_PARTICLE_SYSTEMS = 1u << 23,
    /* Surface deformables, which is what a simulated cloth is built from.
     * Implies OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT. */
    OPENUSD_PHYSX_CAPABILITY_SURFACE_DEFORMABLES = 1u << 24,
    /* Finite element volume deformables. Implies
     * OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT. */
    OPENUSD_PHYSX_CAPABILITY_VOLUME_DEFORMABLES = 1u << 25,
    /* Per vertex deformation results are published for every built GPU domain.
     * Implies OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT. */
    OPENUSD_PHYSX_CAPABILITY_DEFORMATION_RESULTS = 1u << 26
} openusd_physx_capability_flags;

typedef enum openusd_physx_query_type
{
    OPENUSD_PHYSX_QUERY_RAYCAST = 0,
    OPENUSD_PHYSX_QUERY_SWEEP = 1,
    OPENUSD_PHYSX_QUERY_OVERLAP = 2,
    OPENUSD_PHYSX_QUERY_TYPE_COUNT = 3
} openusd_physx_query_type;

typedef enum openusd_physx_query_flags
{
    OPENUSD_PHYSX_QUERY_FLAG_NONE = 0u,
    OPENUSD_PHYSX_QUERY_FLAG_ANY_HIT = 1u << 0,
    OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_STATIC = 1u << 1,
    OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_DYNAMIC = 1u << 2,
    /* Drops every hit against a shape that was built as a trigger. */
    OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_TRIGGERS = 1u << 3,
    /* Reports sweeps that already overlap at the start pose instead of
     * discarding them. Such a hit carries a zero distance, no position, and no
     * normal, and is tagged with OPENUSD_PHYSX_QUERY_HIT_FLAG_INITIAL_OVERLAP.
     * When the flag is absent every initially overlapping sweep hit is dropped
     * before it reaches the caller, which is the only way a sweep can promise
     * that every hit it reports carries a usable position and normal. */
    OPENUSD_PHYSX_QUERY_FLAG_SWEEP_INITIAL_OVERLAP = 1u << 4,
    OPENUSD_PHYSX_QUERY_FLAG_ALL = 0x1Fu
} openusd_physx_query_flags;

/* Describes which fields of a query hit carry data. PhysX does not report a
 * position, a normal, or a distance for an overlap or for a sweep that starts
 * already overlapping, so those fields stay zero and their flags stay clear
 * instead of reporting a fabricated value. */
typedef enum openusd_physx_query_hit_flags
{
    OPENUSD_PHYSX_QUERY_HIT_FLAG_NONE = 0u,
    OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_POSITION = 1u << 0,
    OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_NORMAL = 1u << 1,
    OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE = 1u << 2,
    /* face_index names a triangle of a triangle mesh or a polygon of a convex
     * mesh; it is meaningless for analytic shapes. */
    OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_FACE = 1u << 3,
    OPENUSD_PHYSX_QUERY_HIT_FLAG_INITIAL_OVERLAP = 1u << 4,
    OPENUSD_PHYSX_QUERY_HIT_FLAG_TRIGGER = 1u << 5,
    OPENUSD_PHYSX_QUERY_HIT_FLAG_ALL = 0x3Fu
} openusd_physx_query_hit_flags;

typedef enum openusd_physx_page_section
{
    OPENUSD_PHYSX_SECTION_HEADER = 0,
    OPENUSD_PHYSX_SECTION_STRINGS = 1,
    OPENUSD_PHYSX_SECTION_IDENTITIES = 2,
    OPENUSD_PHYSX_SECTION_SCENES = 3,
    OPENUSD_PHYSX_SECTION_MATERIALS = 4,
    OPENUSD_PHYSX_SECTION_SHAPES = 5,
    OPENUSD_PHYSX_SECTION_ACTORS = 6,
    OPENUSD_PHYSX_SECTION_ACTOR_SHAPES = 7,
    OPENUSD_PHYSX_SECTION_JOINTS = 8,
    OPENUSD_PHYSX_SECTION_FILTER_PAIRS = 9,
    OPENUSD_PHYSX_SECTION_MESH_POINTS = 10,
    OPENUSD_PHYSX_SECTION_MESH_INDICES = 11,
    OPENUSD_PHYSX_SECTION_CAPACITIES = 12,
    OPENUSD_PHYSX_SECTION_HEIGHTFIELD_SAMPLES = 13,
    OPENUSD_PHYSX_SECTION_ARTICULATIONS = 14,
    OPENUSD_PHYSX_SECTION_ARTICULATION_LINKS = 15,
    OPENUSD_PHYSX_SECTION_CONTROLLERS = 16,
    OPENUSD_PHYSX_SECTION_ARTICULATION_TENDONS = 17,
    OPENUSD_PHYSX_SECTION_ARTICULATION_TENDON_NODES = 18,
    OPENUSD_PHYSX_SECTION_ARTICULATION_MIMIC_JOINTS = 19,
    OPENUSD_PHYSX_SECTION_VEHICLES = 20,
    OPENUSD_PHYSX_SECTION_VEHICLE_WHEELS = 21,
    OPENUSD_PHYSX_SECTION_PARTICLE_MATERIALS = 22,
    OPENUSD_PHYSX_SECTION_PARTICLE_SYSTEMS = 23,
    OPENUSD_PHYSX_SECTION_PARTICLE_BODIES = 24,
    OPENUSD_PHYSX_SECTION_DEFORMABLE_MATERIALS = 25,
    OPENUSD_PHYSX_SECTION_DEFORMABLES = 26,
    OPENUSD_PHYSX_SECTION_COUNT = 27
} openusd_physx_page_section;

typedef enum openusd_physx_page_error
{
    OPENUSD_PHYSX_PAGE_ERROR_NONE = 0,
    OPENUSD_PHYSX_PAGE_ERROR_NULL = 1,
    OPENUSD_PHYSX_PAGE_ERROR_SIZE = 2,
    OPENUSD_PHYSX_PAGE_ERROR_MAGIC = 3,
    OPENUSD_PHYSX_PAGE_ERROR_ABI = 4,
    OPENUSD_PHYSX_PAGE_ERROR_HEADER_SIZE = 5,
    OPENUSD_PHYSX_PAGE_ERROR_ALIGNMENT = 6,
    OPENUSD_PHYSX_PAGE_ERROR_RANGE = 7,
    OPENUSD_PHYSX_PAGE_ERROR_OVERLAP = 8,
    OPENUSD_PHYSX_PAGE_ERROR_COUNT_LIMIT = 9,
    OPENUSD_PHYSX_PAGE_ERROR_VALUE = 10,
    OPENUSD_PHYSX_PAGE_ERROR_REFERENCE = 11,
    OPENUSD_PHYSX_PAGE_ERROR_DUPLICATE_ID = 12,
    OPENUSD_PHYSX_PAGE_ERROR_ENCODING = 13,
    OPENUSD_PHYSX_PAGE_ERROR_CAPACITY = 14,
    OPENUSD_PHYSX_PAGE_ERROR_COUNT = 15
} openusd_physx_page_error;

/* ---------------------------------------------------------------------------
 * Pointer free build page records.
 * Every record has a fixed layout, explicit reserved padding, and a size that
 * is a multiple of OPENUSD_PHYSX_PAGE_ALIGNMENT.
 * ------------------------------------------------------------------------ */

typedef struct openusd_physx_page_span
{
    uint32_t offset; /* byte offset from the first byte of the page */
    uint32_t count;  /* element count, or byte count for the string section */
} openusd_physx_page_span;

typedef struct openusd_physx_result_capacities
{
    uint32_t max_body_states;
    uint32_t max_events;
    uint32_t max_diagnostics;
    uint32_t max_debug_lines;
    uint32_t max_query_hits;
    /* Per vertex deformation output. A build that carries no GPU domain, or a
     * build whose GPU domains were all skipped, declares zero for both, which
     * is exactly a caller that allocates no deformation buffer at all. */
    uint32_t max_deformation_bodies;
    uint32_t max_deformation_points;
    uint32_t reserved0;
} openusd_physx_result_capacities;

typedef struct openusd_physx_build_page_header
{
    uint64_t magic;
    uint32_t abi_version;
    uint32_t header_size;
    uint64_t byte_size;
    uint64_t revision;
    uint64_t source_hash;
    double meters_per_unit;
    double kilograms_per_unit;
    double time_codes_per_second;
    double start_time_code;
    double end_time_code;
    uint32_t up_axis;
    uint32_t flags;
    uint32_t simulation_rate_hz;
    uint32_t max_substeps;
    openusd_physx_page_span string_bytes;
    openusd_physx_page_span identities;
    openusd_physx_page_span scenes;
    openusd_physx_page_span materials;
    openusd_physx_page_span shapes;
    openusd_physx_page_span actors;
    openusd_physx_page_span actor_shapes;
    openusd_physx_page_span joints;
    openusd_physx_page_span filter_pairs;
    openusd_physx_page_span mesh_points;
    openusd_physx_page_span mesh_indices;
    openusd_physx_page_span heightfield_samples;
    openusd_physx_page_span articulations;
    openusd_physx_page_span articulation_links;
    openusd_physx_page_span controllers;
    openusd_physx_page_span articulation_tendons;
    openusd_physx_page_span articulation_tendon_nodes;
    openusd_physx_page_span articulation_mimic_joints;
    openusd_physx_page_span vehicles;
    openusd_physx_page_span vehicle_wheels;
    openusd_physx_page_span particle_materials;
    openusd_physx_page_span particle_systems;
    openusd_physx_page_span particle_bodies;
    openusd_physx_page_span deformable_materials;
    openusd_physx_page_span deformables;
    openusd_physx_result_capacities capacities;
    uint64_t reserved[3];
} openusd_physx_build_page_header;

typedef struct openusd_physx_identity
{
    uint64_t id;
    uint32_t path_offset; /* byte offset inside the string section */
    uint32_t path_length; /* byte length, no terminator, UTF-8 */
    uint32_t instance_domain;
    uint32_t instance_index;
} openusd_physx_identity;

typedef struct openusd_physx_scene_desc
{
    uint64_t id;
    openusd_physx_vec3f gravity_direction;
    float gravity_magnitude;
    uint32_t flags;
    uint32_t position_iterations;
    uint32_t velocity_iterations;
    float bounce_threshold;
    float contact_offset;
    uint32_t reserved0;
} openusd_physx_scene_desc;

/* One row major height field sample. The height is a raw integer that the
 * shape height_scale turns into a distance, which is what a height field
 * needs so that a whole terrain stays exact under a single scale. */
typedef struct openusd_physx_heightfield_sample
{
    int16_t height;
    uint8_t material0;
    uint8_t material1;
} openusd_physx_heightfield_sample;

typedef struct openusd_physx_material_desc
{
    uint64_t id;
    float static_friction;
    float dynamic_friction;
    float restitution;
    float density;
    uint32_t flags;
    /* openusd_physx_combine_mode. A default initialized record therefore asks
     * for the average, which is what an unauthored material means. */
    uint32_t friction_combine_mode;
    uint32_t restitution_combine_mode;
    /* Contact damping used when OPENUSD_PHYSX_MATERIAL_FLAG_COMPLIANT_CONTACT
     * is set; ignored otherwise. */
    float damping;
} openusd_physx_material_desc;

typedef struct openusd_physx_shape_desc
{
    uint64_t id;
    uint32_t type;
    uint32_t flags;
    openusd_physx_transform local_pose;
    openusd_physx_vec3f scale;
    openusd_physx_vec3f half_extents;
    float radius;
    float half_height;
    uint32_t point_offset; /* element offset into the mesh point section */
    uint32_t point_count;
    uint32_t index_offset; /* element offset into the mesh index section */
    uint32_t index_count;
    int32_t material_index; /* -1 selects the world default material */
    /* Zero asks for the value the scene declares. A positive contact offset
     * must stay above the rest offset. */
    float contact_offset;
    float rest_offset;
    /* Radius of the contact patch used to resolve torsional friction. Zero
     * keeps the simulation SDK default of no torsional friction. */
    float torsional_patch_radius;
    float min_torsional_patch_radius;
    /* Height field sample window. Samples are read row major from the height
     * field sample section and are only meaningful for a height field. */
    uint32_t sample_offset;
    uint32_t row_count;
    uint32_t column_count;
    float height_scale;
    float row_scale;
    float column_scale;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_shape_desc;

typedef struct openusd_physx_actor_desc
{
    uint64_t id;
    int32_t scene_index;
    uint32_t type;
    openusd_physx_transform world_pose;
    openusd_physx_vec3f linear_velocity;
    openusd_physx_vec3f angular_velocity;
    float mass; /* 0 requests density based mass computation */
    openusd_physx_vec3f center_of_mass;
    openusd_physx_vec3f inertia;
    /* Rotation from the actor frame into the frame the diagonal inertia is
     * stated in. It must be a finite unit quaternion, or all zero, which is
     * what a default initialized description carries and is read as the
     * identity rotation. */
    openusd_physx_quatf principal_axes;
    float linear_damping;
    float angular_damping;
    uint32_t flags;
    uint32_t shape_offset; /* element offset into the actor shape section */
    uint32_t shape_count;
    uint32_t collision_group;
    /* Zero selects the iteration counts the owning scene declares. */
    uint32_t position_iterations;
    uint32_t velocity_iterations;
    /* Zero selects the simulation SDK default for every budget below. */
    float max_linear_velocity;
    float max_angular_velocity;
    float max_depenetration_velocity;
    float max_contact_impulse;
    /* Zero keeps the sleep threshold the scene decides. Use
     * OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING to say a body never sleeps. */
    float sleep_threshold;
    float stabilization_threshold;
    /* Seconds the body stays awake after it is woken. Zero keeps the default. */
    float wake_counter;
    float min_ccd_advance_coefficient;
    float contact_slop_coefficient;
    uint32_t reserved0;
} openusd_physx_actor_desc;

typedef struct openusd_physx_actor_shape_ref
{
    uint32_t shape_index;
    int32_t material_index; /* -1 keeps the material of the shape */
} openusd_physx_actor_shape_ref;

/* One joint.
 *
 * The single axis fields (axis, lower_limit, upper_limit, drive_*) describe a
 * revolute, prismatic, spherical, or distance joint. A six degree of freedom
 * joint additionally reads the per axis arrays, which are indexed by
 * openusd_physx_joint_axis. A default initialized record locks every axis,
 * which is exactly a fixed joint, so a page that does not author the arrays
 * still describes something legal. */
typedef struct openusd_physx_joint_desc
{
    uint64_t id;
    uint32_t type;
    uint32_t flags;
    int32_t actor0_index; /* -1 attaches to the static world frame */
    int32_t actor1_index;
    openusd_physx_transform local_frame0;
    openusd_physx_transform local_frame1;
    uint32_t axis;
    float lower_limit;
    float upper_limit;
    float min_distance;
    float max_distance;
    float cone_angle0;
    float cone_angle1;
    float drive_stiffness;
    float drive_damping;
    float drive_max_force;
    float drive_target_position;
    float drive_target_velocity;
    float break_force;
    float break_torque;
    /* Soft limit spring, read when OPENUSD_PHYSX_JOINT_FLAG_LIMIT_SOFT is set. */
    float limit_stiffness;
    float limit_damping;
    /* Hard limit response, read when the limit is not soft. */
    float limit_restitution;
    float limit_bounce_threshold;
    float limit_contact_distance;
    /* Mass scaling of the two attached bodies. Zero keeps the unscaled mass. */
    float inv_mass_scale0;
    float inv_inertia_scale0;
    float inv_mass_scale1;
    float inv_inertia_scale1;
    uint32_t reserved0;
    uint32_t reserved1;
    uint32_t reserved2;
    uint32_t reserved3;
    /* Per axis description of a six degree of freedom joint. */
    uint32_t motion[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_lower_limit[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_upper_limit[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_drive_stiffness[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_drive_damping[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_drive_max_force[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float axis_drive_target_velocity[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    uint32_t axis_drive_flags[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
} openusd_physx_joint_desc;

typedef struct openusd_physx_filter_pair
{
    uint32_t actor0_index;
    uint32_t actor1_index;
} openusd_physx_filter_pair;

/* ---------------------------------------------------------------------------
 * Reduced coordinate articulations.
 *
 * An articulation owns a contiguous window of the link section. The first link
 * of the window is the root and must name no parent; every other link must name
 * a parent that appears earlier in the same window, so a page describes a tree
 * by construction and can never describe a cycle.
 * ------------------------------------------------------------------------ */

typedef enum openusd_physx_articulation_flags
{
    OPENUSD_PHYSX_ARTICULATION_FLAG_NONE = 0u,
    /* The root link is welded to the world instead of being free to move. */
    OPENUSD_PHYSX_ARTICULATION_FLAG_FIXED_BASE = 1u << 0,
    OPENUSD_PHYSX_ARTICULATION_FLAG_SELF_COLLISION = 1u << 1,
    OPENUSD_PHYSX_ARTICULATION_FLAG_DISABLE_SLEEPING = 1u << 2,
    /* Integrate the gyroscopic term on every link. */
    OPENUSD_PHYSX_ARTICULATION_FLAG_ENABLE_GYROSCOPIC_FORCES = 1u << 3,
    OPENUSD_PHYSX_ARTICULATION_FLAG_ALL = 0xFu
} openusd_physx_articulation_flags;

/* The joint that binds one link to its parent. The root link of an articulation
 * carries OPENUSD_PHYSX_ARTICULATION_JOINT_NONE. */
typedef enum openusd_physx_articulation_joint_type
{
    OPENUSD_PHYSX_ARTICULATION_JOINT_NONE = 0,
    OPENUSD_PHYSX_ARTICULATION_JOINT_FIXED = 1,
    OPENUSD_PHYSX_ARTICULATION_JOINT_REVOLUTE = 2,
    OPENUSD_PHYSX_ARTICULATION_JOINT_PRISMATIC = 3,
    OPENUSD_PHYSX_ARTICULATION_JOINT_SPHERICAL = 4,
    OPENUSD_PHYSX_ARTICULATION_JOINT_TYPE_COUNT = 5
} openusd_physx_articulation_joint_type;

typedef enum openusd_physx_articulation_link_flags
{
    OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_NONE = 0u,
    OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_DISABLE_GRAVITY = 1u << 0,
    /* The joint drives are read as accelerations rather than as forces. */
    OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_DRIVE_ACCELERATION = 1u << 1,
    OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_ALL = 0x3u
} openusd_physx_articulation_link_flags;

typedef struct openusd_physx_articulation_desc
{
    uint64_t id;
    int32_t scene_index;
    uint32_t flags;
    /* Element window into the articulation link section. */
    uint32_t link_offset;
    uint32_t link_count;
    /* Zero selects the iteration counts the owning scene declares. */
    uint32_t position_iterations;
    uint32_t velocity_iterations;
    /* Zero keeps the simulation SDK default for every budget below. */
    float sleep_threshold;
    float stabilization_threshold;
    float max_joint_velocity;
    float wake_counter;
    uint32_t reserved0;
    uint32_t reserved1;
    uint32_t reserved2;
    uint32_t reserved3;
} openusd_physx_articulation_desc;

/* One articulation link and the joint that binds it to its parent.
 *
 * The per axis arrays are indexed by openusd_physx_joint_axis, exactly as a six
 * degree of freedom joint is, so a reader that already understands a joint
 * record understands a link record without a second convention. A revolute
 * joint reads OPENUSD_PHYSX_JOINT_AXIS_TWIST, a prismatic joint reads
 * OPENUSD_PHYSX_JOINT_AXIS_X, and a spherical joint reads the two swing axes. */
typedef struct openusd_physx_articulation_link_desc
{
    uint64_t id;
    /* Identity of the parent link, or zero for the root link. */
    uint64_t parent_id;
    openusd_physx_transform world_pose;
    /* Joint frame stated in the parent link frame. */
    openusd_physx_transform parent_frame;
    /* Joint frame stated in this link frame. */
    openusd_physx_transform child_frame;
    openusd_physx_vec3f center_of_mass;
    openusd_physx_vec3f inertia;
    openusd_physx_quatf principal_axes;
    float mass; /* 0 requests density based mass computation */
    float linear_damping;
    float angular_damping;
    float max_linear_velocity;
    float max_angular_velocity;
    /* Coulomb friction of the inbound joint. Zero keeps the SDK default. */
    float joint_friction;
    float max_joint_velocity;
    uint32_t joint_type; /* openusd_physx_articulation_joint_type */
    uint32_t flags;
    uint32_t shape_offset; /* element offset into the actor shape section */
    uint32_t shape_count;
    uint32_t collision_group;
    uint32_t reserved0;
    /* Per axis joint description. */
    uint32_t motion[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float lower_limit[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float upper_limit[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float drive_stiffness[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float drive_damping[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float drive_max_force[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    float drive_target_velocity[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    uint32_t drive_flags[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
    /* Additional inertia on the joint axis, which is what makes a stiff drive
     * stable at a coarse time step. Zero adds none. */
    float armature[OPENUSD_PHYSX_JOINT_AXIS_COUNT];
} openusd_physx_articulation_link_desc;

/* ---------------------------------------------------------------------------
 * Character controllers.
 * ------------------------------------------------------------------------ */

typedef enum openusd_physx_controller_shape
{
    OPENUSD_PHYSX_CONTROLLER_CAPSULE = 0,
    OPENUSD_PHYSX_CONTROLLER_BOX = 1,
    OPENUSD_PHYSX_CONTROLLER_SHAPE_COUNT = 2
} openusd_physx_controller_shape;

/* What a controller does once it stands on ground steeper than its slope
 * limit. Preventing the climb is what stops a character walking up a wall. */
typedef enum openusd_physx_controller_non_walkable_mode
{
    OPENUSD_PHYSX_CONTROLLER_PREVENT_CLIMBING = 0,
    OPENUSD_PHYSX_CONTROLLER_PREVENT_CLIMBING_AND_FORCE_SLIDING = 1,
    OPENUSD_PHYSX_CONTROLLER_NON_WALKABLE_MODE_COUNT = 2
} openusd_physx_controller_non_walkable_mode;

/* How a capsule controller decides it may climb a step. The easy mode uses the
 * capsule bottom sphere only, which lets a character walk over small ledges. */
typedef enum openusd_physx_controller_climbing_mode
{
    OPENUSD_PHYSX_CONTROLLER_CLIMBING_EASY = 0,
    OPENUSD_PHYSX_CONTROLLER_CLIMBING_CONSTRAINED = 1,
    OPENUSD_PHYSX_CONTROLLER_CLIMBING_MODE_COUNT = 2
} openusd_physx_controller_climbing_mode;

typedef enum openusd_physx_controller_flags
{
    OPENUSD_PHYSX_CONTROLLER_FLAG_NONE = 0u,
    /* Report a controller hit event for every shape, controller, and obstacle
     * the controller touches while it moves. */
    OPENUSD_PHYSX_CONTROLLER_FLAG_REPORT_HITS = 1u << 0,
    /* Apply the scene gravity to the controller between move commands so a
     * controller that is not commanded still falls. */
    OPENUSD_PHYSX_CONTROLLER_FLAG_APPLY_GRAVITY = 1u << 1,
    OPENUSD_PHYSX_CONTROLLER_FLAG_ALL = 0x3u
} openusd_physx_controller_flags;

typedef struct openusd_physx_controller_desc
{
    uint64_t id;
    int32_t scene_index;
    uint32_t shape; /* openusd_physx_controller_shape */
    openusd_physx_vec3f position;
    /* Must be a finite non zero direction, or all zero, which reads as the up
     * axis the page declares. */
    openusd_physx_vec3f up_direction;
    /* Capsule dimensions. The height is the distance between the two sphere
     * centres, exactly as the simulation SDK states it. */
    float radius;
    float height;
    /* Box dimensions, read only for a box controller. */
    openusd_physx_vec3f half_extents;
    /* Radians. Zero keeps the SDK default. */
    float slope_limit;
    float step_offset;
    float contact_offset;
    float density;
    float scale_coefficient;
    float volume_growth;
    uint32_t flags;
    uint32_t non_walkable_mode;
    uint32_t climbing_mode;
    int32_t material_index; /* -1 selects the world default material */
    uint32_t collision_group;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_controller_desc;

/* Which PhysX tendon a tendon record builds. */
typedef enum openusd_physx_tendon_type
{
    OPENUSD_PHYSX_TENDON_FIXED = 0,
    OPENUSD_PHYSX_TENDON_SPATIAL = 1,
    OPENUSD_PHYSX_TENDON_TYPE_COUNT = 2
} openusd_physx_tendon_type;

/* Modifiers a tendon may declare. */
typedef enum openusd_physx_tendon_flags
{
    OPENUSD_PHYSX_TENDON_FLAG_NONE = 0u,
    /* Apply the low and high limit carried by the record. A tendon that leaves
     * this clear runs unlimited, which is what a default record describes. */
    OPENUSD_PHYSX_TENDON_FLAG_LIMIT_ENABLED = 1u << 0,
    OPENUSD_PHYSX_TENDON_FLAG_ALL = 0x1u
} openusd_physx_tendon_flags;

/* One fixed or spatial tendon on one articulation.
 *
 * A fixed tendon couples joint axes: its nodes are tendon joints, each naming a
 * link and one axis of that link's inbound joint. A spatial tendon couples
 * points in space: its nodes are attachments, each naming a link and an offset
 * in that link's frame. Both kinds share this record because PhysX shares the
 * stiffness, damping, limit stiffness and offset on `PxArticulationTendon`. */
typedef struct openusd_physx_tendon_desc
{
    uint64_t id;
    uint32_t articulation_index; /* index into the articulation section */
    uint32_t type;               /* openusd_physx_tendon_type */
    uint32_t node_offset;        /* first node in the tendon node section */
    uint32_t node_count;         /* nodes owned by this tendon, at least one */
    uint32_t flags;              /* openusd_physx_tendon_flags */
    uint32_t reserved0;
    float stiffness;
    float damping;
    float limit_stiffness;
    float offset;
    /* Fixed tendons only. A spatial tendon states its rest length and limits
     * per leaf attachment instead. */
    float rest_length;
    float low_limit;
    float high_limit;
    float reserved1;
} openusd_physx_tendon_desc;

/* One node of a tendon: a tendon joint for a fixed tendon, an attachment for a
 * spatial tendon.
 *
 * `parent_index` is zero for the node that roots the tendon and otherwise the
 * one based local index of a node that appears earlier in the same window, so a
 * cycle is unrepresentable rather than merely rejected. */
typedef struct openusd_physx_tendon_node_desc
{
    uint64_t id;
    uint32_t parent_index; /* 0 = tendon root, else 1 based local node index */
    uint32_t link_index;   /* local link index inside the articulation window */
    uint32_t axis;         /* openusd_physx_joint_axis, fixed tendons only */
    uint32_t flags;        /* openusd_physx_tendon_flags */
    float coefficient;
    float recip_coefficient;      /* fixed tendons only */
    openusd_physx_vec3f relative_offset; /* spatial tendons only */
    float rest_length;            /* spatial attachments only */
    float low_limit;
    float high_limit;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_tendon_node_desc;

/* One mimic joint, which couples one axis of one articulation joint to one axis
 * of another with a gear ratio and an offset.
 *
 * Both links name a local link index inside the articulation window; the joint
 * used is that link's inbound joint, so neither link may be the root. */
typedef struct openusd_physx_mimic_joint_desc
{
    uint64_t id;
    uint32_t articulation_index;
    uint32_t link_a;
    uint32_t axis_a; /* openusd_physx_joint_axis */
    uint32_t link_b;
    uint32_t axis_b; /* openusd_physx_joint_axis */
    uint32_t reserved0;
    float gear_ratio;
    float offset;
    float natural_frequency;
    float damping_ratio;
    uint64_t reserved1;
    uint64_t reserved2;
} openusd_physx_mimic_joint_desc;

/* How a vehicle delivers torque to its wheels. The engine drivetrain is the
 * only path this ABI models, because it is the one that carries the engine,
 * clutch, gearbox, autobox and differential a drivable vehicle needs. */
typedef enum openusd_physx_vehicle_drive
{
    /* A full engine, clutch, gearbox, autobox and differential drivetrain. */
    OPENUSD_PHYSX_VEHICLE_DRIVE_ENGINE = 0,
    OPENUSD_PHYSX_VEHICLE_DRIVE_COUNT = 1
} openusd_physx_vehicle_drive;

/* Which road geometry query a vehicle uses under each wheel. */
typedef enum openusd_physx_vehicle_query
{
    OPENUSD_PHYSX_VEHICLE_QUERY_RAYCAST = 0,
    OPENUSD_PHYSX_VEHICLE_QUERY_SWEEP = 1,
    OPENUSD_PHYSX_VEHICLE_QUERY_COUNT = 2
} openusd_physx_vehicle_query;

/* Modifiers a vehicle may declare. */
typedef enum openusd_physx_vehicle_flags
{
    OPENUSD_PHYSX_VEHICLE_FLAG_NONE = 0u,
    /* Build the vehicle with an automatic gearbox. The autobox picks the gear
     * whenever a command asks for gear zero, and the vehicle starts in neutral
     * because the autobox pulls it out of neutral as soon as the engine turns.
     * Without this flag no autobox is built at all: gear zero holds the gear
     * the gearbox already targets, only an explicit gear command shifts, and
     * the vehicle starts in its first forward gear so that a manual drivetrain
     * is drivable from the first step. An explicit gear command is honoured
     * either way. */
    OPENUSD_PHYSX_VEHICLE_FLAG_AUTOBOX_ENABLED = 1u << 0,
    /* Publish one body state per wheel carrying the wheel world transform. */
    OPENUSD_PHYSX_VEHICLE_FLAG_PUBLISH_WHEELS = 1u << 1,
    /* Limit the speed at which a suspension may expand, which stops a wheel
     * from leaving the ground on a sharp crest. */
    OPENUSD_PHYSX_VEHICLE_FLAG_LIMIT_SUSPENSION_EXPANSION = 1u << 2,
    OPENUSD_PHYSX_VEHICLE_FLAG_ALL = 0x7u
} openusd_physx_vehicle_flags;

/* One vehicle. The chassis is an actor declared by the actor section, which
 * must be dynamic and must not be kinematic, so the chassis keeps the identity
 * and the body state it already had. */
typedef struct openusd_physx_vehicle_desc
{
    uint64_t id;
    uint32_t scene_index;
    uint32_t actor_index;  /* chassis actor in the actor section */
    uint32_t wheel_offset; /* first wheel in the vehicle wheel section */
    uint32_t wheel_count;  /* wheels owned by this vehicle, at least one */
    uint32_t flags;        /* openusd_physx_vehicle_flags */
    uint32_t drive;        /* openusd_physx_vehicle_drive */
    uint32_t query;        /* openusd_physx_vehicle_query */
    uint32_t longitudinal_axis; /* openusd_physx_axis, the forward axis */
    uint32_t lateral_axis;      /* openusd_physx_axis, the right axis */
    uint32_t vertical_axis;     /* openusd_physx_axis, the up axis */
    float chassis_mass;         /* 0 keeps the actor mass */
    openusd_physx_vec3f chassis_moi; /* 0 keeps the actor inertia */
    /* Engine. */
    float engine_peak_torque;
    float engine_moi;
    float engine_idle_omega;
    float engine_max_omega;
    float engine_damping_full_throttle;
    float engine_damping_zero_throttle_clutch_engaged;
    float engine_damping_zero_throttle_clutch_disengaged;
    /* Clutch, gearbox and autobox. */
    float clutch_strength;
    float gear_switch_time;
    float final_gear_ratio;
    float reverse_gear_ratio;
    float first_gear_ratio;
    float top_gear_ratio;
    float autobox_up_ratio;
    float autobox_down_ratio;
    float autobox_latency;
    uint32_t forward_gear_count; /* forward gears, at least one */
    /* Brakes, steering and road queries. */
    float max_brake_torque;
    float max_hand_brake_torque;
    float max_steer_angle; /* radians at full steer */
    float default_friction;
    float sprung_mass_total; /* 0 resolves the sprung masses from the chassis */
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_vehicle_desc;

/* Modifiers a wheel may declare. */
typedef enum openusd_physx_vehicle_wheel_flags
{
    OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_NONE = 0u,
    /* The wheel answers the steer command. */
    OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_STEERS = 1u << 0,
    /* The wheel answers the brake command. */
    OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_BRAKES = 1u << 1,
    /* The wheel answers the handbrake command. */
    OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_HAND_BRAKES = 1u << 2,
    /* The wheel receives drive torque from the differential. */
    OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_DRIVEN = 1u << 3,
    OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_ALL = 0xFu
} openusd_physx_vehicle_wheel_flags;

/* One wheel of one vehicle. */
typedef struct openusd_physx_vehicle_wheel_desc
{
    uint64_t id;
    /* Where the suspension is anchored on the chassis, in chassis space. */
    openusd_physx_transform suspension_attachment;
    openusd_physx_vec3f suspension_travel_dir;
    float suspension_travel_dist;
    /* Where the wheel sits relative to the suspension frame. */
    openusd_physx_transform wheel_attachment;
    float radius;
    float half_width;
    float mass;
    float moi; /* 0 resolves a solid disc from the mass and radius */
    float damping_rate;
    float suspension_stiffness;
    float suspension_damping;
    float sprung_mass; /* 0 splits the chassis mass evenly over the wheels */
    float tire_lat_stiff_x;
    float tire_lat_stiff_y;
    float tire_long_stiff;
    float tire_camber_stiff;
    float tire_rest_load; /* 0 uses the sprung mass weight */
    float tire_friction;
    float steer_response;      /* fraction of the maximum steer angle */
    float brake_response;      /* fraction of the maximum brake torque */
    float hand_brake_response; /* fraction of the maximum handbrake torque */
    float drive_torque_ratio;  /* differential share, 0 shares evenly */
    uint32_t axle_index;
    uint32_t flags; /* openusd_physx_vehicle_wheel_flags */
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_vehicle_wheel_desc;

/* ---------------------------------------------------------------------------
 * CUDA accelerated domains.
 *
 * Every record below describes an object PhysX can only simulate on a CUDA
 * device. A page may always declare them: a runtime that cannot reach a device
 * skips each of them individually with a diagnostic that names the object and
 * still builds and steps every CPU domain of the same page. Nothing in this
 * section is ever emulated on the CPU.
 *
 * Geometry is carried by the shared mesh point and mesh index sections, which
 * is what keeps the page pointer free and lets a particle body, a surface, and
 * a volume all be validated against the same bounds a collider is.
 * ------------------------------------------------------------------------ */

/* Material response of position based dynamics particles, which covers solid
 * particles, granular material, and fluids. */
typedef struct openusd_physx_particle_material_desc
{
    uint64_t id;
    float friction;
    float damping;
    float adhesion;
    float adhesion_offset_scale;
    float particle_friction_scale;
    float particle_adhesion_scale;
    float viscosity;
    float surface_tension;
    float cohesion;
    float vorticity_confinement;
    float drag;
    float lift;
    /* 0 keeps unscaled scene gravity. */
    float gravity_scale;
    /* 0 requests the density the simulation SDK derives from the rest offset. */
    float density;
    /* Courant-Friedrichs-Lewy coefficient bounding the fluid step. 0 keeps the
     * simulation SDK default. */
    float cfl_coefficient;
    uint32_t reserved0;
} openusd_physx_particle_material_desc;

typedef enum openusd_physx_particle_system_flags
{
    OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_NONE = 0u,
    OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_ENABLE_CCD = 1u << 0,
    /* Particles of two different particle groups collide with each other. */
    OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_GLOBAL_SELF_COLLISION = 1u << 1,
    /* Particles collide with rigid and deformable geometry. */
    OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_NON_PARTICLE_COLLISION = 1u << 2,
    OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_ALL = 0x7u
} openusd_physx_particle_system_flags;

/* One position based dynamics particle system. It owns a contiguous window of
 * the particle body section; a body outside every window is unreachable and is
 * rejected by the page validator rather than being silently dropped. */
typedef struct openusd_physx_particle_system_desc
{
    uint64_t id;
    int32_t scene_index;
    uint32_t flags;
    /* Every offset below is a distance in stage linear units. Zero asks for the
     * value the simulation SDK derives from the particle contact offset. */
    float contact_offset;
    float rest_offset;
    float particle_contact_offset;
    float solid_rest_offset;
    float fluid_rest_offset;
    /* 0 keeps the simulation SDK default. */
    float max_depenetration_velocity;
    float neighborhood_scale;
    uint32_t max_neighborhood;
    /* 0 selects the iteration count the owning scene declares. */
    uint32_t solver_position_iterations;
    openusd_physx_vec3f wind;
    /* Element window into the particle body section. */
    uint32_t body_offset;
    uint32_t body_count;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_particle_system_desc;

typedef enum openusd_physx_particle_body_kind
{
    /* Solid particles or, with OPENUSD_PHYSX_PARTICLE_BODY_FLAG_FLUID, a
     * fluid. Both are one particle buffer of one particle system. */
    OPENUSD_PHYSX_PARTICLE_BODY_SET = 0,
    OPENUSD_PHYSX_PARTICLE_BODY_KIND_COUNT = 1
} openusd_physx_particle_body_kind;

typedef enum openusd_physx_particle_body_flags
{
    OPENUSD_PHYSX_PARTICLE_BODY_FLAG_NONE = 0u,
    /* Simulates the particles with the fluid constraint model. */
    OPENUSD_PHYSX_PARTICLE_BODY_FLAG_FLUID = 1u << 0,
    /* Particles of this body collide with each other. */
    OPENUSD_PHYSX_PARTICLE_BODY_FLAG_SELF_COLLISION = 1u << 1,
    OPENUSD_PHYSX_PARTICLE_BODY_FLAG_ALL = 0x3u
} openusd_physx_particle_body_flags;

/* One particle buffer of one particle system.
 *
 * point_offset and point_count name the rest configuration in the shared mesh
 * point section; world_pose places that configuration in the world. The record
 * carries no velocity window: a particle body starts at rest, which is what an
 * authored point set means, and is driven by the solver afterwards. */
typedef struct openusd_physx_particle_body_desc
{
    uint64_t id;
    uint32_t kind; /* openusd_physx_particle_body_kind */
    uint32_t flags;
    /* Particles of two bodies that share a group never collide with each
     * other, which is how a page filters one emitter against itself. */
    uint32_t particle_group;
    int32_t material_index; /* -1 selects the world default particle material */
    /* Total mass of the body in stage mass units. 0 requests a mass derived
     * from the material density and the particle rest volume. */
    float mass;
    uint32_t point_offset;
    uint32_t point_count;
    openusd_physx_transform world_pose;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_particle_body_desc;

typedef enum openusd_physx_deformable_kind
{
    /* A triangulated surface solved on its own vertices. */
    OPENUSD_PHYSX_DEFORMABLE_SURFACE = 0,
    /* A finite element volume driven by a tetrahedral simulation mesh. */
    OPENUSD_PHYSX_DEFORMABLE_VOLUME = 1,
    OPENUSD_PHYSX_DEFORMABLE_KIND_COUNT = 2
} openusd_physx_deformable_kind;

typedef enum openusd_physx_deformable_flags
{
    OPENUSD_PHYSX_DEFORMABLE_FLAG_NONE = 0u,
    OPENUSD_PHYSX_DEFORMABLE_FLAG_ENABLE_CCD = 1u << 0,
    OPENUSD_PHYSX_DEFORMABLE_FLAG_SELF_COLLISION = 1u << 1,
    /* The simulation mesh is driven by the authored animation rather than by
     * the solver. Volume deformables only. */
    OPENUSD_PHYSX_DEFORMABLE_FLAG_KINEMATIC = 1u << 2,
    OPENUSD_PHYSX_DEFORMABLE_FLAG_DISABLE_GRAVITY = 1u << 3,
    OPENUSD_PHYSX_DEFORMABLE_FLAG_ALL = 0xFu
} openusd_physx_deformable_flags;

/* Material response of a surface or a volume deformable. `kind` selects which
 * fields are read; the surface only fields must be zero on a volume material so
 * a page can never claim a bending response for a body that has no shell.
 *
 * There is one damping value because the simulation SDK now models damping as a
 * single elasticity damping term. A stage that authors a separate damping and
 * damping scale has them folded into this one value by the composer, which is
 * where the authored vocabulary is known, rather than by a runtime that would
 * have to guess. */
typedef struct openusd_physx_deformable_material_desc
{
    uint64_t id;
    uint32_t kind; /* openusd_physx_deformable_kind */
    uint32_t reserved0;
    float youngs_modulus;
    float poissons_ratio;
    float dynamic_friction;
    float density;
    float elasticity_damping;
    /* Surface only. */
    float bending_stiffness;
    float bending_damping;
    float thickness;
    uint32_t reserved1;
    uint32_t reserved2;
} openusd_physx_deformable_material_desc;

/* One surface or volume deformable.
 *
 * The simulation mesh is the point and index window every solver vertex comes
 * from: three indices per triangle for a surface, four per tetrahedron for a
 * volume. A volume may additionally name a collision mesh; a zero collision
 * window reuses the simulation mesh, which is what an authored volume without a
 * separate collision tetrahedralization means. */
typedef struct openusd_physx_deformable_desc
{
    uint64_t id;
    int32_t scene_index;
    uint32_t kind; /* openusd_physx_deformable_kind */
    uint32_t flags;
    int32_t material_index; /* -1 selects the world default deformable material */
    /* 0 selects the iteration count the owning scene declares. */
    uint32_t solver_position_iterations;
    /* Surface only; 0 keeps the simulation SDK default for both. */
    uint32_t collision_iteration_multiplier;
    uint32_t collision_pair_update_frequency;
    float vertex_velocity_damping;
    /* Surface only. 0 disables the per step displacement clamp. The runtime
     * turns it into the maximum vertex velocity the simulation SDK models by
     * multiplying it with the fixed simulation rate the page declares. */
    float max_displacement;
    float self_collision_filter_distance;
    /* Volume only; 0 keeps the simulation SDK default for each. */
    float max_depenetration_velocity;
    float settling_threshold;
    float sleep_threshold;
    /* Simulation mesh window into the shared mesh point and index sections. */
    uint32_t point_offset;
    uint32_t point_count;
    uint32_t index_offset;
    uint32_t index_count;
    /* Collision mesh window, volume only. A zero count reuses the simulation
     * mesh for collision. */
    uint32_t collision_point_offset;
    uint32_t collision_point_count;
    uint32_t collision_index_offset;
    uint32_t collision_index_count;
    openusd_physx_transform world_pose;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_deformable_desc;


/* ---------------------------------------------------------------------------
 * Batched command input. Records are pointer free; the arrays holding them are
 * owned by the caller for the duration of the call only.
 * ------------------------------------------------------------------------ */

/* One batched runtime command.
 *
 * Commands are validated as a whole batch before any of them is applied: a
 * single malformed command rejects the entire step and mutates nothing. A well
 * formed command whose target is missing, or whose target cannot accept it, is
 * reported as a diagnostic and skipped while the rest of the batch still runs.
 *
 * Commands are applied strictly in submission order, so a clear command
 * replaces every accumulation submitted before it in the same batch and leaves
 * every accumulation submitted after it intact. */
typedef struct openusd_physx_command
{
    uint64_t target_id;
    uint32_t type;
    uint32_t flags; /* openusd_physx_command_flags */
    openusd_physx_transform pose;
    openusd_physx_vec3f vector;
    openusd_physx_vec3f point;
    float scalar;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_command;

typedef struct openusd_physx_body_state
{
    uint64_t id;
    openusd_physx_transform pose;
    openusd_physx_vec3f linear_velocity;
    openusd_physx_vec3f angular_velocity;
    uint32_t flags;
    uint32_t reserved0;
    uint32_t reserved1;
} openusd_physx_body_state;

/* One immutable simulation event.
 *
 * Identity contract per event type. Every identity is the stable identity of a
 * prim declared by the build page; no field ever carries a pointer, an index,
 * or a stage handle.
 *
 *   SLEEP, WAKE          id0 = actor, id1 = 0,        detail0 = 0,      detail1 = 0
 *   JOINT_BREAK          id0 = joint, id1 = actor0,   detail0 = actor1, detail1 = 0
 *   CONTACT_FOUND, LOST  id0 = actor0, id1 = actor1,  detail0 = shape0, detail1 = shape1
 *   TRIGGER_ENTER, LEAVE id0 = trigger actor,
 *                        id1 = other actor,           detail0 = trigger shape,
 *                                                     detail1 = other shape
 *   CONTROLLER_HIT       id0 = controller, id1 = hit actor,
 *                                                     detail0 = hit shape, detail1 = 0
 *
 * For contact, trigger, and controller hit events the detail pair names shapes
 * and OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE is set. An identity that PhysX
 * cannot attribute is OPENUSD_PHYSX_INVALID_ID rather than a guess.
 *
 * Events of one step are reported in one deterministic total order:
 * step_index, then type, then id0, id1, detail0, detail1. The order does not
 * depend on the worker thread count, on PhysX internal iteration order, or on
 * how many events were dropped. */
typedef struct openusd_physx_event
{
    uint64_t id0;
    uint64_t id1;
    uint64_t step_index;
    uint32_t type;
    uint32_t flags;
    openusd_physx_vec3f position;
    openusd_physx_vec3f normal;
    float impulse;
    uint32_t reserved;
    uint64_t detail0;
    uint64_t detail1;
} openusd_physx_event;

typedef struct openusd_physx_diagnostic
{
    uint64_t id;
    uint32_t severity;
    uint32_t code;
    char message[OPENUSD_PHYSX_DIAGNOSTIC_MESSAGE_BYTES];
} openusd_physx_diagnostic;

typedef struct openusd_physx_debug_line
{
    openusd_physx_vec3f start;
    openusd_physx_vec3f end;
    uint32_t color;
    uint32_t category;
} openusd_physx_debug_line;

typedef enum openusd_physx_deformation_kind
{
    /* Solid or granular particles of one particle body. */
    OPENUSD_PHYSX_DEFORMATION_PARTICLES = 0,
    /* Fluid particles of one particle body. */
    OPENUSD_PHYSX_DEFORMATION_FLUID = 1,
    /* Simulated vertices of one surface deformable. */
    OPENUSD_PHYSX_DEFORMATION_SURFACE = 2,
    /* Simulated vertices of one volume deformable's simulation mesh. */
    OPENUSD_PHYSX_DEFORMATION_VOLUME = 3,
    OPENUSD_PHYSX_DEFORMATION_KIND_COUNT = 4
} openusd_physx_deformation_kind;

typedef enum openusd_physx_deformation_flags
{
    OPENUSD_PHYSX_DEFORMATION_FLAG_NONE = 0u,
    /* The body did not move since the previous published step, so a consumer
     * may keep the vertices it already uploaded. The points are still written. */
    OPENUSD_PHYSX_DEFORMATION_FLAG_SLEEPING = 1u << 0,
    OPENUSD_PHYSX_DEFORMATION_FLAG_ALL = 0x1u
} openusd_physx_deformation_flags;

/* One deformable body's published vertex window.
 *
 * A rigid body state cannot express a per vertex domain, so a particle body, a
 * surface, and a volume each publish one of these plus a contiguous window of
 * the deformation point buffer. Windows never overlap, are written in the order
 * the build page declared the objects, and are complete: a body whose vertices
 * do not fit the declared capacity is dropped whole and counted, so a consumer
 * never reads half a body. */
typedef struct openusd_physx_deformation_state
{
    uint64_t id;
    uint32_t kind;  /* openusd_physx_deformation_kind */
    uint32_t flags; /* openusd_physx_deformation_flags */
    /* Element window into the deformation point buffer of the same result. */
    uint32_t point_offset;
    uint32_t point_count;
    uint64_t reserved0;
} openusd_physx_deformation_state;

typedef struct openusd_physx_result_header
{
    uint64_t revision;
    uint64_t step_index;
    double simulation_time;
    double last_step_seconds;
    double total_step_seconds;
    uint32_t body_state_count;
    uint32_t event_count;
    uint32_t diagnostic_count;
    uint32_t debug_line_count;
    uint32_t dropped_event_count;
    uint32_t dropped_diagnostic_count;
    uint32_t dropped_debug_line_count;
    uint32_t overflow_flags;
    uint32_t state;
    uint32_t deformation_body_count;
    uint32_t deformation_point_count;
    /* Deformation bodies that did not fit the declared capacity. Each of them
     * was dropped whole, never truncated. */
    uint32_t dropped_deformation_body_count;
} openusd_physx_result_header;

/* Caller owned, fixed capacity result page. The library never allocates,
 * retains, or frees any of these buffers. */
typedef struct openusd_physx_result_page
{
    uint32_t struct_size;
    uint32_t abi_version;
    openusd_physx_result_header header;
    openusd_physx_body_state* body_states;
    size_t body_state_capacity;
    openusd_physx_event* events;
    size_t event_capacity;
    openusd_physx_diagnostic* diagnostics;
    size_t diagnostic_capacity;
    openusd_physx_debug_line* debug_lines;
    size_t debug_line_capacity;
    /* Both deformation buffers are optional and are only written when both are
     * present. A caller that allocates one and not the other is rejected
     * rather than being served a half filled result. */
    openusd_physx_deformation_state* deformations;
    size_t deformation_capacity;
    openusd_physx_vec3f* deformation_points;
    size_t deformation_point_capacity;
} openusd_physx_result_page;

typedef struct openusd_physx_world_desc
{
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t worker_thread_count;
    uint32_t flags;
    uint64_t reserved0;
    uint64_t reserved1;
} openusd_physx_world_desc;

typedef struct openusd_physx_step_desc
{
    uint32_t struct_size;
    uint32_t flags;
    double fixed_time_step;
    uint32_t substep_count;
    uint32_t reserved;
    const openusd_physx_command* commands;
    size_t command_count;
} openusd_physx_step_desc;

typedef struct openusd_physx_reset_desc
{
    uint32_t struct_size;
    uint32_t flags;
    double simulation_time;
    const openusd_physx_body_state* body_states;
    size_t body_state_count;
} openusd_physx_reset_desc;

/* One batched scene query. The request is pointer free and never names a stage
 * object: it addresses a scene by index and filters by collision group mask
 * only. max_hits bounds the hits this one request may retain and must be at
 * least one; the nearest max_hits hits are kept and every further touch this
 * library observes is counted as dropped. A request whose touch count also
 * exhausts the simulation SDK's own buffer additionally raises
 * OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED, because that many touches can no
 * longer be counted exactly. */
typedef struct openusd_physx_query_request
{
    uint64_t user_id;
    uint32_t type;
    uint32_t flags;
    openusd_physx_vec3f origin;
    openusd_physx_vec3f direction;
    float max_distance;
    uint32_t shape_type;
    openusd_physx_vec3f half_extents;
    openusd_physx_quatf rotation;
    float radius;
    float half_height;
    uint32_t filter_mask; /* 0 accepts every collision group */
    uint32_t max_hits;
    uint32_t scene_index;
} openusd_physx_query_request;

/* One immutable query hit. Hits of one request are written in one deterministic
 * total order: distance, then actor identity, then shape identity, then face
 * index. Requests are written in submission order, so the hit array of a batch
 * is fully determined by the batch itself. flags carries
 * openusd_physx_query_hit_flags and states which of position, normal, distance,
 * and face_index actually carry data. */
typedef struct openusd_physx_query_hit
{
    uint64_t user_id;
    uint64_t actor_id;
    uint64_t shape_id;
    openusd_physx_vec3f position;
    openusd_physx_vec3f normal;
    float distance;
    uint32_t face_index;
    uint32_t flags;
    uint32_t reserved;
} openusd_physx_query_hit;

typedef struct openusd_physx_query_desc
{
    uint32_t struct_size;
    uint32_t abi_version;
    const openusd_physx_query_request* requests;
    size_t request_count;
    openusd_physx_query_hit* hits;
    size_t hit_capacity;
} openusd_physx_query_desc;

typedef struct openusd_physx_query_result
{
    uint32_t struct_size;
    uint32_t overflow_flags;
    size_t hit_count;
    /* Exact when OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED is clear, a lower bound
     * when it is set. */
    size_t dropped_hit_count;
    size_t rejected_request_count;
} openusd_physx_query_result;

typedef struct openusd_physx_abi_info
{
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t page_magic;
    uint32_t build_page_header_size;
    uint32_t page_span_size;
    uint32_t capacities_size;
    uint32_t identity_size;
    uint32_t scene_desc_size;
    uint32_t material_desc_size;
    uint32_t shape_desc_size;
    uint32_t actor_desc_size;
    uint32_t actor_shape_ref_size;
    uint32_t joint_desc_size;
    uint32_t filter_pair_size;
    uint32_t command_size;
    uint32_t body_state_size;
    uint32_t event_size;
    uint32_t diagnostic_size;
    uint32_t debug_line_size;
    uint32_t result_header_size;
    uint32_t query_request_size;
    uint32_t query_hit_size;
    uint32_t heightfield_sample_size;
    uint32_t articulation_desc_size;
    uint32_t articulation_link_desc_size;
    uint32_t controller_desc_size;
    uint32_t tendon_desc_size;
    uint32_t tendon_node_desc_size;
    uint32_t mimic_joint_desc_size;
    uint32_t vehicle_desc_size;
    uint32_t vehicle_wheel_desc_size;
    uint32_t particle_material_desc_size;
    uint32_t particle_system_desc_size;
    uint32_t particle_body_desc_size;
    uint32_t deformable_material_desc_size;
    uint32_t deformable_desc_size;
    uint32_t deformation_state_size;
    uint32_t page_alignment;
    uint32_t reserved;
} openusd_physx_abi_info;

typedef struct openusd_physx_capabilities
{
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t flags;
    uint32_t physx_version_major;
    uint32_t physx_version_minor;
    uint32_t physx_version_bugfix;
    uint32_t max_scenes;
    uint32_t max_collision_groups;
    uint32_t min_simulation_rate_hz;
    uint32_t max_simulation_rate_hz;
    uint32_t max_substeps;
    uint32_t max_result_capacity;
} openusd_physx_capabilities;

typedef struct openusd_physx_page_validation
{
    uint32_t struct_size;
    uint32_t error_code;
    uint32_t section;
    uint32_t element_index;
    uint64_t byte_offset;
    uint64_t revision;
    uint64_t source_hash;
    uint32_t identity_count;
    uint32_t scene_count;
    uint32_t material_count;
    uint32_t shape_count;
    uint32_t actor_count;
    uint32_t dynamic_actor_count;
    uint32_t joint_count;
    uint32_t filter_pair_count;
    openusd_physx_result_capacities capacities;
} openusd_physx_page_validation;

typedef struct openusd_physx_world_status_info
{
    uint32_t struct_size;
    uint32_t state;
    uint64_t revision;
    uint64_t step_index;
    double simulation_time;
    uint32_t actor_count;
    /* Number of body states the world publishes each step. It counts every
     * movable actor plus every articulation link plus every character
     * controller, so it is exactly the body state capacity a result page
     * needs. */
    uint32_t dynamic_actor_count;
    uint32_t joint_count;
    uint32_t scene_count;
    uint32_t articulation_count;
    uint32_t articulation_link_count;
    uint32_t controller_count;
    uint32_t tendon_count;
    uint32_t mimic_joint_count;
    uint32_t vehicle_count;
    uint32_t vehicle_wheel_count;
    /* GPU domains that were actually created. A page that declares them on a
     * runtime without a device reports zero for each and reports one skip
     * diagnostic per object. */
    uint32_t particle_system_count;
    uint32_t particle_body_count;
    uint32_t deformable_surface_count;
    uint32_t deformable_volume_count;
    /* Number of deformation bodies and vertices the world publishes each step.
     * They are exactly the deformation capacities a result page needs. */
    uint32_t deformation_body_count;
    uint32_t deformation_point_count;
    uint32_t reserved0;
    openusd_physx_result_capacities capacities;
} openusd_physx_world_status_info;

#if defined(__cplusplus)
static_assert(sizeof(openusd_physx_page_span) == 8, "openusd_physx_page_span layout changed");
static_assert(sizeof(openusd_physx_result_capacities) == 32, "openusd_physx_result_capacities layout changed");
static_assert(sizeof(openusd_physx_build_page_header) == 352, "openusd_physx_build_page_header layout changed");
static_assert(sizeof(openusd_physx_identity) == 24, "openusd_physx_identity layout changed");
static_assert(sizeof(openusd_physx_scene_desc) == 48, "openusd_physx_scene_desc layout changed");
static_assert(sizeof(openusd_physx_material_desc) == 40, "openusd_physx_material_desc layout changed");
static_assert(sizeof(openusd_physx_shape_desc) == 144, "openusd_physx_shape_desc layout changed");
static_assert(sizeof(openusd_physx_actor_desc) == 184, "openusd_physx_actor_desc layout changed");
static_assert(sizeof(openusd_physx_actor_shape_ref) == 8, "openusd_physx_actor_shape_ref layout changed");
static_assert(sizeof(openusd_physx_joint_desc) == 408, "openusd_physx_joint_desc layout changed");
static_assert(sizeof(openusd_physx_heightfield_sample) == 4, "openusd_physx_heightfield_sample layout changed");
static_assert(sizeof(openusd_physx_filter_pair) == 8, "openusd_physx_filter_pair layout changed");
static_assert(sizeof(openusd_physx_articulation_desc) == 64, "openusd_physx_articulation_desc layout changed");
static_assert(sizeof(openusd_physx_articulation_link_desc) == 432, "openusd_physx_articulation_link_desc layout changed");
static_assert(
    sizeof(openusd_physx_articulation_link_desc) == 432,
    "openusd_physx_articulation_link_desc layout changed");
static_assert(sizeof(openusd_physx_controller_desc) == 112, "openusd_physx_controller_desc layout changed");
static_assert(sizeof(openusd_physx_tendon_desc) == 64, "openusd_physx_tendon_desc layout changed");
static_assert(sizeof(openusd_physx_tendon_node_desc) == 64, "openusd_physx_tendon_node_desc layout changed");
static_assert(sizeof(openusd_physx_mimic_joint_desc) == 64, "openusd_physx_mimic_joint_desc layout changed");
static_assert(sizeof(openusd_physx_vehicle_desc) == 160, "openusd_physx_vehicle_desc layout changed");
static_assert(sizeof(openusd_physx_vehicle_wheel_desc) == 168, "openusd_physx_vehicle_wheel_desc layout changed");
static_assert(
    sizeof(openusd_physx_particle_material_desc) == 72,
    "openusd_physx_particle_material_desc layout changed");
static_assert(
    sizeof(openusd_physx_particle_system_desc) == 80,
    "openusd_physx_particle_system_desc layout changed");
static_assert(sizeof(openusd_physx_particle_body_desc) == 72, "openusd_physx_particle_body_desc layout changed");
static_assert(
    sizeof(openusd_physx_deformable_material_desc) == 56,
    "openusd_physx_deformable_material_desc layout changed");
static_assert(sizeof(openusd_physx_deformable_desc) == 128, "openusd_physx_deformable_desc layout changed");
static_assert(sizeof(openusd_physx_deformation_state) == 32, "openusd_physx_deformation_state layout changed");
static_assert(sizeof(openusd_physx_command) == 80, "openusd_physx_command layout changed");
static_assert(sizeof(openusd_physx_body_state) == 72, "openusd_physx_body_state layout changed");
static_assert(sizeof(openusd_physx_event) == 80, "openusd_physx_event layout changed");
static_assert(sizeof(openusd_physx_diagnostic) == 208, "openusd_physx_diagnostic layout changed");
static_assert(sizeof(openusd_physx_debug_line) == 32, "openusd_physx_debug_line layout changed");
static_assert(sizeof(openusd_physx_result_header) == 88, "openusd_physx_result_header layout changed");
static_assert(sizeof(openusd_physx_query_request) == 96, "openusd_physx_query_request layout changed");
static_assert(sizeof(openusd_physx_query_hit) == 64, "openusd_physx_query_hit layout changed");
static_assert(sizeof(openusd_physx_transform) == 28, "openusd_physx_transform layout changed");
#endif

/* ---------------------------------------------------------------------------
 * Entry points. Every function is exception safe and returns a status; no
 * function throws, and none of them accepts or invokes a callback.
 * ------------------------------------------------------------------------ */

/* Reports the exact ABI version, page magic, and the size of every record so a
 * caller can assert its own layout before it builds a page. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_get_abi(
    openusd_physx_abi_info* info,
    openusd_physx_error_buffer* error);

/* Reports runtime capabilities. Requires an exact ABI match. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_get_capabilities(
    uint32_t abi_version,
    openusd_physx_capabilities* capabilities,
    openusd_physx_error_buffer* error);

/* Computes the stable identity of a prim path plus instance domain and index.
 * The result is never OPENUSD_PHYSX_INVALID_ID. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_identity_compute(
    const char* path,
    size_t path_length,
    uint32_t instance_domain,
    uint32_t instance_index,
    uint64_t* id,
    openusd_physx_error_buffer* error);

/* Validates a pointer free build page without creating any simulation object.
 * The page memory stays owned by the caller and is only read. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_page_validate(
    const void* page,
    size_t page_size,
    openusd_physx_page_validation* validation,
    openusd_physx_error_buffer* error);

/* Creates an empty retained world. The world owns its scenes, dispatcher, and
 * cooked resources, and holds one reference on the process runtime that owns
 * the single PxFoundation and PxPhysics instance. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_create(
    const openusd_physx_world_desc* desc,
    openusd_physx_world** world,
    openusd_physx_error_buffer* error);

/* Releases a world. Passing NULL is a no-op. Releasing the last world releases
 * the process runtime. */
OPENUSD_PHYSX_API void openusd_physx_world_release(openusd_physx_world* world);

/* Validates and applies a build page. The page is copied where needed and is
 * not retained after the call returns. A failed build leaves an empty world. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_build(
    openusd_physx_world* world,
    const void* page,
    size_t page_size,
    openusd_physx_page_validation* validation,
    openusd_physx_error_buffer* error);

/* Restores the built state, optionally overridden by a batch of body states. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_reset(
    openusd_physx_world* world,
    const openusd_physx_reset_desc* desc,
    openusd_physx_error_buffer* error);

/* Applies one command batch, advances the simulation, and fills one caller
 * owned result page. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_step(
    openusd_physx_world* world,
    const openusd_physx_step_desc* desc,
    openusd_physx_result_page* results,
    openusd_physx_error_buffer* error);

/* Fills a caller owned result page from the current state without stepping. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_fetch_results(
    openusd_physx_world* world,
    openusd_physx_result_page* results,
    openusd_physx_error_buffer* error);

/* Runs one batch of raycast, sweep, and overlap requests. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_query(
    openusd_physx_world* world,
    const openusd_physx_query_desc* desc,
    openusd_physx_query_result* result,
    openusd_physx_error_buffer* error);

/* Reports world state, revision, counts, and the declared result capacities. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_world_get_status(
    const openusd_physx_world* world,
    openusd_physx_world_status_info* info,
    openusd_physx_error_buffer* error);

#ifdef __cplusplus
}
#endif

#endif
