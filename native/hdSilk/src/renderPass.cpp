// Copyright (c) marcschier. Licensed under the MIT License.

#include "renderPass.h"

#include "instanceLinking.h"
#include "instancer.h"
#include "openusd_hdsilk.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/instancer.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/renderPassState.h"
#include "pxr/imaging/hd/rprim.h"
#include "pxr/imaging/hd/sceneDelegate.h"

#include <algorithm>
#include <utility>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
void _Canonicalize(std::vector<std::string>* categories)
{
    std::sort(categories->begin(), categories->end());
    categories->erase(
        std::unique(categories->begin(), categories->end()),
        categories->end());
}

std::vector<std::string> _ToCategoryStrings(const VtArray<TfToken>& categories)
{
    std::vector<std::string> result;
    result.reserve(categories.size());
    for (const TfToken& category : categories)
    {
        if (!category.IsEmpty())
        {
            result.push_back(category.GetString());
        }
    }
    _Canonicalize(&result);
    return result;
}
}

HdSilkRenderPass::HdSilkRenderPass(
    HdRenderIndex* index,
    HdRprimCollection const& collection,
    std::shared_ptr<HdSilkSceneState> sceneState)
    : HdRenderPass(index, collection)
    , _sceneState(std::move(sceneState))
{
}

void
HdSilkRenderPass::_Execute(
    HdRenderPassStateSharedPtr const& renderPassState,
    TfTokenVector const& /*renderTags*/)
{
    if (!_sceneState || !renderPassState)
    {
        return;
    }

    const GfMatrix4d viewMatrix = renderPassState->GetWorldToViewMatrix();
    const GfMatrix4d projectionMatrix = renderPassState->GetProjectionMatrix();
    const GfVec4f viewport = renderPassState->GetViewport();

    HdSilkFrameState frame;
    frame.width = static_cast<int32_t>(viewport[2]);
    frame.height = static_cast<int32_t>(viewport[3]);
    HdSilkFlattenMatrix(viewMatrix, frame.viewMatrix);
    HdSilkFlattenMatrix(projectionMatrix, frame.projectionMatrix);

    _sceneState->SetFrame(frame);
    _CollectCategoryMemberships();
}

