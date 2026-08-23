# openusd_physx

`openusd_physx` is the native physics shim. It exposes two C ABIs:

- **`include/openusd_physx.h`** - the original stage simulation ABI. A caller
  opens a USD stage, the shim translates `UsdPhysics` prims into a PhysX scene,
  steps it, and hands out a page of body transforms. It is unchanged and still
  exported.
- **`include/openusd_physx_world.h`** - the retained world ABI described below.
  It owns a long lived world that is built once from a pointer free page and
  then driven with batched commands, batched results, and batched queries.

Both ABIs share one process wide PhysX runtime, one set of translation helpers,
and one set of exception guards, so a process may use either or both.

## Layout

- `src/openusd_physx_support.{h,cpp}` - PhysX free helpers: error buffer
  writing, string copying, finite and rotation checks, UTF-8 validation, the
  identity hash, and the exception `Guard` that wraps every entry point.
- `src/openusd_physx_page.{h,cpp}` - PhysX free build page reader and strict
  validator plus the three entry points that need no simulation SDK
  (`openusd_physx_world_get_abi`, `openusd_physx_identity_compute`,
  `openusd_physx_page_validate`).
- `src/openusd_physx_runtime.{h,cpp}` - reference counted, mutex protected
  owner of the single `PxFoundation`, `PxPhysics`, and cooking parameters. It
  also owns the recursive factory lock that serializes shared object creation
  and release, and the thread local storage for PhysX error messages.
- `src/openusd_physx_translate.{h,cpp}` - PhysX only helpers shared by both
  ABIs: the filter shader, mesh cooking, axis frames, and vector conversions.
- `src/openusd_physx_events.{h,cpp}` - the event, command, and query policy. It
  owns the deterministic event and hit orders, the bounded sinks that keep a
  deterministic prefix on overflow, the allowed command flag matrix, and the
  command and query validators. It needs neither PhysX nor OpenUSD, so it is
  compiled into the contract probes and exercised on its own.
- `src/openusd_physx_world.cpp` - the retained world implementation.
- `src/openusd_physx_cuda.{h,cpp}` - the optional, once per process CUDA context
  probe. It creates and validates the context manager and caches the answer, so
  a machine without a device pays for one failed device enumeration and reports
  one stable reason. It is the only place that decides whether the GPU domains
  can run at all.
- `src/openusd_physx_gpu.{h,cpp}` - the CUDA accelerated domains: position based
  dynamics particle systems and particle bodies, surface deformables, and finite
  element volume deformables, plus the per vertex deformation readback. The
  whole implementation sits behind `PX_SUPPORT_GPU_PHYSX`; on a platform without
  GPU support the entry points still exist and report every declared object as
  skipped.
- `src/openusd_physx_scene.cpp` - the primitive scene half of the stage
  simulation ABI (`openusd_physx_get_version` and the `openusd_physx_scene_*`
  entry points). It needs PhysX but no OpenUSD, so it can be compiled and
  exercised on its own.
- `src/openusd_physx.cpp` - the OpenUSD dependent half of the stage simulation
  ABI (`openusd_physx_stage_simulate_file`), layered on the same runtime and
  translation helpers.
- `../openusd_physx_probe/main.cpp` - legacy probe. Requires PhysX only and
  checks stack settling, friction, and angular behaviour of the primitive
  scene ABI.
- `../openusd_physx_probe/contract_main.cpp` - contract probe. Builds without
  PhysX and asserts the ABI record sizes, the identity rules, and roughly
  thirty page rejection cases.
- `../openusd_physx_probe/world_main.cpp` - world probe. Requires PhysX and
  drives create, build, step, fetch, query, reset, and release plus the
  rejection paths.
- `../openusd_physx_probe/concurrency_main.cpp` - concurrency probe. Requires
  PhysX and drives two retained worlds, two legacy scenes, two runtime lifetime
  churn loops, and two error isolation loops from eight threads at once.
- `../openusd_physx_probe/events_main.cpp` - event contract probe. Builds
  without PhysX and asserts the event order is a total order, that overflow
  keeps a deterministic prefix, that the hit sink keeps the nearest hits, and
  the command and query rejection matrices.
- `../openusd_physx_probe/events_world_main.cpp` - event world probe. Requires
  PhysX and drives triggers, contacts, sleep, force and impulse commands,
  clear ordering, multi substep step index stamping, a mixed query batch, a
  narrow hit budget that must still keep the nearest hit, initial overlap sweep
  semantics, hit overflow, continuous collision detection against a thin plate,
  a page whose actor count exceeds the suppressed pair bound, and two concurrent
  worlds that must produce identical event sequences.
- `../openusd_physx_probe/cpu_domains_main.cpp` - CPU domain probe. Requires
  PhysX and drives the version `4` and version `5` domains: every representable
  shape, the per body tuning fields, per axis D6 joints, reduced coordinate
  articulations, and character controllers.
- `../openusd_physx_probe/coupling_main.cpp` - articulation coupling probe.
  Requires PhysX and proves that a fixed tendon, a spatial tendon and a mimic
  joint each change the motion of an otherwise identical articulation chain,
  by comparing each coupled chain against an uncoupled control chain.
- `../openusd_physx_probe/vehicle_main.cpp` - vehicle probe. Requires PhysX and
  drives a real vehicle: it settles on its suspension, accelerates under
  throttle, changes gear through the autobox, steers, and stops under braking,
  with the wheel poses read out of the published body states.
- `../openusd_physx_probe/page_builder.h` - the test only build page builder
  shared by the probes. Its reference scene orders the actors
  `0 = GroundBody` (static), `1 = BoxBody` (dynamic, pinned by a revolute
  joint), `2 = SphereBody` (free dynamic), `3 = RampBody` (static), so a check
  that needs an unconstrained free body must use actor `2`.

## ABI contract

