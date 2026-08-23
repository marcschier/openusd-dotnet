# Test assets

Only small, redistribution-compatible USD fixtures belong here. Every imported asset must record its source,
revision, license, and intended test coverage.

`viewer-stage-camera-smoke.usda` is repository-authored under this repository's MIT license. It
provides asymmetric visible geometry and a nested, sampled, off-axis `UsdGeomCamera` for the bounded
Viewer Storm/D3D12/Vulkan authored-camera evidence scenario.

`viewer-physics-smoke.usda` is repository-authored under this repository's MIT license. It provides
a physics scene, a static collider, and two rigid bodies with colliders so the Viewer physics smoke
can extract real bindings, simulate real frames, and apply real transform overrides.
