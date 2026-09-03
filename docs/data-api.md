# Data API

The core API phase exposes a versioned, NativeAOT-safe managed wrapper over `openusd_dotnet` plus an
idiomatic facade in the `OpenUsd` package covering stages, layers, prims, typed attribute values,
prim lifecycle, relationships, composition, variants, sublayers, and metadata.

```csharp
using UsdStage stage = UsdStage.Create("scene.usda");
UsdPrim sensor = stage.DefinePrim("/World/Sensor", "Xform");
sensor.SetDouble("custom:temperature", 42.5);
sensor.SetDoubleArray("custom:samples", [1, 2, 3, 5, 8]);
sensor.SetBool("custom:enabled", true);
sensor.SetInt64("custom:count", 42);
sensor.SetString("custom:label", "north");
sensor.SetToken("custom:kind", "Beacon");
sensor.SetVec3f("custom:direction", new UsdVec3f(0, 1, 0));
sensor.SetColor3f("custom:tint", new UsdVec3f(1, 0, 0));
sensor.SetVisibility("inherited");
sensor.SetMetadata("owner", "team-sensors");
stage.Save();
```

## Stage timing, default prim, and layers

Viewer-facing stage controls expose the composed timeline and root-layer authored defaults:

```csharp
stage.StartTimeCode = 1;
stage.EndTimeCode = 240;
stage.FramesPerSecond = 24;
stage.TimeCodesPerSecond = 24;

stage.DefinePrim("/World", "Xform");
stage.SetDefaultPrim("/World");
UsdPrim defaultPrim = stage.GetDefaultPrim();

using UsdLayer sessionLayer = stage.GetSessionLayer();
string sessionIdentifier = stage.SessionLayerIdentifier;
stage.ClearDefaultPrim();
```

`GetDefaultPrim` throws `OpenUsdNativeException` with `NotFound` when no valid default prim is
authored, and `SetDefaultPrim` reports `NotFound` when the requested prim does not exist. Playback
rates must be positive and finite; time-code endpoints must be finite.

`UsdStage.Reload` reloads the non-session layers contributing to the stage and can affect other
stages sharing those layers, matching OpenUSD's threading and ownership rules. `UsdStage.Export`
writes a flattened composed stage. `UsdLayer.Reload` returns whether content was re-read, and
`UsdLayer.Export` writes a non-destructive copy of that layer.

### Edit targets and layer muting

Live authoring can direct edits to the root layer, session layer, or an owned `UsdLayer` that is
currently present in the stage's local layer stack:

```csharp
stage.SetEditTargetToSessionLayer();
stage.OverridePrim("/World/Preview");

using UsdLayer rootLayer = stage.GetRootLayer();
stage.SetEditTarget(rootLayer);
stage.DefinePrim("/World/Persistent", "Xform");

string currentLayer = stage.EditTargetLayerIdentifier;
string[] layerStack = stage.GetLayerStackIdentifiers();
string sublayer = layerStack.First(
    id => id != stage.RootLayerIdentifier && id != stage.SessionLayerIdentifier);
stage.MuteLayer(sublayer);
bool muted = stage.IsLayerMuted(sublayer);
stage.UnmuteLayer(sublayer);
```

Layer-stack identifiers cross the ABI as one packed UTF-8 string list in strong-to-weak order,
including session layers. Native validation rejects a layer handle from another stage or a layer no
longer present in the local stack. Muting and queries report `NotFound` for unknown identifiers;
the stage root and session layers cannot be muted. Session-layer edits compose immediately but are
not written by `UsdStage.Save`, while root-layer edits persist normally.

### Shared stage access and render sources

Data ABI v10 keeps the v8 intrusive-reference-counted stage handle and serializes every stage-based
status call with a recursive mutex. Ordinary calls take a short lock internally. The explicit native
access guard retains the stage and holds that same lock across a lexical operation; nested stage API
calls on the owner thread remain valid. A guard must end on the thread that began it. A wrong-thread
end reports the explicit `WrongThread` status and deliberately leaves the guard owned so its original
thread can release it safely. Once owner-thread teardown starts it cannot report failure: unlock,
guard destruction, and retained-stage release are a native `noexcept` commit with no later
`TfErrorMark` inspection.

`UsdStageScheduler` executes each complete work item under one lexical access guard, including serial
reads, the synchronous callback, result validation, and change notification. It never awaits while
the guard is held. Callback exceptions and cancellation still release the guard on the scheduler
thread; an access-end failure causes fail-fast because continuing with an owned native lock would be
unsafe. Access-begin allocation and post-lock failures roll back both the recursive lock and retained
stage reference before returning `NativeError`.

```csharp
UsdStageScheduler scheduler = UsdStageScheduler.Open("scene.usda");
UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();

// The renderer acquires its own child lease for the exact same UsdStage.
using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, source);
source.Dispose();
using OpenUsdSilkPage page = session.Sync(1280, 720);
var scene = new SilkSceneState();
scene.Apply(page);
session.Dispose();
await scheduler.DisposeAsync();
```

`UsdStageRenderSource` owns an independent retained native stage handle and is stage-bound. Every
renderer/session acquires another retained child lease; disposing the source therefore does not
invalidate an active renderer. Explicit disposal unregisters each child. Scheduler disposal rejects
active sources or renderer leases instead of waiting or deadlocking, and succeeds after all are
released. A root or session layer handle owns both its `SdfLayerHandle` and a retained reference to the
shared stage core. Every layer operation takes the same recursive mutex, so the layer remains valid
after the original stage handle is released and cross-thread layer mutation blocks behind an active
stage access guard. Layer release drops the layer reference before releasing the retained stage core
and never holds the mutex across final core destruction.

Storm and hdSilk expose source-based creation overloads:

```csharp
OpenUsdStormRenderer storm = OpenUsdStormRuntime.Create(pluginPath, source);
OpenUsdSilkSession silk = OpenUsdSilkRuntime.Create(pluginPath, source);
```

These overloads retain the stage core and copy the exact `UsdStageRefPtr`; they never reopen the root
layer path, so unsaved root/session-layer edits and later live edits are visible. The path overloads
remain compatibility shims: native code opens a temporary data-stage handle and delegates to the same
source-based implementation. Storm creation, render, and teardown remain on its OpenGL owner thread.
Storm renderer ABI v4 records both that thread and the platform context identity (WGL, GLX, or CGL)
and accepts the shared automatic/explicit matrix camera on every render.
Render and checked destruction require the exact original thread/context. The renderer name is cached
at creation and never touches the engine, so it is safe to read from another thread. Managed Storm
ownership is deliberately non-finalizable: call `Dispose` with the original context current. After
context loss or visual detachment, call `Abandon` on the owner thread. It does not inspect or require a
current context: it orphans the `UsdImagingGLEngine` pointer without running its destructor, then clears
the wrapper's copied stage reference and cached resources, releases the retained project stage core, and
deletes the wrapper. Only the GL engine and references inherently owned by that orphan remain for process
lifetime. Managed `Abandon`, `ReleaseAfterDetach`, and `OnOpenGlLost` use this status-returning path and
release their scheduler child lease only after native success; failed stage access remains retryable.
The ABI-compatible void release remains non-throwing and attempts only normal checked destruction, never
an unsafe context-free destructor.

hdSilk session ABI v5 uses never-reused opaque tokens backed by a process registry of shared session
states. Every operation acquires a shared state before waiting on the session mutex. Checked destruction
first closes the token to new lookups, waits for all acquired operations, tears down under stage access,
and then removes the registry entry. A failed stage-access acquisition reopens the same token for retry;
successful destruction makes stale handles return `INVALID_ARGUMENT`. The registry retains active
sessions only, so it needs no per-session tombstones. Managed explicit disposal uses checked destruction,
while its internal safe handle provides finalizer cleanup through the non-throwing compatibility release.
Native page bytes are copied into managed memory and the native page is released before `Sync` returns,
so no public command view can outlive native storage. `SilkSceneState.Apply(page)` is the supported
retained-state path. Concurrent session creation uses a separate thread-local capture token plus a
mutex-protected token-to-scene-state registry; only the plugin-created delegate may publish to the active
capture, so unrelated external delegates cannot steal another session's state.

