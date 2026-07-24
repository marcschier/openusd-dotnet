// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this plugin registration mirrors Pixar's Apache-2.0
// hdTiny example (OpenUSD extras/imaging/examples/hdTiny/rendererPlugin.*).

#ifndef HDSILK_RENDERER_PLUGIN_H
#define HDSILK_RENDERER_PLUGIN_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/rendererPlugin.h"

#include <string>

PXR_NAMESPACE_OPEN_SCOPE

/// HdSilkRendererPlugin registers the hdSilk render delegate with Hydra's
/// renderer plugin registry. It is loaded on demand by the Plug system when
/// a host application (or the openusd_hdsilk C ABI, via UsdImagingGLEngine)
/// asks Hydra to construct the "HdSilkRendererPlugin" renderer.
class HdSilkRendererPlugin final : public HdRendererPlugin
{
public:
    HdSilkRendererPlugin() = default;
    ~HdSilkRendererPlugin() override = default;

    /// Construct a new render delegate of type HdSilkRenderDelegate.
    HdRenderDelegate* CreateRenderDelegate() override;

    /// Construct a new render delegate of type HdSilkRenderDelegate.
    HdRenderDelegate* CreateRenderDelegate(HdRenderSettingsMap const& settingsMap) override;

    /// Destroy a render delegate created by this class's CreateRenderDelegate.
    void DeleteRenderDelegate(HdRenderDelegate* renderDelegate) override;

    /// Checks to see if the plugin is supported on the running system.
    bool IsSupported(
        HdRendererCreateArgs const& rendererCreateArgs,
        std::string* reasonWhyNot = nullptr) const override;

private:
    HdSilkRendererPlugin(const HdSilkRendererPlugin&) = delete;
    HdSilkRendererPlugin& operator=(const HdSilkRendererPlugin&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
