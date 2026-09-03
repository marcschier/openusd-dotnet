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

// The ABI constants the retained records are described in terms of. The header
// is the authority for the wire format, and this file's records are what is
// serialized into it, so naming the constants here keeps the two from drifting.
#include "openusd_hdsilk.h"

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

/// One resolved sub-shape of a published deformation block: the sparse deltas
/// [firstDelta, firstDelta + deltaCount) of the block's delta table, scaled by
/// the sub-shape weight UsdSkel resolved at the record's evaluation time.
struct HdSilkMeshBlendRange
{
    uint32_t firstDelta = 0;
    uint32_t deltaCount = 0;
    float weight = 0.0f;
};

/// One sparse blend-shape delta, addressed by authored point index.
struct HdSilkMeshBlendDelta
{
    uint32_t pointIndex = 0;
    float positionOffset[3] = {0.0f, 0.0f, 0.0f};
    float normalOffset[3] = {0.0f, 0.0f, 0.0f};
};

/// The bounded, renderer-neutral rig of one deformed prototype, published as
/// the ABI v20 deformation block beside the CPU-resolved points that stay
/// authoritative.
///
/// Every array here crosses the ABI in bulk. The joint palette is already
/// remapped into this prim's own joint order, so jointIndices index it
/// directly and no OpenUSD path or token reaches a consumer. `published` is
/// set only when the whole block is complete, self-consistent and verified
/// against the CPU points; otherwise `unsupportedFeatures` names why, and no
/// block is written.
struct HdSilkMeshDeformation
{
    bool published = false;
    uint32_t flags = 0;
    uint32_t unsupportedFeatures = 0;
    uint32_t jointCount = 0;
    uint32_t influencesPerPoint = 0;
    float geomBindTransform[16] = {
        1.0f, 0.0f, 0.0f, 0.0f,
        0.0f, 1.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f};
    std::vector<float> bindPoints;      // x, y, z per point.
    std::vector<float> bindNormals;     // Empty without FLAG_BIND_NORMALS.
    std::vector<uint32_t> jointIndices; // influencesPerPoint per point.
    std::vector<float> jointWeights;    // influencesPerPoint per point.
    std::vector<float> jointMatrices;   // 16 row-major floats per joint.
    std::vector<HdSilkMeshBlendRange> blendRanges;
    std::vector<HdSilkMeshBlendDelta> blendDeltas;

    /// Drops the rig but keeps the reason, so a record whose emitted geometry
    /// stopped matching the bind pose is diagnosed rather than published with a
    /// rig that addresses points it no longer has.
    void Reject(uint32_t reason)
    {
        published = false;
        flags = 0;
        unsupportedFeatures |= reason;
        jointCount = 0;
        influencesPerPoint = 0;
        bindPoints.clear();
        bindNormals.clear();
        jointIndices.clear();
        jointWeights.clear();
        jointMatrices.clear();
        blendRanges.clear();
        blendDeltas.clear();
    }
};

/// A single mesh Rprim's renderable data, captured by HdSilkMesh::Sync and
/// consumed by HdSilkSceneState::BuildPage. Every field is plain-old-data so
/// it can be appended directly to the wire buffer without ever exposing a
/// native pointer.
/// Whether the two subprim-identity tables a record WOULD publish exceed the
/// shared byte budget.
///
/// This is deliberately a pure function of the two counts, so it can be -- and
/// is -- evaluated before either table is reserved or filled. Checking the
/// budget only after building the tables would make an oversized mesh pay the
/// entire allocation the budget exists to refuse.
inline bool HdSilkSubprimIdentityExceedsBudget(
    size_t pointOriginCount,
    size_t cornerEdgeCount)
{
    constexpr size_t maximumEntries =
        OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES / sizeof(uint32_t);
    if (pointOriginCount > maximumEntries ||
        cornerEdgeCount > maximumEntries - pointOriginCount)
    {
        return true;
    }
    return (pointOriginCount + cornerEdgeCount) * sizeof(uint32_t) >
        OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES;
}

