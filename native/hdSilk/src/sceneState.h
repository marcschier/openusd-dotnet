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

#include <cstdint>
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
    double transform[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    std::vector<float> points;      // x, y, z per point.
    std::vector<uint32_t> indices;  // 3 indices per triangle.
    std::vector<uint32_t> triangleSubprims; // Authored USD face per triangle.
    float displayColor[4] = {0.7f, 0.7f, 0.7f, 1.0f};
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
    void UpsertMesh(const std::string& path, HdSilkMeshRecord record);
    void RemoveMesh(const std::string& path);
    void SetFrame(const HdSilkFrameState& frame);

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

    mutable std::mutex _mutex;
    std::unordered_map<std::string, _Entry> _meshes;
    std::vector<std::string> _pendingRemovals;
    HdSilkFrameState _frame;
    uint64_t _revision = 0;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
