// Copyright (c) marcschier. Licensed under the MIT License.

#include "mdlMaterialSceneIndexPlugin.h"

#include "pxr/base/tf/staticTokens.h"
#include "pxr/imaging/hd/materialFilteringSceneIndexBase.h"
#include "pxr/imaging/hd/materialNetworkInterface.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/usd/sdf/assetPath.h"

PXR_NAMESPACE_OPEN_SCOPE

TF_DEFINE_PRIVATE_TOKENS(
    _hdSilkMdlMaterialTokens,
    ((sceneIndexPluginName, "HdSilk_MdlMaterialSceneIndexPlugin"))
    ((mdlSourceAsset, "mdl:sourceAsset"))
    ((mdlSubIdentifier, "mdl:sourceAsset:subIdentifier"))
    ((implementationSource, "implementationSource"))
    ((sourceAsset, "sourceAsset"))
);

namespace
{
// Must match the "displayName" in resources/plugInfo.json.in.
const char* const _pluginDisplayName = "Silk.NET";

/// The prefix hdSilk stamps on a synthesized MDL node identifier. It is not a
/// registered Sdr identifier and is not meant to be one: it exists so the
/// material resolver can tell an MDL node from a UsdPreviewSurface node in a
/// network that carries neither an Sdr node nor a source asset field.
const char* const _identifierPrefix = "mdl:";

std::string
ReadAssetPath(const VtValue& value)
{
    if (value.IsHolding<SdfAssetPath>())
    {
        const SdfAssetPath& asset = value.UncheckedGet<SdfAssetPath>();
        // The authored path is the identity. The resolved path is deliberately
        // not preferred: an Omniverse module resolves only inside Kit's MDL
        // search path, and identity must not depend on whether it resolved.
        return asset.GetAssetPath().empty() ? asset.GetResolvedPath()
                                            : asset.GetAssetPath();
    }
    if (value.IsHolding<std::string>())
    {
        return value.UncheckedGet<std::string>();
    }
    return std::string();
}

std::string
ReadToken(const VtValue& value)
{
    if (value.IsHolding<TfToken>())
    {
        return value.UncheckedGet<TfToken>().GetString();
    }
    if (value.IsHolding<std::string>())
    {
        return value.UncheckedGet<std::string>();
    }
    return std::string();
}

void
_ResolveMdlNodeIdentifiers(HdMaterialNetworkInterface* network)
{
    if (network == nullptr)
    {
        return;
    }
    for (const TfToken& nodeName : network->GetNodeNames())
    {
        if (!network->GetNodeType(nodeName).IsEmpty())
        {
            // An identifier already resolved, which is what happens when an
            // MDL Sdr parser plugin is installed. Rewriting it would replace a
            // registered node type with a synthetic one.
            continue;
        }
        const std::string implementation = ReadToken(network->GetNodeTypeInfoValue(
            nodeName, _hdSilkMdlMaterialTokens->implementationSource));
        if (implementation != _hdSilkMdlMaterialTokens->sourceAsset.GetString())
        {
            continue;
        }
        const std::string module = ReadAssetPath(network->GetNodeTypeInfoValue(
            nodeName, _hdSilkMdlMaterialTokens->mdlSourceAsset));
        if (module.empty())
        {
            continue;
        }
        const std::string material = ReadToken(network->GetNodeTypeInfoValue(
            nodeName, _hdSilkMdlMaterialTokens->mdlSubIdentifier));
        network->SetNodeType(
            nodeName,
            TfToken(_identifierPrefix + module + ":" + material));
    }
}

class _MdlMaterialSceneIndex final : public HdMaterialFilteringSceneIndexBase
{
public:
    static TfRefPtr<_MdlMaterialSceneIndex> New(
        const HdSceneIndexBaseRefPtr& inputScene)
    {
        return TfCreateRefPtr(new _MdlMaterialSceneIndex(inputScene));
    }

protected:
    FilteringFnc _GetFilteringFunction() const override
    {
        return _ResolveMdlNodeIdentifiers;
    }

private:
    explicit _MdlMaterialSceneIndex(const HdSceneIndexBaseRefPtr& inputScene)
        : HdMaterialFilteringSceneIndexBase(inputScene)
    {
    }
};
}

TF_REGISTRY_FUNCTION(TfType)
{
    HdSceneIndexPluginRegistry::Define<HdSilk_MdlMaterialSceneIndexPlugin>();
}

TF_REGISTRY_FUNCTION(HdSceneIndexPlugin)
{
    const HdSceneIndexPluginRegistry::InsertionPhase insertionPhase = 0;

    HdSceneIndexPluginRegistry::GetInstance().RegisterSceneIndexForRenderer(
        _pluginDisplayName,
        _hdSilkMdlMaterialTokens->sceneIndexPluginName,
        /* inputArgs = */ nullptr,
        insertionPhase,
        HdSceneIndexPluginRegistry::InsertionOrderAtStart);
}

HdSilk_MdlMaterialSceneIndexPlugin::HdSilk_MdlMaterialSceneIndexPlugin() = default;

HdSceneIndexBaseRefPtr
HdSilk_MdlMaterialSceneIndexPlugin::_AppendSceneIndex(
    const HdSceneIndexBaseRefPtr& inputScene,
    const HdContainerDataSourceHandle& /*inputArgs*/)
{
    return _MdlMaterialSceneIndex::New(inputScene);
}

PXR_NAMESPACE_CLOSE_SCOPE
