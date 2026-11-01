// Copyright (c) marcschier. Licensed under the MIT License.

#include "ptex_sync_test_hook.h"

#include "hdsilk_test_hooks.h"
#include "renderDelegate.h"
#include "subdivision.h"

#include "pxr/base/gf/matrix4f.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/filteringSceneIndex.h"
#include "pxr/imaging/hd/material.h"
#include "pxr/imaging/hd/materialConnectionSchema.h"
#include "pxr/imaging/hd/materialNetworkSchema.h"
#include "pxr/imaging/hd/materialNodeParameterSchema.h"
#include "pxr/imaging/hd/materialNodeSchema.h"
#include "pxr/imaging/hd/materialSchema.h"
#include "pxr/imaging/hd/primvarSchema.h"
#include "pxr/imaging/hd/primvarsSchema.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/rprim.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/sceneIndex.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/unitTestDelegate.h"
#include "pxr/imaging/pxOsd/tokens.h"
#include "pxr/usd/sdf/assetPath.h"
#include "pxr/usd/sdf/path.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usdImaging/usdImaging/stageSceneIndex.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstring>
#include <iterator>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace
{
std::atomic<bool> recording{false};
std::mutex observationsMutex;
std::vector<openusd_hdsilk_test_ptex_observation> observations;
}

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
class ReplaceChildDataSource final : public HdContainerDataSource
{
public:
    HD_DECLARE_DATASOURCE(ReplaceChildDataSource)

    ReplaceChildDataSource(
        HdContainerDataSourceHandle source,
        TfToken name,
        HdDataSourceBaseHandle replacement)
        : _source(std::move(source))
        , _name(std::move(name))
        , _replacement(std::move(replacement))
    {
    }

    TfTokenVector GetNames() override { return _source->GetNames(); }

    HdDataSourceBaseHandle Get(const TfToken& name) override
    {
        return name == _name ? _replacement : _source->Get(name);
    }

private:
    HdContainerDataSourceHandle _source;
    TfToken _name;
    HdDataSourceBaseHandle _replacement;
};

class RedactPtexElementSize final : public HdSingleInputFilteringSceneIndexBase
{
public:
    static HdSceneIndexBaseRefPtr New(const HdSceneIndexBaseRefPtr& input)
    {
        return TfCreateRefPtr(new RedactPtexElementSize(input));
    }

    HdSceneIndexPrim GetPrim(const SdfPath& path) const override
    {
        HdSceneIndexPrim prim = _GetInputSceneIndex()->GetPrim(path);
        if ((path != SdfPath("/World/Grouped") &&
             path != SdfPath("/World/GroupedIndexed")) ||
            !prim.dataSource)
        {
            return prim;
        }
        const TfToken name("ptexFaceOffset");
        const HdPrimvarsSchema primvars = HdPrimvarsSchema::GetFromParent(prim.dataSource);
        const HdPrimvarSchema offset = primvars.GetPrimvar(name);
        HdPrimvarSchema::Builder builder;
        if (offset.IsIndexed())
        {
            builder.SetIndexedPrimvarValue(offset.GetIndexedPrimvarValue())
                .SetIndices(offset.GetIndices());
        }
        else
        {
            builder.SetPrimvarValue(offset.GetPrimvarValue());
        }
        const HdContainerDataSourceHandle replacement =
            builder.SetInterpolation(offset.GetInterpolation()).Build();
        const auto overriddenPrimvars = ReplaceChildDataSource::New(
            primvars.GetContainer(), name, replacement);
        prim.dataSource = ReplaceChildDataSource::New(
            prim.dataSource, HdPrimvarsSchemaTokens->primvars, overriddenPrimvars);
        return prim;
    }

    SdfPathVector GetChildPrimPaths(const SdfPath& path) const override
    {
        return _GetInputSceneIndex()->GetChildPrimPaths(path);
    }

private:
    explicit RedactPtexElementSize(const HdSceneIndexBaseRefPtr& input)
        : HdSingleInputFilteringSceneIndexBase(input)
    {
    }

    void _PrimsAdded(
        const HdSceneIndexBase&, const HdSceneIndexObserver::AddedPrimEntries& entries) override
    {
        _SendPrimsAdded(entries);
    }

    void _PrimsRemoved(
        const HdSceneIndexBase&, const HdSceneIndexObserver::RemovedPrimEntries& entries) override
    {
        _SendPrimsRemoved(entries);
    }

