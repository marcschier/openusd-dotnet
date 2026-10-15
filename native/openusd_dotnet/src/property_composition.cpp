// Copyright (c) marcschier. Licensed under the MIT License.

#include "property_pcp_headers.g.h"
#include "internal/property_composition.h"
#include "internal/owned_admission_observation.h"

#include <memory>
#include <type_traits>

static_assert(PXR_VERSION == 2608,
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

// SDK26.08 GetLocalErrors() copies its vector. Standard explicit instantiation
// obtains compiler-typed member pointers under its access-control exception.
// Configure AND build verify exact SDK headers; the generated include selects
// those verified files. No SDK definitions or byte offsets are substituted.
template <typename Tag, Errors Tag::Owner::* Member>
struct ErrorOwnerAccess
{
    friend bool HasErrors(const typename Tag::Owner& owner, Tag) noexcept
    {
        using OpenUsdOwnedAdmissionObservation::Kind;
        if constexpr (std::is_same_v<Tag, PrimErrors>)
            OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.prim.borrow");
        else
            OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.layer.borrow");
        const auto& errors = owner.*Member;
        static_assert(std::is_same_v<decltype(errors), const Errors&>);
        if constexpr (std::is_same_v<Tag, PrimErrors>)
            OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.prim.count");
        else
            OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.layer.count");
        const bool present = errors && !errors->empty();
        if (present)
        {
            if constexpr (std::is_same_v<Tag, PrimErrors>)
                OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.prim.rejected");
            else
                OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.layer.rejected");
        }
        if constexpr (std::is_same_v<Tag, PrimErrors>)
            OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.prim.return");
        else
            OpenUsdOwnedAdmissionObservation::Write<Kind::Property>("property.layer.return");
        return present;
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
