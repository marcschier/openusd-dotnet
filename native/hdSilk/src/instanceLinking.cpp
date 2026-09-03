// Copyright (c) marcschier. Licensed under the MIT License.

#include "instanceLinking.h"

#include "openusd_hdsilk.h"

#include <algorithm>
#include <iterator>
#include <limits>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
using CategoryList = std::vector<std::string>;

const CategoryList kNoCategories;

/// Returns "categories" when it is already the sorted, duplicate-free form the
/// resolution compares and merges against, and otherwise a normalized copy held
/// by "scratch". Checking costs one pass, which is what keeps a caller that
/// already normalized -- the render pass does -- from paying for a copy per
/// instance it touches.
const CategoryList& Normalized(
    const CategoryList& categories,
    CategoryList* scratch)
{
    if (std::is_sorted(categories.begin(), categories.end()) &&
        std::adjacent_find(categories.begin(), categories.end()) ==
            categories.end())
    {
        return categories;
    }
    *scratch = categories;
    std::sort(scratch->begin(), scratch->end());
    scratch->erase(
        std::unique(scratch->begin(), scratch->end()),
        scratch->end());
    return *scratch;
}

/// The set union of two normalized category lists. Categories compose by union
/// along an instancing chain because collection membership does: an instance
/// that a collection includes carries that membership to everything instanced
/// beneath it, and nothing an ancestor contributes can be taken away by a
/// descendant. Intersecting them, or crossing them into per-level products,
/// would invent memberships UsdLux never authored.
CategoryList MergeCategories(const CategoryList& left, const CategoryList& right)
{
    if (right.empty())
    {
        return left;
    }
    if (left.empty())
    {
        return right;
    }
    CategoryList merged;
    merged.reserve(left.size() + right.size());
    std::set_union(
        left.begin(),
        left.end(),
        right.begin(),
        right.end(),
        std::back_inserter(merged));
    return merged;
}

bool IsSubset(const CategoryList& candidate, const CategoryList& superset)
{
    return std::includes(
        superset.begin(),
        superset.end(),
        candidate.begin(),
        candidate.end());
}

/// One level of the chain after its published indices have been reduced to the
/// ones that address a record hdSilk actually publishes.
struct ResolvedLevel
{
    const HdSilkInstancerLevel* level = nullptr;
    // Ascending and duplicate free, so the walk emits rows in ascending
    // composed-index order and an unchanged scene produces the same rows.
    std::vector<int> indices;
    // The subset of "indices" whose own membership can move a composed
    // identity off the prototype's row. Everything else resolves to that row
    // by construction, which is what lets whole subtrees be skipped instead of
    // enumerated.
    std::vector<int> interesting;

    bool IsInteresting(int index) const
    {
        return std::binary_search(interesting.begin(), interesting.end(), index);
    }

    const CategoryList* Reported(int index) const
    {
        const std::vector<CategoryList>& reported = level->instanceCategories;
        if (index < 0 || static_cast<size_t>(index) >= reported.size())
        {
            return nullptr;
        }
        return &reported[static_cast<size_t>(index)];
    }
};

struct WalkContext
{
    const std::string* primPath = nullptr;
    // The prototype Rprim's own categories, which is what an instance whose own
    // level reports nothing falls back to.
    const CategoryList* primCategories = nullptr;
    // The categories the prototype path's own row resolves to: its own plus
    // every ancestor instancer's path-wide set.
    const CategoryList* effective = nullptr;
    const std::vector<ResolvedLevel>* levels = nullptr;
    // suffixInteresting[k] is true when level k or any level below it can move
    // an identity off the prototype's row.
    std::vector<bool> suffixInteresting;
    size_t rowLimit = 0;
    std::vector<HdSilkCategoryMembership>* out = nullptr;
    HdSilkNestedLinkDiagnostics* diagnostics = nullptr;
    bool fitted = true;
};