    void _PrimsDirtied(
        const HdSceneIndexBase&, const HdSceneIndexObserver::DirtiedPrimEntries& entries) override
    {
        _SendPrimsDirtied(entries);
    }
};

void ObservePtexMaterial(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    const HdSceneIndexBaseRefPtr& terminal,
    openusd_hdsilk_test_ptex_observation* result)
{
    const SdfPath materialId = sceneDelegate->GetMaterialId(id);
    if (materialId.IsEmpty() || !terminal)
    {
        return;
    }

    const HdSceneIndexPrim materialPrim = terminal->GetPrim(materialId);
    result->terminal_material = materialPrim.primType == HdPrimTypeTokens->material;
    const HdMaterialSchema material = HdMaterialSchema::GetFromParent(materialPrim.dataSource);
    const HdMaterialNetworkSchema network = material.GetMaterialNetwork();
    const HdMaterialConnectionSchema surfaceConnection =
        network.GetTerminals().Get(HdMaterialSchemaTokens->surface);
    const HdTokenDataSourceHandle surfacePath = surfaceConnection.GetUpstreamNodePath();
    const HdTokenDataSourceHandle surfaceOutput = surfaceConnection.GetUpstreamNodeOutputName();
    if (!surfacePath || !surfaceOutput || surfaceOutput->GetTypedValue(0.0f) != TfToken("surface"))
    {
        return;
    }
    const TfToken surfaceName = surfacePath->GetTypedValue(0.0f);
    const HdMaterialNodeContainerSchema nodes = network.GetNodes();
    const HdMaterialNodeSchema surface = nodes.Get(surfaceName);
    const HdTokenDataSourceHandle surfaceIdentifier = surface.GetNodeIdentifier();
    const HdMaterialConnectionVectorSchema connections =
        surface.GetInputConnections().Get(TfToken("emissiveColor"));
    if (!surfaceIdentifier || surfaceIdentifier->GetTypedValue(0.0f) != TfToken("UsdPreviewSurface") ||
        !connections || connections.GetNumElements() != 1)
    {
        return;
    }
    const HdMaterialConnectionSchema textureConnection = connections.GetElement(0);
    const HdTokenDataSourceHandle texturePath = textureConnection.GetUpstreamNodePath();
    const HdTokenDataSourceHandle textureOutput = textureConnection.GetUpstreamNodeOutputName();
    if (!texturePath || !textureOutput || textureOutput->GetTypedValue(0.0f) != TfToken("rgb"))
    {
        return;
    }
    const TfToken textureName = texturePath->GetTypedValue(0.0f);
    const HdMaterialNodeSchema texture = nodes.Get(textureName);
    const HdTokenDataSourceHandle identifier = texture.GetNodeIdentifier();
    const HdSampledDataSourceHandle file =
        texture.GetParameters().Get(TfToken("file")).GetValue();
    if (!identifier || identifier->GetTypedValue(0.0f) != TfToken("HwPtexTexture_1") || !file)
    {
        return;
    }
    const VtValue terminalFile = file->GetValue(0.0f);
    if (!terminalFile.IsHolding<SdfAssetPath>())
    {
        return;
    }
    result->ptex_material_connection = 1;
    const SdfAssetPath& asset = terminalFile.UncheckedGet<SdfAssetPath>();
    auto copy = [&](const std::string& value, char* destination, size_t capacity)
    {
        const size_t count = std::min(value.size(), capacity - 1);
        std::memcpy(destination, value.data(), count);
        result->ptex_material_path_truncated |= count != value.size();
    };
    copy(asset.GetAssetPath(), result->ptex_material_asset, sizeof(result->ptex_material_asset));
    copy(
        asset.GetResolvedPath(),
        result->ptex_material_resolved_asset,
        sizeof(result->ptex_material_resolved_asset));

    const VtValue resource = sceneDelegate->GetMaterialResource(materialId);
    if (!resource.IsHolding<HdMaterialNetworkMap>())
    {
        return;
    }
    const HdMaterialNetworkMap& networkMap = resource.UncheckedGet<HdMaterialNetworkMap>();
    const auto entry = networkMap.map.find(HdMaterialTerminalTokens->surface);
    if (entry == networkMap.map.end())
    {
        return;
    }
    const HdMaterialNetwork& delegateNetwork = entry->second;
    const SdfPath surfaceNode(surfaceName.GetString());
    const SdfPath textureNode(textureName.GetString());
    const auto node = std::find_if(
        delegateNetwork.nodes.begin(),
        delegateNetwork.nodes.end(),
        [&](const HdMaterialNode& value)
        {
            return value.path == textureNode && value.identifier == TfToken("HwPtexTexture_1");
        });
    const auto surfaceNodeEntry = std::find_if(
        delegateNetwork.nodes.begin(),
        delegateNetwork.nodes.end(),
        [&](const HdMaterialNode& value)
        {
            return value.path == surfaceNode && value.identifier == TfToken("UsdPreviewSurface");
        });
    const auto relationship = std::find_if(
        delegateNetwork.relationships.begin(),
        delegateNetwork.relationships.end(),
        [&](const HdMaterialRelationship& value)
        {
            return value.outputId == surfaceNode && value.outputName == TfToken("emissiveColor") &&
                value.inputId == textureNode && value.inputName == TfToken("rgb");
        });
    if (node == delegateNetwork.nodes.end() || surfaceNodeEntry == delegateNetwork.nodes.end() ||
        relationship == delegateNetwork.relationships.end())
    {
        return;
    }
    const auto delegateFile = node->parameters.find(TfToken("file"));
    result->ptex_material_delegate_matches =
        delegateFile != node->parameters.end() && delegateFile->second == terminalFile;
}
}

