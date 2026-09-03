// Copyright (c) marcschier. Licensed under the MIT License.
//
// Resolves UsdLux link categories onto the composed instance identities hdSilk
// publishes, including under nested point instancing.
//
// hdSilk does not draw instances: it flattens every resolved instance into its
// own wire record whose identity is (prototype path, composed instance index).
// Under nesting that index is the mixed-radix composition HdSilkInstancer
// builds -- parentIndex * innerInstanceCount + innerIndex, repeated per level.
// Hydra, by contrast, reports link categories one level at a time: one array
// per instance of each instancer, indexed by that instancer's own instance
// index. Neither array addresses a composed identity, so a mask resolved from
// either alone would be applied to the wrong instances.
//
// This module is the mapping between the two. It enumerates exactly the
// composed identities the instancer chain publishes, resolves each one's
// categories from the levels that make it up, and emits a membership row only
// where the result differs from the row the prototype path already publishes.
// It never emits a row for an identity no record is published under, and it
// never falls back to an index it did not resolve.

#ifndef HDSILK_INSTANCE_LINKING_H
#define HDSILK_INSTANCE_LINKING_H

#include "sceneState.h"

#include "pxr/pxr.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/// One level of the instancer chain that reaches a published prototype,
/// ordered from the root instancer down to the instancer the Rprim names.
struct HdSilkInstancerLevel
{
    /// The instancer's own path. Diagnostic only: the published identity is
    /// (prototype path, composed instance index).
    std::string path;

    /// The authoritative instance count of this instancer, which is the radix
    /// hdSilk composes this level's index against. It is this instancer's own
    /// instance-primvar length and never a per-prototype value, exactly as
    /// HdSilkInstancer resolves it: a radix widened to fit one prototype's
    /// samples would give the instancer's other prototypes a different index
    /// space. It is unused for the root level, whose index is the most
    /// significant digit and is multiplied by nothing.
    int64_t instanceCount = 0;

    /// The instancer-relative indices this level publishes for the child it
    /// scatters -- the next level's instancer, or the prototype at the leaf --
    /// exactly as Hydra reports them through GetInstanceIndices. A negative
    /// index addresses no instance primvar element, so it draws nothing and is
    /// dropped here for the same reason HdSilkInstancer drops it.
    std::vector<int> publishedIndices;

    /// Hydra's per-instance categories for this instancer, one array per
    /// instance of the instancer. An empty vector is Hydra's own answer for a
    /// delegate that reports no per-instance membership at all, and means every
    /// instance of this level resolves to whatever the level itself resolves
    /// to; it is not a missing answer and is never diagnosed as one.
    std::vector<std::vector<std::string>> instanceCategories;
};

/// The composed identities this module could not resolve exactly, so a caller
/// diagnoses them against a named prototype instead of publishing a mask it
/// guessed. Every counted identity keeps the prototype path's own row, which is
/// the documented fallback, rather than being given another instance's mask.
struct HdSilkNestedLinkDiagnostics
{
    /// Indices a level published that its own authoritative instance count
    /// cannot explain. HdSilkInstancer drops those samples as well, so they
    /// name no published record here either.
    size_t uncomposableIndices = 0;

    /// Indices a level published for which it reported a per-instance category
    /// array that does not reach them. The instance's membership is unknown
    /// rather than empty, so no row is emitted for it or -- when the level is
    /// an ancestor -- for anything composed beneath it.
    size_t unresolvedIndices = 0;

    /// Composed identities that do not fit the signed 32-bit instance index the
    /// page ABI carries. One count is one pruned subtree, which may stand for
    /// more than one identity.
    size_t unrepresentableIndices = 0;

    bool Any() const
    {
        return uncomposableIndices != 0 ||
            unresolvedIndices != 0 ||
            unrepresentableIndices != 0;
    }
};

