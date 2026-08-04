// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_MATERIAL_X_BRIDGE_H
#define HDSILK_MATERIAL_X_BRIDGE_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/material.h"

#include <string>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

struct HdSilkMaterialXDocument
{
    bool success = false;
    std::string xml;
    std::string error;
    std::string validation;
    size_t textureNodeCount = 0;
    size_t primvarNodeCount = 0;
};

struct HdSilkMaterialXVulkanShader
{
    bool success = false;
    std::string fragmentSource;
    std::vector<uint32_t> fragmentSpirv;
    std::string fragmentMslSource;
    std::string error;
};

/// Converts the Hydra material-network input side into a validated MaterialX
/// document using OpenUSD's hdMtlx translator and standard libraries.
HdSilkMaterialXDocument
HdSilkCreateMaterialXDocumentFromNetwork(
    SdfPath const& materialPath,
    HdMaterialNetworkMap const& networkMap);

/// Generates Vulkan GLSL with MaterialX's VkShaderGenerator and compiles it to
/// SPIR-V with shaderc/glslang for generated-source validation.
HdSilkMaterialXVulkanShader
HdSilkGenerateMaterialXVulkanFragment(
    SdfPath const& materialPath,
    HdMaterialNetworkMap const& networkMap);

PXR_NAMESPACE_CLOSE_SCOPE

#endif
