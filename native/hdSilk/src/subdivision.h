// Copyright (c) marcschier. Licensed under the MIT License.
//
// Bounded OpenSubdiv refinement for hdSilk mesh Rprims.
//
// hdSilk publishes triangle lists, so a subdivision surface has to be evaluated
// on the CPU before it reaches the wire. This module owns that evaluation: it
// builds one OpenSubdiv Far::TopologyRefiner per mesh from the authored control
// cage plus its subdivision tags, refines uniformly to an explicit level, and
// caches everything that depends only on topology so an animated point array
// re-runs interpolation without rebuilding the refiner.
//
// Everything here is renderer-neutral: the output is refined points, refined
// primvar arrays, a triangle list, and one authored coarse face index per
// emitted triangle. Nothing in this file knows about the page wire format.

#ifndef HDSILK_SUBDIVISION_H
#define HDSILK_SUBDIVISION_H

#include "pxr/pxr.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/tf/token.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/meshTopology.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/// The highest uniform refinement level hdSilk will evaluate. Uniform
/// Catmull-Clark quadruples the face count per level, so a bound is what keeps
/// a session-level complexity switch from turning a mid-sized asset into an
/// allocation the process cannot serve. Level 3 is 64x the control cage, which
/// is where the visual difference from the limit surface stops being visible at
/// viewport resolutions.
inline constexpr int HdSilkMaxMeshRefineLevel = 3;

/// Refinement is refused, and the control cage published instead, when the
/// refined mesh would exceed either bound. Both are counts rather than bytes so
/// the check can run from the coarse level's component counts before a single
/// refined vertex is allocated; together they bound the refined position,
/// index, and primvar allocations to tens of megabytes.
inline constexpr uint64_t HdSilkMaxRefinedVertices = 1ull << 21;
inline constexpr uint64_t HdSilkMaxRefinedFaces = 1ull << 21;

/// Refusal diagnostics are composed from authored primvar names and counts, all
/// of which a scene controls, so they are truncated to this many bytes. One
/// malformed asset must not be able to turn a per-mesh refusal into an
/// unbounded log line.
inline constexpr size_t HdSilkMaxSubdivisionDiagnosticLength = 512;

/// Why a mesh is, or is not, being refined. Every value other than Refined
/// leaves the caller on the unrefined HdMeshUtil path with the complete control
/// cage, which is a correct mesh rather than a partial one.
enum class HdSilkSubdivisionStatus : uint32_t
{
    /// The refiner is built and the refined tables below are populated.
    Refined = 0,
    /// Refinement was not asked for: refine level 0, or an authored
    /// subdivisionScheme of "none", which is a polygon mesh by definition.
    NotRequested = 1,
    /// The authored subdivisionScheme is not one this delegate evaluates.
    UnsupportedScheme = 2,
    /// The control cage is not a mesh OpenSubdiv can refine: out-of-range
    /// indices, degenerate faces, or non-manifold connectivity.
    InvalidTopology = 3,
    /// The refined mesh would exceed HdSilkMaxRefinedVertices or
    /// HdSilkMaxRefinedFaces.
    ExceedsBounds = 4,
    /// OpenSubdiv rejected the descriptor for a reason it did not classify.
    RefinerFailed = 5,
    /// An authored face-varying primvar cannot be described as an OpenSubdiv
    /// channel, so refining would publish a surface missing that primvar.
    UnsupportedFaceVarying = 6,
};

/// One face-varying primvar bound to the refiner as its own OpenSubdiv channel.
///
/// "indices" is one value index per coarse face-vertex, in authored face order
/// -- exactly USD's indexed-primvar encoding. A primvar authored without
/// indices carries the identity sequence, which is what makes every corner its
/// own face-varying value and matches how Storm builds the same channel.
struct HdSilkSubdivisionChannel
{
    TfToken name;
    VtIntArray indices;
    /// The number of distinct coarse face-varying values, i.e. the element
    /// count the authored primvar array must have.
    int valueCount = 0;
    /// Set when the authored primvar exists but its face-varying topology
    /// cannot be described as an OpenSubdiv channel.
    ///
    /// Rebuild then refuses the whole mesh rather than binding the channels it
    /// could describe: a refined surface that silently lost its UVs or its
    /// authored normals is shaded from data the scene never authored, and the
    /// result reads as a material bug rather than as the geometry refusal it
    /// actually is. The control cage carries every primvar it always did, so
    /// refusing is the only outcome that loses nothing.
    std::string unsupportedReason;
};

/// The refined topology of one mesh, plus the refiner needed to interpolate its
/// primvars. Rebuilt only when the control cage, its tags, its face-varying
/// topologies, or the requested level change.
class HdSilkMeshSubdivision
{
public:
    HdSilkMeshSubdivision();
    ~HdSilkMeshSubdivision();

    HdSilkMeshSubdivision(const HdSilkMeshSubdivision&) = delete;
    HdSilkMeshSubdivision& operator=(const HdSilkMeshSubdivision&) = delete;