| Item | Value |
| --- | --- |
| `OPENUSD_PHYSX_WORLD_ABI_VERSION` | `7` (exact match, no compatibility window) |
| `OPENUSD_PHYSX_PAGE_MAGIC` | `0x5853594850445355` |
| Page alignment | 8 bytes |
| Maximum page size | 1 GiB |
| Maximum scenes | 64 |
| Maximum wheels per vehicle | 20 (`PxVehicleLimits::eMAX_NB_WHEELS`, asserted natively) |
| Maximum gears per vehicle | 32 |
| Collision groups | 32 |
| Simulation rate | 24 Hz to 240 Hz |
| Maximum substeps per step | 64 |
| Maximum result capacity | 1048576 records per section |
| Diagnostic message | 192 bytes, always NUL terminated |
| `OPENUSD_PHYSX_INVALID_ID` | `0`, never a valid identity |
| Maximum particles per particle body | 4194304 |
| Maximum simulated vertices per deformable | 1048576 |
| Particle neighbourhood budget | 8 to 1024 |
| Maximum particle collision group | 1048575 |

Every caller must ask for version `7` exactly. A different version is rejected
with `OPENUSD_PHYSX_STATUS_VERSION_MISMATCH`; there is no downgrade path and no
silent fallback.

Version `2` widened `openusd_physx_event` from 64 to 80 bytes so that contact,
trigger, and controller hit events can name the collider as well as the body
through `detail0` and `detail1`. That is what makes the event order collision
free, so it could not be expressed in the version `1` record.

Version `3` widened `openusd_physx_actor_desc` from 128 to 144 bytes by adding
`principal_axes`, the rotation from the actor frame into the frame the diagonal
inertia is stated in. A body that authors a rotated principal axis frame is
otherwise given its inertia about the wrong axes, which no other field can
express. The quaternion is optional: an all zero value, which is what a default
initialized description carries, is read as the identity rotation.

Version `4` completed the rigid body, collision and joint domains:

- `openusd_physx_material_desc` grew to 40 bytes and gained the friction and
  restitution combine modes plus `COMPLIANT_CONTACT`, which reads a negative
  restitution as a contact spring stiffness.
- `openusd_physx_shape_desc` grew to 144 bytes and gained per shape contact,
  rest and torsional patch offsets, a local scale, and the `CYLINDER`, `CONE`
  and `HEIGHTFIELD` shape types. Cylinders and cones are `PxConvexCoreGeometry`
  primitives about their local X axis and remain valid for dynamic bodies;
  triangle meshes and heightfields stay static or kinematic only.
- `openusd_physx_actor_desc` grew to 184 bytes and gained speculative CCD,
  retained accelerations, the six per axis linear and angular locks, gyroscopic
  forces, sleep suppression, per body solver iteration counts, maximum linear
  and angular velocity, maximum depenetration velocity and maximum contact
  impulse.
- `openusd_physx_joint_desc` grew to 408 bytes so that a D6 joint can declare a
  motion, a limit pair, a limit spring and a drive independently on each of the
  six axes, along with `EXCLUDE_FROM_ARTICULATION`, acceleration drives, force
  drive limits and soft limits.

Version `5` added the reduced coordinate articulation and character controller
domains. The build page header grew from 248 to 272 bytes because it gained the
`ARTICULATIONS`, `ARTICULATION_LINKS` and `CONTROLLERS` spans, which moved the
`capacities` field to offset 216 and the trailing `reserved` field to offset
248. The version also added `OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER`, the
`ARTICULATION_LINK` and `CONTROLLER` body state flags, the `ARTICULATIONS` and
`CHARACTER_CONTROLLERS` capability flags, and the three new record sizes in
`openusd_physx_abi_info`.

Version `6` completed the articulation coupling and vehicle domains. The header
grew from 272 to 312 bytes because it gained the `ARTICULATION_TENDONS`,
`ARTICULATION_TENDON_NODES`, `ARTICULATION_MIMIC_JOINTS`, `VEHICLES` and
`VEHICLE_WHEELS` spans, which moved `capacities` to offset 256 and the trailing
`reserved` field to offset 288. The section count is 22 and the header carries
20 spans. The version also added:

- `openusd_physx_tendon_desc` (64 bytes) and `openusd_physx_tendon_node_desc`
  (64 bytes), which carry a fixed or spatial tendon and its node tree.
- `openusd_physx_mimic_joint_desc` (64 bytes), which couples two articulation
  axes through a gear ratio and an offset.
- `openusd_physx_vehicle_desc` (160 bytes) and
  `openusd_physx_vehicle_wheel_desc` (168 bytes).
- `OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT`,
  `OPENUSD_PHYSX_EVENT_VEHICLE_GEAR_CHANGE`, the
  `OPENUSD_PHYSX_BODY_STATE_FLAG_VEHICLE_WHEEL` body state flag, and the
  `ARTICULATION_TENDONS`, `ARTICULATION_MIMIC_JOINTS` and `VEHICLES`
  capability flags.

`openusd_physx_world_status_info` reports `articulation_count`,
`articulation_link_count` and `controller_count`. `dynamic_actor_count` is the
published body state count, which is the movable actors plus every articulation
link plus every controller plus every vehicle wheel, so a caller sizes one
result section from one field.

Version `7` added the optional CUDA accelerated domains. The header grew from
312 to 352 bytes because it gained the `PARTICLE_MATERIALS`, `PARTICLE_SYSTEMS`,
`PARTICLE_BODIES`, `DEFORMABLE_MATERIALS` and `DEFORMABLES` spans, which moved
`capacities` to offset 296 and the trailing `reserved` field to offset 328. The
section count is 27 and the header carries 25 spans. The version also added:

- `openusd_physx_particle_material_desc` (72 bytes), the position based dynamics
  material response of solid particles, granular material and fluids.
- `openusd_physx_particle_system_desc` (80 bytes), which owns a contiguous window
  of the particle body section exactly as a vehicle owns a window of the wheel
  section.
- `openusd_physx_particle_body_desc` (72 bytes), one particle buffer whose rest
  configuration is a window of the shared mesh point section.