/// Appends the per-instance membership rows one prototype publishes under an
/// instancer chain of any depth.
///
/// "primCategories" is the prototype Rprim's own category set, which is the row
/// the prototype path publishes and therefore the fallback every composed
/// instance inherits. "sharedCategories" is the union of the path-wide
/// categories the instancers in the chain report: Hydra states that an
/// instancer's own categories apply to every one of its instances, so they
/// reach every composed instance below equally. They are folded into every
/// resolved instance and into the prototype's own effective row, so they can
/// never make an instance differ from its path and can never be dropped by an
/// instance that overrides its own level.
///
/// "levels" is the chain from the root instancer to the instancer the Rprim
/// names, which must not be empty. The composed index of an identity is
/// levels[0].publishedIndices[i0] for the root and
/// composed * levels[k].instanceCount + ik for every deeper level, which is
/// exactly what HdSilkInstancer publishes.
///
/// A row is emitted only where the resolved categories differ from the
/// prototype's effective row, because an instance that resolves to it is
/// already described by it. Rows come out in ascending composed index order, so
/// an unchanged scene produces the same rows every frame.
///
/// "rowLimit" is a raw-memory policy and nothing else. It bounds how many
/// unresolved rows one call may materialize, because a category set that
/// differs is not yet a mask that differs and the resolution that decides is
/// downstream of here. It is deliberately not the page ABI's entry budget:
/// admitting entries against that budget before the masks are known charges a
/// prim for linking to everything, and a scene of such prims then crowds out
/// the one prim a collection really excluded. See
/// HdSilkMaxCollectedInstanceRows.
///
/// Returns false when the limit stopped it, leaving "outMemberships" holding
/// every row that fitted. The limit is checked before the vector is grown, so a
/// caller's bound is never exceeded even momentarily.
bool HdSilkAppendNestedInstanceMemberships(
    const std::string& primPath,
    const std::vector<std::string>& primCategories,
    const std::vector<std::string>& sharedCategories,
    const std::vector<HdSilkInstancerLevel>& levels,
    size_t rowLimit,
    std::vector<HdSilkCategoryMembership>* outMemberships,
    HdSilkNestedLinkDiagnostics* outDiagnostics);

/// How many unresolved per-instance rows one prototype may contribute to a
/// page's collected membership set.
///
/// This is a bound on transient memory, not on the published table. Whether a
/// row survives is decided by HdSilkSceneState against the page's own light and
/// dome orderings, where a row whose masks match its path's is discarded and
/// costs the table nothing: an instancer whose instances differ only in
/// categories no light names publishes a hundred thousand differing rows here
/// and exactly zero entries there. The bound therefore has to sit far above the
/// ABI's OPENUSD_SILK_MAX_LINK_ENTRIES, and it is a separate constant so that
/// the two can never be confused again -- charging the ABI budget here is what
/// dropped the only prim a scene had excluded and reported it as truncated.
///
/// The value bounds one prototype's rows at roughly the memory of a mid-sized
/// instancer: each row is a shared path string and a small category vector, so
/// 65536 rows is single-digit megabytes and is reached only by a prototype
/// whose instances each carry their own authored collection.
constexpr size_t HdSilkMaxCollectedInstanceRows = 65536;

/// Appends everything one published prim path contributes to the collected
/// membership set: its path-wide row, and -- when it is instanced -- the
/// per-instance rows its chain resolves.
///
/// A path is appended atomically. The path-wide row is what a consumer falls
/// back to for every instance it has no row for, so a path row published
/// without the overrides that narrow or widen it is worse than no row at all: a
/// restrictive path row would be applied to instances the author excluded from
/// it. When the instance rows exceed "rowLimit" the whole group is removed
/// again and false is returned, which leaves the path with no row, so it fails
/// open to every light exactly as an unlinked prim does.
///
/// Neither the path row nor the instance rows are charged against the page ABI's
/// entry budget here. Nothing at this point knows which of them resolve to the
/// default masks and disappear; that is decided once, against the page's light
/// and dome orderings, in HdSilkSceneState.
///
/// Returns false when "rowLimit" stopped it, having left "outMemberships"
/// exactly as it found it for this path.
bool HdSilkAppendPathMemberships(
    const std::string& primPath,
    const std::vector<std::string>& primCategories,
    const std::vector<std::string>& sharedCategories,
    const std::vector<HdSilkInstancerLevel>& levels,
    size_t rowLimit,
    std::vector<HdSilkCategoryMembership>* outMemberships,
    HdSilkNestedLinkDiagnostics* outDiagnostics);

PXR_NAMESPACE_CLOSE_SCOPE

#endif
