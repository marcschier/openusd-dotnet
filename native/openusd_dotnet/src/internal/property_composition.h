// Copyright (c) marcschier. Licensed under the MIT License.

#pragma once

#include "pxr/pxr.h"

PXR_NAMESPACE_OPEN_SCOPE
class PcpPrimIndex;
class PcpLayerStack;
PXR_NAMESPACE_CLOSE_SCOPE

namespace OpenUsdProperties
{
bool HasStoredCompositionErrors(const PXR_NS::PcpPrimIndex& index) noexcept;
bool HasStoredCompositionErrors(const PXR_NS::PcpLayerStack& stack) noexcept;
}