- `openusd_physx_deformable_material_desc` (56 bytes) and
  `openusd_physx_deformable_desc` (128 bytes), which carry a surface deformable
  and a finite element volume deformable and their simulation and collision
  meshes. `OPENUSD_PHYSX_DEFORMABLE_FLAG_DISABLE_GRAVITY` is applied as
  `PxActorFlag::eDISABLE_GRAVITY`, which is the same actor flag a rigid body and
  an articulation link already use, because a deformable is a `PxActor`.
- `openusd_physx_deformation_state` (32 bytes) plus the deformation point buffer
  of the result page. A rigid body state cannot express a per vertex domain, so
  every particle body, surface and volume publishes one state and one contiguous
  vertex window.
- `max_deformation_bodies` and `max_deformation_points` in
  `openusd_physx_result_capacities`, which replaced two reserved fields and left
  the record at 32 bytes.
- `deformation_body_count`, `deformation_point_count` and
  `dropped_deformation_body_count` in `openusd_physx_result_header`, which
  replaced the three reserved fields and left the record at 88 bytes.
- `OPENUSD_PHYSX_OVERFLOW_DEFORMATION`,
  `OPENUSD_PHYSX_DIAGNOSTIC_GPU_UNAVAILABLE`,
  `OPENUSD_PHYSX_DIAGNOSTIC_GPU_OBJECT_SKIPPED`, and the `CUDA_CONTEXT`,
  `PARTICLE_SYSTEMS`, `SURFACE_DEFORMABLES`, `VOLUME_DEFORMABLES` and
  `DEFORMATION_RESULTS` capability flags.

### CUDA accelerated domains

Everything in this section is optional at every level and is never emulated.

- The simulation SDK only exposes a CUDA context on the platforms it builds GPU
  support for, so the whole implementation sits behind `PX_SUPPORT_GPU_PHYSX`.
  On a platform without it the entry points still exist and report every
  declared object as skipped.
- The PhysXGpu module is loaded by name at runtime from the directory the
  library itself lives in. The build stages it next to `openusd_physx` when the
  simulation SDK ships one, so a machine that has a device can reach it and a
  machine that does not simply fails the late load.
- The context is probed once per process during capability negotiation. The
  `CUDA_CONTEXT` bit, and with it every domain bit, is published only after a
  context manager has been created, has reported a valid context, has reported
  a device of a supported compute architecture, and has reported a non zero
  device memory size. It is never published because the library was compiled
  with GPU support.
- A scene is created on the device only when the page declares a CUDA backed
  object for it. Such a scene runs the temporal Gauss-Seidel solver and the GPU
  broad phase, and drops enhanced determinism, which the GPU pipeline does not
  implement.
- A page may always declare these objects. A runtime that cannot reach a device
  skips each of them individually with a diagnostic naming the object, and every
  CPU domain of the same build still builds and steps.

A particle system owns a contiguous window of the particle body section, and
every particle body must belong to exactly one system. A body carries a point
window into the shared mesh point section plus a world pose; it starts at rest,
which is what an authored point set means. A body that declares the fluid flag
is simulated with the fluid constraint model and publishes the `FLUID`
deformation kind.

A surface deformable is solved on the vertices of its own triangulation. A
volume deformable is solved on an authored tetrahedral simulation mesh and may
name a separate tetrahedral collision mesh; a zero collision window reuses the
simulation mesh. Nothing tetrahedralizes a surface on the CPU, so a volume that
authors no tetrahedra is reported rather than approximated.

Deformation output is bounded exactly like every other result section. Windows
never overlap, are written in the order the build page declared the objects, and
are always complete: a body whose vertices do not fit the declared capacity is
dropped whole, counted in `dropped_deformation_body_count`, and reported through
`OPENUSD_PHYSX_OVERFLOW_DEFORMATION`. The deformation body buffer and the
deformation point buffer must both be present or both be absent; a half declared
window is refused with `OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT`.

`openusd_physx_world_status_info` additionally reports
`particle_system_count`, `particle_body_count`, `deformable_surface_count`,
`deformable_volume_count`, `deformation_body_count` and
`deformation_point_count`. The counts are what was actually created, so a page
that declares GPU objects on a runtime without a device reports zero for each.

### Articulations

An articulation is a window into the articulation link section. The links of one
articulation must tile that window with no gap and no overlap. The link at local
index 0 is the root: it declares `parent_id == 0` and joint type `NONE`. Every
other link names a parent that appears **earlier in the same window**, which is
what makes a cycle unrepresentable rather than merely rejected. Each link
declares its inbound joint type, its parent and child frames, and a per axis
motion, limit, armature and drive over the same six axis order the joint record
uses. A drive on a locked axis is rejected before the world is touched.

Solver iteration counts of zero fall back to the scene values, a sleep threshold
of zero means the articulation never sleeps, and a link mass of zero is resolved
from the attached shapes exactly as a rigid body is.

Every articulation link is movable, so - exactly as for a dynamic actor - a link
may not reference a plane, triangle mesh or height field shape. The validator
rejects the whole page with the offending link named, which is why the managed
composer drops such an articulation as one unit rather than staging part of it.
Shape attachment is checked per shape: a shape PhysX refuses to attach fails the
build with the link and shape index named instead of being silently lost.

### Articulation tendons and mimic joints

A tendon is a window into the tendon node section, exactly as an articulation is
a window into the link section. Node 0 is the tendon root and declares
`parent_index == 0`; every other node names an earlier node in the same window
through a one based `parent_index`, so a cycle is unrepresentable.

A **fixed** tendon couples articulation joint coordinates. Each node names a link
and the axis of that link's inbound joint, and carries a gearing and a force
coefficient. PhysX ignores the axis and the coefficient of the parentless root
node, so the record must place the root node on the **parent** link of the first
coupled joint and carry every coupled joint as a child node; a page that instead
starts at the first coupled joint silently loses that joint's coordinate from the
tendon length. The validator therefore rejects a fixed non root node that names
link 0. A fixed tendon node can only couple a coordinate that exists, so the
build also rejects a non root node whose link has a fixed or absent inbound
joint, or whose named axis that joint locks, rather than handing PhysX a
precondition violation that a release build does not check.