The renderer initialization bridge is private C++ only, non-installed, and absent from generated
managed interop. While an owner-thread stage access guard is held, it passes a pointer to an ephemeral
stack `UsdStageRefPtr` view into a renderer-specific initialization callback. The callback may copy the
`UsdStageRefPtr` into its engine/session, but must never retain the pointer address. Renderer objects
retain `openusd_stage*` independently and acquire stage access for creation, sync/render, and native
teardown. Lock order is Storm GL owner thread then stage access; hdSilk session mutex then stage access;
the creation-capture registry mutex is held only for publication/take, not across engine construction.
The hdSilk session-handle registry is held only long enough to acquire/close shared state. Operation
lifetime accounting completes before taking the session mutex, and destroy never waits while holding the
registry.

ABI v8 still exposes no generic public `UsdStage*`, `void*`, or callback bridge and gains no data-ABI
capability bit for renderer integration. Its world-bounds, world-transform, camera-state, and
composition-enumeration capabilities are data-only. Native probes compile
separate, non-installed test variants
with access-begin failpoints; those test hooks and the private renderer bridge are absent from installed
headers, generated interop, and packages. Runtime soak diagnostics use separately named internal exports
for live/peak StageCore, Storm wrapper/abandon, and hdSilk session/page counts. They are intentionally
omitted from public headers and generated interop.

`StormViewportControl.SetRenderSource` accepts an application scheduler/source pair with one aggregate
ownership mode: `FullyBorrowed`, `BorrowedSchedulerOwnedSource`, or `OwnedSchedulerAndSource`. This makes
the invalid owned-scheduler/borrowed-source combination unrepresentable. The renderer still acquires an
independent child lease, so path/source replacement and GL teardown do not invalidate caller-owned stage
resources. The path properties remain a compatibility mode that owns both scheduler and source. OpenGL
loss abandons the GL engine and releases its child lease before releasing the binding; detach uses the
compatibility release before binding teardown. Owned-source release is idempotent, and an owned scheduler
whose first disposal is blocked by an active child remains retryable without releasing the source twice.

The shared-stage soak uses one scheduler/source with simultaneous Storm and hdSilk children. Its fixed,
interleaved 12,500-operation plan contains 5,000 property edits, 2,500 mesh
point/topology/create/remove edits, 2,500 composition edits (session edit target, references, payloads,
variants, and active state), and 2,500 validating no-op reads. The 10,000 workload edits plus setup and a
controlled canonical `primvars:displayColor` edit must each advance the native serial. That controlled
edit must upsert only its target mesh while the second mesh remains unchanged; hdSilk now consumes the
canonical display-color primvar rather than serializing a constant color. Exact scheduler invalidation
counters must match the declared property/topology/composition category for every changed transaction.

The bounded notification feed must preserve all edit counts while coalescing delivery. The gate also
requires topology upserts/removals, final steady pages, temporary hdSilk-session teardown racing later
edits, bounded page/mesh/GPU state, active-child disposal rejection followed by successful ordered
teardown, and authoritative baseline/peak/final counters for native StageCore objects, scheduler
children, render sources/leases, Storm wrappers/abandoned engines, hdSilk sessions/pages, and managed
GPU resources. Forced-GC checkpoints every 500 ordered operations after warmup record serial/page/mesh
correlation and enforce later-window retained-memory and working-set slope limits.

## Scene authoring and bulk inspection

Define, override, and class prim specifiers are authored explicitly:

```csharp
UsdPrim defined = stage.DefinePrim("/World/Model", "Xform");
UsdPrim over = stage.OverridePrim("/World/Existing");
UsdPrim classPrim = stage.CreateClassPrim("/Template");
```

Class prim creation accepts only absolute root prim paths. Inspection remains schema-neutral while
exposing the composed type and applied API schema names needed by viewers:

```csharp
string typeName = defined.TypeName;
string[] appliedSchemas = defined.GetAppliedSchemas();
IReadOnlyList<UsdPrim> children = stage.GetPrim("/World").GetChildren();
string[] attributes = defined.GetAttributeNames();
string[] relationships = defined.GetRelationshipNames();
```

`GetChildren` returns all direct composed children, including inactive, undefined, and abstract
children, but never descendants. Child paths, applied schemas, attribute names, and relationship
names each cross the ABI as one native-owned packed UTF-8 string list. Attribute and relationship
names are returned separately, so inspection never requires a per-property P/Invoke loop.
Invalid paths report `InvalidArgument`; inspection of a missing prim reports `NotFound`.

### Composition enumeration

Variant-set discovery uses OpenUSD's official `UsdVariantSets::GetNames` API:

```csharp
string[] variantSets = prim.GetVariantSetNames();
```

The result contains unique authored/composed names in OpenUSD's deterministic prim-index strength and
variant-set list-op order. The binding does not alphabetically sort the names. An unchanged composition
therefore returns the same order on every call. A prim with no sets returns an empty array; missing prims
report `NotFound`. Inactive prims remain inspectable. Names cross the ABI in one native-owned packed
UTF-8 string list and are decoded into detached managed strings.

Payload inspection returns immutable detached values through one bulk call:

```csharp
IReadOnlyList<UsdPayloadArc> payloads = prim.GetPayloadArcs();
foreach (UsdPayloadArc payload in payloads)
{
    Console.WriteLine(
        $"{payload.AssetPath} -> {payload.TargetPrimPath} ({payload.SourceLayerIdentifier})");
}
```

These results are the applied **direct payload-list entries**, not a snapshot of only the Pcp payload
nodes currently instantiated by the stage load set. Native code computes an expanded prim index,
visits non-ancestral nodes in deterministic strength order, and uses the public
`PcpComposeSitePayloads` API for each site. Within a site, OpenUSD's composed payload list-op order is
preserved. Explicit, prepended, and appended entries are included after composition; deleted entries
are absent. This makes unloaded and inactive prims inspectable and also preserves unresolved payload
intent for Viewer diagnostics.

`AssetPath` is the authored asset path reported by `PcpArcInfo`, so relative paths remain relative and
internal payloads remain empty. `TargetPrimPath` is empty when the author omitted a target and relied on
the target layer's default prim. `SourceLayerIdentifier` is the identifier of the layer that supplied
the composed list entry. It is reliable for identifying a layer in the current stage, but anonymous
identifiers are intentionally process-local and are not portable asset paths.

A prim with no composed direct payload entries returns an empty read-only result. A missing prim reports
`NotFound`; Pcp composition errors and unconsumed `TfErrorMark` diagnostics report `NativeError`.
The ABI owns one version-1 packed payload view containing three canonical NUL-terminated UTF-8 offsets
per arc, and managed code copies every field before releasing the native owner. No per-arc P/Invoke or
native pointer escapes.

