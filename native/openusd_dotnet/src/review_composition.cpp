// Copyright (c) marcschier. Licensed under the MIT License.
#include "internal/review_document.h"

namespace OpenUsdReview
{
namespace
{
constexpr size_t Limit = OPENUSD_REVIEW_MAX_COMPOSITION_WORK;

template <class T, class Action>
void Items(const SdfListOp<T>& list, Action&& action)
{
    for (const auto* items : {&list.GetExplicitItems(), &list.GetAddedItems(), &list.GetPrependedItems(),
        &list.GetAppendedItems(), &list.GetDeletedItems(), &list.GetOrderedItems()})
    {
        Check(items->size() <= OpenUsdEdit::MaxItems, "Source composition list budget exceeded.");
        for (const auto& item : *items) { action(item); }
    }
}

class CompositionBudget
{
public:
    explicit CompositionBudget(const std::map<std::string, TfRefPtr<CountedData>>& data) : layers(data) {}
    void Validate(const std::vector<std::string>& roots)
    {
        Context combined;
        for (const auto& root : roots)
        {
            const auto layer = layers.find(root);
            Check(layer != layers.end(), "Missing admitted composition root.");
            combined.roots.push_back(&layer->first);
        }
        Context& context = roots.size() == 1 ? External(roots.front()) : combined;
        ContextCost(context, SdfPath::AbsoluteRootPath(), 0);
    }
private:
    struct Cached { int state = 0; size_t cost = 0; };
    struct Context
    {
        std::vector<const std::string*> roots;
        std::map<SdfPath, Cached> sites;
        std::map<const std::string*, std::map<SdfPath, Cached>> files;
    };
    Context& External(const std::string& file)
    {
        const auto layer = layers.find(file);
        Check(layer != layers.end(), "Composition references a layer outside the admitted byte-image graph.");
        auto& context = contexts[&layer->first];
        if (context.roots.empty()) { context.roots.push_back(&layer->first); }
        return context;
    }
    void Begin(Cached& cached, size_t depth)
    {
        Check(depth < OPENUSD_REVIEW_MAX_COMPOSITION_DEPTH,
            "Portable composition budget exceeded (32 dependency/site levels); simplify the source composition.");
        Check(cached.state != 1, "Cyclic composition exceeds the bounded portable composition budget.");
        Check(++sites <= OPENUSD_REVIEW_MAX_SOURCE_EDGES, "Portable composition budget exceeded (65536 distinct sites).");
        cached.state = 1;
    }
    static void Add(size_t& cost, size_t amount)
    {
        Check(amount <= Limit - cost,
            "Portable composition budget exceeded (262144 expanded specs/arcs); simplify the source before review.");
        cost += amount;
    }
    size_t ContextCost(Context& context, const SdfPath& site, size_t depth)
    {
        auto& cached = context.sites[site];
        if (cached.state == 2) { return cached.cost; }
        Begin(cached, depth);
        size_t cost = 0;
        for (const auto* root : context.roots) { Add(cost, Cost(context, *root, site, depth)); }
        cached = {2, cost};
        return cost;
    }
    size_t Cost(Context& context, const std::string& file, const SdfPath& site, size_t depth)
    {
        const auto layer = layers.find(file);
        Check(layer != layers.end(), "Composition references a layer outside the admitted byte-image graph.");
        auto& cached = context.files[&layer->first][site];
        if (cached.state == 2) { return cached.cost; }
        Begin(cached, depth);
        size_t cost = 0;
        const auto& data = layer->second;
        const auto sublayers = data->Get(SdfPath::AbsoluteRootPath(), SdfFieldKeys->SubLayers);
        if (!sublayers.IsEmpty())
        {
            Check(sublayers.IsHolding<std::vector<std::string>>(), "Invalid source sublayer storage.");
            for (const auto& sublayer : sublayers.UncheckedGet<std::vector<std::string>>())
            {
                Add(cost, 1 + Cost(context, ResolvePath(sublayer, file), site, depth + 1));
            }
        }
        const auto arc = [&](const auto& reference)
        {
            const bool internal = reference.GetAssetPath().empty();
            const auto targetFile = internal ? file : ResolvePath(reference.GetAssetPath(), file);
            auto targetPath = reference.GetPrimPath();
            if (targetPath.IsEmpty())
            {
                const auto target = layers.find(targetFile);
                Check(target != layers.end(), "Missing admitted composition target.");
                const auto defaultPrim = target->second->Get(SdfPath::AbsoluteRootPath(), SdfFieldKeys->DefaultPrim);
                std::string name;
                if (defaultPrim.template IsHolding<TfToken>())
                {
                    name = defaultPrim.template UncheckedGet<TfToken>().GetString();
                }
                else if (defaultPrim.template IsHolding<std::string>())
                {
                    name = defaultPrim.template UncheckedGet<std::string>();
                }
                Check(SdfPath::IsValidIdentifier(name),
                    "A portable composition arc needs an explicit prim path or a valid target defaultPrim.");
                targetPath = SdfPath::AbsoluteRootPath().AppendChild(TfToken(name));
            }
            Check(targetPath.IsAbsolutePath() && targetPath.IsPrimPath() && !targetPath.ContainsPrimVariantSelection()
                && targetPath.GetPathElementCount() <= OpenUsdEdit::MaxDepth,
                "Unsupported source composition target prim path.");
            Add(cost, 1 + ContextCost(internal ? context : External(targetFile), targetPath, depth + 1));
        };
        for (const auto& spec : data->Inventory())
        {
            Check(data->Get(spec.first, SdfFieldKeys->Relocates).IsEmpty(),
                "Relocate composition is outside the initial bounded portable domain; reconcile or flatten those arcs.");
            const auto path = spec.first.StripAllVariantSelections();
            const bool below = path.HasPrefix(site);
            if (!below && !site.HasPrefix(path)) { continue; }
            if (below) { Add(cost, 1); }
            const auto references = data->Get(spec.first, SdfFieldKeys->References);
            if (!references.IsEmpty())
            {
                Check(references.IsHolding<SdfReferenceListOp>(), "Invalid source reference storage.");
                Items(references.UncheckedGet<SdfReferenceListOp>(), arc);
            }
            const auto payloads = data->Get(spec.first, SdfFieldKeys->Payload);
            if (!payloads.IsEmpty())
            {
                Check(payloads.IsHolding<SdfPayloadListOp>(), "Invalid source payload storage.");
                Items(payloads.UncheckedGet<SdfPayloadListOp>(), arc);
            }
            for (const auto& field : {SdfFieldKeys->InheritPaths, SdfFieldKeys->Specializes})
            {
                const auto value = data->Get(spec.first, field);
                if (value.IsEmpty()) { continue; }
                Check(value.IsHolding<SdfPathListOp>(), "Invalid inherited/specialized path storage.");
                Items(value.UncheckedGet<SdfPathListOp>(), [](const SdfPath&)
                {
                    throw std::runtime_error("Inherit/specialize composition is outside the initial bounded portable domain; reconcile or flatten those arcs.");
                });
            }
        }
        cached = {2, cost};
        return cost;
    }
    const std::map<std::string, TfRefPtr<CountedData>>& layers;
    std::map<const std::string*, Context> contexts;
    size_t sites = 0;
};
}

void ValidateComposition(const std::map<std::string, TfRefPtr<CountedData>>& layers,
    const std::vector<std::string>& roots)
{
    Check(layers.size() <= MaxFiles + 1, "Portable composition layer budget exceeded.");
    CompositionBudget budget(layers);
    budget.Validate(roots);
}
}
