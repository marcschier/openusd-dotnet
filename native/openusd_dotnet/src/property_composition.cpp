// Copyright (c) marcschier. Licensed under the MIT License.

#include "property_pcp_headers.g.h"
#include "internal/property_composition.h"

#include <memory>
#include <type_traits>

static_assert(PXR_VERSION == 2605,
    "Review the allocation-free Pcp error-owner access before changing the pinned OpenUSD SDK.");

namespace OpenUsdProperties
{
namespace
{
using Errors = std::unique_ptr<PXR_NS::PcpErrorVector>;

struct PrimErrors
{
    using Owner = PXR_NS::PcpPrimIndex;
    friend bool HasErrors(const Owner& owner, PrimErrors) noexcept;
};

struct LayerErrors
{
    using Owner = PXR_NS::PcpLayerStack;
    friend bool HasErrors(const Owner& owner, LayerErrors) noexcept;
};

// SDK26.05 GetLocalErrors() copies its vector. Standard explicit instantiation
// obtains compiler-typed member pointers under its access-control exception.
// Configure AND build verify exact SDK headers; the generated include selects
// those verified files. No SDK definitions or byte offsets are substituted.
template <typename Tag, Errors Tag::Owner::* Member>
struct ErrorOwnerAccess
{
    friend bool HasErrors(const typename Tag::Owner& owner, Tag) noexcept
    {
        const auto& errors = owner.*Member;
        static_assert(std::is_same_v<decltype(errors), const Errors&>);
        return errors && !errors->empty();
    }
};

template struct ErrorOwnerAccess<PrimErrors, &PXR_NS::PcpPrimIndex::_localErrors>;
template struct ErrorOwnerAccess<LayerErrors, &PXR_NS::PcpLayerStack::_localErrors>;
}

bool HasStoredCompositionErrors(const PXR_NS::PcpPrimIndex& index) noexcept
{
    return HasErrors(index, PrimErrors{});
}

bool HasStoredCompositionErrors(const PXR_NS::PcpLayerStack& stack) noexcept
{
    return HasErrors(stack, LayerErrors{});
}
}
