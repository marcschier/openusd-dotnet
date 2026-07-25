# Viewer

`OpenUsd.Viewer` is an Avalonia desktop inspector for USD stages. Launch a staged build with:

```powershell
.\eng\run-viewer.ps1 -Rid win-x64 -StagePath test-assets\minimal.usda
```

`-StagePath` alone stages exactly one file, which is sufficient for a self-contained `.usda` or
`.usdz`. A multi-file USD project whose root layer references sibling assets must also pass
`-StageAssetRoot` so the referenced payload tree is staged alongside the root layer:

```powershell
.\eng\run-viewer.ps1 -Rid win-x64 -StagePath assets\chess\chess_set.usda -StageAssetRoot assets\chess
```

Without it every reference fails to resolve, the Viewer opens an effectively empty stage, and the
renderer legitimately reports zero draws while logging one `Could not open asset` warning per
unresolved reference.

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

**Use Selected Camera** applies only to the currently selected prim when its inspector confirms
that it is a `UsdGeomCamera`; the Viewer does not discover or choose arbitrary stage cameras. The
action is disabled with an explanatory tooltip for no selection and non-camera selections. One
stage-scheduler callback detaches the selected path, time code, composed local-to-world transform,
double-precision inverse world-to-view transform, and one bulk `UsdGeomCameraState` from
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
**Toggle Projection**, **Use Selected Camera**, and **Frame Selected**. The toolbar reports
`Automatic`, orbit `Perspective`/`Orthographic`, or `Stage Perspective`/`Stage Orthographic` plus
the active camera path. Reset Automatic clears stage-camera mode. Orbit, pan, zoom, explicit legacy
pose, projection toggle, and Frame Selected all leave stage-camera mode and continue with the
renderer-neutral orbit controller. On composition viewports, pointer gestures intentionally leave
unmodified left click available for future picking:

- `Alt+left drag`: orbit
- `Alt+middle drag`: pan
- `Alt+right drag`: perspective dolly or orthographic zoom
- wheel over the keyboard-focused viewport: zoom

Storm wheel snapshots use the same sign and logical-step intent on every platform. Windows divides
the native delta by 120, Linux maps wheel buttons 4/5 to +1/-1, and macOS maps traditional wheel
events to signed detents. Precise macOS trackpad deltas use 40 points per logical step, retain
fractional movement, clamp each event to four steps, and compensate for device-direction inversion.

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

Native Storm child-window camera input is polled from the ABI-7 child snapshot at a bounded
UI cadence. The snapshot carries physical pointer position, buttons, modifiers, cumulative wheel,
focus/inside state, and cumulative F/Home/P command counters, so the child may retain native focus
without losing orbit, pan, dolly, zoom, or shortcuts. Polling baselines reset on attach, focus
transitions, and backend switches. Avalonia-routed camera events suppress the overlapping native
sample to avoid duplicate handling. Composition backends continue to use Avalonia routing, and the
toolbar and menu camera commands remain available on every backend.
F, Home, and P execute once per physical press on native and Avalonia paths; platform auto-repeat is
suppressed until the matching release, and focus loss clears held-command state.

## Diagnostics

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

Camera shortcuts are ignored while a text box or combo box is being edited. Controls use logical
markup order, automation names, and theme resources rather than fixed foreground colors.

## Automated evidence

Shared soak and renderer evidence runs do not load or save Viewer settings, enable session or
variant controls, or sample the interactive diagnostics model. Schema 8 camera evidence is fully
automated and does not change interactive camera navigation: it temporarily applies deterministic
managed view/projection matrices, captures bound backend pixels and Storm camera diagnostics, and
restores automatic mode before continuing fallback, loss, quarantine, or switching scenarios.
On Windows it additionally delivers a real Win32 Alt-left drag to the Storm child, polls the ABI-7
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
