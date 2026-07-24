// Copyright (c) marcschier. Licensed under the MIT License.

#include "rendererPlugin.h"

#include "renderDelegate.h"

#include "pxr/imaging/hd/rendererPluginRegistry.h"

PXR_NAMESPACE_OPEN_SCOPE

// Register the plugin with the renderer plugin system. This runs when the
// Plug system loads this shared library (see resources/plugInfo.json.in).
TF_REGISTRY_FUNCTION(TfType)
{
    HdRendererPluginRegistry::Define<HdSilkRendererPlugin>();
}

HdRenderDelegate*
HdSilkRendererPlugin::CreateRenderDelegate()
{
    auto* renderDelegate = new HdSilkRenderDelegate();
    HdSilkRenderDelegate::PublishSceneStateForActiveCapture(
        renderDelegate->GetSceneState());
    return renderDelegate;
}

HdRenderDelegate*
HdSilkRendererPlugin::CreateRenderDelegate(HdRenderSettingsMap const& settingsMap)
{
    auto* renderDelegate = new HdSilkRenderDelegate(settingsMap);
    HdSilkRenderDelegate::PublishSceneStateForActiveCapture(
        renderDelegate->GetSceneState());
    return renderDelegate;
}

void
HdSilkRendererPlugin::DeleteRenderDelegate(HdRenderDelegate* renderDelegate)
{
    delete renderDelegate;
}

bool
HdSilkRendererPlugin::IsSupported(
    HdRendererCreateArgs const& /*rendererCreateArgs*/,
    std::string* /*reasonWhyNot*/) const
{
    // No GPU or platform-specific dependency: if the plugin loaded, it's
    // supported.
    return true;
}

PXR_NAMESPACE_CLOSE_SCOPE