    /// Rebuilds the refiner and every table derived from topology alone.
    ///
    /// "topology" must already carry its subdivision tags and hole indices.
    /// "channels" must name every authored face-varying primvar, including any
    /// this delegate could not describe; a channel carrying an
    /// unsupportedReason, or one whose indices do not address every coarse
    /// face-vertex, refuses the whole mesh rather than being dropped.
    ///
    /// Returns the resulting status, which is also retained; anything other
    /// than Refined clears the object and leaves IsRefined() false. The
    /// diagnostic is truncated to HdSilkMaxSubdivisionDiagnosticLength.
    HdSilkSubdivisionStatus Rebuild(
        HdMeshTopology const& topology,
        int refineLevel,
        std::vector<HdSilkSubdivisionChannel> channels,
        std::string* diagnostic);

    /// Drops the refiner and every refined table.
    void Clear();

    bool IsRefined() const { return _status == HdSilkSubdivisionStatus::Refined; }
    HdSilkSubdivisionStatus GetStatus() const { return _status; }
    int GetRefineLevel() const { return _refineLevel; }

    /// Refined vertex index per emitted triangle corner, three per triangle.
    const std::vector<uint32_t>& GetTriangleIndices() const
    {
        return _triangleIndices;
    }

    /// The authored coarse face index each emitted triangle descends from, so a
    /// material subset or a uniform primvar keyed by authored face still
    /// resolves after refinement.
    const std::vector<uint32_t>& GetTriangleSubprims() const
    {
        return _triangleSubprims;
    }

    size_t GetCoarseVertexCount() const { return _coarseVertexCount; }
    size_t GetRefinedVertexCount() const { return _refinedVertexCount; }

    /// The index of the face-varying channel bound under "name", or -1.
    int FindChannel(const TfToken& name) const;

    size_t GetCoarseFaceVaryingValueCount(int channel) const;
    size_t GetRefinedFaceVaryingValueCount(int channel) const;

    /// Refines control-cage positions to the last level. A source longer than
    /// the control-vertex count is accepted: a cage may author points no face
    /// references, and those trailing points address no refined vertex.
    bool RefinePoints(
        const VtVec3fArray& coarse,
        VtVec3fArray* refined) const;

    /// Refines a tightly packed vertex-interpolated primvar. "coarse" holds
    /// componentCount floats per control vertex; "refined" receives the same
    /// layout for every refined vertex.
    bool RefineVertexPrimvar(
        const std::vector<float>& coarse,
        uint32_t componentCount,
        std::vector<float>* refined) const;

    /// Refines a varying-interpolated primvar. Varying data is interpolated
    /// bilinearly rather than by the subdivision rules, which is what USD's
    /// "varying" interpolation means.
    bool RefineVaryingPrimvar(
        const std::vector<float>& coarse,
        uint32_t componentCount,
        std::vector<float>* refined) const;

    /// Refines one face-varying channel and resolves it onto the emitted
    /// triangle corners, so "refined" holds componentCount floats per corner of
    /// GetTriangleIndices() and needs no further indexing by the consumer.
    bool RefineFaceVaryingPrimvarOntoCorners(
        int channel,
        const std::vector<float>& coarse,
        uint32_t componentCount,
        std::vector<float>* refined) const;

    /// Number of refiner rebuilds since process start. A mesh whose points
    /// animate must not move this counter, which is the only direct evidence
    /// that the topology cache is actually reused across frames.
    static uint64_t GetRefinerBuildCount();

    /// Number of meshes refused refinement with a diagnostic since process
    /// start, counted per Rebuild call rather than per frame.
    static uint64_t GetDiagnosticCount();

    /// Overrides HdSilkMaxRefinedVertices. Zero restores the default. The
    /// bound is a static rather than a constructor argument because it exists
    /// to be exercised: a probe cannot afford to author a control cage large
    /// enough to overflow the shipped budget just to prove the budget holds.
    static void SetRefinedVertexBudgetForTesting(uint64_t budget);

private:
    struct _Impl;

    /// Interpolates "coarse" through every level into a flat buffer holding all
    /// levels, then copies the last level out. Shared by the vertex and varying
    /// paths, which differ only in the OpenSubdiv entry point they call.
    bool _RefineLevels(
        const std::vector<float>& coarse,
        uint32_t componentCount,
        bool varying,
        std::vector<float>* refined) const;

    std::unique_ptr<_Impl> _impl;
    HdSilkSubdivisionStatus _status = HdSilkSubdivisionStatus::NotRequested;
    int _refineLevel = 0;
    size_t _coarseVertexCount = 0;
    size_t _refinedVertexCount = 0;
    std::vector<uint32_t> _triangleIndices;
    std::vector<uint32_t> _triangleSubprims;
};

/// A stable one-line reason for a status, for diagnostics and probes.
const char* HdSilkSubdivisionStatusName(HdSilkSubdivisionStatus status);

PXR_NAMESPACE_CLOSE_SCOPE

#endif