void Walk(
    WalkContext* context,
    size_t depth,
    int64_t composed,
    bool prefixInteresting,
    const CategoryList& accumulated)
{
    const ResolvedLevel& level = (*context->levels)[depth];
    const bool isLeaf = depth + 1 == context->levels->size();
    const bool deeperInteresting = context->suffixInteresting[depth + 1];

    // A level nobody below is interested in, reached by a prefix that is not
    // interesting either, can only produce rows through its own interesting
    // indices: every other index of it resolves to the prototype's row. Walking
    // just those is what keeps the cost proportional to the rows emitted rather
    // than to the size of the instancer cross product.
    const std::vector<int>& walked = (prefixInteresting || deeperInteresting)
        ? level.indices
        : level.interesting;

    constexpr int64_t maximumIndex =
        static_cast<int64_t>(std::numeric_limits<int32_t>::max());
    for (int index : walked)
    {
        if (!context->fitted)
        {
            return;
        }

        int64_t next = index;
        if (depth != 0)
        {
            const int64_t radix = level.level->instanceCount;
            if (radix <= 0 || composed > (maximumIndex - index) / radix)
            {
                // The identity exists but the page ABI cannot name it, so the
                // whole subtree keeps the prototype's row rather than being
                // published under an index that means another instance.
                ++context->diagnostics->unrepresentableIndices;
                continue;
            }
            next = (composed * radix) + index;
        }

        const bool interesting =
            prefixInteresting || level.IsInteresting(index);
        CategoryList scratch;
        if (!isLeaf)
        {
            const CategoryList* reported = level.Reported(index);
            const CategoryList& contribution = reported == nullptr
                ? kNoCategories
                : Normalized(*reported, &scratch);
            Walk(
                context,
                depth + 1,
                next,
                interesting,
                MergeCategories(accumulated, contribution));
            continue;
        }

        if (!interesting)
        {
            continue;
        }
        const CategoryList* reported = level.Reported(index);
        const CategoryList& base = reported == nullptr
            ? *context->primCategories
            : Normalized(*reported, &scratch);
        CategoryList resolved = MergeCategories(accumulated, base);
        if (resolved == *context->effective)
        {
            // Already described by the prototype's own row.
            continue;
        }
        if (context->out->size() >= context->rowLimit)
        {
            context->fitted = false;
            return;
        }
        HdSilkCategoryMembership membership;
        membership.path = *context->primPath;
        membership.instanceIndex = static_cast<int32_t>(next);
        membership.categories = std::move(resolved);
        context->out->push_back(std::move(membership));
    }
}
}