The [Viewer Properties tab](viewer.md#stage-and-session-editing) queries these APIs only for the
selected prim on the stage scheduler. It copies variant names, current selections, and payload fields
into detached snapshots before updating UI controls. Variant edits use composition invalidation;
payload arcs remain read-only while load/unload continues to use the composed load-state API.

`UsdPrim.GetPrimIndex()` exposes the broader Pcp graph needed by a usdview-style Composition tab. It
returns one detached `PcpPrimIndex` snapshot containing strong-to-weak nodes, parent indexes, arc type,
culled/inert/spec contribution flags, introduction paths, the node layer-stack root identifier, all
contributing layer identifiers, and local Pcp error strings. The ABI shape is one native-owned versioned
view with a node-record array plus one packed string table; managed code copies the complete tree before
releasing the native owner. This deliberately leaves Pcp map-expression internals, relocation maps, and
private diagnostic subclasses out of the first surface; callers get stable paths, layers, flags, ordering,
and stringified local errors without any `pxr::` type crossing the ABI.

### Ts splines

`TsSpline` owns a double-valued OpenUSD `pxr/base/ts` spline and moves all knots across the ABI in one
bulk `TsKnot` array:

```csharp
using var spline = new TsSpline();
spline.SetData(
    [
        new TsKnot(0, 0, null, 0, 0, 0, 0, TsInterpMode.Linear,
            TsTangentAlgorithm.None, TsTangentAlgorithm.None),
        new TsKnot(1, 10, null, 0, 0, 0, 0, TsInterpMode.Held,
            TsTangentAlgorithm.None, TsTangentAlgorithm.None),
    ]);
double? value = spline.Evaluate(0.5);
```

The first surface covers authored knots, optional pre-values, pre/post tangent widths and slopes,
interpolation modes, tangent algorithms, Bezier/Hermite curve type, held/linear/sloped/looping
extrapolation records, and evaluation at a time. It intentionally leaves sampled
polyline generation, custom knot data, loop baking, anti-regression controls, and authoring breakdowns
for later; the Viewer spline plot can already copy knots once and evaluate interactively without
per-knot P/Invoke.

`GetKnots` returns only the knots. `GetData` returns the same knots together with the curve family,
both extrapolation records, and the time-valued flag as a single `TsSplineData`, and
`SetData(TsSplineData)` authors that same snapshot back:

```csharp
TsSplineData data = spline.GetData();
TsExtrapMode after = data.PostExtrapolation.Mode;
copy.SetData(data);
```

`TsSplineData` is a detached result: unlike `TsSpline`, it holds no native handle, so it can be
returned from a `UsdStageScheduler` callback, and two snapshots of an unchanged spline compare
equal. `GetKnots` now projects `GetData`, so both surfaces read the spline once. The
[Viewer Value tab](viewer.md#ts-splines-in-the-value-tab) displays that snapshot.

Authoring is double-valued: `TsSpline` creates a double spline, and the C ABI carries knot values,
pre-values, and tangent slopes as doubles. Reading is not restricted to doubles. A layer may author
a `float` or `half` spline, and OpenUSD's typed knot accessors refuse a mismatched type rather than
converting, so the native readback reads each knot field in its own type and widens it to double.
A float or half spline therefore reports its authored values rather than zeros; evaluation was
never type-restricted, because Ts evaluates in double internally. A knot field that cannot be
widened fails the whole read with a message naming the field and the spline's value type, rather
than being reported as a zero no one authored.

Widening is one-way. `TsSplineData` carries doubles and has no record of the spline's original
value type, so re-authoring a float or half spline through this surface produces a **double**
spline: the values are the widened ones, and the result may no longer be compatible with the
attribute the spline was read from. Reading a float or half spline for display is supported;
copying one back is a type change, not a copy.

`TsKnot.PreValue` is null unless the knot is dual valued. `TsKnot.GetPreValue` in OpenUSD returns
the ordinary value for a knot that is not dual valued, so the readback tests `IsDualValued` first;
otherwise every knot would come back carrying a pre-value and `SetData(GetData())` would author a
dual value onto knots that never had one.

`SetData(GetData())` is an identity for a **double-valued** spline whose knot times, values,
pre-values, and tangent slopes are all finite, including the authored pre-value of the knots that
are dual valued. That is the whole scope of the claim. A float or half spline changes value type as
described above, and the ABI rejects non-finite knot fields on the way in, so a spline carrying one
cannot be round-tripped through this surface at all.

### UsdValidation

`UsdValidation` exposes the validation registry through detached lists:

```csharp
IReadOnlyList<UsdValidationValidatorInfo> validators =
    UsdValidation.GetRegisteredValidators();
IReadOnlyList<UsdValidationError> errors = UsdValidation.Validate(stage);
```

Validator metadata is returned as records containing name, documentation, plugin name, keywords, schema
types, suite flag, and time-dependency flag. Validation results are returned as records containing
severity, validator name, error name, message, and site strings. Stage and prim validation both use
`UsdValidationContext` with all registry validators loaded, and each run crosses the ABI once as a
native-owned record array plus packed string table. Fixers, filtered contexts, layer-only validation,
time-range selection, and structured site objects are deliberately omitted; a validation panel can still
enumerate validators and render all errors for a stage or selected prim without further ABI work.

### UsdShaderRegistry

`UsdShaderRegistry` exposes the process-global OpenUSD Sdr/Ndr shader node-definition registry, not any
open stage, through the same detached-list pattern:

```csharp
UsdShaderNodeDefinitionSnapshot snapshot = UsdShaderRegistry.GetNodeDefinitionsSnapshot();
IReadOnlyList<UsdShaderNodeDefinition> definitions = UsdShaderRegistry.GetNodeDefinitions();
bool found = UsdShaderRegistry.TryGetNodeDefinitionFromAsset(
    sourceAsset, subIdentifier: null, shadingSystem: null, out UsdShaderNodeDefinition? definition);
```

`GetNodeDefinitionsSnapshot()` enumerates every shader node the registry can currently discover in one
bulk page: UsdPreviewSurface/UsdUVTexture built-ins, MaterialX standard-library nodes when the usdMtlx
discovery plugin is registered, and any MDL nodes an optional MDL SDK parser plugin has registered. Each
`UsdShaderNodeDefinition` carries identifier, name, function (family), shading system (source type),
context, resolved definition/implementation URIs, implementation name, a validity flag, and a bounded
list of `UsdShaderProperty` records (name, Sdr type, input/output direction, array and connectable
flags). The returned `UsdShaderNodeDefinitionSnapshot` also carries `IsTruncated`: the native page is
bounded (`OPENUSD_SDR_NODE_DEFINITION_MAX_NODES`/`_PROPERTIES`/`_STRING_BYTES`) and truncates rather than
growing without bound, so a caller that must know whether the registry held more than the page could
carry uses the snapshot rather than silently trusting an incomplete list. `GetNodeDefinitions()` is a
convenience wrapper over the snapshot for callers that do not need truncation awareness; it throws
`InvalidOperationException` if the page was truncated instead of returning an incomplete list silently.
`TryGetNodeDefinitionFromAsset` mirrors `UsdShadeShader`'s `info:<sourceType>:sourceAsset` /
`info:<sourceType>:sourceAsset:subIdentifier` terminals, returns `false`, never an exception, when no
registered parser plugin resolves the asset -- for example, an MDL asset with no MDL SDK parser plugin
registered -- and throws the same way `GetNodeDefinitions()` does in the (essentially unreachable, for
any real shader definition) case where the resolved node's own bounded property page was truncated.

No C++ Sdr type and no per-property call cross the ABI: every string is an index into one packed table
shared by both native calls. The retained records and their string references are pointer-free and
index-addressed, so a decoded `UsdShaderNodeDefinitionSnapshot` may be held or compared independently of
the call that produced it; only the native view struct that borrows the underlying page for the
duration of one call carries raw pointers.

### World bounds

Stage and prim bounds use one `UsdGeomBBoxCache` query and return a detached finite value:

```csharp
UsdBounds3d stageBounds = stage.GetWorldBounds();
UsdBounds3d meshBounds = stage.GetPrim("/World/Mesh").GetWorldBounds();
UsdBounds3d renderBounds = stage.GetWorldBounds(UsdGeomPurposeMask.Render);
UsdBounds3d frameBounds = stage.GetWorldBounds(
    timeCode: 24,
    purposeMask: UsdGeomPurposeMask.Default | UsdGeomPurposeMask.Proxy);
```

`UsdGeomPurposeMask` independently selects default, proxy, render, and guide geometry; `All` is the
managed default and `None` returns empty bounds. The no-time overload resolves default-time values,
while the numeric overload requires a finite time code. Prim paths are validated as absolute paths in
managed code before P/Invoke.

`UsdBounds3d` exposes `IsEmpty`, `Min`, `Max`, `Center`, and `Size`. An empty stage, missing or inactive
prim, unbounded hierarchy, or unloaded payload without a usable extents hint returns
`UsdBounds3d.Empty` rather than an error. Empty `Min`, `Max`, `Center`, and `Size` are all finite zero
vectors; callers must use `IsEmpty` to distinguish that state from a point bound at the origin.
Loaded instances and prototype prims follow `UsdGeomBBoxCache` behavior, and unloaded models can use
authored extents hints. Invalid masks, paths, time representations, or non-finite computed ranges fail;
a clean empty cache result does not.

The data ABI performs the stage or prim query in one native call. Its pointer-free 64-byte
`openusd_bounds3d` result carries `struct_size`, result version 1, normalized validity/empty flags, and
8-byte-aligned minimum/maximum double triples. Every failure resets the caller-advertised result region
deterministically to invalid, empty, zero coordinates while preserving `struct_size` and reporting the
current result version.

## Property model

`UsdPrim.GetAttributes` and `GetRelationships` create public path-based descriptors from the same
packed name lists used by the lower-level inspection API:

```csharp
UsdAttribute temperature = prim.GetAttribute("custom:temperature");
string usdType = temperature.TypeName;
UsdAttributeValueState state = temperature.GetValueState();
double[] samples = temperature.GetTimeSamples();

UsdScalarValue value = temperature.GetValue(timeCode: 10);
if (value.Kind == UsdScalarKind.Number)
{
    Console.WriteLine(value.DoubleValue);
}

UsdRelationship dependency = prim.GetRelationship("dependsOn");
string[] targets = dependency.GetTargets();
```

The tagged value path covers boolean, signed 64-bit integer, double, string, token, vec3f, color3f,
matrix4d, and the supported int32, float, double, vec2f, and vec3f arrays. The historical
`UsdScalarValue` name is retained for compatibility even though it now also carries arrays. Accessing
a payload that does not match `UsdScalarValue.Kind` throws explicitly. The default value has
`UsdScalarKind.Invalid`, and every payload accessor rejects it. Missing attributes and blocked values
report `NotFound`; typed reads of mismatched USD types report `InvalidArgument`. Existing typed
convenience methods remain available.

`TryGetValue` and `TrySet` return `false` for every non-success outcome and never throw. Because that
single `false` conflates a missing attribute, an incompatible type, and a failed native call, each
overload has a companion that reports the distinction through
`UsdAttributeTryFailureReason` without changing the return value:

```csharp
if (!temperature.TryGetValue(out UsdScalarValue value, out UsdAttributeTryFailureReason reason))
{
    // AttributeNotFound, UnsupportedValueType, or NativeCallFailed
    Console.WriteLine(reason);
}
```

`None` accompanies success. `AttributeNotFound` covers a missing attribute, `TypeIncompatible` a set
whose value kind does not match the declared USD type, `UnsupportedValueType` a get whose authored
value `UsdScalarValue` cannot represent, and `NativeCallFailed` an underlying OpenUSD failure — the
case previously indistinguishable from a legitimately absent value. The original overloads keep their
existing signatures and behaviour and delegate to the same decision path, so the reporting overloads
cannot drift from them.

`GetTimeSamples` transfers sorted sample ordinates through one bulk buffer API rather than invoking
native code per sample. `BlockValue` authors a default value block and removes authored animation;
`ClearValue` removes the default, samples, spline, or block at the current edit target.

## Contiguous geometry values

`UsdVec2f`, `UsdVec3d`, and row-major `UsdMatrix4d` complement `UsdVec3f`. Matrices use OpenUSD/Gf
row-vector semantics without transposition: affine translation is stored in `M30`, `M31`, and `M32`.
`CreateTranslation`, `ExtractTranslation`, and `TransformPoint` make that convention explicit.
`TryInvert` performs allocation-free, scaled-pivot Gauss-Jordan inversion entirely in
double precision. It returns `false` with an all-zero output for singular or non-finite inputs and
for inverses that would contain a non-finite element. `GetInverse` returns the same result or throws
`InvalidOperationException`.
Geometry-shaped values can be authored at default time or a numeric time code without per-element
native calls:

```csharp
UsdPrim mesh = stage.DefinePrim("/World/Mesh", "Mesh");
mesh.SetInt32Array("faceVertexCounts", [4]);
mesh.SetInt32Array("faceVertexIndices", [0, 1, 2, 3]);
mesh.SetVec3fArray("points",
[
    new(-1, -1, 0), new(1, -1, 0),
    new(1, 1, 0), new(-1, 1, 0)
]);
mesh.SetVec2fArray("custom:uvs",
[
    new(0, 0), new(1, 0), new(1, 1), new(0, 1)
]);
UsdMatrix4d transform = UsdMatrix4d.CreateTranslation(10, 20, 30);
mesh.SetMatrix4d("xformOp:transform", transform);
UsdVec3d worldPoint = transform.TransformPoint(new UsdVec3d(1, 2, 3));
UsdMatrix4d inverse = transform.GetInverse();
UsdVec3d localPoint = inverse.TransformPoint(worldPoint);
```

The typed array surface is `Set`/`GetInt32Array`, `FloatArray`, `Vec2fArray`, and `Vec3fArray`;
existing `DoubleArray` APIs are unchanged. New custom vector arrays use the exact neutral `float2[]`
and `float3[]` USD types. Role-bearing schema arrays use their schema-specific APIs; typed attribute
getters and setters do not silently erase or substitute roles.

Each set ABI borrows one aligned contiguous caller buffer only for the duration of the call and
copies it into a `VtArray`; native code never retains the pointer. Each get first reports an element
count with an `Ok` null/zero-capacity query, then fills one caller-owned contiguous buffer in a
single transfer. The managed wrapper owns the resulting array. Null/non-empty, misaligned,
overflowing, and undersized buffers are rejected explicitly. A `BufferTooSmall` fill does not
publish a required count; callers repeat the successful size query instead. Element counts, rather
than byte counts, are used throughout the typed ABI.

## Focused UsdGeom facade

`OpenUsd.Geom` provides schema-validated `UsdGeomImageable`, `UsdGeomXformable`, `UsdGeomXform`,
`UsdGeomMesh`, and `UsdGeomCamera` views. Focused stage extensions define concrete schemas through
OpenUSD's generated C++ schema APIs:

```csharp
using OpenUsd.Geom;

UsdGeomXform world = stage.DefineXform("/World");
UsdGeomMesh mesh = stage.DefineMesh("/World/Mesh");
UsdGeomCamera camera = stage.DefineCamera("/World/Camera");

world.Xformable.SetLocalTransform(UsdMatrix4d.Identity);
world.Xformable.SetResetXformStack(true);
UsdMatrix4d meshWorld = mesh.Xformable.GetWorldTransform();
UsdMatrix4d cameraAtFrame = camera.Xformable.GetWorldTransform(timeCode: 24);

mesh.Imageable.SetVisibility(UsdGeomVisibility.Inherited);
mesh.Imageable.SetPurpose(UsdGeomPurpose.Render);
mesh.SetTopology([4], [0, 1, 2, 3]);
mesh.SetPoints(
[
    new(-1, -1, 0), new(1, -1, 0),
    new(1, 1, 0), new(-1, 1, 0)
]);
mesh.SetNormals(
[
    new(0, 0, 1), new(0, 0, 1),
    new(0, 0, 1), new(0, 0, 1)
], UsdGeomInterpolation.Vertex);
mesh.SubdivisionScheme = UsdGeomSubdivisionScheme.None;
mesh.Orientation = UsdGeomOrientation.RightHanded;
mesh.DoubleSided = true;

camera.Projection = UsdGeomCameraProjection.Perspective;
camera.FocalLength = 50;
camera.HorizontalAperture = 24;
camera.VerticalAperture = 18;
camera.ClippingRange = new UsdVec2f(0.1f, 1000);
camera.SetTransform(UsdMatrix4d.Identity);

UsdGeomCameraState defaultOptics = camera.GetState();
UsdGeomCameraState opticsAtFrame = camera.GetState(timeCode: 24);
```

Mesh points and normals support default and numeric time samples. Extent uses `UsdExtent3f`.
Topology is authored in one schema-specific native call using contiguous face-count and index
buffers; negative values, overflow, and a count/index-total mismatch are rejected before authoring.
Normal cardinality is validated against mesh data at the requested time: constant requires one,
uniform requires the face count, vertex and varying require the point count, and face-varying
requires the face-vertex count.
The native facade uses `UsdGeomMesh::CreatePointsAttr`, `CreateFaceVertexCountsAttr`,
`CreateFaceVertexIndicesAttr`, `CreateNormalsAttr`, and the corresponding schema APIs, preserving
the standard `point3f[]`, `normal3f[]`, token, and uniform attribute declarations.

`TryWrap` returns `false` for missing or incompatible prims. `Wrap` preserves a missing-prim native
error and throws `ArgumentException` for an existing prim of the wrong schema. Imageable visibility
is computed through `UsdGeomImageable::ComputeVisibility`; purpose uses
`UsdGeomImageable::ComputePurpose`, including inherited parent purpose.
`SetLocalTransform` deliberately replaces the local operation order with one matrix xform operation
via `UsdGeomXformable::MakeMatrixXform`, while `GetLocalTransform` computes the complete local stack.
`GetWorldTransform` performs one `UsdGeomXformCache::GetLocalToWorldTransform` query and returns the
exact row-major `GfMatrix4d` as a detached `UsdMatrix4d`. It honors complete local operation stacks,
ancestor transforms, reset-xform-stack, numeric-time animation, instance proxies, and prototype prims
according to `UsdGeomXformCache`. The numeric overload rejects non-finite time before P/Invoke.
Missing and inactive prims report `NotFound`; an existing non-xformable prim reports
`InvalidArgument`; OpenUSD diagnostics and computed matrices containing any non-finite element report
`NativeError`. Native and managed ABI validation both reject non-finite results before publication.
The pointer-free `openusd_matrix4d` output is zeroed before validation and remains all-zero on every
failure. Reset-xform-stack remains an independent authored opinion.

`UsdGeomCamera.GetState()` and `GetState(double)` perform one bulk
`UsdGeomCamera::GetCamera(time)` query and derive the exact `GfCamera::GetFrustum()` window. The
detached `UsdGeomCameraState` contains projection, left/right/bottom/top at the Gf reference plane,
near/far clipping, focal length, horizontal/vertical aperture and offsets, focus distance, and
f-stop. Focus distance is in world units; f-stop is unitless and zero retains GfCamera's disabled
depth-of-field default. The numeric overload rejects non-finite time before P/Invoke. Missing or
inactive prims report `NotFound`, wrong schemas report `InvalidArgument`, and dirty diagnostics,
non-finite optics, or unordered/degenerate frusta report `NativeError`.

The ABI result is a pointer-free 120-byte version-1 structure. Callers initialize `struct_size` and
`version`; native code validates alignment and version, resets only the caller-advertised bytes,
publishes `is_valid = 1` last, and leaves a deterministic invalid zero state on every failure.
Managed validation independently checks the exact layout, version, finite values, projection,
window/clipping ordering, positive perspective near plane, positive apertures, and non-negative
focus distance/f-stop. Perspective focal length must be positive. Orthographic focal length may be
zero because `GfCamera::GetFrustum()` does not use it, but it must remain finite and non-negative.

## Focused UsdShade facade

`OpenUsd.Shade` provides schema-validated `UsdShadeMaterial`, `UsdShadeShader`,
`UsdShadeNodeGraph`, `UsdShadeConnectable`, `UsdShadeInput`, and `UsdShadeOutput`
views backed by OpenUSD's `UsdShade` C++ schema APIs:

```csharp
using OpenUsd.Geom;
using OpenUsd.Shade;

UsdGeomMesh mesh = stage.DefineMesh("/World/Mesh");
UsdPreviewSurface preview = UsdPreviewSurface.Create(
    stage,
    "/World/Looks/Material",
    "/World/Looks/Material/PreviewSurface");

preview.SetRoughness(0.6f);
preview.SetMetallic(0.2f);
preview.SetOpacity(0.9f);

UsdUvTexture texture = UsdUvTexture.Create(
    stage,
    "/World/Looks/Material/Texture",
    new UsdAssetPath("textures/albedo.png"));
preview.ConnectDiffuseColor(texture.Rgb);
preview.Material.Bind(mesh.Prim);
```

`DefineMaterial`, `DefineShader`, and `DefineNodeGraph` use the matching concrete
UsdShade `Define` methods. `Wrap` rejects an existing prim of the wrong schema with
`ArgumentException`; `TryWrap` returns `false` for missing or incompatible prims.
Shader source identifiers use `SetShaderId`/`GetShaderId`.

Inputs support float, color3f, vector3f, normal3f, token, string, and asset-path values; outputs also
support the roleless float3 type used by `UsdUVTexture`.
`UsdAssetPath` exposes the authored unresolved path. Input/output creation is idempotent only when
the existing property has the same USD value type; a mismatched type is an explicit error.
Connections are exact-type checked except for the schema-valid role-compatible float3-to-color3f
direction used by canonical preview-surface networks. Typed attribute value getters and setters
remain exact-role. Disconnect authors the standard UsdShade disconnect opinion.

`GetConnectedSources()` returns every `UsdShadeConnection` in authored order through one
native-owned packed UTF-8 list. `GetConnectedSource()` remains the single-source convenience and
throws explicitly unless exactly one source exists. Managed code decodes and releases the native
allocation immediately.

`UsdShadeConnectable` exposes common input/output authoring and discovery across shaders and
node graphs. `UsdShadeNodeGraph` forwards to the same connectable surface, so a MaterialX
consumer can enumerate authored node-graph interface inputs/outputs, walk child prims through
the stage, and resolve connections in bulk without per-element native calls.

Material terminal creation covers `surface`, `displacement`, and `volume` outputs in the
universal or named render contexts. Direct binding uses `UsdShadeMaterialBindingAPI::Apply`,
`Bind`, `UnbindDirectBinding`, and `GetDirectBinding`; the material and target prim must belong
to the same managed stage. Direct bindings can author `bindMaterialAs` as weaker or stronger
than descendants, and can target all-purpose, `preview`, or `full` material purposes. Collection
bindings accept the binding-site prim, collection prim/name, optional binding name, material,
strength, and purpose, creating the collection API if needed. `GetBoundMaterial` asks
`UsdShadeMaterialBindingAPI` to resolve direct, inherited, strength-ordered, purpose-specific, and
collection bindings for a prim. `UsdPreviewSurface` provides common diffuseColor, emissiveColor,
metallic, roughness, opacity, opacityThreshold, normal, and displacement setters without replacing
the generic input/output API. `UsdUvTexture` is a small helper for the standard shader ID, file asset
input, and canonical roleless `float3` rgb output. A primvar-reader convenience facade is deferred;
generic shader inputs, outputs, and connections remain sufficient to author that node explicitly.

## Focused UsdLux facade

`OpenUsd.Lux` provides exact-schema wrappers for `UsdLuxDistantLight`, `UsdLuxSphereLight`,
`UsdLuxRectLight`, `UsdLuxDiskLight`, `UsdLuxDomeLight`, and `UsdLuxCylinderLight`. Definitions and
property authoring use OpenUSD's generated UsdLux schema APIs behind the project C ABI:

```csharp
using OpenUsd.Lux;

UsdLuxDistantLight sun = stage.DefineDistantLight("/World/Lights/Sun");
sun.Light.Intensity = 4.5f;
sun.Light.Exposure = 2;
sun.Light.Color = new UsdVec3f(1, 0.8f, 0.6f);
sun.Angle = 0.75f;

UsdLuxSphereLight spot = stage.DefineSphereLight("/World/Lights/Spot");
spot.Radius = 0.25f;
spot.Light.Normalize = true;
UsdLuxShaping shaping = spot.Light.ApplyShaping();
shaping.Focus = 2.5f;
shaping.ConeAngle = 35;
shaping.ConeSoftness = 0.2f;

UsdLuxRectLight panel = stage.DefineRectLight("/World/Lights/Panel");
panel.Width = 3;
panel.Height = 2;
panel.TextureFile = new UsdAssetPath("textures/panel.exr");
panel.Xformable.SetLocalTransform(UsdMatrix4d.Identity);

UsdLuxDomeLight environment = stage.DefineDomeLight("/World/Lights/Environment");
environment.TextureFile = new UsdAssetPath("textures/studio.hdr");
```

Every concrete wrapper exposes shared `UsdLuxLightAPI` controls through `Light`: intensity,
exposure, color, color-temperature enable/value, normalize, and diffuse/specular contribution
multipliers. `Xformable` reuses the existing `UsdGeomXformable` matrix/reset-stack surface.
Type-specific controls are distant angle; sphere/disk/cylinder radius; rect width/height; cylinder
length; and rect/dome texture assets. These focused controls author default-time values.

`TryWrap` returns `false` for missing or wrong-schema prims. `Wrap` preserves `NotFound` for a
missing prim and throws `ArgumentException` for an existing prim of another concrete schema.
Managed setters reject invalid domains before authoring, and native entry points enforce the same
rules: intensity, dimensions, and radii are non-negative; color temperature is 1000K through
10000K; distant angle is at least 0 and less than 360 degrees; shaping focus is non-negative; cone
angle is 0 through 180 degrees; and cone softness is 0 through 1. Non-finite scalar/color values,
relative prim paths, and properties unsupported by a concrete schema are explicit errors.

Shaping is never applied as a side effect of setting a value. `ApplyShaping` calls
`UsdLuxShapingAPI::CanApply`/`Apply` and returns a focused helper; `GetShaping` requires the API to
already be present. Rect and dome are the only exposed texture-file schemas in this slice. Dome
texture format/guide radius, sphere/cylinder treat-as hints, and additional shaping texture controls
remain available for a future broader Lux surface.

## Focused UsdSkel facade

`OpenUsd.Skel` provides schema-validated `UsdSkelRoot`, `UsdSkelSkeleton`,
`UsdSkelAnimation`, `UsdSkelBlendShape`, and `UsdSkelBinding` views backed by
OpenUSD's UsdSkel schema APIs:

```csharp
using OpenUsd.Geom;
using OpenUsd.Skel;

UsdSkelRoot root = stage.DefineSkelRoot("/World/Character");
UsdSkelSkeleton skeleton = stage.DefineSkeleton("/World/Character/Skeleton");
UsdSkelAnimation animation = stage.DefineAnimation("/World/Character/Animation");
UsdSkelBlendShape smile = stage.DefineBlendShape("/World/Character/Smile");
UsdGeomMesh mesh = stage.DefineMesh("/World/Character/Mesh");

string[] joints = ["Root", "Root/Arm"];
skeleton.SetJoints(joints);
skeleton.SetBindTransforms([UsdMatrix4d.Identity, armBindTransform]);
skeleton.SetRestTransforms([UsdMatrix4d.Identity, armRestTransform]);

animation.SetJoints(joints);
animation.SetTranslations([new(0, 0, 0), new(0, 1, 0)]);
animation.SetRotations([UsdQuatf.Identity, UsdQuatf.Identity]);
animation.SetScales([new(1, 1, 1), new(1, 1, 1)]);
animation.SetRotations(sampledRotations, timeCode: 10);

smile.SetOffsets(smileOffsets);
smile.SetNormalOffsets(smileNormalOffsets);
smile.SetPointIndices(smilePointIndices);
smile.SetInbetween("half", 0.5f, halfSmileOffsets);

root.ApplyBinding().SetSkeleton(skeleton);
skeleton.ApplyBinding().SetAnimationSource(animation);

UsdSkelBinding meshBinding = UsdSkelBinding.Apply(mesh.Prim);
meshBinding.GeomBindTransform = UsdMatrix4d.Identity;
meshBinding.SetJointInfluences(
    jointIndices,
    jointWeights,
    elementSize: 2,
    UsdSkelInterpolation.Vertex);
meshBinding.SkinningMethod = UsdSkelSkinningMethod.ClassicLinear;
meshBinding.SetBlendShapes(["smile"]);
meshBinding.SetBlendShapeTargets([smile]);
```

`DefineSkelRoot`, `DefineSkeleton`, `DefineAnimation`, and `DefineBlendShape` use the
corresponding generated UsdSkel schema `Define` methods. `TryWrap` returns `false` for missing or
incompatible prims; `Wrap` preserves a missing-prim error and rejects an existing prim of the wrong
exact schema.
Binding `Apply` uses `UsdSkelBindingAPI::CanApply`/`Apply`, while `Wrap` requires the API to
already be present.

Every managed Skel entry validates absolute prim and relationship-target paths before P/Invoke;
relative paths, the pseudo-root, and property paths are rejected with `ArgumentException`.
Schema, matrix-property, animation-property, binding-relationship, and interpolation enum casts
are domain-checked and rejected with `ArgumentOutOfRangeException`. Schema wrappers preserve a
validated absolute-path invariant from definition or wrapping through every subsequent operation.

Joint tokens and blend-shape channel names cross the ABI in one packed UTF-8 buffer plus offsets.
Set calls borrow that caller-owned buffer only for the call. Get calls return one native-owned
packed string list, which managed code decodes and releases immediately. Matrix, vector,
quaternion, joint-index, weight, blend-shape offset, normal-offset, and point-index arrays are
contiguous caller-owned buffers borrowed synchronously by native code. Reads use a size query
followed by one bulk fill; native code never retains a buffer. Quaternion layout is scalar first
(`real`, `x`, `y`, `z`), and animation scales are converted to and from UsdSkel's `half3[]`
schema attribute.

Skeleton joints must be unique relative prim paths in valid parent-before-child
`UsdSkelTopology` order. Bind/rest transform counts must equal the skeleton joint count.
Animation joint tokens must be unique relative paths; every authored translation, rotation,
or scale sample must match the animation joint count. Numeric values and time codes must be
finite, rotations must be unit quaternions, and scale components must fit the schema's
half-precision representation.

Skeleton and animation-source relationship targets must exist on the same stage and have the
requested schema. Joint influences require a valid inherited skeleton. Index and weight arrays
must be non-empty, equal in length, divisible by `elementSize`, and use matching interpolation
and element-size metadata. Indices are range-checked against the skeleton topology; weights must
be finite and non-negative. Managed code performs shape, interpolation, weight, and index-range
validation before the authoring P/Invoke and throws standard argument exceptions. The native ABI
repeats validation defensively. The focused facade exposes constant and vertex interpolation and
does not normalize weights. Animation joint order may intentionally differ from skeleton topology
order.

Blend-shape authoring covers offsets, normal offsets, point indices, named inbetweens with weights,
binding channel names, target relationships, and the binding skinning-method token. Inbetween
queries return the weight and offset arrays as one detached value. Blend-shape array lengths and
finite weights are validated before dispatch, and target relationships must point at existing
`UsdSkelBlendShape` prims. Skeleton-cache evaluation, skinning computation, packed joint animation
facades, and broader UsdSkel utilities remain outside this viewer-authoring slice.

## Prim lifecycle

`UsdStage.HasPrim`/`RemovePrim` and `UsdPrim.Exists`/`SetActive`/`IsActive` cover existence, removal,
and activation. Visibility and purpose are authored through the standard `UsdGeomImageable` token
attributes (`visibility`, `purpose`) via `UsdPrim.SetVisibility`/`GetVisibility` and
`SetPurpose`/`GetPurpose`, so no separate native entry points were needed for those two properties.

## Relationships

```csharp
UsdPrim prim = stage.GetPrim("/World/Sensor");
prim.CreateRelationship("dependsOn");
prim.SetRelationshipTargets("dependsOn", ["/World/A", "/World/B"]);
string[] targets = prim.GetRelationshipTargets("dependsOn");
prim.ClearRelationshipTargets("dependsOn");
```

Target paths cross the ABI as one packed, null-terminated UTF-8 buffer plus an offset table
(`openusd_string_list_view`), in both directions, avoiding per-element P/Invoke. A relationship with
no authored targets composes to an empty array rather than an error.

## Composition

```csharp
prim.AddReference("asset.usda", "/Ref");   // optional prim path
prim.AddPayload("payload.usda", "/Payload");
prim.SetInstanceable(true);
prim.ClearReferences();
```

Local inherits and specializes use existing absolute prim paths and author at the current edit
target:

```csharp
prim.AddInherit("/Classes/Base");
prim.AddSpecialize("/Classes/Fallback");
prim.ClearInherits();
prim.ClearSpecializes();
```

This API deliberately requires the source and target prims to exist on the stage, even though lower
OpenUSD layers can contain unresolved list-op paths. Invalid paths report `InvalidArgument`; missing
source or target prims report `NotFound`.

Payload load state and native instancing can be inspected directly:

```csharp
prim.Unload();
bool loaded = prim.IsLoaded();
prim.Load();

if (prim.IsInstance())
{
    string prototypePath = prim.GetPrototypePath();
    bool prototype = stage.GetPrim(prototypePath).IsPrototype();
}
```

`IsLoaded` follows OpenUSD composition semantics: a non-loadable prim is considered loaded when it
has no unloaded loadable ancestor. Prototype paths are generated implementation paths valid for the
current stage composition; they should not be persisted as asset identifiers. Asking a non-instance
for a prototype path, or directly loading/unloading a prototype prim, reports `InvalidArgument`.

Population masks are transferred through one packed UTF-8 path list:

```csharp
using UsdStage focused = UsdStage.OpenMasked(
    "city.usda",
    ["/World/Hero", "/World/Props/Chair"]);
```

Mask entries must be absolute prim paths. OpenUSD normalizes redundant descendants and includes the
ancestors required to reach each requested subtree; unrelated prims are not populated or traversed.
An empty mask opens a stage containing no populated prims. `OpenMasked` uses OpenUSD's default
`LoadAll` policy for payloads included by the mask.

Sublayers are authored on a layer, typically the stage's root layer:

```csharp
using UsdLayer rootLayer = stage.GetRootLayer();
rootLayer.AddSublayer("base.usda");
string[] sublayers = rootLayer.GetSublayerPaths();
rootLayer.RemoveSublayer(sublayers[0]);
```

## Variants

```csharp
prim.AddVariantSet("look");
prim.AddVariant("look", "red");
prim.AddVariant("look", "blue");
prim.SetVariantSelection("look", "red");
string[] names = prim.GetVariantNames("look");
string selected = prim.GetVariantSelection("look"); // throws if no selection is authored
prim.SetVariantSelection("look", null);             // clears the selection
```

Variant set/variant creation and selection are supported; authoring prim content inside a specific
variant (`UsdEditContext`/`GetVariantEditContext`) is out of scope for this phase.

## Metadata

Common `string`/`bool`/`int64`/`double` metadata is stored per-key using safe tagged native calls
(`openusd_metadata_value`), on a prim's `customData` dictionary or a layer's `customLayerData`
dictionary (stage-level metadata is authored through the stage's root layer):

```csharp
prim.SetMetadata("owner", "team-sensors");
prim.SetMetadata("verified", true);
string owner = prim.GetMetadataString("owner");
prim.ClearMetadata("owner");

using UsdLayer rootLayer = stage.GetRootLayer();
rootLayer.SetMetadata("buildId", "abc123");
```

Reading a metadata key with the wrong requested type (for example `GetMetadataBool` on a key holding
a string) throws an `OpenUsdNativeException` explicitly rather than silently coercing the value.

## Native ABI contract and runtime discovery

`OpenUsd.Interop` intentionally exposes only the versioned ABI contract, runtime discovery and plugin
registration, native status codes, and typed native exceptions:

```csharp
OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
Console.WriteLine(OpenUsdNativeRuntime.Version);

if (OpenUsdNativeRuntime.AbiVersion != OpenUsdNativeContract.AbiVersion)
{
    throw new InvalidOperationException("The loaded native ABI is incompatible.");
}
```

Native stage/layer handles and ABI transport structs are implementation details behind `UsdStage`,
`UsdLayer`, and the schema APIs rather than package-consumer contracts.

Internally, stages and layers are deterministic `SafeHandle` owners. Prim traversal, relationship targets,
sublayer paths, variant names, and variant-set names all cross the ABI as one native-owned packed
string buffer. Payload arcs use a parallel native-owned packed view with three strings per result.
Numeric and vector arrays use caller-owned contiguous bulk transfers rather than per-element P/Invoke.
`ChangeSerial` is incremented by a stage-scoped `UsdNotice::ObjectsChanged` listener and can be polled
without invoking managed callbacks from OpenUSD threads.

`eng/generate-interop.py` derives the checked-in `[LibraryImport]` declarations from
`native/openusd_dotnet/include/openusd_dotnet.h`. CI fails if `OpenUsdNativeMethods.g.cs` is stale.
Data ABI v15 preserves every v14 export and capability and adds explicit `color3f[]`,
`bool[]`, `token[]`, and `string[]` attribute array accessors through
`OPENUSD_CAPABILITY_ATTRIBUTE_ARRAYS_V2` (`0x20000`). The managed required mask is
`0x3FFFFFF`.

The additive `OPENUSD_CAPABILITY_BOUNDED_STAGE_INSPECTION` capability (`0x40000`)
adds allocation-free prim-count and total-path-byte preflight before packed path
materialization. `OPENUSD_CAPABILITY_SESSION_OVERLAY` (`0x80000`) adds the removable
strongest-opinion session sublayer, and `OPENUSD_CAPABILITY_PHYSICS_BAKE` (`0x100000`)
adds batched preview and transactional bake page authoring.
Data ABI v16 preserves every v15 export and capability and adds
`OPENUSD_CAPABILITY_RESOLVER_CONTEXT_INSPECTION` (`0x1000000`) for resolver contexts, bulk asset
resolution, and plugin enumeration.
Data ABI v17 preserves every v16 export and capability and adds
`OPENUSD_CAPABILITY_SDR_NODE_DEFINITION_QUERY` (`0x2000000`) for bulk, read-only shader
node-definition registry introspection.
Current managed startup requires the v17 `0x3FFFFFF` mask and rejects
older runtimes or runtimes missing required exports. Every status-returning
export executes its complete body inside the common exception/TfError guard, so C++ exceptions and
unconsumed OpenUSD diagnostics never cross C. Access-end alone performs its already-validated
owner-thread `noexcept` commit after the guard.
Clean false typed reads, including blocked or declared-but-unvalued attributes, return `NotFound`;
dirty `TfErrorMark` reads return `NativeError`.

Every non-null output is initialized inside that outer guard before any other argument validation:
handles and list owners become null; counts, sizes, serials, scalars, enums, tags, and POD values
become zero; string buffers are NUL-terminated at byte zero when capacity permits; and string-list
views become empty. Versioned POD outputs are zeroed only through the caller-advertised size while
preserving `struct_size`.

The 14 writable bulk getters cover 15 buffers because joint influences return indices and weights.
On every non-success status, each non-null buffer with nonzero, non-overflowing capacity is cleared
across its entire caller-declared element capacity; required counts and Skel metadata remain zero.
Null or zero-capacity buffers are never dereferenced, and an impossible byte-count multiplication is
rejected without touching the pointer. Successful fills write once without a preliminary clear.

Every caller-supplied versioned view or tagged value (`openusd_string_list_view`,
`openusd_payload_arc_list_view`, `openusd_metadata_value`, `openusd_scalar_value`,
`openusd_bounds3d`) carries a `struct_size` field that the native layer validates before use.
Version-2 string-list views and version-1 payload-arc views carry `offsets_size`; native
decoding validates count multiplication, table alignment and size, canonical contiguous offsets,
bounded terminators, and trailing data before constructing any USD path or token. Managed output
decoding also requires canonical contiguous offsets, one exact terminal NUL per field, no trailing
bytes, and strict UTF-8. Managed direct strings and packed-list inputs use a NativeAOT-safe UTF-8
marshaller that rejects embedded NUL while preserving Unicode.
Float3, vector3f, normal3f, point3f, color3f, and their arrays are role-exact on typed get/set paths.
Matrix4d extended the tagged scalar struct append-only; older scalar payload sizes remain valid for
the pre-existing kinds.

## Asset resolution and plugin trees

`UsdResolver`, `UsdResolverContext`, and `UsdPluginRegistry` expose the upstream `ArResolver` and
`PlugRegistry` without letting OpenUSD call back into managed code. A vendor resolver is a native
OpenUSD plugin discovered from its own plugin tree; the managed surface only creates contexts,
scopes them, and reads results.

```csharp
UsdPluginRegistry.Register(Path.Combine(AppContext.BaseDirectory, "plugins"));

using UsdResolverContext context = UsdResolverContext.Create(
[
    new UsdResolverContextString(string.Empty, assetRoot),
    new UsdResolverContextString("vendor", "vendor-context-string")
]);

IReadOnlyList<UsdResolvedAsset> resolved = UsdResolver.Resolve(
    ["kitchen.usda", "vendor://props/chair.usdc"],
    context);

using UsdStage stage = UsdStage.Open("kitchen.usda", context);
```

Resolution is bulk by contract. `UsdResolver.Resolve` packs every requested path into one
string-list view, binds the supplied context once for the whole batch, and returns one
`UsdResolvedAsset` per input in request order, so a scene-sized dependency list costs one P/Invoke
rather than one per asset. Each record carries the resolver identifier, resolved path, extension,
asset version and name, whether the path resolved, whether it is context dependent, and the
resolver-reported modification timestamp when the resolver has one. An unresolved path is reported
as a record with `IsResolved` false and an empty resolved path rather than as an exception, so a
batch containing missing assets still returns every sibling result.

A record only ever reports a resolved path for an asset the resolver also gave an identifier.
When `ArResolver::CreateIdentifier` returns nothing, the resolver has declined to identify the
asset at all, and upstream composition treats that as unusable — `SdfLayer` and `UsdStage` will not
open it. The record is therefore reported unresolved rather than falling back to resolving the raw
asset path, because that fallback would claim a resolved path for an asset a stage opened with the
same context cannot load.

A `UsdResolverContext` is an immutable copy of the native `ArResolverContext` and can be shared
between threads. A *binding* is not: `UsdResolverContext.Bind` installs a thread-local
`ArResolverContextBinder`, so the returned `UsdResolverContextBinding` must be disposed on the
thread that created it and before any binding created after it. Both violations throw
`InvalidOperationException` — they describe a caller that used the binding wrongly, not a native
failure — and both leave the binding intact and still bound, so the owner thread can still release
it in the right order. A binding is only marked disposed after the native release actually
succeeds. Never hold a binding across an `await`: a continuation that resumes on another thread
would resolve without the binding and would fail to release it. Callers that only need a context
for one batch or one stage should pass it to `UsdResolver.Resolve` or
`UsdStage.Open(path, context)` instead of binding it, because both bind and unbind internally
around the whole operation.

Passing a context to `Resolve` always binds it, including an empty one. An empty context is not
"no context": binding it shadows whatever the calling thread already has bound, which is the only
way to ask for resolution that ignores the ambient context. Passing `null` is what means "use
whatever is already bound".

```csharp
using UsdResolverContext ambient = UsdResolverContext.Create(
    [new UsdResolverContextString(string.Empty, assetRoot)]);
using UsdResolverContext none = UsdResolverContext.CreateDefault();

using (ambient.Bind())
{
    UsdResolver.Resolve(["kitchen.usda"]);        // uses the ambient binding
    UsdResolver.Resolve(["kitchen.usda"], none);  // shadows it for this batch only
}
```

`UsdResolver.PrimaryTypeName` and `GetRegisteredUriSchemes` report what the loaded plugin set
actually selected. `GetAvailableResolverTypeNames` is narrower than its name suggests: it lists the
*primary-resolver candidates*, and upstream marks any resolver that declares URI/IRI schemes as
never eligible to be primary, so a live vendor URI resolver never appears there. Use
`GetRegisteredUriSchemes` to discover those. `ArDefaultResolver` is always a candidate, because it
is the fallback OpenUSD uses when no plugin resolver claims the primary role.

OpenUSD selects and constructs its one primary resolver on first use, and upstream documents
resolver discovery as unsafe concurrently with that construction. The shim serializes its own calls
under a single mutex, but a first use that originates anywhere else - a plain `UsdStage.Open`, a
layer read, or any upstream code - cannot be serialized from managed code. The first-use contract
therefore stands: register every plugin tree, then touch resolution once from a single thread,
before the process goes concurrent.

`UsdResolverContext.Refresh` is process-wide despite hanging off one context. It invalidates
previously resolved paths for every thread and every open stage and sends OpenUSD's
`ArNotice::ResolverChanged`, whose listeners mutate their own state while handling it. Upstream
documents concurrent refreshes of the same context as unsafe, so call it from one thread at a
quiescent point - not from a worker while other threads resolve or compose. Refreshing a context
that currently has a live binding *anywhere in the process* is rejected with an
`OpenUsdNativeException`. The rejection is matched by `ArResolverContext` value identity, so an
unrelated bound context never causes it and this context bound on another thread always does, and
it is retryable: the same call succeeds once the binding is released.

`UsdPluginRegistry.Register` takes a `plugInfo.json` file or a directory containing one and returns
the number of newly registered plugins. `GetRegisteredPlugins` enumerates every discovered plugin,
ordered by name, with its kind, load state, library path, and resource path. The tree is read
exactly as it is laid out on disk: nothing is merged or rewritten, so a third-party resolver keeps
its own `plugInfo.json`, its own resources directory, and its own library path. A monolithic
runtime build reports an empty library path for plugins aggregated into the monolith, and the
registry value is passed through verbatim rather than guessed at. See
[Packaging](packaging.md#third-party-resolver-plugin-contract) for the packaged layout that
contract requires.

## Pure managed helpers

`OpenUsd.UsdPath.IsAbsolutePrimPath`/`ValidateAbsolutePrimPath` validate prim paths in managed code
before crossing the native boundary. Their fixed Unicode `XID_Start`/`XID_Continue` tables match the
pinned OpenUSD 26.05 runtime, including underscore as an identifier start. Native-independent unit
tests exercise them alongside `UsdVec2f`, `UsdVec3f`, `UsdMatrix4d`, `UsdBounds3d`, `UsdPayloadArc`,
and the internal packed-string encoding/decoding helpers.

### UsdGeom schema facades

Data ABI v10 adds `OPENUSD_CAPABILITY_USDGEOM_SCHEMA_COMPLETE` (`0x1000`). The `OpenUsd.Geom`
facade now defines and wraps the remaining focused geometry schemas: subsets, curves, points,
point instancers, implicit surfaces, tetrahedral meshes, primvars, and model draw-mode data. Array
attributes such as points, curve vertex counts, topology, primvar values, and instancer positions
cross the native boundary as contiguous buffers; the managed facade does not introduce per-element
P/Invoke on authoring paths.

## Focused UsdVol, UsdRender, UsdMedia, UsdProc, and UsdUI facades

The data API now includes focused schema views for volume assets, render settings, spatial media,
generative procedurals, and selected UI metadata. The locked native profile includes OpenVDB runtime
support for referenced `.vdb` assets. Rendering support is limited to the Vulkan and D3D12 single-density
OpenVDB gate described in the support matrix; it is not part of Storm cross-renderer parity.

```csharp
UsdVolVolume volume = stage.DefineVolume("/World/Volume");
UsdVolOpenVDBAsset density = stage.DefineOpenVDBAsset("/World/Fields/Density");
density.FilePath = new UsdAssetPath("volumes/smoke.vdb");
density.FieldName = "density";
volume.SetField("density", UsdVolVolumeFieldBase.Wrap(density.Prim));

UsdRenderSettings settings = stage.DefineRenderSettings("/Render/Settings");
UsdRenderProduct product = stage.DefineRenderProduct("/Render/Product");
UsdRenderVar color = stage.DefineRenderVar("/Render/Vars/Color");
product.SetOrderedVars([color]);
settings.SetProducts([product]);

UsdMediaSpatialAudio audio = stage.DefineSpatialAudio("/World/Sound");
audio.FilePath = new UsdAssetPath("audio/ambience.wav");
UsdMediaAssetPreviews previews = UsdMediaAssetPreviews.Apply(stage.GetPrim("/World"));
previews.DefaultThumbnail = new UsdAssetPath("thumbs/default.png");

UsdProcGenerativeProcedural proc = stage.DefineGenerativeProcedural("/World/Proc");
UsdUINodeGraphNode nodeUi = UsdUINodeGraphNode.Apply(proc.Prim);
```

Applied API schemas follow the existing standalone facade pattern used by `UsdSkelBinding`:
`Apply(UsdPrim)`, `TryWrap(UsdPrim, out ...)`, and `Wrap(UsdPrim)` live on the API facade type.
Relationship lists such as volume field bindings, render products, and ordered render vars cross the
native boundary in bulk. `UsdMediaAssetPreviewsAPI` is present in the pinned OpenUSD 26.05 build and
is exposed for default thumbnail asset metadata.

The exposed UsdVol slice covers `Volume`, `VolumeFieldBase`, `VolumeFieldAsset`, `FieldBase`,
`FieldAsset`, the `OpenVDBAsset` schema, and `Field3DAsset`. Particle-field schemas and UsdVol particle API
schemas are intentionally left out of this volume-asset authoring slice. The exposed UsdRender slice
covers `RenderSettingsBase`, `RenderSettings`, `RenderProduct`, `RenderVar`, and `RenderPass`; this
OpenUSD version has no generated `RenderDenoisePass` schema. UsdUI covers `Backdrop`,
`NodeGraphNodeAPI`, and `SceneGraphPrimAPI`; accessibility and hint API schemas remain outside this
focused surface.
