// Copyright (c) marcschier. Licensed under the MIT License.

#include "renderDelegate.h"

#include "openusd_hdsilk.h"
#include "instancer.h"
#include "mesh.h"
#include "renderPass.h"

#include "pxr/base/tf/diagnostic.h"
#include "pxr/imaging/hd/extComputation.h"
#include "pxr/imaging/hd/tokens.h"

#include <atomic>
#include <mutex>
#include <unordered_map>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
std::atomic<uint64_t> _nextCreationToken{1};
thread_local uint64_t _activeCreationToken = 0;
std::mutex _creationRegistryMutex;
std::unordered_map<uint64_t, std::shared_ptr<HdSilkSceneState>> _creationRegistry;
}

const TfTokenVector HdSilkRenderDelegate::SUPPORTED_RPRIM_TYPES =
{
    HdPrimTypeTokens->mesh,
};

const TfTokenVector HdSilkRenderDelegate::SUPPORTED_SPRIM_TYPES =
{
    // Skinned and otherwise procedurally deformed meshes publish their points
    // through an ExtComputation. Without the Sprim the render index never
    // creates the computation, and pulling computed primvars dereferences a
    // prim that does not exist.
    HdPrimTypeTokens->extComputation,
};

const TfTokenVector HdSilkRenderDelegate::SUPPORTED_BPRIM_TYPES =
{
};

HdSilkRenderDelegate::HdSilkRenderDelegate()
    : HdRenderDelegate()
{
    _Initialize();
}

HdSilkRenderDelegate::HdSilkRenderDelegate(HdRenderSettingsMap const& settingsMap)
    : HdRenderDelegate(settingsMap)
{
    _Initialize();
}

HdSilkRenderDelegate::~HdSilkRenderDelegate() = default;

void
HdSilkRenderDelegate::_Initialize()
{
    _resourceRegistry = std::make_shared<HdResourceRegistry>();
    _sceneState = std::make_shared<HdSilkSceneState>();
    _renderParam = std::make_unique<HdSilkRenderParam>(_sceneState);

}

uint64_t
HdSilkRenderDelegate::BeginSceneStateCapture()
{
    const uint64_t token = _nextCreationToken.fetch_add(1, std::memory_order_relaxed);
    _activeCreationToken = token;
    return token;
}

void
HdSilkRenderDelegate::PublishSceneStateForActiveCapture(
    const std::shared_ptr<HdSilkSceneState>& sceneState)
{
    if (_activeCreationToken == 0 || !sceneState)
    {
        return;
    }
    std::lock_guard<std::mutex> lock(_creationRegistryMutex);
    _creationRegistry.try_emplace(_activeCreationToken, sceneState);
}

std::shared_ptr<HdSilkSceneState>
HdSilkRenderDelegate::EndSceneStateCapture(uint64_t token)
{
    if (token == 0 || _activeCreationToken != token)
    {
        return nullptr;
    }
    _activeCreationToken = 0;
    std::lock_guard<std::mutex> lock(_creationRegistryMutex);
    const auto iterator = _creationRegistry.find(token);
    if (iterator == _creationRegistry.end())
    {
        return nullptr;
    }
    std::shared_ptr<HdSilkSceneState> state = std::move(iterator->second);
    _creationRegistry.erase(iterator);
    return state;
}

void
HdSilkRenderDelegate::CancelSceneStateCapture(uint64_t token) noexcept
{
    if (_activeCreationToken == token)
    {
        _activeCreationToken = 0;
    }
    try
    {
        std::lock_guard<std::mutex> lock(_creationRegistryMutex);
        _creationRegistry.erase(token);
    }
    catch (...)
    {
    }
}

const TfTokenVector&
HdSilkRenderDelegate::GetSupportedRprimTypes() const
{
    return SUPPORTED_RPRIM_TYPES;
}