void
HdSilkRenderPass::_CollectCategoryMemberships()
{    // Prim categories are only ever read while at least one light carries a
    // non-default link collection. UsdLux linking is authored rarely and the
    // walk is over the whole render index, so a scene that links nothing pays
    // nothing at all for the feature and keeps publishing the default masks.
    if (!_sceneState->HasLightLinks())
    {
        if (_collectedMemberships)
        {
            _sceneState->SetCategoryMemberships({}, false);
            _collectedMemberships = false;
        }
        return;
    }

    HdRenderIndex* index = GetRenderIndex();
    if (index == nullptr)
    {
        return;
    }

    std::vector<HdSilkCategoryMembership> memberships;
    bool truncated = false;

    // Nothing here is charged against OPENUSD_SILK_MAX_LINK_ENTRIES. A
    // membership is not an entry: which rows survive is decided once the page's
    // light and dome orderings exist, in HdSilkSceneState, where a prim that
    // links to everything and an instance whose masks match its path's both
    // disappear and cost the table nothing. Admitting rows against the ABI
    // budget here charged a prim for linking to everything, so a scene of 4096
    // such prims crowded out the one prim a collection really excluded and
    // reported a truncated table for a page that fits in a single entry.
    //
    // The only bound applied at collection time is
    // HdSilkMaxCollectedInstanceRows, which is a transient-memory policy on the
    // rows one prototype may materialize and sits far above the ABI budget.
    for (const SdfPath& rprimId : index->GetRprimIds())
    {
        HdSceneDelegate* sceneDelegate = index->GetSceneDelegateForRprim(rprimId);
        if (sceneDelegate == nullptr)
        {
            continue;
        }

        std::vector<std::string> primCategories =
            _ToCategoryStrings(sceneDelegate->GetCategories(rprimId));

        const HdRprim* rprim = index->GetRprim(rprimId);
        const bool instanced =
            rprim != nullptr && !rprim->GetInstancerId().IsEmpty();

        // The instancer chain that reaches this prototype, from the root
        // instancer down to the instancer the prim names. Hydra reports link
        // categories one level at a time, so the chain has to be walked whole:
        // an ancestor level's membership reaches every identity composed
        // beneath it and is on no array the leaf level publishes.
        std::vector<const HdSilkInstancer*> chain;
        std::vector<std::string> sharedCategories;
        bool chainResolved = true;
        if (instanced)
        {
            // A bound on the walk rather than on the scene: a render index that
            // reported a cycle of parents would otherwise spin here, and a
            // chain this deep has no representable composed index anyway --
            // every level multiplies the radix by at least two.
            constexpr size_t maximumChainDepth = 64;
            SdfPath instancerId = rprim->GetInstancerId();
            while (!instancerId.IsEmpty())
            {
                HdInstancer* instancer = index->GetInstancer(instancerId);
                if (instancer == nullptr || chain.size() >= maximumChainDepth)
                {
                    chainResolved = false;
                    break;
                }
                chain.push_back(static_cast<const HdSilkInstancer*>(instancer));
                instancerId = instancer->GetParentId();
            }
            std::reverse(chain.begin(), chain.end());
        }

        // An instancer's own categories apply to every one of its instances,
        // which is what Hydra states about GetCategories on an instancer prim,
        // so they are path-wide for every prototype that instancer scatters.
        // Every level of the chain contributes them, the leaf level included:
        // for a point instancer HdsiLightLinkingSceneIndex deliberately reports
        // a collection that names the instancer through the instancer prim's
        // own categories rather than through per-instance ones, and a prototype
        // that lives outside the instancer's namespace has no other way to
        // learn about it. They are folded into the prototype's own row rather
        // than repeated per instance, because they reach every composed
        // identity equally.
        for (const HdSilkInstancer* level : chain)
        {
            HdSceneDelegate* levelDelegate = level->GetDelegate();
            if (levelDelegate == nullptr)
            {
                continue;
            }
            const std::vector<std::string> levelCategories =
                _ToCategoryStrings(levelDelegate->GetCategories(level->GetId()));
            sharedCategories.insert(
                sharedCategories.end(),
                levelCategories.begin(),
                levelCategories.end());
        }
        _Canonicalize(&sharedCategories);

        std::vector<HdSilkInstancerLevel> levels;
        if (instanced && (!chainResolved || chain.empty()))
        {
            // Without the whole chain there is no composed identity to resolve
            // against, and an index resolved from part of it would name a
            // different instance. The prototype's row still applies to every
            // instance, and the gap is named rather than guessed at.
            TF_WARN(
                "hdSilk could not resolve the instancer chain of '%s', so "
                "every instance of it uses the prototype's linking.",
                rprimId.GetText());
        }
        else if (instanced)
        {
            // Only the instances each level actually draws.
            // GetInstanceCategories reports one array per instance of the
            // *instancer*, so an instancer that scatters several prototypes
            // reports every instance to every one of them: walking it directly
            // emitted a membership row for (this path, that index) even when
            // that index draws a different prototype, and those rows are
            // phantoms. They are never matched by a published record, and they
            // consume the bounded link table -- so a scene with a handful of
            // prototypes and a large instancer could fill the budget with rows
            // that address nothing and truncate away the ones that do.
            //
            // GetInstanceIndices is the same call each instancer resolves its
            // samples from, so intersecting with it enumerates exactly the
            // identities hdSilk publishes. A negative index addresses no
            // instance primvar element and is dropped there too, which is how a
            // hidden or proto instance stays out of both.
            levels.reserve(chain.size());
            bool visible = true;
            size_t flattenedLevels = 0;
            for (size_t level = 0; level < chain.size(); ++level)
            {
                const HdSilkInstancer* instancer = chain[level];
                if (!instancer->IsVisible())
                {
                    // An invisible instancer publishes no instance of anything,
                    // so the chain publishes no record and there is nothing to
                    // link per instance.
                    visible = false;
                    break;
                }
                HdSceneDelegate* levelDelegate = instancer->GetDelegate();
                if (levelDelegate == nullptr)
                {
                    levelDelegate = sceneDelegate;
                }
                const SdfPath& childId = level + 1 < chain.size()
                    ? chain[level + 1]->GetId()
                    : rprimId;

                HdSilkInstancerLevel resolved;
                resolved.path = instancer->GetId().GetString();
                resolved.instanceCount = instancer->GetInstanceCount();
                const VtIntArray publishedIndices =
                    levelDelegate->GetInstanceIndices(
                        instancer->GetId(),
                        childId);
                resolved.publishedIndices.assign(
                    publishedIndices.begin(),
                    publishedIndices.end());
                const std::vector<VtArray<TfToken>> instanceCategories =
                    levelDelegate->GetInstanceCategories(instancer->GetId());
                if (level == 0)
                {
                    // Only the root instancer's per-instance categories are an
                    // exact answer. Its instances are the scene's own
                    // instances, so one array entry is one identity's
                    // membership and composing it onto every identity below it
                    // is exact.
                    resolved.instanceCategories.reserve(
                        instanceCategories.size());
                    for (const VtArray<TfToken>& categories : instanceCategories)
                    {
                        resolved.instanceCategories.push_back(
                            _ToCategoryStrings(categories));
                    }
                }
                else if (!instanceCategories.empty())
                {
                    // A nested level's instances are replicated once per
                    // ancestor instance, and Hydra reports one array per
                    // instance of the level rather than one per composed
                    // identity: upstream states that linking through nested
                    // instances is not resolved, and the array it returns is
                    // the union over every ancestor. Using it would hand one
                    // ancestor's collection to identities the author excluded
                    // from it, so hdSilk drops it and says so. Everything the
                    // chain does resolve exactly -- the root level's
                    // per-instance categories, every level's path-wide
                    // categories and the prototype's own -- still reaches every
                    // identity.
                    ++flattenedLevels;
                }
                levels.push_back(std::move(resolved));
            }
            if (!visible)
            {
                levels.clear();
            }
            if (flattenedLevels != 0)
            {
                TF_WARN(
                    "hdSilk did not apply the per-instance link categories "
                    "Hydra reports for %zu nested instancer level(s) of '%s': "
                    "under nesting they describe instances shared by every "
                    "ancestor instance, so they name no single composed "
                    "identity. The root instancer's per-instance categories "
                    "and every level's path-wide categories still resolve "
                    "exactly.",
                    flattenedLevels,
                    rprimId.GetText());
            }
        }

        HdSilkNestedLinkDiagnostics diagnostics;
        if (!HdSilkAppendPathMemberships(
                rprimId.GetString(),
                primCategories,
                sharedCategories,
                levels,
                HdSilkMaxCollectedInstanceRows,
                &memberships,
                &diagnostics))
        {
            // The prototype materializes more unresolved rows than the
            // collector's memory policy admits, so the rows the resolution
            // would have kept cannot be known. The path was left out whole and
            // stays linked to every light rather than publishing a path row its
            // own instances contradict.
            truncated = true;
            TF_WARN(
                "hdSilk omitted the whole light-link group of '%s': it needs "
                "more than %zu unresolved per-instance rows. The prim stays "
                "linked to every light.",
                rprimId.GetText(),
                HdSilkMaxCollectedInstanceRows);
        }
        if (diagnostics.Any())
        {
            TF_WARN(
                "hdSilk left %zu uncomposable, %zu unresolved and %zu "
                "unrepresentable instance identities of '%s' on the "
                "prototype's linking.",
                diagnostics.uncomposableIndices,
                diagnostics.unresolvedIndices,
                diagnostics.unrepresentableIndices,
                rprimId.GetText());
        }
    }

    _sceneState->SetCategoryMemberships(std::move(memberships), truncated);
    _collectedMemberships = true;
}

PXR_NAMESPACE_CLOSE_SCOPE