/// One level of the ordered instancing chain a record's instance belongs to.
///
/// Nested instancing has no single "the" instancer, so an instance is described
/// by one entry per level, ordered outermost to innermost, each naming the
/// instancer at that level and the instance's own index inside it. The
/// composite ordinal a record carries in `instanceIndex` keys the retained
/// identity table and is deliberately not any level's own index; only this
/// chain decodes back to a scene instance.
struct HdSilkInstancerContextEntry
{
    std::string path;
    int32_t index = 0;
};

/// The bounded number of instancing levels one record may publish.
///
/// USD imposes no nesting limit, but a chain is decoded into fixed managed
/// storage on the other side of the ABI, so the wire needs a ceiling that is
/// checked before anything is reserved. Sixty-four levels is far beyond any
/// authored scene and still bounds the block to a few kilobytes.
#define OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES 64u

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
    // ABI v22 subprim identity. `pointOrigins` is the authored point index of
    // every emitted vertex, and `cornerEdges` is the authored mesh edge index
    // of every emitted primitive corner, both using OPENUSD_SILK_SUBPRIM_NONE
    // for an emitted component the scene never authored. Either table is empty
    // when the record refuses that target, and `subprimUnsupported` then names
    // why. They are deliberately not derived by the serializer: only the prim
    // that triangulated the authored topology knows which emitted component
    // came from which authored one.
    std::vector<uint32_t> pointOrigins;
    std::vector<uint32_t> cornerEdges;
    // The absolute USD path of the owning instancer, empty when the prim has no
    // instancer. instanceId is a hash and cannot be inverted, so this is the
    // only authoritative instance identity the wire carries.
    std::string instancerPath;
    // ABI v23. The complete ordered instancing chain, outermost level first.
    // Published exactly when instancerPath is non-empty, and its last entry's
    // path always equals instancerPath, so a single-level scene reads exactly
    // as it did before v23.
    std::vector<HdSilkInstancerContextEntry> instancerContext;
    uint32_t authoredPointCount = 0;
    uint32_t authoredEdgeCount = 0;
    uint32_t subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_NONE;
    uint32_t subprimUnsupported = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE;
    float displayColor[4] = {0.7f, 0.7f, 0.7f, 1.0f};
    std::string materialPath;       // Empty when the mesh has no binding.
    std::vector<HdSilkMeshAttribute> attributes;
    HdSilkMeshDeformation deformation;

    /// Drops both subprim-identity tables and names the reason. Used by every
    /// transform that rebuilds the emitted arrays after the tables were built,
    /// so a stale table is never published against a topology it no longer
    /// describes.
    ///
    /// The vectors are swapped with empty ones rather than cleared, because
    /// `clear()` keeps the capacity: a record rejected for exceeding the
    /// identity budget would otherwise keep holding the very allocation the
    /// budget exists to bound, for as long as the record lives.
    void RejectSubprimIdentity(uint32_t reason)
    {
        std::vector<uint32_t>().swap(pointOrigins);
        std::vector<uint32_t>().swap(cornerEdges);
        authoredPointCount = 0;
        authoredEdgeCount = 0;
        subprimIdentity &= ~(OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
            OPENUSD_SILK_SUBPRIM_IDENTITY_POINT);
        subprimUnsupported |= reason;
    }

    /// Drops the subprim-identity tables of an ABI v8 instance-reference
    /// record. The tables belong to the prototype payload the reference reuses,
    /// exactly as the geometry and the rig do, so an instance carries neither a
    /// table nor a reason: nothing about the instance is unsupported.
    ///
    /// The vectors are swapped with empty ones rather than cleared, for the same
    /// reason RejectSubprimIdentity swaps: `clear()` keeps the capacity, so a
    /// lightweight instance reference copied from a prototype record would keep
    /// holding that prototype's whole identity allocation -- up to the ABI's
    /// bounded 64 MiB -- once per instance, for as long as the instance record
    /// lives. A thousand instances of a large point cloud would then cost a
    /// thousand copies of an allocation no instance publishes a byte of.
    void ClearSubprimIdentity()
    {
        std::vector<uint32_t>().swap(pointOrigins);
        std::vector<uint32_t>().swap(cornerEdges);
        authoredPointCount = 0;
        authoredEdgeCount = 0;
        subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_NONE;
        subprimUnsupported = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE;
    }
};