const TfTokenVector&
HdSilkRenderDelegate::GetSupportedSprimTypes() const
{
    return SUPPORTED_SPRIM_TYPES;
}

const TfTokenVector&
HdSilkRenderDelegate::GetSupportedBprimTypes() const
{
    return SUPPORTED_BPRIM_TYPES;
}

HdResourceRegistrySharedPtr
HdSilkRenderDelegate::GetResourceRegistry() const
{
    return _resourceRegistry;
}

HdRenderPassSharedPtr
HdSilkRenderDelegate::CreateRenderPass(
    HdRenderIndex* index,
    HdRprimCollection const& collection)
{
    return HdRenderPassSharedPtr(new HdSilkRenderPass(index, collection, _sceneState));
}

HdRprim*
HdSilkRenderDelegate::CreateRprim(TfToken const& typeId, SdfPath const& rprimId)
{
    if (typeId == HdPrimTypeTokens->mesh)
    {
        return new HdSilkMesh(rprimId);
    }
    TF_CODING_ERROR("Unknown Rprim type=%s id=%s", typeId.GetText(), rprimId.GetText());
    return nullptr;
}

void
HdSilkRenderDelegate::DestroyRprim(HdRprim* rPrim)
{
    if (rPrim != nullptr && _sceneState)
    {
        _sceneState->RemoveMesh(rPrim->GetId().GetString());
    }
    delete rPrim;
}

HdSprim*
HdSilkRenderDelegate::CreateSprim(TfToken const& typeId, SdfPath const& sprimId)
{
    if (typeId == HdPrimTypeTokens->extComputation)
    {
        return new HdExtComputation(sprimId);
    }
    TF_CODING_ERROR("Unknown Sprim type=%s id=%s", typeId.GetText(), sprimId.GetText());
    return nullptr;
}

HdSprim*
HdSilkRenderDelegate::CreateFallbackSprim(TfToken const& typeId)
{
    if (typeId == HdPrimTypeTokens->extComputation)
    {
        return new HdExtComputation(SdfPath::EmptyPath());
    }
    TF_CODING_ERROR("Creating unknown fallback sprim type=%s", typeId.GetText());
    return nullptr;
}

void
HdSilkRenderDelegate::DestroySprim(HdSprim* sprim)
{
    delete sprim;
}

HdBprim*
HdSilkRenderDelegate::CreateBprim(TfToken const& typeId, SdfPath const& bprimId)
{
    TF_CODING_ERROR("Unknown Bprim type=%s id=%s", typeId.GetText(), bprimId.GetText());
    return nullptr;
}

HdBprim*
HdSilkRenderDelegate::CreateFallbackBprim(TfToken const& typeId)
{
    TF_CODING_ERROR("Creating unknown fallback bprim type=%s", typeId.GetText());
    return nullptr;
}

void
HdSilkRenderDelegate::DestroyBprim(HdBprim* /*bprim*/)
{
    TF_CODING_ERROR("Destroy Bprim not supported");
}

HdInstancer*
HdSilkRenderDelegate::CreateInstancer(HdSceneDelegate* delegate, SdfPath const& id)
{
    return new HdSilkInstancer(delegate, id);
}

void
HdSilkRenderDelegate::DestroyInstancer(HdInstancer* instancer)
{
    delete instancer;
}

void
HdSilkRenderDelegate::CommitResources(HdChangeTracker* /*tracker*/)
{
}

HdRenderParam*
HdSilkRenderDelegate::GetRenderParam() const
{
    return _renderParam.get();
}

PXR_NAMESPACE_CLOSE_SCOPE

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_external_delegate_does_not_publish(void)
{
    const uint64_t token = pxr::HdSilkRenderDelegate::BeginSceneStateCapture();
    {
        pxr::HdSilkRenderDelegate externalDelegate;
    }
    return pxr::HdSilkRenderDelegate::EndSceneStateCapture(token) == nullptr ? 1 : 0;
}
#endif
