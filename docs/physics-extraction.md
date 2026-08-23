# Physics extraction

Physics extraction turns a composed USD stage into one immutable, pointer-free page that
describes everything the simulation needs. It is the boundary between USD and the simulation:
after extraction returns, no USD handle, path, or layer is reachable from the simulation side.

## One traversal, one call

`UsdPhysicsStageExtractor.ExtractAsync` schedules exactly one work item on the stage scheduler.
That work item makes exactly one native call, which performs exactly one composed stage
traversal and returns one serialized page. There is no per-prim or per-property interop.

```csharp
await using var scheduler = UsdStageScheduler.Open(path);
UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.ExtractAsync(scheduler);
```

The page is a detached result: it implements `IUsdDetachedResult`, so the scheduler hands it
straight back to the caller and it keeps working after the stage is disposed.

`UsdPhysicsStageExtractor.GetTraversalCount()` and `GetVisitedPrimCount()` report how many
traversals the process has run and how many prims the last traversal visited. Tests assert the
traversal count moves by exactly one per extraction.

The traversal uses `UsdTraverseInstanceProxies` over active, defined, concrete prims, so
instances are visited through their proxies. Every object gets a stable identity hashed from the
composed prim path and the object type, so two instances of one prototype produce two distinct
identities that both name the same `PrototypeId`.

## What the page contains

The page is a header plus ten byte-aligned sections: strings, objects, properties,
relationships, targets, numbers, texts, points, indices, and diagnostics. Records reference each
other only by index, never by pointer, so the page can be copied, cached, or checkpointed as
bytes.

The header carries `metersPerUnit`, `kilogramsPerUnit`, `timeCodesPerSecond`, the authored start
and end time codes, the sampled time code, the source up axis, the resolved default scene, page
traits, and the content fingerprint.

Each object records its identity, composed path, name, type name, object kind, domains, traits,
collision geometry, the owning scene and parent body, its simulation-space transform, and the
ranges of properties, relationships, mesh points, and triangle indices that belong to it.

Every page is validated before a caller can read it. `UsdPhysicsExtractionPage.Create` rejects a
buffer whose magic, ABI version, header size, declared size, alignment, section bounds, section
overlap, string termination, or cross-section indices do not hold, so a malformed page throws
`UsdPhysicsExtractionException` instead of being indexed.

## Units and the up axis

The page reports transforms in simulation space, which is metres with a Y up basis. Positions
are rotated into the simulation basis and then scaled by `metersPerUnit`; orientations are
composed with the same rotation. Collider extents are scaled but never rotated because they are
local to the collider.

`UsdPhysicsExtractionSpace` reproduces that mapping exactly and inverts it, so a simulation
result maps back onto the stage it came from without guessing:

```csharp
var space = UsdPhysicsExtractionSpace.FromPage(page);
(double X, double Y, double Z) stagePosition = space.ToStage(simulationPosition);
```

When `metersPerUnit` or `kilogramsPerUnit` is not authored, extraction applies the standard
fallback, sets a page trait, and emits an informational diagnostic naming the fallback. A value
that is not a positive finite number is an error diagnostic and the standard fallback applies.

Extraction only converts lengths, because only lengths appear in a transform. Everything else
keeps the authored stage units on the page, and the composer converts it once, when it projects
onto the simulation build page:

| Quantity | Conversion applied by the composer |
| --- | --- |
| Mass | `mass * kilogramsPerUnit` |
| Density | `density * kilogramsPerUnit / metersPerUnit³` |
| Centre of mass | `offset * metersPerUnit` |
| Inertia | `inertia * kilogramsPerUnit * metersPerUnit²` |
| Linear velocity | rotated into the simulation basis, then `* metersPerUnit` |
| Angular velocity | rotated into the simulation basis, then degrees to radians |
| Force and impulse limits | `* kilogramsPerUnit * metersPerUnit` |
| Torque limits | `* kilogramsPerUnit * metersPerUnit²` |
| Angular limits and angular drive targets | degrees to radians |
| Linear limits and linear drive targets | `* metersPerUnit` |
| Mesh collider points | `* metersPerUnit`, then scaled by the authored collider scale in the runtime |
| Particle, surface, and volume simulated points | `* metersPerUnit * authored prim scale` |

An authored point is the one quantity the composer has to scale twice. A point is local and in
stage units, and the object it belongs to also carries an authored scale that nothing downstream
can apply for it: a cooked collision mesh is scaled by the collider scale in the runtime, but a
particle buffer and a deformable vertex buffer are positions rather than shapes and have no
geometry scale at all. Both factors are therefore baked into the staged point, which reproduces
the authored transform exactly because extraction decomposes the local-to-world basis into per
axis lengths and a normalized rotation. A prim whose authored scale cannot be simulated, because
an axis is not finite or collapses the object to nothing, is skipped with its own note rather than
being simulated at a size its author never wrote.

