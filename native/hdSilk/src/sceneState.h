// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: the render delegate / Rprim / render pass split below
// follows the structure of Pixar's Apache-2.0-licensed hdTiny example
// (OpenUSD extras/imaging/examples/hdTiny), adapted to publish serialized
// wire-format pages instead of drawing directly. See ../README.md.

#ifndef HDSILK_SCENE_STATE_H
#define HDSILK_SCENE_STATE_H

#include "pxr/pxr.h"
#include "pxr/base/gf/matrix4d.h"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/// Flattens a row-major GfMatrix4d into a plain double[16] array (row 0
/// first), matching the FRAME/MESH_UPSERT wire layout documented in
/// openusd_hdsilk.h.
inline void HdSilkFlattenMatrix(const GfMatrix4d& matrix, double (&out)[16])
{
    const double* raw = matrix.GetArray();
    for (int i = 0; i < 16; ++i)
    {
        out[i] = raw[i];
    }
}

/// Frame-level state captured once per HdSilkRenderPass::_Execute call.
struct HdSilkFrameState
{
    int32_t width = 0;
    int32_t height = 0;
    double viewMatrix[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    double projectionMatrix[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    uint32_t clipPlaneCount = 0;
    double clipPlanes[8][4] = {};
};

/// One entry in the ABI v4 vertex attribute table. Data is always float and
/// always already resolved onto the emitted triangle-list vertices, so the
/// consumer never has to re-index it.
struct HdSilkMeshAttribute
{
    std::string name;
    uint32_t semantic = 0;
    uint32_t componentCount = 0;
    uint32_t interpolation = 1;
    std::vector<float> data;
};

/// A single mesh Rprim's renderable data, captured by HdSilkMesh::Sync and
/// consumed by HdSilkSceneState::BuildPage. Every field is plain-old-data so
/// it can be appended directly to the wire buffer without ever exposing a
/// native pointer.
struct HdSilkMeshRecord
{
    std::string path;
    int32_t primId = -1;
    int32_t instanceId = 0;
    int32_t instanceIndex = 0;
    uint32_t topologyKind = 1;
    uint64_t topologyRevision = 0;
    uint32_t doubleSided = 1;
    uint32_t cullStyle = 4;
    double transform[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    std::vector<float> points;      // x, y, z per point.
    std::vector<uint32_t> indices;  // 3 indices per triangle or 2 per line.
    // Authored USD face per triangle, or curve segment per line.
    std::vector<uint32_t> triangleSubprims;
    float displayColor[4] = {0.7f, 0.7f, 0.7f, 1.0f};
    std::string materialPath;       // Empty when the mesh has no binding.
    std::vector<HdSilkMeshAttribute> attributes;
};

/// One scalar or vector UsdPreviewSurface input, already resolved to floats.
struct HdSilkMaterialScalar
{
    uint32_t parameter = 0;
    uint32_t componentCount = 0;
    float value[4] = {0.0f, 0.0f, 0.0f, 0.0f};
};

/// One UsdPreviewSurface input driven by a connected UsdUVTexture.
struct HdSilkMaterialTexture
{
    uint32_t parameter = 0;
    uint32_t wrapS = 2;
    uint32_t wrapT = 2;
    uint32_t sourceColorSpace = 0;
    uint32_t componentCount = 4;
    float scale[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float bias[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float fallback[4] = {0.0f, 0.0f, 0.0f, 1.0f};
    std::string asset;
    std::string uvPrimvar;
};

/// A single material Sprim's resolved shading data. An unsupported surface is
/// still recorded, with empty tables, so it can be diagnosed rather than
/// silently approximated.
struct HdSilkMaterialRecord
{
    std::string path;
    uint32_t surfaceKind = 0;
    std::vector<HdSilkMaterialScalar> scalars;
    std::vector<HdSilkMaterialTexture> textures;
};

/// Derives a stable, non-zero 31-bit identifier for an instancer path. The
/// value is diagnostic only: (path, instanceIndex) remains the authoritative
/// identity of a published record.
inline int32_t HdSilkStableInstanceId(const std::string& instancerPath)
{
    uint64_t hash = 14695981039346656037ull;
    for (unsigned char byte : instancerPath)
    {
        hash ^= static_cast<uint64_t>(byte);
        hash *= 1099511628211ull;
    }
    const int32_t folded =
        static_cast<int32_t>(static_cast<uint32_t>(hash ^ (hash >> 32)) &
            0x7FFFFFFFu);
    return folded == 0 ? 1 : folded;
}

/// Identity of one published mesh record. A non-instanced Rprim publishes a
/// single full record at instance index 0; a point-instanced prototype publishes
/// full geometry in instance zero and lightweight transform-only records for
/// later instances. The USD prim path stays authoritative and is shared by every
/// instance of the same prototype.
struct HdSilkMeshKey
{
    std::string path;
    int32_t instanceIndex = 0;

    bool operator==(const HdSilkMeshKey& other) const
    {
        return instanceIndex == other.instanceIndex && path == other.path;
    }
};

struct HdSilkMeshKeyHash
{
    size_t operator()(const HdSilkMeshKey& key) const
    {
        const size_t pathHash = std::hash<std::string>()(key.path);
        const size_t indexHash =
            std::hash<int32_t>()(key.instanceIndex);
        return pathHash ^ (indexHash + 0x9e3779b97f4a7c15ull +
            (pathHash << 6) + (pathHash >> 2));
    }
};

/// Thread-safe scene state shared between HdSilkMesh::Sync (which Hydra may
/// invoke concurrently from worker threads for different Rprims),
/// HdSilkRenderPass::_Execute (which captures camera/viewport state), and
/// the openusd_hdsilk C ABI (which snapshots a page of serialized commands
/// once per session sync call). Exactly one instance is owned by each
/// HdSilkRenderDelegate.
class HdSilkSceneState
{
public:
    /// Replaces every published instance of "path" with "records". Instance
    /// indices that existed before but are absent from "records" are queued
    /// for removal so a shrinking instancer cannot leave stale geometry
    /// behind. An empty "records" vector removes the prim entirely.
    void ReplaceMeshInstances(
        const std::string& path,
        std::vector<HdSilkMeshRecord> records);
    void RemoveMesh(const std::string& path);
    void SetFrame(const HdSilkFrameState& frame);

    /// Publishes or replaces one material. The path is the authoritative
    /// identity, matching MESH_UPSERT's material_path.
    void ReplaceMaterial(HdSilkMaterialRecord record);
    void RemoveMaterial(const std::string& path);

    /// Number of mesh records rejected by wire validation since process
    /// start. Rejected records are skipped with a diagnostic so that one
    /// malformed prim cannot blank an otherwise renderable scene.
    static uint64_t GetRejectedMeshCount();

    /// Number of material records rejected by wire validation since process
    /// start, counted separately from meshes so a shading failure is not
    /// mistaken for a geometry failure.
    static uint64_t GetRejectedMaterialCount();

    /// Builds a serialized page containing the current frame state plus any
    /// mesh upserts/removals queued since the previous call, then clears
    /// that pending state. Returns the page bytes; *outRevision and
    /// *outCommandCount receive the new monotonically increasing revision
    /// and the number of commands written into the returned buffer.
    std::vector<uint8_t> BuildPage(uint64_t* outRevision, uint32_t* outCommandCount);

private:
    struct _Entry
    {
        HdSilkMeshRecord record;
        bool dirty = true;
    };

    struct _MaterialEntry
    {
        HdSilkMaterialRecord record;
        bool dirty = true;
    };

    mutable std::mutex _mutex;
    std::unordered_map<HdSilkMeshKey, _Entry, HdSilkMeshKeyHash> _meshes;
    std::unordered_map<std::string, std::vector<int32_t>> _instancesByPath;
    std::vector<HdSilkMeshKey> _pendingRemovals;
    std::unordered_map<std::string, _MaterialEntry> _materials;
    std::vector<std::string> _pendingMaterialRemovals;
    HdSilkFrameState _frame;
    uint64_t _revision = 0;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
