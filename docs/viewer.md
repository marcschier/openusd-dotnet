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

## Authored RenderProduct captures

**File > Render Authored Product...** and the command palette open a Viewer-owned dialog for bounded
UsdRender product execution with the matching hdSilk session ABI 6 runtime. This source workflow
is not part of the published `0.14.0-alpha` runtime.
The dialog reads the selected `UsdRenderSettings` without authoring,
keeps unsupported products visible with their refusal reason, and renders only products admitted by
the shared `RenderProductJobPlan` profile. Catalogs above 256 products are refused explicitly rather
than silently truncated; select a smaller settings set to use this bounded dialog. Static admission
rules are shared with the job planner, while sampled camera and backend checks run before capture.
The output parent must be an existing local folder chosen
by the operator; opening, refreshing, cancelling or closing the dialog does not create directories.
Each job publishes a generated subfolder only after all frames, sidecars and the manifest are
complete, and authored `productName` values are never treated as filesystem authority.

The capture uses the authored product camera, raster, variables, scene purposes and exact
material-binding purpose rather than the current viewport camera or dimensions. PNG files are
explicit display companions. Raw HDR product variables are top-down RGBA16F, EXR is available only
when the current encoder restrictions admit the product window, and depth variables are top-down
float32 normalized device depth rather than metric distance. Windows Viewer execution covers
hdSilk Direct3D 12 and Vulkan. Both capture on the active composition device, with an exclusive
lease that prevents presentation, device replacement or teardown while capture resources are
owned. Failed resource cleanup retains that lease for retry. Native Storm has no authored-product
binding. The same managed adapter is wired for Metal, but native macOS capture execution remains
unverified; this is not a Metal support claim.

