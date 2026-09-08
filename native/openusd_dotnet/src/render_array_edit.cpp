// Copyright (c) marcschier. Licensed under the MIT License.

#include "render_array_edit_headers.g.h"
#include "internal/render_array_edit.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/base/tf/token.h"

#include <algorithm>
#include <cstring>
#include <stdexcept>
#include <type_traits>

static_assert(PXR_VERSION == 2605, "Review bounded array-edit admission when updating the pinned SDK.");

namespace OpenUsdRenderInput
{
namespace
{
using Edit = PXR_NS::VtArrayEdit<PXR_NS::TfToken>;
using Ops = PXR_NS::Vt_ArrayEditOps;

#if !defined(OPENUSD_WITH_STORAGE_ADMISSION)
struct EditOps
{
    using Member = Ops Edit::*;
    friend Member GetMember(EditOps) noexcept;
};
struct OpWords
{
    using Member = std::vector<int64_t> Ops::*;
    friend Member GetMember(OpWords) noexcept;
};
template <typename Tag, typename Tag::Member Member>
struct ConstMemberAccess
{
    friend typename Tag::Member GetMember(Tag) noexcept { return Member; }
};
template struct ConstMemberAccess<EditOps, &Edit::_ops>;
template struct ConstMemberAccess<OpWords, &Ops::_ins>;

const Ops& Operations(const Edit& edit) noexcept
{
    return edit.*GetMember(EditOps{});
}

PXR_NS::TfSpan<const int64_t> Words(const Edit& edit) noexcept
{
    return Operations(edit).*GetMember(OpWords{});
}
#else
PXR_NS::TfSpan<const int64_t> Words(const Edit& edit) noexcept
{
    return edit.GetEncodedInstructions();
}
#endif

void ValidateFraming(PXR_NS::TfSpan<const int64_t> words)
{
    size_t cursor = 0;
    while (cursor < words.size())
    {
        Ops::OpAndCount block{};
        static_assert(sizeof(block) == sizeof(int64_t));
        std::memcpy(&block, &words[cursor++], sizeof(block));
        if (!Ops::IsValidOp(block.op) || block.count < 0 ||
            static_cast<uint64_t>(block.count) > (words.size() - cursor) /
                static_cast<size_t>(Ops::GetArity(block.op)))
        {
            throw std::invalid_argument("Render array-edit instructions are malformed.");
        }
        cursor += static_cast<size_t>(block.count) * static_cast<size_t>(Ops::GetArity(block.op));
    }
}
}

size_t TokenEditWordCount(const Edit& edit) noexcept
{
    return Words(edit).size();
}

TokenEditCost BoundTokenEdit(const Edit& edit, size_t initialSize, size_t maximumElements)
{
    const auto& words = Words(edit);
    ValidateFraming(words);
    TokenEditCost cost{initialSize, initialSize, initialSize};
    PXR_NS::TfErrorMark errors;
    auto inspect = [&](Ops::Op op, int64_t first, int64_t)
        {
            const size_t before = cost.finalSize;
            switch (op)
            {
                case Ops::OpInsertLiteral:
                case Ops::OpInsertRef:
                    ++cost.finalSize;
                    break;
                case Ops::OpEraseRef:
                    --cost.finalSize;
                    break;
                case Ops::OpMinSize:
                case Ops::OpMinSizeFill:
                    cost.finalSize = std::max(cost.finalSize, static_cast<size_t>(first));
                    break;
                case Ops::OpSetSize:
                case Ops::OpSetSizeFill:
                    cost.finalSize = static_cast<size_t>(first);
                    break;
                case Ops::OpMaxSize:
                    cost.finalSize = std::min(cost.finalSize, static_cast<size_t>(first));
                    break;
                default:
                    break;
            }
            if (cost.finalSize > maximumElements)
                throw std::length_error("Render array-edit intermediate purpose count exceeds its admission budget.");
            cost.peakSize = std::max(cost.peakSize, cost.finalSize);
            cost.copiedElements += (before + cost.finalSize + 16) * 2;
        };
#if defined(OPENUSD_WITH_STORAGE_ADMISSION)
    edit.ForEachValidInstruction(initialSize, inspect);
#else
    Operations(edit).ForEachValid(edit.GetLiterals().size(), initialSize, inspect);
#endif
    if (!errors.IsClean())
    {
        errors.Clear();
        throw std::invalid_argument("Render array-edit indexes or literals are invalid.");
    }
    return cost;
}
}
