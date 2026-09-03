// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HDSILK_TEST_HOOKS_H
#define OPENUSD_HDSILK_TEST_HOOKS_H

#include "openusd_hdsilk.h"

#ifdef __cplusplus
extern "C" {
#endif

OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_external_delegate_does_not_publish(void);

OPENUSD_HDSILK_API size_t
openusd_hdsilk_test_get_session_in_flight(openusd_silk_session* session);

/// Drops the delegate's cached MDL adapter so the next material resolution
/// re-reads OPENUSD_MDL_ADAPTER_PATH and OPENUSD_MDL_MODULE_PATH.
///
/// The cache is deliberately once-per-process in production: an adapter that
/// could be swapped mid-frame would let two materials in one page disagree about
/// what they were shaded from. A probe that has to prove several configurations
/// therefore needs this hook rather than a weaker cache.
OPENUSD_HDSILK_API void
openusd_hdsilk_test_reset_mdl_adapter(void);

/// Number of OpenSubdiv refiners built since process start.
///
/// This is the only direct evidence that refined topology is cached across
/// frames: a mesh whose points animate must re-run interpolation without moving
/// this counter.
OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_subdivision_refiner_build_count(void);

/// Number of meshes refused refinement with a diagnostic since process start.
OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_subdivision_diagnostic_count(void);

/// Overrides the refined-vertex budget so the bound can be exercised without
/// authoring a control cage large enough to overflow the shipped one. Zero
/// restores the shipped budget.
OPENUSD_HDSILK_API void
openusd_hdsilk_test_set_subdivision_vertex_budget(uint64_t budget);

/// Runs the ABI v20 published-rig self-verification against a constructed
/// one-point rig, returning non-zero when the rig verifies.
///
/// Both flags select whether that side of the comparison carries a degenerate
/// normal. The rule is that only a two-sided degeneracy is ignorable: a rig
/// that collapses a normal the CPU deformation kept, or that keeps one the CPU
/// deformation collapsed, disagrees about the surface and must be refused. That
/// case cannot be produced from a stage fixture, so it is constructed here.
OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_verify_degenerate_normal_rule(
    int32_t resolvedDegenerate,
    int32_t evaluatedDegenerate);

/// Number of ND_surface_unlit fragment generations that failed since process
/// start.
///
/// A generation failure no longer changes the published surface kind: the
/// material stays OPENUSD_SILK_SURFACE_MATERIALX_GENERATED with an empty
/// payload, because ND_surface_unlit is unlit whether or not a fragment could be
/// produced for it, and downgrading it to OPENUSD_SILK_SURFACE_UNSUPPORTED
/// handed an unlit surface to the shaded fallback. The failure therefore has no
/// representation in the page, and this counter is where it is observed from.
OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_generated_surface_failure_count(void);

/// Forces the next ND_surface_unlit fragment generation to fail.
///
/// The real failure path cannot otherwise be reached in a build that generates
/// successfully, and it is exactly the path whose behaviour matters: a probe
/// that only ever saw success would gate the empty-payload rule against a
/// hand-built record rather than against what the delegate publishes. Zero
/// restores normal generation.
OPENUSD_HDSILK_API void
openusd_hdsilk_test_set_generated_surface_failure(int32_t fail);

#ifdef __cplusplus
}
#endif

#endif