void
HdSilkTestObservePtexSync(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    bool usdDefaultOneProven)
{
    if (!recording.load(std::memory_order_relaxed))
    {
        return;
    }

    openusd_hdsilk_test_ptex_observation result{};
    const std::string path = id.GetString();
    const size_t pathLength = std::min(path.size(), sizeof(result.path) - 1);
    result.path_truncated = pathLength != path.size();
    std::memcpy(result.path, path.data(), pathLength);

    HdRenderIndex& renderIndex = sceneDelegate->GetRenderIndex();
    result.registered = renderIndex.GetRprim(id) != nullptr;
    result.same_scene_delegate = renderIndex.GetSceneDelegateForRprim(id) == sceneDelegate;
    result.instanced = !sceneDelegate->GetInstancerId(id).IsEmpty();
    const HdSceneIndexBaseRefPtr terminal = renderIndex.GetTerminalSceneIndex();
    const HdSceneIndexPrim terminalPrim = terminal ? terminal->GetPrim(id) : HdSceneIndexPrim{};
    result.terminal_mesh = terminalPrim.primType == HdPrimTypeTokens->mesh;
    ObservePtexMaterial(sceneDelegate, id, terminal, &result);
    const TfToken name("ptexFaceOffset");
    const HdPrimvarSchema primvar =
        HdPrimvarsSchema::GetFromParent(terminalPrim.dataSource).GetPrimvar(name);
    result.has_primvar = static_cast<bool>(primvar);
    const HdIntDataSourceHandle elementSize = primvar.GetElementSize();
    result.has_element_size = static_cast<bool>(elementSize);
    result.element_size = elementSize ? elementSize->GetTypedValue(0.0f) : 0;

    if (primvar)
    {
        const HdSampledDataSourceHandle valueSource = primvar.IsIndexed()
            ? primvar.GetIndexedPrimvarValue()
            : primvar.GetPrimvarValue();
        const VtValue value = valueSource ? valueSource->GetValue(0.0f) : VtValue();
        if (value.IsHolding<VtIntArray>())
        {
            const VtIntArray& values = value.UncheckedGet<VtIntArray>();
            result.value_count = static_cast<int32_t>(values.size());
            for (size_t i = 0; i < std::min(values.size(), std::size(result.values)); ++i)
            {
                result.values[i] = values[i];
            }
        }
        VtIntArray sourceIndices;
        if (primvar.IsIndexed())
        {
            sourceIndices = primvar.GetIndices()->GetTypedValue(0.0f);
        }
        result.index_count = static_cast<int32_t>(sourceIndices.size());
        for (size_t i = 0; i < std::min(sourceIndices.size(), std::size(result.indices)); ++i)
        {
            result.indices[i] = sourceIndices[i];
        }
        VtIntArray delegateIndices;
        const VtValue delegateValue =
            sceneDelegate->GetIndexedPrimvar(id, name, &delegateIndices);
        result.delegate_matches = delegateValue == value && delegateIndices == sourceIndices;
    }

    std::array<HdSilkPtexTriangle, 2> mappings{};
    mappings[1].faceIndex = 1;
    const std::array<uint32_t, 2> sourceFaces{0, 1};
    std::array<HdSilkPtexTriangle, 2> output{};
    const auto write = [&](HdSilkPtexMissingElementSize policy)
    {
        return HdSilkWritePtexFaceOffsetDataSource(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            2, primvar, policy, 0.0f, 16, output.data(), output.size());
    };
    result.strict_status = static_cast<int32_t>(write(HdSilkPtexMissingElementSize::Reject));
    result.usd_default_one_proven = usdDefaultOneProven;
    result.default_one_status =
        static_cast<int32_t>(write(
            usdDefaultOneProven
                ? HdSilkPtexMissingElementSize::KnownDefaultOne
                : HdSilkPtexMissingElementSize::Reject));
    result.mapped_faces[0] = output[0].faceIndex;
    result.mapped_faces[1] = output[1].faceIndex;

    std::lock_guard<std::mutex> lock(observationsMutex);
    observations.push_back(result);
}