After a successful job, **View results** opens the read-only
[completed-job contact sheet](#previewing-completed-render-jobs) using its PNG display companions.
It does not interpret the raw HDR/depth planes or rerun the product.

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

`PickTarget` is a non-nullable `RenderPickTarget` that defaults to
`RenderPickTarget.Primitive`, and it is a fixed concrete request: the host receives that target for
the lifetime of the shell, whatever the operator later chooses under `Tools > Pick Target`. A host
that drives its own selection from prim paths therefore needs to do nothing at all -- the default
already protects it from a Tools-menu change delivering face indices.

A host that wants the operator's choice instead sets `FollowViewerPickTarget = true`, which is the
only way to ask for it; `PickTarget` is then ignored. The mode is a separate property rather than a
null target so that stating the default target stays expressible and stays distinct from stating
nothing. Every callback reports what was actually asked for in
`ViewerPickEventArgs.RequestedTarget`, so the mode is observable from inside the callback.

Running the Viewer standalone from the command line is a third case, and it is not the same as a
host that leaves `PickTarget` at its default. There is no host holding a fixed target on anyone's
behalf, so the operator's `Tools > Pick Target` choice is the only request that exists and it is
what every viewport click resolves. `ViewerStartupOptions.ResolveRequestedPickTarget` is the single
seam every pick path goes through, and the mode it consults is set by which initializer ran --
`Initialize(string[])` for a command-line run, `Initialize(ViewerHostOptions)` for an embedded one --
rather than by comparing the target against `Primitive`. That is what keeps "no embedding host" and
"a host that explicitly asked for `Primitive`" separable: the first follows the menu, the second
never does.

`ViewerPickEventArgs.Item` carries the complete resolved `SelectionItem` for a hit, including
`ElementKind` and the full ordered `InstancerContext`. The flattened `PrimPath`, `InstancerPath`,
`InstanceIndex`, `ElementIndex` and `ElementKind` properties remain, and are convenience views of it.
`InstancerPath` and `InstanceIndex` name the *innermost* instancing level, which is the whole truth
for the overwhelmingly common single-level scene; a host that cares about nested instancing must read
`Item.InstancerContext`, which is ordered outermost to innermost and is the only description that
decodes back to a scene instance. `Item` is null for every non-hit result, so a host can never
mistake a stale identity for a fresh one. `ElementKind` on a hit is always a concrete
`Face`/`Edge`/`Point`: `RenderPickResult.Hit` resolves an item that states no kind -- what
`SelectionItem`'s legacy four-parameter constructor produces -- against the request's own target
before publishing it, so a status line never has to guess what a bare index names.

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

Hierarchy acquisition now uses one bounded `UsdStage.GetHierarchySnapshot(UsdHierarchyLimits.Viewer)`
query, followed by projection of its detached result, rather than native calls for every prim.
The native all-prim traversal preserves inactive, undefined and abstract classifications, child
order and shared prototype identity. Prototype filters cover both prototype roots and descendants.
The snapshot records its stage change serial; quota failures are reported rather than presenting a
partial tree. Viewer ceilings are one million prims, 128 MiB of hierarchy/source-admission text and
depth 1,024, with separate variant and metadata-work bounds.

Ordinary USDC hierarchy rows are supported. Where variant metadata cannot be admitted without
unbounded deferred deserialization, the row is explicitly marked **variants deferred** and has no
partial inline selector. This does not claim that deferred variant expansion is implemented.
Long display names and tooltips are bounded independently; canonical path and selection identities
remain unchanged.

Wide hierarchy levels use **Previous / Next** pages of at most 64 sibling prims. Root navigation
appears below the filters; expanded branches with more children show their own page range and
navigation. Paging does not clear the selected prim or change the renderer's selection. Path/name,
type and state filters still retain matching ancestors, and revealing an existing selection chooses
the ancestor pages containing its exact path rather than a similar path prefix.

Automatic **Expand depth** materialization has a 512-prim work budget. Further branches remain
collapsed with an explicit message and can be expanded on demand. Up to 4,096 prim containers are
retained; at that limit, collapse a branch or narrow the filter before opening more. Collapsing a
branch releases its child controls. Revealing selected ancestors may exceed the automatic budget
but never the retained-container limit; a narrow path filter can expose a deep selection with fewer
sibling controls. These are presentation limits, not hidden truncation of the hierarchy or
permission to flatten, unload or edit the stage. Native hierarchy acquisition has its own separate
limits and is not made bounded merely by paging its presentation. Geometry statistics, camera
discovery and full selected-prim property reads are separate operations; the hierarchy query does
not make those operations bulk or complete the remaining inspector scalability work.

Focused property authoring now uses the exact review-layer history described under
[document editing](#document-editing-foundation). Older prim-metadata controls remain read-only
until their target-local authored-state representation is supported; they must not bypass that
history through the old session/root writer. Load/unload remains available: it changes load rules,
not layer opinions. The Layers edit-target label still reports the stage's raw current edit target.
Selecting the root target does not redirect the focused editor or grant permission to save source.

Each variant set shows its available names, current selection, and an explicit **no selection**
option. Variant authoring is currently read-only because the exact native property-field seam does
not represent variant selections. A composition refresh preserves time and the selected path when
it still exists; if recomposition removes the selected prim, the Viewer clears the selection.
Enumeration failures are shown as errors rather than being presented as empty sets.

Payload arcs are read-only. The Viewer displays a bounded authored asset path, the authored target
or a target-layer-default-prim marker, and the source-layer identifier. Relative asset paths are
labeled as relative. Anonymous source layers are labeled anonymous and process-local because their
identifiers are not portable. Existing load/unload controls remain available; the Viewer does not
offer payload add/remove authoring.

### Searchable property pages

The Properties, Value, and Metadata tabs share a **Find properties by name or type** filter and
**Previous / Next** navigation. Search is case-insensitive; `relationship` finds relationship rows.
Each page contains at most 32 attributes and relationships combined, in their snapshot order, with
an explicit result range and empty-filter state. All matching properties remain reachable; paging
does not truncate the result set or change the selected prim, authored opinions, or layer targets.

Filtering and paging reuse the detached inspector snapshot rather than reading the native stage on
each keystroke. A refresh retains the query and current page where possible; selecting another prim
starts at its first page. Property controls are unavailable while a new snapshot is loading.
On prims with more than 256 attributes, the focused review editor receives only the current page,
so its existing bounded declaration selection no longer prevents editing a property on a large
prim. Exact review-layer capture, conflict checks, and shared undo/redo still govern every edit.

The Value tab keeps the property name, composed state, value preview, and **Edit Review** action
visible. **Source, samples and assets** is collapsed by default; opening it creates controls from
the cached snapshot, without another native read. Only one property's details are expanded at a
time, and collapsing releases its detail controls. The chosen disclosure survives same-prim
refreshes and undo but resets on a different prim. Missing assets remain visible as text warnings
even when their details are collapsed.

### Bounded values, provenance, and inspection time

The selected prim's attributes and relationships now come from one owned native property query,
not an attribute-by-attribute series of value, sample, and target reads. The Viewer admits at most
65,536 complete property rows, 16 MiB of aggregate native text, and 1,048,576 metadata work units.
Value, sample-time, and target previews contain at most 16 entries each, with a 4,096-byte native
text limit per value preview element. A row-inventory quota failure is an inspection error, not a
silently incomplete property list. These are property-query limits, not a CPU/RSS sandbox for
arbitrary plugins or a bound on every other composition, variant, and spline inspection operation.

Stored composition errors on the selected prim, its ancestors, or contributing layer stacks cause
an inspection error rather than a misleading empty or partial property list. Repair missing
references or layers and reload before retrying. An error on an unrelated prim does not, by itself,
prevent inspection of a valid selection.

The Value tab distinguishes composed value availability, default/sample/fallback resolution,
authored declarations, and authored value opinions. **Unproven** is different from **No**. A winning
value block remains visible even when the schema supplies a fallback value. Proven winning layer
and property-spec paths are shown in their source namespace: a local declaration does not
incorrectly replace provenance from a referenced layer. Inspection is read-only; exact review-layer
capture and compare/apply remain the authority for edits and undo.

Arrays, times, direct shader connections, and relationship targets show bounded prefixes, known
counts, and explicit **Complete**, **Truncated**, **Deferred**, or **Unsupported** states. Unknown
counts are labeled unproven rather than zero. Graph targets are not followed or rewritten, and a
shader connection may coexist with an ordinary USD default value. Asset previews expose the raw
authored path, evaluated path when available, native resolved path, anchor layer, and resolved,
missing, or unproven status. Visible fields are shortened with an ellipsis; a property's detail
block is capped at 8,192 characters and explicitly reports omitted details.

Inspection initially uses **Default time (not timeline)**. **Current frame** takes one snapshot at
the requested timeline time, labeled **Time N snapshot (not live)**. Moving the timeline or playing
animation does not issue property queries or silently change that snapshot; click Current frame
again to resample. **Default** returns to USD default-time values. Search and paging stay cached;
ordinary property edits and undo refresh the selected time without changing the chosen mode.

Ordinary USDA values and common USDC scalars are supported. Lazy crate arrays/path lists,
value-clip evaluation, array edits, asset expressions, and other unadmitted materialization retain
explicit deferred reasons; they are not empty values. The existing bounded spline preview below
is separate from composed value evaluation. Broader schema-driven controls and bounds for other
inspector collections remain separate work.

### Ts splines in the Value tab

An attribute with an authored [Ts spline](data-api.md#ts-splines) shows one extra block in its
Value-tab disclosure, below its time samples. The block is a single wrapped text run: a summary line
reporting the authored knot count, the curve family, both extrapolations, and whether the spline is
time-valued, then one line per knot carrying the knot time, value, an authored pre-value when
present, the next-segment interpolation, and both tangent widths, slopes, and algorithms.

A final line shows a small evaluated preview. `TsSpline` owns a native handle and can never leave
the stage scheduler, so the snapshot builder evaluates the spline itself while it still holds
stage access: nine uniformly spaced samples covering the authored knots plus a margin of ten
percent of the knot span on each side, so both extrapolation regions are visible. A single-knot
spline uses a fixed margin of one time unit. A sample that lands in a value block is labeled as
one rather than shown as zero. A spline with no knots, with a non-finite first or last knot time,
or whose knot span cannot be sampled without overflowing to infinity, is shown with no evaluated
line instead of a fabricated or non-finite one.

Three bounds apply. Native property metadata identifies spline candidates without probing every
attribute. The snapshot builder attempts at most 16 spline reads per inspector, before acquiring
individual attributes or calling spline APIs. Splined attributes past that budget are still listed
and are labeled `not read (...)`, naming the budget, rather than dropped or fabricated. Each
projection that *is* read retains at most 32 knots. A single expanded property's detail block has a
64-knot-line budget and is released on collapse, so many splined attributes do not materialize
many knot controls. A block that runs out of budget says how many knots it omitted. The
summary is also appended to the attribute's row in the Inspector tab, so a spline is visible
without opening the Value tab.

Float, half, and double splines all project; their values are widened to double for display.
A spline the native runtime refuses to read is reported as `unreadable (<reason>)` on that one
attribute, because failing the whole inspector over one bad attribute would lose every other one;
when the runtime reports no reason, the label still names one. `unreadable` and `not read` are
distinct: the first was attempted and failed, the second was never attempted.
Splines are read-only in the Viewer; there is no knot authoring.

The spline projection is a detached snapshot with value equality, so a caller polling an unchanged
stage can compare and diff spline payloads. The Value tab rebuilds its bounded summary rows on
inspector refresh and lazily recreates only the currently expanded detail block.

## UsdValidation results

The Validation tab runs the OpenUSD validation registry and shows what it reported. Both entry
points run inside the stage scheduler callback, so no `UsdStage` or `UsdPrim` ever reaches the UI
thread; the tab receives a detached `ViewerValidationSnapshot` with value equality.

A scope selector chooses what a run covers:

- **Whole stage** runs every registered validator over the stage.
- **Selected prim** runs the prim validators over the selected prim only, resolving the prim from
  its path *inside* the scheduler callback. With no prim selected, nothing is scheduled and the tab
  says so rather than running the stage under a label that claims one prim.

Changing the scope re-runs immediately, because results carry the scope they were produced under
and showing old results under a new label would be a lie. The state line always names what the run
actually covered: `UsdValidation (whole stage): ...` or `UsdValidation (prim /World/Cube): ...`.

Results are bounded. A snapshot retains at most 200 results and each retained result keeps at most
512 characters of message and 256 of site list, because the snapshot is copied out of the scheduler
and rendered as text on the UI thread. The retained window is taken from the results ordered by
severity rank - error, warning, info, then anything else - and the ordering is made stable by the
original position, so within one severity it is the order `UsdValidation` already made stable and
two runs over an unchanged stage retain the same window. A severity this build does not recognize
ranks last but is still ranked, so it is retained and shown rather than dropped.

Truncation does not change the counts. The reported total and the per-severity counts are taken
over the whole run before truncation, so a truncated view still says how many results exist; the
state line adds `Showing the 200 most severe.` and the detail list ends with how many were not
shown. Results whose severity is neither error, warning, nor info are counted and reported as
`Unclassified severity: N` rather than folded into the informational count. What is bounded is what
the snapshot retains and displays, not what the run found.

The validator count in the state line is the number of validators **registered** when the run
started, not the number that executed: a prim-scoped run only executes the prim validators among
them.

Every state the tab can show - not run, running, completed, no selection, failed, cancelled - is
part of the snapshot, and rendering is a pure function of it. Nothing is written directly to a
label, so no transient message can survive into a later render that contradicts it. A run is also
tagged with the document generation it started in: closing, reloading, or switching the stage
invalidates in-flight runs and waits for them, and a run that finishes after its document is gone
is discarded instead of being published into the next one.

Validation re-runs automatically on stage open, on reload, and after any layer, prim, or hierarchy
command that refreshes the document; **Refresh** re-runs it on demand and changing the scope
re-runs it immediately. Changing the *selection* alone does not re-run it, so prim-scoped results
stay labeled with the prim they were produced for until the next run.

Two snapshots compare equal when they describe the same run outcome: state, scope, counts, and
retained results. Wall-clock `Duration` is deliberately excluded, because including a measured
time would make every poll of an unchanged stage compare unequal.

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

Camera-menu controls provide **Reset Automatic**, **Explicit Legacy Pose**,
**Toggle Projection**, **Use Selected Camera**, and **Frame Selected**; **Frame Selected** also
stays in the compact always-visible toolbar row. The Camera menu's **Orbit** submenu orbits left,
right, up, or down by 5 degrees per activation; the same four directions are bound to the arrow keys
while the viewport has keyboard focus, so orbit remains reachable with the viewport uncluttered by a
dedicated button cluster. The status area reports
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

**Saved views.** **Camera > Saved Views...** opens an owned, themed manager for adding the current
camera/time, renaming, removing and recalling named views. The manager is also searchable in the
command palette using “saved views”, “add”, “rename” or “remove”. **Camera > Recall Saved View**
and palette searches for a view's name recall that view directly. Dynamic commands use stable
bookmark IDs, not names or filesystem paths; underscores in labels remain literal.

Saved views are **review data**, not global settings or a settings sidecar. The reserved review-only
address is `/__OpenUsdViewerReview.openusdViewer:cameraBookmarks`, a custom uniform `string[]` of
versioned records. The catalog admits **32 views**, **1–128 UTF-8 bytes per trimmed name**,
**4096 UTF-8 bytes per record**, and **128 KiB including array/string framing**. Names are unique
ignoring case. Namespace collisions, incompatible declarations, unknown versions, duplicate IDs,
malformed/oversized records and source-context mismatches are refused without truncating or
overwriting the stored list. Reads use the actual registered review layer, not composed source values.

**Add Current View** captures a finite USD time, exact matrices and aspect, and either the validated
free perspective/orthographic state or the full sampled authored-camera identity. It does not capture
render/display settings, selection, simulation or layout. Pause playback before adding a view.
Pause waits for the last queued time and authored-camera update before enabling manual time edits
or saved-view capture, so a late playback tick cannot change a supposedly paused view.
Source notices coalesce with the newest pending time sample; a notice sampled before a time
change is refreshed at the current time rather than restoring an older camera sample.
Automatic is not an exact pose/projection and cannot be saved as one: use **Frame Selected** or
**Explicit Legacy Pose** first. Unrepresentable/custom camera states are not silently approximated.

Exact recall requires the captured aspect; resizing to another aspect is not silently adapted.
An authored-camera view re-queries its recorded path/time and compares the full sample before
activation. A missing, changed, non-camera or inactive prim leaves the current view intact instead
of falling back to Automatic. After a successful authored-camera recall, normal later playback
and source updates continue to follow that camera. Recalling a free view leaves stage-camera mode.

Recall is navigation, not a review edit or an undo entry. It drains competing playback/gesture/camera
work, checks backend acceptance, and commits matching renderer and logical/UI camera/time state.
Success leaves playback paused. Cancellation or a pre-commit failure restores the prior view and
playback mode; resizing cancels an in-flight recall. Reload and close cancel/drain the operation
before retiring the document, so a closed manager cannot act on a replacement stage.

Add/rename/remove are single exact whole-list transactions in the shared review Undo/Redo history.
Disjoint review edits are preserved; competing changes to the catalog produce a conflict rather
than a merge or overwrite. Cancelling a name edit makes no opinion/history change. An added or
changed view remains **unsaved** until **Save Review Document** or **Save Review As...** succeeds.
The existing native URD, source/dependency verification, late-edit acknowledgement, recovery,
reopen/revert and Save/Discard/Cancel protocols apply. Reopening restores the catalog with fresh
history, but does not automatically recall a view. Verified Windows text-USD review sources are the
supported persistent domain; unverified session-only, crate/package and unsupported-resolver
documents do not advertise persistent saved-view authoring. A stored source stamp is never
permission to open another file.

The native `camera-bookmarks` workflow covers actual review-property/URD round-trip, exact shared
history, manager/palette operations, free/authored recalls, bounds/stale/source/ownership refusals,
failed/cancelled acceptance, playback restoration, and pending Reload/Close. Its public renderer
boundary failure checks supplement actual D3D12 view and pixel comparisons, not replace them.

The Render menu's Draw Mode submenu exposes the usdview draw-mode ladder: Wireframe, Wireframe on
surface, Smooth shaded, Flat shaded, Points, Geom only, Geom flat, Geom smooth, and Hidden
surface wireframe. The statistics HUD displays stage AABB and OBB values from the world-bounds
query capability at the stage start time.

The Render menu's Colour Management submenu is the whole colour-management control surface: one
"Use OpenColorIO display transform" toggle, one "Choose OpenColorIO Config..." file chooser, and
one "Clear OpenColorIO Config" item. Nothing is added to the toolbar. The toggle is disabled until
a config is available, and a config is available when one has been chosen or when the standard
`OCIO` environment variable names one, so an environment already configured for colour-managed
work needs no Viewer configuration at all. The chosen config path is shown in the toggle's
tooltip.

Enabling the transform sets `RenderSettings.DisplayTransform` on the authoritative
`StageRenderState`, which is the same path lighting, shadows, and background colour take, so the
transform reaches live presentation and ordinary captures without a separate presentation-only
code path. Every viewport mutation copies the settings through one shared helper, so toggling
lighting, shadows, culling, materials, the background, the draw mode, or a purpose can never drop
the transform. Selecting a display transform moves the built-in output transform to `Identity` and
clearing it restores the presentation default, because applying both would colour manage the same
image twice.

Enabling is validated, not assumed: the Viewer bakes the requested transform before claiming it,
so a missing, unreadable, or incomplete config turns the toggle back off and reports the exact
reason in the status line rather than leaving an untransformed image that looks like a successful
one. Validation alone is not enough either, because a config can be deleted or edited while the
Viewer is open, so while colour management is enabled the Viewer also polls the active backend's
display-transform diagnostics twice a second; a renderer that fell back clears the transform from
the authoritative state, unchecks the menu item, and reports the renderer's own bounded reason.
Every backend session reports those diagnostics, not only the hdSilk ones: a Storm viewport has no
fullscreen display-transform pass, so it reports `UnsupportedDevice` for a requested transform and
the same reconciliation disables the toggle. The D3D12, Vulkan, and Metal presentation renderer
adapters each delegate their diagnostics to whichever `SilkMeshRenderer` is presenting at that
moment rather than accepting the interface's inactive default, so a composition session sees the
renderer's real refusal instead of a success-shaped silence. The Viewer never leaves the item
checked while the image is untransformed.

Requests are versioned and serialized. Baking a lattice takes long enough that a user can toggle
the setting or choose a different config while an earlier request is still running, so each request
takes a version, cancels the one before it, and discards its own result after every suspension
point if it is no longer current. Enabling config A and then disabling cannot end with A applied
because A's bake happened to finish second. Cancellation is advisory -- a bake already inside
OpenColorIO cannot be interrupted -- so correctness rests on the discard rather than on the token.

A pending selection is kept separate from the committed state. Pending is a generation comparison,
not a count of running operations: a bake already inside OpenColorIO cannot be cancelled, so a
superseded request can still be running long after the request that replaced it committed, and
counting operations would suppress every diagnostic about the request that is actually live --
including its own failure. Once the newest generation commits, nothing older can make the state
pending again. Reconciliation is also correlated: the renderer publishes the cache key of the
transform it evaluated, and a report whose key is not the committed one is ignored. Without that, a
slow failure for config A observed after config B was committed and applied would disable a
transform that is running correctly. The one combination the reconciliation never leaves standing is
the inverse: a committed transform while the menu and the persisted settings say colour management
is off is cleared rather than tolerated.

Nothing is committed until the coordinator confirms the mutation. `RenderBackendManager` and
`ViewerRenderCoordinator` are both transactional: the backend is told first, and the retained and
published state advances only once it accepted. All three coordinator paths that write the
authoritative state -- the computed mutation, the direct replacement, and the live stage-change
pump -- follow that order, so a refused state is never published, never raises `StateChanged`, and
never announces a `StageChanged`.

A backend that threw or was cancelled part-way through applying a state is not merely rolled back
from: it may have applied some of the state and not the rest, so it is discarded and a replacement
of the same kind is built from the last accepted state before the failure is reported. Recovery
deactivates that backend through `IRenderBackendActivationControl` *first*, then disposes it, and
activates a replacement only once one of the two proved the old presenter stopped -- a successful
deactivation, or a successful disposal, which tears the presenter down with it. When neither holds,
the backend is retired and the manager is left with none: two presenters on one surface is a worse
outcome than a reported recovery failure, and retired cleanup runs later and may never run at all,
so it can never be the thing that deactivates. Recovery deliberately ignores the caller's
cancellation token, because abandoning it would leave the manager with no backend at all rather than
one holding a known state. The next frame therefore renders the last accepted state rather than a
half-applied one, and a later failover hands the replacement backend that same state rather than the
one that had just been refused.

The run-time reconciliation commits the same way. Its repair goes through the transactional
mutation, and the disabled model, the cached key, and the persisted settings move only after the
coordinator publishes a state with no transform. A busy window, a cancelled lifetime, or a backend
that refuses leaves the previous commit exactly as it was, records a generation-tagged deferral, and
leaves the poll loop armed so the repair is attempted again.

A request made while there is no coordinator, while a document change is in flight, or against a
backend that refuses is *deferred* rather than applied or dropped: the four views keep agreeing with
each other, and the deferred choice is folded into the render settings the next document opens with.
A deferred request carries the pipeline generation that produced it, so a later request replaces it
outright instead of being overwritten by a stale replay, and an open replays it only while it is
still the newest. The drain compares the deferred generation against the pipeline's current version
immediately before replaying: a superseded deferral is discarded outright rather than entered into
the pipeline, because starting a request cancels whatever is validating, and that would be the very
request that superseded it. An open marks only the generation its opening choice actually
represents: without a matching deferred result it carries the prior committed generation forward, so
a request whose validation is still in flight stays pending instead of being declared decided by an
open that never saw it. Nothing is marked committed by building the opening settings -- the opening
choice is held aside and becomes the committed one only after `ViewerRenderCoordinator.OpenAsync`
has returned a coordinator, with the committed key read back from that coordinator's published
state. Requests that outlived the open are drained after the window reports ready, because draining
while it is still busy would defer them straight back. The commit decision reads the committed view
after its own await rather than from a snapshot taken before it, so a request that ends up deferred
cannot roll back a newer choice an open committed while it was validating.

The colour-management commit uses the state the coordinator published rather than what was
requested. The menu's checked state, the persisted choice, the cached transform key, and the
transform in `StageRenderState` therefore move together or not at all.

The reconciliation poll is a tracked, cancellable task tied to the window's lifetime, not an
`async void` timer tick. Closing cancels it and awaits the tick that may be in flight before
anything that tick touches -- the coordinator, the settings store, the lifetime token -- is
disposed. Disposal paths that never went through closing cancel it without waiting, because
disposal runs on the very dispatcher thread the tick marshals back to.

The persisted choice is restored into the render settings a newly opened stage is initialized
with, not merely into the menu, so a restart or a newly opened stage presents its very first frame
through the restored transform. A restored choice is validated exactly as an interactively chosen
one is -- it is baked before it is allowed into the opening state -- so a config that was deleted,
replaced, or made incompatible while the Viewer was closed disables the setting and reports why
instead of briefly claiming an active transform that the first frame then contradicts. That bake
runs on a worker and is awaited rather than waited on, so it never blocks the dispatcher thread
its own continuation needs.

The opening choice is generation-checked, because a document open is not instantaneous. The choice
is captured, then the open suspends twice: once to bake the restored transform, and again while
`ViewerRenderCoordinator.OpenAsync` creates the coordinator. A View &gt; Reset Layout clear that
lands in either window takes a newer pipeline generation while committing nothing -- the document is
busy, so its mutation is refused and the clear is merely deferred -- which leaves the committed
model, the cached key, and the deferral the open already consumed all still describing the world
before the reset. The generation is the only trace of it the open can see, so the capture is checked
against the pipeline's current version at both points. A capture superseded before the coordinator
exists is dropped and the opening settings are rebuilt from the newest generation, so no coordinator
is ever created with a transform the reset has already declared gone. A capture superseded while the
coordinator was being created commits nothing at all -- not the model, not the cached key, not the
generation, and not the reset's deferred clear -- and the transform it opened with is taken back out
of the authoritative state before the render loop starts, which is the last moment it can be removed
before a frame carries it. Comparison is against the newest generation observed at capture rather
than the generation the open would commit, because those differ whenever some other request's
validation is still in flight, and using the latter would rebuild every such open forever. If
requests keep arriving faster than the choice can be rebuilt, the open falls back to opening with no
display transform, which is the one choice that can never present a superseded enable; whatever is
newest is still in the pipeline or deferred, and the drain applies it once the window reports ready.
A clear the backend then refuses at confirmation commits nothing either: the previously committed
choice, menu, and cached key stand, the deferred clear is retained, and the reconciliation poll loop
stays armed, so the repair is retried rather than claimed.

Only paths and names are persisted: the config path and the colour-space, display, view, and look
names, under the `colorManagement*` keys of the Viewer settings file. No credential or token is
ever written there. The config path must be absolute; a relative one is rejected with a reported
reason rather than resolved against whatever the working directory happens to be.

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

Native Storm child-window camera input is polled from the child ABI-9 navigation v2 snapshot at a bounded
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

The Viewer keeps the physics transport strip out of the way until it is relevant: the strip is
hidden entirely while physics is off, and once **Physics** (in the Physics menu) builds a world, the
strip shows only Play/Pause, Stop, Step, the scrubber, and the status line. Loop, Speed, Apply
Preview, Bake, Gizmo, Snap, Undo, and Redo all moved into the **Physics** menu, alongside **Physics**
itself and **Show Physics Inspector**, so the always-visible strip never grows past the controls
that are meaningful every time physics is running.

| Control | Location | Behaviour |
| --- | --- | --- |
| Physics | Physics menu | Builds the world; once built, the same command rebuilds it. |
| Play / Pause | Transport strip | Paces the world forward in wall-clock time. |
| Stop | Transport strip | Returns to the authored start, clears the preview, and restores authored transforms. |
| Step | Transport strip | Advances exactly one fixed simulation step while paused. |
| Loop | Physics menu | Wraps to the authored start at the authored end. |
| Speed | Physics menu | `0.25x` to `4x`. |
| Apply Preview | Physics menu | Authors the simulated poses into the session overlay. |
| Bake... | Physics menu | Opens the bake dialog. |
| Gizmo | Physics menu | Chooses what a viewport drag manipulates: nothing, move, rotate, scale, or drag a body. |
| Snap | Physics menu | Quantizes gizmo drags to the configured translation, rotation, and scale increments. |
| Undo / Redo | Physics menu | Reverses or replays the newest physics property edit. |
| Scrubber | Transport strip | Seeks within the authored start/end range. |
| Show Physics Inspector | Physics menu | Selects the Physics inspector tab. |

A **Physics** build is transactional: the new world is built before the live one is released, so a
rebuild that fails or is cancelled leaves the world that was already playing intact and paused rather
than dropping the session into an empty state. Only a first build that fails leaves the transport
faulted, and a faulted transport refuses to play.

Build, reset, seek, step, and invalidate all run asynchronously on the stage scheduler and the
physics worker. Nothing simulates on the UI or render threads, and no USD or PhysX handle crosses
the worker or render boundary: the controller exchanges only time codes, whole step counts, and
renderer-neutral snapshots.

Speed scales how much wall-clock time playback accepts, never the fixed simulation step. Scaling
the step would change what the solver computes, so a user who slows playback down to inspect a
collision would be watching a different simulation than the one that gets baked. The status line
reports state, current time code, step index, backlog, and queue depth.

Playback is paced by the Viewer rather than by a free-running transport: the controller converts
elapsed wall-clock time times the speed into whole fixed steps and requests exactly those. The
accumulator is bounded, so a stalled shell slows playback down instead of asking the worker for an
unbounded catch-up burst.

The transport strip never clips a control. Every command still shown in the strip is either at its
full width or moved into the **More physics controls** overflow menu, in authored order.

Playback repaints the status line about as often as it steps, but only the fields that changed are
written. Paced steps never mark the transport busy - only a command the operator issued does - and a
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
- *read only* — this panel's existing scalar projection admits `bool`, `int64`, `double`, `string`, `token`, and
  `float3` scalars only, matched exactly. Stock `UsdPhysics` masses, frictions, break forces, joint
  limits, and joint drives are `float`; centres of mass and joint frames are `point3f` and `quatf`;
  velocities are `vector3f`. Those rows still show their extracted value, because hiding them would
  say the scene has no such setting.

Production physics authoring now uses the same scheduler-owned exact review controller and history
as ordinary property edits. Native capture supplies the selected layer's actual before-state; the
old scalar `Before` value is not trusted as an undo proof. A typed batch uses affected-field native
compare-and-apply, rather than a series of independent scalar writes. Float-vector overflow and
non-finite values are rejected before the batch is submitted. Direct target-layer operations leave
the stage's current edit target unchanged. Strong simulation, user-review and source layers remain
separate. The old scalar adapter/history remain only for callers without the shared document editor.

The history owns immutable edit collections and retains only the first before-value and final
after-value of a continuing single-property gesture. A changed intervening before-value breaks
coalescing. The shared history engine bounds both entry count and retained payload bytes; oversized
legacy physics steps are rejected before authoring, and eviction affects reach rather than stage content.
The default bounds are 128 steps and 32 MiB of retained payload accounting. Failed history travel
restores the retained entry rather than accepting a replacement payload.
The shared engine also accepts explicit gesture identities, so a new document-editing drag can
remain one step across pauses without merging a later, separate gesture.
Its pending-application API reserves undo/redo without moving the cursor; rejected or cancelled
application keeps the entry, and document replacement invalidates any old reservation. The exact
native document adapter uses that API and commits the cursor only after native CAS succeeds.

The legacy physics reader reports composed values and composed `IsAuthored`; that is not proof of
an opinion in the chosen target layer. In particular, a weaker root opinion can make `IsAuthored`
true while the session target contains no prim at all. General conflict-safe document undo cannot
use that reader as its before-state source.

The Viewer recognises its own authored changes exactly, by the change-serial pair that brackets
them, and replaces the caller's conservative classification with what it actually authored. Editing
a mass therefore pauses playback and rebuilds the world once the edit burst goes quiet, while
editing simulation metadata - which is provenance, never an input - does not disturb a running
simulation at all. A change with different serials is never treated as the Viewer's own.

### Document editing foundation

The bounded current-session editing and Windows portable-review workflows are live; the complete
document-editing roadmap is **not complete**. Current sources require a matching native data
runtime, authored-layer transactions and native portable-review inspection. There is no fallback
to composed values, private packet parsing or an older data shim.

**Edit > Edit Selected Property** (`Ctrl+E`) and the Value tab's **Edit Review...** action open an
owned, themed window. It supports existing local declarations of `bool`, `int`, `int64`, `float`,
`double`, `string`, `token`, `asset`, two/four-component float vectors, scalar-first `quatf`,
row-major `matrix4d`, and three-component float/double vectors and their point, normal,
vector and colour roles. Supported aliases include `texCoord2f` and `color4f`.
This includes camera clipping ranges, ordinary translate/scale/Euler/matrix attributes and supported
light/material parameters, not a material graph editor. Unsupported, referenced-only and
schema-only declarations remain read-only instead of guessing their declaration metadata.

Vector, quaternion and matrix input requires the exact number of finite components; float fields
reject values outside the float range. Commas, whitespace, parentheses and matrix brackets/separators
are accepted. Quaternions are scalar-first and are never silently normalized. Matrices retain
OpenUSD row-vector semantics, with translation in the first three values of the final row; no
transpose is applied. The form displays the input convention for the selected type.

Asset properties include **Choose asset...**, which fills in an absolute local path without
authoring anything. **Set** commits that exact asset-path opinion to the review layer; canceling
or failing the picker preserves the previous draft and history. Manually entered paths remain
authored paths, without implicit resolution or rebasing. Native anchor and dependency checks still
govern portable save/reopen. Relinking a texture keeps the existing shader nodes and connections;
shared Undo/Redo restores the earlier review opinion rather than modifying the source material.
Closing the property window cancels a pending choice without applying its eventual result.

The window distinguishes the read-only composed display snapshot from the exact target-layer
opinion. **Set**, **Clear** and **Block** affect only the selected default or individual current-time
sample. Uniform attributes reject sample editing. Other samples and list opinions are not cleared
implicitly. Empty, blocked and absent target opinions are different states. The native history also
round-trips supported connection/relationship list operations, but this window does not expose a
list or spline editor. Conflict and unsupported-state errors keep the document usable and require
an explicit Refresh before retrying.

**Edit > Undo/Redo** (`Ctrl+Z`/`Ctrl+Y`), the palette and the existing Physics undo/redo paths use
one chronological history. Actual returned native snapshots become the next replay precondition.
Disjoint external opinions survive; conflicting values, declarations and stale generations are
refused without advancing the cursor. No-op edits retain the redo branch even when a disjoint edit
has advanced the review revision: after native CAS accepts, no-op detection compares target identity
and exact affected declarations/value bytes, not the unrelated revision. Explicit gesture IDs
coalesce across pauses only while the native packets match; unrelated revisions and separate form
submissions split those steps. The retained-payload bounds are 128 entries and 32 MiB. No ordinary
undo restores an old complete layer over unrelated edits.

The property window waits for capture before focusing its input and drains an in-flight operation
when closed. Only an active owned window suppresses owner hotkeys. Text inputs retain their own
undo shortcut, and held document shortcuts are not repeated through the history. No Avalonia
overlay is added above the live native renderer child.

Dirty state identifies the real review target, source and other changed layers. Querying it does
not create a review layer; creating an empty review editor alone does not count as a user edit.
Document-state observation admits at most 256 local layers. Unavailable or unadmitted state keeps
editing read-only and guarded destructive transitions fail closed, rather than assuming cleanliness.
Close, stage replacement and Reload use an owned **Save Review / Discard / Cancel** prompt.
Cancel preserves the usable stage. Layer identity/revision changes during the prompt reject the
decision. Retirement then closes admission to tracked host callbacks, cancels and drains their
returned tasks, and suspends physics document commands as well as the shared review writer.
The shared scheduler then fences all public producer admission, drains previously accepted work,
and compares the retained ticket on its owner thread. New raw `InvokeAsync`/`EditAsync` calls and
render-source/lease acquisitions are refused while that fence is held. A rejected ticket releases
the fence and Viewer barriers without cancelling the live document. On success, a private
retirement lease keeps admission closed through colour polling, settings save and renderer
cleanup; the Viewer commits retirement only after releasing its render registrations. Explicit
Reload uses lease-authorized owner cleanup rather than bypassing the fence through a public call.
Callback tasks must await their own child work, but that does not revoke scheduler references
held elsewhere. If a late write aborts retirement after a stage-ready service was cancelled, the
stage remains usable but that service is not restarted implicitly. New input callbacks can resume.
Non-cooperative tracked host or physics work produces a bounded retirement failure.
Simulation values are excluded from review dirtiness; unclassified session-topology changes are
reported conservatively. Reload with Discard reopens the source with a fresh review/history and
the normal new-document camera/selection/time state. It never saves a source file.

New eligible Windows text-USD documents establish source origin through
`UsdStageScheduler.OpenForReview` **before** review edits or host stage-ready callbacks. The
prepared scheduler is retained for rendering and editing; the shell does not open a second stage
to author through. Crate, package, non-Windows and unsupported filesystem paths remain available
for ordinary viewing with a visible session-only saving restriction. An attempted native
verification failure requires an explicit **Open session-only** decision or Cancel. A previously
opened unverified session is never silently rebound, reloaded or discarded to manufacture proof;
reloading a session-only document preserves that mode.

**File > Save Review Document** (`Ctrl+S`) and **Save Review As...** (`Ctrl+Shift+S`) write native
`.urd` source-linked review documents. They capture the exact native review and its private
same-session receipt on the stage scheduler, bound the portable file to 24 MiB, and acknowledge
saved state only after publication succeeds. The source, its dependency files, authored graph,
root metadata and asset anchor are not rewritten or flattened. Save As does not silently rebase
relative textures or other assets. The named document changes only after actual publication.
Late edits or failed acknowledgement remain dirty and actionable, even if a captured older
revision was successfully published. Saving does not clear the shared undo/redo history.

Publication checks physical source/dependency identities and filesystem ancestors after destination
selection and again under retained guards at publication. Reparse/short-name aliases and source or
dependency hard links are refused. Guarded source reads admit at most 1,024 files, 16 MiB per file
and 64 MiB total, with at most 4,096 retained ancestor directories. Publication uses stable local
volume paths, uniquely created owned staging, flushing, ordinary atomic rename and handle-owned
cleanup; it never deletes a foreign staging path. Same-process publishers serialize by physical
parent identity and destination name.

Replacing a named review uses an **optimistic observed-version check plus atomic publication**,
not filesystem compare-and-swap. A changed observed file identity/content or parent directory
refuses replacement; choosing another existing Save As destination requires an explicit overwrite
decision. An uncooperative review-file rename/replacement after the final check remains outside
that optimistic guarantee. This residual namespace race does not relax the independent retained
source/dependency guards. Create-new Save As stays non-overwriting. Cancellation or I/O failure
before publication preserves the prior output and edits; a completed publication is not rolled
back over later work.

**File > Open Review Document...**, ordinary Open, recent documents and drop accept `.urd` files.
The bounded native `Inspect` call returns untrusted recorded claims, not source access permission.
An owned confirmation names the source before native Read, verified source opening and pristine
import. The candidate is validated before the old stage is retired through the existing scheduler
fence. Source/dependency mismatches require reconciliation; the shell never patches identities or
auto-replays old checkpoints. Successful import has fresh history and the original source remains
the actual stage root.

**Revert Review to Saved** checks that the published file still matches the observed saved
baseline, then imports it into a fresh verified session after Discard/Cancel confirmation. Cancel
keeps the current document and history. Unrelated dirty source/session opinions must be resolved
first rather than silently lost. This restriction is enforced again on the retained transition
state after saved-candidate preparation, so newly unrelated opinions cannot be accepted by a later
Discard decision. Transient simulation is reset rather than persisted.
**Save Source Layer** remains visibly disabled: no generic source-saving protocol is implemented.

**File > Export Review Delta...** is a separate, working escape hatch for supported opinions.
It exports the native bounded review-only USDA payload to a **new** `.usda` file, using a
same-directory temporary file and non-overwriting publication. Existing files, including source
aliases, are refused; cancellation and publication errors leave them intact. The limit is 4 MiB,
and unsupported pseudo-root metadata or ambiguous relative/expression/cached asset relocation
fails before publication. This is neither flattening nor a self-contained review document: it
does not include source composition, reopening metadata or transient simulation. Its ordinary-spec
payload can include the review's serialized saved-view property, but this does not make the delta a
self-contained saved-view document or acknowledge a saved review; use URD for verified reopening.
It does not acknowledge a saved revision, clear dirty state, or enable Revert to Saved.

Save routing always selects the review target for Save/Ctrl+S, regardless of the current edit
target. An anonymous review or Save As requires a destination and asset-anchor validation.
The source-save policy still requires explicit source-edit opt-in, but does not enable a live
source-saving adapter. A simulation layer can never substitute for review. A named review
destination must not alias the source or a dependency, regardless of the raw stage edit target.
The save/transition policy cannot discard separately dirty source or other-layer opinions after
saving only review. The delta exporter remains a distinct create-only operation.

Verified review changes schedule a coalesced recovery checkpoint after a 750 ms quiet interval.
The cache holds at most 8 MiB of native URD payload plus bounded metadata and a SHA256 checksum;
larger or unsupported checkpoints report recovery unavailability without discarding in-memory
edits. It is keyed by the opened source or named review path and stored below the Viewer settings
directory's `review-recovery` folder. Native capture receipts are not serialized or acknowledged
for a recovery write. Recovery cache publication uses the same independent physical guards and
owned staging rules.

On opening, the cache envelope, source identity/fingerprint, review lineage and asset anchor are
checked, and a fresh native import must validate all dependencies **before** recovery is offered.
Only explicit **Recover review** installs that prepared candidate, as unsaved review work with
fresh history. Cancel keeps the current document; explicit checkpoint discard removes only its
validated owned file. A mismatch retains the checkpoint for reconciliation and can open the
selected document without recovery only by explicit choice. Saving or accepted document retirement
removes matching checkpoints safely; a changed or unremovable cache is retained with a diagnostic.
If a transition's Save is cancelled or refused, the kept document's guarded refresh and recovery
schedule resume after its busy/suspension state is cleared, without requiring another edit.
There is no managed USDA/URD-offset parser, per-edit whole-layer rollback, serialized native handle
or persisted undo stack. The cache is not a source-file save destination.
Loading opens a checkpoint directly: only a missing file or directory means no checkpoint.
Access failures, invalid filesystem entries and other I/O errors remain visible to the caller.
File-identity preparation uses a caller-bounded streaming SHA256 read, not just file length or
timestamps. Missing files, read failures, over-budget inputs and cancellation remain distinct.
A fingerprint is an observation, not a write lease or atomic filesystem compare-and-swap.
Portable review support remains bounded to the native Windows filesystem USDA/text-USD profile
with concrete assets/composition. Non-Windows verified opening, crate/custom/URI/package/template/
UDIM/value-clip/inherit/specialize/relocate portability and unverified dirty legacy reconciliation
remain unsupported. Source saving and broader platform/document-editing work remain completion
gaps. Counted review-layer admission into render-specification queries is validated separately;
the Viewer workflow does not claim that broader render-request domain.

Safe late simulation-overlay normalization preserves the registered review-layer identity and
existing history through start, stop and restart. The review must be the unique strongest direct
session sublayer at identity offset. Unsafe container opinions, metadata, topology or permissions
are refused rather than merged or worked around by rewriting history identities. Older undo/redo
continues to operate on the same native review target; genuinely stale or replaced targets still
fail without advancing the shared cursor.
Workspace presets, themes, saved renderer preference, pick/outline intent and OCIO remain
independent of all these document operations.

Ordinary shared review edits and Undo/Redo refresh the anchored Physics properties; history
replay awaits the refresh before authoring controls become available again. Refresh results are
correlated with the physics session, document lifetime and
newest read request; an older completion cannot repopulate a new session. If the operator changes
selection during a read, the fresh rows preserve that latest object/property identity rather than
restoring the read's old selection. The same guard and document gate apply to Physics property
authoring.

The isolated Windows document-window scenario runs with a matching configured native root:

```powershell
$env:OPENUSD_VIEWER_DOCUMENT_SMOKE = '1'
.\eng\run-managed-tests.ps1 -Project tests\OpenUsd.Viewer.Tests\OpenUsd.Viewer.Tests.csproj `
  -Framework net10.0 -Configuration Release `
  -TestArguments @('--treenode-filter', '/*/*/ViewerDocumentNativeSmokeTests/*')
```

It exercises an owned property window, a live Storm owner, menu/palette/shared history paths,
Avalonia keyboard repeat/text-focus guards, close/reload/replacement cancellation, and source bytes.
It is not physical keyboard injection, mixed-DPI, non-Windows, or portable document-reopen evidence.
Focused retirement and Physics-refresh regressions use `ViewerDocumentRetirementNativeTests`
with `OPENUSD_VIEWER_RETIREMENT_SMOKE=1` and `ViewerPhysicsHistoryNativeTests` with
`OPENUSD_VIEWER_PHYSICS_HISTORY_SMOKE=1`, each in a separate desktop test process. The latter
uses real extraction and ordinary review edits; it does not claim a solver is available when the
separately packaged physics backend is absent.

`ViewerPortableDocumentNativeTests`, run alone with `OPENUSD_VIEWER_PORTABLE_SMOKE=1`, exercises
the live textured-source Save/Ctrl+S/Save As, source-confirmed Open Review, Revert/Cancel,
dirty-close Save and explicit validated recovery journey. Use a physical `OPENUSD_TEST_WORK_ROOT`
and the matching ABI21 runtime. This does not claim non-Windows, mixed-DPI, solver execution or
unrestricted source saving.

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

## Menu-first shell

The default window shows one compact, always-visible toolbar row: the **File**/**Edit**/**View**/**Render**/
**Camera**/**Physics**/**Tools**/**Help** menu bar, **Open**, **Commands**, **Inspect**, **Frame**, and
**Capture**. Inspection and capture availability follows the current document and selection.
Reload and renderer selection remain in their menus instead of competing for viewport width.
The status strip shows current document/camera/renderer information and active document, validation,
or capture work; it is not a render-job queue.

- **File**: Open, Review Sample, Recent Stages, Reload, review-delta export, frame capture/comparison, Exit.
- **Edit**: focused review-property editing and shared Undo/Redo. Portable save/revert actions stay disabled.
- **View**: Stage/Inspector/Timeline panel visibility, an Inspector Tabs submenu that shows or
  hides the Diagnostics, Hydra, and TfDebug developer tabs, snap-to-authored-frames, and
  **Reset Layout**, plus **Theme > Follow system / Light / Dark**, command search, Find Prim,
  Inspect Selection, and workspace presets.
- **Render**: renderer backend, draw mode, render purposes, scene lighting/shadows/materials,
  backface culling, and background colour.
- **Camera**: Reset Automatic, Explicit Legacy Pose, Toggle Projection, Use Selected Camera, Stage
  Cameras, Frame Selected, and an Orbit submenu for the four discrete orbit commands.
- **Physics**: enable/rebuild, play/pause, stop, step, loop, speed, apply preview, bake, gizmo,
  snap, undo/redo, and Show Physics Inspector.
- **Tools**: UsdValidation run/scope, pick mode, **Pick Target**, **Selection Outline**, and a
  Developer submenu with a Show Tab toggle
  plus the refresh/copy/export/path-inclusion actions for each of Diagnostics, Hydra Scene, and
  TfDebug, plus a Connections submenu holding the Omniverse Bridge entry. That entry ships hidden
  and disabled and becomes visible only when an embedding host injects a bridge provider; see
  [Bridge connections](#bridge-connections).
- **Help**: keyboard/mouse shortcuts and an About dialog.

### Workspaces and command discovery

**View > Workspace** offers four explicit, lightweight presets, also available through command search
and `Ctrl+Alt+1` through `Ctrl+Alt+4`:

| Preset | Layout |
| --- | --- |
| Review | Stage and timeline visible; inspector collapsed for a larger viewport. |
| Inspect | Stage, Properties inspector, and timeline visible. |
| Materials / Lighting | Stage and Appearance inspector visible; timeline collapsed. |
| Presentation | Side panels and timeline collapsed; menu, essential actions, and status retained. |

Presets change only panel visibility, requested splitter widths, selected inspector tab, and
developer-tab visibility. They do not reload a stage, author opinions, change the edit target,
select a renderer, change desired pick/outline behavior, change the theme or OpenColorIO, reset
timeline snapping, or stop playback. Appearance groups the existing material/lighting display
toggles and OpenColorIO actions; it is not a new USD material editor.

Existing saved layouts are restored as-is, not replaced by a preset at startup. A preset is checked
only while its layout matches; manual splitter/tab changes remain custom layouts. All four presets
hide developer tabs explicitly, but an existing custom layout keeps its developer-tab choices until
the operator applies a preset or Reset Layout. On compact windows the displayed panel widths are
constrained to retain viewport space without overwriting the requested, persisted splitter widths.
Reset Layout retains its separate, broader reset semantics described below.

**Commands** or **View > Search Commands** opens an owned, themed search window. `Ctrl+Shift+P` opens
it from Viewer controls; when Storm's native child owns keyboard focus, use the adjacent Commands
button or View menu. Search matches words in menu paths, labels, accessible names, command IDs, and
gestures, including current recent files and authored cameras. Queries are limited to 128 characters
and results to 64 entries; refine the query to find additional actions.

Menus remain the canonical action/state sources. The palette, contextual buttons and modified shell
shortcuts invoke those same actions, including ancestor enablement, conditional visibility and
current checked state. Unavailable actions remain dimmed in search; conditionally hidden commands,
such as an unconfigured bridge, are omitted. In the search/results controls, Enter runs the selected
available action and Up/Down moves selection. Focused Run and Close buttons retain normal keyboard
activation; Enter on Close never executes the selected action. Escape closes from any control.
The palette closes before dispatching an action that may open another
dialog. Closing restores the prior control or the still-attached native viewport's actual focus.
Typing in the active palette cannot drive camera or physics shortcuts. An open but inactive
modeless palette does not suppress input after focus returns to the Viewer or native viewport.

The review path is **Open > Find (`Ctrl+F`) > select > Frame (`F`) > Inspect > Capture
(`Ctrl+Shift+C`)**. Find reveals the existing hierarchy search without clearing its filters; Inspect
reveals Properties without resetting other preferences. Undo/redo remain available in Physics,
command search, and the existing `Z`/`Y` bindings when their current state allows them.

### Welcome and recent scenes

With no document, the welcome surface offers Open, existing recent scenes, a drop target, command
search, shortcuts, comparison, and a tiny bundled review sample. Open, recent, sample and drop actions
all use the existing stage-open path. The recent list is still bounded to ten entries and removes
missing files through the existing recent-stage store.

The [review sample](../src/OpenUsd.Viewer/Assets/ReviewScene.usda) is project-owned and MIT-licensed.
It is embedded in the Viewer assembly, requires no download or external textures, and works without
a source checkout. Open Review Sample refreshes its Viewer-owned cache at
`LocalApplicationData\OpenUsd\Viewer\samples\review-scene-v1.usda`, then opens it normally.
This cache is not a user save destination. Runtime/driver failures remain actionable status text,
with guidance to choose an available renderer, rather than being mistaken for an empty scene.
The no-document view hides the empty timeline without changing its saved visibility preference.

An empty but successfully loaded USD stage is a document: it keeps the viewport, stage identity,
reload and inspection workflow rather than returning to welcome. Welcome is hidden before native
surface creation and never overlaid over a live Storm child. Palette and comparison use separate
owned windows; the viewport frame still contains only the renderer host.

### Capturing the current viewport

**File > Capture Frame** saves a PNG by default, or an uncompressed 24-bit BMP when chosen in the
destination picker. The filename must end in `.png` or `.bmp`; other formats are refused rather
than receiving mislabeled image data.
Native-child Storm now uses its preserved completed framebuffer, rather than failing through an
hdSilk-only capture interface. hdSilk retains its existing retained-scene readback path. The
capture result carries its actual dimensions and RGBA row order, so Storm's bottom-up pixels and
hdSilk's top-down pixels produce correctly oriented output without an extra image-sized flip buffer.
PNG preserves RGBA, including alpha; BMP remains opaque. The writers process rows without applying
another exposure or colour transform.

Captured RGBA data and encoded output are each limited to 64 MiB, with at most 8192 pixels per side
for export. Encoding runs off the UI thread into a new sibling temporary file. Only a completed,
flushed image replaces the chosen destination; cancellation or encoding failure preserves any old
file and removes temporary output. This is atomic publication, not a filesystem compare-and-swap
lease against independent writers.

Unsupported presentation paths, including the alternative
Avalonia-hosted OpenGL Storm path without readback, disable Capture Frame rather than advertising
an operation that will fail. Canceling the destination picker leaves the output untouched; access
failures are reported, and closing the Viewer cancels and drains its capture work. This is a viewport still,
not arbitrary-resolution rendering, an HDR/AOV export, or a sequence-render job.

### Rendering an image sequence

**File > Render Image Sequence...** is also discoverable in **Search Commands**. Its owned,
themed window takes start/end time codes and a positive step, shows the derived frame count,
and lets the operator choose an existing local output folder. The end is included when it
falls on the stepped range. These are explicit time codes, not repeated captures of the current
frame or timeline-snap requests. The current physical viewport size is used; resizing cancels
the running job rather than changing its camera window partway through.

PNG is always included. Two unchecked-by-default choices add raw data beside each PNG:

- **HDR color data** writes `frame-000000.hdr.rgba16f`: tightly packed, top-down, little-endian
  RGBA half-float framebuffer color before exposure, display conversion and display selection outlines.
  These are the actual stored renderer-working composited samples, not reconstructed PNG values.
  Alpha is stored framebuffer alpha after blending, not necessarily straight/unassociated; no named
  primaries, albedo or physical-radiance interpretation is promised.
- **Depth data** writes `frame-000000.device-depth.f32`: tightly packed, top-down, little-endian float32
  normalized device depth in **[0, 1]**, with clear value **1**. This is not metric camera distance or
  an independent coverage/hit mask; clear and a far-plane write cannot be distinguished.

Choices are copied into immutable per-job options before rendering and are disabled while the job
drains. They do not change source files or the preference format. Reopening the dialog defaults to
PNG only; existing PNG-only jobs and **Capture Frame** keep their original readback paths without
automatically requesting HDR or depth. The dialog shows the selected file types, applicable capture
charge, and completed PNG/HDR/depth file counts.

When HDR is enabled, **HDR format** offers **Raw half** (the default) or **EXR**.
EXR writes `frame-000000.hdr.exr` through the matching Windows x64 Data ABI 24 Core encoder.
It preserves the same finite binary16 samples and stored alpha without a color/alpha conversion;
equal origin-zero windows, square pixels, unspecified primaries and alpha association remain explicit.
The format is frozen with the job options and disabled during rendering; changing a control
programmatically cannot alter an admitted sequence. EXR shares the existing combined encoded
PNG/depth/HDR byte budget, but native codec working memory is outside that quota and is stated
in the dialog. Other host encoding profiles remain unsupported.

The Windows **hdSilk / Direct3D 12** and **hdSilk / Vulkan** bindings support PNG and both optional data planes.
**Native Storm** captures timed PNG sequences using its completed native viewport framebuffer.
The dialog identifies this native-appearance profile before capture, and the manifest records it
as a diagnostic: Storm's native lighting/material/background behavior is retained, not a promise
to apply the Viewer's hdSilk exposure, tone-map or OCIO controls. The native profile admits only
the application's default presentation choices, smooth shading and ordinary purpose/visibility
settings; custom overrides are refused rather than ignored. It waits for
convergence, matches camera/frame identity and preserves bottom-up framebuffer orientation in
the top-down PNG. Cancel, repeated close, Reload and owner close drain through the shared job
and restoration path.

On the verified Windows child ABI 9 route, optional HDR/depth and Windows EXR also work while
preserving those native PNG pixels. Their stricter raster limit is **4096 per side and 1,048,576
total physical pixels**, matching the child viewport. Reduce the window/viewport before enabling
data planes if the dialog reports that limit; high-DPI scaling counts toward physical pixels.
Raw HDR additionally requires **empty display selection**, because Storm can bake highlights into
its color AOV. The dialog explains the refusal; capture never silently deselects. Native RGBA8 and
top-down AOVs come from one render-thread operation that refuses intervening stage changes.
Linux is source-wired but unverified for this capture profile; macOS Metal Storm remains PNG-only.

The active renderer and dimensions are shown in the dialog. Capture readiness follows the actual
retained graphics device; Vulkan becomes available after its first completed frame rather than
being replaced by another renderer. The common adapter is also wired for Metal, but macOS
sequence execution still needs native evidence. The current sync API
also requires Default, Proxy and Render purposes, Guide off, authored visibility, and one sample
per pixel. Unsupported purpose/visibility or sample requests are explained without rendering.
Disable physics preview before starting: this is an authored USD-time sequence, not a simulation bake.
Both optional planes support the committed GPU display-transform path on the D3D12 and Vulkan
bindings. HDR comes from that capture's completed scene target, before exposure, display/view/look
conversion and selection; it does not come from an earlier cached target or an inverse display
conversion. An unsupported device or a missing/invalid GPU transform fails the job rather than
publishing fallback color as HDR. The existing PNG-only display fallback remains unchanged.

On hdSilk, each frame uses the frozen draw, selection, display and committed OCIO settings.
Native Storm uses the explicitly identified native viewport appearance instead. An active authored
camera is queried at every requested time, including its animated transform, aperture offsets,
focal length and clipping window; unavailable samples fail instead of falling back to another
camera. Presentation must finish with the matching state before retained-scene readback. An OCIO
request must have a matching applied display pass. On composition backends, all selected planes come
from the same retained capture, and their original managed storage is wrapped without copying or
widening pixels. Storm instead copies detached native presentation and typed AOV planes under its
combined storage limits. The shared
`RenderDiskJob` engine writes each frame's PNG and optional planes on a dedicated worker; graphics,
capture and restoration are marshalled through the Viewer dispatcher/coordinator. Borrowed frame
storage is consumed before the next capture. No frame-sized buffers are retained in result descriptors.
Composition capture excludes presenter calls, resizing and teardown for its entire operation.
An authored-product job pins that same device until all of its native/GPU resources are released;
it never creates a hidden replacement device. The coordinator drives presentation on demand so a
backend's continuous-preview preference cannot prevent cancellation or close from draining.

Actual renderer capture diagnostics travel with each detached frame. The shared engine retains
up to 128 distinct diagnostics across the whole job in first-occurrence order, including in
`manifest.json`; a later clean frame cannot erase an earlier degradation. Completion reports
retained diagnostics and highlights warnings/errors rather than presenting a degraded job as clean.

Jobs admit **1-4096 frames**, no dimension above **8192 pixels**, and **4 GiB total output including
the manifest**. The **64 MiB per-frame capture admission** charges **4 bytes/pixel for PNG only**
or a uniform **20 bytes/pixel when either optional plane is selected**, even for HDR without depth.
This is a managed capture-pixel budget, not a whole-process/GPU memory ceiling.
Native Storm data output also enforces its stricter million-pixel limit and a separate 64-MiB
combined snapshot/native-presentation/conversion budget. The combined encoded
PNG, HDR and depth files also share a **64 MiB per-frame output limit**. Oversized selected output
sets are explained and disabled before synchronization or target allocation; reduce the viewport
size or deselect the optional data.

A generated `render-sequence-<id>` child contains ordered `frame-000000.png` files, selected raw
sidecars and `manifest.json`.
The manifest records requested times, dimensions, camera matrices, display/selection/render
settings, plane storage/conventions, image sizes and hashes. Neither scene-authored product names nor
RenderPass commands select filesystem paths or execute code. Only the operator-selected parent folder is used.

Playback and competing view edits pause during execution. Cancel, Escape, the dialog's close,
Viewer close and Reload cancel and drain in-flight work before document retirement. The original
view is restored before the final frame can reach publication, and on cancellation/failure;
playback remains paused. Native opinions are not rewritten or rolled back, and transient job
states are not published as host camera changes or persisted as preferences. A scheduler-observed
stage revision change fails the job while preserving that writer's opinions. These existing
revision gates are not a guarantee against independently owned, unscheduled native writers.
Source-change delivery stays attached for the document's lifetime. A bounded, coalescing handoff
retains notices while sequence processing is paused and replays them after the camera pumps are
ready and suppression is cleared. The same kept-notice path refreshes the active authored camera
at the restored time and affected hierarchy/property data, without requiring another time change,
edit, or resize. Unchanged-source jobs do not synthesize a refresh. Each handoff is bound to its
document, coordinator and scheduler; retirement cancels/drains its work and refuses queued or late
notifications instead of applying them to a replacement document.

Only a fully completed job becomes visible at the final output location. Missing, mismatched or
non-finite raw planes fail the whole job. Cancellation, middle-frame failure or restoration failure
removes every private staging plane and cannot leave a completed-looking job folder. The dialog keeps
progress, cancellation status and the completed location visible.
Cancellation can wait for an already-running graphics call to drain; it does not forcibly interrupt
a driver. This scope does not include arbitrary-resolution renders, UsdRender product/AOV execution,
arbitrary EXR product metadata/windows, movies or a general render queue.

The source/runtime-fingerprinted native scenario is `render-sequence` in
`eng\run-viewer-workflow-tests.ps1`. It exercises differing animated frames, independent authored-camera
matrix expectations, effective draw/OCIO settings, literal stored HDR values and actual device depth,
PNG equality with optional planes, independent half-bit EXR decoding against raw HDR for both
CPU/GPU display paths, raw-data invariance across Reinhard/identity, GPU display/view/look,
exposure and selection, immutable admitted choices, bounded admission and explicit unsupported bindings.
It also exercises GPU three-plane cancellation, mid-job source/non-finite failures, failed-transform
cleanup and retry without stale HDR, actual rendered-view restoration, and pending Reload/Close.
Running this scenario alone is not an all-Viewer-workflows result.

### Previewing completed render jobs

After **Render Image Sequence** or **Render Authored Product** succeeds, its **View results**
button opens an owned, themed **Contact sheet** window. The button stays disabled until success;
starting another job closes and drains the previous preview before clearing its result. A failed
or cancelled newer job cannot expose the previous job as its own result.

The compact, scrollable grid shows all frames for jobs of up to 16 frames. Longer jobs use 16
deterministic, evenly distributed indices in original job order, including the first and last.
For `N` frames and `K = min(N, 16)` tiles, tile `i` uses `floor(i * (N - 1) / (K - 1))`;
a one-frame job uses index zero. Each tile labels the zero-based frame index, original USD time
code, source raster dimensions and generated PNG filename. The header keeps the original generated
job identity, stage identity, output directory and, for products, the original product path.
Changing a dialog's inputs does not relabel previously completed output.

This is an in-app PNG preview, not a new render, export, movie encoder or file-association launcher.
It reads only the recorded generated PNGs; no scheduler, renderer or render slot is acquired.
It does not change the current camera/time, display settings, source or review data. Images keep
their aspect ratio and alpha with transparent letterboxing, no crop and no colour conversion.
The grid follows Viewer dark/light and contrast resources. Tab reaches the output location, frame
grid and Close action; arrow keys move between frames. Escape closes the preview and restores focus
to **View results** when its job dialog remains open.

Every selected file must remain inside the recorded job directory, with no reparse-point or
filesystem-alias traversal. Windows reads reuse the retained physical-directory/file handles.
The exact recorded file length (at most **64 MiB**), stable read/EOF, SHA256, PNG dimensions,
chunk CRCs and zlib checksum are checked before the sheet is displayed. A missing or modified
later sample fails the entire preview: no partial grid is kept, and the error is shown and logged.
Close and reopen **View results** to retry after repairing the completed files; this still does
not render anything.

Each cell has a **256 x 256 RGBA** thumbnail, at most **4 MiB of retained thumbnail rasters** per
16-cell sheet. One source PNG is decoded at a time, admitted at no more than 8192 pixels per side
and 64 MiB of decoded RGBA. Encoded-source storage, that full-size decode, temporary bitmap upload,
uncollected managed storage and Avalonia/native/graphics working memory are **not** covered by the
thumbnail raster quota. This is not a process-memory guarantee.

Close cancels and asynchronously drains loading before releasing the window and every owned
bitmap; it does not block the UI thread waiting for a file or decoder. Late completions cannot
publish into a closing or replacement preview. The sheet belongs to its job dialog: closing that
dialog, closing the stage/Viewer or reloading the stage closes and drains it too. It is not a
persistent results browser that survives document retirement.

### Comparing saved captures

**File > Compare Captures** (also on welcome and in command search) compares two existing files
without opening a stage or starting another render. Choose before/after independently, then use
Side by side, Before, or After. Images fit independently and retain their original dimension labels;
this is a visual comparison, not a pixel-error metric or a new colour-management pipeline.

Comparison supports non-interlaced 8-bit RGBA PNGs with all five standard scanline filters, plus
uncompressed 24-bit and 32-bit Windows BMPs. PNG transparency is preserved; the BMP reserved fourth
byte is treated as opaque, not alpha. Indexed/RGB-only/grayscale/16-bit/interlaced PNG, compressed BMP,
JPEG, HDR and video are not accepted. Each file is limited to 64 MiB, 8192 pixels
per side and 16,777,216 pixels. Both headers, offsets, formats and dimensions are validated before
allocating image buffers; truncated or unsupported files produce explicit errors, never black
success-shaped images. PNG chunk CRCs, data ordering and inflated image size are checked; an
overlong deflate result does not cause an image-sized speculative expansion.
Decoder rejections clear both images and leave comparison/retry controls
usable rather than escaping through an asynchronous button handler.

Only one pair loads at a time, on a worker, with cancellation between bounded row reads. The decoded
pair is at most 128 MiB of RGBA data, plus at most 128 MiB of display bitmap backing data and bounded
I/O/control overhead; each PNG input additionally admits at most 64 MiB of encoded storage. PNG
decoding reads the original IDAT segments directly and reconstructs rows into the output buffer,
without a concatenated compressed stream or an image-sized filtered-data copy. The graphics
compositor may also retain its own copies. Replacement releases
previous display bitmaps before loading. Cancel loading reports cancellation, and closing cancels
and drains the pending read before releasing the owned window. No partial pair is published and
no renderer, theme, pick, authoring or OpenColorIO preference is changed.

### Pick target and selection outline

**Tools &gt; Pick Target** is a four-item radio group -- **Prim**, **Face**, **Edge**, **Point** --
that decides what a later viewport click resolves. In a standalone Viewer this group is what every
click actually requests; only an embedding host that named a fixed `PickTarget` overrides it, and
only for its own `PrimPicked` callbacks (see [Reacting to the operator](#reacting-to-the-operator)).
**Tools &gt; Selection Outline** is a two-item
radio group choosing between the ordinary depth-tested **Visible only** outline and the **X-ray**
outline, which also draws the occluded part of the selection in a distinct, accessible style so a
selection behind geometry stays locatable. Both are modes rather than actions: they change what a
later click means, so neither is promoted to the toolbar, which is exactly the clutter the
menu-first policy exists to avoid.

Both groups use stable command identities (`tools.pickTarget.*` and `tools.selection.*`) rather than
menu positions or labels, so a persisted profile survives a menu reordering and a screen reader
announces the same control after a relabelling. Each item carries an accessible name in the command
catalog, so `ViewerCommandCatalog` remains the single source for the accessibility surface.

Only the hdSilk backends -- D3D12, Vulkan, and Metal -- answer the subprim targets and composite the
occluded outline. On Storm those entries are **disabled rather than hidden**: a hidden control makes
a capability difference look like a missing feature, while a disabled one keeps the control
discoverable and its accessible name names the backend that would answer it. Choosing an
unsupported combination -- from the menu or from a persisted profile -- leaves the applied state
alone and reports the named reason on the status line.

What the user asked for and what the attached backend can answer are tracked separately. A refused
request is still remembered, so a Viewer that opened on Storm -- or before any backend attached --
does not write its restrictive fallback back over a saved edge-picking or x-ray profile, and
switching to a capable backend restores the request without the user choosing it again. The desired
values are re-resolved against the backend every time the active backend changes, which is the only
moment at which a refused request can become answerable or an applied one can stop being.

Both settings persist in the `openusd-viewer-settings=4` profile as `pickTarget` and
`selectionMode`, written from the desired values rather than the applied ones. A v0, v1, or v2
profile has neither key, and migration is exactly that: both fall
back to **Prim** and **Visible only**, which reproduce the pre-v3 behaviour, and the profile is
rewritten with them on the next save. A token this build does not recognise falls back the same way
rather than rejecting the whole profile, because a forward-compatible token must not cost the user
their unrelated layout.

Diagnostics, Hydra, and TfDebug visibility is independently toggleable from two places that stay in
sync: View &gt; Inspector Tabs, and Tools &gt; Developer &gt; (Diagnostics | Hydra Scene | TfDebug)
&gt; Show tab.

The physics transport strip is hidden entirely while physics is off. Once **Physics** builds a
world, the strip shows only Play/Pause, Stop, Step, the scrubber, and the status line; everything
else physics-related lives in the Physics menu and the Physics inspector tab. See
[Transport controls](#transport-controls).

Moving a control into a menu never removes the state it drove: each menu item sets the same
selection or checked state the pre-existing control read, so existing viewport, physics, pick-mode,
and validation logic is unchanged behind the new command surface.

## Bridge connections

The Viewer can drive one live bridge session, and only when an embedding host asks it to. There is
no static registration and no discovery: the surface exists exactly when a host sets
`ViewerHostOptions.BridgeConnection` to an `IViewerBridgeConnectionProvider`. With no provider the
**Tools &gt; Connections &gt; Omniverse Bridge** entry stays hidden and disabled as it ships in
markup, the status bar shows nothing about a bridge, and opening, rendering, and simulating a local
stage behaves exactly as it does today.

The public seam is deliberately small and carries no transport:

| Member | Purpose |
| --- | --- |
| `IViewerBridgeConnectionProvider.DisplayName` | The name the menu, tooltip, and dialog show. |
| `IViewerBridgeConnectionProvider.IsAvailable` | Injected but unusable keeps the entry visible and disabled. |
| `GetStatus()` / `StatusChanged` | Bounded, detached `ViewerBridgeStatus` snapshots. |
| `GetSessionsAsync()` | The bounded session choices the operator picks from. |
| `ConnectAsync` / `DisconnectAsync` / `ResyncAsync` | The three commands the dialog offers. |

`ViewerBridgeStatus` is a readonly record struct of values and immutable strings: connection state,
opaque session identifier, a redacted endpoint description, connect-attempt, applied-update, and
pending-outbound counts, a timestamp, and one bounded detail string. No gRPC type, native handle,
credential, or authored payload can cross the seam, because none of them appear in it.
`ViewerBridgeText` and `ViewerBridgeEndpoint` are public for the same reason: an integration package
applies the identical redaction rule at its own boundary rather than reimplementing it.

**Omniverse Bridge...** opens a focused dialog with a session list, the current status, the three
counters, and Connect, Disconnect, and Resync. There is no endpoint field and no credential field
anywhere in the Viewer, including its settings: those are host configuration, and a text box here
would turn the Viewer into a place secrets are typed and, eventually, persisted. Nothing about a
bridge is written to the settings store. Any endpoint the Viewer displays has its userinfo, query,
and fragment removed first, and the same redaction is applied to every provider-supplied message, so
a failure that quotes a URL back cannot leak a token into a status bar or an exported diagnostic.

The status-bar indicator is hidden until a session is configured or active, and hides again after a
successful disconnect, so a Viewer that is only opening local files never grows a bridge field. A
session that is still active keeps being reported even if the provider starts reporting itself
unavailable: availability gates the commands, not the truth that something is still connected.

Behaviour under load and failure is decided by `ViewerBridgeConnectionModel`, which is headless and
independently tested:

- Provider callbacks are handled off the UI thread. `StatusChanged` only copies a detached snapshot
  into a bounded queue and posts a drain, so a provider raising events from its own transport thread
  never touches a control and never blocks rendering.
- The queue holds at most `ViewerBridgeLimits.MaxPendingStatusEvents` snapshots, drops the oldest
  under a burst, and reports the drop count in the dialog. A bound that discards silently is
  indistinguishable from a provider that stopped sending.
- Exactly one command runs at a time, and Connect, Disconnect, and Resync are gated on the current
  state, so a second click cannot overlap a command in flight.
- A provider that throws, faults, is cancelled, or misbehaves in its own properties produces an
  explicit redacted message in the dialog rather than an exception crossing into UI code.
- Closing the window disposes the model: the subscription is dropped, in-flight commands are
  cancelled, and every later command is refused, so a provider cannot keep a closed window alive.
- The subscription itself never throws back at the provider. A failed hand-off to the UI thread -
  a dispatcher that is shutting down, for example - is counted as a dropped notification and
  reported as an error, because a Viewer-side defect that escaped into a transport's observer
  callback would be blamed on the transport and could end the session that raised it.

The base `OpenUsd.Viewer` package has no gRPC, protobuf, or NVIDIA dependency and never gains one.
A real transport arrives through the optional `OpenUsd.Viewer.Bridge.Grpc` package, which is the
only assembly that references both the Viewer and `OpenUsd.Bridge.Grpc`. A host constructs
`OmniverseViewerBridgeProvider` with an `OmniverseViewerBridgeOptions` whose session factory returns
the coordinator and the fully configured `BridgeClientOptions` - endpoint and credential provider
included - at the moment the operator asks for a connection. The adapter never reads, stores, logs,
or displays a credential, and never mutates the options instance the host handed it.

The session the operator picks in the dialog is not decorative: it becomes the client's
`RequestedSessionId` and reaches the peer on the handshake. A connect that fails, times out, or is
cancelled leaves nothing running behind it, and the redacted, bounded status the dialog shows is
already redacted by the provider before the Viewer sees it. See
[Omniverse bridge](omniverse-bridge.md#viewer-integration).

## Visual system

Viewer chrome uses a local semantic resource layer over the existing Avalonia Fluent controls.
**View > Theme** follows the OS/application theme by default; an explicit Light or Dark choice
applies to this Viewer window and its owned dialogs, not to other windows in an embedding host.
Theme changes do not reapply the settings profile or touch renderer selection, camera, exposure,
OpenColorIO, the authored scene, or the viewport's chosen background.
A host-selected startup renderer is a window override, not a new saved preference. Changing
theme or closing the window preserves the stored renderer until the operator changes its selection.

### Annotated light/dark layout references

Both themes use the same viewport-first geometry. Workspace presets and command discovery use this
same visual system; they do not add floating docking or a new review-layer editing lifecycle.

```text
LIGHT: warm-neutral panels; dark ink; restrained blue/teal interaction
+----------------------------------------------------------------------------+
| CHROME  File View Render Camera Physics Tools Help | Commands | Frame Capture|
+------------------+-------------------------------------+-------------------+
| PANEL            | CANVAS / NATIVE VIEWPORT             | PANEL             |
| Stage identity   | No floating Avalonia chrome over     | Inspector tabs    |
| Filters + tree   | the native Storm child.              | Grouped controls  |
|                  | Rendered pixels keep their own       |                   |
|                  | background and display transform.    |                   |
+------------------+-------------------------------------+-------------------+
| CHROME  Existing timeline: play, time, range                               |
| CHROME  Ready / actionable message         stage, camera, GPU, renderer     |
+----------------------------------------------------------------------------+

DARK: graphite panels; light ink; the same blue/teal interaction
+----------------------------------------------------------------------------+
| CHROME  File View Render Camera Physics Tools Help | Commands | Frame Capture|
+------------------+-------------------------------------+-------------------+
| PANEL            | CANVAS / NATIVE VIEWPORT             | PANEL             |
| Stage identity   | The viewport remains the largest     | Inspector tabs    |
| Filters + tree   | uninterrupted surface.               | Grouped controls  |
|                  | Raised surfaces identify inputs;    |                   |
|                  | accent identifies interaction.       |                   |
+------------------+-------------------------------------+-------------------+
| CHROME  Existing timeline: play, time, range                               |
| CHROME  Ready / actionable message         stage, camera, GPU, renderer     |
+----------------------------------------------------------------------------+
```

| Reference state | Light layout annotation | Dark layout annotation |
| --- | --- | --- |
| No document | Warm-neutral welcome; Open and recent scenes. | Graphite welcome; the same open/sample guidance. |
| Loaded | Image unchanged; neutral panels recede. | Image unchanged; muted teal selection. |
| Editing | Raised fields and a clear focus ring. | Same geometry; a lighter focus ring. |
| Warning | Explicit status beside the viewport. | Contrast-aware status ink, never colour alone. |
| Presentation | Explicit preset hides panels/timeline. | Same preset and geometry; image colours unchanged. |

Physics transport remains visible only when enabled. Warning messages use adjacent chrome or a
dialog, not a native-child overlay. Presentation uses the layout policy's existing visibility
controls while retaining the compact menu/status strip; it does not change the camera or render.

Spacing uses a 4-unit scale (4/8/12/16), compact 28-unit controls, restrained 4-unit corners,
13-unit body text, and 16-unit section headings. System font families retain platform glyph
fallback; code-like shortcut gestures use a monospace fallback chain. Open and Frame
use the small project-owned, MIT-licensed Viewer line-icon family, always alongside a label.
Developer tabs remain hidden in a new profile; legacy visibility choices are preserved.

The semantic palette separates canvas, chrome, panel, raised controls, primary/secondary/disabled
text, accent/on-accent, selection, dividers/control borders, focus, and actual status colours.
High-contrast OS preferences strengthen text, surface, border, and focus separation without changing
the saved Light/Dark/System choice or any rendered pixels. Focus remains visible independently of
hover, and no custom motion is introduced.

### Embedding scope

`MainWindow` and its owned Viewer dialogs install `ViewerStyles.axaml` locally. `App.axaml` keeps
only its existing Fluent theme; neither Viewer brushes nor control selectors are installed in an
embedding application's global styles. Hosts that want the visual system on their own bounded
Viewer surface can add a local `StyleInclude` for
`avares://OpenUsd.Viewer/ViewerStyles.axaml` to that control's `Styles` and set its
`Classes="openusd-viewer"`. Use a `ThemeVariantScope` when that surface needs its own theme.
Do not apply that class to an entire host window containing unrelated controls. Bare embedded
viewport controls do not opt in automatically.

### Visual smoke

The focused headless tests exercise settings, command dispatch, theme inheritance, semantic
palettes, contrast, and embedding scope. The Windows native-window smoke additionally drives the
real menu handlers, verifies live panel/dialog colours and persisted choices, keeps an unrelated
host window unchanged, and captures both themes at 1440x900 and 960x600 logical units. It uses
temporary settings and no USD stage; it is not loaded-scene, native Storm input, or full DPI-matrix
evidence. Run it alone against the built test project:

```powershell
$env:OPENUSD_VIEWER_THEME_SMOKE = '1'
.\eng\run-managed-tests.ps1 -Project tests\OpenUsd.Viewer.Tests\OpenUsd.Viewer.Tests.csproj `
  -Framework net10.0 -TestArguments @('--treenode-filter', '/*/OpenUsd.Viewer.Tests/ViewerVisualNativeSmokeTests/*')
```

`OPENUSD_VIEWER_THEME_SMOKE_ARTIFACTS` optionally selects the screenshot directory; otherwise the
images go under the test output's `TestResults\viewer-theme-smoke` directory.

The separate workspace smoke runs the native shell, a real Storm child, the bundled sample and an
empty stage opened from recent files. It exercises presets without changing saved renderer/pick/theme
intent or the edit target, find/select/frame/inspect, original menu invocation, native palette typing
and focus restoration, owned-window z-order/client hit-testing, and comparison success/error/cancel/
close paths. Run it in its own process with the existing native runtime staged:

```powershell
$env:OPENUSD_VIEWER_WORKSPACE_SMOKE = '1'
$env:OPENUSD_PLUGIN_PATH = (Resolve-Path native\install\win-x64\plugin\usd).Path
.\eng\run-managed-tests.ps1 -Project tests\OpenUsd.Viewer.Tests\OpenUsd.Viewer.Tests.csproj `
  -Framework net10.0 -Configuration Release `
  -TestArguments @('--treenode-filter', '/*/*/ViewerWorkspaceNativeSmokeTests/*')
```

The test host normally prepends the repository's native install on Windows. To use a different
matched build without modifying that shared install, set `OPENUSD_VIEWER_TEST_NATIVE_ROOT` to an
absolute runtime directory with `bin` and `lib`, and point `OPENUSD_PLUGIN_PATH` at its merged
`plugin\usd` tree. The configured directories take precedence; invalid or missing configured
directories fail rather than falling back to an older shim. Managed assemblies and the selected
data shim must agree on the required ABI and capabilities.

This is Windows/WGL evidence at the host's current scaling, not a mixed-monitor DPI matrix or
executed GLX/XWayland/macOS evidence. No Avalonia overlay above a live native surface is assumed.

## Settings and accessibility

Viewer settings are stored under:

```text
LocalApplicationData\OpenUsd\Viewer\viewer-settings.txt
```

The versioned, NativeAOT-safe text store is written atomically. It contains window dimensions, panel
widths and visibility, renderer preference, the selected inspector tab's stable string identity,
Diagnostics/Hydra/TfDebug tab visibility, the manual timeline snap preference, and the Viewer theme
choice. Schema v4 adds `theme=system|light|dark`; missing choices in v0-v3 profiles follow the system.
An unknown or empty theme value falls back to System with an explicit diagnostic while preserving
the rest of the profile. Explicit theme choices are saved atomically on selection and again on
normal close; Reset Layout leaves the theme choice intact. Stage-specific
camera and session state are not persisted. Malformed or oversized settings are ignored with an
explicit Viewer diagnostic; I/O failures are shown in the status area. Unknown/removed selected tab
IDs fall back to Properties and out-of-range saved dimensions are normalized independently, with
a diagnostic, rather than discarding unrelated preferences. Non-finite dimensions use their clean
defaults; numeric dimensions are clamped to the supported ranges. Restored window dimensions are
also constrained to the current display's working area, subject to the 960x600 minimum window size.

Settings schema v2 selects the inspector tab by a stable string identity (`properties`, `value`,
`metadata`, `composition`, `layers`, `diagnostics`, `validation`, `hydra`, `physics`, `tfdebug`)
rather than a visual index, and tracks Diagnostics, Hydra, and TfDebug visibility as three
independent flags. A new profile defaults all three developer tabs to hidden; Stage, Inspector, and
Timeline remain visible. The new `appearance` tab uses the same stable-ID field; presets persist the
existing layout values rather than adding a startup preset override or another settings schema.
**Reset Layout** (View menu) restores the clean defaults deliberately.

Reset Layout is transactional where colour management is concerned. Restoring the default profile
also means restoring "no display transform", and the profile may not claim that before the image
does: an active OpenColorIO transform is cleared from the coordinator first, through exactly the
request/mutation pipeline the Render &gt; Colour Management items use, and the default
colour-management model, menu item, cached key, and persisted choice are committed only once the
coordinator has published a state without the transform. A reset that finds nothing active and
nothing outstanding skips the pipeline entirely. The "active" test consults the committed model, the
cached key, *and* the transform the state actually carries, so a state still carrying a transform the
model has already disowned is cleared rather than mistaken for a clean one.

The clear is also what supersedes colour management's in-flight work, so the reset requests it
whenever a request is still outstanding even if nothing is committed yet and the viewport carries
nothing. A request whose OpenColorIO bake is still running, one suspended inside its transactional
mutation, and one deferred because there was no coordinator are all invisible to the committed model,
the cached key, and the state's transform alike; a reset that consulted only those three would skip
the pipeline, report success, and then be contradicted when the older request landed and colour
managed the viewport it had just declared clean. Requesting the default clear takes a newer pipeline
generation, which cancels the older request and discards its result, and replaces any deferred one
with the default clear, so an enable made just before a reset can neither commit nor be replayed by
the next document open afterwards. Because the mutation is itself a suspension point the pipeline
cannot see through, a request re-checks that it is still the newest generation on both sides of it
and commits nothing when it is not. A deferred *clear* left behind by a refused reset does not make
the viewport colour managed, so it is not reported as though it had. A document open in flight is
covered by the same generation: the open re-checks its captured choice immediately before creating
the coordinator and again immediately before committing it, so a reset that reports the viewport
clean can never be contradicted by the enable an open captured before it.

A clear that cannot reach the image -- no coordinator, a document change in flight, a cancelled
lifetime, or a backend that refuses -- is carried by the same deferral semantics an interactive
request uses: nothing colour-management-related is committed, the previous commit stands, the
request is recorded so the next document open replays it, and the status line says so. Only the
layout half of the profile is applied in that case, so the menu, the model, the cached key, and the
image still agree. Reset Layout can therefore never uncheck the toggle or disarm the reconciliation
poll loop while the viewport is still colour managed; for the same reason the loop is armed whenever
the toggle is on *or* the committed key is non-null, rather than on the toggle alone.

Existing v1 and v0 settings keep working:

- v1's integer `selectedTab` maps to the v2 tab identity through a fixed table (`0` Properties,
  `1` Value, `2` Metadata, `3` Composition, `4` Layers, `5` Diagnostics, `6` Validation, `7` Hydra,
  `8` Physics, `9` TfDebug). Index `10`, the removed Settings tab, was never actually persisted:
  the v1 writer clamped every persisted index to `9`, so a literal persisted `9` cannot be
  distinguished from a Settings selection and is preserved as TfDebug. This is the one documented,
  unrecoverable ambiguity in the v1 format.
- v1's `diagnosticsVisible` flag is preserved as authored. v1 had no Hydra or TfDebug visibility
  flag and always showed both tabs, so a migrated v1 profile initializes both visible rather than
  applying the v2 clean default of hidden: preserving an existing user's layout takes priority over
  a default that only applies to new profiles.
- v0 predates every tab-visibility flag and always showed Diagnostics, Hydra, and TfDebug, so a
  migrated v0 profile also initializes all three visible, preserving that legacy layout. Every
  other field v0 never had an opinion about (the selected tab, panel visibility, snap-to-frames)
  falls back to the v2 clean default.

If a selected tab is hidden or removed, the Viewer falls back to Properties, resolved through the
same pure layout policy used for developer-tab visibility; no behaviour is ever derived from a
visual tab index.

Diagnostics, Hydra, and TfDebug are developer tabs: while any one of them is hidden, the Viewer
performs no background sampling or per-frame refresh for it. Diagnostics capture and Hydra scene
capture are both gated on their tab's visibility before doing any work; TfDebug is refreshed only on
an explicit Refresh action, so hiding it has nothing further to skip.

Access keys are shown with underlined menu/button labels. Keyboard shortcuts include:

- `Ctrl+O`: open a stage
- `Ctrl+E`: edit a supported property of the selected prim in the review layer
- `Ctrl+Z`/`Ctrl+Y`: shared review Undo/Redo outside text inputs
- `Ctrl+R`: reload the current stage
- `Ctrl+Shift+P`: search commands from Viewer controls
- `Ctrl+F`: reveal and focus hierarchy search
- `Ctrl+Shift+C`: capture the current frame
- `Ctrl+Alt+1`/`2`/`3`/`4`: Review/Inspect/Materials-Lighting/Presentation layout
- `F1`: show the keyboard and mouse shortcuts dialog
- `Space`: play or pause the timeline when focus is not in a text box
- `F`: frame the selected prim
- `Home`: reset the camera to Automatic
- `P`: toggle Perspective/Orthographic projection
- arrow keys: orbit by 5 degrees while the viewport has keyboard focus
- `K`: play or pause the physics simulation
- `J`: stop the physics simulation and restore the authored state
- `N`: advance the physics simulation by one fixed step
- `B`: open the physics bake dialog
- `Q`/`G`/`E`/`R`/`H`: physics gizmo none/move/rotate/scale/drag
- `X`: toggle physics gizmo snapping
- `Z`/`Y`: undo/redo the last physics property edit

Camera and physics shortcuts are ignored while a text box or combo box is being edited, and never
fire with a modifier held. Controls use logical markup order, automation names, and theme resources
rather than fixed foreground colors.

## Automated evidence

Shared soak and renderer evidence runs do not load or save Viewer settings, enable session or
variant controls, or sample the interactive diagnostics model. Schema 8 camera evidence is fully
automated and does not change interactive camera navigation: it temporarily applies deterministic
managed view/projection matrices, captures bound backend pixels and Storm camera diagnostics, and
restores automatic mode before continuing fallback, loss, quarantine, or switching scenarios.
On Windows it additionally delivers a real Win32 Alt-left drag to the Storm child, polls the ABI-9
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