/// Builds the lightweight ABI v8 instance-reference record of one prototype.
///
/// Only the identity and per-instance scalars are copied. No geometry vector,
/// no identity vector, no attribute and no rig is copied or even allocated,
/// because an instance reference publishes none of them: the prototype payload
/// record carries them once and every later instance reuses it.
///
/// Copying the whole prototype record and clearing the vectors afterwards --
/// which is what this replaces -- cost one deep copy of the prototype's points,
/// indices, subprims, identity tables and attributes per instance, so a
/// thousand instances of a million-point cloud allocated and freed a thousand
/// copies of data no instance publishes a byte of. `clear()` would not even
/// have released the capacity afterwards, so every one of those allocations
/// stayed resident for as long as the instance record lived. A freshly
/// constructed record holds no capacity at all, which is the only way to
/// release it for certain.
inline HdSilkMeshRecord HdSilkMakeInstanceReference(
    const HdSilkMeshRecord& prototype)
{
    HdSilkMeshRecord reference;
    reference.path = prototype.path;
    reference.primId = prototype.primId;
    reference.instanceId = prototype.instanceId;
    reference.instanceIndex = prototype.instanceIndex;
    reference.topologyKind = prototype.topologyKind;
    reference.topologyRevision = prototype.topologyRevision;
    reference.doubleSided = prototype.doubleSided;
    reference.cullStyle = prototype.cullStyle;
    for (size_t element = 0; element < 16; ++element)
    {
        reference.transform[element] = prototype.transform[element];
    }
    for (size_t component = 0; component < 4; ++component)
    {
        reference.displayColor[component] = prototype.displayColor[component];
    }
    return reference;
}


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
    // The connected output port (OPENUSD_SILK_TEXTURE_CHANNEL_*). Deliberately
    // initialized outside the valid range so a producer that forgets to resolve
    // the connection is rejected by wire validation instead of silently
    // publishing the red channel.
    uint32_t outputChannel = 0xFFFFFFFFu;
    float scale[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float bias[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float fallback[4] = {0.0f, 0.0f, 0.0f, 1.0f};
    // How this entry combines with the primary entry of the same parameter.
    // OPENUSD_SILK_COMPOSITE_NONE marks the primary entry; anything else marks
    // the second operand of a two-image surface input. compositeFactor is
    // meaningful only for OPENUSD_SILK_COMPOSITE_MIX.
    uint32_t compositeOp = 0;
    float compositeFactor = 0.0f;
    // The folded MaterialX place2d transform this texture reads its coordinates
    // through, as the row-major affine (m00, m01, m10, m11, tx, ty). Resolved per
    // texture so the material-wide reconciliation can tell agreement from
    // divergence; only the reconciled material transform reaches the wire.
    float uvTransform[6] = {1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f};
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
    // The single constant UV transform every texture of this material samples
    // through, as the row-major affine (m00, m01, m10, m11, tx, ty). Identity
    // unless a MaterialX place2d node with constant inputs was folded into it.
    float uvTransform[6] = {1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f};
    std::vector<HdSilkMaterialScalar> scalars;
    std::vector<HdSilkMaterialTexture> textures;
    std::vector<uint32_t> generatedFragmentSpirv;
    std::string generatedFragmentMslSource;
};

/// One resolved UsdLux light entry. Transforms stay in world space on the wire;
/// managed frame packing converts them to eye space together with the camera.
struct HdSilkLightRecord
{
    std::string path;
    uint32_t type = 0;
    uint32_t shadowEnabled = 0;
    float shapeX = 0.0f;
    float shapeY = 0.0f;
    float color[3] = {1.0f, 1.0f, 1.0f};
    float intensity = 1.0f;
    double transform[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    float exposure = 0.0f;
    float diffuse = 1.0f;
    float specular = 1.0f;
    float radius = 0.5f;
    bool ambientOnly = false;
    // The resolved dome texture:file, empty for every light that is not a
    // textured dome. A non-empty value moves the record out of the frame
    // ambient accumulation and into its own ENVIRONMENT command, because an
    // image cannot be described by the single ambient colour and folding an
    // untextured approximation in as well would double-count the dome.
    std::string textureAsset;
    uint32_t textureFormat = 0;
    uint32_t sourceColorSpace = 0;
    uint32_t unsupportedFeatures = 0;
    // The UsdLux light-link and shadow-link collection identities Hydra reports
    // for this light. An empty identity is Hydra's own encoding of a collection
    // that includes the root with nothing excluded, so it means "links to every
    // prim" and never has to be resolved against prim categories at all.
    std::string lightLinkCategory;
    std::string shadowLinkCategory;
};

/// The categories one published prim, or one instance of one published prim,
/// belongs to. Collected only while at least one light carries a non-default
/// link collection, because the resolved masks are the default for every prim
/// otherwise and the walk would cost a per-frame pass over the render index for
/// a table that is always empty.
struct HdSilkCategoryMembership
{
    std::string path;
    // OPENUSD_SILK_LINK_ALL_INSTANCES when the categories apply to every
    // published instance of the path.
    int32_t instanceIndex = -1;
    std::vector<std::string> categories;
};

/// One resolved LIGHT_LINK entry: the direct lights that illuminate a prim, the
/// direct lights whose shadows it casts, and the dome lights that illuminate it.
struct HdSilkLinkEntry
{
    std::string path;
    int32_t instanceIndex = -1;
    uint32_t lightMask = 0;
    uint32_t shadowMask = 0;
    // Bit i is set when the dome light at index i of the page's bounded dome
    // table illuminates this prim. There is no matching dome shadow mask: a dome
    // casts no shadow map here, so collection:shadowLink on a dome is diagnosed
    // against the dome rather than converted into a receiver restriction.
    uint32_t domeMask = 0;

    bool operator==(const HdSilkLinkEntry& other) const
    {
        return instanceIndex == other.instanceIndex &&
            lightMask == other.lightMask &&
            shadowMask == other.shadowMask &&
            domeMask == other.domeMask &&
            path == other.path;
    }

    bool operator!=(const HdSilkLinkEntry& other) const
    {
        return !(*this == other);
    }
};

/// The published state of one page's light-link table, compared whole so an
/// unchanged table publishes nothing at all.
struct HdSilkLinkTable
{
    std::vector<HdSilkLinkEntry> entries;
    uint32_t lightCount = 0;
    uint32_t flags = 0;
    uint32_t domeCount = 0;

    bool operator==(const HdSilkLinkTable& other) const
    {
        return lightCount == other.lightCount &&
            flags == other.flags &&
            domeCount == other.domeCount &&
            entries == other.entries;
    }

    bool operator!=(const HdSilkLinkTable& other) const
    {
        return !(*this == other);
    }
};

/// One entry of the ABI v21 FRAME dome table: the dome bit i names, the ambient
/// summand that dome contributes, and whether it publishes an image instead.
struct HdSilkFrameDome
{
    // The dome's own prim path. It never reaches the wire -- the FRAME entry is
    // positional and an ENVIRONMENT record carries the path already -- and is
    // retained so the dome bit can be resolved against a light's collection
    // identity while the table is being built.
    std::string path;
    float ambientColor[3] = {0.0f, 0.0f, 0.0f};
    uint32_t flags = 0;
    // The light-link collection identity this dome reports, empty when it links
    // to every prim.
    std::string lightLinkCategory;

    bool operator==(const HdSilkFrameDome& other) const
    {
        return flags == other.flags &&
            ambientColor[0] == other.ambientColor[0] &&
            ambientColor[1] == other.ambientColor[1] &&
            ambientColor[2] == other.ambientColor[2] &&
            path == other.path &&
            lightLinkCategory == other.lightLinkCategory;
    }

    bool operator!=(const HdSilkFrameDome& other) const
    {
        return !(*this == other);
    }
};

/// One resolved ABI v19 shadow descriptor: the light-space camera a shadow map
/// is rendered with, plus the bounded map identity and the producer's bias and
/// filtering policy for it.
struct HdSilkShadowDescriptor
{
    uint32_t lightIndex = 0;
    uint32_t mapIndex = 0;
    uint32_t resolution = 0;
    uint32_t flags = 0;
    double view[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    double projection[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};
    float depthBias = 0.0f;
    float normalBias = 0.0f;
    float pcfRadius = 0.0f;

    bool operator==(const HdSilkShadowDescriptor& other) const
    {
        if (lightIndex != other.lightIndex ||
            mapIndex != other.mapIndex ||
            resolution != other.resolution ||
            flags != other.flags ||
            depthBias != other.depthBias ||
            normalBias != other.normalBias ||
            pcfRadius != other.pcfRadius)
        {
            return false;
        }
        for (int index = 0; index < 16; ++index)
        {
            if (view[index] != other.view[index] ||
                projection[index] != other.projection[index])
            {
                return false;
            }
        }
        return true;
    }

    bool operator!=(const HdSilkShadowDescriptor& other) const
    {
        return !(*this == other);
    }
};

/// The published state of one page's shadow table, compared whole so an
/// unchanged table publishes nothing and a consumer keeps every retained map.
struct HdSilkShadowTable
{
    std::vector<HdSilkShadowDescriptor> descriptors;
    uint32_t lightCount = 0;
    uint32_t flags = 0;

    bool operator==(const HdSilkShadowTable& other) const
    {
        return lightCount == other.lightCount &&
            flags == other.flags &&
            descriptors == other.descriptors;
    }

    bool operator!=(const HdSilkShadowTable& other) const
    {
        return !(*this == other);
    }
};

/// The world-space axis-aligned bounds of every published caster, or an empty
/// box when nothing with extent is published. Only a bounded box can produce a
/// light-space projection, so an empty one is reported rather than approximated.
struct HdSilkWorldBounds
{
    bool valid = false;
    double minimum[3] = {0.0, 0.0, 0.0};
    double maximum[3] = {0.0, 0.0, 0.0};
};

/// The subset of a textured dome record that decides whether a page has to
/// republish its ENVIRONMENT command. Comparing the published fields rather
/// than the whole light record keeps an unchanged dome silent across pages
/// while still republishing the moment any published value moves.
struct HdSilkEnvironmentSnapshot
{
    std::string textureAsset;
    uint32_t textureFormat = 0;
    uint32_t sourceColorSpace = 0;
    uint32_t unsupportedFeatures = 0;
    // The dome's entry in the page's bounded dome table, or
    // OPENUSD_SILK_DOME_INDEX_NONE when the page publishes no table. It is part
    // of the compared state because a dome that keeps every emission control but
    // moves to a different bit addresses a different mask.
    uint32_t domeIndex = 0xFFFFFFFFu;
    float color[3] = {1.0f, 1.0f, 1.0f};
    float intensity = 1.0f;
    float exposure = 0.0f;
    float diffuse = 1.0f;
    float specular = 1.0f;
    double transform[16] = {
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0};

    bool operator==(const HdSilkEnvironmentSnapshot& other) const
    {
        if (textureAsset != other.textureAsset ||
            textureFormat != other.textureFormat ||
            sourceColorSpace != other.sourceColorSpace ||
            unsupportedFeatures != other.unsupportedFeatures ||
            domeIndex != other.domeIndex ||
            intensity != other.intensity ||
            exposure != other.exposure ||
            diffuse != other.diffuse ||
            specular != other.specular)
        {
            return false;
        }
        for (int index = 0; index < 3; ++index)
        {
            if (color[index] != other.color[index])
            {
                return false;
            }
        }
        for (int index = 0; index < 16; ++index)
        {
            if (transform[index] != other.transform[index])
            {
                return false;
            }
        }
        return true;
    }

    bool operator!=(const HdSilkEnvironmentSnapshot& other) const
    {
        return !(*this == other);
    }
};

/// Builds the published snapshot of one textured dome record. "domeIndex" is the
/// record's entry in the page's bounded dome table, or
/// OPENUSD_SILK_DOME_INDEX_NONE when the page publishes no dome table.
HdSilkEnvironmentSnapshot HdSilkMakeEnvironmentSnapshot(
    const HdSilkLightRecord& record,
    uint32_t domeIndex);

/// Appends the instance membership rows one prototype publishes, from the
/// instancer's per-instance categories and that prototype's own instance
/// indices.
///
/// Lives here rather than in the render pass so the intersection is testable
/// without a render index. It is the whole of what keeps an instancer that
/// scatters several prototypes from emitting a row for every instance to every
/// prototype: Hydra reports one category array per instance of the *instancer*,
/// so every prototype sees every instance, and a row for an instance a prototype
/// does not draw names an identity no record is ever published under. Those rows
/// can never be matched, and they consume the same bounded table the real rows
/// need.
///
/// "publishedIndices" is the instancer-relative index of each instance this
/// prototype draws, exactly as HdSilkInstancer resolves it, so a negative index
/// -- a hidden or proto instance -- addresses nothing here for the same reason
/// it addresses nothing there. A row is emitted only where the instance's
/// categories differ from the prototype's, because an instance that resolves to
/// the prototype's own masks is already described by the prototype's row.
///
/// "rowLimit" bounds how many unresolved rows may be materialized and is a
/// memory policy, not the page ABI's entry budget: a category set that differs
/// is not yet a mask that differs, and which rows survive is decided later,
/// against the page's own light and dome orderings.
///
/// Returns false when the limit stopped it, leaving "outMemberships" holding
/// every row that fitted.
bool HdSilkAppendInstanceMemberships(
    const std::string& primPath,
    const std::vector<std::string>& primCategories,
    const std::vector<std::vector<std::string>>& instanceCategories,
    const std::vector<int>& publishedIndices,
    size_t rowLimit,
    std::vector<HdSilkCategoryMembership>* outMemberships);

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
    void SetComplexity(uint32_t complexity);
    void SetDrawMode(uint32_t drawMode);

    /// Publishes or replaces one material. The path is the authoritative
    /// identity, matching MESH_UPSERT's material_path.
    void ReplaceMaterial(HdSilkMaterialRecord record);
    void RemoveMaterial(const std::string& path);

    /// Publishes supported UsdLux light state. An untextured dome light is
    /// retained as an ambient-only record; a textured dome light is retained as
    /// an environment record and published as its own ENVIRONMENT command.
    /// Unsupported light-linking/shadow controls are preserved only as
    /// diagnostics for now.
    void ReplaceLight(HdSilkLightRecord record);
    void RemoveLight(const std::string& path);

    /// Reports whether any retained light carries a non-default light-link or
    /// shadow-link collection. False -- the common case -- means every prim is
    /// linked to every light and no prim categories have to be collected.
    /// A DomeLight counts: since ABI v21 its collection:lightLink resolves into
    /// the bounded dome mask, so the prim categories a dome collection is
    /// matched against have to be collected for it too.
    bool HasLightLinks() const;

    /// Replaces the prim category memberships the next page resolves link masks
    /// from. Passing an empty table is how a frame states that it collected no
    /// memberships, which resolves every prim to the default masks.
    /// "truncated" is set when the collecting frame itself stopped at the
    /// OPENUSD_SILK_MAX_LINK_ENTRIES bound, so the published table reports the
    /// omission even though the entries it kept are all default-free.
    void SetCategoryMemberships(
        std::vector<HdSilkCategoryMembership> memberships,
        bool truncated);

    /// Number of prims dropped from the light-link table because it exceeded
    /// OPENUSD_SILK_MAX_LINK_ENTRIES since process start. Dropped prims keep the
    /// default of being linked to every light.
    static uint64_t GetTruncatedLinkCount();

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
        // Object-space bounds of this record's own points, computed once when
        // the record is published rather than per page: a shadow projection has
        // to cover every caster, and recomputing the extent of every point on
        // every frame would make an unchanged scene pay for a moving one. An
        // instance record that reuses a payload record's geometry carries no
        // points and therefore no bounds of its own.
        bool hasLocalBounds = false;
        double localMinimum[3] = {0.0, 0.0, 0.0};
        double localMaximum[3] = {0.0, 0.0, 0.0};
    };

    struct _MaterialEntry
    {
        HdSilkMaterialRecord record;
        bool dirty = true;
    };

    /// Resolves the sparse link table for one page from the retained prim
    /// category memberships, the ordered direct lights that page publishes, and
    /// the bounded dome table it publishes alongside them.
    /// Requires _mutex to be held by the caller.
    HdSilkLinkTable _ResolveLinkTable(
        const std::vector<HdSilkLightRecord>& directLights,
        const std::vector<HdSilkFrameDome>& domes,
        bool domeBudgetExceeded) const;

    /// Resolves the world-space bounds of every published record. Requires
    /// _mutex to be held by the caller.
    HdSilkWorldBounds _ResolveCasterBounds() const;

    /// Resolves the bounded shadow table for one page from the ordered direct
    /// lights that page publishes and the caster bounds the maps must cover.
    /// Requires _mutex to be held by the caller.
    HdSilkShadowTable _ResolveShadowTable(
        const std::vector<HdSilkLightRecord>& directLights,
        const HdSilkLinkTable& links) const;

    mutable std::mutex _mutex;
    std::unordered_map<HdSilkMeshKey, _Entry, HdSilkMeshKeyHash> _meshes;
    std::unordered_map<std::string, std::vector<int32_t>> _instancesByPath;
    std::vector<HdSilkMeshKey> _pendingRemovals;
    std::unordered_map<std::string, _MaterialEntry> _materials;
    std::vector<std::string> _pendingMaterialRemovals;
    std::unordered_map<std::string, HdSilkLightRecord> _lights;
    // The environment snapshot each dome path was last published with, so a
    // page emits an ENVIRONMENT_UPSERT only when something it carries changed
    // and an ENVIRONMENT_REMOVE exactly once when a dome stops being textured
    // or leaves the scene.
    std::unordered_map<std::string, HdSilkEnvironmentSnapshot> _publishedEnvironments;
    // The prim categories the most recent frame collected, and the link table
    // the most recent page published. Both are whole-table state: linking is
    // resolved against the frame light ordering of the page that carries it, so
    // there is no meaningful per-prim delta to accumulate.
    std::vector<HdSilkCategoryMembership> _categoryMemberships;
    bool _categoryMembershipsTruncated = false;
    HdSilkLinkTable _publishedLinks;
    // The shadow table the most recent page published. Whole-table state for the
    // same reason linking is: a descriptor is resolved against the frame light
    // ordering and the caster bounds of the page that carries it.
    HdSilkShadowTable _publishedShadows;
    HdSilkFrameState _frame;
    uint32_t _complexity = 0;
    uint32_t _drawMode = 0;
    uint64_t _revision = 0;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