Because the composer has already applied both scales, the build page always describes kilograms
and metres: it reports `MetersPerUnit` and `KilogramsPerUnit` as `1`.

## Namespace precedence

Physics opinions can be authored in three namespaces. Extraction resolves each canonical
property independently, in this order:

1. the project namespace, `openUsdPhysics:*`, which always wins;
2. an optional foreign vendor namespace, `physx*`, only when it is present on the stage;
3. the standard namespace, `physics:*`.

A foreign opinion matches a canonical property when the last component of its namespaced name
equals the canonical leaf, so `physxRigidBody:mass` resolves `physics:mass`. No foreign schema is
required, referenced, or vendored: if nothing authors it, nothing changes.

Each resolved property records which namespace supplied it. When a weaker namespace also
authored the same property, the property is marked as shadowing a weaker opinion. When more than
one foreign name matches, the first is used, the property is marked ambiguous, and a warning
diagnostic names the choice, so the result stays deterministic.

Authored physics properties without a canonical meaning are captured as unmapped. They always
take part in the fingerprint; the `IncludeUnmapped` option only decides whether they also occupy
property records, so turning it on never changes the fingerprint.

Multi-apply properties keep their authored instance. A drive or limit authored as
`drive:angular:physics:stiffness` or `limit:linear:physics:low` is extracted under the canonical
key with its verbatim authored name, so the instance stays recoverable. The composer selects the
instance that matches the joint: the axis instance of a revolute or prismatic joint first, then
the angular or linear instance that suits the joint, and finally the first authored instance in
name order. When a joint authors several usable instances, the choice is recorded in the composer
report so nothing is dropped silently.

Cross-domain opinions are shared, not consumed. A `physics:simulationOwner`, a material binding,
a filtered pair, a density or a collision toggle authored once on a prim that carries several
physics objects reaches every object on that prim, so a rigid body and its collider on the same
prim both see it.

## Classification and ownership

Every rigid body is classified exactly once:

| Authored state | Classification |
| --- | --- |
| The body is disabled | Static |
| The body is kinematic | Kinematic |
| The transform has time samples and the body would be dynamic | Kinematic, with a warning |
| Otherwise | Dynamic |

Contradictory ownership is rejected deterministically. More than one simulation owner, a rigid
body nested inside another rigid body, and a collider that claims a different owner than its body
are all errors. An error disables only the object it names: the object loses its enabled trait,
gains a disabled trait, and the page records that it has disabled objects. Every other object,
and every other domain, keeps extracting.

## Diagnostics

Diagnostics are stable and ordered by the object they name, with stage-wide diagnostics first.
Each one carries a severity, a category, a specific code, the object index and identity, the
canonical property key when there is one, and a message. Because the order is stable, two
extractions of the same stage produce byte-identical diagnostics.

Domains that are extracted but not simulated yet are reported once per object with an
informational diagnostic, and the object is marked as belonging to an unsupported domain. The
supported domains continue to compose normally.

## Content fingerprint

The page carries a 128-bit fingerprint over every physics-relevant authored value: stage
metadata, each object's identity, path, type, domains, traits, prototype, geometry, transform and
extent, every property including unmapped ones with its verbatim authored name, source namespace,
type, traits and values, every relationship with its resolved target paths, mesh points and
indices, and the page traits with truncation masked out.

Diagnostics are excluded, and visual attributes are never read at all, so editing
`primvars:displayColor` leaves the fingerprint unchanged while editing
`physics:kinematicEnabled` changes it. That makes the fingerprint usable directly for checkpoint
and cache invalidation.

## Bounds and cancellation

Every section has a capacity, and `UsdPhysicsExtractionOptions` can lower any of them. Reaching a
bound never grows the page: extraction sets the truncation bit for that section, marks the page
truncated, and records a capacity diagnostic. All offset and size arithmetic is checked, every
section is eight-byte aligned, and the page as a whole is bounded.

Cancellation is honoured at the scheduler boundary, before the stage is touched. Once the
traversal returns, the extraction holds no stage state at all.

## Composing the simulation page

The supported subset of a page projects onto the existing simulation build page: scenes,
materials, shapes, actors with their shapes, joints, and filter pairs. Because the page already
describes metres and a Y up basis, the build page describes simulation space directly rather than
repeating the stage conversion. Objects that cannot be composed are skipped individually and
reported in a stable order.

A rigid body and a collider authored on the same prim compose into one actor that owns that
collider, exactly like a collider authored below the body.

A shape pose is the collider pose expressed in the frame of the actor that owns it, so applying
the actor pose to it reproduces the authored world placement. On top of that, the authored
geometry axis of a capsule, cylinder, cone or plane is turned into a local rotation that aligns
the simulation reference axis with the authored one; geometry without an axis keeps an identity
rotation.