A **spatial** tendon couples world space attachment points. Each node names a
link and carries an attachment offset in that link's frame plus a rest length and
a coefficient, and its `axis` field must be zero because a spatial attachment has
no joint axis. The leaf node carries the rest length the tendon pulls towards.

A tendon declares its stiffness, damping, limit stiffness, low and high limit,
offset and rest length. The limits only apply when the record sets
`OPENUSD_PHYSX_TENDON_FLAG_LIMIT_ENABLED`, so a tendon that leaves them zero is
unlimited rather than pinned at zero.

A **mimic joint** couples two articulation axes with `qA + gearRatio * qB +
offset = 0`. The record names two links inside the same articulation, an axis on
each, a non zero gear ratio and an offset. Naming the same link twice is only
valid when the two axes differ.

Tendons and mimic joints are created before the articulation is added to the
scene, which is the only order PhysX accepts. A tendon or mimic joint that names
a link outside its articulation, an axis that its inbound joint locks, or a zero
gear ratio is rejected with a deterministic diagnostic and the rest of the world
still builds. An articulation that fails at any point after it is created - a
link, a shape, a tendon or a mimic joint - owns itself through a scope guard
until it is handed to the world, so a refused build releases the articulation
and everything attached to it exactly once and repeating the refusal does not
grow the process.
`OPENUSD_PHYSX_CAPABILITY_ARTICULATION_TENDONS` and
`OPENUSD_PHYSX_CAPABILITY_ARTICULATION_MIMIC_JOINTS` are reported only when the
construct was actually built.

### Character controllers

Each scene that declares a controller gets one `PxControllerManager`, created
lazily. A controller is a capsule or a box; it declares its start position, up
direction, slope limit as an **angle** (the library converts it to the cosine
PhysX wants), step offset, contact offset, density, scale coefficient, volume
growth, non walkable mode and climbing mode. Any budget left at zero keeps the
PhysX default rather than clamping to zero.

`OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER` accumulates a displacement for the step;
the accumulated vector is consumed once per substep after `fetchResults`. The
`APPLY_GRAVITY` flag integrates the scene gravity into a per controller fall
velocity between moves, which is cleared on an up or down collision. A command
of any other type aimed at a controller is rejected with a deterministic
diagnostic. Controllers publish a body state carrying `CONTROLLER | KINEMATIC`
whose linear velocity is the achieved displacement over the step, so a caller
never has to infer whether a move was blocked.

Authored controllers reach the world through the extraction page rather than
only through hand written descriptors: `OpenUsdPhysicsCharacterControllerAPI`
publishes the `openUsdPhysics:controller:*` canonical keys, and the composer
turns them into `openusd_physx_controller_desc` records under the identity
`<primPath>.controller`, which is the identity the published body state carries.

### Vehicles

The vehicle domain is the PhysX 5 `vehicle2` CPU path, built on the pinned
`PhysXVehicle2_static_64.lib` and the `include/physx/vehicle2` tree in
`native/install/physx/win-x64`. A vehicle is a window into the vehicle wheel
section: it names an existing dynamic actor as its chassis, declares the
longitudinal, lateral and vertical axes of that chassis, and carries the
drivetrain. `OPENUSD_PHYSX_CAPABILITY_VEHICLES` is reported only when a vehicle
was actually built and stepped.

Each wheel carries its radius, half width, mass, moment of inertia, damping rate,
suspension attachment pose, suspension travel direction and travel distance,
spring strength and damper rate, sprung mass, tire friction, tire longitudinal
and lateral stiffness, its differential share, and the `STEERS`, `BRAKES`,
`HAND_BRAKES` and `DRIVEN` flags. At least one wheel must be `DRIVEN`, which is
what makes an inert vehicle unrepresentable rather than merely useless. A vehicle
may declare at most `OPENUSD_PHYSX_MAX_VEHICLE_WHEELS` wheels, and every wheel
must sit on an axle index below the wheel count. That budget is exactly the
`PxVehicleLimits::eMAX_NB_WHEELS` of the pinned simulation SDK, asserted with a
`static_assert` where the SDK header is visible, because every brake, steer,
differential and axle response table the runtime fills is a fixed array of that
length. The bound is checked in the native validator, in the managed validator,
in the extraction composer, and again in the runtime before a single response is
written.

The drivetrain is `OPENUSD_PHYSX_VEHICLE_DRIVE_ENGINE`: a full engine, clutch,
gearbox, autobox and differential. A direct drive variant is deliberately not
exposed, because every field that would configure it is already a subset of the
engine drivetrain, and offering two shapes that build the same simulation is how
a caller ends up with silently different behaviour for the same authored intent.
The gearbox reserves two ratios beyond the authored forward gears for reverse
and neutral, so `forward_gear_count` of 4 builds 6 ratios with reverse at index 0
and neutral at index 1, and a record may therefore declare at most
`OPENUSD_PHYSX_MAX_VEHICLE_GEARS - 2` forward gears. That bound is checked as a
subtraction in both the native and the managed validator, and again in the
runtime before a single ratio is written, so a count near the top of the
unsigned range cannot wrap past the check and walk off the fixed ratio array.
`OPENUSD_PHYSX_VEHICLE_FLAG_AUTOBOX_ENABLED` builds the
automatic gearbox and raises `OPENUSD_PHYSX_EVENT_VEHICLE_GEAR_CHANGE` whenever
the selected gear changes. The flag is read, not merely recorded: a vehicle that
declares it is handed an autobox and starts in neutral, because the autobox
pulls it out of neutral as soon as the engine turns, and a vehicle that does not
is handed no autobox at all and starts in its first forward gear, because
nothing would ever shift it out of neutral on the driver's behalf.

`OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT` drives one vehicle for the step. `vector`
carries `(throttle, brake, steer)` and `point` carries
`(hand brake, clutch, gear)`. An automatic drivetrain creeps at idle, so a
parked vehicle holds its brake rather than sending a zero command.