bool
HdSilkAppendNestedInstanceMemberships(
    const std::string& primPath,
    const std::vector<std::string>& primCategories,
    const std::vector<std::string>& sharedCategories,
    const std::vector<HdSilkInstancerLevel>& levels,
    size_t rowLimit,
    std::vector<HdSilkCategoryMembership>* outMemberships,
    HdSilkNestedLinkDiagnostics* outDiagnostics)
{
    HdSilkNestedLinkDiagnostics ignored;    HdSilkNestedLinkDiagnostics* diagnostics =
        outDiagnostics == nullptr ? &ignored : outDiagnostics;
    if (outMemberships == nullptr || levels.empty())
    {
        return true;
    }

    CategoryList primScratch;
    const CategoryList& prim = Normalized(primCategories, &primScratch);
    CategoryList sharedScratch;
    const CategoryList& shared = Normalized(sharedCategories, &sharedScratch);
    const CategoryList effective = MergeCategories(prim, shared);

    std::vector<ResolvedLevel> resolvedLevels;
    resolvedLevels.reserve(levels.size());
    for (size_t depth = 0; depth < levels.size(); ++depth)
    {
        const HdSilkInstancerLevel& level = levels[depth];
        const bool isLeaf = depth + 1 == levels.size();
        ResolvedLevel resolved;
        resolved.level = &level;
        resolved.indices.reserve(level.publishedIndices.size());
        for (int index : level.publishedIndices)
        {
            if (index < 0)
            {
                // A hidden or proto instance addresses no instance primvar
                // element, so it draws nothing and has no identity to link.
                continue;
            }
            if (depth != 0 &&
                (level.instanceCount <= 0 ||
                    static_cast<int64_t>(index) >= level.instanceCount))
            {
                // The authoritative count cannot explain the index, so the
                // composition has no unique encoding for it and
                // HdSilkInstancer drops the sample as well.
                ++diagnostics->uncomposableIndices;
                continue;
            }
            if (!level.instanceCategories.empty() &&
                static_cast<size_t>(index) >= level.instanceCategories.size())
            {
                // The level reported per-instance membership but not for this
                // instance, so its categories are unknown rather than empty.
                // Publishing the prototype's row for it is the documented
                // fallback; publishing a mask resolved from another instance
                // would not be.
                ++diagnostics->unresolvedIndices;
                continue;
            }
            resolved.indices.push_back(index);
        }
        std::sort(resolved.indices.begin(), resolved.indices.end());
        resolved.indices.erase(
            std::unique(resolved.indices.begin(), resolved.indices.end()),
            resolved.indices.end());
        if (resolved.indices.empty())
        {
            // This level draws nothing, so the chain publishes no record at all
            // and there is no identity below it to link.
            return true;
        }

        for (int index : resolved.indices)
        {
            const CategoryList* reported = resolved.Reported(index);
            if (reported == nullptr)
            {
                continue;
            }
            CategoryList scratch;
            const CategoryList& categories = Normalized(*reported, &scratch);
            if (isLeaf)
            {
                // The leaf level's array replaces the prototype's categories
                // rather than adding to them: it is the complete membership of
                // that instance at its own level.
                if (categories != prim)
                {
                    resolved.interesting.push_back(index);
                }
                continue;
            }
            // An ancestor adds to what is already resolved, so it can only
            // matter when it carries something the prototype's own row does
            // not already claim.
            if (!categories.empty() && !IsSubset(categories, effective))
            {
                resolved.interesting.push_back(index);
            }
        }
        resolvedLevels.push_back(std::move(resolved));
    }

    WalkContext context;
    context.primPath = &primPath;
    context.primCategories = &prim;
    context.effective = &effective;
    context.levels = &resolvedLevels;
    context.suffixInteresting.assign(resolvedLevels.size() + 1, false);
    for (size_t depth = resolvedLevels.size(); depth-- > 0;)
    {
        context.suffixInteresting[depth] =
            !resolvedLevels[depth].interesting.empty() ||
            context.suffixInteresting[depth + 1];
    }
    if (!context.suffixInteresting[0])
    {
        // No level can move any identity off the prototype's row, so the whole
        // cross product is already described by it and is never enumerated.
        return true;
    }
    context.rowLimit = rowLimit;
    context.out = outMemberships;
    context.diagnostics = diagnostics;
    Walk(&context, 0, 0, false, shared);
    return context.fitted;
}

bool
HdSilkAppendPathMemberships(
    const std::string& primPath,
    const std::vector<std::string>& primCategories,
    const std::vector<std::string>& sharedCategories,
    const std::vector<HdSilkInstancerLevel>& levels,
    size_t rowLimit,
    std::vector<HdSilkCategoryMembership>* outMemberships,
    HdSilkNestedLinkDiagnostics* outDiagnostics)
{
    if (outMemberships == nullptr)
    {
        return true;
    }

    const size_t groupStart = outMemberships->size();

    CategoryList primScratch;
    const CategoryList& prim = Normalized(primCategories, &primScratch);
    CategoryList sharedScratch;
    const CategoryList& shared = Normalized(sharedCategories, &sharedScratch);

    HdSilkCategoryMembership pathRow;
    pathRow.path = primPath;
    pathRow.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
    pathRow.categories = MergeCategories(prim, shared);
    outMemberships->push_back(std::move(pathRow));

    if (levels.empty())
    {
        return true;
    }
    if (HdSilkAppendNestedInstanceMemberships(
            primPath,
            primCategories,
            sharedCategories,
            levels,
            outMemberships->size() + rowLimit,
            outMemberships,
            outDiagnostics))
    {
        return true;
    }

    // The rows this path needs exceed the raw-memory policy, so they cannot be
    // collected in full and the resolution downstream would never see the ones
    // that were dropped. The path-wide row cannot be published on its own
    // either: a consumer resolves an instance it has no row for against that
    // row, and publishing it alone would apply a mask the author wrote for the
    // rest of the path to the instances that were dropped. Removing the whole
    // group leaves the path linked to every light, which is the documented
    // fallback, and the caller reports the omission.
    outMemberships->resize(groupStart);
    return false;
}

PXR_NAMESPACE_CLOSE_SCOPE
