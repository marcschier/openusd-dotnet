// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this render delegate follows the shape of Pixar's
// Apache-2.0 hdTiny example (OpenUSD extras/imaging/examples/hdTiny),
// adapted to own an HdSilkSceneState instead of drawing directly. See
// ../README.md for the token-keyed state handoff this file implements.

#ifndef HDSILK_RENDER_DELEGATE_H
#define HDSILK_RENDER_DELEGATE_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/renderDelegate.h"
#include "pxr/imaging/hd/resourceRegistry.h"

#include "sceneState.h"

#include <cstdint>
#include <memory>

PXR_NAMESPACE_OPEN_SCOPE

/// HdRenderParam wrapper that exposes the render delegate's shared,
/// thread-safe scene state to Rprims during Sync().
class HdSilkRenderParam final : public HdRenderParam
{
public:
    explicit HdSilkRenderParam(std::shared_ptr<HdSilkSceneState> state)
        : _state(std::move(state))
    {
    }

    HdSilkSceneState& GetSceneState() const { return *_state; }
    std::shared_ptr<HdSilkSceneState> const& GetSceneStatePtr() const { return _state; }

private:
    std::shared_ptr<HdSilkSceneState> _state;
};

/// HdSilkRenderDelegate creates HdSilkMesh Rprims and an HdSilkRenderPass,
/// all sharing a single HdSilkSceneState instance owned by the delegate.
///
/// UsdImagingGLEngine does not expose the HdRenderDelegate it constructs
/// internally, so there is no direct way for the openusd_hdsilk C ABI to
/// recover the HdSilkSceneState belonging to the session it just created.
/// As a project-local handoff, the C ABI starts a token-keyed capture before
/// engine construction. HdSilkRendererPlugin publishes only the delegate it
/// constructs for that active token. Direct/external delegate construction
/// does not publish and concurrent threads use independent tokens.
class HdSilkRenderDelegate final : public HdRenderDelegate
{
public:
    HdSilkRenderDelegate();
    explicit HdSilkRenderDelegate(HdRenderSettingsMap const& settingsMap);
    ~HdSilkRenderDelegate() override;

    const TfTokenVector& GetSupportedRprimTypes() const override;
    const TfTokenVector& GetSupportedSprimTypes() const override;
    const TfTokenVector& GetSupportedBprimTypes() const override;

    HdResourceRegistrySharedPtr GetResourceRegistry() const override;

    HdRenderPassSharedPtr CreateRenderPass(
        HdRenderIndex* index,
        HdRprimCollection const& collection) override;

    HdInstancer* CreateInstancer(HdSceneDelegate* delegate, SdfPath const& id) override;
    void DestroyInstancer(HdInstancer* instancer) override;

    HdRprim* CreateRprim(TfToken const& typeId, SdfPath const& rprimId) override;
    void DestroyRprim(HdRprim* rPrim) override;

    HdSprim* CreateSprim(TfToken const& typeId, SdfPath const& sprimId) override;
    HdSprim* CreateFallbackSprim(TfToken const& typeId) override;
    void DestroySprim(HdSprim* sprim) override;

    HdBprim* CreateBprim(TfToken const& typeId, SdfPath const& bprimId) override;
    HdBprim* CreateFallbackBprim(TfToken const& typeId) override;
    void DestroyBprim(HdBprim* bprim) override;

    void CommitResources(HdChangeTracker* tracker) override;

    /// Material render contexts, in descending order of preference.
    ///
    /// The universal context is first so an authored `outputs:surface` --
    /// UsdPreviewSurface, or a MaterialX shader bound in the universal context
    /// -- always wins. `mdl` follows so a material that authors *only*
    /// `outputs:mdl:surface`, which is how Omniverse-authored stages that never
    /// got a preview context are written, reaches this delegate at all instead
    /// of arriving with no surface terminal and being drawn as a default.
    TfTokenVector GetMaterialRenderContexts() const override;

    HdRenderParam* GetRenderParam() const override;

    static uint64_t BeginSceneStateCapture();
    static void PublishSceneStateForActiveCapture(
        const std::shared_ptr<HdSilkSceneState>& sceneState);
    static std::shared_ptr<HdSilkSceneState> EndSceneStateCapture(uint64_t token);
    static void CancelSceneStateCapture(uint64_t token) noexcept;

    std::shared_ptr<HdSilkSceneState> const& GetSceneState() const
    {
        return _sceneState;
    }

private:
    void _Initialize();

    static const TfTokenVector SUPPORTED_RPRIM_TYPES;
    static const TfTokenVector SUPPORTED_SPRIM_TYPES;
    static const TfTokenVector SUPPORTED_BPRIM_TYPES;

    HdResourceRegistrySharedPtr _resourceRegistry;
    std::shared_ptr<HdSilkSceneState> _sceneState;
    std::unique_ptr<HdSilkRenderParam> _renderParam;

    HdSilkRenderDelegate(const HdSilkRenderDelegate&) = delete;
    HdSilkRenderDelegate& operator=(const HdSilkRenderDelegate&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