Every input is range checked before any command in the batch is applied, so a
malformed command rejects the whole step with
`OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT` and leaves the world and the caller's
result page untouched. Throttle, brake, hand brake and clutch are finite and in
`[0, 1]`, steer is finite and in `[-1, 1]`, and the gear component is a finite
non negative integral value no larger than the maximum gear count. The gear
encoding is one based so that the neutral value can mean "let the drivetrain
decide": `0` selects the automatic gear, and `n > 0` selects PhysX gear index
`n - 1`, which is reverse at `1`, neutral at `2` and the first forward gear at
`3`. An explicit gear is honoured whether or not the vehicle has an autobox; the
automatic gear asks the autobox on a vehicle that has one and holds the gear the
gearbox already targets on a vehicle that does not. A gear above the vehicle's
own top gear is clamped to that top gear when it
is applied, so a caller can never index the fixed gearbox or autobox arrays out
of bounds.

Road geometry is resolved with scene sweeps that reject the vehicle's own
chassis in the pre filter, otherwise the suspension pushes against the chassis it
is mounted on and the vehicle launches. The chassis carries
`PxActorFlag::eDISABLE_GRAVITY` because the vehicle update applies gravity
itself, and the chassis is integrated through
`PxVehiclePhysXActorUpdateMode::eAPPLY_VELOCITY`.

With `OPENUSD_PHYSX_VEHICLE_FLAG_PUBLISH_WHEELS` each wheel publishes a body
state carrying `OPENUSD_PHYSX_BODY_STATE_FLAG_VEHICLE_WHEEL` whose identity is
the wheel record identity, so a caller reads wheel poses out of the same result
section as every other body and never needs a second call per wheel.

### Single axis joint drives

PhysX gives `PxRevoluteJoint` only a velocity motor and gives `PxPrismaticJoint`
and `PxSphericalJoint` no motor at all, so a revolute, prismatic or spherical
record that sets `OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED` is built as a
`PxD6Joint` with exactly that one axis released - twist for revolute, linear X
for prismatic, both swings for spherical - and every other axis locked, except
that a spherical joint keeps its twist axis free because a spherical joint has
three angular degrees of freedom and driving one swing must not silently remove
one of the other two. The
authored `drive_stiffness`, `drive_damping`, `drive_max_force`,
`drive_target_position` and `drive_target_velocity` all reach
`PxD6JointDrive`, and `OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ACCELERATION` selects the
acceleration form of that drive. An acceleration drive is never turned into a
free spinning axis. The authored limit still applies: a driven axis is
`eLIMITED` when the record enables a limit and `eFREE` when it does not, so the
promotion changes nothing a caller can observe except that the drive now works.

The drive target is one scalar on the joint axis: a distance along the
prismatic axis, an angle about the revolute twist axis, or an angle about the
first swing axis of a spherical joint, which is the local Y axis of the joint
frame. A driven spherical joint therefore only holds the axis it was given a
target for; the remaining swing and the twist stay free and any load on them
still moves the body.

### Known limitations

- **Joint projection was removed in PhysX 5.** A page may carry projection
  tolerances for round tripping, but no projection is applied and none can be.
- Cylinders and cones are convex core primitives, so they are exact rather than
  faceted, but they are always about their local X axis.
- A PhysX capsule also lies along its local X axis; an unrotated capsule
  therefore rests at `y = radius`, not at `y = radius + half_height`.

### Record sizes

`openusd_physx_world_get_abi` reports the sizes below, and the header locks
them with `static_assert`, so a caller can verify its own layout before it
builds a page.

| Record | Bytes |
| --- | --- |
| `openusd_physx_build_page_header` | 352 |
| `openusd_physx_page_span` | 8 |
| `openusd_physx_result_capacities` | 32 |
| `openusd_physx_identity` | 24 |
| `openusd_physx_scene_desc` | 48 |
| `openusd_physx_material_desc` | 40 |
| `openusd_physx_shape_desc` | 144 |
| `openusd_physx_actor_desc` | 184 |
| `openusd_physx_actor_shape_ref` | 8 |
| `openusd_physx_joint_desc` | 408 |
| `openusd_physx_heightfield_sample` | 4 |
| `openusd_physx_filter_pair` | 8 |
| `openusd_physx_articulation_desc` | 64 |
| `openusd_physx_articulation_link_desc` | 432 |
| `openusd_physx_controller_desc` | 112 |
| `openusd_physx_tendon_desc` | 64 |
| `openusd_physx_tendon_node_desc` | 64 |
| `openusd_physx_mimic_joint_desc` | 64 |
| `openusd_physx_vehicle_desc` | 160 |
| `openusd_physx_vehicle_wheel_desc` | 168 |
| `openusd_physx_particle_material_desc` | 72 |
| `openusd_physx_particle_system_desc` | 80 |
| `openusd_physx_particle_body_desc` | 72 |
| `openusd_physx_deformable_material_desc` | 56 |
| `openusd_physx_deformable_desc` | 128 |
| `openusd_physx_deformation_state` | 32 |
| `openusd_physx_command` | 80 |
| `openusd_physx_body_state` | 72 |
| `openusd_physx_event` | 80 |
| `openusd_physx_diagnostic` | 208 |
| `openusd_physx_debug_line` | 32 |
| `openusd_physx_result_header` | 88 |
| `openusd_physx_query_request` | 96 |
| `openusd_physx_query_hit` | 64 |
| `openusd_physx_transform` | 28 |

Every optional field follows one rule: **zero means unauthored and keeps the
prior behaviour**. A shape contact offset of zero uses the scene value, an
iteration count of zero uses the scene count, a sleep threshold only overrides
when it is positive, a joint mass or inertia scale of zero is unscaled, an all
zero quaternion is the identity rotation, and every controller budget of zero
keeps the PhysX default.

The remaining structures carry `size_t` members or caller pointers, so their
size depends on the data model. Each one starts with a `struct_size` field that
the library compares against its own `sizeof`, which catches a mismatched
build before anything is read.

