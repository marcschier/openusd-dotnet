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
    StageReadyAsync = (session, cancellationToken) =>
    {
        // Author into the viewer's own stage while it renders.
        _ = Task.Run(() => PumpAsync(session.Scheduler, cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }
});
```

`StageReadyAsync` runs once on the UI thread after the startup stage is open and the render loop is
running. It receives a `ViewerStageSession` exposing the `UsdStageScheduler` that owns the stage.
A host **must** author only through that scheduler and must never reopen `StagePath`: a second open
creates a second native stage identity and breaks authoring/render synchronisation. Edits made through
the scheduler flow into its ordered change feed, which the viewer already pumps, so they invalidate and
redraw without any further call. Blocking the callback stalls the UI thread, so start background work
and return promptly; a failing callback is reported as a viewer error and never tears the shell down.

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
- arrow keys: orbit by 5 degrees while the viewport has keyboard focus

Camera shortcuts are ignored while a text box or combo box is being edited. Controls use logical
markup order, automation names, and theme resources rather than fixed foreground colors.

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