A scene with an authored gravity direction rotates that direction into the simulation basis. A
scene that authors no usable direction falls back to simulation down directly, so the fallback
never depends on the stage up axis.

### The mass frame of an actor

A mass opinion is authored in the local frame of the body, and the stage basis change never
rotates a local frame: it is already carried by the world pose of the actor. So the centre of
mass and the diagonal inertia are only scaled by the stage units, and `physics:principalAxes`,
the rotation from the body frame into the frame the diagonal inertia is stated in, is carried
through unchanged. The build page states it next to the inertia, and the runtime applies it as
the rotation of the centre of mass pose so the inertia is used about the axes it was authored
about. A rotation is carried through whenever it is finite and its squared length is within
`[0.25, 4]`, and the runtime normalises it before use, so a legal quaternion that is not exactly
unit length keeps its orientation. Only an unauthored rotation, which arrives with all four
components zero, and a rotation outside that range become the identity.

### What happens to a value the runtime cannot take

A single authored value must never take a whole page down, so the composer reduces anything the
runtime cannot take instead of forwarding it. A property the extractor marked invalid, and a
value that is not finite, is negative where the runtime needs it non negative, or does not
survive the narrowing to the page type, is read as if it had never been authored: mass and
damping fall back to their defaults, the centre of mass falls back to the body origin, the
diagonal inertia falls back to the shapes deciding it, a velocity falls back to rest, and the
principal axes fall back to the identity.

A vector is always reduced as a whole rather than per component, because the three components are
one authored physical value and keeping two of them would state a value nothing authored. Each
reduction adds one note to the composition report, and the notes of one actor are always ordered
mass, linear velocity, angular velocity, centre of mass, diagonal inertia, principal axes, linear
damping, angular damping. A static actor never reports a velocity because it never carries one.

The same rule holds outside the actors. A gravity magnitude or a bounce threshold that is negative
or that does not survive the narrowing falls back to the default of the scene, and a collider scale
that overflows or that collapses to zero once it is narrowed falls back to a unit scale; each one
is reported. A gravity direction is normalised by dividing by its largest component first, so a
direction whose own squared length would overflow still keeps its orientation instead of turning
into a zero direction the runtime would refuse; only a direction that carries no orientation at all
falls back to simulation down, and only an authored one is reported.

Mesh topology is the one case that drops an object rather than reducing it. The mesh points of a
page are one shared section that is validated as a whole, so a collider is checked against the
same rules the page validator applies before any of its topology is staged: every point has to be
finite, a convex hull needs at least four points, a triangle mesh needs at least three points and
whole triangles, and every index has to name one of its own points. A collider that fails any of
them is left out of the page together with a note that names what was missing, no points of it are
staged, and the rigid body that owned nothing else is then reported as a body without a usable
collision shape. Every other actor of the stage keeps simulating.

### What an authored limit means

USD leaves an unauthored bound at an infinity and locks an axis by authoring a lower bound that
is not below the upper bound, so a missing bound is not a zero. The composer resolves each joint
accordingly:

| Authored range | Composed result |
| --- | --- |
| No bound at all | The axis stays free and nothing is reported |
| Both bounds, lower below upper | The limit is enabled and carries that range |
| One bound only | The axis stays free and a note names the joint |
| Lower not below upper, which locks the axis in USD | The axis stays free and a note names the joint |

A one sided range and a locked axis are reported rather than enforced because the build page
carries neither, and turning either into a range the solver would enforce changes the joint into
a different joint.

A spherical joint states its swing through `physics:coneAngle0Limit` and
`physics:coneAngle1Limit`, where a negative or unauthored angle means unlimited. An unlimited
side is carried as the widest cone the solver accepts, an authored side keeps its angle, and a
cone that is authored shut is reported instead of being handed over as a limit the solver
rejects. A distance joint states `physics:minDistance` and `physics:maxDistance` the same way: an
unauthored minimum arrives as zero and an unauthored maximum as the largest float, which are the
values that switch each side of the solver limit off.

The same rules apply whether the range is authored directly on the joint or through a
`PhysicsLimitAPI` instance, and the instance is selected by the joint type and the authored axis.

### The CUDA accelerated domains

Position based dynamics particle systems and their particle bodies, surface deformables, and
finite element volume deformables compose exactly like every other domain, and deliberately
without asking whether the machine running the composition has a CUDA device. Composition is a
property of the authored stage; whether an object can then be simulated is a property of the
runtime. Deciding here would make one stage compose into two different build pages on two
machines, which the build page contract forbids.

The runtime is the single place that decides. It publishes the CUDA capability bits only after a
context manager has been created and has reported a usable device, and a build that cannot reach
one skips each GPU object individually with a diagnostic that names it while every CPU domain of
the same build keeps simulating. Nothing is ever emulated on the CPU.