## Ownership

- **Build page**: caller owned. The library reads it through a memcpy based
  view, copies whatever it needs, and never retains the memory. It stays valid
  only for the duration of the call.
- **World**: library owned and opaque. `openusd_physx_world_create` returns a
  handle, `openusd_physx_world_release` destroys it, and passing `NULL` to
  release is a no-op. The world owns its scenes, CPU dispatcher, materials,
  cooked meshes, actors, shapes, joints, and query scratch buffers.
- **Process runtime**: one `PxFoundation` and one `PxPhysics` per process,
  reference counted under a mutex. The first world or stage session creates
  them. They are **not** destroyed when the last reference goes away: PhysX
  allows exactly one foundation per process and rejects a second
  `PxCreateFoundation` after the first was released, so tearing the SDK down
  would break every world created later in the same process. The fixed cost is
  held until the process exits, which is what makes repeated open and close
  cycles and concurrent worlds work.
- **Result page**: caller owned, fixed capacity. The library writes into the
  caller's arrays and never allocates, reallocates, retains, or frees them.
- **Query buffers**: caller owned request and hit arrays, read and written only
  during the call.
- **Error buffer**: caller owned. Messages are truncated to the supplied
  capacity and always NUL terminated.

There are no callbacks in either direction and no per element entry points.

## Identity

An identity is `FNV-1a` over the canonical prim path, the instance domain, and
the instance index. `openusd_physx_identity_compute` rejects an empty path, a
path that is not valid UTF-8, and a path that does not start with `/`. Zero is
remapped so that a valid identity is never `OPENUSD_PHYSX_INVALID_ID`. The same
path always produces the same value, so a caller can precompute identities and
match them against body states, events, diagnostics, and query hits.

## Build page validation

The page is a single contiguous blob addressed by byte offsets and element
counts. It contains no pointers. `openusd_physx_page_validate` and
`openusd_physx_world_build` run the same validator, which reports the failing
section, element index, and byte offset through
`openusd_physx_page_validation`. Validation is strict and complete before any
PhysX object is created:

- Header: non-null page, size within `[header, 1 GiB]`, exact magic, exact ABI
  version, exact header size, eight byte alignment, and a byte size that
  matches the buffer.
- Spans: every span lies inside the page, is aligned, has a size that is an
  exact multiple of its record size, and does not overlap another span.
- Counts: section counts stay inside the documented limits, and capacities stay
  at or below `OPENUSD_PHYSX_MAX_RESULT_CAPACITY`.
- Values: `meters_per_unit`, `kilograms_per_unit`, and `time_codes_per_second`
  are positive and finite; the simulation rate is inside the supported range;
  the substep count is at or below 64; the up axis is known.
- Identities: every identity references a string inside the string table, the
  string is valid UTF-8, no identity is zero, and no identity repeats.
- Scenes, materials, shapes, actors, joints: known enum values, known flag
  bits, zero reserved fields, finite and usable transforms, normalized enough
  rotations, positive extents and radii, non-negative masses and densities,
  friction and restitution inside their physical ranges, in-range material,
  shape, actor, and scene indices, mesh point and index ranges inside the mesh
  sections, index counts that are a multiple of three, collision groups below
  32, and joint actor indices that are either `-1` for the world frame or a
  valid actor index that is not the same actor twice.

Any failure returns `OPENUSD_PHYSX_STATUS_INVALID_PAGE` with a specific
`openusd_physx_page_error` code, and the world is left with no content.

## World lifetime and state

`openusd_physx_world_create` takes a `worker_thread_count` and world flags
(events, debug lines, CCD, determinism) and returns an empty world. A world is
in exactly one of three states:

- `EMPTY` - created or torn down, no content. Step, reset, and query return
  `OPENUSD_PHYSX_STATUS_INVALID_STATE`.
- `READY` - a page was validated and applied.
- `FAULTED` - a build failed. The world holds no content; a successful build
  clears the fault.

`openusd_physx_world_build` may be called repeatedly on the same handle. That
entry point tears the previous content down first, so a failed build never
leaves a partially built world behind on that handle - but it also does not
preserve the content that was already there.

Rebuilding an already simulating world is therefore **transactional at the
handle level**, and every consumer of this library is required to follow that
contract:

1. create a second world with `openusd_physx_world_create`;
2. build the new page into that second world and read its status;
3. only when the new world reports `READY`, publish it as the live world; and
4. release the previous world afterwards.

A build that fails, throws, or is cancelled therefore leaves the previously
built world untouched and still steppable, and the caller either keeps
simulating the old content or - when there was no previous world - reports the
fault. `UsdPhysicsNativeWorld.Build` in the managed layer implements exactly
this order: it never touches the retained world, its buffers, or its capability
set until the candidate world has built successfully, and it disposes the old
handle only after the swap.

Every entry point is guarded, takes the world mutex, and returns a status; no
entry point throws.

## Threading

- **Per world**: every entry point takes that world's own mutex, so two threads
  may drive two different worlds at the same time and calls against one world
  are serialized.
- **Process runtime**: the reference count, the `PxFoundation`, and the
  `PxPhysics` instance are protected by the runtime lifetime mutex.
- **Shared factory**: scene, material, shape, actor, and joint creation, mesh
  cooking, and the matching release calls all run against the single shared
  `PxPhysics`, so they are serialized by a recursive factory lock
  (`openusd_physx_runtime::FactoryLock`). This covers the retained worlds, the
  legacy stage entry point, and the legacy primitive scene entry points alike.
  Simulation itself (`simulate`, `fetchResults`, queries) never holds the
  factory lock, so worlds still step in parallel.
- **Lock ordering**: world mutex, then factory lock; the runtime lifetime mutex
  is never taken while the factory lock is held. The factory lock is the
  innermost lock and is recursive so shared helpers such as mesh cooking can
  take it without knowing whether an outer build already did. Release paths
  therefore scope the factory lock to the shared object release and destroy the
  owning runtime reference afterwards. The runtime tracks the factory lock
  depth per thread and enforces the rule: `Acquire` fails with a lock ordering
  message and `Release` reports the violation on `stderr` and asserts, so an
  inverted order shows up as a failed probe instead of a rare deadlock.
