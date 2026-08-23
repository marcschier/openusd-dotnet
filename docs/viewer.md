# Viewer

`OpenUsd.Viewer` is an Avalonia desktop inspector for USD stages. Launch a staged build with:

```powershell
.\eng\run-viewer.ps1 -Rid win-x64 -StagePath test-assets\minimal.usda
```

The project is split into an embeddable library (`src/OpenUsd.Viewer`) and a thin desktop entry point
(`src/OpenUsd.Viewer.App`, which produces `OpenUsd.Viewer.App.exe`). The library is packable so a host
application can run the same shell in its own process, which is how the OPC UA — OpenUSD connector
renders a live digital twin. See [Embedding the viewer](#embedding-the-viewer).

`-StagePath` alone stages exactly one file, which is sufficient for a self-contained `.usda` or
`.usdz`. A multi-file USD project whose root layer references sibling assets must also pass
`-StageAssetRoot` so the referenced payload tree is staged alongside the root layer:

```powershell
.\eng\run-viewer.ps1 -Rid win-x64 -StagePath assets\chess\chess_set.usda -StageAssetRoot assets\chess
```

Without it every reference fails to resolve, the Viewer opens an effectively empty stage, and the
renderer legitimately reports zero draws while logging one `Could not open asset` warning per
unresolved reference.

## Embedding the viewer

The `OpenUsd.Viewer` package targets `net8.0`, `net9.0` and `net10.0`, so a host does not have to be
on .NET 10 to embed the viewport. `OpenUsd.Viewer.App` remains `net10.0` because it is a development
entry point rather than a published library. See [Support matrix](support-matrix.md#target-frameworks).

`ViewerEntryPoint.Run(ViewerHostOptions)` runs the shell on the calling thread against a programmatic
configuration instead of the command line. On Windows that thread must be single-threaded-apartment.

```csharp
ViewerEntryPoint.Run(new ViewerHostOptions
{
    StagePath = stagePath,
    PluginPath = pluginPath,
    Renderer = "Auto",
    Title = "Live twin",
    StageCameraPath = "/World/HeroCamera",
    ShutdownToken = shutdownToken,
    StageReadyAsync = async (session, cancellationToken) =>
        // Author into the viewer's own stage while it renders.
        await PumpAsync(session.Scheduler, cancellationToken).ConfigureAwait(false)
});
```

`StageReadyAsync` runs on a thread-pool context after each stage is open and its render loop is
running. It receives a `ViewerStageSession` exposing the `UsdStageScheduler` that owns the stage. A
host **must** author only through that scheduler and must never reopen `StagePath`: a second open
creates a second native stage identity and breaks authoring/render synchronisation. Edits made through
the scheduler flow into its ordered change feed, which the viewer already pumps, so they invalidate
and redraw without any further call. The callback may own a long-running subscription and remain
active until its cancellation token is cancelled when the document closes or is replaced. The viewer
waits for that work to stop before disposing the stage scheduler, so callbacks should honor
cancellation promptly. Callback continuations do not depend on the Avalonia message loop, and a
failure is reported as a viewer error without tearing the shell down.

`StageCameraPath` starts the viewport on an authored `UsdGeomCamera` prim when it resolves.
`ShutdownToken` closes the window when cancelled, so a host that renders for a bounded time does not
leave a window behind.

### Reacting to the operator

A host that needs to know what the operator clicked should use `PrimPicked` rather than attaching its
own pointer handling. The viewer already owns hit-test coordinates, DPI scaling, physical-pixel
conversion and stale-revision retry for its own selection, so the callback reuses all of it.

```csharp
ViewerEntryPoint.Run(new ViewerHostOptions
{
    StagePath = stagePath,
    PickTarget = RenderPickTarget.Primitive,
    PrimPicked = async (pick, cancellationToken) =>
    {
        if (pick.Status == RenderPickStatus.Miss)
        {
            return;
        }
        await SendCommandAsync(pick.PrimPath, pick.WorldPosition, cancellationToken);
    },
    SelectionChangedPrimSubtree = "/World/Commands",
    SelectionChanged = (primPaths, cancellationToken) => HandleSelectionAsync(primPaths),
});
```

`PrimPicked` is awaited off the UI thread, so a callback that performs I/O cannot stall the render
loop. A miss raises the callback with `Status` set to `RenderPickStatus.Miss` and a null `PrimPath`.

`SelectionChanged` reports the current selection independently of clicks. Set
`SelectionChangedPrimSubtree` to scope it to one subtree; a host that is itself authoring live values
into the stage would otherwise be woken by its own edits. Leave it unset to receive every selection.

Framework-neutral `ViewportPointerPressed`, `ViewportPointerMoved` and `ViewportPointerReleased`
callbacks carry coordinates already converted to physical pixels, for hosts that need raw input
without taking a dependency on the UI framework.

`ViewerStageSession` also publishes what the viewport holds, so a host can drive a pick itself or
persist the operator's viewpoint:

| Member | Purpose |
| --- | --- |
| `PickingBackend` | The picking backend, or null when the active backend cannot pick |
| `CurrentRenderState` | The revisions a `RenderPickRequest` must quote to avoid going stale |
| `Camera` | View and projection state, plus the viewport dimensions the projection assumes |
| `CameraChanged` | Raised when the operator changes the view |
| `FrameAsync` | Points the viewport at a prim |

## Stage and session editing

The stage panel shows the resolved root/session layer identities, default prim, traversable prim
count, roots, leaves, and maximum hierarchy depth. The Properties tab shows selected-prim type,
active/load/instanceable state, instance/prototype identity, variant sets, direct payload arcs,
attributes, and relationships. Variant-set and payload order is the deterministic order returned by
the [composition-enumeration data APIs](data-api.md#composition-enumeration).

Selected-prim active, instanceable, visibility, and purpose controls are scheduler-owned and
serialized with the document lifecycle. They author to the session layer by default. Root-layer
editing must be enabled explicitly in the Layers tab; root edits remain in memory and are never
saved automatically. Load/unload changes the stage's load rules and is not layer-authored. The
Layers edit-target label reports the stage's raw current edit target, not the load state.

Each variant set shows its available names, current selection, and an explicit **no selection**
option. Changing a selection uses the same session/root edit policy and composition refresh as the
other prim controls. A refresh preserves the current time, layer state, and selected path when that
path still exists; if recomposition removes the selected prim, the Viewer clears the selection.
Enumeration and edit failures are shown as errors rather than being presented as empty sets.

Payload arcs are read-only. The Viewer displays a bounded authored asset path, the authored target
or a target-layer-default-prim marker, and the source-layer identifier. Relative asset paths are
labeled as relative. Anonymous source layers are labeled anonymous and process-local because their
identifiers are not portable. Existing load/unload controls remain available; the Viewer does not
offer payload add/remove authoring.

## Camera navigation

The Viewer owns one UI-thread-affine renderer-neutral orbit-camera controller. Initial stage open,
stage close, and **Reset Automatic** send `CameraState.Default`, preserving each backend's legacy
automatic camera. Explicit navigation is retained when switching render backends. Render workers
receive only immutable `CameraState` snapshots through the render coordinator; they never read the
mutable controller.

Explicit state materializes the legacy eye `(4, 3, 4)`, target `(0, 0, 0)`, `+Y` up direction,
45-degree vertical perspective field of view, `0.1` near plane, and `1000` far plane at the current
viewport aspect. State records target, distance, yaw, pitch, projection mode, perspective field of
view, orthographic height, clipping planes, and aspect. View matrices use the camera ABI's
right-handed, row-major `System.Numerics` row-vector convention: camera forward is view-space `-Z`,
and translation occupies `M41`, `M42`, and `M43`.

Projection matrices retain the row-vector layout but use the pinned
`GfFrustum::ComputeProjectionMatrix` formulas and OpenGL clip depth `[-1, +1]`, rather than the
`[0, +1]` depth produced by the `System.Numerics` projection builders. Perspective matrices use
`M11 = 1 / (tan(fieldOfView / 2) * aspect)`, `M22 = 1 / tan(fieldOfView / 2)`,
`M33 = -(far + near) / (far - near)`, `M34 = -1`, and
`M43 = -2 * near * far / (far - near)`. Centered orthographic matrices use
`M11 = 2 / (height * aspect)`, `M22 = 2 / height`, `M33 = -2 / (far - near)`, and
`M43 = -(far + near) / (far - near)`. In both cases view-space `z = -near` maps
to NDC `-1` and `z = -far` maps to NDC `+1`.

A startup camera can be requested from the desktop app with `--camera /World/HeroCamera`, or by
setting `ViewerHostOptions.StageCameraPath` from an embedding host. If neither explicit source is
present, the Viewer honors the root layer's authored `primaryCameraPrim` metadata when it names an
absolute camera prim path. Startup precedence is: CLI `--camera`, then host `StageCameraPath`, then
`primaryCameraPrim`, then the automatic backend camera. If the requested path is missing or is not a
`UsdGeomCamera`, the Viewer warns and falls back to Automatic.

The **Camera** menu includes a **Stage Cameras** submenu populated from the stage's `UsdGeomCamera`
prims, so authored shots can be selected without finding them in the hierarchy. **Use Selected
Camera** still applies to the currently selected prim when its inspector confirms that it is a
`UsdGeomCamera`; the action is disabled with an explanatory tooltip for no selection and non-camera
selections. One stage-scheduler callback detaches the selected or menu-requested path, time code,
composed local-to-world transform, double-precision inverse world-to-view transform, and one bulk
`UsdGeomCameraState` from
`UsdGeomCamera::GetCamera(time)` plus `GfCamera::GetFrustum()`. The state includes projection, the
exact left/right/bottom/top reference-plane window with aperture offsets, clipping, focal length,
apertures and offsets, focus distance, and f-stop. No stage-bound schema or prim escapes that
callback. Non-finite focal/aperture values, non-positive apertures, negative focal length,
non-finite clipping values, `far <= near`, unsupported projections, and non-invertible transforms
are reported explicitly. Perspective cameras require focal length and `near` greater than zero.
Orthographic cameras permit zero focal length because Gf frustum construction does not use it, and
also permit finite zero or negative near planes.

Authored-camera projection consumes that exact Gf window instead of reconstructing it from default
properties. The viewport uses the OpenUSD `CameraUtilFit` policy without cropping: a viewport wider
than the authored window expands left/right symmetrically around its authored center, and a narrower
viewport expands bottom/top symmetrically around that center. Empty startup viewports retain the
authored aspect and aperture shift. General perspective matrices place `(right + left) / width` and
`(top + bottom) / height` in row-vector `M31`/`M32`; orthographic matrices place the corresponding
negative translations in `M41`/`M42`. Projection is calculated in double precision, and the
view/projection values are converted to `CameraState` floats only after finite and
representable-range checks.

Angles are radians. At zero yaw the eye is on the target's `+Z` axis; positive yaw moves it toward
`+X`, positive pitch raises it toward `+Y`, and pitch never reaches either pole. Positive pan values
move the target along camera-right and camera-up. The input helpers map rightward pointer motion to
positive yaw, upward pointer motion to positive pitch, drag-pan motion to content-following camera
offsets, and positive wheel motion to a negative zoom exponent.

The pure managed controller supports roll-free orbit and pan, exponential perspective dolly or
orthographic zoom, scale-preserving projection toggles, explicit-pose and automatic resets,
resize-aware projection, and bounds framing with a configurable margin. Bounds framing uses a
scaled diagonal radius so point-sized and very large finite bounds produce finite clamped camera and
clipping values. Control-agnostic pixel and wheel delta helpers define deterministic signs and
sensitivity without depending on Avalonia.

Toolbar and Camera-menu controls provide **Reset Automatic**, **Explicit Legacy Pose**,
**Toggle Projection**, **Use Selected Camera**, and **Frame Selected**. Four toolbar arrow controls
orbit left, right, up, or down by 5 degrees per press. The toolbar reports
`Automatic`, orbit `Perspective`/`Orthographic`, or `Stage Perspective`/`Stage Orthographic` plus
the active camera path. Reset Automatic clears stage-camera mode. Orbit, pan, zoom, explicit legacy
pose, projection toggle, and Frame Selected all leave stage-camera mode and continue with the
renderer-neutral orbit controller. On composition viewports, pointer gestures intentionally leave
unmodified left click available for future picking:

- `Alt+left drag`: orbit
- `Alt+middle drag`: pan
- `Alt+right drag`: perspective dolly or orthographic zoom
- wheel over the keyboard-focused viewport: zoom
- arrow key while the viewport has keyboard focus: orbit 5 degrees in that direction

Storm wheel snapshots use the same sign and logical-step intent on every platform. Windows divides
the native delta by 120, Linux maps wheel buttons 4/5 to +1/-1, and macOS maps traditional wheel
events to signed detents. Precise macOS trackpad deltas use 40 points per logical step, retain
fractional movement, clamp each event to four steps, and compensate for device-direction inversion.

The Viewport display controls expose the usdview draw-mode ladder: Wireframe, Wireframe on
surface, Smooth shaded, Flat shaded, Points, Geom only, Geom flat, Geom smooth, and Hidden
surface wireframe. The statistics HUD displays stage AABB and OBB values from the world-bounds
query capability at the stage start time.

Pointer and wheel snapshots use a bounded latest-wins update pump. Physical-pixel resize publishes
viewport and any required orbit or stage-camera projection revision in one coordinator mutation.
Timeline changes query an active authored camera at the same requested time and publish time plus
camera together, so projection, aperture shift, clipping, and other optics animate with transforms.
Resize, reload, and bounded live-stage notifications coalesce authored-camera
refreshes so scheduler work cannot grow without bound. If the camera disappears, changes schema,
becomes invalid, or no longer converts to finite float matrices, the Viewer reports the reason,
preserves time and selection, and falls back to Automatic. **Frame
Selected** queries the selected prim's world bounds once on the stage scheduler at the current time,
using the current render-purpose mask. Missing selections, missing prims, and empty bounds are
reported without changing the camera.

Native Storm child-window camera input is polled from the ABI-8 navigation v2 child snapshot at a bounded
UI cadence. The snapshot carries physical pointer position, buttons, modifiers, cumulative wheel,
focus/inside state, cumulative F/Home/P command counters, and four repeat-aware arrow counters, so
the child may retain native focus
without losing orbit, pan, dolly, zoom, or shortcuts. Polling baselines reset on attach, focus
transitions, and backend switches. Avalonia-routed camera events suppress the overlapping native
sample to avoid duplicate handling. Composition backends continue to use Avalonia routing, and the
toolbar and menu camera commands remain available on every backend.
F, Home, and P execute once per physical press on native and Avalonia paths. Arrow auto-repeat stays
enabled for continuous orbit while held, and focus loss clears held-command state.

## Interactive physics

Physics is available on `win-x64` and `linux-x64`. The pinned vcpkg PhysX port declares
`(windows & x64 & !mingw & !uwp) | (linux & x64) | (linux & arm64)`, so there is no macOS build of
the simulation SDK and no macOS physics runtime package. On `osx-arm64` the **Physics** control
reports an unavailable backend with the `OPENUSD_PHYSICS_BACKEND_UNAVAILABLE` diagnostic and every
other part of the Viewer — stage loading, editing, camera navigation, Storm and Silk rendering —
works exactly as it does elsewhere. The Viewer never requires a physics runtime to start.

GPU physics domains additionally need NVIDIA's proprietary `PhysXGpu` and `PhysXDevice` modules,
which no OpenUsd package redistributes. Without them the capability matrix in the Physics inspector
shows `Cuda` as unsupported and GPU-only objects are skipped with a diagnostic; a user with the
appropriate NVIDIA licence can place their own copies beside the deployed `openusd_physx` library to
enable them. See [Packaging](packaging.md#gpu-modules-are-not-redistributed).

The Viewer creates a physics controller only when the operator asks for one. Stages carrying no
simulation are the common case, so building a world, starting a worker, and allocating render
buffers for every opened stage would slow every stage open to benefit none of them. **Physics**
builds the world; once built, the same button rebuilds it. A rebuild is transactional: the new
world is built before the live one is released, so a rebuild that fails or is cancelled leaves the
world that was already playing intact and paused rather than dropping the session into an empty
state. Only a first build that fails leaves the transport faulted, and a faulted transport refuses
to play.

Build, reset, seek, step, and invalidate all run asynchronously on the stage scheduler and the
physics worker. Nothing simulates on the UI or render threads, and no USD or PhysX handle crosses
the worker or render boundary: the controller exchanges only time codes, whole step counts, and
renderer-neutral snapshots.

### Transport controls

| Control | Behaviour |
| --- | --- |
| Play / Pause | Paces the world forward in wall-clock time. |
| Stop | Returns to the authored start, clears the preview, and restores authored transforms. |
| Step | Advances exactly one fixed simulation step while paused. |
| Loop | Wraps to the authored start at the authored end. |
| Speed | `0.25x` to `4x`. |
| Apply Preview | Authors the simulated poses into the session overlay. |
| Bake... | Opens the bake dialog. |
| Gizmo | Chooses what a viewport drag manipulates: nothing, move, rotate, scale, or drag a body. |
| Snap | Quantizes gizmo drags to the configured translation, rotation, and scale increments. |
| Undo / Redo | Reverses or replays the newest physics property edit. |
| Scrubber | Seeks within the authored start/end range. |

Speed scales how much wall-clock time playback accepts, never the fixed simulation step. Scaling
the step would change what the solver computes, so a user who slows playback down to inspect a
collision would be watching a different simulation than the one that gets baked. The status line
reports state, current time code, step index, backlog, and queue depth.

Playback is paced by the Viewer rather than by a free-running transport: the controller converts
elapsed wall-clock time times the speed into whole fixed steps and requests exactly those. The
accumulator is bounded, so a stalled shell slows playback down instead of asking the worker for an
unbounded catch-up burst.

The toolbar row never clips a control. Every command is either shown at its full width or moved
into the **More physics controls** overflow menu, in authored order. A control that moves into the
menu stays operable there: the speed selector contributes one checkable entry per speed, and
choosing one changes the speed exactly as the inline selector would. The menu is not rebuilt while
it is open, so the entry under the pointer does not move out from under it.

Playback repaints the status line about as often as it steps, but only the fields that changed are
written. Paced steps never mark the toolbar busy - only a command the operator issued does - and a
timeline the operator is dragging is never overwritten by a repaint.

### Rendering simulated poses

Each rendered frame consumes the latest complete transport snapshot without blocking, interpolates
it to the frame's render time, and applies exactly one bounded override batch to the active
backend through the renderer-neutral physics channel. Storm and Silk receive the batch on the
thread that owns their retained state. If no new frame is ready the previous pose is redrawn, so a
slow simulation degrades to a repeated pose instead of stalling the camera.

Building the world also builds the binding table: every extracted simulated identity is bound to the
prim path it drives, and rebuilding or a new stage revision rebuilds it. An identity that cannot be
bound is reported as a refused row in the inspector rather than silently dropped at the backend.

Batches are handed to the backend as a borrowed lease over one of several staging buffers. The
producer never writes the buffer a backend is still reading, so a batch can never be torn by a
simulation step that lands mid-apply, and a batch that arrives while every buffer is in use is
dropped and counted instead of overwriting one.

Switching backends or recovering from a lost graphics context replays the latest complete override
batch, because the new backend retains nothing. Stop, rebuild, invalidation, document switch, and
close all clear the overrides and restore the authored transforms.

Accepting a batch is not the same as drawing it. An in-process backend takes the batch on the
calling thread and only resolves it against its own scene index later, on the thread that owns that
state, so the accepted count says nothing about how many poses were actually drawn. Every backend
therefore reports back what it resolved and what it could not, as a revisioned, latest-wins report
the render bridge drains around each apply. The applied and unresolved counts in the status line and
the inspector come from those reports rather than from the count the Viewer handed over.

A failure while applying a batch never propagates into the render loop. Simulated poses stop being
applied, the authored transforms are restored, the reason is shown in the status line and the
inspector, and the document keeps rendering. Rebuilding the world clears the recorded failure and
resumes applying poses.

Backends that do not advertise physics transform overrides simply do not draw simulated poses; the
simulation itself still runs. Unsupported CUDA domains never block supported CPU simulation - they
are reported in the capability matrix instead.

### Edits and invalidation

Authored edits are observed and debounced. A physics-relevant edit pauses playback immediately,
because continuing to advance a world the stage no longer describes shows the operator motion the
scene does not contain, and invalidates the built world once the edit burst goes quiet. The
Viewer's own session-overlay preview writes are not treated as operator edits: the transport returns
the exact change serial pair of every chunk it authored, captured inside the edit that produced it,
and only that bounded set is suppressed. A chunked preview that authors many changes is therefore
suppressed chunk by chunk, an edit that arrives late is still ignored, and an unrelated edit that
lands between two chunks is still honoured. The reason is always reported in the physics status line
and in the Physics inspector tab.

Applying the preview is only reported as applied when the transport completed it. A preview that is
not supported, fails, or is cancelled is surfaced as a diagnostic and the **Apply Preview** checkbox
returns to its previous state. Any path that invalidates, rebuilds, or faults the world also clears
the preview from the session overlay; the operator's own session layer is left untouched.

### Baking

Baking is explicit and never implicit. The dialog requires a file-backed `.usd`, `.usda`, or
`.usdc` destination, an authored start/end range, a positive sample stride, and a policy for
existing authored samples (overwrite, skip, or reject the whole bake). Progress is reported while
the bake runs, cancellation rolls the destination back, and saving the destination layer is opt-in.

A running bake shows a progress bar and an enabled **Cancel Bake** button next to it. The bake holds
the transport command gate for its whole duration, so cancelling deliberately does not go through
that gate - it signals the running bake directly and returns immediately. Closing the document or
switching stages cancels a running bake first, so its rollback runs before the transport it authors
through is disposed.

### Physics inspector

The Physics tab shows the transport status, the capability matrix, the bounded diagnostics the most
recent operation produced, and the per-object query rows including unbound identities.

The capability matrix is derived, never asserted. A domain is reported as renderable only when the
built world simulates it, the active backend accepts override batches, the backend has reported
resolving a batch, the bridge has not been disabled by a failure, and at least one identity is
bound. Anything else is reported with the reason it is not drawn, so the matrix cannot claim a scene
is being drawn while it is in fact frozen.

Because playback repaints as often as it steps, the inspector caches what it shows. The capability
matrix is recomputed only when something it is derived from changes, the object rows are rebound
only when the binding revision moves, and rows are not rebuilt at all while the Physics tab is
hidden - it re-renders once when the operator selects it.

The caching is content-based rather than identity-based at every layer, because the retained
transport builds its capability flags and diagnostic set fresh on each read. The transport adapter
resolves the capability enumeration once for the process and keeps the rows it built, returning the
same list until a feature bit moves; the diagnostic rows are kept while the retained set is the same
instance and, when a rebuilt set carries the same entries, the entries themselves are compared so
the rows survive. The comparison is over the entries and never over a hash, so a changed, added, or
removed diagnostic can never be masked by a collision. The controller applies the same rule to what
it publishes, so a transport that hands back a new list on every read still leaves the capability
matrix, the diagnostic list, and therefore the whole inspector untouched until the content actually
changes.

### Authoring physics properties

The Physics tab also authors. **Reload Properties** re-extracts every physics object on the stage
and lists them; choosing one lists its properties with the value the extractor resolved, the schema
opinion it came from, and whether the Viewer may author it.

Nothing in that list is hard-coded. The rows come from the generated `openUsdPhysics` property
catalog, which is produced from the same schema definition the plugin, the `.usda`, and the managed
facades are produced from. A domain gains an editor the moment its schema exists, which is why
scenes, rigid bodies, colliders, materials, articulations, tendons, mimic joints, character
controllers, vehicles, particles, cloth, deformables, and attachments are all editable through the
same panel and why a domain added later needs no Viewer change at all.

A row is editable only when both of these hold, and it says which one failed when it is not:

- *not simulated* — the built world does not report the capability the property's domain needs,
  so authoring it would change the file without changing what you are watching.
- *read only* — the managed runtime carries `bool`, `int64`, `double`, `string`, `token`, and
  `float3` scalars only, matched exactly. Stock `UsdPhysics` masses, frictions, break forces, joint
  limits, and joint drives are `float`; centres of mass and joint frames are `point3f` and `quatf`;
  velocities are `vector3f`. Those rows still show their extracted value, because hiding them would
  say the scene has no such setting.

Every edit is one transaction on the stage scheduler. The edit target is redirected into the
session overlay's user-edit layer for the duration of the edit and restored afterwards, so the
inspector can never write into the file the stage was opened from: simulation results compose above
user edits, user edits compose above the authored scene. A step that authors several properties
produces exactly one observed change rather than one per property.

Undo stores intent rather than layer state. Each step carries the exact value every property held
before it, *including the absence of an authored opinion*, so undoing an edit that created an
opinion removes it again instead of freezing the schema fallback into the file. One pointer gesture
is one undo step: consecutive edits to the same property inside a short merge window coalesce, so
undoing a slider drag takes one press rather than a hundred. An undo whose re-author does not reach
the stage is put back on the undo stack, so the history can never claim the stage holds a value it
does not.

The Viewer recognises its own authored changes exactly, by the change-serial pair that brackets
them, and replaces the caller's conservative classification with what it actually authored. Editing
a mass therefore pauses playback and rebuilds the world once the edit burst goes quiet, while
editing simulation metadata - which is provenance, never an input - does not disturb a running
simulation at all. A change with different serials is never treated as the Viewer's own.

### Gizmos and snapping

The gizmo selector decides what a viewport drag does. Move, rotate, and scale are axis-constrained
or view-plane drags computed from the pointer ray, the gizmo pivot, and the chosen frame (stage axes
or the object's own). Snapping quantizes the result: translation to a linear increment, rotation to
an angle, and scale to a factor that can never reach zero, because a snapped scale of zero would
collapse the object into a transform no later drag could recover.

Degenerate configurations refuse to move anything rather than producing an infinity. A ray parallel
to the drag plane, a camera looking straight down the axis being dragged, and a zero-length axis all
report "no movement": a gizmo that teleported the selection to the far edge of the scene the moment
the camera lined up with an axis would be far worse than one that briefly stops responding.

The camera bindings are unchanged. A gizmo is only active while one is selected, and the shortcut
`Q` turns every gizmo off so a drag navigates the camera again.

### Driving the simulation

Interactive inputs never touch the stage. They are runtime commands the physics worker stages and
the next fixed step applies, submitted as one batch per action or per pump - never one interop call
per input event.

| Control | Command |
| --- | --- |
| Force / Impulse / Torque | A direction, a magnitude, and a mode (force, acceleration, or velocity change). |
| Wake / Sleep | Wakes or sleeps the selected body on the next step. |
| Drag Body | A damped spring applied at the grabbed point, bounded so a pointer flick cannot launch the body. |
| Drive with WASD | Moves the selected character controller by a camera-relative displacement per step. |
| Vehicle sliders | Throttle, brake, steering, hand brake, clutch, and gear, submitted every step. |

The vehicle sliders are only submitted while **Send vehicle input every step** is checked.

Dragging a body is a spring, not a teleport. Setting the pose directly would push the body through
everything it meets and discard the momentum the solver gave it, so what the operator would be
dragging is no longer the simulated object. The spring keeps it inside the simulation: it collides
on the way, it rotates about the point that was grabbed, and releasing it leaves it with the
velocity it actually had. Releasing also clears the staged force, because the runtime would
otherwise apply the last staged push one more time.

The vehicle controls are the real drivetrain inputs. Throttle, brake, hand brake, and clutch are in
`[0, 1]`, steering is in `[-1, 1]`, and the gear is `0` for the drivetrain's own choice, `1` for
reverse, `2` for neutral, and `3` upward for the forward gears. The Viewer clamps at the control,
where the operator can still see the slider reach its limit, because the runtime rejects a whole
step whose vehicle input falls outside those ranges rather than clamping it silently.

Every interaction control is capability gated from the built world's capability matrix, not from the
schema. Vehicles are disabled when the world does not simulate vehicles, character controllers when
it does not simulate controllers, and the whole interaction section when the world does not accept
runtime commands at all - and it says which. A command the world refuses is reported once, and the
control that produced it is switched off rather than repeating the refusal on every pump.

#### What a command is addressed to

One prim usually composes into several simulated objects: a chassis is a rigid actor, a collision
shape, and a vehicle all at once, and each of those is addressed by its own identity. The extractor
gives every record an identity of its own so the inspector can keep a selection on the exact section
the operator chose, but that identity is a hash of the authored path and the object type and is
never the identity the solver holds. The composer's address is, and it is published as
`UsdPhysicsIdentities.ForSimulatedObject`.

Each inspector section therefore carries both: the extractor's identity, which anchors the selection
across a reload, and the composed address, which every command is built from. Sections resolve like
this:

| Section | Addressed object | Accepts |
| --- | --- | --- |
| Rigid body, articulation link | The actor or link at the prim path | Force, torque, wake, sleep, drag |
| Collider | The body or link that owns it, or the static actor it composes into | The owning body's commands |
| Character controller | `<prim>.controller` | Move |
| Vehicle | `<prim>.vehicle` | Driver input |
| Physics scene | The scene at the prim path | Gravity |
| Free rigid actor (not in an articulation) | The actor at the prim path | The above plus impulses |
| Articulation root, joint, material, tendon, mimic joint | Nothing the solver addresses | Nothing |

A collider resolves to its owning body because a force is applied to an actor, not to a shape; a
drag started on a collider section therefore pushes the body it belongs to, including when that body
is an articulation link.

An articulation link is addressed by its own prim path even though it is composed into its
articulation rather than into the actor table, and the retained world resolves it through a link
map. A link accepts forces, torques, clears, wake, and sleep; it refuses impulses and angular
impulses, because PhysX does not accept the impulse or velocity-change force modes on a link, and
it refuses a directly stated linear or angular velocity, a kinematic target, and a teleport, because
a reduced-coordinate link's velocity and pose are functions of the joint degrees of freedom above
it. The Viewer therefore leaves **Apply Force**, **Apply Torque**, **Wake**, **Sleep**, and dragging
enabled on a link and disables only **Apply Impulse**. Everything the world refuses is reported per
object rather than silently dropped.

An application point never carries a force-mode modifier. The point is delivered by converting the
force into a torque about the centre of mass, which needs a real force, so an acceleration or a
velocity change is asked for without a point instead - which is exactly equivalent.

Overlapping or nested articulation roots are ill formed but authorable. The composer assigns every
body to exactly one articulation: the first root in stage traversal order that claims a body owns
it, which for nesting is the outermost one, and any later root that overlaps is refused as a whole
with a diagnostic naming the shared body. Nothing is orphaned - the shared bodies stay links of the
accepted articulation and the rest fall back to ordinary rigid actors joined by ordinary joints.

The articulation *root* is not a body at all: its identity lives in the world's articulation table,
so it offers no interaction. When the root schema sits on a prim that is also a body, that prim
still produces its own rigid-body section and that section carries the interactions.

The interaction controls follow the selection as well as the capability matrix, so selecting a
vehicle section disables the force controls and selecting the chassis body disables the drivetrain
sliders. A section the world cannot address at all offers no interaction and says so, rather than
offering a control whose every press the world would refuse.

Commands submitted while playback is paused are staged, not lost: the outcome says they apply on the
next simulation step, which is what makes single-stepping an interaction possible. A build, a reset,
or an invalidation discards whatever was staged, so an input is never replayed into a world that
replaced the one it was aimed at.

### Authoring keyboard shortcuts

| Key | Action |
| --- | --- |
| `Q` | No gizmo |
| `G` | Move gizmo |
| `E` | Rotate gizmo |
| `R` | Scale gizmo |
| `H` | Drag body |
| `X` | Toggle snapping |
| `Z` / `Y` | Undo / redo the newest physics property edit |
| `W` `A` `S` `D` `Space` `C` | Move the selected character controller while **Drive with WASD** is checked |

Every binding is refused while a text field has focus and while any modifier is held, exactly like
the transport and camera bindings, so typing a prim name can never walk a character across the scene
or undo an edit. The movement keys are held state rather than commands: the controller is asked to
move once per simulated step for as long as the key is down, and losing window focus releases them
so a controller cannot keep walking in the background.

Every other binding is a discrete command and runs exactly once per physical press. Holding the key
produces a stream of repeats from the operating system, and running the command on each of them
would unwind the whole undo history from one held `Z`, step dozens of frames from one held `N`, or
reopen the bake dialog on every repeat. Repeats are therefore swallowed until the key is released,
and a focus transfer or a deactivation drops the held state so the next press still works. Undo,
redo, apply, and clear also refuse to overlap: a second edit started while one is still authoring
and reloading would leave the selection anchored to a document neither produced.


The Diagnostics tab retains only the latest bounded entries and samples existing render activity at
a bounded cadence. It reports the active backend, compositor/API/device identity, fallback or
recovery reason, CPU/GPU frame duration, draw and triangle counts, retired cleanup owners, and
Storm/Silk/page/GPU resource counters.

Copy and export redact the source-tree and user-profile paths by default. Select **Include paths**
only when an unredacted report is required.

## Settings and accessibility

Viewer settings are stored under:

```text
LocalApplicationData\OpenUsd\Viewer\viewer-settings.txt
```

The versioned, NativeAOT-safe text store is written atomically. It contains only window dimensions,
panel widths and visibility, renderer preference, selected details tab, diagnostics visibility, and
the manual timeline snap preference. Stage-specific camera and session state are not persisted.
Malformed or oversized settings are ignored with an explicit Viewer diagnostic; I/O failures are
shown in the status area.

Access keys are shown with underlined menu/button labels. Keyboard shortcuts include:

- `Ctrl+O`: open a stage
- `Ctrl+R`: reload the current stage
- `Ctrl+1` through `Ctrl+4`: Properties, Layers, Diagnostics, and Settings
- `Space`: play or pause the timeline when focus is not in a text box
- `F`: frame the selected prim
- `Home`: reset the camera to Automatic
- `P`: toggle Perspective/Orthographic projection
- arrow keys: orbit by 5 degrees while the viewport has keyboard focus
- `K`: play or pause the physics simulation
- `J`: stop the physics simulation and restore the authored state
- `N`: advance the physics simulation by one fixed step
- `B`: open the physics bake dialog

Camera and physics shortcuts are ignored while a text box or combo box is being edited, and never
fire with a modifier held. Controls use logical markup order, automation names, and theme resources
rather than fixed foreground colors.

## Automated evidence

Shared soak and renderer evidence runs do not load or save Viewer settings, enable session or
variant controls, or sample the interactive diagnostics model. Schema 8 camera evidence is fully
automated and does not change interactive camera navigation: it temporarily applies deterministic
managed view/projection matrices, captures bound backend pixels and Storm camera diagnostics, and
restores automatic mode before continuing fallback, loss, quarantine, or switching scenarios.
On Windows it additionally delivers a real Win32 Alt-left drag to the Storm child, polls the ABI-8
navigation snapshots, applies the normal Viewer camera adapter, and binds the changed camera and
pixel artifacts while proving that no duplicate Avalonia routed event fired.
Automated runs bypass interactive camera publication during resize, so temporary evidence camera
transitions are not overwritten. They also disable the interactive **Use Selected Camera** action.
The one named `stage-camera-backend-smoke` diagnostic is the exception: its runner opens
`test-assets/viewer-stage-camera-smoke.usda`, explicitly selects
`/World/CameraRig/Offset/ShotCamera`, and queries it only through
`ViewerSchedulerStageCameraSource`. The fixture has asymmetric visible geometry, two non-identity
parent transforms, off-axis aperture offsets, valid clipping, and transform/optics samples at time
codes 0 and 24. Without reopening the scheduler-owned stage, the diagnostic applies both detached
camera snapshots, renders each exact state through Storm, D3D12, and Vulkan, verifies Storm native
revision/signature observations, captures non-background pixels, then resets time and camera to
Automatic. Schema 8 binds the stage SHA-256, selected path, canonical snapshot SHA-256 values,
state revisions, camera/native signatures, screenshots, source identity, and zero-resource teardown.
No stage-camera environment value affects normal or other automated startup modes.