A particle system owns the particle bodies that name it, and the composer stages every body of one
system before the system record that names the window, so the window is contiguous by
construction. A particle body is simulated from the points of the geometry it is applied to, and a
surface deformable from that geometry's triangulation, so both are carried by the same shared mesh
point and index sections a collider uses and are validated against the same bounds.

Three authored constructs are reported rather than approximated:

| Authored construct | Composed result |
| --- | --- |
| `OpenUsdPhysicsParticleClothAPI` | Skipped with a note naming `OpenUsdPhysicsSurfaceDeformableAPI` |
| `OpenUsdPhysicsDiffuseParticlesAPI` | Skipped with a note |
| A volume deformable with no authored tetrahedra | Skipped with a note |

Particle cloth is skipped because this build does not implement the position based dynamics cloth
path; surface deformables are the supported cloth path and the note names them. Diffuse particles
are skipped because the build page carries no diffuse particle record. A volume deformable with no
authored tetrahedra is skipped because building a simulation mesh from a render mesh is a device
side operation, and it is never approximated on the CPU.

An authored offset of a particle system is a length in stage units, and the project schemas state a
negative offset as "ask the runtime for its own default", which the build page spells as zero. A
rest offset that exceeds the contact offset it belongs to is clamped to it, because a solver can
never separate particles further than it generates contacts for. A volume material that authors a
separate damping and damping scale has them folded into the one elasticity damping term the pinned
simulation SDK still models, which is done here, where the authored vocabulary is known, rather
than by a runtime that would have to guess.

An authored particle collision group is clamped to the twenty bits a position based dynamics phase
reserves for it, and the build page validator rejects any wider group outright. The bound is not
cosmetic: the runtime packs the group, the per body behaviour flags, and the bound material index
into one phase lookup key, and a wider group would reach the bits the material index occupies, so
two bodies that share nothing could collide on the same key and silently be given one phase.

### What a deformable result drives

A deforming body publishes one simulated position per simulated vertex rather than a transform, so
it reaches a renderer through its own path. `UsdPhysicsFrame.Deformations` names one window per
body into `UsdPhysicsFrame.DeformationVertices`; a window is always complete, because a body whose
vertices did not fit the frame is dropped whole and reported through `DeformationsTruncated`.

Two consumers exist and neither interpolates geometry, because two snapshots only describe the same
vertex buffer while the topology is unchanged:

| Consumer | What it does |
| --- | --- |
| `PhysicsRenderInterpolator.Deformations` | The latest complete geometry as an immutable bounded view |
| `UsdPhysicsResultBatch.DeformationSamples` | One detached `UsdPhysicsPointSample` per window |

The render view is what a backend that can upload geometry applies whole; hdSilk replaces the
points of the retained mesh a region is bound to and refuses a region whose vertex count disagrees
with that mesh. The point samples are what makes a deformed body visible to any backend that draws
the stage, because the preview overlay authors them into the session layer rather than into the
authored layer. Deriving the samples is deliberately explicit: a host that has not bound its
deformable prims would otherwise turn every window into a rejected bake record the moment a stage
grew a cloth.

## Testing

Pure managed coverage lives in `tests/OpenUsd.Physics.Tests/Extraction`. It builds byte-exact
pages by hand to cover page validation, record decoding, the space mapping, the options mapping,
and composition.

Native-backed coverage lives in `tests/OpenUsd.NativeCoverage.Tests`. It authors small USDA
stages and extracts them through the real shim to cover units and the up axis, namespace
precedence, ownership and classification, instancing, diagnostic order, every schema domain,
malformed authored values, fingerprint behaviour, and the one-traversal guarantee. It also runs
whole stages through the composer end to end, asserting the composed actors, shapes, materials,
scenes and joints in simulation units.

```powershell
pwsh eng/run-managed-tests.ps1 -Project tests/OpenUsd.Physics.Tests/OpenUsd.Physics.Tests.csproj
pwsh eng/run-native-managed-tests.ps1 `
    -Project tests/OpenUsd.NativeCoverage.Tests/OpenUsd.NativeCoverage.Tests.csproj
```

### Requiring the physics runtime

The CPU domain tests in `tests/OpenUsd.Physics.Tests` need the compiled
`openusd_physx` library, so they must never pass by quietly doing nothing when it
is absent. `eng/run-native-managed-tests.ps1` looks for the staged library and
exports `OPENUSD_REQUIRE_NATIVE_PHYSICS=1` when it found one and `0` when it did
not. With `1` a test that cannot reach the runtime **fails** and names the
runtime diagnostics that explain why; with `0` it is reported as skipped with the
same reason. A run whose summary shows the CPU domain tests skipped therefore
means the native library was not staged, never that the domains were verified.