- **Errors**: the PhysX error callback stores its message in thread local
  storage, so a failing call only ever reports the message produced on its own
  thread and one thread can never consume or overwrite another thread's
  message. Error buffers themselves are caller owned per call.

## Step, reset, and results

- `fixed_time_step` is the duration of one substep. Zero means "use the rate
  declared by the page". Total advance is `fixed_time_step * substep_count`.
- `substep_count` of zero means one substep, and it may not exceed the maximum
  the page declared.
- Commands are validated as a batch. If any command is structurally invalid,
  carries a non finite value, or names an unknown type, the call returns
  `OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT` and nothing is applied.
- A command that names an unknown identity, or that does not apply to its
  target, is recorded as a diagnostic rather than failing the batch.
- Results are only written after the world and the result page validate. The
  body state capacity must be at least the number of movable actors; otherwise
  the call returns `OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED` and writes nothing.
- Every result buffer pointer must be null exactly when its capacity is zero.
- Events, diagnostics, and debug lines are bounded. Overflow is truncation with
  an explicit dropped count and an overflow flag, never an allocation. A dropped
  count is exact except for query hits that PhysX discarded inside its own
  scratch buffer, which raise
  `OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED` and make the count a lower bound.
- `openusd_physx_world_fetch_results` fills a result page without stepping.
- `openusd_physx_world_reset` restores the state captured at build time and
  recreates broken joints. A caller may supply body state overrides; they are
  all validated first, and an unknown or non movable identity rejects the whole
  batch.

## Events

Events are immutable records batched into the caller's event array once per
step or fetch. There is no callback into the caller and no per event call.

- **Types**: sleep, wake, joint break, contact found, contact lost, trigger
  enter, trigger leave, and controller hit. Contact and trigger events come
  from one `PxSimulationEventCallback` owned by value by the world, so it
  outlives every scene it is attached to and is never heap managed. Sleep,
  wake, and joint break are produced by a deterministic scan in build order
  after the last substep, so they do not depend on PhysX callback order.
- **Identity**: every event names objects by build identity, never by stage
  handle. Sleep and wake name the actor in `object_id0`. Joint break names the
  joint, then both actors. Contact names both actors, canonicalised so that
  `object_id0 <= object_id1` with the normal negated when the pair is swapped,
  and both shapes in `detail0` and `detail1`. Trigger names the trigger actor
  and shape first and is never canonicalised, because the trigger side is
  meaningful. Controller hit names the controller, the actor it hit, and the
  shape. `OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE` marks the details that are
  shape identities.
- **Revision**: every event carries the `step_index` of the step that produced
  it, so a caller can tell a replayed batch from a current one. A step of
  several substeps raises the index before each substep runs, so the events a
  substep reports through the PhysX callback and the events the post substep
  scan derives both carry that substep's index, and the last substep's index is
  the one the result header reports.
- **Order**: events are sorted by step index, then type, then both object
  identities, then both details. Those keys cannot collide, so the order is
  total and does not depend on PhysX internal ordering, thread count, or
  arrival order.
- **Overflow**: the event sink is a bounded heap keyed by that same order, so
  when more events are produced than the capacity allows the retained set is
  always the smallest prefix of the total order, independent of arrival order.
  The remainder is counted in `dropped_event_count` and flagged with
  `OPENUSD_PHYSX_OVERFLOW_EVENTS`. The sink reserves its capacity at build
  time and never allocates during a step.

Event delivery is opt in per world through
`OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS`. When it is not requested the
callback is not attached at all.

The filter shader reads the per shape filter data the build wrote together with
the constant block the world hands the scene. Word 0 and word 1 carry the
collision group mask and the group interaction mask. Word 2 carries the index of
the actor the shape belongs to, which is what indexes the suppressed pair matrix
in the constant block. Word 3 selects swept contact generation: a shape whose
actor, whose scene, or whose world enables continuous collision detection is
tagged so that the pair asks for `eDETECT_CCD_CONTACT`, without which a fast
mover still tunnels through a thin static shape.

Which notifications a pair raises is not a filter data word at all. It is
`PairFilterHeader::notify_flags` at the head of the constant block, so a whole
world turns contact, trigger, and controller notifications on or off at once
rather than per shape.

`PxSceneFlag::eENABLE_CCD` only lets a scene sweep bodies. A body still needs
`PxRigidBodyFlag::eENABLE_CCD` and its pairs still need `eDETECT_CCD_CONTACT`,
so all three levels that can ask for continuous detection - the world flag, the
scene flag, and the actor flag - are folded together before the shapes are
built. An actor that asks for it also raises the scene flag on the scene it
belongs to, so an actor level request is never dropped silently.

The suppressed pair matrix is the only structure whose size grows with the
square of the actor count, so its actor bound applies only to a page that
actually declares a suppressed pair. Enabling events on a page with no
suppressed pair at all never runs into that bound.

## Commands

Commands are validated as a batch before any of them is applied.

- Force, torque, impulse, and angular impulse accept either a vector in
  `vector0` or a magnitude in `scalar0` applied along a normalised `vector0`
  when `OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE` is set.
- The at point variants apply at a world point by default, at a point in actor
  local space with `POINT_LOCAL`, or at the centre of mass with
  `POINT_CENTER_OF_MASS`. The two point modes are mutually exclusive.
- `MODE_ACCELERATION` and `MODE_VELOCITY_CHANGE` select the force mode, are
  mutually exclusive, and are only accepted on the command types that can use
  them. `NO_WAKE` suppresses the implicit wake.
- Validation rejects an unknown type, a non zero reserved field, a zero target
  identity, a flag the type does not allow, a non finite value, a pose, vector,
  or point on a type that does not read it, a magnitude along a zero length
  direction, and a scalar without the magnitude modifier. Any rejection fails
  the whole batch with `OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT` and applies
  nothing.
