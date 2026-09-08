// Copyright (c) marcschier. Licensed under the MIT License.

#pragma once

#include "pxr/pxr.h"
#include <cstddef>

PXR_NAMESPACE_OPEN_SCOPE
class TfToken;
template <typename T> class VtArrayEdit;
PXR_NAMESPACE_CLOSE_SCOPE

namespace OpenUsdRenderInput
{
struct TokenEditCost
{
    size_t finalSize;
    size_t peakSize;
    size_t copiedElements;
};

size_t TokenEditWordCount(const PXR_NS::VtArrayEdit<PXR_NS::TfToken>& edit) noexcept;
TokenEditCost BoundTokenEdit(const PXR_NS::VtArrayEdit<PXR_NS::TfToken>& edit,
    size_t initialSize, size_t maximumElements);
}