PXR_NAMESPACE_CLOSE_SCOPE

extern "C" OPENUSD_HDSILK_API void
openusd_hdsilk_test_begin_ptex_observation(void)
{
    std::lock_guard<std::mutex> lock(observationsMutex);
    observations.clear();
    recording.store(true, std::memory_order_relaxed);
}

extern "C" OPENUSD_HDSILK_API size_t
openusd_hdsilk_test_end_ptex_observation(
    openusd_hdsilk_test_ptex_observation* output, size_t capacity)
{
    recording.store(false, std::memory_order_relaxed);
    std::lock_guard<std::mutex> lock(observationsMutex);
    const size_t count = observations.size();
    if (output != nullptr)
    {
        std::copy_n(observations.begin(), std::min(count, capacity), output);
    }
    return count;
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_ptex_synthetic_sync(const char* stagePath, int32_t mode)
{
    using namespace PXR_NS;
    if (stagePath == nullptr || mode < 0 || mode > 2)
    {
        return 1;
    }
    const bool legacy = mode == 1;
    const bool filteredUnindexed = mode == 2;
    const UsdStageRefPtr stage = UsdStage::Open(stagePath);
    if (!stage)
    {
        return 2;
    }
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 3;
    }
    const UsdImagingStageSceneIndexRefPtr source = UsdImagingStageSceneIndex::New();
    source->SetStage(stage);
    source->SetTime(UsdTimeCode(0.0));
    const HdSceneIndexBaseRefPtr input = legacy
        ? HdSceneIndexBaseRefPtr(source)
        : RedactPtexElementSize::New(source);
    index->InsertSceneIndex(input, SdfPath::AbsoluteRootPath(), false);
    HdSceneDelegate* adapter =
        index->GetSceneDelegateForRprim(SdfPath("/World/Ordinary"));
    if (adapter == nullptr)
    {
        return 4;
    }

    const SdfPath id(legacy
        ? "/LegacyMesh"
        : (filteredUnindexed ? "/World/Grouped" : "/World/GroupedIndexed"));
    HdUnitTestDelegate legacyDelegate(index.get(), SdfPath::AbsoluteRootPath());
    if (legacy)
    {
        VtVec3fArray points(4);
        points[0] = GfVec3f(0, 0, 0);
        points[1] = GfVec3f(1, 0, 0);
        points[2] = GfVec3f(1, 1, 0);
        points[3] = GfVec3f(0, 1, 0);
        VtIntArray counts(2, 3);
        VtIntArray vertices{0, 1, 2, 0, 2, 3};
        legacyDelegate.AddMesh(
            id, GfMatrix4f(1.0f), points, counts, vertices, false, SdfPath(),
            PxOsdOpenSubdivTokens->none);
        legacyDelegate.AddPrimvar(
            id, TfToken("ptexFaceOffset"), VtValue(VtIntArray{2, 7, 3, 8}),
            HdInterpolationUniform, TfToken(), VtIntArray{0, 1});
        if (index->GetSceneDelegateForRprim(id) != &legacyDelegate ||
            adapter == &legacyDelegate)
        {
            return 5;
        }
    }
    HdRprim* rprim = const_cast<HdRprim*>(index->GetRprim(id));
    if (rprim == nullptr)
    {
        return 6;
    }
    HdDirtyBits dirty = rprim->GetInitialDirtyBitsMask();
    rprim->Sync(adapter, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    return dirty == HdChangeTracker::Clean ? 0 : 7;
}