- Commands are applied in submission order, so a clear that follows an add in
  the same batch wins. `CLEAR_FORCE` and `CLEAR_TORQUE` clear both the force
  and the impulse accumulators.
- `MOVE_CONTROLLER` reads `vector`, accepts `MAGNITUDE`, and targets a
  character controller only. It accumulates over the batch, so several move
  commands in one step add up, and the accumulated displacement is consumed
  once per substep. Any other command type aimed at a controller, or a move
  aimed at anything that is not a controller, is diagnosed and skipped rather
  than failing the batch.

## Queries

`openusd_physx_world_query` runs a batch of raycast, sweep, and overlap
requests against one scene each and appends hits into the caller's hit array.
One call covers the whole batch; there is no per request and no per hit call.
Rejected requests are counted and diagnosed individually instead of failing the
batch. Sweep and overlap accept sphere, box, and capsule geometry. A zero
length direction, a non positive distance, an unknown scene index, a zero hit
budget, excluding both static and dynamic actors, or combining a collision group
mask with an any hit query are all rejections.

Each request writes one contiguous run of hits, nearest first, in request
order, so a caller can split the batch without a second pass. Hits carry the
actor and shape identity, and, when the flags say so, the world position, the
normal, the distance, and the face or element index that PhysX reported. There
are no stage handles in a hit. `EXCLUDE_TRIGGERS` drops trigger shapes.

A sweep that already overlaps a shape at its start pose reports no travel
distance and no contact geometry, because there is none. Such a hit is dropped
unless the request sets `SWEEP_INITIAL_OVERLAP`, and when it is kept it carries
`INITIAL_OVERLAP`, a zero distance, and neither `HAS_POSITION` nor `HAS_NORMAL`.
Only a sweep may set that flag.

Hits past a request's budget are discarded by the same bounded sink the events
use, so the retained hits are always the nearest ones; the remainder is counted
in `dropped_hit_count` and flagged with `OPENUSD_PHYSX_OVERFLOW_QUERY_HITS`.
PhysX gathers touching hits into a scratch buffer of its own and silently
discards an arbitrary subset once that buffer is full, without reporting how
many it discarded. The world always hands PhysX its whole scratch buffer rather
than the request budget, so a request with a small budget still selects the
nearest hits rather than whichever subset PhysX kept. When the scratch buffer
saturates anyway, `OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED` is raised, a
diagnostic naming the request is pushed, and `dropped_hit_count` becomes a lower
bound. Without that flag the dropped count is exact.

Collision group filtering uses one mask per request whose zero value accepts
every group, so a caller that wants every group sends zero rather than a mask
that happens to have every bit set.

## Tests

- `openusd_physx_page_contract` - runs `openusd_physx_contract_probe`, which
  builds without PhysX and always runs.
- `openusd_physx_event_contract` - runs `openusd_physx_events_probe`, which
  builds without PhysX and always runs.
- `openusd_physx_abi_probe`, `openusd_physx_world_probe`,
  `openusd_physx_concurrency_probe`, `openusd_physx_events_world_probe`,
  `openusd_physx_cpu_domains_probe`, `openusd_physx_coupling_probe`,
  `openusd_physx_vehicle_probe`, and `openusd_physx_gpu_domains_probe` -
  require the simulation SDK and are only registered when the `openusd_physx`
  target exists.

`openusd_physx_cpu_domains_probe` covers the version `4` and version `5`
domains: the cylinder, cone and heightfield resting heights; the per body
tuning fields; per axis D6 motions, limits, drives and break events; three
fixed base articulation chains, one passive, one driven and one limited; and
five character controllers that walk, are stopped by a wall, climb a ramp
their slope limit admits, refuse a ramp their slope limit rejects, and report
controller hit events. It also drives one revolute, one prismatic and two
spherical joints through the single axis drive promotion, requiring the
revolute and prismatic drives to hold their bodies against gravity, the
spherical swing drive to move its body out of the plane it starts in, and a
torque about the twist axis of the second spherical body to spin it, which
fails if the promotion locks a twist a spherical joint is supposed to leave
free.

`openusd_physx_coupling_probe` and `openusd_physx_vehicle_probe` cover the
version `6` domains. The coupling probe drops four identical articulation
chains - one uncoupled control, one held by a fixed tendon, one held by a
spatial tendon, and one whose two joints are tied by a mimic joint - and
requires each coupled chain to end measurably away from the control, so a
tendon or mimic joint that is accepted but never reaches the solver fails. The
vehicle probe drives one vehicle through settling, acceleration, an autobox
gear change, steering and braking.

The coupling probe also refuses several hundred articulation pages whose fixed
tendon names an axis its link cannot move, requires the diagnostic to be
identical every time, samples process resident memory across the refusals so a
leaked articulation, link or shape fails the probe, and then builds a good page
on top to prove the world still works. The vehicle probe replays a batch of
malformed vehicle commands - non finite, out of range and non integral gears
among them - and requires each one to reject the whole step, leaving the step
index and the chassis pose untouched, before checking that the automatic gear
and the maximum gear are both accepted.

`openusd_physx_gpu_domains_probe` covers the version `7` domains and asserts one
contract on both kinds of machine. With a usable device every particle system,
particle body, surface deformable and volume deformable must build, publish a
deformation window, keep every published vertex finite, and actually move; a
reset must restore the surface to the configuration the build captured. Without
a device every one of those objects must be skipped individually with a
diagnostic that names it, the rigid bodies of the same build must still fall,
and the world must still step, reset and report a consistent status. The probe
also refuses a result page that declares only one half of the deformation window
and accepts one that declares neither half. There is deliberately no third path:
the domains are never emulated on the CPU.

The probes are built and run against the pinned PhysX 5.5.0
(`eng/physx.lock.json`, tag `106.4-physx-5.5.0`) install produced by
`eng/build-physx-native.ps1`, compiled with `/W4 /WX /permissive-`. The two
contract probes also build and run without that install.
